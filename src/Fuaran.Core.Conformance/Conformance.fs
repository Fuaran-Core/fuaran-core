namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Conformance (Phase 243) — a property-based law kit a domain runs
//  against its own witness to certify it conforms to the Fuaran.Core op algebra
//  and op-stream. It generalises the `Core.Wire.Corpus` "methodology-is-the-asset"
//  posture from the wire codec to the op algebra: supply a generator, get a
//  verdict — instead of re-authoring a conformance suite per domain.
//
//  FSharp.Core only (a deterministic uint32 xorshift, no FsCheck), Fable-clean.
//
//  ---- The facade (Phase 297) ------------------------------------------------
//  This module is the kit's PUBLIC surface and is compiled LAST. The families
//  themselves live in the topic modules ahead of it — the tree families
//  (`AlgebraTreeLaws`, `PlacementTreeLaws`, `ValidityTreeLaws`), the stream
//  families (`ChainStreamLaws`, `DagStreamLaws`, `CaptureStreamLaws`),
//  `IntegrityLaws`, `ConcurrencyLaws`, the seam families (`CapabilitySeamLaws`,
//  `QuerySeamLaws`, `RegistrySeamLaws`, `ColumnarSeamLaws`, `PipelineSeamLaws`,
//  `PolicySeamLaws`; `TreeLaws`, `StreamLaws` and `SeamLaws` until Phase 388
//  split them along their banners), `FunctionLaws`,
//  `PropagationLaws`, `SurfaceLaws` — every one of them internal, written over
//  the `LawKit` runner, and reachable by a domain only through the forwards
//  here. A forward is one line with the family's full signature, so this file
//  IS the contract a reader can read top to bottom, and the roster ids
//  (`Conformance.<entry>`), the public-surface baseline and the suite's
//  return-type reflection are unchanged by the split. `certify` and
//  `certifyStream` keep their bodies here because the suite reads which
//  families an aggregate runs off this file's source.
//
//  ---- Out of conformance scope by design ----------------------------------
//  Certification proves **faithful carriage + integrity** — that a host's op
//  encodings read back from the chain's JSONL and re-write byte-identically
//  (`streamLaws`' JSONL cell, Phase 301; no other codec round trip is run, Phase
//  302), that its reducer is total and replays
//  deterministically, and that its chains/DAGs are tamper-evident. It deliberately
//  does NOT grade the following; each is a host- or domain-level concern the kit
//  leaves open on purpose, so "conformant" is a precise claim and not an implied
//  guarantee of quality:
//    - Rejection *quality* — WHETHER a domain's rejection usefully enumerates its
//      valid alternatives is an opt-in domain-supplied predicate (see `reducer`'s
//      `namesAlternatives` param), not a generic verdict the aggregate certifiers
//      compute (they cannot inspect a domain's own `'Rej` vocabulary).
//    - Attestation *policy* — WHEN to sign, key rotation, HSM/KMS choice. The kit
//      certifies the attestation *seam* (`attestationLaws`); the policy is host-side.
//    - Attribution *content* — WHETHER an `Actor` / `Session` id is truthful. The
//      chain proves attribution was not *tampered*; it never proves it was *honest*.
//    - Hash-strength *selection* — the collision-resistance of the supplied `HashFn`.
//      `hashFnLaws` certify parity + tamper-detection for the SUPPLIED `HashFn`
//      and go red on one that cannot detect an op tamper (Phase 302);
//      `hashFnAdversarialLaws` pin the posture, but the strong-crypto *choice* is
//      the host's (Core ships no cryptographic hash — GP3).
// ============================================================================

