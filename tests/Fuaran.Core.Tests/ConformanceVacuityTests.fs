module Fuaran.Core.Tests.ConformanceVacuityTests

// Phase 196 — vacuity measured, per law family, at this repository's own reference witness.
//
// A `LawResult` records that a law HELD. `SampleAdequacy` (Phase 121) added the question "was the
// law reached at all?" and answers it INSIDE a run — and then discards what it measured. So the
// outside of a run is unchanged: a family that exercised twelve hundred cases and a family that
// exercised none report the same green, and a consumer's generated conformance census renders the
// same "adopted" cell for both. The maintainers' own records name that failure class twice already
// (a test filter that matched no test, a launcher stage that skipped and exited 0); this file
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

// Phase 297 — the obsolete forwards the naming rule keeps for one draft are rostered families, so
// their reference runs call them by their deprecated names on purpose.
#nowarn "44"

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
               (Conformance.diffContainedLaws nodew idw ConformanceTests.containedGen 4242 200)
           run "Conformance.normalizeLaws" 200 (Conformance.normalizeLaws nodew idw ConformanceTests.opGen 1234 200)
           run
               "Conformance.containerLaws"
               200
               (Conformance.containerLaws nodew idw ContainedOpsTests.containerGen 1610 200)
           // Phase 312 — the placement algebra, tree lowering and fresh ids, at the contained
           // reference generator (a leaf kind, so every container clause is live).
           run
               "Conformance.placementLaws"
               200
               (Conformance.placementLaws nodew idw ConformanceTests.containedGen 312 200)
           run "Conformance.loweringLaws" 200 (Conformance.loweringLaws nodew idw ConformanceTests.containedGen 312 200)
           run
               "Conformance.freshIdLaws"
               200
               (Conformance.freshIdLaws
                   nodew
                   idw
                   ConformanceTests.containedGen
                   (fun i (n: RNode) -> { n with Id = i })
                   (FreshIds.derived idw)
                   312
                   200)
           run
               "Conformance.mergeConflictLaws"
               300
               (Conformance.mergeConflictLaws nodew idw ConformanceTests.opGen 7171 300)
           run
               "Conformance.reconcileLaws"
               300
               (Conformance.reconcileLaws nodew idw ConformanceTests.opGen encNode 5353 300)
           run
               "Conformance.reconcileLawsWith"
               300
               (Conformance.reconcileLawsWith nodew idw ConformanceTests.opGen encNode OpStream.defaultHash 5353 300)
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
               (Conformance.concurrencyLawsWith nodew idw ConformanceTests.opGen encNode honestFootprint 8080 300)
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
           run
               "Conformance.keyedApplyLaws"
               300
               (Conformance.keyedApplyLaws
                   ConformanceTests.keyw
                   ConformanceTests.knodew
                   idw
                   KeyedApplyTests.deepGen
                   2860
                   300)
           // Phase 247 — arbitration over containers and keyed positions, both built arms reached.
           run
               "Conformance.keyedArbitrationLaws"
               200
               (Conformance.keyedArbitrationLaws
                   ConformanceTests.keyw
                   ConformanceTests.knodew
                   idw
                   KeyedArbitrationTests.containedKGen
                   KeyedArbitrationTests.encK
                   2470
                   200)
           run
               "Conformance.keyedArbitrationLawsWith"
               200
               (Conformance.keyedArbitrationLawsWith
                   ConformanceTests.keyw
                   ConformanceTests.knodew
                   idw
                   KeyedArbitrationTests.containedKGen
                   KeyedArbitrationTests.encK
                   (Ops.footprintKeyed ConformanceTests.keyw ConformanceTests.knodew idw)
                   (Ops.canApplyAllKeyed ConformanceTests.keyw (fun n -> n.Kind <> "para") ConformanceTests.knodew idw)
                   2470
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
                   ConformanceTests.sw
                   ConformanceTests.streamGen
                   string
                   OpStream.defaultHash
                   OpStream.canonicalConfig
                   321
                   100)
           run
               "Conformance.dagLaws"
               100
               (Conformance.dagLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 99 100)
           run
               "Conformance.reachLaws"
               100
               (Conformance.reachLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 289 100)
           run
               "Conformance.casLaws"
               200
               (Conformance.casLaws
                   ConformanceTests.sw
                   ConformanceTests.stratifiedStreamGen
                   OpStream.defaultHash
                   4242
                   200)
           run
               "Conformance.idempotencyLaws"
               200
               (Conformance.idempotencyLaws
                   ConformanceTests.sw.Encode
                   ConformanceTests.sw
                   ConformanceTests.stratifiedStreamGen
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
                               Eff =
                                   { Host = Pure
                                     Determinism = Effect.clock } } ] with
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
               "Conformance.aiSurfaceLawsAt"
               200
               (Conformance.aiSurfaceLawsAt
                   AiSurfaceTests.policedWitness
                   AiSurfaceTests.genNoteOp
                   AiSurfaceTests.state0
                   1234
                   200)
           // Phase 297 — the obsolete forwards the naming rule left for one draft, under their own ids.
           run
               "Conformance.aiSurfaceLaws"
               200
               (Conformance.aiSurfaceLaws
                   AiSurfaceTests.policedWitness
                   AiSurfaceTests.genNoteOp
                   AiSurfaceTests.state0
                   1234
                   200)
           run
               "Conformance.aiSurfaceLawsUnderKitPolicy"
               200
               (Conformance.aiSurfaceLawsUnderKitPolicy
                   AiSurfaceTests.witness
                   AiSurfaceTests.genNoteOp
                   AiSurfaceTests.state0
                   1234
                   200)
           // Phase 246 — the seam families at the reference domain's seam (`…At` since Phase 297, with
           // the obsolete `…With` forwards beside them for one draft).
           run
               "Conformance.capabilityLawsAt"
               300
               (Conformance.capabilityLawsAt WitnessTakingFamiliesTests.capabilityWitness 2460 300)
           run "Conformance.queryLawsAt" 300 (Conformance.queryLawsAt WitnessTakingFamiliesTests.queryWitness 2461 300)
           run
               "Conformance.capabilityPipelineLawsAt"
               200
               (Conformance.capabilityPipelineLawsAt WitnessTakingFamiliesTests.pipelineWitness 2462 200)
           run
               "Conformance.capabilityLawsWith"
               300
               (Conformance.capabilityLawsWith WitnessTakingFamiliesTests.capabilityWitness 2460 300)
           run
               "Conformance.queryLawsWith"
               300
               (Conformance.queryLawsWith WitnessTakingFamiliesTests.queryWitness 2461 300)
           run
               "Conformance.capabilityPipelineLawsWith"
               200
               (Conformance.capabilityPipelineLawsWith WitnessTakingFamiliesTests.pipelineWitness 2462 200)

           // ---- the fixture-only families: the kit supplies its own sample ----
           run
               "Conformance.captureReplayLaws"
               200
               (let encInt (n: int) = Json.render (JInt n)

                let decInt (s: string) =
                    Decode.parse s |> Result.bind Decode.asInt

                Conformance.captureReplayLaws encInt decInt ConfRng.next OpStream.defaultHash 31337 200)
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
           run "Conformance.aggregateNullSkipLaws" 200 (Conformance.aggregateNullSkipLaws 4242 200)
           run "Conformance.columnarValidatorLaws" 200 (Conformance.columnarValidatorLaws 4242 200)
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
           run
               "Conformance.propagationEvaluatorLawsWith"
               120
               (Conformance.propagationEvaluatorLawsWith
                   PropagationCompositionTests.evaluatorWitness
                   PropagationCompositionTests.withPrior
                   2500
                   120)
           run "Conformance.canonicalFloatLaws" 500 (Conformance.canonicalFloatLaws 4242 500)
           run "Conformance.chainBreakReasonLaws" 120 (Conformance.chainBreakReasonLaws 5125 120)
           run "Conformance.dagBreakReasonLaws" 120 (Conformance.dagBreakReasonLaws 5147 120)
           // Phase 232 — no seed and no iteration count: the one run reads every record it pins.
           run "Conformance.witnessSurfaceLaws" 1 (Conformance.witnessSurfaceLaws ())

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
                   FoldConfluenceTests.treeHash
                   FoldConfluenceTests.treeLaneGen
                   3
                   OpStream.defaultHash
                   1000
                   120)

           // Phase 297 — the null-tolerant read vectors: a fixed corpus, one law per vector, so the
           // one run is the whole sample.
           run "WireNullTolerance.laws" 1 (WireNullTolerance.laws ()) ]
        : Run list)

