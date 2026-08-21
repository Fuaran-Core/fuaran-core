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
//  The async envelope (Phase 32 `Deferred`) is not yet shipped, so the Core
//  boundary returns a plain `Result`; the host wraps it in its own async at the
//  resolver boundary. See the fuaran-side consumer (fuaran#323).
// ============================================================================

/// A typed parameter a query expects at invocation — the data-acquisition analogue of a
/// `Capability` hole. Typed by a `ColumnType` (the scalar set the result columns also use), so a
/// bound value's `Cell` shape must agree with `Type` (or be `Null` when not required).
type QueryParam =
    { Name: string
      Type: ColumnType
      Required: bool }

/// A query declaration: a named, registrable data-acquisition contract. Pure data — it carries no
/// host code. `Effect` reuses `Function`'s two-axis `EffectClass` verbatim (`Deterministic` =
/// pure relational/synthetic, re-evaluable; `Network`/`Clock` = captured result replayed). `Source`
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

/// The data-acquisition surface: typed registry (populate + enumerate + dispatch), the
/// param-validation contract, the Phase 27 capture keying. Additive over `Column`/`Function`;
/// FSharp.Core-only, Fable-clean.
module Query =

    /// The `ColumnType` a present (non-`Null`) cell realizes — the type-compatibility surface the
    /// param validator (and the fuaran#323 binding thread) checks against.
    let cellType (c: Cell) : ColumnType option =
        match c with
        | Int _ -> Some IntType
        | Float _ -> Some FloatType
        | Bool _ -> Some BoolType
        | Str _ -> Some StringType
        | Date _ -> Some DateType
        | Timestamp _ -> Some TimestampType
        | Null -> None

    /// The Phase 27 determinism label this query keys its captures on (`"deterministic"` /
    /// `"clock"` / `"random"` / `"network"`).
    let determinismTag (q: Query) : string =
        Effect.determinismTag q.Effect.Determinism

    /// A canonical scalar rendering of a bound `Cell`, for the capture key (a `Null` is `"∅"`).
    let private cellKey (c: Cell) : string =
        match c with
        | Int v -> "i" + string v
        | Float v -> "f" + Canon.canonicalFloat v
        | Bool v -> "b" + (if v then "1" else "0")
        | Str v -> "s" + v
        | Date v -> "d" + v
        | Timestamp v -> "t" + v
        | Null -> "∅"

    /// The effect-identity key the Phase 27 capture seam journals a non-deterministic query under:
    /// the query id + a hash of the canonical (name-sorted) param string. Same args replay the same
    /// captured rows; different args do not collide. A consumer threads this as
    /// `OpStream.captureEffect`'s `eff` argument (the `Capability.invocationKey` pattern).
    let invocationKey (q: Query) (args: (string * Cell) list) : string =
        let canonical =
            args
            |> List.sortBy fst
            |> List.map (fun (n, v) -> n + "=" + cellKey v)
            |> String.concat ""

        q.Id + "#" + Hash.fnv1a canonical

    /// Validate typed `args` (name -> bound `Cell`) against the query's declared params *before* any
    /// fetch: every arg must address a declared param and its cell type must match (or be `Null`);
    /// every required param must be bound. Default-deny by shape (FGP 3) — the host validates this
    /// before running any resolver.
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
                    | Some t when t = p.Type -> checkArgs rest
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

    /// Invoke a query: validate the params, then run the host `resolve` (which performs the actual
    /// fetch per the query's source + effect/placement). A resolver failure is a named
    /// `ExecutionFailed`, never a throw. Deterministic queries re-evaluate freely; for a
    /// non-`Deterministic` query the caller journals the realized result via `OpStream.captureEffect`
    /// keyed by `invocationKey` + `determinismTag` (Phase 27), so the query replays exactly.
    let invoke
        (q: Query)
        (args: (string * Cell) list)
        (resolve: Query -> Result<QueryResult, string>)
        : Result<QueryResult, QueryError> =
        validateParams q args
        |> Result.bind (fun () ->
            match resolve q with
            | Ok r -> Ok r
            | Error m -> Error(ExecutionFailed(m, [])))

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
    /// resolved query's source + placement.
    let dispatch
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> Result<QueryResult, string>)
        : Result<QueryResult, QueryError> =
        match Map.tryFind id r.Queries with
        | None -> Error(NoSuchQuery(id, r.Queries |> Map.toList |> List.map fst))
        | Some q -> Query.invoke q args resolve