/// The law runners. A domain certifies its witness by supplying a generator; the kit
/// owns the laws. `'Node` / `'State` need equality (conformance compares trees / states).
module Conformance =

    // ---- the six report types, at namespace level since Phase 297 ----------------------------
    //
    //  They were nested here until the split; the families that build them now compile ahead of
    //  this module, so the records live at namespace level (`Fuaran.Core.CompositionSample`, …)
    //  and these abbreviations keep every `Conformance.<Type>` annotation compiling. A union CASE
    //  is not carried by an abbreviation: `Conformance.Exhaustive` reads `Exhaustive` now, which is
    //  the one source change the split asks of a consumer (STABILITY.md, 0.33.0).

    type CompositionSample<'A, 'B> = Fuaran.Core.CompositionSample<'A, 'B>
    type VerifyDefect<'Id> = Fuaran.Core.VerifyDefect<'Id>
    type VerifyCounterexample<'Node, 'Id> = Fuaran.Core.VerifyCounterexample<'Node, 'Id>
    type VerifyCoverage = Fuaran.Core.VerifyCoverage
    type FunctionVerifyReport<'Node, 'Id> = Fuaran.Core.FunctionVerifyReport<'Node, 'Id>
    type MemoSample<'Node> = Fuaran.Core.MemoSample<'Node>

    // ---- the families, forwarded ---------------------------------------------------------------

    /// Forward — see `AlgebraTreeLaws.witnessLaws`.
    let witnessLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.witnessLaws nodew idw gen seed iterations

    /// Forward — see `AlgebraTreeLaws.opAlgebra`.
    let opAlgebra
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.opAlgebra nodew idw gen seed iterations

    /// Forward — see `AlgebraTreeLaws.diffLaws`.
    let diffLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.diffLaws nodew idw gen seed iterations

    /// Forward — see `AlgebraTreeLaws.diffContainedLaws`.
    let diffContainedLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.diffContainedLaws nodew idw gen seed iterations

    /// Forward — see `ValidityTreeLaws.digestLaws` (Phase 314): `Tree.digests` and `Tree.Digests.diff` held
    /// to the per-node digests, the four-way partition and `Diff.changes`, over a domain's witness
    /// and content encoder.
    let digestLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (encode: 'Node -> string)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ValidityTreeLaws.digestLaws nodew idw encode gen seed iterations

    /// Forward — see `ValidityTreeLaws.changeLaws` (Phase 314): `Diff.changes` held to the script
    /// `Diff.toOpsWith` emits, kind by kind.
    let changeLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (encode: 'Node -> string)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ValidityTreeLaws.changeLaws nodew idw encode gen seed iterations

    /// Forward — see `ValidityTreeLaws.introducedLaws` (Phase 314): `Validator.introduced`, `verdict`, `gate`
    /// and `encodeVerdict` held to the defect-set difference and the three policies, over a domain's
    /// registry.
    let introducedLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ValidityTreeLaws.introducedLaws nodew idw reg gen seed iterations

    /// Forward — see `AlgebraTreeLaws.normalizeLaws`.
    let normalizeLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.normalizeLaws nodew idw gen seed iterations

    /// Forward — see `AlgebraTreeLaws.containerLaws`.
    let containerLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.containerLaws nodew idw gen seed iterations

    /// Forward — see `AlgebraTreeLaws.keyedChildrenLaws`.
    let keyedChildrenLaws
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.keyedChildrenLaws keyw nodew idw gen seed iterations

    /// Forward — see `PlacementTreeLaws.placementLaws` (Phase 312).
    let placementLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PlacementTreeLaws.placementLaws nodew idw gen seed iterations

    /// Forward — see `ValidityTreeLaws.containmentLaws` (Phase 313).
    let containmentLaws
        (allowedChildren: string -> string list option)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ValidityTreeLaws.containmentLaws allowedChildren nodew idw gen seed iterations

    /// Forward — see `ValidityTreeLaws.referenceLaws` (Phase 313).
    let referenceLaws
        (refw: RefWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ValidityTreeLaws.referenceLaws refw nodew idw gen seed iterations

    /// Forward — see `PlacementTreeLaws.loweringLaws` (Phase 312).
    let loweringLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PlacementTreeLaws.loweringLaws nodew idw gen seed iterations

    /// Forward — see `PlacementTreeLaws.freshIdLaws` (Phase 312). `setId` rebuilds a node with a new id and
    /// `mint` is the strategy certified — `FreshIds.derived idw`, `FreshIds.sequential idw prefix`, or
    /// the domain's own.
    let freshIdLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (setId: 'Id -> 'Node -> 'Node)
        (mint: 'Id -> Set<string> -> 'Id)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PlacementTreeLaws.freshIdLaws nodew idw gen setId mint seed iterations

    /// Forward — see `KeyedApplyLaws.keyedApplyLaws` (Phase 286).
    let keyedApplyLaws
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        KeyedApplyLaws.keyedApplyLaws keyw nodew idw gen seed iterations

    /// Forward — see `ChainStreamLaws.streamLaws`.
    let streamLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.streamLaws sw gen hashFn seed iterations

    /// Forward — see `ChainStreamLaws.reducer`.
    let reducer
        (apply: 'Op -> 'State -> Result<'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (namesAlternatives: ('Rej -> bool) option)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.reducer apply gen namesAlternatives seed iterations

    /// Forward — see `ChainStreamLaws.snapshotLawsWith`.
    let snapshotLawsWith
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (cfg: StreamConfig)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.snapshotLawsWith sw gen stateEncode hashFn cfg seed iterations

    /// Forward — see `ChainStreamLaws.snapshotLaws`.
    let snapshotLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.snapshotLaws sw gen stateEncode hashFn seed iterations

    /// Forward — see `ChainStreamLaws.streamConfigLaws` (Phase 349): the `…With` stream operations held
    /// at the caller's `StreamConfig` and `HashFn`, so a variant that ignores either goes red.
    let streamConfigLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (cfg: StreamConfig)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.streamConfigLaws sw gen hashFn cfg seed iterations

    /// Forward — see `DagStreamLaws.dagLaws`.
    let dagLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DagStreamLaws.dagLaws sw gen hashFn seed iterations

    /// Forward — see `DagStreamLaws.reachLaws` (Phase 289).
    let reachLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DagStreamLaws.reachLaws sw gen hashFn seed iterations

    /// Forward — see `DagStreamLaws.checkpointLaws` (Phase 288).
    let checkpointLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DagStreamLaws.checkpointLaws sw gen stateEncode hashFn seed iterations

    /// Forward — see `DagStreamLaws.laneLaws` (Phase 311).
    let laneLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DagStreamLaws.laneLaws sw gen hashFn seed iterations

    /// Forward — see `CaptureStreamLaws.captureReplayLaws`.
    let captureReplayLaws
        (encode: 'v -> string)
        (decode: string -> Result<'v, string>)
        (draw: ConfRng.T -> 'v * ConfRng.T)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        CaptureStreamLaws.captureReplayLaws encode decode draw hashFn seed iterations

    /// Forward — see `ChainStreamLaws.casLaws`.
    let casLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.casLaws sw gen hashFn seed iterations

    /// Forward — see `ChainStreamLaws.idempotencyLaws`.
    let idempotencyLaws
        (keyOf: 'Op -> string)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.idempotencyLaws keyOf sw gen hashFn seed iterations

    /// Forward — see `ChainStreamLaws.chainBreakReasonLaws`.
    let chainBreakReasonLaws (seed: int) (iterations: int) : LawResult list =
        ChainStreamLaws.chainBreakReasonLaws seed iterations

    /// Forward — see `DagStreamLaws.dagBreakReasonLaws`.
    let dagBreakReasonLaws (seed: int) (iterations: int) : LawResult list =
        DagStreamLaws.dagBreakReasonLaws seed iterations

    /// Forward — see `IntegrityLaws.canonicalFloatLaws`.
    let canonicalFloatLaws (seed: int) (iterations: int) : LawResult list =
        IntegrityLaws.canonicalFloatLaws seed iterations

    /// Forward — see `IntegrityLaws.encoderInjectivityLaws`.
    let encoderInjectivityLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (gen: ConfRng.T -> 'Node * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.encoderInjectivityLaws w encode gen seed iterations

    /// Forward — see `IntegrityLaws.codecInjectivityLaws`.
    let codecInjectivityLaws
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.codecInjectivityLaws w gen seed iterations

    /// Forward — see `IntegrityLaws.attestationLaws`.
    let attestationLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (sink: IAttestationSink)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.attestationLaws sw gen sink hashFn seed iterations

    /// Forward — see `IntegrityLaws.noAttestationVacuityLaws`.
    let noAttestationVacuityLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.noAttestationVacuityLaws sw gen hashFn seed iterations

    /// Forward — see `IntegrityLaws.hashFnLaws`.
    let hashFnLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.hashFnLaws sw gen hashFn seed iterations

    /// Forward — see `IntegrityLaws.hashFnAdversarialLaws`.
    let hashFnAdversarialLaws (cryptoHf: HashFn) (budget: int) (seed: int) : LawResult list =
        IntegrityLaws.hashFnAdversarialLaws cryptoHf budget seed

    /// Forward — see `IntegrityLaws.attributedLaws`.
    let attributedLaws
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.attributedLaws w gen hashFn seed iterations

    /// Forward — see `IntegrityLaws.constructThenEncodeLaws`.
    let constructThenEncodeLaws
        (domain: string)
        (codec: Corpus.Codec<'T>)
        (witness: ConstructWitness<'T> option)
        (corpus: Corpus.Case list)
        : LawResult list =
        IntegrityLaws.constructThenEncodeLaws domain codec witness corpus

    /// Forward — see `ConcurrencyLaws.footprintLaws`.
    let footprintLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.footprintLaws nodew idw gen encode seed iterations

    /// Forward — see `ConcurrencyLaws.footprintLawsAt` (Phase 249): `footprintLaws`' soundness law
    /// at the domain's own ops, stream witness and generator, rather than the skeleton op algebra.
    let footprintLawsAt
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (hashState: 'State -> string)
        (gen: StreamGen<'Op, 'State>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.footprintLawsAt sw footprintOf hashState gen seed iterations

    /// Forward — see `ConcurrencyLaws.mergeConflictLaws`.
    let mergeConflictLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.mergeConflictLaws nodew idw gen seed iterations

    /// Forward — see `ConcurrencyLaws.reconcileLawsWith`: the reconcile laws under the domain's own
    /// chain hash (Phase 297), the pinned parameter last before `seed`.
    let reconcileLawsWith
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.reconcileLawsWith nodew idw gen encode hashFn seed iterations

    /// Forward — see `ConcurrencyLaws.reconcileLaws`.
    let reconcileLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.reconcileLaws nodew idw gen encode seed iterations

    /// Forward — see `ConcurrencyLaws.concurrencyLawsWith`.
    let concurrencyLawsWith
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (footprintOf: SkeletonOp<'Node, 'Id> list -> Footprint)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.concurrencyLawsWith nodew idw gen encode footprintOf seed iterations

    /// Forward — see `ConcurrencyLaws.concurrencyLaws`.
    let concurrencyLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.concurrencyLaws nodew idw gen encode seed iterations

    /// Forward — see `ConcurrencyLaws.arbitrationLaws`.
    let arbitrationLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.arbitrationLaws nodew idw gen encode seed iterations

    /// Forward — see `KeyedArbitrationLaws.keyedArbitrationLawsWith` (Phase 247): the arbitration
    /// laws over `Arbitration.arbitrateWith footprintOf canApplyOf`, held to the keyed engine the
    /// accepted scripts land with, with a container-illegal proposal and a keyed-id clash BUILT.
    /// The injected pair sits last before the seed (the kit's `…With` rule).
    let keyedArbitrationLawsWith
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (footprintOf: SkeletonOp<'Node, 'Id> list -> Footprint)
        (canApplyOf: SkeletonOp<'Node, 'Id> list -> 'Node -> Result<unit, int * Rejection<'Id>>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        KeyedArbitrationLaws.keyedArbitrationLawsWith
            "Conformance.keyedArbitrationLawsWith"
            keyw
            nodew
            idw
            gen
            encode
            footprintOf
            canApplyOf
            seed
            iterations

    /// Forward — `keyedArbitrationLawsWith` pinned to the keyed composition (Phase 247):
    /// `Ops.footprintKeyed keyw nodew idw` and `Ops.canApplyAllKeyed keyw canHold nodew idw`, with
    /// `canHold` the generator's `CanHold` (every node, when it is `None`). The shape a domain that
    /// arbitrates over containers or keyed positions runs.
    let keyedArbitrationLaws
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        KeyedArbitrationLaws.keyedArbitrationLawsWith
            "Conformance.keyedArbitrationLawsWith"
            keyw
            nodew
            idw
            gen
            encode
            (Ops.footprintKeyed keyw nodew idw)
            (Ops.canApplyAllKeyed keyw (LawKit.canHoldOf gen) nodew idw)
            seed
            iterations

    /// Forward — see `CapabilitySeamLaws.capabilityLaws`.
    let capabilityLaws (seed: int) (iterations: int) : LawResult list =
        CapabilitySeamLaws.capabilityLaws seed iterations

    /// Forward — see `CapabilitySeamLaws.capabilityLawsAt`: the capability seam laws at a DOMAIN'S seam.
    let capabilityLawsAt (w: CapabilitySeamWitness<'v>) (seed: int) (iterations: int) : LawResult list =
        CapabilitySeamLaws.capabilityLawsAt "Conformance.capabilityLawsAt" w seed iterations

    /// Obsolete — `capabilityLawsAt` (Phase 297's naming rule).
    [<System.Obsolete("Renamed capabilityLawsAt by the Phase 297 naming rule: an At suffix is the domain-witness form, a With suffix a pinned parameter last before the seed. This forward keeps its own roster id and guard label through the 0.33.0 draft and is then removed.")>]
    let capabilityLawsWith (w: CapabilitySeamWitness<'v>) (seed: int) (iterations: int) : LawResult list =
        CapabilitySeamLaws.capabilityLawsAt "Conformance.capabilityLawsWith" w seed iterations

    /// Forward — see `QuerySeamLaws.queryLaws`.
    let queryLaws (seed: int) (iterations: int) : LawResult list = QuerySeamLaws.queryLaws seed iterations

    /// Forward — see `QuerySeamLaws.queryLawsAt`: the query seam laws at a DOMAIN'S seam.
    let queryLawsAt (w: QuerySeamWitness) (seed: int) (iterations: int) : LawResult list =
        QuerySeamLaws.queryLawsAt "Conformance.queryLawsAt" w seed iterations

    /// Obsolete — `queryLawsAt` (Phase 297's naming rule).
    [<System.Obsolete("Renamed queryLawsAt by the Phase 297 naming rule: an At suffix is the domain-witness form, a With suffix a pinned parameter last before the seed. This forward keeps its own roster id and guard label through the 0.33.0 draft and is then removed.")>]
    let queryLawsWith (w: QuerySeamWitness) (seed: int) (iterations: int) : LawResult list =
        QuerySeamLaws.queryLawsAt "Conformance.queryLawsWith" w seed iterations

    /// Forward — see `RegistrySeamLaws.registryLaws`.
    let registryLaws (seed: int) (iterations: int) : LawResult list =
        RegistrySeamLaws.registryLaws seed iterations

    /// Forward — see `PolicySeamLaws.policyLaws` (Phase 318): the policy decision's join and the gate on
    /// the three invocable registries, from the kit's own fixtures.
    let policyLaws (seed: int) (iterations: int) : LawResult list =
        PolicySeamLaws.policyLaws seed iterations

    /// Forward — see `PolicySeamLaws.policyLawsAt` (Phase 318): the no-unapproved-write law and the dry
    /// run's agreement with `Apply`, under the DOMAIN'S own policy, its effects accessor and the
    /// registry its actors act through. `privileged` names the actors a host-writing op may be
    /// allowed for; every other actor drawn from `actors` is held to the law.
    let policyLawsAt
        (gw: GuardedSurfaceWitness<'State, 'Op, 'Rej>)
        (registry: CapabilityRegistry)
        (state0: 'State)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (actors: string list)
        (privileged: string -> bool)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PolicySeamLaws.policyLawsAt
            "Conformance.policyLawsAt"
            gw
            registry
            state0
            genOp
            actors
            privileged
            seed
            iterations

    /// Forward — see `PolicySeamLaws.writeGateLaws` (Phase 318): the write gate's targets cover what an
    /// op writes, the gate runs before the reducer, a lock covers its subtree, and a wider lock never
    /// admits more — at the domain's tree witnesses and op generator.
    let writeGateLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PolicySeamLaws.writeGateLaws "Conformance.writeGateLaws" nodew idw gen seed iterations

    /// Forward — see `PolicySeamLaws.keyedCaptureLaws` (Phase 318): the keyed, two-phase capture journal
    /// verifies, replays by key in any order across keys, refuses a miss, detects tampering, and
    /// replays a network capability exactly through the seam.
    let keyedCaptureLaws (seed: int) (iterations: int) : LawResult list =
        PolicySeamLaws.keyedCaptureLaws seed iterations

    /// Forward — see `RegistrySeamLaws.packLoadingLaws`.
    let packLoadingLaws (seed: int) (iterations: int) : LawResult list =
        RegistrySeamLaws.packLoadingLaws seed iterations

    /// Forward — see `ColumnarSeamLaws.aggregateNullSkipLaws`.
    let aggregateNullSkipLaws (seed: int) (iterations: int) : LawResult list =
        ColumnarSeamLaws.aggregateNullSkipLaws seed iterations

    /// Forward — see `ColumnarSeamLaws.columnarValidatorLaws`.
    let columnarValidatorLaws (seed: int) (iterations: int) : LawResult list =
        ColumnarSeamLaws.columnarValidatorLaws seed iterations

    /// Forward — see `PipelineSeamLaws.deferredLaws`.
    let deferredLaws (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.deferredLaws seed iterations

    /// Forward — see `PipelineSeamLaws.capabilityPipelineLaws`.
    let capabilityPipelineLaws (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.capabilityPipelineLaws seed iterations

    /// Forward — see `PipelineSeamLaws.capabilityPipelineLawsAt`: the pipeline laws at a DOMAIN'S registry.
    let capabilityPipelineLawsAt (w: CapabilityPipelineWitness) (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.capabilityPipelineLawsAt "Conformance.capabilityPipelineLawsAt" w seed iterations

    /// Obsolete — `capabilityPipelineLawsAt` (Phase 297's naming rule).
    [<System.Obsolete("Renamed capabilityPipelineLawsAt by the Phase 297 naming rule: an At suffix is the domain-witness form, a With suffix a pinned parameter last before the seed. This forward keeps its own roster id and guard label through the 0.33.0 draft and is then removed.")>]
    let capabilityPipelineLawsWith (w: CapabilityPipelineWitness) (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.capabilityPipelineLawsAt "Conformance.capabilityPipelineLawsWith" w seed iterations

    /// Forward — see `PipelineSeamLaws.capabilityPipelineIncrementalLaws`.
    let capabilityPipelineIncrementalLaws (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.capabilityPipelineIncrementalLaws seed iterations

    /// Forward — see `FunctionLaws.compositionLaws`.
    let compositionLaws
        (wa: ArtifactWitness<'A, 'IdA>)
        (wb: ArtifactWitness<'B, 'IdB>)
        (embed: 'B -> 'A)
        (draw: ConfRng.T -> CompositionSample<'A, 'B> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.compositionLaws wa wb embed draw seed iterations

    /// Forward — see `FunctionLaws.verifyFunction`.
    let verifyFunction
        (w: ArtifactWitness<'Node, 'Id>)
        (fn: 'Node)
        (reg: Validator.Registry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : FunctionVerifyReport<'Node, 'Id> =
        FunctionLaws.verifyFunction w fn reg genParams seed iterations

    /// Forward — see `FunctionLaws.verifyFunctionSymbolic`.
    let verifyFunctionSymbolic
        (w: ArtifactWitness<'Node, 'Id>)
        (fn: 'Node)
        (reg: Validator.Registry<'Node, 'Id>)
        (fixedArgs: Map<string, Arg<'Node>>)
        (maxCases: int)
        (seed: int)
        : FunctionVerifyReport<'Node, 'Id> =
        FunctionLaws.verifyFunctionSymbolic w fn reg fixedArgs maxCases seed

    /// Forward — see `FunctionLaws.renderCounterexample`.
    let renderCounterexample (w: ArtifactWitness<'Node, 'Id>) (cx: VerifyCounterexample<'Node, 'Id>) : string =
        FunctionLaws.renderCounterexample w cx

    /// Forward — see `FunctionLaws.functionVerifyLaws`.
    let functionVerifyLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (sound: 'Node)
        (broken: 'Node)
        (reg: Validator.Registry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.functionVerifyLaws w sound broken reg genParams seed iterations

    /// Forward — see `FunctionLaws.memoLaws`.
    let memoLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (draw: ConfRng.T -> MemoSample<'Node> * ConfRng.T)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.memoLaws w encode draw hashFn seed iterations

    /// Forward — see `FunctionLaws.compositionPilot`.
    let compositionPilot
        (wa: ArtifactWitness<'A, 'IdA>)
        (wb: ArtifactWitness<'B, 'IdB>)
        (embed: 'B -> 'A)
        (encodeA: 'A -> string)
        (encodeB: 'B -> string)
        (draw: ConfRng.T -> CompositionSample<'A, 'B> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.compositionPilot wa wb embed encodeA encodeB draw seed iterations

    /// Forward — see `FunctionLaws.verifyHonestyLaws`.
    let verifyHonestyLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (mkSound: DeterminismSource -> 'Node)
        (mkBroken: DeterminismSource -> 'Node)
        (reg: Validator.Registry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.verifyHonestyLaws w mkSound mkBroken reg genParams seed iterations

    /// Forward — see `FunctionLaws.memoSoundnessLaws`.
    let memoSoundnessLaws
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (underDeclaredFn: 'Node)
        (underDeclaredArgs: Map<string, Arg<'Node>>)
        (seed: int)
        (_iterations: int)
        : LawResult list =
        FunctionLaws.memoSoundnessLaws w encode underDeclaredFn underDeclaredArgs seed _iterations

    /// Forward — see `PropagationLaws.dirtyPropagationLaws`.
    let dirtyPropagationLaws (seed: int) (iterations: int) : LawResult list =
        PropagationLaws.dirtyPropagationLaws seed iterations

    /// Forward — see `PropagationLaws.propagationEvalLaws`.
    let propagationEvalLaws (seed: int) (iterations: int) : LawResult list =
        PropagationLaws.propagationEvalLaws seed iterations

    /// Forward — see `PropagationLaws.propagationEvaluatorLaws`.
    let propagationEvaluatorLaws (evw: EvaluatorWitness<'Model, 'V>) (seed: int) (iterations: int) : LawResult list =
        PropagationLaws.propagationEvaluatorLaws evw seed iterations

    /// Forward — see `PropagationLaws.propagationEvaluatorLawsWith`.
    let propagationEvaluatorLawsWith
        (evw: EvaluatorWitness<'Model, 'V>)
        (evalNodeWith: 'Model -> (string -> 'V option) -> 'V option -> string -> Result<'V, string>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PropagationLaws.propagationEvaluatorLawsWith evw evalNodeWith seed iterations

    /// Forward — see `SurfaceLaws.projectionLaws`.
    let projectionLaws
        (pw: ProjectionWitness<'Node, 'Id, 'Op>)
        (applyOps: 'Op list -> Result<'Node, string>)
        (wireEncode: 'Node -> string)
        (gen: ConfRng.T -> 'Node * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        SurfaceLaws.projectionLaws pw applyOps wireEncode gen seed iterations

    /// Forward — see `ObserverLaws.observerLaws` (Phase 298): in-memory equals live, a cyclic parent
    /// declaration terminates, and re-entrant subscribers are isolated, over a domain's
    /// `ObserverWitness` and input generator.
    let observerLaws<'Input, 'Flag when 'Input: equality and 'Flag: equality>
        (w: ObserverWitness<'Input, 'Flag>)
        (genInput: ConfRng.T -> 'Input * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ObserverLaws.observerLaws w genInput seed iterations

    /// Forward — see `SanitizeLaws.sanitizeLaws` (Phase 349): the sanitisation floor each of the six
    /// `Fuaran.Core.Idl.Sanitize` functions claims, over generated adversarial strings and the pinned
    /// vectors, at a `SanitizeWitness` — `SanitizeWitness.core` for Core's own floor.
    let sanitizeLaws (w: SanitizeWitness) (seed: int) (iterations: int) : LawResult list =
        SanitizeLaws.sanitizeLaws w seed iterations

    /// Forward — see `SurfaceLaws.aiSurfaceLawsAt`: the AI-surface laws under the DOMAIN'S own
    /// policy. `aiSurfaceLawsUnderKitPolicy` is the kit-fixture form beside it.
    let aiSurfaceLawsAt
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        SurfaceLaws.aiSurfaceLawsAt "Conformance.aiSurfaceLawsAt" w genOp state0 seed iterations

    /// Obsolete — `aiSurfaceLawsAt` (Phase 297's naming rule). The bare name carried the domain's
    /// policy, the INVERSE of every other bare name in the kit (a bare name is the kit-fixture or
    /// pinned-default form); it is retired rather than reassigned, because a name that changes
    /// meaning under a caller is worse than one that disappears.
    [<System.Obsolete("Renamed aiSurfaceLawsAt by the Phase 297 naming rule: an At suffix is the domain-witness form, a With suffix a pinned parameter last before the seed. This forward keeps its own roster id and guard label through the 0.33.0 draft and is then removed.")>]
    let aiSurfaceLaws
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        SurfaceLaws.aiSurfaceLawsAt "Conformance.aiSurfaceLaws" w genOp state0 seed iterations

    /// Forward — see `SurfaceLaws.aiSurfaceLawsUnderKitPolicy`.
    let aiSurfaceLawsUnderKitPolicy
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        SurfaceLaws.aiSurfaceLawsUnderKitPolicy w genOp state0 seed iterations

    /// Forward — see `SurfaceLaws.frozenWitnessFields`.
    let frozenWitnessFields: (string * string list) list =
        SurfaceLaws.frozenWitnessFields

    /// Forward — see `SurfaceLaws.unfrozenWitnesses`.
    let unfrozenWitnesses: (string * string) list = SurfaceLaws.unfrozenWitnesses

    /// Forward — see `SurfaceLaws.declaredWitnessFields` (Phase 387).
    let declaredWitnessFields: (string * string list) list =
        SurfaceLaws.declaredWitnessFields

    /// Forward — see `SurfaceLaws.witnessDeclaredFieldsLaw` (Phase 387).
    let witnessDeclaredFieldsLaw (record: string) (pinned: string list) (declared: string list) : LawResult =
        SurfaceLaws.witnessDeclaredFieldsLaw record pinned declared

#if !FABLE_COMPILER
    /// Forward — see `SurfaceLaws.witnessFieldsLaw`. .NET-only since Phase 387: it reads by reflection.
    let witnessFieldsLaw (record: string) (pinned: string list) (t: System.Type) : LawResult =
        SurfaceLaws.witnessFieldsLaw record pinned t

    /// Forward — see `SurfaceLaws.witnessCoverageLaw`. .NET-only since Phase 387: it reads by reflection.
    let witnessCoverageLaw (records: System.Type list) : LawResult = SurfaceLaws.witnessCoverageLaw records
#endif

    /// Forward — see `SurfaceLaws.witnessSurfaceLaws`.
    let witnessSurfaceLaws () : LawResult list = SurfaceLaws.witnessSurfaceLaws ()

    // ---- the aggregates ------------------------------------------------------------------------

    /// Certify a witness end-to-end: run the **witness laws first** (Phase 253), then — only
    /// if the witness is well-formed — the op-algebra laws + the diff laws (Phase 03) + the
    /// op-stream laws, and return a structured pass / counterexample report. A witness defect
    /// short-circuits the downstream laws (running them over a broken witness produces noise,
    /// not signal). Domains with only a tree (no stream) call `witnessLaws` + `opAlgebra`
    /// (+ `diffLaws`) directly.
    let certify
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (opGen: OpGen<'Node, 'Id>)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (streamGen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : ConformanceReport =
        let witness = witnessLaws nodew idw opGen seed iterations

        // Phase 302 — the stream side's adequacy. `streamLaws` guards the accepted side (Phase
        // 245); the refused side was guarded only through `certifyStream`'s `reducer`, so a
        // refusal-free `StreamGen` certified green here on the accept path alone. `reducer`'s two
        // guards are folded in — its subject laws are `certifyStream`'s, not this aggregate's.
        let reducerGuards () =
            let p = SampleAdequacy.guardOpening

            reducer sw.Apply streamGen None (seed + 4) iterations
            |> List.filter (fun r -> r.Law.Length >= p.Length && r.Law.Substring(0, p.Length) = p)

        let rest =
            if witness |> List.forall (fun r -> r.Passed) then
                opAlgebra nodew idw opGen (seed + 1) iterations
                @ diffLaws nodew idw opGen (seed + 3) iterations
                @ streamLaws sw streamGen hashFn (seed + 2) iterations
                @ reducerGuards ()
            else
                [] // witness defect — the downstream laws would be noise

        let results = witness @ rest

        { Results = results
          AllPassed = results |> List.forall (fun r -> r.Passed) }

    /// Certify a **reducer-only / heterogeneous-tree** domain — one with no single uniform node
    /// type for a `NodeWitness` (a layered `Model → Sheet → Region` tree, an op-stream with no
    /// addressable skeleton at all, …). This is the adoption surface for every domain whose op
    /// layer fits Core but whose tree does **not** (adoption finding F7): it runs the op-stream
    /// laws + the production-reducer laws over the *same* `StreamGen`, skipping the tree witness
    /// entirely. The reducer under test is the witness's own `Apply`, so a domain certifies its
    /// real `apply` (not a stand-in). `'State` needs equality. Domains with a homogeneous tree
    /// call `certify`; heterogeneous / stream-only domains call this.
    ///
    /// **Envelope-quality is deliberately not folded in.** The `reducer` laws below
    /// are invoked with `namesAlternatives = None`, so the "rejection enumerates its
    /// alternatives" law does not run through this aggregate. This is intentional, not
    /// an oversight: that law needs a *domain-supplied* predicate over the domain's own
    /// `'Rej` vocabulary — a semantic judgement the generic aggregate cannot make (it
    /// has no `'Rej` predicate to pass). It is the same opt-in shape as the snapshot /
    /// DAG laws below (not folded into `certify` / `certifyStream`; a domain calls them
    /// alongside its base run). A domain that wants to certify envelope quality calls
    /// `Conformance.reducer sw.Apply gen (Some myNamesAlternatives) seed iters` directly
    /// alongside `certifyStream`. See the header's "Out of conformance scope by design".
    let certifyStream
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (streamGen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : ConformanceReport =
        // Reducer laws first (they alone guard `apply` against throwing — Phase 254's totality
        // try/catch); only run the op-stream laws if the reducer is total, since `streamLaws`
        // drives the *unguarded* reducer through `append` and a non-total `apply` would crash it
        // rather than report. Same short-circuit philosophy as `certify`: foundational law first.
        let red = reducer sw.Apply streamGen None seed iterations

        // Phase 220 — the short-circuit reads the SUBJECT laws only. It exists because a non-total
        // reducer would crash `streamLaws`; a starved adequacy guard is no such hazard, and
        // hiding the stream laws behind it would turn one red line into four.
        let isGuard (r: LawResult) =
            let p = SampleAdequacy.guardOpening
            r.Law.Length >= p.Length && r.Law.Substring(0, p.Length) = p

        let stream =
            if red |> List.filter (isGuard >> not) |> List.forall (fun r -> r.Passed) then
                streamLaws sw streamGen hashFn (seed + 1) iterations
            else
                []

        let results = red @ stream

        { Results = results
          AllPassed = results |> List.forall (fun r -> r.Passed) }
