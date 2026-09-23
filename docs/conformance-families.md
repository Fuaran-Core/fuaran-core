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

63 families, across `Conformance`, `FoldConfluence`, `IncrementalDelta`.

| Family | Run by | Why opt-in | Witness | Discharges |
|---|---|---|---|---|
| `Conformance.aggregateParityLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.aiSurfaceLaws` | opt-in | `needs-witness-capability` | `AiSurfaceWitness` | — |
| `Conformance.arbitrationLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.attestationLaws` | opt-in | `needs-witness-capability` | `StreamWitness`, `StreamGen`, `IAttestationSink` | — |
| `Conformance.attributedLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.canonicalFloatLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.capabilityLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.capabilityPipelineIncrementalLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.capabilityPipelineLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.captureReplayLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.casLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.chainBreakReasonLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.codecInjectivityLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.columnarOpLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.columnarOpLawsWith` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.columnarValidatorLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.compositionLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — |
| `Conformance.compositionPilot` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — |
| `Conformance.concurrencyLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | `lanes-apply` |
| `Conformance.concurrencyLawsWith` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.constructThenEncodeLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.containerLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.dagBreakReasonLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.dagLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.deferredLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.diffContainedLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.diffLaws` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.dirtyPropagationLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.encoderInjectivityLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — |
| `Conformance.footprintLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | `independence-diamond` |
| `Conformance.functionVerifyLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — |
| `Conformance.hashFnAdversarialLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.hashFnLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.idempotencyLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.incrementalLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.keyedChildrenLaws` | opt-in | `needs-witness-capability` | `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen` | `witness-surface-scope` |
| `Conformance.memoLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — |
| `Conformance.memoSoundnessLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — |
| `Conformance.mergeConflictLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.noAttestationVacuityLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.normalizeLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.nowLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.opAlgebra` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `tree-algebra-well-formed-states` |
| `Conformance.packLoadingLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.paramLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.projectionLaws` | opt-in | `needs-witness-capability` | `ProjectionWitness` | — |
| `Conformance.propagationEvalLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.queryLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.reconcileLaws` | opt-in | `stronger-promise` | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.reducer` | base run | — | `StreamGen` | — |
| `Conformance.registryLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.schemaWalkLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.slotParamLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.snapshotLaws` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.snapshotLawsWith` | opt-in | `stronger-promise` | `StreamWitness`, `StreamGen` | — |
| `Conformance.streamLaws` | base run | — | `StreamWitness`, `StreamGen` | — |
| `Conformance.transformLaws` | opt-in | `seam-not-every-domain-has` | — | — |
| `Conformance.verifyHonestyLaws` | opt-in | `needs-witness-capability` | `ArtifactWitness` | — |
| `Conformance.witnessLaws` | base run | — | `NodeWitness`, `IdWitness`, `OpGen` | `lawful-abstract-witness` |
| `FoldConfluence.laneFoldLaws` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — |
| `FoldConfluence.laneFoldLawsWith` | opt-in | `needs-witness-capability` | `StreamWitness`, `LaneGen` | — |
| `IncrementalDelta.laws` | opt-in | `seam-not-every-domain-has` | — | — |
| `IncrementalDelta.lawsWith` | opt-in | `seam-not-every-domain-has` | — | — |