/// The canonical wire codec for a `Query` declaration + a `QueryResult`. Round-trips the typed
/// params, the result schema, the effect class, the source (via `ColumnCodec`), and the paging
/// fields. Fable-clean (`Json` / `Decode`).
module QueryCodec =

    // ---- column type ----

    let private colTypeStr =
        function
        | IntType -> "int"
        | FloatType -> "float"
        | BoolType -> "bool"
        | StringType -> "string"
        | DateType -> "date"
        | TimestampType -> "timestamp"

    let private colTypeOf =
        function
        | "int" -> Ok IntType
        | "float" -> Ok FloatType
        | "bool" -> Ok BoolType
        | "string" -> Ok StringType
        | "date" -> Ok DateType
        | "timestamp" -> Ok TimestampType
        | other -> Error("unknown column type: " + other)

    // ---- effect class ----

    let private hostStr =
        function
        | Pure -> "pure"
        | ReadsHost -> "readsHost"
        | WritesHost -> "writesHost"

    let private hostOf =
        function
        | "pure" -> Ok Pure
        | "readsHost" -> Ok ReadsHost
        | "writesHost" -> Ok WritesHost
        | other -> Error("unknown host effect: " + other)

    let private detStr =
        function
        | Deterministic -> "deterministic"
        | Clock -> "clock"
        | Random -> "random"
        | Network -> "network"

    let private detOf =
        function
        | "deterministic" -> Ok Deterministic
        | "clock" -> Ok Clock
        | "random" -> Ok Random
        | "network" -> Ok Network
        | other -> Error("unknown determinism source: " + other)

    let private effectJson (e: EffectClass) : JVal =
        JObj [ "host", JStr(hostStr e.Host); "determinism", JStr(detStr e.Determinism) ]

    let private effectOf (el: JVal) : Result<EffectClass, string> =
        Decode.strField "host" el
        |> Result.bind hostOf
        |> Result.bind (fun h ->
            Decode.strField "determinism" el
            |> Result.bind detOf
            |> Result.map (fun d -> { Host = h; Determinism = d }))

    // ---- param ----

    let private paramJson (p: QueryParam) : JVal =
        JObj
            [ "name", JStr p.Name
              "type", JStr(colTypeStr p.Type)
              "required", JBool p.Required ]

    let private asBool (el: JVal) : Result<bool, string> =
        match el with
        | JBool b -> Ok b
        | _ -> Error "expected a bool"

    let private paramOf (el: JVal) : Result<QueryParam, string> =
        Decode.strField "name" el
        |> Result.bind (fun name ->
            Decode.strField "type" el
            |> Result.bind colTypeOf
            |> Result.bind (fun ty ->
                Decode.getProp "required" el
                |> Result.bind asBool
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

    let private schemaOf (el: JVal) : Result<Schema, string> =
        Decode.mapList
            (fun e ->
                Decode.strField "name" e
                |> Result.bind (fun n ->
                    Decode.strField "type" e |> Result.bind colTypeOf |> Result.map (fun t -> n, t)))
            el

    // ---- optional int ----

    let private optIntJson (name: string) (v: int option) : (string * JVal) list =
        match v with
        | Some n -> [ name, JInt n ]
        | None -> []

    let private optIntOf (name: string) (el: JVal) : Result<int option, string> =
        match Decode.getProp name el with
        | Ok j -> Decode.asInt j |> Result.map Some
        | Error _ -> Ok None

    // ---- query declaration ----

    let queryJson (q: Query) : JVal =
        JObj(
            [ "$type", JStr "query"
              "id", JStr q.Id
              "params", JArr(q.Params |> List.map paramJson)
              "resultSchema", schemaJson q.ResultSchema
              "effect", effectJson q.Effect
              "source", ColumnCodec.encodeJson q.Source ]
            @ optIntJson "timeoutMs" q.TimeoutMs
            @ optIntJson "pageSize" q.PageSize
        )

    let encode (q: Query) : string = Canon.render (queryJson q)

    let queryOf (el: JVal) : Result<Query, QueryError> =
        let dataErr (s: string) = ExecutionFailed("decode: " + s, [])

        let r =
            Decode.strField "id" el
            |> Result.bind (fun id ->
                Decode.getProp "params" el
                |> Result.bind (Decode.mapList paramOf)
                |> Result.bind (fun ps ->
                    Decode.getProp "resultSchema" el
                    |> Result.bind schemaOf
                    |> Result.bind (fun sch ->
                        Decode.getProp "effect" el
                        |> Result.bind effectOf
                        |> Result.bind (fun eff ->
                            optIntOf "timeoutMs" el
                            |> Result.bind (fun tmo ->
                                optIntOf "pageSize" el |> Result.map (fun pg -> id, ps, sch, eff, tmo, pg))))))

        match r with
        | Error m -> Error(dataErr m)
        | Ok(id, ps, sch, eff, tmo, pg) ->
            match Decode.getProp "source" el with
            | Error m -> Error(dataErr m)
            | Ok srcEl ->
                match ColumnCodec.decodeJson srcEl with
                | Error _ -> Error(dataErr "source")
                | Ok src ->
                    Ok
                        { Id = id
                          Params = ps
                          ResultSchema = sch
                          Effect = eff
                          Source = src
                          TimeoutMs = tmo
                          PageSize = pg }

    let decode (s: string) : Result<Query, QueryError> =
        match Decode.parse s with
        | Error m -> Error(ExecutionFailed("parse: " + m, []))
        | Ok el -> queryOf el

    // ---- query result ----

    let resultJson (qr: QueryResult) : JVal =
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

    let resultOf (el: JVal) : Result<QueryResult, QueryError> =
        let dataErr (s: string) = ExecutionFailed("decode: " + s, [])

        match Decode.getProp "rows" el with
        | Error m -> Error(dataErr m)
        | Ok rowsEl ->
            match ColumnCodec.decodeJson rowsEl with
            | Error _ -> Error(dataErr "rows")
            | Ok(Embedded t) ->
                let parts =
                    Decode.intField "pageNum" el
                    |> Result.bind (fun pn -> optIntOf "totalRowCount" el |> Result.map (fun trc -> pn, trc))

                match parts with
                | Error m -> Error(dataErr m)
                | Ok(pn, trc) ->
                    let tok =
                        match Decode.strField "nextPageToken" el with
                        | Ok s -> Some s
                        | Error _ -> None

                    Ok
                        { Rows = t
                          PageNum = pn
                          TotalRowCount = trc
                          NextPageToken = tok }
            | Ok(Ref _) -> Error(dataErr "rows must be embedded, not a ref")

    let decodeResult (s: string) : Result<QueryResult, QueryError> =
        match Decode.parse s with
        | Error m -> Error(ExecutionFailed("parse: " + m, []))
        | Ok el -> resultOf el
