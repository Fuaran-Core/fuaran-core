module Fuaran.Core.Tests.SampleAdequacyTests

// Phase 121 — the sample-adequacy guard, its go-red proofs, and the census-completeness check
// that keeps the audit true after this session ends.
//
// The guard itself is proved the only way a guard can be: by making it fail. Every test below that
// asserts green is paired with one that perturbs the sample and asserts red, because a coverage
// guard which cannot go red is exactly the thing it exists to detect.

open System
open System.Reflection
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
//  the two motivating instances, as go-red proofs
// ---------------------------------------------------------------------------

let private adequacyLaws (rs: LawResult list) =
    rs |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

[<Tests>]
let motivatingInstanceTests =
    testList
        "SampleAdequacy.instances"
        [

          testCase "reverting Phase 115's table widening turns the equivalence family's guard red"
          <| fun _ ->
              // Phase 115 raised the equivalence family's generated tables from one-to-five rows to
              // one-to-nine, having measured that most held ONE row, so no tie between a named and
              // an unnamed row ever arose and a merge with no stability tiebreak passed every seed.
              // Nothing was watching the table width; this is that watch, and this is its teeth.
              let span (rs: LawResult list) =
                  rs |> List.find (fun r -> r.Law.Contains "spans the source rows range")

              for seed in [ 1; 7; 99; 20260821 ] do
                  Expect.isTrue
                      (span (IncrementalDelta.lawsWith 9 seed 60)).Passed
                      (sprintf "seed %d: the shipped bound spans the width the order laws read" seed)

                  let narrowed = span (IncrementalDelta.lawsWith 5 seed 60)

                  Expect.isFalse narrowed.Passed (sprintf "seed %d: the pre-115 bound must fail the span demand" seed)

                  Expect.stringContains (cx narrowed) "at least 7" "and it names the width the laws need"

          testCase "the equivalence family's guard is green at the shipped bound, across seeds"
          <| fun _ ->
              for seed in [ 1; 7; 99; 20260821 ] do
                  for r in adequacyLaws (IncrementalDelta.laws seed 60) do
                      Expect.isTrue r.Passed (sprintf "seed %d — %s: %A" seed r.Law r.Counterexample)

          testCase "the guard reports the refresh classes the equivalence family's laws branch on"
          <| fun _ ->
              // A guard that passed while demanding nothing would be worse than none, so the
              // dimension's own vocabulary is pinned here rather than left to the demand list.
              let verdicts =
                  IncrementalDelta.demands
                  |> List.tryPick (function
                      | ReachesEvery("refresh class", vs, _) -> Some vs
                      | _ -> None)

              Expect.equal
                  verdicts
                  (Some [ "declined"; "row-restricted"; "group-restricted"; "merged-order-restricted" ])
                  "every class the family's laws distinguish is demanded"

          // The Phase 100 instance — 150 halting trials out of 150, the folding branch never
          // executed — has its go-red proof in `FoldConfluenceTests`: an order-sensitive witness
          // with a blind footprint never conflicts, so the guard reports `halted=0` and refuses to
          // let a vacuous halt law read as a certification. It is not duplicated here.

          testCase "a family whose sample misses a verdict fails even though its laws all hold"
          <| fun _ ->
              // The whole claim, in one assertion: green laws plus a red guard. The blind-footprint
              // witness's halt-determinism law is TRUE (nothing halted, so nothing halted wrongly)
              // and the guard is what stops that reading as evidence.
              let narrowed = IncrementalDelta.lawsWith 1 7 60

              let core =
                  narrowed |> List.filter (fun r -> not (r.Law.StartsWith "sample adequacy"))

              Expect.isTrue
                  (core |> List.forall (fun r -> r.Passed))
                  (sprintf "the laws themselves still hold over one-row tables: %A" core)

              Expect.isFalse
                  (adequacyLaws narrowed |> List.forall (fun r -> r.Passed))
                  "and the guard refuses to certify the sample they held over" ]

// ---------------------------------------------------------------------------
//  census completeness — the half a declaration cannot check about itself
// ---------------------------------------------------------------------------

/// Every public law entry point the kit ships, found by reflection rather than by a list, because a
/// list is exactly what cannot notice a family nobody added to it.
let private shippedFamilies () : string list =
    let asm = typeof<LawResult>.Assembly

    let isLawEntry (m: MethodInfo) =
        let n = m.Name

        n.EndsWith("Laws", StringComparison.Ordinal)
        || n.EndsWith("LawsWith", StringComparison.Ordinal)
        || n = "laws"
        || n = "lawsWith"

    [ for moduleName in [ "Conformance"; "FoldConfluence"; "IncrementalDelta"; "WireNullTolerance" ] do
          match asm.GetType("Fuaran.Core." + moduleName) with
          | null -> failtestf "the kit no longer ships a module named %s" moduleName
          | t ->
              for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.Static ||| BindingFlags.DeclaredOnly) do
                  if isLawEntry m then
                      yield moduleName + "." + m.Name ]
    |> List.distinct
    |> List.sort

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
              // Reflection is what closes it: a family added without answering the adequacy
              // question fails to ship rather than passing silently.
              let declared = SampleAdequacy.census |> List.map fst |> Set.ofList
              let shipped = shippedFamilies ()
              let unclassified = shipped |> List.filter (fun f -> not (Set.contains f declared))

              Expect.isEmpty
                  unclassified
                  (sprintf
                      "these law families are not in SampleAdequacy.census — declare each as Guarded or Unconditional (with the reason): %A"
                      unclassified)

          testCase "the census names no family the kit no longer ships"
          <| fun _ ->
              // The other direction, and it matters for the same reason: a row for a family that
              // was renamed or removed reads as coverage while covering nothing.
              let shipped = shippedFamilies () |> Set.ofList

              let stale =
                  SampleAdequacy.census
                  |> List.map fst
                  |> List.filter (fun f -> not (Set.contains f shipped))

              Expect.isEmpty stale (sprintf "these census rows name no shipped law family: %A" stale)

          testCase "the census carries no duplicate row and no empty reason"
          <| fun _ ->
              let names = SampleAdequacy.census |> List.map fst

              Expect.equal
                  (List.length (List.distinct names))
                  (List.length names)
                  "a family classified twice could be classified two ways"

              for name, cls in SampleAdequacy.census do
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

              Expect.isTrue (emits (IncrementalDelta.laws 7 20)) "IncrementalDelta.laws"

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
                    "aggregateParityLaws", Conformance.aggregateParityLaws
                    "columnarOpLaws", Conformance.columnarOpLaws
                    "columnarValidatorLaws", Conformance.columnarValidatorLaws
                    "incrementalLaws", Conformance.incrementalLaws
                    "paramLaws", Conformance.paramLaws
                    "deferredLaws", Conformance.deferredLaws
                    "capabilityPipelineLaws", Conformance.capabilityPipelineLaws
                    "canonicalFloatLaws", Conformance.canonicalFloatLaws
                    "leaseLaws", Conformance.leaseLaws ] do
                  match SampleAdequacy.census |> List.tryFind (fun (n, _) -> n = "Conformance." + name) with
                  | Some(_, Unconditional _) ->
                      Expect.isFalse
                          (emits (run 4242 20))
                          (sprintf "%s emits an adequacy law but the census calls it Unconditional" name)
                  | Some(_, Guarded _) ->
                      Expect.isTrue
                          (emits (run 4242 20))
                          (sprintf "%s is censused Guarded but emits no adequacy law" name)
                  | None -> failtestf "%s is missing from the census" name ]
