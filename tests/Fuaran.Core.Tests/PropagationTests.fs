module Fuaran.Core.Tests.PropagationTests

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// A node declares a single reference via its `Value` field (empty ⇒ reads nothing) — the test's
// stand-in for a domain's declared bindings.
let private readsOf (n: RNode) : string seq =
    if n.Value = "" then Seq.empty else Seq.singleton n.Value

// r ─┬ a (reads b)
//    ├ b (reads c)
//    └ c (reads nothing)
let private tree () =
    RNode.node
        "r"
        "section"
        [ RNode.leaf "a" "para" "b"
          RNode.leaf "b" "para" "c"
          RNode.leaf "c" "para" "" ]

[<Tests>]
let tests =
    testList
        "Propagation"
        [ testCase "dependencyMap folds readsOf over the tree into an id → reads map"
          <| fun _ ->
              let deps = Propagation.dependencyMap nodew idw readsOf (tree ())

              Expect.equal
                  deps
                  (Map.ofList
                      [ "r", Set.empty
                        "a", Set.singleton "b"
                        "b", Set.singleton "c"
                        "c", Set.empty ])
                  "every node mapped to its declared reads"

          testCase "dirtyFromChangedIds is the transitive-dependents closure"
          <| fun _ ->
              let deps = Propagation.dependencyMap nodew idw readsOf (tree ())
              // c changed → b reads c → a reads b : whole chain dirty
              Expect.equal
                  (Propagation.dirtyFromChangedIds deps (Set.singleton "c"))
                  (Set.ofList [ "a"; "b"; "c" ])
                  "changing a leaf dirties its transitive dependents"
              // nothing reads a → only a is dirty (minimality)
              Expect.equal
                  (Propagation.dirtyFromChangedIds deps (Set.singleton "a"))
                  (Set.singleton "a")
                  "changing a node with no dependents dirties only itself"
              // r is read by nobody and reads nobody
              Expect.equal
                  (Propagation.dirtyFromChangedIds deps (Set.singleton "r"))
                  (Set.singleton "r")
                  "isolated node"

          testCase "touchedBy maps each SkeletonOp to its container ids"
          <| fun _ ->
              let root = tree ()
              let tb op = Propagation.touchedBy nodew idw root op

              Expect.equal
                  (tb (InsertChild("r", RNode.leaf "z" "para" "")))
                  (Set.ofList [ "r"; "z" ])
                  "insert touches parent + inserted subtree"

              // Phase 308 — a removal touches the parent it removed from, a move the parent it left:
              // both containers' child lists changed, as an insert's and a reorder's do.
              Expect.equal
                  (tb (RemoveNode "b"))
                  (Set.ofList [ "b"; "r" ])
                  "remove touches the removed subtree + its parent"

              Expect.equal (tb (MoveNode("a", "r"))) (Set.ofList [ "a"; "r" ]) "move touches target + new parent"

              Expect.equal
                  (tb (ReorderChildren("r", [ "c"; "b"; "a" ])))
                  (Set.singleton "r")
                  "reorder touches the parent"

              Expect.equal
                  (tb (Batch [ RemoveNode "b"; ReorderChildren("r", []) ]))
                  (Set.ofList [ "b"; "r" ])
                  "batch unions its sub-ops"

          testCase "touchedBy removes a whole subtree, not just its root"
          <| fun _ ->
              // a subtree under a container: r ─ s ─ [s1, s2]
              let root =
                  RNode.node
                      "r"
                      "section"
                      [ RNode.node "s" "section" [ RNode.leaf "s1" "para" ""; RNode.leaf "s2" "para" "" ] ]

              Expect.equal
                  (Propagation.touchedBy nodew idw root (RemoveNode "s"))
                  (Set.ofList [ "r"; "s"; "s1"; "s2" ])
                  "removing a container touches every id in its subtree, and the parent it left"

          testCase "dirtyFromOp removing a referenced node dirties its dangling dependents"
          <| fun _ ->
              // removing b (which a reads) makes a dirty (its binding now dangles)
              Expect.equal
                  (Propagation.dirtyFromOp nodew idw (tree ()) readsOf (RemoveNode "b"))
                  (Set.ofList [ "a"; "b"; "r" ])
                  "removed node + its parent + its dependents"

          testCase "sort enumerates a reference cycle as data, never diverges"
          <| fun _ ->
              // p ↔ q cycle + a linear tail t reading p
              let deps =
                  Map.ofList [ "p", Set.singleton "q"; "q", Set.singleton "p"; "t", Set.singleton "p" ]

              let result = Propagation.sort deps

              Expect.isTrue
                  (result.Cycles |> List.exists (fun g -> Set.ofList g = Set.ofList [ "p"; "q" ]))
                  "cycle enumerated"

              Expect.equal (result.Order) [ "t" ] "only the acyclic node is ordered"

              match Propagation.cycleThrough "q" deps with
              | Some g -> Expect.equal (Set.ofList g) (Set.ofList [ "p"; "q" ]) "cycleThrough returns the cycle group"
              | None -> failtest "expected a cycle through q"

          testCase "dirtyPropagationLaws certify sound+minimal dirty set + byte-identity + cycle-as-data (Phase 68)"
          <| fun _ ->
              let results = Conformance.dirtyPropagationLaws 4242 200
              Expect.equal (List.length results) 5 "four laws + the Phase 121 dirty-frontier adequacy guard reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "dirtyPropagationLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.dirtyPropagationLaws 4242 200) results "same seed ⇒ identical report"

          // ---- Phase 69: the tree-level incremental recompute driver ----

          testCase "eval evaluates every acyclic node in dependency order (Phase 69)"
          <| fun _ ->
              let deps =
                  Map.ofList [ "s", Set.empty; "a", Set.singleton "s"; "b", Set.singleton "a" ]

              let baseOf = Map.ofList [ "s", 10; "a", 1; "b", 2 ]

              let evalNode (resolve: string -> int option) id =
                  Ok(
                      Map.find id baseOf
                      + (Map.find id deps
                         |> Set.fold (fun acc r -> acc + (resolve r |> Option.defaultValue 0)) 0)
                  )

              match Propagation.eval evalNode deps with
              | Ok outcome ->
                  Expect.equal
                      outcome.Values
                      (Map.ofList [ "s", 10; "a", 11; "b", 13 ])
                      "chain evaluated in dependency order"

                  Expect.isEmpty outcome.Cyclic "no cycles"
              | Error e -> failtestf "eval errored: %A" e

          testCase "evalFrom reuses clean branches; recomputes only the dirty subgraph (Phase 69)"
          <| fun _ ->
              // s1 → a, s2 → b : changing s1 leaves the s2/b branch clean
              let deps =
                  Map.ofList
                      [ "s1", Set.empty
                        "s2", Set.empty
                        "a", Set.singleton "s1"
                        "b", Set.singleton "s2" ]

              let evalWith (bs: Map<string, int>) (recorder: ResizeArray<string>) =
                  fun (resolve: string -> int option) id ->
                      recorder.Add id

                      Ok(
                          Map.find id bs
                          + (Map.find id deps
                             |> Set.fold (fun acc r -> acc + (resolve r |> Option.defaultValue 0)) 0)
                      )

              let base0 = Map.ofList [ "s1", 10; "s2", 20; "a", 0; "b", 0 ]

              match Propagation.eval (evalWith base0 (ResizeArray())) deps with
              | Ok prior ->
                  Expect.equal prior.Values (Map.ofList [ "s1", 10; "s2", 20; "a", 10; "b", 20 ]) "full eval"
                  let base1 = Map.add "s1" 100 base0
                  let invoked = ResizeArray()

                  match Propagation.evalFrom (evalWith base1 invoked) prior.Values (Set.singleton "s1") deps with
                  | Ok outcome ->
                      Expect.equal
                          outcome.Values
                          (Map.ofList [ "s1", 100; "s2", 20; "a", 100; "b", 20 ])
                          "byte-identical to a full eval over the changed input"

                      Expect.equal (Set.ofSeq invoked) (Set.ofList [ "s1"; "a" ]) "only the dirty branch re-evaluated"
                  | Error e -> failtestf "evalFrom errored: %A" e
              | Error e -> failtestf "eval errored: %A" e

          testCase "eval returns cyclic SCCs as data + evaluates the acyclic part (Phase 69)"
          <| fun _ ->
              // p ↔ q cycle; t acyclic
              let deps =
                  Map.ofList [ "p", Set.singleton "q"; "q", Set.singleton "p"; "t", Set.empty ]

              let baseOf = Map.ofList [ "p", 1; "q", 2; "t", 3 ]
              let evalNode (_: string -> int option) id = Ok(Map.find id baseOf)

              match Propagation.eval evalNode deps with
              | Ok outcome ->
                  Expect.isTrue
                      (outcome.Cyclic |> List.exists (fun g -> Set.ofList g = Set.ofList [ "p"; "q" ]))
                      "cyclic SCC surfaced as data"

                  Expect.equal (Map.tryFind "t" outcome.Values) (Some 3) "acyclic node evaluated"
                  Expect.isFalse (Map.containsKey "p" outcome.Values) "cyclic node not evaluated"
              | Error e -> failtestf "eval errored: %A" e

          testCase "evalFrom names an out-of-graph change (EvalUnknownChange, Phase 69)"
          <| fun _ ->
              let deps = Map.ofList [ "a", Set.empty ]
              let evalNode (_: string -> int option) id = Ok(if id = "a" then 1 else 0)

              match Propagation.evalFrom evalNode Map.empty (Set.singleton "ghost") deps with
              | Error(Propagation.PropagationError.EvalUnknownChange [ "ghost" ]) -> ()
              | other -> failtestf "expected EvalUnknownChange [ghost], got %A" other

          testCase
              "propagationEvalLaws certify byte-identity + minimality + unknown-change + the declared-reads refusal (Phases 69, 209)"
          <| fun _ ->
              let results = Conformance.propagationEvalLaws 4242 200

              Expect.equal
                  (List.length results)
                  6
                  "four laws + the Phase 121 node-reuse guard + the Phase 209 undeclared-read guard reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "propagationEvalLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.propagationEvalLaws 4242 200) results "same seed ⇒ identical report" ]

