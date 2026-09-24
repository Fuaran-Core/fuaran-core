module Fuaran.Core.Tests.ConformanceVacuityTests

// Phase 196 — vacuity measured, per law family, at this repository's own reference witness.
//
// A `LawResult` records that a law HELD. `SampleAdequacy` (Phase 121) added the question "was the
// law reached at all?" and answers it INSIDE a run — and then discards what it measured. So the
// outside of a run is unchanged: a family that exercised twelve hundred cases and a family that
// exercised none report the same green, and a consumer's generated conformance census renders the
// same "adopted" cell for both. The estate's own memory names that failure class twice already
// (`expecto-filter-dot-separator-vacuous-green`, `lastexitcode-vacuous-green-sweep`); this file
// exists so the kit does not manufacture a third instance.
//
// What it is: EVERY law family the roster ships, run once here, at the witnesses this repository
// already certifies with, with the measurement kept. Three things follow, and the third is the one
// that could not be had any other way:
//
//   * a family the kit ships with no reference run is red, both directions, against
//     `Fuaran.Core.Families` — so the run set cannot quietly fall behind the roster;
//   * every family reaches a NON-ZERO, non-starved count here, which is what lets a consumer read
//     a zero in its own census as a fact about its own witness rather than about the kit;
//   * `attestationLaws` at `OpStream.noAttestation` is shown VACUOUS — five green laws over zero
//     exercised cases — which is the concrete instance the phase was filed for, and the go-red for
//     everything above it.
//
// The runs use the same seeds and iteration counts as the suites that certify each family, so this
// file measures the run the repository actually stands behind rather than a cheaper one drawn for
// the census.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2

// ---------------------------------------------------------------------------
//  the reference run
// ---------------------------------------------------------------------------

/// One family's reference run: its roster id, the sample size it was driven over, and what it
/// answered. `Iterations` is the count the family's own sample is sized by — the iteration
/// argument for a drawn family, the corpus length for `constructThenEncodeLaws`, the search budget
/// for `hashFnAdversarialLaws`. The family knows which; this record carries the number.
type Run =
    { Id: string
      Iterations: int
      Results: LawResult list }

let private run id iterations results =
    { Id = id
      Iterations = iterations
      Results = results }

/// A signing sink, so the attestation family's evidence is BUILT. The unsigned path is
/// `noAttestationVacuityLaws`, and the test below shows what this family does without a signer.
let private sink = ConformanceTests.keyedSink "test-key-0"

/// The honest footprint `concurrencyLaws` itself supplies — the `With` entry point takes it as a
/// parameter so a test can under-approximate it and prove the law has teeth. The census wants the
/// honest one.
let private honestFootprint = Ops.footprint nodew idw

