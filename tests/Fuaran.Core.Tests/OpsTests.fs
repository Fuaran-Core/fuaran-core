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
              let op = InsertChild("a", RNode.leaf "a3" "para" "w")

              match Ops.apply nodew idw op (sample ()) with
              | Ok r -> Expect.equal (childIds nodew "a" r) [ "a1"; "a2"; "a3" ] "appended"
              | Error e -> failtestf "unexpected %A" e

          testCase "InsertChild rejects a duplicate id"
          <| fun _ ->
              let op = InsertChild("a", RNode.leaf "b1" "para" "dup")

              match Ops.apply nodew idw op (sample ()) with
              | Error(DuplicateId "b1") -> ()
              | other -> failtestf "expected DuplicateId, got %A" other

          testCase "InsertChild under an unknown parent enumerates the addressable ids"
          <| fun _ ->
              match Ops.apply nodew idw (InsertChild("ghost", RNode.leaf "z" "para" "")) (sample ()) with
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

          testCase "MoveNode relocates under a new parent, appending"
          <| fun _ ->
              // Membership only: the move APPENDS. Landing it anywhere else is
              // `Batch [MoveNode …; ReorderChildren …]` — see the next case.
              match Ops.apply nodew idw (MoveNode("a1", "b")) (sample ()) with
              | Ok r ->
                  Expect.equal (childIds nodew "b" r) [ "b1"; "a1" ] "a1 appended under b"
                  Expect.equal (childIds nodew "a" r) [ "a2" ] "a1 left a"
              | Error e -> failtestf "unexpected %A" e

          testCase "a move to a chosen position is the move plus a reorder"
          <| fun _ ->
              let op = Batch [ MoveNode("a1", "b"); ReorderChildren("b", [ "a1"; "b1" ]) ]

              match Ops.apply nodew idw op (sample ()) with
              | Ok r -> Expect.equal (childIds nodew "b" r) [ "a1"; "b1" ] "placed first by naming the order"
              | Error e -> failtestf "unexpected %A" e

          testCase "MoveNode refuses to nest a node under itself"
          <| fun _ ->
              match Ops.apply nodew idw (MoveNode("a", "a1")) (sample ()) with
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
                  Batch [ InsertChild("a", RNode.leaf "a3" "para" "w"); RemoveNode "root" ]

              match Ops.apply nodew idw batch (sample ()) with
              | Error CannotRemoveRoot -> ()
              | other -> failtestf "expected CannotRemoveRoot (atomic abort), got %A" other

          testCase "applyAll reports the failing index and partial tree"
          <| fun _ ->
              let ops = [ InsertChild("a", RNode.leaf "a3" "para" "w"); RemoveNode "ghost" ]

              match Ops.applyAll nodew idw ops (sample ()) with
              | Error(1, UnknownNode("ghost", _), partial) ->
                  Expect.isSome (Tree.tryFind nodew idw "a3" partial) "first op applied in the partial"
              | other -> failtestf "expected Error at index 1, got %A" other

          // Phase 23 — op-script normalisation.
          testCase "normalize cancels an adjacent insert-then-remove of the same node"
          <| fun _ ->
              let ops =
                  [ InsertChild("a", RNode.leaf "tmp" "para" "x")
                    RemoveNode "tmp"
                    InsertChild("a", RNode.leaf "keep" "para" "y") ]

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
                    Batch [ InsertChild("a", RNode.leaf "t" "para" "x"); RemoveNode "t" ] ]

              Expect.equal (Ops.normalize nodew idw ops) [] "both batches collapse to nothing"

          testCase "normalize leaves an irreducible script untouched"
          <| fun _ ->
              let ops = [ InsertChild("a", RNode.leaf "x" "para" "v"); RemoveNode "b1" ]
              Expect.equal (Ops.normalize nodew idw ops) ops "nothing to collapse"

          testCase "normalize is idempotent when a cancellation exposes a new adjacency (review fix)"
          <| fun _ ->
              // cancelling the insert/remove adjoins the two same-target moves; the fixpoint must
              // then collapse them, and a second normalize must be a no-op.
              let ops =
                  [ MoveNode("a1", "b")
                    InsertChild("a", RNode.leaf "tmp" "para" "x")
                    RemoveNode "tmp"
                    MoveNode("a1", "a") ]

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

