module Fuaran.Core.Tests.ProofOracle.CapabilityPipelineTests

// The Proofs.Oracle differential for proofs/Capability.fst, continued: the pipeline, the capability and pipeline codecs, and the seam codec (split from ProofOracle.CapabilityTests.fs for length; the shared renderers and generators are read from there).

open System.IO
open Expecto

module ModelCap = Capability
module ModelProp = Propagation

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.CapabilityOracle
open Fuaran.Core.Tests.ProofOracle.CapabilityTests

// ---- the pipeline ----

/// The two float comparisons `Space.subsumes` makes, exactly as production writes them.
let private feedReaders: ModelCap.feed_readers =
    let f (s: string) = System.Double.Parse(s, inv)

    { ModelCap.feed_readers.float_within = fun rl rh al ah -> f rl <= f al && f ah <= f rh
      ModelCap.feed_readers.int_within = fun rl rh al ah -> f rl <= float (int al) && float (int ah) <= f rh }

let private argSrcToModel (s: ArgSource) : ModelCap.arg_source =
    match s with
    | ArgSource.Literal v -> ModelCap.Literal v
    | ArgSource.FromNode n -> ModelCap.FromNode n

let private nodeToModel (n: PipelineNode) : ModelCap.pipeline_node =
    match n with
    | PipelineNode.Source(id, dref, ty) -> ModelCap.Source(id, dref, spaceToModel ty)
    | PipelineNode.Invoke(id, capId, ty, args) ->
        ModelCap.Invoke(id, capId, spaceToModel ty, args |> List.map (fun (a, s) -> a, argSrcToModel s))

let private pipeToModel (p: CapabilityPipeline) : ModelCap.pipeline =
    { ModelCap.pipeline.p_nodes = p.Nodes |> List.map nodeToModel }

let private outputTypeOf (n: PipelineNode) : ValueSpace =
    match n with
    | PipelineNode.Source(_, _, ty) -> ty
    | PipelineNode.Invoke(_, _, ty, _) -> ty

let private upstreamsOf (n: PipelineNode) : string list =
    match n with
    | PipelineNode.Source _ -> []
    | PipelineNode.Invoke(_, _, _, args) ->
        args
        |> List.choose (fun (_, s) ->
            match s with
            | ArgSource.FromNode up -> Some up
            | ArgSource.Literal _ -> None)

/// Declaration order is a topological order of the edges — `ordered [] p.p_nodes`, computed here
/// from production's nodes and from nothing the model wrote.
let private isTopological (nodes: PipelineNode list) : bool =
    let rec go (earlier: Set<string>) =
        function
        | [] -> true
        | n :: rest ->
            let nid = CapabilityPipeline.nodeId n

            not (earlier.Contains nid)
            && List.forall earlier.Contains (upstreamsOf n)
            && go (earlier.Add nid) rest

    go Set.empty nodes

let private prodPipeErrRender (e: PipelineError) : string =
    match e with
    | PipelineError.DuplicateNode id -> sprintf "DuplicateNode(%s)" id
    | PipelineError.UnknownNode id -> sprintf "UnknownNode(%s)" id
    | PipelineError.PipelineNoSuchCapability(id, known) ->
        sprintf "PipelineNoSuchCapability(%s;%s)" id (String.concat "," (List.sort known))
    | PipelineError.PipelineArgRefused(n, r) -> sprintf "PipelineArgRefused(%s;%s)" n (prodInvokeErrRender r)
    | PipelineError.PipelineCycle(n, c) -> sprintf "PipelineCycle(%s;%s)" n (String.concat ">" c)
    | PipelineError.EdgeTypeMismatch(n, a, p, c) -> sprintf "EdgeTypeMismatch(%s;%s;%s;%s)" n a p c
    | PipelineError.PipelineForwardEdge(n, a, u) -> sprintf "PipelineForwardEdge(%s;%s;%s)" n a u

let private modelPipeErrRender (e: ModelCap.pipeline_error) : string =
    match e with
    | ModelCap.DuplicateNode id -> sprintf "DuplicateNode(%s)" id
    | ModelCap.UnknownNode id -> sprintf "UnknownNode(%s)" id
    | ModelCap.PipelineNoSuchCapability(id, known) ->
        sprintf "PipelineNoSuchCapability(%s;%s)" id (String.concat "," (List.sort known))
    | ModelCap.PipelineArgRefused(n, r) -> sprintf "PipelineArgRefused(%s;%s)" n (modelInvokeErrRender r)
    | ModelCap.PipelineCycle(n, c) -> sprintf "PipelineCycle(%s;%s)" n (String.concat ">" c)
    | ModelCap.EdgeTypeMismatch(n, a, p, c) -> sprintf "EdgeTypeMismatch(%s;%s;%s;%s)" n a p c
    | ModelCap.PipelineForwardEdge(n, a, u) -> sprintf "PipelineForwardEdge(%s;%s;%s)" n a u

let private pipeError: Differential<PipelineError, ModelCap.pipeline_error> =
    Differential.pair prodPipeErrRender modelPipeErrRender

let private prodEvalErrRender (e: PipelineEvalError) : string =
    match e with
    | PipelineEvalError.EvalIllTyped r -> sprintf "EvalIllTyped(%s)" (prodPipeErrRender r)
    | PipelineEvalError.EvalNodeFailed(n, m) -> sprintf "EvalNodeFailed(%s;%s)" n m
    | PipelineEvalError.EvalArgRefused(n, r) -> sprintf "EvalArgRefused(%s;%s)" n (prodInvokeErrRender r)
    // Phase 383 — the policy admission the model does not carry; the oracle's lookups run no gate,
    // so it is never answered here, and a render that reached it would differ from every model render.
    | PipelineEvalError.EvalPolicyRefused(n, r) -> sprintf "EvalPolicyRefused(%s;%s)" n (prodInvokeErrRender r)

let private modelEvalErrRender (e: ModelCap.pipeline_eval_error) : string =
    match e with
    | ModelCap.EvalIllTyped r -> sprintf "EvalIllTyped(%s)" (modelPipeErrRender r)
    | ModelCap.EvalNodeFailed(n, m) -> sprintf "EvalNodeFailed(%s;%s)" n m
    | ModelCap.EvalArgRefused(n, r) -> sprintf "EvalArgRefused(%s;%s)" n (modelInvokeErrRender r)

/// An evaluation's answer, rendered: the result map by id, or the refusal.
let private prodEvalRender (r: Result<Map<string, string>, PipelineEvalError>) : string =
    match r with
    | Ok m ->
        "Ok "
        + (Map.toList m |> List.map (fun (k, v) -> k + "=" + v) |> String.concat ";")
    | Error e -> "Error " + prodEvalErrRender e

/// The model's result map is a list read latest-first (`Map.add` is the cons), so it is folded
/// oldest-first into a map before it is rendered.
let private modelEvalRender (r: ModelCap.outcome<(string * string) list, ModelCap.pipeline_eval_error>) : string =
    match r with
    | ModelCap.Ok l ->
        "Ok "
        + (Map.ofList (List.rev l)
           |> Map.toList
           |> List.map (fun (k, v) -> k + "=" + v)
           |> String.concat ";")
    | ModelCap.Error e -> "Error " + modelEvalErrRender e

/// The evaluation's render pair: the result map or the refusal, on each side.
let private evaluation
    : Differential<
          Result<Map<string, string>, PipelineEvalError>,
          ModelCap.outcome<(string * string) list, ModelCap.pipeline_eval_error>
       > =
    Differential.pair prodEvalRender modelEvalRender

