module Fuaran.Core.Tests.ProofOracle.DagMergeTests

// The Proofs.Oracle differential for the DAG beneath the fold, continued: delta recovery and
// `Dag.mergeBase` over a merged head (Phase 158), the reconcile's partition and the lanes-apply
// test (Phase 300), and the lane-DAG primitives and boundaries (Phases 437 and 457), split from
// ProofOracle.DagFoldTests.fs for length.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.ProofOracle.DagFoldTests
open Fuaran.Core.Tests.ProofOracle.DagDrainTests

// ---------------------------------------------------------------------------
//  Phase 158 — delta recovery over a MERGED HEAD, and `Dag.mergeBase`, beside production's own.
//
//  Section 14 of `proofs/DagClosure.fst` proves that over a head whose lane hangs off an already
//  MERGED node, `Dag.between` returns that lane's own nodes in append order (`between_merged`),
//  that the fold above it is the deltas-first fold (`reconcile_many_merged_eq`), and that
//  `Dag.mergeBase` of two such heads is the merged node they diverged from
//  (`DagPrimitives.merge_base_now_is_divergence`, over production's `deepest`). What runs here is the other half: production's OWN
//  `Dag.mergeBase` / `Dag.betweenOps` / `Dag.reconcileMany` over the DAG a clone actually holds
//  after it has folded, pulled and folded again — round one's lanes off a base, their heads folded
//  into one convergent head with `Dag.merge`, and round two's lanes appended off THAT — against the
//  extracted model on the same nodes.
//
//  Nothing is recomputed across the bridge: the ids are the content hashes production minted. The
//  model's recovery runs under section 13's drain at `String.CompareOrdinal`, which is the order
//  production's `topoCore` takes, and over this shape the drain's frontier genuinely widens — so
//  the order beneath the merged head is a real choice, and the claim measured is that the
//  recovered delta does not depend on it.
// ---------------------------------------------------------------------------

