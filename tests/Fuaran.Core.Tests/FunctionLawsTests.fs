module Fuaran.Core.Tests.FunctionLawsTests

// Phase 331 — the tests of the kit's function laws (`FunctionLaws.fs`): composition, memoisation and
// artifact-function verification, moved verbatim from `ConformanceTests.fs`. The in-repo validity
// oracle and the parameter generator they drive stay in `ConformanceTests.fs`.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2
open Fuaran.Core.Tests.Reference.Counter
open Fuaran.Core.Tests.ConformanceTests

[<Tests>]
let functionLawTests =
    testList
        "Conformance"
        [ // Phase 47 — the cross-witness composition laws.
          testCase "compositionLaws certify nested-application + associativity + hygiene + effect-join (Phase 47)"
          <| fun _ ->
              let results = Conformance.compositionLaws artw artw id genComposition 4242 200

              Expect.equal
                  (List.length results)
                  4
                  "nested-application + associativity + hygiene + effect-join laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "compositionLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.compositionLaws artw artw id genComposition 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 49 — the memoised-application laws.
          testCase "memoLaws certify equals-direct + param-miss + effecting-bypass + replay-parity (Phase 49)"
          <| fun _ ->
              let results =
                  Conformance.memoLaws artw encNode genMemo OpStream.defaultHash 4242 200

              Expect.equal
                  (List.length results)
                  5
                  "equals-direct + param-miss + effecting-bypass + replay-parity + separator-miss (Phase 290) laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "memoLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.memoLaws artw encNode genMemo OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 51 — the cross-witness composition pilot (RNode outer + R2Node inner).
          testCase "compositionPilot certifies composeAcross + applyMemo across two distinct witnesses (Phase 51)"
          <| fun _ ->
              let results =
                  Conformance.compositionPilot artw artw2 embedToR encNode encNode2 genComposition2 4242 200

              Expect.equal
                  (List.length results)
                  6
                  "composeAcross (nested + associative + hygiene + effect-join) + applyMemo (sub-fn + composed) reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "compositionPilot failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.compositionPilot artw artw2 embedToR encNode encNode2 genComposition2 4242 200)
                  results
                  "same seed ⇒ identical report"

          // Phase 53 — the memo audited-effect soundness laws.
          testCase "memoSoundnessLaws certify an under-declared-impure function is bypassed (Phase 53)"
          <| fun _ ->
              // root declares Pure/Deterministic, but the count node secretly observes the clock — the
              // under-declared case the pre-Phase-53 declared-root gate would have wrongly memoised.
              let underDeclared =
                  { RNode.node
                        "ud"
                        "template"
                        [ { RNode.hole "c" "field" "count" (ValueHole(IntRange(0, 5))) with
                              Eff =
                                  { Host = Pure
                                    Determinism = Effect.clock } } ] with
                      Eff = Effect.pureDeterministic }

              let underDeclaredArgs = Map.ofList [ "ud/c", ValueArg "3" ]

              let results =
                  Conformance.memoSoundnessLaws artw encNode underDeclared underDeclaredArgs 4242 50

              Expect.equal (List.length results) 2 "gate-distinction + bypass laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "memoSoundnessLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.memoSoundnessLaws artw encNode underDeclared underDeclaredArgs 4242 50)
                  results
                  "same seed ⇒ identical report" ]

/// A single-hole template: just a `count` over [lo, hi] — a small finite space for the symbolic mode.
let private countOnly (lo, hi) =
    { RNode.node "ct" "template" [ RNode.hole "c" "field" "count" (ValueHole(IntRange(lo, hi))) ] with
        Eff = Effect.pureDeterministic }

