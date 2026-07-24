module Fuaran.Core.Tests.OpsTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private childIds (w: NodeWitness<RNode, string>) (parentId: string) (root: RNode) =
    Tree.tryFind w idw parentId root
    |> Option.map (fun p -> p.Children |> List.map (fun c -> c.Id))
    |> Option.defaultValue []

[<Tests>]
let tests =
    testList
        "Ops"
        [ testCase "InsertChild appends at index"
          <| fun _ ->
              let op = InsertChild("a", 2, RNode.leaf "a3" "para" "w")

              match Ops.apply nodew idw op (sample ()) with
              | Ok r -> Expect.equal (childIds nodew "a" r) [ "a1"; "a2"; "a3" ] "appended"
              | Error e -> failtestf "unexpected %A" e

          testCase "InsertChild rejects a duplicate id"
          <| fun _ ->
              let op = InsertChild("a", 0, RNode.leaf "b1" "para" "dup")

              match Ops.apply nodew idw op (sample ()) with
              | Error(DuplicateId "b1") -> ()
              | other -> failtestf "expected DuplicateId, got %A" other

          testCase "InsertChild rejects an out-of-range index, reporting the count"
          <| fun _ ->
              match Ops.apply nodew idw (InsertChild("a", 9, RNode.leaf "z" "para" "")) (sample ()) with
              | Error(IndexOutOfRange("a", 9, 2)) -> ()
              | other -> failtestf "expected IndexOutOfRange .. 2, got %A" other

          testCase "InsertChild under an unknown parent enumerates the addressable ids"
          <| fun _ ->
              match Ops.apply nodew idw (InsertChild("ghost", 0, RNode.leaf "z" "para" "")) (sample ()) with
              | Error(UnknownNode("ghost", addressable)) -> Expect.contains addressable "root" "addressable lists ids"
              | other -> failtestf "expected UnknownNode, got %A" other

          testCase "RemoveNode drops the subtree"
          <| fun _ ->
              match Ops.apply nodew idw (RemoveNode "a") (sample ()) with
              | Ok r -> Expect.isNone (Tree.tryFind nodew idw "a" r) "a gone"
              | Error e -> failtestf "unexpected %A" e

          testCase "RemoveNode refuses the root"
          <| fun _ ->
              match Ops.apply nodew idw (RemoveNode "root") (sample ()) with
              | Error CannotRemoveRoot -> ()
              | other -> failtestf "expected CannotRemoveRoot, got %A" other

          testCase "MoveNode relocates under a new parent"
          <| fun _ ->
              match Ops.apply nodew idw (MoveNode("a1", "b", 0)) (sample ()) with
              | Ok r ->
                  Expect.equal (childIds nodew "b" r) [ "a1"; "b1" ] "a1 moved under b"
                  Expect.equal (childIds nodew "a" r) [ "a2" ] "a1 left a"
              | Error e -> failtestf "unexpected %A" e

          testCase "MoveNode refuses to nest a node under itself"
          <| fun _ ->
              match Ops.apply nodew idw (MoveNode("a", "a1", 0)) (sample ()) with
              | Error(WouldNestUnderSelf "a") -> ()
              | other -> failtestf "expected WouldNestUnderSelf, got %A" other

          testCase "ReorderChildren permutes"
          <| fun _ ->
              match Ops.apply nodew idw (ReorderChildren("a", [ "a2"; "a1" ])) (sample ()) with
              | Ok r -> Expect.equal (childIds nodew "a" r) [ "a2"; "a1" ] "reordered"
              | Error e -> failtestf "unexpected %A" e

          testCase "ReorderChildren rejects a non-permutation"
          <| fun _ ->
              match Ops.apply nodew idw (ReorderChildren("a", [ "a1"; "a1" ])) (sample ()) with
              | Error(ReorderMismatch("a", _, _)) -> ()
              | other -> failtestf "expected ReorderMismatch, got %A" other

          testCase "Batch is atomic — a failing member leaves the tree untouched"
          <| fun _ ->
              let batch =
                  Batch [ InsertChild("a", 2, RNode.leaf "a3" "para" "w"); RemoveNode "root" ]

              match Ops.apply nodew idw batch (sample ()) with
              | Error CannotRemoveRoot -> ()
              | other -> failtestf "expected CannotRemoveRoot (atomic abort), got %A" other

          testCase "applyAll reports the failing index and partial tree"
          <| fun _ ->
              let ops = [ InsertChild("a", 2, RNode.leaf "a3" "para" "w"); RemoveNode "ghost" ]

              match Ops.applyAll nodew idw ops (sample ()) with
              | Error(1, UnknownNode("ghost", _), partial) ->
                  Expect.isSome (Tree.tryFind nodew idw "a3" partial) "first op applied in the partial"
              | other -> failtestf "expected Error at index 1, got %A" other

          // Phase 23 — op-script normalisation.
          testCase "normalize cancels an adjacent insert-then-remove of the same node"
          <| fun _ ->
              let ops =
                  [ InsertChild("a", 0, RNode.leaf "tmp" "para" "x")
                    RemoveNode "tmp"
                    InsertChild("a", 0, RNode.leaf "keep" "para" "y") ]

              let normd = Ops.normalize nodew idw ops
              Expect.equal (List.length normd) 1 "the insert/remove pair collapses"

              Expect.equal
                  (Ops.applyAll nodew idw normd (sample ()))
                  (Ops.applyAll nodew idw ops (sample ()))
                  "applyAll is unchanged"

          testCase "normalize keeps only the last of adjacent reorders on a parent"
          <| fun _ ->
              let ops =
                  [ ReorderChildren("a", [ "a2"; "a1" ]); ReorderChildren("a", [ "a1"; "a2" ]) ]

              Expect.equal (Ops.normalize nodew idw ops) [ ReorderChildren("a", [ "a1"; "a2" ]) ] "last reorder wins"

          testCase "normalize drops an empty batch and a nested no-op batch"
          <| fun _ ->
              let ops =
                  [ Batch []
                    Batch [ InsertChild("a", 0, RNode.leaf "t" "para" "x"); RemoveNode "t" ] ]

              Expect.equal (Ops.normalize nodew idw ops) [] "both batches collapse to nothing"

          testCase "normalize leaves an irreducible script untouched"
          <| fun _ ->
              let ops = [ InsertChild("a", 0, RNode.leaf "x" "para" "v"); RemoveNode "b1" ]
              Expect.equal (Ops.normalize nodew idw ops) ops "nothing to collapse"

          testCase "normalize is idempotent when a cancellation exposes a new adjacency (review fix)"
          <| fun _ ->
              // cancelling the insert/remove adjoins the two same-target moves; the fixpoint must
              // then collapse them, and a second normalize must be a no-op.
              let ops =
                  [ MoveNode("a1", "b", 0)
                    InsertChild("a", 0, RNode.leaf "tmp" "para" "x")
                    RemoveNode "tmp"
                    MoveNode("a1", "a", 0) ]

              let once = Ops.normalize nodew idw ops
              Expect.equal (List.length once) 1 "the exposed same-target moves collapse to the net move"
              Expect.equal (Ops.normalize nodew idw once) once "normalize ∘ normalize = normalize"

          testCase "normalizeLaws certify the reference witness green"
          <| fun _ ->
              let genFresh (taken: Set<string>) (rng: ConfRng.T) =
                  let n, r = ConfRng.next rng
                  let mutable id = sprintf "n%d" (abs n)

                  while taken.Contains id do
                      id <- id + "'"

                  RNode.leaf id "para" "v", r

              let opGen: OpGen<RNode, string> =
                  { Tree = (fun rng -> sample (), rng)
                    FreshNode = genFresh
                    CanHold = None }

              let results = Conformance.normalizeLaws nodew idw opGen 1234 200
              Expect.equal (List.length results) 3 "preservation + idempotence + non-growth"
              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) (sprintf "all pass: %A" results) ]
