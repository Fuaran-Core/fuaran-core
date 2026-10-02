namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Query — the declarative, cross-domain data-acquisition seam
//  (Phase 46). The data-acquisition *sibling* to `Capability`:
//
//    Capability  — invocable compute   (typed inputs -> typed output; host body).
//    Query       — data acquisition    (a declared external fetch -> a Table).
//
//  A `Query` is *data* — a serialisable declaration of external data to fetch
//  (relational rows, retrieval hits, file/asset ingest, reference lookup),
//  typed by the `Table`/`Schema` it produces. The wire carries the declaration,
//  never the host body; the host supplies the resolver (the witness pattern
//  applied to data acquisition — the Core never sees a concrete source kind).
//
//  Cross-cutting, mirrored from `Capability` (Phase 30):
//    * default-deny registry (an unregistered id is a named error);
//    * typed param validation before any fetch (named errors, never a throw);
//    * Phase 27 capture keying (`invocationKey`) so a non-`Deterministic`
//      query replays byte-identically;
//    * Fable-clean canonical codec (`Json`/`Decode`, FSharp.Core only).
//
//  The async axis rides the shipped `Deferred<'T>` envelope (Phase 32, in
//  `Fuaran.Core.Function`): the host resolver returns `Deferred<QueryResult>`,
//  so "not yet" is expressible in the substrate's own vocabulary instead of
//  each adopter inventing one at the resolver boundary (Phase 198). The ERROR
//  axis stays typed — `invoke` returns `Result<Deferred<QueryResult>,
//  QueryError>`, and a resolver's `Failed` is projected into the enumerated
//  `ExecutionFailed`. So a dispatch has exactly three outcomes — SETTLED
//  (`Ok(Ready r)`), PENDING (`Ok Pending`) and REFUSED (`Error e`, typed) — and
//  `Ok(Failed _)` is unreachable by construction, which `queryLaws` certifies
//  rather than this comment merely asserting.
//
//  What this deliberately is NOT: an async runtime. `Deferred` is data;
//  scheduling, polling and completion stay the host's. `Pending` carries no
//  handle — the host correlates a pending fetch by `invocationKey`, which is a
//  function of the declaration and the validated args alone.
//
//  The sibling framing above covers the ASYNC axis too, since Phase 210:
//  `Capability.invoke` takes `body: unit -> Deferred<'v>` and returns
//  `Result<Deferred<'v>, InvokeError>`, projecting a body's `Failed` into the
//  enumerated `BodyFailed` exactly as this seam projects a resolver's into
//  `ExecutionFailed`. So both seams a host adopts have the same three outcomes
//  and the same unreachable fourth, and each keeps its OWN typed error — which
//  is what the sibling relation does and does not mean: one async shape, two
//  error vocabularies. (`Query` was the first to carry the envelope; Phase 198
//  recorded the asymmetry it left behind, and 210 closed it.)
// ============================================================================

/// A typed parameter a query expects at invocation — the data-acquisition analogue of a
/// `Capability` hole. Typed by a `ColumnType` (the scalar set the result columns also use), so a
/// bound value's `Cell` shape must agree with `Type`, or be `Null`.
///
/// `Required` means the parameter must be bound to a VALUE. `validateParams` refuses a required
/// parameter that is left out as `RequiredParamsUnbound`, and one that is present but bound only to
/// `Null` (a `Null` binding is absence) as `RequiredParamsNull`. An optional parameter may be left
/// out or bound to `Null`. This has held since Phase 226. Before it, `Required` checked only that
/// the NAME was present, so a required parameter bound to `Null` reached the resolver.
type QueryParam =
    { Name: string
      Type: ColumnType
      Required: bool }

/// A query declaration: a named, registrable data-acquisition contract. Pure data — it carries no
/// host code. `Effect` reuses `Function`'s two-axis `EffectClass` verbatim (an empty determinism set = pure
/// relational/synthetic, re-evaluable; a set naming `Network` and/or `Clock` = captured result replayed). `Source`
/// is the `Column` `DataSource` (`Embedded` template or host-resolved `Ref`). `ResultSchema` is the
/// typed shape the query produces — so a UI can be typed against it in a schema-only (no-rows) fetch.
type Query =
    { Id: string
      Params: QueryParam list
      ResultSchema: Schema
      Effect: EffectClass
      Source: DataSource
      TimeoutMs: int option
      PageSize: int option }

/// A query invocation result — a page of typed rows. `TotalRowCount`/`NextPageToken` are present
/// when the source can report them (streaming/paging); `None` when unknown.
type QueryResult =
    { Rows: Table
      PageNum: int
      TotalRowCount: int option
      NextPageToken: string option }

/// Why a typed invocation (or a registration) was refused — total, names the failure and, where a
/// closed set is expected, enumerates the alternatives (GP5). Default-deny by shape: only a
/// registered id with in-type params dispatches.
type QueryError =
    | NoSuchQuery of id: string * known: string list
    | DuplicateQuery of id: string
    | UnknownParam of name: string * declared: string list
    | ParamTypeMismatch of name: string * expected: ColumnType * got: ColumnType
    | RequiredParamsUnbound of names: string list
    | SourceNotResolved of ref: string
    | ExecutionFailed of detail: string * recoverable: string list
    | Timeout
    /// The required params the args bind only to `Null` — present, but bound to no value (Phase 226).
    /// Distinct from `RequiredParamsUnbound` (left out), so a caller can tell the two apart.
    | RequiredParamsNull of names: string list

