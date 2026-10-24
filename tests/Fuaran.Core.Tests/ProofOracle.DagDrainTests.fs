module Fuaran.Core.Tests.ProofOracle.DagDrainTests

// The Proofs.Oracle differential for the DAG beneath the fold, continued: the id-ordered drain
// (`proofs/DagClosure.fst` section 13, Phase 156) and `Dag.tryReplayTo` beside the extracted
// `replay_to` (section 16, Phase 309), split from ProofOracle.DagFoldTests.fs for length.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges
open Fuaran.Core.Tests.MergeOracle

// ---------------------------------------------------------------------------
//  Phase 156 — the ID-ORDERED DRAIN over an abstract node set, beside production's own.
//
//  Section 13 of `proofs/DagClosure.fst` proves the drain deterministic, a linear extension of the
//  parent relation, and total on an acyclic set — over an ABSTRACT node set and an ABSTRACT total
//  order on ids. What ties that abstract order to production's is here and nowhere else:
//  `Dag.topoCore` keeps its ready frontier sorted with `List.sort`, which on strings is F#'s
//  structural comparison and so `String.CompareOrdinal`, and that is the `lt` the model is
//  instantiated at below.
//
//  Two shapes are compared, and the second is the point. Per LANE HEAD the closure is a spine and
//  the frontier is one element wide at every step, which is the case Phase 142 already proves and
//  which no tie-break can get wrong. Over a MULTI-HEAD UNION — the lane heads folded together with
//  `Dag.merge`, the convergent node a real reconciliation writes — the frontier is N wide once the
//  base is drained, and the tie-break is what decides the sequence. That is the case section 13
//  exists for, and it is why the go-red below runs there.
// ---------------------------------------------------------------------------

/// The id order production's `List.sort` imposes is the model's `lt` instantiated (`ordLt`, the fixtures'
/// `VectorOracle.Merge.fs`). The go-red instrument: the same drain at the REVERSED order, which is `pick_min` of a flipped
/// total order and so the frontier's MAXIMUM. It satisfies everything the model asks of a
/// selector — it is still a member of the frontier it is handed — so what it breaks is agreement
/// with production and nothing else, which is exactly the claim these cases make. On a spine it
/// cannot lose, because a one-element frontier has one minimum and one maximum; the multi-head
/// union is what gives it room.
let private ordGt (a: string) (b: string) = System.String.CompareOrdinal(a, b) > 0

