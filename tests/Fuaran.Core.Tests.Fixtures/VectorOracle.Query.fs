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

/// The declaration a shape renderer encodes: everything but the member it reads is fixed.
let shapeCarrier: Query =
    { Id = "shape"
      Params = []
      ResultSchema = []
      Effect = Effect.pureDeterministic
      Source = Ref "shape"
      TimeoutMs = None
      PageSize = None
      Where = []
      OrderBy = [] }

let qColOfModel (t: ModelQuery.column_type) : ColumnType =
    match t with
    | ModelQuery.IntType -> IntType
    | ModelQuery.FloatType -> FloatType
    | ModelQuery.BoolType -> BoolType
    | ModelQuery.StringType -> StringType
    | ModelQuery.DateType -> DateType
    | ModelQuery.TimestampType u -> TimestampType(qUnitOfModel u)
    | ModelQuery.DecimalType -> DecimalType

/// A predicate literal's value as `QueryCodec` writes it — read off the PUBLIC encoder, over a
/// declaration whose only filter compares with that literal, so the writer is production's own call.
let literalValueJson (v: Cell) : JVal =
    let q =
        { shapeCarrier with
            ResultSchema = [ Field.create "v" (Cell.typeOf v |> Option.defaultValue StringType) ]
            Where = [ ColumnPredicate.EqualTo("v", v) ] }

    match Json.parse (QueryCodec.encode q) with
    | Ok(JObj ms) ->
        match ms |> List.tryFind (fun (k, _) -> k = "where") with
        | Some(_, JArr [ JObj p ]) ->
            match p |> List.tryFind (fun (k, _) -> k = "value") with
            | Some(_, v) -> v
            | None -> JObj []
        | _ -> JObj []
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
