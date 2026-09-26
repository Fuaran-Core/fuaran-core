# The law families this kit ships

_GENERATED from `Fuaran.Core.Families`. Do not hand-edit — the suite compares this file
against the roster and names the command that rewrites it:_

```
dotnet run --project tests/Fuaran.Core.Tests -- --emit-families
```

_The machine-readable half is `conformance-families.json` beside it; `STABILITY.md`
documents its shape._

A **law family** is a public entry point of this kit that answers with `LawResult list`.
Running one at your own witness is how a domain certifies it conforms; this table is the
enumeration of what there is to run, so a family cannot be quietly absent from a
conformance census that quantifies over it. The enumeration is held to reflection over
the shipped assemblies BY RETURN TYPE, so it cannot miss a family by how the family is
named.

**Base run / opt-in.** The five `base run` families are the ones `Conformance.certify` and
`Conformance.certifyStream` are built from — a domain gets them by calling an aggregate.
Every other family certifies a seam not every domain has, so a domain calls it
deliberately, alongside its base run. Neither is a statement about importance: `reducer`
is a base-run family only for stream-shaped domains, and `footprintLaws` is opt-in while
discharging a ladder obligation.

**Witness.** The witness and generator types the entry point takes, in parameter order. A
family with none runs against the kit's own fixtures and needs nothing but a seed.

**Discharges.** The claims-ladder obligations (`proofs.json` row ids) a green run of the
family at your own witness discharges. Most families discharge none — they certify, they
do not answer for an assumption this repository's proofs leave open.

**Cases.** What a RUN measured, not what the roster declares: the subject-law assertions
the family made at this repository's own reference witness. `vacuous` — never a number —
means the run certified nothing, either because it asserted nothing at all or because a
guarded dimension was starved, and the starved dimension is named in the cell. `vacuous`
is the state a family passing green while exercising nothing used to render as, which is
what this column exists to make impossible to read past. `unmeasured` means no run was
handed to the renderer, which is a different fact and deliberately a different word.

**Adequacy.** How the family's green run is to be read. `unconditional` — every iteration
builds every branch the laws distinguish, so a green run is a pass. `guarded-reached` — the
family carries an adequacy guard and this run reached every guarded side. `guarded-starved`
— the guard went red: the run tested nothing on a side a law is about, and the family is
RED in `certify`'s verdict rather than silently green. `guarded-unmeasured` — a guarded
family no run was handed for.

**Refusal.** The refusable-family audit (Phase 220): where the family's refused outcomes
come from. `none` — no law reads one. `built` — every refused case is constructed, so no
run can miss it. `drawn-miss-is-red` — drawn, but a law demands the refused case, so a
run that misses it fails. `drawn` — drawn, and a run that misses it stays green unless
the family is guarded, which is what the `Adequacy` cell beside it answers.

**Package.** The package a family ships from — the one reference a consumer adds to run it.
The families that read the dataframe layer ship from `Fuaran.Core.DataFrame.Conformance`
(Phase 257, DECISIONS.md D68); every other family ships from `Fuaran.Core.Conformance`.

71 families, across `Conformance`, `FoldConfluence`, `IncrementalDelta`, from `Fuaran.Core.Conformance`, `Fuaran.Core.DataFrame.Conformance`.

