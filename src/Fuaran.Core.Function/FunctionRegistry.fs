namespace Fuaran.Core

// ============================================================================
//  Signature-typed function registry (Phase 50) — the artifact-function
//  catalogue an orchestrator (and the content-pack marketplace) enumerate and
//  query BY SIGNATURE: "find functions producing a Document from a {parties,
//  amounts} context", "find an app-archetype with these holes". The tool
//  catalogue and the content-pack library become the SAME object — a typed,
//  queryable index keyed by what a function *produces* (its result type) and
//  what it *requires* (its hole shape), not by name.
//
//  Additive over the FROZEN witnesses (GP1/GP7); it EXTENDS the Phase-30
//  `Capability` registry pattern rather than introducing a parallel one — a
//  registry entry IS an invocable `Capability` (reused verbatim for the typed
//  signature + arg-validated, default-deny dispatch via `Capability.invoke`)
//  annotated with the node-kind it produces. No new trust posture; no new
//  dispatch path. A partially-applied function (a content pack) is a first-
//  class entry whose `Capability.Signature` has been narrowed
//  (`Function.signatureExcluding`, Phase 24) — fewer required holes, no special
//  case. Totality (GP4): typed failures (`InvokeError`), never exceptions.
// ============================================================================

/// A registry entry: an invocable `Capability` (its `Signature` carries the REQUIRED holes — what
/// the function needs) annotated with the node-kind it PRODUCES (`ResultType`). Keying by
/// `(ResultType, required-hole shape)` is what makes the catalogue queryable "by what it produces and
/// what it requires", not by name. The entry's id is its `Capability.Id`.
type FunctionEntry =
    {
        /// The invocable function; its `Id` is the entry's registry key.
        Capability: Capability
        /// The node kind the function produces; the `ByResult` index key, compared exactly.
        ResultType: string
    }

/// A signature query (Phase 50): the desired result type (`None` = any result type — a wildcard on the
/// produce-axis) plus the available-context shape — the holes the caller can fill, each keyed by its
/// absolute address (hygiene). `findBySignature` returns the entries whose REQUIRED holes are all
/// satisfiable from this context.
type SignatureQuery =
    {
        /// The result kind wanted, compared exactly; `None` matches every result kind.
        ResultType: string option
        /// The holes the caller can fill, matched to an entry's required holes by `Addr`. Under
        /// `Exact` the address set must equal the entry's required set; under `Subsumes` it may be larger.
        Available: SigEntry list
    }

/// How strictly an entry's signature must match a query (Phase 50).
type MatchMode =
    /// The entry's required-hole set is SUBSUMED by the available context — every required hole is
    /// satisfiable (the context supplies it, shape-compatibly) and the context may supply more. The
    /// "find everything I can run with the context I have" query (structural subsumption).
    | Subsumes
    /// The entry's required-hole set EXACTLY equals the available context (same addresses + same hole
    /// shapes) and the result type matches. The "find the function with precisely these holes" query.
    | Exact

/// A signature-typed function registry (Phase 50) — the artifact-function catalogue, queried BY
/// SIGNATURE. It keeps a result-type index (result-kind → the ids producing it) so a "produces a
/// Document" query narrows before the hole-shape filter runs. Default-deny by shape on dispatch (only
/// a registered id resolves) — the same trust posture as `CapabilityRegistry`, reusing
/// `Capability.invoke`.
///
/// OPAQUE since Phase 316: the entries and the index are reachable only through this module's
/// verbs, every one of which keeps the index the exact projection of the entries. While the record
/// was public, `{ r with Entries = Map.remove id r.Entries }` left the index naming an id `tryFind`
/// no longer resolved, and an entry added to `Entries` by hand was never found by result type; now
/// `findBySignature` returns exactly the enumerated entries the query matches, by law.
type FunctionRegistry =
    private
        {
            /// Every entry, keyed by its capability id; each was admitted by `register`, so each is total.
            Entries: Map<string, FunctionEntry>
            /// Result kind to the ids producing it: exactly the ids of `Entries`, grouped by result
            /// kind, with no empty group — the projection every verb below maintains.
            ByResult: Map<string, Set<string>>
            /// The gates `dispatch` runs and the observers a refusal reaches (Phase 318); carried
            /// through every lifecycle verb, combined by `union`.
            Policy: RegistryPolicy<Capability, (string * string) list>
        }

