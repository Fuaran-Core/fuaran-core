module Fuaran.Core.Tests.ConcurrencyTests

// Phase 80 — Conformance.concurrencyLaws: confluence of independence-declared scripts under
// sampled interleavings, the dependent-pair skip (sufficiency-not-necessity), seed replay, and
// the teeth-check (an injected under-approximating footprint makes the law bite).

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference

// The FootprintTests generator shape (test-local duplicate; each law-test file carries its own).

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
let concurrencyLawTests =
    testList
        "Conformance.concurrencyLaws"
        [ testCase "the reference witness certifies interleaving totality + confluence + coverage green"
          <| fun _ ->
              let results = Conformance.concurrencyLaws nodew idw opGen encNode 8080 300
              Expect.equal (List.length results) 3 "totality + confluence + coverage laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "concurrencyLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism of the kit itself
              Expect.equal
                  (Conformance.concurrencyLaws nodew idw opGen encNode 8080 300)
                  results
                  "same seed ⇒ identical report"

          testCase "teeth-check: an under-approximating footprint (everything independent) makes the law bite"
          <| fun _ ->
              // Inject a footprint that erases every address set — `Ops.independent` then declares
              // EVERY pair independent, so genuinely-dependent pairs (same-parent positional
              // inserts, remove-vs-insert races) are asserted and must fail totality and/or
              // confluence. If this run comes back green, the law certifies nothing.
              let underApprox (_: SkeletonOp<RNode, string> list) = Ops.footprint nodew idw []

              let results =
                  Conformance.concurrencyLawsWith underApprox nodew idw opGen encNode 8080 300

              Expect.isTrue
                  (results |> List.exists (fun r -> not r.Passed))
                  "falsely-declared independence must produce a counterexample — the law has teeth"

          testCase "teeth-check: dropping ONLY the unknown-parent clause makes the law bite (Phase 143)"
          <| fun _ ->
              // The sharper teeth. The case above erases every address, which proves the law can
              // fail but says nothing about WHICH clause was earning its keep. This erases exactly
              // one set — `UnknownParentWrites` — and nothing else, which is precisely
              // `Ops.independent` with its last two clauses removed: with both sides' unknown set
              // empty, those two clauses are vacuously true and the other four still read the real
              // addresses. So this run IS the tightened `independent` Phase 143 set out to ship.
              //
              // It must go red. A relocation freed against a concurrent structural write includes
              // the case where that write's parent is inside the removed subtree, and there the two
              // orders do not agree — `proofs/TreeOps.fst` section 18 proves it at a named witness
              // (`relocation_diamond_fails_for_a_remove`), and this is the same fact over a
              // generated pool. If it ever comes back green, either the generator stopped producing
              // relocations or the algebra changed under the theorem: read section 18 before
              // treating a green here as permission to tighten the clause.
              let noUnknownParent (ops: SkeletonOp<RNode, string> list) =
                  let fp = Ops.footprint nodew idw ops

                  { fp with
                      UnknownParentWrites = Set.empty }

              let results =
                  Conformance.concurrencyLawsWith noUnknownParent nodew idw opGen encNode 8080 300

              // The failing law is NAMED, not merely counted. `coverage` is a vacuity guard and
              // erasing an address set can only ever make MORE pairs independent, so a run that
              // "failed" on coverage alone would be this check passing for the opposite of its
              // reason. What has to lose is totality or confluence — the two laws that assert
              // something about a pair the footprint declared independent.
              let failed =
                  results |> List.filter (fun r -> not r.Passed) |> List.map (fun r -> r.Law)

              Expect.isNonEmpty
                  failed
                  "erasing UnknownParentWrites is exactly the tightened clause — it must produce a counterexample, or the pinned over-approximation is buying nothing"

              Expect.isTrue
                  (failed
                   |> List.exists (fun l -> l.Contains "totality" || l.Contains "confluence"))
                  (sprintf
                      "the break must be a pair that was asserted and did not hold, not the vacuity guard — failed laws: %s"
                      (String.concat "; " failed))

          testCase "dependent pairs are skipped, not asserted — and the coverage guard names the vacuous run"
          <| fun _ ->
              // Force EVERY pair dependent: grafting a RemoveNode onto each script's footprint puts
              // an UnknownParentWrites in both, and Ops.independent serialises a remove against any
              // structural write. The law must then skip every pair (totality + confluence stay
              // vacuously green — sufficiency-not-necessity: no claim about dependent pairs) while
              // the coverage vacuity guard reports that nothing was certified.
              let allConflict (ops: SkeletonOp<RNode, string> list) =
                  Ops.footprint nodew idw (RemoveNode "phantom-clash" :: ops)

              let results =
                  Conformance.concurrencyLawsWith allConflict nodew idw opGen encNode 8080 60

              let byLaw (name: string) =
                  results |> List.find (fun r -> r.Law.Contains name)

              Expect.isTrue (byLaw "totality").Passed "no pair asserted ⇒ no totality counterexample"
              Expect.isTrue (byLaw "confluence").Passed "dependent pairs are skipped, never asserted"
              Expect.isFalse (byLaw "coverage").Passed "the vacuity guard reports the empty sample" ]
