module Fuaran.Core.Tests.ProofOracle.WireColumnTests

// The `Proofs.Oracle` differential for `proofs/WireColumn.fst`: the extracted model
// (`proofs/oracle/WireColumn.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.CanonOracle

// ---------------------------------------------------------------------------
// Phase 428 — the field codec beside `WireColumn.fst` section 10.
// ---------------------------------------------------------------------------

module private FieldDiff =

    let host = ColumnOracle.host

    let showField (f: Field) : string =
        f.Name
        + ":"
        + ColumnType.tag f.Type
        + (match f.Unit with
           | Some u -> " unit=" + Unit.render u
           | None -> "")
        + (match f.Label with
           | Some l -> " label=" + l
           | None -> "")
        + (match f.Description with
           | Some d -> " description=" + d
           | None -> "")
        + (if f.Ext.IsEmpty then
               ""
           else
               " ext="
               + (f.Ext |> Map.toList |> List.map (fun (k, v) -> k + "=" + v) |> String.concat ","))

    let private showProdField (r: Result<Field, ColumnError>) : string =
        match r with
        | Result.Ok f -> "ok " + showField f
        | Result.Error e -> "refused " + ColumnOracle.prodClass e

    let private showModelField (r: WireColumn.res<WireColumn.field>) : string =
        match r with
        | WireColumn.Good f -> "ok " + showField (ColumnOracle.ofModelField f)
        | WireColumn.Bad e -> "refused " + ColumnOracle.modelClass e

    /// The field tally: the fields drawn, those stating a unit, those with extension members and
    /// those with two or more; the documents read by both; the tables drawn, those `validate`
    /// accepts and those outside the normal form; the go-red's lost fields; and the refusal classes.
    let tally: Tally =
        Tally.empty
            [ "Fields"
              "WithUnit"
              "WithExt"
              "ExtMulti"
              "Docs"
              "Tables"
              "Valid"
              "OutsideNormalForm" ]
        |> Tally.sets [ "Refusals" ]

    let private compareText = ColumnDiff.compareText

    /// One document, asked of `decodeField` and of the model's `decode_field`.
    let probeDoc (label: string) (el: JVal) (t: Tally) : Tally =
        let t = Tally.bump "Docs" t
        let prod = ColumnCodec.decodeField el
        let model = WireColumn.decode_field (canonToModel el)

        let t =
            match prod with
            | Result.Error e -> Tally.reach "Refusals" ((ColumnOracle.prodClass e).Split(' ')[0]) t
            | Result.Ok _ -> t

        compareText label "decodeField" (showProdField prod) (showModelField model) t

    /// One field: `fieldJson` beside `field_json` byte for byte, then the document back through
    /// both readers. `reverseExt` is the go-red: the model handed the extension members reversed.
    let probeField (reverseExt: bool) (label: string) (f: Field) (t: Tally) : Tally =
        let t =
            t
            |> Tally.bump "Fields"
            |> Tally.bumpIf f.Unit.IsSome "WithUnit"
            |> Tally.bumpIf (not f.Ext.IsEmpty) "WithExt"
            |> Tally.bumpIf (f.Ext.Count >= 2) "ExtMulti"

        let prodJson = ColumnCodec.fieldJson f

        let modelJson =
            canonOfModel (WireColumn.field_json (ColumnOracle.toModelFieldWith reverseExt f))

        if reverseExt then
            compareText
                label
                "fieldJson through the extension-reversing bridge"
                (Json.render prodJson)
                (Json.render modelJson)
                t
        else
            t
            |> compareText label "fieldJson" (Json.render prodJson) (Json.render modelJson)
            |> probeDoc label prodJson

    // ---- tables with metadata ----

    let showTable (t: Table) : string =
        (t.Schema |> List.map showField |> String.concat ", ")
        + " | "
        + ColumnDiff.showSource (Embedded t)

    let private showProdSource (r: Result<DataSource, ColumnError>) : string =
        match r with
        | Result.Ok(Embedded t) -> "ok " + showTable t
        | Result.Ok(Ref r) -> "ok ref " + r
        | Result.Error e -> "refused " + ColumnOracle.prodClass e

    let private showModelSource (r: WireColumn.res<WireColumn.data_source_f<int, float>>) : string =
        match r with
        | WireColumn.Good(WireColumn.Embedded_f t) -> "ok " + showTable (ColumnOracle.ofModelTableF t)
        | WireColumn.Good(WireColumn.Ref_f r) -> "ok ref " + canonFromChs r
        | WireColumn.Bad e -> "refused " + ColumnOracle.modelClass e

    /// One table: `validate`; `encodeJson` beside `encode_json_f`; `decodeJson` of the encoding beside
    /// `decode_json_f`; and, for a table `validate` accepts, the round trip against the model's
    /// `normal_table_f` — the columns in schema order, every field intact.
    let probeTable (label: string) (table: Table) (t: Tally) : Tally =
        let m = ColumnOracle.toModelTableF table
        let t = Tally.bump "Tables" t
        let prodValid = Table.validate table
        let modelValid = WireColumn.validate_f host m

        let prodV =
            match prodValid with
            | Result.Ok() -> "ok"
            | Result.Error e -> "refused " + ColumnOracle.prodClass e

        let modelV =
            match modelValid with
            | WireColumn.Good _ -> "ok"
            | WireColumn.Bad e -> "refused " + ColumnOracle.modelClass e

        let prodJson = ColumnCodec.encodeJson (Embedded table)
        let modelJson = WireColumn.encode_json_f host (WireColumn.Embedded_f m)
        let prodBack = ColumnCodec.decodeJson prodJson
        let modelBack = WireColumn.decode_json_f host modelJson

        let t =
            t
            |> compareText label "validate" prodV modelV
            |> Tally.bumpIf (Result.isOk prodValid) "Valid"
            |> compareText label "encodeJson" (Json.render prodJson) (Json.render (canonOfModel modelJson))
            |> compareText label "decodeJson (encodeJson t)" (showProdSource prodBack) (showModelSource modelBack)

        match prodValid with
        | Result.Error _ -> t
        | Result.Ok() ->
            let normal = ColumnOracle.ofModelTableF (WireColumn.normal_table_f host m)

            t
            |> Tally.bumpIf (showTable normal <> showTable table) "OutsideNormalForm"
            |> compareText label "the round trip" (showProdSource prodBack) ("ok " + showTable normal)

    // ---- the pools ----

    let private words =
        [| "Revenue"; "payload mass"; "speed, km/h"; "x"; "é"; "a \"quoted\" one"; "" |]

    /// A drawn field: a type the list model spells, a unit from the unit pool or none, a label, a
    /// description and up to three extension members, each stated or not.
    let drawnFields (seed: int) (count: int) : Field list =
        let rng = System.Random(seed)
        let units = UnitDiff.drawn (seed + 7) 64 |> Array.ofList
        let names = [| "a"; "b"; "c"; "value"; "k\"q" |]

        [ for i in 1..count ->
              let f =
                  Field.create
                      (names[rng.Next names.Length] + string i)
                      ColumnOracle.modelTypes[rng.Next ColumnOracle.modelTypes.Length]

              let f =
                  if rng.Next 2 = 0 then
                      Field.withUnit units[rng.Next units.Length] f
                  else
                      f

              let f =
                  if rng.Next 2 = 0 then
                      Field.withLabel words[rng.Next words.Length] f
                  else
                      f

              let f =
                  if rng.Next 2 = 0 then
                      Field.withDescription words[rng.Next words.Length] f
                  else
                      f

              [ 1 .. rng.Next 4 ]
              |> List.fold
                  (fun g k ->
                      Field.withExt (sprintf "vendor.k%d" (k * (1 + rng.Next 3))) words[rng.Next words.Length] g)
                  f ]

    /// The hand-written documents: what `decodeField` refuses and what it reads past.
    let handWritten: (string * JVal) list =
        let entry (members: (string * JVal) list) = JObj members
        let plain = [ "name", JStr "m"; "type", JStr "float" ]

        [ "plain entry", entry plain
          "no name", entry [ "type", JStr "int" ]
          "no type", entry [ "name", JStr "m" ]
          "unknown type", entry [ "name", JStr "m"; "type", JStr "complex" ]
          "name not a string", entry [ "name", JInt 1; "type", JStr "int" ]
          "not an object", JArr []
          "a unit the kit parses", entry (plain @ [ "unit", JStr "km/h" ])
          "a unit in non-canonical spelling", entry (plain @ [ "unit", JStr "s-1.m" ])
          "an aliased unit", entry (plain @ [ "unit", JStr "ml" ])
          "a unit the kit does not parse", entry (plain @ [ "unit", JStr "furlongs" ])
          "an annotated unit", entry (plain @ [ "unit", JStr "m{dry}" ])
          "an empty unit", entry (plain @ [ "unit", JStr "" ])
          "a unit that is not a string", entry (plain @ [ "unit", JInt 3 ])
          "a label", entry (plain @ [ "label", JStr "Length" ])
          "a label that is not a string", entry (plain @ [ "label", JBool true ])
          "a description", entry (plain @ [ "description", JStr "How far." ])
          "a description that is not a string", entry (plain @ [ "description", JArr [] ])
          "extension members", entry (plain @ [ "ext", JObj [ "vendor.b", JStr "2"; "vendor.a", JStr "1" ] ])
          "an extension member repeated", entry (plain @ [ "ext", JObj [ "k", JStr "1"; "k", JStr "2" ] ])
          "an extension that is not an object", entry (plain @ [ "ext", JArr [] ])
          "an extension member that is not a string", entry (plain @ [ "ext", JObj [ "vendor.a", JInt 1 ] ])
          "an empty extension object", entry (plain @ [ "ext", JObj [] ])
          "every member",
          entry (
              plain
              @ [ "unit", JStr "kg"
                  "label", JStr "Mass"
                  "description", JStr "d"
                  "ext", JObj [ "x.y", JStr "z" ] ]
          )
          "an unknown member, read past", entry (plain @ [ "colour", JStr "red" ])
          "members out of order", entry [ "unit", JStr "m"; "type", JStr "float"; "name", JStr "m" ] ]

    /// Drawn tables carrying metadata: two or three columns the shipped builders accept (so the
    /// stripped table is one `validate` accepts), a third with their columns out of schema order.
    let drawnTables (seed: int) (count: int) : Table list =
        let rng = System.Random(seed)
        let fields = drawnFields (seed + 3) (count * 3) |> Array.ofList

        /// The drawn field's metadata on a field of the column's own name, through the builders.
        let named (name: string) (f: Field) : Field =
            let g = Field.create name f.Type

            let g =
                (match f.Unit with
                 | Some u -> Field.withUnit u g
                 | None -> g)

            let g =
                (match f.Label with
                 | Some l -> Field.withLabel l g
                 | None -> g)

            let g =
                (match f.Description with
                 | Some d -> Field.withDescription d g
                 | None -> g)

            f.Ext |> Map.toList |> List.fold (fun h (k, v) -> Field.withExt k v h) g

        [ for i in 0 .. count - 1 ->
              let k = 2 + rng.Next 2

              let schema =
                  List.init k (fun j -> named (sprintf "c%d" j) fields[(i * 3 + j) % fields.Length])

              let rows = 1 + rng.Next 4

              let columns =
                  schema
                  |> List.map (fun f ->
                      let cells =
                          List.init rows (fun r ->
                              if rng.Next 4 = 0 then
                                  Null
                              else
                                  match f.Type with
                                  | IntType -> Int r
                                  | FloatType -> Float(float r * 0.5)
                                  | BoolType -> Bool(r % 2 = 0)
                                  | StringType -> Str(words[rng.Next words.Length])
                                  | DateType -> Date "2026-02-28"
                                  | TimestampType _ -> Timestamp "2026-06-22T17:00:00Z"
                                  | DecimalType -> Decimal "1.5")

                      match Column.ofCells f.Name f.Type cells with
                      | Result.Ok c -> c
                      | Result.Error e -> failtestf "the pool's column %s did not build: %A" f.Name e)

              let columns = if i % 3 = 0 then List.rev columns else columns
              { Schema = schema; Columns = columns } ]

    let generated (reverseExt: bool) (seed: int) (count: int) : Tally =
        tally
        |> Differential.over (drawnFields seed count) (probeField reverseExt "drawn field")
        |> Differential.over (if reverseExt then [] else handWritten) (fun (label, el) -> probeDoc label el)

    let generatedTables (seed: int) (count: int) : Tally =
        tally |> Differential.over (drawnTables seed count) (probeTable "drawn table")

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- the columnar codec (Phase 306) ----

          testCase
              "the column oracle agrees with Table.validate, encodeJson and decodeJson, and the round trip is the model's normal form"
          <| fun _ ->
              // Two pools. The first holds only tables `validate` accepts — and NOT only tables in
              // normal form: a third have their columns out of schema order. The second wanders
              // outside what `validate` accepts.
              // MEASURED at seed 3063, 2,000 tables: 192 outside the normal form (355 before Phase
              // 417, when a float or a decimal column could also hold an `Int` cell; `Column.ofCells`
              // now normalises a widened `Int` at construction, so a production table reaches the
              // model's normal form for that arm before it is encoded, and only the column order is
              // left). The threshold sits under it with headroom and well above zero.
              let valid = onBigStack (fun () -> ColumnDiff.generated false 3063 2000 0)
              let wild = onBigStack (fun () -> ColumnDiff.generated false 3064 3000 1)

              for (name, t) in [ "the valid pool", valid; "the wild pool", wild ] do
                  Differential.expectAgrees (name + ": the column oracle") t

              Expect.equal
                  (Tally.count "Valid" valid)
                  (Tally.count "Tables" valid)
                  "every table of the first pool is one validate accepts"

              Expect.isGreaterThan
                  (Tally.count "OutsideNormalForm" valid)
                  150
                  (sprintf
                      "valid tables OUTSIDE the normal form — where the round trip is not the literal one (%d)"
                      (Tally.count "OutsideNormalForm" valid))

              Expect.isGreaterThan
                  ((Tally.count "Valid" valid) - (Tally.count "OutsideNormalForm" valid))
                  200
                  "and valid tables already in it, where it is"

              for cls in
                  [ "Malformed"
                    "TypeMismatch"
                    "RaggedColumns"
                    "NonFiniteFloat"
                    "MalformedShape" ] do
                  Expect.isTrue
                      ((Tally.reached "Refusals" wild).Contains cls)
                      (sprintf "the wild pool reached a %s refusal (reached: %A)" cls (Tally.reached "Refusals" wild))

              Expect.isGreaterThan
                  (Tally.count "Valid" wild)
                  200
                  (sprintf "the wild pool still holds valid tables (%d)" (Tally.count "Valid" wild))

          testCase
              "… and over documents no encoder writes — the decode-only acceptances, and the arms the model declines"
          <| fun _ ->
              let t =
                  ColumnDiff.tally
                  |> Differential.over ColumnDiff.handWritten (fun (label, text) -> ColumnDiff.decodeProbe label text)

              Differential.expectAgrees "the column oracle" t

              // The five lenient-ingest arms are the model's `OutOfModel` and nothing else is: a
              // probe that skipped more would be comparing less than it says.
              Expect.equal (Tally.count "OutOfModel" t) 5 "exactly the five lenient-ingest documents are declined"
              Expect.equal (Tally.count "Decoded" t) (ColumnDiff.handWritten.Length - 5) "and every other is compared"

              for cls in
                  [ "Malformed"
                    "TypeMismatch"
                    "RaggedColumns"
                    "MalformedShape"
                    "MissingField"
                    "UnknownType"
                    "LengthMismatch" ] do
                  Expect.isTrue
                      ((Tally.reached "Refusals" t).Contains cls)
                      (sprintf "a %s refusal was reached (reached: %A)" cls (Tally.reached "Refusals" t))

          testCase "a comparison against the table ITSELF loses — on exactly the tables outside the normal form"
          <| fun _ ->
              // The go-red, and it is the law the suite's generative codec test asserts: the
              // LITERAL round trip. `literal_round_trip_fails_on_*` refute it on the model; here
              // it loses on production, twice per table (at the JVal, and through the string) and
              // on no table already in normal form.
              let t = onBigStack (fun () -> ColumnDiff.generated true 3063 2000 0)

              // The same pool as above: 192 tables outside the normal form, all by column order
              // since Phase 417 (see the measurement there).
              Expect.isGreaterThan
                  (Tally.count "OutsideNormalForm" t)
                  150
                  "the pool holds tables outside the normal form"

              Expect.equal
                  (List.length (Tally.disagreements t))
                  (2 * (Tally.count "OutsideNormalForm" t))
                  "the literal comparison lost on those tables, at both levels, and on no other"

          testCase
              "the two facts about Int32 the round trip assumes hold over the pool, and the decimal canonicaliser agrees"
          <| fun _ ->
              // `int_text_canonical` and `int_floats_finite` are HYPOTHESES of `round_trip`, each
              // proved necessary. They are facts about .NET's Int32; this samples them.
              for i in
                  [ 0
                    1
                    -1
                    9
                    10
                    -10
                    1000
                    -1000
                    2147483647
                    -2147483648
                    123456789
                    -987654321 ]
                  @ [ for k in 0..200 -> k * 7919 - 800000 ] do
                  Expect.isTrue
                      (WireColumn.is_canonical (canonToChs (string i)))
                      (sprintf "the text of %d is canonical decimal text" i)

                  Expect.isTrue (System.Double.IsFinite(float i)) (sprintf "the float of %d is finite" i)

              // `DecimalText.tryCanonical`, which the model carries clause for clause.
              let texts =
                  [ "0"
                    "-0"
                    "00"
                    "007"
                    "7"
                    "-7"
                    "1.5"
                    "1.50"
                    "01.50"
                    "-0.0"
                    "0.05"
                    ".5"
                    "5."
                    "+5"
                    "1e3"
                    ""
                    "-"
                    "--1"
                    "1.2.3"
                    " 1"
                    "1 "
                    "12345678901234567890.000100"
                    "-000.000"
                    "x" ]

              let t =
                  Tally.empty [ "Canonicalised"; "Refused" ]
                  |> Differential.over texts (fun s t ->
                      let model =
                          match WireColumn.try_canonical (canonToChs s) with
                          | FStar_Pervasives_Native.Some c -> Some(canonFromChs c)
                          | FStar_Pervasives_Native.None -> None

                      t
                      |> Differential.compare
                          Differential.structural<string option>
                          (sprintf "tryCanonical %A" s)
                          (DecimalText.tryCanonical s)
                          model
                      |> Tally.bump (if model.IsSome then "Canonicalised" else "Refused"))

              Differential.expectAgrees "the decimal canonicaliser" t
              Expect.isGreaterThan (Tally.count "Canonicalised" t) 8 "texts the canonicaliser read"
              Expect.isGreaterThan (Tally.count "Refused" t) 8 "and texts it refused"

          testCase
              "the field oracle agrees with fieldJson and decodeField over drawn fields and the hand-written documents"
          <| fun _ ->
              // 800 drawn fields (a unit from the drawn pool or none, a label, a description, up to
              // three extension members, each stated or not), each written by both and read back by
              // both; then the hand-written documents — every refusal `decodeField` has, the
              // non-canonical and aliased unit spellings it reads, and the member it reads past.
              let t = onBigStack (fun () -> FieldDiff.generated false 4280 800)

              Differential.expectAgrees "the field oracle" t

              Expect.isGreaterThan
                  (Tally.count "WithUnit" t)
                  300
                  (sprintf "fields stating a unit (%d)" (Tally.count "WithUnit" t))

              Expect.isGreaterThan
                  (Tally.count "WithExt" t)
                  400
                  (sprintf "fields with extension members (%d)" (Tally.count "WithExt" t))

              Expect.equal (Tally.count "Docs" t) (800 + FieldDiff.handWritten.Length) "every document was read by both"

              for cls in [ "MissingField"; "UnknownType"; "MalformedShape" ] do
                  Expect.isTrue
                      ((Tally.reached "Refusals" t).Contains cls)
                      (sprintf "the documents reached a %s refusal (reached: %A)" cls (Tally.reached "Refusals" t))

          testCase
              "the field codec oracle agrees with encodeJson and decodeJson over tables with metadata, and the round trip keeps every field"
          <| fun _ ->
              // 300 drawn tables whose stripped form `validate` accepts, a third with their columns
              // out of schema order: `validate`, `encodeJson` beside `encode_json_f`, `decodeJson`
              // beside `decode_json_f`, and the round trip against `normal_table_f` — the columns in
              // schema order, every field's metadata intact.
              let t = onBigStack (fun () -> FieldDiff.generatedTables 4281 300)

              Differential.expectAgrees "the field codec oracle" t

              Expect.equal (Tally.count "Valid" t) (Tally.count "Tables" t) "every drawn table is one validate accepts"

              Expect.isGreaterThan
                  (Tally.count "OutsideNormalForm" t)
                  60
                  (sprintf "tables outside the normal form, by column order (%d)" (Tally.count "OutsideNormalForm" t))

          testCase
              "a bridge that drops the extension order loses - on exactly the fields with two or more extension members"
          <| fun _ ->
              // The go-red: the model handed the extension members in REVERSED order. Production
              // holds a map and writes the members in key order, so the documents differ on exactly
              // the fields with two or more members, and on no other.
              let t = onBigStack (fun () -> FieldDiff.generated true 4280 800)

              Expect.isGreaterThan
                  (Tally.count "ExtMulti" t)
                  200
                  (sprintf "fields with two or more extension members (%d)" (Tally.count "ExtMulti" t))

              Expect.isGreaterThan
                  ((Tally.count "Fields" t) - (Tally.count "ExtMulti" t))
                  300
                  (sprintf "fields with fewer (%d)" ((Tally.count "Fields" t) - (Tally.count "ExtMulti" t)))

              Differential.expectLoses "a bridge that drops the extension order" t

              Expect.equal
                  (List.length (Tally.disagreements t))
                  (Tally.count "ExtMulti" t)
                  "the reversed bridge lost on those fields, and on no other" ]
