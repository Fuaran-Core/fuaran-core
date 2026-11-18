namespace Fuaran.Core

/// A typed query registry — the discovery surface an agent enumerates (the data-acquisition analogue
/// of node-introspection / capability discovery): "what data may I acquire, with what typed params,
/// producing what schema". Default-deny by shape on dispatch — only a registered id resolves.
///
/// OPAQUE since `0.37.0` (Phase 386), with `FunctionRegistry`'s shape (Phase 316): the map and the
/// policy are reachable only through this module's verbs, so `register` — and its admission gate —
/// is the only way a query gets in. Since Phase 509 it is a `Seam` over `Query`, in this seam's
/// vocabulary, and every verb below forwards to it. Since Phase 651 its gates decide under the
/// decision context `'Ctx`, as the capability registry's do (DECISIONS.md D261): a registry that
/// needs no context is a `QueryRegistry<unit>`.
type QueryRegistry<'Ctx> =
    private
        {
            /// The registered queries keyed by `Query.Id` — each admitted by `register` — and the
            /// gates every dispatch runs after the parameters validate and before the resolver, with
            /// the observers a refusal reaches (Phase 318).
            Seam: Seam<'Ctx, Query, (string * Cell) list, QueryError>
        }

/// Building, enumerating and dispatching through a `QueryRegistry`. `ModuleSuffix`, so the module
/// and the type share a name, as `CapabilityRegistry` does.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
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
    /// name. `admissionFaultAt` answers the same refusal with the path to the member at fault, which
    /// the declaration reader reports it at.
    ///
    /// Since Phase 688 (DECISIONS.md D266.9) the gate reads the source FIRST: an embedded table
    /// `Table.validate` refuses is refused `IllFormedSource` at `source`, with the column layer's
    /// sentence, so an admitted declaration's table is one the column codec writes and reads back. A
    /// `Ref` is not read. Then the parameters, then the filter, then the order.
    let internal admissionFaultAt (q: Query) : (PathSegment list * QueryError) option =
        let sourceFault =
            match q.Source with
            | Embedded t ->
                match Table.validate t with
                | Ok() -> None
                | Error e -> Some([ PathSegment.Key "source" ], IllFormedSource(ColumnCodec.errorString e))
            | Ref _ -> None

        match sourceFault with
        | Some fault -> Some fault
        | None ->
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

    /// THE GATE, answering the admitted declaration (Phase 685, DECISIONS.md D264.8 point 1):
    /// `admissionFaultAt`'s refusal with its path, or the declaration as an `AdmittedQuery`. The one
    /// place a declaration becomes an admitted one; `admit` and the declaration reader answer it.
    let internal admitAt (q: Query) : Result<AdmittedQuery, PathSegment list * QueryError> =
        match admissionFaultAt q with
        | Some fault -> Error fault
        | None -> Ok(Admitted q)

    /// The admission gate (Phase 685): the declaration as an `AdmittedQuery` when the gate admits it,
    /// or the refusal `register` would answer for it (an embedded table the column layer refuses,
    /// Phase 688, then a parameter named twice, then the filter, then the order). What a host runs before it writes a declaration (`QueryCodec.encode`) or keys a
    /// read of it (`Query.invocationKey`, `invocationPreimage`, `grant`). It reads the declaration's
    /// own fields and nothing of a registry, so a declaration is admitted wherever it is admitted.
    let admit (q: Query) : Result<AdmittedQuery, QueryError> = admitAt q |> Result.mapError snd

    /// The query seam's vocabulary (Phase 509): `NoSuchQuery` / `DuplicateQuery`, `admissionFault`
    /// as the registration gate, `Query.validateParams` before the policy, and `QueryPolicyRefused` /
    /// `QueryApprovalRequired` for its gates. `Invocation` is `Query.invocationPreimage` (Phase 638;
    /// named and public since Phase 651): the identity a presented `Grant` is compared with. A
    /// declaration the seam holds is one `register` or `replace` admitted, so it is handed to the
    /// identity as the `AdmittedQuery` it is (Phase 685), as the journal keys below are.
    let private vocabulary: SeamVocabulary<Query, (string * Cell) list, QueryError> =
        { Id = _.Id
          NoSuch = fun id known -> NoSuchQuery(id, known)
          Duplicate = DuplicateQuery
          Admission = admissionFault
          Validate = Query.validateParams
          Refused = fun policy g -> QueryPolicyRefused(policy, g.Message, g.Alternatives)
          Approval = QueryApprovalRequired
          Invocation = fun q args -> Query.invocationPreimage (Admitted q) args
          Describe = QueryError.describe }

    /// The registry with no queries: every dispatch against it is `NoSuchQuery` with an empty
    /// `known` list.
    let empty<'Ctx> : QueryRegistry<'Ctx> = { Seam = Seam.create vocabulary }

    let private lift (r: Result<Seam<'Ctx, Query, (string * Cell) list, QueryError>, QueryError>) =
        r |> Result.map (fun s -> { Seam = s })

    /// Register a query — additive, no silent overwrite (a duplicate id is a named error), and,
    /// since Phase 316, only a declaration whose parameter names are distinct (`DuplicateParam`
    /// otherwise, naming the repeated name). The id is checked first, then the admission gate
    /// (`admit`), whose first clause since Phase 688 refuses an embedded table the column layer
    /// refuses (`IllFormedSource`).
    let register (q: Query) (r: QueryRegistry<'Ctx>) : Result<QueryRegistry<'Ctx>, QueryError> =
        Seam.register q r.Seam |> lift

    /// The query registered under `id`, or `None`. A bare lookup: unlike `dispatch` it
    /// validates nothing and names no alternatives. Since Phase 685 it answers an `AdmittedQuery`:
    /// `register` and `replace` hold only declarations the gate admitted.
    let tryFind (id: string) (r: QueryRegistry<'Ctx>) : AdmittedQuery option =
        Seam.tryFind id r.Seam |> Option.map Admitted

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part of
    /// the contract (`queryLaws` certifies it). Each an `AdmittedQuery` since Phase 685.
    let enumerate (r: QueryRegistry<'Ctx>) : AdmittedQuery list =
        Seam.enumerate r.Seam |> List.map Admitted

    // ---- the policy gate (Phase 318) ----

    /// `r` with `gate` added to the gates every dispatch runs (`RegistryPolicy.withGate`): after the
    /// parameters validate, before the resolver. The decision is the join over every gate, so adding
    /// one can only refuse more: a read `r` refused under a context, the result refuses under it. A
    /// gate that reads no context is `PolicyGate.ignoringContext`.
    let withGate (gate: PolicyGate<'Ctx, Query, (string * Cell) list>) (r: QueryRegistry<'Ctx>) : QueryRegistry<'Ctx> =
        { Seam = Seam.withGate gate r.Seam }

    /// `r` with `observe` told of every invocation its policy refuses, before the refusal returns,
    /// with the context it was refused under.
    let onDenied
        (observe: PolicyDenial<'Ctx, (string * Cell) list> -> unit)
        (r: QueryRegistry<'Ctx>)
        : QueryRegistry<'Ctx> =
        { Seam = Seam.onDenied observe r.Seam }

    /// `r` as the one context `ctx` sees it (Phase 651, as `CapabilityRegistry.under`): the same
    /// queries, every gate deciding as it does under `ctx`. The result takes the `unit` context and
    /// decides and dispatches exactly as `r` does under `ctx` — the registry a conformance witness
    /// is handed, and the view a host gives a caller that must not choose its own context.
    let under (ctx: 'Ctx) (r: QueryRegistry<'Ctx>) : QueryRegistry<unit> = { Seam = Seam.under ctx r.Seam }

    /// What the registry would decide for an invocation without dispatching it: an unknown id is a
    /// `Deny` naming the registered ids as alternatives, parameters that do not validate a `Deny`
    /// with the refusal's sentence, otherwise the gates' join under `ctx`, with a `NeedsApproval`
    /// lifted to `Allow` when `grant` is for exactly this read (Phase 651: `Query.grant`). A `Deny`
    /// is lifted by no grant and an `Allow` is changed by none. No observer is told.
    let decide
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
        (id: string)
        (args: (string * Cell) list)
        : PolicyDecision =
        Seam.decide r.Seam ctx grant id args

    /// The registry's NAMED decision for an invocation, read without dispatching it (Phase 650; under
    /// a context and with a grant since Phase 651): `Ok` of the name of the gate that made the
    /// decision and the decision `decide` answers — the first gate of the most restrictive rank,
    /// `""` when every gate allows, and kept when `grant` lifts a `NeedsApproval` — or, where no gate
    /// decided, the refusal `dispatch` gives: `NoSuchQuery` naming the registered ids for an
    /// unregistered id, the validation refusal for parameters that do not validate. No observer is
    /// told.
    ///
    /// **Advisory.** It is computed by the one function a dispatch judges by, so `dispatch` refuses
    /// `QueryPolicyRefused(name, …)` for `Ok(name, Deny guidance)` and `QueryApprovalRequired name`
    /// for `Ok(name, NeedsApproval)`, and runs the resolver for `Ok(_, Allow)`. But `dispatch`
    /// decides again and is authoritative.
    let decideNamed
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
        (id: string)
        (args: (string * Cell) list)
        : Result<string * PolicyDecision, QueryError> =
        Seam.decideNamed r.Seam ctx grant id args

    /// Dispatch an invocation through the registry: resolve the id (default-deny — an unregistered id
    /// is `NoSuchQuery`, whatever is presented), validate the parameters, run the policy's gates under
    /// `ctx` (Phase 318 — a `Deny` is `QueryPolicyRefused`, whatever is presented; a `NeedsApproval`
    /// is `QueryApprovalRequired` unless `grant` is for exactly this read, Phase 651; a refusal
    /// reaches every `onDenied` observer), then the resolver; with no gate this is `Query.invoke`
    /// exactly, under every context and grant. The host resolver is supplied by the
    /// caller per the resolved query's source + placement, and answers in the `Deferred` envelope — so the same
    /// three outcomes `Query.invoke` documents are what a registry dispatch returns.
    let dispatch
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        Seam.dispatch r.Seam ctx grant id args (fun q -> Query.invoke q args resolve)

    /// `dispatch`, with the resolver handed the resolved query and the validated argument list
    /// (`Query.invokeWithArgs`, Phase 251). Additive beside `dispatch`; default-deny the same.
    let dispatchWithArgs
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        : Result<Deferred<QueryResult>, QueryError> =
        Seam.dispatch r.Seam ctx grant id args (fun q -> Query.invokeWithArgs q args resolve)

    /// `dispatch` for ONE PAGE (Phase 316): resolve the id (default-deny), then `Query.invokePage`,
    /// handing the resolver the page token. `dispatch r ctx grant id args resolve` is
    /// `dispatchPage r ctx grant id args None (fun q _ -> resolve q)`. The token is not in what a
    /// gate or a grant reads, so the grant for a read covers every page of it (DECISIONS.md D261.4).
    let dispatchPage
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
        (id: string)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> Deferred<QueryResult>)
        : Result<Deferred<QueryResult>, QueryError> =
        Seam.dispatch r.Seam ctx grant id args (fun q -> Query.invokePage q args pageToken resolve)

    /// `dispatchPage` with the typed resolver (Phase 385): resolve the id (default-deny), validate,
    /// admit through the policy, then `Query.invokePageWithArgs` — so a resolver answering a
    /// `ResolveFault` pages through the registry. `dispatchWithArgs r ctx grant id args resolve` is
    /// `dispatchPageWith r ctx grant id args None (fun q _ typed -> resolve q typed)`.
    let dispatchPageWith
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
        (id: string)
        (args: (string * Cell) list)
        (pageToken: string option)
        (resolve: Query -> string option -> (string * Cell) list -> Result<Deferred<QueryResult>, ResolveFault>)
        : Result<Deferred<QueryResult>, QueryError> =
        Seam.dispatch r.Seam ctx grant id args (fun q -> Query.invokePageWithArgs q args pageToken resolve)

    // ---- the lifecycle (Phase 316): a registry is a lattice, not an append log ----

    /// Remove the query registered under `id` — refused `NoSuchQuery(id, known)` when the registry
    /// does not hold it, naming every id it does hold. Removing a query just registered gives back
    /// the registry it was registered into.
    let unregister (id: string) (r: QueryRegistry<'Ctx>) : Result<QueryRegistry<'Ctx>, QueryError> =
        Seam.unregister id r.Seam |> lift

    /// Swap the query registered under `q.Id` for `q` — the hot-reload verb. Refused `NoSuchQuery`
    /// when the id is not registered, and held to the admission `register` runs (`IllFormedSource`,
    /// `DuplicateParam`, the filter and the order); on a refusal the registry is unchanged.
    let replace (q: Query) (r: QueryRegistry<'Ctx>) : Result<QueryRegistry<'Ctx>, QueryError> =
        Seam.replace q r.Seam |> lift

    /// The registry narrowed to the ids in `keep` — a session- or actor-scoped default-deny. An id
    /// in `keep` the registry does not hold is ignored, so the result enumerates a subset of what `r`
    /// enumerates.
    let restrict (keep: Set<string>) (r: QueryRegistry<'Ctx>) : QueryRegistry<'Ctx> =
        { Seam = Seam.restrict keep r.Seam }

    /// The join of two registries whose ids are disjoint — refused `DuplicateQuery` naming the first
    /// id, in id order, that both hold (no silent overwrite, as `register`). Associative. The union
    /// runs both registries' gates (Phase 318, `RegistryPolicy.combine`).
    let union (a: QueryRegistry<'Ctx>) (b: QueryRegistry<'Ctx>) : Result<QueryRegistry<'Ctx>, QueryError> =
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
    let private unpaged = capture (fun q args -> Query.invocationKey (Admitted q) args)

    /// Journalled under `Query.invocationKeyPage` for the page `pageToken`.
    let private paged (pageToken: string option) =
        capture (fun q args -> Query.invocationKeyPage (Admitted q) args pageToken)

    /// `dispatch`, journalling the invocation in the KEYED capture journal under
    /// `Query.invocationKey` (Phase 318): the attempt after the id resolved, the parameters validated
    /// and the policy admitted it, before the resolver runs; a `Ready` result settles it `Completed`
    /// (through `encode`, `QueryCodec.encodeResult` for the canonical form), a refusal from the
    /// resolver settles it `Refused` with the refusal's wire document (`QueryCodec.encodeQueryError`;
    /// `QueryError.describe`'s sentence before Phase 385), and `Pending` leaves it open with the
    /// returned `Ticket`. Rows `Table.validate` refuses are the resolver's fault, refused before
    /// anything is settled (Phase 692), so they are journalled as that refusal and never as a
    /// completion whose text reads back as other rows. A refusal before the resolver journals nothing; a deterministic query
    /// journals nothing. The `Outcome` is exactly `dispatch`'s; a `CapturedDispatch` since Phase 391
    /// (a positional triple before `0.37.0`).
    let dispatchCaptured
        (hashFn: HashFn)
        (encode: QueryResult -> string)
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
        (id: string)
        (args: (string * Cell) list)
        (resolve: Query -> Deferred<QueryResult>)
        (journal: KeyedCapture list)
        : CapturedDispatch<QueryResult, QueryError> =
        Seam.dispatchCaptured
            hashFn
            encode
            unpaged
            r.Seam
            ctx
            grant
            id
            args
            (fun q -> Query.invoke q args resolve)
            journal

    /// `dispatchPage`, journalled under `Query.invocationKeyPage` (Phase 318), so every page of a
    /// non-deterministic query has its own capture and replays from it.
    let dispatchPageCaptured
        (hashFn: HashFn)
        (encode: QueryResult -> string)
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
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
            ctx
            grant
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
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
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
            ctx
            grant
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
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
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
            ctx
            grant
            id
            args
            (fun q -> Query.invokePageWithArgs q args pageToken resolve)
            journal

    /// SETTLE a pending query capture (Phase 695, DECISIONS.md D268): the capture a captured verb left
    /// open under `ticket` when its resolver answered `Pending`, settled with the `answer` the fetch came
    /// back with — a result, or a fault in the typed resolver's words (a plain resolver's `Failed m` is
    /// `ResolveFault.Failed (m, [])`); `Pending` is not an answer it takes. `query`, `args` and
    /// `pageToken` are the invocation the ticket was issued for, the declaration as it was dispatched.
    ///
    /// The answer passes the step a dispatch puts its resolver's answer through (`Query.invokePageWithArgs`):
    /// rows `Table.validate` refuses are `ExecutionFailed` in the column layer's words (Phase 692), a
    /// `PredicateUnsupported` names the declaration's own predicate or is `ExecutionFailed` (Phase 685),
    /// and each other fault is the refusal it names. What that step answers is journalled as the captured
    /// verbs journal it: a result `Completed` through `encode`, a refusal `Refused` with its query-error
    /// document. So the settlement record carries the phase and the value a dispatch whose resolver gave
    /// the same answer at once journals, and the replay of the invocation answers what it answers over that
    /// journal; the whole journal is that journal, byte for byte, when nothing was journalled between the
    /// attempt and the settlement. `Outcome` is what that dispatch answers (`Ok (Ready r)` or `Error e`),
    /// `Ticket` the ticket settled, `Journal` the journal with the settlement.
    ///
    /// Refused, with the journal unmoved: `NotAttempted ticket` when the ticket's key is not this
    /// invocation's (`Query.invocationKeyPage`), or the declaration is labelled `deterministic` (its
    /// dispatch journals nothing); then `KeyedCapture.settleEffectKeyed`'s `NotAttempted` for an
    /// occurrence with no attempt and `AlreadySettled` for one already settled. Everything that reads the
    /// ticket is in this verb. `KeyedCapture.settleEffectKeyed` used directly on a query's ticket runs no
    /// step: what it journals is under the result writers' contract (`QueryCodec.encodeResult`).
    let settleCaptured
        (hashFn: HashFn)
        (encode: QueryResult -> string)
        (query: AdmittedQuery)
        (args: (string * Cell) list)
        (pageToken: string option)
        (ticket: string * int)
        (answer: Result<QueryResult, ResolveFault>)
        (journal: KeyedCapture list)
        : Result<CapturedDispatch<QueryResult, QueryError>, KeyedCaptureFault> =
        let c = paged pageToken
        let q = query.Declaration
        let key, occurrence = ticket

        if c.Label q = EffectCapture.deterministicTag || key <> c.Key q args then
            Error(KeyedCaptureFault.NotAttempted ticket)
        else
            let outcome =
                Query.invokePageWithArgs q args pageToken (fun _ _ _ -> Result.map Ready answer)

            // The projection the captured dispatch journals an answer through (KeyedRegistry): the answer
            // type holds no `Pending`, so the step always settles.
            let settlement =
                match outcome with
                | Ok d -> Deferred.settled d
                | Error e -> Some(Error(c.Record e))

            match settlement with
            | None ->
                Ok
                    { Outcome = outcome
                      Ticket = Some ticket
                      Journal = journal }
            | Some s ->
                KeyedCapture.settleEffectKeyed StreamConfig.canonical hashFn encode key occurrence s journal
                |> Result.map (fun j ->
                    { Outcome = outcome
                      Ticket = Some ticket
                      Journal = j })

    /// REPLAY a query invocation from the keyed journal instead of resolving it (Phase 318). The id,
    /// the parameters and the policy run exactly as `dispatchPage` (a refusal there is answered as
    /// `dispatchPage` answers it, consulting no journal); then a non-deterministic query is answered by
    /// its invocation key — `invocationKeyPage`, which for `None` is `invocationKey` — a completion as
    /// `Ready` (through `decode`, `QueryCodec.decodeResult` for the canonical form), a recorded
    /// refusal as the `QueryError` it records (Phase 385; a reason that is no query-error document,
    /// from a journal written before 0.36.0, as `ExecutionFailed` with the recorded text), an
    /// unsettled attempt as `Pending`. A deterministic query resolves live, through the clause every
    /// dispatch passes (Phase 692: rows `Table.validate` refuses are `ExecutionFailed`); an entry a
    /// captured dispatch settled holds no rows that read back as others (a pending capture the host
    /// settles later is not one). A journal that cannot
    /// answer is `ReplayFailure.Unanswered` with the `KeyedCaptureFault`, never a live fetch; every
    /// refusal of the seam, live or recorded, is `ReplayFailure.Refused`. A `ReplayedDispatch` since
    /// Phase 391 (a nested `Result` before `0.37.0`).
    let dispatchReplayed
        (decode: string -> Result<QueryResult, string>)
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
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
            ctx
            grant
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
        (r: QueryRegistry<'Ctx>)
        (ctx: 'Ctx)
        (grant: Grant option)
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
            ctx
            grant
            id
            args
            (fun q -> Query.invokePageWithArgs q args pageToken resolve)
            cursor
            journal
