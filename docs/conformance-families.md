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

| Family | Run by | Witness | Discharges |
|---|---|---|---|
| `Conformance.aggregateParityLaws` | opt-in | — | — |
| `Conformance.aiSurfaceLaws` | opt-in | `AiSurfaceWitness` | — |
| `Conformance.arbitrationLaws` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.attestationLaws` | opt-in | `StreamWitness`, `StreamGen`, `IAttestationSink` | — |
| `Conformance.attributedLaws` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.canonicalFloatLaws` | opt-in | — | — |
| `Conformance.capabilityLaws` | opt-in | — | — |
| `Conformance.capabilityPipelineIncrementalLaws` | opt-in | — | — |
| `Conformance.capabilityPipelineLaws` | opt-in | — | — |
| `Conformance.captureReplayLaws` | opt-in | — | — |
| `Conformance.casLaws` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.chainBreakReasonLaws` | opt-in | — | — |
| `Conformance.codecInjectivityLaws` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.columnarOpLaws` | opt-in | — | — |
| `Conformance.columnarOpLawsWith` | opt-in | — | — |
| `Conformance.columnarValidatorLaws` | opt-in | — | — |
| `Conformance.compositionLaws` | opt-in | `ArtifactWitness` | — |
| `Conformance.compositionPilot` | opt-in | `ArtifactWitness` | — |
| `Conformance.concurrencyLaws` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | `lanes-apply` |
| `Conformance.concurrencyLawsWith` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.constructThenEncodeLaws` | opt-in | — | — |
| `Conformance.containerLaws` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.dagBreakReasonLaws` | opt-in | — | — |
| `Conformance.dagLaws` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.deferredLaws` | opt-in | — | — |
| `Conformance.diffContainedLaws` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.diffLaws` | base run | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.dirtyPropagationLaws` | opt-in | — | — |
| `Conformance.encoderInjectivityLaws` | opt-in | `ArtifactWitness` | — |
| `Conformance.footprintLaws` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | `independence-diamond` |
| `Conformance.functionVerifyLaws` | opt-in | `ArtifactWitness` | — |
| `Conformance.hashFnAdversarialLaws` | opt-in | — | — |
| `Conformance.hashFnLaws` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.idempotencyLaws` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.incrementalLaws` | opt-in | — | — |
| `Conformance.leaseLaws` | opt-in | — | — |
| `Conformance.memoLaws` | opt-in | `ArtifactWitness` | — |
| `Conformance.memoSoundnessLaws` | opt-in | `ArtifactWitness` | — |
| `Conformance.mergeConflictLaws` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.noAttestationVacuityLaws` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.normalizeLaws` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.nowLaws` | opt-in | — | — |
| `Conformance.opAlgebra` | base run | `NodeWitness`, `IdWitness`, `OpGen` | `tree-algebra-well-formed-states` |
| `Conformance.packLoadingLaws` | opt-in | — | — |
| `Conformance.paramLaws` | opt-in | — | — |
| `Conformance.projectionLaws` | opt-in | `ProjectionWitness` | — |
| `Conformance.propagationEvalLaws` | opt-in | — | — |
| `Conformance.queryLaws` | opt-in | — | — |
| `Conformance.reconcileLaws` | opt-in | `NodeWitness`, `IdWitness`, `OpGen` | — |
| `Conformance.reducer` | base run | `StreamGen` | — |
| `Conformance.registryLaws` | opt-in | — | — |
| `Conformance.schemaWalkLaws` | opt-in | — | — |
| `Conformance.slotParamLaws` | opt-in | — | — |
| `Conformance.snapshotLaws` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.snapshotLawsWith` | opt-in | `StreamWitness`, `StreamGen` | — |
| `Conformance.streamLaws` | base run | `StreamWitness`, `StreamGen` | — |
| `Conformance.transformLaws` | opt-in | — | — |
| `Conformance.verifyHonestyLaws` | opt-in | `ArtifactWitness` | — |
| `Conformance.witnessLaws` | base run | `NodeWitness`, `IdWitness`, `OpGen` | `lawful-abstract-witness` |
| `FoldConfluence.laneFoldLaws` | opt-in | `StreamWitness`, `LaneGen` | — |
| `FoldConfluence.laneFoldLawsWith` | opt-in | `StreamWitness`, `LaneGen` | — |
| `IncrementalDelta.laws` | opt-in | — | — |
| `IncrementalDelta.lawsWith` | opt-in | — | — |