// ---------------------------------------------------------------------------
//  Phase 137 — insert-subtree id uniqueness
//
//  `validateInsert` used to check only the inserted node's OWN id, so a subtree whose
//  DESCENDANT id was already in the tree — or which repeated an id within itself — was
//  accepted, after which `Tree.updateNode` rewrites every node carrying the repeated id and
//  `Tree.Index.build`'s `Map.ofList` silently keeps the last. These cases are the go-red
//  proof: each one is accepted before the fix and refused after it.
//
//  In `sample ()` the ids are root / a / a1 / a2 / b / b1.
// ---------------------------------------------------------------------------

/// In the reference domain a "para" is a leaf; everything else can hold children — the same
/// predicate `ContainedOpsTests` uses, so the contained path is exercised as a domain would.
let private canHold137 (n: RNode) = n.Kind <> "para"

/// Root id fresh, one DESCENDANT id ("a1") already in the tree.
let private descendantCollision () =
    RNode.node "z" "section" [ RNode.leaf "a1" "para" "clash" ]

/// Root id fresh, and NEITHER child id is in the tree — the subtree repeats an id within itself.
let private internallyDuplicated () =
    RNode.node "z" "section" [ RNode.leaf "dup" "para" "1"; RNode.leaf "dup" "para" "2" ]