/// The family's own adequacy class, which is what decides how its run is read. Looked up rather
/// than passed, because the census is the single declaration and this file must not become a
/// second one.
let private classOf (id: string) : AdequacyClass =
    match KitRoster.census |> List.tryFind (fun (k, _) -> k = id) with
    | Some(_, k) -> k
    | None -> failwithf "%s has no census row (SampleAdequacy.census) — the roster and the census disagree" id

/// Phase 297 — what one family's guards say against the roster and its census class: every guard
/// label is a roster id (a delegating family emits its delegate's, which is one), a `Guarded` family
/// emits at least one guard, and an `Unconditional` family emits none. Pure, so the go-red perturbs
/// its input rather than the tree.
let guardDefects (ids: Set<string>) (id: string) (klass: AdequacyClass) (results: LawResult list) : string list =
    let opening = SampleAdequacy.guardOpening

    let labelOf (law: string) =
        let rest = law.Substring opening.Length
        let at = rest.IndexOf "): "
        if at < 0 then rest else rest.Substring(0, at)

    let guards =
        results
        |> List.filter (fun r -> r.Law.StartsWith(opening, System.StringComparison.Ordinal))

    let bare =
        guards
        |> List.map (fun r -> labelOf r.Law)
        |> List.filter (fun l -> not (Set.contains l ids))
        |> List.distinct
        |> List.map (fun l -> sprintf "%s: a guard is labelled `%s`, which is not a roster id" id l)

    let classDefect =
        match klass, guards with
        | Guarded _, [] -> [ sprintf "%s: the census says Guarded and the family emits no guard" id ]
        | Unconditional _, _ :: _ ->
            [ sprintf "%s: the census says Unconditional and the family emits %d guard(s)" id (List.length guards) ]
        | _ -> []

    bare @ classDefect