// ---------------------------------------------------------------------------
//  Phase 308 — the change set of a structural edit, derived from the diff of the two trees. Each
//  probe was a stale value before: a count-of-children node kept its old count after a move out
//  of it or a removal from it, and a reader of a node a Batch moved and then removed kept its old
//  value, because the Batch was resolved against the pre-edit tree.
// ---------------------------------------------------------------------------

/// A node's value: its child count, or — when it declares a read — one more than what it reads, and
/// 101 when the read answers nothing. A function of the node's own content, its child ids and its
/// resolved declared reads: the class `changedForOp` is complete for.
let private countEval (t: RNode) (resolve: string -> int option) (id: string) : Result<int, string> =
    match Tree.tryFind nodew idw id t with
    | None -> Error("no node " + id)
    | Some n ->
        if n.Value = "" then
            Ok(List.length n.Children)
        else
            match resolve n.Value with
            | Some v -> Ok(v + 1)
            | None -> Ok 101

/// The edit applied, its change set, and the incremental re-evaluation from the pre-edit values held
/// against the full evaluation of the post-edit tree.
let private replay (pre: RNode) (op: SkeletonOp<RNode, string>) =
    let post =
        match Ops.apply nodew idw op pre with
        | Ok t -> t
        | Error e -> failtestf "the probe's op was refused: %A" e

    let deps0 = Propagation.dependencyMap nodew idw readsOf pre
    let deps1 = Propagation.dependencyMap nodew idw readsOf post

    let prior =
        match Propagation.eval (countEval pre) deps0 with
        | Ok o -> o.Values |> Map.filter (fun k _ -> Map.containsKey k deps1)
        | Error e -> failtestf "the pre-edit tree did not evaluate: %A" e

    let changed = Propagation.changedForOp nodew idw readsOf pre post op
    let incremental = Propagation.evalFrom (countEval post) prior changed deps1
    let full = Propagation.eval (countEval post) deps1
    changed, incremental, full

