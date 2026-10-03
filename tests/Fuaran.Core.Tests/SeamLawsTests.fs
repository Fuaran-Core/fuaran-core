module Fuaran.Core.Tests.SeamLawsTests

// Phase 331 — the tests of the kit's seam laws (`SeamLaws.fs`): capability, query, registry, pack loading
// and the column aggregate's null-skip semantics, moved verbatim from `ConformanceTests.fs`.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2
open Fuaran.Core.Tests.Reference.Counter
open Fuaran.Core.Tests.ConformanceTests

[<Tests>]
let seamLawTests =
    testList
        "Conformance"
        [ // Phase 30 — the invocable-capability laws; Phase 210 added the three envelope laws, Phase 229 the slotted-artifact law, Phase 319 the two capture-effect laws.
          testCase "capabilityLaws certify validation + replay + enumeration + round-trip + envelope (Phase 30)"
          <| fun _ ->
              let results = Conformance.capabilityLaws 4242 200

              Expect.equal
                  (List.length results)
                  10
                  "validation + replay + enumeration + round-trip + the three envelope laws + the slotted-artifact law (Phase 229) + the capture-covers law and its under-declaration converse (Phase 319) reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "capabilityLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.capabilityLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 46 — the data-acquisition Query laws; Phase 198 added the three envelope laws.
          testCase "queryLaws certify param-validation + replay + enumeration + round-trip + envelope (Phase 46)"
          <| fun _ ->
              let results = Conformance.queryLaws 4242 200

              Expect.equal
                  (List.length results)
                  11
                  "validation + replay + enumeration + round-trip + the three envelope laws + the widening relation + the typed fault + paging (Phase 316) + the registration refusal (Phase 316) reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "queryLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.queryLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 50 — the signature-typed function registry laws.
          testCase "registryLaws certify findable + non-match + narrowing + default-deny dispatch (Phase 50)"
          <| fun _ ->
              let results = Conformance.registryLaws 4242 200

              Expect.equal
                  (List.length results)
                  11
                  "findable + non-match + narrowing + default-deny + space-relation + the six lifecycle laws (Phase 316) reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "registryLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.registryLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 57 — the content-pack loading-contract laws.
          testCase
              "packLoadingLaws certify load round-trip + version-mismatch + default-deny + shape-derived version (Phase 57)"
          <| fun _ ->
              let results = Conformance.packLoadingLaws 4242 200

              Expect.equal
                  (List.length results)
                  5
                  "round-trip + version-mismatch + unknown-base + shape-derived + unload (Phase 316) laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "packLoadingLaws failed:\n%s" (String.concat "\n" fails)

              // seed-replay determinism
              Expect.equal (Conformance.packLoadingLaws 4242 200) results "same seed ⇒ identical report"

          // Phase 36's aggregate-parity law (Column.aggregate as the single source GroupBy calls) was
          // split by Phase 257: its parity half is `aggregateParityLaws`, which reads the dataframe
          // layer and left this repository with it in Phase 258 (D66), and its null-skip half is
          // below.
          // Phase 257 — the null-skip half of Phase 36, a family of its own in the kit.
          testCase "aggregateNullSkipLaws certify Column.aggregate's null-skip semantics (Phase 36, split by 257)"
          <| fun _ ->
              let results = Conformance.aggregateNullSkipLaws 4242 200
              // Phase 276 — the null-skip law, then the column-type guard.
              Expect.equal (List.length results) 2 "the null-skip law and the column-type guard reported"

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s — %A" r.Law r.Counterexample)

              Expect.equal (Conformance.aggregateNullSkipLaws 4242 200) results "same seed ⇒ identical report"

              // The semantics the law pins, read directly once so the sample's verdict has a
              // fixed point beside it: a Null is not counted.
              let col = Column.create "c" IntType [ Int 1; Null; Int 2 ]

              Expect.equal
                  (Column.aggregate Count col)
                  (Ok(Int 2))
                  "Count over [1; null; 2] counts the two present cells"

              // ... and over a decimal column, a Null is not summed: the exact `Sum` skips it.
              let dec = Column.create "d" DecimalType [ Decimal "0.1"; Null; Decimal "0.2" ]
              Expect.equal (Column.aggregate Sum dec) (Ok(Decimal "0.3")) "Sum over [0.1; null; 0.2] is 0.3, exactly"

          // Phase 276 — the column-type guard goes red on a run that never draws a decimal column. One
          // iteration draws ONE column type, so it can never reach all three: the subject law holds
          // and the guard alone is red, naming what it missed.
          testCase "aggregateNullSkipLaws goes red, on its column-type guard alone, when a run draws no decimal column"
          <| fun _ ->
              let mutable decimalMissed = 0

              for seed in 1..30 do
                  let results = Conformance.aggregateNullSkipLaws seed 1
                  let guard = List.last results

                  Expect.isTrue (List.head results).Passed (sprintf "seed %d: the subject law holds" seed)
                  Expect.isFalse guard.Passed (sprintf "seed %d: one iteration cannot reach three column types" seed)

                  let cx = guard.Counterexample |> Option.defaultValue ""

                  match cx.IndexOf "never reached" with
                  | -1 -> failtestf "seed %d: the red guard names nothing it missed: %s" seed cx
                  | at ->
                      if (cx.Substring at).Contains "decimal cell" then
                          decimalMissed <- decimalMissed + 1

              Expect.isGreaterThan decimalMissed 0 "some single-iteration run missed the decimal column and said so" ]