/// A resolver's typed failure (Phase 295) — what `Query.invokeWithArgs`'s resolver answers when the
/// fetch cannot complete, so the refusals the resolver alone can know of reach the caller as the
/// `QueryError` that names them: a `Ref` the host could not resolve is `SourceNotResolved`, a fetch
/// that ran out of time is `Timeout`, and any other failure is `ExecutionFailed` with the arguments a
/// retry may change (`recoverable`). Before Phase 295 the resolver could answer only a string
/// (`Deferred.Failed`), so the first two cases were unreachable and `recoverable` was always empty.
[<RequireQualifiedAccess>]
type ResolveFault =
    | SourceMissing of ref: string
    | TimedOut
    | Failed of detail: string * recoverable: string list

/// What a model reads when a dispatch is refused (Phase 251) — the `InvokeError.describe` twin. Every
/// case that refuses against a closed set names its members, and a `ParamTypeMismatch` over a type
/// a JSON value cannot spell directly (`decimal`, `date`, `timestamp`) also says how to write one.
/// The wire form of the same value is `QueryCodec.queryErrorJson`.
module QueryError =

    let private quoteAll (xs: string list) : string =
        xs |> List.map (fun x -> "'" + x + "'") |> String.concat ", "

    /// How to write a value of a type a JSON scalar does not carry as itself.
    let private howToWrite (t: ColumnType) : string =
        match t with
        | DecimalType ->
            " Write a decimal as a JSON string of decimal text, such as \"12.50\": an optional '-', digits, and an optional '.' followed by digits; never as a JSON number."
        | DateType -> " Write a date as a JSON string, YYYY-MM-DD."
        | TimestampType -> " Write a timestamp as a JSON string, YYYY-MM-DDThh:mm:ssZ."
        | _ -> ""

    /// One sentence a model can act on, naming the failure and what would be accepted.
    let describe (e: QueryError) : string =
        match e with
        | NoSuchQuery(id, []) ->
            "Refused: there is no query '"
            + id
            + "' you may run. There are no queries you may run."
        | NoSuchQuery(id, known) ->
            "Refused: there is no query '"
            + id
            + "' you may run. The queries you may run are "
            + quoteAll known
            + "."
        | DuplicateQuery id -> "Refused: the query '" + id + "' is registered twice."
        | UnknownParam(name, []) ->
            "Refused: '"
            + name
            + "' is not a parameter of this query. It takes no parameters."
        | UnknownParam(name, declared) ->
            "Refused: '"
            + name
            + "' is not a parameter of this query. Its parameters are "
            + quoteAll declared
            + "."
        | ParamTypeMismatch(name, expected, got) ->
            "Refused: parameter '"
            + name
            + "' must be "
            + ColumnType.tag expected
            + "; you sent "
            + ColumnType.tag got
            + "."
            + howToWrite expected
        | RequiredParamsUnbound names -> "Refused: required parameters missing: " + quoteAll names + "."
        | SourceNotResolved r -> "Refused: the data source '" + r + "' could not be resolved."
        | ExecutionFailed(detail, []) -> "Refused: the query ran and failed: " + detail + "."
        | ExecutionFailed(detail, recoverable) ->
            "Refused: the query ran and failed: "
            + detail
            + ". You may retry with "
            + quoteAll recoverable
            + "."
        | Timeout -> "Refused: the query timed out."
        | RequiredParamsNull names -> "Refused: required parameters bound to no value: " + quoteAll names + "."

    /// Every refusal, one sentence per line, in the order given — the reading of
    /// `Query.validateParamsAll`'s answer.
    let describeAll (es: QueryError list) : string =
        es |> List.map describe |> String.concat "\n"