[<Tests>]
let insertIdUniquenessTests =
    testList
        "Ops.InsertChild id uniqueness (Phase 137)"
        [ testCase "a subtree whose descendant id is already in the tree is refused by apply"
          <| fun _ ->
              match Ops.apply nodew idw (InsertChild("b", descendantCollision ())) (sample ()) with
              | Error(DuplicateId "a1") -> ()
              | other -> failtestf "expected DuplicateId \"a1\", got %A" other

          testCase "canApply gives the same envelope for the descendant collision"
          <| fun _ ->
              let op = InsertChild("b", descendantCollision ())

              Expect.equal
                  (Ops.canApply nodew idw op (sample ()))
                  (Ops.apply nodew idw op (sample ()) |> Result.map ignore)
                  "canApply ≡ apply on the descendant collision"

              match Ops.canApply nodew idw op (sample ()) with
              | Error(DuplicateId "a1") -> ()
              | other -> failtestf "expected DuplicateId \"a1\", got %A" other

          testCase "applyContained inherits the check — it shares one validateInsert"
          <| fun _ ->
              // The container predicate is applied to the PARENT, never to the inserted node, so
              // the id check is reached whatever kind the incoming subtree carries.
              let op = InsertChild("b", descendantCollision ())

              match Ops.applyContained canHold137 nodew idw op (sample ()) with
              | Error(DuplicateId "a1") -> ()
              | other -> failtestf "expected DuplicateId \"a1\", got %A" other

              Expect.equal
                  (Ops.canApplyContained canHold137 nodew idw op (sample ()))
                  (Ops.applyContained canHold137 nodew idw op (sample ()) |> Result.map ignore)
                  "canApplyContained ≡ applyContained"

          testCase "a subtree duplicated WITHIN ITSELF is refused even though no id is in the tree"
          <| fun _ ->
              // The half none of the sibling hosts covers: they seed their `existing` set from the
              // root alone, so this shape is accepted there. Core refuses it, because the invariant
              // is about the tree that RESULTS, and this one carries "dup" twice.
              let op = InsertChild("b", internallyDuplicated ())

              match Ops.apply nodew idw op (sample ()) with
              | Error(DuplicateId "dup") -> ()
              | other -> failtestf "expected DuplicateId \"dup\", got %A" other

              match Ops.canApply nodew idw op (sample ()) with
              | Error(DuplicateId "dup") -> ()
              | other -> failtestf "expected DuplicateId \"dup\" from canApply, got %A" other

              match Ops.applyContained canHold137 nodew idw op (sample ()) with
              | Error(DuplicateId "dup") -> ()
              | other -> failtestf "expected DuplicateId \"dup\" from applyContained, got %A" other

          testCase "the FIRST offender in Tree.ids order is named"
          <| fun _ ->
              // preorder over the incoming subtree is z, b1, a1 — so "b1" is named, not "a1".
              let sub =
                  RNode.node "z" "section" [ RNode.leaf "b1" "para" "x"; RNode.leaf "a1" "para" "y" ]

              match Ops.apply nodew idw (InsertChild("b", sub)) (sample ()) with
              | Error(DuplicateId "b1") -> ()
              | other -> failtestf "expected the first offender \"b1\", got %A" other

          testCase "the node's own id still outranks an unknown parent (precedence unchanged)"
          <| fun _ ->
              // The pre-137 validator checked the inserted node's own id BEFORE parent existence.
              // `Tree.ids` is preorder, so the widened scan checks that same id first and the
              // precedence is preserved rather than quietly reordered.
              match Ops.apply nodew idw (InsertChild("ghost", RNode.leaf "b1" "para" "")) (sample ()) with
              | Error(DuplicateId "b1") -> ()
              | other -> failtestf "expected DuplicateId \"b1\" ahead of UnknownNode, got %A" other

          testCase "a descendant collision also outranks an unknown parent"
          <| fun _ ->
              match Ops.apply nodew idw (InsertChild("ghost", descendantCollision ())) (sample ()) with
              | Error(DuplicateId "a1") -> ()
              | other -> failtestf "expected DuplicateId \"a1\" ahead of UnknownNode, got %A" other

          testCase "the ACCEPT path is unchanged — a genuinely fresh subtree still inserts"
          <| fun _ ->
              // The rejection surface widens; the accepted bytes do not move.
              let fresh =
                  RNode.node "z" "section" [ RNode.leaf "z1" "para" "x"; RNode.leaf "z2" "para" "y" ]

              match Ops.apply nodew idw (InsertChild("b", fresh)) (sample ()) with
              | Ok r ->
                  Expect.equal (childIds nodew "b" r) [ "b1"; "z" ] "appended under b"

                  Expect.equal
                      (Tree.ids nodew r)
                      [ "root"; "a"; "a1"; "a2"; "b"; "b1"; "z"; "z1"; "z2" ]
                      "every id occurs exactly once"
              | Error e -> failtestf "a fresh subtree must still insert, got %A" e

          testCase "Batch is all-or-nothing over the widened refusal"
          <| fun _ ->
              // The first op is legal; the second collides on a descendant. The batch aborts and
              // the original tree survives, so a partially-grafted duplicate can never be observed.
              let ops =
                  Batch
                      [ InsertChild("a", RNode.leaf "a3" "para" "ok")
                        InsertChild("b", descendantCollision ()) ]

              match Ops.apply nodew idw ops (sample ()) with
              | Error(DuplicateId "a1") -> ()
              | other -> failtestf "expected the batch to abort with DuplicateId \"a1\", got %A" other

          testCase "MoveNode is unaffected — relocation carries no new id"
          <| fun _ ->
              // A move takes an EXISTING subtree to a new parent, so its ids are already in the
              // tree by construction; the insert-side check must not make a legal move look like a
              // collision.
              match Ops.apply nodew idw (MoveNode("a1", "b")) (sample ()) with
              | Ok r -> Expect.equal (childIds nodew "b" r) [ "b1"; "a1" ] "the move still lands"
              | Error e -> failtestf "a legal move must not be refused, got %A" e

          testCase "the conformance law certifies the reference witness green (Phase 137)"
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

              let results = Conformance.opAlgebra nodew idw opGen 137 200

              let law =
                  results
                  |> List.tryFind (fun r -> r.Law = "an accepted insert introduces no id already present")

              match law with
              | None ->
                  failtestf "opAlgebra no longer reports the insert-uniqueness law: %A" (results |> List.map _.Law)
              | Some r -> Expect.isTrue r.Passed (sprintf "the law must hold on the reference witness: %A" r)

          // ---- Phase 139: Tree.WellFormed, and the validators that read it ----

          testCase "wellFormed accepts a clean tree and names the FIRST repeated id in preorder"
          <| fun _ ->
              match Tree.wellFormed nodew idw (sample ()) with
              | Tree.Structural -> ()
              | Tree.RepeatedId d -> failtestf "the reference sample is well-formed, but `%s` was reported twice" d

              // `[root; a; a1; a1]` — the offender is reported at its SECOND occurrence.
              let twice =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "a1" "para" "x" ]
                        RNode.leaf "a1" "para" "y" ]

              match Tree.wellFormed nodew idw twice with
              | Tree.RepeatedId d -> Expect.equal d "a1" "the repeated id is named"
              | Tree.Structural -> failtest "a tree carrying `a1` twice read as well-formed"

              Expect.isTrue (Tree.isWellFormed nodew idw (sample ())) "the boolean form agrees"
              Expect.isFalse (Tree.isWellFormed nodew idw twice) "and disagrees where it should"

          testCase "wellFormed names the offender at its SECOND occurrence, not by duplicate-group order"
          <| fun _ ->
              // `[root; a; b; a]` in preorder: `a` repeats last, `b` is the first id seen twice.
              // Two answers are defensible and the engine now gives ONE of them everywhere — this is
              // the case that tells them apart, and it is what Phase 139 moved `Diff`'s check onto.
              let t =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "a" "section" [ RNode.leaf "b" "para" "x"; RNode.leaf "b" "para" "y" ]
                        RNode.leaf "a" "para" "z" ]

              match Tree.wellFormed nodew idw t with
              | Tree.RepeatedId d -> Expect.equal d "b" "the first id reached twice in preorder is named"
              | Tree.Structural -> failtest "a tree carrying `a` and `b` twice read as well-formed"

              match Diff.toOps nodew idw t (sample ()) with
              | Error(Diff.DuplicateIdInTree d) ->
                  Expect.equal d "b" "Diff names the same offender the accept path would — one definition, one answer"
              | other -> failtestf "the diff should refuse a malformed `before` tree: %A" other

          testCase "graftWellFormed answers for the tree a graft WOULD produce, and Ops.apply reads it"
          <| fun _ ->
              let t = sample ()
              let clean = RNode.node "fresh" "section" [ RNode.leaf "fresh-a" "para" "x" ]
              let collides = RNode.node "fresh" "section" [ RNode.leaf "a1" "para" "x" ]

              let internalDup =
                  RNode.node "fresh" "section" [ RNode.leaf "twin" "para" "x"; RNode.leaf "twin" "para" "y" ]

              match Tree.graftWellFormed nodew idw clean t with
              | Tree.Structural -> ()
              | Tree.RepeatedId d -> failtestf "a graft of entirely fresh ids was refused, naming `%s`" d

              match Tree.graftWellFormed nodew idw collides t with
              | Tree.RepeatedId d -> Expect.equal d "a1" "the descendant id the tree already holds is named"
              | Tree.Structural -> failtest "a graft carrying an id the tree holds read as clean"

              match Tree.graftWellFormed nodew idw internalDup t with
              | Tree.RepeatedId d -> Expect.equal d "twin" "the id the graft repeats within itself is named"
              | Tree.Structural -> failtest "a graft repeating an id within itself read as clean"

              // and the validator is a projection of it rather than a second copy: the same three
              // grafts, through the shipped accept path.
              let verdict graft =
                  match Ops.apply nodew idw (InsertChild("b", graft)) t with
                  | Ok _ -> "accept"
                  | Error(DuplicateId d) -> "duplicate:" + d
                  | Error e -> sprintf "%A" e

              Expect.equal (verdict clean) "accept" "the clean graft is accepted"
              Expect.equal (verdict collides) "duplicate:a1" "the colliding graft is refused, naming the same id"
              Expect.equal (verdict internalDup) "duplicate:twin" "so is the internally duplicated one"

          testCase "opAlgebra reports the WellFormed-preservation law, and it holds on the reference witness"
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

              let results = Conformance.opAlgebra nodew idw opGen 139 200

              match
                  results
                  |> List.tryFind (fun r -> r.Law = "apply's accept path preserves Tree.WellFormed")
              with
              | None -> failtestf "opAlgebra no longer reports the preservation law: %A" (results |> List.map _.Law)
              | Some r -> Expect.isTrue r.Passed (sprintf "the law must hold on the reference witness: %A" r)

              Expect.equal (List.length results) 5 "the algebra family reports five laws" ]
