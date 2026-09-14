module Fuaran.Core.Tests.ContainedOpsTests

// Phase 251 — container-aware skeleton ops. `applyContained`/`canApplyContained` reject an
// insert/move under a leaf with `NotAContainer` instead of silently no-op'ing (the F1
// adoption finding); the plain `apply`/`canApply` are unchanged (every node can hold
// children). A leaf-bearing witness then certifies green through the container-aware path.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

/// In the reference domain, a "para" is a leaf; everything else can hold children.
let private canHold (n: RNode) = n.Kind <> "para"

/// root(doc) [ s(section)[], p(para) ] — a container and a leaf as siblings.
let private mixed () =
    RNode.node "root" "doc" [ RNode.node "s" "section" []; RNode.leaf "p" "para" "x" ]

[<Tests>]
let tests =
    testList
        "Ops.applyContained"
        [ testCase "rejects an insert under a leaf with NotAContainer"
          <| fun _ ->
              let op = InsertChild("p", RNode.leaf "c" "para" "")

              match Ops.applyContained canHold nodew idw op (mixed ()) with
              | Error(NotAContainer("p", "para")) -> ()
              | other -> failtestf "expected NotAContainer, got %A" other

          testCase "accepts an insert under a container"
          <| fun _ ->
              let op = InsertChild("s", RNode.leaf "c" "para" "hi")

              match Ops.applyContained canHold nodew idw op (mixed ()) with
              | Ok r -> Expect.isSome (Tree.tryFind nodew idw "c" r) "inserted under the container"
              | Error e -> failtestf "unexpected %A" e

          testCase "the plain apply is unchanged — no NotAContainer on the default path"
          <| fun _ ->
              // default `apply` treats every node as a container (back-compat); the insert
              // succeeds structurally (ReplaceChildren on the reference RNode is total).
              let op = InsertChild("p", RNode.leaf "c" "para" "")
              Expect.isOk (Ops.apply nodew idw op (mixed ())) "default apply still accepts it"

          testCase "MoveNode under a leaf is NotAContainer"
          <| fun _ ->
              match Ops.applyContained canHold nodew idw (MoveNode("s", "p")) (mixed ()) with
              | Error(NotAContainer("p", "para")) -> ()
              | other -> failtestf "expected NotAContainer, got %A" other

          testCase "canApplyContained mirrors applyContained"
          <| fun _ ->
              let op = InsertChild("p", RNode.leaf "c" "para" "")

              Expect.equal
                  (Ops.canApplyContained canHold nodew idw op (mixed ()))
                  (Ops.applyContained canHold nodew idw op (mixed ()) |> Result.map ignore)
                  "check ≡ apply (contained)"

          // ---- Phase 161 (DECISIONS D37, option A): the graft's INTERIOR is inspected ----
          // Phase 140's `contained_op` counterexample, now a refusal. `canHold` used to be applied
          // to the PARENT and to nothing inside the inserted subtree, so a graft that placed
          // children under its own non-container node was accepted whole and the invariant
          // "every node with children satisfies canHold" broke across an ACCEPTED operation.

          testCase "a graft whose interior places children under a non-container is refused"
          <| fun _ ->
              // "a" is a para — a leaf kind — and it holds "b". The offending node is in the GRAFT,
              // not in the tree, which is what makes this case different from the two above.
              let graft = InsertChild("s", RNode.node "a" "para" [ RNode.leaf "b" "para" "" ])

              match Ops.applyContained canHold nodew idw graft (mixed ()) with
              | Error(NotAContainer("a", "para")) -> ()
              | other -> failtestf "expected NotAContainer naming the interior offender 'a', got %A" other

          testCase "the interior walk names the FIRST offender in preorder"
          <| fun _ ->
              // Two offenders, nested. `a` precedes `a2` in preorder, so `a` is named — the same
              // first-offender discipline `firstDuplicateId` follows for `DuplicateId`.
              let graft =
                  InsertChild("s", RNode.node "a" "para" [ RNode.node "a2" "para" [ RNode.leaf "a3" "para" "" ] ])

              match Ops.applyContained canHold nodew idw graft (mixed ()) with
              | Error(NotAContainer("a", "para")) -> ()
              | other -> failtestf "expected the first offender 'a', got %A" other

          testCase "a childless non-container inside a graft is NOT an offender"
          <| fun _ ->
              // The predicate answers "can this node hold children AT ALL", so a leaf that holds
              // none says nothing. A graft of containers carrying leaves is ordinary and accepted.
              let graft =
                  InsertChild("s", RNode.node "a" "section" [ RNode.leaf "b" "para" ""; RNode.leaf "c" "para" "" ])

              match Ops.applyContained canHold nodew idw graft (mixed ()) with
              | Ok r -> Expect.isSome (Tree.tryFind nodew idw "b" r) "the whole graft went in"
              | Error e -> failtestf "a contained graft must still be accepted, got %A" e

          testCase "canApplyContained sees the interior refusal too"
          <| fun _ ->
              let graft = InsertChild("s", RNode.node "a" "para" [ RNode.leaf "b" "para" "" ])

              Expect.equal
                  (Ops.canApplyContained canHold nodew idw graft (mixed ()))
                  (Ops.applyContained canHold nodew idw graft (mixed ()) |> Result.map ignore)
                  "check ≡ apply over the interior walk"

          testCase "the plain apply is byte-for-byte unaffected by the interior walk"
          <| fun _ ->
              // `Ops.apply` is `applyWith (fun _ -> true)`, under which no node is ever an
              // offender — the machine-checked form is `apply_contained_is_apply` in
              // proofs/Preservation.fst. Asserted on the RESULT TREE, not merely on acceptance:
              // a widened refusal surface must not move a single accepted byte.
              let graft = InsertChild("s", RNode.node "a" "para" [ RNode.leaf "b" "para" "" ])

              Expect.equal
                  (Ops.apply nodew idw graft (mixed ()))
                  (Ops.applyContained (fun _ -> true) nodew idw graft (mixed ()))
                  "apply ≡ applyContained (fun _ -> true) on a graft the real predicate refuses"

              match Ops.apply nodew idw graft (mixed ()) with
              | Ok r -> Expect.isSome (Tree.tryFind nodew idw "b" r) "the plain engine still accepts it whole"
              | Error e -> failtestf "the plain apply must be unchanged, got %A" e

          testCase "the duplicate-id scan still outranks the interior walk"
          <| fun _ ->
              // D37's precedence decision, pinned: the new check goes LAST, so no operation that
              // was refused before Phase 161 changes its class. This graft breaks BOTH invariants.
              let graft = InsertChild("s", RNode.node "p" "para" [ RNode.leaf "b" "para" "" ])

              match Ops.applyContained canHold nodew idw graft (mixed ()) with
              | Error(DuplicateId "p") -> ()
              | other -> failtestf "expected DuplicateId to keep its precedence, got %A" other

          testCase "MoveNode does NOT walk the moved subtree's interior"
          <| fun _ ->
              // D37's scope decision. The moved subtree is already in the tree, so a move carries
              // in no interior structure the tree did not already hold; refusing it would be an
              // invariant-REPAIR gate rather than a graft check. Built by hand because the engine
              // will not produce this tree any more: `s` holds a para that holds a para.
              let tree =
                  RNode.node
                      "root"
                      "doc"
                      [ RNode.node "s" "section" [ RNode.node "a" "para" [ RNode.leaf "b" "para" "" ] ]
                        RNode.node "s2" "section" [] ]

              match Ops.applyContained canHold nodew idw (MoveNode("a", "s2")) tree with
              | Ok r -> Expect.isSome (Tree.tryFind nodew idw "b" r) "the subtree moved intact, interior and all"
              | Error e -> failtestf "a move must not be refused for a pre-existing interior violation, got %A" e

          testCase "a leaf-bearing witness certifies green via the container-aware path (F1 resolved)"
          <| fun _ ->
              // a generator that emits leaf "para" nodes mixed with "section" containers —
              // the exact shape that silently no-op'd before Phase 251.
              let genTree (rng: ConfRng.T) : RNode * ConfRng.T =
                  let mutable counter = 0
                  let mutable r = rng

                  let freshId () =
                      let s = sprintf "n%d" counter
                      counter <- counter + 1
                      s

                  let rec build depth =
                      let id = freshId ()
                      let leafRoll, r1 = ConfRng.intBelow 2 r
                      r <- r1

                      if depth <= 0 || leafRoll = 0 then
                          RNode.leaf id "para" "v"
                      else
                          let nKids, r2 = ConfRng.intBelow 3 r
                          r <- r2
                          RNode.node id "section" [ for _ in 1..nKids -> build (depth - 1) ]

                  let rootId = freshId ()
                  let nKids, r2 = ConfRng.intBelow 3 r
                  r <- r2
                  RNode.node rootId "section" [ for _ in 1..nKids -> build 1 ], r

              let genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
                  let mutable r = rng

                  let rec pick () =
                      let v, r' = ConfRng.next r
                      r <- r'
                      let id = sprintf "f%d" (v % 100000)
                      if existing.Contains id then pick () else id

                  RNode.leaf (pick ()) "para" "x", r

              let opGen: OpGen<RNode, string> =
                  { Tree = genTree
                    FreshNode = genFresh
                    CanHold = Some canHold }

              let results = Conformance.opAlgebra nodew idw opGen 4242 200
              let failed = results |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failed) then
                  let msg =
                      failed
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "leaf-bearing witness failed conformance:\n%s" msg ]