// root ─┬ p1 ─ x ─ y
//       ├ p2
//       └ r (reads y)
let private probeTree () =
    RNode.node
        "root"
        "section"
        [ RNode.node "p1" "section" [ RNode.node "x" "section" [ RNode.leaf "y" "para" "" ] ]
          RNode.node "p2" "section" []
          RNode.leaf "r" "para" "y" ]

[<Tests>]
let changeSetTests =
    testList
        "Propagation.changedForOp (Phase 308)"
        [ testCase "a move names the parent it LEFT, so a count of its children is not stale"
          <| fun _ ->
              let changed, incremental, full = replay (probeTree ()) (MoveNode("x", "p2"))
              Expect.isTrue (Set.contains "p1" changed) (sprintf "p1 lost a child; changed = %A" changed)
              Expect.isTrue (Set.contains "p2" changed) "p2 gained one"
              Expect.equal incremental full "evalFrom over the change set agrees with eval"

          testCase "a removal names the parent it removed from"
          <| fun _ ->
              let changed, incremental, full = replay (probeTree ()) (RemoveNode "x")
              Expect.isTrue (Set.contains "p1" changed) (sprintf "p1 lost a child; changed = %A" changed)
              Expect.isTrue (Set.contains "r" changed) "r read y, which went with x"
              Expect.equal incremental full "evalFrom over the change set agrees with eval"

          testCase "a Batch is accounted for by its end tree: a node moved then removed reaches its readers"
          <| fun _ ->
              let op = Batch [ MoveNode("x", "p2"); RemoveNode "p2" ]
              let changed, incremental, full = replay (probeTree ()) op
              Expect.isTrue (Set.contains "r" changed) (sprintf "r read y, removed with p2; changed = %A" changed)
              Expect.equal incremental full "evalFrom over the change set agrees with eval"

              match full with
              | Ok o -> Expect.equal (Map.tryFind "r" o.Values) (Some 101) "r's read answers nothing now"
              | Error e -> failtestf "%A" e

          testCase "touchedBy threads a Batch through its intermediate trees"
          <| fun _ ->
              let touched =
                  Propagation.touchedBy nodew idw (probeTree ()) (Batch [ MoveNode("x", "p2"); RemoveNode "p2" ])

              Expect.equal
                  touched
                  (Set.ofList [ "p1"; "p2"; "root"; "x"; "y" ])
                  "the move's old and new parent, and the removal's whole subtree — x and y with it — and its parent"

          testCase "a node whose reads are derived from its subtree is named when something below it moved"
          <| fun _ ->
              // A `sum` node reads every id below it — a structural formula. An insert two levels
              // down changes what it reads although neither its content nor its child ids moved, and
              // touchedBy names only the grown container and the inserted leaf; the diff names it.
              let subtreeReads (n: RNode) : string seq =
                  if n.Kind = "sum" then
                      Tree.ids nodew n |> List.tail |> Seq.ofList
                  else
                      Seq.empty

              let pre =
                  RNode.node "root" "section" [ RNode.node "s" "sum" [ RNode.node "c" "section" [] ] ]

              let op = InsertChild("c", RNode.leaf "z" "para" "")

              match Ops.apply nodew idw op pre with
              | Error e -> failtestf "%A" e
              | Ok post ->
                  Expect.isFalse
                      (Set.contains "s" (Propagation.touchedBy nodew idw pre op))
                      "touchedBy does not reach s"

                  Expect.isTrue
                      (Set.contains "s" (Propagation.changedForOp nodew idw subtreeReads pre post op))
                      "the change set names s, whose declared reads moved"

          testCase "the change set never shrinks below the pre-edit dirty closure of touchedBy"
          <| fun _ ->
              let pre = probeTree ()

              for op in
                  [ MoveNode("x", "p2")
                    RemoveNode "x"
                    ReorderChildren("root", [ "r"; "p2"; "p1" ])
                    UpdateNode(RNode.leaf "r" "para" "p2")
                    InsertChild("p2", RNode.leaf "z" "para" "y") ] do
                  match Ops.apply nodew idw op pre with
                  | Error e -> failtestf "%A refused: %A" op e
                  | Ok post ->
                      let survivors = Tree.ids nodew post |> Set.ofList
                      let old = Set.intersect (Propagation.dirtyFromOp nodew idw pre readsOf op) survivors
                      let now = Propagation.changedForOp nodew idw readsOf pre post op
                      Expect.isTrue (Set.isSubset old now) (sprintf "%A: %A is not within %A" op old now) ]