/// A value that lies in a well-formed space.
let private validValueFor (s: ValueSpace) : string =
    match s with
    | IntRange(lo, _) -> lo.ToString(inv)
    | FloatRange(lo, _) -> lo.ToString("R", inv)
    | StringLen(lo, _) -> String.replicate (max lo 0) "s"
    | Enum(x :: _) -> x
    | Enum [] -> "zz"
    | AnyString -> "v"
    | SlotTree c -> sprintf """{"kind":"%s"}""" (defaultArg c "k0")

/// A registry on both sides holding up to the three pool ids, each tried up to three times so
/// most registries hold all of them (the admission gate refuses a good share of the draws).
let private genRegistryPair (rd: ModelCap.readers) (r: ConfRng.T) : CapabilityRegistry * ModelCap.registry * ConfRng.T =
    let mutable rng = r
    let mutable preg = CapabilityRegistry.empty
    let mutable mreg = ModelCap.empty

    for id in capIdPool do
        let mutable placed = false

        for k in 1..3 do
            if not placed then
                let sg, r1 = genCapSignature (id + string k) rng
                rng <- r1
                let cap = Capability.create id sg Server

                match CapabilityRegistry.register cap preg, ModelCap.register rd (capToModel cap) mreg with
                | Ok p', ModelCap.Ok m' ->
                    preg <- p'
                    mreg <- m'
                    placed <- true
                | _ -> ()

    preg, mreg, rng

let private withArgs (n: PipelineNode) (f: (string * ArgSource) list -> (string * ArgSource) list) : PipelineNode =
    match n with
    | PipelineNode.Invoke(id, capId, ty, args) -> PipelineNode.Invoke(id, capId, ty, f args)
    | other -> other

let private isInvokeWithArgs (n: PipelineNode) : bool =
    match n with
    | PipelineNode.Invoke(_, _, _, _ :: _) -> true
    | _ -> false

/// Rewrite the first node `pick` holds of.
let private rewriteFirst (pick: PipelineNode -> bool) (f: PipelineNode -> PipelineNode) (ns: PipelineNode list) =
    match List.tryFindIndex pick ns with
    | Some i -> ns |> List.mapi (fun k n -> if k = i then f n else n)
    | None -> ns

/// A pipeline over a registry. Each `Invoke` binds every required hole of a registered
/// capability: a literal in its space, a fresh `Source` whose output IS the hole's space (declared
/// first), an earlier node whatever it outputs (so an edge may be ill-typed), or a literal drawn
/// against the space (so it may lie outside it). Then, two pipelines in five, ONE fault: a repeated
/// node id, the first node moved last (a forward edge), a self-edge, a cycle through two nodes, an
/// edge to no node, an argument at no hole, an argument bound twice, an argument dropped, or the
/// whole declaration reversed.
let private genPipeline (preg: CapabilityRegistry) (r: ConfRng.T) : CapabilityPipeline * ConfRng.T =
    let mutable rng = r
    let nodes = System.Collections.Generic.List<PipelineNode>()
    let registered = CapabilityRegistry.enumerate preg
    let count, r0 = ConfRng.intBelow 4 rng
    rng <- r0
    let mutable fresh = 0

    for i in 1..count do
        let pick, r1 = ConfRng.intBelow 12 rng
        rng <- r1
        let nid = sprintf "n%d" i

        if List.isEmpty registered || pick = 0 then
            nodes.Add(PipelineNode.Invoke(nid, "cap-x", AnyString, []))
        else
            let cap, r2 = ConfRng.choose registered rng
            let outTy, r3 = genSpace r2
            rng <- r3
            let args = System.Collections.Generic.List<string * ArgSource>()

            for e in cap.Signature.Holes do
                match e.Space with
                | Some sp when e.Required ->
                    let how, r4 = ConfRng.intBelow 10 rng
                    rng <- r4

                    if how < 4 then
                        args.Add(e.Addr, ArgSource.Literal(validValueFor sp))
                    elif how < 7 then
                        fresh <- fresh + 1
                        let sid = sprintf "s%d" fresh
                        nodes.Add(PipelineNode.Source(sid, "ref-" + sid, sp))
                        args.Add(e.Addr, ArgSource.FromNode sid)
                    elif how < 9 && nodes.Count > 0 then
                        let k, r5 = ConfRng.intBelow nodes.Count rng
                        rng <- r5
                        args.Add(e.Addr, ArgSource.FromNode(CapabilityPipeline.nodeId nodes[k]))
                    else
                        let v, r5 = genValueFor sp rng
                        rng <- r5
                        args.Add(e.Addr, ArgSource.Literal v)
                | _ -> ()

            nodes.Add(PipelineNode.Invoke(nid, cap.Id, outTy, List.ofSeq args))

    let fault, rf = ConfRng.intBelow 22 rng
    rng <- rf
    let ns = List.ofSeq nodes

    let faulted =
        match fault, ns with
        | 0, first :: _ -> ns @ [ PipelineNode.Source(CapabilityPipeline.nodeId first, "dup", AnyString) ]
        | 1, first :: rest -> rest @ [ first ]
        | 2, _ ->
            ns
            |> rewriteFirst isInvokeWithArgs (fun n ->
                withArgs n (fun args ->
                    match args with
                    | (a, _) :: tl -> (a, ArgSource.FromNode(CapabilityPipeline.nodeId n)) :: tl
                    | [] -> []))
        | 3, _ ->
            // a cycle through two nodes: the first `Invoke` with arguments gains an edge to a
            // LATER node that already reaches it.
            (match List.tryFindIndex isInvokeWithArgs ns with
             | Some i ->
                 let aId = CapabilityPipeline.nodeId ns[i]

                 (match
                     ns
                     |> List.indexed
                     |> List.tryFind (fun (k, n) -> k > i && List.contains aId (upstreamsOf n))
                  with
                  | Some(_, b) ->
                      ns
                      |> List.mapi (fun k n ->
                          if k = i then
                              withArgs n (fun args ->
                                  match args with
                                  | (a, _) :: tl -> (a, ArgSource.FromNode(CapabilityPipeline.nodeId b)) :: tl
                                  | [] -> [])
                          else
                              n)
                  | None -> ns)
             | None -> ns)
        | 4, _ ->
            ns
            |> rewriteFirst isInvokeWithArgs (fun n ->
                withArgs n (fun args ->
                    match args with
                    | (a, _) :: tl -> (a, ArgSource.FromNode "n9") :: tl
                    | [] -> []))
        | 5, _ ->
            ns
            |> rewriteFirst isInvokeWithArgs (fun n ->
                withArgs n (fun args -> args @ [ "root/zz", ArgSource.Literal "1" ]))
        | 6, _ ->
            ns
            |> rewriteFirst isInvokeWithArgs (fun n ->
                withArgs n (fun args ->
                    match args with
                    | first :: _ -> args @ [ first ]
                    | [] -> []))
        | 7, _ -> ns |> rewriteFirst isInvokeWithArgs (fun n -> withArgs n List.tail)
        | 8, _ -> List.rev ns
        | _ -> ns

    ({ Nodes = faulted }: CapabilityPipeline), rng

/// The pipeline's tally: the type-check and evaluation verdicts, the incremental evaluations and
/// the values they reused, the reorderings (and the topological ones among them), the stale
/// reuses the hypothesis rules out, the space pairs compared, and the refusal classes.
let private pipeTally =
    Tally.empty
        [ "Accepted"
          "Refused"
          "Evaluated"
          "EvalRefused"
          "Incremental"
          "Reused"
          "Reordered"
          "ReorderedTopological"
          "StaleLost"
          "Subsumes" ]
    |> Tally.sets [ "Classes" ]

