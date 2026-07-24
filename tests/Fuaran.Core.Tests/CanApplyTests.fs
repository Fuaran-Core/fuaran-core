module Fuaran.Core.Tests.CanApplyTests

// Phase 246 — dry-run validation: `canApply` returns the exact rejection `apply` would,
// without building the new tree for the index/structure ops.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// One representative op per outcome class — Ok and every Rejection the skeleton emits.
let private cases: SkeletonOp<RNode, string> list =
    [ InsertChild("a", 2, RNode.leaf "a3" "para" "w") // Ok
      InsertChild("a", 0, RNode.leaf "b1" "para" "dup") // DuplicateId
      InsertChild("a", 9, RNode.leaf "z" "para" "") // IndexOutOfRange
      InsertChild("ghost", 0, RNode.leaf "z" "para" "") // UnknownNode
      RemoveNode "a" // Ok
      RemoveNode "root" // CannotRemoveRoot
      RemoveNode "ghost" // UnknownNode
      MoveNode("a1", "b", 0) // Ok
      MoveNode("a", "a1", 0) // WouldNestUnderSelf
      MoveNode("root", "b", 0) // CannotRemoveRoot
      ReorderChildren("a", [ "a2"; "a1" ]) // Ok
      ReorderChildren("a", [ "a1"; "a1" ]) // ReorderMismatch
      Batch [ InsertChild("a", 2, RNode.leaf "a3" "para" "w"); RemoveNode "root" ] ] // CannotRemoveRoot (atomic)

[<Tests>]
let tests =
    testList
        "Ops.canApply"
        [ testCase "canApply agrees with apply on accept/reject and the exact envelope"
          <| fun _ ->
              for op in cases do
                  let applied = Ops.apply nodew idw op (sample ())
                  let checked' = Ops.canApply nodew idw op (sample ())

                  match applied, checked' with
                  | Ok _, Ok() -> ()
                  | Error ea, Error ec -> Expect.equal ec ea (sprintf "same envelope for %A" op)
                  | _ -> failtestf "canApply/apply disagree on %A: apply=%A canApply=%A" op applied checked'

          testCase "canApply on a valid op returns Ok"
          <| fun _ ->
              match Ops.canApply nodew idw (InsertChild("a", 2, RNode.leaf "a3" "para" "w")) (sample ()) with
              | Ok() -> ()
              | Error e -> failtestf "expected Ok, got %A" e

          testCase "canApply names the rejection (IndexOutOfRange reports the count)"
          <| fun _ ->
              match Ops.canApply nodew idw (InsertChild("a", 9, RNode.leaf "z" "para" "")) (sample ()) with
              | Error(IndexOutOfRange("a", 9, 2)) -> ()
              | other -> failtestf "expected IndexOutOfRange .. 2, got %A" other

          testCase "canApplyAll reports the first failing index without a tree"
          <| fun _ ->
              let ops = [ InsertChild("a", 2, RNode.leaf "a3" "para" "w"); RemoveNode "ghost" ]

              match Ops.canApplyAll nodew idw ops (sample ()) with
              | Error(1, UnknownNode("ghost", _)) -> ()
              | other -> failtestf "expected Error at index 1, got %A" other

          testCase "canApplyAll accepts an all-valid sequence"
          <| fun _ ->
              let ops =
                  [ InsertChild("a", 2, RNode.leaf "a3" "para" "w")
                    ReorderChildren("a", [ "a3"; "a2"; "a1" ]) ]

              Expect.isOk (Ops.canApplyAll nodew idw ops (sample ())) "valid sequence accepted" ]