[<Tests>]
let topoCertificateTests =
    testList
        "Propagation.validTopo (Phase 308)"
        [ testCase "sort's result passes its certificate, cycles and dangling reads included"
          <| fun _ ->
              let deps =
                  Map.ofList
                      [ "a", Set.ofList [ "b"; "zz" ]
                        "b", Set.ofList [ "c" ]
                        "c", Set.empty
                        "p", Set.ofList [ "q" ]
                        "q", Set.ofList [ "p" ]
                        "s", Set.ofList [ "s" ]
                        "t", Set.ofList [ "p"; "s" ] ]

              Expect.isTrue (Propagation.validTopo deps (Propagation.sort deps)) "Tarjan's order is certified"
              Expect.isTrue (Propagation.validTopo Map.empty (Propagation.sort Map.empty)) "and the empty map's"

          testCase "each clause of the certificate can refuse"
          <| fun _ ->
              let deps =
                  Map.ofList [ "a", Set.ofList [ "b" ]; "b", Set.empty; "c", Set.ofList [ "c" ] ]

              let ok: Propagation.TopoResult =
                  { Order = [ "b"; "a" ]
                    Cycles = [ [ "c" ] ] }

              Expect.isTrue (Propagation.validTopo deps ok) "the honest order"

              Expect.isFalse (Propagation.validTopo deps { ok with Order = [ "b"; "a"; "b" ] }) "an id twice in Order"

              Expect.isFalse (Propagation.validTopo deps { ok with Order = [ "a" ] }) "an id held nowhere"

              Expect.isFalse
                  (Propagation.validTopo deps { ok with Order = [ "b"; "a"; "zz" ] })
                  "an id deps does not hold"

              Expect.isFalse (Propagation.validTopo deps { ok with Order = [ "a"; "b" ] }) "a read after its reader"

              Expect.isFalse
                  (Propagation.validTopo
                      deps
                      { Order = [ "b"; "a"; "c" ]
                        Cycles = [] })
                  "a self-reader walked as acyclic"

              Expect.isFalse
                  (Propagation.validTopo
                      deps
                      { ok with
                          Cycles = [ [ "c" ]; [ "a" ] ] })
                  "an id in Order and in a cycle" ]
