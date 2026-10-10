module Fuaran.Core.Tests.ColumnTests

open Expecto
open Fuaran.Core

// ---- fixtures ----

/// A column of type `ty` holding `cells`, built through `Column.ofCells` (Phase 417) — for a fixture
/// whose cells fit their type; a refusal fails the test that asked for it.
let private mkCol (name: string) (ty: ColumnType) (cells: Cell list) : Column =
    match Column.ofCells name ty cells with
    | Ok c -> c
    | Error e -> failtestf "column %s did not build: %A" name e

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
        [ mkCol "i" IntType [ Int 1; Null; Int -42 ]
          mkCol "f" FloatType [ Float 0.1; Float(1.0 / 3.0); Null ]
          mkCol "b" BoolType [ Bool true; Null; Bool false ]
          mkCol "s" StringType [ Str "a\"b"; Str ""; Null ]
          mkCol "d" DateType [ Date "2026-06-22"; Null; Date "1970-01-01" ]
          mkCol "t" TimestampType [ Timestamp "2026-06-22T17:00:00Z"; Null; Timestamp "2000-01-01T00:00:00Z" ]
          mkCol "m" DecimalType [ Decimal "12.5"; Null; Decimal "-0.05" ] ] }

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
        |> List.map (fun (name, ty) -> mkCol name ty [ for r in 0 .. rows - 1 -> mkCell ty r ])

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
                          (Column.toCells c)
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
                  | [ c ] ->
                      Expect.equal (Column.toCells c) [ Timestamp "1969-12-30T23:59:59Z" ] "one second before Dec 31"
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
                  Expect.equal expected ColumnType.all "enumerates the valid types"
              | other -> failtestf "expected UnknownType, got %A" other

          testCase "TypeMismatch — a string where an int column is declared"
          <| fun _ ->
              let json =
                  """{"schema":[{"name":"x","type":"int"}],"columns":{"x":{"values":["nope"],"validity":[true]}}}"""

              match ColumnCodec.decode json with
              | Error(TypeMismatch("x", IntType, "string")) -> ()
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
                        Columns = [ mkCol "f" FloatType [ Float 1.0; Float(0.0 / 0.0) ] ] }

              match ColumnCodec.tryEncode src with
              | Error(NonFiniteFloat("f", "NaN")) -> ()
              | other -> failtestf "expected NonFiniteFloat f NaN, got %A" other

          testCase "tryEncode rejects +Infinity and -Infinity by token"
          <| fun _ ->
              let mk f =
                  Embedded
                      { Schema = [ "f", FloatType ]
                        Columns = [ mkCol "f" FloatType [ Float f ] ] }

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
                    Columns = [ mkCol "a" IntType [ Int 1; Int 2 ]; mkCol "b" IntType [ Int 9 ] ] }

              // RaggedColumns since Phase 299 — LengthMismatch names one column's values and
              // validity arrays disagreeing on the wire, a different fault.
              match Table.validate t with
              | Error(RaggedColumns("b", 2, 1)) -> ()
              | other -> failtestf "expected RaggedColumns, got %A" other

          testCase "Table.validate flags a schema name with no column, and an extra column"
          <| fun _ ->
              let missing =
                  { Schema = [ "a", IntType; "b", IntType ]
                    Columns = [ mkCol "a" IntType [ Int 1 ] ] }

              match Table.validate missing with
              | Error(Malformed _) -> ()
              | other -> failtestf "expected Malformed (missing column), got %A" other

              let extra =
                  { Schema = [ "a", IntType ]
                    Columns = [ mkCol "a" IntType [ Int 1 ]; mkCol "z" IntType [ Int 1 ] ] }

              match Table.validate extra with
              | Error(Malformed _) -> ()
              | other -> failtestf "expected Malformed (extra column), got %A" other

          testCase "Table.validate flags a column whose type disagrees with the schema"
          <| fun _ ->
              let t =
                  { Schema = [ "a", IntType ]
                    Columns = [ mkCol "a" StringType [ Str "x" ] ] }

              match Table.validate t with
              | Error(TypeMismatch("a", IntType, "string")) -> ()
              | other -> failtestf "expected TypeMismatch, got %A" other

          testCase "Table.validate passes a well-formed table; tryEncode rejects a malformed one"
          <| fun _ ->
              Expect.equal (Table.validate sampleTable) (Ok()) "the sample table is well-formed"

              let malformed =
                  Embedded
                      { Schema = [ "a", IntType; "b", IntType ]
                        Columns = [ mkCol "a" IntType [ Int 1 ] ] }

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
              let ints = mkCol "x" IntType [ Int 10; Null; Int 30; Int 20 ]
              Expect.equal (Column.aggregate Sum ints) (Ok(Int 60)) "Sum skips null, keeps int"
              Expect.equal (Column.aggregate Count ints) (Ok(Int 3)) "Count is present-only"
              Expect.equal (Column.aggregate Mean ints) (Ok(Float 20.0)) "Mean is float over present"
              Expect.equal (Column.aggregate Min ints) (Ok(Int 10)) "Min over present"
              Expect.equal (Column.aggregate Max ints) (Ok(Int 30)) "Max over present"
              Expect.equal (Column.aggregate First ints) (Ok(Int 10)) "First keeps the first cell"
              Expect.equal (Column.aggregate Last ints) (Ok(Int 20)) "Last keeps the last cell"

              let floats = mkCol "y" FloatType [ Float 1.0; Float 2.0; Float 6.0 ]
              Expect.equal (Column.aggregate Median floats) (Ok(Float 2.0)) "Median of 3 is the middle"
              Expect.equal (Column.aggregate Mean floats) (Ok(Float 3.0)) "Mean is the pinned float mean"

          testCase "Column.aggregate over an all-null / empty numeric column is Null"
          <| fun _ ->
              Expect.equal (Column.aggregate Sum (mkCol "x" IntType [ Null; Null ])) (Ok Null) "Sum of all-null is Null"

              Expect.equal (Column.aggregate Mean (mkCol "x" IntType [])) (Ok Null) "Mean of empty is Null"

          testCase "Column.aggregate names an incompatible aggregate type"
          <| fun _ ->
              let strs = mkCol "s" StringType [ Str "a"; Str "b" ]

              match Column.aggregate Sum strs with
              | Error(IncompatibleAggType(Sum, StringType, expected)) ->
                  Expect.equal expected [ IntType; FloatType; DecimalType ] "enumerates numeric types"
              | other -> failtestf "expected IncompatibleAggType, got %A" other

          testCase "Column.aggregate Sum overflow is a named AggregateOverflow"
          <| fun _ ->
              let big = mkCol "x" IntType [ Int System.Int32.MaxValue; Int 1 ]

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

                  Expect.equal
                      (Column.toCells m)
                      [ Decimal "12.5"; Null; Decimal "-3" ]
                      "canonical cells, the masked one Null"

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
                  Expect.equal
                      (Table.tryColumn "m" t |> Option.get |> Column.toCells)
                      [ Decimal "3" ]
                      "an integer is exact"
              | other -> failtestf "unexpected: %A" other

              match ColumnCodec.decode (withValues "3.5") with
              | Error(TypeMismatch("m", DecimalType, "float")) -> ()
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
                        Columns = [ mkCol "m" DecimalType [ Decimal text ] ] }

              for text in [ "1.50"; "abc"; "01" ] do
                  match ColumnCodec.tryEncode (build text) with
                  | Error(MalformedShape _) -> ()
                  | other -> failtestf "expected MalformedShape for '%s', got %A" text other

          testCase "Column.aggregate over a decimal column: an exact Sum, exact Min and Max, a float Mean"
          <| fun _ ->
              let col = mkCol "m" DecimalType [ Decimal "0.1"; Decimal "0.2"; Null; Decimal "-5" ]

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
                  (Column.aggregate Sum (mkCol "m" DecimalType [ Null; Null ]))
                  (Ok Null)
                  "Sum of all-null is Null"

              // Ten tenths: the float sum is 0.9999999999999999.
              let tenths = mkCol "m" DecimalType (List.replicate 10 (Decimal "0.1"))
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
      Columns = [ mkCol "c" ty cells ] }