/// Every law family the kit ships, run once. Evaluated at most once per process: several of these
/// are three-hundred-iteration property runs.
let private runs =
    lazy
        ([
           // ---- the base run ----
           run "Conformance.witnessLaws" 100 (Conformance.witnessLaws nodew idw ConformanceTests.opGen 7 100)
           run "Conformance.opAlgebra" 200 (Conformance.opAlgebra nodew idw ConformanceTests.opGen 999 200)
           run "Conformance.diffLaws" 200 (Conformance.diffLaws nodew idw ConformanceTests.opGen 4242 200)
           run
               "Conformance.streamLaws"
               200
               (Conformance.streamLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 4242 200)
           run
               "Conformance.reducer"
               200
               (Conformance.reducer ConformanceTests.sw.Apply ConformanceTests.streamGen None 314 200)

           // ---- tree-shaped opt-ins ----
           run
               "Conformance.diffContainedLaws"
               200
               (Conformance.diffContainedLaws nodew idw ContainedOpsTests.containerGen 4242 200)
           run "Conformance.normalizeLaws" 200 (Conformance.normalizeLaws nodew idw ConformanceTests.opGen 1234 200)
           run
               "Conformance.containerLaws"
               200
               (Conformance.containerLaws nodew idw ContainedOpsTests.containerGen 1610 200)
           run
               "Conformance.mergeConflictLaws"
               300
               (Conformance.mergeConflictLaws nodew idw ConformanceTests.opGen 7171 300)
           run
               "Conformance.reconcileLaws"
               300
               (Conformance.reconcileLaws nodew idw ConformanceTests.opGen encNode 5353 300)
           run
               "Conformance.footprintLaws"
               300
               (Conformance.footprintLaws nodew idw ConformanceTests.opGen encNode 4242 300)
           run
               "Conformance.concurrencyLaws"
               300
               (Conformance.concurrencyLaws nodew idw ConformanceTests.opGen encNode 8080 300)
           run
               "Conformance.concurrencyLawsWith"
               300
               (Conformance.concurrencyLawsWith honestFootprint nodew idw ConformanceTests.opGen encNode 8080 300)
           run
               "Conformance.arbitrationLaws"
               300
               (Conformance.arbitrationLaws nodew idw ConformanceTests.opGen encNode 8585 300)
           run
               "Conformance.keyedChildrenLaws"
               200
               (Conformance.keyedChildrenLaws
                   ConformanceTests.keyw
                   ConformanceTests.knodew
                   idw
                   ConformanceTests.kGen
                   1890
                   200)

           // ---- stream-shaped opt-ins ----
           run
               "Conformance.snapshotLaws"
               100
               (Conformance.snapshotLaws
                   ConformanceTests.sw
                   ConformanceTests.streamGen
                   string
                   OpStream.defaultHash
                   321
                   100)
           run
               "Conformance.snapshotLawsWith"
               100
               (Conformance.snapshotLawsWith
                   OpStream.canonicalConfig
                   ConformanceTests.sw
                   ConformanceTests.streamGen
                   string
                   OpStream.defaultHash
                   321
                   100)
           run
               "Conformance.dagLaws"
               100
               (Conformance.dagLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 99 100)
           run
               "Conformance.casLaws"
               200
               (Conformance.casLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 4242 200)
           run
               "Conformance.idempotencyLaws"
               200
               (Conformance.idempotencyLaws
                   ConformanceTests.sw.Encode
                   ConformanceTests.sw
                   ConformanceTests.streamGen
                   OpStream.defaultHash
                   8282
                   200)
           run
               "Conformance.hashFnLaws"
               200
               (Conformance.hashFnLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 4242 200)
           run
               "Conformance.attributedLaws"
               200
               (Conformance.attributedLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 4242 200)
           run
               "Conformance.codecInjectivityLaws"
               200
               (Conformance.codecInjectivityLaws ConformanceTests.sw ConformanceTests.streamGen 1450 200)
           run
               "Conformance.noAttestationVacuityLaws"
               200
               (Conformance.noAttestationVacuityLaws
                   ConformanceTests.sw
                   ConformanceTests.streamGen
                   OpStream.defaultHash
                   4242
                   200)
           run
               "Conformance.attestationLaws"
               200
               (Conformance.attestationLaws
                   ConformanceTests.sw
                   ConformanceTests.streamGen
                   sink
                   OpStream.defaultHash
                   4242
                   200)

           // ---- artifact-witness opt-ins ----
           run
               "Conformance.compositionLaws"
               200
               (Conformance.compositionLaws artw artw id ConformanceTests.genComposition 4242 200)
           run
               "Conformance.compositionPilot"
               200
               (Conformance.compositionPilot
                   artw
                   artw2
                   embedToR
                   encNode
                   encNode2
                   ConformanceTests.genComposition2
                   4242
                   200)
           run
               "Conformance.memoLaws"
               200
               (Conformance.memoLaws artw encNode ConformanceTests.genMemo OpStream.defaultHash 4242 200)
           run
               "Conformance.memoSoundnessLaws"
               50
               (let underDeclared =
                   { RNode.node
                         "ud"
                         "template"
                         [ { RNode.hole "c" "field" "count" (ValueHole(IntRange(0, 5))) with
                               Eff = { Host = Pure; Determinism = Clock } } ] with
                       Eff = Effect.pureDeterministic }

                let underDeclaredArgs = Map.ofList [ "ud/c", ValueArg "3" ]

                Conformance.memoSoundnessLaws artw encNode underDeclared underDeclaredArgs 4242 50)
           run
               "Conformance.functionVerifyLaws"
               200
               (Conformance.functionVerifyLaws
                   artw
                   (ConformanceTests.tplCount (0, 5))
                   (ConformanceTests.tplCount (0, 10))
                   ConformanceTests.countReg
                   ConformanceTests.genParamsFor
                   777
                   200)
           run
               "Conformance.verifyHonestyLaws"
               200
               (let mkSoundDet (d: DeterminismSource) =
                   { ConformanceTests.tplCount (0, 5) with
                       Eff = { Host = Pure; Determinism = d } }

                let mkBrokenDet (d: DeterminismSource) =
                    { ConformanceTests.tplCount (0, 10) with
                        Eff = { Host = Pure; Determinism = d } }

                Conformance.verifyHonestyLaws
                    artw
                    mkSoundDet
                    mkBrokenDet
                    ConformanceTests.countReg
                    ConformanceTests.genParamsFor
                    777
                    200)
           run
               "Conformance.encoderInjectivityLaws"
               200
               (Conformance.encoderInjectivityLaws artw encNode ConformanceTests.genTree 4242 200)

           // ---- the remaining witnessed opt-ins ----
           run
               "Conformance.projectionLaws"
               200
               (Conformance.projectionLaws
                   ProjectionTests.pw
                   ProjectionTests.applyOps
                   ProjectionTests.wireEncode
                   ProjectionTests.genTree
                   42
                   200)
           run
               "Conformance.aiSurfaceLaws"
               200
               (Conformance.aiSurfaceLaws AiSurfaceTests.witness AiSurfaceTests.genNoteOp AiSurfaceTests.state0 1234 200)

           // ---- the fixture-only families: the kit supplies its own sample ----
           run
               "Conformance.captureReplayLaws"
               200
               (let encInt (n: int) = Json.render (JInt n)

                let decInt (s: string) =
                    Decode.parse s |> Result.bind Decode.asInt

                Conformance.captureReplayLaws encInt decInt ConfRng.next OpStream.defaultHash 31337 200)
           run
               "Conformance.transformLaws"
               LawVectorExport.iterations
               (Conformance.transformLaws
                   DataFrame.evalPipeline
                   (LawVectorExport.lawGen ())
                   LawVectorExport.seed
                   LawVectorExport.iterations)
           run
               "Conformance.constructThenEncodeLaws"
               (List.length ConstructThenEncodeTests.corpus)
               (Conformance.constructThenEncodeLaws
                   "reference"
                   ConstructThenEncodeTests.codec
                   (Some ConstructThenEncodeTests.honest)
                   ConstructThenEncodeTests.corpus)
           run
               "Conformance.hashFnAdversarialLaws"
               500000
               (Conformance.hashFnAdversarialLaws ConformanceTests.wideHash 500000 4242)
           run "Conformance.capabilityLaws" 200 (Conformance.capabilityLaws 4242 200)
           run "Conformance.queryLaws" 200 (Conformance.queryLaws 4242 200)
           run "Conformance.registryLaws" 200 (Conformance.registryLaws 4242 200)
           run "Conformance.packLoadingLaws" 200 (Conformance.packLoadingLaws 4242 200)
           run "Conformance.aggregateParityLaws" 200 (Conformance.aggregateParityLaws 4242 200)
           run "Conformance.columnarOpLaws" 200 (Conformance.columnarOpLaws 4242 200)
           run "Conformance.columnarOpLawsWith" 200 (Conformance.columnarOpLawsWith ColumnOps.invert 4242 200)
           run "Conformance.columnarValidatorLaws" 200 (Conformance.columnarValidatorLaws 4242 200)
           run "Conformance.incrementalLaws" 200 (Conformance.incrementalLaws 4242 200)
           run "Conformance.paramLaws" 200 (Conformance.paramLaws 7714 200)
           run "Conformance.schemaWalkLaws" 300 (Conformance.schemaWalkLaws 1121 300)
           run "Conformance.deferredLaws" 200 (Conformance.deferredLaws 4242 200)
           run "Conformance.capabilityPipelineLaws" 200 (Conformance.capabilityPipelineLaws 4242 200)
           run
               "Conformance.capabilityPipelineIncrementalLaws"
               200
               (Conformance.capabilityPipelineIncrementalLaws 4242 200)
           run "Conformance.dirtyPropagationLaws" 200 (Conformance.dirtyPropagationLaws 4242 200)
           run "Conformance.propagationEvalLaws" 200 (Conformance.propagationEvalLaws 4242 200)
           run
               "Conformance.propagationEvaluatorLaws"
               200
               (Conformance.propagationEvaluatorLaws ConformanceTests.sheetw 2110 200)
           run "Conformance.canonicalFloatLaws" 500 (Conformance.canonicalFloatLaws 4242 500)
           run "Conformance.chainBreakReasonLaws" 120 (Conformance.chainBreakReasonLaws 5125 120)
           run "Conformance.dagBreakReasonLaws" 120 (Conformance.dagBreakReasonLaws 5147 120)
           run "Conformance.nowLaws" 150 (Conformance.nowLaws 1250 150)
           run "Conformance.slotParamLaws" 120 (Conformance.slotParamLaws 12500 120)

           // ---- the families outside `Conformance` ----
           run
               "FoldConfluence.laneFoldLaws"
               120
               (FoldConfluence.laneFoldLaws
                   FoldConfluenceTests.treeW
                   FoldConfluenceTests.treeFootprint
                   FoldConfluenceTests.treeHash
                   FoldConfluenceTests.treeLaneGen
                   3
                   1000
                   120)
           run
               "FoldConfluence.laneFoldLawsWith"
               120
               (FoldConfluence.laneFoldLawsWith
                   FoldConfluenceTests.treeW
                   FoldConfluenceTests.treeFootprint
                   OpStream.defaultHash
                   FoldConfluenceTests.treeHash
                   FoldConfluenceTests.treeLaneGen
                   3
                   1000
                   120)
           run "IncrementalDelta.laws" 60 (IncrementalDelta.laws 7 60)
           // The SHIPPED row bound (9) and the sample size its own suite sweeps at. A narrower
           // bound is the family's documented go-red, not a census run.
           run "IncrementalDelta.lawsWith" 100 (IncrementalDelta.lawsWith 9 7 100) ]
        : Run list)

