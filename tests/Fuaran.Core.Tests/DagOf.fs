/// Building a `Dag.T` in a test (Phase 386: the constructor is private, and `Dag.ofNodes` is the
/// public way in). Every fixture here files each node under its own id — a test that forges a node
/// tampers its CONTENT, which is what `firstBreak` and `verifyDag` must catch — so the key-mismatch
/// refusal is a defect in the fixture, failed as one.
module Fuaran.Core.Tests.DagOf

open Fuaran.Core

/// `Dag.ofNodes`, the refusal failed as a fixture defect.
let nodes (nodes: Map<string, DagNode<'Op>>) : Dag.T<'Op> =
    match Dag.ofNodes nodes with
    | Ok dag -> dag
    | Error m -> failwithf "a fixture filed a node under %s that carries id %s" m.Key m.NodeId
