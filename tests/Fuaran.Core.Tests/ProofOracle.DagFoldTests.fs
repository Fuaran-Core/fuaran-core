module Fuaran.Core.Tests.ProofOracle.DagFoldTests

// Phase 131 — the F* oracle as a corpus host.
//
// `proofs/DagFold.fst` is an F* model of the N-lane DAG fold with fold confluence proved as a
// theorem; `proofs/oracle/DagFold.fs` is that model extracted to F# by F*'s own code generator.
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

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges

/// Run the differential over `iterations` generated lane sets; the first disagreement is shrunk
/// and reported with its seed, iteration and the lanes in the domain's own encoding.
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
    : Verdict =
    let mutable rng = ConfRng.ofSeed seed
    let mutable folded = 0
    let mutable halted = 0
    let mutable failure: string option = None

    let diverges (ls: 'Op list list) =
        not (List.isEmpty (disagreements true w fp oracleFp hashState gen.State0 gen.BaseOp ls))

    for i in 0 .. iterations - 1 do
        if failure.IsNone then
            let lanes, r' = gen.Lanes laneCount rng
            rng <- r'

            match disagreements true w fp oracleFp hashState gen.State0 gen.BaseOp lanes with
            | [] ->
                match productionFold w fp hashState gen.State0 gen.BaseOp lanes with
                | LaneFolded _ -> folded <- folded + 1
                | LaneHalted _ -> halted <- halted + 1
                | LaneRejected _ -> ()
            | _ ->
                let small = FoldConfluence.shrinkLanes diverges lanes

                failure <-
                    Some(
                        sprintf
                            "%s: seed=%d iter=%d — the oracle and production DISAGREE; shrunk to\n%s\n%s"
                            label
                            seed
                            i
                            (renderLanes w.Encode small)
                            (disagreements true w fp oracleFp hashState gen.State0 gen.BaseOp small
                             |> List.head)
                    )

    { Failure = failure
      Folded = folded
      Halted = halted }

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
    : string list * int =
    let apply = modelApply w
    let mutable met = 0
    let mutable breaks = []

    for a in ops do
        for b in ops do
            if DagFold.independent (toModelFootprint (oracleFp a)) (toModelFootprint (oracleFp b)) then
                for s in states do
                    match apply a s, apply b s with
                    | DagFold.Ok sa, DagFold.Ok sb ->
                        // The premise is met here, and only here: both ops apply at `s`.
                        met <- met + 1

                        let broke (why: string) =
                            breaks <-
                                breaks
                                @ [ sprintf
                                        "independent %s / %s both apply at a state, but %s"
                                        (w.Encode a)
                                        (w.Encode b)
                                        why ]

                        match apply b sa, apply a sb with
                        | DagFold.Ok t1, DagFold.Ok t2 when t1 = t2 -> ()
                        | DagFold.Ok _, DagFold.Ok _ -> broke "the two orders reach different states"
                        | DagFold.Error _, _ -> broke "the second does not apply after the first"
                        | _, DagFold.Error _ -> broke "the first does not apply after the second"
                    | _ -> ()

    breaks, met

/// The op and state pools Phase 80's construction yields for the reference witness: applyable
/// skeleton-op scripts threaded from the base tree (`treeLaneGen` — the public form of that
/// construction, shared with the differential above), pooled into ops, and every state a prefix
/// of that pool reaches. The states matter: the hypothesis quantifies over ALL of them, and a
/// check at the base tree alone would measure one instance of a `forall s`.
let private treeDiamondSample
    (oracleFp: SkeletonOp<RNode, string> -> Footprint)
    (seed: int)
    (trials: int)
    : string list * int =
    let mutable r = ConfRng.ofSeed seed
    let mutable breaks = []
    let mutable met = 0

    for _ in 1..trials do
        let lanes, r' = treeLaneGen.Lanes 3 r
        r <- r'
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

        let b, m = diamondBreaks treeW oracleFp ops states
        breaks <- breaks @ b
        met <- met + m

    breaks, met

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
let private toModelDag (dag: Dag.T<'Op>) : DagFold.dag<'Op> =
    { DagFold.nodes =
        dag.Nodes
        |> Map.toList
        |> List.map (fun (_, n) ->
            { DagFold.nid = n.Id
              DagFold.nparents = n.Parents
              DagFold.nop = n.Op }) }

/// The faithful bridge — what every real run uses.
let private unperturbed (_: string) (_: string list) (dag: Dag.T<'Op>) : DagFold.dag<'Op> = toModelDag dag

/// The go-red instrument, and the fold family's blind-footprint counterpart for this layer: every
/// lane's FIRST node is re-parented from the base onto the PREVIOUS lane's head, so the model
/// walks a spine the DAG does not have. The ids are untouched, so every lookup still succeeds and
/// the only thing that has moved is the shape the recovery walks — which is the thing under test.
let private reparented (baseId: string) (heads: string list) (dag: Dag.T<'Op>) : DagFold.dag<'Op> =
    let previousHead (actor: Actor) =
        match actor with
        | Human name when name.StartsWith "lane-" ->
            match System.Int32.TryParse(name.Substring 5) with
            | true, i when i > 0 && i - 1 < List.length heads -> Some(List.item (i - 1) heads)
            | _ -> None
        | _ -> None

    { DagFold.nodes =
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

            { DagFold.nid = n.Id
              DagFold.nparents = parents
              DagFold.nop = n.Op }) }

/// Every way production's per-lane delta recovery and the model's disagree on ONE lane set, plus
/// how many ops were actually recovered — a run that recovered nothing has compared two empty
/// lists and measured nothing at all.
let private deltaDisagreements
    (bridge: string -> string list -> Dag.T<'Op> -> DagFold.dag<'Op>)
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
            let o = DagFold.between_ops model fuel baseId head

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

    DagFold.fold_once_dag (modelApply w) (fp >> toModelFootprint) model model.nodes baseId state0 heads
    |> renderModelOutcome w hashState state0

type private DeltaTally =
    {
        Failure: string option
        /// Ops recovered across every lane of every trial — the vacuity guard.
        Recovered: int
        /// Lane sets carrying a lane of two or more ops: a one-node chain exercises no walk.
        Chains: int
    }

/// Run the delta differential over `iterations` generated lane sets from one pool.
let private deltaDifferential
    (label: string)
    (bridge: string -> string list -> Dag.T<'Op> -> DagFold.dag<'Op>)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (gen: LaneGen<'Op, 'State>)
    (laneCount: int)
    (seed: int)
    (iterations: int)
    : DeltaTally =
    let mutable rng = ConfRng.ofSeed seed
    let mutable failure: string option = None
    let mutable recovered = 0
    let mutable chains = 0

    for i in 0 .. iterations - 1 do
        let lanes, r' = gen.Lanes laneCount rng
        rng <- r'

        if lanes |> List.exists (fun l -> List.length l > 1) then
            chains <- chains + 1

        let diffs, got = deltaDisagreements bridge w gen.BaseOp lanes
        recovered <- recovered + got

        match diffs, failure with
        | d :: _, None ->
            failure <- Some(sprintf "%s: seed=%d iter=%d\n%s\n%s" label seed i (renderLanes w.Encode lanes) d)
        | _ -> ()

    { Failure = failure
      Recovered = recovered
      Chains = chains }

let private expectDeltaAgreement (label: string) (t: DeltaTally) =
    match t.Failure with
    | Some why -> failtest why
    | None ->
        Expect.isGreaterThan t.Recovered 0 (sprintf "%s: no ops were recovered at all (recovered=%d)" label t.Recovered)

        Expect.isGreaterThan
            t.Chains
            0
            (sprintf "%s: no trial carried a lane longer than one op, so no chain was walked" label)

// ---------------------------------------------------------------------------
//  Phase 156 — the ID-ORDERED DRAIN over an abstract node set, beside production's own.
//
//  Section 13 of `proofs/DagFold.fst` proves the drain deterministic, a linear extension of the
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

/// The id order production's `List.sort` imposes — the model's `lt` parameter, instantiated.
let private ordLt (a: string) (b: string) = System.String.CompareOrdinal(a, b) < 0

/// The go-red instrument: the same drain at the REVERSED order, which is `pick_min` of a flipped
/// total order and so the frontier's MAXIMUM. It satisfies everything the model asks of a
/// selector — it is still a member of the frontier it is handed — so what it breaks is agreement
/// with production and nothing else, which is exactly the claim these cases make. On a spine it
/// cannot lose, because a one-element frontier has one minimum and one maximum; the multi-head
/// union is what gives it room.
let private ordGt (a: string) (b: string) = System.String.CompareOrdinal(a, b) > 0

/// The model's `covers` bound: one walk step per node, plus the one its base case asks for.
let private drainFuel (ns: 'a list) : 'a list =
    match ns with
    | [] -> []
    | n :: _ -> n :: ns

/// The model nodes of a production DAG restricted to one head's ancestor closure — "the union" the
/// drain is over. Nothing is recomputed across the bridge: the ids are the content hashes
/// production minted, so a disagreement can only be about the ORDER.
let private modelClosure (dag: Dag.T<'Op>) (head: string) : DagFold.node<'Op> list =
    let anc = Dag.ancestorsOf dag head

    dag.Nodes
    |> Map.toList
    |> List.filter (fun (id, _) -> Set.contains id anc)
    |> List.map (fun (_, n) ->
        { DagFold.nid = n.Id
          DagFold.nparents = n.Parents
          DagFold.nop = n.Op })

/// One head whose closure is EVERY node: the lane heads folded together with `Dag.merge`. Returns
/// the union head and the DAG carrying the merge nodes.
let private mergedUnion
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
                    Dag.merge OpStream.defaultHash w (Human "merge") mergeOp acc h d
                    |> Reference.built)
            (h0, dag)

let private drainedOrder (r: DagFold.drain_result) : string list option =
    match r with
    | DagFold.Drained ord -> Some ord
    | DagFold.Refused _ -> None

/// Every way the extracted drain and production's own topological order disagree over one head's
/// closure, plus how wide the ready frontier ever got — a run whose frontier never passed one
/// compared two spines and measured nothing the spine theorem did not already cover.
let private drainDisagreements (lt: string -> string -> bool) (dag: Dag.T<'Op>) (head: string) : string list * int =
    let ns = modelClosure dag head
    let model = drainedOrder (DagFold.drain DagFold.IgnoreDangling lt (drainFuel ns) ns)

    // The widest ready frontier the closure ever presents: the nodes whose in-closure parents are
    // all drained, at the step that has the most of them. Computed from the model's own `frontier`
    // over its own emitted prefix, so it measures the drain that ran rather than a re-derivation.
    let widest =
        match model with
        | None -> 0
        | Some ord ->
            let closure = ns |> List.map (fun (n: DagFold.node<'Op>) -> n.nid)

            let rec go (rest: DagFold.node<'Op> list) (emitted: string list) (pending: string list) best =
                match pending with
                | [] -> best
                | id :: tl ->
                    let f = DagFold.frontier rest closure emitted
                    let best' = max best (List.length f)
                    go (DagFold.remove_id rest id) (emitted @ [ id ]) tl best'

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
    | Error e, _ -> [ sprintf "head %s: production reports a cyclic closure it cannot have: %s" head e ], widest

// ---------------------------------------------------------------------------
//  Phase 309 — `Dag.tryReplayTo` beside the extracted `replay_to` (DagFold.fst section 16).
//
//  The model replays a head as production does: refuse a head the DAG does not hold, drain the
//  head's closure with section 13's id-ordered drain, refuse a drain that did not place the whole
//  closure, then apply each drained node's op in order. Both sides are rendered to one string —
//  the state through the domain's own hash, a refusal by its case and the id it names — so a
//  disagreement says which clause moved.
// ---------------------------------------------------------------------------

let private renderProductionReplay (hashState: 'State -> string) (r: Result<'State, Dag.ReplayFault<'Rej>>) : string =
    match r with
    | Ok s -> "ok " + hashState s
    | Error(Dag.ReplayFault.UnknownHead h) -> "unknown-head " + h
    | Error(Dag.ReplayFault.CyclicHistory h) -> "cyclic " + h
    | Error(Dag.ReplayFault.Rejected(id, e)) -> sprintf "rejected %s %A" id e

let private renderModelReplay
    (hashState: 'State -> string)
    (r: DagFold.outcome<'State, DagFold.replay_fault<'Rej>>)
    : string =
    match r with
    | DagFold.Ok s -> "ok " + hashState s
    | DagFold.Error(DagFold.RUnknownHead h) -> "unknown-head " + h
    | DagFold.Error(DagFold.RCyclicHistory h) -> "cyclic " + h
    | DagFold.Error(DagFold.RRejected(id, e)) -> sprintf "rejected %s %A" id e

/// The extracted `replay_to` over a model DAG, at production's id order, with fuel for every node.
let private modelReplay
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (state0: 'State)
    (d: DagFold.dag<'Op>)
    (head: string)
    : DagFold.outcome<'State, DagFold.replay_fault<'Rej>> =
    let fuel = drainFuel d.nodes
    DagFold.replay_to (modelApply w) ordLt fuel d fuel state0 head

/// One head, both sides: `None` when they agree, the disagreement otherwise; and which arm it was.
let private replayDisagreement
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (hashState: 'State -> string)
    (state0: 'State)
    (bridge: Dag.T<'Op> -> DagFold.dag<'Op>)
    (dag: Dag.T<'Op>)
    (head: string)
    : string option * string =
    let p = renderProductionReplay hashState (Dag.tryReplayTo w state0 dag head)
    let m = renderModelReplay hashState (modelReplay w state0 (bridge dag) head)
    let arm = p.Split(' ')[0]

    if p = m then
        None, arm
    else
        Some(sprintf "head %s: the replay differs\n  production: %s\n  model:      %s" head p m), arm

/// The go-red bridge: the model is handed the DAG with ONE node of the head's closure dropped — the
/// node the store would have lost to a silent `Map.add` replacement before Phase 296, which is the
/// failure the `node-ids-distinct` row hid. Everything else is production's.
let private droppingOneNode (head: string) (dag: Dag.T<'Op>) : DagFold.dag<'Op> =
    let anc = Dag.ancestorsOf dag head

    let victim = anc |> Set.toList |> List.filter (fun id -> id <> head) |> List.tryHead

    let m = toModelDag dag

    match victim with
    | None -> m
    | Some v -> { DagFold.nodes = m.nodes |> List.filter (fun n -> n.nid <> v) }

// ---------------------------------------------------------------------------
//  Phase 158 — delta recovery over a MERGED HEAD, and `Dag.mergeBase`, beside production's own.
//
//  Section 14 of `proofs/DagFold.fst` proves that over a head whose lane hangs off an already
//  MERGED node, `Dag.between` returns that lane's own nodes in append order (`between_merged`),
//  that the fold above it is the deltas-first fold (`reconcile_many_merged_eq`), and that
//  `Dag.mergeBase` of two such heads is the merged node they diverged from
//  (`merge_base_is_divergence`). What runs here is the other half: production's OWN
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
                    |> List.fold (fun (h, dd) op -> Dag.append hashFn w actor op h dd |> Reference.built) (merged, d)

                hs @ [ head ], d')
            ([], dagM)

    baseId, merged, heads1, heads2, dag2

let private foundId (r: DagFold.found<string>) : string option =
    match r with
    | DagFold.Found id -> Some id
    | DagFold.Missing -> None

/// The go-red instrument for `mergeBase`: the model's own function at the REVERSED key — the same
/// intersection, the same extracted `max_by`, with `deeper` flipped — so it returns the SHALLOWEST
/// common ancestor, which over a fold-pull-fold union is the ORIGINAL BASE rather than the
/// divergence point. It is as well-formed a `maxBy` as the model's own; what it breaks is agreement
/// with production, and the delta recovered from the base it names.
let private shallowestCommon (model: DagFold.dag<'Op>) (fuel: DagFold.node<'Op> list) (left: string) (right: string) =
    match DagFold.inter (DagFold.ancestors_of model fuel left) (DagFold.ancestors_of model fuel right) with
    | [] -> None
    | c :: t -> Some(DagFold.max_by (fun x y -> DagFold.deeper ordLt model fuel y x) c t)

type private MergedTally =
    {
        Failures: string list
        /// Ops recovered across every round-two lane — the vacuity guard.
        Recovered: int
        /// Round-two lanes of two or more ops: a one-node lane walks no chain.
        Chains: int
        /// Two-parent nodes beneath the merged heads: with none, the "merged head" was a plain node
        /// and the case re-measured Phase 134's shape under a different name.
        MergeNodes: int
        /// Head pairs whose merge base was located.
        Pairs: int
    }

/// One fold-pull-fold union, compared three ways: each round-two head's recovered delta (production
/// `betweenOps` from the merged head, the model's `between_ops_drained`, and the lane that was
/// appended — the theorem's own right-hand side); every pair of round-two heads' merge base
/// (production, the model, and the merged head itself); and the model's computable premises, so a
/// green run is known to be one the theorems are ABOUT.
let private mergedDisagreements
    (locate: DagFold.dag<'Op> -> DagFold.node<'Op> list -> string -> string -> string option)
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (baseOp: 'Op)
    (round1: 'Op list list)
    (round2: 'Op list list)
    : MergedTally =
    let _, merged, _, heads2, dag = foldPullFold w baseOp round1 round2
    let model = toModelDag dag
    let fuel = model.nodes
    let kfuel = drainFuel model.nodes
    let failures = ResizeArray<string>()
    let mutable recovered = 0

    for (i, (head, lane)) in List.indexed (List.zip heads2 round2) do
        let p = Dag.betweenOps dag merged head
        let o = DagFold.between_ops_drained ordLt kfuel model fuel merged head
        recovered <- recovered + List.length p

        if p <> o then
            failures.Add(
                sprintf
                    "round-two lane %d: the recovered delta differs\n  production: [%s]\n  model:      [%s]"
                    i
                    (p |> List.map w.Encode |> String.concat "; ")
                    (o |> List.map w.Encode |> String.concat "; ")
            )

        if o <> lane then
            failures.Add(
                sprintf
                    "round-two lane %d: the model did not recover the lane that was appended (between_ops_merged)\n  lane:  [%s]\n  model: [%s]"
                    i
                    (lane |> List.map w.Encode |> String.concat "; ")
                    (o |> List.map w.Encode |> String.concat "; ")
            )

        // The FUEL premise of `merged_recovers`, evaluated: the walk below the lane completed.
        if not (DagFold.walk_ok model (DagFold.drop_by fuel (List.rev lane)) merged) then
            failures.Add(sprintf "round-two lane %d: the model's walk below the lane ran out of fuel — premise unmet" i)

    let mutable pairs = 0

    for (i, hi) in List.indexed heads2 do
        for (j, hj) in List.indexed heads2 do
            if i < j then
                pairs <- pairs + 1
                let p = Dag.mergeBase dag hi hj
                let o = locate model fuel hi hj

                if p <> o then
                    failures.Add(
                        sprintf "heads %d and %d: the merge base differs\n  production: %A\n  model:      %A" i j p o
                    )

                if p <> Some merged then
                    failures.Add(
                        sprintf "heads %d and %d: production's merge base %A is not the merged head %s" i j p merged
                    )

    let mergeNodes =
        let anc = Dag.ancestorsOf dag merged

        dag.Nodes
        |> Map.toList
        |> List.filter (fun (id, n) -> Set.contains id anc && List.length n.Parents > 1)
        |> List.length

    { Failures = List.ofSeq failures
      Recovered = recovered
      Chains = round2 |> List.filter (fun l -> List.length l > 1) |> List.length
      MergeNodes = mergeNodes
      Pairs = pairs }

/// The faithful locator — the extracted `merge_base` at production's id order.
let private modelMergeBase (model: DagFold.dag<'Op>) (fuel: DagFold.node<'Op> list) (l: string) (r: string) =
    foundId (DagFold.merge_base ordLt model fuel l r)


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
    let cs = heads |> List.distinct |> List.map (DagFold.ancestors_of model fuel)
    let baseC = DagFold.ancestors_of model fuel baseId
    let union = DagFold.closure_nodes model.nodes (DagFold.concat cs)
    let order = DagFold.drain_order ordLt (drainFuel union) union

    DagFold.reconcile_ids order baseC cs |> List.map (fun id -> dag.Nodes[id].Op)

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
        |> List.fold (fun (p, dd) op -> Dag.append h planW (Human actor) op p dd |> Reference.built) (parent, dag)

    let g, d0 =
        Dag.append h planW (Human "base") planLaneGen.BaseOp "" Dag.empty
        |> Reference.built

    let ha, d1 = chain "lane-a" a g d0
    let hb, d2 = chain "lane-b" b g d1
    let hff, d3 = chain "lane-b" b ha d1

    let m1, d4 =
        Dag.merge h planW (Human "merge-1") planLaneGen.BaseOp ha hb d2
        |> Reference.built

    let m2, d5 =
        Dag.merge h planW (Human "merge-2") planLaneGen.BaseOp ha hb d4
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
//  The theorems are proved in `proofs/DagFold.fst`; what runs here is the other half, production's
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
        |> List.fold (fun (p, dd) op -> Dag.append h w (Human actor) op p dd |> Reference.built) (parent, dag)

    let g, d0 = Dag.append h w (Human "base") baseOp "" Dag.empty |> Reference.built
    let ha, d1 = chain "lane-a" a g d0
    let hb, d2 = chain "lane-b" b g d1

    let m1, d3 = Dag.merge h w (Human "merge-1") baseOp ha hb d2 |> Reference.built
    let m2, d4 = Dag.merge h w (Human "merge-2") baseOp hb ha d3 |> Reference.built
    let h1, d5 = chain "lane-c" c m1 d4
    let h2, d6 = chain "lane-d" d m2 d5
    h1, h2, d6

/// The model's `merge_base` at production's id order, with fuel for every node.
let private modelMergeBaseOf (dag: Dag.T<'Op>) (left: string) (right: string) : string option =
    let model = toModelDag dag

    match DagFold.merge_base ordLt model model.nodes left right with
    | DagFold.Found id -> Some id
    | DagFold.Missing -> None

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
    let cs = heads |> List.distinct |> List.map (DagFold.ancestors_of model fuel)
    let baseC = DagFold.ancestors_of model fuel baseId
    let union = DagFold.closure_nodes model.nodes (DagFold.concat cs)
    let order = DagFold.drain_order ordLt (drainFuel union) union

    DagFold.conflicts_of_heads (fp >> toModelFootprint) model order baseC cs
    |> List.map (fun c -> renderNodeConflict (c.left.nid, c.right.nid, c.address, ofModelShape c.shape))
    |> List.distinct
    |> List.sort

/// `Dag.CheckpointFault` and the model's `ck_fault`, rendered alike.
let private renderProductionFrom (hashState: 'State -> string) (r: Result<'State, Dag.CheckpointFault<'Rej>>) =
    match r with
    | Ok s -> "ok " + hashState s
    | Error(Dag.CheckpointFault.UnknownNode n) -> "unknown-node " + n
    | Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.UnknownHead h)) -> "unknown-head " + h
    | Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.CyclicHistory h)) -> "cyclic " + h
    | Error(Dag.CheckpointFault.Replay(Dag.ReplayFault.Rejected(id, e))) -> sprintf "rejected %s %A" id e
    | Error(Dag.CheckpointFault.Unreached h) -> "unreached " + h
    | Error(Dag.CheckpointFault.Uncovered n) -> "uncovered " + n

let private renderModelFrom (hashState: 'State -> string) (r: DagFold.outcome<'State, DagFold.ck_fault<'Rej>>) =
    match r with
    | DagFold.Ok s -> "ok " + hashState s
    | DagFold.Error(DagFold.CUnknownNode n) -> "unknown-node " + n
    | DagFold.Error(DagFold.CReplay(DagFold.RUnknownHead h)) -> "unknown-head " + h
    | DagFold.Error(DagFold.CReplay(DagFold.RCyclicHistory h)) -> "cyclic " + h
    | DagFold.Error(DagFold.CReplay(DagFold.RRejected(id, e))) -> sprintf "rejected %s %A" id e
    | DagFold.Error(DagFold.CUnreached h) -> "unreached " + h
    | DagFold.Error(DagFold.CUncovered n) -> "uncovered " + n

let private toModelCheckpoint (cp: Dag.Checkpoint<'State>) : DagFold.checkpoint<'State> =
    { DagFold.cnode = cp.Node
      DagFold.cstate = cp.State
      DagFold.cseal = cp.Hash }

/// The extracted `replay_from` over a model DAG, at production's id order, with fuel for every node.
let private modelReplayFrom
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (d: DagFold.dag<'Op>)
    (cp: DagFold.checkpoint<'State>)
    (head: string)
    =
    let fuel = drainFuel d.nodes
    DagFold.replay_from (modelApply w) ordLt fuel d fuel cp head

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
            Dag.merge OpStream.defaultHash w (Human "side") baseOp r2 r1 dag
            |> Reference.built

        baseId, merged, heads1, side :: rest, dag'
    | _ -> baseId, merged, heads1, heads2, dag

type private CheckpointTally =
    { mutable Resumed: int
      mutable Uncovered: int
      mutable Unreached: int
      mutable Compacted: int
      mutable Banded: int
      mutable CutRefused: int }

/// Every node as a checkpoint, every head as a target: production's `checkpointAt` / `replayFrom` /
/// `compactAt` beside the extracted `checkpoint_at` / `replay_from` / `truncate` + `truncation_ok`.
let private checkpointDisagreements
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (hashState: 'State -> string)
    (state0: 'State)
    (tally: CheckpointTally)
    (dag: Dag.T<'Op>)
    (heads: string list)
    : string list =
    let h = OpStream.defaultHash
    let model = toModelDag dag
    let fuel = drainFuel model.nodes
    let seal (n: string) (st: 'State) = (Dag.sealAt h hashState w n st).Hash
    let targets = (dag.Nodes |> Map.toList |> List.map fst) @ heads @ [ "no-such-node" ]

    [ for c in (dag.Nodes |> Map.toList |> List.map fst) @ [ "no-such-node" ] do
          let p = Dag.checkpointAt h hashState w state0 dag c

          let m = DagFold.checkpoint_at (modelApply w) ordLt fuel model fuel seal state0 c

          match p, m with
          | Ok pc, DagFold.Ok mc ->
              if
                  pc.Node <> mc.cnode
                  || hashState pc.State <> hashState mc.cstate
                  || pc.Hash <> mc.cseal
              then
                  yield
                      sprintf "checkpointAt %s: production (%s, %s) model (%s, %s)" c pc.Node pc.Hash mc.cnode mc.cseal

              for t in targets |> List.distinct do
                  let pr = renderProductionFrom hashState (Dag.replayFrom w pc dag t)

                  let mr =
                      renderModelFrom hashState (modelReplayFrom w model (toModelCheckpoint pc) t)

                  if pr <> mr then
                      yield sprintf "replayFrom %s -> %s\n  production: %s\n  model:      %s" c t pr mr
                  elif pr.StartsWith "ok " then
                      tally.Resumed <- tally.Resumed + 1
                  elif pr.StartsWith "uncovered " then
                      tally.Uncovered <- tally.Uncovered + 1
                  elif pr.StartsWith "unreached " then
                      tally.Unreached <- tally.Unreached + 1

              match Dag.compactAt h hashState w state0 dag c with
              | Ok(cp, compacted) ->
                  let keep = compacted.Nodes |> Map.toList |> List.map fst

                  if not (DagFold.truncation_ok model fuel c keep) then
                      yield sprintf "compactAt %s: the kept set does not meet the model's truncation contract" c

                  let truncated = DagFold.truncate model keep

                  if
                      (truncated.nodes |> List.map (fun n -> n.nid) |> List.sort)
                      <> (keep |> List.sort)
                  then
                      yield sprintf "compactAt %s: the model's truncation is not production's DAG" c

                  tally.Compacted <- tally.Compacted + 1

                  let behind = Dag.ancestorsOf dag c

                  if keep |> List.exists (fun id -> id <> c && Set.contains id behind) then
                      tally.Banded <- tally.Banded + 1

                  for t in keep do
                      let full = renderProductionFrom hashState (Dag.replayFrom w cp dag t)
                      let cut = renderProductionFrom hashState (Dag.replayFrom w cp compacted t)

                      let mcut =
                          renderModelFrom hashState (modelReplayFrom w truncated (toModelCheckpoint cp) t)

                      if cut <> mcut then
                          yield sprintf "compacted at %s, head %s\n  production: %s\n  model:      %s" c t cut mcut

                      if full.StartsWith "ok " || full.StartsWith "rejected " then
                          if cut <> full then
                              yield
                                  sprintf
                                      "compacted at %s, head %s: the truncation changed the replay\n  full: %s\n  cut:  %s"
                                      c
                                      t
                                      full
                                      cut
              | Error(Dag.CheckpointFault.Uncovered _) -> tally.CutRefused <- tally.CutRefused + 1
              | Error e -> yield sprintf "compactAt %s refused a checkpoint checkpointAt took: %A" c e
          | Error pe, DagFold.Error me ->
              let pr = renderProductionFrom hashState (Error pe)
              let mr = renderModelFrom hashState (DagFold.Error me)

              if pr <> mr then
                  yield sprintf "checkpointAt %s refused differently\n  production: %s\n  model:      %s" c pr mr
          | Ok _, DagFold.Error me ->
              yield
                  sprintf
                      "checkpointAt %s: production took it, the model refused %s"
                      c
                      (renderModelFrom hashState (DagFold.Error me))
          | Error pe, DagFold.Ok _ -> yield sprintf "checkpointAt %s: the model took it, production refused %A" c pe ]

/// Every ordered pair of ids (and a missing one): production's index and walk beside the extracted
/// index built over the model's drain.
let private reachDisagreements (dag: Dag.T<'Op>) : string list * int =
    let model = toModelDag dag
    let fuel = drainFuel model.nodes
    let idx = DagFold.reach_of_dag ordLt fuel model
    let reach = Dag.Reach.ofDag dag
    let ids = (dag.Nodes |> Map.toList |> List.map fst) @ [ "no-such-node" ]
    let mutable positives = 0

    let failures =
        [ for d in ids do
              let walk = Dag.ancestorsOf dag d
              let mAnc = DagFold.reach_ancestors model fuel idx d |> Set.ofList

              if mAnc <> walk || Dag.Reach.ancestors reach d <> walk then
                  yield sprintf "ancestors %s: walk %A, model %A" d walk mAnc

              for a in ids do
                  let truth = Set.contains a walk
                  let p = Dag.Reach.reaches reach a d
                  let m = DagFold.reach_reaches model fuel idx a d

                  if truth then
                      positives <- positives + 1

                  if p <> truth || m <> truth then
                      yield sprintf "reaches %s %s: walk %b, production %b, model %b" a d truth p m ]

    failures, positives

// ---------------------------------------------------------------------------
//  Phase 457 — the lane DAG's remaining boundaries (`proofs/DagPrimitives.fst`).
//
//  The model now computes `Dag.mergeBase` as Phase 388 does (`deepest`, maximal members only),
//  `Dag.commonBase` over N heads, the index's own `mergeBase` / `commonBase` / `between` /
//  `tryTopoOrder` over its slots and the whole DAG's drain, and `cut`'s kept set. Each runs here
//  beside production's over every pair, every node and several head sets of the DAGs a clone holds.
// ---------------------------------------------------------------------------

let private ofFound (f: DagFold.found<string>) : string option =
    match f with
    | DagFold.Found x -> Some x
    | DagFold.Missing -> None

let private renderTopo (r: Result<string list, string>) : string =
    match r with
    | Ok l -> "ok " + String.concat "," l
    | Error _ -> "cyclic"

let private renderModelTopo (r: DagPrimitives.topo_result) : string =
    match r with
    | DagPrimitives.TopoOk l -> "ok " + String.concat "," l
    | DagPrimitives.TopoCyclic _ -> "cyclic"

type private BoundaryTally =
    { mutable Based: int
      mutable Disjoint: int
      mutable Deltas: int
      mutable NHead: int
      mutable Kept: int
      mutable Banded: int
      mutable Refused: int }

let private newBoundaryTally () =
    { Based = 0
      Disjoint = 0
      Deltas = 0
      NHead = 0
      Kept = 0
      Banded = 0
      Refused = 0 }

/// Every ordered pair of ids (and an absent one), several head sets, and every node as a cut point:
/// production's DAG-level and index-level answers beside the extracted ones.
let private boundaryDisagreements
    (w: StreamWitness<'Op, 'State, 'Rej>)
    (hashState: 'State -> string)
    (state0: 'State)
    (tally: BoundaryTally)
    (dag: Dag.T<'Op>)
    (heads: string list)
    : string list =
    let model = toModelDag dag
    let fuel = drainFuel model.nodes
    let idx = DagFold.reach_of_dag ordLt fuel model
    let ord = DagFold.drain_order ordLt fuel model.nodes
    let reach = Dag.Reach.ofDag dag
    let held = dag.Nodes |> Map.toList |> List.map fst
    let ids = held @ [ "no-such-node" ]
    let nids (ns: DagFold.node<'Op> list) = ns |> List.map (fun n -> n.nid)

    [ for a in ids do
          let pt = renderTopo (Dag.tryTopoOrder dag a)
          let mt = renderModelTopo (DagPrimitives.try_topo_order ordLt fuel model fuel a)

          let rt =
              renderModelTopo (DagPrimitives.reach_try_topo_order ordLt fuel model fuel idx ord a)

          if mt <> pt || rt <> pt || renderTopo (Dag.Reach.tryTopoOrder reach a) <> pt then
              yield sprintf "tryTopoOrder %s: production %s, model %s, model index %s" a pt mt rt

          for b in ids do
              let pm = Dag.mergeBase dag a b
              let mm = ofFound (DagPrimitives.merge_base_now ordLt fuel model fuel a b)
              let rm = ofFound (DagPrimitives.reach_merge_base ordLt fuel model fuel idx a b)

              if mm <> pm || rm <> pm || Dag.Reach.mergeBase reach a b <> pm then
                  yield sprintf "mergeBase %s %s: production %A, model %A, model index %A" a b pm mm rm

              if pm.IsSome then
                  tally.Based <- tally.Based + 1
              else
                  tally.Disjoint <- tally.Disjoint + 1

              let pb = Dag.between dag a b |> List.map _.Id
              let mb = DagFold.between_drained ordLt fuel model fuel a b |> nids
              let rb = DagPrimitives.reach_between ordLt fuel model fuel idx ord a b |> nids

              if mb <> pb || rb <> pb || (Dag.Reach.between reach a b |> List.map _.Id) <> pb then
                  yield sprintf "between %s %s: production %A, model %A, model index %A" a b pb mb rb

              if not (List.isEmpty pb) then
                  tally.Deltas <- tally.Deltas + 1

      for hs in [ heads; held; List.rev heads @ heads; heads @ [ "no-such-node" ]; [] ] do
          let pc = Dag.commonBase dag hs
          let mc = ofFound (DagPrimitives.common_base ordLt fuel model fuel hs)
          let rc = ofFound (DagPrimitives.reach_common_base ordLt fuel model fuel idx hs)

          if mc <> pc || rc <> pc || Dag.Reach.commonBase reach hs <> pc then
              yield sprintf "commonBase %A: production %A, model %A, model index %A" hs pc mc rc

          if List.length (List.distinct hs) >= 3 && pc.IsSome then
              tally.NHead <- tally.NHead + 1

      for c in held do
          let m = DagPrimitives.cut_keep ordLt model fuel c

          match Dag.compactAt OpStream.defaultHash hashState w state0 dag c, m with
          | Ok(_, compacted), DagPrimitives.CutKeep k ->
              let kept = compacted.Nodes |> Map.toList |> List.map fst |> Set.ofList

              if Set.ofList k <> kept then
                  yield sprintf "cut %s: production keeps %A, the model %A" c kept (Set.ofList k)

              if not (DagFold.truncation_ok model fuel c k) then
                  yield sprintf "cut %s: the model's kept set misses its own contract" c

              tally.Kept <- tally.Kept + 1

              let behind = Dag.ancestorsOf dag c

              if kept |> Set.exists (fun id -> id <> c && Set.contains id behind) then
                  tally.Banded <- tally.Banded + 1
          | Error(Dag.CheckpointFault.Uncovered u), DagPrimitives.CutUncovered v ->
              if u <> v then
                  yield sprintf "cut %s: production refuses %s uncovered, the model %s" c u v

              tally.Refused <- tally.Refused + 1
          | Error(Dag.CheckpointFault.Uncovered _), _
          | Ok _, _ -> yield sprintf "cut %s: production and the model disagree on whether it is refused" c
          | Error _, _ -> () ] // checkpointAt refused: the domain rejects the history, so there is no cut

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

              let v =
                  differential "blind oracle" planW planFootprint blind planHash planLaneGen 3 4100 150

              match v.Failure with
              | None -> failtest "a blind oracle never halts, so it must disagree with a production fold that does"
              | Some why ->
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

                  let n = List.length ops
                  let mutable folded = 0
                  let mutable halted = 0

                  let check (lanes: CorpusOp list list) =
                      match disagreements false corpusW corpusFp corpusFp id "" baseOp lanes with
                      | [] ->
                          match productionFold corpusW corpusFp id "" baseOp lanes with
                          | LaneFolded _ -> folded <- folded + 1
                          | LaneHalted _ -> halted <- halted + 1
                          | LaneRejected _ -> ()
                      | d :: _ -> failtestf "corpus lanes\n%s\n%s" (renderLanes corpusW.Encode lanes) d

                  for i in 0 .. n - 1 do
                      for j in i + 1 .. n - 1 do
                          check [ [ List.item i ops ]; [ List.item j ops ] ]

                          for k in j + 1 .. n - 1 do
                              check [ [ List.item i ops ]; [ List.item j ops ]; [ List.item k ops ] ]

                  // Two-op lanes as well: a lane whose ops interfere with each other is legal (a
                  // lane is one writer's sequence) and must not be reported as a cross-lane halt.
                  for i in 0 .. n - 2 do
                      check [ [ List.item i ops; List.item (i + 1) ops ]; [ List.item ((i + 2) % n) ops ] ]

                  Expect.isGreaterThan folded 0 "some corpus lane sets fold"
                  Expect.isGreaterThan halted 0 "some corpus lane sets halt"

          // ---- the pieces the comparison rests on ----

          testCase "the model's independence agrees with Ops.independent over the reference footprints"
          <| fun _ ->
              // The model's `independent` is `Ops.independent` clause for clause; over every pair
              // of ops the reference generator produces, the two booleans agree.
              let mutable r = ConfRng.ofSeed 77
              let mutable pairs = 0

              for _ in 1..40 do
                  let lanes, r' = treeLaneGen.Lanes 2 r
                  r <- r'

                  for a in List.concat lanes do
                      for b in List.concat lanes do
                          pairs <- pairs + 1
                          let fa = treeFootprint a
                          let fb = treeFootprint b

                          Expect.equal
                              (DagFold.independent (toModelFootprint fa) (toModelFootprint fb))
                              (Ops.independent fa fb)
                              (sprintf "independence of %s / %s" (treeW.Encode a) (treeW.Encode b))

              Expect.isGreaterThan pairs 0 "pairs were compared"

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
              let breaks, met = treeDiamondSample treeFootprint 1320 250

              match breaks with
              | why :: _ -> failtestf "the reference witness BREAKS independence_diamond: %s" why
              | [] ->
                  // Sample adequacy, the pack's posture: a run that never met the premise
                  // measures nothing at all, and a run that met it a handful of times measures
                  // almost nothing. The threshold is far below what the sample delivers and is
                  // there to catch a generator that stops producing independent pairs.
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

              let breaks, met = treeDiamondSample blind 1320 250
              Expect.isGreaterThan met 0 "the blind run met the premise, so it had something to measure"

              match breaks with
              | [] ->
                  failtest
                      "a footprint declaring EVERY pair independent must break the diamond — otherwise this comparison cannot lose"
              | why :: _ -> Expect.stringContains why "independent" "the break names the pair it found"

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

                  let n = List.length ops
                  let mutable recovered = 0
                  let mutable chains = 0

                  let check (lanes: CorpusOp list list) =
                      if lanes |> List.exists (fun l -> List.length l > 1) then
                          chains <- chains + 1

                      match deltaDisagreements unperturbed corpusW baseOp lanes with
                      | [], got -> recovered <- recovered + got
                      | d :: _, _ -> failtestf "corpus lanes\n%s\n%s" (renderLanes corpusW.Encode lanes) d

                  for i in 0 .. n - 1 do
                      for j in i + 1 .. n - 1 do
                          check [ [ List.item i ops ]; [ List.item j ops ] ]

                          for k in j + 1 .. n - 1 do
                              check [ [ List.item i ops ]; [ List.item j ops ]; [ List.item k ops ] ]

                  // Multi-op lanes are where the WALK is exercised: a one-node chain is recovered
                  // by a single step and would certify almost nothing about the closure.
                  for i in 0 .. n - 2 do
                      check [ [ List.item i ops; List.item (i + 1) ops ]; [ List.item ((i + 2) % n) ops ] ]

                  Expect.isGreaterThan recovered 0 "ops were recovered"
                  Expect.isGreaterThan chains 0 "multi-op lanes were walked"

          testCase "the oracle's DAG-shaped fold agrees with production over the work-plan domain"
          <| fun _ ->
              // `fold_once_dag` is the Phase 134 entry point: the model doing the WHOLE thing from
              // the content-addressed DAG — recovering each lane's delta, sweeping the pairs,
              // composing and replaying — where `fold_once` is handed the deltas. Compared against
              // production end to end, and against the deltas-first oracle beside it, so a
              // disagreement says which of the two halves moved.
              let mutable rng = ConfRng.ofSeed 1344
              let mutable folded = 0
              let mutable halted = 0

              for i in 1..120 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'

                  let p =
                      productionFold planW planFootprint planHash planLaneGen.State0 planLaneGen.BaseOp lanes

                  let o =
                      oracleFoldFromDag planW planFootprint planHash planLaneGen.State0 planLaneGen.BaseOp lanes

                  let deltasFirst = oracleFold planW planFootprint planHash planLaneGen.State0 lanes

                  if p <> o then
                      failtestf
                          "iter %d: the DAG-shaped oracle and production differ\n%s\n  production: %A\n  oracle:     %A"
                          i
                          (renderLanes encPlanOp lanes)
                          p
                          o

                  if o <> deltasFirst then
                      failtestf
                          "iter %d: fold_once_dag and fold_once differ on the same lanes\n%s\n  from the DAG: %A\n  from deltas:  %A"
                          i
                          (renderLanes encPlanOp lanes)
                          o
                          deltasFirst

                  match p with
                  | LaneFolded _ -> folded <- folded + 1
                  | LaneHalted _ -> halted <- halted + 1
                  | LaneRejected _ -> ()

              Expect.isGreaterThan folded 0 "some lane sets folded"
              Expect.isGreaterThan halted 0 "some lane sets halted"

          testCase "a model DAG whose lanes hang off the WRONG parent disagrees with production"
          <| fun _ ->
              // The teeth. Re-parenting each lane's first node onto the previous lane's head makes
              // the model walk a longer spine than the DAG has, so it recovers a longer delta. If
              // this comes back clean, the green cases above certify nothing: they would be
              // comparing a recovery that cannot notice the shape it walks.
              let mutable rng = ConfRng.ofSeed 1342
              let mutable found = 0
              let mutable example = ""

              for _ in 1..150 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'

                  match deltaDisagreements reparented planW planLaneGen.BaseOp lanes with
                  | [], _ -> ()
                  | d :: _, _ ->
                      found <- found + 1

                      if example = "" then
                          example <- d

              Expect.isGreaterThan
                  found
                  0
                  "a DAG whose lanes hang off the wrong parent must recover different deltas — this comparison cannot lose"

              Expect.stringContains example "the recovered delta differs" "the disagreement names what moved"

          // ---- Phase 156: the ID-ORDERED DRAIN beside production's own topological order ----

          testCase "the extracted drain is production's topological order over every lane head"
          <| fun _ ->
              // The SPINE case: one head's closure is a chain, the frontier is one element wide at
              // every step, and Phase 142 already proves the two orders coincide there. It runs
              // because a green multi-head case means little if the easy case is broken.
              let mutable rng = ConfRng.ofSeed 1560
              let mutable heads = 0

              for i in 1..80 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes

                  for h in hs do
                      heads <- heads + 1

                      match drainDisagreements ordLt dag h with
                      | [], _ -> ()
                      | d :: _, _ -> failtestf "iter %d\n%s\n%s" i (renderLanes encPlanOp lanes) d

                  ignore baseId

              Expect.isGreaterThan heads 0 "some lane heads were drained"

          testCase "the extracted drain is production's topological order over a MULTI-HEAD UNION"
          <| fun _ ->
              // The case Phase 142 does not cover and section 13 exists for: the lane heads folded
              // into one convergent head with `Dag.merge`, so the union head's closure is every
              // node and the ready frontier is N wide once the base is drained. The adequacy guard
              // is the frontier WIDTH — a run whose frontier never passed one has re-measured the
              // spine case under a different name.
              let mutable rng = ConfRng.ofSeed 1561
              let mutable widest = 0
              let mutable unions = 0

              for i in 1..80 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag
                  unions <- unions + 1

                  match drainDisagreements ordLt merged unionHead with
                  | [], w -> widest <- max widest w
                  | d :: _, _ -> failtestf "iter %d\n%s\n%s" i (renderLanes encPlanOp lanes) d

              for i in 1..40 do
                  let lanes, r' = treeLaneGen.Lanes 4 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag treeW treeLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion treeW treeLaneGen.BaseOp baseId hs dag
                  unions <- unions + 1

                  match drainDisagreements ordLt merged unionHead with
                  | [], w -> widest <- max widest w
                  | d :: _, _ -> failtestf "reference witness iter %d\n%s\n%s" i (renderLanes treeW.Encode lanes) d

              Expect.isGreaterThan unions 0 "some unions were drained"

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
              let mutable rng = ConfRng.ofSeed 1561
              let mutable found = 0
              let mutable example = ""

              for _ in 1..80 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag

                  match drainDisagreements ordGt merged unionHead with
                  | [], _ -> ()
                  | d :: _, _ ->
                      found <- found + 1

                      if example = "" then
                          example <- d

              Expect.isGreaterThan
                  found
                  0
                  "a drain taking the LARGEST ready id must disagree with production — this comparison cannot lose"

              Expect.stringContains example "the drain differs" "the disagreement names what moved"

          testCase "the drain is a function of the node SET — every arrival order gives one sequence"
          <| fun _ ->
              // `drain_deterministic`, measured. `DagFold.frontier` answers in the WORK LIST's
              // order, so permuting the node set hands the selector a permuted frontier; only an
              // order-invariant selector survives that, and this is the arm that would catch a
              // drain that took the head of an unsorted frontier instead of its minimum.
              let mutable rng = ConfRng.ofSeed 1562
              let mutable compared = 0

              for i in 1..60 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag
                  let ns = modelClosure merged unionHead
                  let canonical = DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel ns) ns

                  // Three permutations, two of them fixed so the case cannot go quiet on a seed
                  // that happened to shuffle nothing, and one drawn.
                  let shuffled, r'' = ConfRng.shuffle ns rng
                  rng <- r''

                  for perm in
                      [ List.rev ns
                        (match ns with
                         | [] -> []
                         | h :: t -> t @ [ h ])
                        shuffled ] do
                      compared <- compared + 1
                      let got = DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel perm) perm

                      if got <> canonical then
                          failtestf
                              "iter %d: a permuted node set drained differently\n%s\n  canonical: %A\n  permuted:  %A"
                              i
                              (renderLanes encPlanOp lanes)
                              canonical
                              got

              Expect.isGreaterThan compared 0 "some permutations were drained"

          testCase "the two dangling-parent policies are the two production call sites, and differ only on a dangler"
          <| fun _ ->
              // `drain_policies_agree` and `drain_refusal_characterised`, measured against the two
              // callers they model. On a closure — which is parent-closed, so no parent lies
              // outside — the policies agree and `Dag.verifyDag` is happy. Drop the BASE node from
              // the set and every lane's first node names a parent outside it: `RefuseDangling`
              // refuses, exactly as `Dag.firstBreak` reports `MissingParent`, while
              // `IgnoreDangling` still drains every remaining node, exactly as `Dag.topoCore`'s
              // `parentsIn` filter makes it.
              let mutable rng = ConfRng.ofSeed 1563
              let mutable refusals = 0
              let mutable agreements = 0

              for i in 1..60 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag
                  let ns = modelClosure merged unionHead

                  Expect.isTrue
                      (Dag.verifyDag OpStream.defaultHash planW merged)
                      "the production DAG holds every parent it names"

                  let ignored = DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel ns) ns
                  let refused = DagFold.drain DagFold.RefuseDangling ordLt (drainFuel ns) ns

                  if ignored <> refused then
                      failtestf "iter %d: the policies differ on a closure with no dangling parent" i

                  agreements <- agreements + 1

                  // … and now WITH one: the base node dropped, so every lane's first node names it
                  // from outside the set.
                  let orphaned = ns |> List.filter (fun n -> n.nid <> baseId)

                  match DagFold.drain DagFold.RefuseDangling ordLt (drainFuel orphaned) orphaned with
                  | DagFold.Refused id ->
                      refusals <- refusals + 1

                      Expect.isTrue
                          (orphaned |> List.exists (fun n -> n.nid = id && List.contains baseId n.nparents))
                          "the refusal names a node that really does name the dropped base"
                  | DagFold.Drained _ -> failtestf "iter %d: RefuseDangling drained a set missing the base" i

                  match DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel orphaned) orphaned with
                  | DagFold.Drained ord ->
                      Expect.equal
                          (List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)) ord)
                          (orphaned
                           |> List.map _.nid
                           |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b)))
                          "IgnoreDangling places every remaining node — the dropped parent constrains nothing"
                  | DagFold.Refused _ -> failtestf "iter %d: IgnoreDangling refused" i

              Expect.isGreaterThan agreements 0 "the policies were compared on a clean closure"
              Expect.isGreaterThan refusals 0 "the refusing policy was made to refuse"

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
              | Error e -> Expect.stringContains e "cyclic history" "production names the cycle"

              // The model: the same short drain, from the same nodes.
              let ns = modelClosure cyclic "d"
              Expect.equal (List.length ns) 4 "the closure is the whole hand-built set"

              match DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel ns) ns with
              | DagFold.Drained ord ->
                  Expect.equal ord [ "a" ] "the model drains the acyclic prefix and stops, as topoCore does"

                  Expect.isFalse
                      (DagFold.is_topo_enum ns ord)
                      "and a short drain is not a topological enumeration — which is the signal isAcyclic reads"
              | DagFold.Refused _ -> failtest "the hand-built set holds every parent it names"

              // … and the same set with the back edge removed drains completely and IS one, so the
              // comparison above is a discrimination rather than a refusal of everything.
              let acyclic: Dag.T<PlanOp> =
                  [ node "a" []; node "b" [ "a" ]; node "c" [ "b" ]; node "d" [ "c" ] ]
                  |> List.map (fun n -> n.Id, n)
                  |> Map.ofList
                  |> DagOf.nodes

              let ns' = modelClosure acyclic "d"

              match DagFold.drain DagFold.IgnoreDangling ordLt (drainFuel ns') ns', Dag.tryTopoOrder acyclic "d" with
              | DagFold.Drained ord, Ok production ->
                  Expect.equal ord production "the model and production agree once the back edge is gone"
                  Expect.isTrue (DagFold.is_topo_enum ns' ord) "and the complete drain IS a topological enumeration"
              | m, p -> failtestf "the acyclic control disagreed: model=%A production=%A" m p

          // ---- Phase 309: Dag.tryReplayTo beside the extracted replay_to ----

          testCase "the extracted replay_to is production's tryReplayTo over every union, with a missing-head arm"
          <| fun _ ->
              // `replay_to_is_fold_over_drain` and `replay_to_unknown_head_is_refused`, measured.
              // Every lane head, the merged union head (whose closure is every node, so the drain's
              // tie-break decides the order the ops are applied in), and a head the DAG does not
              // hold — the arm the pre-296 `replayTo` answered with the initial state as `Ok`.
              let mutable rng = ConfRng.ofSeed 3090
              let arms = System.Collections.Generic.Dictionary<string, int>()

              let tally arm =
                  arms[arm] <-
                      (match arms.TryGetValue arm with
                       | true, n -> n + 1
                       | _ -> 1)

              let checkAll (w: StreamWitness<'Op, 'State, 'Rej>) hashState state0 label (dag: Dag.T<'Op>) heads =
                  for h in heads @ [ "no-such-node" ] do
                      match replayDisagreement w hashState state0 toModelDag dag h with
                      | None, arm -> tally arm
                      | Some d, _ -> failtestf "%s\n%s" label d

              for i in 1..60 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag

                  checkAll
                      planW
                      planHash
                      planLaneGen.State0
                      (sprintf "plan iter %d" i)
                      merged
                      (unionHead :: baseId :: hs)

              for i in 1..30 do
                  let lanes, r' = treeLaneGen.Lanes 4 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag treeW treeLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion treeW treeLaneGen.BaseOp baseId hs dag

                  checkAll
                      treeW
                      treeHash
                      treeLaneGen.State0
                      (sprintf "tree iter %d" i)
                      merged
                      (unionHead :: baseId :: hs)

              let count arm =
                  match arms.TryGetValue arm with
                  | true, n -> n
                  | _ -> 0

              Expect.isGreaterThan (count "ok") 0 "some heads replayed to a state"
              Expect.isGreaterThan (count "unknown-head") 0 "the missing-head arm ran"

          testCase "a CYCLIC closure is refused as CyclicHistory by both — and the acyclic control replays"
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
              let mutable rng = ConfRng.ofSeed 3091
              let mutable found = 0
              let mutable example = ""

              for _ in 1..60 do
                  let lanes, r' = planLaneGen.Lanes 3 rng
                  rng <- r'
                  let baseId, hs, dag = productionDag planW planLaneGen.BaseOp lanes
                  let unionHead, merged = mergedUnion planW planLaneGen.BaseOp baseId hs dag

                  match
                      replayDisagreement planW planHash planLaneGen.State0 (droppingOneNode unionHead) merged unionHead
                  with
                  | None, _ -> ()
                  | Some d, _ ->
                      found <- found + 1

                      if example = "" then
                          example <- d

              Expect.isGreaterThan
                  found
                  0
                  "a store missing a closure node must replay differently — this comparison cannot lose"

              Expect.stringContains example "the replay differs" "the disagreement names what moved"

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

              let mnode (p: string list) (op: PlanOp) : DagFold.node<PlanOp> =
                  { DagFold.nid = "same-id"
                    DagFold.nparents = p
                    DagFold.nop = op }

              let same (a: DagFold.node<PlanOp>) (b: DagFold.node<PlanOp>) =
                  List.sort a.nparents = List.sort b.nparents && enc a.nop = enc b.nop

              let ops =
                  let lanes, _ = planLaneGen.Lanes 3 (ConfRng.ofSeed 3092)
                  lanes |> List.concat |> List.distinctBy enc

              Expect.isGreaterThan (List.length ops) 1 "two ops that encode differently were drawn"
              let op1 = List.item 0 ops
              let op2 = List.item 1 ops

              // Production.
              let id1, d1 =
                  Dag.append collide planW (Human "x") op1 "" Dag.empty |> Reference.built

              match Dag.append collide planW (Human "x") op1 "" d1 with
              | Ok(id, d) ->
                  Expect.equal id id1 "the same node twice is one node"
                  Expect.equal d.Nodes.Count 1 "and the DAG is unchanged"
              | Error f -> failtestf "production refused an identical node: %A" f

              match Dag.append collide planW (Human "x") op2 "" d1 with
              | Error(DagAppendFault.ContentIdCollision id) -> Expect.equal id id1 "production names the colliding id"
              | other -> failtestf "production must refuse a differing node minting a held id: %A" other

              // The model.
              let m0: DagFold.dag<PlanOp> = { DagFold.nodes = [] }

              let m1 =
                  match DagFold.add_node same (mnode [] op1) m0 with
                  | DagFold.Appended(_, d) -> d
                  | DagFold.Collision _ -> failtest "the model refused a node into an empty DAG"

              match DagFold.add_node same (mnode [] op1) m1 with
              | DagFold.Appended(id, d) ->
                  Expect.equal id "same-id" "the model deduplicates"
                  Expect.equal d m1 "and leaves the DAG unchanged"
              | DagFold.Collision _ -> failtest "the model refused an identical node"

              match DagFold.add_node same (mnode [] op2) m1 with
              | DagFold.Collision id -> Expect.equal id "same-id" "the model refuses the differing node by name"
              | DagFold.Appended _ -> failtest "the model must refuse a differing node minting a held id"

              // The go-red: a `same` that accepts everything is the pre-296 replacement's twin — it
              // admits the differing node, which production refuses.
              match DagFold.add_node (fun _ _ -> true) (mnode [] op2) m1 with
              | DagFold.Appended _ -> ()
              | DagFold.Collision _ -> failtest "a permissive sameness admits the differing node"

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
              let mutable rng = ConfRng.ofSeed 1580
              let mutable recovered = 0
              let mutable chains = 0
              let mutable mergeNodes = 0
              let mutable pairs = 0

              for i in 1..60 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  rng <- r2
                  let t = mergedDisagreements modelMergeBase planW planLaneGen.BaseOp round1 round2

                  match t.Failures with
                  | [] -> ()
                  | d :: _ ->
                      failtestf
                          "work-plan domain iter %d\nround one:\n%s\nround two:\n%s\n%s"
                          i
                          (renderLanes encPlanOp round1)
                          (renderLanes encPlanOp round2)
                          d

                  recovered <- recovered + t.Recovered
                  chains <- chains + t.Chains
                  mergeNodes <- mergeNodes + t.MergeNodes
                  pairs <- pairs + t.Pairs

              for i in 1..30 do
                  let round1, r1 = treeLaneGen.Lanes 4 rng
                  let round2, r2 = treeLaneGen.Lanes 3 r1
                  rng <- r2
                  let t = mergedDisagreements modelMergeBase treeW treeLaneGen.BaseOp round1 round2

                  match t.Failures with
                  | [] -> ()
                  | d :: _ ->
                      failtestf
                          "reference witness iter %d\nround one:\n%s\nround two:\n%s\n%s"
                          i
                          (renderLanes treeW.Encode round1)
                          (renderLanes treeW.Encode round2)
                          d

                  recovered <- recovered + t.Recovered
                  chains <- chains + t.Chains
                  mergeNodes <- mergeNodes + t.MergeNodes
                  pairs <- pairs + t.Pairs

              Expect.isGreaterThan recovered 0 (sprintf "no ops were recovered at all (recovered=%d)" recovered)
              Expect.isGreaterThan chains 0 "no round-two lane was longer than one op, so no chain was walked"
              Expect.isGreaterThan pairs 0 "no pair of round-two heads had its merge base located"

              Expect.isGreaterThan
                  mergeNodes
                  0
                  "no merged head had a two-parent node beneath it, so this case re-measured Phase 134's base-plus-chains shape"

          testCase "the fold FROM a merged head is the deltas-first fold over the round-two lanes"
          <| fun _ ->
              // `reconcile_many_merged_eq`, measured: production's `Dag.reconcileMany` from the
              // merged head, the extracted `reconcile_many_drained` over the same DAG, and the
              // deltas-first `reconcile_many` handed the round-two lanes directly — the theorem's
              // own right-hand side. Scripts are compared as scripts and halts as canonical
              // reports, exactly as the Phase 131 cases compare them.
              let mutable rng = ConfRng.ofSeed 1581
              let mutable folded = 0
              let mutable halted = 0

              for i in 1..60 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  rng <- r2
                  let _, merged, _, heads2, dag = foldPullFold planW planLaneGen.BaseOp round1 round2
                  let model = toModelDag dag

                  let production =
                      match reconcileScript planW planFootprint dag merged heads2 with
                      | Ok script -> Ok script
                      | Error cs -> Error(FoldConfluence.canonicalConflictReport planW.Encode cs)

                  let fromDag =
                      match
                          DagFold.reconcile_many_drained
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

                  match production with
                  | Ok _ -> folded <- folded + 1
                  | Error _ -> halted <- halted + 1

                  if production <> fromDag || fromDag <> deltasFirst then
                      failtestf
                          "iter %d\nround one:\n%s\nround two:\n%s\n  production:   %A\n  model (DAG):  %A\n  deltas-first: %A"
                          i
                          (renderLanes encPlanOp round1)
                          (renderLanes encPlanOp round2)
                          production
                          fromDag
                          deltasFirst

              Expect.isGreaterThan (folded + halted) 0 "some unions were folded"
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
              let mutable rng = ConfRng.ofSeed 1580
              let mutable found = 0
              let mutable namedTheBase = 0
              let mutable longerDeltas = 0
              let mutable example = ""

              for _ in 1..60 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  rng <- r2
                  let t = mergedDisagreements shallowestCommon planW planLaneGen.BaseOp round1 round2

                  match t.Failures with
                  | [] -> ()
                  | d :: _ ->
                      found <- found + 1

                      if example = "" then
                          example <- d

                  let baseId, merged, _, heads2, dag =
                      foldPullFold planW planLaneGen.BaseOp round1 round2

                  let model = toModelDag dag

                  match heads2 with
                  | h0 :: h1 :: _ ->
                      match shallowestCommon model model.nodes h0 h1 with
                      | Some wrong when wrong = baseId && baseId <> merged ->
                          namedTheBase <- namedTheBase + 1

                          let fromWrong =
                              DagFold.between_ops_drained ordLt (drainFuel model.nodes) model model.nodes wrong h0

                          if fromWrong <> Dag.betweenOps dag merged h0 then
                              longerDeltas <- longerDeltas + 1
                      | _ -> ()
                  | _ -> ()

              Expect.isGreaterThan
                  found
                  0
                  "a locator naming the SHALLOWEST common ancestor must disagree with production — this comparison cannot lose"

              Expect.stringContains example "the merge base differs" "the disagreement names what moved"

              Expect.isGreaterThan
                  namedTheBase
                  0
                  "the reversed locator named the ORIGINAL BASE, which is the go-red the phase asks for"

              Expect.isGreaterThan
                  longerDeltas
                  0
                  "a delta recovered from the base rather than the divergence point must differ from production's"

          // ---- Phase 300: the reconcile's partition and the rejecting-lane pool, beside production ----

          testCase "the extracted reconcile partition is production's over the four shapes"
          <| fun _ ->
              let mutable rng = ConfRng.ofSeed 3001
              let mutable shared = 0

              for _ in 1..40 do
                  let lanes, r = planLaneGen.Lanes 4 rng
                  rng <- r

                  let a, b, c, d =
                      List.item 0 lanes, List.item 1 lanes, List.item 2 lanes, List.item 3 lanes

                  for name, dag, baseId, heads in reconcileShapes a b c d do
                      let production =
                          match heads with
                          | [ h1; h2 ] -> Dag.reconcile blindPlanFootprint dag baseId h1 h2
                          | _ -> failtest "two heads per shape"

                      let model = modelReconcileOps dag baseId heads

                      Expect.equal
                          production
                          (Ok model)
                          (sprintf "%s: production's script is the model's partition" name)

                      // the old rule — each lane's whole delta, concatenated — is what the model is
                      // measured AGAINST: over a shape whose heads share history it must differ
                      let old = heads |> List.collect (Dag.betweenOps dag baseId)

                      if old <> model then
                          shared <- shared + 1

              Expect.isGreaterThan shared 0 "the pool reached a shape where the old rule applied shared history twice"

          testCase
              "the extracted fold and production refuse a rejecting-lane pool identically, under every arrival order"
          <| fun _ ->
              let mutable rng = ConfRng.ofSeed 3002
              let mutable refused = 0
              let rejecting = [ Retitle("missing", "x") ]

              for i in 1..40 do
                  let lanes0, r = planLaneGen.Lanes 3 rng
                  rng <- r
                  // a lane that does not apply from the base, at a position that moves
                  let at = i % (List.length lanes0 + 1)
                  let lanes = List.take at lanes0 @ [ rejecting ] @ List.skip at lanes0

                  let outcomes =
                      FoldConfluence.arrivalOrders (List.length lanes)
                      |> List.map (fun p ->
                          let ls = p |> List.map (fun k -> List.item k lanes)

                          let production =
                              productionFold planW planFootprint planHash basePlan planLaneGen.BaseOp ls

                          let oracle = oracleFold planW planFootprint planHash basePlan ls
                          Expect.equal oracle production "the extracted fold renders what production renders"
                          production)
                      |> List.distinct

                  Expect.equal (List.length outcomes) 1 "one outcome under every arrival order"

                  match outcomes with
                  | [ LaneRejected report ] when report.Contains "T|missing|x" -> refused <- refused + 1
                  | _ -> ()

              Expect.isGreaterThan refused 0 "the pool reached the refusal it exists to measure"

          // ---- Phase 437: the lane-DAG primitives beside sections 18 to 21 ----

          testCase "the extracted merge_base is production's over CRISS-CROSS merges, and is a maximal common ancestor"
          <| fun _ ->
              // `merge_base_maximal`, measured: production's `mergeBase` (and the index's) beside the
              // extracted one over two merges of the same two lanes, and the answer checked to be a
              // common ancestor no other common ancestor descends from. The guard: the pool reached
              // pairs with SEVERAL maximal common ancestors, where the tie-break genuinely decides.
              let mutable rng = ConfRng.ofSeed 4370
              let mutable crissCrossed = 0

              for i in 1..60 do
                  let lanes, r = planLaneGen.Lanes 4 rng
                  rng <- r

                  let a, b, c, d =
                      List.item 0 lanes, List.item 1 lanes, List.item 2 lanes, List.item 3 lanes

                  let h1, h2, dag = crissCross planW planLaneGen.BaseOp a b c d
                  let production = Dag.mergeBase dag h1 h2
                  let model = modelMergeBaseOf dag h1 h2

                  Expect.equal model production (sprintf "iter %d: the extracted merge_base is production's" i)

                  Expect.equal
                      (Dag.Reach.mergeBase (Dag.Reach.ofDag dag) h1 h2)
                      production
                      (sprintf "iter %d: the index answers alike" i)

                  let common = Set.intersect (Dag.ancestorsOf dag h1) (Dag.ancestorsOf dag h2)

                  let maximal =
                      common
                      |> Set.filter (fun x ->
                          common
                          |> Set.forall (fun y -> y = x || not (Set.contains x (Dag.ancestorsOf dag y))))

                  match production with
                  | None -> Expect.isTrue (Set.isEmpty common) "a refusal only over disjoint histories"
                  | Some mb ->
                      Expect.isTrue (Set.contains mb maximal) (sprintf "iter %d: %s is a maximal common ancestor" i mb)

                  if Set.count maximal >= 2 then
                      crissCrossed <- crissCrossed + 1

              Expect.isGreaterThan crissCrossed 0 "the pool reached a pair with two maximal common ancestors"

          testCase "the extracted conflicts_of_heads is production's Dag.conflictsOfHeads over the four shapes"
          <| fun _ ->
              // `conflicts_of_heads_is_the_sweep`, measured over the shapes the partition exists for —
              // disjoint, fast-forward, a duplicate head and a criss-cross — under the domain's real
              // footprint, read canonically (the model's set algebra lists a member once per clause it
              // fails). And the theorem's corollary on production: the report is empty exactly when
              // `reconcileMany` does not refuse with `LanesInterfere`.
              let mutable rng = ConfRng.ofSeed 4371
              let mutable reported = 0
              let mutable clean = 0

              for i in 1..60 do
                  let lanes, r = planLaneGen.Lanes 4 rng
                  rng <- r

                  let a, b, c, d =
                      List.item 0 lanes, List.item 1 lanes, List.item 2 lanes, List.item 3 lanes

                  for name, dag, baseId, heads in reconcileShapes a b c d do
                      let production = productionHeadsReport planFootprint dag baseId heads
                      let model = modelHeadsReport planFootprint dag baseId heads

                      Expect.equal model production (sprintf "iter %d, %s: the report is production's" i name)

                      let interferes =
                          match Dag.reconcileMany planW planFootprint dag baseId planLaneGen.State0 heads with
                          | Error(ReconcileFault.LanesInterfere _) -> true
                          | _ -> false

                      Expect.equal
                          (not (List.isEmpty production))
                          interferes
                          (sprintf "iter %d, %s: a report exactly when reconcileMany refuses with LanesInterfere" i name)

                      if List.isEmpty production then
                          clean <- clean + 1
                      else
                          reported <- reported + 1

              Expect.isGreaterThan reported 0 "the pool reached interfering heads"
              Expect.isGreaterThan clean 0 "the pool reached heads that do not interfere"

          testCase "the extracted reachability index answers production's Reach and the walk over every pair"
          <| fun _ ->
              // `reach_of_dag_agrees`, measured: every ordered pair of ids, and an absent id, over
              // fold-pull-fold DAGs with a side merge and over criss-crosses.
              let mutable rng = ConfRng.ofSeed 4372
              let mutable positives = 0

              for i in 1..30 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 3 r1
                  let cc, r3 = planLaneGen.Lanes 4 r2
                  rng <- r3
                  let _, _, _, _, dag = foldPullFoldSide planW planLaneGen.BaseOp round1 round2

                  let _, _, ccDag =
                      crissCross
                          planW
                          planLaneGen.BaseOp
                          (List.item 0 cc)
                          (List.item 1 cc)
                          (List.item 2 cc)
                          (List.item 3 cc)

                  for label, d in [ "fold-pull-fold", dag; "criss-cross", ccDag ] do
                      match reachDisagreements d with
                      | [], n -> positives <- positives + n
                      | f :: _, _ -> failtestf "iter %d, %s: %s" i label f

              Expect.isGreaterThan positives 0 "some pairs were ancestors"

          testCase "the extracted checkpoint, replay_from and truncation are production's over every node and head"
          <| fun _ ->
              // Theorems 1 and 2, measured: every node as a checkpoint and every node (and an absent
              // id) as a head, on fold-pull-fold DAGs with a side merge — so the replay meets a covered
              // head, a head below the checkpoint and a branch from below it — and `compactAt` at every
              // node, its kept set held to the model's contract and its replays to the full DAG's.
              let tally =
                  { Resumed = 0
                    Uncovered = 0
                    Unreached = 0
                    Compacted = 0
                    Banded = 0
                    CutRefused = 0 }

              let mutable rng = ConfRng.ofSeed 4373

              for i in 1..25 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1
                  rng <- r2

                  let _, _, _, heads2, dag = foldPullFoldSide planW planLaneGen.BaseOp round1 round2

                  match checkpointDisagreements planW planHash planLaneGen.State0 tally dag heads2 with
                  | [] -> ()
                  | f :: _ ->
                      failtestf
                          "work-plan domain iter %d\nround one:\n%s\nround two:\n%s\n%s"
                          i
                          (renderLanes encPlanOp round1)
                          (renderLanes encPlanOp round2)
                          f

              Expect.isGreaterThan tally.Resumed 0 "some replays resumed from a checkpoint"
              Expect.isGreaterThan tally.Uncovered 0 "the branch-from-below refusal was reached"
              Expect.isGreaterThan tally.Unreached 0 "the unreached refusal was reached"
              Expect.isGreaterThan tally.Compacted 0 "some compactions succeeded"
              Expect.isGreaterThan tally.Banded 0 "some compaction kept a band behind its node"
              Expect.isGreaterThan tally.CutRefused 0 "some compaction was refused as uncovered"

          testCase "a model checkpoint sealed at ANOTHER node loses — the measurement can fail"
          <| fun _ ->
              // The go-red of theorem 1, at the differential: the checkpoint production took, handed to
              // the model with its node moved to the base — the state of one node sealed at another.
              let mutable rng = ConfRng.ofSeed 4374
              let mutable lost = 0

              for _ in 1..20 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1
                  rng <- r2

                  let baseId, merged, _, heads2, dag =
                      foldPullFold planW planLaneGen.BaseOp round1 round2

                  let model = toModelDag dag

                  match Dag.checkpointAt OpStream.defaultHash planHash planW planLaneGen.State0 dag merged with
                  | Ok cp ->
                      let moved =
                          { toModelCheckpoint cp with
                              DagFold.cnode = baseId }

                      for t in heads2 do
                          let p = renderProductionFrom planHash (Dag.replayFrom planW cp dag t)
                          let m = renderModelFrom planHash (modelReplayFrom planW model moved t)

                          if p <> m then
                              lost <- lost + 1
                  | Error _ -> () // the merged history does not replay in this domain: no checkpoint to move

              Expect.isGreaterThan lost 0 "a checkpoint sealed at the wrong node disagrees with production"

          testCase "the extracted mergeBase, commonBase and index queries are production's over every pair and head set"
          <| fun _ ->
              // Phase 457's theorems, measured: `merge_base_now_is_merge_base`, `common_base_maximal`
              // and the four `reach_*_agrees`, over fold-pull-fold DAGs with a side merge and over
              // criss-crosses — every ordered pair of ids, an absent id, and head sets of three or
              // more, a repeated head and an absent head included. The guards: some pairs share a
              // base and some do not, some deltas are non-empty, and some N-head base was found.
              let tally = newBoundaryTally ()
              let mutable rng = ConfRng.ofSeed 4570

              for i in 1..12 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1
                  let cc, r3 = planLaneGen.Lanes 4 r2
                  rng <- r3

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

                  for label, d, hs in [ "fold-pull-fold", dag, heads1 @ heads2; "criss-cross", ccDag, [ h1; h2 ] ] do
                      match boundaryDisagreements planW planHash planLaneGen.State0 tally d hs with
                      | [] -> ()
                      | f :: _ -> failtestf "iter %d, %s: %s" i label f

              Expect.isGreaterThan tally.Based 0 "some pairs share a base"
              Expect.isGreaterThan tally.Disjoint 0 "some pairs refuse"
              Expect.isGreaterThan tally.Deltas 0 "some deltas are non-empty"
              Expect.isGreaterThan tally.NHead 0 "some head set of three or more has a common base"

          testCase "the extracted cut keeps production's set at every node, and refuses where production refuses"
          <| fun _ ->
              // `cut_keep_meets_contract`, measured: `Dag.compactAt` at every node of fold-pull-fold
              // DAGs with a side merge, its kept set equal to the extracted `cut_keep`'s and its
              // refusal naming the same node. The guards: a compaction kept a band behind its node,
              // and a compaction was refused as uncovered.
              let tally = newBoundaryTally ()
              let mutable rng = ConfRng.ofSeed 4571

              for i in 1..12 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1
                  rng <- r2
                  let _, _, _, heads2, dag = foldPullFoldSide planW planLaneGen.BaseOp round1 round2

                  match boundaryDisagreements planW planHash planLaneGen.State0 tally dag heads2 with
                  | [] -> ()
                  | f :: _ -> failtestf "iter %d: %s" i f

              Expect.isGreaterThan tally.Kept 0 "some compactions succeeded"
              Expect.isGreaterThan tally.Banded 0 "some compaction kept a band behind its node"
              Expect.isGreaterThan tally.Refused 0 "some compaction was refused as uncovered"

          testCase "an index between that drops one node loses — the measurement can fail"
          <| fun _ ->
              // The go-red of `reach_between_agrees`, at the differential: the extracted index's
              // delta with its first node dropped disagrees with production wherever the delta is
              // non-empty. The proof leg carries the same go-red, run by Phase 457 against the model.
              let mutable rng = ConfRng.ofSeed 4572
              let mutable lost = 0

              for _ in 1..10 do
                  let round1, r1 = planLaneGen.Lanes 3 rng
                  let round2, r2 = planLaneGen.Lanes 2 r1
                  rng <- r2
                  let _, _, _, _, dag = foldPullFoldSide planW planLaneGen.BaseOp round1 round2
                  let model = toModelDag dag
                  let fuel = drainFuel model.nodes
                  let idx = DagFold.reach_of_dag ordLt fuel model
                  let ord = DagFold.drain_order ordLt fuel model.nodes
                  let reach = Dag.Reach.ofDag dag
                  let held = dag.Nodes |> Map.toList |> List.map fst

                  for a in held do
                      for b in held do
                          let dropped =
                              match DagPrimitives.reach_between ordLt fuel model fuel idx ord a b with
                              | [] -> []
                              | _ :: rest -> rest
                              |> List.map (fun n -> n.nid)

                          if dropped <> (Dag.Reach.between reach a b |> List.map _.Id) then
                              lost <- lost + 1

              Expect.isGreaterThan lost 0 "an index between that drops a node disagrees with production" ]
