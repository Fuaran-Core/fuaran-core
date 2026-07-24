module Fuaran.Core.Tests.TreeTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

[<Tests>]
let tests =
    testList
        "Tree"
        [ testCase "preorder visits node-then-children"
          <| fun _ ->
              let order = Tree.preorder nodew (sample ()) |> List.map (fun n -> n.Id)
              Expect.equal order [ "root"; "a"; "a1"; "a2"; "b"; "b1" ] "preorder"

          testCase "ids enumerates the whole tree"
          <| fun _ -> Expect.equal (Tree.ids nodew (sample ()) |> List.length) 6 "six ids"

          testCase "tryFind locates a node"
          <| fun _ ->
              let n = Tree.tryFind nodew idw "a2" (sample ())
              Expect.equal (n |> Option.map (fun x -> x.Value)) (Some "y") "a2 value"

          testCase "tryFind misses an absent id"
          <| fun _ -> Expect.isNone (Tree.tryFind nodew idw "nope" (sample ())) "absent"

          testCase "parentOf finds the parent"
          <| fun _ ->
              let p = Tree.parentOf nodew idw "a1" (sample ())
              Expect.equal (p |> Option.map (fun x -> x.Id)) (Some "a") "parent of a1"

          testCase "parentOf is None for the root"
          <| fun _ -> Expect.isNone (Tree.parentOf nodew idw "root" (sample ())) "root has no parent"

          testCase "path is the absolute id-path"
          <| fun _ -> Expect.equal (Tree.path nodew idw "b1" (sample ())) (Some [ "root"; "b"; "b1" ]) "path to b1"

          testCase "updateNode rebuilds only the target"
          <| fun _ ->
              let updated =
                  Tree.updateNode nodew idw "a1" (fun n -> { n with Value = "X" }) (sample ())

              let v =
                  updated
                  |> Option.bind (Tree.tryFind nodew idw "a1")
                  |> Option.map (fun n -> n.Value)

              Expect.equal v (Some "X") "a1 updated"

          testCase "contentHash is deterministic and shape-sensitive"
          <| fun _ ->
              let h1 = Tree.contentHash nodew (sample ())
              let h2 = Tree.contentHash nodew (sample ())
              Expect.equal h1 h2 "stable"

              let mutated =
                  RNode.node "root" "doc" [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "x" ] ]

              Expect.notEqual h1 (Tree.contentHash nodew mutated) "shape change ⇒ different hash"

          testCase "encodeHash is content-sensitive where contentHash is shape-only"
          <| fun _ ->
              let enc (n: RNode) = n.Kind + ":" + n.Value
              let a = sample ()
              // same SHAPE, different payload on one leaf
              let b =
                  Tree.updateNode nodew idw "a1" (fun n -> { n with Value = "CHANGED" }) a
                  |> Option.get

              Expect.equal
                  (Tree.contentHash nodew a)
                  (Tree.contentHash nodew b)
                  "payload-only change ⇒ contentHash unchanged (shape-only)"

              Expect.notEqual
                  (Tree.encodeHash nodew enc a)
                  (Tree.encodeHash nodew enc b)
                  "payload-only change ⇒ encodeHash differs"

              Expect.equal
                  (Tree.encodeHash nodew enc a)
                  (Tree.encodeHash nodew enc (sample ()))
                  "stable across runs for an equal tree"

          testCase "deep tree: path / map / updateNode / remapIds are stack-safe (Phase 19)"
          <| fun _ ->
              // build a 50k-deep linear tree bottom-up (no recursion in the builder), deep enough
              // to overflow the prior body-recursive walkers
              let depth = 50000

              let deep =
                  let mutable n = RNode.leaf "leaf" "para" "x"

                  for i in depth .. -1 .. 1 do
                      n <- RNode.node (sprintf "n%d" i) "section" [ n ]

                  n

              Expect.equal (Tree.count nodew deep) (depth + 1) "node count = depth + 1"

              Expect.equal
                  (Tree.path nodew idw "leaf" deep |> Option.map List.length)
                  (Some(depth + 1))
                  "path to the deep leaf"

              // map (identity) rebuilds the whole tree without overflow
              Expect.equal (Tree.count nodew (Tree.map nodew id deep)) (depth + 1) "map preserves count"

              // updateNode reaches and rewrites the deep leaf
              let updated =
                  Tree.updateNode nodew idw "leaf" (fun n -> { n with Value = "Y" }) deep

              Expect.equal
                  (updated
                   |> Option.bind (Tree.tryFind nodew idw "leaf")
                   |> Option.map (fun n -> n.Value))
                  (Some "Y")
                  "deep leaf updated"

              // remapIds rewrites every id without overflow
              let remapped =
                  Tree.remapIds nodew (fun newId n -> { n with Id = newId }) (fun i -> i + "'") deep

              Expect.equal (Tree.count nodew remapped) (depth + 1) "remap preserves count"
              Expect.isTrue (Tree.exists nodew idw "leaf'" remapped) "remapped leaf id present"

          testCase "encodeHash separates adjacent encodings (Phase 11 — no boundary collision)"
          <| fun _ ->
              // Two trees whose per-node encodings concatenate to the SAME string but split at a
              // different child boundary: ["ab";"c"] vs ["a";"bc"]. Pre-Phase-11 the `""` fold
              // aliased them; the U+0001 separator must keep them distinct.
              let enc (n: RNode) = n.Value
              let a = RNode.node "r" "k" [ RNode.leaf "c1" "k" "ab"; RNode.leaf "c2" "k" "c" ]
              let b = RNode.node "r" "k" [ RNode.leaf "c1" "k" "a"; RNode.leaf "c2" "k" "bc" ]

              Expect.notEqual
                  (Tree.encodeHash nodew enc a)
                  (Tree.encodeHash nodew enc b)
                  "boundary-aliased trees hash distinctly with the separator" ]