/// A source from `genSource`, with — on about half the seeds — one fault the codec cannot carry
/// injected: a column whose type disagrees with its schema entry, a repeated column, non-canonical
/// decimal or temporal text, a non-finite float, or a ragged column. The law below needs both
/// outcomes.
///
/// Phase 417: fault 0 was a cell outside its column's type (a `Bool` prepended to the first column),
/// which the typed storage can no longer represent — `Column.ofCells` refuses it at construction,
/// and `trustsNothingTests` pins that refusal. It is replaced by the other `TypeMismatch` the table
/// can still carry: the first column rebuilt as a column of another type under its schema entry's
/// name. Faults 2–4 put their cell in the first column OF ITS TYPE (a decimal, date or float
/// column), since in a column of any other type the cell is now unrepresentable too.
let private genMaybeBroken (seed: int) : DataSource =
    match genSource seed with
    | Ref r -> Ref r
    | Embedded t ->
        let fault = (seed * 7 + 3) % 12

        /// The column rebuilt — same name, same type — over `f` of its cells.
        let rebuild (f: Cell list -> Cell list) (c: Column) : Column =
            mkCol c.Name c.Type (f (Column.toCells c))

        /// Replace the head cell of the first column of type `ty` (if any, and non-empty) with `cell`.
        let replaceHeadOf (ty: ColumnType) (cell: Cell) : Table =
            match t.Columns |> List.tryFindIndex (fun c -> c.Type = ty && Column.length c > 0) with
            | Some k ->
                { t with
                    Columns =
                        t.Columns
                        |> List.mapi (fun i c ->
                            if i = k then
                                rebuild (fun cells -> cell :: List.tail cells) c
                            else
                                c) }
            | None -> t

        let broken =
            match fault with
            | 0 ->
                match t.Columns with
                | c :: rest ->
                    let n = Column.length c

                    let retyped =
                        if c.Type = BoolType then
                            Column.ofInts c.Name (Vector.init n id) (Validity.all n)
                        else
                            Column.ofBools c.Name (Vector.init n (fun i -> i % 2 = 0)) (Validity.all n)

                    { t with Columns = retyped :: rest }
                | [] -> t
            | 1 ->
                match t.Columns with
                | c :: _ -> { t with Columns = t.Columns @ [ c ] }
                | [] -> t
            | 2 -> replaceHeadOf DecimalType (Decimal "1.50")
            | 3 -> replaceHeadOf DateType (Date "2026-02-30")
            | 4 -> replaceHeadOf FloatType (Float nan)
            | 5 ->
                match t.Columns with
                | a :: b :: rest when Column.length b > 0 ->
                    { t with
                        Columns = a :: rebuild List.tail b :: rest }
                | _ -> t
            | _ -> t

        Embedded broken

