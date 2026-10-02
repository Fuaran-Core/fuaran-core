module Fuaran.Core.Tests.DiffTests

// Phase 245 — structural tree-diff → op script. Defining law:
// Ops.applyAll (Diff.toOps before after) before  ==  after  (structurally).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private applyAllOk ops tree =
    match Ops.applyAll nodew idw ops tree with
    | Ok r -> r
    | Error(i, e, _) -> failtestf "applyAll failed at %d: %A" i e

let private diffOk before after =
    match Diff.toOps nodew idw before after with
    | Ok ops -> ops
    | Error e -> failtestf "diff failed: %A" e

/// Each scenario is a known op sequence producing `after` from the sample tree — the
/// diff must reconstruct *some* valid script yielding the same `after`.
let private scenarios: (string * SkeletonOp<RNode, string> list) list =
    [ "insert leaf", [ InsertChild("a", RNode.leaf "a3" "para" "w") ]
      "insert subtree", [ InsertChild("b", RNode.node "c" "section" [ RNode.leaf "c1" "para" "z" ]) ]
      "remove leaf", [ RemoveNode "a1" ]
      "remove subtree", [ RemoveNode "a" ]
      "move subtree", [ MoveNode("a", "b") ]
      "move leaf", [ MoveNode("a1", "b") ]
      "reorder", [ ReorderChildren("a", [ "a2"; "a1" ]) ]
      "move into a freshly-inserted parent", [ InsertChild("root", RNode.node "n" "section" []); MoveNode("a1", "n") ]
      "survive out of a removed region",
      [ InsertChild("root", RNode.node "keep" "section" [])
        MoveNode("a1", "keep")
        RemoveNode "a" ]
      "mixed insert+move+remove+reorder",
      [ InsertChild("a", RNode.leaf "a3" "para" "w")
        MoveNode("a3", "b")
        RemoveNode "a2"
        ReorderChildren("root", [ "b"; "a" ]) ] ]

[<Tests>]
let tests =
    testList
        "Diff.toOps"
        [ testCase "diff reconstructs after for every scenario"
          <| fun _ ->
              for name, ops in scenarios do
                  let before = sample ()
                  let after = applyAllOk ops before
                  let rebuilt = applyAllOk (diffOk before after) before
                  Expect.equal rebuilt after (sprintf "diff reproduces after — %s" name)

          testCase "before == after diffs to no ops"
          <| fun _ -> Expect.isEmpty (diffOk (sample ()) (sample ())) "identical trees ⇒ empty script"

          testCase "a relocated subtree diffs to MoveNode, never remove+insert"
          <| fun _ ->
              let before = sample ()
              let after = applyAllOk [ MoveNode("a", "b") ] before
              let d = diffOk before after

              Expect.isTrue
                  (d
                   |> List.exists (function
                       | MoveNode("a", _) -> true
                       | _ -> false))
                  "uses MoveNode for the relocated subtree"

              Expect.isFalse
                  (d
                   |> List.exists (function
                       | RemoveNode "a" -> true
                       | _ -> false))
                  "the subtree is not destroyed"

          testCase "differing root ids yield a typed error, not an unapplyable script"
          <| fun _ ->
              match Diff.toOps nodew idw (sample ()) (RNode.node "different" "doc" []) with
              | Error(Diff.RootIdMismatch("root", "different")) -> ()
              | other -> failtestf "expected RootIdMismatch, got %A" other

          // ---- container-aware diff (Phase 09); paras are leaves ----

          testCase "toOpsContained matches toOps when every parent is a container"
          <| fun _ ->
              let canHold (n: RNode) = n.Kind <> "para"
              let before = sample ()
              let after = applyAllOk [ InsertChild("b", RNode.leaf "b2" "para" "w") ] before

              Expect.equal
                  (Diff.toOpsContained canHold nodew idw before after)
                  (Diff.toOps nodew idw before after)
                  "identical to toOps on container trees"

          testCase "toOpsContained refuses an after-tree nesting a child under a leaf"
          <| fun _ ->
              let canHold (n: RNode) = n.Kind <> "para"
              let before = sample ()
              // plain apply (every node a container) lets us build the illegal-for-leaves after
              let after = applyAllOk [ InsertChild("a1", RNode.leaf "x" "para" "v") ] before

              match Diff.toOpsContained canHold nodew idw before after with
              | Error(Diff.TargetNotAContainer("a1", "para")) -> ()
              | other -> failtestf "expected TargetNotAContainer at a1, got %A" other

          // ---- the settled-order drop (Phase 305, 305.t1): steps 1-3 leave a parent holding its
          // kept survivors in before-order, then the inserted shells, then the moved-in survivors,
          // the last two in after-order; a parent whose after-order IS that order gets no reorder.
          // The model is `TreeDiff.fst`'s `settled_order`; the proof is `diff_settles_order`.

          testCase "an append emits no trailing ReorderChildren"
          <| fun _ ->
              let before = sample ()
              let after = applyAllOk [ InsertChild("a", RNode.leaf "a3" "para" "w") ] before
              let d = diffOk before after
              Expect.equal d [ InsertChild("a", RNode.leaf "a3" "para" "w") ] "one insert, no reorder"
              Expect.equal (applyAllOk d before) after "and it still reconstructs"

          testCase "a move-in at the end emits no trailing ReorderChildren"
          <| fun _ ->
              let before = sample ()
              let after = applyAllOk [ MoveNode("b1", "a") ] before
              let d = diffOk before after
              Expect.equal d [ MoveNode("b1", "a") ] "one move, no reorder"
              Expect.equal (applyAllOk d before) after "and it still reconstructs"

          testCase "kept survivors, then shells, then moved-in survivors is the settled order"
          <| fun _ ->
              // a: [a1; a2] -> [a1; a3 (new); b1 (moved in)] with a2 removed — three steps, no reorder
              let before = sample ()

              let after =
                  applyAllOk
                      [ InsertChild("a", RNode.leaf "a3" "para" "w")
                        MoveNode("b1", "a")
                        RemoveNode "a2" ]
                      before

              let d = diffOk before after

              Expect.isFalse
                  (d
                   |> List.exists (function
                       | ReorderChildren _ -> true
                       | _ -> false))
                  "the settled order is after's order, so nothing restates it"

              Expect.equal (applyAllOk d before) after "and it still reconstructs"

          testCase "an insert anywhere but the end still emits the reorder"
          <| fun _ ->
              let before = sample ()

              let after =
                  applyAllOk
                      [ InsertChild("a", RNode.leaf "a0" "para" "w")
                        ReorderChildren("a", [ "a0"; "a1"; "a2" ]) ]
                      before

              let d = diffOk before after

              Expect.isTrue
                  (d |> List.exists ((=) (ReorderChildren("a", [ "a0"; "a1"; "a2" ]))))
                  "the shell lands last, so after's order needs stating"

              Expect.equal (applyAllOk d before) after "and it still reconstructs"

          testCase "a pure permutation still emits exactly one reorder"
          <| fun _ ->
              let before = sample ()
              let after = applyAllOk [ ReorderChildren("a", [ "a2"; "a1" ]) ] before
              Expect.equal (diffOk before after) [ ReorderChildren("a", [ "a2"; "a1" ]) ] "one reorder" ]