/// The model nodes of a production DAG restricted to one head's ancestor closure — "the union" the
/// drain is over. Nothing is recomputed across the bridge: the ids are the content hashes
/// production minted, so a disagreement can only be about the ORDER.
let private modelClosure (dag: Dag.T<'Op>) (head: string) : DagClosure.node<'Op> list =
    let anc = Dag.ancestorsOf dag head

    dag.Nodes
    |> Map.toList
    |> List.filter (fun (id, _) -> Set.contains id anc)
    |> List.map (fun (_, n) ->
        { DagClosure.nid = n.Id
          DagClosure.nparents = n.Parents
          DagClosure.nop = n.Op })

/// One head whose closure is EVERY node: the lane heads folded together with `Dag.merge`. Returns
/// the union head and the DAG carrying the merge nodes.
let mergedUnion
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (mergeOp: 'Op)
    (baseId: string)
    (heads: string list)
    (dag: Dag.T<'Op>)
    : string * Dag.T<'Op> =
    match heads with
    | [] -> baseId, dag
    | h0 :: rest ->
        rest
        |> List.fold
            (fun (acc, d) h ->
                if h = acc then
                    acc, d
                else
                    Dag.merge (OpStream.canonical OpStream.defaultHash w) (Human "merge") mergeOp acc h d
                    |> Reference.built)
            (h0, dag)

let private drainedOrder (r: DagClosure.drain_result) : string list option =
    match r with
    | DagClosure.Drained ord -> Some ord
    | DagClosure.Refused _ -> None

/// Every way the extracted drain and production's own topological order disagree over one head's
/// closure, plus how wide the ready frontier ever got — a run whose frontier never passed one
/// compared two spines and measured nothing the spine theorem did not already cover.
let private drainDisagreements (lt: string -> string -> bool) (dag: Dag.T<'Op>) (head: string) : string list * int =
    let ns = modelClosure dag head

    let model =
        drainedOrder (DagClosure.drain DagClosure.IgnoreDangling lt (drainFuel ns) ns)

    // The widest ready frontier the closure ever presents: the nodes whose in-closure parents are
    // all drained, at the step that has the most of them. Computed from the model's own `frontier`
    // over its own emitted prefix, so it measures the drain that ran rather than a re-derivation.
    let widest =
        match model with
        | None -> 0
        | Some ord ->
            let closure = ns |> List.map (fun (n: DagClosure.node<'Op>) -> n.nid)

            let rec go (rest: DagClosure.node<'Op> list) (emitted: string list) (pending: string list) best =
                match pending with
                | [] -> best
                | id :: tl ->
                    let f = DagClosure.frontier rest closure emitted
                    let best' = max best (List.length f)
                    go (DagClosure.remove_id rest id) (emitted @ [ id ]) tl best'

            go ns [] ord 0

    match Dag.tryTopoOrder dag head, model with
    | Ok production, Some m when production = m -> [], widest
    | Ok production, Some m ->
        [ sprintf
              "head %s: the drain differs\n  production: [%s]\n  model:      [%s]"
              head
              (String.concat "; " production)
              (String.concat "; " m) ],
        widest
    | Ok _, None -> [ sprintf "head %s: the model REFUSED a closure with no dangling parent" head ], widest
    | Error e, _ ->
        [ sprintf "head %s: production reports a cyclic closure it cannot have: %A" head e.Unplaced ], widest

// ---------------------------------------------------------------------------
//  Phase 309 — `Dag.tryReplayTo` beside the extracted `replay_to` (DagClosure.fst section 16).
//
//  The model replays a head as production does: refuse a head the DAG does not hold, drain the
//  head's closure with section 13's id-ordered drain, refuse a drain that did not place the whole
//  closure, then apply each drained node's op in order. Both sides are rendered to one string —
//  the state through the domain's own hash, a refusal by its case and the id it names — so a
//  disagreement says which clause moved.
// ---------------------------------------------------------------------------

/// Production's one `Cyclic` answer (Phase 529) names every unplaced node of the replayed closure, where
/// the model's `RCyclicHistory` names the head: a cyclic closure leaves its own head unplaced (a placed
/// node's ancestors are all placed), so the bridge renders production's answer as the head exactly when
/// the head is among the nodes it names, and as the whole set otherwise — which then disagrees.
let renderCyclic (head: string) (c: Dag.Cyclic) : string =
    if List.contains head c.Unplaced then
        "cyclic " + head
    else
        sprintf "cyclic without its head %s: %A" head c.Unplaced

let private renderProductionReplay
    (hashState: 'State -> string)
    (head: string)
    (r: Result<'State, Dag.ReplayFault<'Rej>>)
    : string =
    match r with
    | Ok s -> "ok " + hashState s
    | Error(Dag.ReplayFault.UnknownHead h) -> "unknown-head " + h
    | Error(Dag.ReplayFault.Cyclic c) -> renderCyclic head c
    | Error(Dag.ReplayFault.Rejected(id, e)) -> sprintf "rejected %s %A" id e

let private renderModelReplay
    (hashState: 'State -> string)
    (r: DagFold.outcome<'State, DagClosure.replay_fault<'Rej>>)
    : string =
    match r with
    | DagFold.Ok s -> "ok " + hashState s
    | DagFold.Error(DagClosure.RUnknownHead h) -> "unknown-head " + h
    | DagFold.Error(DagClosure.RCyclicHistory h) -> "cyclic " + h
    | DagFold.Error(DagClosure.RRejected(id, e)) -> sprintf "rejected %s %A" id e

/// The replay's render pair at one head: production's `tryReplayTo` answer and the model's `replay_to`.
let private replay
    (hashState: 'State -> string)
    (head: string)
    : Differential<Result<'State, Dag.ReplayFault<'Rej>>, DagFold.outcome<'State, DagClosure.replay_fault<'Rej>>> =
    Differential.pair (renderProductionReplay hashState head) (renderModelReplay hashState)

/// The extracted `replay_to` over a model DAG, at production's id order, with fuel for every node.
let private modelReplay
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (d: DagClosure.dag<'Op>)
    (head: string)
    : DagFold.outcome<'State, DagClosure.replay_fault<'Rej>> =
    let fuel = drainFuel d.nodes
    DagClosure.replay_to (modelApply w) ordLt fuel d fuel state0 head

/// One head, both sides: `None` when they agree, the disagreement otherwise; and which arm it was.
let private replayDisagreement
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (hashState: 'State -> string)
    (state0: 'State)
    (bridge: Dag.T<'Op> -> DagClosure.dag<'Op>)
    (dag: Dag.T<'Op>)
    (head: string)
    : string option * string =
    let pair = replay hashState head
    let p = pair.Production(Dag.tryReplayTo w state0 dag head)
    let m = pair.Model(modelReplay w state0 (bridge dag) head)
    let arm = p.Split(' ')[0]

    if p = m then
        None, arm
    else
        Some(sprintf "head %s: the replay differs\n  production: %s\n  model:      %s" head p m), arm

/// The go-red bridge: the model is handed the DAG with ONE node of the head's closure dropped — the
/// node the store would have lost to a silent `Map.add` replacement before Phase 296, which is the
/// failure the `node-ids-distinct` row hid. Everything else is production's.
let private droppingOneNode (head: string) (dag: Dag.T<'Op>) : DagClosure.dag<'Op> =
    let anc = Dag.ancestorsOf dag head

    let victim = anc |> Set.toList |> List.filter (fun id -> id <> head) |> List.tryHead

    let m = toModelDag dag

    match victim with
    | None -> m
    | Some v -> { DagClosure.nodes = m.nodes |> List.filter (fun n -> n.nid <> v) }

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 156: the ID-ORDERED DRAIN beside production's own topological order ----

          testCase "the extracted drain is production's topological order over every lane head"
          <| fun _ ->
              // The SPINE case: one head's closure is a chain, the frontier is one element wide at
              // every step, and Phase 142 already proves the two orders coincide there. It runs
              // because a green multi-head case means little if the easy case is broken.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  let _, hs, dag = productionDag planW planLaneGen.BaseOp lanes

                  r',
                  t
                  |> Differential.over hs (fun h t ->
                      let diffs, _ = drainDisagreements ordLt dag h

                      (Tally.bump "Heads" t, diffs)
                      ||> List.fold (fun t d ->
                          Tally.disagree (sprintf "iter %d\n%s\n%s" i (renderLanes encPlanOp lanes) d) t))

              let t = Differential.seeded 80 (ConfRng.ofSeed 1560) step (Tally.empty [ "Heads" ])
              Differential.expectAgrees "the drain over every lane head" t
              Expect.isGreaterThan (Tally.count "Heads" t) 0 "some lane heads were drained"

          testCase "the extracted drain is production's topological order over a MULTI-HEAD UNION"
          <| fun _ ->
              // The case Phase 142 does not cover and section 13 exists for: the lane heads folded
              // into one convergent head with `Dag.merge`, so the union head's closure is every
              // node and the ready frontier is N wide once the base is drained. The adequacy guard
              // is the frontier WIDTH — a run whose frontier never passed one has re-measured the
              // spine case under a different name.
              // the frontier widths reached are recorded as a set; the adequacy guard reads its maximum
              let drained (where: string) (diffs: string list, width: int) (t: Tally) =
                  let t = Tally.bump "Unions" t

                  match diffs with
                  | [] -> Tally.reach "Widths" (string width) t
                  | d :: _ -> Tally.disagree (where + "\n" + d) t

              let planStep (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag

                  r',
                  drained
                      (sprintf "iter %d\n%s" i (renderLanes encPlanOp lanes))
                      (drainDisagreements ordLt merged unionHead)
                      t

              let treeStep (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = treeLaneGen.Lanes 4 rng
                  let baseId, hs, dag = productionDag treeW treeLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion treeW treeLaneGen.BaseOp baseId hs dag

                  r',
                  drained
                      (sprintf "reference witness iter %d\n%s" i (renderLanes treeW.Encode lanes))
                      (drainDisagreements ordLt merged unionHead)
                      t

              let r, t =
                  Tally.empty [ "Unions" ]
                  |> Tally.sets [ "Widths" ]
                  |> Differential.seededFrom 80 (ConfRng.ofSeed 1561) planStep

              let t = Differential.seeded 40 r treeStep t
              Differential.expectAgrees "the drain over a multi-head union" t
              Expect.isGreaterThan (Tally.count "Unions" t) 0 "some unions were drained"
              let widest = Tally.reached "Widths" t |> Seq.map int |> Seq.fold max 0

              Expect.isGreaterThan
                  widest
                  1
                  (sprintf
                      "the ready frontier never held more than one node (widest=%d), so the tie-break was never exercised and this case re-measured the spine"
                      widest)

          testCase "a model draining LARGEST-id-first loses over the same unions — the measurement can fail"
          <| fun _ ->
              // The teeth. The reversed order is as legitimate a selector as the model's own — it
              // returns a member of the frontier it is handed, which is all `picks_from_frontier`
              // asks — so a green run above certifies nothing unless this one is red. It is
              // deliberately run over the UNIONS and not the lane heads: on a spine the frontier
              // holds one id, whose minimum and maximum are the same id, and no tie-break can lose.
              let step (_: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag

                  match drainDisagreements ordGt merged unionHead with
                  | [], _ -> r', t
                  | d :: _, _ -> r', Tally.disagree d t

              let t = Differential.seeded 80 (ConfRng.ofSeed 1561) step (Tally.empty [])

              Differential.expectLoses
                  "a drain taking the LARGEST ready id must disagree with production — this comparison cannot lose"
                  t

              Expect.stringContains
                  (List.head (Tally.disagreements t))
                  "the drain differs"
                  "the disagreement names what moved"

          testCase "the drain is a function of the node SET — every arrival order gives one sequence"
          <| fun _ ->
              // `drain_deterministic`, measured. `DagClosure.frontier` answers in the WORK LIST's
              // order, so permuting the node set hands the selector a permuted frontier; only an
              // order-invariant selector survives that, and this is the arm that would catch a
              // drain that took the head of an unsorted frontier instead of its minimum.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag
                  let ns = modelClosure merged unionHead
                  let canonical = DagClosure.drain DagClosure.IgnoreDangling ordLt (drainFuel ns) ns

                  // Three permutations, two of them fixed so the case cannot go quiet on a seed
                  // that happened to shuffle nothing, and one drawn.
                  let shuffled, r'' = ConfRng.shuffle ns r'

                  let perms =
                      [ List.rev ns
                        (match ns with
                         | [] -> []
                         | h :: t -> t @ [ h ])
                        shuffled ]

                  r'',
                  t
                  |> Differential.over perms (fun perm t ->
                      let got = DagClosure.drain DagClosure.IgnoreDangling ordLt (drainFuel perm) perm

                      t
                      |> Tally.bump "Compared"
                      |> Differential.check (got = canonical) (fun () ->
                          sprintf
                              "iter %d: a permuted node set drained differently\n%s\n  canonical: %A\n  permuted:  %A"
                              i
                              (renderLanes encPlanOp lanes)
                              canonical
                              got))

              let t =
                  Differential.seeded 60 (ConfRng.ofSeed 1562) step (Tally.empty [ "Compared" ])

              Differential.expectAgrees "the drain over permuted node sets" t
              Expect.isGreaterThan (Tally.count "Compared" t) 0 "some permutations were drained"

          testCase "the two dangling-parent policies are the two production call sites, and differ only on a dangler"
          <| fun _ ->
              // `drain_policies_agree` and `drain_refusal_characterised`, measured against the two
              // callers they model. On a closure — which is parent-closed, so no parent lies
              // outside — the policies agree and `Dag.verifyDag` is happy. Drop the BASE node from
              // the set and every lane's first node names a parent outside it: `RefuseDangling`
              // refuses, exactly as `Dag.firstBreak` reports `MissingParent`, while
              // `IgnoreDangling` still drains every remaining node, exactly as `Dag.topoCore`'s
              // `parentsIn` filter makes it.
              let ordinal (ids: string list) =
                  List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)) ids

              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag
                  let ns = modelClosure merged unionHead
                  let ignored = DagClosure.drain DagClosure.IgnoreDangling ordLt (drainFuel ns) ns
                  let refused = DagClosure.drain DagClosure.RefuseDangling ordLt (drainFuel ns) ns

                  // … and now WITH one: the base node dropped, so every lane's first node names it
                  // from outside the set.
                  let orphaned = ns |> List.filter (fun n -> n.nid <> baseId)

                  let t =
                      t
                      |> Differential.check
                          (Dag.verifyDag (OpStream.canonical OpStream.defaultHash planW) merged)
                          (fun () -> sprintf "iter %d: the production DAG holds every parent it names" i)
                      |> Differential.check (ignored = refused) (fun () ->
                          sprintf "iter %d: the policies differ on a closure with no dangling parent" i)
                      |> Tally.bump "Agreements"

                  let t =
                      match DagClosure.drain DagClosure.RefuseDangling ordLt (drainFuel orphaned) orphaned with
                      | DagClosure.Refused id ->
                          t
                          |> Tally.bump "Refusals"
                          |> Differential.check
                              (orphaned |> List.exists (fun n -> n.nid = id && List.contains baseId n.nparents))
                              (fun () ->
                                  sprintf "iter %d: the refusal names a node that really does name the dropped base" i)
                      | DagClosure.Drained _ ->
                          Tally.disagree (sprintf "iter %d: RefuseDangling drained a set missing the base" i) t

                  r',
                  match DagClosure.drain DagClosure.IgnoreDangling ordLt (drainFuel orphaned) orphaned with
                  | DagClosure.Drained ord ->
                      let placed = ordinal ord
                      let remaining = orphaned |> List.map _.nid |> ordinal

                      Differential.check
                          (placed = remaining)
                          (fun () ->
                              sprintf
                                  "iter %d: IgnoreDangling places every remaining node — the dropped parent constrains nothing\n  placed:    %A\n  remaining: %A"
                                  i
                                  placed
                                  remaining)
                          t
                  | DagClosure.Refused _ -> Tally.disagree (sprintf "iter %d: IgnoreDangling refused" i) t

              let t =
                  Differential.seeded 60 (ConfRng.ofSeed 1563) step (Tally.empty [ "Agreements"; "Refusals" ])

              Differential.expectAgrees "the two dangling-parent policies" t
              Expect.isGreaterThan (Tally.count "Agreements" t) 0 "the policies were compared on a clean closure"
              Expect.isGreaterThan (Tally.count "Refusals" t) 0 "the refusing policy was made to refuse"

          testCase "a CYCLE is surfaced as a SHORT drain — the model and production read the same signal"
          <| fun _ ->
              // `drain_complete_is_acyclic`'s other side, measured. Production's `topoCore` does
              // not raise on a cycle: a node inside one never reaches in-degree zero, so the
              // emitted list is strictly shorter than the closure, and `Dag.isAcyclic` /
              // `Dag.tryTopoOrder` are the callers that read that comparison. A cyclic DAG cannot
              // be built through `Dag.append` (the parent's id is minted before the child's), so
              // it is built by hand here — which is exactly the hand-crafted or tampered JSONL load
              // `Dag.fromJsonl`'s docstring warns about.
              let node id parents : DagNode<PlanOp> =
                  { Id = id
                    Parents = parents
                    Actor = Human "cyclic"
                    Op = planLaneGen.BaseOp }

              let cyclic: Dag.T<PlanOp> =
                  [ node "a" []
                    node "b" [ "a"; "d" ] // b waits on d …
                    node "c" [ "b" ]
                    node "d" [ "c" ] ] // … and d waits on c waits on b
                  |> List.map (fun n -> n.Id, n)
                  |> Map.ofList
                  |> DagOf.nodes

              // Production: the closure is four nodes and only "a" is ever ready.
              Expect.isFalse (Dag.isAcyclic cyclic "d") "production sees the cycle"

              match Dag.tryTopoOrder cyclic "d" with
              | Ok _ -> failtest "production must refuse a cyclic closure through tryTopoOrder"
              | Error e -> Expect.equal e.Unplaced [ "b"; "c"; "d" ] "production names every node of the cycle"

              // The model: the same short drain, from the same nodes.
              let ns = modelClosure cyclic "d"
              Expect.equal (List.length ns) 4 "the closure is the whole hand-built set"

              match DagClosure.drain DagClosure.IgnoreDangling ordLt (drainFuel ns) ns with
              | DagClosure.Drained ord ->
                  Expect.equal ord [ "a" ] "the model drains the acyclic prefix and stops, as topoCore does"

                  Expect.isFalse
                      (DagClosure.is_topo_enum ns ord)
                      "and a short drain is not a topological enumeration — which is the signal isAcyclic reads"
              | DagClosure.Refused _ -> failtest "the hand-built set holds every parent it names"

              // … and the same set with the back edge removed drains completely and IS one, so the
              // comparison above is a discrimination rather than a refusal of everything.
              let acyclic: Dag.T<PlanOp> =
                  [ node "a" []; node "b" [ "a" ]; node "c" [ "b" ]; node "d" [ "c" ] ]
                  |> List.map (fun n -> n.Id, n)
                  |> Map.ofList
                  |> DagOf.nodes

              let ns' = modelClosure acyclic "d"

              match
                  DagClosure.drain DagClosure.IgnoreDangling ordLt (drainFuel ns') ns', Dag.tryTopoOrder acyclic "d"
              with
              | DagClosure.Drained ord, Ok production ->
                  Expect.equal ord production "the model and production agree once the back edge is gone"
                  Expect.isTrue (DagClosure.is_topo_enum ns' ord) "and the complete drain IS a topological enumeration"
              | m, p -> failtestf "the acyclic control disagreed: model=%A production=%A" m p

          // ---- Phase 309: Dag.tryReplayTo beside the extracted replay_to ----

          testCase "the extracted replay_to is production's tryReplayTo over every union, with a missing-head arm"
          <| fun _ ->
              // `replay_to_is_fold_over_drain` and `replay_to_unknown_head_is_refused`, measured.
              // Every lane head, the merged union head (whose closure is every node, so the drain's
              // tie-break decides the order the ops are applied in), and a head the DAG does not
              // hold — the arm the pre-296 `replayTo` answered with the initial state as `Ok`.
              // one counter per replay arm — the first word of the rendered answer
              let checkAll
                  (w: StreamWitness<'Op, 'State, 'Rej>)
                  hashState
                  state0
                  label
                  (dag: Dag.T<'Op>)
                  heads
                  (t: Tally)
                  =
                  t
                  |> Differential.over (heads @ [ "no-such-node" ]) (fun h t ->
                      match replayDisagreement w hashState state0 toModelDag dag h with
                      | None, arm -> Tally.bump arm t
                      | Some d, _ -> Tally.disagree (sprintf "%s\n%s" label d) t)

              let planStep (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag

                  r',
                  checkAll
                      planW
                      planHash
                      planLaneGen.State0
                      (sprintf "plan iter %d" i)
                      merged
                      (unionHead :: baseId :: hs)
                      t

              let treeStep (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = treeLaneGen.Lanes 4 rng
                  let baseId, hs, dag = productionDag treeW treeLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion treeW treeLaneGen.BaseOp baseId hs dag

                  r',
                  checkAll
                      treeW
                      treeHash
                      treeLaneGen.State0
                      (sprintf "tree iter %d" i)
                      merged
                      (unionHead :: baseId :: hs)
                      t

              let r, t =
                  Tally.empty [ "ok"; "unknown-head"; "cyclic"; "rejected" ]
                  |> Differential.seededFrom 60 (ConfRng.ofSeed 3090) planStep

              let t = Differential.seeded 30 r treeStep t
              Differential.expectAgrees "replay_to" t
              Expect.isGreaterThan (Tally.count "ok" t) 0 "some heads replayed to a state"
              Expect.isGreaterThan (Tally.count "unknown-head" t) 0 "the missing-head arm ran"

          testCase "a CYCLIC closure is refused as cyclic by both — and the acyclic control replays"
          <| fun _ ->
              let node id parents : DagNode<PlanOp> =
                  { Id = id
                    Parents = parents
                    Actor = Human "cyclic"
                    Op = planLaneGen.BaseOp }

              let build ns : Dag.T<PlanOp> =
                  DagOf.nodes (ns |> List.map (fun (n: DagNode<PlanOp>) -> n.Id, n) |> Map.ofList)

              let cyclic =
                  build [ node "a" []; node "b" [ "a"; "d" ]; node "c" [ "b" ]; node "d" [ "c" ] ]

              let acyclic =
                  build [ node "a" []; node "b" [ "a" ]; node "c" [ "b" ]; node "d" [ "c" ] ]

              match replayDisagreement planW planHash planLaneGen.State0 toModelDag cyclic "d" with
              | None, arm -> Expect.equal arm "cyclic" "both refuse the cyclic closure, naming the head"
              | Some d, _ -> failtest d

              match replayDisagreement planW planHash planLaneGen.State0 toModelDag acyclic "d" with
              | None, arm -> Expect.equal arm "ok" "the acyclic control replays on both sides"
              | Some d, _ -> failtest d

          testCase
              "a model handed a store that LOST one closure node disagrees with tryReplayTo — the measurement can fail"
          <| fun _ ->
              // The teeth, and the failure the `node-ids-distinct` row used to hide: before Phase 296
              // a second node minting a held id REPLACED the first, so the store silently lost a
              // node of some head's history. A model replaying a DAG with one closure node gone must
              // disagree with production somewhere over the pool, or the green case above certifies
              // nothing about which nodes were folded.
              let step (_: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag

                  match
                      replayDisagreement planW planHash planLaneGen.State0 (droppingOneNode unionHead) merged unionHead
                  with
                  | None, _ -> r', t
                  | Some d, _ -> r', Tally.disagree d t

              let t = Differential.seeded 60 (ConfRng.ofSeed 3091) step (Tally.empty [])

              Differential.expectLoses
                  "a store missing a closure node must replay differently — this comparison cannot lose"
                  t

              Expect.stringContains
                  (List.head (Tally.disagreements t))
                  "the replay differs"
                  "the disagreement names what moved"

          testCase "the extracted add_node refuses a differing node and never replaces — beside Dag.append"
          <| fun _ ->
              // `append_refuses_differing_node` and `append_never_replaces`, held to production's
              // `addNode`: the same node twice deduplicates on both sides, and a node that mints a
              // held id with different content is refused on both sides — production through a
              // HashFn that collides on purpose, the model through `same` at production's notion of
              // sameness (parents modulo order, op through its encoding; the model node carries no
              // actor, and the colliding pair here differs in its op).
              let collide: HashFn = fun _ _ -> "same-id"
              let enc = planW.Encode

              let mnode (p: string list) (op: PlanOp) : DagClosure.node<PlanOp> =
                  { DagClosure.nid = "same-id"
                    DagClosure.nparents = p
                    DagClosure.nop = op }

              let same (a: DagClosure.node<PlanOp>) (b: DagClosure.node<PlanOp>) =
                  List.sort a.nparents = List.sort b.nparents && enc a.nop = enc b.nop

              let ops =
                  let lanes, _ = planLaneGen.Lanes 3 (ConfRng.ofSeed 3092)
                  lanes |> List.concat |> List.distinctBy enc

              Expect.isGreaterThan (List.length ops) 1 "two ops that encode differently were drawn"
              let op1 = List.item 0 ops
              let op2 = List.item 1 ops

              // Production.
              let id1, d1 =
                  Dag.append (OpStream.canonical collide planW) (Human "x") op1 "" Dag.empty
                  |> Reference.built

              match Dag.append (OpStream.canonical collide planW) (Human "x") op1 "" d1 with
              | Ok { Id = id; Dag = d } ->
                  Expect.equal id id1 "the same node twice is one node"
                  Expect.equal d.Nodes.Count 1 "and the DAG is unchanged"
              | Error f -> failtestf "production refused an identical node: %A" f

              match Dag.append (OpStream.canonical collide planW) (Human "x") op2 "" d1 with
              | Error(DagAppendFault.ContentIdCollision id) -> Expect.equal id id1 "production names the colliding id"
              | other -> failtestf "production must refuse a differing node minting a held id: %A" other

              // The model.
              let m0: DagClosure.dag<PlanOp> = { DagClosure.nodes = [] }

              let m1 =
                  match DagClosure.add_node same (mnode [] op1) m0 with
                  | DagClosure.Appended(_, d) -> d
                  | DagClosure.Collision _ -> failtest "the model refused a node into an empty DAG"

              match DagClosure.add_node same (mnode [] op1) m1 with
              | DagClosure.Appended(id, d) ->
                  Expect.equal id "same-id" "the model deduplicates"
                  Expect.equal d m1 "and leaves the DAG unchanged"
              | DagClosure.Collision _ -> failtest "the model refused an identical node"

              match DagClosure.add_node same (mnode [] op2) m1 with
              | DagClosure.Collision id -> Expect.equal id "same-id" "the model refuses the differing node by name"
              | DagClosure.Appended _ -> failtest "the model must refuse a differing node minting a held id"

              // The go-red: a `same` that accepts everything is the pre-296 replacement's twin — it
              // admits the differing node, which production refuses.
              match DagClosure.add_node (fun _ _ -> true) (mnode [] op2) m1 with
              | DagClosure.Appended _ -> ()
              | DagClosure.Collision _ -> failtest "a permissive sameness admits the differing node" ]
