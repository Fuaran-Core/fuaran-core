module Fuaran.Core.Tests.TreeExtTests

// Phase 249 — Tree convenience combinators (fold / count / ancestors / descendants /
// siblings / depth / subtree).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

let private ids (ns: RNode list) = ns |> List.map _.Id

[<Tests>]
let tests =
    testList
        "Tree (combinators)"
        [ testCase "fold accumulates over every node (preorder)"
          <| fun _ ->
              let n = Tree.fold nodew (fun acc _ -> acc + 1) 0 (sample ())
              Expect.equal n 6 "fold counts all nodes"
              Expect.equal n (Tree.count nodew (sample ())) "fold-count agrees with count"

          testCase "count is the node total"
          <| fun _ -> Expect.equal (Tree.count nodew (sample ())) 6 "six nodes"

          testCase "ancestors are root-first and end at the immediate parent"
          <| fun _ ->
              Expect.equal (ids (Tree.ancestors nodew idw "a1" (sample ()))) [ "root"; "a" ] "root → a"
              Expect.equal (Tree.ancestors nodew idw "root" (sample ())) [] "root has no ancestors"
              Expect.equal (Tree.ancestors nodew idw "ghost" (sample ())) [] "absent ⇒ empty"

          testCase "descendants are the subtree minus its own root, ⊆ preorder"
          <| fun _ ->
              let all = ids (Tree.preorder nodew (sample ()))
              let desc = ids (Tree.descendants nodew (sample ()))
              Expect.equal desc [ "a"; "a1"; "a2"; "b"; "b1" ] "all but root"
              Expect.isTrue (desc |> List.forall (fun d -> List.contains d all)) "descendants ⊆ preorder"

          testCase "siblings exclude the target"
          <| fun _ ->
              Expect.equal (ids (Tree.siblings nodew idw "a1" (sample ()))) [ "a2" ] "a1's sibling is a2"
              Expect.equal (Tree.siblings nodew idw "root" (sample ())) [] "root has no siblings"

          testCase "depth is the path length minus one (root = 0)"
          <| fun _ ->
              Expect.equal (Tree.depth nodew idw "root" (sample ())) (Some 0) "root depth 0"
              Expect.equal (Tree.depth nodew idw "a" (sample ())) (Some 1) "a depth 1"
              Expect.equal (Tree.depth nodew idw "b1" (sample ())) (Some 2) "b1 depth 2"
              Expect.equal (Tree.depth nodew idw "ghost" (sample ())) None "absent ⇒ None"

          testCase "subtree extracts the node as a standalone root"
          <| fun _ ->
              match Tree.subtree nodew idw "a" (sample ()) with
              | Some sub -> Expect.equal (ids (Tree.preorder nodew sub)) [ "a"; "a1"; "a2" ] "a's subtree"
              | None -> failtest "expected the subtree at a"

              Expect.isNone (Tree.subtree nodew idw "ghost" (sample ())) "absent ⇒ None" ]

