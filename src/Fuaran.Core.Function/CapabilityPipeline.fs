namespace Fuaran.Core

// ============================================================================
//  Serializable capability pipeline (Phase 35) — a typed capability-DAG declared
//  as DATA, so a multi-step compute (load → invoke → derive → invoke) is
//  replayable end-to-end through the Phase-27 capture seam, not host-side glue.
//  A node's typed output feeds a downstream node's typed arg; an ill-typed edge
//  is a named error at composition time, never a runtime throw.
//
//  Nodes are `Source` (a named data ref) + `Invoke` (a registered capability).
//  A compute step — a data-frame transform, say, from the compute layer that is
//  produced outside this repository since 0.33.0 — participates as a `Capability`
//  whose body runs its evaluator, so the pipeline needs NO dedicated transform node
//  and `Function` takes no dependency on any compute package (GP1/GP2: additive over
//  the Capability surface). FSharp.Core only, Fable-clean.
// ============================================================================

/// Where an `Invoke` node's argument comes from: a literal scalar, or the output of an upstream node
/// (the edge that wires the DAG).
type ArgSource =
    /// A fixed argument string, which must lie in its hole's space.
    | Literal of value: string
    /// The output of the node with this id, which must be declared EARLIER and whose output space
    /// must fit the hole's.
    | FromNode of nodeId: string

/// A node in a capability pipeline. `Source` is a named data ref (the `DataSource.Ref` precedent) with a
/// declared output value-space; `Invoke` runs a registered capability, declaring its output value-space
/// and wiring each arg (by hole address) to a `Literal` or an upstream node's output.
type PipelineNode =
    /// An input the host supplies: `dataRef` is opaque to the core, and `outputType` is what
    /// downstream edges are checked against.
    | Source of id: string * dataRef: string * outputType: ValueSpace
    /// A call of `capabilityId`, each argument keyed by hole address; `outputType` is declared, not
    /// derived, and every downstream edge is checked against it.
    | Invoke of id: string * capabilityId: string * outputType: ValueSpace * args: (string * ArgSource) list

/// A serializable, typed capability-DAG (Phase 35) — nodes in topological (declaration) order; edges are
/// the `FromNode` arg references. `OpStream.Dag` can host it; type-checked at composition.
type CapabilityPipeline =
    {
        /// The nodes in declaration order, which must be a topological order: an edge may point only
        /// to an earlier node, and node ids must be unique.
        Nodes: PipelineNode list
    }

/// Why a pipeline was rejected — recoverable + enumerated (GP5), never a throw (GP4). Default-deny by
/// shape: only a registered capability + a type-compatible, fully-bound, ACYCLIC edge set in
/// declaration order composes. The cases keep their tags where they survived (`EdgeTypeMismatch` is
/// still the sixth).
///
/// Since Phase 295 an argument refusal wraps the `InvokeError` the node's capability gives the same
/// argument — `UnknownArg` naming the declared holes, `ArgOutOfSpace` carrying the space and the
/// value, `UninvocableArg`, `RequiredArgsUnbound` — as `PackLoadError.PackRegisterFailed` wraps a
/// registration's, so a pipeline refusal says what the equivalent invocation refusal says. It
/// replaced `PipelineUnknownArg`, `PipelineArgOutOfSpace` and `PipelineRequiredUnbound`, which
/// carried the address alone.
type PipelineError =
    /// Two nodes share this id; checked before anything else.
    | DuplicateNode of id: string
    /// A `FromNode` edge names an id that is no node of the pipeline.
    | UnknownNode of id: string
    /// An `Invoke` names a capability the lookup does not resolve; `known` is the lookup's ids.
    | PipelineNoSuchCapability of id: string * known: string list
    /// The node's capability refuses this argument (Phase 295): the wrapped `InvokeError` is the
    /// refusal `Capability.validateArgs` gives it.
    | PipelineArgRefused of node: string * reason: InvokeError
    /// An edge closes a cycle (Phase 295): `cycle` is the node ids around it, starting at `node`
    /// and following each `FromNode` edge to its upstream; a self-edge is `[node]`.
    | PipelineCycle of node: string * cycle: string list
    /// The upstream's output space does not fit argument `arg` of `node`. `producer` and `consumer`
    /// name only the space FAMILIES (`int`, `float`, `string`, `enum`, `anyString`, `slotTree`), so a
    /// bounds mismatch inside one family reads as e.g. `int` against `int`.
    | EdgeTypeMismatch of node: string * arg: string * producer: string * consumer: string
    /// An acyclic edge that points FORWARD in declaration order (Phase 295) — the pipeline is not in
    /// topological order, so the upstream would not have run.
    | PipelineForwardEdge of node: string * arg: string * upstream: string

