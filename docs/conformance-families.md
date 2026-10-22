# The law families this kit ships

_GENERATED from `Fuaran.Core.Families`. Do not hand-edit — the suite compares this file
against the roster and names the command that rewrites it:_

```
dotnet run --project tools/Fuaran.Core.Tools -- render families
```

_The machine-readable half is `conformance-families.json` beside it; `STABILITY.md`
documents its shape. Why each row reads as it does is recorded, phase by phase, in
`families-history.md`._

A **law family** is a public entry point of this kit that answers with `LawResult list`; running
one at your own witness is how a domain certifies it conforms. The table is held to reflection
over the shipped assemblies BY RETURN TYPE, so it cannot miss a family by how it is named.

**Base run / opt-in.** `base run` families are the ones `Conformance.certify` and
`Conformance.certifyStream` are built from; every other family is opt-in, for the reason its
`Why opt-in` cell gives, and a domain calls it deliberately beside its base run.

**Witness.** The witness, generator and sink types the entry point takes, in parameter order;
a family with none runs against the kit's own fixtures, or the Core it was compiled against.

**Discharges.** The claims-ladder obligations (`proofs.json` row ids) a green run of the
family at your own witness discharges.

**Cases.** What a RUN measured at this repository's reference witness: the subject-law
assertions made. `vacuous` — never a number — means the run certified nothing (it asserted
nothing, or a guarded dimension starved, named in the cell); `unmeasured` means no run was
handed to the renderer, a different fact and a different word.

**Adequacy.** `unconditional` — every iteration builds every branch the laws distinguish.
`guarded-reached` — the run reached every guarded side. `guarded-starved` — the guard went red.
`guarded-unmeasured` — a guarded family no run was handed for.

**Refusal.** Where refused outcomes come from: `none`, `built` (no run can miss one),
`drawn-miss-is-red` (a law demands one, so a run that misses it fails) or `drawn` (a run that
misses one stays green unless the family is guarded).

**Package.** The package a family ships from. The families that read the dataframe layer ship
from `Fuaran.Compute.Conformance` (DECISIONS.md D68, D66), whose own census lists them.

95 families, across `Conformance`, `EncodingProfileVectors`, `FoldConfluence`, `ParityVectors`, `StoredIdentity`, `StringEscapeVectors`, `WireNullTolerance`, from `Fuaran.Core.Conformance`.

