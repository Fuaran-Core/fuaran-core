module Fuaran.Core.Tests.MergeOracle

// ============================================================================
//  Phase 569 — the DAG bridges the lane-DAG differentials share (moved from the test project's
//  `ProofOracle.DagFoldTests.fs`, its split files and `ProofOracle.Bridges.fs`, so each is defined
//  once): a production DAG as the models read it, production's id order as their `lt`, the drain's
//  fuel, and the reconcile's partition as section 15 of `proofs/DagClosure.fst` states it.
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
