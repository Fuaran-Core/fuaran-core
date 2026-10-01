module Fuaran.Core.Tests.StreamLawsTests

// Phase 331 — the tests of the kit's stream laws (`StreamLaws.fs`): capture / replay, compare-and-swap,
// the reducer's refusal guard and the stream sample-adequacy guards, moved verbatim from
// `ConformanceTests.fs`. The list names are the ones the cases carried there, so every test name is
// unchanged.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2
open Fuaran.Core.Tests.Reference.Counter
open Fuaran.Core.Tests.ConformanceTests

[<Tests>]
let streamLawTests =
    testList
        "Conformance"
        [ // Phase 27 — the determinism-capture / replay laws.
          testCase "captureReplayLaws certify exact replay for a non-deterministic int witness (Phase 27)"
          <| fun _ ->
              let encInt (n: int) = Json.render (JInt n)

              let decInt (s: string) =
                  Decode.parse s |> Result.bind Decode.asInt

              let results =
                  Conformance.captureReplayLaws encInt decInt ConfRng.next OpStream.defaultHash 31337 200

              Expect.equal
                  (List.length results)
                  6
                  "exact-replay + deterministic + tamper + identity-order + JSONL round-trip (Phase 301) laws, and the Phase 297 tampered-capture guard, reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "captureReplayLaws failed:\n%s" (String.concat "\n" fails)

              // determinism: the same seed reproduces the identical verdict (seed-replay)
              Expect.equal
                  (Conformance.captureReplayLaws encInt decInt ConfRng.next OpStream.defaultHash 31337 200)
                  results
                  "same seed ⇒ identical report" ]

// The casLaws cases keep the `Conformance.functionVerify` list name they were registered under.
[<Tests>]
let casLawTests =
    testList
        "Conformance.functionVerify"
        [ // Phase 79 — compare-and-append (optimistic concurrency) over the StreamWitness.
          testCase "casLaws certify match≡append + stale-rejection + race-serialisation (Phase 79)"
          <| fun _ ->
              // Phase 223 — the stratified reference generator, so the refused-op guard is reached
              // by the generator's shape.
              let results =
                  Conformance.casLaws sw stratifiedStreamGen OpStream.defaultHash 4242 200

              Expect.equal
                  (List.length results)
                  6
                  "match + stale + race laws, the two Phase 223 guards, and the Phase 297 race-arm guard"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "casLaws failed under defaultHash:\n%s" (String.concat "\n" fails)

              // the CAS is over head identity, so it is HashFn-agnostic — green under a wide HashFn too.
              let wide = Conformance.casLaws sw stratifiedStreamGen wideHash 4242 200

              Expect.isTrue
                  (wide |> List.forall (fun r -> r.Passed))
                  (sprintf "casLaws green under a wide HashFn: %A" (wide |> List.filter (fun r -> not r.Passed)))

              // seed-replay determinism
              Expect.equal
                  (Conformance.casLaws sw stratifiedStreamGen OpStream.defaultHash 4242 200)
                  results
                  "same seed ⇒ identical report"

          testCase "Phase 223 — a StreamGen that draws no refusal turns casLaws RED, on the refused-op guard alone"
          <| fun _ ->
              // The must-fail case: `Inc` only, so `match ≡ append` never compares a refusal. Every
              // subject law still passes — which is exactly the green this guard exists to refuse.
              let results =
                  Conformance.casLaws sw refusalFreeStreamGen OpStream.defaultHash 4242 200

              Expect.equal
                  (results |> List.filter (fun r -> not r.Passed) |> List.map (fun r -> r.Law))
                  [ SampleAdequacy.lawPrefix "Conformance.casLaws"
                    + "the sample reached every refused op the laws distinguish" ]
                  "exactly the refused-op guard is red" ]

/// The counter reducer driven by a generator that only ever increments, so it never reaches a
/// refusal. Every subject law is green over it, and until Phase 220 `certifyStream` certified it —
/// totality included, although no op it drew could have exercised a typed refusal.
let private incOnlyGen: StreamGen<CounterOp, int> =
    { State0 = 0
      Op =
        fun rng ->
            let n, r = ConfRng.intBelow 5 rng
            Inc n, r }

