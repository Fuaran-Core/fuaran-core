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
          "t", TimestampType
          "m", DecimalType ]
      Columns =
        [ Column.create "i" IntType [ Int 1; Null; Int -42 ]
          Column.create "f" FloatType [ Float 0.1; Float(1.0 / 3.0); Null ]
          Column.create "b" BoolType [ Bool true; Null; Bool false ]
          Column.create "s" StringType [ Str "a\"b"; Str ""; Null ]
          Column.create "d" DateType [ Date "2026-06-22"; Null; Date "1970-01-01" ]
          Column.create "t" TimestampType [ Timestamp "2026-06-22T17:00:00Z"; Null; Timestamp "2000-01-01T00:00:00Z" ]
          Column.create "m" DecimalType [ Decimal "12.5"; Null; Decimal "-0.05" ] ] }

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
            // Canonical by construction: an integer part as `string` lays it out, and a fraction
            // that does not end in zero.
            | DecimalType -> Decimal(string (pick 2000 - 1000) + "." + string (pick 90) + string (1 + pick 9))

    let types =
        [ IntType
          FloatType
          BoolType
          StringType
          DateType
          TimestampType
          DecimalType ]
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
              // a float column's value tokens must match Canon.render of the same JFloat exactly —
              // the codec renders through Canon (Phase 299: this compared against Json.render, the
              // author-ordered renderer the codec does not use)
              let json = ColumnCodec.encode sample
              let expected = Canon.render (JFloat 0.1)
              Expect.stringContains json expected "0.1 renders via the Wire {0:R} layout"
              Expect.stringContains json (Canon.render (JFloat(1.0 / 3.0))) "1/3 renders canonically"

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

          // ---- the codec envelope ----

          testCase "NotJson — a syntax error surfaces as NotJson, carrying the parser's structured error"
          <| fun _ ->
              match ColumnCodec.decode "{not json" with
              | Error(NotJson e as err) ->
                  Expect.equal e.Kind ExpectedToken "the parser's classified kind"
                  Expect.equal e.Position 1 "the parser's position"
                  // One prefix, not two (Phase 299: the string form was prefixed twice).
                  Expect.equal
                      (ColumnCodec.errorString err)
                      ("not valid JSON: " + e.Message + " at position 1")
                      "errorString spells it once"
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

          // Phase 299 dropped the Phase 88 rule that a ref source carry a schema: the decoder
          // discarded it, so the rule asked for a statement nothing kept.
          testCase "a ref source decodes with or without a schema, and keeps none"
          <| fun _ ->
              Expect.equal (ColumnCodec.decode """{"ref":"orders"}""") (Ok(Ref "orders")) "no schema"
              Expect.equal (ColumnCodec.decode """{"ref":"orders","schema":[]}""") (Ok(Ref "orders")) "empty schema"

              Expect.equal
                  (ColumnCodec.decode """{"ref":"orders","schema":[{"name":"a","type":"int"}]}""")
                  (Ok(Ref "orders"))
                  "a schema is read for well-formedness and not kept"

              match ColumnCodec.decode """{"ref":"orders","schema":[{"name":"a","type":"money"}]}""" with
              | Error(UnknownType("money", _)) -> ()
              | other -> failtestf "a malformed schema beside a ref is still refused, got %A" other

          testCase "MissingField — a schema column missing from columns"
          <| fun _ ->
              match ColumnCodec.decode """{"schema":[{"name":"x","type":"int"}],"columns":{}}""" with
              | Error(MissingField "columns.x") -> ()
              | other -> failtestf "expected MissingField columns.x, got %A" other

          testCase "UnknownType — a type tag outside the fixed set, with the enumeration"
          <| fun _ ->
              match ColumnCodec.decode """{"schema":[{"name":"x","type":"money"}],"columns":{}}""" with
              | Error(UnknownType("money", expected)) ->
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
                      Json = """{"schema":[{"name":"x","type":"money"}],"columns":{}}"""
                      Tag = "reject" } ]

              let outcomes = Corpus.runCorpus ColumnCodec.codec cases
              Expect.all outcomes _.Passed "every corpus case passes"

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

              // RaggedColumns since Phase 299 — LengthMismatch names one column's values and
              // validity arrays disagreeing on the wire, a different fault.
              match Table.validate t with
              | Error(RaggedColumns("b", 2, 1)) -> ()
              | other -> failtestf "expected RaggedColumns, got %A" other

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

          testCase "Schema.classify: widening a depended-on column is SchemaCompat.Compatible, narrowing is Breaking"
          <| fun _ ->
              let widened =
                  Schema.diff [ "a", IntType; "b", StringType ] [ "a", FloatType; "b", StringType ]

              Expect.equal (Schema.classify [ "a"; "b" ] widened) SchemaCompat.Compatible "int→float is a safe widening"

              let narrowed = Schema.diff [ "a", FloatType ] [ "a", IntType ]

              match Schema.classify [ "a" ] narrowed with
              | SchemaCompat.Breaking reasons -> Expect.isNonEmpty reasons "narrowing names a reason"
              | other -> failtestf "expected SchemaCompat.Breaking, got %A" other

          testCase
              "Schema.classify: removing a depended-on column is SchemaCompat.Breaking; an un-depended change is Compatible"
          <| fun _ ->
              let delta = Schema.diff [ "a", IntType; "b", IntType ] [ "a", IntType ]

              match Schema.classify [ "b" ] delta with
              | SchemaCompat.Breaking _ -> ()
              | other -> failtestf "expected SchemaCompat.Breaking (b removed), got %A" other

              Expect.equal (Schema.classify [ "a" ] delta) SchemaCompat.Compatible "an un-depended-on removal is safe"

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
                  Expect.equal expected [ "int"; "float"; "decimal" ] "enumerates numeric types"
              | other -> failtestf "expected IncompatibleAggType, got %A" other

          testCase "Column.aggregate Sum overflow is a named AggregateOverflow"
          <| fun _ ->
              let big = Column.create "x" IntType [ Int System.Int32.MaxValue; Int 1 ]

              match Column.aggregate Sum big with
              | Error(AggregateOverflow _) -> ()
              | other -> failtestf "expected AggregateOverflow, got %A" other

          // ---- the exact decimal (0.33.0) ----

          testCase "DecimalText.tryCanonical normalises what a database renders and refuses the rest"
          <| fun _ ->
              let canon = DecimalText.tryCanonical
              Expect.equal (canon "12.50") (Some "12.5") "a fixed-scale rendering loses its trailing zero"
              Expect.equal (canon "007") (Some "7") "leading zeros go"
              Expect.equal (canon "0.00") (Some "0") "zero has one spelling"
              Expect.equal (canon "-0.0") (Some "0") "and no sign"
              Expect.equal (canon "-0.050") (Some "-0.05") "a negative fraction keeps its sign"

              Expect.equal
                  (canon "12345678901234567890.123456789")
                  (Some "12345678901234567890.123456789")
                  "there is no precision limit"

              for bad in
                  [ ""
                    "-"
                    "."
                    ".5"
                    "5."
                    "+1"
                    "1e3"
                    "1,000"
                    " 1"
                    "1 "
                    "1.2.3"
                    "--1"
                    "0x10"
                    "NaN" ] do
                  Expect.equal (canon bad) None (sprintf "'%s' is not decimal text" bad)

              Expect.isTrue (DecimalText.isCanonical "12.5") "a canonical text says so"
              Expect.isFalse (DecimalText.isCanonical "12.50") "and a non-canonical one does not"

          testCase "DecimalText.compare orders by value, exactly"
          <| fun _ ->
              Expect.equal (DecimalText.compare "1.50" "1.5") (Some 0) "two spellings of one number are equal"
              Expect.equal (DecimalText.compare "-2" "1") (Some -1) "a negative is below a positive"
              Expect.equal (DecimalText.compare "10" "9.999") (Some 1) "magnitude is by place, not by text"
              Expect.equal (DecimalText.compare "-10" "-9.999") (Some -1) "and reverses under the sign"
              Expect.equal (DecimalText.compare "0" "-0.0") (Some 0) "zero has no sign"

              Expect.equal
                  (DecimalText.compare "0.10000000000000000001" "0.1")
                  (Some 1)
                  "two values one float holds are still ordered"

              Expect.equal (DecimalText.compare "abc" "1") None "text that is not decimal has no order"

          testCase "DecimalText.add is exact"
          <| fun _ ->
              Expect.equal (DecimalText.add "0.1" "0.2") (Some "0.3") "the sum a float gets wrong"
              Expect.equal (DecimalText.add "99.99" "0.01") (Some "100") "a carry through the point"
              Expect.equal (DecimalText.add "999" "1") (Some "1000") "a carry that widens"
              Expect.equal (DecimalText.add "1000" "-1") (Some "999") "a borrow that narrows"
              Expect.equal (DecimalText.add "1" "-1") (Some "0") "cancellation is an unsigned zero"
              Expect.equal (DecimalText.add "-0.5" "0.25") (Some "-0.25") "the larger magnitude's sign wins"
              Expect.equal (DecimalText.add "0.25" "-0.5") (Some "-0.25") "in either order"
              Expect.equal (DecimalText.add "-1.5" "-2.5") (Some "-4") "two negatives add magnitudes"

              Expect.equal
                  (DecimalText.add "12345678901234567890" "0.000000000000000000001")
                  (Some "12345678901234567890.000000000000000000001")
                  "nothing is rounded, at any scale"

              Expect.equal (DecimalText.add "x" "1") None "text that is not decimal has no sum"

          testCase "DecimalText.add and compare agree with integer arithmetic over a sample"
          <| fun _ ->
              // Scaled by 1000, so the expected value is integer arithmetic and the check is exact.
              let render (n: int64) =
                  let sign = if n < 0L then "-" else ""
                  let m = abs n
                  sign + string (m / 1000L) + "." + (string (m % 1000L)).PadLeft(3, '0')

              let mutable st = 12345u

              let next () =
                  st <- (st * 1664525u) + 1013904223u
                  int64 (st >>> 8) % 2000001L - 1000000L

              for _ in 1..500 do
                  let a = next ()
                  let b = next ()

                  Expect.equal
                      (DecimalText.add (render a) (render b))
                      (DecimalText.tryCanonical (render (a + b)))
                      (sprintf "%s + %s" (render a) (render b))

                  Expect.equal
                      (DecimalText.compare (render a) (render b))
                      (Some(compare a b))
                      (sprintf "%s vs %s" (render a) (render b))

          testCase "Cell.decimal canonicalises, and refuses text that is not decimal"
          <| fun _ ->
              Expect.equal (Cell.decimal "12.50") (Some(Decimal "12.5")) "canonical on the way in"
              Expect.equal (Cell.decimal "1e3") None "an exponent is not decimal text"
              Expect.equal (Cell.typeOf (Decimal "1")) (Some DecimalType) "the cell carries its type"

          testCase "a decimal column is strings on the wire, and decodes canonical"
          <| fun _ ->
              let json =
                  """{"schema":[{"name":"m","type":"decimal"}],"columns":{"m":{"values":["12.50","0","-3"],"validity":[true,false,true]}}}"""

              match ColumnCodec.decode json with
              | Ok(Embedded t) ->
                  let m = Table.tryColumn "m" t |> Option.get
                  Expect.equal m.Type DecimalType "the declared type"
                  Expect.equal m.Cells [ Decimal "12.5"; Null; Decimal "-3" ] "canonical cells, the masked one Null"

                  let again = ColumnCodec.encode (Embedded t)
                  Expect.stringContains again "\"12.5\"" "a decimal is emitted as a string"

                  Expect.equal
                      (ColumnCodec.tryEncode (Embedded t))
                      (Ok again)
                      "the guarded encode agrees over canonical cells"
              | other -> failtestf "unexpected: %A" other

          testCase "a decimal column accepts an integer token and refuses a fractional number token"
          <| fun _ ->
              let withValues (values: string) =
                  """{"schema":[{"name":"m","type":"decimal"}],"columns":{"m":{"values":["""
                  + values
                  + """],"validity":[true]}}}"""

              match ColumnCodec.decode (withValues "3") with
              | Ok(Embedded t) ->
                  Expect.equal (Table.tryColumn "m" t |> Option.get).Cells [ Decimal "3" ] "an integer is exact"
              | other -> failtestf "unexpected: %A" other

              match ColumnCodec.decode (withValues "3.5") with
              | Error(TypeMismatch("m", "decimal", "float")) -> ()
              | other -> failtestf "expected TypeMismatch, got %A" other

              match ColumnCodec.decode (withValues "\"1e3\"") with
              | Error(MalformedShape _) -> ()
              | other -> failtestf "expected MalformedShape, got %A" other

          testCase "schema inference never infers decimal"
          <| fun _ ->
              match ColumnCodec.decode """{"columns":{"m":["12.50","3"]}}""" with
              | Ok(Embedded t) -> Expect.equal t.Schema [ "m", StringType ] "digit strings are strings"
              | other -> failtestf "unexpected: %A" other

          testCase "tryEncode rejects a decimal cell that is not canonical"
          <| fun _ ->
              let build (text: string) =
                  Embedded
                      { Schema = [ "m", DecimalType ]
                        Columns = [ Column.create "m" DecimalType [ Decimal text ] ] }

              for text in [ "1.50"; "abc"; "01" ] do
                  match ColumnCodec.tryEncode (build text) with
                  | Error(MalformedShape _) -> ()
                  | other -> failtestf "expected MalformedShape for '%s', got %A" text other

          testCase "Column.aggregate over a decimal column: an exact Sum, exact Min and Max, a float Mean"
          <| fun _ ->
              let col =
                  Column.create "m" DecimalType [ Decimal "0.1"; Decimal "0.2"; Null; Decimal "-5" ]

              Expect.equal (Column.aggregate Sum col) (Ok(Decimal "-4.7")) "Sum is exact and is a decimal"
              Expect.equal (Column.aggregate Min col) (Ok(Decimal "-5")) "Min compares by value"
              Expect.equal (Column.aggregate Max col) (Ok(Decimal "0.2")) "Max compares by value"
              Expect.equal (Column.aggregate Count col) (Ok(Int 3)) "Count skips the null"
              Expect.equal (Column.aggregate CountDistinct col) (Ok(Int 3)) "three distinct values"

              Expect.equal
                  (Column.aggregate Mean col)
                  (Ok(Float(List.sum [ 0.1; 0.2; -5.0 ] / 3.0)))
                  "Mean is the float mean of the nearest floats"

              Expect.equal (Column.aggType Sum DecimalType) DecimalType "Sum keeps the source type"
              Expect.equal (Column.aggType Mean DecimalType) FloatType "Mean is a float"

              Expect.equal
                  (Column.aggregate Sum (Column.create "m" DecimalType [ Null; Null ]))
                  (Ok Null)
                  "Sum of all-null is Null"

              // Ten tenths: the float sum is 0.9999999999999999.
              let tenths = Column.create "m" DecimalType (List.replicate 10 (Decimal "0.1"))
              Expect.equal (Column.aggregate Sum tenths) (Ok(Decimal "1")) "ten tenths are one"

          testCase "int→decimal is a safe widening; float and decimal are not interchangeable"
          <| fun _ ->
              Expect.isTrue (ColumnType.widens IntType DecimalType) "an int is exactly a decimal"
              Expect.isFalse (ColumnType.widens FloatType DecimalType) "a float is an approximation"
              Expect.isFalse (ColumnType.widens DecimalType FloatType) "and a decimal is not one"
              Expect.isFalse (ColumnType.widens DecimalType IntType) "narrowing is not a widening"

              let widened = Schema.diff [ "a", IntType ] [ "a", DecimalType ]
              Expect.equal (Schema.classify [ "a" ] widened) SchemaCompat.Compatible "int→decimal is compatible"

              match Schema.classify [ "a" ] (Schema.diff [ "a", FloatType ] [ "a", DecimalType ]) with
              | SchemaCompat.Breaking reasons -> Expect.isNonEmpty reasons "float→decimal names a reason"
              | other -> failtestf "expected SchemaCompat.Breaking, got %A" other

          testCase "the type tag set names decimal, last"
          <| fun _ ->
              Expect.equal
                  ColumnType.allTags
                  [ "int"; "float"; "bool"; "string"; "date"; "timestamp"; "decimal" ]
                  "the closed set, in encode order"

              Expect.equal (ColumnType.ofTag "decimal") (Some DecimalType) "the tag resolves" ]

