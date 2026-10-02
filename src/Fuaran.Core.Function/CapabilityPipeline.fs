namespace Fuaran.Core

// ============================================================================
//  Serializable capability pipeline (Phase 35) — a typed capability-DAG declared
//  as DATA, so a multi-step compute (load → invoke → derive → invoke) is
//  replayable end-to-end through the Phase-27 capture seam, not host-side glue.
//  A node's typed output feeds a downstream node's typed arg; an ill-typed edge
//  is a named error at composition time, never a runtime throw.
//
//  Nodes are `Source` (a named data ref) + `Invoke` (a registered capability).
//  A DataFrame `Transform` step participates as a `Capability` whose body runs the
//  evaluator — so the pipeline needs NO dedicated transform node and `Function`
//  takes no `DataFrame` dependency (GP1/GP2: additive over the Capability surface;
//  the data strand stays downstream). FSharp.Core only, Fable-clean.
// ============================================================================

/// Where an `Invoke` node's argument comes from: a literal scalar, or the output of an upstream node
/// (the edge that wires the DAG).
type ArgSource =
    | Literal of value: string
    | FromNode of nodeId: string

/// A node in a capability pipeline. `Source` is a named data ref (the `DataSource.Ref` precedent) with a
/// declared output value-space; `Invoke` runs a registered capability, declaring its output value-space
/// and wiring each arg (by hole address) to a `Literal` or an upstream node's output.
type PipelineNode =
    | Source of id: string * dataRef: string * outputType: ValueSpace
    | Invoke of id: string * capabilityId: string * outputType: ValueSpace * args: (string * ArgSource) list

/// A serializable, typed capability-DAG (Phase 35) — nodes in topological (declaration) order; edges are
/// the `FromNode` arg references. `OpStream.Dag` can host it; type-checked at composition.
type CapabilityPipeline = { Nodes: PipelineNode list }

/// Why a pipeline was rejected — recoverable + enumerated (GP5), never a throw (GP4). Default-deny by
/// shape: only a registered capability + a type-compatible, fully-bound edge set composes.
type PipelineError =
    | DuplicateNode of id: string
    | UnknownNode of id: string
    | PipelineNoSuchCapability of id: string * known: string list
    | PipelineUnknownArg of node: string * arg: string
    | PipelineArgOutOfSpace of node: string * arg: string
    | EdgeTypeMismatch of node: string * arg: string * producer: string * consumer: string
    | PipelineRequiredUnbound of node: string * addrs: string list

/// A pipeline node's argument, resolved for evaluation (Phase 62): an upstream node's realised output, or
/// a declared literal. The engine resolves each `ArgSource` edge into one of these before handing the arg
/// list to the host `body` — so the body sees values, never the wire-level `FromNode` reference.
type PipelineArg<'v> =
    | FromUpstream of 'v
    | LiteralArg of string

/// Why pipeline evaluation failed — recoverable + named (GP5), never a throw (GP4). A `FromNode` edge that
/// references a node not yet evaluated (a forward reference — the pipeline is out of topological order) is
/// `EvalUnknownNode`; a host `body` failure is `EvalNodeFailed`.
type PipelineEvalError =
    | EvalUnknownNode of node: string * missing: string
    | EvalNodeFailed of node: string * message: string