[<Tests>]
let trustsNothingTests =
    testList
        "Column.trusts nothing it is handed (Phase 299)"
        [ // Phase 417: the refusal moved from `Table.validate` to construction — the typed storage
          // cannot hold a cell outside its column's type, so `Column.ofCells` refuses it with the
          // `TypeMismatch` validate used to name, through the same `ColumnType.widens`.
          testCase "a cell outside its column's type is refused at construction, through ColumnType.widens"
          <| fun _ ->
              Expect.equal
                  (Column.ofCells "c" IntType [ Bool true ])
                  (Error(TypeMismatch("c", IntType, "bool")))
                  "a Bool in an int column"

              Expect.equal
                  (Column.ofCells "c" DecimalType [ Float 1.5 ])
                  (Error(TypeMismatch("c", DecimalType, "float")))
                  "a Float in a decimal column"

              Expect.equal
                  (Column.ofCells "c" FloatType [ Decimal "1.5" ])
                  (Error(TypeMismatch("c", FloatType, "decimal")))
                  "a Decimal in a float column"

              Expect.equal
                  (Column.ofCells "c" IntType [ Float 1.0 ])
                  (Error(TypeMismatch("c", IntType, "float")))
                  "a Float in an int column"

              // genMaybeBroken's old fault 0 — a Bool prepended to a column of another type.
              for ty in [ IntType; FloatType; StringType; DateType; TimestampType; DecimalType ] do
                  Expect.equal
                      (Column.ofCells "c0" ty [ Bool true; Null ])
                      (Error(TypeMismatch("c0", ty, "bool")))
                      (sprintf "a Bool at the head of a %A column" ty)

              Expect.equal (Table.validate (oneColumn FloatType [ Int 3; Null ])) (Ok()) "Int widens into float"
              Expect.equal (Table.validate (oneColumn DecimalType [ Int 3 ])) (Ok()) "Int widens into decimal"

          testCase "validate refuses a duplicate schema name and a duplicate column name"
          <| fun _ ->
              let col = mkCol "a" IntType [ Int 1 ]

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
                        Columns = [ mkCol "a" IntType [ Int 1 ]; mkCol "b" IntType [] ] })
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

          // Phase 417: a cell of another TYPE can no longer reach `aggregate` — `Column.ofCells` refuses
          // it at construction (so it is neither truncated into a Sum nor dropped from one and counted
          // in a Mean, because no column can hold it). `CellOutsideType` remains for the one cell the
          // storage can hold and `aggregate` cannot read: a `Decimal` whose text is not decimal.
          testCase "aggregate refuses a cell outside its column's type by name, rather than truncating or dropping"
          <| fun _ ->
              Expect.equal
                  (Column.ofCells "a" IntType [ Int 1; Float 2.7 ])
                  (Error(TypeMismatch("a", IntType, "float")))
                  "a Float in an int column cannot be built, so it is never truncated into the Sum"

              Expect.equal
                  (Column.ofCells "m" DecimalType [ Decimal "1"; Float 2.5 ])
                  (Error(TypeMismatch("m", DecimalType, "float")))
                  "a Float in a decimal column cannot be built, so it is never dropped from Sum and counted in Mean"

              Expect.equal
                  (Column.ofCells "b" IntType [ Bool true ])
                  (Error(TypeMismatch("b", IntType, "bool")))
                  "a Bool in an int column cannot be built"

              match Column.aggregate Count (mkCol "m" DecimalType [ Decimal "1"; Decimal "abc" ]) with
              | Error(CellOutsideType("m", DecimalType, cell)) ->
                  Expect.stringContains cell "abc" "every aggregate admits its cells first"
              | other -> failtestf "expected CellOutsideType from Count, got %A" other

              match Column.aggregate Sum (mkCol "m" DecimalType [ Decimal "abc" ]) with
              | Error(CellOutsideType("m", DecimalType, cell)) -> Expect.stringContains cell "abc" "names the text"
              | other -> failtestf "expected CellOutsideType, got %A" other

              Expect.equal
                  (Column.aggregate Sum (mkCol "f" FloatType [ Int 1; Float 0.5 ]))
                  (Ok(Float 1.5))
                  "an Int widens into a float column's Sum"

          testCase "aggregate canonicalises decimal text at entry"
          <| fun _ ->
              let col = mkCol "m" DecimalType [ Decimal "1.50"; Decimal "1.5"; Decimal "02" ]

              Expect.equal (Column.aggregate CountDistinct col) (Ok(Int 2)) "1.50 and 1.5 are one value"
              Expect.equal (Column.aggregate Min col) (Ok(Decimal "1.5")) "Min answers canonical text"
              Expect.equal (Column.aggregate Max col) (Ok(Decimal "2")) "Max answers canonical text"
              Expect.equal (Column.aggregate First col) (Ok(Decimal "1.5")) "First answers canonical text"

          testCase "Min / Max / Median order NaN last and -0 equal to 0, on every host"
          <| fun _ ->
              let col = mkCol "f" FloatType [ Float 3.0; Float nan; Float -1.0; Float -0.0 ]

              Expect.equal (Column.aggregate Min col) (Ok(Float -1.0)) "Min is not NaN"

              match Column.aggregate Max col with
              | Ok(Float f) -> Expect.isTrue (System.Double.IsNaN f) "Max is NaN — NaN sorts last"
              | other -> failtestf "expected Float NaN, got %A" other

              // sorted: -1, -0, 3, NaN — the median of four is the mean of -0 and 3.
              Expect.equal (Column.aggregate Median col) (Ok(Float 1.5)) "Median counts NaN at the top"

              Expect.equal
                  (Column.aggregate CountDistinct (mkCol "f" FloatType [ Float 0.0; Float -0.0; Float nan; Float nan ]))
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
                          bits (Column.aggregate Min (mkCol "f" FloatType [ Float x; Float y ]))

                      let bitsOf (x: float) = System.BitConverter.DoubleToInt64Bits x
                      let tie = minOf a b = bitsOf a && minOf b a = bitsOf b

                      let oneValue =
                          Column.aggregate CountDistinct (mkCol "f" FloatType [ Float a; Float b ]) = Ok(Int 1)

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
                  | Ok(Embedded t) -> t.Columns |> List.collect Column.toCells
                  | other -> failtestf "unexpected: %A" other

              Expect.equal (cells (decode "3000000000")) [ Decimal "3000000000" ] "past int32, as 12 always was"
              Expect.equal (cells (decode "-3000000000")) [ Decimal "-3000000000" ] "and negative"
              Expect.equal (cells (decode "3e9")) [ Decimal "3000000000" ] "whatever the token's spelling"

              Expect.equal
                  (cells (decode "9007199254740992"))
                  [ Decimal "9007199254740992" ]
                  "2^53 itself, the guard's edge"

              match decode "1e300" with
              | Error(TypeMismatch("m", DecimalType, "float")) -> ()
              | other -> failtestf "past the guard is refused, got %A" other

          testCase "DecimalText.tryToFloat refuses past the float range rather than returning infinity"
          <| fun _ ->
              let huge = "1" + String.replicate 400 "0"
              Expect.equal (DecimalText.tryToFloat huge) None "no float is nearest to it"
              Expect.equal (DecimalText.tryToFloat ("-" + huge)) None "nor to its negation"
              Expect.equal (DecimalText.tryToFloat "1.5") (Some 1.5) "a float-range decimal reads"

              let col = mkCol "m" DecimalType [ Decimal huge; Decimal "1" ]

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
                        Columns = [ mkCol "m" DecimalType cells ] }
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
              | Ok(Embedded t) ->
                  Expect.equal (t.Columns |> List.collect Column.toCells) [ Null ] "the masked slot is Null"
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

// ---- Phase 417: the typed vector, the typed column, and column equality ----

