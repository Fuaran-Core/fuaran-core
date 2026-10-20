namespace Fuaran.Core

/// Build / type-check / key / serialise / **evaluate** a `CapabilityPipeline`. Additive over the
/// `Capability` surface; FSharp.Core-only, Fable-clean.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module CapabilityPipeline =

    /// The id of either node case — the key `FromNode` edges, the result map and the dirty set use.
    let nodeId (n: PipelineNode) : string =
        match n with
        | Source(id, _, _) -> id
        | Invoke(id, _, _, _) -> id

    let internal nodeOutputType (n: PipelineNode) : ValueSpace =
        match n with
        | Source(_, _, ty) -> ty
        | Invoke(_, _, ty, _) -> ty

    let private spaceTag =
        function
        | IntRange _ -> "int"
        | FloatRange _ -> "float"
        | StringLen _ -> "string"
        | Enum _ -> "enum"
        | AnyString -> "anyString"
        | SlotTree _ -> "slotTree"

    /// Does a producer output value-space feed a consumer arg value-space — every value the producer
    /// can emit is acceptable to the consumer? THE space relation, `Space.subsumes` (Phase 295), which
    /// compares bounds: an `IntRange(0, 1000)` output no longer feeds an `IntRange(0, 10)` argument.
    let private spaceFeeds (producer: ValueSpace) (consumer: ValueSpace) : bool = Space.subsumes consumer producer

    /// The upstream ids an `Invoke` node's `FromNode` edges name, in argument order.
    let private upstreams (n: PipelineNode) : string list =
        match n with
        | Source _ -> []
        | Invoke(_, _, _, args) ->
            args
            |> List.choose (fun (_, src) ->
                match src with
                | FromNode up -> Some up
                | Literal _ -> None)

    /// Type-check the pipeline against a capability lookup (Phase 35; the lookup since Phase 295):
    /// node ids are unique; every `Invoke`'s capability resolves (default-deny); every arg is one the
    /// capability takes, once (`DuplicateArg`, Phase 307), refused as `Capability.validateArgs`
    /// refuses it and wrapped `PipelineArgRefused`; a `Literal` is in its hole's space; a `FromNode` edge names a declared
    /// node that comes EARLIER in declaration order — a self-edge or an edge that closes a cycle is
    /// `PipelineCycle` naming the cycle, any other later node `PipelineForwardEdge` — and its
    /// producer's output space feeds the arg's space (`Space.subsumes`; an ill-typed edge is a named
    /// `EdgeTypeMismatch`); every required hole is bound. Total; the first refusal in declaration
    /// order is the answer.
    let typeCheck (lookup: CapabilityLookup) (p: CapabilityPipeline) : Result<unit, PipelineError> =
        let ids = p.Nodes |> List.map nodeId
        let nodeById = p.Nodes |> List.map (fun n -> nodeId n, n) |> Map.ofList
        let position = ids |> List.mapi (fun i id -> id, i) |> Map.ofList

        // The path from `start` along `FromNode` edges to a node whose edge reaches `target`, if
        // any: [start; …; x] with x -> target. Each node is visited once.
        let pathTo (target: string) (start: string) : string list option =
            let rec walk (seen: Set<string>) (cur: string) : (Set<string> * string list option) =
                if seen.Contains cur then
                    seen, None
                else
                    let seen = seen.Add cur

                    let ups = Map.tryFind cur nodeById |> Option.map upstreams |> Option.defaultValue []

                    if List.contains target ups then
                        seen, Some [ cur ]
                    else
                        let rec tryUps (seen: Set<string>) =
                            function
                            | [] -> seen, None
                            | u :: rest ->
                                match walk seen u with
                                | seen', Some path -> seen', Some(cur :: path)
                                | seen', None -> tryUps seen' rest

                        tryUps seen ups

            snd (walk Set.empty start)

        let dup =
            ids
            |> List.countBy id
            |> List.tryPick (fun (k, c) -> if c > 1 then Some k else None)

        match dup with
        | Some d -> Error(PipelineError.DuplicateNode d)
        | None ->
            let rec go =
                function
                | [] -> Ok()
                | Source _ :: rest -> go rest
                | Invoke(nid, capId, _, args) :: rest ->
                    match lookup.TryFind capId with
                    | None -> Error(PipelineError.PipelineNoSuchCapability(capId, lookup.Known))
                    | Some cap ->
                        let holes = cap.Signature.Holes
                        let declared = holes |> List.map _.Addr

                        let edgeFault (addr: string) (up: string) (argSpace: ValueSpace) =
                            match Map.tryFind up nodeById with
                            | None -> Some(PipelineError.UnknownNode up)
                            | Some _ when up = nid -> Some(PipelineError.PipelineCycle(nid, [ nid ]))
                            | Some upNode ->
                                if position[up] > position[nid] then
                                    match pathTo nid up with
                                    | Some path -> Some(PipelineError.PipelineCycle(nid, nid :: path))
                                    | None -> Some(PipelineError.PipelineForwardEdge(nid, addr, up))
                                elif spaceFeeds (nodeOutputType upNode) argSpace then
                                    None
                                else
                                    Some(
                                        PipelineError.EdgeTypeMismatch(
                                            nid,
                                            addr,
                                            spaceTag (nodeOutputType upNode),
                                            spaceTag argSpace
                                        )
                                    )

                        let argFault (addr: string, src: ArgSource) =
                            match src with
                            | Literal v ->
                                Capability.argFault cap declared (addr, v)
                                |> Option.map (fun e -> PipelineError.PipelineArgRefused(nid, e))
                            | FromNode up ->
                                match holes |> List.tryFind (fun h -> h.Addr = addr) with
                                | None -> Some(PipelineError.PipelineArgRefused(nid, UnknownArg(addr, declared)))
                                | Some h ->
                                    match h.Space with
                                    | None -> Some(PipelineError.PipelineArgRefused(nid, UninvocableArg addr))
                                    | Some argSpace -> edgeFault addr up argSpace

                        // Phase 307: an address bound twice is `DuplicateArg`, as `validateArgs`
                        // refuses it — checked first, at the second occurrence.
                        let duplicate = Capability.repeatedAddrs args |> List.tryHead

                        match
                            duplicate
                            |> Option.map (fun a -> PipelineError.PipelineArgRefused(nid, DuplicateArg a))
                            |> Option.orElse (args |> List.tryPick argFault)
                        with
                        | Some e -> Error e
                        | None ->
                            let bound = args |> List.map fst |> Set.ofList

                            let unbound =
                                holes
                                |> List.filter (fun h -> h.Required && not (bound.Contains h.Addr))
                                |> List.map _.Addr

                            if List.isEmpty unbound then
                                go rest
                            else
                                Error(PipelineError.PipelineArgRefused(nid, RequiredArgsUnbound unbound))

            go p.Nodes

    /// The Phase-27 capture key a node's realized result is journalled under: a readable prefix
    /// (`source#<id>#` / `<capId>#<id>#`) + a hash of the node's canonical pre-image. A consumer
    /// threads this as `EffectCapture.captureEffect`'s `eff` argument, so the whole dataflow replays
    /// byte-identically. Since Phase 225 the hashed pre-image goes through `Hash.canonicalFields`
    /// and covers EVERY component — the ids as well as the arg references, each binding three
    /// fields (addr, `L`/`N`, value) and the bindings in sorted order — so it is injective: two
    /// nodes share a pre-image only when they share every component, whatever `#` or `=` an id or
    /// a literal contains. (It joined `addr=L:value` on the empty string before, the collision
    /// `Query.invocationKey`'s `key_collision` finding named.)
    ///
    /// **The node KIND leads the hashed pre-image (Phase 307)**: `source` for a `Source`, `invoke`
    /// for an `Invoke`. Without it `Source("source", "source", _)` and an argument-less
    /// `Invoke("source", "source", _, [])` shared the pre-image `[source; source]` and the readable
    /// prefix `source#source#`, so one key named two different computations. The pre-images now
    /// differ in their first field whatever the ids say. Every key moves (a breaking change to the
    /// journalled keys, on the `0.35.0` draft); the other hosts' twins gain the field at their next
    /// raise.
    ///
    /// **The pipeline grammar (D173)**, built by `Hash.keyedBy`: the readable prefix, then the kind and
    /// the ids, then for an `Invoke` one triple per binding, the triples ordered by their ENCODED
    /// pre-image (as the bytes were before the builder, so no journalled key moved).
    let nodeInvocationKey (n: PipelineNode) : string =
        match n with
        | Source(id, dref, _) -> Hash.keyedBy ("source#" + id) [ "source"; id; dref ]
        | Invoke(id, capId, _, args) ->
            let bindings =
                args
                |> List.map (fun (a, s) ->
                    match s with
                    | Literal v -> [ a; "L"; v ]
                    | FromNode up -> [ a; "N"; up ])
                |> List.sortBy Hash.canonicalFields
                |> List.concat

            Hash.keyedBy (capId + "#" + id) ([ "invoke"; capId; id ] @ bindings)

    // ---- wire codec ----

    // Phase 551 — the wire codec is DERIVED from the pipeline's IDL declaration (`PipelineSeamCodec`,
    // DECISIONS.md D200); a node's output space is read through `PipelineWire.outputSpace`.

    /// Encode a pipeline to its canonical wire string. Total; a pipeline whose output spaces carry
    /// a non-finite bound writes bytes `decode` refuses — `tryEncode` refuses it instead.
    let encode (p: CapabilityPipeline) : string =
        Canonical.write Rendering.CrossHost (PipelineSeamCodec.encodeCapabilityPipeline p)

    /// The GUARDED encode (Phase 307): `Canonical.tryWrite Rendering.CrossHost`, so a non-finite bound or an ill-formed
    /// string is refused, naming it. `Ok` is exactly `encode`'s string.
    let tryEncode (p: CapabilityPipeline) : Result<string, string> =
        Canonical.tryWrite Rendering.CrossHost (PipelineSeamCodec.encodeCapabilityPipeline p)

    /// Decode a pipeline from a wire string, answering a typed refusal (Phase 310): its code, the
    /// path to the value at fault, and [[decode]]'s sentence. A parse failure is refused at the root.
    let decodeDetailed (s: string) : Result<CapabilityPipeline, DecodeError> =
        Decoder.parse ReadOptions.strict s
        |> Result.bind PipelineSeamCodec.decodeCapabilityPipeline

    /// Decode a pipeline from a wire string (`Result`-typed, named errors) — the sentence of
    /// [[decodeDetailed]]'s refusal.
    let decode (s: string) : Result<CapabilityPipeline, string> =
        decodeDetailed s |> Result.mapError DecodeError.describe

    // ---- evaluation + incremental re-evaluation (Phase 62) ----
    // The reference evaluator the incremental path is certified byte-identical to. Core runs NO capability
    // body (GP6 — render/recompute stays domain-side); it topologically walks the DAG (nodes are in
    // declaration = topological order), resolves each `FromNode` edge into the upstream node's realised
    // value, and hands the resolved arg list to the caller-supplied host `body`. This is the capability-DAG
    // analogue of the compute layer's data-frame pipeline evaluator (Phase 34, which left this repository
    // with the compute layer in 0.33.0) — the pipeline plumbing is Core's, the compute is the host's.

    /// Resolve one node's args against the results-so-far, then run the host `body`. Shared by `eval` and
    /// `evalFrom` so the two agree by construction. A `Literal` passes through as a `LiteralArg` (the
    /// type-check put it in its hole's space); a `FromNode up` resolves to `up`'s realised value, which
    /// must lie in the space of the hole it feeds, read through `spell` (Phase 295) — a value outside
    /// it is `EvalArgRefused`, wrapping the `ArgOutOfSpace` the capability gives the same string.
    let private runNode
        (lookup: CapabilityLookup)
        (spell: 'v -> string)
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (results: Map<string, 'v>)
        (n: PipelineNode)
        : Result<'v, PipelineEvalError> =
        let nid = nodeId n

        let resolved =
            match n with
            | Source _ -> Ok []
            | Invoke(_, capId, _, args) ->
                let spaceOf (addr: string) =
                    lookup.TryFind capId
                    |> Option.bind (fun c ->
                        c.Signature.Holes
                        |> List.tryPick (fun h -> if h.Addr = addr then h.Space else None))

                (Ok [], args)
                ||> List.fold (fun acc (addr, src) ->
                    acc
                    |> Result.bind (fun xs ->
                        match src with
                        | Literal s -> Ok((addr, LiteralArg s) :: xs)
                        | FromNode up ->
                            match Map.tryFind up results, spaceOf addr with
                            | Some v, Some space ->
                                let spelled = spell v

                                if Space.validate space spelled then
                                    Ok((addr, FromUpstream v) :: xs)
                                else
                                    Error(PipelineEvalError.EvalArgRefused(nid, ArgOutOfSpace(addr, space, spelled)))
                            // Unreachable after `typeCheck`: an edge names an earlier node into a
                            // spaced hole of a resolved capability. Refused as the type-check would.
                            | None, _ ->
                                Error(
                                    PipelineEvalError.EvalIllTyped(PipelineError.PipelineForwardEdge(nid, addr, up))
                                )
                            | Some _, None ->
                                Error(
                                    PipelineEvalError.EvalIllTyped(
                                        PipelineError.PipelineArgRefused(nid, UninvocableArg addr)
                                    )
                                )))
                |> Result.map List.rev

        // Phase 383 — the admission gate (D111: one gate at every reader). An `Invoke` passes the
        // lookup's policy over its arguments as the seam reads them — a literal as declared, an
        // upstream value through `spell` — exactly as `CapabilityRegistry.dispatch` would, observers
        // told; a refusal stops evaluation before the body. A `Source` invokes nothing.
        let admitted (args: (string * PipelineArg<'v>) list) : Result<unit, PipelineEvalError> =
            match n with
            | Source _ -> Ok()
            | Invoke(_, capId, _, _) ->
                match lookup.TryFind capId with
                // Unreachable after `typeCheck`, which refused an unresolved id; refused as it would.
                | None ->
                    Error(PipelineEvalError.EvalIllTyped(PipelineError.PipelineNoSuchCapability(capId, lookup.Known)))
                | Some c ->
                    let spelled =
                        args
                        |> List.map (fun (addr, a) ->
                            match a with
                            | LiteralArg s -> addr, s
                            | FromUpstream v -> addr, spell v)

                    RegistryPolicy.admit InvokeError.policyRefused ApprovalRequired lookup.Policy capId c spelled
                    |> Result.mapError (fun e -> PipelineEvalError.EvalPolicyRefused(nid, e))

        resolved
        |> Result.bind (fun args -> admitted args |> Result.map (fun () -> args))
        |> Result.bind (fun args ->
            match body n args with
            | Ok v -> Ok v
            | Error m -> Error(PipelineEvalError.EvalNodeFailed(nid, m)))

    /// The reference evaluator (Phase 62): fold the host `body` over the pipeline in declaration
    /// (topological) order, threading an `id → value` result map. `body` receives each node and its args
    /// with every `FromNode` edge already resolved to the upstream value. Total — an ill-typed pipeline,
    /// an upstream value outside its hole's space, a policy refusal or a body failure is a named
    /// `PipelineEvalError`, never a throw. The cross-host compute contract the incremental `evalFrom`
    /// is certified byte-identical to.
    ///
    /// **It type-checks first (Phase 295) and admits through the lookup's policy (Phase 383).** `eval`
    /// refuses a pipeline `typeCheck` refuses as `EvalIllTyped`, before any body runs; then each
    /// `Invoke` node's arguments pass the lookup's `Policy` — the registry's own gates, in both
    /// projections — before its body runs, and a refusal is `EvalPolicyRefused` carrying the
    /// registry's `PolicyRefused` / `ApprovalRequired`, observers told. So it is not a second dispatch
    /// path beside `CapabilityRegistry.dispatch`: a node runs only where its capability resolves, its
    /// arguments are ones that capability takes, and the registry's policy admits the invocation.
    /// `spell` writes an upstream value as the argument string the seam reads (the identity for a
    /// `string` pipeline), and a value outside the space of the hole it feeds is `EvalArgRefused`.
    ///
    /// **It stays SYNCHRONOUS, by decision (Phase 210's routed-out question, operator decision
    /// 2026-09-19).** `Capability.invoke` and both dispatchers carry the `Deferred` envelope; `body`
    /// here returns a plain `Result` and does not. Enveloping it would be a RESUMPTION model rather
    /// than a retype — a fold that meets `Pending` at one node must say what becomes of every node
    /// after it, and `evalFrom`'s byte-identical contract would have to hold across the suspension —
    /// and no demand for one has been measured. Asynchrony belongs at the leaves: a host that must
    /// wait resolves its `Deferred` invocations before it folds the pipeline.
    let eval
        (lookup: CapabilityLookup)
        (spell: 'v -> string)
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (p: CapabilityPipeline)
        : Result<Map<string, 'v>, PipelineEvalError> =
        match typeCheck lookup p with
        | Error e -> Error(PipelineEvalError.EvalIllTyped e)
        | Ok() ->
            let rec go (results: Map<string, 'v>) =
                function
                | [] -> Ok results
                | n :: rest ->
                    runNode lookup spell body results n
                    |> Result.bind (fun v -> go (Map.add (nodeId n) v results) rest)

            go Map.empty p.Nodes

    /// The pipeline's dependency map — each node id to the upstream ids its `FromNode` edges name (a
    /// repeated id to the union of its nodes' edges) — in the shape `Propagation` reads. The pipeline's
    /// dirty set for a changed-input set is `Propagation.dirtyFromChangedIds (dependencyMap p) changed`:
    /// `changed` and every node transitively downstream of it, and nothing else — the closure
    /// `proofs/Propagation.fst` proves sound and least, which `proofs/Capability.fst` instantiates at
    /// exactly these edges (Phase 510, D173; Phase 544, which removed the `dirtySet` forward so the
    /// closure has one name). Pure over the DAG's edge structure.
    let dependencyMap (p: CapabilityPipeline) : Map<string, Set<string>> =
        p.Nodes
        |> List.groupBy nodeId
        |> List.map (fun (id, ns) -> id, ns |> List.collect upstreams |> Set.ofList)
        |> Map.ofList

    /// Incrementally re-evaluate a pipeline (Phase 62) given the PRIOR evaluation and the set of
    /// changed-input node ids: re-invoke only the nodes downstream-of-change (the dirty set,
    /// `Propagation.dirtyFromChangedIds` over `dependencyMap`), reusing each clean node's prior result.
    /// **Byte-identical to a full `eval` over the same inputs** (the Phase-34
    /// discipline, `Conformance.capabilityPipelineIncrementalLaws`) — a clean node's inputs are unchanged,
    /// so its `eval` result is exactly its prior result; a dirty node re-invokes the body against the new
    /// upstream values. Effect-honesty on the dirty path: a clean node reuses its recorded value (the
    /// Phase-53 gate), an uncaptured live node on the dirty path re-invokes, so incrementality never serves
    /// a stale effect result. A node absent from `prior` (never evaluated) is always (re-)evaluated.
    /// It type-checks first and admits every node it re-invokes through the lookup's policy, exactly as
    /// `eval` does (Phases 295, 383), so the two refuse the same pipelines; a reused clean node runs no
    /// body, and so is admitted by the evaluation that recorded it.
    let evalFrom
        (lookup: CapabilityLookup)
        (spell: 'v -> string)
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (p: CapabilityPipeline)
        : Result<Map<string, 'v>, PipelineEvalError> =
        match typeCheck lookup p with
        | Error e -> Error(PipelineEvalError.EvalIllTyped e)
        | Ok() ->
            let dirty = Propagation.dirtyFromChangedIds (dependencyMap p) changed

            let rec go (results: Map<string, 'v>) =
                function
                | [] -> Ok results
                | n :: rest ->
                    let nid = nodeId n

                    if not (Set.contains nid dirty) && Map.containsKey nid prior then
                        go (Map.add nid (Map.find nid prior) results) rest
                    else
                        runNode lookup spell body results n
                        |> Result.bind (fun v -> go (Map.add nid v results) rest)

            go Map.empty p.Nodes