/// The DAG a clone holds after FOLD, PULL, FOLD: round one's lanes off one base, their heads merged
/// into one convergent head, and round two's lanes appended off that head under their own actors.
/// Returns the original base id, the merged head, round one's heads, round two's heads, and the DAG.
let private foldPullFold
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (round1: 'Op list list)
    (round2: 'Op list list)
    : string * string * string list * string list * Dag.T<'Op> =
    let hashFn = OpStream.defaultHash
    let baseId, heads1, dag1 = productionDag w baseOp round1
    let merged, dagM = mergedUnion w baseOp baseId heads1 dag1

    let heads2, dag2 =
        round2
        |> List.indexed
        |> List.fold
            (fun (hs, d) (i, ops) ->
                let actor = Human("pull-" + string i)

                let head, d' =
                    ops
                    |> List.fold
                        (fun (h, dd) op -> Dag.append (OpStream.canonical hashFn w) actor op h dd |> Reference.built)
                        (merged, d)

                hs @ [ head ], d')
            ([], dagM)

    baseId, merged, heads1, heads2, dag2

let private foundId (r: DagClosure.found<string>) : string option =
    match r with
    | DagClosure.Found id -> Some id
    | DagClosure.Missing -> None

/// The go-red instrument for `mergeBase`: the model's own function at the REVERSED key — the same
/// intersection, the same extracted `max_by`, with `deeper` flipped — so it returns the SHALLOWEST
/// common ancestor, which over a fold-pull-fold union is the ORIGINAL BASE rather than the
/// divergence point. It is as well-formed a `maxBy` as the model's own; what it breaks is agreement
/// with production, and the delta recovered from the base it names.
let private shallowestCommon
    (model: DagClosure.dag<'Op>)
    (fuel: DagClosure.node<'Op> list)
    (left: string)
    (right: string)
    =
    match ListAlgebra.inter (DagClosure.ancestors_of model fuel left) (DagClosure.ancestors_of model fuel right) with
    | [] -> None
    | c :: t -> Some(DagClosure.max_by (fun x y -> DagClosure.deeper ordLt model fuel y x) c t)

/// The fold-pull-fold counters: `Recovered` — ops recovered across every round-two lane, the vacuity
/// guard; `Chains` — round-two lanes of two or more ops (a one-node lane walks no chain);
/// `MergeNodes` — two-parent nodes beneath the merged heads (with none, the "merged head" was a plain
/// node and the case re-measured Phase 134's shape under a different name); `Pairs` — head pairs
/// whose merge base was located.
let private mergedCounters = [ "Recovered"; "Chains"; "MergeNodes"; "Pairs" ]

/// One fold-pull-fold union, compared three ways: each round-two head's recovered delta (production
/// `betweenOps` from the merged head, the model's `between_ops_drained`, and the lane that was
/// appended — the theorem's own right-hand side); every pair of round-two heads' merge base
/// (production, the model, and the merged head itself); and the model's computable premises, so a
/// green run is known to be one the theorems are ABOUT. Each finding is recorded under `where`.
let private mergedDisagreements
    (locate: DagClosure.dag<'Op> -> DagClosure.node<'Op> list -> string -> string -> string option)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (round1: 'Op list list)
    (round2: 'Op list list)
    (where: string)
    (t0: Tally)
    : Tally =
    let _, merged, _, heads2, dag = foldPullFold w baseOp round1 round2
    let model = toModelDag dag
    let fuel = model.nodes
    let kfuel = drainFuel model.nodes

    let found (ok: bool) (message: unit -> string) =
        Differential.check ok (fun () -> where + "\n" + message ())

    let encode (ops: 'Op list) =
        ops |> List.map w.Encode |> String.concat "; "

    let lanes =
        t0
        |> Differential.over (List.indexed (List.zip heads2 round2)) (fun (i, (head, lane)) t ->
            let p = Dag.betweenOps dag merged head
            let o = DagClosure.between_ops_drained ordLt kfuel model fuel merged head

            t
            |> Tally.add "Recovered" (List.length p)
            |> found (p = o) (fun () ->
                sprintf
                    "round-two lane %d: the recovered delta differs\n  production: [%s]\n  model:      [%s]"
                    i
                    (encode p)
                    (encode o))
            |> found (o = lane) (fun () ->
                sprintf
                    "round-two lane %d: the model did not recover the lane that was appended (between_ops_merged)\n  lane:  [%s]\n  model: [%s]"
                    i
                    (encode lane)
                    (encode o))
            // The FUEL premise of `merged_recovers`, evaluated: the walk below the lane completed.
            |> found (DagClosure.walk_ok model (DagClosure.drop_by fuel (List.rev lane)) merged) (fun () ->
                sprintf "round-two lane %d: the model's walk below the lane ran out of fuel — premise unmet" i))

    let headPairs =
        [ for (i, hi) in List.indexed heads2 do
              for (j, hj) in List.indexed heads2 do
                  if i < j then
                      yield i, hi, j, hj ]

    let pairs =
        lanes
        |> Differential.over headPairs (fun (i, hi, j, hj) t ->
            let p = Dag.mergeBase dag hi hj
            let o = locate model fuel hi hj

            t
            |> Tally.bump "Pairs"
            |> found (p = o) (fun () ->
                sprintf "heads %d and %d: the merge base differs\n  production: %A\n  model:      %A" i j p o)
            |> found (p = Some merged) (fun () ->
                sprintf "heads %d and %d: production's merge base %A is not the merged head %s" i j p merged))

    let mergeNodes =
        let anc = Dag.ancestorsOf dag merged

        dag.Nodes
        |> Map.toList
        |> List.filter (fun (id, n) -> Set.contains id anc && List.length n.Parents > 1)
        |> List.length

    pairs
    |> Tally.add "Chains" (round2 |> List.filter (fun l -> List.length l > 1) |> List.length)
    |> Tally.add "MergeNodes" mergeNodes

/// The faithful locator — the extracted `merge_base_now` (production's `deepest`) at production's id order.
let private modelMergeBase (model: DagClosure.dag<'Op>) (fuel: DagClosure.node<'Op> list) (l: string) (r: string) =
    foundId (DagPrimitives.merge_base_now ordLt fuel model fuel l r)


// ---------------------------------------------------------------------------
//  Phase 300 — the reconcile's partition, and the lanes-apply test, beside the model's.
//
//  `reconcile_sound` is proved over the partition section 15 models clause for clause; these cases
//  run the EXTRACTED partition over the closures and the drain the model computes from a real DAG
//  in each of the four shapes the old rule got wrong, beside production's `Dag.reconcile`. And
//  `fold_confluence_total` is proved over the checked `fold_once`; the rejecting-lane pool runs the
//  extracted fold beside production's `foldOnce` over lane sets that do not all apply.
// ---------------------------------------------------------------------------

/// The model's reconcile script over a production DAG: the extracted `reconcile_ids` over the
/// deduplicated heads' closures (`ancestors_of`), the base's closure, and the model's own drain of the
/// union — each id mapped back to its op through the same DAG.
let private modelReconcileOps (dag: Dag.T<'Op>) (baseId: string) (heads: string list) : 'Op list =
    let model = toModelDag dag
    let fuel = model.nodes
    let cs = heads |> List.distinct |> List.map (DagClosure.ancestors_of model fuel)
    let baseC = DagClosure.ancestors_of model fuel baseId
    let union = DagClosure.closure_nodes model.nodes (DagFold.concat cs)
    let order = DagClosure.drain_order ordLt (drainFuel union) union

    DagClosure.reconcile_ids order baseC cs |> List.map (fun id -> dag.Nodes[id].Op)

let private blindPlanFootprint (_: PlanOp) : Footprint =
    { Reads = Set.empty
      StructureWrites = Set.empty
      ContentWrites = Set.empty
      UnknownParentWrites = Set.empty
      SlotReads = Set.empty
      SlotWrites = Set.empty }

/// The four shapes over four drawn lanes, each lane under its own actor: disjoint, a fast-forward (the
/// second lane chained onto the first), the first head named twice, and a criss-cross (two merges of
/// the first two lanes under two actors, a lane off each, reconciled over `Dag.mergeBase`).
let private reconcileShapes (a: PlanOp list) (b: PlanOp list) (c: PlanOp list) (d: PlanOp list) =
    let h = OpStream.defaultHash

    let chain (actor: string) (ops: PlanOp list) (parent: string) (dag: Dag.T<PlanOp>) =
        ops
        |> List.fold
            (fun (p, dd) op -> Dag.append (OpStream.canonical h planW) (Human actor) op p dd |> Reference.built)
            (parent, dag)

    let g, d0 =
        Dag.append (OpStream.canonical h planW) (Human "base") planLaneGen.BaseOp "" Dag.empty
        |> Reference.built

    let ha, d1 = chain "lane-a" a g d0
    let hb, d2 = chain "lane-b" b g d1
    let hff, d3 = chain "lane-b" b ha d1

    let m1, d4 =
        Dag.merge (OpStream.canonical h planW) (Human "merge-1") planLaneGen.BaseOp ha hb d2
        |> Reference.built

    let m2, d5 =
        Dag.merge (OpStream.canonical h planW) (Human "merge-2") planLaneGen.BaseOp ha hb d4
        |> Reference.built

    let h1, d6 = chain "lane-a" c m1 d5
    let h2, d7 = chain "lane-b" d m2 d6
    let mb = Dag.mergeBase d7 h1 h2 |> Option.defaultValue g

    [ "disjoint", d2, g, [ ha; hb ]
      "fast-forward", d3, g, [ ha; hff ]
      "duplicate head", d1, g, [ ha; ha ]
      "criss-cross", d7, mb, [ h1; h2 ] ]

// ---------------------------------------------------------------------------
//  Phase 437 — the lane-DAG primitives beside the model's sections 18 to 21: `Dag.mergeBase` over a
//  criss-cross, `Dag.conflictsOfHeads`, the reachability index, and checkpoints with the truncation.
//
//  The theorems are proved in `proofs/DagClosure.fst` and `proofs/DagPrimitives.fst`; what runs here is the other half, production's
//  own functions beside the extracted ones over DAGs a clone actually holds. Each comparison renders
//  both sides to one string, and each case carries a vacuity guard: the arm it exists to measure was
//  reached.
// ---------------------------------------------------------------------------

/// A criss-cross: two lanes off a base, merged TWICE (under two actors, in both orders), and a lane
/// off each merge. The two lane heads have two maximal common ancestors — the two lane heads below
/// the merges — so `mergeBase` decides between them on its tie-break.
let private crissCross
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (a: 'Op list)
    (b: 'Op list)
    (c: 'Op list)
    (d: 'Op list)
    : string * string * Dag.T<'Op> =
    let h = OpStream.defaultHash

    let chain (actor: string) (ops: 'Op list) (parent: string) (dag: Dag.T<'Op>) =
        ops
        |> List.fold
            (fun (p, dd) op -> Dag.append (OpStream.canonical h w) (Human actor) op p dd |> Reference.built)
            (parent, dag)

    let g, d0 =
        Dag.append (OpStream.canonical h w) (Human "base") baseOp "" Dag.empty
        |> Reference.built

    let ha, d1 = chain "lane-a" a g d0
    let hb, d2 = chain "lane-b" b g d1

    let m1, d3 =
        Dag.merge (OpStream.canonical h w) (Human "merge-1") baseOp ha hb d2
        |> Reference.built

    let m2, d4 =
        Dag.merge (OpStream.canonical h w) (Human "merge-2") baseOp hb ha d3
        |> Reference.built

    let h1, d5 = chain "lane-c" c m1 d4
    let h2, d6 = chain "lane-d" d m2 d5
    h1, h2, d6

/// The model's `merge_base_now` at production's id order, with fuel for every node.
let private modelMergeBaseOf (dag: Dag.T<'Op>) (left: string) (right: string) : string option =
    let model = toModelDag dag
    modelMergeBase model model.nodes left right

/// A conflict rendered by node ids, address and shape — the canonical reading both sides share.
let private renderNodeConflict (left: string, right: string, address: string, shape: MergeConflictShape) =
    sprintf "%s|%s|%s|%A" left right address shape

let private productionHeadsReport (fp: 'Op -> Footprint) (dag: Dag.T<'Op>) (baseId: string) (heads: string list) =
    Dag.conflictsOfHeads fp dag baseId heads
    |> List.map (fun c -> renderNodeConflict (c.Left.Id, c.Right.Id, c.Address, c.Shape))
    |> List.distinct
    |> List.sort

/// The extracted `conflicts_of_heads` over the same partition `modelReconcileOps` computes.
let private modelHeadsReport (fp: 'Op -> Footprint) (dag: Dag.T<'Op>) (baseId: string) (heads: string list) =
    let model = toModelDag dag
    let fuel = model.nodes
    let cs = heads |> List.distinct |> List.map (DagClosure.ancestors_of model fuel)
    let baseC = DagClosure.ancestors_of model fuel baseId
    let union = DagClosure.closure_nodes model.nodes (DagFold.concat cs)
    let order = DagClosure.drain_order ordLt (drainFuel union) union

    DagClosure.conflicts_of_heads (fp >> toModelFootprint) model order baseC cs
    |> List.map (fun c -> renderNodeConflict (c.left.nid, c.right.nid, c.address, ofModelShape c.shape))
    |> List.distinct
    |> List.sort

/// `Dag.CheckpointFault` and the model's `ck_fault`, rendered alike.
let private renderProductionFrom
    (hashState: 'State -> string)
    (head: string)
    (r: Result<'State, Dag.CheckpointFault<'Rej>>)
    =
    match r with
    | Ok s -> "ok " + hashState s
    | Error(Dag.CheckpointFault.UnknownNode n) -> "unknown-node " + n
    | Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.UnknownHead h)) -> "unknown-head " + h
    | Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.Cyclic c)) -> renderCyclic head c
    | Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.Rejected(id, e))) -> sprintf "rejected %s %A" id e
    | Error(Dag.CheckpointFault.Unreached h) -> "unreached " + h
    | Error(Dag.CheckpointFault.Uncovered n) -> "uncovered " + n

let private renderModelFrom (hashState: 'State -> string) (r: DagFold.outcome<'State, DagClosure.ck_fault<'Rej>>) =
    match r with
    | DagFold.Ok s -> "ok " + hashState s
    | DagFold.Error(DagClosure.CUnknownNode n) -> "unknown-node " + n
    | DagFold.Error(DagClosure.CReplay(DagClosure.RUnknownHead h)) -> "unknown-head " + h
    | DagFold.Error(DagClosure.CReplay(DagClosure.RCyclicHistory h)) -> "cyclic " + h
    | DagFold.Error(DagClosure.CReplay(DagClosure.RRejected(id, e))) -> sprintf "rejected %s %A" id e
    | DagFold.Error(DagClosure.CUnreached h) -> "unreached " + h
    | DagFold.Error(DagClosure.CUncovered n) -> "uncovered " + n

/// The checkpoint replay's render pair at one head: production's `replayFrom` / `checkpointAt`
/// answer and the model's `replay_from` / `checkpoint_at`.
let private resume
    (hashState: 'State -> string)
    (head: string)
    : Differential<Result<'State, Dag.CheckpointFault<'Rej>>, DagFold.outcome<'State, DagClosure.ck_fault<'Rej>>> =
    Differential.pair (renderProductionFrom hashState head) (renderModelFrom hashState)

let private toModelCheckpoint (cp: Dag.Checkpoint<'State>) : DagClosure.checkpoint<'State> =
    { DagClosure.cnode = cp.Node
      DagClosure.cstate = cp.State
      DagClosure.cseal = cp.Hash }

/// The extracted `replay_from` over a model DAG, at production's id order, with fuel for every node.
let private modelReplayFrom
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (d: DagClosure.dag<'Op>)
    (cp: DagClosure.checkpoint<'State>)
    (head: string)
    =
    let fuel = drainFuel d.nodes
    DagClosure.replay_from (modelApply w) ordLt fuel d fuel cp head

/// Fold, pull, fold — and then a SIDE MERGE: the first round-two head merged with the first round-one
/// head, which names a parent behind the merged node, so a compaction at the merged node keeps a band.
let private foldPullFoldSide
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (round1: 'Op list list)
    (round2: 'Op list list)
    : string * string * string list * string list * Dag.T<'Op> =
    let baseId, merged, heads1, heads2, dag = foldPullFold w baseOp round1 round2

    match heads1, heads2 with
    | r1 :: _, r2 :: rest when r1 <> merged && r2 <> merged ->
        let side, dag' =
            Dag.merge (OpStream.canonical OpStream.defaultHash w) (Human "side") baseOp r2 r1 dag
            |> Reference.built

        baseId, merged, heads1, side :: rest, dag'
    | _ -> baseId, merged, heads1, heads2, dag

/// The checkpoint tally: replays that resumed, the two refusals a replay from a checkpoint can meet,
/// the compactions that succeeded, those that kept a band behind their node, and those refused.
let private checkpointTally =
    Tally.empty [ "Resumed"; "Uncovered"; "Unreached"; "Compacted"; "Banded"; "CutRefused" ]

/// Every node as a checkpoint, every head as a target: production's `checkpointAt` / `replayFrom` /
/// `compactAt` beside the extracted `checkpoint_at` / `replay_from` / `truncate` + `truncation_ok`.
/// Each finding is recorded under `where`.
let private checkpointDisagreements
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (hashState: 'State -> string)
    (state0: 'State)
    (where: string)
    (dag: Dag.T<'Op>)
    (heads: string list)
    (t0: Tally)
    : Tally =
    let h = OpStream.defaultHash
    let model = toModelDag dag
    let fuel = drainFuel model.nodes
    let seal (n: string) (st: 'State) = (Dag.sealAt h hashState w n st).Hash
    let targets = (dag.Nodes |> Map.toList |> List.map fst) @ heads @ [ "no-such-node" ]

    let found (ok: bool) (message: unit -> string) =
        Differential.check ok (fun () -> where + "\n" + message ())

    let fail (message: string) = found false (fun () -> message)

    // Every target replayed from production's checkpoint at `c`, on both sides.
    let replays (c: string) (pc: Dag.Checkpoint<'State>) (t: Tally) =
        t
        |> Differential.over (List.distinct targets) (fun target t ->
            let pair = resume hashState target
            let pr = pair.Production(Dag.replayFrom w pc dag target)
            let mr = pair.Model(modelReplayFrom w model (toModelCheckpoint pc) target)

            if pr <> mr then
                fail (sprintf "replayFrom %s -> %s\n  production: %s\n  model:      %s" c target pr mr) t
            elif pr.StartsWith "ok " then
                Tally.bump "Resumed" t
            elif pr.StartsWith "uncovered " then
                Tally.bump "Uncovered" t
            elif pr.StartsWith "unreached " then
                Tally.bump "Unreached" t
            else
                t)

    // The compaction at `c`: its kept set held to the model's truncation, its replays to the full DAG's.
    let compaction (c: string) (t: Tally) =
        match Dag.compactAt h hashState w state0 dag c with
        | Ok { Checkpoint = cp; Dag = compacted } ->
            let keep = compacted.Nodes |> Map.toList |> List.map fst
            let truncated = DagClosure.truncate model keep
            let behind = Dag.ancestorsOf dag c

            t
            |> found (DagClosure.truncation_ok model fuel c keep) (fun () ->
                sprintf "compactAt %s: the kept set does not meet the model's truncation contract" c)
            |> found ((truncated.nodes |> List.map (fun n -> n.nid) |> List.sort) = (keep |> List.sort)) (fun () ->
                sprintf "compactAt %s: the model's truncation is not production's DAG" c)
            |> Tally.bump "Compacted"
            |> Tally.bumpIf (keep |> List.exists (fun id -> id <> c && Set.contains id behind)) "Banded"
            |> Differential.over keep (fun target t ->
                let pair = resume hashState target
                let full = pair.Production(Dag.replayFrom w cp dag target)
                let cut = pair.Production(Dag.replayFrom w cp compacted target)
                let mcut = pair.Model(modelReplayFrom w truncated (toModelCheckpoint cp) target)

                t
                |> found (cut = mcut) (fun () ->
                    sprintf "compacted at %s, head %s\n  production: %s\n  model:      %s" c target cut mcut)
                |> found (not (full.StartsWith "ok " || full.StartsWith "rejected ") || cut = full) (fun () ->
                    sprintf
                        "compacted at %s, head %s: the truncation changed the replay\n  full: %s\n  cut:  %s"
                        c
                        target
                        full
                        cut))
        | Error(Dag.CheckpointFault.Uncovered _) -> Tally.bump "CutRefused" t
        | Error e -> fail (sprintf "compactAt %s refused a checkpoint checkpointAt took: %A" c e) t

    t0
    |> Differential.over ((dag.Nodes |> Map.toList |> List.map fst) @ [ "no-such-node" ]) (fun c t ->
        let pair = resume hashState c
        let p = Dag.checkpointAt h hashState w state0 dag c
        let m = DagClosure.checkpoint_at (modelApply w) ordLt fuel model fuel seal state0 c

        match p, m with
        | Ok pc, DagFold.Ok mc ->
            t
            |> found
                (pc.Node = mc.cnode
                 && hashState pc.State = hashState mc.cstate
                 && pc.Hash = mc.cseal)
                (fun () ->
                    sprintf "checkpointAt %s: production (%s, %s) model (%s, %s)" c pc.Node pc.Hash mc.cnode mc.cseal)
            |> replays c pc
            |> compaction c
        | Error pe, DagFold.Error me ->
            let pr = pair.Production(Error pe)
            let mr = pair.Model(DagFold.Error me)

            found
                (pr = mr)
                (fun () -> sprintf "checkpointAt %s refused differently\n  production: %s\n  model:      %s" c pr mr)
                t
        | Ok _, DagFold.Error me ->
            fail
                (sprintf "checkpointAt %s: production took it, the model refused %s" c (pair.Model(DagFold.Error me)))
                t
        | Error pe, DagFold.Ok _ -> fail (sprintf "checkpointAt %s: the model took it, production refused %A" c pe) t)

/// Every ordered pair of ids (and a missing one): production's index and walk beside the extracted
/// index built over the model's drain. `Positives` counts the pairs where the walk says "ancestor".
let private reachDisagreements (where: string) (dag: Dag.T<'Op>) (t0: Tally) : Tally =
    let model = toModelDag dag
    let fuel = drainFuel model.nodes
    let idx = DagClosure.reach_of_dag ordLt fuel model
    let reach = Dag.Reach.ofDag dag
    let ids = (dag.Nodes |> Map.toList |> List.map fst) @ [ "no-such-node" ]

    let found (ok: bool) (message: unit -> string) =
        Differential.check ok (fun () -> where + ": " + message ())

    t0
    |> Differential.over ids (fun d t ->
        let walk = Dag.ancestorsOf dag d
        let mAnc = DagClosure.reach_ancestors model fuel idx d |> Set.ofList

        t
        |> found (mAnc = walk && Dag.Reach.ancestors reach d = walk) (fun () ->
            sprintf "ancestors %s: walk %A, model %A" d walk mAnc)
        |> Differential.over ids (fun a t ->
            let truth = Set.contains a walk
            let p = Dag.Reach.reaches reach a d
            let m = DagClosure.reach_reaches model fuel idx a d

            t
            |> Tally.bumpIf truth "Positives"
            |> found (p = truth && m = truth) (fun () ->
                sprintf "reaches %s %s: walk %b, production %b, model %b" a d truth p m)))

// ---------------------------------------------------------------------------
//  Phase 457 — the lane DAG's remaining boundaries (`proofs/DagPrimitives.fst`).
//
//  The model now computes `Dag.mergeBase` as Phase 388 does (`deepest`, maximal members only),
//  `Dag.commonBase` over N heads, the index's own `mergeBase` / `commonBase` / `between` /
//  `tryTopoOrder` over its slots and the whole DAG's drain, and `cut`'s kept set. Each runs here
//  beside production's over every pair, every node and several head sets of the DAGs a clone holds.
// ---------------------------------------------------------------------------

let private renderTopo (r: Result<string list, Dag.Cyclic>) : string =
    match r with
    | Ok l -> "ok " + String.concat "," l
    | Error _ -> "cyclic"

let private renderModelTopo (r: DagPrimitives.topo_result) : string =
    match r with
    | DagPrimitives.TopoOk l -> "ok " + String.concat "," l
    | DagPrimitives.TopoCyclic _ -> "cyclic"

/// The topological order's render pair: production's `tryTopoOrder` and the model's `try_topo_order`.
let private topo: Differential<Result<string list, Dag.Cyclic>, DagPrimitives.topo_result> =
    Differential.pair renderTopo renderModelTopo

/// The boundary tally: pairs that share a base and pairs that refuse, non-empty deltas, N-head sets
/// with a common base, compactions kept, kept with a band behind their node, and refused.
let private boundaryTally =
    Tally.empty [ "Based"; "Disjoint"; "Deltas"; "NHead"; "Kept"; "Banded"; "Refused" ]

/// Every ordered pair of ids (and an absent one), several head sets, and every node as a cut point:
/// production's DAG-level and index-level answers beside the extracted ones. Each finding is
/// recorded under `where`.
let private boundaryDisagreements
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (hashState: 'State -> string)
    (state0: 'State)
    (where: string)
    (dag: Dag.T<'Op>)
    (heads: string list)
    (t0: Tally)
    : Tally =
    let model = toModelDag dag
    let fuel = drainFuel model.nodes
    let idx = DagClosure.reach_of_dag ordLt fuel model
    let ord = DagClosure.drain_order ordLt fuel model.nodes
    let reach = Dag.Reach.ofDag dag
    let held = dag.Nodes |> Map.toList |> List.map fst
    let ids = held @ [ "no-such-node" ]
    let nids (ns: DagClosure.node<'Op> list) = ns |> List.map (fun n -> n.nid)

    let found (ok: bool) (message: unit -> string) =
        Differential.check ok (fun () -> where + ": " + message ())

    let pairs =
        t0
        |> Differential.over ids (fun a t ->
            let pt = topo.Production(Dag.tryTopoOrder dag a)
            let mt = topo.Model(DagPrimitives.try_topo_order ordLt fuel model fuel a)

            let rt =
                topo.Model(DagPrimitives.reach_try_topo_order ordLt fuel model fuel idx ord a)

            t
            |> found (mt = pt && rt = pt && topo.Production(Dag.Reach.tryTopoOrder reach a) = pt) (fun () ->
                sprintf "tryTopoOrder %s: production %s, model %s, model index %s" a pt mt rt)
            |> Differential.over ids (fun b t ->
                let pm = Dag.mergeBase dag a b
                let mm = foundId (DagPrimitives.merge_base_now ordLt fuel model fuel a b)
                let rm = foundId (DagPrimitives.reach_merge_base ordLt fuel model fuel idx a b)
                let pb = Dag.between dag a b |> List.map _.Id
                let mb = DagClosure.between_drained ordLt fuel model fuel a b |> nids
                let rb = DagPrimitives.reach_between ordLt fuel model fuel idx ord a b |> nids

                t
                |> found (mm = pm && rm = pm && Dag.Reach.mergeBase reach a b = pm) (fun () ->
                    sprintf "mergeBase %s %s: production %A, model %A, model index %A" a b pm mm rm)
                |> Tally.bump (if pm.IsSome then "Based" else "Disjoint")
                |> found (mb = pb && rb = pb && (Dag.Reach.between reach a b |> List.map _.Id) = pb) (fun () ->
                    sprintf "between %s %s: production %A, model %A, model index %A" a b pb mb rb)
                |> Tally.bumpIf (not (List.isEmpty pb)) "Deltas"))

    let headSets =
        pairs
        |> Differential.over [ heads; held; List.rev heads @ heads; heads @ [ "no-such-node" ]; [] ] (fun hs t ->
            let pc = Dag.commonBase dag hs
            let mc = foundId (DagPrimitives.common_base ordLt fuel model fuel hs)
            let rc = foundId (DagPrimitives.reach_common_base ordLt fuel model fuel idx hs)

            t
            |> found (mc = pc && rc = pc && Dag.Reach.commonBase reach hs = pc) (fun () ->
                sprintf "commonBase %A: production %A, model %A, model index %A" hs pc mc rc)
            |> Tally.bumpIf (List.length (List.distinct hs) >= 3 && pc.IsSome) "NHead")

    headSets
    |> Differential.over held (fun c t ->
        match
            Dag.compactAt OpStream.defaultHash hashState w state0 dag c, DagPrimitives.cut_keep ordLt model fuel c
        with
        | Ok { Dag = compacted }, DagPrimitives.CutKeep k ->
            let kept = compacted.Nodes |> Map.toList |> List.map fst |> Set.ofList
            let behind = Dag.ancestorsOf dag c

            t
            |> found (Set.ofList k = kept) (fun () ->
                sprintf "cut %s: production keeps %A, the model %A" c kept (Set.ofList k))
            |> found (DagClosure.truncation_ok model fuel c k) (fun () ->
                sprintf "cut %s: the model's kept set misses its own contract" c)
            |> Tally.bump "Kept"
            |> Tally.bumpIf (kept |> Set.exists (fun id -> id <> c && Set.contains id behind)) "Banded"
        | Error(Dag.CheckpointFault.Uncovered u), DagPrimitives.CutUncovered v ->
            t
            |> found (u = v) (fun () -> sprintf "cut %s: production refuses %s uncovered, the model %s" c u v)
            |> Tally.bump "Refused"
        | Error(Dag.CheckpointFault.Uncovered _), _
        | Ok _, _ ->
            found false (fun () -> sprintf "cut %s: production and the model disagree on whether it is refused" c) t
        | Error _, _ -> t) // checkpointAt refused: the domain rejects the history, so there is no cut

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 158: delta recovery over a MERGED HEAD, and mergeBase, beside production's ----

          testCase "the extracted recovery and mergeBase are production's over every FOLD-PULL-FOLD union"
          <| fun _ ->
              // The shape section 14 is about: round one's lanes folded into one convergent head
              // with `Dag.merge`, round two's lanes appended off it. Three comparisons per union —
              // production's delta, the model's, and the lane that was appended; production's merge
              // base, the model's, and the merged head — plus the model's fuel premise, evaluated.
              // The adequacy guards are the ones a green run could otherwise hide behind: ops were
              // recovered, a chain was walked, a merge base was located, and the head the lanes
              // hang off really had two-parent nodes beneath it.
              let planStep (i: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1

                  r2,
                  mergedDisagreements
                      modelMergeBase
                      planW
                      planLaneGen.BaseOp
                      round1
                      round2
                      (sprintf
                          "work-plan domain iter %d\nround one:\n%s\nround two:\n%s"
                          i
                          (renderLanes encPlanOp round1)
                          (renderLanes encPlanOp round2))
                      t

              let treeStep (i: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = treeLaneGen.Lanes 4 rng
                  let round2, r2 = treeLaneGen.Lanes 3 r1

                  r2,
                  mergedDisagreements
                      modelMergeBase
                      treeW
                      treeLaneGen.BaseOp
                      round1
                      round2
                      (sprintf
                          "reference witness iter %d\nround one:\n%s\nround two:\n%s"
                          i
                          (renderLanes treeW.Encode round1)
                          (renderLanes treeW.Encode round2))
                      t

              let r, t =
                  Differential.seededFrom 60 (ConfRng.ofSeed 1580) planStep (Tally.empty mergedCounters)

              let t = Differential.seeded 30 r treeStep t
              Differential.expectAgrees "fold-pull-fold" t
              let recovered = Tally.count "Recovered" t
              Expect.isGreaterThan recovered 0 (sprintf "no ops were recovered at all (recovered=%d)" recovered)

              Expect.isGreaterThan
                  (Tally.count "Chains" t)
                  0
                  "no round-two lane was longer than one op, so no chain was walked"

              Expect.isGreaterThan (Tally.count "Pairs" t) 0 "no pair of round-two heads had its merge base located"

              Expect.isGreaterThan
                  (Tally.count "MergeNodes" t)
                  0
                  "no merged head had a two-parent node beneath it, so this case re-measured Phase 134's base-plus-chains shape"

          testCase "the fold FROM a merged head is the deltas-first fold over the round-two lanes"
          <| fun _ ->
              // `reconcile_many_merged_eq`, measured: production's `Dag.reconcileMany` from the
              // merged head, the extracted `reconcile_many_drained` over the same DAG, and the
              // deltas-first `reconcile_many` handed the round-two lanes directly — the theorem's
              // own right-hand side. Scripts are compared as scripts and halts as canonical
              // reports, exactly as the Phase 131 cases compare them.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  let _, merged, _, heads2, dag = foldPullFold planW planLaneGen.BaseOp round1 round2
                  let model = toModelDag dag

                  let production =
                      match reconcileScript planW planFootprint dag merged heads2 with
                      | Ok script -> Ok script
                      | Error cs -> Error(FoldConfluence.canonicalConflictReport planW.Encode cs)

                  let fromDag =
                      match
                          DagClosure.reconcile_many_drained
                              (planFootprint >> toModelFootprint)
                              ordLt
                              (drainFuel model.nodes)
                              model
                              model.nodes
                              merged
                              heads2
                      with
                      | DagFold.Ok script -> Ok script
                      | DagFold.Error cs ->
                          Error(FoldConfluence.canonicalConflictReport planW.Encode (cs |> List.map ofModelConflict))

                  let deltasFirst = oracleScript planW planFootprint round2

                  r2,
                  t
                  |> Tally.bump (if Result.isOk production then "Folded" else "Halted")
                  |> Differential.check (production = fromDag && fromDag = deltasFirst) (fun () ->
                      sprintf
                          "iter %d\nround one:\n%s\nround two:\n%s\n  production:   %A\n  model (DAG):  %A\n  deltas-first: %A"
                          i
                          (renderLanes encPlanOp round1)
                          (renderLanes encPlanOp round2)
                          production
                          fromDag
                          deltasFirst)

              let t = Differential.seeded 60 (ConfRng.ofSeed 1581) step foldTally
              Differential.expectAgrees "the fold from a merged head" t
              let folded = Tally.count "Folded" t
              Expect.isGreaterThan (folded + Tally.count "Halted" t) 0 "some unions were folded"
              Expect.isGreaterThan folded 0 "no union folded clean, so no merge script was ever compared"

          testCase "a model whose mergeBase returns the BASE rather than the divergence point loses"
          <| fun _ ->
              // The teeth. The locator is the model's own `max_by` over the model's own
              // intersection at the REVERSED key, so it names the SHALLOWEST common ancestor — over
              // a fold-pull-fold union, the original base. If this comes back clean, the green
              // case above certifies nothing: it would be comparing a locator that cannot tell the
              // divergence point from anything else the two heads share. Two things must lose —
              // the merge base itself, and the delta recovered from the base it names, which is
              // the whole of round one as well as the lane.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1

                  let t =
                      mergedDisagreements
                          shallowestCommon
                          planW
                          planLaneGen.BaseOp
                          round1
                          round2
                          (sprintf "iter %d" i)
                          t

                  let baseId, merged, _, heads2, dag =
                      foldPullFold planW planLaneGen.BaseOp round1 round2

                  let model = toModelDag dag

                  r2,
                  match heads2 with
                  | h0 :: h1 :: _ ->
                      match shallowestCommon model model.nodes h0 h1 with
                      | Some wrong when wrong = baseId && baseId <> merged ->
                          let fromWrong =
                              DagClosure.between_ops_drained ordLt (drainFuel model.nodes) model model.nodes wrong h0

                          t
                          |> Tally.bump "NamedTheBase"
                          |> Tally.bumpIf (fromWrong <> Dag.betweenOps dag merged h0) "LongerDeltas"
                      | _ -> t
                  | _ -> t

              let t =
                  Differential.seeded
                      60
                      (ConfRng.ofSeed 1580)
                      step
                      (Tally.empty (mergedCounters @ [ "NamedTheBase"; "LongerDeltas" ]))

              Differential.expectLoses
                  "a locator naming the SHALLOWEST common ancestor must disagree with production — this comparison cannot lose"
                  t

              Expect.stringContains
                  (List.head (Tally.disagreements t))
                  "the merge base differs"
                  "the disagreement names what moved"

              Expect.isGreaterThan
                  (Tally.count "NamedTheBase" t)
                  0
                  "the reversed locator named the ORIGINAL BASE, which is the go-red the phase asks for"

              Expect.isGreaterThan
                  (Tally.count "LongerDeltas" t)
                  0
                  "a delta recovered from the base rather than the divergence point must differ from production's"

          // ---- Phase 300: the reconcile's partition and the rejecting-lane pool, beside production ----

          testCase "the extracted reconcile partition is production's over the four shapes"
          <| fun _ ->
              let step (_: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r = planLaneGen.Lanes 4 rng

                  let a, b, c, d =
                      List.item 0 lanes, List.item 1 lanes, List.item 2 lanes, List.item 3 lanes

                  r,
                  t
                  |> Differential.over (reconcileShapes a b c d) (fun (name, dag, baseId, heads) t ->
                      let production =
                          match heads with
                          | [ h1; h2 ] -> Dag.reconcile blindPlanFootprint dag baseId h1 h2
                          | _ -> failtest "two heads per shape"

                      let model = modelReconcileOps dag baseId heads

                      // the old rule — each lane's whole delta, concatenated — is what the model is
                      // measured AGAINST: over a shape whose heads share history it must differ
                      let old = heads |> List.collect (Dag.betweenOps dag baseId)

                      t
                      |> Differential.check (production = Ok model) (fun () ->
                          sprintf
                              "%s: production's script is the model's partition\n  production: %A\n  the model:  %A"
                              name
                              production
                              model)
                      |> Tally.bumpIf (old <> model) "Shared")

              let t = Differential.seeded 40 (ConfRng.ofSeed 3001) step (Tally.empty [ "Shared" ])
              Differential.expectAgrees "the reconcile partition" t

              Expect.isGreaterThan
                  (Tally.count "Shared" t)
                  0
                  "the pool reached a shape where the old rule applied shared history twice"

          testCase
              "the extracted fold and production refuse a rejecting-lane pool identically, under every arrival order"
          <| fun _ ->
              let rejecting = [ Retitle("missing", "x") ]

              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes0, r = planLaneGen.Lanes 3 rng
                  // a lane that does not apply from the base, at a position that moves
                  let at = i % (List.length lanes0 + 1)
                  let lanes = List.take at lanes0 @ [ rejecting ] @ List.skip at lanes0

                  let runs =
                      FoldConfluence.arrivalOrders (List.length lanes)
                      |> List.map (fun p ->
                          let ls = p |> List.map (fun k -> List.item k lanes)

                          let production =
                              productionFold planW planFootprint planHash basePlan planLaneGen.BaseOp ls

                          p, production, oracleFold planW planFootprint planHash basePlan ls)

                  let outcomes =
                      runs |> List.map (fun (_, production, _) -> production) |> List.distinct

                  r,
                  t
                  |> Differential.over runs (fun (p, production, oracle) t ->
                      Differential.check
                          (oracle = production)
                          (fun () ->
                              sprintf
                                  "iter %d, order %A: the extracted fold renders what production renders\n  production: %A\n  oracle:     %A"
                                  i
                                  p
                                  production
                                  oracle)
                          t)
                  |> Differential.check (List.length outcomes = 1) (fun () ->
                      sprintf "iter %d: one outcome under every arrival order, got %A" i outcomes)
                  |> Tally.bumpIf
                      (match outcomes with
                       | [ LaneRejected report ] when report.Contains "T|missing|x" -> true
                       | _ -> false)
                      "Refused"

              let t =
                  Differential.seeded 40 (ConfRng.ofSeed 3002) step (Tally.empty [ "Refused" ])

              Differential.expectAgrees "the rejecting-lane pool" t
              Expect.isGreaterThan (Tally.count "Refused" t) 0 "the pool reached the refusal it exists to measure"

          // ---- Phase 437: the lane-DAG primitives beside sections 18 to 21 ----

          testCase
              "the extracted merge_base_now is production's over CRISS-CROSS merges, and is a maximal common ancestor"
          <| fun _ ->
              // `merge_base_now_maximal`, measured: production's `mergeBase` (and the index's) beside the
              // extracted one over two merges of the same two lanes, and the answer checked to be a
              // common ancestor no other common ancestor descends from. The guard: the pool reached
              // pairs with SEVERAL maximal common ancestors, where the tie-break genuinely decides.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r = planLaneGen.Lanes 4 rng

                  let a, b, c, d =
                      List.item 0 lanes, List.item 1 lanes, List.item 2 lanes, List.item 3 lanes

                  let h1, h2, dag = crissCross planW planLaneGen.BaseOp a b c d
                  let production = Dag.mergeBase dag h1 h2
                  let model = modelMergeBaseOf dag h1 h2
                  let index = Dag.Reach.mergeBase (Dag.Reach.ofDag dag) h1 h2
                  let common = Set.intersect (Dag.ancestorsOf dag h1) (Dag.ancestorsOf dag h2)

                  let maximal =
                      common
                      |> Set.filter (fun x ->
                          common
                          |> Set.forall (fun y -> y = x || not (Set.contains x (Dag.ancestorsOf dag y))))

                  r,
                  t
                  |> Differential.check (model = production) (fun () ->
                      sprintf
                          "iter %d: the extracted merge_base_now is production's\n  production: %A\n  the model:  %A"
                          i
                          production
                          model)
                  |> Differential.check (index = production) (fun () ->
                      sprintf "iter %d: the index answers alike\n  production: %A\n  the index:  %A" i production index)
                  |> (match production with
                      | None ->
                          Differential.check (Set.isEmpty common) (fun () ->
                              sprintf "iter %d: a refusal only over disjoint histories" i)
                      | Some mb ->
                          Differential.check (Set.contains mb maximal) (fun () ->
                              sprintf "iter %d: %s is a maximal common ancestor" i mb))
                  |> Tally.bumpIf (Set.count maximal >= 2) "CrissCrossed"

              let t =
                  Differential.seeded 60 (ConfRng.ofSeed 4370) step (Tally.empty [ "CrissCrossed" ])

              Differential.expectAgrees "merge_base_now over criss-crosses" t

              Expect.isGreaterThan
                  (Tally.count "CrissCrossed" t)
                  0
                  "the pool reached a pair with two maximal common ancestors"

          testCase "the extracted conflicts_of_heads is production's Dag.conflictsOfHeads over the four shapes"
          <| fun _ ->
              // `conflicts_of_heads_is_the_sweep`, measured over the shapes the partition exists for —
              // disjoint, fast-forward, a duplicate head and a criss-cross — under the domain's real
              // footprint, read canonically (the model's set algebra lists a member once per clause it
              // fails). And the theorem's corollary on production: the report is empty exactly when
              // `reconcileMany` does not refuse with `LanesInterfere`.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r = planLaneGen.Lanes 4 rng

                  let a, b, c, d =
                      List.item 0 lanes, List.item 1 lanes, List.item 2 lanes, List.item 3 lanes

                  r,
                  t
                  |> Differential.over (reconcileShapes a b c d) (fun (name, dag, baseId, heads) t ->
                      let production = productionHeadsReport planFootprint dag baseId heads
                      let model = modelHeadsReport planFootprint dag baseId heads

                      let interferes =
                          match Dag.reconcileMany planW planFootprint dag baseId planLaneGen.State0 heads with
                          | Error(ReconcileFault.LanesInterfere _) -> true
                          | _ -> false

                      t
                      |> Differential.check (model = production) (fun () ->
                          sprintf
                              "iter %d, %s: the report is production's\n  production: %A\n  the model:  %A"
                              i
                              name
                              production
                              model)
                      |> Differential.check (not (List.isEmpty production) = interferes) (fun () ->
                          sprintf
                              "iter %d, %s: a report exactly when reconcileMany refuses with LanesInterfere (report %A, interferes %b)"
                              i
                              name
                              production
                              interferes)
                      |> Tally.bump (if List.isEmpty production then "Clean" else "Reported"))

              let t =
                  Differential.seeded 60 (ConfRng.ofSeed 4371) step (Tally.empty [ "Reported"; "Clean" ])

              Differential.expectAgrees "conflicts_of_heads" t
              Expect.isGreaterThan (Tally.count "Reported" t) 0 "the pool reached interfering heads"
              Expect.isGreaterThan (Tally.count "Clean" t) 0 "the pool reached heads that do not interfere"

          testCase "the extracted reachability index answers production's Reach and the walk over every pair"
          <| fun _ ->
              // `reach_of_dag_agrees`, measured: every ordered pair of ids, and an absent id, over
              // fold-pull-fold DAGs with a side merge and over criss-crosses.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  let cc, r3 = planLaneGen.Lanes 4 r2
                  let _, _, _, _, dag = foldPullFoldSide planW planLaneGen.BaseOp round1 round2

                  let _, _, ccDag =
                      crissCross
                          planW
                          planLaneGen.BaseOp
                          (List.item 0 cc)
                          (List.item 1 cc)
                          (List.item 2 cc)
                          (List.item 3 cc)

                  r3,
                  t
                  |> Differential.over [ "fold-pull-fold", dag; "criss-cross", ccDag ] (fun (label, d) t ->
                      reachDisagreements (sprintf "iter %d, %s" i label) d t)

              let t =
                  Differential.seeded 30 (ConfRng.ofSeed 4372) step (Tally.empty [ "Positives" ])

              Differential.expectAgrees "the reachability index" t
              Expect.isGreaterThan (Tally.count "Positives" t) 0 "some pairs were ancestors"

          testCase "the extracted checkpoint, replay_from and truncation are production's over every node and head"
          <| fun _ ->
              // `DagClosure.fst`'s section 21 theorems 1 and 2, measured: every node as a checkpoint
              // and every node (and an absent id) as a head, on fold-pull-fold DAGs with a side
              // merge — so the replay meets a covered head, a head below the checkpoint and a branch
              // from below it — and `compactAt` at every node, its kept set held to the model's
              // contract and its replays to the full DAG's.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1

                  let _, _, _, heads2, dag = foldPullFoldSide planW planLaneGen.BaseOp round1 round2

                  let where =
                      sprintf
                          "work-plan domain iter %d\nround one:\n%s\nround two:\n%s"
                          i
                          (renderLanes encPlanOp round1)
                          (renderLanes encPlanOp round2)

                  r2, checkpointDisagreements planW planHash planLaneGen.State0 where dag heads2 t

              let t = Differential.seeded 25 (ConfRng.ofSeed 4373) step checkpointTally
              Differential.expectAgrees "checkpoints" t
              Expect.isGreaterThan (Tally.count "Resumed" t) 0 "some replays resumed from a checkpoint"
              Expect.isGreaterThan (Tally.count "Uncovered" t) 0 "the branch-from-below refusal was reached"
              Expect.isGreaterThan (Tally.count "Unreached" t) 0 "the unreached refusal was reached"
              Expect.isGreaterThan (Tally.count "Compacted" t) 0 "some compactions succeeded"
              Expect.isGreaterThan (Tally.count "Banded" t) 0 "some compaction kept a band behind its node"
              Expect.isGreaterThan (Tally.count "CutRefused" t) 0 "some compaction was refused as uncovered"

          testCase "a model checkpoint sealed at ANOTHER node loses — the measurement can fail"
          <| fun _ ->
              // The go-red of that theorem 1, at the differential: the checkpoint production took, handed to
              // the model with its node moved to the base — the state of one node sealed at another.
              let step (_: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1

                  let baseId, merged, _, heads2, dag =
                      foldPullFold planW planLaneGen.BaseOp round1 round2

                  let model = toModelDag dag

                  r2,
                  match Dag.checkpointAt OpStream.defaultHash planHash planW planLaneGen.State0 dag merged with
                  | Ok cp ->
                      let moved =
                          { toModelCheckpoint cp with
                              DagClosure.cnode = baseId }

                      t
                      |> Differential.over heads2 (fun head t ->
                          Differential.compare
                              (resume planHash head)
                              (sprintf "replayFrom %s -> %s" merged head)
                              (Dag.replayFrom planW cp dag head)
                              (modelReplayFrom planW model moved head)
                              t)
                  | Error _ -> t // the merged history does not replay in this domain: no checkpoint to move

              let t = Differential.seeded 20 (ConfRng.ofSeed 4374) step (Tally.empty [])
              Differential.expectLoses "a checkpoint sealed at the wrong node disagrees with production" t

          testCase "the extracted mergeBase, commonBase and index queries are production's over every pair and head set"
          <| fun _ ->
              // Phase 457's theorems, measured: `merge_base_now_maximal`, `common_base_maximal`
              // and the four `reach_*_agrees`, over fold-pull-fold DAGs with a side merge and over
              // criss-crosses — every ordered pair of ids, an absent id, and head sets of three or
              // more, a repeated head and an absent head included. The guards: some pairs share a
              // base and some do not, some deltas are non-empty, and some N-head base was found.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1
                  let cc, r3 = planLaneGen.Lanes 4 r2

                  let _, _, heads1, heads2, dag =
                      foldPullFoldSide planW planLaneGen.BaseOp round1 round2

                  let h1, h2, ccDag =
                      crissCross
                          planW
                          planLaneGen.BaseOp
                          (List.item 0 cc)
                          (List.item 1 cc)
                          (List.item 2 cc)
                          (List.item 3 cc)

                  r3,
                  t
                  |> Differential.over
                      [ "fold-pull-fold", dag, heads1 @ heads2; "criss-cross", ccDag, [ h1; h2 ] ]
                      (fun (label, d, hs) t ->
                          boundaryDisagreements
                              planW
                              planHash
                              planLaneGen.State0
                              (sprintf "iter %d, %s" i label)
                              d
                              hs
                              t)

              let t = Differential.seeded 12 (ConfRng.ofSeed 4570) step boundaryTally
              Differential.expectAgrees "the lane-DAG boundaries" t
              Expect.isGreaterThan (Tally.count "Based" t) 0 "some pairs share a base"
              Expect.isGreaterThan (Tally.count "Disjoint" t) 0 "some pairs refuse"
              Expect.isGreaterThan (Tally.count "Deltas" t) 0 "some deltas are non-empty"
              Expect.isGreaterThan (Tally.count "NHead" t) 0 "some head set of three or more has a common base"

          testCase "the extracted cut keeps production's set at every node, and refuses where production refuses"
          <| fun _ ->
              // `cut_keep_meets_contract`, measured: `Dag.compactAt` at every node of fold-pull-fold
              // DAGs with a side merge, its kept set equal to the extracted `cut_keep`'s and its
              // refusal naming the same node. The guards: a compaction kept a band behind its node,
              // and a compaction was refused as uncovered.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1
                  let _, _, _, heads2, dag = foldPullFoldSide planW planLaneGen.BaseOp round1 round2

                  r2, boundaryDisagreements planW planHash planLaneGen.State0 (sprintf "iter %d" i) dag heads2 t

              let t = Differential.seeded 12 (ConfRng.ofSeed 4571) step boundaryTally
              Differential.expectAgrees "the cut" t
              Expect.isGreaterThan (Tally.count "Kept" t) 0 "some compactions succeeded"
              Expect.isGreaterThan (Tally.count "Banded" t) 0 "some compaction kept a band behind its node"
              Expect.isGreaterThan (Tally.count "Refused" t) 0 "some compaction was refused as uncovered"

          testCase "an index between that drops one node loses — the measurement can fail"
          <| fun _ ->
              // The go-red of `reach_between_agrees`, at the differential: the extracted index's
              // delta with its first node dropped disagrees with production wherever the delta is
              // non-empty. The proof leg carries the same go-red, run by Phase 457 against the model.
              let step (_: int) (rng: ConfRng.T) (t: Tally) =
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1
                  let _, _, _, _, dag = foldPullFoldSide planW planLaneGen.BaseOp round1 round2
                  let model = toModelDag dag
                  let fuel = drainFuel model.nodes
                  let idx = DagClosure.reach_of_dag ordLt fuel model
                  let ord = DagClosure.drain_order ordLt fuel model.nodes
                  let reach = Dag.Reach.ofDag dag
                  let held = dag.Nodes |> Map.toList |> List.map fst

                  r2,
                  t
                  |> Differential.over (List.allPairs held held) (fun (a, b) t ->
                      let dropped =
                          match DagPrimitives.reach_between ordLt fuel model fuel idx ord a b with
                          | [] -> []
                          | _ :: rest -> rest
                          |> List.map (fun n -> n.nid)

                      let production = Dag.Reach.between reach a b |> List.map _.Id

                      Differential.check
                          (dropped = production)
                          (fun () ->
                              sprintf "between %s %s: production %A, the dropping index %A" a b production dropped)
                          t)

              let t = Differential.seeded 10 (ConfRng.ofSeed 4572) step (Tally.empty [])
              Differential.expectLoses "an index between that drops a node disagrees with production" t ]
