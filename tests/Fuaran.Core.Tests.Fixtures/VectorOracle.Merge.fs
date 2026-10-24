module Fuaran.Core.Tests.MergeOracle

// ============================================================================
//  Phase 569 — the merge family's composition of models, and the DAG bridges the lane-DAG
//  differentials share with it (moved from the test project's `ProofOracle.DagFoldTests.fs` and
//  `ProofOracle.Bridges.fs`, so each is defined once).
//
//  No single model answers a merge vector. Four do, composed, and each step is an extracted model
//  function (DECISIONS.md D238):
//
//    1. the content ids: `Chain.apply_step`, the DAG build, minting each write's id by `node_hash`;
//    2. the base: `DagPrimitives.common_base` of the heads, unless the vector names one;
//    3. the base's state: `DagClosure.replay_to` from the vector's initial tree;
//    4. the reconcile, as `reconcile_fold_order_free` states `Dag.reconcileMany`: the partition
//       (`DagPrimitives.distinct_go` on the heads, `DagClosure.shared_ids` / `excl_lanes` over the
//       union's drain), the interference sweep over the exclusive lanes (`DagFold.reconcile_many`),
//       the shared region replayed from the base (`DagClosure.replay_ids`), and the checked fold of
//       the exclusive lanes from there (`DagFold.fold_once`) — over the tree model's own `apply` and
//       footprint (`TreeOps.apply`, `TreeOps.op_fp`).
//
//  `DagClosure.reconcile_many_drained` is NOT the reconcile: it folds each head's whole delta from
//  the base, which is the rule Phase 300 replaced, and applies shared history once per lane.
// ============================================================================

open Fuaran.Core

/// The id order production's `List.sort` imposes on string ids — the models' `lt`, instantiated.
let ordLt (a: string) (b: string) = System.String.CompareOrdinal(a, b) < 0