/// The data-acquisition surface: typed registry (populate + enumerate + dispatch), the
/// param-validation contract, the Phase 27 capture keying. Additive over `Column`/`Function`;
/// FSharp.Core-only, Fable-clean.
module Query =

    /// The `ColumnType` a present (non-`Null`) cell realizes — `Cell.typeOf`, the column strand's own
    /// (Phase 295; this module re-implemented it before).
    let private cellType (c: Cell) : ColumnType option = Cell.typeOf c

    /// Does a cell of type `got` fill a parameter of type `declared`? THE widening lattice,
    /// `ColumnType.widens` (Phase 295): the identity, or a lossless promotion — an `int` fills a
    /// `float` or a `decimal` parameter. It was type equality before, so an `int` argument to a
    /// `float` parameter was refused while the column codec read the same value into a `float`
    /// column. For the types that have a value space the answer is `Space.subsumes`'s (the
    /// `spaceRelationLaws` family pins the two together).
    let private fills (declared: ColumnType) (got: ColumnType) : bool = ColumnType.widens got declared

    /// A validated cell at its parameter's declared type: an `int` that fills a `float` or `decimal`
    /// parameter is handed on as that type, so a resolver reads the type it declared.
    let private promote (declared: ColumnType) (c: Cell) : Cell =
        match declared, c with
        | FloatType, Int v -> Float(float v)
        | DecimalType, Int v -> Cell.decimal (string v) |> Option.defaultValue c
        | _ -> c

    /// The Phase 27 determinism label this query keys its captures on (`"deterministic"` /
    /// `"clock"` / `"random"` / `"network"`).
    let determinismTag (q: Query) : string =
        Effect.determinismTag q.Effect.Determinism

    /// A bound `Cell` as two fields of the capture key's pre-image: a one-letter constructor tag
    /// and the scalar rendering (a `Null` is tag `n` with an empty payload).
    let private cellFields (c: Cell) : string * string =
        match c with
        | Int v -> "i", string v
        | Float v -> "f", Canon.canonicalFloat v
        | Bool v -> "b", (if v then "1" else "0")
        | Str v -> "s", v
        | Date v -> "d", v
        | Timestamp v -> "t", v
        // `m`: `d` is a date's. The text is the payload as it stands, which is what keeps the
        // pre-image injective on cells (`cell_fields_injective` in `proofs/Query.fst`).
        | Decimal v -> "m", v
        | Null -> "n", ""

    /// The effect-identity key the Phase 27 capture seam journals a non-deterministic query under:
    /// the query id + a hash of the canonical (name-sorted) pre-image, built through
    /// `Hash.canonicalFields` — three fields per binding (name, cell tag, cell payload). Same args
    /// replay the same captured rows. The pre-image is INJECTIVE (Phase 225,
    /// `invocation_key_injective` in `proofs/Query.fst`): distinct argument sets never share one,
    /// whatever their string cells contain. That two distinct pre-images hash apart is a property
    /// of `Hash.fnv1a` and is not claimed. A consumer threads this as `OpStream.captureEffect`'s
    /// `eff` argument (the `Capability.invocationKey` pattern).
    let invocationKey (q: Query) (args: (string * Cell) list) : string =
        let canonical =
            args
            |> List.sortBy fst
            |> List.collect (fun (n, v) ->
                let tag, payload = cellFields v
                [ n; tag; payload ])
            |> Hash.canonicalFields

        q.Id + "#" + Hash.fnv1a canonical

    /// Validate typed `args` (name -> bound `Cell`) against the query's declared params *before* any
    /// fetch: every arg must address a declared param and its cell type must fill the param's type —
    /// `ColumnType.widens`, Phase 295 — or be `Null`;
    /// every required param must be bound (`RequiredParamsUnbound` otherwise); and every required
    /// param must be bound to a VALUE — one whose bindings are all `Null` is refused as
    /// `RequiredParamsNull` (Phase 226, `required_is_non_null` in `proofs/Query.fst`). The steps run
    /// in that order and the first refusal is the answer. Default-deny by shape (FGP 3) — the host
    /// validates this before running any resolver.
    let validateParams (q: Query) (args: (string * Cell) list) : Result<unit, QueryError> =
        let declared = q.Params |> List.map (fun p -> p.Name)
        let argMap = Map.ofList args

        let rec checkArgs =
            function
            | [] -> Ok()
            | (name, cell) :: rest ->
                match q.Params |> List.tryFind (fun p -> p.Name = name) with
                | None -> Error(UnknownParam(name, declared))
                | Some p ->
                    match cellType cell with
                    | None -> checkArgs rest // a Null binding — absence, type-agnostic
                    | Some t when fills p.Type t -> checkArgs rest
                    | Some t -> Error(ParamTypeMismatch(name, p.Type, t))

        checkArgs args
        |> Result.bind (fun () ->
            let unbound =
                q.Params
                |> List.filter (fun p -> p.Required && not (Map.containsKey p.Name argMap))
                |> List.map (fun p -> p.Name)

            if List.isEmpty unbound then
                Ok()
            else
                Error(RequiredParamsUnbound unbound))
        |> Result.bind (fun () ->
            // Some binding gives the name a non-`Null` cell. A `Null` binding is absence (step 1
            // treats it so), so a required name bound only to `Null` is bound to no value.
            let boundToValue (name: string) =
                args
                |> List.exists (fun (n, c) ->
                    n = name
                    && (match c with
                        | Null -> false
                        | _ -> true))

            let nullBound =
                q.Params
                |> List.filter (fun p -> p.Required && not (boundToValue p.Name))
                |> List.map (fun p -> p.Name)

            if List.isEmpty nullBound then
                Ok()
            else
                Error(RequiredParamsNull nullBound))

    /// Invoke a query: validate the params, then run the host `resolve` (which performs the actual
    /// fetch per the query's source + effect/placement). The resolver answers in the shipped
    /// `Deferred` envelope, so a fetch it has not completed is `Pending` rather than a failure or a
    /// host-invented shape; a resolver failure is the named `ExecutionFailed`, never a throw and
    /// never an untyped `Failed` riding out of the seam. Deterministic queries re-evaluate freely;
    /// for a non-`Deterministic` query the caller journals the realized result (the `Ready` payload —
    /// `Pending` is not captured, replay re-issues) via `OpStream.captureEffect` keyed by
    /// `invocationKey` + `determinismTag` (Phase 27), so the query replays exactly.
    ///
    /// The three outcomes, exhaustively: `Ok(Ready r)` settled · `Ok Pending` in flight · `Error e`
    /// refused, typed. `Ok(Failed _)` cannot occur — certified by `queryLaws`.
    let invoke
        (q: Query)
        (args: (string * Cell) list)
        (resolve: Query -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        validateParams q args
        |> Result.bind (fun () ->
            match resolve q with
            | Ready r -> Ok(Ready r)
            | Pending -> Ok Pending
            | Failed m -> Error(ExecutionFailed(m, [])))

    // ---- every refusal at once, the validated arguments handed on, and the schema (Phase 251) ----

    /// Validate as `validateParams` does, but answer with EVERY refusal rather than the first: one
    /// per refused argument (`UnknownParam` / `ParamTypeMismatch`), in argument order, then
    /// `RequiredParamsUnbound` naming every required parameter left out, then `RequiredParamsNull`
    /// naming every required parameter that is present and bound only to `Null`. The first-failure
    /// form is kept, and the two agree by construction of that order: the head of this list is
    /// exactly `validateParams`'s refusal, and `Ok ()` here is `Ok ()` there.
    let validateParamsAll (q: Query) (args: (string * Cell) list) : Result<unit, QueryError list> =
        let declared = q.Params |> List.map (fun p -> p.Name)
        let argMap = Map.ofList args

        let argFault (name: string, cell: Cell) : QueryError option =
            match q.Params |> List.tryFind (fun p -> p.Name = name) with
            | None -> Some(UnknownParam(name, declared))
            | Some p ->
                match cellType cell with
                | None -> None
                | Some t when fills p.Type t -> None
                | Some t -> Some(ParamTypeMismatch(name, p.Type, t))

        let faults = args |> List.choose argFault

        let unbound =
            q.Params
            |> List.filter (fun p -> p.Required && not (Map.containsKey p.Name argMap))
            |> List.map (fun p -> p.Name)

        let boundToValue (name: string) =
            args |> List.exists (fun (n, c) -> n = name && (cellType c).IsSome)

        let nullBound =
            q.Params
            |> List.filter (fun p -> p.Required && Map.containsKey p.Name argMap && not (boundToValue p.Name))
            |> List.map (fun p -> p.Name)

        let all =
            faults
            @ (if List.isEmpty unbound then
                   []
               else
                   [ RequiredParamsUnbound unbound ])
            @ (if List.isEmpty nullBound then
                   []
               else
                   [ RequiredParamsNull nullBound ])

        if List.isEmpty all then Ok() else Error all

    /// `invoke`, with the resolver handed the validated argument list — typed, as `Cell`s, the list
    /// `validateParams` checked, each cell at its parameter's declared type (an `int` filling a
    /// `float` parameter arrives as a `float`) — so a resolver reads its arguments instead of closing
    /// over the caller's list. The same three outcomes as `invoke`, and a resolver's `Failed m` is
    /// projected into `ExecutionFailed` exactly as there.
    ///
    /// Since Phase 295 the resolver answers a TYPED failure beside the envelope (`ResolveFault`), so
    /// the refusals only it can know of reach the caller by name: `SourceMissing` is
    /// `SourceNotResolved`, `TimedOut` is `Timeout`, and `Failed(detail, recoverable)` is
    /// `ExecutionFailed(detail, recoverable)` with the arguments a retry may change.
    let invokeWithArgs
        (q: Query)
        (args: (string * Cell) list)
        (resolve: Query -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        : Result<Deferred<QueryResult>, QueryError> =
        validateParams q args
        |> Result.bind (fun () ->
            let typed =
                args
                |> List.map (fun (name, cell) ->
                    match q.Params |> List.tryFind (fun p -> p.Name = name) with
                    | Some p -> name, promote p.Type cell
                    | None -> name, cell)

            match resolve q typed with
            | Ok(Ready r) -> Ok(Ready r)
            | Ok Pending -> Ok Pending
            | Ok(Failed m) -> Error(ExecutionFailed(m, []))
            | Error(ResolveFault.SourceMissing r) -> Error(SourceNotResolved r)
            | Error ResolveFault.TimedOut -> Error Timeout
            | Error(ResolveFault.Failed(detail, recoverable)) -> Error(ExecutionFailed(detail, recoverable)))

    /// The text an exact decimal is written as: the grammar `DecimalText` READS, which is exactly
    /// the set of strings the codec decodes into a `Decimal` cell (and canonicalises: `12.50` is
    /// `Decimal "12.5"`).
    let private decimalPattern = "^-?[0-9]+(\\.[0-9]+)?$"
    let private datePattern = "^[0-9]{4}-[0-9]{2}-[0-9]{2}$"

    let private timestampPattern =
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$"

    /// The JSON Schema of one value of a column type, as a model emits it and the codec reads it.
    /// A `decimal` is a STRING with the decimal-text pattern, never a number: a model told
    /// "number" emits a fractional token, and the codec refuses one, because a value that has
    /// been through a float is not a value an exact type can vouch for. A `date` / `timestamp` is
    /// a string of its canonical ISO-8601 shape; an `int` is bounded to the 32-bit range the cell
    /// holds.
    let private columnSchema (t: ColumnType) : JVal =
        match t with
        | IntType ->
            JObj
                [ "type", JStr "integer"
                  "minimum", JInt System.Int32.MinValue
                  "maximum", JInt System.Int32.MaxValue ]
        | FloatType -> JObj [ "type", JStr "number" ]
        | BoolType -> JObj [ "type", JStr "boolean" ]
        | StringType -> JObj [ "type", JStr "string" ]
        | DateType -> JObj [ "type", JStr "string"; "pattern", JStr datePattern ]
        | TimestampType -> JObj [ "type", JStr "string"; "pattern", JStr timestampPattern ]
        | DecimalType -> JObj [ "type", JStr "string"; "pattern", JStr decimalPattern ]

    /// Project a query into a STANDARD JSON Schema `object` — `Function.toJsonSchema`'s twin, over
    /// the same convention (Phase 251; the convention is written down beside `Function.toSchema`):
    /// no tag, `"title"` the query id, each parameter a property keyed by its NAME with the schema
    /// of its column type, the required ones listed, and what JSON Schema has no keyword for outside
    /// `properties` under `x-` keys — `x-effect` (the two-axis effect class, as there) and
    /// `x-result`, the schema of ONE result row keyed by column name, so a model reads what comes
    /// back without it reading as an argument to fill. A parameter's value is what
    /// `QueryCodec.decodeArgs` reads. `Canon.render` of the result is canonical and stable for a
    /// fixed query.
    let toJsonSchema (q: Query) : JVal =
        JObj
            [ "type", JStr "object"
              "title", JStr q.Id
              "x-effect", EffectCodec.toJson q.Effect
              "properties", JObj(q.Params |> List.map (fun p -> p.Name, columnSchema p.Type))
              "required", JArr(q.Params |> List.filter (fun p -> p.Required) |> List.map (fun p -> JStr p.Name))
              "x-result",
              JObj
                  [ "type", JStr "object"
                    "properties", JObj(q.ResultSchema |> List.map (fun (n, t) -> n, columnSchema t)) ] ]

/// A typed query registry — the discovery surface an agent enumerates (the data-acquisition analogue
/// of node-introspection / capability discovery): "what data may I acquire, with what typed params,
/// producing what schema". Default-deny by shape on dispatch — only a registered id resolves.
type QueryRegistry = { Queries: Map<string, Query> }

module QueryRegistry =

    let empty: QueryRegistry = { Queries = Map.empty }

    /// Register a query — additive, no silent overwrite (a duplicate id is a named error).
    let register (q: Query) (r: QueryRegistry) : Result<QueryRegistry, QueryError> =
        if Map.containsKey q.Id r.Queries then
            Error(DuplicateQuery q.Id)
        else
            Ok
                { r with
                    Queries = Map.add q.Id q r.Queries }

    let tryFind (id: string) (r: QueryRegistry) : Query option = Map.tryFind id r.Queries

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part of
    /// the contract (`queryLaws` certifies it).
    let enumerate (r: QueryRegistry) : Query list = r.Queries |> Map.toList |> List.map snd

    /// Dispatch an invocation through the registry: resolve the id (default-deny — an unregistered id
    /// is `NoSuchQuery`), then `Query.invoke`. The host resolver is supplied by the caller per the
    /// resolved query's source + placement, and answers in the `Deferred` envelope — so the same
    /// three outcomes `Query.invoke` documents are what a registry dispatch returns.
    let dispatch
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        match Map.tryFind id r.Queries with
        | None -> Error(NoSuchQuery(id, r.Queries |> Map.toList |> List.map fst))
        | Some q -> Query.invoke q args resolve

    /// `dispatch`, with the resolver handed the resolved query and the validated argument list
    /// (`Query.invokeWithArgs`, Phase 251). Additive beside `dispatch`; default-deny the same.
    let dispatchWithArgs
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        : Result<Deferred<QueryResult>, QueryError> =
        match Map.tryFind id r.Queries with
        | None -> Error(NoSuchQuery(id, r.Queries |> Map.toList |> List.map fst))
        | Some q -> Query.invokeWithArgs q args resolve

/// The canonical wire codec for a `Query` declaration + a `QueryResult`. Round-trips the typed
/// params, the result schema, the effect class, the source (via `ColumnCodec`), and the paging
/// fields. Fable-clean (`Json` / `Decode`).
module QueryCodec =

    // ---- column type ----

    // A column type is spelled by `ColumnType.tag` and read back by `ColumnType.ofTag` (Phase 295;
    // this codec carried its own copy of both before).
    let private colTypeStr = ColumnType.tag

    // Phase 310 — every member is read through the typed decode layer (`Decoder`); a refusal is
    // spelled into this codec's `QueryError` envelope at the entry points, with the sentence it
    // has always carried.

    /// A string from a closed set; a miss is `UnknownTag`, in this codec's sentence `<what><value>`.
    let private tagged (what: string) (cases: (string * 'T) list) : Decoder<'T> =
        Decoder.str
        |> Decoder.andThen (fun s ->
            match cases |> List.tryFind (fun (k, _) -> k = s) with
            | Some(_, v) -> Ok v
            | None ->
                Error(
                    DecodeError.make
                        DecodeCode.UnknownTag
                        ("one of "
                         + (cases |> List.map (fun (k, _) -> "'" + k + "'") |> String.concat ", "))
                        (what + s)
                ))

    let private colTypeOf: Decoder<ColumnType> =
        tagged "unknown column type: " (ColumnType.all |> List.map (fun t -> ColumnType.tag t, t))

    // The effect class is `EffectCodec`'s (Phase 295), the capability codec's reader and writer.

    // ---- param ----

    let private paramJson (p: QueryParam) : JVal =
        JObj
            [ "name", JStr p.Name
              "type", JStr(colTypeStr p.Type)
              "required", JBool p.Required ]

    let private paramOf (el: JVal) : Result<QueryParam, DecodeError> =
        Decoder.field "name" Decoder.str el
        |> Result.bind (fun name ->
            Decoder.field "type" colTypeOf el
            |> Result.bind (fun ty ->
                // This codec's sentence for a non-bool `required` is its own, kept.
                Decoder.field
                    "required"
                    (fun j ->
                        Decoder.bool j
                        |> Result.mapError (DecodeError.reword (fun _ -> "expected a bool")))
                    el
                |> Result.map (fun req ->
                    { Name = name
                      Type = ty
                      Required = req })))

    // ---- schema ----

    let private schemaJson (s: Schema) : JVal =
        JArr(
            s
            |> List.map (fun (n, t) -> JObj [ "name", JStr n; "type", JStr(colTypeStr t) ])
        )

    let private schemaOf: Decoder<Schema> =
        Decoder.list (fun e ->
            Decoder.field "name" Decoder.str e
            |> Result.bind (fun n -> Decoder.field "type" colTypeOf e |> Result.map (fun t -> n, t)))

    // ---- optional int ----

    let private optIntJson (name: string) (v: int option) : (string * JVal) list =
        match v with
        | Some n -> [ name, JInt n ]
        | None -> []

    // ---- query declaration ----

    let internal queryJson (q: Query) : JVal =
        JObj(
            [ "$type", JStr "query"
              "id", JStr q.Id
              "params", JArr(q.Params |> List.map paramJson)
              "resultSchema", schemaJson q.ResultSchema
              "effect", EffectCodec.toJson q.Effect
              "source", ColumnCodec.encodeJson q.Source ]
            @ optIntJson "timeoutMs" q.TimeoutMs
            @ optIntJson "pageSize" q.PageSize
        )

    let encode (q: Query) : string = Canon.render (queryJson q)

    // Phase 295 — a decode failure is a decode failure: the readers answer a typed `DecodeError` (the
    // `…Detailed` entry points, Phase 310's convention) and the string forms its sentence, as
    // `CapabilityCodec` does. They answered `ExecutionFailed("decode: …")` before, a refusal of a
    // fetch that never ran.

    /// A refusal from the column codec, carried as a decode refusal at the member it was reading.
    let private columnRefused (memberName: string) (what: string) : DecodeError =
        DecodeError.under
            (PathSegment.Key memberName)
            (DecodeError.make DecodeCode.OutOfRange what (memberName + ": not " + what + " the column codec reads"))

    let internal queryOf (el: JVal) : Result<Query, DecodeError> =
        Decoder.field "id" Decoder.str el
        |> Result.bind (fun id ->
            Decoder.field "params" (Decoder.list paramOf) el
            |> Result.bind (fun ps ->
                Decoder.field "resultSchema" schemaOf el
                |> Result.bind (fun sch ->
                    Decoder.field "effect" EffectCodec.decoder el
                    |> Result.bind (fun eff ->
                        Decoder.optField "timeoutMs" Decoder.int el
                        |> Result.bind (fun tmo ->
                            Decoder.optField "pageSize" Decoder.int el
                            |> Result.bind (fun pg ->
                                Decoder.field "source" Decoder.json el
                                |> Result.bind (fun srcEl ->
                                    match ColumnCodec.decodeJson srcEl with
                                    | Error _ -> Error(columnRefused "source" "a data source")
                                    | Ok src ->
                                        Ok
                                            { Id = id
                                              Params = ps
                                              ResultSchema = sch
                                              Effect = eff
                                              Source = src
                                              TimeoutMs = tmo
                                              PageSize = pg })))))))

    // ---- query result ----

    let internal resultJson (qr: QueryResult) : JVal =
        let nextTok =
            match qr.NextPageToken with
            | Some tok -> [ "nextPageToken", JStr tok ]
            | None -> []

        JObj(
            [ "$type", JStr "queryResult"
              "rows", ColumnCodec.encodeJson (Embedded qr.Rows)
              "pageNum", JInt qr.PageNum ]
            @ optIntJson "totalRowCount" qr.TotalRowCount
            @ nextTok
        )

    let encodeResult (qr: QueryResult) : string = Canon.render (resultJson qr)

    let internal resultOf (el: JVal) : Result<QueryResult, DecodeError> =
        Decoder.field "rows" Decoder.json el
        |> Result.bind (fun rowsEl ->
            match ColumnCodec.decodeJson rowsEl with
            | Error _ -> Error(columnRefused "rows" "a table")
            | Ok(Ref _) -> Error(columnRefused "rows" "an embedded table (a ref is not a result)")
            | Ok(Embedded t) ->
                Decoder.field "pageNum" Decoder.int el
                |> Result.bind (fun pn ->
                    Decoder.optField "totalRowCount" Decoder.int el
                    |> Result.bind (fun trc ->
                        // Phase 310: a present `nextPageToken` that is not a string is refused,
                        // where it was read as absent.
                        Decoder.optField "nextPageToken" Decoder.str el
                        |> Result.map (fun tok ->
                            { Rows = t
                              PageNum = pn
                              TotalRowCount = trc
                              NextPageToken = tok }))))

    // ---- deferred query result (Phase 198) ----
    // The seam's result type is now `Deferred<QueryResult>`, so it has to cross the wire like every
    // other Query type — a host that answers `Pending` over a transport has nothing to send
    // otherwise. This reuses the shipped envelope codec (`CapabilityCodec.deferredJson` /
    // `deferredOf`, `"$type"`-tagged `pending` / `ready` / `failed`) rather than minting a second
    // encoding of the same three cases: one envelope, one wire shape, both seams.

    let internal deferredResultJson (d: Deferred<QueryResult>) : JVal =
        CapabilityCodec.deferredJson resultJson d

    let encodeDeferredResult (d: Deferred<QueryResult>) : string = Canon.render (deferredResultJson d)

    let internal deferredResultOf (el: JVal) : Result<Deferred<QueryResult>, DecodeError> =
        CapabilityCodec.deferredOf (fun e -> resultOf e |> Result.mapError DecodeError.describe) el
        |> Result.mapError (fun m -> DecodeError.make DecodeCode.OutOfRange "a deferred query result" m)

    // ---- typed refusal (Phase 251) ----
    // `CapabilityCodec.invokeErrorJson`'s twin: one `$type` per case, in camelCase, and a column
    // type by its tag. The sentence a model reads is `QueryError.describe`.

    let private strs (xs: string list) : JVal = JArr(xs |> List.map JStr)

    /// Encode a `QueryError` to a `JVal` (`"$type"` is the case: `noSuchQuery`, `paramTypeMismatch`, …).
    let queryErrorJson (e: QueryError) : JVal =
        match e with
        | NoSuchQuery(id, known) -> Canon.typed "noSuchQuery" [ "id", JStr id; "known", strs known ]
        | DuplicateQuery id -> Canon.typed "duplicateQuery" [ "id", JStr id ]
        | UnknownParam(name, declared) -> Canon.typed "unknownParam" [ "name", JStr name; "declared", strs declared ]
        | ParamTypeMismatch(name, expected, got) ->
            Canon.typed
                "paramTypeMismatch"
                [ "name", JStr name
                  "expected", JStr(colTypeStr expected)
                  "got", JStr(colTypeStr got) ]
        | RequiredParamsUnbound names -> Canon.typed "requiredParamsUnbound" [ "names", strs names ]
        | SourceNotResolved r -> Canon.typed "sourceNotResolved" [ "ref", JStr r ]
        | ExecutionFailed(detail, recoverable) ->
            Canon.typed "executionFailed" [ "detail", JStr detail; "recoverable", strs recoverable ]
        | Timeout -> Canon.typed "timeout" []
        | RequiredParamsNull names -> Canon.typed "requiredParamsNull" [ "names", strs names ]

    let encodeQueryError (e: QueryError) : string = Canon.render (queryErrorJson e)

    let private queryErrorOfDetailed: Decoder<QueryError> =
        let str name = Decoder.field name Decoder.str

        let strList name =
            Decoder.field name (Decoder.list Decoder.str)

        let both (a: Decoder<'A>) (b: Decoder<'B>) (f: 'A -> 'B -> QueryError) : Decoder<QueryError> =
            a |> Decoder.bind (fun x -> b |> Decoder.map (f x))

        let cases =
            [ "noSuchQuery", both (str "id") (strList "known") (fun id known -> NoSuchQuery(id, known))
              "duplicateQuery", str "id" |> Decoder.map DuplicateQuery
              "unknownParam", both (str "name") (strList "declared") (fun name d -> UnknownParam(name, d))
              "paramTypeMismatch",
              str "name"
              |> Decoder.bind (fun name ->
                  both (Decoder.field "expected" colTypeOf) (Decoder.field "got" colTypeOf) (fun exp got ->
                      ParamTypeMismatch(name, exp, got)))
              "requiredParamsUnbound", strList "names" |> Decoder.map RequiredParamsUnbound
              "sourceNotResolved", str "ref" |> Decoder.map SourceNotResolved
              "executionFailed", both (str "detail") (strList "recoverable") (fun d r -> ExecutionFailed(d, r))
              "timeout", Decoder.succeed Timeout
              "requiredParamsNull", strList "names" |> Decoder.map RequiredParamsNull ]

        // The dispatch's own miss keeps this codec's sentence.
        fun el ->
            Decoder.tagDispatch "$type" cases el
            |> Result.mapError (fun e ->
                match e.Code, e.Path, Decoder.tryMember "$type" el with
                | DecodeCode.UnknownTag, [ PathSegment.Key "$type" ], Some(JStr other) ->
                    { e with
                        Message = "unknown query error: " + other }
                | _ -> e)

    /// Decode a `QueryError` from a `JVal`. The error side is a plain `string`: a refusal that
    /// cannot be read is not itself a refusal of the query.
    let queryErrorOf (el: JVal) : Result<QueryError, string> =
        Decoder.describing queryErrorOfDetailed el

    let decodeQueryError (s: string) : Result<QueryError, string> =
        Decode.parse s |> Result.bind queryErrorOf

    // ---- a model's arguments (Phase 251) ----
    // `Query.toJsonSchema` tells a model to emit one object keyed by parameter name; this reads that
    // object back into the typed argument list `validateParams` takes. Each value is read by the
    // ONE cell decoder the column codec owns — as the single cell of a one-row column of the
    // parameter's type — so an argument decodes exactly as the same value would in a result table:
    // a decimal is read from decimal text and canonicalised, a date or timestamp only in its
    // canonical form, and a fractional number token for a decimal is refused.

    /// The column type a JSON scalar spells as itself, for naming what was sent.
    let private sentType (v: JVal) : ColumnType option =
        match v with
        | JInt _ -> Some IntType
        | JFloat _ -> Some FloatType
        | JBool _ -> Some BoolType
        | JStr _ -> Some StringType
        | JArr _
        | JObj _ -> None

    let private argCell (name: string) (ty: ColumnType) (v: JVal) : Result<string * Cell, QueryError> =
        let oneCell =
            JObj
                [ "schema", JArr [ JObj [ "name", JStr name; "type", JStr(colTypeStr ty) ] ]
                  "columns", JObj [ name, JObj [ "values", JArr [ v ]; "validity", JArr [ JBool true ] ] ] ]

        let refused () =
            match sentType v with
            | Some got -> Error(ParamTypeMismatch(name, ty, got))
            | None ->
                Error(
                    ExecutionFailed(
                        "decode: parameter '"
                        + name
                        + "' takes a "
                        + colTypeStr ty
                        + " value, not a JSON "
                        + JVal.kindName v,
                        []
                    )
                )

        match ColumnCodec.decodeJson oneCell with
        | Ok(Embedded { Columns = [ { Cells = [ cell ] } ] }) -> Ok(name, cell)
        | _ -> refused ()

    /// Read a model's argument object — the shape `Query.toJsonSchema` describes — into the typed
    /// argument list, answering with EVERY refusal: an unknown member is `UnknownParam` naming the
    /// declared parameters (whatever the read policy: a parameter the query does not declare is never
    /// read past), and a value its parameter's type cannot read is `ParamTypeMismatch` naming the JSON
    /// type that was sent. Required-ness is not checked here; `Query.validateParams` /
    /// `validateParamsAll` take the list this returns.
    let decodeArgsJson (q: Query) (el: JVal) : Result<(string * Cell) list, QueryError list> =
        match el with
        | JObj fields ->
            let declared = q.Params |> List.map (fun p -> p.Name)

            let decoded =
                fields
                |> List.map (fun (name, v) ->
                    match q.Params |> List.tryFind (fun p -> p.Name = name) with
                    | None -> Error(UnknownParam(name, declared))
                    | Some p -> argCell name p.Type v)

            match
                decoded
                |> List.choose (function
                    | Error e -> Some e
                    | Ok _ -> None)
            with
            | [] ->
                Ok(
                    decoded
                    |> List.choose (function
                        | Ok a -> Some a
                        | Error _ -> None)
                )
            | errors -> Error errors
        | other ->
            Error
                [ ExecutionFailed(
                      "decode: a query's arguments are one JSON object keyed by parameter name, not a JSON "
                      + JVal.kindName other,
                      []
                  ) ]

    /// `decodeArgsJson` over text.
    let decodeArgs (q: Query) (s: string) : Result<(string * Cell) list, QueryError list> =
        match Decode.parse s with
        | Error m -> Error [ ExecutionFailed("parse: " + m, []) ]
        | Ok el -> decodeArgsJson q el

    // ---- strict read policy (Phase 251) ----
    // `CapabilityCodec`'s policy, over this codec's documents: `Strict` checks that every object
    // this codec reads carries only the members it reads, then runs the ordinary decoder, so
    // `Lenient` is byte-for-byte the old behaviour. The embedded `source` and `rows` documents are
    // the column codec's, read by its own rules; the check stops at them.

    let private quoteAll (xs: string list) : string =
        xs |> List.map (fun x -> "'" + x + "'") |> String.concat ", "

    // Phase 310 — the members check is the decode layer's strict policy (`Decoder.members`); the
    // refusal keeps this codec's sentence, naming the object it is in.

    let private tagOf (el: JVal) : string option =
        match Decoder.tryMember "$type" el with
        | Some(JStr t) -> Some t
        | _ -> None

    let private members (where: string) (known: string list) : Decoder<unit> =
        fun el ->
            Decoder.members known el
            |> Result.mapError (fun e ->
                match List.tryLast e.Path with
                | Some(PathSegment.Key k) ->
                    { e with
                        Message =
                            "unknown member '"
                            + k
                            + "' in "
                            + where
                            + "; its members are "
                            + quoteAll (List.sort known) }
                | _ -> e)

    /// Check the member `name` of `el`, where it is present.
    let private within (name: string) (check: Decoder<unit>) : Decoder<unit> =
        fun el ->
            match el with
            | JObj _ -> Decoder.optField name check el |> Result.map ignore
            | _ -> Ok()

    /// Check every element of an array.
    let private each (check: Decoder<unit>) : Decoder<unit> =
        fun el ->
            match el with
            | JArr _ -> Decoder.list check el |> Result.map ignore
            | _ -> Ok()

    let private strictQuery (el: JVal) =
        members
            "query"
            [ "$type"
              "id"
              "params"
              "resultSchema"
              "effect"
              "source"
              "timeoutMs"
              "pageSize" ]
            el
        |> Result.bind (fun () -> within "params" (each (members "query parameter" [ "name"; "type"; "required" ])) el)
        |> Result.bind (fun () -> within "resultSchema" (each (members "result column" [ "name"; "type" ])) el)
        |> Result.bind (fun () -> within "effect" (members "effect" EffectCodec.members) el)

    let private strictResult (el: JVal) =
        members "query result" [ "$type"; "rows"; "pageNum"; "totalRowCount"; "nextPageToken" ] el

    let private strictDeferredResult (el: JVal) =
        let extra =
            match tagOf el with
            | Some "ready" -> [ "value" ]
            | Some "failed" -> [ "message" ]
            | _ -> []

        members "deferred" ("$type" :: extra) el
        |> Result.bind (fun () ->
            match tagOf el with
            | Some "ready" -> within "value" strictResult el
            | _ -> Ok())

    let private readWith
        (policy: ReadPolicy)
        (check: Decoder<unit>)
        (read: JVal -> Result<'T, DecodeError>)
        (s: string)
        : Result<'T, DecodeError> =
        Decoder.parse s
        |> Result.bind (fun el ->
            match policy with
            | ReadPolicy.Lenient -> read el
            | ReadPolicy.Strict -> check el |> Result.bind (fun () -> read el))

    /// Decode a query declaration under a read policy, answering a typed refusal (Phase 295; Phase
    /// 310's convention): its code, the path to the value at fault, and `decodeWith`'s sentence.
    /// `Strict` refuses an unknown member of the declaration, its parameters, its result columns or
    /// its effect. A parse failure is refused at the root.
    let decodeDetailedWith (policy: ReadPolicy) (s: string) : Result<Query, DecodeError> =
        readWith policy strictQuery queryOf s

    /// `decodeResult` under a read policy, answering a typed refusal (Phase 295).
    let decodeResultDetailedWith (policy: ReadPolicy) (s: string) : Result<QueryResult, DecodeError> =
        readWith policy strictResult resultOf s

    /// `decodeDeferredResult` under a read policy, answering a typed refusal (Phase 295).
    let decodeDeferredResultDetailedWith (policy: ReadPolicy) (s: string) : Result<Deferred<QueryResult>, DecodeError> =
        readWith policy strictDeferredResult deferredResultOf s

    /// Decode a query declaration — `Result`-typed with a named error, as `CapabilityCodec.decode`
    /// (Phase 295: the sentence of `decodeDetailedWith`'s refusal; it was an `ExecutionFailed` before).
    let decode (s: string) : Result<Query, string> =
        decodeDetailedWith ReadPolicy.Lenient s |> Result.mapError DecodeError.describe

    /// Decode a `QueryResult`, `Result`-typed with a named error (Phase 295).
    let decodeResult (s: string) : Result<QueryResult, string> =
        decodeResultDetailedWith ReadPolicy.Lenient s
        |> Result.mapError DecodeError.describe

    /// Decode a `Deferred<QueryResult>`, `Result`-typed with a named error (Phase 295).
    let decodeDeferredResult (s: string) : Result<Deferred<QueryResult>, string> =
        decodeDeferredResultDetailedWith ReadPolicy.Lenient s
        |> Result.mapError DecodeError.describe

    /// `decode` under a read policy: `Strict` refuses an unknown member of the declaration, its
    /// parameters, its result columns or its effect.
    let decodeWith (policy: ReadPolicy) (s: string) : Result<Query, string> =
        decodeDetailedWith policy s |> Result.mapError DecodeError.describe

    /// `decodeResult` under a read policy.
    let decodeResultWith (policy: ReadPolicy) (s: string) : Result<QueryResult, string> =
        decodeResultDetailedWith policy s |> Result.mapError DecodeError.describe

    /// `decodeDeferredResult` under a read policy: `Strict` refuses an unknown member of the
    /// envelope or of the result it carries.
    let decodeDeferredResultWith (policy: ReadPolicy) (s: string) : Result<Deferred<QueryResult>, string> =
        decodeDeferredResultDetailedWith policy s
        |> Result.mapError DecodeError.describe
