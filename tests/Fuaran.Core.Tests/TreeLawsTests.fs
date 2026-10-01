module Fuaran.Core.Tests.TreeLawsTests

// Phase 331 — the tests of the kit's tree laws (`TreeLaws.fs`): the op algebra, the diff laws, the
// contained diff laws and the keyed-children laws, moved verbatim from `ConformanceTests.fs`. The shared
// reference domains and generators they run against stay in `ConformanceTests.fs`.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2
open Fuaran.Core.Tests.Reference.Counter
open Fuaran.Core.Tests.ConformanceTests

[<Tests>]
let treeLawTests =
    testList
        "Conformance"
        [ testCase "op-algebra laws run standalone (no stream)"
          <| fun _ ->
              let results = Conformance.opAlgebra nodew idw opGen 999 200
              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) "all algebra laws pass"

          testCase "diff laws certify the reference witness green (Phase 03)"
          <| fun _ ->
              let results = Conformance.diffLaws nodew idw opGen 4242 200

              Expect.equal
                  (List.length results)
                  4
                  "reconstruction + applyability + survivor + the non-identity guard (Phase 302)"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "reference witness failed diff laws:\n%s" (String.concat "\n" fails)

              // determinism: the same seed reproduces the identical verdict (seed-replay)
              Expect.equal (Conformance.diffLaws nodew idw opGen 4242 200) results "same seed ⇒ identical report"

          testCase "contained diff laws certify a container-bearing witness green (Phase 141)"
          <| fun _ ->
              // `diffLaws` above certifies `Diff.toOps`' scripts with the PLAIN sequence pair,
              // which is blind to containment by construction. This family asks the questions of
              // the pair that belong together: `toOpsContained` emitted the script, so
              // `canApplyAllWith` / `applyAllWith` under the SAME predicate are what must accept
              // it. Run against a witness with a real capability — the generator builds "section"
              // internally and "para" at the leaves, so a para is a genuine non-container and the
              // family's minted probe has somewhere to graft.
              // Phase 223 — the stratified `containedGen` (a para under every drawn root), which is
              // the kit reference generator the census row is measured against.
              let containerGen = containedGen

              // The probe was MEASURED rather than trusted, because a refusal law is green whether
              // or not its demanding direction is ever taken: under this predicate, before the
              // Phase 223 stratification, 100 of 200
              // iterations mint the violating probe (the rest draw an `after` with no para at all),
              // all 100 are refused with the offender named by id AND kind, and all 100 are
              // ACCEPTED by the plain `toOps` — so the refusal really is the container check's
              // contribution and not something else's.
              let results = Conformance.diffContainedLaws nodew idw containerGen 4242 200

              Expect.equal
                  (List.length results)
                  6
                  "reconstruction + applyability + refusal exactness + Phase 228's refusal correspondence + the two Phase 223 guards"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "reference witness failed the contained diff laws:\n%s" (String.concat "\n" fails)

              Expect.equal
                  (Conformance.diffContainedLaws nodew idw containerGen 4242 200)
                  results
                  "same seed ⇒ identical report"

          testCase "the contained diff laws have teeth — a witness whose canHold refuses everything"
          <| fun _ ->
              // The go-red the green run above cannot be: with a predicate that admits nothing, any
              // `after` carrying a child is a container violation, so the refusal law's DEMANDING
              // direction is the only one exercised. If `toOpsContained` ever stopped consulting
              // the predicate, this run would report a script where a refusal is required and the
              // family would go red — which is what makes the green run above evidence rather than
              // an assertion about a branch nothing takes.
              // MEASURED here too: 132 of 200 generated `after` trees violate natively under this
              // predicate, plus 68 minted probes — so the demanding direction is what this run is
              // almost entirely made of.
              let refuseAll =
                  { opGen with
                      CanHold = Some(fun (_: RNode) -> false) }

              let results = Conformance.diffContainedLaws nodew idw refuseAll 4242 200

              Expect.isTrue
                  (results |> List.forall (fun r -> r.Passed))
                  (sprintf
                      "a predicate that refuses everything must still be certified — every diff is a refusal and the refusal must be exact: %A"
                      (results |> List.filter (fun r -> not r.Passed)))

              // and the refusal really was the branch taken: the plain family still certifies the
              // same witness, so the difference between the two reports is the container check and
              // nothing else.
              Expect.isTrue
                  (Conformance.diffLaws nodew idw refuseAll 4242 200
                   |> List.forall (fun r -> r.Passed))
                  "the PLAIN diff laws are unaffected by the predicate — the container check is the only difference"

          testCase
              "Phase 223 — a canHold that refuses nothing turns diffContainedLaws RED, on the refused-pair guard alone"
          <| fun _ ->
              // The must-fail case: `opGen` supplies no `CanHold`, so the refusal iff is only ever
              // asked in its trivial direction. The three subject laws pass; the guard does not.
              let results = Conformance.diffContainedLaws nodew idw opGen 4242 200

              Expect.equal
                  (results |> List.filter (fun r -> not r.Passed) |> List.map (fun r -> r.Law))
                  [ SampleAdequacy.lawPrefix "Conformance.diffContainedLaws"
                    + "the sample reached every refused pair the laws distinguish" ]
                  "exactly the refused-pair guard is red"

          testCase "a deliberately-broken witness fails with a reproducible counterexample"
          <| fun _ ->
              // ReplaceChildren that ignores the new children — structural edits silently
              // no-op, so apply∘invert can no longer be the identity.
              let brokenW =
                  { nodew with
                      ReplaceChildren = fun n _ -> n }

              let results = Conformance.opAlgebra brokenW idw opGen 7 200
              Expect.isFalse (results |> List.forall (fun r -> r.Passed)) "the broken witness must fail a law"

              let failed = results |> List.filter (fun r -> not r.Passed)
              Expect.isNonEmpty failed "at least one law failed"

              Expect.isTrue
                  (failed |> List.forall (fun r -> r.Counterexample.IsSome))
                  "every failure carries a seeded counterexample"

              // determinism: the same seed reproduces the identical verdict
              let again = Conformance.opAlgebra brokenW idw opGen 7 200
              Expect.equal again results "same seed ⇒ identical report" ]