[<Tests>]
let functionVerifyTests =
    testList
        "Conformance.functionVerify"
        [ testCase "a sound function verifies clean; a broken one yields a (param-set, defect) counterexample"
          <| fun _ ->
              let sound =
                  Conformance.verifyFunction artw (tplCount (0, 5)) countReg genParamsFor 4242 200

              Expect.isTrue sound.Verified "count∈[0,5] never violates the ≤5 rule"
              Expect.isNone sound.Counterexample "a sound function has no counterexample"

              let broken =
                  Conformance.verifyFunction artw (tplCount (0, 10)) countReg genParamsFor 4242 200

              Expect.isFalse broken.Verified "count∈[0,10] admits a >5 value — not correct-by-construction"

              match broken.Counterexample with
              | Some cx ->
                  match cx.Defect with
                  | ValidatorRejected ds -> Expect.isNonEmpty ds "the validator's defect travels in the counterexample"
                  | other -> failtestf "expected ValidatorRejected, got %A" other

                  Expect.stringContains
                      (Conformance.renderCounterexample artw cx)
                      "tpl/c"
                      "the rendered counterexample cites the count hole"
              | None -> failtest "a broken function must surface a counterexample"

          testCase "verification is deterministic — same seed ⇒ identical report"
          <| fun _ ->
              let a =
                  Conformance.verifyFunction artw (tplCount (0, 10)) countReg genParamsFor 999 200

              let b =
                  Conformance.verifyFunction artw (tplCount (0, 10)) countReg genParamsFor 999 200

              Expect.equal a b "same seed reproduces the verdict + counterexample"

          testCase "symbolic mode exhaustively covers a small space and reports the coverage (Phase 48)"
          <| fun _ ->
              let sound =
                  Conformance.verifyFunctionSymbolic artw (countOnly (0, 5)) countReg Map.empty 100 7

              Expect.equal sound.Coverage (Exhaustive 6) "6 ints in [0,5], all enumerated"
              Expect.isTrue sound.Verified "every value in [0,5] respects the ≤5 rule"

              let broken =
                  Conformance.verifyFunctionSymbolic artw (countOnly (0, 10)) countReg Map.empty 100 7

              Expect.equal broken.Coverage (Exhaustive 11) "11 ints in [0,10], the whole space"
              Expect.isFalse broken.Verified "exhaustive enumeration finds the >5 values"

          testCase "symbolic mode samples a large space with the coverage reported (coverage honesty)"
          <| fun _ ->
              let report =
                  Conformance.verifyFunctionSymbolic artw (countOnly (0, 100000)) countReg Map.empty 50 7

              match report.Coverage with
              | Sampled(50, Some 100001) -> ()
              | other -> failtestf "expected Sampled(50, Some 100001), got %A" other

          // Phase 297 — the space size is an int64 product that saturates, so a multi-hole space too
          // large for an `int` can neither wrap into an exhaustive enumeration nor be misreported.
          testCase "symbolic mode sizes a multi-hole space without wrapping (Phase 297)"
          <| fun _ ->
              let holes (n: int) (lo, hi) =
                  { RNode.node
                        "mh"
                        "template"
                        [ for k in 1..n -> RNode.hole (sprintf "h%d" k) "field" "count" (ValueHole(IntRange(lo, hi))) ] with
                      Eff = Effect.pureDeterministic }

              // 100^4 = 10^8 fits an int: sampled, and the size reported exactly.
              let fits =
                  Conformance.verifyFunctionSymbolic artw (holes 4 (0, 99)) countReg Map.empty 50 7

              match fits.Coverage with
              | Sampled(50, Some 100000000) -> ()
              | other -> failtestf "four holes of 100: expected Sampled(50, Some 100000000), got %A" other

              // 1000^4 = 10^12: as an unchecked `int` product this wrapped to a negative number, which
              // passed `<= maxCases` and materialised the full cartesian product. It samples now, and
              // the size — too large for the report's `int` — is reported unknown, never wrapped.
              let wraps =
                  Conformance.verifyFunctionSymbolic artw (holes 4 (0, 999)) countReg Map.empty 50 7

              match wraps.Coverage with
              | Sampled(50, None) -> ()
              | other -> failtestf "four holes of 1,000: expected Sampled(50, None), got %A" other

              Expect.isFalse wraps.Verified "values above five are drawn, and the validator catches one"

          testCase "a hole over the whole int range is sampled, not refused as an overflowing size (Phase 297)"
          <| fun _ ->
              // `hi - lo + 1` for IntRange(0, Int32.MaxValue) overflowed `int` and read as an empty
              // domain, reporting a false DidNotApply. Sized in int64 it samples, and the ≤5 rule's real
              // counterexample surfaces.
              let report =
                  Conformance.verifyFunctionSymbolic artw (countOnly (0, System.Int32.MaxValue)) countReg Map.empty 50 7

              match report.Counterexample with
              | Some cx ->
                  match cx.Defect with
                  | ValidatorRejected _ -> ()
                  | other -> failtestf "expected the validator's defect, got %A" other
              | None -> failtest "a count over the whole int range must surface a value above five"

          testCase "symbolic mode varies value holes while slots are pinned via fixedArgs"
          <| fun _ ->
              let fixedArgs =
                  Map.ofList [ "tpl/t", ValueArg "T"; "tpl/s", SlotArg(RNode.leaf "p" "para" "x") ]

              let report =
                  Conformance.verifyFunctionSymbolic artw (tplCount (0, 5)) countReg fixedArgs 100 7

              Expect.equal report.Coverage (Exhaustive 6) "only the count hole varies (6 cases)"
              Expect.isTrue report.Verified "clean across the pinned-slot param space"

          testCase "verifyFunction surfaces an undeclared effect as a defect (Fork-3 cross-check)"
          <| fun _ ->
              // a 'pure'-declared template whose count node secretly observes the clock.
              let leaky =
                  { RNode.node
                        "lk"
                        "template"
                        [ { RNode.hole "c" "field" "count" (ValueHole(IntRange(0, 5))) with
                              Eff =
                                  { Host = Pure
                                    Determinism = Effect.clock } } ] with
                      Eff = Effect.pureDeterministic }

              let report = Conformance.verifyFunction artw leaky Validator.empty genParamsFor 1 25

              Expect.isFalse report.Verified "an effect the declaration doesn't cover is a defect"

              match report.Counterexample with
              | Some cx ->
                  match cx.Defect with
                  | EffectObserved(_, observed) ->
                      Expect.equal observed.Determinism Effect.clock "the observed clock effect is named"
                  | other -> failtestf "expected EffectObserved, got %A" other
              | None -> failtest "the effect leak must surface a counterexample"

          testCase "functionVerifyLaws certify sound-clean + broken-fails + determinism (Phase 48)"
          <| fun _ ->
              let results =
                  Conformance.functionVerifyLaws artw (tplCount (0, 5)) (tplCount (0, 10)) countReg genParamsFor 777 200

              Expect.equal (List.length results) 3 "sound + broken + determinism laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "functionVerifyLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism of the kit itself
              Expect.equal
                  (Conformance.functionVerifyLaws
                      artw
                      (tplCount (0, 5))
                      (tplCount (0, 10))
                      countReg
                      genParamsFor
                      777
                      200)
                  results
                  "same seed ⇒ identical report"

          // Phase 52 — the verifyFunction contract honesty boundary (effect-class-aware guard).
          testCase
              "verifyHonestyLaws certify stochastic-verifies-on-structure + effect-class-agnostic verdict (Phase 52)"
          <| fun _ ->
              // structurally-identical functions under a chosen effect-determinism axis: sound
              // (count∈[0,5], never violates the ≤5 rule) and broken (count∈[0,10], admits a >5 value).
              let mkSoundDet (d: DeterminismSource) =
                  { tplCount (0, 5) with
                      Eff = { Host = Pure; Determinism = d } }

              let mkBrokenDet (d: DeterminismSource) =
                  { tplCount (0, 10) with
                      Eff = { Host = Pure; Determinism = d } }

              let results =
                  Conformance.verifyHonestyLaws artw mkSoundDet mkBrokenDet countReg genParamsFor 777 200

              Expect.equal (List.length results) 2 "stochastic-verifies + effect-class-agnostic laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "verifyHonestyLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal
                  (Conformance.verifyHonestyLaws artw mkSoundDet mkBrokenDet countReg genParamsFor 777 200)
                  results
                  "same seed ⇒ identical report" ]
