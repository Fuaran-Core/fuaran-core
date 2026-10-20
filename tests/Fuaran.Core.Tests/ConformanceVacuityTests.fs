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

open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.Reference2

/// A column of type `ty` over `cells`, which fit it (Phase 417: the typed column is built through
/// `Column.ofCells`, which refuses a cell of another type).
let private cellColumn (name: string) (ty: ColumnType) (cells: Cell list) : Column =
    match Column.ofCells name ty cells with
    | Ok c -> c
    | Error e -> failtestf "column %s did not build: %A" name e

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
    LawKit.FamilyId id,
    { Id = id
      Iterations = iterations
      Results = results }

/// A signing sink, so the attestation family's evidence is BUILT. The unsigned path is
/// `noAttestationVacuityLaws`, and the test below shows what this family does without a signer.
let private sink = ConformanceTests.keyedSink "test-key-0"

/// The honest footprint `concurrencyLaws` itself supplies — the `With` entry point takes it as a
/// parameter so a test can under-approximate it and prove the law has teeth. The census wants the
/// honest one.
let private honestFootprint = Ops.footprint Policy.none (OpScope.plain nodew idw)

/// A chain format that is NOT the canonical one (Phase 349): its own genesis and a payload that
/// frames the canonical pre-image, so every `…With` operation's reference run is at a config a
/// variant ignoring its parameter could not agree with.
let secondConfig: StreamConfig =
    { Genesis = "phase-349-genesis"
      Payload = fun seq actor encoded -> "v2|" + StreamConfig.canonical.Payload seq actor encoded
      Quote = StreamConfig.canonical.Quote }

/// Phase 360 — the stores the stored-identity families' reference runs read: three ops whose strings
/// carry a line feed, a tab and a carriage return, written by the ordinary append under the CURRENT
/// profile, so each run declares `v2`. (The published `0.30.0` stores, declared `v1`, are
/// `EncodingProfileTests`'.)
let private storedIdentityWitness: StreamWitness<JVal, unit, string> =
    { Apply = fun _ s -> Ok s
      Encode = Json.render
      Decode = Json.parse }

let private storedIdentityOps =
    [ JStr "a\nb"; JObj [ "t", JStr "c\td" ]; JStr "e\rf" ]

