module Fuaran.Core.Tests.MergeTests

// Phase 64 — Dag.conflicts: the three conflict shapes (concurrent-update / insert-position clash /
// move-vs-remove) + the disjoint-delta no-false-positive case, plus the generative mergeConflictLaws.
// Phase 83 — Dag.reconcile: the clean fold, the conflicted (inert) path, order-equivalence, plus the
// generative reconcileLaws.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// The caller-injected address projection: a single op's footprint over the reference witnesses.
let private fp (op: SkeletonOp<RNode, string>) = Ops.footprint nodew idw [ op ]

// ---- Phase 64: Dag.conflicts by shape ----

[<Tests>]
let conflictShapeTests =
    testList
        "Dag.conflicts"
        [ testCase "concurrent-update: one branch authors a node the other destroys (content-vs-content)"
          <| fun _ ->
              let a = [ InsertChild("p", 0, RNode.leaf "z" "para" "v") ]
              let b = [ RemoveNode "z" ]

              match Dag.conflicts fp a b with
              | [ c ] ->
                  Expect.equal c.Shape ConcurrentUpdate "both touch z's content lifecycle"
                  Expect.equal c.Address "z" "the shared address is z"
                  Expect.equal c.Left (InsertChild("p", 0, RNode.leaf "z" "para" "v")) "Left is delta-A's op"
                  Expect.equal c.Right (RemoveNode "z") "Right is delta-B's op"
              | other -> failtestf "expected a single ConcurrentUpdate on z, got %A" other

          testCase "insert-position clash: two inserts under the SAME named parent"
          <| fun _ ->
              let a = [ InsertChild("p", 0, RNode.leaf "x" "para" "v") ]
              let b = [ InsertChild("p", 1, RNode.leaf "y" "para" "w") ]

              match Dag.conflicts fp a b with
              | [ c ] ->
                  Expect.equal c.Shape InsertPositionClash "both shift p's siblings"
                  Expect.equal c.Address "p" "the shared parent p"
              | other -> failtestf "expected a single InsertPositionClash on p, got %A" other

          testCase "move-vs-remove: a remove races the other branch's structural write (conservative)"
          <| fun _ ->
              // disjoint by NAMED ids, but a remove's source parent is a tree fact the script cannot
              // name — so it conservatively conflicts with b's insert (the pinned #78 over-approximation).
              let a = [ RemoveNode "a1" ]
              let b = [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]

              match Dag.conflicts fp a b with
              | [ c ] ->
                  Expect.equal c.Shape MoveVsRemove "the remove of a1 races b's structural write"
                  Expect.equal c.Address "a1" "keyed by the removed node"
              | other -> failtestf "expected a single MoveVsRemove on a1, got %A" other

          testCase "disjoint deltas return [] — no false positives"
          <| fun _ ->
              // inserts under DIFFERENT parents, disjoint new ids, no removes ⇒ footprint-independent.
              let a = [ InsertChild("a", 0, RNode.leaf "x" "para" "v") ]
              let b = [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]
              Expect.isTrue (Ops.independent (fp a.Head) (fp b.Head)) "the pair is footprint-independent"
              Expect.isEmpty (Dag.conflicts fp a b) "independent deltas ⇒ no conflicts"

          testCase "the empty delta conflicts with nothing"
          <| fun _ ->
              Expect.isEmpty (Dag.conflicts fp [] [ RemoveNode "a" ]) "empty delta A ⇒ []"
              Expect.isEmpty (Dag.conflicts fp [ RemoveNode "a" ] []) "empty delta B ⇒ []"

          testCase "firing is exactly the negation of Ops.independent (per pair)"
          <| fun _ ->
              // a small matrix: for every pair, `reported` must equal `not (Ops.independent …)`.
              let ops =
                  [ InsertChild("p", 0, RNode.leaf "x" "para" "v")
                    InsertChild("p", 1, RNode.leaf "y" "para" "w")
                    RemoveNode "a1"
                    MoveNode("a1", "b", 0)
                    ReorderChildren("p", [ "b"; "a" ]) ]

              for oa in ops do
                  for ob in ops do
                      let reported = Dag.conflicts fp [ oa ] [ ob ] |> List.isEmpty |> not
                      let dependent = not (Ops.independent (fp oa) (fp ob))
                      Expect.equal reported dependent (sprintf "reported≡dependent for (%A, %A)" oa ob)

          testCase "conflicts is symmetric up to Left/Right swap"
          <| fun _ ->
              let a = [ InsertChild("p", 0, RNode.leaf "z" "para" "v"); RemoveNode "a1" ]
              let b = [ RemoveNode "z"; InsertChild("b", 0, RNode.leaf "y" "para" "w") ]

              let fwd =
                  Dag.conflicts fp a b |> List.map (fun c -> c.Shape, c.Address, c.Left, c.Right)

              let bwd =
                  Dag.conflicts fp b a |> List.map (fun c -> c.Shape, c.Address, c.Right, c.Left)

              Expect.equal (List.length fwd) (List.length bwd) "same number of conflicts either way"
              Expect.isTrue (fwd |> List.forall (fun x -> List.contains x bwd)) "same collisions up to swap" ]

