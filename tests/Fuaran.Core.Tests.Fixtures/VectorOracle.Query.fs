module Fuaran.Core.Tests.QueryOracle

// ============================================================================
//  Phase 563 — the data-acquisition seam's model (`proofs/Query.fst`, ladder row
//  `query-codec-differential`) as the exporters ask it, and the bridges the query differentials share
//  with them: the column types and cells as the model spells them, and the model's column-codec
//  parameter instantiated at production's column codec. Moved from the test project's
//  `ProofOracle.QueryTests.fs`, so each is defined once.
// ============================================================================

// Aliased before `Fuaran.Core` is opened: production's `Query` module shares the model's name.
module ModelQuery = Query

open Fuaran.Core
open Fuaran.Core.Tests.VectorOracle
open Fuaran.Core.Tests.CapabilityOracle

/// The query model's unit, restated in its own module (Phase 430).
let qUnitToModel (u: TimeUnit) : ModelQuery.time_unit =
    match u with
    | TimeUnit.Seconds -> ModelQuery.Seconds
    | TimeUnit.Milliseconds -> ModelQuery.Milliseconds
    | TimeUnit.Microseconds -> ModelQuery.Microseconds
    | TimeUnit.Nanoseconds -> ModelQuery.Nanoseconds

let qUnitOfModel (u: ModelQuery.time_unit) : TimeUnit =
    match u with
    | ModelQuery.Seconds -> TimeUnit.Seconds
    | ModelQuery.Milliseconds -> TimeUnit.Milliseconds
    | ModelQuery.Microseconds -> TimeUnit.Microseconds
    | ModelQuery.Nanoseconds -> TimeUnit.Nanoseconds

let qColToModel (t: ColumnType) : ModelQuery.column_type =
    match t with
    | IntType -> ModelQuery.IntType
    | FloatType -> ModelQuery.FloatType
    | BoolType -> ModelQuery.BoolType
    | StringType -> ModelQuery.StringType
    | DateType -> ModelQuery.DateType
    | TimestampType u -> ModelQuery.TimestampType(qUnitToModel u)
    | DecimalType -> ModelQuery.DecimalType

