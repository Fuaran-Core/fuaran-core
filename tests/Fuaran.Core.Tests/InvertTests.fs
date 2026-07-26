module Fuaran.Core.Tests.InvertTests

// Phase 242 — generic op inversion → undo/redo over the skeleton-five.
// Law: apply (invert op pre) (apply op pre) = pre  (apply-then-undo is identity).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private applyOk op tree =
    match Ops.apply nodew idw op tree with
    | Ok r -> r
    | Error e -> failtestf "apply failed: %A" e

let private invertOk op pre =
    match Ops.invert nodew idw op pre with
    | Ok inv -> inv
    | Error e -> failtestf "invert failed: %A" e

/// Every invertible (applyable) op class over the sample tree.
let private invertible: SkeletonOp<RNode, string> list =
    [ InsertChild("a", RNode.leaf "a3" "para" "w")
      InsertChild("b", RNode.node "c" "section" [ RNode.leaf "c1" "para" "deep" ])
      RemoveNode "a1"
      RemoveNode "a" // remove a whole subtree
      MoveNode("a1", "b")
      MoveNode("a2", "a") // move within the same parent
      ReorderChildren("a", [ "a2"; "a1" ])
      Batch
          [ InsertChild("a", RNode.leaf "a3" "para" "w")
            MoveNode("a3", "b")
            ReorderChildren("a", [ "a2"; "a1" ]) ] ]

[<Tests>]
let tests =
    testList
        "Ops.invert"
        [ testCase "apply-then-undo is identity for every op class"
          <| fun _ ->
              for op in invertible do
                  let pre = sample ()
                  let post = applyOk op pre
                  let inv = invertOk op pre
                  Expect.equal (applyOk inv post) pre (sprintf "undo restores pre for %A" op)

          testCase "undo of undo is redo — re-applying reproduces the forward effect"
          <| fun _ ->
              for op in invertible do
                  let pre = sample ()
                  let post = applyOk op pre
                  let inv = invertOk op pre // inv : post -> pre
                  let redo = invertOk inv post // redo : pre -> post
                  Expect.equal (applyOk redo pre) post (sprintf "redo reproduces post for %A" op)

          testCase "InsertChild inverts to RemoveNode of the inserted id"
          <| fun _ ->
              let op = InsertChild("a", RNode.leaf "a3" "para" "w")

              match Ops.invert nodew idw op (sample ()) with
              | Ok(RemoveNode "a3") -> ()
              | other -> failtestf "expected RemoveNode a3, got %A" other

          testCase "RemoveNode inverts to InsertChild restoring parent + index + subtree"
          <| fun _ ->
              match Ops.invert nodew idw (RemoveNode "a2") (sample ()) with
              | Ok(InsertChild("a", sub)) -> Expect.equal sub.Id "a2" "the removed subtree is recaptured"
              | other -> failtestf "expected InsertChild a@1, got %A" other

          testCase "MoveNode inverts to a move back plus the order it belonged to"
          <| fun _ ->
              // `a1` was FIRST under `a`, and the inverse move appends, so restoring
              // the position takes a reorder naming the pre-state order. That order is
              // read from the pre-state exactly as the index used to be — the same
              // information, named rather than counted.
              match Ops.invert nodew idw (MoveNode("a1", "b")) (sample ()) with
              | Ok(Batch [ MoveNode("a1", "a"); ReorderChildren("a", [ "a1"; "a2" ]) ]) -> ()
              | other -> failtestf "expected the move-back + reorder batch, got %A" other

          testCase "a node that was already last inverts to the bare move — no redundant reorder"
          <| fun _ ->
              match Ops.invert nodew idw (MoveNode("a2", "b")) (sample ()) with
              | Ok(MoveNode("a2", "a")) -> ()
              | other -> failtestf "expected a bare move-back for a last child, got %A" other

          testCase "a non-applyable op has no inverse — its rejection is returned"
          <| fun _ ->
              match Ops.invert nodew idw (RemoveNode "root") (sample ()) with
              | Error CannotRemoveRoot -> ()
              | other -> failtestf "expected CannotRemoveRoot, got %A" other

              match Ops.invert nodew idw (RemoveNode "ghost") (sample ()) with
              | Error(UnknownNode("ghost", _)) -> ()
              | other -> failtestf "expected UnknownNode, got %A" other ]