[<Tests>]
let vectorTests =
    testList
        "Column.Vector (Phase 417)"
        [ testCase "ofArray copies: a later write into the source array does not reach the vector"
          <| fun _ ->
              let source = [| 1; 2; 3 |]
              let v = Vector.ofArray source
              source[0] <- 99
              Expect.equal (Vector.toList v) [ 1; 2; 3 ] "the vector holds its own copy"

          testCase "adopt shares: a write through the adopted array IS visible (the contract it documents)"
          <| fun _ ->
              // The caller promised not to do this (Phase 418 adds the law family); the test pins
              // that `adopt` is the zero-copy route, which is exactly why the promise is needed.
              let source = [| 1; 2; 3 |]
              let v = Vector.adopt source
              source[0] <- 99
              Expect.equal v[0] 99 "the vector reads the adopted array itself"

          testCase "slice is a zero-copy view; an out-of-range slice raises"
          <| fun _ ->
              let v = Vector.ofList [ 0..9 ]
              let s = Vector.slice 2 3 v
              Expect.equal (Vector.toList s) [ 2; 3; 4 ] "the view's elements"
              Expect.equal s.Length 3 "the view's length"

              let parent = Vector.Unsafe.borrow v
              let view = Vector.Unsafe.borrow s
              Expect.isTrue (obj.ReferenceEquals(view.Array, parent.Array)) "the view borrows the parent's array"
              Expect.equal view.Offset (parent.Offset + 2) "at the parent's offset plus the start"
              Expect.equal view.Length 3 "over the view's length"

              // Phase 418 — the view reads its parent's storage itself, not a copy taken at the slice:
              // a write through the parent's borrow (the broken promise the ownership family exists to
              // catch) is what the view then reads. Restored after, so the rest of the test is unchanged.
              parent.Array[parent.Offset + 3] <- 30
              Expect.equal s[1] 30 "a write into the parent's storage is visible through the view"
              parent.Array[parent.Offset + 3] <- 3

              let nested = Vector.slice 1 2 s
              Expect.equal (Vector.toList nested) [ 3; 4 ] "a view of a view"

              Expect.isTrue
                  (obj.ReferenceEquals((Vector.Unsafe.borrow nested).Array, parent.Array))
                  "a view of a view still shares the storage"

              Expect.equal (Vector.Unsafe.borrow nested).Offset 3 "offsets compose"

              Expect.throwsT<System.ArgumentOutOfRangeException>
                  (fun () -> Vector.slice 8 3 v |> ignore)
                  "a range past the end"

              Expect.throwsT<System.ArgumentOutOfRangeException>
                  (fun () -> Vector.slice -1 2 v |> ignore)
                  "a negative start"

              Expect.throwsT<System.ArgumentOutOfRangeException>
                  (fun () -> Vector.slice 0 4 s |> ignore)
                  "past the VIEW's end, though the parent is longer"

              Expect.equal (Vector.slice 10 0 v).Length 0 "an empty slice at the end is in range"

          testCase "the indexer raises past the view's end even though the backing array is longer"
          <| fun _ ->
              let s = Vector.slice 2 3 (Vector.ofList [ 0..9 ])
              Expect.equal s[2] 4 "the view's last element"

              Expect.throwsT<System.IndexOutOfRangeException> (fun () -> s[3] |> ignore) "one past the view's end"

              Expect.throwsT<System.IndexOutOfRangeException> (fun () -> s[-1] |> ignore) "before the view's start"

              Expect.throwsT<System.IndexOutOfRangeException>
                  (fun () -> Vector.item 5 s |> ignore)
                  "Vector.item is the indexer"

          testCase "tryItem is total"
          <| fun _ ->
              let s = Vector.slice 2 3 (Vector.ofList [ 0..9 ])
              Expect.equal (Vector.tryItem 0 s) (Some 2) "in range"
              Expect.equal (Vector.tryItem 2 s) (Some 4) "the last"
              Expect.equal (Vector.tryItem 3 s) None "past the view's end"
              Expect.equal (Vector.tryItem -1 s) None "negative"
              Expect.equal (Vector.tryItem 0 Vector.empty<int>) None "the empty vector"

          testCase "map yields a new vector and leaves the source; toArray is a copy"
          <| fun _ ->
              let v = Vector.ofList [ 1; 2; 3 ]
              let m = Vector.map (fun x -> x * 10) v
              Expect.equal (Vector.toList m) [ 10; 20; 30 ] "mapped"
              Expect.equal (Vector.toList v) [ 1; 2; 3 ] "the source is unchanged"

              Expect.isFalse
                  (obj.ReferenceEquals((Vector.Unsafe.borrow m).Array, (Vector.Unsafe.borrow v).Array))
                  "new storage"

              let a = Vector.toArray v
              a[0] <- 99
              Expect.equal (Vector.toList v) [ 1; 2; 3 ] "a write into toArray's result does not reach the vector"
              Expect.isFalse (obj.ReferenceEquals(a, (Vector.Unsafe.borrow v).Array)) "never the storage"

              Expect.equal
                  (Vector.toArray (Vector.slice 1 2 (Vector.ofList [ 7; 8; 9 ])))
                  [| 8; 9 |]
                  "a view's toArray is the view's range only"

          testCase "float equality is Cell.compare's identity: NaN is one value, -0.0 is 0.0"
          <| fun _ ->
              Expect.equal (Vector.ofList [ nan ]) (Vector.ofList [ nan ]) "NaN equals NaN"
              Expect.equal (Vector.ofList [ nan ]) (Vector.ofList [ -nan ]) "every NaN is one value"
              Expect.equal (Vector.ofList [ -0.0 ]) (Vector.ofList [ 0.0 ]) "-0.0 equals 0.0"
              Expect.notEqual (Vector.ofList [ 1.0 ]) (Vector.ofList [ 2.0 ]) "distinct values differ"
              Expect.notEqual (Vector.ofList [ 1.0 ]) (Vector.ofList [ 1.0; 1.0 ]) "lengths differ ⇒ unequal"
              Expect.notEqual (Vector.ofList [ nan ]) (Vector.ofList [ 0.0 ]) "NaN is not zero"

              Expect.equal (hash (Vector.ofList [ nan ])) (hash (Vector.ofList [ -nan ])) "equal NaN vectors hash equal"
              Expect.equal (hash (Vector.ofList [ -0.0 ])) (hash (Vector.ofList [ 0.0 ])) "and both zeroes"

              Expect.equal
                  (hash (Vector.ofList [ 1.0; 2.5; nan ]))
                  (hash (Vector.ofArray [| 1.0; 2.5; nan |]))
                  "equal vectors hash equal"

          testCase "a Vector<int> and a Vector<string> behave structurally"
          <| fun _ ->
              Expect.equal (Vector.ofList [ 1; 2 ]) (Vector.ofArray [| 1; 2 |]) "ints by element"
              Expect.notEqual (Vector.ofList [ 1; 2 ]) (Vector.ofList [ 2; 1 ]) "order matters"

              Expect.equal
                  (Vector.ofList [ "a"; "" ])
                  (Vector.ofSeq (
                      seq {
                          "a"
                          ""
                      }
                  ))
                  "strings by element"

              Expect.notEqual (Vector.ofList [ "a" ]) (Vector.ofList [ "A" ]) "exactly"
              Expect.equal (hash (Vector.ofList [ "a"; "b" ])) (hash (Vector.ofList [ "a"; "b" ])) "hash agrees"

              let viewed = Vector.slice 1 2 (Vector.ofList [ 9; 1; 2; 9 ])
              Expect.equal viewed (Vector.ofList [ 1; 2 ]) "a view equals a fresh vector of its range"
              Expect.equal (hash viewed) (hash (Vector.ofList [ 1; 2 ])) "and hashes as one"
              Expect.equal Vector.empty<string> (Vector.ofList []) "the empty vectors" ]