/// The NEUTERED check, and it is the realistic defect rather than a strawman: the same uniqueness
/// question asked over `Tree.ids`, which walks `Children` alone. It is what a domain writes when
/// it reaches for the engine's own scan, and it is green on every tree this suite draws.
let private surfaceOnlyUnique (root: KNode) =
    let ks = Tree.ids knodew root
    List.length (List.distinct ks) = List.length ks

/// The other half of the acceptance: the plain reference witness, which holds nothing anywhere
/// `Children` does not report, declares the empty list and says so.
let private noKeyed: KeyedWitness<RNode, string> =
    { Surface = "Tree.ids over the reference witness"
      KeyedChildren = fun _ -> []
      ReplaceKeyedChildren = fun n _ -> n
      PlaceKeyedChild = fun _ _ -> None
      IdsUnique =
        fun t ->
            let ks = Tree.ids nodew t |> List.map idw.ToString
            List.length (List.distinct ks) = List.length ks }

let private lawNamed (prefix: string) (results: LawResult list) =
    results |> List.find (fun r -> r.Law.StartsWith prefix)

[<Tests>]
let keyedChildrenLawTests =
    testList
        "Conformance.keyedChildrenLaws"
        [ testCase "the reference witness with a keyed slot is the first certifier — every law green"
          <| fun _ ->
              let results = Conformance.keyedChildrenLaws keyw knodew idw kGen 1890 200

              let failed = results |> List.filter (fun r -> not r.Passed)

              if not (List.isEmpty failed) then
                  let msg =
                      failed
                      |> List.map (fun r -> sprintf "  %s — %A" r.Law r.Counterexample)
                      |> String.concat "\n"

                  failtestf "the keyed reference witness failed keyedChildrenLaws:\n%s" msg

              Expect.equal (List.length results) 4 "three laws + the adequacy guard"

              Expect.equal
                  (Conformance.keyedChildrenLaws keyw knodew idw kGen 1890 200)
                  results
                  "same seed ⇒ identical report"

          testCase "the census calls it Guarded, and the run reports the arms it reached"
          <| fun _ ->
              // The half `SampleAdequacyTests` does not run for a witness-taking family — it leaves
              // those to their own suite, and this is that suite.
              match
                  KitRoster.census
                  |> List.tryFind (fun (n, _) -> n = "Conformance.keyedChildrenLaws")
              with
              | Some(_, Guarded _) -> ()
              | Some(_, Unconditional why) -> failtestf "censused Unconditional (%s) but it emits a guard" why
              | None -> failtest "Conformance.keyedChildrenLaws is missing from SampleAdequacy.census"

              let adequacy =
                  Conformance.keyedChildrenLaws keyw knodew idw kGen 1890 200
                  |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

              Expect.equal (List.length adequacy) 1 "exactly one adequacy law"
              Expect.isTrue (List.head adequacy).Passed "the reference witness reaches every arm"

              Expect.stringContains
                  (List.head adequacy).Law
                  "the sample reached every built arm"
                  "a witness WITH keyed positions is measured, not declared vacuous"

          testCase "go-red: a check that walks only `Children` loses both collision laws"
          <| fun _ ->
              // The defect the family exists to catch, and the one the ladder row describes: the
              // engine's own scan, adopted as the domain's check. It is green on every drawn tree,
              // which is why the collision subjects are BUILT.
              let results =
                  Conformance.keyedChildrenLaws
                      { keyw with
                          IdsUnique = surfaceOnlyUnique }
                      knodew
                      idw
                      kGen
                      1890
                      200

              Expect.isTrue
                  (lawNamed "the domain's id check accepts" results).Passed
                  "a surface-only check still accepts a clean tree — the acceptance law is not what catches it"

              let clash =
                  lawNamed "the domain's id check refuses an id held in a keyed position and" results

              Expect.isFalse clash.Passed "a surface-only check must lose the keyed-vs-surface law"

              Expect.stringContains
                  (clash.Counterexample |> Option.defaultValue "")
                  "ACCEPTED a tree holding"
                  "the counterexample says what was accepted"

              let twice = lawNamed "the domain's id check refuses an id held in two keyed" results

              Expect.isFalse twice.Passed "a surface-only check must lose the twice-keyed law too"

          testCase "go-red: a check that refuses everything loses the acceptance law"
          <| fun _ ->
              // Without this arm the two collision laws certify `fun _ -> false`, which refuses
              // every tree the domain will ever hold and is not a check at all.
              let results =
                  Conformance.keyedChildrenLaws { keyw with IdsUnique = fun _ -> false } knodew idw kGen 1890 200

              let accepts = lawNamed "the domain's id check accepts" results
              Expect.isFalse accepts.Passed "a check that refuses everything must lose the acceptance law"

              Expect.stringContains
                  (accepts.Counterexample |> Option.defaultValue "")
                  "REFUSED a tree whose full walk"
                  "the counterexample says what was refused"

          testCase "a witness declaring no keyed position passes VACUOUSLY, and the report says so"
          <| fun _ ->
              let results = Conformance.keyedChildrenLaws noKeyed nodew idw opGen 1890 100

              Expect.isTrue (results |> List.forall (fun r -> r.Passed)) "a domain with no keyed position is not failed"

              Expect.equal (List.length results) 4 "the report keeps its shape whatever the witness declares"

              let adequacy = lawNamed "sample adequacy" results

              Expect.stringContains
                  adequacy.Law
                  "vacuous BY DECLARATION"
                  "the adequacy line distinguishes 'nothing to certify' from 'three laws certified'"

          testCase "a witness that declares keyed positions and can build none FAILS the guard"
          <| fun _ ->
              // The case that must not be read as the one above. Declaring keyed positions is a
              // claim this family can measure; giving it no way to build one makes the claim
              // unmeasurable, which is not the same as having nothing to claim.
              let results =
                  Conformance.keyedChildrenLaws
                      { keyw with
                          PlaceKeyedChild = fun _ _ -> None }
                      knodew
                      idw
                      kGen
                      1890
                      200

              let adequacy = lawNamed "sample adequacy" results
              Expect.isFalse adequacy.Passed "an arm nothing could reach must be reported"

              Expect.stringContains
                  (adequacy.Counterexample |> Option.defaultValue "")
                  "keyed id in the witness surface"
                  "and it names the arm that was never built" ]

