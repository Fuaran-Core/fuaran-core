module Fuaran.Core.Tests.ProofOracle.DagFoldTests

// Phase 131 — the F* oracle as a corpus host.
//
// `proofs/DagFold.fst` is an F* model of the N-lane DAG fold with fold confluence proved as a
// theorem, and `proofs/DagClosure.fst` (its sections 11 to 21 until Phase 534) the DAG beneath it;
// `proofs/oracle/DagFold.fs` and `proofs/oracle/DagClosure.fs` are the two extracted to F# by F*'s
// own code generator.
// This file runs the extracted model BESIDE the production fold — `Dag.reconcileMany` through a
// real DAG, rendered exactly as `FoldConfluence.foldOnce` renders it — over the same generated lane
// sets the Phase 100 pack certifies production against, and over the wire corpus's `ops/` fixtures.
// A disagreement is red and is shrunk to a minimal lane set before it is reported.
//
// Three things are compared per lane set, per arrival order:
//   1. the OUTCOME — folded to the same state hash / halted with the same canonical report /
//      rejected with the same reason (`LaneFoldOutcome` equality);
//   2. the MERGE SCRIPT — the op sequence `reconcileMany` composes, or its canonical halt report;
//   3. the oracle's own outcomes across the sampled arrival orders — one distinct outcome, which is
//      the theorem's statement instantiated on the extracted code.
//
// What this does and does not establish is stated in proofs/README.md: the model starts where the
// lane deltas are known, so agreement here is a claim about production's `betweenOps` recovery and
// pairwise sweep together against a model whose fold is proved order-invariant.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.
// The drain and replay differentials continue in `ProofOracle.DagDrainTests.fs`, and the merged-head,
// reconcile, primitive and boundary ones in `ProofOracle.DagMergeTests.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges

/// Counts one production outcome into a `foldTally`: folded clean, or halted (a rejection counts as neither).
let private countOutcome (outcome: LaneFoldOutcome) (t: Tally) : Tally =
    match outcome with
    | LaneFolded _ -> Tally.bump "Folded" t
    | LaneHalted _ -> Tally.bump "Halted" t
    | LaneRejected _ -> t

