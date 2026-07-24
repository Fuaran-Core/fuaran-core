module Fuaran.Core.Tests.ColumnTests

open Expecto
open Fuaran.Core

// ---- fixtures ----

/// A table exercising every scalar type, nulls in every column, and the canonical-float
/// divergence-zone values (0.1, 1/3, a large magnitude).
let private sampleTable: Table =
    { Schema =
        [ "i", IntType
          "f", FloatType
          "b", BoolType
          "s", StringType
          "d", DateType
          "t", TimestampType ]
      Columns =
        [ Column.create "i" IntType [ Int 1; Null; Int -42 ]
          Column.create "f" FloatType [ Float 0.1; Float(1.0 / 3.0); Null ]
          Column.create "b" BoolType [ Bool true; Null; Bool false ]
          Column.create "s" StringType [ Str "a\"b"; Str ""; Null ]
          Column.create "d" DateType [ Date "2026-06-22"; Null; Date "1970-01-01" ]
          Column.create "t" TimestampType [ Timestamp "2026-06-22T17:00:00Z"; Null; Timestamp "2000-01-01T00:00:00Z" ] ] }

let private sample = Embedded sampleTable

// ---- generator (for the codec round-trip law) ----

let private genSource (seed: int) : DataSource =
    let mutable st = (uint32 seed * 2654435761u) + 1u

    let next () =
        st <- (st * 1664525u) + 1013904223u
        int (st >>> 1)

    let pick n = next () % n
    let rows = pick 4

    let mkCell ty i =
        if pick 5 = 0 then
            Null
        else
            match ty with
            | IntType -> Int(pick 2000 - 1000)
            | FloatType -> Float(float (pick 1000) * 0.1 - 50.0)
            | BoolType -> Bool(pick 2 = 0)
            | StringType -> Str("v" + string i + "\\\n")
            | DateType -> Date("20" + string (10 + pick 80) + "-01-15")
            | TimestampType -> Timestamp("20" + string (10 + pick 80) + "-01-15T12:00:00Z")

    let types =
        [ IntType; FloatType; BoolType; StringType; DateType; TimestampType ]
        |> List.filter (fun _ -> pick 2 = 0)
        |> function
            | [] -> [ IntType ]
            | xs -> xs

    let schema = types |> List.mapi (fun i ty -> "c" + string i, ty)

    let columns =
        schema
        |> List.map (fun (name, ty) -> Column.create name ty [ for r in 0 .. rows - 1 -> mkCell ty r ])

    Embedded { Schema = schema; Columns = columns }