[<Tests>]
let reducerGuardTests =
    testList
        "Conformance.refusableFamilies"
        [ testCase "go-red: a reducer whose generator draws no refusal starves the guard, and certifyStream goes RED"
          <| fun _ ->
              let results = Conformance.reducer sw.Apply incOnlyGen None 314 200

              Expect.isTrue
                  (subjectOf results |> List.forall (fun r -> r.Passed))
                  "every subject law is green — which is the problem the guard exists for"

              Expect.isFalse
                  (guardNamed "Conformance.reducer" "refused op" results).Passed
                  "the refused side is starved"

              Expect.isTrue (guardNamed "Conformance.reducer" "accepted op" results).Passed "the accepted side is not"

              let measured =
                  SampleAdequacy.cases "Conformance.reducer" (Guarded [ "accepted"; "refused" ]) 200 results

              Expect.isTrue (SampleAdequacy.isVacuous measured) "the census reads it as starved, not as a count"

              let report = Conformance.certifyStream sw incOnlyGen OpStream.defaultHash 271 200

              Expect.isFalse
                  report.AllPassed
                  "the aggregate verdict MOVES: this domain certified green before Phase 220"

              Expect.equal
                  (List.length report.Results)
                  10
                  "a starved guard does not short-circuit the stream laws — reducer (2 + 2 guards) + stream (4 + 2 guards, Phase 245; the JSONL round trip, Phase 301)" ]

/// One op, always the same one: every chain is non-empty, and every tamper the family draws
/// encodes identically to the op it would replace, so the tamper law never runs. A non-empty
/// chain is therefore not the evidence that law needs; a TAMPERED one is.
let private oneOpGen: StreamGen<CounterOp, int> =
    { State0 = 0
      Op = fun rng -> Inc 1, rng }

[<Tests>]
let streamAdequacyTests =
    testList
        "Conformance.streamAdequacy"
        [ testCase "the reference stream generator reaches both guarded sides of streamLaws"
          <| fun _ ->
              let results = Conformance.streamLaws sw streamGen OpStream.defaultHash 4242 200

              for side in [ "accepted op"; "tampered chain" ] do
                  let g = guardNamed "Conformance.streamLaws" side results
                  Expect.isTrue g.Passed (sprintf "streamLaws: %s — %A" side g.Counterexample)

          testCase "go-red: a generator that refuses every op starves both sides, never green"
          <| fun _ ->
              let results = Conformance.streamLaws sw allRefusedGen OpStream.defaultHash 4242 200

              Expect.isTrue
                  (subjectOf results |> List.forall (fun r -> r.Passed))
                  "every subject law holds over an empty chain — which is the problem the guard exists for"

              for side in [ "accepted op"; "tampered chain" ] do
                  let g = guardNamed "Conformance.streamLaws" side results
                  Expect.isFalse g.Passed (sprintf "the %s side is starved" side)

                  Expect.stringContains
                      (defaultArg g.Counterexample "")
                      "=0"
                      "the red guard carries the count it reached, not merely that it failed"

              let measured =
                  SampleAdequacy.cases "Conformance.streamLaws" (Guarded [ "accepted"; "tampered chain" ]) 200 results

              Expect.isTrue (SampleAdequacy.isVacuous measured) "the census reads the run as starved"

              Expect.equal
                  (Families.adequacyToken [ "Conformance.streamLaws", measured ] "Conformance.streamLaws")
                  "guarded-starved"
                  "and the adequacy cell says `guarded-starved`, never a pass"

          testCase "go-red: one repeated op builds non-empty chains it can never tamper, and that side is starved"
          <| fun _ ->
              let results = Conformance.streamLaws sw oneOpGen OpStream.defaultHash 4242 200

              Expect.isTrue (guardNamed "Conformance.streamLaws" "accepted op" results).Passed "every draw was accepted"

              Expect.isFalse
                  (guardNamed "Conformance.streamLaws" "tampered chain" results).Passed
                  "but no chain was ever tampered, so the tamper law asserted nothing" ]
