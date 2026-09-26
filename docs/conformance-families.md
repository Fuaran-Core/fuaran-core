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
the shipped assembly BY RETURN TYPE, so it cannot miss a family by how the family is
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

65 families, across `Conformance`, `FoldConfluence`, `IncrementalDelta`.

| Family | Run by | Why opt-in | Witness | Discharges | Cases | Adequacy | Refusal |
|---|---|---|---|---|---|---|---|
| `Conformance.aggregateParityLaws` | opt-in | `seam-not-every-domain-has` | — | — | 400 | `unconditional` | `none` |
| `Conformance.aiSurfaceLaws` | opt-in | `needs-witness-capability` | `AiSurfaceWitness` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.arbitrationLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1800 | `guarded-reached` | `drawn` |
| `Conformance.attestationLaws` | opt-in | `needs-witness-capability` | `StreamWitness`, `StreamGen`, `IAttestationSink` | — | 1000 | `guarded-reached` | `built` |
| `Conformance.attributedLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.canonicalFloatLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1500 | `unconditional` | `none` |
| `Conformance.capabilityLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1600 | `unconditional` | `built` |
| `Conformance.capabilityPipelineIncrementalLaws` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `guarded-reached` | `none` |
| `Conformance.capabilityPipelineLaws` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `built` |
| `Conformance.captureReplayLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `unconditional` | `built` |
| `Conformance.casLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `guarded-reached` | `drawn` |
| `Conformance.chainBreakReasonLaws` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `built` |
| `Conformance.codecInjectivityLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `none` |
| `Conformance.columnarOpLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1200 | `guarded-reached` | `drawn` |
| `Conformance.columnarOpLawsWith` | opt-in | `seam-not-every-domain-has` | — | — | 1200 | `guarded-reached` | `drawn` |
| `Conformance.columnarValidatorLaws` | opt-in | `seam-not-every-domain-has` | — | — | 400 | `guarded-reached` | `drawn` |
| `Conformance.compositionLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 800 | `unconditional` | `none` |
| `Conformance.compositionPilot` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 1200 | `unconditional` | `none` |
| `Conformance.concurrencyLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | `lanes-apply` | 900 | `guarded-reached` | `none` |
| `Conformance.concurrencyLawsWith` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 900 | `guarded-reached` | `none` |
| `Conformance.constructThenEncodeLaws` | opt-in | `seam-not-every-domain-has` | — | — | 12 | `unconditional` | `none` |
| `Conformance.containerLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `guarded-reached` | `built` |
| `Conformance.dagBreakReasonLaws` | opt-in | `seam-not-every-domain-has` | — | — | 480 | `unconditional` | `built` |
| `Conformance.dagLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 400 | `unconditional` | `built` |
| `Conformance.deferredLaws` | opt-in | `seam-not-every-domain-has` | — | — | 600 | `unconditional` | `built` |
| `Conformance.diffContainedLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.diffLaws` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `unconditional` | `none` |
| `Conformance.dirtyPropagationLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `guarded-reached` | `none` |
| `Conformance.encoderInjectivityLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 200 | `unconditional` | `none` |
| `Conformance.footprintLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | `independence-diamond` | 900 | `guarded-reached` | `none` |
| `Conformance.functionVerifyLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 600 | `unconditional` | `drawn-miss-is-red` |
| `Conformance.hashFnAdversarialLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1000000 | `unconditional` | `none` |
| `Conformance.hashFnLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.idempotencyLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 800 | `guarded-reached` | `drawn` |
| `Conformance.incrementalLaws` | opt-in | `seam-not-every-domain-has` | — | — | 400 | `unconditional` | `none` |
| `Conformance.keyedChildrenLaws` | opt-in | `needs-witness-capability` | `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen` | `witness-surface-scope` | 600 | `guarded-reached` | `built` |
| `Conformance.memoLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 800 | `unconditional` | `none` |
| `Conformance.memoSoundnessLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 100 | `unconditional` | `none` |
| `Conformance.mergeConflictLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 900 | `guarded-reached` | `none` |
| `Conformance.noAttestationVacuityLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.normalizeLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 | `unconditional` | `none` |
| `Conformance.nowLaws` | opt-in | `seam-not-every-domain-has` | — | — | 750 | `unconditional` | `built` |
| `Conformance.opAlgebra` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `tree-algebra-well-formed-states` | 1000 | `guarded-reached` | `drawn` |
| `Conformance.packLoadingLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `unconditional` | `built` |
| `Conformance.paramLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `unconditional` | `built` |
| `Conformance.projectionLaws` | opt-in | `needs-witness-capability` | `ProjectionWitness` | — | 800 | `unconditional` | `none` |
| `Conformance.propagationEvalLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `guarded-reached` | `built` |
| `Conformance.propagationEvaluatorLaws` | opt-in | `needs-witness-capability` | `EvaluatorWitness` | `propagation-change-set-and-prior` | 600 | `guarded-reached` | `drawn` |
| `Conformance.propagationEvaluatorLawsWith` | opt-in | `needs-witness-capability` | `EvaluatorWitness` | `propagation-prior-blind` | 720 | `guarded-reached` | `drawn` |
| `Conformance.queryLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1400 | `unconditional` | `built` |
| `Conformance.reconcileLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1200 | `guarded-reached` | `drawn` |
| `Conformance.reducer` | base run | — | `StreamGen` | — | 400 | `guarded-reached` | `drawn` |
| `Conformance.registryLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 | `unconditional` | `built` |
| `Conformance.schemaWalkLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1200 | `guarded-reached` | `none` |
| `Conformance.slotParamLaws` | opt-in | `seam-not-every-domain-has` | — | — | 720 | `unconditional` | `built` |
| `Conformance.snapshotLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 200 | `unconditional` | `none` |
| `Conformance.snapshotLawsWith` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 200 | `unconditional` | `none` |
| `Conformance.streamLaws` | base run | — | `StreamWitness`, `StreamGen` | — | 600 | `unconditional` | `built` |
| `Conformance.transformLaws` | opt-in | `seam-not-every-domain-has` | — | — | 32 | `guarded-reached` | `drawn` |
| `Conformance.verifyHonestyLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 400 | `unconditional` | `drawn-miss-is-red` |
| `Conformance.witnessLaws` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `lawful-abstract-witness` | 400 | `unconditional` | `none` |
| `FoldConfluence.laneFoldLaws` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — | 360 | `guarded-reached` | `drawn` |
| `FoldConfluence.laneFoldLawsWith` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — | 360 | `guarded-reached` | `drawn` |
| `IncrementalDelta.laws` | opt-in | `seam-not-every-domain-has` | — | — | 420 | `guarded-reached` | `drawn` |
| `IncrementalDelta.lawsWith` | opt-in | `seam-not-every-domain-has` | — | — | 700 | `guarded-reached` | `drawn` |