/// The measured census: one `CaseCount` per family, in roster order. This is what the generated
/// `docs/conformance-families.{md,json}` render their `cases` column from.
let cases () : (string * CaseCount) list =
    runs.Value
    |> List.map (fun r -> r.Id, SampleAdequacy.cases r.Id (classOf r.Id) r.Iterations r.Results)
    |> List.sortBy fst

/// Phase 223 — the six families Phase 220's audit found drawing a refusal population a run could
/// silently miss, with the dimensions each is now `Guarded` over. Five are this repository's since
/// Phase 258: `transformLaws` left with the dataframe layer it reads (D66), and the compute
/// repository's suite holds its row. Since Phase 297 `casLaws` also counts its race arm (two
/// `appendIf` calls at one head that both apply), which is drawn too.
let private drawnRefusalSix: (string * string list) list =
    [ "Conformance.casLaws", [ "accepted"; "refused"; "race arm" ]
      "Conformance.idempotencyLaws", [ "accepted"; "refused" ]
      "Conformance.aiSurfaceLaws", [ "accepted"; "refused"; "allowed"; "parked"; "denied" ]
      // Phase 276 — and the column type of the ranged column, int or decimal.
      "Conformance.columnarValidatorLaws", [ "null cell"; "out-of-range cell"; "int cell"; "decimal cell" ]
      "Conformance.diffContainedLaws", [ "accepted"; "refused" ] ]

