module Fuaran.Core.Tests.SampleAdequacyTests

// Phase 121 — the sample-adequacy guard, its go-red proofs, and the census-completeness check
// that keeps the audit true after this session ends.
//
// The guard itself is proved the only way a guard can be: by making it fail. Every test below that
// asserts green is paired with one that perturbs the sample and asserts red, because a coverage
// guard which cannot go red is exactly the thing it exists to detect.

open System
open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  the guard's own behaviour
// ---------------------------------------------------------------------------

let private cx (r: LawResult) =
    match r.Counterexample with
    | Some c -> c
    | None -> failtestf "law %s carried no counterexample" r.Law

[<Tests>]
let guardTests =
    testList
        "SampleAdequacy.guard"
        [

          testCase "a verdict reached by nobody fails, and the failure carries every count"
          <| fun _ ->
              let green =
                  SampleAdequacy.reached "fam" "outcome" 42 [ "folded", 73; "halted", 227 ]

              Expect.isTrue green.Passed "both verdicts were reached"
              Expect.isNone green.Counterexample "a green guard says nothing further"

              let red = SampleAdequacy.reached "fam" "outcome" 42 [ "folded", 150; "halted", 0 ]

              Expect.isFalse red.Passed "a verdict reached zero times fails the family"
              let c = cx red
              Expect.stringContains c "folded=150" "the count it did reach is reported"
              Expect.stringContains c "halted=0" "the count it missed is reported"
              Expect.stringContains c "never reached halted" "the missed verdict is named"
              Expect.stringContains c "seed=42" "the failure is reproducible from the seed"

          testCase "the remedy is to widen the generator, not to re-seed or iterate harder"
          <| fun _ ->
              // Phase 106 measured the trap this sentence exists to close: across seeds 2200-2260
              // only 3 of 61 produced any folding lane set, and the best produced one in 300. A
              // guard turned green by seed-hunting is a law certified by a single trial.
              let c = cx (SampleAdequacy.reached "fam" "outcome" 1 [ "x", 0 ])
              Expect.stringContains c "WIDEN THE GENERATOR" "the counterexample names the right remedy"

          testCase "a demand that demands nothing fails rather than passing"
          <| fun _ ->
              let empty = SampleAdequacy.reached "fam" "outcome" 1 []
              Expect.isFalse empty.Passed "declaring no verdicts is not a way to be adequate"
              Expect.stringContains (cx empty) "demands nothing" "and it says so"

          testCase "a span shorter than the law needs fails, naming the width it reached"
          <| fun _ ->
              Expect.isTrue (SampleAdequacy.spanned "fam" "rows" 7 42 9 60).Passed "9 >= 7"
              let red = SampleAdequacy.spanned "fam" "rows" 7 42 5 60
              Expect.isFalse red.Passed "5 < 7"
              let c = cx red
              Expect.stringContains c "widest rows was 5" "the width it reached is reported"
              Expect.stringContains c "at least 7" "the width the law needs is reported"

          testCase "check runs a family's declared demands in declaration order"
          <| fun _ ->
              let demands: AdequacyDemand<int> list =
                  [ ReachesEvery("parity", [ "even"; "odd" ], (fun n -> [ (if n % 2 = 0 then "even" else "odd") ]))
                    Spans("magnitude", 10, id) ]

              let green = SampleAdequacy.check "fam" 1 demands [ 1; 2; 11 ]
              Expect.equal (List.length green) 2 "one law per demand"
              Expect.isTrue (green |> List.forall (fun r -> r.Passed)) "the sample reached both and spanned far enough"

              let red = SampleAdequacy.check "fam" 1 demands [ 2; 4; 6 ]
              Expect.isFalse (List.item 0 red).Passed "no odd sample"
              Expect.isFalse (List.item 1 red).Passed "nothing reached 10"
              Expect.stringContains (cx (List.item 0 red)) "even=3" "the counts come from the sample" ]

// ---------------------------------------------------------------------------
//  the two motivating instances
// ---------------------------------------------------------------------------
//  The guard's two motivating instances — Phase 115's table widening reverted, and the corpus as it
//  stood before Phase 212 — are go-red proofs over `IncrementalDelta`, the equivalence family over
//  the dataframe layer's incremental seam. They left this repository with that layer in Phase 258
//  (DECISIONS.md D66) and run in the compute repository's suite. The guard's own behaviour above is
//  proved without them, over fixed results, and the witness-taking families' adequacy is held by
//  their own suites.

let private adequacyLaws (rs: LawResult list) =
    rs |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

// ---------------------------------------------------------------------------
//  census completeness — the half a declaration cannot check about itself
// ---------------------------------------------------------------------------

