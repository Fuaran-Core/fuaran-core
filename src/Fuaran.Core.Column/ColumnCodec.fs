namespace Fuaran.Core

/// A data source: embedded columns, or a host-resolved named `Ref` (spec §1 — the
/// `Binding.Query` by-reference precedent). The evaluator resolves a `Ref` through a caller
/// supplied resolver; the wire carries the name, never the rows — and no schema: the encoder
/// writes the empty `"schema":[]` a reader of the embedded form expects, and the decoder reads a
/// `ref` source with or without one and keeps none (Phase 299 dropped the rule that a `ref` had to
/// carry a schema the decoder then discarded).
type DataSource =
    /// The rows travel inline. `ColumnCodec.tryEncode` validates the table first; `encode` assumes
    /// it is valid.
    | Embedded of Table
    /// A name the host resolves to rows; the codec carries it uninterpreted and always encodes it.
    | Ref of string

/// The canonical wire codec for the columnar strand. Column-oriented (a `values` array + a
/// `validity` mask per column, the Arrow layout), reusing the `Fuaran.Core.Wire` canonical rules
/// so numeric columns are byte-identical across hosts. Decode is `Result`-typed with the codec
/// envelope (`ColumnError`), over the `Wire.Decode` combinators. Fable-clean (only `Json` / `Canon` /
/// `Decode`).
module ColumnCodec =

    // ---- encode (Fable-clean `JVal` construction → `Canonical.write Rendering.CrossHost`) ----

    /// One column's `values` and `validity` arrays, walked off the typed storage (Phase 417): the
    /// element at a present row as its JSON value, the absent slot at an absent one — never what
    /// the vector happens to hold there — so the bytes are those the `Cell list` column wrote. A
    /// widened cell was normalised at construction, so a float column writes floats and a decimal
    /// column its texts, as the cell encoder wrote them before. An `AllValid` column (Phase 420)
    /// holds no mask and writes its all-true validity array directly, without materialising one.
    /// What a value and an absent slot are written as is the type's row (`TypeOps`, Phase 514).
    let private columnJson (c: Column) : JVal =
        let n = Column.length c
        let rows = Column.validity c
        let present (i: int) = Validity.isPresent i rows

        let values =
            TypeOps.ofData
                c.Data
                { new ColumnDataUser<JVal list> with
                    member _.Use(ops, get) =
                        List.init n (fun i -> if present i then ops.WriteJson(get i) else ops.AbsentSlot) }

        let validity = List.init n (fun i -> JBool(present i))
        JObj [ "values", JArr values; "validity", JArr validity ]

    /// One schema entry as the wire carries it (Phase 427): `name` and `type` always, then the
    /// metadata members a field states and no other — `unit` (the unit's canonical text), `label`,
    /// `description`, and `ext` (an object of the extension members, each a string) — so a field
    /// with no metadata writes exactly the `{name, type}` entry it wrote before this phase, and
    /// every existing column's bytes stand. Shared with the schema-delta codec, whose `added` and
    /// `removed` entries are these.
    let fieldJson (f: Field) : JVal =
        let stated (name: string) (v: string option) =
            match v with
            | Some s -> [ name, JStr s ]
            | None -> []

        JObj(
            [ "name", JStr f.Name; "type", JStr(ColumnType.tag f.Type) ]
            @ stated "unit" (f.Unit |> Option.map Unit.render)
            @ stated "label" f.Label
            @ stated "description" f.Description
            @ (if f.Ext.IsEmpty then
                   []
               else
                   [ "ext", JObj(f.Ext |> Map.toList |> List.map (fun (k, v) -> k, JStr v)) ])
        )

    let private schemaJson (schema: Schema) : JVal = schema |> List.map fieldJson |> JArr

    /// Encode a `DataSource` to a `JVal` — embedded columns keyed by name (type comes from the
    /// schema, so it is not repeated), or a `ref` string beside an empty `schema`. The members are
    /// built in author order; `encode` renders them under `Canon`, which sorts keys, so the BYTES
    /// are canonical whatever the order here.
    let encodeJson (src: DataSource) : JVal =
        match src with
        | Embedded t ->
            let columns =
                t.Schema
                |> List.map (fun f ->
                    let col =
                        t.Columns
                        |> List.tryFind (fun c -> c.Name = f.Name)
                        |> Option.defaultValue (Column.ofStrs f.Name Vector.empty AllValid)

                    f.Name, columnJson col)

            JObj [ "schema", schemaJson t.Schema; "columns", JObj columns ]
        | Ref r -> JObj [ "schema", JArr []; "ref", JStr r ]

    /// The canonical wire string for a `DataSource` — rendered under the shared `$type` discipline
    /// (`Canon`): Ordinal-sorted keys + the cross-host float layout, so a columnar payload is
    /// byte-identical across the .NET, Fable, TS and Python hosts. **Assumes a source
    /// `Table.validate` accepts** — use `tryEncode` for the guarded, total entry point on
    /// untrusted/derived data.
    let encode (src: DataSource) : string =
        Canonical.write Rendering.CrossHost (encodeJson src)

    /// Total, guarded encode (Phases 38 + 43 + 299): `Table.validate`, then `encode`. So over what
    /// `validate` accepts it is EXACTLY `Ok (encode src)` — a law in the suite pins it — and
    /// everything it refuses is refused by `validate`, with `validate`'s error: a structurally
    /// malformed table, a cell outside its column's type, a non-finite float, and decimal, date or
    /// timestamp text that is not canonical. A `ref` source carries no table and always encodes.
    let tryEncode (src: DataSource) : Result<string, ColumnError> =
        match src with
        | Ref _ -> Ok(encode src)
        | Embedded t -> Table.validate t |> Result.map (fun () -> encode src)

    // ---- decode (`Json.read` → the codec envelope `ColumnError`) ----

    /// THE CODEC'S PROJECTION OF A DECODE REFUSAL (Phase 519; `SchemaDeltaCodec` reads through it
    /// too): the codec reads with the `Decoder` combinators and spells their refusal in its own
    /// envelope. An absent member is `MissingField`, naming it (the last step of the refusal's
    /// path); any other refusal is `MalformedShape`, its sentence prefixed with where it was met when
    /// there is a where. A wrong JSON kind reads `expected <kind>, got <kind>`, as it did when the
    /// codec read through the string-error twin's `Fault` (deleted with it): no refusal moved.
    let internal ofDecodeError (ctx: string) (e: DecodeError) : ColumnError =
        match e.Code, List.tryLast e.Path with
        | DecodeCode.MissingField, Some(PathSegment.Key name) -> MissingField name
        | _ -> MalformedShape((if ctx = "" then "" else ctx + ": ") + e.Message)

    /// The codec's projection of a read's refusal (Phase 519): malformed text is `NotJson`, carrying
    /// the parser's classified error. The depth cap — the one breach `ReadOptions.strict` admits —
    /// keeps the spelling it had before every reader reported it as a breach: `NotJson` with kind
    /// `MaxDepthExceeded` at the container's position, in the scanner's sentence, so no column
    /// refusal moved. Another bound's breach, which a strict read never meets, is `MalformedShape`
    /// naming the limit.
    let internal ofJsonRefusal (r: JsonRefusal) : ColumnError =
        match r with
        | JsonRefusal.Malformed e -> NotJson e
        | JsonRefusal.LimitExceeded b when b.Limit = JsonLimit.Depth ->
            NotJson
                { Kind = MaxDepthExceeded
                  Position = b.Position
                  Message = "max nesting depth " + string b.Declared + " exceeded" }
        | JsonRefusal.LimitExceeded _ -> MalformedShape((DecodeError.ofJsonRefusal r).Message)

    let private getField (name: string) (el: JVal) : Result<JVal, ColumnError> =
        Decoder.field name Decoder.json el |> Result.mapError (ofDecodeError "")

    let private asArr (ctx: string) (el: JVal) : Result<JVal list, ColumnError> =
        Decoder.items el |> Result.mapError (ofDecodeError ctx)

    let private asStr (ctx: string) (el: JVal) : Result<string, ColumnError> =
        Decoder.str el |> Result.mapError (ofDecodeError ctx)

    /// The sentence a unit refusal is reported in, naming the token (or the shape fault) and its
    /// zero-based position in the text, as `Unit.parse` classifies it.
    let private unitRefusalText (r: UnitRefusal) : string =
        let at (what: string) (token: string) (position: int) =
            what + " '" + token + "' at position " + string position

        match r with
        | UnitRefusal.Empty -> "the text is empty"
        | UnitRefusal.Malformed(found, position, expected) ->
            "expected "
            + expected
            + " at position "
            + string position
            + ", found '"
            + found
            + "'"
        | UnitRefusal.UnknownAtom(token, position) -> at "unknown unit symbol" token position
        | UnitRefusal.Annotation(token, position) -> at "annotation" token position
        | UnitRefusal.ArbitraryUnit(token, position) -> at "arbitrary unit" token position
        | UnitRefusal.NonRatioUnit(token, position) -> at "non-ratio unit" token position
        | UnitRefusal.PrefixNotAllowed(token, position) -> at "prefix not allowed on" token position
        | UnitRefusal.NumericFactor(token, position) -> at "numeric factor" token position

    /// An optional string member of a schema entry: absent is `None`; present and not a string is
    /// refused naming the column and the member.
    let private optionalText (column: string) (name: string) (el: JVal) : Result<string option, ColumnError> =
        match Decoder.tryMember name el with
        | None -> Ok None
        | Some(JStr s) -> Ok(Some s)
        | Some other ->
            Error(
                MalformedShape(
                    column
                    + ": schema entry member '"
                    + name
                    + "' must be a string, got "
                    + JVal.kindName other
                )
            )

    /// A schema entry from the wire (Phase 427): `name` and `type` as before, then the metadata
    /// members when present — `unit` is parsed by `Unit.parse` and REFUSED, naming the column, when
    /// it is not a unit the kit parses (a parseable spelling is read to the unit and re-encodes as
    /// its canonical text); `label` and `description` must be strings; `ext` must be an object whose
    /// every member is a string. A member this codec does not know is read past, as a member of
    /// the source object is. The reader the schema-delta codec's entries share.
    let decodeField (el: JVal) : Result<Field, ColumnError> =
        getField "name" el
        |> Result.bind (asStr "schema.name")
        |> Result.bind (fun name ->
            getField "type" el
            |> Result.bind (asStr "schema.type")
            |> Result.bind (fun tag ->
                match ColumnType.ofTag tag with
                | Some ty -> Ok(Field.create name ty)
                | None -> Error(UnknownType(tag, ColumnType.all)))
            |> Result.bind (fun f ->
                optionalText name "unit" el
                |> Result.bind (fun unit ->
                    match unit with
                    | None -> Ok f
                    | Some text ->
                        match Unit.parse text with
                        | Ok u -> Ok(Field.withUnit u f)
                        | Error r ->
                            Error(
                                MalformedShape(
                                    name
                                    + ": unit '"
                                    + text
                                    + "' is not a unit the kit parses: "
                                    + unitRefusalText r
                                )
                            )))
            |> Result.bind (fun f ->
                optionalText name "label" el
                |> Result.map (fun label ->
                    match label with
                    | Some l -> Field.withLabel l f
                    | None -> f))
            |> Result.bind (fun f ->
                optionalText name "description" el
                |> Result.map (fun description ->
                    match description with
                    | Some d -> Field.withDescription d f
                    | None -> f))
            |> Result.bind (fun f ->
                match Decoder.tryMember "ext" el with
                | None -> Ok f
                | Some(JObj members) ->
                    let rec go (f: Field) =
                        function
                        | [] -> Ok f
                        | (k, JStr v) :: rest -> go (Field.withExt k v f) rest
                        | (k, other) :: _ ->
                            Error(
                                MalformedShape(
                                    name
                                    + ": schema entry extension member '"
                                    + k
                                    + "' must be a string, got "
                                    + JVal.kindName other
                                )
                            )

                    go f members
                | Some other ->
                    Error(
                        MalformedShape(
                            name
                            + ": schema entry member 'ext' must be an object, got "
                            + JVal.kindName other
                        )
                    )))

    let private decodeSchema (el: JVal) : Result<Schema, ColumnError> =
        asArr "schema" el
        |> Result.bind (fun xs ->
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | x :: rest ->
                    match decodeField x with
                    | Ok e -> go (e :: acc) rest
                    | Error e -> Error e

            go [] xs)

    /// Phase 88 (lenient-ingest) — a column that rides as a BARE JSON array is
    /// the "just the data" shorthand: `values` is the array itself with an
    /// all-present validity mask. Unambiguous — the Fuaran wire has no JSON
    /// null (tree rule 4), so a bare array can only mean every cell present;
    /// absent cells require the wrapped `{values, validity}` form, which
    /// stays canonical (the encoder always emits it).
    let private columnParts (name: string) (colEl: JVal) : Result<JVal list * JVal list, ColumnError> =
        match colEl with
        | JArr xs -> Ok(xs, xs |> List.map (fun _ -> JBool true))
        | _ ->
            getField "values" colEl
            |> Result.bind (asArr (name + ".values"))
            |> Result.bind (fun values ->
                // Phase 94 (lenient-ingest, pilot-5 census) — a wrapped column object
                // carrying `values` but NO `validity` mask is the same all-present
                // statement as the Phase-88 bare array (the wire has no JSON null, so
                // omission cannot mean absent cells): models reproduce the canonical
                // object shape minus the mask. Synthesize all-present; absent cells
                // still require the full wrapped form, which stays canonical.
                match Decoder.tryMember "validity" colEl with
                | None -> Ok(values, values |> List.map (fun _ -> JBool true))
                | Some validityEl ->
                    asArr (name + ".validity") validityEl
                    |> Result.map (fun validity -> values, validity))

    /// Decode a single named column against its declared type from the `columns` object, straight
    /// into its typed storage (Phase 417): the `values` and `validity` arrays are walked once in
    /// lockstep — `TypeOps.fill` over the type's row, the walk `Column.ofCells` runs too (Phase 514)
    /// — a present value read by the row's reader into the vector and an absent one left as the
    /// type's zero with its mask bit clear; a column with no absent row is `AllValid` and keeps no
    /// mask (Phase 420). The refusals and their order are the `Cell list` decoder's: the two lengths
    /// first (`LengthMismatch`), then row by row a validity entry that is not a bool
    /// (`MalformedShape`) before the value beside it.
    let private decodeColumn (columnsObj: JVal) (name: string) (ty: ColumnType) : Result<Column, ColumnError> =
        match Decoder.tryMember name columnsObj with
        | None -> Error(MissingField("columns." + name))
        | Some colEl ->
            columnParts name colEl
            |> Result.bind (fun (values, validity) ->
                if List.length values <> List.length validity then
                    Error(LengthMismatch(name, List.length values, List.length validity))
                else
                    let present = List.toArray validity

                    TypeOps.ofType
                        ty
                        { new TypeOpsUser<Result<ColumnData, ColumnError>> with
                            member _.Use ops =
                                TypeOps.fill ops values (fun i v ->
                                    match present[i] with
                                    | JBool true -> ops.ReadJson name v |> Result.map ValueSome
                                    | JBool false -> Ok ValueNone
                                    | p ->
                                        Error(
                                            MalformedShape(name + ".validity: expected bool, got " + JVal.kindName p)
                                        )) }
                    |> Result.map (fun data -> { Name = name; Data = data }))

    /// Phase 88 (lenient-ingest) — infer one column's `ColumnType` from its
    /// present cells. PINNED deterministic rules: all-int numerics ⇒ int, any
    /// fractional ⇒ float, all-bool ⇒ bool, all-string ⇒ string — **never**
    /// date/timestamp (temporal types require a declared schema; a date-looking
    /// string stays a string), and never decimal, on the same ground: a column of
    /// digit strings is a string column until a schema says otherwise. An empty column, or mixed kinds, is a
    /// DIDACTIC reject naming the explicit-schema remedy. (The Fuaran wire
    /// has no JSON null, so inference sees every value slot; masked-absent
    /// cells only ride the wrapped form.)
    let private inferColumnType (name: string) (values: JVal list) : Result<ColumnType, ColumnError> =
        let present = values

        let kindTag (v: JVal) =
            match v with
            | JInt _ -> "int"
            | JFloat _ -> "float"
            | JBool _ -> "bool"
            | JStr _ -> "string"
            | _ -> "other"

        match present with
        | [] ->
            Error(
                MalformedShape(
                    name
                    + ": cannot infer a column type from an empty / all-null column — declare it in an explicit \"schema\" array"
                )
            )
        | _ ->
            let tags = present |> List.map kindTag |> List.distinct

            match tags with
            | [ "int" ] -> Ok IntType
            | [ "float" ]
            | [ "int"; "float" ]
            | [ "float"; "int" ] -> Ok FloatType
            | [ "bool" ] -> Ok BoolType
            | [ "string" ] -> Ok StringType
            | mixed ->
                Error(
                    MalformedShape(
                        name
                        + ": cannot infer a single column type from mixed cell kinds ("
                        + String.concat ", " mixed
                        + ") — declare it in an explicit \"schema\" array"
                    )
                )

    /// The `columns` object, refused where it names one column twice (Phase 299). A parsed object
    /// keeps a repeated key, and the readers disagreed about which occurrence wins; the encoder
    /// never writes one (`Table.validate` refuses the duplicate name first), so a document that
    /// carries one was not written by it and has two readings.
    let private uniqueColumnKeys (columnsObj: JVal) : Result<JVal, ColumnError> =
        match columnsObj with
        | JObj fields ->
            match Table.firstDuplicate (fields |> List.map fst) with
            | Some key -> Error(Malformed("duplicate column key in \"columns\": " + key))
            | None -> Ok columnsObj
        | _ -> Ok columnsObj

    /// Decode a `DataSource` from a `JVal` root — the codec envelope on every failure.
    /// Phase 88: `schema` may be OMITTED on an EMBEDDED source (inferred per
    /// `inferColumnType`, columns in Ordinal key order). A `ref` source carries no rows, so it
    /// needs no schema and keeps none (Phase 299 dropped the rule that it carry one the decoder
    /// then discarded); a schema it does carry must still be a well-formed schema array. The
    /// canonical encoder always emits the explicit schema, so the shorthand normalises on
    /// re-encode. An embedded source ENDS in `Table.validate` (Phase 299), so what decodes is a
    /// table `tryEncode` accepts: a ragged table is a `RaggedColumns` here, not an `Ok` that encode
    /// then refuses with another cause.
    ///
    /// SURPLUS MEMBERS ARE MUST-IGNORE (stated by Phase 306; it was always so). A member of the
    /// source object other than `schema`, `columns` and `ref`, and — under an explicit schema — a
    /// member of `columns` the schema does not name, is read past, not refused: the wire's
    /// forward-compatibility rule (a reader ignores what it does not know), and what lets a host
    /// wrap the source in its own discriminator. The table that results is the schema's, checked by
    /// `validate`; a surplus column is not in it and is not re-encoded. (With NO schema every member
    /// of `columns` is a column, so nothing there is surplus.) A key REPEATED in `columns` is a
    /// different thing — two readings of one name — and is refused above. Two consequences of the
    /// same rule, stated because a reader will otherwise find them: a source carrying BOTH `ref`
    /// and `columns` is a `Ref` (the `ref` member is looked for first, and the columns are then
    /// surplus); and under an explicitly EMPTY schema no column is read, so `columns` must be
    /// present and is otherwise not looked at.
    ///
    /// WHAT COMES BACK is the table in NORMAL FORM, not always the table that was encoded: its
    /// columns in schema order, and an `Int` cell in a float or decimal column as the `Float` or
    /// `Decimal` it widens to. `decode (encode t)` is that normal form for every table `validate`
    /// accepts, and is `t` itself for every table this function returned (`proofs/WireColumn.fst`).
    let decodeJson (el: JVal) : Result<DataSource, ColumnError> =
        let schemaR =
            match Decoder.tryMember "schema" el with
            | Some schemaEl -> decodeSchema schemaEl |> Result.map Some
            | None -> Ok None

        schemaR
        |> Result.bind (fun schemaOpt ->
            match Decoder.tryMember "ref" el with
            | Some refEl -> asStr "ref" refEl |> Result.map Ref
            | None ->
                getField "columns" el
                |> Result.bind uniqueColumnKeys
                |> Result.bind (fun columnsObj ->
                    let schemaResolved =
                        match schemaOpt with
                        | Some schema -> Ok schema
                        | None ->
                            // Infer from the columns object, Ordinal key order.
                            match columnsObj with
                            | JObj colFields ->
                                colFields
                                |> List.map fst
                                |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
                                |> List.fold
                                    (fun acc name ->
                                        acc
                                        |> Result.bind (fun entries ->
                                            match Decoder.tryMember name columnsObj with
                                            | None -> Error(MissingField("columns." + name))
                                            | Some colEl ->
                                                columnParts name colEl
                                                |> Result.bind (fun (values, _) ->
                                                    inferColumnType name values
                                                    |> Result.map (fun ty -> entries @ [ Field.create name ty ]))))
                                    (Ok [])
                            | _ -> Error(MalformedShape "columns: expected object")

                    schemaResolved
                    |> Result.bind (fun schema ->
                        let rec go acc =
                            function
                            | [] -> Ok(List.rev acc)
                            | (f: Field) :: rest ->
                                match decodeColumn columnsObj f.Name f.Type with
                                | Ok c -> go (c :: acc) rest
                                | Error e -> Error e

                        go [] schema
                        |> Result.map (fun columns -> { Schema = schema; Columns = columns })
                        |> Result.bind (fun table -> Table.validate table |> Result.map (fun () -> Embedded table)))))

    /// Decode a wire string into a `DataSource`, surfacing the codec envelope `ColumnError` (a
    /// JSON-syntax failure becomes `NotJson`, carrying the parser's structured error).
    let decode (s: string) : Result<DataSource, ColumnError> =
        Json.read ReadOptions.strict s
        |> Result.mapError ofJsonRefusal
        |> Result.bind decodeJson

    // ---- one cell (Phase 514) ----
    // A value a document carries OUTSIDE a table — a predicate's literal, a model's argument to a
    // query — is still a value of a column type, and it is written and read exactly as the codec
    // writes and reads it inside a column, through the same row (`TypeOps`). Until this phase each
    // such reader built a one-row table document and ran the whole codec over it.

    /// THE ONE-CELL WRITER: the JSON value the codec writes for `cell` in the `values` array of a
    /// column `column` of type `ty` — the cell widened as `Column.ofCells` widens it (an `Int` in a
    /// float column is written as a float), and the absent slot for `Null` — or the refusal
    /// `Column.ofCells` names for a cell the type cannot hold. Like `encode`, it writes what it is
    /// handed: a non-finite float or a non-canonical decimal is `tryEncode`'s to refuse, through
    /// `Table.validate`.
    let writeCell (column: string) (ty: ColumnType) (cell: Cell) : Result<JVal, ColumnError> =
        TypeOps.ofType
            ty
            { new TypeOpsUser<Result<JVal, ColumnError>> with
                member _.Use ops =
                    ops.OfCell column cell
                    |> Result.map (fun v ->
                        match v with
                        | ValueSome x -> ops.WriteJson x
                        | ValueNone -> ops.AbsentSlot) }

    /// THE ONE-CELL READER: one PRESENT value of a column `column` of type `ty`, read as decode reads
    /// it at a present row and answered as the cell `Column.cell` reads back — so a decimal is read
    /// from decimal text and canonicalised, a date or timestamp only in its canonical form, and an
    /// integer token in a float column as a `Float` — or the refusal decode names for it, including
    /// the one `Table.validate` ends decode with (a non-finite float). A JSON value read here is the
    /// value the same JSON would decode to in a column.
    let readCell (column: string) (ty: ColumnType) (value: JVal) : Result<Cell, ColumnError> =
        TypeOps.ofType
            ty
            { new TypeOpsUser<Result<Cell, ColumnError>> with
                member _.Use ops =
                    ops.ReadJson column value
                    |> Result.bind (fun v ->
                        match ops.Uncarriable with
                        | Some(carriable, refusal) when not (carriable v) -> Error(refusal column 0 v)
                        | _ -> Ok(ops.ToCell v)) }

    /// Render a `ColumnError` as a stable human string — the adapter for `Corpus.Codec`'s
    /// `string`-error decode slot and for diagnostics.
    let errorString (e: ColumnError) : string =
        match e with
        | NotJson e -> "not valid JSON: " + e.Message + " at position " + string e.Position
        | MissingField f -> "missing field: " + f
        | MalformedShape d -> "malformed: " + d
        | UnknownType(got, expected) ->
            "unknown column type '"
            + got
            + "'; expected one of: "
            + String.concat ", " (expected |> List.map ColumnType.tag)
        | TypeMismatch(col, expected, got) ->
            "column '"
            + col
            + "': expected "
            + ColumnType.tag expected
            + " value, got "
            + got
        | LengthMismatch(col, v, va) ->
            "column '"
            + col
            + "': values/validity length mismatch ("
            + string v
            + " vs "
            + string va
            + ")"
        | NonFiniteFloat(col, tok) ->
            "column '"
            + col
            + "': non-finite float is not representable on the Fuaran wire: "
            + tok
        | Malformed d -> "malformed table: " + d
        | RaggedColumns(col, expected, got) ->
            "ragged table: column '"
            + col
            + "' has "
            + string got
            + " rows where the first column has "
            + string expected

    /// The `Fuaran.Core.Wire.Corpus.Codec` over `DataSource` — encode + a `string`-error decode,
    /// so the columnar strand plugs straight into the conformance corpus tooling (`runCorpus` /
    /// `codecLaws`).
    let codec: Corpus.Codec<DataSource> =
        { Encode = encode
          Decode = fun s -> decode s |> Result.mapError errorString }
