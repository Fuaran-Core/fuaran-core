module Fuaran.Core.Tests.TreeTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// A tree of 1-40 nodes, shaped by `seed`, whose ids are drawn from `idPool width`: a narrow pool gives
/// repeated ids, which is what the first-match rule is about (Phase 607).
let private idPool (width: int) =
    [ for i in 0 .. width - 1 -> sprintf "n%d" i ]

let private generatedTree (seed: int) (width: int) : RNode =
    let rng = System.Random seed
    let pool = idPool width |> Array.ofList
    let pick () = pool.[rng.Next pool.Length]

    let rec build (budget: int) (depth: int) : RNode * int =
        let kids = if depth >= 5 || budget <= 1 then 0 else rng.Next 4

        let children, used =
            List.fold
                (fun (acc, used) _ ->
                    if budget - used > 1 then
                        let c, n = build (budget - used - 1) (depth + 1)
                        c :: acc, used + n
                    else
                        acc, used)
                ([], 1)
                [ 1..kids ]

        RNode.node (pick ()) "section" (List.rev children), used

    build (1 + rng.Next 40) 0 |> fst

[<Tests>]
let tests =
    testList
        "Tree"
        [ testCase "preorder visits node-then-children"
          <| fun _ ->
              let order = Tree.preorder nodew (sample ()) |> List.map _.Id
              Expect.equal order [ "root"; "a"; "a1"; "a2"; "b"; "b1" ] "preorder"

          testCase "ids enumerates the whole tree"
          <| fun _ -> Expect.equal (Tree.ids nodew (sample ()) |> List.length) 6 "six ids"

          testCase "tryFind locates a node"
          <| fun _ ->
              let n = Tree.tryFind nodew idw "a2" (sample ())
              Expect.equal (n |> Option.map _.Value) (Some "y") "a2 value"

          testCase "tryFind misses an absent id"
          <| fun _ -> Expect.isNone (Tree.tryFind nodew idw "nope" (sample ())) "absent"

          testCase "parentOf finds the parent"
          <| fun _ ->
              let p = Tree.parentOf nodew idw "a1" (sample ())
              Expect.equal (p |> Option.map _.Id) (Some "a") "parent of a1"

          testCase "parentOf is None for the root"
          <| fun _ -> Expect.isNone (Tree.parentOf nodew idw "root" (sample ())) "root has no parent"

          testCase "path is the absolute id-path"
          <| fun _ -> Expect.equal (Tree.path nodew idw "b1" (sample ())) (Some [ "root"; "b"; "b1" ]) "path to b1"

          testCase "updateNode rebuilds only the target"
          <| fun _ ->
              let updated =
                  Tree.updateNode nodew idw "a1" (fun n -> { n with Value = "X" }) (sample ())

              let v = updated |> Option.bind (Tree.tryFind nodew idw "a1") |> Option.map _.Value

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
                  (updated |> Option.bind (Tree.tryFind nodew idw "leaf") |> Option.map _.Value)
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
                  "boundary-aliased trees hash distinctly with the separator"

          testCase "encodeHash and contentHash see the tree's SHAPE — two trees, one preorder (Phase 290)"
          <| fun _ ->
              // `root(a(a1,a2), b(b1))` and `root(a(a1,a2,b(b1)))` have the SAME preorder — id for
              // id, kind for kind, value for value — and are one `MoveNode(b, a)` apart. Until
              // Phase 290 both folds ran over the preorder alone, so they hashed EQUAL under every
              // encoder, and a memo keyed on `encodeHash` served one tree for the other. The arity
              // fold sees where `b` hangs.
              let flat =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "x"; RNode.leaf "a2" "para" "y" ]
                        RNode.node "b" "section" [ RNode.leaf "b1" "para" "z" ] ]

              let nested =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node
                            "a"
                            "section"
                            [ RNode.leaf "a1" "para" "x"
                              RNode.leaf "a2" "para" "y"
                              RNode.node "b" "section" [ RNode.leaf "b1" "para" "z" ] ] ]

              Expect.equal
                  (Tree.preorder nodew flat |> List.map (fun n -> n.Id, n.Kind, n.Value))
                  (Tree.preorder nodew nested |> List.map (fun n -> n.Id, n.Kind, n.Value))
                  "the two trees share one preorder — the premise"

              Expect.notEqual (Tree.contentHash nodew flat) (Tree.contentHash nodew nested) "contentHash sees the shape"

              Expect.notEqual
                  (Tree.encodeHash nodew encNode flat)
                  (Tree.encodeHash nodew encNode nested)
                  "encodeHash sees the shape under the reference encoder"

              // And under an encoder that carries the ids too — the shard's "including one that
              // carries ids": the ids are the same in both preorders, so only the arity can tell.
              let withIds (n: RNode) = n.Id + "|" + n.Kind + "|" + n.Value

              Expect.notEqual
                  (Tree.encodeHash nodew withIds flat)
                  (Tree.encodeHash nodew withIds nested)
                  "encodeHash sees the shape under an id-carrying encoder"

              Expect.notEqual
                  (Tree.encodePreimage nodew withIds flat)
                  (Tree.encodePreimage nodew withIds nested)
                  "and the pre-image itself differs, not merely its digest"

              // The digest is the pre-image's FNV-1a, and the pre-image is the canonical field
              // encoding of (encoding, arity) pairs in preorder — pinned here so a re-derivation
              // elsewhere (a host twin) has the exact shape.
              Expect.equal
                  (Tree.encodePreimage nodew withIds flat)
                  (Hash.canonicalFields
                      [ "root|doc|"
                        "2"
                        "a|section|"
                        "2"
                        "a1|para|x"
                        "0"
                        "a2|para|y"
                        "0"
                        "b|section|"
                        "1"
                        "b1|para|z"
                        "0" ])
                  "the pre-image is (encode, arity) per preorder node through canonicalFields"

              Expect.equal
                  (Tree.encodeHash nodew withIds flat)
                  (Hash.fnv1a (Tree.encodePreimage nodew withIds flat))
                  "encodeHash is the FNV-1a of encodePreimage"

          // Phase 607: the lookups stop at the first preorder match, and the graft check keys the graft rather
          // than the tree. Both are held against the implementations they replaced, which are still expressible
          // through the public surface: a scan of `Tree.preorder`, and `Tree.firstRepeatedId` over the tree's
          // ids and the graft's. The trees draw ids from a small pool, so most carry repeats: the first-match
          // rule is what is under test, and an id-unique tree would hold it vacuously.
          testCase "the lookups answer the node the preorder scan answers, on generated trees"
          <| fun _ ->
              let mutable repeated = 0

              for seed in 1..300 do
                  let t = generatedTree seed 4
                  let order = Tree.preorder nodew t

                  if List.length order <> (order |> List.distinctBy _.Id |> List.length) then
                      repeated <- repeated + 1

                  for target in "absent" :: idPool 4 do
                      let same what (actual: RNode option) (expected: RNode option) =
                          match actual, expected with
                          | Some a, Some e ->
                              Expect.isTrue
                                  (obj.ReferenceEquals(a, e))
                                  (sprintf "%s %s, seed %d: not the same node" what target seed)
                          | None, None -> ()
                          | _ ->
                              failtestf "%s %s, seed %d: %A where the scan answered %A" what target seed actual expected

                      let scanFind = order |> List.tryFind (fun n -> n.Id = target)

                      let scanParent =
                          order
                          |> List.tryFind (fun n -> n.Children |> List.exists (fun c -> c.Id = target))

                      same "tryFind" (Tree.tryFind nodew idw target t) scanFind
                      same "subtree" (Tree.subtree nodew idw target t) scanFind
                      same "parentOf" (Tree.parentOf nodew idw target t) scanParent

                      same "tryPick" (Tree.tryPick nodew (fun n -> if n.Id = target then Some n else None) t) scanFind

                      Expect.equal
                          (Tree.exists nodew idw target t)
                          scanFind.IsSome
                          (sprintf "exists %s, seed %d" target seed)

                  Expect.equal
                      (Tree.fold nodew (fun acc (n: RNode) -> n.Id :: acc) [] t)
                      (order |> List.map _.Id |> List.rev)
                      (sprintf "fold visits in preorder, seed %d" seed)

              Expect.isGreaterThan repeated 100 "most generated trees carry a repeated id"

          testCase "the graft check names the offender firstRepeatedId names over the whole tree, on generated trees"
          <| fun _ ->
              let mutable refused = 0
              let mutable accepted = 0

              for seed in 1..400 do
                  // a pool wide enough that a graft is sometimes clean, narrow enough that it often is not
                  let t = generatedTree seed 30
                  let g = generatedTree (seed + 100000) 30

                  let expected =
                      match Tree.firstRepeatedId idw (Tree.ids nodew t) (Tree.ids nodew g) with
                      | Some d -> Tree.RepeatedId d
                      | None -> Tree.Structural

                  let actual = Tree.graftWellFormed nodew idw g t
                  Expect.equal actual expected (sprintf "seed %d" seed)

                  match actual with
                  | Tree.RepeatedId _ -> refused <- refused + 1
                  | Tree.Structural -> accepted <- accepted + 1

                  // and a graft drawn from ids the tree cannot carry is clean exactly when it repeats nothing itself
                  let fresh =
                      Tree.map nodew (fun n -> { n with Id = "g-" + n.Id }) (generatedTree (seed + 200000) 30)

                  let expectedFresh =
                      match Tree.firstRepeatedId idw (Tree.ids nodew t) (Tree.ids nodew fresh) with
                      | Some d -> Tree.RepeatedId d
                      | None -> Tree.Structural

                  Expect.equal
                      (Tree.graftWellFormed nodew idw fresh t)
                      expectedFresh
                      (sprintf "fresh graft, seed %d" seed)

              Expect.isGreaterThan refused 50 "some grafts repeat an id"
              Expect.isGreaterThan accepted 50 "some grafts are clean" ]