/// Populate / enumerate / query / dispatch the signature-typed registry. Additive over the `Capability`
/// surface; FSharp.Core-only, Fable-clean. (`ModuleSuffix` so the module and the `FunctionRegistry`
/// type can share a name — the same idiom as `Option`/`List`.)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FunctionRegistry =

    /// The registry with no entries and an empty result index; every query against it is empty.
    let empty: FunctionRegistry =
        { Entries = Map.empty
          ByResult = Map.empty
          Policy = RegistryPolicy.none }

    /// Build an entry from the node-kind it produces + an invocable capability.
    let entry (resultType: string) (cap: Capability) : FunctionEntry =
        { Capability = cap
          ResultType = resultType }

    /// Register an entry — additive, no silent overwrite (a duplicate id is a named
    /// `DuplicateCapability`, reusing the Capability registry's error vocabulary), and only a total,
    /// well-formed one (`NonTotalCapability` / `IllFormedCapability`, through the one admission gate
    /// `CapabilityRegistry.register` runs — Phases 295 and 307). Maintains both the id map and the
    /// result-type index.
    let register (e: FunctionEntry) (r: FunctionRegistry) : Result<FunctionRegistry, InvokeError> =
        let id = e.Capability.Id

        KeyedRegistry.register
            DuplicateCapability
            (fun (e: FunctionEntry) -> Capability.admissionFault e.Capability)
            id
            e
            r.Entries
        |> Result.map (fun entries ->
            let ids =
                r.ByResult
                |> Map.tryFind e.ResultType
                |> Option.defaultValue Set.empty
                |> Set.add id

            { Entries = entries
              ByResult = Map.add e.ResultType ids r.ByResult
              Policy = r.Policy })

    /// The entry registered under exactly `id` (ordinal, case-sensitive), or `None`; the result index
    /// is not consulted.
    let tryFind (id: string) (r: FunctionRegistry) : FunctionEntry option = Map.tryFind id r.Entries

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part of
    /// the contract (`registryLaws` certifies it).
    let enumerate (r: FunctionRegistry) : FunctionEntry list = r.Entries |> Map.toList |> List.map snd

    /// The registered ids, in id order — what a `NoSuchCapability` refusal from this registry names
    /// (Phase 316: the record is opaque, so the id set is read through the module).
    let ids (r: FunctionRegistry) : string list = KeyedRegistry.ids r.Entries

    /// The registry over `entries` with its result index REBUILT from them — the one constructor the
    /// lifecycle verbs below share, so the index cannot be anything but the entries' projection.
    let private ofEntries
        (policy: RegistryPolicy<Capability, (string * string) list>)
        (entries: Map<string, FunctionEntry>)
        : FunctionRegistry =
        { Entries = entries
          ByResult =
            entries
            |> Map.fold
                (fun idx id (e: FunctionEntry) ->
                    let held = idx |> Map.tryFind e.ResultType |> Option.defaultValue Set.empty
                    Map.add e.ResultType (Set.add id held) idx)
                Map.empty
          Policy = policy }

    // ---- the lifecycle (Phase 316): a registry is a lattice, not an append log ----

    /// Remove the entry registered under `id` — refused `NoSuchCapability(id, known)` when the
    /// registry does not hold it, naming every id it does hold. The result index drops the id with
    /// it, so `findBySignature` can never return an entry `tryFind` no longer resolves; removing an
    /// entry just registered gives back the registry it was registered into.
    let unregister (id: string) (r: FunctionRegistry) : Result<FunctionRegistry, InvokeError> =
        KeyedRegistry.unregister (fun id known -> NoSuchCapability(id, known)) id r.Entries
        |> Result.map (ofEntries r.Policy)

    /// Swap the entry registered under `e.Capability.Id` for `e` — the hot-reload verb, which may
    /// change the result kind the entry is indexed under. Refused `NoSuchCapability` when the id is
    /// not registered, and held to the admission gate `register` runs; on a refusal the registry is
    /// unchanged.
    let replace (e: FunctionEntry) (r: FunctionRegistry) : Result<FunctionRegistry, InvokeError> =
        KeyedRegistry.replace
            (fun id known -> NoSuchCapability(id, known))
            (fun (e: FunctionEntry) -> Capability.admissionFault e.Capability)
            e.Capability.Id
            e
            r.Entries
        |> Result.map (ofEntries r.Policy)

    /// The registry narrowed to the ids in `keep`, its index rebuilt — a session- or actor-scoped
    /// catalogue. An id in `keep` the registry does not hold is ignored, so the result enumerates a
    /// subset of what `r` enumerates.
    let restrict (keep: Set<string>) (r: FunctionRegistry) : FunctionRegistry =
        ofEntries r.Policy (KeyedRegistry.restrict keep r.Entries)

    /// The join of two registries whose ids are disjoint, its index rebuilt — refused
    /// `DuplicateCapability` naming the first id, in id order, that both hold. Associative. The union
    /// runs both registries' gates (Phase 318, `RegistryPolicy.combine`).
    let union (a: FunctionRegistry) (b: FunctionRegistry) : Result<FunctionRegistry, InvokeError> =
        KeyedRegistry.union DuplicateCapability a.Entries b.Entries
        |> Result.map (ofEntries (RegistryPolicy.combine a.Policy b.Policy))

    // ---- the policy gate (Phase 318) ----

    /// `r` with `gate` added to the gates `dispatch` runs — the same gate shape, over the entry's
    /// capability, that `CapabilityRegistry.withGate` takes, so one gate value serves both registries.
    /// Adding a gate can only refuse more.
    let withGate (gate: PolicyGate<Capability, (string * string) list>) (r: FunctionRegistry) : FunctionRegistry =
        { r with
            Policy = RegistryPolicy.withGate gate r.Policy }

    /// `r` with `observe` told of every invocation its policy refuses, before the refusal returns.
    let onDenied (observe: PolicyDenial<(string * string) list> -> unit) (r: FunctionRegistry) : FunctionRegistry =
        { r with
            Policy = RegistryPolicy.onDenied observe r.Policy }

    /// What the registry would decide for an invocation without dispatching it — as
    /// `CapabilityRegistry.decide`: an unknown id and an invalid argument set are denials, otherwise
    /// the gates' join. No observer is told.
    let decide (r: FunctionRegistry) (id: string) (args: (string * string) list) : PolicyDecision =
        match Map.tryFind id r.Entries with
        | None -> PolicyDecision.denyWith (InvokeError.describe (NoSuchCapability(id, ids r))) (ids r)
        | Some e ->
            match Capability.validateArgs e.Capability args with
            | Error err -> PolicyDecision.deny (InvokeError.describe err)
            | Ok() -> RegistryPolicy.decide r.Policy e.Capability args

    /// Is a required slot constraint satisfied by an available slot? An unconstrained required slot
    /// (`None`) accepts any available slot; a constrained one needs the same kind.
    let private slotSubsumes (required: string option) (available: string option) : bool =
        match required, available with
        | None, _ -> true
        | Some rk, Some ak -> rk = ak
        | Some _, None -> false

    /// Is a single required hole satisfied by the matching available-context entry (same address)?
    /// value/repeat: kinds agree and the available value-space ⊆ the required space, by THE space
    /// relation (`Space.subsumes`, Phase 295 — the relation `CapabilityPipeline.typeCheck` asks too,
    /// so an int context now fills a number hole, as validation always accepted); slot: kinds agree
    /// and the available slot constraint satisfies the required one.
    let private holeSatisfied (req: SigEntry) (av: SigEntry) : bool =
        req.Kind = av.Kind
        && (match req.HoleKind with
            | Some(SlotHole _) -> slotSubsumes req.Slot av.Slot
            | _ ->
                match req.Space, av.Space with
                | Some rs, Some avs -> Space.subsumes rs avs
                | _ -> false)

    /// The core signature-search predicate (Phase 50) — does `entry` match `query` under `mode`? Only
    /// the entry's REQUIRED holes gate a match (optional holes never block); hygiene — holes match by
    /// absolute address. `Subsumes`: result type matches (or query wildcard) and every required hole is
    /// satisfiable from the context. `Exact`: result type matches AND the required-hole address set
    /// equals the context address set AND each pair is shape-EQUAL (not merely subsumed).
    let private matchesQuery (mode: MatchMode) (query: SignatureQuery) (entry: FunctionEntry) : bool =
        let availByAddr = query.Available |> List.map (fun e -> e.Addr, e) |> Map.ofList
        let required = entry.Capability.Signature.Holes |> List.filter _.Required

        let resultMatches =
            match query.ResultType with
            | None -> true
            | Some t -> t = entry.ResultType

        match mode with
        | Subsumes ->
            resultMatches
            && required
               |> List.forall (fun req ->
                   match Map.tryFind req.Addr availByAddr with
                   | Some av -> holeSatisfied req av
                   | None -> false)
        | Exact ->
            let reqAddrs = required |> List.map _.Addr |> Set.ofList
            let avAddrs = query.Available |> List.map _.Addr |> Set.ofList

            resultMatches
            && reqAddrs = avAddrs
            && required
               |> List.forall (fun req ->
                   match Map.tryFind req.Addr availByAddr with
                   | Some av ->
                       req.Kind = av.Kind
                       && Function.slotSpaceOf req = Function.slotSpaceOf av
                       && req.Slot = av.Slot
                   | None -> false)

    /// Find every registered function whose signature matches the query under `mode` (Phase 50). A
    /// `Some` result type narrows the candidate set via the `ByResult` index first (the "produces a
    /// Document" key); a `None` result type scans all entries. The surviving candidates are filtered by
    /// the hole-shape predicate (exact or structural-subsumption) and returned id-stable (the same
    /// enumeration-stability contract as `enumerate`).
    let findBySignature (mode: MatchMode) (query: SignatureQuery) (r: FunctionRegistry) : FunctionEntry list =
        let candidateIds =
            match query.ResultType with
            | Some t -> r.ByResult |> Map.tryFind t |> Option.defaultValue Set.empty |> Set.toList
            | None -> r.Entries |> Map.toList |> List.map fst

        candidateIds
        |> List.sort
        |> List.choose (fun id -> Map.tryFind id r.Entries)
        |> List.filter (matchesQuery mode query)

    /// Dispatch an invocation through the registry (Phase 50): resolve the id (default-deny — an
    /// unregistered id is `NoSuchCapability`), then invoke via `Capability.invoke` (arg-validated — the
    /// SAME trust posture as `CapabilityRegistry.dispatch`, no parallel path). The host supplies the body per the
    /// resolved entry's placement, and answers in the `Deferred` envelope.
    ///
    /// It moved with `Capability.invoke` in Phase 210 because it IS `Capability.invoke` — there is no
    /// total projection from `Result<Deferred<'v>, InvokeError>` back to `Result<'v, InvokeError>`
    /// (`Pending` has no `InvokeError` case, and minting one would widen a published union to avoid
    /// carrying the envelope), and a parallel path is the one thing this registry was built not to be.
    let dispatch
        (r: FunctionRegistry)
        (id: string)
        (args: (string * string) list)
        (body: FunctionEntry -> unit -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        match Map.tryFind id r.Entries with
        | None -> Error(NoSuchCapability(id, ids r))
        | Some e ->
            // Phase 318: validate, then the policy's gates, then the body — `Capability.invoke`
            // exactly when no gate runs.
            Capability.validateArgs e.Capability args
            |> Result.bind (fun () ->
                RegistryPolicy.admit InvokeError.policyRefused ApprovalRequired r.Policy id e.Capability args)
            |> Result.bind (fun () -> InvokeError.settle (body e ()))

    /// Partially apply a registered function (Phase 50) — the content-pack formalism. Produce a NEW
    /// entry under `newId` whose signature is `Function.signatureExcluding boundAddrs` of the source's
    /// (the bound holes drop out — a narrowed signature, fewer required holes), with the source's result
    /// type + placement. Registering it makes the content pack a first-class registry entry findable by
    /// its narrowed hole shape (a smaller available context now subsumes it, while the un-narrowed
    /// original still demands the dropped holes). Hygiene: holes drop by absolute address. Does NOT
    /// mutate the source entry.
    ///
    /// Every bound address must be a BINDABLE hole of the source — a data hole (value, slot or
    /// repeat), not an action hole and not an address the signature does not declare (Phase 295).
    /// The first that is not is refused `UnknownArg(addr, bindable)`, naming the bindable holes, so a
    /// typo cannot register an un-narrowed signature under the pack's name; `ContentPack.load`
    /// reports it as `UnknownBoundAddr`.
    let partiallyApply
        (newId: string)
        (boundAddrs: Set<string>)
        (source: FunctionEntry)
        : Result<FunctionEntry, InvokeError> =
        let bindable =
            source.Capability.Signature.Holes
            |> List.filter (fun h ->
                match h.HoleKind with
                | Some(ActionHole _)
                | None -> false
                | Some _ -> true)
            |> List.map _.Addr

        match
            boundAddrs
            |> Set.toList
            |> List.tryFind (fun a -> not (List.contains a bindable))
        with
        | Some stray -> Error(UnknownArg(stray, bindable))
        | None ->
            let narrowed = Function.signatureExcluding boundAddrs source.Capability.Signature

            Ok
                { Capability = Capability.create newId narrowed source.Capability.Placement
                  ResultType = source.ResultType }
