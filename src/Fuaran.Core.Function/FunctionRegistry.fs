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
    { Capability: Capability
      ResultType: string }

/// A signature query (Phase 50): the desired result type (`None` = any result type — a wildcard on the
/// produce-axis) plus the available-context shape — the holes the caller can fill, each keyed by its
/// absolute address (hygiene). `findBySignature` returns the entries whose REQUIRED holes are all
/// satisfiable from this context.
type SignatureQuery =
    { ResultType: string option
      Available: SigEntry list }

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
/// SIGNATURE. `ByResult` is the result-type index (result-kind → the ids producing it), maintained
/// additively so a "produces a Document" query narrows before the hole-shape filter runs. Default-deny
/// by shape on dispatch (only a registered id resolves) — the same trust posture as
/// `CapabilityRegistry`, reusing `Capability.invoke`.
type FunctionRegistry =
    { Entries: Map<string, FunctionEntry>
      ByResult: Map<string, Set<string>> }

/// Populate / enumerate / query / dispatch the signature-typed registry. Additive over the `Capability`
/// surface; FSharp.Core-only, Fable-clean. (`ModuleSuffix` so the module and the `FunctionRegistry`
/// type can share a name — the same idiom as `Option`/`List`.)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FunctionRegistry =

    let empty: FunctionRegistry =
        { Entries = Map.empty
          ByResult = Map.empty }

    /// Build an entry from the node-kind it produces + an invocable capability.
    let entry (resultType: string) (cap: Capability) : FunctionEntry =
        { Capability = cap
          ResultType = resultType }

    /// Register an entry — additive, no silent overwrite (a duplicate id is a named
    /// `DuplicateCapability`, reusing the Capability registry's error vocabulary), and only a total
    /// one (`NonTotalCapability`, as `CapabilityRegistry.register` refuses it — Phase 295). Maintains
    /// both the id map and the result-type index.
    let register (e: FunctionEntry) (r: FunctionRegistry) : Result<FunctionRegistry, InvokeError> =
        let id = e.Capability.Id

        if Map.containsKey id r.Entries then
            Error(DuplicateCapability id)
        else
            match Capability.totalityFault e.Capability with
            | Some fault -> Error fault
            | None ->
                let ids =
                    r.ByResult
                    |> Map.tryFind e.ResultType
                    |> Option.defaultValue Set.empty
                    |> Set.add id

                Ok
                    { Entries = Map.add id e r.Entries
                      ByResult = Map.add e.ResultType ids r.ByResult }

    let tryFind (id: string) (r: FunctionRegistry) : FunctionEntry option = Map.tryFind id r.Entries

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part of
    /// the contract (`registryLaws` certifies it).
    let enumerate (r: FunctionRegistry) : FunctionEntry list = r.Entries |> Map.toList |> List.map snd

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
        let required = entry.Capability.Signature.Holes |> List.filter (fun h -> h.Required)

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
            let reqAddrs = required |> List.map (fun h -> h.Addr) |> Set.ofList
            let avAddrs = query.Available |> List.map (fun e -> e.Addr) |> Set.ofList

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
        | None -> Error(NoSuchCapability(id, r.Entries |> Map.toList |> List.map fst))
        | Some e -> Capability.invoke e.Capability args (body e)

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
            |> List.map (fun h -> h.Addr)

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