/// The family's own adequacy class, which is what decides how its run is read. Looked up rather
/// than passed, because the census is the single declaration and this file must not become a
/// second one.
let private classOf (id: string) : AdequacyClass =
    match SampleAdequacy.census |> List.tryFind (fun (k, _) -> k = id) with
    | Some(_, k) -> k
    | None -> failwithf "%s has no SampleAdequacy.census row — the roster and the census disagree" id

/// The measured census: one `CaseCount` per family, in roster order. This is what the generated
/// `docs/conformance-families.{md,json}` render their `cases` column from.
let cases () : (string * CaseCount) list =
    runs.Value
    |> List.map (fun r -> r.Id, SampleAdequacy.cases r.Id (classOf r.Id) r.Iterations r.Results)
    |> List.sortBy fst

// ---------------------------------------------------------------------------

[<Tests>]
let vacuityTests =
    testList
        "Conformance.Vacuity"
        [

          testCase "every law family the kit ships has a reference run, and no run names a phantom"
          <| fun _ ->
              // Both directions, for the reason the roster's own completeness check runs both: a
              // missing run is a family measured by nobody, and a run naming nothing is a census
              // cell for a family that no longer exists.
              let ran = runs.Value |> List.map (fun r -> r.Id) |> Set.ofList
              let rostered = Set.ofList Families.ids

              Expect.isEmpty
                  (Set.difference rostered ran |> Set.toList)
                  "these law families have no reference run in ConformanceVacuityTests — add one in the commit that ships the family, or its census cell reads `unmeasured` forever"

              Expect.isEmpty (Set.difference ran rostered |> Set.toList) "these reference runs name no roster family"

          testCase "the reference run is green — a red law makes its count meaningless"
          <| fun _ ->
              // The counts below are only evidence if the laws they count held. A failing law here
              // is not this file's finding to report (the owning suite reports it properly); it is
              // the reason to stop trusting the census.
              let failed =
                  [ for r in runs.Value do
                        for l in r.Results do
                            if not l.Passed then
                                yield sprintf "%s — %s: %A" r.Id l.Law l.Counterexample ]

              Expect.isEmpty failed (sprintf "the reference run is not green:\n%s" (String.concat "\n" failed))

          testCase "no family is vacuous at the reference witness"
          <| fun _ ->
              // The claim the whole file exists to make, and the one that gives a CONSUMER's zero
              // its meaning: a family that reads `vacuous` over there is that consumer's witness,
              // never this kit.
              let vacuous =
                  cases ()
                  |> List.filter (snd >> SampleAdequacy.isVacuous)
                  |> List.map (fun (id, c) -> id + " → " + SampleAdequacy.renderCases c)

              Expect.isEmpty
                  vacuous
                  (sprintf
                      "these families certify nothing at this repository's own reference witness:\n%s"
                      (String.concat "\n" vacuous))

          testCase "attestationLaws at `noAttestation` is VACUOUS — five green laws over zero cases"
          <| fun _ ->
              // The concrete instance the phase was filed for, and the go-red for everything
              // above. `OpStream.attestHead noAttestation` is always `None`, so four of the five
              // laws assert nothing — and every one of them reports green, which is exactly what a
              // census cell used to render as "adopted".
              let results =
                  Conformance.attestationLaws
                      ConformanceTests.sw
                      ConformanceTests.streamGen
                      OpStream.noAttestation
                      OpStream.defaultHash
                      4242
                      200

              let subject =
                  results
                  |> List.filter (fun r ->
                      not (r.Law.StartsWith(SampleAdequacy.lawPrefix "Conformance.attestationLaws")))

              Expect.isTrue
                  (subject |> List.forall (fun r -> r.Passed))
                  "the five subject laws are green under the no-op sink — which is the problem, not the fix"

              let measured =
                  SampleAdequacy.cases "Conformance.attestationLaws" (classOf "Conformance.attestationLaws") 200 results

              Expect.isTrue (SampleAdequacy.isVacuous measured) "the run is vacuous"

              Expect.stringStarts
                  (SampleAdequacy.renderCases measured)
                  SampleAdequacy.vacuousToken
                  "and the census cell says so rather than rendering a count"

          testCase "the derivation itself goes red — measured on made-up runs, not on the tree"
          <| fun _ ->
              // A vacuity check that cannot report vacuity is the thing it exists to detect, so it
              // is exercised over inputs chosen here rather than over whatever the kit happens to
              // produce today.
              let subject law passed =
                  { Law = law
                    Passed = passed
                    Counterexample = None }

              let guard family passed =
                  { Law =
                      SampleAdequacy.lawPrefix family
                      + "the sample reached every arm the laws distinguish"
                    Passed = passed
                    Counterexample = None }

              let unconditional = Unconditional "each iteration builds both arms"

              let full =
                  SampleAdequacy.cases "M.f" unconditional 10 [ subject "a" true; subject "b" true ]

              Expect.equal full.Cases 20 "two subject laws over ten iterations is twenty assertions"
              Expect.isFalse (SampleAdequacy.isVacuous full) "a run that asserted something is not vacuous"
              Expect.equal (SampleAdequacy.renderCases full) "20" "and its cell is the count"

              let noIterations = SampleAdequacy.cases "M.f" unconditional 0 [ subject "a" true ]

              Expect.isTrue (SampleAdequacy.isVacuous noIterations) "zero iterations certifies nothing"

              Expect.equal
                  (SampleAdequacy.renderCases noIterations)
                  SampleAdequacy.vacuousToken
                  "and reads `vacuous`, never `0`"

              let noLaws = SampleAdequacy.cases "M.f" unconditional 10 []
              Expect.isTrue (SampleAdequacy.isVacuous noLaws) "no law is no assertion"

              // The guarded half: a green total over a starved dimension. This is the case a count
              // alone cannot see, and the one Phase 181 found in `columnarOpLaws`.
              let starved =
                  SampleAdequacy.cases "M.f" (Guarded [ "arm" ]) 10 [ subject "a" true; guard "M.f" false ]

              Expect.equal starved.Cases 10 "the subject law still ran ten times"

              Expect.isTrue
                  (SampleAdequacy.isVacuous starved)
                  "but a starved dimension is vacuity on the side the law is about"

              Expect.equal
                  starved.Starved
                  [ "the sample reached every arm the laws distinguish" ]
                  "and the cell names the dimension, stripped of the guard's own prefix"

              // A DELEGATING family emits its delegate's name in the guard — `columnarOpLaws` runs
              // `columnarOpLawsWith`, `IncrementalDelta.laws` runs `lawsWith` — and that guard is
              // still the caller's own verdict, which is why the recognition keys on the opening
              // rather than on the name. Keying on the name would read these as subject laws and
              // lose exactly the starvation the census exists to show.
              let delegated =
                  SampleAdequacy.cases "M.f" (Guarded [ "arm" ]) 10 [ subject "a" true; guard "M.fWith" false ]

              Expect.isTrue
                  (SampleAdequacy.isVacuous delegated)
                  "a delegate's guard is the delegating family's starvation"

              Expect.equal
                  delegated.Starved
                  [ "the sample reached every arm the laws distinguish" ]
                  "and the dimension is cut at the guard's own `): `, not at an assumed family name"

          testCase "`unmeasured` is a third state, not a synonym for `vacuous`"
          <| fun _ ->
              // A consumer that handed the renderer no run has not measured zero cases; it has
              // measured nothing. Collapsing the two would let the second read as the first.
              let declarationOnly = Families.toMarkdown ()

              Expect.stringContains
                  declarationOnly
                  Families.unmeasuredToken
                  "the count-free rendering says so in every cell"

              Expect.isFalse
                  (declarationOnly.Contains("| " + SampleAdequacy.vacuousToken + " |"))
                  "and never claims a family ran empty" ]