// Phase 220 — opAlgebra's accepted / refused guard, shown starved by a run too short to reach both sides.
[<Tests>]
let opAlgebraGuardTests =
    testList
        "Conformance.refusableFamilies"
        [ testCase "go-red: an opAlgebra run too short to reach both sides reports the guard, not a pass"
          <| fun _ ->
              let results = Conformance.opAlgebra nodew idw loneLeafGen 999 1

              // Phase 297 — the accept-side laws are COVERED by the accepted-op guard, so a run that
              // never accepted an op reads them through the guard (green here, red there) rather
              // than as three "never reached" reds beside a red guard.
              Expect.isTrue
                  (subjectOf results |> List.forall (fun r -> r.Passed))
                  "every subject law is green over one drawn op"

              let starved =
                  [ "accepted op and op kind"; "refused op" ]
                  |> List.filter (fun side -> not (guardNamed "Conformance.opAlgebra" side results).Passed)

              // One drawn op is one kind, so the accepted-op guard is starved on the five kinds it did
              // not draw whichever side the op reached; the refused side is starved iff the op applied.
              Expect.isNonEmpty starved "one op cannot reach both sides and every kind"

              Expect.contains
                  starved
                  "accepted op and op kind"
                  "one drawn op leaves five kinds unreached, and the guard says so" ]