/// A pipeline node's argument, resolved for evaluation (Phase 62): an upstream node's realised output, or
/// a declared literal. The engine resolves each `ArgSource` edge into one of these before handing the arg
/// list to the host `body` — so the body sees values, never the wire-level `FromNode` reference.
type PipelineArg<'v> =
    /// The upstream node's realised value, already checked (through `spell`) to lie in the hole's space.
    | FromUpstream of 'v
    /// The node's `Literal` string, as declared; `typeCheck` put it in the hole's space.
    | LiteralArg of string

/// Why pipeline evaluation failed — recoverable + named (GP5), never a throw (GP4). Since Phase 295
/// evaluation type-checks first: a pipeline `typeCheck` refuses is `EvalIllTyped`, and no body runs —
/// which is also what retired `EvalUnknownNode`, since a forward reference is now a typeCheck refusal.
/// An upstream value outside the space of the hole it feeds is `EvalArgRefused`, wrapping the
/// `ArgOutOfSpace` the capability gives it; a host `body` failure is `EvalNodeFailed`.
type PipelineEvalError =
    /// `typeCheck` refused the pipeline; no body ran.
    | EvalIllTyped of reason: PipelineError
    /// The host `body` answered `Error message` for this node; evaluation stops there.
    | EvalNodeFailed of node: string * message: string
    /// An upstream value, spelled, lies outside the space of the hole it feeds; `reason` is the
    /// `ArgOutOfSpace` carrying that spelling.
    | EvalArgRefused of node: string * reason: InvokeError

/// Where a pipeline resolves its `Invoke` nodes' capabilities (Phase 295): a lookup and the ids it
/// holds (for the refusal that names them). Both registries project one — `ofRegistry`,
/// `ofFunctionRegistry` — so a host that loads content packs into a `FunctionRegistry` type-checks
/// and evaluates against it, and keeps no second registry.
type CapabilityLookup =
    {
        /// Resolves a capability id; `None` makes an `Invoke` of it a `PipelineNoSuchCapability`.
        TryFind: string -> Capability option
        /// The ids `TryFind` resolves, reported in `PipelineNoSuchCapability`; it is not consulted to
        /// resolve, so a hand-built lookup should keep the two in step.
        Known: string list
    }

/// The two projections onto `CapabilityLookup`.
module CapabilityLookup =

    /// A capability registry as a lookup.
    let ofRegistry (r: CapabilityRegistry) : CapabilityLookup =
        { TryFind = fun id -> CapabilityRegistry.tryFind id r
          Known = r.Capabilities |> Map.toList |> List.map fst }

    /// A function registry as a lookup: each entry's capability, by its id.
    let ofFunctionRegistry (r: FunctionRegistry) : CapabilityLookup =
        { TryFind = fun id -> FunctionRegistry.tryFind id r |> Option.map (fun e -> e.Capability)
          Known = r.Entries |> Map.toList |> List.map fst }

