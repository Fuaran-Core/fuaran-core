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
//  themselves live in the twenty-two topic modules ahead of it — the tree
//  families (`AlgebraTreeLaws`, `PlacementTreeLaws`, `ValidityTreeLaws`,
//  `KeyedApplyLaws`), the stream families (`ChainStreamLaws`, `DagStreamLaws`,
//  `CaptureStreamLaws`), `IntegrityLaws`, `ConcurrencyLaws`,
//  `KeyedArbitrationLaws`, the seam families (`CapabilitySeamLaws`,
//  `QuerySeamLaws`, `RegistrySeamLaws`, `ColumnarSeamLaws`, `PipelineSeamLaws`,
//  `PolicySeamLaws`), `FunctionLaws`, `PropagationLaws`, `SurfaceLaws`,
//  `ObserverLaws`, `SanitizeLaws` and `CodecLaws` (Phase 388 split the tree,
//  stream and seam files along their banners) — every one of them internal, written over
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

    /// The runner a public entry point hands its family (Phase 507): the family's roster id — spelled
    /// here, beside the entry point that names it, and nowhere in the law modules — and the caller's
    /// seed. A pinned-default spelling hands the id of the family it spells (`Families.LawFamily.Spellings`).
    let private family (entry: string) (seed: int) : LawKit.Runner =
        LawKit.Runner(LawKit.FamilyId("Conformance." + entry), seed)

    /// Run one family body under `runner` and label what it returns with the runner's family (Phase
    /// 515): every forward ends `X.laws … |> within (family "<entry>" seed) iterations`, so the subject
    /// laws a family ran carry its id the same way its guards do — filled by the runner, never spelled
    /// in a law module.
    let private within
        (runner: LawKit.Runner)
        (iterations: int)
        (body: LawKit.Runner -> int -> LawResult list)
        : LawResult list =
        body runner iterations |> LawKit.label runner

    /// Forward — see `AlgebraTreeLaws.witnessLaws`.
    let witnessLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.witnessLaws nodew idw gen
        |> within (family "witnessLaws" seed) iterations

    /// Forward — see `AlgebraTreeLaws.opAlgebra`.
    let opAlgebra
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.opAlgebra nodew idw gen
        |> within (family "opAlgebra" seed) iterations

    /// Forward — see `AlgebraTreeLaws.diffLaws`.
    let diffLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.diffLaws nodew idw gen
        |> within (family "diffLaws" seed) iterations

    /// Forward — see `AlgebraTreeLaws.diffContainedLaws`.
    let diffContainedLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.diffContainedLaws nodew idw gen
        |> within (family "diffContainedLaws" seed) iterations

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
        ValidityTreeLaws.digestLaws nodew idw encode gen
        |> within (family "digestLaws" seed) iterations

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
        ValidityTreeLaws.changeLaws nodew idw encode gen
        |> within (family "changeLaws" seed) iterations

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
        ValidityTreeLaws.introducedLaws nodew idw reg gen
        |> within (family "introducedLaws" seed) iterations

    /// Forward — see `AlgebraTreeLaws.normalizeLaws`.
    let normalizeLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.normalizeLaws nodew idw gen
        |> within (family "normalizeLaws" seed) iterations

    /// Forward — see `AlgebraTreeLaws.containerLaws`.
    let containerLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.containerLaws nodew idw gen
        |> within (family "containerLaws" seed) iterations

    /// Forward — see `AlgebraTreeLaws.keyedChildrenLawsAt`: the keyed-position laws at a DOMAIN'S
    /// `KeyedWitness`.
    let keyedChildrenLawsAt
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        AlgebraTreeLaws.keyedChildrenLawsAt keyw nodew idw gen
        |> within (family "keyedChildrenLawsAt" seed) iterations

    /// Forward — see `PlacementTreeLaws.placementLaws` (Phase 312).
    let placementLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PlacementTreeLaws.placementLaws nodew idw gen
        |> within (family "placementLaws" seed) iterations

    /// Forward — see `ValidityTreeLaws.containmentLaws` (Phase 313).
    let containmentLaws
        (allowedChildren: string -> string list option)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ValidityTreeLaws.containmentLaws allowedChildren nodew idw gen
        |> within (family "containmentLaws" seed) iterations

    /// Forward — see `ValidityTreeLaws.referenceLawsAt` (Phase 313): the reference laws at a DOMAIN'S
    /// `RefWitness`.
    let referenceLawsAt
        (refw: RefWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ValidityTreeLaws.referenceLawsAt refw nodew idw gen
        |> within (family "referenceLawsAt" seed) iterations

    /// Forward — see `PlacementTreeLaws.loweringLaws` (Phase 312).
    let loweringLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PlacementTreeLaws.loweringLaws nodew idw gen
        |> within (family "loweringLaws" seed) iterations

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
        PlacementTreeLaws.freshIdLaws nodew idw gen setId mint
        |> within (family "freshIdLaws" seed) iterations

    /// Forward — see `KeyedApplyLaws.keyedApplyLawsAt` (Phase 286).
    let keyedApplyLawsAt
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        KeyedApplyLaws.keyedApplyLawsAt keyw nodew idw gen
        |> within (family "keyedApplyLawsAt" seed) iterations

    /// Forward — see `ChainStreamLaws.streamLaws`.
    let streamLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.streamLaws sw gen hashFn
        |> within (family "streamLaws" seed) iterations

    /// Forward — see `ChainStreamLaws.reducer`.
    let reducer
        (apply: 'Op -> 'State -> Result<'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (namesAlternatives: ('Rej -> bool) option)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.reducer apply gen namesAlternatives
        |> within (family "reducer" seed) iterations

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
        ChainStreamLaws.snapshotLawsWith sw gen stateEncode hashFn cfg
        |> within (family "snapshotLawsWith" seed) iterations

    /// Forward — see `ChainStreamLaws.snapshotLaws`.
    let snapshotLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.snapshotLaws sw gen stateEncode hashFn
        |> within (family "snapshotLawsWith" seed) iterations

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
        ChainStreamLaws.streamConfigLaws sw gen hashFn cfg
        |> within (family "streamConfigLaws" seed) iterations

    /// Forward — see `DagStreamLaws.dagLaws`.
    let dagLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DagStreamLaws.dagLaws sw gen hashFn |> within (family "dagLaws" seed) iterations

    /// Forward — see `DagStreamLaws.reachLaws` (Phase 289).
    let reachLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DagStreamLaws.reachLaws sw gen hashFn
        |> within (family "reachLaws" seed) iterations

    /// Forward — see `DagStreamLaws.checkpointLaws` (Phase 288).
    let checkpointLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (stateEncode: 'State -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DagStreamLaws.checkpointLaws sw gen stateEncode hashFn
        |> within (family "checkpointLaws" seed) iterations

    /// Forward — see `DagStreamLaws.laneLaws` (Phase 311).
    let laneLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        DagStreamLaws.laneLaws sw gen hashFn
        |> within (family "laneLaws" seed) iterations

    /// Forward — see `CaptureStreamLaws.captureReplayLaws`.
    let captureReplayLaws
        (encode: 'v -> string)
        (decode: string -> Result<'v, string>)
        (draw: ConfRng.T -> 'v * ConfRng.T)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        CaptureStreamLaws.captureReplayLaws encode decode draw hashFn
        |> within (family "captureReplayLaws" seed) iterations

    /// Forward — see `ChainStreamLaws.casLaws`.
    let casLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.casLaws sw gen hashFn
        |> within (family "casLaws" seed) iterations

    /// Forward — see `ChainStreamLaws.idempotencyLaws`.
    let idempotencyLaws
        (keyOf: 'Op -> string)
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ChainStreamLaws.idempotencyLaws keyOf sw gen hashFn
        |> within (family "idempotencyLaws" seed) iterations

    /// Forward — see `ChainStreamLaws.chainBreakReasonLaws`.
    let chainBreakReasonLaws (seed: int) (iterations: int) : LawResult list =
        ChainStreamLaws.chainBreakReasonLaws
        |> within (family "chainBreakReasonLaws" seed) iterations

    /// Forward — see `DagStreamLaws.dagBreakReasonLaws`.
    let dagBreakReasonLaws (seed: int) (iterations: int) : LawResult list =
        DagStreamLaws.dagBreakReasonLaws
        |> within (family "dagBreakReasonLaws" seed) iterations

    /// Forward — see `IntegrityLaws.canonicalFloatLaws`.
    let canonicalFloatLaws (seed: int) (iterations: int) : LawResult list =
        IntegrityLaws.canonicalFloatLaws
        |> within (family "canonicalFloatLaws" seed) iterations

    /// Forward — see `IntegrityLaws.encoderInjectivityLawsAt`.
    let encoderInjectivityLawsAt
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (gen: ConfRng.T -> 'Node * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.encoderInjectivityLawsAt w encode gen
        |> within (family "encoderInjectivityLawsAt" seed) iterations

    /// Forward — see `IntegrityLaws.codecInjectivityLaws`.
    let codecInjectivityLaws
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.codecInjectivityLaws w gen
        |> within (family "codecInjectivityLaws" seed) iterations

    /// Forward — see `IntegrityLaws.attestationLawsAt`.
    let attestationLawsAt
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (sink: IAttestationSink)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.attestationLawsAt sw gen sink hashFn
        |> within (family "attestationLawsAt" seed) iterations

    /// Forward — see `IntegrityLaws.noAttestationVacuityLaws`.
    let noAttestationVacuityLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.noAttestationVacuityLaws sw gen hashFn
        |> within (family "noAttestationVacuityLaws" seed) iterations

    /// Forward — see `IntegrityLaws.hashFnLaws`.
    let hashFnLaws
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.hashFnLaws sw gen hashFn
        |> within (family "hashFnLaws" seed) iterations

    /// Forward — see `IntegrityLaws.hashFnAdversarialLaws`.
    let hashFnAdversarialLaws (cryptoHf: HashFn) (budget: int) (seed: int) : LawResult list =
        let runner = family "hashFnAdversarialLaws" seed

        IntegrityLaws.hashFnAdversarialLaws cryptoHf budget runner
        |> LawKit.label runner

    /// Forward — see `IntegrityLaws.attributedLaws`.
    let attributedLaws
        (w: StreamWitness<'Op, 'State, 'Rej>)
        (gen: StreamGen<'Op, 'State>)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        IntegrityLaws.attributedLaws w gen hashFn
        |> within (family "attributedLaws" seed) iterations

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
        ConcurrencyLaws.footprintLaws nodew idw gen encode
        |> within (family "footprintLaws" seed) iterations

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
        ConcurrencyLaws.footprintLawsAt sw footprintOf hashState gen
        |> within (family "footprintLawsAt" seed) iterations

    /// Forward — see `ConcurrencyLaws.footprintInLawsAt` (Phase 500): the same laws at the base-aware
    /// projection a domain folds with when it holds the tree, plus its refinement of the pure one.
    let footprintInLawsAt
        (sw: StreamWitness<'Op, 'State, 'Rej>)
        (footprintOf: 'Op -> Footprint)
        (footprintIn: 'State -> 'Op -> Footprint)
        (hashState: 'State -> string)
        (gen: StreamGen<'Op, 'State>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.footprintInLawsAt sw footprintOf footprintIn hashState gen
        |> within (family "footprintInLawsAt" seed) iterations

    /// Forward — see `ConcurrencyLaws.mergeConflictLaws`.
    let mergeConflictLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.mergeConflictLaws nodew idw gen
        |> within (family "mergeConflictLaws" seed) iterations

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
        ConcurrencyLaws.reconcileLawsWith nodew idw gen encode hashFn
        |> within (family "reconcileLawsWith" seed) iterations

    /// Forward — see `ConcurrencyLaws.reconcileLaws`.
    let reconcileLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.reconcileLaws nodew idw gen encode
        |> within (family "reconcileLawsWith" seed) iterations

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
        ConcurrencyLaws.concurrencyLawsWith nodew idw gen encode footprintOf
        |> within (family "concurrencyLawsWith" seed) iterations

    /// Forward — see `ConcurrencyLaws.concurrencyLaws`.
    let concurrencyLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.concurrencyLaws nodew idw gen encode
        |> within (family "concurrencyLawsWith" seed) iterations

    /// Forward — see `ConcurrencyLaws.arbitrationLaws`.
    let arbitrationLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ConcurrencyLaws.arbitrationLaws nodew idw gen encode
        |> within (family "arbitrationLaws" seed) iterations

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
        KeyedArbitrationLaws.keyedArbitrationLawsWith keyw nodew idw gen encode footprintOf canApplyOf
        |> within (family "keyedArbitrationLawsWith" seed) iterations

    /// Forward — the keyed-arbitration laws at a DOMAIN'S `KeyedWitness`, with the keyed composition as
    /// the footprint and admission pair (Phase 247; Phase 390's spelling — `keyedArbitrationLawsWith` is
    /// this family with the pair as a further parameter, last before the seed):
    /// `Ops.footprintKeyed keyw nodew idw` and `Ops.canApplyAllKeyed keyw canHold nodew idw`, with
    /// `canHold` the generator's `CanHold` (every node, when it is `None`). The shape a domain that
    /// arbitrates over containers or keyed positions runs. A pinned-default SPELLING of
    /// `keyedArbitrationLawsWith` since Phase 507: one family, one census cell, its guards labelled
    /// with that family's id.
    let keyedArbitrationLawsAt
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        KeyedArbitrationLaws.keyedArbitrationLawsWith
            keyw
            nodew
            idw
            gen
            encode
            (Ops.footprintKeyed keyw nodew idw)
            (Ops.canApplyAllKeyed keyw (LawKit.canHoldOf gen) nodew idw)
        |> within (family "keyedArbitrationLawsWith" seed) iterations

    /// Forward — see `CapabilitySeamLaws.capabilityLaws`.
    let capabilityLaws (seed: int) (iterations: int) : LawResult list =
        CapabilitySeamLaws.capabilityLaws
        |> within (family "capabilityLaws" seed) iterations

    /// Forward — see `CapabilitySeamLaws.capabilityLawsAt`: the capability seam laws at a DOMAIN'S seam.
    let capabilityLawsAt
        (w: CapabilitySeamWitness<'v>)
        (body: (string * string) list -> Capability -> Deferred<'v>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        CapabilitySeamLaws.capabilityLawsAt w body
        |> within (family "capabilityLawsAt" seed) iterations

    /// Forward — see `QuerySeamLaws.queryLaws`.
    let queryLaws (seed: int) (iterations: int) : LawResult list =
        QuerySeamLaws.queryLaws |> within (family "queryLaws" seed) iterations

    /// Forward — see `QuerySeamLaws.queryLawsAt`: the query seam laws at a DOMAIN'S seam.
    let queryLawsAt
        (w: QuerySeamWitness)
        (resolver: (string * Cell) list -> Query -> Deferred<QueryResult>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        QuerySeamLaws.queryLawsAt w resolver
        |> within (family "queryLawsAt" seed) iterations

    /// Forward — see `RegistrySeamLaws.registryLaws`.
    let registryLaws (seed: int) (iterations: int) : LawResult list =
        RegistrySeamLaws.registryLaws |> within (family "registryLaws" seed) iterations

    /// Forward — see `PolicySeamLaws.policyLaws` (Phase 318): the policy decision's join and the gate on
    /// the three invocable registries, from the kit's own fixtures.
    let policyLaws (seed: int) (iterations: int) : LawResult list =
        PolicySeamLaws.policyLaws |> within (family "policyLaws" seed) iterations

    /// Forward — see `PolicySeamLaws.policyLawsAt` (Phase 318): the no-unapproved-write law and the dry
    /// run's agreement with `Apply`, under the DOMAIN'S own policy, its effects accessor and the
    /// registry its actors act through. `privileged` names the actors a host-writing op may be
    /// allowed for; every other actor drawn from `actors` is held to the law. The actors are the
    /// stream's typed `Actor` (Phase 391; bare strings before `0.37.0`), and the surface's policy is
    /// asked about each by its attribution id, `Actor.id`.
    let policyLawsAt
        (gw: GuardedSurfaceWitness<'State, 'Op, 'Rej>)
        (registry: CapabilityRegistry)
        (state0: 'State)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (actors: Actor list)
        (privileged: Actor -> bool)
        (seed: int)
        (iterations: int)
        : LawResult list =
        PolicySeamLaws.policyLawsAt gw registry state0 genOp actors privileged
        |> within (family "policyLawsAt" seed) iterations

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
        PolicySeamLaws.writeGateLaws nodew idw gen
        |> within (family "writeGateLaws" seed) iterations

    /// Forward — see `PolicySeamLaws.keyedCaptureLaws` (Phase 318): the keyed, two-phase capture journal
    /// verifies, replays by key in any order across keys, refuses a miss, detects tampering, and
    /// replays a network capability exactly through the seam.
    let keyedCaptureLaws (seed: int) (iterations: int) : LawResult list =
        PolicySeamLaws.keyedCaptureLaws
        |> within (family "keyedCaptureLaws" seed) iterations

    /// Forward — see `RegistrySeamLaws.packLoadingLaws`.
    let packLoadingLaws (seed: int) (iterations: int) : LawResult list =
        RegistrySeamLaws.packLoadingLaws
        |> within (family "packLoadingLaws" seed) iterations

    /// Forward — see `ColumnarSeamLaws.aggregateNullSkipLaws`.
    let aggregateNullSkipLaws (seed: int) (iterations: int) : LawResult list =
        ColumnarSeamLaws.aggregateNullSkipLaws
        |> within (family "aggregateNullSkipLaws" seed) iterations

    /// Forward — see `ColumnarSeamLaws.columnVectorLaws` (Phase 417).
    let columnVectorLaws (seed: int) (iterations: int) : LawResult list =
        ColumnarSeamLaws.columnVectorLaws
        |> within (family "columnVectorLaws" seed) iterations

    /// Forward — see `ColumnarSeamLaws.columnOwnershipLaws` (Phase 418): the ownership law over the
    /// kit's own sample, with `operation` the consumer's pipeline.
    let columnOwnershipLaws (operation: Column list -> unit) (seed: int) (iterations: int) : LawResult list =
        ColumnarSeamLaws.columnOwnershipLaws operation
        |> within (family "columnOwnershipLawsWith" seed) iterations

    /// Forward — see `ColumnarSeamLaws.columnOwnershipLawsWith` (Phase 418): the ownership law over
    /// columns the consumer draws itself (`draw` is the injected sample, last before the seed).
    let columnOwnershipLawsWith
        (operation: Column list -> unit)
        (draw: ConfRng.T -> Column list * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ColumnarSeamLaws.columnOwnershipLawsWith operation draw
        |> within (family "columnOwnershipLawsWith" seed) iterations

    /// Forward — see `ColumnarSeamLaws.columnarValidatorLaws`.
    let columnarValidatorLaws (seed: int) (iterations: int) : LawResult list =
        ColumnarSeamLaws.columnarValidatorLaws
        |> within (family "columnarValidatorLaws" seed) iterations

    /// Forward — see `PipelineSeamLaws.deferredLaws`.
    let deferredLaws (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.deferredLaws |> within (family "deferredLaws" seed) iterations

    /// Forward — see `PipelineSeamLaws.capabilityPipelineLaws`.
    let capabilityPipelineLaws (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.capabilityPipelineLaws
        |> within (family "capabilityPipelineLaws" seed) iterations

    /// Forward — see `PipelineSeamLaws.capabilityPipelineLawsAt`: the pipeline laws at a DOMAIN'S registry.
    let capabilityPipelineLawsAt (w: CapabilityPipelineWitness) (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.capabilityPipelineLawsAt w
        |> within (family "capabilityPipelineLawsAt" seed) iterations

    /// Forward — see `PipelineSeamLaws.capabilityPipelineIncrementalLaws`.
    let capabilityPipelineIncrementalLaws (seed: int) (iterations: int) : LawResult list =
        PipelineSeamLaws.capabilityPipelineIncrementalLaws
        |> within (family "capabilityPipelineIncrementalLaws" seed) iterations

    /// Forward — see `FunctionLaws.compositionLaws`: the family at a DOMAIN'S `ArtifactWitness` (Phase 390's spelling).
    let compositionLawsAt
        (wa: ArtifactWitness<'A, 'IdA>)
        (wb: ArtifactWitness<'B, 'IdB>)
        (embed: 'B -> 'A)
        (draw: ConfRng.T -> CompositionSample<'A, 'B> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.compositionLaws wa wb embed draw
        |> within (family "compositionLawsAt" seed) iterations

    /// Forward — see `FunctionLaws.verifyFunction`.
    let verifyFunction
        (w: ArtifactWitness<'Node, 'Id>)
        (fn: 'Node)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : FunctionVerifyReport<'Node, 'Id> =
        FunctionLaws.verifyFunction w fn reg genParams seed iterations

    /// Forward — see `FunctionLaws.verifyFunctionSymbolic`.
    let verifyFunctionSymbolic
        (w: ArtifactWitness<'Node, 'Id>)
        (fn: 'Node)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (fixedArgs: Map<string, Arg<'Node>>)
        (maxCases: int)
        (seed: int)
        : FunctionVerifyReport<'Node, 'Id> =
        FunctionLaws.verifyFunctionSymbolic w fn reg fixedArgs maxCases seed

    /// Forward — see `FunctionLaws.renderCounterexample`.
    let renderCounterexample (w: ArtifactWitness<'Node, 'Id>) (cx: VerifyCounterexample<'Node, 'Id>) : string =
        FunctionLaws.renderCounterexample w cx

    /// Forward — see `FunctionLaws.functionVerifyLaws`: the family at a DOMAIN'S `ArtifactWitness` (Phase 390's spelling).
    let functionVerifyLawsAt
        (w: ArtifactWitness<'Node, 'Id>)
        (sound: 'Node)
        (broken: 'Node)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.functionVerifyLaws w sound broken reg genParams
        |> within (family "functionVerifyLawsAt" seed) iterations

    /// Forward — see `FunctionLaws.memoLaws`: the family at a DOMAIN'S `ArtifactWitness` (Phase 390's spelling).
    let memoLawsAt
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (draw: ConfRng.T -> MemoSample<'Node> * ConfRng.T)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.memoLaws w encode draw hashFn
        |> within (family "memoLawsAt" seed) iterations

    /// Forward — see `FunctionLaws.compositionPilot`: the family at a DOMAIN'S `ArtifactWitness` (Phase 390's spelling).
    let compositionPilotAt
        (wa: ArtifactWitness<'A, 'IdA>)
        (wb: ArtifactWitness<'B, 'IdB>)
        (embed: 'B -> 'A)
        (encodeA: 'A -> string)
        (encodeB: 'B -> string)
        (draw: ConfRng.T -> CompositionSample<'A, 'B> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.compositionPilot wa wb embed encodeA encodeB draw
        |> within (family "compositionPilotAt" seed) iterations

    /// Forward — see `FunctionLaws.verifyHonestyLaws`: the family at a DOMAIN'S `ArtifactWitness` (Phase 390's spelling).
    let verifyHonestyLawsAt
        (w: ArtifactWitness<'Node, 'Id>)
        (mkSound: DeterminismSource -> 'Node)
        (mkBroken: DeterminismSource -> 'Node)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (genParams: 'Node -> ConfRng.T -> Map<string, Arg<'Node>> * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.verifyHonestyLaws w mkSound mkBroken reg genParams
        |> within (family "verifyHonestyLawsAt" seed) iterations

    /// Forward — see `FunctionLaws.memoSoundnessLaws`: the family at a DOMAIN'S `ArtifactWitness` (Phase 390's spelling).
    let memoSoundnessLawsAt
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (underDeclaredFn: 'Node)
        (underDeclaredArgs: Map<string, Arg<'Node>>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        FunctionLaws.memoSoundnessLaws w encode underDeclaredFn underDeclaredArgs
        |> within (family "memoSoundnessLawsAt" seed) iterations

    /// Forward — see `PropagationLaws.dirtyPropagationLaws`.
    let dirtyPropagationLaws (seed: int) (iterations: int) : LawResult list =
        PropagationLaws.dirtyPropagationLaws
        |> within (family "dirtyPropagationLaws" seed) iterations

    /// Forward — see `PropagationLaws.propagationEvalLaws`.
    let propagationEvalLaws (seed: int) (iterations: int) : LawResult list =
        PropagationLaws.propagationEvalLaws
        |> within (family "propagationEvalLaws" seed) iterations

    /// Forward — see `PropagationLaws.propagationEvaluatorLawsAt`: the evaluator-contract laws at a
    /// DOMAIN'S `EvaluatorWitness`.
    let propagationEvaluatorLawsAt (evw: EvaluatorWitness<'Model, 'V>) (seed: int) (iterations: int) : LawResult list =
        PropagationLaws.propagationEvaluatorLawsAt evw
        |> within (family "propagationEvaluatorLawsAt" seed) iterations

    /// The evaluator-contract laws for a PRIOR-AWARE evaluator — see `PropagationLaws.propagationPriorLawsAt`
    /// (Phase 250; its own stem since Phase 507): `propagationEvaluatorLawsAt` over the domain's
    /// reference evaluator, then the three laws about the prior-aware `evalNodeWith` the domain hands
    /// `Propagation.evalWith` / `evalFromWith`. A different law SET from `propagationEvaluatorLawsAt`
    /// rather than that family with a parameter pinned, so it carries a stem, not a `…With` (D127).
    let propagationPriorLawsAt
        (evw: EvaluatorWitness<'Model, 'V>)
        (evalNodeWith: 'Model -> (string -> 'V option) -> 'V option -> string -> Result<'V, string>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        propagationEvaluatorLawsAt evw seed iterations
        @ (PropagationLaws.propagationPriorLawsAt evw evalNodeWith
           |> within (family "propagationPriorLawsAt" seed) iterations)

    /// Forward — `propagationPriorLawsAt` under the name it carried until Phase 507, when `…With` came
    /// to mean one pinned parameter only. Kept as a roster row under its own id and its own guard
    /// label, as D127's forwards were, until the next breaking cut removes it.
    [<System.Obsolete("Use Conformance.propagationPriorLawsAt — the same laws under their own stem (Phase 507); this forward goes at the next breaking cut.")>]
    let propagationEvaluatorLawsWith
        (evw: EvaluatorWitness<'Model, 'V>)
        (evalNodeWith: 'Model -> (string -> 'V option) -> 'V option -> string -> Result<'V, string>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        propagationEvaluatorLawsAt evw seed iterations
        @ (PropagationLaws.propagationPriorLawsAt evw evalNodeWith
           |> within (family "propagationEvaluatorLawsWith" seed) iterations)

    /// Forward — see `SurfaceLaws.projectionLaws`: the projection laws at a DOMAIN'S
    /// `ProjectionWitness`.
    let projectionLawsAt
        (pw: ProjectionWitness<'Node, 'Id, 'Op>)
        (applyOps: 'Op list -> Result<'Node, string>)
        (wireEncode: 'Node -> string)
        (gen: ConfRng.T -> 'Node * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        SurfaceLaws.projectionLaws pw applyOps wireEncode gen
        |> within (family "projectionLawsAt" seed) iterations

    /// Forward — see `ObserverLaws.observerLaws` (Phase 298): in-memory equals live, a cyclic parent
    /// declaration terminates, and re-entrant subscribers are isolated, at a DOMAIN'S
    /// `ObserverWitness` and input generator.
    let observerLawsAt<'Input, 'Flag when 'Input: equality and 'Flag: equality>
        (w: ObserverWitness<'Input, 'Flag>)
        (genInput: ConfRng.T -> 'Input * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        ObserverLaws.observerLaws w genInput
        |> within (family "observerLawsAt" seed) iterations

    /// Forward — see `SanitizeLaws.sanitizeLaws` (Phase 349): the sanitisation floor each of the six
    /// `Fuaran.Core.Idl.Sanitize` functions claims, over generated adversarial strings and the pinned
    /// vectors, at a host's `SanitizeWitness` — `SanitizeWitness.core` for Core's own floor.
    let sanitizeLawsAt (w: SanitizeWitness) (seed: int) (iterations: int) : LawResult list =
        SanitizeLaws.sanitizeLaws w |> within (family "sanitizeLawsAt" seed) iterations

    /// Forward — see `CodecLaws.codecLaws` (Phase 442): the premises a domain owes for the parts it
    /// hands the `Codec` blocks, sampled at its own codec over the values its generator draws. Spelled
    /// `…At` since Phase 507, when the roster typed the codec it takes as a witness capability (D127,
    /// D148.3 amended).
    let codecLawsAt
        (codec: Codec<'T>)
        (gen: ConfRng.T -> 'T * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        CodecLaws.codecLaws codec gen |> within (family "codecLawsAt" seed) iterations

    /// Forward — `codecLawsAt` under the name it carried until Phase 507. Kept as a roster row under
    /// its own id and its own guard label, as D127's forwards were, until the next breaking cut.
    [<System.Obsolete("Use Conformance.codecLawsAt — the same laws, spelled …At because the family takes the domain's codec (Phase 507); this forward goes at the next breaking cut.")>]
    let codecLaws (codec: Codec<'T>) (gen: ConfRng.T -> 'T * ConfRng.T) (seed: int) (iterations: int) : LawResult list =
        CodecLaws.codecLaws codec gen |> within (family "codecLaws" seed) iterations

    /// Forward — see `SurfaceLaws.aiSurfaceLawsAt`: the AI-surface laws under the DOMAIN'S own
    /// policy. `aiSurfaceLawsUnderKitPolicy` is the kit-fixture form beside it.
    let aiSurfaceLawsAt
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        SurfaceLaws.aiSurfaceLawsAt w genOp state0
        |> within (family "aiSurfaceLawsAt" seed) iterations

    /// Forward — see `SurfaceLaws.aiSurfaceKitPolicyLawsAt`: the AI-surface laws with the KIT'S policy swapped
    /// in for the domain's, at the domain's witness — the proposal plumbing, beside `aiSurfaceLawsAt`.
    let aiSurfaceKitPolicyLawsAt
        (w: AiSurfaceWitness<'State, 'Op, 'Rej>)
        (genOp: ConfRng.T -> 'Op * ConfRng.T)
        (state0: 'State)
        (seed: int)
        (iterations: int)
        : LawResult list =
        SurfaceLaws.aiSurfaceKitPolicyLawsAt w genOp state0
        |> within (family "aiSurfaceKitPolicyLawsAt" seed) iterations

    /// Forward — see `SurfaceLaws.frozenWitnessFields`.
    let frozenWitnessFields: WitnessFields list = SurfaceLaws.frozenWitnessFields

    /// Forward — see `SurfaceLaws.unfrozenWitnesses`.
    let unfrozenWitnesses: FreezeExemption list = SurfaceLaws.unfrozenWitnesses

    /// Forward — see `SurfaceLaws.witnessDeclaredFieldsLaw` (Phase 387).
    let witnessDeclaredFieldsLaw (record: string) (pinned: string list) (declared: string list) : LawResult =
        SurfaceLaws.witnessDeclaredFieldsLaw record pinned declared

    // `witnessFieldsLaw` and `witnessCoverageLaw` left this facade at `0.37.0` (Phase 386): they take a
    // `System.Type`, a reflection handle no domain hands the kit, and `witnessSurfaceLaws ()` below
    // runs both over the records the kit was compiled against. They stay internal to the kit.

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
        // Each member family draws its own stream, forked from the caller's seed by a fixed key
        // (Phase 507: `Runner.Fork` holds the arithmetic the four offsets used to spell inline).
        let reducerGuards () =
            ChainStreamLaws.reducer sw.Apply streamGen None
            |> within ((family "reducer" seed).Fork 4) iterations
            |> List.filter LawResult.isAdequacy

        let rest =
            if witness |> List.forall _.Passed then
                (AlgebraTreeLaws.opAlgebra nodew idw opGen
                 |> within ((family "opAlgebra" seed).Fork 1) iterations)
                @ (AlgebraTreeLaws.diffLaws nodew idw opGen
                   |> within ((family "diffLaws" seed).Fork 3) iterations)
                @ (ChainStreamLaws.streamLaws sw streamGen hashFn
                   |> within ((family "streamLaws" seed).Fork 2) iterations)
                @ reducerGuards ()
            else
                [] // witness defect — the downstream laws would be noise

        let results = witness @ rest

        { Results = results
          AllPassed = results |> List.forall _.Passed }

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
        let stream =
            if red |> List.filter (LawResult.isAdequacy >> not) |> List.forall _.Passed then
                ChainStreamLaws.streamLaws sw streamGen hashFn
                |> within ((family "streamLaws" seed).Fork 1) iterations
            else
                []

        let results = red @ stream

        { Results = results
          AllPassed = results |> List.forall _.Passed }