// ---------------------------------------------------------------------------
//  Phase 161 — `Conformance.containerLaws`, certified against the reference witness
// ---------------------------------------------------------------------------

/// A container-aware generator over the reference `RNode`: "section" nodes hold children,
/// "para" nodes are leaves, and `FreshNode` mints a para — so a fresh node given a child is an
/// interior offender, which is what the family's graft arm needs to reach.
let private genTree (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable counter = 0
    let mutable r = rng

    let freshId () =
        let s = sprintf "n%d" counter
        counter <- counter + 1
        s

    let rec build depth =
        let id = freshId ()
        let leafRoll, r1 = ConfRng.intBelow 2 r
        r <- r1

        if depth <= 0 || leafRoll = 0 then
            RNode.leaf id "para" "v"
        else
            let nKids, r2 = ConfRng.intBelow 3 r
            r <- r2
            RNode.node id "section" [ for _ in 1..nKids -> build (depth - 1) ]

    let rootId = freshId ()
    let nKids, r2 = ConfRng.intBelow 3 r
    r <- r2
    RNode.node rootId "section" [ for _ in 1..nKids -> build 1 ], r

let private genFresh (existing: Set<string>) (rng: ConfRng.T) : RNode * ConfRng.T =
    let mutable r = rng

    let rec pick () =
        let v, r' = ConfRng.next r
        r <- r'
        let id = sprintf "f%d" (v % 100000)
        if existing.Contains id then pick () else id

    RNode.leaf (pick ()) "para" "x", r

let private containerGen: OpGen<RNode, string> =
    { Tree = genTree
      FreshNode = genFresh
      CanHold = Some canHold }

[<Tests>]
let containerLawTests =
    testList
        "Conformance.containerLaws"
        [ testCase "the reference witness is the first certifier — every law green"
          <| fun _ ->
              let results = Conformance.containerLaws nodew idw containerGen 1610 200

              let failed = results |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failed) then
                  let msg =
                      failed
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "the reference witness failed containerLaws:\n%s" msg

              Expect.equal (List.length results) 4 "three laws + the adequacy guard"

              Expect.equal
                  (Conformance.containerLaws nodew idw containerGen 1610 200)
                  results
                  "same seed ⇒ identical report"

          testCase "the census calls it Guarded, and it emits an adequacy law"
          <| fun _ ->
              // The half `SampleAdequacyTests` cannot run for itself — that suite checks the
              // Guarded/Unconditional claim only for families that need no domain witness, and
              // leaves the witness-taking ones to their own suite. This is that suite.
              let adequacy =
                  Conformance.containerLaws nodew idw containerGen 1610 40
                  |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

              Expect.equal (List.length adequacy) 1 "exactly one adequacy law"

              match
                  SampleAdequacy.census
                  |> List.tryFind (fun (n, _) -> n = "Conformance.containerLaws")
              with
              | Some(_, Guarded _) -> ()
              | Some(_, Unconditional why) -> failtestf "censused Unconditional (%s) but it emits a guard" why
              | None -> failtest "Conformance.containerLaws is missing from SampleAdequacy.census"

          testCase "a canHold that READS THE CHILD LIST fails the child-blindness law"
          <| fun _ ->
              // The go-red for law 1, and the predicate is Phase 140's `cx_childless`: lawful by
              // the engine's type, and unstable under the very edit `applyContained` licenses.
              let childReading (n: RNode) = List.isEmpty (nodew.Children n)

              let results =
                  Conformance.containerLaws
                      nodew
                      idw
                      { containerGen with
                          CanHold = Some childReading }
                      1610
                      200

              let blind =
                  results |> List.find (fun r -> r.Law.StartsWith "canHold is child-blind")

              Expect.isFalse blind.Passed "a child-reading predicate must fail the child-blindness law"

              Expect.stringContains
                  (blind.Counterexample |> Option.defaultValue "")
                  "READS THE CHILD LIST"
                  "the counterexample says what is wrong"

          testCase "a domain that declares no container predicate is named, not skipped"
          <| fun _ ->
              let results =
                  Conformance.containerLaws nodew idw { containerGen with CanHold = None } 1610 20

              Expect.equal (List.length results) 1 "one law, and it is the refusal"
              Expect.isFalse (results |> List.forall (fun r -> r.Passed)) "CanHold = None must not certify green"

          testCase "a generator that can never build the graft arm fails the adequacy guard"
          <| fun _ ->
              // The guard's own teeth. `canHold = fun _ -> true` admits every node, so no graft
              // can carry an interior offender and the interior arm is never reached — the family
              // reports the empty arm with its counts rather than passing on two laws out of three.
              let results =
                  Conformance.containerLaws
                      nodew
                      idw
                      { containerGen with
                          CanHold = Some(fun _ -> true) }
                      1610
                      200

              let adequacy = results |> List.find (fun r -> r.Law.StartsWith "sample adequacy")
              Expect.isFalse adequacy.Passed "an arm nothing reached must be reported"

              Expect.stringContains
                  (adequacy.Counterexample |> Option.defaultValue "")
                  "interior graft"
                  "and it names the arm" ]