[<Tests>]
let typedColumnTests =
    testList
        "Column.typed storage (Phase 417)"
        [ testCase "cell is Null for an absent row and for an out-of-range index"
          <| fun _ ->
              let c = mkCol "x" IntType [ Int 1; Null; Int 3 ]
              Expect.equal (Column.cell 0 c) (Int 1) "present"
              Expect.equal (Column.cell 1 c) Null "absent"
              Expect.equal (Column.cell 3 c) Null "past the end"
              Expect.equal (Column.cell -1 c) Null "negative"
              Expect.isFalse (Column.isPresent 1 c) "the absent row is not present"
              Expect.isFalse (Column.isPresent 3 c) "nor is an out-of-range one"

          testCase "the typed builders round-trip through the typed readers without conversion or copy"
          <| fun _ ->
              let same (handed: Vector<'T>) (read: Vector<'T> option) (what: string) =
                  match read with
                  | Some v -> Expect.isTrue (obj.ReferenceEquals(v, handed)) (what + ": the vector handed in")
                  | None -> failtestf "%s: the reader answered None" what

              let ints = Vector.ofList [ 1; 2 ]
              same ints (Column.tryInts (Column.ofInts "n" ints (Validity.all 2))) "ints"

              let floats = Vector.ofList [ 1.5; nan ]
              same floats (Column.tryFloats (Column.ofFloats "n" floats (Validity.all 2))) "floats"

              let bools = Vector.ofList [ true; false ]
              same bools (Column.tryBools (Column.ofBools "n" bools (Validity.all 2))) "bools"

              let strs = Vector.ofList [ "a"; "b" ]
              same strs (Column.tryStrs (Column.ofStrs "n" strs (Validity.all 2))) "strings"

              let dates = Vector.ofList [ "2026-06-22" ]
              same dates (Column.tryDates (Column.ofDates "n" dates (Validity.all 1))) "dates"

              let stamps = Vector.ofList [ "2026-06-22T17:00:00Z" ]
              same stamps (Column.tryTimestamps (Column.ofTimestamps "n" stamps (Validity.all 1))) "timestamps"

              let decs = Vector.ofList [ "12.5" ]
              same decs (Column.tryDecimals (Column.ofDecimals "n" decs (Validity.all 1))) "decimals"

          testCase "a typed reader answers None for a column of another type"
          <| fun _ ->
              let ints = Column.ofInts "n" (Vector.ofList [ 1 ]) (Validity.all 1)
              Expect.isNone (Column.tryFloats ints) "tryFloats on an int column"
              Expect.isNone (Column.tryBools ints) "tryBools"
              Expect.isNone (Column.tryStrs ints) "tryStrs"
              Expect.isNone (Column.tryDates ints) "tryDates"
              Expect.isNone (Column.tryTimestamps ints) "tryTimestamps"
              Expect.isNone (Column.tryDecimals ints) "tryDecimals"

              Expect.isNone
                  (Column.tryInts (Column.ofDecimals "m" (Vector.ofList [ "1" ]) (Validity.all 1)))
                  "tryInts on a decimal column"

              Expect.isNone
                  (Column.tryStrs (Column.ofDates "d" (Vector.ofList [ "2026-01-01" ]) (Validity.all 1)))
                  "a date column is not a string column"

          testCase "ofCells and toCells round-trip exactly for cells already in normal form"
          <| fun _ ->
              let cases =
                  [ IntType, [ Int 1; Null; Int -7 ]
                    FloatType, [ Float 0.1; Null; Float -1.5; Float infinity ]
                    BoolType, [ Bool false; Bool true; Null ]
                    StringType, [ Str ""; Null; Str "a\"b" ]
                    DateType, [ Date "2026-06-22"; Null ]
                    TimestampType, [ Null; Timestamp "2026-06-22T17:00:00Z" ]
                    DecimalType, [ Decimal "12.5"; Null; Decimal "-0.05" ]
                    IntType, []
                    StringType, [ Null; Null ] ]

              for ty, cells in cases do
                  let c = mkCol "c" ty cells
                  Expect.equal c.Type ty (sprintf "%A: the declared type" ty)
                  Expect.equal (Column.toCells c) cells (sprintf "%A: toCells (ofCells cells) = cells" ty)
                  Expect.equal (Column.length c) (List.length cells) (sprintf "%A: the length" ty)

              match Column.toCells (mkCol "f" FloatType [ Float nan ]) with
              | [ Float f ] -> Expect.isTrue (System.Double.IsNaN f) "a NaN is held as found"
              | other -> failtestf "expected one NaN, got %A" other

          testCase "ofCells normalises a widened Int: Float in a float column, Decimal in a decimal column"
          <| fun _ ->
              let f = mkCol "f" FloatType [ Int 1; Null; Float 2.5 ]
              Expect.equal (Column.toCells f) [ Float 1.0; Null; Float 2.5 ] "Int 1 reads back as Float 1.0"
              Expect.equal (Column.tryFloats f |> Option.map (Vector.item 0)) (Some 1.0) "held as the float"

              let m = mkCol "m" DecimalType [ Int 1; Decimal "2.5"; Int -30 ]
              Expect.equal (Column.toCells m) [ Decimal "1"; Decimal "2.5"; Decimal "-30" ] "Int reads back as Decimal"

              Expect.equal
                  (Column.tryDecimals m |> Option.map Vector.toList)
                  (Some [ "1"; "2.5"; "-30" ])
                  "held as its digits"

              Expect.equal f (mkCol "f" FloatType [ Float 1.0; Null; Float 2.5 ]) "and equals the column built normal"

          testCase "ofCells refuses in row order: the FIRST offending cell is named"
          <| fun _ ->
              Expect.equal
                  (Column.ofCells "c" IntType [ Null; Int 1; Str "x"; Bool true ])
                  (Error(TypeMismatch("c", IntType, "string")))
                  "the Str at row 2, not the Bool at row 3"

              Expect.equal
                  (Column.ofCells "c" IntType [ Null; Int 1; Bool true; Str "x" ])
                  (Error(TypeMismatch("c", IntType, "bool")))
                  "the Bool when it comes first"

              Expect.equal
                  (Column.ofCells "c" FloatType [ Int 1; Decimal "2"; Date "2026-01-01" ])
                  (Error(TypeMismatch("c", FloatType, "decimal")))
                  "a widened Int passes, the Decimal after it does not"

              Expect.isOk (Column.ofCells "c" BoolType [ Null; Null ]) "a Null fits every type"

          testCase "Table.validate names LengthMismatch for storage whose values and validity disagree"
          <| fun _ ->
              let c =
                  { Name = "c"
                    Data = Ints(Vector.ofList [ 1; 2 ], Validity.all 1) }

              Expect.equal
                  (Table.validate
                      { Schema = [ "c", IntType ]
                        Columns = [ c ] })
                  (Error(LengthMismatch("c", 2, 1)))
                  "values 2, validity 1"

              match
                  ColumnCodec.tryEncode (
                      Embedded
                          { Schema = [ "c", IntType ]
                            Columns = [ c ] }
                  )
              with
              | Error(LengthMismatch("c", 2, 1)) -> ()
              | other -> failtestf "tryEncode refuses with validate's error, got %A" other ]