let private pipeDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : Tally =
    let step (i: int) (r0: ConfRng.T) (tally: Tally) =
        let mutable rng = r0

        // THE SPACE RELATION, on its own: production's `Space.subsumes` against the model's over a
        // drawn pair, so an edge's verdict below is not the only place the relation is compared.
        let sa, ra = genSpace rng
        let sb, rb = genSpace ra
        rng <- rb

        let tally =
            tally
            |> Tally.bump "Subsumes"
            |> Differential.check
                (Space.subsumes sa sb = ModelCap.subsumes feedReaders rd (spaceToModel sa) (spaceToModel sb))
                (fun () -> sprintf "seed %d: subsumes differs on %A against %A" i sa sb)

        let preg, mreg, r1 = genRegistryPair rd rng
        let p, r2 = genPipeline preg r1
        rng <- r2
        let lookup = CapabilityLookup.ofRegistry preg
        let mlookup = ModelCap.lookup_of_registry mreg
        let mp = pipeToModel p

        // ---- typeCheck ----
        let pt = CapabilityPipeline.typeCheck lookup p
        let mt = ModelCap.type_check feedReaders rd mlookup mp

        let tally =
            match pt, mt with
            | Ok(), ModelCap.Ok() -> Tally.bump "Accepted" tally
            | Error pe, ModelCap.Error me ->
                tally
                |> Tally.bump "Refused"
                |> Tally.reach "Classes" (errClass pipeError.Production pe)
                |> Differential.compareWith
                    pipeError
                    (fun ps ms -> sprintf "seed %d: typeCheck refused differently\n  prod %s\n  model %s" i ps ms)
                    pe
                    me
            | Ok(), ModelCap.Error me ->
                Tally.disagree
                    (sprintf
                        "seed %d: typeCheck accepted by production, refused by the model (%s)"
                        i
                        (pipeError.Model me))
                    tally
            | Error pe, ModelCap.Ok() ->
                Tally.disagree
                    (sprintf
                        "seed %d: typeCheck refused by production (%s), accepted by the model"
                        i
                        (pipeError.Production pe))
                    tally

        // `typecheck_topological`, on the shipped function: an accepted pipeline is in topological
        // order, and another declaration of the same nodes is accepted exactly when it is too.
        let tally =
            if pt = Ok() then
                let tally =
                    Differential.check
                        (isTopological p.Nodes)
                        (fun () ->
                            sprintf "seed %d: typeCheck accepted a declaration that is not in topological order" i)
                        tally

                if List.length p.Nodes > 1 then
                    let k, rk = ConfRng.intBelow (List.length p.Nodes) rng
                    rng <- rk
                    let rotated = List.skip k p.Nodes @ List.take k p.Nodes

                    let p': CapabilityPipeline =
                        { Nodes = (if k = 0 then List.rev rotated else rotated) }

                    let pt' = CapabilityPipeline.typeCheck lookup p'
                    let mt' = ModelCap.type_check feedReaders rd mlookup (pipeToModel p')
                    let tally = Tally.bump "Reordered" tally

                    let tally =
                        if isTopological p'.Nodes then
                            tally
                            |> Tally.bump "ReorderedTopological"
                            |> Differential.check (pt' = Ok()) (fun () ->
                                sprintf
                                    "seed %d: typeCheck's verdict moved under another topological order of the same nodes"
                                    i)
                        else
                            match pt' with
                            | Error(PipelineError.PipelineForwardEdge _) -> tally
                            | other ->
                                Tally.disagree
                                    (sprintf
                                        "seed %d: a reordering out of topological order was not refused as a forward edge (%A)"
                                        i
                                        other)
                                    tally

                    match pt', mt' with
                    | Ok(), ModelCap.Ok() -> tally
                    | Error pe, ModelCap.Error me when pipeError.Production pe = pipeError.Model me -> tally
                    | _ -> Tally.disagree (sprintf "seed %d: typeCheck disagrees on the reordered declaration" i) tally
                else
                    tally
            else
                tally

        // ---- eval ----
        // The host body is a table: each node's answer drawn once — a value for its output space
        // (in it four draws in five), or a failure one draw in twelve — and a log of what it was
        // handed, so the RESOLVED ARGUMENTS are compared and not only the answers.
        let table = System.Collections.Generic.Dictionary<string, Result<string, string>>()

        for n in p.Nodes do
            let roll, ra1 = ConfRng.intBelow 12 rng
            let v, ra2 = genValueFor (outputTypeOf n) ra1
            rng <- ra2

            table[CapabilityPipeline.nodeId n] <-
                (if roll = 0 then Error "boom"
                 elif roll < 10 then Ok(validValueFor (outputTypeOf n))
                 else Ok v)

        let renderArgs (render: 'a -> string) (args: (string * 'a) list) =
            args |> List.map (fun (a, x) -> a + "=" + render x) |> String.concat ","

        let pArg (a: PipelineArg<string>) =
            match a with
            | FromUpstream v -> "U:" + v
            | LiteralArg s -> "L:" + s

        let mArg (a: ModelCap.pipeline_arg<string>) =
            match a with
            | ModelCap.FromUpstream v -> "U:" + v
            | ModelCap.LiteralArg s -> "L:" + s

        let pBodyOf
            (tbl: System.Collections.Generic.Dictionary<string, Result<string, string>>)
            (log: ResizeArray<string>)
            =
            fun (n: PipelineNode) (args: (string * PipelineArg<string>) list) ->
                log.Add(CapabilityPipeline.nodeId n + "(" + renderArgs pArg args + ")")
                tbl[CapabilityPipeline.nodeId n]

        let mBodyOf
            (tbl: System.Collections.Generic.Dictionary<string, Result<string, string>>)
            (log: ResizeArray<string>)
            =
            fun (n: ModelCap.pipeline_node) (args: (string * ModelCap.pipeline_arg<string>) list) ->
                log.Add(ModelCap.node_id n + "(" + renderArgs mArg args + ")")

                match tbl[ModelCap.node_id n] with
                | Ok v -> ModelCap.Ok v
                | Error m -> ModelCap.Error m

        let pLog = ResizeArray<string>()
        let mLog = ResizeArray<string>()
        let pe0 = CapabilityPipeline.eval lookup id (pBodyOf table pLog) p
        let me0 = ModelCap.eval feedReaders rd mlookup id (mBodyOf table mLog) mp

        let evaluates (tally: Tally) =
            match pt, pe0 with
            | Error e, Error(PipelineEvalError.EvalIllTyped e') when e = e' ->
                // `pipeline_refused_never_evaluated`: the refusal is the type check's, and no body ran.
                Differential.check
                    (pLog.Count = 0)
                    (fun () -> sprintf "seed %d: eval ran a body on a pipeline typeCheck refused" i)
                    tally
            | Error _, _ -> Tally.disagree (sprintf "seed %d: eval did not answer the type check's refusal" i) tally
            | Ok(), Error(PipelineEvalError.EvalIllTyped _) ->
                // `pipeline_illtyped_iff_refused`: never, over an accepted pipeline.
                Tally.disagree
                    (sprintf
                        "seed %d: eval answered PipelineEvalError.EvalIllTyped over a pipeline typeCheck ACCEPTED"
                        i)
                    tally
            | Ok(), Ok _ -> Tally.bump "Evaluated" tally
            | Ok(), Error _ -> Tally.bump "EvalRefused" tally

        let tally =
            tally
            |> Differential.compareWith
                evaluation
                (fun ps ms -> sprintf "seed %d: eval differs\n  prod %s\n  model %s" i ps ms)
                pe0
                me0
            |> Differential.check (List.ofSeq pLog = List.ofSeq mLog) (fun () ->
                sprintf "seed %d: eval ran the body on different nodes or arguments" i)
            |> evaluates

        // ---- dependencyMap, the dirty set and evalFrom ----
        // Phase 544: the model's dirty set is Propagation's closure instantiated at `deps_of`, and
        // production's is Propagation's closure over `dependencyMap`, so the differential compares
        // the INSTANTIATION (the edge relation each map names, which is what `pipeline_edge_iff`
        // and the two closure theorems are stated over) and then the closure over it — model
        // against production on both, never production against itself.
        let pDeps = CapabilityPipeline.dependencyMap p
        let mDeps = ModelCap.deps_of mp.p_nodes

        let pEdges =
            pDeps
            |> Map.toSeq
            |> Seq.collect (fun (n, rs) -> rs |> Seq.map (fun r -> n, r))
            |> Set.ofSeq

        let mEdges =
            mDeps |> Seq.collect (fun (n, rs) -> rs |> Seq.map (fun r -> n, r)) |> Set.ofSeq

        let nodeIds = p.Nodes |> List.map CapabilityPipeline.nodeId
        let mutable changed = []

        for nid in nodeIds do
            let c, rc = ConfRng.intBelow 4 rng
            rng <- rc

            if c = 0 then
                changed <- nid :: changed

        let stray, rs = ConfRng.intBelow 8 rng
        rng <- rs

        if stray = 0 then
            changed <- "n9" :: changed

        let changedSet = Set.ofList changed
        let changedList = Set.toList changedSet
        let pDirty = Propagation.dirtyFromChangedIds pDeps changedSet
        let mDirty = Set.ofList (ModelProp.dirty_from_changed_ids mDeps changedList)

        let tally =
            tally
            |> Differential.check (pEdges = mEdges) (fun () ->
                sprintf "seed %d: dependencyMap's edges differ (%A vs %A)" i pEdges mEdges)
            |> Differential.check (pDirty = mDirty) (fun () ->
                sprintf "seed %d: the dirty set differs (%A vs %A)" i pDirty mDirty)

        // The new body: the old one, re-drawn at exactly the changed nodes — `agree_off`.
        let table1 =
            System.Collections.Generic.Dictionary<string, Result<string, string>>(table)

        for n in p.Nodes do
            let nid = CapabilityPipeline.nodeId n

            if changedSet.Contains nid then
                let roll, rb1 = ConfRng.intBelow 12 rng
                rng <- rb1

                table1[nid] <-
                    (if roll = 0 then
                         Error "boom2"
                     else
                         Ok(validValueFor (outputTypeOf n)))

        // A prior: the first evaluation's result where it finished, a partial map otherwise (the
        // model agrees on any prior; the theorem is about the first kind).
        let prior =
            match pe0 with
            | Ok m -> m
            | Error _ -> nodeIds |> List.truncate 1 |> List.map (fun nid -> nid, "stale") |> Map.ofList

        let pLog1 = ResizeArray<string>()
        let mLog1 = ResizeArray<string>()

        let pf =
            CapabilityPipeline.evalFrom lookup id (pBodyOf table1 pLog1) prior changedSet p

        let mf =
            ModelCap.eval_from feedReaders rd mlookup id (mBodyOf table1 mLog1) (Map.toList prior) changedList mp

        let tally =
            tally
            |> Differential.compareWith
                evaluation
                (fun ps ms -> sprintf "seed %d: evalFrom differs\n  prod %s\n  model %s" i ps ms)
                pf
                mf
            |> Differential.check (List.ofSeq pLog1 = List.ofSeq mLog1) (fun () ->
                sprintf "seed %d: evalFrom ran the body on different nodes or arguments" i)

        let tally =
            match pe0 with
            | Ok _ ->
                // `pipeline_evalfrom_agrees`, on the shipped functions: over a finished prior
                // evaluation and a body that moved only at the changed nodes, the incremental answer
                // IS the full one.
                let full =
                    CapabilityPipeline.eval lookup id (pBodyOf table1 (ResizeArray<string>())) p

                // THE HYPOTHESIS IS NEEDED: a body that also moves at a node the caller did NOT name
                // is a stale reuse. Counted, not asserted per probe — the unnamed node must be one
                // `evalFrom` reuses for the two to part.
                let staleLost =
                    match nodeIds |> List.tryFind (fun nid -> not (pDirty.Contains nid)) with
                    | Some unnamed ->
                        let table2 =
                            System.Collections.Generic.Dictionary<string, Result<string, string>>(table1)

                        table2[unnamed] <- Ok "moved-unnamed"

                        let stale =
                            CapabilityPipeline.evalFrom
                                lookup
                                id
                                (pBodyOf table2 (ResizeArray<string>()))
                                prior
                                changedSet
                                p

                        let fresh =
                            CapabilityPipeline.eval lookup id (pBodyOf table2 (ResizeArray<string>())) p

                        stale <> fresh
                    | None -> false

                tally
                |> Tally.bump "Incremental"
                |> Differential.check (pf = full) (fun () ->
                    sprintf
                        "seed %d: evalFrom is not eval over the same inputs\n  evalFrom %s\n  eval     %s"
                        i
                        (prodEvalRender pf)
                        (prodEvalRender full))
                |> Tally.add "Reused" (List.length nodeIds - pLog1.Count |> max 0)
                |> Tally.bumpIf staleLost "StaleLost"
            | Error _ -> tally

        rng, tally

    Differential.seeded trials (ConfRng.ofSeed seed) step pipeTally

// ---- the codecs ----

/// The go-red: a `float_fault` that finds nothing, so a float range with a NaN, an infinite or an
/// inverted bound is well-formed to the model and refused by production's readers.
let private blindFaultReaders: ModelCap.readers =
    { readers with
        ModelCap.readers.float_fault = fun _ _ -> FStar_Pervasives_Native.None }

let private prodDecodeErrRender (e: DecodeError) : string =
    sprintf
        "%A@%s"
        e.Code
        (e.Path
         |> List.map (function
             | PathSegment.Key k -> "k:" + k
             | PathSegment.Index n -> "i:" + string n)
         |> String.concat "/")

let private modelDecodeErrRender (e: ModelCap.decode_error) : string =
    sprintf
        "%A@%s"
        e.d_code
        (e.d_path
         |> List.map (function
             | ModelCap.Key k -> "k:" + k
             | ModelCap.Index n -> "i:" + string n)
         |> String.concat "/")

/// The decode refusal's render pair: its code and its path. The query codec reads it too.
let decodeError: Differential<DecodeError, ModelCap.decode_error> =
    Differential.pair prodDecodeErrRender modelDecodeErrRender

/// The codecs' tally: the capabilities encoded, the documents read and their verdicts, the
/// well-formed round trips, the payload refusals, the byte round trips, the signature and
/// pipeline documents, and the refusal codes the pool reached.
let private codecTally =
    Tally.empty
        [ "Encoded"
          "Documents"
          "Accepted"
          "Refused"
          "WellFormed"
          "PayloadRefused"
          "Bytes"
          "Signatures"
          "Nodes"
          "NodeDocuments" ]
    |> Tally.sets [ "Codes" ]

let private codecDifferential (rd: ModelCap.readers) (seed: int) (trials: int) : Tally =
    let placements =
        [ BuildTime
          Server
          ClientDeclarative
          Precomputed
          ClientIsland Pyodide
          ClientIsland Fable
          ClientIsland Js ]

    let withEntry (k: int) (f: (string * JVal) list -> (string * JVal) list) (j: JVal) : JVal =
        let onHoles =
            List.map (fun (name, v) ->
                match name, v with
                | "holes", JArr es ->
                    name,
                    JArr(
                        es
                        |> List.mapi (fun n e ->
                            match e with
                            | JObj fs when n = k -> JObj(f fs)
                            | other -> other)
                    )
                | _ -> name, v)

        match j with
        | JObj fs ->
            JObj(
                fs
                |> List.map (fun (name, v) ->
                    match name, v with
                    | "signature", JObj sfs -> name, JObj(onHoles sfs)
                    | _ -> name, v)
            )
        | other -> other

    let payload = set [ "space"; "actionEffect"; "slotKind" ]

    let step (i: int) (r0: ConfRng.T) (tally: Tally) =
        let mutable rng = r0

        // ---- a capability: a derived signature. Since Phase 409 an entry IS its hole kind, so the
        //      two shapes only a hand-built entry had (a spaceless slot, a kind that is no hole kind's)
        //      cannot be built; the reader meets their documents below, as targeted mutations ----
        let sg, r1 = genCapSignature (sprintf "c%d" i) rng
        let placement, r2 = ConfRng.choose placements r1
        rng <- r2

        let cap = Capability.create (sprintf "cap-%d" i) sg placement
        let mcap = capToModel cap
        let j0 = CapabilityCodec.encodeJson cap

        // `capability_roundtrip_identity` and its converse, on the shipped codec: decode after
        // encode is the identity EXACTLY on the well-formed capabilities.
        let wf = ModelCap.wf_capability rd mcap

        let roundTrips (tally: Tally) =
            match CapabilityCodec.decodeJsonDetailedWith ReadPolicy.Lenient j0 with
            | Ok c when c = cap ->
                Differential.check
                    wf
                    (fun () -> sprintf "seed %d: a capability that is not well-formed read back as itself" i)
                    tally
            | Ok _ ->
                Tally.disagree
                    (sprintf "seed %d: a capability read back as another (Phase 409: no normal form)" i)
                    tally
            | Error e ->
                Differential.check
                    (not wf)
                    (fun () ->
                        sprintf "seed %d: a well-formed capability was refused (%s)" i (decodeError.Production e))
                    tally

        // ... and through the BYTES, where the document can be written: `Canonical.write Rendering.CrossHost` then the
        // parser, which is where a whole float comes back as an integer token.
        let throughBytes (tally: Tally) =
            match CapabilityCodec.tryEncode cap with
            | Ok s ->
                let tally = Tally.bump "Bytes" tally

                match CapabilityCodec.decode s with
                | Ok c when c = cap ->
                    Differential.check
                        wf
                        (fun () -> sprintf "seed %d: through the bytes, an ill-formed capability read back as itself" i)
                        tally
                | Ok _
                | Error _ ->
                    Differential.check
                        (not wf)
                        (fun () -> sprintf "seed %d: through the bytes, a well-formed capability did not read back" i)
                        tally
            | Error _ -> tally

        let tally =
            tally
            |> Tally.bump "Encoded"
            |> Differential.check (jvalToModel j0 = ModelCap.capability_json mcap) (fun () ->
                sprintf "seed %d: the capability encodes differently" i)
            |> Tally.bumpIf wf "WellFormed"
            |> roundTrips
            |> throughBytes

        // ---- the reader, over the encoding, three mutations of it, and the targeted ones: each
        //      entry's kind payload removed (a value or repeat entry without its `space`, an action
        //      entry without its `actionEffect`, a slot without its `slotKind`), and its tag replaced
        //      by one outside `HoleKind.tags` ----
        let mutable docs = [ j0 ]

        for _ in 1..3 do
            let m, rm = jmutate j0 rng
            rng <- rm
            docs <- m :: docs

        for k in 0 .. List.length sg.Holes - 1 do
            docs <-
                withEntry k (List.filter (fun (name, _) -> not (payload.Contains name))) j0
                :: docs

            docs <-
                withEntry k (List.map (fun (name, v) -> if name = "kind" then name, JStr "int" else name, v)) j0
                :: docs

        let readCapability (j: JVal) (tally: Tally) =
            let tally = Tally.bump "Documents" tally
            let p = CapabilityCodec.decodeJsonDetailedWith ReadPolicy.Lenient j
            let m = ModelCap.capability_of_j rd codecReaders (jvalToModel j)

            let tally =
                match p, m with
                | Ok pc, ModelCap.Ok mc ->
                    tally
                    |> Tally.bump "Accepted"
                    |> Differential.check (capToModel pc = mc) (fun () ->
                        sprintf "seed %d: a capability document decoded to different values" i)
                    // `capability_decoded_wf`: whatever was accepted is well-formed.
                    |> Differential.check (ModelCap.wf_capability rd (capToModel pc)) (fun () ->
                        sprintf "seed %d: the reader accepted a capability that is not well-formed" i)
                | Error pe, ModelCap.Error me ->
                    tally
                    |> Tally.bump "Refused"
                    |> Tally.reach "Codes" (sprintf "%A" pe.Code)
                    |> Tally.bumpIf
                        (match pe.Code, List.tryLast pe.Path with
                         | DecodeCode.MissingField, Some(PathSegment.Key("space" | "actionEffect")) -> true
                         | _ -> false)
                        "PayloadRefused"
                    |> Differential.compareWith
                        decodeError
                        (fun ps ms ->
                            sprintf
                                "seed %d: a capability document was refused differently\n  prod %s\n  model %s"
                                i
                                ps
                                ms)
                        pe
                        me
                | Ok _, ModelCap.Error me ->
                    Tally.disagree
                        (sprintf
                            "seed %d: a capability document accepted by production, refused by the model (%s)"
                            i
                            (decodeError.Model me))
                        tally
                | Error pe, ModelCap.Ok _ ->
                    Tally.disagree
                        (sprintf
                            "seed %d: a capability document refused by production (%s), accepted by the model"
                            i
                            (decodeError.Production pe))
                        tally

            // the signature reader on its own, where the document still carries one
            match Decoder.tryMember "signature" j with
            | Some sj ->
                let tally = Tally.bump "Signatures" tally

                match CapabilityCodec.signatureOf sj, ModelCap.signature_of_j rd codecReaders (jvalToModel sj) with
                | Ok ps, ModelCap.Ok ms ->
                    Differential.check
                        (sigToModel ps = ms)
                        (fun () -> sprintf "seed %d: a signature document decoded to different values" i)
                        tally
                | Error _, ModelCap.Error _ -> tally
                | _ -> Tally.disagree (sprintf "seed %d: the signature reader's verdict differs" i) tally
            | None -> tally

        let tally = Differential.over docs readCapability tally

        // ---- a pipeline: its encoding byte for byte, and its reader over the parsed document ----
        let preg, _, r4 = genRegistryPair rd rng
        let pl, r5 = genPipeline preg r4
        rng <- r5
        let mpl = pipeToModel pl

        let tally =
            tally
            |> Tally.add "Nodes" (List.length pl.Nodes)
            |> Differential.check
                (CapabilityPipeline.encode pl = Canonical.write
                    Rendering.CrossHost
                    (jvalOfModel (ModelCap.pipeline_json mpl)))
                (fun () -> sprintf "seed %d: the pipeline encodes differently" i)

        let readPipeline (j: JVal) (tally: Tally) =
            let tally = Tally.bump "NodeDocuments" tally
            let text = Canonical.write Rendering.CrossHost j

            match Decoder.parse ReadOptions.strict text with
            | Error _ -> Tally.disagree (sprintf "seed %d: a mutated pipeline document did not parse" i) tally
            | Ok reparsed ->
                match
                    CapabilityPipeline.decodeDetailed text,
                    ModelCap.pipeline_of_j rd codecReaders (jvalToModel reparsed)
                with
                | Ok pp, ModelCap.Ok mpp ->
                    Differential.check
                        (pipeToModel pp = mpp)
                        (fun () -> sprintf "seed %d: a pipeline document decoded to different values" i)
                        tally
                | Error pe, ModelCap.Error me ->
                    tally
                    |> Tally.reach "Codes" (sprintf "%A" pe.Code)
                    |> Differential.compareWith
                        decodeError
                        (fun ps ms ->
                            sprintf
                                "seed %d: a pipeline document was refused differently\n  prod %s\n  model %s"
                                i
                                ps
                                ms)
                        pe
                        me
                | _ -> Tally.disagree (sprintf "seed %d: the pipeline reader's verdict differs" i) tally

        let tally =
            match CapabilityPipeline.tryEncode pl with
            | Error _ -> tally
            | Ok s ->
                // `node_roundtrip`, on the shipped codec: the identity exactly where every node's
                // output space is well-formed.
                let allWf =
                    pl.Nodes |> List.forall (fun n -> Space.wellFormed (outputTypeOf n) = Ok())

                let tally =
                    match CapabilityPipeline.decodeDetailed s with
                    | Ok back when back = pl ->
                        Differential.check
                            allWf
                            (fun () ->
                                sprintf "seed %d: a pipeline with an ill-formed output space read back as itself" i)
                            tally
                    | Ok _ -> Tally.disagree (sprintf "seed %d: a pipeline read back as another" i) tally
                    | Error e ->
                        Differential.check
                            (not allWf)
                            (fun () ->
                                sprintf "seed %d: a well-formed pipeline was refused (%s)" i (decodeError.Production e))
                            tally

                match Decoder.parse ReadOptions.strict s with
                | Error _ -> Tally.disagree (sprintf "seed %d: the pipeline's own encoding did not parse" i) tally
                | Ok parsed ->
                    let mutable pdocs = [ parsed ]

                    for _ in 1..3 do
                        let m, rm = jmutate parsed rng
                        rng <- rm
                        pdocs <- m :: pdocs

                    Differential.over pdocs readPipeline tally

        rng, tally

    Differential.seeded trials (ConfRng.ofSeed seed) step codecTally

// ---- Phase 455 — the seam codec, whole: the invocation, the async envelope and the typed refusal ----

let private faultToModel (f: DeclarationFault) : ModelCap.decl_fault =
    match f with
    | EmptySpace(a, s) -> ModelCap.EmptySpace(a, spaceToModel s)
    | NonFiniteBound a -> ModelCap.NonFiniteBound a
    | DuplicateHoleAddr a -> ModelCap.DuplicateHoleAddr a
    | HoleUnderSlot n -> ModelCap.HoleUnderSlot n

/// Production's twelve-case `InvokeError` as the model's `gated_error`.
let private invokeErrorToModel (e: InvokeError) : ModelCap.gated_error =
    match e with
    | NoSuchCapability(id, known) -> ModelCap.GRefused(ModelCap.NoSuchCapability(id, known))
    | DuplicateCapability id -> ModelCap.GRefused(ModelCap.DuplicateCapability id)
    | UnknownArg(a, d) -> ModelCap.GRefused(ModelCap.UnknownArg(a, d))
    | ArgOutOfSpace(a, s, g) -> ModelCap.GRefused(ModelCap.ArgOutOfSpace(a, spaceToModel s, g))
    | RequiredArgsUnbound xs -> ModelCap.GRefused(ModelCap.RequiredArgsUnbound xs)
    | UninvocableArg a -> ModelCap.GRefused(ModelCap.UninvocableArg a)
    | BodyFailed r -> ModelCap.GRefused(ModelCap.BodyFailed r)
    | NonTotalCapability(id, xs) -> ModelCap.GRefused(ModelCap.NonTotalCapability(id, xs))
    | IllFormedCapability(id, f) -> ModelCap.GRefused(ModelCap.IllFormedCapability(id, faultToModel f))
    | DuplicateArg a -> ModelCap.GRefused(ModelCap.DuplicateArg a)
    | PolicyRefused(p, r, a) -> ModelCap.GPolicyRefused(p, r, a)
    | ApprovalRequired p -> ModelCap.GApprovalRequired p

let private seamNames = [ "a"; "b"; "c"; "p" ]

let private genNames (r: ConfRng.T) : string list * ConfRng.T =
    let n, r1 = ConfRng.intBelow 3 r
    let mutable rr = r1

    let xs =
        [ for _ in 1..n do
              let x, r2 = ConfRng.choose seamNames rr
              rr <- r2
              x ]

    xs, rr

let private genInvokeError (r: ConfRng.T) : InvokeError * ConfRng.T =
    let k, r1 = ConfRng.intBelow 16 r
    let s, r2 = ConfRng.choose seamNames r1
    let xs, r3 = genNames r2
    let sp, r4 = genSpace r3

    let e =
        match k with
        | 0 -> NoSuchCapability(s, xs)
        | 1 -> DuplicateCapability s
        | 2 -> UnknownArg(s, xs)
        | 3 -> ArgOutOfSpace(s, sp, "got")
        | 4 -> RequiredArgsUnbound xs
        | 5 -> UninvocableArg s
        | 6 -> BodyFailed "boom"
        | 7 -> NonTotalCapability(s, xs)
        | 8 -> IllFormedCapability(s, EmptySpace(s, sp))
        | 9 -> IllFormedCapability(s, NonFiniteBound s)
        | 10 -> IllFormedCapability(s, DuplicateHoleAddr s)
        | 11 -> IllFormedCapability(s, HoleUnderSlot s)
        | 12 -> DuplicateArg s
        | 13 -> PolicyRefused(s, "no", xs)
        | _ -> ApprovalRequired s

    e, r4

/// The seam codec's tally: the documents read, the refusals of each codec (and of an unmutated
/// invocation, where only a repeated address refuses it), the values encoded, and the refusal
/// codes the pool reached.
let private seamCodecTally =
    Tally.empty
        [ "Documents"
          "InvocationRefused"
          "RepeatRefused"
          "ErrorsEncoded"
          "ErrorsRefused"
          "Deferred"
          "DeferredRefused"
          "SentenceDeferredRefused" ]
    |> Tally.sets [ "Codes" ]

/// Runs the model's invocation, envelope and refusal codecs beside production's: the encoding, the
/// reading of each encoding and of mutations of it (acceptance, the value, and — where production
/// answers a typed refusal — its code and path), and, on production, that every typed refusal
/// RESOLVES in the document it read (`DecodeError.resolvesIn`, the theorem's statement). The model's
/// invocation writer is a parameter so a go-red can hand it a writer that drops an argument.
let private seamCodecDifferential
    (invocationJson: string -> (string * string) list -> ModelCap.jval)
    (seed: int)
    (trials: int)
    : Tally =
    let step (i: int) (r0: ConfRng.T) (tally: Tally) =
        let rng = ref r0

        let mutations (j: JVal) =
            let mutable out = [ j ]

            for _ in 1..4 do
                let m, r = jmutate j rng.Value
                rng.Value <- r
                out <- m :: out

            out

        // ---- the invocation: addresses drawn WITH replacement, so a repeat arises ----
        let cid, r1 = ConfRng.choose [ "cap-a"; "cap-b" ] rng.Value
        let n, r2 = ConfRng.intBelow 4 r1
        rng.Value <- r2

        let args =
            [ for k in 1..n do
                  let a, r = ConfRng.choose seamNames rng.Value
                  rng.Value <- r
                  a, sprintf "v%d" k ]

        let text = CapabilityCodec.encodeInvocation cid args

        let readInvocation (j0: JVal) (jm: JVal) (tally: Tally) =
            let tally = Tally.bump "Documents" tally
            // The document production reads is the text the mutation renders to, so the model
            // reads exactly that document too.
            let j =
                match Json.parse (Canonical.write Rendering.CrossHost jm) with
                | Ok v -> v
                | Error _ -> jm

            let p =
                CapabilityCodec.decodeInvocationDetailedWith ReadPolicy.Lenient (Canonical.write Rendering.CrossHost j)

            let m = ModelCap.invocation_of_j (jvalToModel j)

            match p, m with
            | Ok(pc, pa), ModelCap.Ok(mc, ma) when pc = mc && pa = ma -> tally
            | Error pe, ModelCap.Error me when decodeError.Production pe = decodeError.Model me ->
                tally
                |> Tally.bump "InvocationRefused"
                |> Tally.reach "Codes" (string pe.Code)
                |> Tally.bumpIf (jm = j0) "RepeatRefused"
                |> Differential.check (DecodeError.resolvesIn j pe) (fun () ->
                    sprintf
                        "seed %d: an invocation refusal does not resolve: %s in %s"
                        i
                        (decodeError.Production pe)
                        (Canonical.write Rendering.CrossHost j))
            | _ ->
                Tally.disagree
                    (sprintf
                        "seed %d: the invocation readers disagree on %s: %A vs %A"
                        i
                        (Canonical.write Rendering.CrossHost j)
                        p
                        m)
                    tally

        let tally =
            match Json.parse text with
            | Error e -> Tally.disagree (sprintf "seed %d: the invocation did not parse: %A" i e) tally
            | Ok j0 ->
                // The encoding is compared as the canonical TEXT both write: `encodeInvocation` renders it.
                let tally =
                    Differential.check
                        (Canonical.write Rendering.CrossHost (jvalOfModel (invocationJson cid args)) = text)
                        (fun () -> sprintf "seed %d: the invocation encodes differently: %s" i text)
                        tally

                Differential.over (mutations j0) (readInvocation j0) tally

        // ---- the typed refusal: every case, a value space and a declaration fault inside ----
        let e, r3 = genInvokeError rng.Value
        rng.Value <- r3
        let ej = CapabilitySeamCodec.encodeInvokeError e

        let readInvokeError (j: JVal) (tally: Tally) =
            let tally = Tally.bump "Documents" tally

            match CapabilitySeamCodec.decodeInvokeError j, ModelCap.invoke_error_of_j codecReaders (jvalToModel j) with
            | Ok pe, ModelCap.Ok me when invokeErrorToModel pe = me -> tally
            | Error _, ModelCap.Error me ->
                tally
                |> Tally.bump "ErrorsRefused"
                |> Tally.reach "Codes" (string (modelCodeOf me.d_code))
            | p, m ->
                Tally.disagree
                    (sprintf
                        "seed %d: the invoke-error readers disagree on %s: %A vs %A"
                        i
                        (Canonical.write Rendering.CrossHost j)
                        p
                        m)
                    tally

        let tally =
            tally
            |> Tally.bump "ErrorsEncoded"
            |> Differential.check (jvalToModel ej = ModelCap.invoke_error_json (invokeErrorToModel e)) (fun () ->
                sprintf "seed %d: the invoke error encodes differently: %A" i e)

        let tally = Differential.over (mutations ej) readInvokeError tally

        // ---- the async envelope, over a string payload ----
        let k, r4 = ConfRng.intBelow 3 rng.Value
        rng.Value <- r4

        let d =
            match k with
            | 0 -> Pending
            | 1 -> Ready(sprintf "x%d" i)
            | _ -> Failed "late"

        let md =
            match d with
            | Pending -> ModelCap.Pending
            | Ready v -> ModelCap.Ready v
            | Failed m -> ModelCap.Failed m

        let dj = CapabilitySeamCodec.encodeDeferred JStr d

        let mEnc (s: string) = ModelCap.JStr s

        let pDec (v: JVal) : Result<string, string> =
            match v with
            | JStr s -> Ok s
            | _ -> Error "not a string"

        let mDec (v: ModelCap.jval) : ModelCap.outcome<string, string> =
            match v with
            | ModelCap.JStr s -> ModelCap.Ok s
            | _ -> ModelCap.Error "not a string"

        let sameRead (p: Deferred<string>) (m: ModelCap.deferred<string>) =
            match p, m with
            | Pending, ModelCap.Pending -> true
            | Ready a, ModelCap.Ready b
            | Failed a, ModelCap.Failed b -> a = b
            | _ -> false

        // Two readers of the one envelope (Phase 570). The TYPED reader,
        // `CapabilitySeamCodec.decodeDeferred` over a typed payload reader, passes the payload's refusal
        // through, so it is compared on code and path against `deferred_dec` over the model's `d_str`.
        // `CapabilityCodec.decodeDeferred`, whose payload reader answers a sentence, raises that refusal
        // `OutOfRange` in production as in `deferred_of_j`; its refusal crosses as a sentence, so it is
        // compared on acceptance and value.
        let readDeferred (j: JVal) (tally: Tally) =
            let tally = Tally.bump "Documents" tally
            let mj = jvalToModel j

            let tally =
                match CapabilitySeamCodec.decodeDeferred Decoder.str j, ModelCap.deferred_dec ModelCap.d_str mj with
                | Ok p, ModelCap.Ok m when sameRead p m -> tally
                | Error pe, ModelCap.Error me when decodeError.Production pe = decodeError.Model me ->
                    tally
                    |> Tally.bump "DeferredRefused"
                    |> Tally.reach "Codes" (string pe.Code)
                    |> Differential.check (DecodeError.resolvesIn j pe) (fun () ->
                        sprintf
                            "seed %d: an envelope refusal does not resolve: %s in %s"
                            i
                            (decodeError.Production pe)
                            (Canonical.write Rendering.CrossHost j))
                | p, m ->
                    Tally.disagree
                        (sprintf
                            "seed %d: the typed envelope readers disagree on %s: %A vs %A"
                            i
                            (Canonical.write Rendering.CrossHost j)
                            p
                            m)
                        tally

            // The text form reads the bytes the document renders to, so the model reads exactly that
            // document too.
            let text = Canonical.write Rendering.CrossHost j

            let jt =
                match Json.parse text with
                | Ok v -> v
                | Error _ -> j

            match CapabilityCodec.decodeDeferred pDec text, ModelCap.deferred_of_j mDec (jvalToModel jt) with
            | Ok p, ModelCap.Ok m when sameRead p m -> tally
            | Error _, ModelCap.Error me ->
                tally
                |> Tally.bump "SentenceDeferredRefused"
                |> Tally.reach "Codes" (string (modelCodeOf me.d_code))
            | p, m ->
                Tally.disagree
                    (sprintf
                        "seed %d: the sentence envelope readers disagree on %s: %A vs %A"
                        i
                        (Canonical.write Rendering.CrossHost j)
                        p
                        m)
                    tally

        let tally =
            tally
            |> Tally.bump "Deferred"
            |> Differential.check (jvalToModel dj = ModelCap.deferred_json mEnc md) (fun () ->
                sprintf "seed %d: the envelope encodes differently: %A" i d)

        let tally = Differential.over (mutations dj) readDeferred tally

        rng.Value, tally

    Differential.seeded trials (ConfRng.ofSeed seed) step seamCodecTally

/// The go-red's writer: the model's invocation writer handed an argument list with its last
/// argument DROPPED.
let private droppingInvocationJson (cid: string) (args: (string * string) list) : ModelCap.jval =
    let kept =
        match List.rev args with
        | _ :: rest -> List.rev rest
        | [] -> []

    ModelCap.invocation_json cid kept


[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [ testCase
              "the pipeline oracle agrees with CapabilityPipeline.typeCheck, eval, dependencyMap and evalFrom, and its dirty set with Propagation.dirtyFromChangedIds over that map, over generated registries, pipelines, reorderings and change sets"
          <| fun _ ->
              let t = underCulture "he-IL" (fun () -> pipeDifferential readers 3542 500)

              Differential.expectAgrees "the pipeline oracle" t
              let n name = Tally.count name t

              // Adequacy. Measured at 500 pipelines: accepted 313, refused 187, evaluated to a
              // result 290, refused in evaluation 23, held to eval incrementally 290 (254
              // values reused), re-declared in another order 133 (79 of them topological), and
              // 137 probes where a body moved at an unnamed node and the agreement broke.
              Expect.isGreaterThan (n "Accepted") 200 (sprintf "pipelines were accepted (accepted=%d)" (n "Accepted"))
              Expect.isGreaterThan (n "Refused") 100 (sprintf "pipelines were refused (refused=%d)" (n "Refused"))

              Expect.isGreaterThan
                  (n "Evaluated")
                  180
                  (sprintf "accepted pipelines evaluated to a result (evaluated=%d)" (n "Evaluated"))

              Expect.isGreaterThan
                  (n "EvalRefused")
                  8
                  (sprintf "accepted pipelines were refused in evaluation (evalRefused=%d)" (n "EvalRefused"))

              Expect.isGreaterThan
                  (n "Incremental")
                  180
                  (sprintf "evalFrom was held to eval over a finished prior (incremental=%d)" (n "Incremental"))

              Expect.isGreaterThan (n "Reused") 120 (sprintf "evalFrom reused prior values (reused=%d)" (n "Reused"))

              Expect.isGreaterThan
                  (n "Reordered")
                  70
                  (sprintf "accepted pipelines were re-declared in another order (reordered=%d)" (n "Reordered"))

              Expect.isGreaterThan
                  (n "ReorderedTopological")
                  30
                  (sprintf
                      "some reorderings were topological orders too (reorderedTopological=%d)"
                      (n "ReorderedTopological"))

              Expect.isGreaterThan
                  (n "StaleLost")
                  60
                  (sprintf
                      "a body that moved at an UNNAMED node broke the agreement — the hypothesis is needed (staleLost=%d)"
                      (n "StaleLost"))

              let reached = Tally.reached "Classes" t

              for cls in
                  [ "DuplicateNode"
                    "UnknownNode"
                    "PipelineNoSuchCapability"
                    "PipelineArgRefused"
                    "PipelineCycle"
                    "EdgeTypeMismatch"
                    "PipelineForwardEdge" ] do
                  Expect.isTrue
                      (Set.contains cls reached)
                      (sprintf "the sample reached a %s refusal (reached: %A)" cls reached)

              Expect.equal
                  (underCulture "he-IL" (fun () -> pipeDifferential readers 3542 500))
                  t
                  "same seed => identical tally"

          testCase
              "a pipeline oracle handed a BLIND int reader DISAGREES with CapabilityPipeline.typeCheck — the measurement can fail"
          <| fun _ ->
              // Under the blind reader a literal in an integer hole is out of space to the model
              // and in space to production, so the model refuses a pipeline production accepts.
              let t = pipeDifferential blindReaders 3542 150
              Differential.expectLoses "a blind int reader on the pipeline" t

              Expect.isTrue
                  (Tally.disagreements t |> List.exists (fun d -> d.Contains "ArgOutOfSpace"))
                  "and the disagreement is about a literal's SPACE check, which is what the reader blinded"

          testCase
              "the codec oracle agrees with CapabilityCodec and the pipeline codec over generated capabilities, pipelines and mutated documents"
          <| fun _ ->
              let t = underCulture "he-IL" (fun () -> codecDifferential readers 3543 300)

              Differential.expectAgrees "the codec oracle" t
              let n name = Tally.count name t

              // Adequacy. Measured at 300 capabilities: 1,200 documents (588 accepted, 612
              // refused), 237 well-formed, 22 read back in a normal form, 294 through the
              // bytes, 1,199 signature documents, 573 pipeline nodes and 1,148 pipeline
              // documents.
              Expect.equal (n "Encoded") 300 "every capability's encoding was compared"

              Expect.isGreaterThan
                  (n "Accepted")
                  350
                  (sprintf "capability documents were accepted (accepted=%d)" (n "Accepted"))

              Expect.isGreaterThan
                  (n "Refused")
                  350
                  (sprintf "capability documents were refused (refused=%d)" (n "Refused"))

              Expect.isGreaterThan
                  (n "WellFormed")
                  150
                  (sprintf "well-formed capabilities read back as themselves (wellFormed=%d)" (n "WellFormed"))

              Expect.isGreaterThan
                  (n "PayloadRefused")
                  100
                  (sprintf
                      "an entry without the member its kind needs was refused at that member (payloadRefused=%d)"
                      (n "PayloadRefused"))

              Expect.isGreaterThan
                  (n "Bytes")
                  200
                  (sprintf "the round trip ran through the bytes (bytes=%d)" (n "Bytes"))

              Expect.isGreaterThan
                  (n "Signatures")
                  800
                  (sprintf "the signature reader was compared on its own (signatures=%d)" (n "Signatures"))

              Expect.isGreaterThan (n "Nodes") 300 (sprintf "pipeline nodes were encoded (nodes=%d)" (n "Nodes"))

              Expect.isGreaterThan
                  (n "NodeDocuments")
                  700
                  (sprintf "pipeline documents were decoded (nodeDocuments=%d)" (n "NodeDocuments"))

              let reached = Tally.reached "Codes" t

              for code in [ "MissingField"; "WrongKind"; "UnknownTag"; "OutOfRange" ] do
                  Expect.isTrue
                      (Set.contains code reached)
                      (sprintf "the sample reached a %s refusal (reached: %A)" code reached)

              Expect.equal
                  (underCulture "he-IL" (fun () -> codecDifferential readers 3543 300))
                  t
                  "same seed => identical tally"

          testCase
              "a codec oracle handed a BLIND float-fault reader DISAGREES with the shipped readers — the measurement can fail"
          <| fun _ ->
              // Under the blind reader a float range with a NaN, an infinite or an inverted bound
              // is well-formed to the model, which then accepts a declaration the shipped reader
              // refuses `OutOfRange`.
              let t = codecDifferential blindFaultReaders 3543 300
              Differential.expectLoses "a blind float-fault reader" t

              Expect.isTrue
                  (Tally.disagreements t |> List.exists (fun d -> d.Contains "OutOfRange"))
                  "and the disagreement is about the admission check on read, which is what the reader blinded"

          // ---- Phase 455 — the seam codec, whole ----

          testCase
              "the seam-codec oracle agrees with CapabilityCodec's invocation, envelope and invoke-error codecs over generated values and mutated documents, and every typed refusal resolves"
          <| fun _ ->
              let t =
                  underCulture "he-IL" (fun () -> seamCodecDifferential ModelCap.invocation_json 4550 300)

              Differential.expectAgrees "the seam-codec oracle" t
              let n name = Tally.count name t

              Expect.isGreaterThan (n "Documents") 3000 (sprintf "documents were read (%d)" (n "Documents"))

              Expect.isGreaterThan
                  (n "RepeatRefused")
                  20
                  (sprintf "a repeated address was refused on an UNMUTATED encoding (%d)" (n "RepeatRefused"))

              Expect.isGreaterThan
                  (n "InvocationRefused")
                  100
                  (sprintf "invocations were refused (%d)" (n "InvocationRefused"))

              Expect.isGreaterThan
                  (n "ErrorsRefused")
                  100
                  (sprintf "invoke errors were refused (%d)" (n "ErrorsRefused"))

              Expect.isGreaterThan
                  (n "DeferredRefused")
                  100
                  (sprintf "envelopes were refused (%d)" (n "DeferredRefused"))

              Expect.isGreaterThan
                  (n "SentenceDeferredRefused")
                  100
                  (sprintf "envelopes were refused through the sentence reader (%d)" (n "SentenceDeferredRefused"))

              let codes = Tally.reached "Codes" t

              for c in [ "MissingField"; "WrongKind"; "UnknownTag"; "OutOfRange" ] do
                  Expect.isTrue (codes.Contains c) (sprintf "a %s refusal was compared (%A)" c codes)

          testCase
              "a seam-codec oracle whose invocation writer DROPS an argument DISAGREES with CapabilityCodec — the measurement can fail"
          <| fun _ ->
              let t = seamCodecDifferential droppingInvocationJson 4551 100
              Differential.expectLoses "a writer that drops an argument" t

              Expect.isTrue
                  (Tally.disagreements t |> List.exists (fun d -> d.Contains "encodes differently"))
                  "and the disagreement is about the encoding, which is what the writer dropped" ]
