module Fuaran.Core.Tests.ArbitrationTests

// Phase 85 — AiSurface.arbitrate: the deterministic, total partition of N op-script
// proposals against one base tree. Concrete all-independent / all-conflicting / mixed /
// permutation-invariance cases over the reference witness, plus the generative
// arbitrationLaws (determinism / partition / independence / actionability / confluence).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// A Phase-59-shaped proposal with a queue-assigned id (unique per test).
let private prop id ops : Proposals.Proposal<SkeletonOp<RNode, string>> =
    { Id = id
      Author = sprintf "agent-%d" id
      ProposedAt = "2026-07-10T00:00:00Z"
      Intent = None
      Ops = ops
      Status = Proposals.Pending }

let private acceptedIds (r: Arbitration<RNode, string>) = r.Accepted |> List.map (fun p -> p.Id)

let private rejectedWith (r: Arbitration<RNode, string>) =
    r.Rejected |> List.map (fun (p, reason) -> p.Id, reason)

[<Tests>]
let arbitrateTests =
    testList
        "AiSurface.arbitrate"
        [ testCase "all-independent proposals are all accepted, in pinned (ascending-id) order"
          <| fun _ ->
              // sample(): root[a[a1,a2], b[b1]] — inserts under different parents commute.
              let tree = sample ()
              let p1 = prop 1 [ InsertChild("a", RNode.leaf "x" "para" "v") ]
              let p2 = prop 2 [ InsertChild("b", RNode.leaf "y" "para" "w") ]

              let r = AiSurface.arbitrate nodew idw tree [ p2; p1 ] // input order ≠ pinned order

              Expect.equal (acceptedIds r) [ 1; 2 ] "both accepted, ascending id"
              Expect.isEmpty r.Rejected "nothing rejected"
              Expect.equal r.MergedScript (p1.Ops @ p2.Ops) "merged script composes in pinned order"

              match Ops.applyAll nodew idw r.MergedScript tree with
              | Ok _ -> ()
              | Error e -> failtestf "the merged script must apply green: %A" e

          testCase "all-conflicting proposals: the lowest id wins, the rest name it (Conflicts)"
          <| fun _ ->
              // three inserts under ONE parent — the pinned same-parent rule makes them pairwise
              // dependent, so greedy accepts only the first in pinned order.
              let tree = sample ()
              let p1 = prop 1 [ InsertChild("a", RNode.leaf "x" "para" "1") ]
              let p2 = prop 2 [ InsertChild("a", RNode.leaf "y" "para" "2") ]
              let p3 = prop 3 [ InsertChild("a", RNode.leaf "z" "para" "3") ]

              let r = AiSurface.arbitrate nodew idw tree [ p3; p1; p2 ]

              Expect.equal (acceptedIds r) [ 1 ] "only the lowest-id proposal is accepted"

              Expect.equal
                  (rejectedWith r)
                  [ 2, Conflicts [ 1 ]; 3, Conflicts [ 1 ] ]
                  "each reject cites the accepted interferer (never the other reject)"

          testCase "an inapplicable proposal carries the op-algebra's own rejection envelope"
          <| fun _ ->
              let tree = sample ()
              let bad = [ InsertChild("zz", RNode.leaf "x" "para" "v") ] // unknown parent
              let p1 = prop 1 bad

              let r = AiSurface.arbitrate nodew idw tree [ p1 ]

              match r.Rejected with
              | [ (p, Inapplicable(ix, rej)) ] ->
                  Expect.equal p.Id 1 "the proposal is the rejected one"
                  Expect.equal (Ops.canApplyAll nodew idw bad tree) (Error(ix, rej)) "exactly the canApplyAll envelope"

                  match rej with
                  | UnknownNode("zz", _) -> ()
                  | other -> failtestf "expected UnknownNode 'zz', got %A" other
              | other -> failtestf "expected one Inapplicable rejection, got %A" other

          testCase "mixed: accepted / inapplicable / conflicting each land in the right bucket"
          <| fun _ ->
              let tree = sample ()
              let p1 = prop 1 [ InsertChild("a", RNode.leaf "x" "para" "1") ] // accepted
              let p2 = prop 2 [ InsertChild("zz", RNode.leaf "y" "para" "2") ] // inapplicable
              let p3 = prop 3 [ RemoveNode "b1" ] // remove ⇒ unknown-parent ⇒ conflicts with any structural write
              let p4 = prop 4 [ InsertChild("b", RNode.leaf "w" "para" "4") ] // independent of p1 — accepted

              let r = AiSurface.arbitrate nodew idw tree [ p4; p3; p2; p1 ]

              Expect.equal (acceptedIds r) [ 1; 4 ] "the two independent applicable proposals are accepted"

              match rejectedWith r with
              | [ (2, Inapplicable(0, UnknownNode("zz", _))); (3, Conflicts interfering) ] ->
                  Expect.equal interfering [ 1; 4 ] "the conflict cites the FULL accepted interferer set"
              | other -> failtestf "unexpected rejected shape: %A" other

          testCase "Conflicts citations are recomputed against the full accepted set (later acceptances included)"
          <| fun _ ->
              // p2 (a remove — unknown-parent) is decided between p1 and p3 in pinned order, but
              // its citation must include p3, accepted after it.
              let tree = sample ()
              let p1 = prop 1 [ InsertChild("a", RNode.leaf "x" "para" "1") ]
              let p2 = prop 2 [ RemoveNode "b1" ]
              let p3 = prop 3 [ InsertChild("b", RNode.leaf "y" "para" "3") ]

              let r = AiSurface.arbitrate nodew idw tree [ p1; p2; p3 ]

              Expect.equal (acceptedIds r) [ 1; 3 ] "p1 + p3 accepted"
              Expect.equal (rejectedWith r) [ 2, Conflicts [ 1; 3 ] ] "p2 cites BOTH accepted interferers"

          testCase "the outcome is invariant under permutation of the input list"
          <| fun _ ->
              let tree = sample ()
              let p1 = prop 1 [ InsertChild("a", RNode.leaf "x" "para" "1") ]
              let p2 = prop 2 [ InsertChild("a", RNode.leaf "y" "para" "2") ]
              let p3 = prop 3 [ InsertChild("b", RNode.leaf "z" "para" "3") ]

              let reference = AiSurface.arbitrate nodew idw tree [ p1; p2; p3 ]

              for input in [ [ p3; p2; p1 ]; [ p2; p3; p1 ]; [ p3; p1; p2 ] ] do
                  Expect.equal (AiSurface.arbitrate nodew idw tree input) reference "same partition, any input order"

          testCase "the accepted scripts apply confluently in either order (same content hash)"
          <| fun _ ->
              let tree = sample ()
              let p1 = prop 1 [ InsertChild("a", RNode.leaf "x" "para" "1") ]
              let p2 = prop 2 [ InsertChild("b", RNode.leaf "y" "para" "2") ]
              let r = AiSurface.arbitrate nodew idw tree [ p1; p2 ]
              Expect.equal (acceptedIds r) [ 1; 2 ] "both accepted"

              let hashOf = Tree.encodeHash nodew encNode

              let apply2 first second =
                  Ops.applyAll nodew idw first tree
                  |> Result.bind (Ops.applyAll nodew idw second)
                  |> Result.map hashOf

              Expect.equal (apply2 p1.Ops p2.Ops) (apply2 p2.Ops p1.Ops) "any order, one tree"

          testCase "arbitrate is total on degenerate input (empty list, empty scripts)"
          <| fun _ ->
              let tree = sample ()
              let empty = AiSurface.arbitrate nodew idw tree []
              Expect.isEmpty empty.Accepted "no proposals, nothing accepted"
              Expect.isEmpty empty.Rejected "no proposals, nothing rejected"
              Expect.isEmpty empty.MergedScript "no proposals, empty merged script"

              // an empty script applies vacuously and is independent of everything.
              let r = AiSurface.arbitrate nodew idw tree [ prop 1 [] ]
              Expect.equal (acceptedIds r) [ 1 ] "the empty script is accepted"
              Expect.isEmpty r.MergedScript "and contributes no ops" ]

// ---- the generative arbitrationLaws ----

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
let arbitrationLawTests =
    testList
        "Conformance.arbitrationLaws"
        [ testCase "the reference witness certifies the arbitration laws green"
          <| fun _ ->
              let results = Conformance.arbitrationLaws nodew idw opGen encNode 8585 300
              Expect.equal (List.length results) 5 "determinism/partition/independence/actionability/confluence"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "arbitrationLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism of the kit itself
              Expect.equal
                  (Conformance.arbitrationLaws nodew idw opGen encNode 8585 300)
                  results
                  "same seed ⇒ identical report" ]