| Family | Package | Run by | Why opt-in | Witness | Discharges | Cases | Adequacy | Refusal |
|---|---|---|---|---|---|---|---|---|
| `Conformance.aggregateNullSkipLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 200 | `guarded-reached` | `none` |
| `Conformance.aiSurfaceKitPolicyLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `AiSurfaceWitness` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.aiSurfaceLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `AiSurfaceWitness` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.arbitrationLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1800 | `guarded-reached` | `drawn` |
| `Conformance.attestationLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `StreamWitness`, `StreamGen`, `IAttestationSink` | — | 1000 | `guarded-reached` | `built` |
| `Conformance.attributedLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `guarded-reached` | `drawn` |
| `Conformance.canonicalFloatLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 2000 | `unconditional` | `none` |
| `Conformance.capabilityLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 2000 | `unconditional` | `built` |
| `Conformance.capabilityLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `CapabilitySeamWitness` | — | 900 | `guarded-reached` | `drawn` |
| `Conformance.capabilityPipelineIncrementalLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `guarded-reached` | `none` |
| `Conformance.capabilityPipelineLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1400 | `unconditional` | `built` |
| `Conformance.capabilityPipelineLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `CapabilityPipelineWitness` | — | 800 | `guarded-reached` | `built` |
| `Conformance.captureReplayLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1000 | `guarded-reached` | `drawn` |
| `Conformance.casLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `guarded-reached` | `drawn` |
| `Conformance.chainBreakReasonLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `built` |
| `Conformance.checkpointLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 900 | `guarded-reached` | `drawn` |
| `Conformance.codecInjectivityLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `none` |
| `Conformance.codecLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `Codec` | `decode-layer-refusal-premises`, `decode-layer-round-trip-premises` | 1000 | `guarded-reached` | `drawn` |
| `Conformance.columnOwnershipLawsWith` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 200 | `unconditional` | `none` |
| `Conformance.columnVectorLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `none` |
| `Conformance.columnarValidatorLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 400 | `guarded-reached` | `drawn` |
| `Conformance.compositionLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 800 | `unconditional` | `none` |
| `Conformance.compositionPilotAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 1200 | `unconditional` | `none` |
| `Conformance.concurrencyLawsWith` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `guarded-reached` | `none` |
| `Conformance.constructThenEncodeLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 12 | `unconditional` | `none` |
| `Conformance.containerLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `guarded-reached` | `built` |
| `Conformance.containmentLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1600 | `guarded-reached` | `drawn` |
| `Conformance.dagBreakReasonLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 480 | `unconditional` | `built` |
| `Conformance.dagLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 900 | `unconditional` | `drawn-miss-is-red` |
| `Conformance.deferredLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `built` |
| `Conformance.diffContainedLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.diffLaws` | `Fuaran.Core.Conformance` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `guarded-reached` | `none` |
| `Conformance.digestLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1200 | `guarded-reached` | `none` |
| `Conformance.dirtyPropagationLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `guarded-reached` | `none` |
| `Conformance.encoderInjectivityLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 200 | `guarded-reached` | `none` |
| `Conformance.footprintInLawsAt` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 900 | `guarded-reached` | `none` |
| `Conformance.footprintLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 900 | `guarded-reached` | `none` |
| `Conformance.footprintLawsAt` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | `independence-diamond` | 900 | `guarded-reached` | `none` |
| `Conformance.freshIdLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1400 | `guarded-reached` | `none` |
| `Conformance.functionVerifyLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 600 | `unconditional` | `drawn-miss-is-red` |
| `Conformance.hashFnAdversarialLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1000000 | `unconditional` | `none` |
| `Conformance.hashFnLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 1200 | `guarded-reached` | `drawn` |
| `Conformance.idempotencyLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.introducedLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1200 | `guarded-reached` | `none` |
| `Conformance.keyedApplyLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen` | `witness-surface-scope` | 2400 | `guarded-reached` | `built` |
| `Conformance.keyedArbitrationLawsWith` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen` | — | 1400 | `guarded-reached` | `drawn` |
| `Conformance.keyedCaptureLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1200 | `unconditional` | `built` |
| `Conformance.keyedChildrenLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `guarded-reached` | `built` |
| `Conformance.keyedDiffLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen` | — | 800 | `guarded-reached` | `built` |
| `Conformance.laneLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 900 | `guarded-reached` | `drawn` |
| `Conformance.loweringLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 800 | `guarded-reached` | `built` |
| `Conformance.memoLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 1000 | `unconditional` | `none` |
| `Conformance.memoSoundnessLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 100 | `unconditional` | `none` |
| `Conformance.mergeConflictLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 900 | `guarded-reached` | `none` |
| `Conformance.noAttestationVacuityLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.normalizeLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `guarded-reached` | `none` |
| `Conformance.observerLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ObserverWitness` | — | 400 | `unconditional` | `none` |
| `Conformance.opAlgebra` | `Fuaran.Core.Conformance` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `tree-algebra-well-formed-states` | 1000 | `guarded-reached` | `drawn` |
| `Conformance.packLoadingLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1000 | `unconditional` | `built` |
| `Conformance.placementLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 800 | `guarded-reached` | `built` |
| `Conformance.policyLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1200 | `unconditional` | `built` |
| `Conformance.policyLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `GuardedSurfaceWitness` | — | 900 | `guarded-reached` | `drawn` |
| `Conformance.projectionLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ProjectionWitness` | — | 800 | `unconditional` | `none` |
| `Conformance.propagationEvalLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `guarded-reached` | `built` |
| `Conformance.propagationEvaluatorLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `EvaluatorWitness` | `propagation-change-set-and-prior` | 600 | `guarded-reached` | `drawn` |
| `Conformance.propagationPriorLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `EvaluatorWitness` | `propagation-prior-blind` | 720 | `guarded-reached` | `drawn` |
| `Conformance.queryLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 2600 | `unconditional` | `built` |
| `Conformance.queryLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `QuerySeamWitness` | — | 1200 | `guarded-reached` | `drawn` |
| `Conformance.reachLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 900 | `guarded-reached` | `drawn` |
| `Conformance.reconcileLawsWith` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1500 | `guarded-reached` | `drawn` |
| `Conformance.reducer` | `Fuaran.Core.Conformance` | base run | — | `StreamGen` | — | 400 | `guarded-reached` | `drawn` |
| `Conformance.referenceLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `RefWitness`, `NodeWitness`, `IdWitness`, `OpGen` | — | 1400 | `guarded-reached` | `drawn` |
| `Conformance.registryLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 3200 | `unconditional` | `built` |
| `Conformance.sanitizeLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `SanitizeWitness` | — | 1200 | `unconditional` | `built` |
| `Conformance.seamRefusalLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 276 | `unconditional` | `built` |
| `Conformance.snapshotLawsWith` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 200 | `unconditional` | `none` |
| `Conformance.streamConfigLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 500 | `unconditional` | `built` |
| `Conformance.streamLaws` | `Fuaran.Core.Conformance` | base run | — | `StreamWitness`, `StreamGen` | `op-stream-load-premises` | 800 | `guarded-reached` | `drawn` |
| `Conformance.verifyHonestyLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 400 | `unconditional` | `drawn-miss-is-red` |
| `Conformance.witnessLaws` | `Fuaran.Core.Conformance` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `lawful-abstract-witness` | 500 | `unconditional` | `none` |
| `Conformance.witnessSurfaceLaws` | `Fuaran.Core.Conformance` | opt-in | `no-witness-to-certify` | — | — | 17 | `unconditional` | `none` |
| `Conformance.writeGateLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1200 | `guarded-reached` | `drawn` |
| `EncodingProfileVectors.laws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 49 | `unconditional` | `none` |
| `EncodingProfileVectors.lawsWith` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 49 | `unconditional` | `none` |
| `EncodingProfileVectors.storedCodecLawsAt` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `Codec` | — | 10 | `unconditional` | `none` |
| `FoldConfluence.laneFoldLawsWith` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — | 360 | `guarded-reached` | `drawn` |
| `ParityVectors.laws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 5 | `unconditional` | `none` |
| `ParityVectors.lawsWith` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 5 | `unconditional` | `none` |
| `StoredIdentity.captureLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 8 | `unconditional` | `none` |
| `StoredIdentity.dagLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | `StreamWitness` | — | 12 | `unconditional` | `none` |
| `StoredIdentity.linearLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | `StreamWitness` | — | 12 | `unconditional` | `none` |
| `StringEscapeVectors.laws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 40 | `unconditional` | `none` |
| `StringEscapeVectors.lawsWith` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 40 | `unconditional` | `none` |
| `WireNullTolerance.laws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 16 | `unconditional` | `built` |
| `WireNullTolerance.lawsWith` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 16 | `unconditional` | `built` |