/// A pair of columns of one type for the equality law, from a seed, with the cell lists they were
/// built from: the second column is the first with each row, independently, swapped for an
/// EQUIVALENT cell (one NaN for another, -0.0 for 0.0, an `Int` for the `Float` / `Decimal` it
/// widens to) or, now and then, for an arbitrary one — so the pairs land on both sides of the law,
/// and on its edges; one pair in eight has two lengths. Float columns are built through the typed
/// builder with GARBAGE under every absent row, drawn independently for each column, so the law
/// also covers the element an absent row holds and nothing reads.
let private genColumnPair (seed: int) : (Cell list * Column) * (Cell list * Column) =
    let mutable st = (uint32 seed * 2246822519u) + 7u

    let next () =
        st <- (st * 1664525u) + 1013904223u
        int (st >>> 1)

    let pick n = next () % n

    // Each pool lists cells in classes of equivalents: `partner` picks a member of the cell's class.
    let floatClasses =
        [ [ Float 0.0; Float -0.0 ]
          [ Float nan; Float -nan ]
          [ Float 1.0; Int 1 ]
          [ Float 2.0; Int 2 ]
          [ Float 2.5 ]
          [ Float -1.0; Int -1 ]
          [ Float infinity ]
          [ Null ] ]

    let intClasses = [ [ Int 0 ]; [ Int 1 ]; [ Int -1 ]; [ Int 1000 ]; [ Null ] ]

    let decimalClasses =
        [ [ Decimal "0"; Int 0 ]
          [ Decimal "1"; Int 1 ]
          [ Decimal "-2.5" ]
          [ Decimal "10"; Int 10 ]
          [ Null ] ]

    let stringClasses =
        [ [ Str "" ]; [ Str "a" ]; [ Str "A" ]; [ Str "a\n" ]; [ Null ] ]

    let boolClasses = [ [ Bool true ]; [ Bool false ]; [ Null ] ]

    let ty, classes =
        match pick 10 with
        | 0 -> IntType, intClasses
        | 1 -> DecimalType, decimalClasses
        | 2 -> StringType, stringClasses
        | 3 -> BoolType, boolClasses
        | _ -> FloatType, floatClasses

    let draw () =
        let cls = classes[pick classes.Length]
        cls[pick cls.Length]

    // A cell's class, found by its `Cell.token` — `List.contains` would miss NaN, which `=` on a
    // `Cell` does not find equal to itself.
    let partner (cell: Cell) =
        let cls =
            classes |> List.find (List.exists (fun c -> Cell.token c = Cell.token cell))

        cls[pick cls.Length]

    let lenA = pick 5
    let lenB = if pick 8 = 0 then pick 5 else lenA
    let cellsA = List.init lenA (fun _ -> draw ())

    let cellsB =
        List.init lenB (fun i ->
            if i < lenA && pick 5 <> 0 then
                partner cellsA[i]
            else
                draw ())

    let garbage () =
        [| nan; 7.0; -3.0; 0.0; 1e300 |][pick 5]

    let build (cells: Cell list) : Column =
        if ty = FloatType then
            let values =
                cells
                |> List.map (fun c ->
                    match c with
                    | Float f -> f
                    | Int i -> float i
                    | _ -> garbage ())
                |> Array.ofList

            let mask = cells |> List.map (fun c -> c <> Null)
            Column.ofFloats "c" (Vector.adopt values) (Validity.ofList mask)
        else
            mkCol "c" ty cells

    (cellsA, build cellsA), (cellsB, build cellsB)

