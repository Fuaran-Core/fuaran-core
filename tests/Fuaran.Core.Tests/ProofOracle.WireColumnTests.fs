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

// ---------------------------------------------------------------------------
// Phase 428 — the field codec beside `WireColumn.fst` section 10.
// ---------------------------------------------------------------------------

module private FieldDiff =

    let host = ColumnDiff.host

    let private toOpt (o: 'a option) : FStar_Pervasives_Native.option<'a> =
        match o with
        | Some x -> FStar_Pervasives_Native.Some x
        | None -> FStar_Pervasives_Native.None

    let private ofOpt (o: FStar_Pervasives_Native.option<'a>) : 'a option =
        match o with
        | FStar_Pervasives_Native.Some x -> Some x
        | FStar_Pervasives_Native.None -> None

    /// Production's field as the model's: the unit through `UnitDiff.toModel`, the extension map in
    /// the key order `Map.toList` answers. `reverseExt` is the go-red's dial.
    let toModelFieldWith (reverseExt: bool) (f: Field) : WireColumn.field =
        let ext = f.Ext |> Map.toList |> List.map (fun (k, v) -> canonToChs k, canonToChs v)

        { fname = canonToChs f.Name
          fty = ColumnDiff.toModelType f.Type
          funit = f.Unit |> Option.map UnitDiff.toModel |> toOpt
          flabel = f.Label |> Option.map canonToChs |> toOpt
          fdesc = f.Description |> Option.map canonToChs |> toOpt
          fext = (if reverseExt then List.rev ext else ext) }

    let toModelField (f: Field) : WireColumn.field = toModelFieldWith false f

    /// The model's field as production's — the unit through its canonical text, the extension
    /// members through `withExt` in the list's order.
    let ofModelField (f: WireColumn.field) : Field =
        let unit =
            match ofOpt f.funit with
            | Some m ->
                match Unit.parse (UnitDiff.modelRender m) with
                | Ok u -> Some u
                | Error e -> failtestf "production refused the model's unit text %s: %A" (UnitDiff.modelRender m) e
            | None -> None

        let f0 = Field.create (canonFromChs f.fname) (ColumnDiff.ofModelType f.fty)

        let f1 =
            (match unit with
             | Some u -> Field.withUnit u f0
             | None -> f0)

        let f2 =
            (match ofOpt f.flabel with
             | Some l -> Field.withLabel (canonFromChs l) f1
             | None -> f1)

        let f3 =
            match ofOpt f.fdesc with
            | Some d -> Field.withDescription (canonFromChs d) f2
            | None -> f2

        f.fext
        |> List.fold (fun g (k, v) -> Field.withExt (canonFromChs k) (canonFromChs v) g) f3

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
        | Result.Error e -> "refused " + ColumnDiff.prodClass e

    let private showModelField (r: WireColumn.res<WireColumn.field>) : string =
        match r with
        | WireColumn.Good f -> "ok " + showField (ofModelField f)
        | WireColumn.Bad e -> "refused " + ColumnDiff.modelClass e

    type Tally =
        { Fields: int
          WithUnit: int
          WithExt: int
          ExtMulti: int
          Docs: int
          Refusals: Set<string>
          Tables: int
          Valid: int
          OutsideNormalForm: int
          LiteralLost: int
          Diffs: string list }

    let empty =
        { Fields = 0
          WithUnit = 0
          WithExt = 0
          ExtMulti = 0
          Docs = 0
          Refusals = Set.empty
          Tables = 0
          Valid = 0
          OutsideNormalForm = 0
          LiteralLost = 0
          Diffs = [] }

    let private diff (label: string) (what: string) (prod: string) (model: string) (t: Tally) : Tally =
        { t with
            Diffs =
                sprintf "%s: %s\n  production: %s\n  the model:  %s" label what prod model
                :: t.Diffs }

    /// One document, asked of `decodeField` and of the model's `decode_field`.
    let probeDoc (label: string) (el: JVal) (t: Tally) : Tally =
        let t = { t with Docs = t.Docs + 1 }
        let prod = ColumnCodec.decodeField el
        let model = WireColumn.decode_field (canonToModel el)

        let t =
            match prod with
            | Result.Error e ->
                { t with
                    Refusals = t.Refusals.Add((ColumnDiff.prodClass e).Split(' ')[0]) }
            | Result.Ok _ -> t

        if showProdField prod = showModelField model then
            t
        else
            diff label "decodeField" (showProdField prod) (showModelField model) t

    /// One field: `fieldJson` beside `field_json` byte for byte, then the document back through
    /// both readers. `reverseExt` is the go-red: the model handed the extension members reversed.
    let probeField (reverseExt: bool) (label: string) (f: Field) (t: Tally) : Tally =
        let t = { t with Fields = t.Fields + 1 }

        let t =
            if f.Unit.IsSome then
                { t with WithUnit = t.WithUnit + 1 }
            else
                t

        let t =
            if f.Ext.IsEmpty then
                t
            else
                { t with WithExt = t.WithExt + 1 }

        let t =
            if f.Ext.Count >= 2 then
                { t with ExtMulti = t.ExtMulti + 1 }
            else
                t

        let prodJson = ColumnCodec.fieldJson f
        let modelJson = canonOfModel (WireColumn.field_json (toModelFieldWith reverseExt f))

        if reverseExt then
            if Json.render prodJson = Json.render modelJson then
                t
            else
                { t with
                    LiteralLost = t.LiteralLost + 1 }
        else
            let t =
                if Json.render prodJson = Json.render modelJson then
                    t
                else
                    diff label "fieldJson" (Json.render prodJson) (Json.render modelJson) t

            probeDoc label prodJson t

    // ---- tables with metadata ----

    let toModelTable (t: Table) : WireColumn.table_f<int, float> =
        let m = ColumnDiff.toModelTable t

        { fschema = t.Schema |> List.map toModelField
          fcolumns = m.columns }

    let ofModelTable (m: WireColumn.table_f<int, float>) : Table =
        let stripped = ColumnDiff.ofModelTable (WireColumn.strip m)

        { stripped with
            Schema = m.fschema |> List.map ofModelField }

    let showTable (t: Table) : string =
        (t.Schema |> List.map showField |> String.concat ", ")
        + " | "
        + ColumnDiff.showSource (Embedded t)

    let private showProdSource (r: Result<DataSource, ColumnError>) : string =
        match r with
        | Result.Ok(Embedded t) -> "ok " + showTable t
        | Result.Ok(Ref r) -> "ok ref " + r
        | Result.Error e -> "refused " + ColumnDiff.prodClass e

    let private showModelSource (r: WireColumn.res<WireColumn.data_source_f<int, float>>) : string =
        match r with
        | WireColumn.Good(WireColumn.Embedded_f t) -> "ok " + showTable (ofModelTable t)
        | WireColumn.Good(WireColumn.Ref_f r) -> "ok ref " + canonFromChs r
        | WireColumn.Bad e -> "refused " + ColumnDiff.modelClass e

    /// One table: `validate`; `encodeJson` beside `encode_json_f`; `decodeJson` of the encoding beside
    /// `decode_json_f`; and, for a table `validate` accepts, the round trip against the model's
    /// `normal_table_f` — the columns in schema order, every field intact.
    let probeTable (label: string) (table: Table) (t: Tally) : Tally =
        let m = toModelTable table
        let t = { t with Tables = t.Tables + 1 }
        let prodValid = Table.validate table
        let modelValid = WireColumn.validate_f host m

        let prodV =
            match prodValid with
            | Result.Ok() -> "ok"
            | Result.Error e -> "refused " + ColumnDiff.prodClass e

        let modelV =
            match modelValid with
            | WireColumn.Good _ -> "ok"
            | WireColumn.Bad e -> "refused " + ColumnDiff.modelClass e

        let t =
            if prodV = modelV then
                t
            else
                diff label "validate" prodV modelV t

        let t =
            (match prodValid with
             | Result.Ok() -> { t with Valid = t.Valid + 1 }
             | _ -> t)

        let prodJson = ColumnCodec.encodeJson (Embedded table)
        let modelJson = WireColumn.encode_json_f host (WireColumn.Embedded_f m)

        let t =
            if Json.render prodJson = Json.render (canonOfModel modelJson) then
                t
            else
                diff label "encodeJson" (Json.render prodJson) (Json.render (canonOfModel modelJson)) t

        let prodBack = ColumnCodec.decodeJson prodJson
        let modelBack = WireColumn.decode_json_f host modelJson

        let t =
            if showProdSource prodBack = showModelSource modelBack then
                t
            else
                diff label "decodeJson (encodeJson t)" (showProdSource prodBack) (showModelSource modelBack) t

        match prodValid with
        | Result.Error _ -> t
        | Result.Ok() ->
            let normal = ofModelTable (WireColumn.normal_table_f host m)

            let t =
                if showTable normal = showTable table then
                    t
                else
                    { t with
                        OutsideNormalForm = t.OutsideNormalForm + 1 }

            let expected = "ok " + showTable normal

            if showProdSource prodBack = expected then
                t
            else
                diff label "the round trip" (showProdSource prodBack) expected t

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
                      ColumnDiff.modelTypes[rng.Next ColumnDiff.modelTypes.Length]

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
        let t =
            drawnFields seed count
            |> List.fold (fun t f -> probeField reverseExt "drawn field" f t) empty

        if reverseExt then
            t
        else
            handWritten |> List.fold (fun t (label, el) -> probeDoc label el t) t

    let generatedTables (seed: int) (count: int) : Tally =
        drawnTables seed count
        |> List.fold (fun t table -> probeTable "drawn table" table t) empty

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
                  Expect.isEmpty
                      t.Diffs
                      (sprintf "%s: the column oracle disagreed with production:\n%s" name (renderCanonDiffs t.Diffs))

              Expect.equal valid.Valid valid.Tables "every table of the first pool is one validate accepts"

              Expect.isGreaterThan
                  valid.OutsideNormalForm
                  150
                  (sprintf
                      "valid tables OUTSIDE the normal form — where the round trip is not the literal one (%d)"
                      valid.OutsideNormalForm)

              Expect.isGreaterThan
                  (valid.Valid - valid.OutsideNormalForm)
                  200
                  "and valid tables already in it, where it is"

              for cls in
                  [ "Malformed"
                    "TypeMismatch"
                    "RaggedColumns"
                    "NonFiniteFloat"
                    "MalformedShape" ] do
                  Expect.isTrue
                      (wild.Refusals.Contains cls)
                      (sprintf "the wild pool reached a %s refusal (reached: %A)" cls wild.Refusals)

              Expect.isGreaterThan wild.Valid 200 (sprintf "the wild pool still holds valid tables (%d)" wild.Valid)

          testCase
              "… and over documents no encoder writes — the decode-only acceptances, and the arms the model declines"
          <| fun _ ->
              let t =
                  ColumnDiff.handWritten
                  |> List.fold (fun acc (label, text) -> ColumnDiff.decodeProbe label text acc) ColumnDiff.empty

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the column oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              // The five lenient-ingest arms are the model's `OutOfModel` and nothing else is: a
              // probe that skipped more would be comparing less than it says.
              Expect.equal t.OutOfModel 5 "exactly the five lenient-ingest documents are declined"
              Expect.equal t.Decoded (ColumnDiff.handWritten.Length - 5) "and every other is compared"

              for cls in
                  [ "Malformed"
                    "TypeMismatch"
                    "RaggedColumns"
                    "MalformedShape"
                    "MissingField"
                    "UnknownType"
                    "LengthMismatch" ] do
                  Expect.isTrue
                      (t.Refusals.Contains cls)
                      (sprintf "a %s refusal was reached (reached: %A)" cls t.Refusals)

          testCase "a comparison against the table ITSELF loses — on exactly the tables outside the normal form"
          <| fun _ ->
              // The go-red, and it is the law the suite's generative codec test asserts: the
              // LITERAL round trip. `literal_round_trip_fails_on_*` refute it on the model; here
              // it loses on production, twice per table (at the JVal, and through the string) and
              // on no table already in normal form.
              let t = onBigStack (fun () -> ColumnDiff.generated true 3063 2000 0)

              // The same pool as above: 192 tables outside the normal form, all by column order
              // since Phase 417 (see the measurement there).
              Expect.isGreaterThan t.OutsideNormalForm 150 "the pool holds tables outside the normal form"

              Expect.equal
                  (List.length t.Diffs)
                  (2 * t.OutsideNormalForm)
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

              let mutable canonicalised = 0
              let mutable refused = 0

              for s in texts do
                  let model =
                      match WireColumn.try_canonical (canonToChs s) with
                      | FStar_Pervasives_Native.Some c -> Some(canonFromChs c)
                      | FStar_Pervasives_Native.None -> None

                  Expect.equal model (DecimalText.tryCanonical s) (sprintf "tryCanonical %A" s)

                  if model.IsSome then
                      canonicalised <- canonicalised + 1
                  else
                      refused <- refused + 1

              Expect.isGreaterThan canonicalised 8 "texts the canonicaliser read"
              Expect.isGreaterThan refused 8 "and texts it refused"

          testCase
              "the field oracle agrees with fieldJson and decodeField over drawn fields and the hand-written documents"
          <| fun _ ->
              // 800 drawn fields (a unit from the drawn pool or none, a label, a description, up to
              // three extension members, each stated or not), each written by both and read back by
              // both; then the hand-written documents — every refusal `decodeField` has, the
              // non-canonical and aliased unit spellings it reads, and the member it reads past.
              let t = onBigStack (fun () -> FieldDiff.generated false 4280 800)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the field oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.isGreaterThan t.WithUnit 300 (sprintf "fields stating a unit (%d)" t.WithUnit)
              Expect.isGreaterThan t.WithExt 400 (sprintf "fields with extension members (%d)" t.WithExt)
              Expect.equal t.Docs (800 + FieldDiff.handWritten.Length) "every document was read by both"

              for cls in [ "MissingField"; "UnknownType"; "MalformedShape" ] do
                  Expect.isTrue
                      (t.Refusals.Contains cls)
                      (sprintf "the documents reached a %s refusal (reached: %A)" cls t.Refusals)

          testCase
              "the field codec oracle agrees with encodeJson and decodeJson over tables with metadata, and the round trip keeps every field"
          <| fun _ ->
              // 300 drawn tables whose stripped form `validate` accepts, a third with their columns
              // out of schema order: `validate`, `encodeJson` beside `encode_json_f`, `decodeJson`
              // beside `decode_json_f`, and the round trip against `normal_table_f` — the columns in
              // schema order, every field's metadata intact.
              let t = onBigStack (fun () -> FieldDiff.generatedTables 4281 300)

              Expect.isEmpty
                  t.Diffs
                  (sprintf "the field codec oracle disagreed with production:\n%s" (renderCanonDiffs t.Diffs))

              Expect.equal t.Valid t.Tables "every drawn table is one validate accepts"

              Expect.isGreaterThan
                  t.OutsideNormalForm
                  60
                  (sprintf "tables outside the normal form, by column order (%d)" t.OutsideNormalForm)

          testCase
              "a bridge that drops the extension order loses - on exactly the fields with two or more extension members"
          <| fun _ ->
              // The go-red: the model handed the extension members in REVERSED order. Production
              // holds a map and writes the members in key order, so the documents differ on exactly
              // the fields with two or more members, and on no other.
              let t = onBigStack (fun () -> FieldDiff.generated true 4280 800)

              Expect.isGreaterThan t.ExtMulti 200 (sprintf "fields with two or more extension members (%d)" t.ExtMulti)

              Expect.isGreaterThan
                  (t.Fields - t.ExtMulti)
                  300
                  (sprintf "fields with fewer (%d)" (t.Fields - t.ExtMulti))

              Expect.equal t.LiteralLost t.ExtMulti "the reversed bridge lost on those fields, and on no other" ]