// ---- Phase 83: Dag.reconcile ----

/// A minimal StreamWitness over the reference skeleton ops, so reconcile can run over a real DAG.
/// Encode is a structural fingerprint (append needs it for the content id); Apply/Decode are unused
/// by reconcile/betweenOps but supplied for completeness.
let private sw: StreamWitness<SkeletonOp<RNode, string>, RNode, Rejection<string>> =
    let rec encOp (op: SkeletonOp<RNode, string>) : string =
        match op with
        | InsertChild(p, ix, node) ->
            "I|"
            + p
            + "|"
            + string ix
            + "|"
            + (Tree.preorder nodew node |> List.map encNode |> String.concat ",")
        | RemoveNode t -> "R|" + t
        | MoveNode(t, np, ix) -> "M|" + t + "|" + np + "|" + string ix
        | ReorderChildren(p, order) -> "O|" + p + "|" + String.concat "," order
        | Batch inner -> "B|" + (inner |> List.map encOp |> String.concat ";")

    { Apply = fun op st -> Ops.apply nodew idw op st
      Encode = encOp
      Decode = fun _ -> Error "unused" }

let private h = OpStream.defaultHash

/// Build a fork DAG: genesis base, branch A (script a), branch B (script b, forked off base).
let private forkDag (a: SkeletonOp<RNode, string> list) (b: SkeletonOp<RNode, string> list) =
    let chain ops parent d0 =
        let mutable head = parent
        let mutable d = d0

        for op in ops do
            let id, d' = Dag.append h sw (Human "x") op head d
            head <- id
            d <- d'

        head, d

    let baseId, d1 = Dag.append h sw (Human "x") (RemoveNode "root") "" Dag.empty
    let headA, d2 = chain a baseId d1
    let headB, dag = chain b baseId d2
    baseId, headA, headB, dag