/// A table whose one column `c` of type `ty` holds `cells`.
let private oneColumn (ty: ColumnType) (cells: Cell list) : Table =
    { Schema = [ "c", ty ]
      Columns = [ Column.create "c" ty cells ] }

/// A source from `genSource`, with — on about half the seeds — one fault the codec cannot carry
/// injected: a cell outside its column's type, a repeated column, non-canonical decimal or temporal
/// text, a non-finite float, or a ragged column. The law below needs both outcomes.
let private genMaybeBroken (seed: int) : DataSource =
    match genSource seed with
    | Ref r -> Ref r
    | Embedded t ->
        let fault = (seed * 7 + 3) % 12

        let mapFirst (f: Column -> Column) =
            match t.Columns with
            | c :: rest -> { t with Columns = f c :: rest }
            | [] -> t

        let broken =
            match fault with
            | 0 ->
                mapFirst (fun c ->
                    { c with
                        Cells = Bool true :: c.Cells |> List.truncate (max 1 (List.length c.Cells)) })
            | 1 ->
                match t.Columns with
                | c :: _ -> { t with Columns = t.Columns @ [ c ] }
                | [] -> t
            | 2 ->
                mapFirst (fun c ->
                    { c with
                        Cells =
                            (if List.isEmpty c.Cells then
                                 []
                             else
                                 Decimal "1.50" :: List.tail c.Cells) })
            | 3 ->
                mapFirst (fun c ->
                    { c with
                        Cells =
                            (if List.isEmpty c.Cells then
                                 []
                             else
                                 Date "2026-02-30" :: List.tail c.Cells) })
            | 4 ->
                mapFirst (fun c ->
                    { c with
                        Cells =
                            (if List.isEmpty c.Cells then
                                 []
                             else
                                 Float nan :: List.tail c.Cells) })
            | 5 ->
                match t.Columns with
                | a :: b :: rest when not (List.isEmpty b.Cells) ->
                    { t with
                        Columns = a :: { b with Cells = List.tail b.Cells } :: rest }
                | _ -> t
            | _ -> t

        Embedded broken