[<Tests>]
let columnEqualityTests =
    testList
        "Column.equality (Phase 417)"
        [ // THE EQUALITY LAW: two columns of one type are equal exactly when they hold one row count,
          // one validity mask, and at every present row cells `Cell.compare` calls equal — so a column
          // compares the way its cells do — and equal columns hash equal.
          testCase "columns are equal exactly when their cells compare equal, and equal columns hash equal"
          <| fun _ ->
              let mutable equalPairs = 0
              let mutable unequalPairs = 0
              let mutable nanEqual = 0
              let mutable zeroEqual = 0
              let mutable widenedEqual = 0
              let mutable garbageEqual = 0
              let mutable nullMismatch = 0

              let isNaNCell (c: Cell) =
                  match c with
                  | Float f -> System.Double.IsNaN f
                  | _ -> false

              let isNegZero (c: Cell) =
                  match c with
                  | Float f -> f = 0.0 && System.Double.IsNegative f
                  | _ -> false

              let isInt (c: Cell) =
                  match c with
                  | Int _ -> true
                  | _ -> false

              for seed in 1..500 do
                  let (cellsA, a), (cellsB, b) = genColumnPair seed
                  let n = Column.length a

                  let presence (c: Column) =
                      List.init (Column.length c) (fun i -> Column.isPresent i c)

                  let spec =
                      n = Column.length b
                      && presence a = presence b
                      && List.forall
                          (fun i ->
                              not (Column.isPresent i a)
                              || Cell.compare (Column.cell i a) (Column.cell i b) = Some 0)
                          [ 0 .. n - 1 ]

                  Expect.equal (a = b) spec (sprintf "seed %d: %A vs %A" seed cellsA cellsB)
                  Expect.equal (b = a) spec (sprintf "seed %d: symmetric" seed)

                  if a = b then
                      equalPairs <- equalPairs + 1
                      Expect.equal (hash a) (hash b) (sprintf "seed %d: equal columns hash equal" seed)
                      let rows = List.zip cellsA cellsB

                      if List.exists (fun (x, _) -> isNaNCell x) rows then
                          nanEqual <- nanEqual + 1

                      if List.exists (fun (x, y) -> isNegZero x <> isNegZero y) rows then
                          zeroEqual <- zeroEqual + 1

                      if List.exists (fun (x, y) -> isInt x <> isInt y) rows then
                          widenedEqual <- widenedEqual + 1

                      if a.Type = FloatType && List.exists (fun (x, _) -> x = Null) rows then
                          garbageEqual <- garbageEqual + 1
                  else
                      unequalPairs <- unequalPairs + 1

                      if
                          List.length cellsA = List.length cellsB
                          && List.exists2 (fun x y -> (x = Null) <> (y = Null)) cellsA cellsB
                      then
                          nullMismatch <- nullMismatch + 1

              Expect.isGreaterThan equalPairs 100 "the law measured equal pairs"
              Expect.isGreaterThan unequalPairs 100 "the law measured unequal pairs"
              Expect.isGreaterThan nanEqual 10 "equal pairs holding NaN"
              Expect.isGreaterThan zeroEqual 10 "equal pairs differing in the sign of a zero"
              Expect.isGreaterThan widenedEqual 10 "equal pairs differing as Int against Float or Decimal"
              Expect.isGreaterThan garbageEqual 10 "equal float pairs with an absent row (garbage beneath it)"
              Expect.isGreaterThan nullMismatch 10 "unequal pairs whose nulls sit at different rows"

          testCase "falsifier: a NaN column equals itself, where = on its float list would not"
          <| fun _ ->
              Expect.isFalse ([ nan ] = [ nan ]) "= on the underlying float list says false"

              let c = mkCol "c" FloatType [ Float nan; Float 1.0 ]
              Expect.isTrue (c = c) "the column equals itself"
              Expect.equal c (mkCol "c" FloatType [ Float nan; Float 1.0 ]) "and a column built alike"
              Expect.equal c (mkCol "c" FloatType [ Float -nan; Int 1 ]) "whichever NaN, and the widened Int"
              Expect.equal (hash c) (hash (mkCol "c" FloatType [ Float -nan; Int 1 ])) "hashes agree"
              Expect.equal (mkCol "z" FloatType [ Float -0.0 ]) (mkCol "z" FloatType [ Float 0.0 ]) "-0.0 is 0.0"

          testCase "falsifier: columns differing only at an ABSENT row are equal"
          <| fun _ ->
              let mask = Validity.ofList [ true; false; true ]
              let a = Column.ofFloats "c" (Vector.adopt [| 1.0; 99.0; 3.0 |]) mask
              let b = Column.ofFloats "c" (Vector.adopt [| 1.0; -5.0; 3.0 |]) mask
              Expect.notEqual (Column.tryFloats a) (Column.tryFloats b) "the values vectors differ"
              Expect.equal a b "the columns do not — the absent row is not a cell"
              Expect.equal (hash a) (hash b) "and hash equal"

              let ia =
                  Column.ofInts "i" (Vector.adopt [| 0; 1 |]) (Validity.ofList [ false; true ])

              let ib =
                  Column.ofInts "i" (Vector.adopt [| 42; 1 |]) (Validity.ofList [ false; true ])

              Expect.equal ia ib "an int column alike"
              Expect.equal (hash ia) (hash ib) "and hash equal"

              let sa =
                  Column.ofStrs "s" (Vector.adopt [| "x"; "kept" |]) (Validity.ofList [ false; true ])

              let sb =
                  Column.ofStrs "s" (Vector.adopt [| "y"; "kept" |]) (Validity.ofList [ false; true ])

              Expect.equal sa sb "a string column alike"

              Expect.notEqual
                  a
                  (Column.ofFloats "c" (Vector.adopt [| 1.0; 99.0; 3.0 |]) (Validity.all 3))
                  "but presence itself is part of the value"

              Expect.notEqual a { a with Name = "d" } "and so is the name"

              Expect.notEqual
                  (Column.ofInts "c" (Vector.ofList [ 1 ]) (Validity.all 1))
                  (Column.ofFloats "c" (Vector.ofList [ 1.0 ]) (Validity.all 1))
                  "and the type: Int 1 and Float 1.0 compare equal but live in different columns" ]

// ---- the ownership contract (Phase 418) ----