/// Every law family the kit ships — the roster `Fuaran.Core.Families` declares, which is itself
/// held to REFLECTION OVER RETURN TYPE across the shipped assembly by `ConformanceFamiliesTests`.
///
/// Phase 184 replaced what stood here, and the replacement is the point rather than a tidy-up.
/// This function used to do its own reflection over method NAMES — anything ending in `Laws` /
/// `LawsWith`, plus the two bare `laws` spellings — over four named modules. A naming convention
/// is not what a law family IS, and three families are not spelled that way: `opAlgebra`,
/// `reducer` and `compositionPilot` were invisible here, and therefore absent from the census,
/// and therefore absent from the roster a consumer's conformance projection quantifies over. Two
/// of the three are the families `certify` and `certifyStream` are built from.
///
/// Reading the roster keeps this file's completeness claim exactly as strong as it was and moves
/// the derivation to one place: a family answers with `LawResult list` or it is not a family, and
/// no module list is restated here for a new module to fall outside of.
let private shippedFamilies () : string list = KitRoster.ids

[<Tests>]
let censusTests =
    testList
        "SampleAdequacy.census"
        [

          testCase "every law family the kit ships is classified in the census"
          <| fun _ ->
              // This is the half the census structurally cannot do for itself. A declaration
              // quantifies over what it names, so a family nobody enrolled produces no finding at
              // any grade — which is how a store can hold nine files while the class holds twelve.
              // The kit's declared roster closes it — itself held to reflection over the shipped
              // assembly BY RETURN TYPE — so a family added without answering the adequacy
              // question fails to ship rather than passing silently.
              let declared = KitRoster.census |> List.map fst |> Set.ofList
              let shipped = shippedFamilies ()
              let unclassified = shipped |> List.filter (fun f -> not (Set.contains f declared))

              Expect.isEmpty
                  unclassified
                  (sprintf
                      "these law families are not in the census (SampleAdequacy.census) — declare each as Guarded or Unconditional (with the reason): %A"
                      unclassified)

          testCase "the census names no family the kit no longer ships"
          <| fun _ ->
              // The other direction, and it matters for the same reason: a row for a family that
              // was renamed or removed reads as coverage while covering nothing.
              let shipped = shippedFamilies () |> Set.ofList

              let stale =
                  KitRoster.census
                  |> List.map fst
                  |> List.filter (fun f -> not (Set.contains f shipped))

              Expect.isEmpty stale (sprintf "these census rows name no shipped law family: %A" stale)

          testCase "the census carries no duplicate row and no empty reason"
          <| fun _ ->
              let names = KitRoster.census |> List.map fst

              Expect.equal
                  (List.length (List.distinct names))
                  (List.length names)
                  "a family classified twice could be classified two ways"

              for name, cls in KitRoster.census do
                  match cls with
                  | Guarded dims ->
                      Expect.isNonEmpty dims (sprintf "%s is Guarded but names no dimension" name)

                      for d in dims do
                          Expect.isTrue (d.Trim() <> "") (sprintf "%s names an empty dimension" name)
                  | Unconditional why ->
                      Expect.isTrue
                          (why.Trim().Length > 10)
                          (sprintf
                              "%s is Unconditional with no usable reason — the reason is what lets the next reader CHECK the classification rather than trust it"
                              name)

          testCase "every family the census calls Guarded actually emits an adequacy law"
          <| fun _ ->
              // The classification is a claim about the code, so it is checked against the code for
              // the families that can be run without a domain witness. The witness-taking ones are
              // checked by their own suites, whose `expectGreen` now covers the guard they gained.
              let emits (rs: LawResult list) = not (List.isEmpty (adequacyLaws rs))

              Expect.isTrue (emits (Conformance.dirtyPropagationLaws 4242 20)) "dirtyPropagationLaws"
              Expect.isTrue (emits (Conformance.propagationEvalLaws 4242 20)) "propagationEvalLaws"

              Expect.isTrue
                  (emits (Conformance.capabilityPipelineIncrementalLaws 4242 20))
                  "capabilityPipelineIncrementalLaws"

          testCase "no family the census calls Unconditional quietly emits one instead"
          <| fun _ ->
              // The inverse claim, over the seed/iteration-only families — a family that gained a
              // guard without moving its census row would leave the census describing the old code.
              let emits (rs: LawResult list) = not (List.isEmpty (adequacyLaws rs))

              for name, run in
                  [ "capabilityLaws", Conformance.capabilityLaws
                    "queryLaws", Conformance.queryLaws
                    "registryLaws", Conformance.registryLaws
                    "packLoadingLaws", Conformance.packLoadingLaws
                    "aggregateNullSkipLaws", Conformance.aggregateNullSkipLaws
                    "columnarValidatorLaws", Conformance.columnarValidatorLaws
                    "deferredLaws", Conformance.deferredLaws
                    "capabilityPipelineLaws", Conformance.capabilityPipelineLaws
                    "canonicalFloatLaws", Conformance.canonicalFloatLaws ] do
                  match KitRoster.census |> List.tryFind (fun (n, _) -> n = "Conformance." + name) with
                  | Some(_, Unconditional _) ->
                      Expect.isFalse
                          (emits (run 4242 20))
                          (sprintf "%s emits an adequacy law but the census calls it Unconditional" name)
                  | Some(_, Guarded _) ->
                      Expect.isTrue
                          (emits (run 4242 20))
                          (sprintf "%s is censused Guarded but emits no adequacy law" name)
                  | None -> failtestf "%s is missing from the census" name ]
