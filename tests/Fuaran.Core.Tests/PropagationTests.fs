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

          testCase "staleSet is dirtyFromChangedIds (staleness as returned data)"
          <| fun _ ->
              let deps = Propagation.dependencyMap nodew idw readsOf (tree ())

              Expect.equal
                  (Propagation.staleSet deps (Set.singleton "c"))
                  (Propagation.dirtyFromChangedIds deps (Set.singleton "c"))
                  "staleSet == the dirty closure"

          testCase "touchedBy maps each SkeletonOp to its container ids"
          <| fun _ ->
              let root = tree ()
              let tb op = Propagation.touchedBy nodew idw root op

              Expect.equal
                  (tb (InsertChild("r", 0, RNode.leaf "z" "para" "")))
                  (Set.ofList [ "r"; "z" ])
                  "insert touches parent + inserted subtree"

              Expect.equal (tb (RemoveNode "b")) (Set.singleton "b") "remove touches the removed subtree"
              Expect.equal (tb (MoveNode("a", "r", 0))) (Set.ofList [ "a"; "r" ]) "move touches target + new parent"

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
                  (Set.ofList [ "s"; "s1"; "s2" ])
                  "removing a container touches every id in its subtree"

          testCase "dirtyFromOp removing a referenced node dirties its dangling dependents"
          <| fun _ ->
              // removing b (which a reads) makes a dirty (its binding now dangles)
              Expect.equal
                  (Propagation.dirtyFromOp nodew idw (tree ()) readsOf (RemoveNode "b"))
                  (Set.ofList [ "a"; "b" ])
                  "removed node + its dependents"

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
              Expect.equal (List.length results) 4 "four laws reported"

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
              | Error(Propagation.EvalUnknownChange [ "ghost" ]) -> ()
              | other -> failtestf "expected EvalUnknownChange [ghost], got %A" other

          testCase "propagationEvalLaws certify byte-identity + minimality + unknown-change (Phase 69)"
          <| fun _ ->
              let results = Conformance.propagationEvalLaws 4242 200
              Expect.equal (List.length results) 3 "three laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "propagationEvalLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.propagationEvalLaws 4242 200) results "same seed ⇒ identical report" ]