/// Run the differential over `iterations` generated lane sets; the first disagreement is shrunk
/// and reported with its seed, iteration and the lanes in the domain's own encoding, and the run
/// stops there.
let private differential
    (label: string)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (oracleFp: 'Op -> Footprint)
    (hashState: 'State -> string)
    (gen: LaneGen<'Op, 'State>)
    (laneCount: int)
    (seed: int)
    (iterations: int)
    : Tally =
    let differs (ls: 'Op list list) =
        disagreements true w fp oracleFp hashState gen.State0 gen.BaseOp ls

    let diverges (ls: 'Op list list) = not (List.isEmpty (differs ls))

    let step (i: int) (rng: ConfRng.T) (t: Tally) =
        if not (List.isEmpty (Tally.disagreements t)) then
            rng, t
        else
            let lanes, r' = gen.Lanes laneCount rng

            match differs lanes with
            | [] -> r', countOutcome (productionFold w fp hashState gen.State0 gen.BaseOp lanes) t
            | _ ->
                let small = Differential.shrunk diverges FoldConfluence.shrinkLanes lanes

                r',
                Tally.disagree
                    (sprintf
                        "%s: seed=%d iter=%d — the oracle and production DISAGREE; shrunk to\n%s\n%s"
                        label
                        seed
                        (i - 1)
                        (renderLanes w.Encode small)
                        (differs small |> List.head))
                    t

    Differential.seeded iterations (ConfRng.ofSeed seed) step foldTally

// ---------------------------------------------------------------------------
//  The wire corpus's ops/ fixtures as a third lane pool.
//
//  The corpus ops are UI-domain ops (InsertChild / RemoveNode / MoveNode / ReorderChildren /
//  EditNode / UpdateProp / UpdateState / UpdateStyle / ReplaceBinding / ReplaceRoot / Batch), and
//  Core has no reducer for them. What the fold needs of an op is its FOOTPRINT, so each fixture is
//  projected to one here — test-local, mirroring `Ops.footprint`'s clauses for the structural ops
//  (an insert writes its parent's structure and its subtree's content; a remove/move is the
//  unknown-source-parent case; a reorder reads its named children) and treating every
//  target-addressed property edit as a content write on its target. The reducer is a log — order
//  sensitive, deliberately: this pool compares oracle against production under ONE order at a
//  time, and an order-sensitive state makes the folded outcome a real comparison rather than a
//  constant.
// ---------------------------------------------------------------------------

type CorpusOp = { Name: string; Fp: Footprint }

let private corpusW: StreamWitness<CorpusOp, string, string> =
    { Apply = fun op st -> Ok(st + "/" + op.Name)
      Encode = fun op -> op.Name
      Decode = fun _ -> Error "ProofOracle.DagFoldTests: the corpus witness's decode is unused" }

let private corpusFp (op: CorpusOp) = op.Fp

let private unionFp (a: Footprint) (b: Footprint) : Footprint =
    { Reads = Set.union a.Reads b.Reads
      StructureWrites = Set.union a.StructureWrites b.StructureWrites
      ContentWrites = Set.union a.ContentWrites b.ContentWrites
      UnknownParentWrites = Set.union a.UnknownParentWrites b.UnknownParentWrites
      SlotReads = Set.empty
      SlotWrites = Set.empty }

let rec private idsIn (v: JVal) : string list =
    match v with
    | JObj fields ->
        fields
        |> List.collect (fun (k, v) ->
            match k, v with
            | "id", JStr s -> [ s ]
            | _ -> idsIn v)
    | JArr xs -> List.collect idsIn xs
    | _ -> []

/// The footprint of one corpus op, or the reason the projection could not read it.
let rec private corpusFootprint (v: JVal) : Result<Footprint, string> =
    match v with
    | JObj fields ->
        let one x = Set.singleton x

        match strField fields "$type" with
        | Some "InsertChild" ->
            match strField fields "parentId", field fields "child" with
            | Some p, Some child ->
                Ok
                    { Reads = one p
                      StructureWrites = one p
                      ContentWrites = Set.ofList (idsIn child)
                      UnknownParentWrites = noAddr
                      SlotReads = Set.empty
                      SlotWrites = Set.empty }
            | _ -> Error "InsertChild without parentId/child"
        | Some "RemoveNode" ->
            match strField fields "target" with
            | Some t ->
                Ok
                    { Reads = one t
                      StructureWrites = noAddr
                      ContentWrites = one t
                      UnknownParentWrites = one t
                      SlotReads = Set.empty
                      SlotWrites = Set.empty }
            | None -> Error "RemoveNode without target"
        | Some "MoveNode" ->
            match strField fields "target", strField fields "newParentId" with
            | Some t, Some np ->
                Ok
                    { Reads = Set.ofList [ t; np ]
                      StructureWrites = one np
                      ContentWrites = one t
                      UnknownParentWrites = one t
                      SlotReads = Set.empty
                      SlotWrites = Set.empty }
            | _ -> Error "MoveNode without target/newParentId"
        | Some "ReorderChildren" ->
            match strField fields "parentId", field fields "newOrder" with
            | Some p, Some(JArr order) ->
                let named =
                    order
                    |> List.choose (function
                        | JStr s -> Some s
                        | _ -> None)

                Ok
                    { Reads = Set.ofList (p :: named)
                      StructureWrites = one p
                      ContentWrites = noAddr
                      UnknownParentWrites = noAddr
                      SlotReads = Set.empty
                      SlotWrites = Set.empty }
            | _ -> Error "ReorderChildren without parentId/newOrder"
        | Some "ReplaceRoot" ->
            match field fields "node" with
            | Some node ->
                Ok
                    { Reads = noAddr
                      StructureWrites = noAddr
                      ContentWrites = Set.ofList (idsIn node)
                      UnknownParentWrites = noAddr
                      SlotReads = Set.empty
                      SlotWrites = Set.empty }
            | None -> Error "ReplaceRoot without node"
        | Some "Batch" ->
            match field fields "ops" with
            | Some(JArr ops) ->
                ops
                |> List.map corpusFootprint
                |> List.fold
                    (fun acc r ->
                        match acc, r with
                        | Ok a, Ok b -> Ok(unionFp a b)
                        | Error e, _
                        | _, Error e -> Error e)
                    (Ok
                        { Reads = noAddr
                          StructureWrites = noAddr
                          ContentWrites = noAddr
                          UnknownParentWrites = noAddr
                          SlotReads = Set.empty
                          SlotWrites = Set.empty })
            | _ -> Error "Batch without ops"
        | Some other ->
            // EditNode / UpdateProp / UpdateState / UpdateStyle / ReplaceBinding — a property
            // edit on the target node.
            match strField fields "target" with
            | Some t ->
                Ok
                    { Reads = one t
                      StructureWrites = noAddr
                      ContentWrites = one t
                      UnknownParentWrites = noAddr
                      SlotReads = Set.empty
                      SlotWrites = Set.empty }
            | None -> Error(other + " without target")
        | None -> Error "no $type"
    | _ -> Error "not a JSON object"

/// Every ops/ fixture as a `CorpusOp`, or the reason one could not be read — an unreadable
/// fixture FAILS rather than being dropped, because a lane pool with silent holes certifies
/// less than it says.
let private loadCorpusOps (root: string) : Result<CorpusOp list, string> =
    Directory.GetFiles(Path.Combine(root, "ops"), "*.json")
    |> Array.sort
    |> Array.toList
    |> List.map (fun path ->
        let name = Path.GetFileNameWithoutExtension path

        match Json.parse (File.ReadAllText path) with
        | Error e -> Error(sprintf "%s: not JSON (%s)" name e)
        | Ok v ->
            match corpusFootprint v with
            | Ok fp -> Ok { Name = name; Fp = fp }
            | Error why -> Error(sprintf "%s: %s" name why))
    |> List.fold
        (fun acc r ->
            match acc, r with
            | Ok ops, Ok op -> Ok(ops @ [ op ])
            | Error e, _ -> Error e
            | _, Error e -> Error e)
        (Ok [])

/// The corpus pool's lane sets, in order: every pair and triple of one-op lanes, then a two-op lane
/// beside a one-op lane at every position. Multi-op lanes are where the WALK is exercised, and a lane
/// whose ops interfere with each other is legal (a lane is one writer's sequence) and must not be
/// reported as a cross-lane halt.
let private corpusLaneSets (ops: CorpusOp list) : CorpusOp list list list =
    let n = List.length ops

    [ for i in 0 .. n - 1 do
          for j in i + 1 .. n - 1 do
              yield [ [ List.item i ops ]; [ List.item j ops ] ]

              for k in j + 1 .. n - 1 do
                  yield [ [ List.item i ops ]; [ List.item j ops ]; [ List.item k ops ] ]

      for i in 0 .. n - 2 do
          yield [ [ List.item i ops; List.item (i + 1) ops ]; [ List.item ((i + 2) % n) ops ] ] ]

// ---------------------------------------------------------------------------
//  The theorem's HYPOTHESIS, measured on the domain it names (Phase 132).
//
//  `fold_confluence`'s one domain hypothesis is `independence_diamond`: ops the model's
//  `independent` declares disjoint and which BOTH APPLY at a state each apply after the other
//  and reach the same state. Phase 131 assumed the stronger `independence_sound` — commuting at
//  every state, REJECTIONS INCLUDED — which the reference algebra cannot keep (the last case
//  below is the witness). A hypothesis stated and never measured is a theorem about a domain
//  nobody has, so this pool measures it on the reference tree witness itself.
// ---------------------------------------------------------------------------

/// Every way a witness breaks the diamond over one pool of ops and one pool of states, and the
/// number of (pair, state) triples at which the PREMISE was actually met — both orders asked
/// for. `oracleFp` is the footprint the check runs under; handing it a blind one is what proves
/// this measurement can fail, exactly as the differential's go-red case does.
let private diamondBreaks
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (oracleFp: 'Op -> Footprint)
    (ops: 'Op list)
    (states: 'State list)
    (t0: Tally)
    : Tally =
    let apply = modelApply w

    let premises =
        seq {
            for a in ops do
                for b in ops do
                    if DagFold.independent (toModelFootprint (oracleFp a)) (toModelFootprint (oracleFp b)) then
                        for s in states do
                            yield a, b, s
        }

    t0
    |> Differential.over premises (fun (a, b, s) t ->
        match apply a s, apply b s with
        | DagFold.Ok sa, DagFold.Ok sb ->
            // The premise is met here, and only here: both ops apply at `s`.
            let broke (why: string) =
                Tally.disagree (
                    sprintf "independent %s / %s both apply at a state, but %s" (w.Encode a) (w.Encode b) why
                )

            let t = Tally.bump "Met" t

            match apply b sa, apply a sb with
            | DagFold.Ok t1, DagFold.Ok t2 when t1 = t2 -> t
            | DagFold.Ok _, DagFold.Ok _ -> broke "the two orders reach different states" t
            | DagFold.Error _, _ -> broke "the second does not apply after the first" t
            | _, DagFold.Error _ -> broke "the first does not apply after the second" t
        | _ -> t)

/// The op and state pools Phase 80's construction yields for the reference witness: applyable
/// skeleton-op scripts threaded from the base tree (`treeLaneGen` — the public form of that
/// construction, shared with the differential above), pooled into ops, and every state a prefix
/// of that pool reaches. The states matter: the hypothesis quantifies over ALL of them, and a
/// check at the base tree alone would measure one instance of a `forall s`.
let private treeDiamondSample (oracleFp: SkeletonOp<RNode, string> -> Footprint) (seed: int) (trials: int) : Tally =
    let step (_: int) (r: ConfRng.T) (t: Tally) =
        let lanes, r' = treeLaneGen.Lanes 3 r
        let ops = List.concat lanes

        let states =
            ops
            |> List.fold
                (fun (acc, cur) op ->
                    match treeW.Apply op cur with
                    | Ok t -> (acc @ [ t ]), t
                    | Error _ -> acc, cur)
                ([ treeBase ], treeBase)
            |> fst

        r', diamondBreaks treeW oracleFp ops states t

    Differential.seeded trials (ConfRng.ofSeed seed) step (Tally.empty [ "Met" ])

// ---------------------------------------------------------------------------
//  Phase 134 — the DAG BENEATH the fold, differentially.
//
//  Sections 0–10 of the model start where the lane deltas are known, so until now the oracle was
//  fed them directly while production rebuilt them from a content-addressed DAG, and the
//  difference between those two starting points was the widest unproved gap in the claims ladder.
//  Section 11 closes it on the model side (`between_chain`, `reconcile_many_dag_eq`,
//  `fold_confluence_dag`); what runs here is the other half — production's OWN
//  `Dag.ancestorsOf` / `topoOrder` / `Dag.between` / `Dag.betweenOps`, on the DAG `foldOnce`
//  actually builds, against the model's recovery on the SAME nodes.
//
//  Nothing is recomputed across the bridge: the ids the model walks are the content hashes
//  production minted, so a disagreement can only be about the RECOVERY. The one step the model
//  does not prove — that Kahn's frontier drain and the reverse of the parent walk are the same
//  order on a spine — is exactly what these cases measure.
// ---------------------------------------------------------------------------

/// A production `Dag.T` as the model reads it: the same nodes, the same content ids, the same
/// parent lists.
let toModelDag (dag: Dag.T<'Op>) : DagClosure.dag<'Op> =
    { DagClosure.nodes =
        dag.Nodes
        |> Map.toList
        |> List.map (fun (_, n) ->
            { DagClosure.nid = n.Id
              DagClosure.nparents = n.Parents
              DagClosure.nop = n.Op }) }

/// The faithful bridge — what every real run uses.
let private unperturbed (_: string) (_: string list) (dag: Dag.T<'Op>) : DagClosure.dag<'Op> = toModelDag dag

/// The go-red instrument, and the fold family's blind-footprint counterpart for this layer: every
/// lane's FIRST node is re-parented from the base onto the PREVIOUS lane's head, so the model
/// walks a spine the DAG does not have. The ids are untouched, so every lookup still succeeds and
/// the only thing that has moved is the shape the recovery walks — which is the thing under test.
let private reparented (baseId: string) (heads: string list) (dag: Dag.T<'Op>) : DagClosure.dag<'Op> =
    let previousHead (actor: Actor) =
        match actor with
        | Human name when name.StartsWith "lane-" ->
            match System.Int32.TryParse(name.Substring 5) with
            | true, i when i > 0 && i - 1 < List.length heads -> Some(List.item (i - 1) heads)
            | _ -> None
        | _ -> None

    { DagClosure.nodes =
        dag.Nodes
        |> Map.toList
        |> List.map (fun (_, n) ->
            let parents =
                if n.Parents = [ baseId ] then
                    match previousHead n.Actor with
                    | Some p when p <> baseId -> [ p ]
                    | _ -> n.Parents
                else
                    n.Parents

            { DagClosure.nid = n.Id
              DagClosure.nparents = parents
              DagClosure.nop = n.Op }) }

/// Every way production's per-lane delta recovery and the model's disagree on ONE lane set, plus
/// how many ops were actually recovered — a run that recovered nothing has compared two empty
/// lists and measured nothing at all.
let private deltaDisagreements
    (bridge: string -> string list -> Dag.T<'Op> -> DagClosure.dag<'Op>)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : string list * int =
    let baseId, heads, dag = productionDag w baseOp lanes
    let model = bridge baseId heads dag
    // One walk step per node in the DAG, which is the bound production's own work-list runs to.
    let fuel = model.nodes

    let diffs =
        heads
        |> List.mapi (fun i head ->
            let p = Dag.betweenOps dag baseId head
            let o = DagClosure.between_ops model fuel baseId head

            if p <> o then
                Some(
                    sprintf
                        "lane %d: the recovered delta differs\n  production: [%s]\n  model:      [%s]"
                        i
                        (p |> List.map w.Encode |> String.concat "; ")
                        (o |> List.map w.Encode |> String.concat "; ")
                )
            else
                None)
        |> List.choose id

    diffs, (heads |> List.sumBy (fun h -> List.length (Dag.betweenOps dag baseId h)))

/// The oracle's fold FROM THE DAG — `fold_once_dag`, the Phase 134 entry point beside
/// `fold_once` — rendered exactly as `FoldConfluence.foldOnce` renders production's. Where
/// `oracleFold` above is handed the lane deltas, this one is handed the DAG and recovers them.
let private oracleFoldFromDag
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (fp: 'Op -> Footprint)
    (hashState: 'State -> string)
    (state0: 'State)
    (baseOp: 'Op)
    (lanes: 'Op list list)
    : LaneFoldOutcome =
    let baseId, heads, dag = productionDag w baseOp lanes
    let model = toModelDag dag

    DagClosure.fold_once_dag (modelApply w) (fp >> toModelFootprint) model model.nodes baseId state0 heads
    |> renderModelOutcome w hashState state0

/// The delta tally: `Recovered` counts the ops recovered across every lane of every trial (the
/// vacuity guard), `Chains` the lane sets carrying a lane of two or more ops (a one-node chain
/// exercises no walk).
let private deltaTally = Tally.empty [ "Recovered"; "Chains" ]

/// Run the delta differential over `iterations` generated lane sets from one pool.
let private deltaDifferential
    (label: string)
    (bridge: string -> string list -> Dag.T<'Op> -> DagClosure.dag<'Op>)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (gen: LaneGen<'Op, 'State>)
    (laneCount: int)
    (seed: int)
    (iterations: int)
    : Tally =
    let step (i: int) (rng: ConfRng.T) (t: Tally) =
        let lanes, r' = gen.Lanes laneCount rng
        let diffs, got = deltaDisagreements bridge w gen.BaseOp lanes

        let t =
            t
            |> Tally.bumpIf (lanes |> List.exists (fun l -> List.length l > 1)) "Chains"
            |> Tally.add "Recovered" got

        r',
        (t, diffs)
        ||> List.fold (fun t d ->
            Tally.disagree (sprintf "%s: seed=%d iter=%d\n%s\n%s" label seed (i - 1) (renderLanes w.Encode lanes) d) t)

    Differential.seeded iterations (ConfRng.ofSeed seed) step deltaTally

let private expectDeltaAgreement (label: string) (t: Tally) =
    Differential.expectAgrees label t
    let recovered = Tally.count "Recovered" t
    Expect.isGreaterThan recovered 0 (sprintf "%s: no ops were recovered at all (recovered=%d)" label recovered)

    Expect.isGreaterThan
        (Tally.count "Chains" t)
        0
        (sprintf "%s: no trial carried a lane longer than one op, so no chain was walked" label)

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [ testCase "the oracle agrees with production over the reference witness at 3 lanes"
          <| fun _ ->
              differential "reference witness" treeW treeFootprint treeFootprint treeHash treeLaneGen 3 1000 120
              |> expectAgreement "reference witness, 3 lanes"

          testCase "the oracle agrees with production over the reference witness at 4 lanes"
          <| fun _ ->
              differential "reference witness" treeW treeFootprint treeFootprint treeHash treeLaneGen 4 2200 60
              |> expectAgreement "reference witness, 4 lanes"

          // ---- the second, non-tree domain (a Map state, its own footprint) ----

          testCase "the oracle agrees with production over the work-plan domain"
          <| fun _ ->
              differential "work-plan domain" planW planFootprint planFootprint planHash planLaneGen 3 4100 150
              |> expectAgreement "work-plan domain, 3 lanes"

          testCase "the oracle agrees with production over the work-plan domain at 5 lanes (sampled orders)"
          <| fun _ ->
              differential "work-plan domain" planW planFootprint planFootprint planHash planLaneGen 5 5100 40
              |> expectAgreement "work-plan domain, 5 lanes"

          // ---- the teeth: the comparison can fail, and shrinks what it reports ----

          testCase "an oracle handed a blind footprint DISAGREES with production, shrunk to two lanes"
          <| fun _ ->
              let blind (_: PlanOp) : Footprint =
                  { Reads = noAddr
                    StructureWrites = noAddr
                    ContentWrites = noAddr
                    UnknownParentWrites = noAddr
                    SlotReads = Set.empty
                    SlotWrites = Set.empty }

              let t =
                  differential "blind oracle" planW planFootprint blind planHash planLaneGen 3 4100 150

              // a blind oracle never halts, so it must disagree with a production fold that does
              Differential.expectLoses "blind oracle" t
              let why = List.head (Tally.disagreements t)
              Expect.stringContains why "shrunk to" "the disagreement was shrunk"
              Expect.stringContains why "outcome differs" "the disagreement names what differed"

              let laneLines =
                  why.Split('\n')
                  |> Array.filter (fun l -> l.StartsWith "  lane ")
                  |> List.ofArray

              Expect.equal (List.length laneLines) 2 (sprintf "two lanes survive the shrink — got:\n%s" why)

          // ---- the wire corpus's ops/ fixtures ----

          testCase "the oracle agrees with production over every pair and triple of corpus ops"
          <| fun _ ->
              match SiblingCorpus.resolve "ops" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let ops =
                      match loadCorpusOps root with
                      | Ok ops -> ops
                      | Error why -> failtestf "a corpus op could not be projected to a footprint: %s" why

                  Expect.isGreaterThan (List.length ops) 8 "the ops/ family carries a real pool"

                  let baseOp =
                      { Name = "base"
                        Fp = (List.head ops).Fp }

                  let check (lanes: CorpusOp list list) (t: Tally) =
                      match disagreements false corpusW corpusFp corpusFp id "" baseOp lanes with
                      | [] -> countOutcome (productionFold corpusW corpusFp id "" baseOp lanes) t
                      | d :: _ -> Tally.disagree (sprintf "corpus lanes\n%s\n%s" (renderLanes corpusW.Encode lanes) d) t

                  let t = foldTally |> Differential.over (corpusLaneSets ops) check
                  Differential.expectAgrees "corpus ops" t
                  Expect.isGreaterThan (Tally.count "Folded" t) 0 "some corpus lane sets fold"
                  Expect.isGreaterThan (Tally.count "Halted" t) 0 "some corpus lane sets halt"

          // ---- the pieces the comparison rests on ----

          testCase "the model's independence agrees with Ops.independent over the reference footprints"
          <| fun _ ->
              // The model's `independent` is `Ops.independent` clause for clause; over every pair
              // of ops the reference generator produces, the two booleans agree.
              let step (_: int) (r: ConfRng.T) (t: Tally) =
                  let lanes, r' = treeLaneGen.Lanes 2 r
                  let ops = List.concat lanes

                  r',
                  t
                  |> Differential.over (List.allPairs ops ops) (fun (a, b) t ->
                      let fa = treeFootprint a
                      let fb = treeFootprint b
                      let model = DagFold.independent (toModelFootprint fa) (toModelFootprint fb)
                      let production = Ops.independent fa fb

                      t
                      |> Tally.bump "Pairs"
                      |> Differential.check (model = production) (fun () ->
                          sprintf
                              "independence of %s / %s: model %b, production %b"
                              (treeW.Encode a)
                              (treeW.Encode b)
                              model
                              production))

              let t = Differential.seeded 40 (ConfRng.ofSeed 77) step (Tally.empty [ "Pairs" ])
              Differential.expectAgrees "independence" t
              Expect.isGreaterThan (Tally.count "Pairs" t) 0 "pairs were compared"

          testCase "the model's conflicts are Dag.conflicts up to the canonical rendering"
          <| fun _ ->
              let a = [ AddItem("n1", "title-0"); Retitle("p1", "t") ]
              let b = [ AddItem("n1", "title-1"); AddDep("p2", "p1") ]

              let production =
                  FoldConfluence.canonicalConflictReport encPlanOp (Dag.conflicts planFootprint a b)

              let model =
                  FoldConfluence.canonicalConflictReport
                      encPlanOp
                      (DagFold.conflicts (planFootprint >> toModelFootprint) a b
                       |> List.map ofModelConflict)

              Expect.isFalse (production = "") "the two deltas genuinely conflict"
              Expect.equal model production "the model enumerates the same interferences"

          // ---- the theorem's hypothesis, measured on the reference witness (Phase 132) ----

          testCase "the reference witness keeps the theorem's hypothesis: independent ops form a diamond"
          <| fun _ ->
              let t = treeDiamondSample treeFootprint 1320 250
              Differential.expectAgrees "the reference witness BREAKS independence_diamond" t
              // Sample adequacy, the pack's posture: a run that never met the premise
              // measures nothing at all, and a run that met it a handful of times measures
              // almost nothing. The threshold is far below what the sample delivers and is
              // there to catch a generator that stops producing independent pairs.
              let met = Tally.count "Met" t
              Expect.isGreaterThan met 300 (sprintf "the sample met the diamond's premise in earnest (met=%d)" met)

          testCase "a blind footprint BREAKS the diamond over the same sample — the measurement can fail"
          <| fun _ ->
              // The teeth. A footprint that erases every address makes `independent` declare
              // EVERY pair independent, so genuinely-dependent pairs (an insert racing a reorder
              // of the same parent, an insert under a node another op removes) are measured and
              // must break. If this comes back clean, the green run above certifies nothing.
              let blind (_: SkeletonOp<RNode, string>) : Footprint =
                  { Reads = noAddr
                    StructureWrites = noAddr
                    ContentWrites = noAddr
                    UnknownParentWrites = noAddr
                    SlotReads = Set.empty
                    SlotWrites = Set.empty }

              let t = treeDiamondSample blind 1320 250

              Expect.isGreaterThan
                  (Tally.count "Met" t)
                  0
                  "the blind run met the premise, so it had something to measure"

              Differential.expectLoses
                  "a footprint declaring EVERY pair independent must break the diamond — otherwise this comparison cannot lose"
                  t

              Expect.stringContains
                  (List.head (Tally.disagreements t))
                  "independent"
                  "the break names the pair it found"

          testCase "the reference algebra cannot keep rejection identity — the clause the diamond drops"
          <| fun _ ->
              // Phase 131's hypothesis demanded that independent ops commute at EVERY state,
              // REJECTIONS INCLUDED. `Rejection.UnknownNode` carries `addressable` — the whole id
              // set of the tree it was raised against — so that clause is unavailable here, and
              // this is the witness that keeps the claims ladder's "not claimed" honest. `a`
              // inserts under a real parent; `b` inserts under an absent one and always rejects.
              let a = InsertChild("a", RNode.leaf "fresh-132" "para" "v")
              let b = InsertChild("ghost-132", RNode.leaf "fresh-132b" "para" "v")

              Expect.isTrue
                  (Ops.independent (treeFootprint a) (treeFootprint b))
                  "the two inserts are footprint-independent — different parents, different fresh ids"

              let ab = treeW.Apply a treeBase |> Result.bind (treeW.Apply b)
              let ba = treeW.Apply b treeBase |> Result.bind (treeW.Apply a)

              match ab, ba with
              | Error(UnknownNode(t1, ids1)), Error(UnknownNode(t2, ids2)) ->
                  Expect.equal t1 t2 "both orders reject on the same absent parent"

                  Expect.notEqual
                      ids1
                      ids2
                      "but NOT with the same envelope — `addressable` is the tree at the moment of rejection"

                  Expect.isTrue
                      (List.contains "fresh-132" ids1)
                      "`a; b` rejects against a tree that already carries a's insert"

                  Expect.isFalse (List.contains "fresh-132" ids2) "`b; a` rejects against the tree before it"
              | _ -> failtestf "expected both orders to reject with UnknownNode; got %A / %A" ab ba

              // The DIAMOND is silent about this pair, and that is the whole of the change: `b`
              // never applies, so the premise is not met and nothing is claimed.
              match treeW.Apply b treeBase with
              | Error _ -> ()
              | Ok _ -> failtest "the witness requires `b` to reject at the base tree"

          // ---- Phase 134: production's DELTA RECOVERY beside the model's ----

          testCase "production's betweenOps recovers the model's delta over the reference witness"
          <| fun _ ->
              deltaDifferential "reference witness" unperturbed treeW treeLaneGen 3 1340 120
              |> expectDeltaAgreement "reference witness, 3 lanes"

          testCase "production's betweenOps recovers the model's delta over the reference witness at 4 lanes"
          <| fun _ ->
              deltaDifferential "reference witness" unperturbed treeW treeLaneGen 4 1341 60
              |> expectDeltaAgreement "reference witness, 4 lanes"

          testCase "production's betweenOps recovers the model's delta over the work-plan domain"
          <| fun _ ->
              deltaDifferential "work-plan domain" unperturbed planW planLaneGen 3 1342 150
              |> expectDeltaAgreement "work-plan domain, 3 lanes"

          testCase "production's betweenOps recovers the model's delta over the work-plan domain at 5 lanes"
          <| fun _ ->
              deltaDifferential "work-plan domain" unperturbed planW planLaneGen 5 1343 40
              |> expectDeltaAgreement "work-plan domain, 5 lanes"

          testCase "production's betweenOps recovers the model's delta over the corpus ops pool"
          <| fun _ ->
              match SiblingCorpus.resolve "ops" with
              | SiblingCorpus.NotAsked why -> skiptest why
              | SiblingCorpus.Absent why -> failtest why
              | SiblingCorpus.Found root ->
                  let ops =
                      match loadCorpusOps root with
                      | Ok ops -> ops
                      | Error why -> failtestf "a corpus op could not be projected to a footprint: %s" why

                  Expect.isGreaterThan (List.length ops) 8 "the ops/ family carries a real pool"

                  let baseOp =
                      { Name = "base"
                        Fp = (List.head ops).Fp }

                  let check (lanes: CorpusOp list list) (t: Tally) =
                      let t =
                          t |> Tally.bumpIf (lanes |> List.exists (fun l -> List.length l > 1)) "Chains"

                      match deltaDisagreements unperturbed corpusW baseOp lanes with
                      | [], got -> Tally.add "Recovered" got t
                      | d :: _, _ ->
                          Tally.disagree (sprintf "corpus lanes\n%s\n%s" (renderLanes corpusW.Encode lanes) d) t

                  // Multi-op lanes are where the WALK is exercised: a one-node chain is recovered
                  // by a single step and would certify almost nothing about the closure.
                  let t = deltaTally |> Differential.over (corpusLaneSets ops) check
                  Differential.expectAgrees "corpus ops" t
                  Expect.isGreaterThan (Tally.count "Recovered" t) 0 "ops were recovered"
                  Expect.isGreaterThan (Tally.count "Chains" t) 0 "multi-op lanes were walked"

          testCase "the oracle's DAG-shaped fold agrees with production over the work-plan domain"
          <| fun _ ->
              // `fold_once_dag` is the Phase 134 entry point: the model doing the WHOLE thing from
              // the content-addressed DAG — recovering each lane's delta, sweeping the pairs,
              // composing and replaying — where `fold_once` is handed the deltas. Compared against
              // production end to end, and against the deltas-first oracle beside it, so a
              // disagreement says which of the two halves moved.
              let step (i: int) (rng: ConfRng.T) (t: Tally) =
                  let lanes, r' = planLaneGen.Lanes 3 rng

                  let p =
                      productionFold planW planFootprint planHash planLaneGen.State0 planLaneGen.BaseOp lanes

                  let o =
                      oracleFoldFromDag planW planFootprint planHash planLaneGen.State0 planLaneGen.BaseOp lanes

                  let deltasFirst = oracleFold planW planFootprint planHash planLaneGen.State0 lanes

                  r',
                  t
                  |> Differential.check (p = o) (fun () ->
                      sprintf
                          "iter %d: the DAG-shaped oracle and production differ\n%s\n  production: %A\n  oracle:     %A"
                          i
                          (renderLanes encPlanOp lanes)
                          p
                          o)
                  |> Differential.check (o = deltasFirst) (fun () ->
                      sprintf
                          "iter %d: fold_once_dag and fold_once differ on the same lanes\n%s\n  from the DAG: %A\n  from deltas:  %A"
                          i
                          (renderLanes encPlanOp lanes)
                          o
                          deltasFirst)
                  |> countOutcome p

              let t = Differential.seeded 120 (ConfRng.ofSeed 1344) step foldTally
              Differential.expectAgrees "the DAG-shaped fold" t
              Expect.isGreaterThan (Tally.count "Folded" t) 0 "some lane sets folded"
              Expect.isGreaterThan (Tally.count "Halted" t) 0 "some lane sets halted"

          testCase "a model DAG whose lanes hang off the WRONG parent disagrees with production"
          <| fun _ ->
              // The teeth. Re-parenting each lane's first node onto the previous lane's head makes
              // the model walk a longer spine than the DAG has, so it recovers a longer delta. If
              // this comes back clean, the green cases above certify nothing: they would be
              // comparing a recovery that cannot notice the shape it walks.
              let t =
                  deltaDifferential "re-parented model" reparented planW planLaneGen 3 1342 150

              Differential.expectLoses
                  "a DAG whose lanes hang off the wrong parent must recover different deltas — this comparison cannot lose"
                  t

              Expect.stringContains
                  (List.head (Tally.disagreements t))
                  "the recovered delta differs"
                  "the disagreement names what moved" ]