/// Build / type-check / key / serialise / **evaluate** a `CapabilityPipeline`. Additive over the
/// `Capability` surface; FSharp.Core-only, Fable-clean.
module CapabilityPipeline =

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

    /// Does a producer output value-space `feed` a consumer arg value-space — every value the producer
    /// can emit is acceptable to the consumer (int feeds int or widens to float; a string space feeds
    /// `AnyString`; otherwise same family).
    let private spaceFeeds (producer: ValueSpace) (consumer: ValueSpace) : bool =
        match producer, consumer with
        | IntRange _, (IntRange _ | FloatRange _) -> true
        | FloatRange _, FloatRange _ -> true
        | StringLen _, (StringLen _ | AnyString) -> true
        | Enum _, (Enum _ | AnyString) -> true
        | AnyString, AnyString -> true
        | SlotTree _, SlotTree None -> true
        | a, b -> a = b

    /// Type-check the pipeline against the registry (Phase 35): node ids are unique; every `Invoke`'s
    /// capability resolves (default-deny); every arg addresses a declared scalar hole; a `Literal` is
    /// in-space; a `FromNode` edge's producer output-type `feeds` the arg's value-space (an ill-typed
    /// edge is a named `EdgeTypeMismatch`); every required hole is bound. Total.
    let typeCheck (reg: CapabilityRegistry) (p: CapabilityPipeline) : Result<unit, PipelineError> =
        let ids = p.Nodes |> List.map nodeId
        let nodeById = p.Nodes |> List.map (fun n -> nodeId n, n) |> Map.ofList

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
                    match Registry.tryFind capId reg with
                    | None -> Error(PipelineNoSuchCapability(capId, reg.Capabilities |> Map.toList |> List.map fst))
                    | Some cap ->
                        let holes = cap.Signature.Holes

                        let argFault (addr, src) =
                            match holes |> List.tryFind (fun h -> h.Addr = addr) with
                            | None -> Some(PipelineUnknownArg(nid, addr))
                            | Some h ->
                                match h.Space with
                                | None -> Some(PipelineUnknownArg(nid, addr)) // a spaceless (action) hole — not feedable
                                | Some argSpace ->
                                    match src with
                                    | Literal v ->
                                        if Space.validate argSpace v then
                                            None
                                        else
                                            Some(PipelineArgOutOfSpace(nid, addr))
                                    | FromNode up ->
                                        match Map.tryFind up nodeById with
                                        | None -> Some(UnknownNode up)
                                        | Some upNode ->
                                            if spaceFeeds (nodeOutputType upNode) argSpace then
                                                None
                                            else
                                                Some(
                                                    EdgeTypeMismatch(
                                                        nid,
                                                        addr,
                                                        spaceTag (nodeOutputType upNode),
                                                        spaceTag argSpace
                                                    )
                                                )

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
                                Error(PipelineRequiredUnbound(nid, unbound))

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

    // Phase 310 — the value-space codec is the capability codec's, not a second copy of it; the one
    // difference, the sentence for an unknown space, is this codec's and is kept.

    let private spaceToJ (s: ValueSpace) : JVal = CapabilityCodec.spaceJson s

    let private spaceFromJ: Decoder<ValueSpace> =
        fun el ->
            CapabilityCodec.spaceOfDetailed el
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
    // analogue of `DataFrame.evalPipelineWith resolve` (Phase 34) — the pipeline plumbing is Core's, the
    // compute is the host's.

    /// Resolve one node's args against the results-so-far, then run the host `body`. Shared by `eval` and
    /// `evalFrom` so the two agree by construction. A `Literal` passes through as a `LiteralArg`; a
    /// `FromNode up` resolves to `up`'s realised value (a forward reference is `EvalUnknownNode`).
    let private runNode
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (results: Map<string, 'v>)
        (n: PipelineNode)
        : Result<'v, PipelineEvalError> =
        let nid = nodeId n

        let resolved =
            match n with
            | Source _ -> Ok []
            | Invoke(_, _, _, args) ->
                (Ok [], args)
                ||> List.fold (fun acc (addr, src) ->
                    acc
                    |> Result.bind (fun xs ->
                        match src with
                        | Literal s -> Ok((addr, LiteralArg s) :: xs)
                        | FromNode up ->
                            match Map.tryFind up results with
                            | Some v -> Ok((addr, FromUpstream v) :: xs)
                            | None -> Error(EvalUnknownNode(nid, up))))
                |> Result.map List.rev

        resolved
        |> Result.bind (fun args ->
            match body n args with
            | Ok v -> Ok v
            | Error m -> Error(EvalNodeFailed(nid, m)))

    /// The reference evaluator (Phase 62): fold the host `body` over the pipeline in declaration
    /// (topological) order, threading an `id → value` result map. `body` receives each node and its args
    /// with every `FromNode` edge already resolved to the upstream value. Total — a forward `FromNode`
    /// reference or a body failure is a named `PipelineEvalError`, never a throw. The cross-host compute
    /// contract the incremental `evalFrom` is certified byte-identical to.
    ///
    /// **It stays SYNCHRONOUS, by decision (Phase 210's routed-out question, operator decision
    /// 2026-09-19).** `Capability.invoke` and both dispatchers carry the `Deferred` envelope; `body`
    /// here returns a plain `Result` and does not. Enveloping it would be a RESUMPTION model rather
    /// than a retype — a fold that meets `Pending` at one node must say what becomes of every node
    /// after it, and `evalFrom`'s byte-identical contract would have to hold across the suspension —
    /// and no demand for one has been measured. Asynchrony belongs at the leaves: a host that must
    /// wait resolves its `Deferred` invocations before it folds the pipeline.
    let eval
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (p: CapabilityPipeline)
        : Result<Map<string, 'v>, PipelineEvalError> =
        let rec go (results: Map<string, 'v>) =
            function
            | [] -> Ok results
            | n :: rest ->
                runNode body results n
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
    let evalFrom
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (p: CapabilityPipeline)
        : Result<Map<string, 'v>, PipelineEvalError> =
        let dirty = dirtySet changed p

        let rec go (results: Map<string, 'v>) =
            function
            | [] -> Ok results
            | n :: rest ->
                let nid = nodeId n

                if not (Set.contains nid dirty) && Map.containsKey nid prior then
                    go (Map.add nid (Map.find nid prior) results) rest
                else
                    runNode body results n |> Result.bind (fun v -> go (Map.add nid v results) rest)

        go Map.empty p.Nodes