/// Every read a `Vector` offers, over one vector — `Unsafe.borrow` included, read and never written.
let private readVector (v: Vector<'T>) : unit =
    Vector.toArray v |> ignore
    Vector.toList v |> ignore
    Vector.fold (fun k _ -> k + 1) 0 v |> ignore
    Vector.iter ignore v
    Vector.map id v |> ignore
    Vector.exists (fun _ -> false) v |> ignore
    Vector.slice 0 v.Length v |> ignore
    let lent = Vector.Unsafe.borrow v

    for i in lent.Offset .. lent.Offset + lent.Length - 1 do
        lent.Array[i] |> ignore

/// Every read Core's column layer offers, over a list of columns (Phase 418): the cell reads, every
/// aggregate, the typed readers and every vector read, the cell bridge both ways, and the table check
/// and the codec over a table of the columns. The operation Core certifies ITSELF against with the
/// ownership family — a reader of Core's that wrote into the storage it was handed reds it.
let coreColumnReads (columns: Column list) : unit =
    for c in columns do
        Column.toCells c |> Column.ofCells c.Name c.Type |> ignore

        for i in -1 .. Column.length c do
            Column.cell i c |> ignore
            Column.isPresent i c |> ignore

        readVector (Column.validity c)
        Validity.presentCount (Column.validity c) |> ignore

        for f in [ Sum; Mean; Min; Max; Count; Median; StdDev; First; Last; CountDistinct ] do
            Column.aggregate f c |> ignore

        match c.Data with
        | Ints(xs, _) -> readVector xs
        | Floats(xs, _) -> readVector xs
        | Bools(xs, _) -> readVector xs
        | Strs(xs, _)
        | Dates(xs, _)
        | Timestamps(xs, _)
        | Decimals(xs, _) -> readVector xs

    let table =
        { Schema = columns |> List.map (fun c -> c.Name, c.Type)
          Columns = columns }

    Table.validate table |> ignore

    match ColumnCodec.tryEncode (Embedded table) with
    | Ok json -> ColumnCodec.decode json |> ignore
    | Error _ -> ()

/// A consumer's own draw for `Conformance.columnOwnershipLawsWith` (Phase 418): the two columns a
/// pricing pipeline reads, rebuilt over fresh adopted arrays every iteration.
let ownershipDraw (r: ConfRng.T) : Column list * ConfRng.T =
    let k, r1 = ConfRng.intBelow 5 r
    let n = k + 1

    [ Column.ofInts "qty" (Vector.adopt (Array.init n id)) (Validity.all n)
      Column.ofFloats "price" (Vector.adopt (Array.init n (fun i -> float i * 0.5))) (Validity.all n) ],
    r1

/// The single failing law of an ownership run, with its counterexample — or a test failure naming
/// what the run said instead.
let private ownershipFailure (results: LawResult list) : string =
    match results |> List.filter (fun r -> not r.Passed) with
    | [ { Counterexample = Some cx } ] -> cx
    | other -> failtestf "expected the ownership law alone to fail, got %A" other

/// The kit's sample order (`ColumnarSeamLaws.ownershipSample`): i, f, g, b, s, d, t, m — `f` and `g`
/// adjacent views of one backing array and one mask array.
let private sampleColumn (name: string) (columns: Column list) : Column =
    columns |> List.find (fun c -> c.Name = name)

[<Tests>]
let ownershipTests =
    testList
        "Column.Vector ownership (Phase 418)"
        [ testCase "the ownership family is green over every read Core's column layer offers"
          <| fun _ ->
              let results = Conformance.columnOwnershipLaws coreColumnReads 4242 300

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

              Expect.equal
                  (Conformance.columnOwnershipLaws coreColumnReads 4242 300)
                  results
                  "same seed ⇒ identical report"

              for r in Conformance.columnOwnershipLawsWith coreColumnReads ownershipDraw 4242 100 do
                  Expect.isTrue r.Passed (sprintf "over a consumer's own draw — %s: %A" r.Law r.Counterexample)

          testCase "go-red: a write through an Unsafe borrow fails the family, naming the column"
          <| fun _ ->
              let writeThroughBorrow (columns: Column list) =
                  match Column.tryInts (sampleColumn "i" columns) with
                  | Some xs ->
                      let lent = Vector.Unsafe.borrow xs
                      lent.Array[lent.Offset] <- lent.Array[lent.Offset] + 1
                  | None -> failtest "the kit's sample has no int column i"

              let cx =
                  ownershipFailure (Conformance.columnOwnershipLaws writeThroughBorrow 4242 20)

              Expect.stringContains cx "column 0 \"i\" (int): the bytes of its values moved" "the column, by name"
              Expect.isFalse (cx.Contains "column 1") "and no other"

          testCase "go-red: a borrowed write past one view's end is named as its NEIGHBOUR's bytes moving"
          <| fun _ ->
              // `f` and `g` are adjacent views of one array: the element one past `f`'s range is `g`'s
              // first. The type let the borrower reach it; the fingerprint names whose value changed.
              let overrun (columns: Column list) =
                  match Column.tryFloats (sampleColumn "f" columns) with
                  | Some xs ->
                      let lent = Vector.Unsafe.borrow xs
                      lent.Array[lent.Offset + lent.Length] <- 7.25
                  | None -> failtest "the kit's sample has no float column f"

              let cx = ownershipFailure (Conformance.columnOwnershipLaws overrun 4242 20)
              Expect.stringContains cx "column 2 \"g\" (float): the bytes of its values moved" "the neighbour"
              Expect.isFalse (cx.Contains "column 1 \"f\"") "not the column whose borrow was written through"

          testCase "go-red: a write into a borrowed validity mask is named as the mask moving"
          <| fun _ ->
              let flipMask (columns: Column list) =
                  let lent = Vector.Unsafe.borrow (Column.validity (sampleColumn "b" columns))
                  lent.Array[lent.Offset] <- not lent.Array[lent.Offset]

              let cx = ownershipFailure (Conformance.columnOwnershipLaws flipMask 4242 20)
              Expect.stringContains cx "column 3 \"b\" (bool): the bytes of its validity mask moved" "the mask"

          testCase "go-red: the caller writing an array it adopted fails; the same write after ofArray does not"
          <| fun _ ->
              let kept = ref [||]

              let draw (build: int[] -> Vector<int>) (r: ConfRng.T) =
                  let xs = [| 1; 2; 3 |]
                  kept.Value <- xs
                  [ Column.ofInts "a" (build xs) (Validity.all 3) ], r

              let writeKept (_: Column list) = kept.Value[1] <- 9

              let cx =
                  ownershipFailure (Conformance.columnOwnershipLawsWith writeKept (draw Vector.adopt) 7 5)

              Expect.stringContains cx "column 0 \"a\" (int): the bytes of its values moved" "adopt shares"

              for r in Conformance.columnOwnershipLawsWith writeKept (draw Vector.ofArray) 7 5 do
                  Expect.isTrue r.Passed (sprintf "ofArray copies, so the source's write reaches nothing: %A" r)

          testCase "go-red: a write under an absent row, and -0.0 over 0.0, are writes"
          <| fun _ ->
              // Neither changes a CELL — column equality ignores the element under an absent row and
              // calls -0.0 equal to 0.0 — but both change storage another holder may read.
              let draw (r: ConfRng.T) =
                  [ Column.ofFloats "x" (Vector.adopt [| 1.0; 0.0 |]) (Validity.ofList [ false; true ]) ], r

              let write (index: int) (value: float) (columns: Column list) =
                  match Column.tryFloats columns.Head with
                  | Some xs ->
                      let lent = Vector.Unsafe.borrow xs
                      lent.Array[lent.Offset + index] <- value
                  | None -> failtest "a float column"

              for index, value, what in [ 0, 2.0, "under the absent row"; 1, -0.0, "-0.0 over 0.0" ] do
                  let cx =
                      ownershipFailure (Conformance.columnOwnershipLawsWith (write index value) draw 3 2)

                  Expect.stringContains cx "column 0 \"x\" (float): the bytes of its values moved" what

          testCase "a draw that builds no column reds the family as never reached, not green over nothing"
          <| fun _ ->
              let results = Conformance.columnOwnershipLawsWith ignore (fun r -> [], r) 4242 20

              Expect.isFalse (results |> List.forall _.Passed) "an empty sample is not a pass"

              Expect.stringContains (ownershipFailure results) SampleAdequacy.neverReached "named as the sample's fault" ]