// Phase 02 — whole-tree transform + deterministic id-remap (the clone/paste primitive).
[<Tests>]
let transformTests =
    let setId newId (n: RNode) = { n with Id = newId }

    testList
        "Tree (transform + remap)"
        [ testCase "map id is the identity"
          <| fun _ -> Expect.equal (Tree.map nodew id (sample ())) (sample ()) "map id = id"

          testCase "map rebuilds every node bottom-up"
          <| fun _ ->
              let upper = Tree.map nodew (fun n -> { n with Kind = n.Kind.ToUpper() }) (sample ())
              Expect.equal upper.Kind "DOC" "root rebuilt"

              Expect.isTrue
                  (Tree.preorder nodew upper |> List.forall (fun n -> n.Kind = n.Kind.ToUpper()))
                  "every node transformed"

          testCase "map over a leaf passes through"
          <| fun _ ->
              let leaf = RNode.leaf "x" "para" "v"
              Expect.equal (Tree.map nodew id leaf) leaf "leaf identity"

          testCase "filter selects in preorder; tryPick finds the first"
          <| fun _ ->
              Expect.equal
                  (ids (Tree.filter nodew (fun n -> n.Kind = "para") (sample ())))
                  [ "a1"; "a2"; "b1" ]
                  "all paras in preorder"

              Expect.equal
                  (Tree.tryPick nodew (fun n -> if n.Kind = "para" then Some n.Id else None) (sample ()))
                  (Some "a1")
                  "first para"

          testCase "remapIds relocates a copied subtree without DuplicateId"
          <| fun _ ->
              let sub = (Tree.subtree nodew idw "a" (sample ())).Value
              let copy = Tree.remapIds nodew setId (fun oldId -> "copy-" + oldId) sub

              Expect.equal (Tree.subtree nodew idw "a" copy) None "old ids rewritten away"

              Expect.equal (ids (Tree.preorder nodew copy)) [ "copy-a"; "copy-a1"; "copy-a2" ] "ids carry the rename"

              // the remapped copy inserts cleanly; the un-remapped original would collide
              match Ops.apply nodew idw (InsertChild("b", copy)) (sample ()) with
              | Ok _ -> ()
              | Error e -> failtestf "insert of remapped copy rejected: %A" e

              match Ops.apply nodew idw (InsertChild("b", sub)) (sample ()) with
              | Error(DuplicateId "a") -> ()
              | other -> failtestf "expected DuplicateId for the un-remapped copy, got %A" other

          // Phase 137 — the case a root-id-only check could not see. `remapIds` is what makes a
          // copied subtree insertable, so a remap that misses part of the subtree is exactly the
          // shape a caller produces by accident.
          testCase "a PARTIALLY remapped copy is refused — the root id is fresh, a descendant is not"
          <| fun _ ->
              let sub = (Tree.subtree nodew idw "a" (sample ())).Value

              // rename the subtree root alone, leaving a1 / a2 as they are
              let partial =
                  Tree.remapIds nodew setId (fun oldId -> if oldId = "a" then "copy-a" else oldId) sub

              Expect.equal (ids (Tree.preorder nodew partial)) [ "copy-a"; "a1"; "a2" ] "only the root was renamed"

              match Ops.apply nodew idw (InsertChild("b", partial)) (sample ()) with
              | Error(DuplicateId "a1") -> ()
              | other -> failtestf "expected DuplicateId \"a1\" for the partially remapped copy, got %A" other

          testCase "a remap that collapses two ids into one is refused (Phase 137)"
          <| fun _ ->
              // Every id is renamed away from the tree, so no id collides with the ROOT — but the
              // remap is not injective, so the copy carries "flat" twice and the resulting tree
              // would silently alias two nodes under `Tree.updateNode` / `Tree.Index.build`.
              let sub = (Tree.subtree nodew idw "a" (sample ())).Value
              let collapsed = Tree.remapIds nodew setId (fun _ -> "flat") sub

              Expect.equal (ids (Tree.preorder nodew collapsed)) [ "flat"; "flat"; "flat" ] "all three collapsed"

              match Ops.apply nodew idw (InsertChild("b", collapsed)) (sample ()) with
              | Error(DuplicateId "flat") -> ()
              | other -> failtestf "expected DuplicateId \"flat\" for the non-injective remap, got %A" other ]