| Family | Package | Run by | Why opt-in | Witness | Discharges | Cases | Adequacy | Refusal |
|---|---|---|---|---|---|---|---|---|
| `Conformance.aggregateNullSkipLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 200 | `unconditional` | `none` |
| `Conformance.aggregateParityLaws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 200 | `unconditional` | `none` |
| `Conformance.aiSurfaceLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `AiSurfaceWitness` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.aiSurfaceLawsUnderKitPolicy` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `AiSurfaceWitness` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.arbitrationLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1800 | `guarded-reached` | `drawn` |
| `Conformance.attestationLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `StreamWitness`, `StreamGen`, `IAttestationSink` | — | 1000 | `guarded-reached` | `built` |
| `Conformance.attributedLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.canonicalFloatLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1500 | `unconditional` | `none` |
| `Conformance.capabilityLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1600 | `unconditional` | `built` |
| `Conformance.capabilityLawsWith` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `CapabilitySeamWitness` | — | 900 | `guarded-reached` | `drawn` |
| `Conformance.capabilityPipelineIncrementalLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `guarded-reached` | `none` |
| `Conformance.capabilityPipelineLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `built` |
| `Conformance.capabilityPipelineLawsWith` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `CapabilityPipelineWitness` | — | 800 | `guarded-reached` | `built` |
| `Conformance.captureReplayLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `unconditional` | `built` |
| `Conformance.casLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `guarded-reached` | `drawn` |
| `Conformance.chainBreakReasonLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `built` |
| `Conformance.codecInjectivityLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `none` |
| `Conformance.columnarOpLaws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1200 | `guarded-reached` | `drawn` |
| `Conformance.columnarOpLawsWith` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | `StreamGen` | — | 1200 | `guarded-reached` | `drawn` |
| `Conformance.columnarValidatorLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 400 | `guarded-reached` | `drawn` |
| `Conformance.compositionLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 800 | `unconditional` | `none` |
| `Conformance.compositionPilot` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 1200 | `unconditional` | `none` |
| `Conformance.concurrencyLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | `lanes-apply` | 900 | `guarded-reached` | `none` |
| `Conformance.concurrencyLawsWith` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 900 | `guarded-reached` | `none` |
| `Conformance.constructThenEncodeLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 12 | `unconditional` | `none` |
| `Conformance.containerLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `guarded-reached` | `built` |
| `Conformance.dagBreakReasonLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 480 | `unconditional` | `built` |
| `Conformance.dagLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 400 | `unconditional` | `built` |
| `Conformance.deferredLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `built` |
| `Conformance.diffContainedLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.diffLaws` | `Fuaran.Core.Conformance` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `unconditional` | `none` |
| `Conformance.dirtyPropagationLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `guarded-reached` | `none` |
| `Conformance.encoderInjectivityLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 200 | `unconditional` | `none` |
| `Conformance.footprintLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | `independence-diamond` | 900 | `guarded-reached` | `none` |
| `Conformance.functionVerifyLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 600 | `unconditional` | `drawn-miss-is-red` |
| `Conformance.hashFnAdversarialLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1000000 | `unconditional` | `none` |
| `Conformance.hashFnLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.idempotencyLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.incrementalLaws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 400 | `unconditional` | `none` |
| `Conformance.incrementalLawsWith` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | `StreamGen` | — | 200 | `guarded-reached` | `none` |
| `Conformance.keyedChildrenLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen` | `witness-surface-scope` | 600 | `guarded-reached` | `built` |
| `Conformance.memoLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 800 | `unconditional` | `none` |
| `Conformance.memoSoundnessLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 100 | `unconditional` | `none` |
| `Conformance.mergeConflictLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 900 | `guarded-reached` | `none` |
| `Conformance.noAttestationVacuityLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.normalizeLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `unconditional` | `none` |
| `Conformance.nowLaws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 750 | `unconditional` | `built` |
| `Conformance.opAlgebra` | `Fuaran.Core.Conformance` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `tree-algebra-well-formed-states` | 1000 | `guarded-reached` | `drawn` |
| `Conformance.packLoadingLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `unconditional` | `built` |
| `Conformance.paramLaws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `unconditional` | `built` |
| `Conformance.projectionLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ProjectionWitness` | — | 800 | `unconditional` | `none` |
| `Conformance.propagationEvalLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `guarded-reached` | `built` |
| `Conformance.propagationEvaluatorLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `EvaluatorWitness` | `propagation-change-set-and-prior` | 600 | `guarded-reached` | `drawn` |
| `Conformance.propagationEvaluatorLawsWith` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `EvaluatorWitness` | `propagation-prior-blind` | 720 | `guarded-reached` | `drawn` |
| `Conformance.queryLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1400 | `unconditional` | `built` |
| `Conformance.queryLawsWith` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `QuerySeamWitness` | — | 900 | `guarded-reached` | `drawn` |
| `Conformance.reconcileLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1200 | `guarded-reached` | `drawn` |
| `Conformance.reducer` | `Fuaran.Core.Conformance` | base run | — | `StreamGen` | — | 400 | `guarded-reached` | `drawn` |
| `Conformance.registryLaws` | `Fuaran.Core.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `unconditional` | `built` |
| `Conformance.schemaWalkLaws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 1200 | `guarded-reached` | `none` |
| `Conformance.slotParamLaws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 720 | `unconditional` | `built` |
| `Conformance.snapshotLaws` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 200 | `unconditional` | `none` |
| `Conformance.snapshotLawsWith` | `Fuaran.Core.Conformance` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 200 | `unconditional` | `none` |
| `Conformance.streamLaws` | `Fuaran.Core.Conformance` | base run | — | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.transformLaws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 32 | `guarded-reached` | `drawn` |
| `Conformance.verifyHonestyLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 400 | `unconditional` | `drawn-miss-is-red` |
| `Conformance.witnessLaws` | `Fuaran.Core.Conformance` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `lawful-abstract-witness` | 400 | `unconditional` | `none` |
| `FoldConfluence.laneFoldLaws` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — | 360 | `guarded-reached` | `drawn` |
| `FoldConfluence.laneFoldLawsWith` | `Fuaran.Core.Conformance` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — | 360 | `guarded-reached` | `drawn` |
| `IncrementalDelta.laws` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 420 | `guarded-reached` | `drawn` |
| `IncrementalDelta.lawsWith` | `Fuaran.Core.DataFrame.Conformance` | opt-in | `seam-not-every-domain-has` | — | — | 700 | `guarded-reached` | `drawn` |
