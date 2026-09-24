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

64 families, across `Conformance`, `FoldConfluence`, `IncrementalDelta`.

| Family | Run by | Why opt-in | Witness | Discharges | Cases |
|---|---|---|---|---|---|
| `Conformance.aggregateParityLaws` | opt-in | `seam-not-every-domain-has` | — | — | 400 |
| `Conformance.aiSurfaceLaws` | opt-in | `needs-witness-capability` | `AiSurfaceWitness` | — | 800 |
| `Conformance.arbitrationLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1800 |
| `Conformance.attestationLaws` | opt-in | `needs-witness-capability` | `StreamWitness`, `StreamGen`, `IAttestationSink` | — | 1000 |
| `Conformance.attributedLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 |
| `Conformance.canonicalFloatLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1500 |
| `Conformance.capabilityLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1400 |
| `Conformance.capabilityPipelineIncrementalLaws` | opt-in | `seam-not-every-domain-has` | — | — | 600 |
| `Conformance.capabilityPipelineLaws` | opt-in | `seam-not-every-domain-has` | — | — | 600 |
| `Conformance.captureReplayLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 |
| `Conformance.casLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 |
| `Conformance.chainBreakReasonLaws` | opt-in | `seam-not-every-domain-has` | — | — | 600 |
| `Conformance.codecInjectivityLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 |
| `Conformance.columnarOpLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1200 |
| `Conformance.columnarOpLawsWith` | opt-in | `seam-not-every-domain-has` | — | — | 1200 |
| `Conformance.columnarValidatorLaws` | opt-in | `seam-not-every-domain-has` | — | — | 400 |
| `Conformance.compositionLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 800 |
| `Conformance.compositionPilot` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 1200 |
| `Conformance.concurrencyLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | `lanes-apply` | 900 |
| `Conformance.concurrencyLawsWith` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 900 |
| `Conformance.constructThenEncodeLaws` | opt-in | `seam-not-every-domain-has` | — | — | 12 |
| `Conformance.containerLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 |
| `Conformance.dagBreakReasonLaws` | opt-in | `seam-not-every-domain-has` | — | — | 480 |
| `Conformance.dagLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 400 |
| `Conformance.deferredLaws` | opt-in | `seam-not-every-domain-has` | — | — | 600 |
| `Conformance.diffContainedLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 |
| `Conformance.diffLaws` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 |
| `Conformance.dirtyPropagationLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 |
| `Conformance.encoderInjectivityLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 200 |
| `Conformance.footprintLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | `independence-diamond` | 900 |
| `Conformance.functionVerifyLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 600 |
| `Conformance.hashFnAdversarialLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1000000 |
| `Conformance.hashFnLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 |
| `Conformance.idempotencyLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 800 |
| `Conformance.incrementalLaws` | opt-in | `seam-not-every-domain-has` | — | — | 400 |
| `Conformance.keyedChildrenLaws` | opt-in | `needs-witness-capability` | `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen` | `witness-surface-scope` | 600 |
| `Conformance.memoLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 800 |
| `Conformance.memoSoundnessLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 100 |
| `Conformance.mergeConflictLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 900 |
| `Conformance.noAttestationVacuityLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 600 |
| `Conformance.normalizeLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 600 |
| `Conformance.nowLaws` | opt-in | `seam-not-every-domain-has` | — | — | 750 |
| `Conformance.opAlgebra` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `tree-algebra-well-formed-states` | 1000 |
| `Conformance.packLoadingLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 |
| `Conformance.paramLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 |
| `Conformance.projectionLaws` | opt-in | `needs-witness-capability` | `ProjectionWitness` | — | 800 |
| `Conformance.propagationEvalLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 |
| `Conformance.propagationEvaluatorLaws` | opt-in | `needs-witness-capability` | `EvaluatorWitness` | `propagation-change-set-and-prior` | 600 |
| `Conformance.queryLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1400 |
| `Conformance.reconcileLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — | 1200 |
| `Conformance.reducer` | base run | — | `StreamGen` | — | 400 |
| `Conformance.registryLaws` | opt-in | `seam-not-every-domain-has` | — | — | 800 |
| `Conformance.schemaWalkLaws` | opt-in | `seam-not-every-domain-has` | — | — | 1200 |
| `Conformance.slotParamLaws` | opt-in | `seam-not-every-domain-has` | — | — | 720 |
| `Conformance.snapshotLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 200 |
| `Conformance.snapshotLawsWith` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — | 200 |
| `Conformance.streamLaws` | base run | — | `StreamWitness`, `StreamGen` | — | 600 |
| `Conformance.transformLaws` | opt-in | `seam-not-every-domain-has` | — | — | 32 |
| `Conformance.verifyHonestyLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — | 400 |
| `Conformance.witnessLaws` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `lawful-abstract-witness` | 400 |
| `FoldConfluence.laneFoldLaws` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — | 360 |
| `FoldConfluence.laneFoldLawsWith` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — | 360 |
| `IncrementalDelta.laws` | opt-in | `seam-not-every-domain-has` | — | — | 420 |
| `IncrementalDelta.lawsWith` | opt-in | `seam-not-every-domain-has` | — | — | 700 |