/// ... and each one's reference run as a function of the seed, at the size the census runs it:
/// the kit reference generator, stratified, which is what "the kit's own run" means.
let private drawnRefusalSixRuns: (string * (int * (int -> LawResult list))) list =
    [ "Conformance.casLaws",
      (200,
       fun seed ->
           Conformance.casLaws ConformanceTests.sw ConformanceTests.stratifiedStreamGen OpStream.defaultHash seed 200)
      "Conformance.idempotencyLaws",
      (200,
       fun seed ->
           Conformance.idempotencyLaws
               ConformanceTests.sw.Encode
               ConformanceTests.sw
               ConformanceTests.stratifiedStreamGen
               OpStream.defaultHash
               seed
               200)
      "Conformance.aiSurfaceLaws",
      (200,
       fun seed ->
           Conformance.aiSurfaceLaws
               AiSurfaceTests.policedWitness
               AiSurfaceTests.genNoteOp
               AiSurfaceTests.state0
               seed
               200)
      "Conformance.columnarValidatorLaws", (200, fun seed -> Conformance.columnarValidatorLaws seed 200)
      "Conformance.diffContainedLaws",
      (200, fun seed -> Conformance.diffContainedLaws nodew idw ConformanceTests.containedGen seed 200) ]

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
              let rostered = Set.ofList KitRoster.ids

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

          // ---- Phase 297: guards the census can see, and a class read off the code ----

          testCase
              "every guard is labelled with a roster id, every Guarded family emits one, and no Unconditional family does"
          <| fun _ ->
              // Until Phase 297 seven families labelled their guards with a bare entry name and one
              // with its module name, so a census keyed by roster id read none of them; and the
              // census class was a declaration nothing compared with what the family emits. Both
              // are read off the reference run here.
              let ids = Set.ofList KitRoster.ids

              let defects =
                  [ for r in runs.Value do
                        yield! guardDefects ids r.Id (classOf r.Id) r.Results ]

              Expect.isEmpty
                  defects
                  (sprintf "guard labels or census classes disagree with the code:\n%s" (String.concat "\n" defects))

          testCase "the guard-label check goes red in all three directions — made-up runs"
          <| fun _ ->
              let ids = Set.ofList [ "M.f"; "M.fWith" ]

              let guard (label: string) =
                  { Law =
                      SampleAdequacy.lawPrefix label
                      + "the sample reached every arm the laws distinguish"
                    Passed = true
                    Counterexample = None }

              let subject =
                  { Law = "a"
                    Passed = true
                    Counterexample = None }

              Expect.isEmpty
                  (guardDefects ids "M.f" (Guarded [ "arm" ]) [ subject; guard "M.fWith" ])
                  "a delegate's roster id is a legitimate label"

              Expect.isNonEmpty
                  (guardDefects ids "M.f" (Guarded [ "arm" ]) [ subject; guard "f" ])
                  "a bare entry name is not a roster id"

              Expect.isNonEmpty
                  (guardDefects ids "M.f" (Guarded [ "arm" ]) [ subject ])
                  "a Guarded family that emits no guard is named"

              Expect.isNonEmpty
                  (guardDefects ids "M.f" (Unconditional "built") [ subject; guard "M.f" ])
                  "an Unconditional family that emits a guard is named"

          testCase "a law the runner reports NEVER REACHED is starvation, under either class — made-up runs"
          <| fun _ ->
              let unreached =
                  { Law = "the tamper law"
                    Passed = false
                    Counterexample = Some(SampleAdequacy.neverReached + " — widen the generator") }

              let held =
                  { Law = "the replay law"
                    Passed = true
                    Counterexample = None }

              for klass in [ Unconditional "built"; Guarded [ "arm" ] ] do
                  let measured = SampleAdequacy.cases "M.f" klass 10 [ held; unreached ]
                  Expect.isTrue (SampleAdequacy.isVacuous measured) (sprintf "%A: a never-reached law is vacuity" klass)
                  Expect.equal measured.Starved [ "the tamper law" ] "and the cell names the law"

              let refuted =
                  { unreached with
                      Counterexample = Some "seed=1 iter=0: a real counterexample" }

              Expect.isFalse
                  (SampleAdequacy.isVacuous (SampleAdequacy.cases "M.f" (Unconditional "built") 10 [ held; refuted ]))
                  "a law refuted by a counterexample is red, not starved — the census is not where that is read"

          testCase "a constant generator starves dagLaws' tamper arm, and the census cell says so"
          <| fun _ ->
              // The shard's case in one family: the tamper arm runs only when a fresh draw differs
              // from the op it replaces, so a generator that draws one op every time never tampers.
              // The law is a strict runner cell, so it reds as never reached rather than passing.
              let op0, _ = ConformanceTests.streamGen.Op(ConfRng.ofSeed 1)

              let constant =
                  { ConformanceTests.streamGen with
                      Op = fun r -> op0, r }

              let results =
                  Conformance.dagLaws ConformanceTests.sw constant OpStream.defaultHash 4242 50

              let measured =
                  SampleAdequacy.cases "Conformance.dagLaws" (classOf "Conformance.dagLaws") 50 results

              Expect.isTrue (SampleAdequacy.isVacuous measured) "the constant generator certified no tamper"

              Expect.stringStarts
                  (SampleAdequacy.renderCases measured)
                  SampleAdequacy.vacuousToken
                  "and the cell reads vacuous"

          testCase "the newly counted families go red on a degenerate generator, never green over nothing"
          <| fun _ ->
              // Phase 297's cases, each at the reference witness with ONE thing made degenerate: a
              // generator that draws one value every time (so no fresh draw ever differs, and one
              // tree is hashed against itself), a generator refused every time (so there is no
              // chain to re-attribute — `attributedLaws`' gate is a non-empty chain, not a differing
              // draw, and a constant ACCEPTED op reaches it; `hashFnLaws`' length gates are starved the
              // same way). Each run must be red, and a run whose red is starvation rather than a
              // counterexample must read `vacuous` in the census. A hash answering one digest for
              // everything still leaves `hashFnLaws` green — measured here, and the op-tamper arm that
              // would catch it is Phase 302's, not this phase's.
              let op0, _ = ConformanceTests.streamGen.Op(ConfRng.ofSeed 1)

              let constantOps =
                  { ConformanceTests.streamGen with
                      Op = fun r -> op0, r }

              let refusedOps =
                  { ConformanceTests.streamGen with
                      Op = fun r -> Reference.Counter.Dec ConformanceTests.overdraw, r }

              let tree0, _ = ConformanceTests.genTree (ConfRng.ofSeed 1)

              let degenerate =
                  [ "Conformance.encoderInjectivityLaws",
                    Conformance.encoderInjectivityLaws artw encNode (fun r -> tree0, r) 4242 200
                    "Conformance.attributedLaws",
                    Conformance.attributedLaws ConformanceTests.sw refusedOps OpStream.defaultHash 4242 200
                    "Conformance.dagLaws",
                    Conformance.dagLaws ConformanceTests.sw constantOps OpStream.defaultHash 4242 200
                    "Conformance.captureReplayLaws",
                    Conformance.captureReplayLaws
                        (fun (n: int) -> Json.render (JInt n))
                        (fun s -> Decode.parse s |> Result.bind Decode.asInt)
                        (fun r -> 7, r)
                        OpStream.defaultHash
                        31337
                        200
                    "Conformance.hashFnLaws",
                    Conformance.hashFnLaws ConformanceTests.sw refusedOps OpStream.defaultHash 4242 200 ]

              let isStarvation (r: LawResult) =
                  r.Law.StartsWith SampleAdequacy.guardOpening
                  || (match r.Counterexample with
                      | Some cx -> cx.StartsWith SampleAdequacy.neverReached
                      | None -> false)

              for id, results in degenerate do
                  let red = results |> List.filter (fun r -> not r.Passed)
                  Expect.isNonEmpty red (sprintf "%s reported green over a degenerate generator" id)

                  if red |> List.forall isStarvation then
                      Expect.isTrue
                          (SampleAdequacy.isVacuous (SampleAdequacy.cases id (classOf id) 200 results))
                          (sprintf "%s starved, so its census cell must read vacuous" id)

          // ---- Phase 220: the refusable-family audit ----

          testCase "the refusal audit covers the roster, both directions, once per family, with its evidence"
          <| fun _ ->
              // The audit is data so the next audit can diff it; a family missing from it is a
              // family nobody asked the question of, and a row naming nothing is a verdict about a
              // family that no longer exists.
              let audited = KitRoster.refusalAudit |> List.map (fun a -> a.Family)
              let rostered = Set.ofList KitRoster.ids

              Expect.isEmpty
                  (Set.difference rostered (Set.ofList audited) |> Set.toList)
                  "these law families have no refusal-audit row (Families.refusalAudit) — audit each in the commit that ships it"

              Expect.isEmpty
                  (Set.difference (Set.ofList audited) rostered |> Set.toList)
                  "these refusal-audit rows name no roster family"

              Expect.equal (List.length audited) (Set.count (Set.ofList audited)) "one row per family"

              for a in KitRoster.refusalAudit do
                  Expect.isTrue (a.Why.Trim().Length > 10) (sprintf "%s carries no usable evidence" a.Family)

          testCase "every family whose refusals a run can silently miss is Guarded"
          <| fun _ ->
              // The PROPERTY, read off the audit and the census rather than off a list of families:
              // a `Drawn` row is a refusal population a run can miss while every law stays green, and
              // a `Guarded` census class is what reports that. So a family added later with a drawn
              // refusal and no guard fails here without anyone having to remember to list it.
              //
              // Phase 220 shipped this as a ratchet naming six permitted violators; Phase 223 guarded
              // all six and emptied it, so there are no exceptions left to name.
              let unguardedDrawn =
                  KitRoster.refusalAudit
                  |> List.filter (fun a -> a.Population = Families.Drawn)
                  |> List.filter (fun a ->
                      match classOf a.Family with
                      | Guarded _ -> false
                      | Unconditional _ -> true)
                  |> List.map (fun a -> a.Family)

              Expect.isEmpty
                  unguardedDrawn
                  "these families have a refusal population a run can silently miss (audited `Drawn`) and no adequacy guard — guard them, or the census reports an unguarded pass"

          testCase "the two base-run families certify is built from are Guarded, and reached at the reference witness"
          <| fun _ ->
              // (A) of Phase 196's deferred call: `opAlgebra` and `reducer` read a refusal
              // population the run DRAWS, so their census class is `Guarded` over accepted/refused,
              // and the generated data says which way a run went rather than rendering a count.
              let measured = cases ()

              for id in [ "Conformance.opAlgebra"; "Conformance.reducer" ] do
                  Expect.equal
                      (KitRoster.tryRefusal id |> Option.map (fun a -> a.Population))
                      (Some Families.Drawn)
                      (sprintf "%s is audited Drawn" id)

                  Expect.equal (classOf id) (Guarded [ "accepted"; "refused" ]) (sprintf "%s is censused Guarded" id)

                  Expect.equal
                      (KitRoster.adequacyToken measured id)
                      "guarded-reached"
                      (sprintf "%s reached both sides at the reference witness" id)

          // ---- Phase 245: the stream family the aggregates share ----

          testCase
              "streamLaws is Guarded over accepted op / tampered chain, and reached at the reference witness on every seed tried"
          <| fun _ ->
              // The third base-run family certify and certifyStream are built from, and the last
              // one whose chain was drawn but whose census row claimed every iteration built it.
              let id = "Conformance.streamLaws"

              Expect.equal
                  (KitRoster.tryRefusal id |> Option.map (fun a -> a.Population))
                  (Some Families.Drawn)
                  "streamLaws is audited Drawn"

              Expect.equal (classOf id) (Guarded [ "accepted"; "tampered chain" ]) "streamLaws is censused Guarded"
              Expect.equal (KitRoster.adequacyToken (cases ()) id) "guarded-reached" "reached at the reference run"

              // The same run the census is measured from, over twenty other seeds: a reference
              // generator that starved here would be an intermittent red, not a finding.
              for seed in [ 1..20 ] |> List.map (fun k -> k * 7919 + 3) do
                  let results =
                      Conformance.streamLaws
                          ConformanceTests.sw
                          ConformanceTests.streamGen
                          OpStream.defaultHash
                          seed
                          200

                  Expect.equal
                      (KitRoster.adequacyToken [ id, SampleAdequacy.cases id (classOf id) 200 results ] id)
                      "guarded-reached"
                      (sprintf "%s at seed %d" id seed)

          // ---- Phase 223: the drawn-refusal families (six, five here since Phase 258) ----

          testCase "the drawn-refusal families are Guarded, and reached at the reference witness"
          <| fun _ ->
              let measured = cases ()

              for id, dims in drawnRefusalSix do
                  Expect.equal
                      (KitRoster.tryRefusal id |> Option.map (fun a -> a.Population))
                      (Some Families.Drawn)
                      (sprintf "%s is audited Drawn" id)

                  Expect.equal (classOf id) (Guarded dims) (sprintf "%s is censused Guarded" id)

                  Expect.equal
                      (KitRoster.adequacyToken measured id)
                      "guarded-reached"
                      (sprintf "%s reached every guarded dimension at the reference witness" id)

          testCase "each kit reference generator reaches the refused branch on every seed tried, at the default size"
          <| fun _ ->
              // The stratification half, proven rather than asserted: the same run the census is
              // measured from, over twenty seeds that are not the reference seed. A guard that fired
              // here would be the intermittent failure the stratification exists to rule out, so
              // every seed must read `guarded-reached` — never `guarded-starved`.
              let seeds = [ 1..20 ] |> List.map (fun k -> k * 7919 + 3)

              for id, (iterations, runAt) in drawnRefusalSixRuns do
                  for seed in seeds do
                      let measured = [ id, SampleAdequacy.cases id (classOf id) iterations (runAt seed) ]

                      Expect.equal
                          (KitRoster.adequacyToken measured id)
                          "guarded-reached"
                          (sprintf "%s at seed %d, %d iterations" id seed iterations)

          testCase "the adequacy cell tells reached from starved from unconditional — made-up runs"
          <| fun _ ->
              // The roster carries the verdict, so its derivation is exercised over inputs chosen
              // here, not over whatever the reference run happens to produce.
              let reached =
                  [ "Conformance.reducer",
                    { Family = "Conformance.reducer"
                      Cases = 10
                      Starved = [] } ]

              let starved =
                  [ "Conformance.reducer",
                    { Family = "Conformance.reducer"
                      Cases = 10
                      Starved = [ "the sample reached every refused op the laws distinguish" ] } ]

              Expect.equal (Families.adequacyToken reached "Conformance.reducer") "guarded-reached" "reached"
              Expect.equal (Families.adequacyToken starved "Conformance.reducer") "guarded-starved" "starved"
              Expect.equal (Families.adequacyToken [] "Conformance.reducer") "guarded-unmeasured" "unmeasured"
              Expect.equal (Families.adequacyToken reached "Conformance.witnessLaws") "unconditional" "unconditional"

          testCase "`unmeasured` is a third state, not a synonym for `vacuous`"
          <| fun _ ->
              // A consumer that handed the renderer no run has not measured zero cases; it has
              // measured nothing. Collapsing the two would let the second read as the first.
              let declarationOnly = KitRoster.toMarkdown ()

              Expect.stringContains
                  declarationOnly
                  Families.unmeasuredToken
                  "the count-free rendering says so in every cell"

              Expect.isFalse
                  (declarationOnly.Contains("| " + SampleAdequacy.vacuousToken + " |"))
                  "and never claims a family ran empty" ]