// Phase 05 — build-once index: O(log n)/O(depth) navigators that agree with the combinators.
[<Tests>]
let indexTests =
    testList
        "Tree.Index"
        [ testCase "index navigators agree with the plain combinators for every id"
          <| fun _ ->
              let t = sample ()
              let ix = Tree.Index.build nodew idw t

              for id in Tree.ids nodew t do
                  Expect.equal
                      (Tree.Index.tryFind idw id ix |> Option.map _.Id)
                      (Tree.tryFind nodew idw id t |> Option.map _.Id)
                      (sprintf "tryFind %s" id)

                  Expect.equal
                      (Tree.Index.parentOf idw id ix |> Option.map _.Id)
                      (Tree.parentOf nodew idw id t |> Option.map _.Id)
                      (sprintf "parentOf %s" id)

                  Expect.equal (Tree.Index.path idw id ix) (Tree.path nodew idw id t) (sprintf "path %s" id)

                  Expect.equal
                      (ids (Tree.Index.ancestors idw id ix))
                      (ids (Tree.ancestors nodew idw id t))
                      (sprintf "ancestors %s" id)

                  Expect.equal (Tree.Index.depth idw id ix) (Tree.depth nodew idw id t) (sprintf "depth %s" id)

          testCase "absent ids match the combinators (None / empty)"
          <| fun _ ->
              let ix = Tree.Index.build nodew idw (sample ())
              Expect.isNone (Tree.Index.tryFind idw "ghost" ix) "absent tryFind"
              Expect.isNone (Tree.Index.parentOf idw "ghost" ix) "absent parentOf"
              Expect.isNone (Tree.Index.path idw "ghost" ix) "absent path"
              Expect.isEmpty (Tree.Index.ancestors idw "ghost" ix) "absent ancestors"
              Expect.isNone (Tree.Index.depth idw "ghost" ix) "absent depth"

          // Phase 17 — freshness: an index built from a tree is fresh for that tree and stale after
          // any edit, so a caller can detect (not silently trust) reuse. Exact since Phase 447
          // (D150): the check compares the index with the tree node by node, `encNode` reading each
          // node's content.
          testCase "isFreshFor is true for the tree the index was built from"
          <| fun _ ->
              let t = sample ()
              let ix = Tree.Index.build nodew idw t
              Expect.isTrue (Tree.Index.isFreshFor nodew idw encNode t ix) "fresh for its own tree"

          testCase "isFreshFor detects a removed node"
          <| fun _ ->
              let ix = Tree.Index.build nodew idw (sample ())
              // a2 removed from section a
              let edited =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "x" ]
                        RNode.node "b" "section" [ RNode.leaf "b1" "para" "z" ] ]

              Expect.isFalse (Tree.Index.isFreshFor nodew idw encNode edited ix) "stale after a remove"

          testCase "isFreshFor detects a child reorder"
          <| fun _ ->
              let ix = Tree.Index.build nodew idw (sample ())
              // a's children swapped: [a2; a1]
              let reordered =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "a2" "para" "y"; RNode.leaf "a1" "para" "x" ]
                        RNode.node "b" "section" [ RNode.leaf "b1" "para" "z" ] ]

              Expect.isFalse (Tree.Index.isFreshFor nodew idw encNode reordered ix) "stale after a reorder"

          // Phase 447 — a go-red per case the retired 32-bit stamp could miss or was documented to.
          testCase "isFreshFor detects a reparent that keeps every id, kind and child count"
          <| fun _ ->
              let ix = Tree.Index.build nodew idw (sample ())
              // a1 and b1 trade parents: every node keeps its id, its kind and its child count
              let swapped =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "b1" "para" "z"; RNode.leaf "a2" "para" "y" ]
                        RNode.node "b" "section" [ RNode.leaf "a1" "para" "x" ] ]

              Expect.isFalse (Tree.Index.isFreshFor nodew idw encNode swapped ix) "stale after a reparent"

              Expect.notEqual
                  (Tree.Index.parentOf idw "a1" ix |> Option.map _.Id)
                  (Tree.parentOf nodew idw "a1" swapped |> Option.map _.Id)
                  "and the index's answer is the one that went stale"

          testCase "isFreshFor detects a content-only edit, through the encoder"
          <| fun _ ->
              let t = sample ()
              let ix = Tree.Index.build nodew idw t

              let edited =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "rewritten"; RNode.leaf "a2" "para" "y" ]
                        RNode.node "b" "section" [ RNode.leaf "b1" "para" "z" ] ]

              Expect.isFalse (Tree.Index.isFreshFor nodew idw encNode edited ix) "stale after a content edit"

              // The structural-only check is the one a caller spells out: a constant encoder.
              Expect.isTrue
                  (Tree.Index.isFreshFor nodew idw (fun _ -> "") edited ix)
                  "a constant encoder reads only the structure"

              Expect.notEqual
                  (Tree.Index.tryFind idw "a1" ix |> Option.map _.Value)
                  (Tree.tryFind nodew idw "a1" edited |> Option.map _.Value)
                  "which is why it is not the default: tryFind's node went stale"

          // Phase 496 — the one answer `build`'s doc states: over a repeated id `tryBuild` refuses,
          // naming what `wellFormed` names; `build` is the lenient form, keeping the LAST occurrence
          // as `Tree.digests` does; and `isFreshFor` reads no index of such a tree as fresh, build's
          // own included (the index keeps the last `a`, the walk finds the first).
          testCase "a repeated id: tryBuild refuses as wellFormed does, build keeps the last, none is fresh"
          <| fun _ ->
              let twice =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "b" "para" "x" ]
                        RNode.node "a" "section" [] ]

              Expect.equal
                  (Tree.Index.tryBuild nodew idw twice |> Result.map ignore)
                  (Error(Tree.wellFormed nodew idw twice))
                  "tryBuild's refusal is wellFormed's verdict"

              Expect.equal (Tree.wellFormed nodew idw twice) (Tree.RepeatedId "a") "which names the repeat"

              let ix = Tree.Index.build nodew idw twice

              Expect.equal
                  (Tree.Index.tryFind idw "a" ix |> Option.map (nodew.Children >> List.length))
                  (Some 0)
                  "build keeps the last a"

              Expect.isFalse (Tree.Index.isFreshFor nodew idw encNode twice ix) "and no index of it is fresh"

          testCase "isFreshFor refuses an index holding a key the tree does not"
          <| fun _ ->
              let t = sample ()
              let ix = Tree.Index.build nodew idw t

              let extra =
                  { ix with
                      ById = Map.add "ghost" (RNode.leaf "ghost" "para" "") ix.ById }

              Expect.isFalse (Tree.Index.isFreshFor nodew idw encNode t extra) "an extra ById key"

              let relinked =
                  { ix with
                      ParentOf = Map.add "a1" "b" ix.ParentOf }

              Expect.isFalse (Tree.Index.isFreshFor nodew idw encNode t relinked) "a parent link the tree does not have"
              Expect.isFalse (Tree.Index.isFreshFor nodew idw encNode t { ix with Root = "a" }) "another root" ]