/// The models' `covers` bound: one walk step per node, plus the one its base case asks for.
let drainFuel (ns: 'a list) : 'a list =
    match ns with
    | [] -> []
    | n :: _ -> n :: ns

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

/// A model lookup's answer as an option.
let foundId (r: DagClosure.found<string>) : string option =
    match r with
    | DagClosure.Found id -> Some id
    | DagClosure.Missing -> None

let ofModelShape (s: DagFold.shape) : MergeConflictShape =
    match s with
    | DagFold.ConcurrentUpdate -> MergeConflictShape.ConcurrentUpdate
    | DagFold.InsertPositionClash -> MergeConflictShape.InsertPositionClash
    | DagFold.MoveVsRemove -> MergeConflictShape.MoveVsRemove
    | DagFold.SlotClash slot -> MergeConflictShape.SlotClash slot

let ofModelConflict (c: DagFold.conflict<'Op>) : MergeConflict<'Op> =
    { Left = c.left
      Right = c.right
      Address = c.address
      Shape = ofModelShape c.shape }

/// The reconcile's partition as section 15 of `proofs/DagClosure.fst` states it, over a model DAG:
/// the heads deduplicated (first occurrence kept, `distinct_go`), their closures, the base's
/// closure, and the drain of the closures' union at production's id order.
type Region =
    { Heads: string list
      Closures: string list list
      BaseClosure: string list
      Order: string list }

    /// The shared ids, applied once, first.
    member r.Shared = DagClosure.shared_ids r.Order r.BaseClosure r.Closures

    /// One exclusive lane per deduplicated head, in head order.
    member r.Exclusive = DagClosure.excl_lanes r.Order r.BaseClosure r.Closures r.Closures

    /// The ids of the `Ok` script: the shared region, then each exclusive lane.
    member r.Script = DagClosure.reconcile_ids r.Order r.BaseClosure r.Closures

let regionOf (model: DagClosure.dag<'Op>) (baseId: string) (heads: string list) : Region =
    let fuel = model.nodes
    let hs = DagPrimitives.distinct_go [] heads
    let cs = hs |> List.map (DagClosure.ancestors_of model fuel)
    let union = DagClosure.closure_nodes model.nodes (DagFold.concat cs)

    { Heads = hs
      Closures = cs
      BaseClosure = DagClosure.ancestors_of model fuel baseId
      Order = DagClosure.drain_order ordLt (drainFuel union) union }

/// A node's op, by its id, in a model DAG.
let opOf (model: DagClosure.dag<'Op>) (id: string) : 'Op =
    match DagClosure.lookup model.nodes id with
    | DagClosure.Found n -> n.nop
    | DagClosure.Missing -> failwithf "the model DAG holds no node %s" id

// ---------------------------------------------------------------------------
//  The merge family's answer (Phase 569)
// ---------------------------------------------------------------------------

[<Literal>]
let Model = "DagClosure"

[<Literal>]
let LadderRow = "reconcile-differential"

/// The `noBase` answer is the common base's, so its stamp names that model.
[<Literal>]
let BaseModel = "DagPrimitives"

[<Literal>]
let BaseLadderRow = "dag-boundaries-differential"

/// One write of a merge scenario, as the composition reads it: its label, the DAG verb, its parents
/// (`@label` names an earlier write), the actor's canonical text, and the op.
type Write<'Op> =
    { Label: string
      Verb: string
      Parents: string list
      Actor: string
      Op: 'Op }

/// What the composition answers, in the model's own terms — and no more than the models state.
type Verdict<'Op, 'State, 'Rej> =
    | NoBase
    /// The interference sweep's conflicts. The model reads sets as lists (ladder row
    /// `sets-are-lists`), so this is a SET of conflicts: its order and multiplicity are not stated.
    | Interfere of DagFold.conflict<'Op> list
    | SharedRejected of nodeId: string * reject: 'Rej
    /// The heads whose exclusive lanes do not apply from the shared state, in head order. The model's
    /// `LanesRejected` names the lanes and nothing about them: not the node that rejects, the
    /// rejection, or an order by head id.
    | LanesRejected of heads: string list
    | Merged of script: 'Op list * landed: 'State
    | Unappliable of 'Rej

type Answered<'Op, 'State, 'Rej> =
    {
        /// Each write's id, in write order.
        Ids: (string * string) list
        CommonBase: string option
        Base: string option
        Verdict: Verdict<'Op, 'State, 'Rej>
    }

    /// The label a node id was FIRST written under.
    member a.LabelOf(id: string) : string =
        a.Ids
        |> List.tryPick (fun (l, i) -> if i = id then Some l else None)
        |> Option.defaultValue id

/// The scenario's DAG, built by the Chain model: each write is one `step`, minted by `apply_step`
/// (`node_hash` over the sorted parents, the actor's text and the op's encoding). The Chain model
/// states an append on at most one parent; a write it does not state is declined, with the reason.
let private build
    (hash: HashFn)
    (encodeOp: 'Op -> string)
    (writes: Write<'Op> list)
    : Result<(string * string) list * Chain.entry<'Op> list, string> =
    writes
    |> List.fold
        (fun acc w ->
            match acc with
            | Error _ -> acc
            | Ok(ids, es) ->
                let parent (p: string) =
                    if p.StartsWith "@" then
                        ids |> List.tryFind (fun (l, _) -> l = p.Substring 1) |> Option.map snd
                    else
                        Some p

                let step =
                    match w.Verb, w.Parents |> List.map parent with
                    | "appendOn", [] -> Ok(Chain.SAppend("", w.Actor, w.Op))
                    | "appendOn", [ Some p ] -> Ok(Chain.SAppend(p, w.Actor, w.Op))
                    | verb, ps ->
                        Error(
                            sprintf
                                "the write `%s` (%s on %d parents) is not a step the Chain model's DAG build states"
                                w.Label
                                verb
                                (List.length ps)
                        )

                step
                |> Result.map (fun s ->
                    let es' = Chain.apply_step (fun p s -> hash p s) encodeOp ChainOracle.ordinalLe es s
                    // `apply_step` files the node under this id; it is read back by its own function.
                    let id =
                        Chain.node_hash
                            (fun p s -> hash p s)
                            encodeOp
                            ChainOracle.ordinalLe
                            (Chain.step_parents s)
                            w.Actor
                            w.Op

                    ids @ [ w.Label, id ], es'))
        (Ok([], []))

/// The partition one reconcile folds: the deduplicated heads, the shared ids, one exclusive lane per
/// head, and the ids of the script.
type Partition =
    { Heads: string list
      Shared: string list
      Exclusive: string list list
      Script: string list }

/// The reconcile's partition (`reconcile_sound`'s): `regionOf`, read as a `Partition`.
let partitionOf (model: DagClosure.dag<'Op>) (baseId: string) (heads: string list) : Partition =
    let r = regionOf model baseId heads

    { Heads = r.Heads
      Shared = r.Shared
      Exclusive = r.Exclusive
      Script = r.Script }

/// The steps of the composition a test can perturb one at a time (`DagMergeTests`' go-red): the hash
/// the DAG build mints ids with, the tree model's `apply` and footprint, how the base is chosen from
/// the named base and the common base, and the partition.
type Steps<'Op, 'State, 'Rej> =
    { Hash: HashFn
      Apply: 'Op -> 'State -> DagFold.outcome<'State, 'Rej>
      Fp: 'Op -> DagFold.footprint
      Base: string option -> string option -> string option
      Partition: DagClosure.dag<'Op> -> string -> string list -> Partition }

/// The composition as the models state it: the vector's named base, else the common base; the
/// reconcile's own partition.
let steps
    (hash: HashFn)
    (apply: 'Op -> 'State -> DagFold.outcome<'State, 'Rej>)
    (fp: 'Op -> DagFold.footprint)
    : Steps<'Op, 'State, 'Rej> =
    { Hash = hash
      Apply = apply
      Fp = fp
      Base = fun named common -> Option.orElse common named
      Partition = partitionOf }

/// A merge vector as the composition answers it, or the reason it does not (a write the DAG build
/// does not state). `named` is the vector's own base label, if it names one.
let reconcile
    (s: Steps<'Op, 'State, 'Rej>)
    (encodeOp: 'Op -> string)
    (state0: 'State)
    (writes: Write<'Op> list)
    (headLabels: string list)
    (named: string option)
    : Result<Answered<'Op, 'State, 'Rej>, string> =
    let apply, fp = s.Apply, s.Fp

    build s.Hash encodeOp writes
    |> Result.bind (fun (ids, entries) ->
        let model: DagClosure.dag<'Op> =
            { DagClosure.nodes =
                entries
                |> List.map (fun e ->
                    { DagClosure.nid = e.ekey
                      DagClosure.nparents = e.enode.dparents
                      DagClosure.nop = e.enode.dop }) }

        let idOf (l: string) =
            match ids |> List.tryFind (fun (x, _) -> x = l) with
            | Some(_, id) -> id
            | None -> failwithf "the label `%s` names no write the model built" l

        let fuel = drainFuel model.nodes
        let heads = headLabels |> List.map idOf
        let common = foundId (DagPrimitives.common_base ordLt fuel model fuel heads)

        let answered b v =
            { Ids = ids
              CommonBase = common
              Base = b
              Verdict = v }

        match s.Base (named |> Option.map idOf) common with
        | None -> Ok(answered None NoBase)
        | Some baseId ->
            let baseState =
                match DagClosure.replay_to apply ordLt fuel model fuel state0 baseId with
                | DagFold.Ok s -> s
                | DagFold.Error _ -> failwithf "the model does not replay the base %s" baseId

            let r = s.Partition model baseId heads
            let lanes = DagClosure.lanes_by (opOf model) r.Exclusive

            match DagFold.reconcile_many fp lanes with
            | DagFold.Error cs -> Ok(answered (Some baseId) (Interfere cs))
            | DagFold.Ok _ ->
                match DagClosure.replay_ids apply model.nodes r.Shared baseState with
                | DagFold.Error(DagClosure.RRejected(n, rej)) -> Ok(answered (Some baseId) (SharedRejected(n, rej)))
                | DagFold.Error _ -> failwith "replay_ids refuses only by a rejection"
                | DagFold.Ok sharedState ->
                    match DagFold.fold_once apply fp sharedState lanes with
                    | DagFold.LaneFolded landed ->
                        Ok(answered (Some baseId) (Merged(DagClosure.ops_by (opOf model) r.Script, landed)))
                    | DagFold.LaneRejected rej -> Ok(answered (Some baseId) (Unappliable rej))
                    | DagFold.LanesRejected rs ->
                        let rejected =
                            List.zip r.Heads lanes
                            |> List.filter (fun (_, lane) -> List.contains lane rs)
                            |> List.map fst

                        Ok(answered (Some baseId) (LanesRejected rejected))
                    | DagFold.LaneHalted _ -> failwith "the interference sweep above already passed")