[<Tests>]
let reconcileTests =
    testList
        "Dag.reconcile"
        [ testCase "a conflict-free fork reconciles to delta A ++ delta B (pinned order)"
          <| fun _ ->
              let a = [ InsertChild("a", 0, RNode.leaf "x" "para" "v") ]
              let b = [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]
              let baseId, headA, headB, dag = forkDag a b

              match Dag.reconcile fp dag baseId headA headB with
              | Ok script -> Expect.equal script (a @ b) "the merge script is delta A followed by delta B"
              | Error cs -> failtestf "expected a clean Ok merge, got Error %A" cs

          testCase "the clean fold replays order-independently on the base tree (content hash)"
          <| fun _ ->
              // root[a[a1,a2], b[b1]] — insert under a (branch A) vs insert under b (branch B) commute.
              let baseTree = sample ()
              let a = [ InsertChild("a", 0, RNode.leaf "x" "para" "v") ]
              let b = [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]
              let baseId, headA, headB, dag = forkDag a b

              match Dag.reconcile fp dag baseId headA headB with
              | Ok script ->
                  let hashOf = Tree.encodeHash nodew encNode
                  let applyAll ops t = Ops.applyAll nodew idw ops t
                  let viaScript = applyAll script baseTree |> Result.map hashOf
                  let ab = applyAll a baseTree |> Result.bind (applyAll b) |> Result.map hashOf
                  let ba = applyAll b baseTree |> Result.bind (applyAll a) |> Result.map hashOf
                  Expect.equal viaScript ab "merge script ≡ A-then-B"
                  Expect.equal ab ba "A-then-B ≡ B-then-A (order-independent fold)"
              | Error cs -> failtestf "expected a clean Ok merge, got Error %A" cs

          testCase "a genuine conflict yields Phase 64's typed report, nothing applied"
          <| fun _ ->
              // branch A authors z under p; branch B removes z — a content-vs-content conflict.
              let a = [ InsertChild("p", 0, RNode.leaf "z" "para" "v") ]
              let b = [ RemoveNode "z" ]
              let baseId, headA, headB, dag = forkDag a b

              match Dag.reconcile fp dag baseId headA headB with
              | Error cs ->
                  Expect.equal cs (Dag.conflicts fp a b) "the Error carries Dag.conflicts' report verbatim"
                  Expect.isTrue (cs |> List.exists (fun c -> c.Shape = ConcurrentUpdate)) "a concurrent-update conflict"
              | Ok script -> failtestf "expected an Error conflict report, got Ok %A" script

          testCase "reconcile is a pure function of (base, headA, headB)"
          <| fun _ ->
              let a = [ RemoveNode "a1" ]
              let b = [ InsertChild("b", 0, RNode.leaf "y" "para" "w") ]
              let baseId, headA, headB, dag = forkDag a b
              let r1 = Dag.reconcile fp dag baseId headA headB
              let r2 = Dag.reconcile fp dag baseId headA headB
              Expect.equal r1 r2 "same inputs ⇒ identical result" ]

// ---- generative laws: mergeConflictLaws (Phase 64) + reconcileLaws (Phase 83) ----

let private genTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let id = sprintf "n%d" counter
        counter <- counter + 1
        id

    let rec build depth =
        let id = freshId ()

        if depth <= 0 then
            RNode.leaf id "para" "v"
        else
            let nKids, r' = ConfRng.intBelow 3 r
            r <- r'
            RNode.node id "section" [ for _ in 1..nKids -> build (depth - 1) ]

    let t = build 2
    t, r

let private genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    RNode.leaf (pick ()) "para" "x", r

let private opGen: OpGen<RNode, string> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = None }

[<Tests>]
let mergeLawTests =
    testList
        "Conformance merge/reconcile laws"
        [ testCase "mergeConflictLaws certify symmetric + deterministic + #78-agreement green"
          <| fun _ ->
              let results = Conformance.mergeConflictLaws nodew idw opGen 7171 300
              Expect.equal (List.length results) 3 "symmetry + determinism + agreement laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "mergeConflictLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal
                  (Conformance.mergeConflictLaws nodew idw opGen 7171 300)
                  results
                  "same seed ⇒ identical report"

          testCase "reconcileLaws certify clean-fold + cross-validation + inert-conflict + determinism green"
          <| fun _ ->
              let results = Conformance.reconcileLaws nodew idw opGen encNode 5353 300
              Expect.equal (List.length results) 4 "clean + cross + conflicted + determinism laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "reconcileLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal
                  (Conformance.reconcileLaws nodew idw opGen encNode 5353 300)
                  results
                  "same seed ⇒ identical report" ]