[<Tests>]
let tests =
    testList
        "Column"
        [ testCase "a null-aware multi-type table round-trips byte-identically"
          <| fun _ ->
              let once = ColumnCodec.encode sample

              match ColumnCodec.decode once with
              | Error e -> failtestf "decode failed: %s" (ColumnCodec.errorString e)
              | Ok src2 ->
                  Expect.equal src2 sample "decode reproduces the value"
                  Expect.equal (ColumnCodec.encode src2) once "re-encode is byte-identical"

          testCase "a null cell survives the round-trip as Null, not the placeholder"
          <| fun _ ->
              match ColumnCodec.decode (ColumnCodec.encode sample) with
              | Ok(Embedded t) ->
                  let f = Table.tryColumn "f" t |> Option.get
                  Expect.equal (Column.cell 2 f) Null "the null float cell stays Null"
              | other -> failtestf "unexpected: %A" other

          testCase "numeric columns use the Wire canonical-float layout"
          <| fun _ ->
              // a float column's value tokens must match Json.render of the same JFloat exactly
              let json = ColumnCodec.encode sample
              let expected = Json.render (JFloat 0.1)
              Expect.stringContains json expected "0.1 renders via the Wire {0:R} layout"
              Expect.stringContains json (Json.render (JFloat(1.0 / 3.0))) "1/3 renders canonically"

          testCase "embedded float column accepts an integer token (lossless widening)"
          <| fun _ ->
              let json =
                  """{"schema":[{"name":"f","type":"float"}],"columns":{"f":{"values":[3],"validity":[true]}}}"""

              match ColumnCodec.decode json with
              | Ok(Embedded t) ->
                  let f = Table.tryColumn "f" t |> Option.get
                  Expect.equal (Column.cell 0 f) (Float 3.0) "int token widened to float"
              | other -> failtestf "unexpected: %A" other

          testCase "a ref source round-trips"
          <| fun _ ->
              let src = Ref "sales-2026"

              match ColumnCodec.decode (ColumnCodec.encode src) with
              | Ok src2 -> Expect.equal src2 src "ref round-trips"
              | Error e -> failtestf "decode failed: %s" (ColumnCodec.errorString e)

          // ---- the six-code error envelope ----

          testCase "NotJson — a syntax error surfaces as NotJson"
          <| fun _ ->
              match ColumnCodec.decode "{not json" with
              | Error(NotJson _) -> ()
              | other -> failtestf "expected NotJson, got %A" other

          // Phase 88 — `schema` may be omitted on an EMBEDDED source (inferred
          // from the cells); an empty columns object infers an empty table.
          testCase "Phase 88 — schema absent, empty columns infers the empty table"
          <| fun _ ->
              match ColumnCodec.decode """{"columns":{}}""" with
              | Ok(Embedded t) ->
                  Expect.equal t.Schema [] "empty schema"
                  Expect.equal t.Columns [] "empty columns"
              | other -> failtestf "expected Ok Embedded empty, got %A" other

          testCase "Phase 88 — schema inference: int / float / bool / string, Ordinal order"
          <| fun _ ->
              let wire = """{"columns":{"b":[true,false],"f":[1.5,2],"i":[1,2],"s":["x","y"]}}"""

              match ColumnCodec.decode wire with
              | Ok(Embedded t) ->
                  Expect.equal
                      t.Schema
                      [ "b", BoolType; "f", FloatType; "i", IntType; "s", StringType ]
                      "inferred types in Ordinal column order (any fractional ⇒ float; ints stay int)"
              | other -> failtestf "expected Ok Embedded, got %A" other

          testCase "Phase 88 — a date-looking string infers STRING (temporal types need a declared schema)"
          <| fun _ ->
              match ColumnCodec.decode """{"columns":{"d":["2026-07-18"]}}""" with
              | Ok(Embedded t) -> Expect.equal t.Schema [ "d", StringType ] "never date"
              | other -> failtestf "expected Ok Embedded, got %A" other

          testCase "Phase 88 — bare-array columns round-trip to the canonical wrapped bytes"
          <| fun _ ->
              let shorthand = """{"columns":{"amount":[100,200],"dept":["ops","eng"]}}"""

              let verbose =
                  """{"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}],"columns":{"amount":{"values":[100,200],"validity":[true,true]},"dept":{"values":["ops","eng"],"validity":[true,true]}}}"""

              match ColumnCodec.decode shorthand, ColumnCodec.decode verbose with
              | Ok a, Ok b ->
                  Expect.equal a b "shorthand decodes to the explicit twin's value"
                  Expect.equal (ColumnCodec.encode a) (ColumnCodec.encode b) "re-encodes byte-identically"
              | a, b -> failtestf "expected both Ok, got %A / %A" a b

          testCase "Phase 94 — a values-only column object is the all-present shorthand (pilot-5 census)"
          <| fun _ ->
              // The exact gemini n=1 shape (six tasks): the canonical wrapped object minus
              // the validity mask — same all-present statement as the Phase-88 bare array.
              let valuesOnly =
                  """{"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}],"columns":{"amount":{"values":[100,200]},"dept":{"values":["ops","eng"]}}}"""

              let verbose =
                  """{"schema":[{"name":"amount","type":"int"},{"name":"dept","type":"string"}],"columns":{"amount":{"values":[100,200],"validity":[true,true]},"dept":{"values":["ops","eng"],"validity":[true,true]}}}"""

              match ColumnCodec.decode valuesOnly, ColumnCodec.decode verbose with
              | Ok a, Ok b ->
                  Expect.equal a b "values-only decodes to the explicit twin's value"

                  Expect.equal
                      (ColumnCodec.encode a)
                      (ColumnCodec.encode b)
                      "re-encodes byte-identically (mask restored)"
              | a, b -> failtestf "expected both Ok, got %A / %A" a b

          testCase "Phase 94 — epoch ints in a declared timestamp column decode to canonical ISO (s + ms)"
          <| fun _ ->
              // tier-a-052's shape: schema says timestamp, values are epoch numbers.
              // 1752000000 s = 2025-07-08T18:40:00Z; the ms twin arrives as a whole
              // JFloat (overflows the parser's Int32 path) and lands on the same instant.
              let wire =
                  """{"schema":[{"name":"finish_ts","type":"timestamp"}],"columns":{"finish_ts":{"values":[1752000000,1752000000000],"validity":[true,true]}}}"""

              match ColumnCodec.decode wire with
              | Ok(Embedded t) ->
                  match t.Columns with
                  | [ c ] ->
                      Expect.equal
                          c.Cells
                          [ Timestamp "2025-07-08T18:40:00Z"; Timestamp "2025-07-08T18:40:00Z" ]
                          "seconds and milliseconds decode to the same canonical ISO instant"
                  | other -> failtestf "expected one column, got %A" other
              | other -> failtestf "expected Ok Embedded, got %A" other

          testCase "Phase 94 — a pre-1970 epoch decodes correctly (negative floor-div path)"
          <| fun _ ->
              match
                  ColumnCodec.decode
                      """{"schema":[{"name":"ts","type":"timestamp"}],"columns":{"ts":{"values":[-86401],"validity":[true]}}}"""
              with
              | Ok(Embedded t) ->
                  match t.Columns with
                  | [ c ] -> Expect.equal c.Cells [ Timestamp "1969-12-30T23:59:59Z" ] "one second before Dec 31"
                  | other -> failtestf "expected one column, got %A" other
              | other -> failtestf "expected Ok Embedded, got %A" other

          testCase "Phase 88 — mixed-kind column is a didactic reject naming the schema remedy"
          <| fun _ ->
              match ColumnCodec.decode """{"columns":{"m":[1,"two"]}}""" with
              | Error(MalformedShape d) -> Expect.stringContains d "declare it in an explicit" "names the remedy"
              | other -> failtestf "expected MalformedShape, got %A" other

          testCase "Phase 88 — empty column is a didactic reject naming the schema remedy"
          <| fun _ ->
              match ColumnCodec.decode """{"columns":{"e":[]}}""" with
              | Error(MalformedShape d) -> Expect.stringContains d "declare it in an explicit" "names the remedy"
              | other -> failtestf "expected MalformedShape, got %A" other

          testCase "Phase 88 — a ref source without schema is a didactic reject"
          <| fun _ ->
              match ColumnCodec.decode """{"ref":"orders"}""" with
              | Error(MalformedShape d) -> Expect.stringContains d "ref source requires" "names the constraint"
              | other -> failtestf "expected MalformedShape, got %A" other

          testCase "MissingField — a schema column missing from columns"
          <| fun _ ->
              match ColumnCodec.decode """{"schema":[{"name":"x","type":"int"}],"columns":{}}""" with
              | Error(MissingField "columns.x") -> ()
              | other -> failtestf "expected MissingField columns.x, got %A" other

          testCase "UnknownType — a type tag outside the fixed set, with the enumeration"
          <| fun _ ->
              match ColumnCodec.decode """{"schema":[{"name":"x","type":"decimal"}],"columns":{}}""" with
              | Error(UnknownType("decimal", expected)) ->
                  Expect.equal expected ColumnType.allTags "enumerates the valid tags"
              | other -> failtestf "expected UnknownType, got %A" other

          testCase "TypeMismatch — a string where an int column is declared"
          <| fun _ ->
              let json =
                  """{"schema":[{"name":"x","type":"int"}],"columns":{"x":{"values":["nope"],"validity":[true]}}}"""

              match ColumnCodec.decode json with
              | Error(TypeMismatch("x", "int", "string")) -> ()
              | other -> failtestf "expected TypeMismatch, got %A" other

          testCase "LengthMismatch — values and validity disagree"
          <| fun _ ->
              let json =
                  """{"schema":[{"name":"x","type":"int"}],"columns":{"x":{"values":[1,2],"validity":[true]}}}"""

              match ColumnCodec.decode json with
              | Error(LengthMismatch("x", 2, 1)) -> ()
              | other -> failtestf "expected LengthMismatch, got %A" other

          testCase "MalformedShape — values is not an array"
          <| fun _ ->
              let json =
                  """{"schema":[{"name":"x","type":"int"}],"columns":{"x":{"values":5,"validity":[]}}}"""

              match ColumnCodec.decode json with
              | Error(MalformedShape _) -> ()
              | other -> failtestf "expected MalformedShape, got %A" other

          // ---- generative round-trip + corpus coverage ----

          testCase "generative codec round-trip law over a wide sample"
          <| fun _ ->
              match Corpus.codecLaws ColumnCodec.codec genSource 1 400 with
              | Ok() -> ()
              | Error m -> failtest m

          testCase "corpus coverage gate over every scalar type + null + ref"
          <| fun _ ->
              let cases: Corpus.Case list =
                  [ { Name = "all-types-with-nulls"
                      Kind = Corpus.RoundTrip
                      Json = ColumnCodec.encode sample
                      Tag = "all" }
                    { Name = "ref"
                      Kind = Corpus.RoundTrip
                      Json = ColumnCodec.encode (Ref "r")
                      Tag = "ref" }
                    { Name = "bad-type"
                      Kind = Corpus.Reject
                      Json = """{"schema":[{"name":"x","type":"decimal"}],"columns":{}}"""
                      Tag = "reject" } ]

              let outcomes = Corpus.runCorpus ColumnCodec.codec cases
              Expect.all outcomes (fun o -> o.Passed) "every corpus case passes"

              match Corpus.coverageGate [ "all"; "ref"; "reject" ] cases with
              | Ok() -> ()
              | Error m -> failtest m

          // ---- the shared canonical `$type` discipline (Canon) — Stage 1 unification ----

          testCase "encode uses the Canon discipline: Ordinal-sorted keys"
          <| fun _ ->
              let json = ColumnCodec.encode sample
              // top-level keys sort Ordinal: "columns" < "schema"
              Expect.stringStarts json "{\"columns\":" "columns precedes schema (Ordinal)"
              // each column object: "validity" < "values" (Ordinal: 'i' < 'u' at index 3)
              Expect.stringContains json "{\"validity\":" "validity precedes values within a column"

          testCase "Canon float layout matches .NET ToString(\"R\") incl. scientific form"
          <| fun _ ->
              Expect.equal (Canon.render (JFloat 0.1)) "0.1" "fixed-point for small exponents"
              Expect.equal (Canon.render (JFloat 1e21)) "1E+21" "scientific layout above the threshold"
              Expect.equal (Canon.render (JFloat 1e-7)) "1E-07" "scientific layout below the threshold"
              Expect.equal (Canon.render (JFloat -0.0)) "0" "negative zero collapses to 0"

          // ---- Phase 38: non-finite-float guard on the columnar encode ----

          testCase "tryEncode rejects a NaN cell as NonFiniteFloat (not un-decodable wire)"
          <| fun _ ->
              let src =
                  Embedded
                      { Schema = [ "f", FloatType ]
                        Columns = [ Column.create "f" FloatType [ Float 1.0; Float(0.0 / 0.0) ] ] }

              match ColumnCodec.tryEncode src with
              | Error(NonFiniteFloat("f", "NaN")) -> ()
              | other -> failtestf "expected NonFiniteFloat f NaN, got %A" other

          testCase "tryEncode rejects +Infinity and -Infinity by token"
          <| fun _ ->
              let mk f =
                  Embedded
                      { Schema = [ "f", FloatType ]
                        Columns = [ Column.create "f" FloatType [ Float f ] ] }

              match ColumnCodec.tryEncode (mk System.Double.PositiveInfinity) with
              | Error(NonFiniteFloat("f", "Infinity")) -> ()
              | other -> failtestf "expected +Infinity, got %A" other

              match ColumnCodec.tryEncode (mk System.Double.NegativeInfinity) with
              | Error(NonFiniteFloat("f", "-Infinity")) -> ()
              | other -> failtestf "expected -Infinity, got %A" other

          testCase "tryEncode over an all-finite well-formed source equals encode"
          <| fun _ ->
              match ColumnCodec.tryEncode sample with
              | Ok s -> Expect.equal s (ColumnCodec.encode sample) "guarded encode == encode for finite input"
              | Error e -> failtestf "unexpected error: %s" (ColumnCodec.errorString e)

          // ---- Phase 43: Table.validate structural well-formedness ----

          testCase "Table.validate flags a ragged column"
          <| fun _ ->
              let t =
                  { Schema = [ "a", IntType; "b", IntType ]
                    Columns =
                      [ Column.create "a" IntType [ Int 1; Int 2 ]
                        Column.create "b" IntType [ Int 9 ] ] }

              match Table.validate t with
              | Error(LengthMismatch("b", 2, 1)) -> ()
              | other -> failtestf "expected ragged LengthMismatch, got %A" other

          testCase "Table.validate flags a schema name with no column, and an extra column"
          <| fun _ ->
              let missing =
                  { Schema = [ "a", IntType; "b", IntType ]
                    Columns = [ Column.create "a" IntType [ Int 1 ] ] }

              match Table.validate missing with
              | Error(Malformed _) -> ()
              | other -> failtestf "expected Malformed (missing column), got %A" other

              let extra =
                  { Schema = [ "a", IntType ]
                    Columns = [ Column.create "a" IntType [ Int 1 ]; Column.create "z" IntType [ Int 1 ] ] }

              match Table.validate extra with
              | Error(Malformed _) -> ()
              | other -> failtestf "expected Malformed (extra column), got %A" other

          testCase "Table.validate flags a column whose type disagrees with the schema"
          <| fun _ ->
              let t =
                  { Schema = [ "a", IntType ]
                    Columns = [ Column.create "a" StringType [ Str "x" ] ] }

              match Table.validate t with
              | Error(TypeMismatch("a", "int", "string")) -> ()
              | other -> failtestf "expected TypeMismatch, got %A" other

          testCase "Table.validate passes a well-formed table; tryEncode rejects a malformed one"
          <| fun _ ->
              Expect.equal (Table.validate sampleTable) (Ok()) "the sample table is well-formed"

              let malformed =
                  Embedded
                      { Schema = [ "a", IntType; "b", IntType ]
                        Columns = [ Column.create "a" IntType [ Int 1 ] ] }

              match ColumnCodec.tryEncode malformed with
              | Error(Malformed _) -> ()
              | other -> failtestf "expected tryEncode to reject malformed table, got %A" other

          // Phase 33 — Schema.diff + compatibility verdict + fingerprint.
          testCase "Schema.diff reports added / removed / retyped / reordered"
          <| fun _ ->
              let old = [ "a", IntType; "b", StringType; "c", BoolType ]
              let target = [ "b", StringType; "a", FloatType; "d", DateType ]
              let delta = Schema.diff old target
              Expect.equal delta.Added [ "d", DateType ] "d added"
              Expect.equal delta.Removed [ "c", BoolType ] "c removed"
              Expect.equal delta.Retyped [ "a", IntType, FloatType ] "a retyped int→float"
              Expect.isTrue delta.Reordered "a/b swapped relative order"

          testCase "Schema.classify: widening a depended-on column is Compatible, narrowing is Breaking"
          <| fun _ ->
              let widened =
                  Schema.diff [ "a", IntType; "b", StringType ] [ "a", FloatType; "b", StringType ]

              Expect.equal (Schema.classify [ "a"; "b" ] widened) Compatible "int→float is a safe widening"

              let narrowed = Schema.diff [ "a", FloatType ] [ "a", IntType ]

              match Schema.classify [ "a" ] narrowed with
              | Breaking reasons -> Expect.isNonEmpty reasons "narrowing names a reason"
              | other -> failtestf "expected Breaking, got %A" other

          testCase "Schema.classify: removing a depended-on column is Breaking; an un-depended change is Compatible"
          <| fun _ ->
              let delta = Schema.diff [ "a", IntType; "b", IntType ] [ "a", IntType ]

              match Schema.classify [ "b" ] delta with
              | Breaking _ -> ()
              | other -> failtestf "expected Breaking (b removed), got %A" other

              Expect.equal (Schema.classify [ "a" ] delta) Compatible "an un-depended-on removal is safe"

          testCase "Schema.fingerprint is stable + order-sensitive + type-sensitive"
          <| fun _ ->
              let s1 = [ "a", IntType; "b", FloatType ]

              Expect.equal
                  (Schema.fingerprint s1)
                  (Schema.fingerprint [ "a", IntType; "b", FloatType ])
                  "same schema ⇒ same fingerprint"

              Expect.notEqual
                  (Schema.fingerprint s1)
                  (Schema.fingerprint [ "b", FloatType; "a", IntType ])
                  "reorder changes the fingerprint"

              Expect.notEqual
                  (Schema.fingerprint s1)
                  (Schema.fingerprint [ "a", FloatType; "b", FloatType ])
                  "retype changes the fingerprint"

          // Phase 36 — Column.aggregate public surface.
          testCase "Column.aggregate computes the v1 aggregates with null-skip + pinned float semantics"
          <| fun _ ->
              let ints = Column.create "x" IntType [ Int 10; Null; Int 30; Int 20 ]
              Expect.equal (Column.aggregate Sum ints) (Ok(Int 60)) "Sum skips null, keeps int"
              Expect.equal (Column.aggregate Count ints) (Ok(Int 3)) "Count is present-only"
              Expect.equal (Column.aggregate Mean ints) (Ok(Float 20.0)) "Mean is float over present"
              Expect.equal (Column.aggregate Min ints) (Ok(Int 10)) "Min over present"
              Expect.equal (Column.aggregate Max ints) (Ok(Int 30)) "Max over present"
              Expect.equal (Column.aggregate First ints) (Ok(Int 10)) "First keeps the first cell"
              Expect.equal (Column.aggregate Last ints) (Ok(Int 20)) "Last keeps the last cell"

              let floats = Column.create "y" FloatType [ Float 1.0; Float 2.0; Float 6.0 ]
              Expect.equal (Column.aggregate Median floats) (Ok(Float 2.0)) "Median of 3 is the middle"
              Expect.equal (Column.aggregate Mean floats) (Ok(Float 3.0)) "Mean is the pinned float mean"

          testCase "Column.aggregate over an all-null / empty numeric column is Null"
          <| fun _ ->
              Expect.equal
                  (Column.aggregate Sum (Column.create "x" IntType [ Null; Null ]))
                  (Ok Null)
                  "Sum of all-null is Null"

              Expect.equal (Column.aggregate Mean (Column.create "x" IntType [])) (Ok Null) "Mean of empty is Null"

          testCase "Column.aggregate names an incompatible aggregate type"
          <| fun _ ->
              let strs = Column.create "s" StringType [ Str "a"; Str "b" ]

              match Column.aggregate Sum strs with
              | Error(IncompatibleAggType("sum", "string", expected)) ->
                  Expect.equal expected [ "int"; "float" ] "enumerates numeric types"
              | other -> failtestf "expected IncompatibleAggType, got %A" other

          testCase "Column.aggregate Sum overflow is a named AggregateOverflow"
          <| fun _ ->
              let big = Column.create "x" IntType [ Int System.Int32.MaxValue; Int 1 ]

              match Column.aggregate Sum big with
              | Error(AggregateOverflow _) -> ()
              | other -> failtestf "expected AggregateOverflow, got %A" other ]
