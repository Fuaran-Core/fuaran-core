namespace Fuaran.Core

/// A typed query registry — the discovery surface an agent enumerates (the data-acquisition analogue
/// of node-introspection / capability discovery): "what data may I acquire, with what typed params,
/// producing what schema". Default-deny by shape on dispatch — only a registered id resolves.
///
/// OPAQUE since `0.37.0` (Phase 386), with `FunctionRegistry`'s shape (Phase 316): the map and the
/// policy are reachable only through this module's verbs, so `register` — and its admission gate —
/// is the only way a query gets in. Since Phase 509 it is a `Seam` over `Query`, in this seam's
/// vocabulary, and every verb below forwards to it.
type QueryRegistry =
    private
        {
            /// The registered queries keyed by `Query.Id` — each admitted by `register` — and the
            /// gates every dispatch runs after the parameters validate and before the resolver, with
            /// the observers a refusal reaches (Phase 318).
            Seam: Seam<Query, (string * Cell) list, QueryError>
        }

/// Building, enumerating and dispatching through a `QueryRegistry`.
module QueryRegistry =

    /// The registration refusal a declaration earns, or `None` (Phase 316): a declaration naming a
    /// parameter twice is refused `DuplicateParam`, naming the first repeated name — the query seam's
    /// admission gate, as `IllFormedCapability` is the capability seam's. An invocation could never
    /// bind such a declaration's repeated name sensibly (`validateParams` refuses the second binding),
    /// and `proofs/Query.fst` states its exact all-`Null` refusal over distinct names, so registration
    /// is where the premise is discharged rather than assumed (`register_refuses_duplicate_params`).
    /// Since Phase 385 it is THE gate of the seam (D111): `register`, `replace` and
    /// `QueryCodec`'s declaration reader all run this one function, so a reader never admits what
    /// the registry refuses.
    ///
    /// Since Phase 398 the gate also holds the declaration's `Where` and `OrderBy` to its
    /// `ResultSchema` (`QueryShape.whereFault` / `orderFault`): a predicate or an order key naming an
    /// undeclared column, `contains` on a column that is not a string, a literal of another type than
    /// its column's or one its column cannot carry, and an order naming a column twice are refused by
    /// name. The parameters are checked first, then the filter, then the order. `admissionFaultAt`
    /// answers the same refusal with the path to the member at fault, which the declaration reader
    /// reports it at.
    let internal admissionFaultAt (q: Query) : (PathSegment list * QueryError) option =
        match Capability.repeatedAddrs (q.Params |> List.map (fun p -> p.Name, ())) with
        | dup :: _ -> Some([ PathSegment.Key "params" ], DuplicateParam dup)
        | [] ->
            match QueryShape.whereFault q.ResultSchema q.Where with
            | Some(i, e) -> Some([ PathSegment.Key "where"; PathSegment.Index i ], e)
            | None ->
                QueryShape.orderFault q.ResultSchema q.OrderBy
                |> Option.map (fun (i, e) -> [ PathSegment.Key "orderBy"; PathSegment.Index i ], e)

    /// The registration refusal a declaration earns, or `None` — `admissionFaultAt` without the path.
    let internal admissionFault (q: Query) : QueryError option = admissionFaultAt q |> Option.map snd

    /// The query seam's vocabulary (Phase 509): `NoSuchQuery` / `DuplicateQuery`, `admissionFault`
    /// as the registration gate, `Query.validateParams` before the policy, and `QueryPolicyRefused` /
    /// `QueryApprovalRequired` for its gates.
    let private vocabulary: SeamVocabulary<Query, (string * Cell) list, QueryError> =
        { Id = _.Id
          NoSuch = fun id known -> NoSuchQuery(id, known)
          Duplicate = DuplicateQuery
          Admission = admissionFault
          Validate = Query.validateParams
          Refused = fun policy g -> QueryPolicyRefused(policy, g.Message, g.Alternatives)
          Approval = QueryApprovalRequired
          Describe = QueryError.describe }

    /// The registry with no queries: every dispatch against it is `NoSuchQuery` with an empty
    /// `known` list.
    let empty: QueryRegistry = { Seam = Seam.create vocabulary }

    let private lift (r: Result<Seam<Query, (string * Cell) list, QueryError>, QueryError>) =
        r |> Result.map (fun s -> { Seam = s })

    /// Register a query — additive, no silent overwrite (a duplicate id is a named error), and,
    /// since Phase 316, only a declaration whose parameter names are distinct (`DuplicateParam`
    /// otherwise, naming the repeated name). The id is checked first.
    let register (q: Query) (r: QueryRegistry) : Result<QueryRegistry, QueryError> = Seam.register q r.Seam |> lift

    /// The query registered under `id`, or `None`. A bare lookup: unlike `dispatch` it
    /// validates nothing and names no alternatives.
    let tryFind (id: string) (r: QueryRegistry) : Query option = Seam.tryFind id r.Seam

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part of
    /// the contract (`queryLaws` certifies it).
    let enumerate (r: QueryRegistry) : Query list = Seam.enumerate r.Seam

    // ---- the policy gate (Phase 318) ----

    /// `r` with `gate` added to the gates every dispatch runs (`RegistryPolicy.withGate`): after the
    /// parameters validate, before the resolver. The decision is the join over every gate, so adding
    /// one can only refuse more.
    let withGate (gate: PolicyGate<Query, (string * Cell) list>) (r: QueryRegistry) : QueryRegistry =
        { Seam = Seam.withGate gate r.Seam }

    /// `r` with `observe` told of every invocation its policy refuses, before the refusal returns.
    let onDenied (observe: PolicyDenial<(string * Cell) list> -> unit) (r: QueryRegistry) : QueryRegistry =
        { Seam = Seam.onDenied observe r.Seam }

    /// What the registry would decide for an invocation without dispatching it: an unknown id is a
    /// `Deny` naming the registered ids as alternatives, parameters that do not validate a `Deny`
    /// with the refusal's sentence, otherwise the gates' join. No observer is told.
    let decide (r: QueryRegistry) (id: string) (args: (string * Cell) list) : PolicyDecision =
        Seam.decide r.Seam id args

    /// Dispatch an invocation through the registry: resolve the id (default-deny — an unregistered id
    /// is `NoSuchQuery`), validate the parameters, run the policy's gates (Phase 318 —
    /// `QueryPolicyRefused` / `QueryApprovalRequired`, reaching every `onDenied` observer), then the
    /// resolver; with no gate this is `Query.invoke` exactly. The host resolver is supplied by the
    /// caller per the resolved query's source + placement, and answers in the `Deferred` envelope — so the same
    /// three outcomes `Query.invoke` documents are what a registry dispatch returns.
    let dispatch
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        Seam.dispatch r.Seam id args (fun q -> Query.invoke q args resolve)

    /// `dispatch`, with the resolver handed the resolved query and the validated argument list
    /// (`Query.invokeWithArgs`, Phase 251). Additive beside `dispatch`; default-deny the same.
    let dispatchWithArgs
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        : Result<Deferred<QueryResult>, QueryError> =
        Seam.dispatch r.Seam id args (fun q -> Query.invokeWithArgs q args resolve)

    /// `dispatch` for ONE PAGE (Phase 316): resolve the id (default-deny), then `Query.invokePage`,
    /// handing the resolver the page token. `dispatch r id args resolve` is
    /// `dispatchPage r id args None (fun q _ -> resolve q)`.
    let dispatchPage
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        Seam.dispatch r.Seam id args (fun q -> Query.invokePage q args pageToken resolve)

    /// `dispatchPage` with the typed resolver (Phase 385): resolve the id (default-deny), validate,
    /// admit through the policy, then `Query.invokePageWithArgs` — so a resolver answering a
    /// `ResolveFault` pages through the registry. `dispatchWithArgs r id args resolve` is
    /// `dispatchPageWith r id args None (fun q _ typed -> resolve q typed)`.
    let dispatchPageWith
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        : Result<Deferred<QueryResult>, QueryError> =
        Seam.dispatch r.Seam id args (fun q -> Query.invokePageWithArgs q args pageToken resolve)

    // ---- the lifecycle (Phase 316): a registry is a lattice, not an append log ----

    /// Remove the query registered under `id` — refused `NoSuchQuery(id, known)` when the registry
    /// does not hold it, naming every id it does hold. Removing a query just registered gives back
    /// the registry it was registered into.
    let unregister (id: string) (r: QueryRegistry) : Result<QueryRegistry, QueryError> =
        Seam.unregister id r.Seam |> lift

    /// Swap the query registered under `q.Id` for `q` — the hot-reload verb. Refused `NoSuchQuery`
    /// when the id is not registered, and held to the admission `register` runs (`DuplicateParam`);
    /// on a refusal the registry is unchanged.
    let replace (q: Query) (r: QueryRegistry) : Result<QueryRegistry, QueryError> = Seam.replace q r.Seam |> lift

    /// The registry narrowed to the ids in `keep` — a session- or actor-scoped default-deny. An id
    /// in `keep` the registry does not hold is ignored, so the result enumerates a subset of what `r`
    /// enumerates.
    let restrict (keep: Set<string>) (r: QueryRegistry) : QueryRegistry = { Seam = Seam.restrict keep r.Seam }

    /// The join of two registries whose ids are disjoint — refused `DuplicateQuery` naming the first
    /// id, in id order, that both hold (no silent overwrite, as `register`). Associative. The union
    /// runs both registries' gates (Phase 318, `RegistryPolicy.combine`).
    let union (a: QueryRegistry) (b: QueryRegistry) : Result<QueryRegistry, QueryError> =
        Seam.union a.Seam b.Seam |> lift

    // ---- invocation-keyed capture (Phase 318) ----
    // Since Phase 385 a refusal the resolver answered is journalled as its `QueryError` wire document
    // (`QueryErrorWire`) and replayed back as that error, so a replayed refusal is the refusal that
    // was answered live — a `Timeout` replays as `Timeout`, an `ExecutionFailed` with its
    // `recoverable` arguments — exactly as a `Ready` result replays as itself. A reason that is not a
    // query-error document (a journal written before 0.36.0 recorded `QueryError.describe`'s
    // sentence) replays as `ExecutionFailed(reason, [])`, as it always did.

    /// How the query seam journals one invocation (Phase 509; the shape since Phase 318): under the
    /// query's determinism label and `key`, a resolver's refusal recorded as its wire document and
    /// read back as the `QueryError` it records.
    let private capture
        (key: Query -> (string * Cell) list -> string)
        : SeamCapture<Query, (string * Cell) list, QueryError> =
        { Label = Query.determinismTag
          Key = key
          Record = QueryErrorWire.render
          Read =
            fun reason ->
                match QueryErrorWire.ofReason reason with
                | Some e -> e
                | None -> ExecutionFailed(reason, []) }

    /// Journalled under `Query.invocationKey`.
    let private unpaged = capture Query.invocationKey

    /// Journalled under `Query.invocationKeyPage` for the page `pageToken`.
    let private paged (pageToken: string option) =
        capture (fun q args -> Query.invocationKeyPage q args pageToken)

    /// `dispatch`, journalling the invocation in the KEYED capture journal under
    /// `Query.invocationKey` (Phase 318): the attempt after the id resolved, the parameters validated
    /// and the policy admitted it, before the resolver runs; a `Ready` result settles it `Completed`
    /// (through `encode`, `QueryCodec.encodeResult` for the canonical form), a refusal from the
    /// resolver settles it `Refused` with the refusal's wire document (`QueryCodec.encodeQueryError`;
    /// `QueryError.describe`'s sentence before Phase 385), and `Pending` leaves it open with the
    /// returned `Ticket`. A refusal before the resolver journals nothing; a deterministic query
    /// journals nothing. The `Outcome` is exactly `dispatch`'s; a `CapturedDispatch` since Phase 391
    /// (a positional triple before `0.37.0`).
    let dispatchCaptured
        (hashFn: HashFn)
        (encode: QueryResult -> string)
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> Deferred<QueryResult>)
        (journal: KeyedCapture list)
        : CapturedDispatch<QueryResult, QueryError> =
        Seam.dispatchCaptured hashFn encode unpaged r.Seam id args (fun q -> Query.invoke q args resolve) journal

    /// `dispatchPage`, journalled under `Query.invocationKeyPage` (Phase 318), so every page of a
    /// non-deterministic query has its own capture and replays from it.
    let dispatchPageCaptured
        (hashFn: HashFn)
        (encode: QueryResult -> string)
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> Deferred<QueryResult>)
        (journal: KeyedCapture list)
        : CapturedDispatch<QueryResult, QueryError> =
        Seam.dispatchCaptured
            hashFn
            encode
            (paged pageToken)
            r.Seam
            id
            args
            (fun q -> Query.invokePage q args pageToken resolve)
            journal

    /// `dispatchCaptured` with the typed resolver (Phase 385): `dispatchWithArgs`, journalled under
    /// `Query.invocationKey`, so a `ResolveFault` is captured as the refusal it names
    /// (`SourceNotResolved`, `Timeout`, `ExecutionFailed` with its `recoverable`) and
    /// `dispatchReplayedWith` answers it back unchanged. The result is exactly `dispatchWithArgs`'s.
    let dispatchCapturedWith
        (hashFn: HashFn)
        (encode: QueryResult -> string)
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        (journal: KeyedCapture list)
        : CapturedDispatch<QueryResult, QueryError> =
        Seam.dispatchCaptured
            hashFn
            encode
            unpaged
            r.Seam
            id
            args
            (fun q -> Query.invokeWithArgs q args resolve)
            journal

    /// `dispatchPageCaptured` with the typed resolver (Phase 385): `dispatchPageWith`, journalled
    /// under `Query.invocationKeyPage`, so each page's answer — a result or a `ResolveFault` — is
    /// captured under that page's key. The result is exactly `dispatchPageWith`'s.
    let dispatchPageCapturedWith
        (hashFn: HashFn)
        (encode: QueryResult -> string)
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        (journal: KeyedCapture list)
        : CapturedDispatch<QueryResult, QueryError> =
        Seam.dispatchCaptured
            hashFn
            encode
            (paged pageToken)
            r.Seam
            id
            args
            (fun q -> Query.invokePageWithArgs q args pageToken resolve)
            journal

    /// REPLAY a query invocation from the keyed journal instead of resolving it (Phase 318). The id,
    /// the parameters and the policy run exactly as `dispatchPage` (a refusal there is answered as
    /// `dispatchPage` answers it, consulting no journal); then a non-deterministic query is answered by
    /// its invocation key — `invocationKeyPage`, which for `None` is `invocationKey` — a completion as
    /// `Ready` (through `decode`, `QueryCodec.decodeResult` for the canonical form), a recorded
    /// refusal as the `QueryError` it records (Phase 385; a reason that is no query-error document,
    /// from a journal written before 0.36.0, as `ExecutionFailed` with the recorded text), an
    /// unsettled attempt as `Pending`. A deterministic query resolves live. A journal that cannot
    /// answer is `ReplayFailure.Unanswered` with the `KeyedCaptureFault`, never a live fetch; every
    /// refusal of the seam, live or recorded, is `ReplayFailure.Refused`. A `ReplayedDispatch` since
    /// Phase 391 (a nested `Result` before `0.37.0`).
    let dispatchReplayed
        (decode: string -> Result<QueryResult, string>)
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> Deferred<QueryResult>)
        (cursor: Map<string, int>)
        (journal: KeyedCapture list)
        : ReplayedDispatch<QueryResult, QueryError> =
        Seam.dispatchReplayed
            decode
            (paged pageToken)
            r.Seam
            id
            args
            (fun q -> Query.invokePage q args pageToken resolve)
            cursor
            journal

    /// `dispatchReplayed` with the typed resolver (Phase 385): the journal is read exactly as there,
    /// so a `ResolveFault` captured by `dispatchCapturedWith` / `dispatchPageCapturedWith` replays as
    /// the refusal it was answered live; a deterministic query resolves live through
    /// `Query.invokePageWithArgs`.
    let dispatchReplayedWith
        (decode: string -> Result<QueryResult, string>)
        (r: QueryRegistry)
        (id: string)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        (cursor: Map<string, int>)
        (journal: KeyedCapture list)
        : ReplayedDispatch<QueryResult, QueryError> =
        Seam.dispatchReplayed
            decode
            (paged pageToken)
            r.Seam
            id
            args
            (fun q -> Query.invokePageWithArgs q args pageToken resolve)
            cursor
            journal