let qCellToModel (c: Cell) : ModelQuery.cell =
    match c with
    | Cell.Int v -> ModelQuery.Int(bigint v)
    | Cell.Float v -> ModelQuery.Float(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
    | Cell.Bool v -> ModelQuery.Bool v
    | Cell.Str v -> ModelQuery.Str v
    | Cell.Date v -> ModelQuery.Date v
    // Phase 430: a timestamp crosses with the unit `Cell.typeOf` names for it — the coarsest unit
    // holding its instant, `TemporalText.unitOf` — beside its text; the model reads the unit as the
    // cell's type and the text as its payload (its header says why).
    | Cell.Timestamp v -> ModelQuery.Timestamp(qUnitToModel (TemporalText.unitOf v), v)
    | Cell.Decimal v -> ModelQuery.Decimal v
    | Cell.Null -> ModelQuery.Null

/// A model cell back as production's: the inverse of `qCellToModel` (a float's carrier is its
/// round-trip text).
let qCellOfModel (c: ModelQuery.cell) : Cell =
    match c with
    | ModelQuery.Int v -> Cell.Int(int v)
    | ModelQuery.Float v -> Cell.Float(System.Double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
    | ModelQuery.Bool v -> Cell.Bool v
    | ModelQuery.Str v -> Cell.Str v
    | ModelQuery.Date v -> Cell.Date v
    | ModelQuery.Timestamp(_, v) -> Cell.Timestamp v
    | ModelQuery.Decimal v -> Cell.Decimal v
    | ModelQuery.Null -> Cell.Null

let qColOfModel (t: ModelQuery.column_type) : ColumnType =
    match t with
    | ModelQuery.IntType -> IntType
    | ModelQuery.FloatType -> FloatType
    | ModelQuery.BoolType -> BoolType
    | ModelQuery.StringType -> StringType
    | ModelQuery.DateType -> DateType
    | ModelQuery.TimestampType u -> TimestampType(qUnitOfModel u)
    | ModelQuery.DecimalType -> DecimalType

// ---- the declaration, its arguments and the renderers as the model reads them (moved here from the
// query differential in Phase 651, so the exporters and the differential share one bridge) ----

let qArgsToModel (args: (string * Cell) list) : ModelQuery.arguments =
    args |> List.map (fun (n, c) -> n, qCellToModel c)

let qHostToModel (h: HostEffect) : ModelQuery.host_effect =
    match h with
    | Pure -> ModelQuery.Pure
    | ReadsHost -> ModelQuery.ReadsHost
    | WritesHost -> ModelQuery.WritesHost

let qDetToModel (d: DeterminismSource) : ModelQuery.determinism_source =
    { ModelQuery.determinism_source.has_clock = Set.contains ClockFactor d
      ModelQuery.determinism_source.has_random = Set.contains RandomFactor d
      ModelQuery.determinism_source.has_network = Set.contains NetworkFactor d }

/// A filter predicate as the model reads it (Phase 398).
let qPredToModel (p: ColumnPredicate) : ModelQuery.predicate =
    match p with
    | ColumnPredicate.EqualTo(c, v) -> ModelQuery.EqualTo(c, qCellToModel v)
    | ColumnPredicate.GreaterThan(c, v) -> ModelQuery.GreaterThan(c, qCellToModel v)
    | ColumnPredicate.AtLeast(c, v) -> ModelQuery.AtLeast(c, qCellToModel v)
    | ColumnPredicate.LessThan(c, v) -> ModelQuery.LessThan(c, qCellToModel v)
    | ColumnPredicate.AtMost(c, v) -> ModelQuery.AtMost(c, qCellToModel v)
    | ColumnPredicate.Contains(c, t) -> ModelQuery.Contains(c, t)
    | ColumnPredicate.IsNull c -> ModelQuery.IsNull c
    | ColumnPredicate.IsNotNull c -> ModelQuery.IsNotNull c

/// An order key as the model reads it (Phase 398).
let qKeyToModel (k: SortKey) : ModelQuery.sort_key =
    { ModelQuery.sort_key.k_column = k.Column
      ModelQuery.sort_key.k_direction =
        match k.Direction with
        | SortDirection.Ascending -> ModelQuery.Ascending
        | SortDirection.Descending -> ModelQuery.Descending }

/// A declaration as the model reads it. `keepRequired = false` is the FORGETFUL bridge the
/// go-red uses: every param crosses as optional, so the model stops seeing step 2.
let queryToModelWith (keepRequired: bool) (q: Query) : ModelQuery.query =
    { ModelQuery.query.q_id = q.Id
      ModelQuery.query.q_params =
        q.Params
        |> List.map (fun p ->
            { ModelQuery.query_param.p_name = p.Name
              ModelQuery.query_param.p_type = qColToModel p.Type
              ModelQuery.query_param.p_required = keepRequired && p.Required })
      ModelQuery.query.q_schema = q.ResultSchema |> List.map (fun f -> f.Name, qColToModel f.Type)
      ModelQuery.query.q_effect =
        { ModelQuery.effect_class.host = qHostToModel q.Effect.Host
          ModelQuery.effect_class.determinism = qDetToModel q.Effect.Determinism }
      ModelQuery.query.q_source = sprintf "%A" q.Source
      ModelQuery.query.q_timeout_ms = toMOpt (Option.map (fun (n: PositiveInt) -> bigint n.Value) q.TimeoutMs)
      ModelQuery.query.q_page_size = toMOpt (Option.map (fun (n: PositiveInt) -> bigint n.Value) q.PageSize)
      ModelQuery.query.q_where = q.Where |> List.map qPredToModel
      ModelQuery.query.q_order_by = q.OrderBy |> List.map qKeyToModel }

let queryToModel (q: Query) : ModelQuery.query = queryToModelWith true q

let qPredOfModel (p: ModelQuery.predicate) : ColumnPredicate =
    match p with
    | ModelQuery.EqualTo(c, v) -> ColumnPredicate.EqualTo(c, qCellOfModel v)
    | ModelQuery.GreaterThan(c, v) -> ColumnPredicate.GreaterThan(c, qCellOfModel v)
    | ModelQuery.AtLeast(c, v) -> ColumnPredicate.AtLeast(c, qCellOfModel v)
    | ModelQuery.LessThan(c, v) -> ColumnPredicate.LessThan(c, qCellOfModel v)
    | ModelQuery.AtMost(c, v) -> ColumnPredicate.AtMost(c, qCellOfModel v)
    | ModelQuery.Contains(c, t) -> ColumnPredicate.Contains(c, t)
    | ModelQuery.IsNull c -> ColumnPredicate.IsNull c
    | ModelQuery.IsNotNull c -> ColumnPredicate.IsNotNull c

let qKeyOfModel (k: ModelQuery.sort_key) : SortKey =
    { Column = k.k_column
      Direction =
        match k.k_direction with
        | ModelQuery.Ascending -> SortDirection.Ascending
        | ModelQuery.Descending -> SortDirection.Descending }

/// The canonical text of a list of member documents, as `QueryShape.keyFields` renders a filter or
/// an order (`Canonical.write Rendering.CrossHost` over the array).
let private shapeText (docs: JVal list) : string =
    Canonical.write Rendering.CrossHost (JArr docs)

/// Production's own seven calls. `compare` on two F# strings is ordinal, which is what
/// `List.sortBy fst` sorts a name by; `field` is the Phase 225 canonicaliser both seams share; the
/// shape renderers (Phase 398) are the declaration codec's `where` and `orderBy` members.
let queryRenderers: ModelQuery.renderers =
    { ModelQuery.renderers.render_int = fun (n: bigint) -> string (int n)
      ModelQuery.renderers.render_float =
        fun (s: string) ->
            FloatLayout.canonical (System.Double.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
      ModelQuery.renderers.hash = Hash.fnv1a
      ModelQuery.renderers.name_le = fun (a: string) (b: string) -> System.String.CompareOrdinal(a, b) <= 0
      ModelQuery.renderers.field = Hash.canonicalField
      // Phase 685: the model renders the shape of every declaration it is handed, refused ones among
      // them, so each predicate is written through the total query-error writer's body and each key
      // through the published sort-key writer, the bodies the key's shape fields are written through.
      ModelQuery.renderers.render_where =
        fun (w: ModelQuery.predicate list) -> shapeText (w |> List.map (qPredOfModel >> QuerySeamLaws.predicateJson))
      ModelQuery.renderers.render_order =
        fun (o: ModelQuery.sort_key list) -> shapeText (o |> List.map (qKeyOfModel >> QuerySeamCodec.encodeSortKey)) }

// ---- Phase 651: the identity a grant for a read is bound to, as the exporters ask it ----

/// The query model, as the `answeredBy` stamp names it.
[<Literal>]
let QueryModel = "Query"

/// The ladder row whose differential holds the query model's identity and grant to production.
[<Literal>]
let QueryLadderRow = "query-differential"

/// `invocation_identity`, over production's renderers: the identity a grant for a read is for.
let queryIdentity (q: Query) (args: (string * Cell) list) : string =
    ModelQuery.invocation_identity queryRenderers (queryToModel q) (qArgsToModel args)

/// `granted`: whether a presented grant, modelled as the identity it carries, is for exactly this read.
let queryGranted (q: Query) (args: (string * Cell) list) (grant: string option) : bool =
    ModelQuery.granted queryRenderers (queryToModel q) (qArgsToModel args) (toMOpt grant)

/// The query model's answer to one vector, for the exporter's stamp.
let queryAnswer (expected: (string * CorpusCell) list) : Answer option =
    Some
        { Model = QueryModel
          LadderRow = QueryLadderRow
          Cell = CorpusCell.Row expected }

/// A declaration's text, for a declaration the gate may refuse (Phase 685: the declaration's writer
/// takes only an admitted declaration). It is the text of the same declaration with no parameter,
/// filter or order — which the gate admits — with those three members written over it as the writer
/// writes them: a parameter as `{name, required, type}`, a predicate through the predicate writer's
/// body (`QuerySeamLaws.predicateJson`) and an order key through the published sort-key writer. For
/// an admitted declaration it is `QueryCodec.encode`'s text byte for byte.
let declarationText (q: Query) : string =
    let bare =
        QuerySeamLaws.admitted
            { q with
                Params = []
                Where = []
                OrderBy = [] }

    let paramJson (p: QueryParam) =
        JObj
            [ "name", JStr p.Name
              "required", JBool p.Required
              "type", JStr(ColumnType.tag p.Type) ]

    let written =
        [ "params", JArr(q.Params |> List.map paramJson) ]
        @ (if List.isEmpty q.Where then
               []
           else
               [ "where", JArr(q.Where |> List.map QuerySeamLaws.predicateJson) ])
        @ (if List.isEmpty q.OrderBy then
               []
           else
               [ "orderBy", JArr(q.OrderBy |> List.map QuerySeamCodec.encodeSortKey) ])

    match Json.parse (QueryCodec.encode bare) with
    | Ok(JObj ms) ->
        Canonical.write Rendering.CrossHost (JObj((ms |> List.filter (fun (k, _) -> k <> "params")) @ written))
    | other -> failwithf "an admitted declaration's text did not read back as an object: %A" other

/// A predicate literal's value as production writes it: the `value` member of a comparison with
/// that literal, written through the predicate writer's body (`QuerySeamLaws.predicateJson`, Phase
/// 685), so the writer is production's own call.
let literalValueJson (v: Cell) : JVal =
    match QuerySeamLaws.predicateJson (ColumnPredicate.EqualTo("v", v)) with
    | JObj p ->
        match p |> List.tryFind (fun (k, _) -> k = "value") with
        | Some(_, v) -> v
        | None -> JObj []
    | _ -> JObj []

/// A literal read at a stated type as `QueryShape.literalOf` reads it: the one cell of a one-row
/// column of that type, through the column codec's public reader.
let literalOfJson (column: string) (ty: ColumnType) (v: JVal) : Cell option =
    let doc =
        JObj
            [ "schema", JArr [ JObj [ "name", JStr column; "type", JStr(ColumnType.tag ty) ] ]
              "columns", JObj [ column, JObj [ "values", JArr [ v ]; "validity", JArr [ JBool true ] ] ] ]

    match ColumnCodec.decodeJson doc with
    | Ok(Embedded { Columns = [ c ] }) when Column.length c = 1 -> Some(Column.cell 0 c)
    | _ -> None

/// The model's column-codec parameter, instantiated at production's column codec: the schema entry,
/// a `Ref` source, an embedded table of rows, and a predicate literal.
let columnCodec: ModelQuery.column_codec<Table> =
    { ModelQuery.column_codec.field_json =
        fun (n, t) -> jvalToModel (ColumnCodec.fieldJson (Field.create n (qColOfModel t)))
      ModelQuery.column_codec.field_of =
        fun j ->
            match ColumnCodec.decodeField (jvalOfModel j) with
            | Ok f -> FStar_Pervasives_Native.Some(f.Name, qColToModel f.Type)
            | Error _ -> FStar_Pervasives_Native.None
      ModelQuery.column_codec.source_json = fun s -> jvalToModel (ColumnCodec.encodeJson (Ref s))
      ModelQuery.column_codec.source_of =
        fun j ->
            match ColumnCodec.decodeJson (jvalOfModel j) with
            | Ok(Ref s) -> FStar_Pervasives_Native.Some s
            | _ -> FStar_Pervasives_Native.None
      ModelQuery.column_codec.rows_json = fun t -> jvalToModel (ColumnCodec.encodeJson (Embedded t))
      ModelQuery.column_codec.rows_of =
        fun j ->
            match ColumnCodec.decodeJson (jvalOfModel j) with
            | Ok(Embedded t) -> FStar_Pervasives_Native.Some t
            | _ -> FStar_Pervasives_Native.None
      ModelQuery.column_codec.lit_json = fun _ c -> jvalToModel (literalValueJson (qCellOfModel c))
      ModelQuery.column_codec.lit_of =
        fun c t j ->
            match literalOfJson c (qColOfModel t) (jvalOfModel j) with
            | Some cell -> FStar_Pervasives_Native.Some(qCellToModel cell)
            | None -> FStar_Pervasives_Native.None }


// ---------------------------------------------------------------------------
//  The seam readers, as the seam-refusal vectors ask them (Phase 563)
// ---------------------------------------------------------------------------

/// The seams' readers as the models answer them: the model's reader of a seam document and the
/// model's writer of what it read (`proofs/Capability.fst` section 18 and the pipeline codec,
/// `proofs/Query.fst` section 19). A reader no model states — the strict-members readings, the schema
/// delta's `FieldChange`, the ops `Rejection` envelope — is absent, and its vectors are the
/// reference's alone.
[<RequireQualifiedAccess>]
module Seams =

    module ModelCap = Capability

    /// The ladder row holding the invocation, envelope and invoke-error codecs to production.
    [<Literal>]
    let SeamRow = "capability-seam-codec-differential"

    /// The ladder row holding the capability, placement and pipeline codecs to production.
    [<Literal>]
    let SectionsRow = "capability-sections-differential"

    /// The ladder row holding the query codecs to production.
    [<Literal>]
    let QueryRow = "query-codec-differential"

    /// A reader's outcome: what it read, written back, or the refusal's code and path.
    type Outcome = Result<JVal, DecodeCode * PathSegment list>

    let private segment (s: ModelCap.path_seg) : PathSegment =
        match s with
        | ModelCap.Key k -> PathSegment.Key k
        | ModelCap.Index n -> PathSegment.Index(int n)

    let private run
        (read: ModelCap.jval -> ModelCap.outcome<'a, ModelCap.decode_error>)
        (write: 'a -> ModelCap.jval)
        (j: JVal)
        : Outcome =
        match read (jvalToModel j) with
        | ModelCap.Ok v -> Ok(jvalOfModel (write v))
        | ModelCap.Error e -> Error(modelCodeOf e.d_code, e.d_path |> List.map segment)

    /// Each reader a model states: its model, its ladder row, and the model's read-and-write.
    let readers: (string * (string * string * (JVal -> Outcome))) list =
        [ "invokeError", (Model, SeamRow, run (ModelCap.invoke_error_of_j codecReaders) ModelCap.invoke_error_json)
          // The vectors' `deferred` reader is `CapabilitySeamCodec.decodeDeferred Decoder.str`, the TYPED
          // envelope reader: the model's `deferred_dec` over `d_str`, the payload's refusal passed through
          // with its own code and path (Phase 570). `deferred_of_j` is `CapabilityCodec.decodeDeferred`'s
          // reader, whose sentence-answering payload reader production raises `OutOfRange`.
          "deferred",
          (Model, SeamRow, run (ModelCap.deferred_dec ModelCap.d_str) (ModelCap.deferred_json ModelCap.JStr))
          "placement", (Model, SectionsRow, run ModelCap.placement_of_j ModelCap.placement_json)
          "declarationFault", (Model, SectionsRow, run (ModelCap.fault_of_j codecReaders) ModelCap.fault_json)
          "pipeline", (Model, SectionsRow, run (ModelCap.pipeline_of_j readers codecReaders) ModelCap.pipeline_json)
          "pipelineNode", (Model, SectionsRow, run (ModelCap.node_of_j readers codecReaders) ModelCap.node_json)
          "argSource", (Model, SectionsRow, run ModelCap.arg_source_of_j ModelCap.arg_source_json)
          "queryError",
          ("Query", QueryRow, run (ModelQuery.query_error_of_j columnCodec) (ModelQuery.query_error_json columnCodec))
          "columnPredicate",
          ("Query", QueryRow, run (ModelQuery.predicate_of_j columnCodec) (ModelQuery.predicate_json columnCodec))
          "sortKey", ("Query", QueryRow, run ModelQuery.sort_key_of_j ModelQuery.sort_key_json)
          "decodeError", ("Query", QueryRow, run ModelQuery.decode_error_of_j ModelQuery.decode_error_json) ]

    /// The model's outcome for `document` read by `reader`, read through the parser model: its model and
    /// row beside it, or none when no model states the reader.
    let read (reader: string) (document: string) : (string * string * Outcome) option =
        readers
        |> List.tryFind (fun (n, _) -> n = reader)
        |> Option.map (fun (_, (model, row, readWrite)) ->
            match JsonModel.readStrict document with
            | Ok j -> model, row, readWrite j
            | Error _ -> model, row, Error(DecodeCode.InvalidJson, []))