/// Build / type-check / key / serialise / **evaluate** a `CapabilityPipeline`. Additive over the
/// `Capability` surface; FSharp.Core-only, Fable-clean.
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
    /// capability takes, refused as `Capability.validateArgs` refuses it and wrapped
    /// `PipelineArgRefused`; a `Literal` is in its hole's space; a `FromNode` edge names a declared
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
        | Some d -> Error(DuplicateNode d)
        | None ->
            let rec go =
                function
                | [] -> Ok()
                | Source _ :: rest -> go rest
                | Invoke(nid, capId, _, args) :: rest ->
                    match lookup.TryFind capId with
                    | None -> Error(PipelineNoSuchCapability(capId, lookup.Known))
                    | Some cap ->
                        let holes = cap.Signature.Holes
                        let declared = holes |> List.map (fun h -> h.Addr)

                        let edgeFault (addr: string) (up: string) (argSpace: ValueSpace) =
                            match Map.tryFind up nodeById with
                            | None -> Some(UnknownNode up)
                            | Some _ when up = nid -> Some(PipelineCycle(nid, [ nid ]))
                            | Some upNode ->
                                if position.[up] > position.[nid] then
                                    match pathTo nid up with
                                    | Some path -> Some(PipelineCycle(nid, nid :: path))
                                    | None -> Some(PipelineForwardEdge(nid, addr, up))
                                elif spaceFeeds (nodeOutputType upNode) argSpace then
                                    None
                                else
                                    Some(
                                        EdgeTypeMismatch(nid, addr, spaceTag (nodeOutputType upNode), spaceTag argSpace)
                                    )

                        let argFault (addr: string, src: ArgSource) =
                            match src with
                            | Literal v ->
                                Capability.argFault cap declared (addr, v)
                                |> Option.map (fun e -> PipelineArgRefused(nid, e))
                            | FromNode up ->
                                match holes |> List.tryFind (fun h -> h.Addr = addr) with
                                | None -> Some(PipelineArgRefused(nid, UnknownArg(addr, declared)))
                                | Some h ->
                                    match h.Space with
                                    | None -> Some(PipelineArgRefused(nid, UninvocableArg addr))
                                    | Some argSpace -> edgeFault addr up argSpace

                        match args |> List.tryPick argFault with
                        | Some e -> Error e
                        | None ->
                            let bound = args |> List.map fst |> Set.ofList

                            let unbound =
                                holes
                                |> List.filter (fun h -> h.Required && not (bound.Contains h.Addr))
                                |> List.map (fun h -> h.Addr)

                            if List.isEmpty unbound then
                                go rest
                            else
                                Error(PipelineArgRefused(nid, RequiredArgsUnbound unbound))

            go p.Nodes

    /// Enumerate the pipeline's nodes in declaration (topological) order — the stable discovery surface.
    let enumerate (p: CapabilityPipeline) : PipelineNode list = p.Nodes

    /// The Phase-27 capture key a node's realized result is journalled under: a readable prefix
    /// (`source#<id>#` / `<capId>#<id>#`) + a hash of the node's canonical pre-image. A consumer
    /// threads this as `OpStream.captureEffect`'s `eff` argument, so the whole dataflow replays
    /// byte-identically. Since Phase 225 the hashed pre-image goes through `Hash.canonicalFields`
    /// and covers EVERY component — the ids as well as the arg references, each binding three
    /// fields (addr, `L`/`N`, value) and the bindings in sorted order — so it is injective: two
    /// nodes share a pre-image only when they share every component, whatever `#` or `=` an id or
    /// a literal contains. (It joined `addr=L:value` on the empty string before, the collision
    /// `Query.invocationKey`'s `key_collision` finding named.)
    let nodeInvocationKey (n: PipelineNode) : string =
        match n with
        | Source(id, dref, _) -> "source#" + id + "#" + Hash.fnv1a (Hash.canonicalFields [ id; dref ])
        | Invoke(id, capId, _, args) ->
            let bindings =
                args
                |> List.map (fun (a, s) ->
                    match s with
                    | Literal v -> Hash.canonicalFields [ a; "L"; v ]
                    | FromNode up -> Hash.canonicalFields [ a; "N"; up ])
                |> List.sort
                |> String.concat ""

            capId
            + "#"
            + id
            + "#"
            + Hash.fnv1a (Hash.canonicalFields [ capId; id ] + bindings)

    // ---- wire codec ----

    // Phase 310 — the value-space codec is `SpaceCodec` (Phase 295), not a second copy of it; the
    // one difference, the sentence for an unknown space, is this codec's and is kept.

    let private spaceToJ (s: ValueSpace) : JVal = SpaceCodec.toJson s

    let private spaceFromJ: Decoder<ValueSpace> =
        fun el ->
            SpaceCodec.decoder el
            |> Result.mapError (fun e ->
                match e.Code, e.Path, Decoder.tryMember "$type" el with
                | DecodeCode.UnknownTag, [ PathSegment.Key "$type" ], Some(JStr other) ->
                    { e with
                        Message = "unknown value-space: " + other }
                | _ -> e)

    /// Dispatch on `$type`; a miss keeps this codec's sentence `<what><tag>`.
    let private dispatch (what: string) (cases: (string * Decoder<'T>) list) : Decoder<'T> =
        fun el ->
            Decoder.tagDispatch "$type" cases el
            |> Result.mapError (fun e ->
                match e.Code, e.Path, Decoder.tryMember "$type" el with
                | DecodeCode.UnknownTag, [ PathSegment.Key "$type" ], Some(JStr other) ->
                    { e with Message = what + other }
                | _ -> e)

    let private argSrcToJ (s: ArgSource) : JVal =
        match s with
        | Literal v -> Canon.typed "literal" [ "value", JStr v ]
        | FromNode n -> Canon.typed "fromNode" [ "node", JStr n ]

    let private argSrcFromJ: Decoder<ArgSource> =
        dispatch
            "unknown arg source: "
            [ "literal", Decoder.field "value" Decoder.str |> Decoder.map Literal
              "fromNode", Decoder.field "node" Decoder.str |> Decoder.map FromNode ]

    let private argToJ (addr: string, s: ArgSource) : JVal =
        JObj [ "addr", JStr addr; "source", argSrcToJ s ]

    let private argFromJ (el: JVal) : Result<string * ArgSource, DecodeError> =
        Decoder.field "addr" Decoder.str el
        |> Result.bind (fun addr -> Decoder.field "source" argSrcFromJ el |> Result.map (fun s -> addr, s))

    let private nodeToJ (n: PipelineNode) : JVal =
        match n with
        | Source(id, dref, ty) ->
            Canon.typed "source" [ "id", JStr id; "dataRef", JStr dref; "outputType", spaceToJ ty ]
        | Invoke(id, capId, ty, args) ->
            Canon.typed
                "invoke"
                [ "id", JStr id
                  "capabilityId", JStr capId
                  "outputType", spaceToJ ty
                  "args", JArr(args |> List.map argToJ) ]

    let private nodeFromJ: Decoder<PipelineNode> =
        let str name = Decoder.field name Decoder.str

        let source (el: JVal) =
            str "id" el
            |> Result.bind (fun id ->
                str "dataRef" el
                |> Result.bind (fun dref ->
                    Decoder.field "outputType" spaceFromJ el
                    |> Result.map (fun ty -> Source(id, dref, ty))))

        let invoke (el: JVal) =
            str "id" el
            |> Result.bind (fun id ->
                str "capabilityId" el
                |> Result.bind (fun capId ->
                    Decoder.field "outputType" spaceFromJ el
                    |> Result.bind (fun ty ->
                        Decoder.field "args" (Decoder.list argFromJ) el
                        |> Result.map (fun args -> Invoke(id, capId, ty, args)))))

        dispatch "unknown pipeline node: " [ "source", source; "invoke", invoke ]

    /// Encode a pipeline to its canonical wire string.
    let encode (p: CapabilityPipeline) : string =
        Canon.render (JObj [ "nodes", JArr(p.Nodes |> List.map nodeToJ) ])

    /// Decode a pipeline from a wire string, answering a typed refusal (Phase 310): its code, the
    /// path to the value at fault, and [[decode]]'s sentence. A parse failure is refused at the root.
    let decodeDetailed (s: string) : Result<CapabilityPipeline, DecodeError> =
        Decoder.parse s
        |> Result.bind (Decoder.field "nodes" (Decoder.list nodeFromJ))
        |> Result.map (fun nodes -> { Nodes = nodes })

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
                                    Error(EvalArgRefused(nid, ArgOutOfSpace(addr, space, spelled)))
                            // Unreachable after `typeCheck`: an edge names an earlier node into a
                            // spaced hole of a resolved capability. Refused as the type-check would.
                            | None, _ -> Error(EvalIllTyped(PipelineForwardEdge(nid, addr, up)))
                            | Some _, None -> Error(EvalIllTyped(PipelineArgRefused(nid, UninvocableArg addr)))))
                |> Result.map List.rev

        resolved
        |> Result.bind (fun args ->
            match body n args with
            | Ok v -> Ok v
            | Error m -> Error(EvalNodeFailed(nid, m)))

    /// The reference evaluator (Phase 62): fold the host `body` over the pipeline in declaration
    /// (topological) order, threading an `id → value` result map. `body` receives each node and its args
    /// with every `FromNode` edge already resolved to the upstream value. Total — an ill-typed pipeline,
    /// an upstream value outside its hole's space, or a body failure is a named `PipelineEvalError`,
    /// never a throw. The cross-host compute contract the incremental `evalFrom` is certified
    /// byte-identical to.
    ///
    /// **It type-checks first (Phase 295).** `eval` takes the capability lookup and refuses a pipeline
    /// `typeCheck` refuses as `EvalIllTyped`, before any body runs, so it is not a second dispatch path
    /// beside `CapabilityRegistry.dispatch`: a node runs only where its capability resolves and its
    /// arguments are ones that capability takes. `spell` writes an upstream value as the argument
    /// string the seam reads (the identity for a `string` pipeline), and a value outside the space of
    /// the hole it feeds is `EvalArgRefused`.
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
        | Error e -> Error(EvalIllTyped e)
        | Ok() ->
            let rec go (results: Map<string, 'v>) =
                function
                | [] -> Ok results
                | n :: rest ->
                    runNode lookup spell body results n
                    |> Result.bind (fun v -> go (Map.add (nodeId n) v results) rest)

            go Map.empty p.Nodes

    /// The dirty set for a changed-input set: `changed` ∪ every node transitively downstream of it via
    /// `FromNode` edges (the reverse-reachability closure). Minimal — a node not reachable from any change
    /// is never included. Pure over the DAG's edge structure.
    let dirtySet (changed: Set<string>) (p: CapabilityPipeline) : Set<string> =
        // dependents: for each `FromNode up` edge into a node, `up` gains that node as a dependent.
        let dependents =
            [ for n in p.Nodes do
                  match n with
                  | Invoke(id, _, _, args) ->
                      for _, src in args do
                          match src with
                          | FromNode up -> yield up, id
                          | Literal _ -> ()
                  | Source _ -> () ]
            |> List.groupBy fst
            |> List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)
            |> Map.ofList

        let rec grow (frontier: Set<string>) (acc: Set<string>) =
            if Set.isEmpty frontier then
                acc
            else
                let next =
                    (Set.empty, frontier)
                    ||> Set.fold (fun s node ->
                        match Map.tryFind node dependents with
                        | Some deps -> Set.union s deps
                        | None -> s)

                let fresh = Set.difference next acc
                grow fresh (Set.union acc fresh)

        grow changed changed

    /// Incrementally re-evaluate a pipeline (Phase 62) given the PRIOR evaluation and the set of
    /// changed-input node ids: re-invoke only the nodes downstream-of-change (`dirtySet`), reusing each
    /// clean node's prior result. **Byte-identical to a full `eval` over the same inputs** (the Phase-34
    /// discipline, `Conformance.capabilityPipelineIncrementalLaws`) — a clean node's inputs are unchanged,
    /// so its `eval` result is exactly its prior result; a dirty node re-invokes the body against the new
    /// upstream values. Effect-honesty on the dirty path: a clean node reuses its recorded value (the
    /// Phase-53 gate), an uncaptured live node on the dirty path re-invokes, so incrementality never serves
    /// a stale effect result. A node absent from `prior` (never evaluated) is always (re-)evaluated.
    /// It type-checks first, exactly as `eval` does (Phase 295), so the two refuse the same pipelines.
    let evalFrom
        (lookup: CapabilityLookup)
        (spell: 'v -> string)
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (p: CapabilityPipeline)
        : Result<Map<string, 'v>, PipelineEvalError> =
        match typeCheck lookup p with
        | Error e -> Error(EvalIllTyped e)
        | Ok() ->
            let dirty = dirtySet changed p

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