let private storedIdentityLinear =
    storedIdentityOps
    |> List.fold
        (fun recs op ->
            match
                OpStream.append
                    (OpStream.canonical OpStream.defaultHash storedIdentityWitness)
                    (Human "a\nb")
                    op
                    ()
                    recs
            with
            | Ok(_, recs') -> recs'
            | Error e -> failwith e)
        OpStream.empty

let private storedIdentityDag =
    storedIdentityOps
    |> List.fold
        (fun (parent, d) op ->
            match Dag.append OpStream.defaultHash storedIdentityWitness (Human "a\tb") op parent d with
            | Ok(id, d') -> id, d'
            | Error e -> failwithf "%A" e)
        ("", Dag.empty)
    |> snd

let private storedIdentityCaptures =
    [ "x\ny"; "z" ]
    |> List.fold
        (fun caps eff ->
            EffectCapture.captureEffect
                StreamConfig.canonical
                OpStream.defaultHash
                id
                "io\twall"
                eff
                (fun () -> "\"v\"")
                caps
            |> snd)
        []

/// The observer family's reference witness (Phase 298): an int metric, two flags. Shared by the
/// `observerLawsAt` run and its obsolete forward's.
let private observerWitness =
    ObserverWitness.create (fun (x: int) ->
        [ if x > 5 then
              "big"

              if x % 2 = 0 then
                  "even" ])

/// Every law family the kit ships, run once, keyed by its id (Phase 507: a map, so a family has
/// exactly one reference run, and the completeness test below walks the roster through it). A
/// family's pinned-default spellings are not keys: their family's run is theirs, and
/// `spellingTests` holds each spelling equal to its family at the default. Evaluated at most once
/// per process: several of these are three-hundred-iteration property runs.
let private runs: Lazy<Map<LawKit.FamilyId, Run>> =
    lazy
        (let declared =
            [
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
              run
                  "Conformance.loweringLaws"
                  200
                  (Conformance.loweringLaws nodew idw ConformanceTests.containedGen 312 200)
              // Phase 314 — the digest maps and the defect-set gate, at
              // the reference encoder and registry; the kit's own second draw supplies the independent
              // pairs that reach a changed kind, a removed region and a moved survivor.
              run
                  "Conformance.digestLaws"
                  200
                  (Conformance.digestLaws nodew idw DigestTests.encode ConformanceTests.opGen 314 200)
              // Phase 518 — the keyed diff at the keyed reference domain, under an own-content encoder
              // that reads no keyed position (the change below one is seen regardless).
              run
                  "Conformance.keyedDiffLawsAt"
                  200
                  (Conformance.keyedDiffLawsAt
                      ConformanceTests.keyw
                      ConformanceTests.knodew
                      idw
                      (fun (n: ConformanceTests.KNode) -> n.Id + ":" + n.Kind)
                      ConformanceTests.kGen
                      518
                      200)
              run
                  "Conformance.introducedLaws"
                  200
                  (Conformance.introducedLaws nodew idw DigestTests.registry ConformanceTests.opGen 314 200)
              // Phase 313 — the structural-integrity families at the reference grammar and the
              // reference `RefWitness`, whose generators draw both sides of each refusal.
              run
                  "Conformance.containmentLaws"
                  200
                  (Conformance.containmentLaws
                      StructuralIntegrityTests.grammar
                      nodew
                      idw
                      StructuralIntegrityTests.grammarGen
                      313
                      200)
              run
                  "Conformance.referenceLawsAt"
                  200
                  (Conformance.referenceLawsAt
                      StructuralIntegrityTests.refw
                      nodew
                      idw
                      StructuralIntegrityTests.refGen
                      313
                      200)
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
                  "Conformance.reconcileLawsWith"
                  300
                  (Conformance.reconcileLawsWith nodew idw ConformanceTests.opGen encNode OpStream.defaultHash 5353 300)
              run
                  "Conformance.footprintLaws"
                  300
                  (Conformance.footprintLaws nodew idw ConformanceTests.opGen encNode 4242 300)
              // Phase 249 — the same soundness law at the keyed reference's own stream witness.
              run
                  "Conformance.footprintLawsAt"
                  300
                  (Conformance.footprintLawsAt
                      FootprintLawsAtTests.keyedW
                      FootprintLawsAtTests.keyedFootprint
                      FootprintLawsAtTests.keyedHash
                      FootprintLawsAtTests.keyedGen
                      2490
                      300)
              // Phase 500 — the base-aware projection at the reference tree's own stream witness.
              run
                  "Conformance.footprintInLawsAt"
                  300
                  (Conformance.footprintInLawsAt
                      FoldFixtures.treeW
                      FootprintInTests.footprintOf
                      FootprintInTests.footprintIn
                      FootprintInTests.treeHash
                      FootprintInTests.treeGen
                      5000
                      300)
              run
                  "Conformance.concurrencyLawsWith"
                  300
                  (Conformance.concurrencyLawsWith nodew idw ConformanceTests.opGen encNode honestFootprint 8080 300)
              run
                  "Conformance.arbitrationLaws"
                  300
                  (Conformance.arbitrationLaws nodew idw ConformanceTests.opGen encNode 8585 300)
              run
                  "Conformance.keyedChildrenLawsAt"
                  200
                  (Conformance.keyedChildrenLawsAt
                      ConformanceTests.keyw
                      ConformanceTests.knodew
                      idw
                      ConformanceTests.kGen
                      1890
                      200)
              run
                  "Conformance.keyedApplyLawsAt"
                  300
                  (Conformance.keyedApplyLawsAt
                      ConformanceTests.keyw
                      ConformanceTests.knodew
                      idw
                      KeyedApplyTests.deepGen
                      2860
                      300)
              // Phase 247 — arbitration over containers and keyed positions, both built arms reached.
              run
                  "Conformance.keyedArbitrationLawsWith"
                  200
                  (Conformance.keyedArbitrationLawsWith
                      ConformanceTests.keyw
                      ConformanceTests.knodew
                      idw
                      KeyedArbitrationTests.containedKGen
                      KeyedArbitrationTests.encK
                      (Ops.footprint
                          Policy.none
                          (OpScope.keyed ConformanceTests.keyw (fun _ -> true) ConformanceTests.knodew idw))
                      (Ops.canApplyAll
                          Policy.none
                          (OpScope.keyed ConformanceTests.keyw (fun n -> n.Kind <> "para") ConformanceTests.knodew idw))
                      2470
                      200)

              // ---- stream-shaped opt-ins ----
              run
                  "Conformance.snapshotLawsWith"
                  100
                  (Conformance.snapshotLawsWith
                      ConformanceTests.sw
                      ConformanceTests.streamGen
                      string
                      OpStream.sha256Hash
                      secondConfig
                      321
                      100)
              // Phase 349 — the `…With` operations at the second config and a non-default hash.
              run
                  "Conformance.streamConfigLaws"
                  100
                  (Conformance.streamConfigLaws
                      ConformanceTests.sw
                      ConformanceTests.streamGen
                      OpStream.sha256Hash
                      secondConfig
                      349
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
                  "Conformance.checkpointLaws"
                  100
                  (Conformance.checkpointLaws
                      ConformanceTests.sw
                      ConformanceTests.streamGen
                      (fun (s: int) -> string s)
                      OpStream.defaultHash
                      288
                      100)
              run
                  "Conformance.laneLaws"
                  100
                  (Conformance.laneLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 311 100)
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
                  (Conformance.attributedLaws
                      ConformanceTests.sw
                      ConformanceTests.streamGen
                      OpStream.defaultHash
                      4242
                      200)
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
                  "Conformance.attestationLawsAt"
                  200
                  (Conformance.attestationLawsAt
                      ConformanceTests.sw
                      ConformanceTests.streamGen
                      sink
                      OpStream.defaultHash
                      4242
                      200)


              // ---- artifact-witness opt-ins ----
              run
                  "Conformance.compositionLawsAt"
                  200
                  (Conformance.compositionLawsAt artw artw id ConformanceTests.genComposition 4242 200)
              run
                  "Conformance.compositionPilotAt"
                  200
                  (Conformance.compositionPilotAt
                      artw
                      artw2
                      embedToR
                      encNode
                      encNode2
                      ConformanceTests.genComposition2
                      4242
                      200)
              run
                  "Conformance.memoLawsAt"
                  200
                  (Conformance.memoLawsAt artw encNode ConformanceTests.genMemo OpStream.defaultHash 4242 200)
              run
                  "Conformance.memoSoundnessLawsAt"
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

                   Conformance.memoSoundnessLawsAt artw encNode underDeclared underDeclaredArgs 4242 50)
              run
                  "Conformance.functionVerifyLawsAt"
                  200
                  (Conformance.functionVerifyLawsAt
                      artw
                      (ConformanceTests.tplCount (0, 5))
                      (ConformanceTests.tplCount (0, 10))
                      ConformanceTests.countReg
                      ConformanceTests.genParamsFor
                      777
                      200)
              run
                  "Conformance.verifyHonestyLawsAt"
                  200
                  (let mkSoundDet (d: DeterminismSource) =
                      { ConformanceTests.tplCount (0, 5) with
                          Eff = { Host = Pure; Determinism = d } }

                   let mkBrokenDet (d: DeterminismSource) =
                       { ConformanceTests.tplCount (0, 10) with
                           Eff = { Host = Pure; Determinism = d } }

                   Conformance.verifyHonestyLawsAt
                       artw
                       mkSoundDet
                       mkBrokenDet
                       ConformanceTests.countReg
                       ConformanceTests.genParamsFor
                       777
                       200)
              run
                  "Conformance.encoderInjectivityLawsAt"
                  200
                  (Conformance.encoderInjectivityLawsAt artw encNode ConformanceTests.genTree 4242 200)


              // ---- the remaining witnessed opt-ins ----
              run
                  "Conformance.projectionLawsAt"
                  200
                  (Conformance.projectionLawsAt
                      ProjectionTests.pw
                      ProjectionTests.applyOps
                      ProjectionTests.wireEncode
                      ProjectionTests.genTree
                      42
                      200)
              // Phase 298 — the observer family at a reference witness: an int metric, two flags.
              run
                  "Conformance.observerLawsAt"
                  200
                  (Conformance.observerLawsAt observerWitness (ConfRng.intBelow 10) 42 200)
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
                  "Conformance.aiSurfaceKitPolicyLawsAt"
                  200
                  (Conformance.aiSurfaceKitPolicyLawsAt
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
                  (Conformance.capabilityLawsAt
                      WitnessTakingFamiliesTests.capabilityWitness
                      WitnessTakingFamiliesTests.capabilityBody
                      2460
                      300)
              run
                  "Conformance.queryLawsAt"
                  300
                  (Conformance.queryLawsAt
                      WitnessTakingFamiliesTests.queryWitness
                      WitnessTakingFamiliesTests.queryResolver
                      2461
                      300)
              run
                  "Conformance.capabilityPipelineLawsAt"
                  200
                  (Conformance.capabilityPipelineLawsAt WitnessTakingFamiliesTests.pipelineWitness 2462 200)

              // ---- the fixture-only families: the kit supplies its own sample ----
              run
                  "Conformance.captureReplayLaws"
                  200
                  (let encInt (n: int) = Json.render (JInt n)

                   let decInt (s: string) =
                       Json.parse s |> Result.bind (Decoder.describing Decoder.int)

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
              // Phase 318 — the policy gate, the write gate and the keyed capture journal.
              run "Conformance.policyLaws" 200 (Conformance.policyLaws 4242 200)
              run "Conformance.keyedCaptureLaws" 200 (Conformance.keyedCaptureLaws 4242 200)
              run "Conformance.writeGateLaws" 300 (Conformance.writeGateLaws nodew idw ConformanceTests.opGen 4242 300)
              run
                  "Conformance.policyLawsAt"
                  300
                  (Conformance.policyLawsAt
                      PolicyGateTests.guarded
                      PolicyGateTests.noteRegistry
                      AiSurfaceTests.state0
                      AiSurfaceTests.genNoteOp
                      PolicyGateTests.actors
                      PolicyGateTests.privileged
                      4242
                      300)
              run "Conformance.packLoadingLaws" 200 (Conformance.packLoadingLaws 4242 200)
              run "Conformance.aggregateNullSkipLaws" 200 (Conformance.aggregateNullSkipLaws 4242 200)
              run "Conformance.columnarValidatorLaws" 200 (Conformance.columnarValidatorLaws 4242 200)
              run "Conformance.columnVectorLaws" 200 (Conformance.columnVectorLaws 4242 200)
              // Phase 418 — the ownership law, with Core's own column reads as the operation.
              run
                  "Conformance.columnOwnershipLawsWith"
                  200
                  (Conformance.columnOwnershipLawsWith ColumnTests.coreColumnReads ColumnTests.ownershipDraw 4242 200)
              run "Conformance.deferredLaws" 200 (Conformance.deferredLaws 4242 200)
              run "Conformance.capabilityPipelineLaws" 200 (Conformance.capabilityPipelineLaws 4242 200)
              run
                  "Conformance.capabilityPipelineIncrementalLaws"
                  200
                  (Conformance.capabilityPipelineIncrementalLaws 4242 200)
              run "Conformance.dirtyPropagationLaws" 200 (Conformance.dirtyPropagationLaws 4242 200)
              run "Conformance.propagationEvalLaws" 200 (Conformance.propagationEvalLaws 4242 200)
              run
                  "Conformance.propagationEvaluatorLawsAt"
                  200
                  (Conformance.propagationEvaluatorLawsAt ConformanceTests.sheetw 2110 200)
              run
                  "Conformance.propagationPriorLawsAt"
                  120
                  (Conformance.propagationPriorLawsAt
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
                  "FoldConfluence.laneFoldLawsWith"
                  120
                  (FoldConfluence.laneFoldLawsWith
                      FoldFixtures.treeW
                      FoldFixtures.treeFootprint
                      FoldFixtures.treeHash
                      FoldFixtures.treeLaneGen
                      3
                      OpStream.defaultHash
                      1000
                      120)

              // Phase 297 — the null-tolerant read vectors: a fixed corpus, one law per vector, so the
              // one run is the whole sample.
              run "WireNullTolerance.laws" 1 (WireNullTolerance.laws ())
              run "StringEscapeVectors.laws" 1 (StringEscapeVectors.laws ())
              run "Conformance.sanitizeLawsAt" 200 (Conformance.sanitizeLawsAt SanitizeWitness.core 349 200)
              // Phase 442 — the codec family at the reference declaration (CodecLawsTests).
              run
                  "Conformance.codecLawsAt"
                  200
                  (Conformance.codecLawsAt CodecLawsTests.referenceCodec CodecLawsTests.referenceGen 442 200)
              // Phase 360 — the encoding-profile vectors (a fixed corpus) and the stored-identity families,
              // each at a store the ordinary append wrote: the store, walked whole, is the sample.
              run "EncodingProfileVectors.laws" 1 (EncodingProfileVectors.laws ())
              // Phase 390 — each vector family's `…With` over its committed corpus, and the cross-pipeline
              // value table: the table, walked whole, is the sample.
              run "WireNullTolerance.lawsWith" 1 (WireNullTolerance.lawsWith WireNullTolerance.vectors)
              run
                  "StringEscapeVectors.lawsWith"
                  1
                  (StringEscapeVectors.lawsWith StringEscapeVectors.vectors StringEscapeVectors.actorVectors)
              run
                  "EncodingProfileVectors.lawsWith"
                  1
                  (EncodingProfileVectors.lawsWith EncodingProfileVectors.vectors EncodingProfileVectors.actorVectors)
              run "ParityVectors.laws" 1 (ParityVectors.laws ())
              run "ParityVectors.lawsWith" 1 (ParityVectors.lawsWith ParityVectors.vectors)
              // Phase 379 — the stored-codec family at a two-text store written under `v2` through the
              // identity codec, one text carrying a line feed so the positive control has a text to move.
              run
                  "EncodingProfileVectors.storedCodecLawsAt"
                  2
                  (EncodingProfileVectors.storedCodecLawsAt "v2" Codec.json [ "{\"a\":\"x\\u000a\"}"; "[1,2]" ])
              run
                  "StoredIdentity.linearLaws"
                  3
                  (StoredIdentity.linearLaws
                      "v2"
                      OpStream.defaultHash
                      storedIdentityWitness
                      storedIdentityWitness
                      storedIdentityLinear)
              run
                  "StoredIdentity.dagLaws"
                  3
                  (StoredIdentity.dagLaws
                      "v2"
                      OpStream.defaultHash
                      storedIdentityWitness
                      storedIdentityWitness
                      storedIdentityDag)
              run
                  "StoredIdentity.captureLaws"
                  2
                  (StoredIdentity.captureLaws "v2" OpStream.defaultHash storedIdentityCaptures) ]

         match declared |> List.countBy fst |> List.filter (fun (_, n) -> n > 1) with
         | [] -> Map.ofList declared
         | twice -> failwithf "a family has more than one reference run: %A" (List.map fst twice))

/// The reference runs, in id order.
let private allRuns () : Run list =
    runs.Value |> Map.toList |> List.map snd

/// The family's own adequacy class, which is what decides how its run is read. Looked up rather
/// than passed, because the census is the single declaration and this file must not become a
/// second one.
let private classOf (id: string) : AdequacyClass =
    match KitRoster.census |> List.tryFind (fun (k, _) -> k = id) with
    | Some(_, k) -> k
    | None -> failwithf "%s has no census row (SampleAdequacy.census) — the roster and the census disagree" id

/// Phase 297 — what one family's guards say against the roster and its census class: every guard
/// carries the family's own id, a `Guarded` family emits at least one guard, and an `Unconditional`
/// family emits none. Pure, so the go-red perturbs its input rather than the tree.
///
/// Phase 498 — the one other label a guard may carry is a delegate the family's OWN census row
/// declares, as `(delegates to <entry>)` in a `Guarded` dimension: a family that runs another entry
/// point which mints its guard under its own id (`reconcileLaws` runs `reconcileLawsWith`,
/// `laneFoldLawsAt` the public `laneFoldLawsWith`, which takes no label to hand it) emits that
/// entry's guard, and the row says so. Until Phase 498 ANY roster id passed, which is how
/// `keyedArbitrationLawsAt` read its guard under `keyedArbitrationLawsWith`'s id with nothing able to
/// object; it now hands the internal its own id, its row declares no delegate, and the label is held
/// to the row rather than to the whole roster.
///
/// Phase 515 — a guard is a result whose `Kind` is `Guard` (a `Declared` line is the family's
/// adequacy line too, and is labelled the same way), and its label is its `Family`: both read off the
/// result, where they were cut out of its law text.
let guardDefects (ids: Set<string>) (id: string) (klass: AdequacyClass) (results: LawResult list) : string list =
    let guards = results |> List.filter LawResult.isAdequacy

    // A declared delegate is in the family's own module: `<Module>.<entry>`.
    let declared =
        let marker = "(delegates to "
        let modulePrefix = id.Substring(0, id.LastIndexOf '.' + 1)

        match klass with
        | Guarded dims ->
            [ for d in dims do
                  let at = d.IndexOf marker

                  if at >= 0 then
                      let rest = d.Substring(at + marker.Length)
                      let close = rest.IndexOf ')'

                      if close > 0 then
                          yield modulePrefix + rest.Substring(0, close) ]
        | Unconditional _ -> []

    let bare =
        guards
        |> List.map _.Family
        |> List.filter (fun l -> l <> id && not (List.contains l declared && Set.contains l ids))
        |> List.distinct
        |> List.map (fun l ->
            sprintf
                "%s: a guard is labelled `%s`, which is neither its own id nor a roster id its census row declares it delegates to"
                id
                l)

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
    allRuns ()
    |> List.map (fun r -> r.Id, SampleAdequacy.cases r.Id (classOf r.Id) r.Iterations r.Results)
    |> List.sortBy fst

/// Phase 223 — the six families Phase 220's audit found drawing a refusal population a run could
/// silently miss. Five are this repository's since Phase 258: `transformLaws` left with the dataframe
/// layer it reads (D66), and the compute repository's suite holds its row. Since Phase 297 `casLaws`
/// also counts its race arm (two `appendIf` calls at one head that both apply), which is drawn too.
/// Since Phase 515 the verdicts each is `Guarded` over are read from the roster and held to the
/// counts its guards carry (`Reached`), where a second list here restated them.
/// Each one's reference run as a function of the seed, at the size the census runs it:
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
      "Conformance.aiSurfaceLawsAt",
      (200,
       fun seed ->
           Conformance.aiSurfaceLawsAt
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
              //
              // Phase 507 — the roster is walked THROUGH the map: every family's run is looked up by
              // its id, so a family with no run is a missing key, named.
              let unrun =
                  [ for f in KitRoster.families do
                        match Map.tryFind (LawKit.FamilyId f.Id) runs.Value with
                        | Some r when r.Id = f.Id -> ()
                        | Some r -> yield sprintf "%s (keyed, but the run says %s)" f.Id r.Id
                        | None -> yield f.Id ]

              Expect.isEmpty
                  unrun
                  "these law families have no reference run in ConformanceVacuityTests — add one in the commit that ships the family, or its census cell reads `unmeasured` forever"

              let ran = allRuns () |> List.map _.Id |> Set.ofList
              let rostered = Set.ofList KitRoster.ids

              Expect.isEmpty (Set.difference ran rostered |> Set.toList) "these reference runs name no roster family"

          testCase "the reference run is green — a red law makes its count meaningless"
          <| fun _ ->
              // The counts below are only evidence if the laws they count held. A failing law here
              // is not this file's finding to report (the owning suite reports it properly); it is
              // the reason to stop trusting the census.
              let failed =
                  [ for r in allRuns () do
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
                  Conformance.attestationLawsAt
                      ConformanceTests.sw
                      ConformanceTests.streamGen
                      Attestation.noAttestation
                      OpStream.defaultHash
                      4242
                      200

              let subject = results |> List.filter (LawResult.isAdequacy >> not)

              Expect.isTrue
                  (subject |> List.forall _.Passed)
                  "the five subject laws are green under the no-op sink — which is the problem, not the fix"

              let measured =
                  SampleAdequacy.cases
                      "Conformance.attestationLawsAt"
                      (classOf "Conformance.attestationLawsAt")
                      200
                      results

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
                  if passed then
                      LawResult.pass "M.f" law 1
                  else
                      LawResult.fail "M.f" law 1 "a counterexample"

              let guard family passed =
                  SampleAdequacy.reached family "arm" 1 [ "arm", (if passed then 1 else 0) ]

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

              Expect.equal starved.Starved [ "arm" ] "and the cell names the dimension the guard carries in its kind"

              // A DELEGATING family emits its delegate's id on the guard — `columnarOpLaws` runs
              // `columnarOpLawsWith`, `IncrementalDelta.laws` runs `lawsWith` — and that guard is
              // still the caller's own verdict, which is why the census reads the guard's KIND and
              // never its family. Keying on the family would read these as subject laws and lose
              // exactly the starvation the census exists to show.
              let delegated =
                  SampleAdequacy.cases "M.f" (Guarded [ "arm" ]) 10 [ subject "a" true; guard "M.fWith" false ]

              Expect.isTrue
                  (SampleAdequacy.isVacuous delegated)
                  "a delegate's guard is the delegating family's starvation"

              Expect.equal
                  delegated.Starved
                  [ "arm" ]
                  "and the dimension is the guard's own, whichever family minted it"

          // ---- Phase 297: guards the census can see, and a class read off the code ----

          testCase
              "every guard carries its family's own id or a delegate its census row declares, every Guarded family emits one, and no Unconditional family does"
          <| fun _ ->
              // Until Phase 297 seven families labelled their guards with a bare entry name and one
              // with its module name, so a census keyed by roster id read none of them; and the
              // census class was a declaration nothing compared with what the family emits. Both
              // are read off the reference run here.
              let ids = Set.ofList KitRoster.ids

              let defects =
                  [ for r in allRuns () do
                        yield! guardDefects ids r.Id (classOf r.Id) r.Results ]

              Expect.isEmpty
                  defects
                  (sprintf "guard labels or census classes disagree with the code:\n%s" (String.concat "\n" defects))

          testCase "the guard-label check goes red in all three directions — made-up runs"
          <| fun _ ->
              let ids = Set.ofList [ "M.f"; "M.fWith" ]

              let guard (label: string) =
                  SampleAdequacy.reached label "arm" 1 [ "arm", 1 ]

              let subject = LawResult.pass "M.f" "a" 1

              Expect.isEmpty
                  (guardDefects ids "M.f" (Guarded [ "arm (delegates to fWith)" ]) [ subject; guard "M.fWith" ])
                  "a delegate the row declares is a legitimate label"

              Expect.isNonEmpty
                  (guardDefects ids "M.f" (Guarded [ "arm" ]) [ subject; guard "M.fWith" ])
                  "a delegate the row does not declare is named, roster id or not"

              Expect.isNonEmpty
                  (guardDefects ids "M.f" (Guarded [ "arm" ]) [ subject; guard "f" ])
                  "a bare entry name is not a roster id"

              // Phase 498 — another family's roster id is no longer enough: the label is the
              // family's own id or a delegate its own row declares, so an `…At` form labelled with
              // the `…With` id it shares an internal with is named.
              let wider = Set.ofList [ "M.f"; "M.fWith"; "M.fAt"; "M.g" ]

              Expect.isNonEmpty
                  (guardDefects wider "M.f" (Guarded [ "arm (delegates to fWith)" ]) [ subject; guard "M.g" ])
                  "another family's roster id is not this family's label"

              Expect.isNonEmpty
                  (guardDefects wider "M.fAt" (Guarded [ "arm" ]) [ subject; guard "M.fWith" ])
                  "an `…At` family labelled with the `…With` id it does not declare is named"

              Expect.isEmpty
                  (guardDefects wider "M.fAt" (Guarded [ "arm" ]) [ subject; guard "M.fAt" ])
                  "an `…At` family labelled with its own id is clean"

              Expect.isNonEmpty
                  (guardDefects ids "M.f" (Guarded [ "arm" ]) [ subject ])
                  "a Guarded family that emits no guard is named"

              Expect.isNonEmpty
                  (guardDefects ids "M.f" (Unconditional "built") [ subject; guard "M.f" ])
                  "an Unconditional family that emits a guard is named"

          testCase "a law the runner reports NEVER REACHED is starvation, under either class — made-up runs"
          <| fun _ ->
              // The runner's never-reached verdict: a strict law, red at zero evidence.
              let unreached =
                  LawResult.fail "M.f" "the tamper law" 0 (SampleAdequacy.neverReached + " — widen the generator")

              let held = LawResult.pass "M.f" "the replay law" 10

              for klass in [ Unconditional "built"; Guarded [ "arm" ] ] do
                  let measured = SampleAdequacy.cases "M.f" klass 10 [ held; unreached ]
                  Expect.isTrue (SampleAdequacy.isVacuous measured) (sprintf "%A: a never-reached law is vacuity" klass)
                  Expect.equal measured.Starved [ "the tamper law" ] "and the cell names the law"

              // A refuted law was asserted: its counterexample is the assertion that failed.
              let refuted =
                  { unreached with
                      Evidence = 1
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
                  [ "Conformance.encoderInjectivityLawsAt",
                    Conformance.encoderInjectivityLawsAt artw encNode (fun r -> tree0, r) 4242 200
                    "Conformance.attributedLaws",
                    Conformance.attributedLaws ConformanceTests.sw refusedOps OpStream.defaultHash 4242 200
                    "Conformance.dagLaws",
                    Conformance.dagLaws ConformanceTests.sw constantOps OpStream.defaultHash 4242 200
                    "Conformance.captureReplayLaws",
                    Conformance.captureReplayLaws
                        (fun (n: int) -> Json.render (JInt n))
                        (fun s -> Json.parse s |> Result.bind (Decoder.describing Decoder.int))
                        (fun r -> 7, r)
                        OpStream.defaultHash
                        31337
                        200
                    "Conformance.hashFnLaws",
                    Conformance.hashFnLaws ConformanceTests.sw refusedOps OpStream.defaultHash 4242 200 ]

              let isStarvation (r: LawResult) =
                  LawResult.isAdequacy r || LawResult.isNeverReached r

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
              let audited = KitRoster.refusalAudit |> List.map _.Family
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
                  |> List.map _.Family

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
                      (KitRoster.tryRefusal id |> Option.map _.Population)
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
                  (KitRoster.tryRefusal id |> Option.map _.Population)
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

              for id, _ in drawnRefusalSixRuns do
                  Expect.equal
                      (KitRoster.tryRefusal id |> Option.map _.Population)
                      (Some Families.Drawn)
                      (sprintf "%s is audited Drawn" id)

                  Expect.isTrue
                      (match classOf id with
                       | Guarded _ -> true
                       | Unconditional _ -> false)
                      (sprintf "%s is censused Guarded" id)

                  // Phase 515 — the run's guards say how often each side was reached, on the green
                  // path too: every guard of the reference run carries its counts, and every one of
                  // them is positive (a drawn family's reference generator is stratified to reach
                  // every verdict it counts, demanded or beside).
                  let guards =
                      (runs.Value[LawKit.FamilyId id]).Results
                      |> List.filter (fun r -> r.Kind.IsGuard)

                  Expect.isNonEmpty guards (sprintf "%s emits its guards at the reference witness" id)

                  for g in guards do
                      Expect.isTrue
                          (g.Passed
                           && not (List.isEmpty g.Reached)
                           && g.Reached |> List.forall (fun (_, n) -> n > 0))
                          (sprintf "%s: %A reached %A at the reference witness" id g.Kind g.Reached)

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

// ---------------------------------------------------------------------------
//  Phase 302 — the non-degeneracy floor, re-measured
// ---------------------------------------------------------------------------
//
// The audit that opened Phase 302 ran every family against deliberately broken witnesses and found
// the kit blind in one shape: an arm whose evidence is drawn and then gated on a difference the
// defective witness cannot produce, so the law is skipped and nothing counts the skip. Each case
// below is one cell of that matrix that was GREEN, and is the go-red that keeps it red.

let private redLaws (results: LawResult list) =
    results |> List.filter (fun r -> not r.Passed) |> List.map _.Law

let private hasRed (fragment: string) (results: LawResult list) =
    results |> List.exists (fun r -> not r.Passed && r.Law.Contains fragment)

let private constantEncoder: StreamWitness<Counter.CounterOp, int, string> =
    { ConformanceTests.sw with
        Encode = fun _ -> "{}" }

let private constantHash: HashFn = fun _ _ -> "h"

let private caseBlindIdw: IdWitness<string> =
    { idw with
        Equals = fun a b -> a.ToLowerInvariant() = b.ToLowerInvariant() }

let private constantNodeEncode (_: RNode) = "x"

/// Phase 302's premise, checked rather than trusted, and read off the results since Phase 515: a cell
/// that reads green at zero evidence because a guard counts its arm is a `Subject (Some dimension)`
/// result, and the law that reports its zero must be IN THE SAME RUN, naming that dimension: a guard
/// over it, or the family's corpus law counting it (`constructThenEncodeLaws`' round-trip document). A
/// covered dimension nothing in its run names is a cell that can be starved silently. Pure, so
/// the go-red perturbs its input rather than the tree. It replaces a regex over the kit's sources,
/// which could see a cell's text but not which run emitted it.
let coverageOrphans (results: LawResult list) : (string * string) list =
    let guards =
        results
        |> List.choose (fun r ->
            match r.Kind with
            | LawKind.Guard d
            | LawKind.Corpus d -> Some d
            | _ -> None)
        |> set

    [ for r in results do
          match r.Kind with
          | LawKind.Subject(Some d) when not (Set.contains d guards) -> yield r.Law, d
          | _ -> () ]

/// Every covered cell the reference runs emit, with its family and dimension.
let private coveredCells () : (string * string) list =
    [ for r in allRuns () do
          for law in r.Results do
              match law.Kind with
              | LawKind.Subject(Some d) -> yield r.Id, d
              | _ -> () ]
    |> List.distinct

[<Tests>]
let floorTests =
    testList
        "Conformance.Floor (Phase 302)"
        [ testCase
              "certify is red under each defect of the audit matrix — constant encoder, constant HashFn, case-blind Equals, refusal-free stream"
          <| fun _ ->
              let certifyAt sw' idw' hashFn streamGen' =
                  (Conformance.certify nodew idw' ConformanceTests.opGen sw' streamGen' hashFn 4242 200).Results

              let enc =
                  certifyAt constantEncoder idw OpStream.defaultHash ConformanceTests.streamGen

              Expect.isTrue (hasRed "tampered chain" enc) (sprintf "constant encoder: %A" (redLaws enc))

              let hash = certifyAt ConformanceTests.sw idw constantHash ConformanceTests.streamGen
              Expect.isTrue (hasRed "detects a tampered op" hash) (sprintf "constant HashFn: %A" (redLaws hash))

              let eq =
                  certifyAt ConformanceTests.sw caseBlindIdw OpStream.defaultHash ConformanceTests.streamGen

              Expect.isTrue (hasRed "IdWitness identities agree" eq) (sprintf "case-blind Equals: %A" (redLaws eq))

              let free =
                  certifyAt ConformanceTests.sw idw OpStream.defaultHash ConformanceTests.refusalFreeStreamGen

              Expect.isTrue
                  (free
                   |> List.exists (fun r -> not r.Passed && r.Kind.IsGuard && r.Family = "Conformance.reducer"))
                  (sprintf "refusal-free StreamGen: %A" (redLaws free))

              let reference =
                  certifyAt ConformanceTests.sw idw OpStream.defaultHash ConformanceTests.streamGen

              Expect.isEmpty (redLaws reference) "and the reference witness is green"

          testCase "certifyStream is red under the constant encoder and the constant HashFn"
          <| fun _ ->
              let enc =
                  (Conformance.certifyStream constantEncoder ConformanceTests.streamGen OpStream.defaultHash 4242 200)
                      .Results

              let hash =
                  (Conformance.certifyStream ConformanceTests.sw ConformanceTests.streamGen constantHash 4242 200)
                      .Results

              Expect.isTrue (hasRed "tampered chain" enc) (sprintf "constant encoder: %A" (redLaws enc))
              Expect.isTrue (hasRed "detects a tampered op" hash) (sprintf "constant HashFn: %A" (redLaws hash))

          testCase "hashFnLaws is red under a constant HashFn — the three arms that need the hash itself"
          <| fun _ ->
              let results =
                  Conformance.hashFnLaws ConformanceTests.sw ConformanceTests.streamGen constantHash 4242 200

              for fragment in [ "op-tamper detection"; "re-minting moves the head"; "content addressing" ] do
                  Expect.isTrue (hasRed fragment results) (sprintf "%s is red: %A" fragment (redLaws results))

              let honest =
                  Conformance.hashFnLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.defaultHash 4242 200

              Expect.isEmpty (redLaws honest) "and green under the default HashFn"

          // Phase 349 — every sanitisation law is red at a witness whose floor is open, each arm on
          // the defect it exists for; and green at Core's own floor.
          testCase "sanitizeLawsAt is red at an open floor, every law, and green at SanitizeWitness.core"
          <| fun _ ->
              let core = SanitizeWitness.core

              let witnesses =
                  [ "an accepted URL passes the floor", { core with SanitizeUrl = Some }
                    "the URL floor is idempotent",
                    { core with
                        SanitizeUrlOrBlank = fun u -> u + " " }
                    "the URL floor refuses every dangerous URL",
                    { core with
                        SanitizeUrl = fun _ -> None }
                    "the attribute floor is exactly its claim",
                    { core with
                        IsSafeAttributeValue = fun _ -> true }
                    "scrubbed markdown passes the floor", { core with ScrubMarkdown = id }
                    "the markdown scrub is idempotent",
                    { core with
                        ScrubMarkdown = fun s -> s + "x" } ]

              for fragment, w in witnesses do
                  let results = Conformance.sanitizeLawsAt w 349 200
                  Expect.isTrue (hasRed fragment results) (sprintf "%s is red: %A" fragment (redLaws results))

              Expect.isEmpty (redLaws (Conformance.sanitizeLawsAt core 349 200)) "and green at Core's own floor"

          // Phase 349 — the configured-stream laws are green at the canonical config as well as the
          // second one the reference run uses, so the family is not a statement about one format.
          testCase "streamConfigLaws are green at the canonical config and the default hash too"
          <| fun _ ->
              Expect.isEmpty
                  (redLaws (
                      Conformance.streamConfigLaws
                          ConformanceTests.sw
                          ConformanceTests.streamGen
                          OpStream.defaultHash
                          StreamConfig.canonical
                          349
                          100
                  ))
                  "green at the canonical config"

          testCase "a constant node encode starves all four confluence families on the encode-distinguished guard"
          <| fun _ ->
              let gen = ConformanceTests.opGen

              for name, results in
                  [ "footprintLaws", Conformance.footprintLaws nodew idw gen constantNodeEncode 4242 300
                    "concurrencyLaws", Conformance.concurrencyLaws nodew idw gen constantNodeEncode 8080 300
                    "reconcileLaws", Conformance.reconcileLaws nodew idw gen constantNodeEncode 5353 300
                    "arbitrationLaws", Conformance.arbitrationLaws nodew idw gen constantNodeEncode 8585 300 ] do
                  let guard =
                      results
                      |> List.exists (fun r ->
                          not r.Passed
                          && r.Kind.IsGuard
                          && List.contains ("encode-distinguished node", 0) r.Reached)

                  Expect.isTrue guard (sprintf "%s names the starved encode: %A" name (redLaws results))

          testCase "footprintLaws counts only non-empty independent pairs, and reaches them at the reference"
          <| fun _ ->
              let results =
                  Conformance.footprintLaws nodew idw ConformanceTests.opGen encNode 4242 300

              Expect.isEmpty (redLaws results) "the reference reaches non-empty independent pairs"

              let lone =
                  Conformance.footprintLaws nodew idw ConformanceTests.loneLeafGen encNode 4242 100

              Expect.isTrue
                  (hasRed "script-pair independence" lone)
                  (sprintf
                      "a generator whose every op is refused builds only empty scripts, and is told so: %A"
                      (redLaws lone))

          testCase "diffLaws and normalizeLaws are Guarded, and red over a generator whose every op is refused"
          <| fun _ ->
              let diff = Conformance.diffLaws nodew idw ConformanceTests.loneLeafGen 4242 100
              Expect.isTrue (hasRed "non-identity pair" diff) (sprintf "diffLaws: %A" (redLaws diff))

              let norm = Conformance.normalizeLaws nodew idw ConformanceTests.loneLeafGen 1234 100
              Expect.isTrue (hasRed "non-identity script" norm) (sprintf "normalizeLaws: %A" (redLaws norm))

              for family in [ "Conformance.diffLaws"; "Conformance.normalizeLaws" ] do
                  match SampleAdequacy.census |> List.tryFind (fun (id, _) -> id = family) with
                  | Some(_, Guarded _) -> ()
                  | other -> failtestf "%s left `Unconditional` (Phase 302): %A" family other

          testCase "idempotencyLaws answers a non-functional keyOf with a counterexample, never a throw"
          <| fun _ ->
              let calls = ref 0

              let drifting (op: Counter.CounterOp) =
                  calls.Value <- calls.Value + 1
                  sprintf "%A#%d" op calls.Value

              let results =
                  Conformance.idempotencyLaws
                      drifting
                      ConformanceTests.sw
                      ConformanceTests.stratifiedStreamGen
                      OpStream.defaultHash
                      4242
                      50

              Expect.isTrue
                  (results
                   |> List.exists (fun r ->
                       not r.Passed
                       && (r.Counterexample
                           |> Option.exists (fun cx -> cx.Contains "keyOf is not a function"))))
                  (sprintf "the duplicate law names the defect: %A" (redLaws results))

          testCase
              "queryLawsAt binds a settled result to its declared schema — a resolver that answers another schema is red"
          <| fun _ ->
              let w = WitnessTakingFamiliesTests.queryWitness

              let lying (args: (string * Cell) list) (q: Query) =
                  match WitnessTakingFamiliesTests.queryResolver args q with
                  | Ready r ->
                      Ready
                          { r with
                              Rows =
                                  { Schema = [ Field.create "other" IntType ]
                                    Columns = [ cellColumn "other" IntType [ Int 11 ] ] } }
                  | d -> d

              let results = Conformance.queryLawsAt w lying 4242 200
              Expect.isTrue (hasRed "bound to its declaration" results) (sprintf "%A" (redLaws results))

              let honest =
                  Conformance.queryLawsAt w WitnessTakingFamiliesTests.queryResolver 4242 200

              Expect.isEmpty (redLaws honest) "and the reference resolver is green"

          testCase "propagationEvaluatorLawsAt is red on an evaluator that asks for a read it does not declare"
          <| fun _ ->
              let w = ConformanceTests.sheetw

              let peeking =
                  { w with
                      EvalNode =
                          fun m resolve id ->
                              resolve "undeclared-peek" |> ignore
                              w.EvalNode m resolve id }

              let results = Conformance.propagationEvaluatorLawsAt peeking 4242 100

              Expect.isTrue
                  (results
                   |> List.exists (fun r ->
                       not r.Passed
                       && (r.Counterexample |> Option.exists (fun cx -> cx.Contains "do not hold"))))
                  (sprintf "%A" (redLaws results))

          testCase "every covered cell names a guard or corpus law of its own run — the zero is always reported"
          <| fun _ ->
              Expect.isNonEmpty (coveredCells ()) "the reference runs emit the covered cells this is about"

              let orphans =
                  [ for r in allRuns () do
                        for law, dim in coverageOrphans r.Results do
                            yield sprintf "%s: %s (covered by %s)" r.Id law dim ]

              Expect.isEmpty
                  orphans
                  (sprintf
                      "a covered cell whose dimension no guard emits can be starved silently:
%s"
                      (String.concat
                          "
"
                          orphans))

          testCase "the covered-cell check goes red on a planted orphan — made-up runs"
          <| fun _ ->
              let covered dim =
                  { LawResult.pass "M.f" "a covered law" 0 with
                      Kind = LawKind.Subject(Some dim) }

              let guard = SampleAdequacy.reached "M.f" "arm" 1 [ "arm", 3 ]

              Expect.isEmpty (coverageOrphans [ covered "arm"; guard ]) "a cell its run's guard counts is clean"

              Expect.equal
                  (coverageOrphans [ covered "elsewhere"; guard ])
                  [ "a covered law", "elsewhere" ]
                  "a cell no guard of its run counts is named"

              Expect.equal
                  (coverageOrphans [ covered "arm" ])
                  [ "a covered law", "arm" ]
                  "a guard in another run does not cover it"

              let corpusLaw =
                  { LawResult.pass "M.f" "the corpus holds a document" 2 with
                      Kind = LawKind.Corpus "document" }

              Expect.isEmpty (coverageOrphans [ covered "document"; corpusLaw ]) "a corpus law of its run covers it" ]

// ---- Phase 383 — the collision search demands a budget ----

[<Tests>]
let budgetTests =
    testList
        "hashFnAdversarialLaws demands a budget (Phase 383)"
        [ testCase "a budget below two is a named refusal, and the resistance law is never green over no pre-image"
          <| fun _ ->
              for budget in [ -1; 0; 1 ] do
                  let results =
                      Conformance.hashFnAdversarialLaws ConformanceTests.wideHash budget 4242

                  Expect.isTrue
                      (hasRed "budget is at least two" results)
                      (sprintf "budget=%d is refused by name: %A" budget (redLaws results))

                  Expect.isTrue
                      (hasRed "resists a re-hashed forgery" results)
                      (sprintf "budget=%d: the resistance law hashed nothing and must not read green" budget)

          testCase "a budget of two is admitted — the refusal is the argument's, not the outcome's"
          <| fun _ ->
              let results = Conformance.hashFnAdversarialLaws ConformanceTests.wideHash 2 4242

              Expect.isFalse
                  (results |> List.exists (fun r -> r.Law.Contains "budget is at least two"))
                  "no budget refusal at two"

              Expect.isFalse (hasRed "resists a re-hashed forgery" results) "two distinct pre-images, no collision" ]

// ---- Phase 390 — the vector families, planted empty ----
//
// A vector family's sample is the table it is handed, so the vacuity a drawn family's guard catches
// has one shape here: a run handed NO vectors. Each family answers it with a law of its own that goes
// red by name, and this list is what holds every vector family the roster declares to that.

/// The roster modules that hold VECTOR families — runners over an enumerated table rather than drawn
/// law families. Held equal to the roster's modules below, so a new kit module is classified here.
let private vectorModules =
    set
        [ "WireNullTolerance"
          "StringEscapeVectors"
          "EncodingProfileVectors"
          "StoredIdentity"
          "ParityVectors" ]

/// Each vector family's run over NO vectors, with the item its `Corpus` law counts — the law that
/// must read red (Phase 515: found by its kind, where a fragment of its name used to find it).
let private zeroVectorRuns: (string * string * (unit -> LawResult list)) list =
    let corpus = "vector"

    [ "WireNullTolerance.laws", corpus, (fun () -> WireNullTolerance.lawsWith [])
      "WireNullTolerance.lawsWith", corpus, (fun () -> WireNullTolerance.lawsWith [])
      "StringEscapeVectors.laws", corpus, (fun () -> StringEscapeVectors.lawsWith [] [])
      "StringEscapeVectors.lawsWith", corpus, (fun () -> StringEscapeVectors.lawsWith [] [])
      "EncodingProfileVectors.laws", corpus, (fun () -> EncodingProfileVectors.lawsWith [] [])
      "EncodingProfileVectors.lawsWith", corpus, (fun () -> EncodingProfileVectors.lawsWith [] [])
      "ParityVectors.laws", corpus, (fun () -> ParityVectors.lawsWith [])
      "ParityVectors.lawsWith", corpus, (fun () -> ParityVectors.lawsWith [])
      // The stored families' sample is the store a consumer hands them: an empty store is the zero.
      "EncodingProfileVectors.storedCodecLawsAt",
      "text",
      (fun () -> EncodingProfileVectors.storedCodecLawsAt "v2" Codec.json [])
      "StoredIdentity.linearLaws",
      "record",
      (fun () ->
          StoredIdentity.linearLaws
              "v2"
              OpStream.defaultHash
              storedIdentityWitness
              storedIdentityWitness
              OpStream.empty)
      "StoredIdentity.dagLaws",
      "node",
      (fun () -> StoredIdentity.dagLaws "v2" OpStream.defaultHash storedIdentityWitness storedIdentityWitness Dag.empty)
      "StoredIdentity.captureLaws", "capture", (fun () -> StoredIdentity.captureLaws "v2" OpStream.defaultHash []) ]

[<Tests>]
let vectorFamilyTests =
    testList
        "Conformance.VectorFamilies (Phase 390)"
        [ testCase "every vector family the roster declares has a planted zero-vector run here"
          <| fun _ ->
              let declared =
                  Families.families
                  |> List.filter (fun f -> Set.contains f.Module vectorModules)
                  |> List.map _.Id
                  |> set

              let planted = zeroVectorRuns |> List.map (fun (id, _, _) -> id) |> set
              Expect.equal planted declared "the planted runs and the roster's vector families are one set"

          testCase
              "every kit module is classified: law families in Conformance and FoldConfluence, the rest vector families"
          <| fun _ ->
              Expect.equal
                  (set Families.modules)
                  (Set.union vectorModules (set [ "Conformance"; "FoldConfluence" ]))
                  "a new kit module is classified here, so its families cannot escape the zero-vector check"

          testCase "a zero-vector run of every vector family is red, by name"
          <| fun _ ->
              for id, what, runEmpty in zeroVectorRuns do
                  let results = runEmpty ()

                  Expect.isTrue
                      (results
                       |> List.exists (fun r -> r.Kind = LawKind.Corpus what && LawResult.isNeverReached r))
                      (sprintf "%s over no %ss must be red by its corpus law: %A" id what (redLaws results))

          testCase "laws () is lawsWith over the committed corpus, and green"
          <| fun _ ->
              let pairs =
                  [ "WireNullTolerance", WireNullTolerance.laws (), WireNullTolerance.lawsWith WireNullTolerance.vectors
                    "StringEscapeVectors",
                    StringEscapeVectors.laws (),
                    StringEscapeVectors.lawsWith StringEscapeVectors.vectors StringEscapeVectors.actorVectors
                    "EncodingProfileVectors",
                    EncodingProfileVectors.laws (),
                    EncodingProfileVectors.lawsWith EncodingProfileVectors.vectors EncodingProfileVectors.actorVectors
                    "ParityVectors",
                    ParityVectors.laws (),
                    ParityVectors.lawsWith (
                        ParityVectors.vectors @ ParityVectors.hashSweep @ ParityVectors.sanitiseSweep
                    ) ]

              for name, laws, withCorpus in pairs do
                  // The two are two roster rows, so each entry labels its results with its own id.
                  Expect.equal
                      laws
                      (withCorpus |> LawKit.labelAs (name + ".laws"))
                      (sprintf "%s.laws () is lawsWith over its committed corpus, under its own id" name)

                  Expect.isTrue
                      (withCorpus |> List.forall (fun r -> r.Family = name + ".lawsWith"))
                      (sprintf "%s.lawsWith labels every result with its own id" name)

                  Expect.isEmpty (redLaws laws) (sprintf "%s is green over its committed corpus" name)

          testCase "ParityVectors.lawsWith is red on a row that breaks what makes it comparable"
          <| fun _ ->
              let red row fragment =
                  let results = ParityVectors.lawsWith [ row ]
                  Expect.isTrue (hasRed fragment results) (sprintf "%A must red %s: %A" row fragment (redLaws results))

              red ("a b", "00") "names one row"
              red ("café", "00") "printable ASCII"
              red ("idlSanitize/scrub/x", "diverges:00") "sanitiser-sweep row reads ok"

              Expect.isTrue
                  (hasRed "names one row" (ParityVectors.lawsWith [ "a", "1"; "a", "2" ]))
                  "a repeated label is red"

          testCase "no cell in the kit asserts a literal true — a tautological check is evidence (`Saw`), not a law"
          <| fun _ ->
              // Phase 390. `Check(true, …)` takes the evidence and asserts nothing, so the census reads
              // it as a law that held. The two sites it found became `Saw()`; this keeps the shape out.
              let dir =
                  Path.Combine(
                      Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx"),
                      "src",
                      "Fuaran.Core.Conformance"
                  )

              let pattern = Regex(@"\.Check\s*\(\s*true\s*,")

              let offenders =
                  [ for file in Directory.GetFiles(dir, "*.fs") do
                        let lines = File.ReadAllLines file

                        for i in 0 .. lines.Length - 1 do
                            if pattern.IsMatch lines[i] then
                                yield sprintf "%s:%d" (Path.GetFileName file) (i + 1) ]

              Expect.isNonEmpty (Directory.GetFiles(dir, "*.fs")) "the scan read the kit's sources"
              Expect.isEmpty offenders "a literal-true check is `Saw()` in disguise" ]

// ---- Phase 390 — the naming rule, held to the roster ----
//
// D78's rule as Phase 390 applies it (DECISIONS.md D127): a family that takes a witness capability
// the base contract does not (`NeedsWitnessCapability`) is spelled `…At`, and its configured form is
// `…With` — the `…At` family with one more parameter, last before the seed, never `…AtWith`. A bare
// spelling survives only as an `[<Obsolete>]` forward until `0.37.0`. This is what keeps a family added
// before or after `1.0` from reintroducing the exception the rule exists to remove.

/// Whether the kit's public entry `Module.Entry` carries `[<Obsolete>]` — read by reflection, so a
/// forward is recognised by what it IS rather than by a list kept here.
let private isObsoleteIn (asm: System.Reflection.Assembly) (typeName: string) (entry: string) : bool =
    let t = asm.GetType typeName

    not (isNull t)
    && t.GetMethods()
       |> Array.exists (fun mi ->
           mi.Name = entry
           && not (isNull (System.Attribute.GetCustomAttribute(mi, typeof<System.ObsoleteAttribute>))))

let private isObsoleteEntry (m: string) (entry: string) : bool =
    isObsoleteIn typeof<LawResult>.Assembly ("Fuaran.Core." + m) entry

/// A planted obsolete forward beside a live `…At` form (Phase 386): the kit ships none from `0.37.0`
/// (the `OneDotZero` family holds that), so the exemption's reading is probed against this decoy.
module ObsoleteDecoy =
    let decoyLawsAt (seed: int) = seed

    [<System.Obsolete("a planted forward — the naming rule's obsolete reading is probed against it")>]
    let decoyLaws (seed: int) = decoyLawsAt seed

/// Every way a roster breaks the rule, named. Pure over its inputs so the go-red below can plant one.
let private namingDefects (families: Families.LawFamily list) (obsolete: string -> string -> bool) : string list =
    let live = families |> List.filter (fun f -> not (obsolete f.Module f.Entry))
    // A pinned-default spelling is an entry point of the family (Phase 507), so an `…At` spelling
    // stands beside its `…With` exactly as an `…At` row did.
    let ids = families |> List.collect (fun f -> f.Id :: f.Spellings) |> set

    [ for f in live do
          let witnessTaking = f.Reason = Some Families.NeedsWitnessCapability

          if f.Entry.Contains "AtWith" then
              yield sprintf "%s: a double suffix — the configured form of an …At family is …With" f.Id

          if witnessTaking && not (f.Entry.EndsWith "At" || f.Entry.EndsWith "With") then
              yield sprintf "%s takes a witness capability and is spelled neither …At nor …With" f.Id

          if witnessTaking && f.Entry.EndsWith "With" then
              let at = f.Module + "." + f.Entry.Substring(0, f.Entry.Length - 4) + "At"

              if not (Set.contains at ids) then
                  yield sprintf "%s is a configured form with no %s beside it" f.Id at

          if f.Entry.EndsWith "At" && List.isEmpty f.Witness then
              yield sprintf "%s is spelled …At and takes no witness" f.Id ]

[<Tests>]
let namingRuleTests =
    testList
        "Conformance.NamingRule (Phase 390)"
        [ testCase "every live witness-taking family is …At or …With, every …With has its …At, and no …AtWith"
          <| fun _ -> Expect.isEmpty (namingDefects Families.families isObsoleteEntry) "the roster follows the rule"

          testCase "the obsolete reading sees the forwards it exempts"
          <| fun _ ->
              // The kit ships no forward at 0.37.0, so the reading is probed against a planted one.
              Expect.isNotNull
                  (typeof<LawResult>.Assembly.GetType "Fuaran.Core.Conformance")
                  "the reading resolves the kit's facade by name"

              let here = System.Reflection.Assembly.GetExecutingAssembly()
              let decoyType = "Fuaran.Core.Tests.ConformanceVacuityTests+ObsoleteDecoy"

              Expect.isTrue (isObsoleteIn here decoyType "decoyLaws") "a planted obsolete forward is read as one"
              Expect.isFalse (isObsoleteIn here decoyType "decoyLawsAt") "its …At form is live"

          testCase "go-red: a planted bare witness-taking family, a double suffix and an orphaned …With are each named"
          <| fun _ ->
              let template =
                  Families.families |> List.find (fun f -> f.Id = "Conformance.keyedApplyLawsAt")

              let plant entry =
                  { template with
                      Id = "Conformance." + entry }

              let defects =
                  namingDefects [ plant "plantedLaws"; plant "plantedLawsAtWith"; plant "orphanLawsWith" ] (fun _ _ ->
                      false)

              // Each plant is named for its own defect and nothing else: the entry point a plant is
              // read under is its `Id` (`Entry` is derived from it), so the findings are exactly these.
              Expect.equal
                  defects
                  [ "Conformance.plantedLaws takes a witness capability and is spelled neither …At nor …With"
                    "Conformance.plantedLawsAtWith: a double suffix — the configured form of an …At family is …With"
                    "Conformance.plantedLawsAtWith is a configured form with no Conformance.plantedLawsAtAt beside it"
                    "Conformance.orphanLawsWith is a configured form with no Conformance.orphanLawsAt beside it" ]
                  "the plants are named, each for its own defect" ]

// ---------------------------------------------------------------------------
//  the pinned-default spellings (Phase 507)
// ---------------------------------------------------------------------------

/// Each pinned-default spelling the roster declares, run beside its family called with the pinned
/// parameter at its default, at one seed and size. The roster reads a spelling as its family's (one
/// census cell, one reference run); that reading is only honest if the two answer alike, which is
/// what this holds.
let private spellingPairs: (string * string * Lazy<LawResult list * LawResult list>) list =
    [ "Conformance.reconcileLaws",
      "Conformance.reconcileLawsWith",
      lazy
          (Conformance.reconcileLaws nodew idw ConformanceTests.opGen encNode 5353 60,
           Conformance.reconcileLawsWith nodew idw ConformanceTests.opGen encNode OpStream.defaultHash 5353 60)
      "Conformance.concurrencyLaws",
      "Conformance.concurrencyLawsWith",
      lazy
          (Conformance.concurrencyLaws nodew idw ConformanceTests.opGen encNode 8080 60,
           Conformance.concurrencyLawsWith nodew idw ConformanceTests.opGen encNode honestFootprint 8080 60)
      "Conformance.keyedArbitrationLawsAt",
      "Conformance.keyedArbitrationLawsWith",
      lazy
          (Conformance.keyedArbitrationLawsAt
              ConformanceTests.keyw
              ConformanceTests.knodew
              idw
              KeyedArbitrationTests.containedKGen
              KeyedArbitrationTests.encK
              2470
              60,
           Conformance.keyedArbitrationLawsWith
               ConformanceTests.keyw
               ConformanceTests.knodew
               idw
               KeyedArbitrationTests.containedKGen
               KeyedArbitrationTests.encK
               (Ops.footprint
                   Policy.none
                   (OpScope.keyed ConformanceTests.keyw (fun _ -> true) ConformanceTests.knodew idw))
               (Ops.canApplyAll
                   Policy.none
                   (OpScope.keyed
                       ConformanceTests.keyw
                       (LawKit.canHoldOf KeyedArbitrationTests.containedKGen)
                       ConformanceTests.knodew
                       idw))
               2470
               60)
      "Conformance.snapshotLaws",
      "Conformance.snapshotLawsWith",
      lazy
          (Conformance.snapshotLaws ConformanceTests.sw ConformanceTests.streamGen string OpStream.defaultHash 321 40,
           Conformance.snapshotLawsWith
               ConformanceTests.sw
               ConformanceTests.streamGen
               string
               OpStream.defaultHash
               StreamConfig.canonical
               321
               40)
      "Conformance.columnOwnershipLaws",
      "Conformance.columnOwnershipLawsWith",
      lazy
          (Conformance.columnOwnershipLaws ColumnTests.coreColumnReads 4242 40,
           Conformance.columnOwnershipLawsWith ColumnTests.coreColumnReads ColumnarSeamLaws.ownershipSample 4242 40)
      "FoldConfluence.laneFoldLawsAt",
      "FoldConfluence.laneFoldLawsWith",
      lazy
          (FoldConfluence.laneFoldLawsAt
              FoldFixtures.treeW
              FoldFixtures.treeFootprint
              FoldFixtures.treeHash
              FoldFixtures.treeLaneGen
              3
              1000
              40,
           FoldConfluence.laneFoldLawsWith
               FoldFixtures.treeW
               FoldFixtures.treeFootprint
               FoldFixtures.treeHash
               FoldFixtures.treeLaneGen
               3
               OpStream.defaultHash
               1000
               40) ]

[<Tests>]
let spellingTests =
    testList
        "Conformance.Spellings (Phase 507)"
        [ testCase "the pairs here are exactly the roster's spellings"
          <| fun _ ->
              let declared =
                  [ for f in KitRoster.families do
                        for sp in f.Spellings -> sp, f.Id ]
                  |> Set.ofList

              let paired = spellingPairs |> List.map (fun (sp, f, _) -> sp, f) |> Set.ofList
              Expect.equal paired declared "every declared spelling is held below, under the family that declares it"

          testCase "a spelling answers exactly as its family at the pinned default — results and guard labels"
          <| fun _ ->
              for spelling, family, pair in spellingPairs do
                  let viaSpelling, viaFamily = pair.Value
                  Expect.isNonEmpty viaFamily (sprintf "%s answered nothing" family)
                  Expect.equal viaSpelling viaFamily (sprintf "%s is not %s at its default" spelling family) ]