[<Tests>]
let trustsNothingTests =
    testList
        "Column.trusts nothing it is handed (Phase 299)"
        [ testCase "validate refuses a cell outside its column's type, through ColumnType.widens"
          <| fun _ ->
              Expect.equal
                  (Table.validate (oneColumn IntType [ Bool true ]))
                  (Error(TypeMismatch("c", "int", "bool")))
                  "a Bool in an int column"

              Expect.equal
                  (Table.validate (oneColumn DecimalType [ Float 1.5 ]))
                  (Error(TypeMismatch("c", "decimal", "float")))
                  "a Float in a decimal column"

              Expect.equal
                  (Table.validate (oneColumn FloatType [ Decimal "1.5" ]))
                  (Error(TypeMismatch("c", "float", "decimal")))
                  "a Decimal in a float column"

              Expect.equal
                  (Table.validate (oneColumn IntType [ Float 1.0 ]))
                  (Error(TypeMismatch("c", "int", "float")))
                  "a Float in an int column"

              Expect.equal (Table.validate (oneColumn FloatType [ Int 3; Null ])) (Ok()) "Int widens into float"
              Expect.equal (Table.validate (oneColumn DecimalType [ Int 3 ])) (Ok()) "Int widens into decimal"

          testCase "validate refuses a duplicate schema name and a duplicate column name"
          <| fun _ ->
              let col = Column.create "a" IntType [ Int 1 ]

              Expect.equal
                  (Table.validate
                      { Schema = [ "a", IntType; "a", IntType ]
                        Columns = [ col; col ] })
                  (Error(Malformed "duplicate schema name: a"))
                  "the schema names a twice"

              Expect.equal
                  (Table.validate
                      { Schema = [ "a", IntType ]
                        Columns = [ col; col ] })
                  (Error(Malformed "duplicate column name: a"))
                  "two columns named a"

              // …so encodeJson can no longer be asked to emit a repeated member key.
              match
                  ColumnCodec.tryEncode (
                      Embedded
                          { Schema = [ "a", IntType; "a", IntType ]
                            Columns = [ col; col ] }
                  )
              with
              | Error(Malformed _) -> ()
              | other -> failtestf "tryEncode refuses the duplicate, got %A" other

          testCase "validate refuses non-canonical decimal, date and timestamp text"
          <| fun _ ->
              for cell in [ Decimal "1.50"; Decimal "abc"; Decimal "01" ] do
                  match Table.validate (oneColumn DecimalType [ cell ]) with
                  | Error(MalformedShape d) -> Expect.stringContains d "canonical decimal text" "names the rule"
                  | other -> failtestf "expected MalformedShape for %A, got %A" cell other

              for text in
                  [ ""
                    "2026-02-30"
                    "2023-02-29"
                    "1900-02-29"
                    "2026-6-1"
                    "2026-13-01"
                    "26-06-01" ] do
                  match Table.validate (oneColumn DateType [ Date text ]) with
                  | Error(MalformedShape d) -> Expect.stringContains d "YYYY-MM-DD" "names the form"
                  | other -> failtestf "expected MalformedShape for date '%s', got %A" text other

              for text in
                  [ ""
                    "2026-06-22T24:00:00Z"
                    "2026-06-22T17:60:00Z"
                    "2026-06-22T17:00:60Z"
                    "2026-06-22 17:00:00Z"
                    "2026-06-22T17:00:00"
                    "2026-06-22T17:00:00+00:00"
                    "2026-06-22T17:00:00.000Z" ] do
                  match Table.validate (oneColumn TimestampType [ Timestamp text ]) with
                  | Error(MalformedShape d) -> Expect.stringContains d "YYYY-MM-DDThh:mm:ssZ" "names the form"
                  | other -> failtestf "expected MalformedShape for timestamp '%s', got %A" text other

              for text in [ "2024-02-29"; "2000-02-29"; "0000-01-01"; "9999-12-31" ] do
                  Expect.isTrue (TemporalText.isCanonicalDate text) (sprintf "%s is a date" text)

              Expect.isTrue (TemporalText.isCanonicalTimestamp "1970-01-01T00:00:00Z") "the epoch"
              Expect.isTrue (TemporalText.isCanonicalTimestamp "2026-06-22T23:59:59Z") "the last second"
              Expect.isFalse (TemporalText.isCanonicalDate null) "null is no date"

          testCase "validate refuses a non-finite float, and names the ragged table apart from LengthMismatch"
          <| fun _ ->
              Expect.equal
                  (Table.validate (oneColumn FloatType [ Float nan ]))
                  (Error(NonFiniteFloat("c", "NaN")))
                  "the wire has no NaN"

              Expect.equal
                  (Table.validate
                      { Schema = [ "a", IntType; "b", IntType ]
                        Columns = [ Column.create "a" IntType [ Int 1 ]; Column.create "b" IntType [] ] })
                  (Error(RaggedColumns("b", 1, 0)))
                  "ragged"

          // THE LAW: over what validate accepts, tryEncode is exactly Ok (encode src); over what it
          // refuses, tryEncode refuses with validate's own error. And what encodes, decodes.
          testCase "tryEncode is exactly Ok (encode src) over what validate accepts, and validate's error otherwise"
          <| fun _ ->
              let mutable accepted = 0
              let mutable refused = 0

              for seed in 1..600 do
                  match genMaybeBroken seed with
                  | Ref _ -> ()
                  | Embedded t as src ->
                      match Table.validate t, ColumnCodec.tryEncode src with
                      | Ok(), Ok s ->
                          accepted <- accepted + 1
                          Expect.equal s (ColumnCodec.encode src) (sprintf "seed %d: Ok (encode src)" seed)

                          match ColumnCodec.decode s with
                          | Ok _ -> ()
                          | Error e -> failtestf "seed %d: a table that encodes must decode: %A" seed e
                      | Error e, Error e2 ->
                          refused <- refused + 1
                          Expect.equal e2 e (sprintf "seed %d: tryEncode refuses with validate's error" seed)
                      | v, r -> failtestf "seed %d: validate %A but tryEncode %A" seed v r

              Expect.isGreaterThan accepted 100 "the law measured acceptances"
              Expect.isGreaterThan refused 100 "the law measured refusals"

          testCase "aggregate refuses a cell outside its column's type by name, rather than truncating or dropping"
          <| fun _ ->
              let intCol = Column.create "a" IntType [ Int 1; Float 2.7 ]

              Expect.equal
                  (Column.aggregate Sum intCol)
                  (Error(CellOutsideType("a", "int", "float")))
                  "a Float in an int column is not truncated into the Sum"

              let decCol = Column.create "m" DecimalType [ Decimal "1"; Float 2.5 ]

              Expect.equal
                  (Column.aggregate Mean decCol)
                  (Error(CellOutsideType("m", "decimal", "float")))
                  "a Float in a decimal column is not dropped from Sum and counted in Mean"

              Expect.equal
                  (Column.aggregate Count (Column.create "b" IntType [ Bool true ]))
                  (Error(CellOutsideType("b", "int", "bool")))
                  "every aggregate admits its cells first"

              match Column.aggregate Sum (Column.create "m" DecimalType [ Decimal "abc" ]) with
              | Error(CellOutsideType("m", "decimal", cell)) -> Expect.stringContains cell "abc" "names the text"
              | other -> failtestf "expected CellOutsideType, got %A" other

              Expect.equal
                  (Column.aggregate Sum (Column.create "f" FloatType [ Int 1; Float 0.5 ]))
                  (Ok(Float 1.5))
                  "an Int widens into a float column's Sum"

          testCase "aggregate canonicalises decimal text at entry"
          <| fun _ ->
              let col =
                  Column.create "m" DecimalType [ Decimal "1.50"; Decimal "1.5"; Decimal "02" ]

              Expect.equal (Column.aggregate CountDistinct col) (Ok(Int 2)) "1.50 and 1.5 are one value"
              Expect.equal (Column.aggregate Min col) (Ok(Decimal "1.5")) "Min answers canonical text"
              Expect.equal (Column.aggregate Max col) (Ok(Decimal "2")) "Max answers canonical text"
              Expect.equal (Column.aggregate First col) (Ok(Decimal "1.5")) "First answers canonical text"

          testCase "Min / Max / Median order NaN last and -0 equal to 0, on every host"
          <| fun _ ->
              let col =
                  Column.create "f" FloatType [ Float 3.0; Float nan; Float -1.0; Float -0.0 ]

              Expect.equal (Column.aggregate Min col) (Ok(Float -1.0)) "Min is not NaN"

              match Column.aggregate Max col with
              | Ok(Float f) -> Expect.isTrue (System.Double.IsNaN f) "Max is NaN — NaN sorts last"
              | other -> failtestf "expected Float NaN, got %A" other

              // sorted: -1, -0, 3, NaN — the median of four is the mean of -0 and 3.
              Expect.equal (Column.aggregate Median col) (Ok(Float 1.5)) "Median counts NaN at the top"

              Expect.equal
                  (Column.aggregate
                      CountDistinct
                      (Column.create "f" FloatType [ Float 0.0; Float -0.0; Float nan; Float nan ]))
                  (Ok(Int 2))
                  "-0 is 0 and NaN is one value"

          // The order and the token are one normal form: two floats tie under the aggregate order
          // exactly when they are one distinct value. Probed through the public aggregate: `Min` keeps
          // the FIRST of a tied pair, so a pair ties exactly when `Min` answers the first element in
          // BOTH orders — a strict order answers the smaller one in both.
          testCase "the float order and the distinct token agree over every pair of specials"
          <| fun _ ->
              let specials =
                  [ 0.0
                    -0.0
                    1.0
                    -1.0
                    0.1
                    nan
                    -nan
                    infinity
                    -infinity
                    System.Double.Epsilon
                    1e308 ]

              let bits (c: Result<Cell, AggregateError>) =
                  match c with
                  | Ok(Float f) -> System.BitConverter.DoubleToInt64Bits f
                  | other -> failtestf "expected a float, got %A" other

              for a in specials do
                  for b in specials do
                      let minOf (x: float) (y: float) =
                          bits (Column.aggregate Min (Column.create "f" FloatType [ Float x; Float y ]))

                      let bitsOf (x: float) = System.BitConverter.DoubleToInt64Bits x
                      let tie = minOf a b = bitsOf a && minOf b a = bitsOf b

                      let oneValue =
                          Column.aggregate CountDistinct (Column.create "f" FloatType [ Float a; Float b ]) = Ok(Int 1)

                      Expect.equal tie oneValue (sprintf "%g vs %g: tie ⇔ one distinct value" a b)

          testCase "a decimal column reads a whole-valued number token within the int53 guard"
          <| fun _ ->
              let decode (values: string) =
                  ColumnCodec.decode (
                      """{"schema":[{"name":"m","type":"decimal"}],"columns":{"m":{"values":["""
                      + values
                      + """],"validity":[true]}}}"""
                  )

              let cells (r: Result<DataSource, ColumnError>) =
                  match r with
                  | Ok(Embedded t) -> t.Columns |> List.collect _.Cells
                  | other -> failtestf "unexpected: %A" other

              Expect.equal (cells (decode "3000000000")) [ Decimal "3000000000" ] "past int32, as 12 always was"
              Expect.equal (cells (decode "-3000000000")) [ Decimal "-3000000000" ] "and negative"
              Expect.equal (cells (decode "3e9")) [ Decimal "3000000000" ] "whatever the token's spelling"

              Expect.equal
                  (cells (decode "9007199254740992"))
                  [ Decimal "9007199254740992" ]
                  "2^53 itself, the guard's edge"

              match decode "1e300" with
              | Error(TypeMismatch("m", "decimal", "float")) -> ()
              | other -> failtestf "past the guard is refused, got %A" other

          testCase "DecimalText.tryToFloat refuses past the float range rather than returning infinity"
          <| fun _ ->
              let huge = "1" + String.replicate 400 "0"
              Expect.equal (DecimalText.tryToFloat huge) None "no float is nearest to it"
              Expect.equal (DecimalText.tryToFloat ("-" + huge)) None "nor to its negation"
              Expect.equal (DecimalText.tryToFloat "1.5") (Some 1.5) "a float-range decimal reads"

              let col = Column.create "m" DecimalType [ Decimal huge; Decimal "1" ]

              match Column.aggregate Mean col with
              | Error(AggregateOverflow d) -> Expect.stringContains d "past the float range" "named"
              | other -> failtestf "expected AggregateOverflow, got %A" other

              Expect.equal
                  (Column.aggregate Sum col)
                  (Ok(Decimal("1" + String.replicate 399 "0" + "1")))
                  "the exact Sum still answers"

              // The columnar range rule must not read the refusal as "in range": a decimal past the
              // float range is out of every finite range, in either sign.
              let reg =
                  ColumnValidator.ofRules [ ColumnValidator.inRange "m" 0.0 100.0 ]
                  |> Result.defaultWith (fun e -> failwithf "registry: %A" e)

              let rangeDefects (cells: Cell list) =
                  ColumnValidator.validate
                      reg
                      { Schema = [ "m", DecimalType ]
                        Columns = [ Column.create "m" DecimalType cells ] }
                  |> List.filter (fun d -> d.Code = "COL-INRANGE")
                  |> List.length

              Expect.equal (rangeDefects [ Decimal huge; Decimal("-" + huge); Decimal "5" ]) 2 "both huge values"

          testCase "date and timestamp text is validated at decode, epochs included"
          <| fun _ ->
              let decode (ty: string) (values: string) =
                  ColumnCodec.decode (
                      "{\"schema\":[{\"name\":\"t\",\"type\":\""
                      + ty
                      + "\"}],\"columns\":{\"t\":{\"values\":["
                      + values
                      + """],"validity":[true]}}}"""
                  )

              match decode "date" "\"2026-02-30\"" with
              | Error(MalformedShape _) -> ()
              | other -> failtestf "an impossible date, got %A" other

              match decode "timestamp" "\"2026-06-22T17:00:00+01:00\"" with
              | Error(MalformedShape _) -> ()
              | other -> failtestf "an offset timestamp, got %A" other

              match decode "timestamp" "900000000000000" with
              | Error(MalformedShape d) -> Expect.stringContains d "0000-9999" "names the range"
              | other -> failtestf "an epoch past year 9999, got %A" other

              // A null date slot carries the wire's absent-slot placeholder, which is never read.
              match
                  ColumnCodec.decode
                      """{"schema":[{"name":"t","type":"date"}],"columns":{"t":{"values":[""],"validity":[false]}}}"""
              with
              | Ok(Embedded t) -> Expect.equal (t.Columns |> List.collect _.Cells) [ Null ] "the masked slot is Null"
              | other -> failtestf "unexpected: %A" other

          testCase "decode ends in validate: a ragged, duplicated or repeated-key table is refused at decode"
          <| fun _ ->
              Expect.equal
                  (ColumnCodec.decode """{"columns":{"a":[1,2],"b":[3]}}""")
                  (Error(RaggedColumns("b", 2, 1)))
                  "ragged is refused at decode, not by the encode that follows"

              Expect.equal
                  (ColumnCodec.decode
                      """{"schema":[{"name":"a","type":"int"},{"name":"a","type":"int"}],"columns":{"a":[1]}}""")
                  (Error(Malformed "duplicate schema name: a"))
                  "a schema naming a column twice"

              match ColumnCodec.decode """{"schema":[{"name":"a","type":"int"}],"columns":{"a":[1],"a":[2]}}""" with
              | Error(Malformed d) -> Expect.stringContains d "duplicate column key" "a repeated key"
              | other -> failtestf "expected Malformed, got %A" other ]
