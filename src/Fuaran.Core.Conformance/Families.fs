namespace Fuaran.Core

// Phase 184 — the law-family ROSTER, exported by the kit as data.
//
// Until this module the kit shipped its families and enumerated them nowhere. Three separate
// readers each kept their own list, and each list was derived by a rule that could miss a family:
//
//   * `SampleAdequacy.census` classifies a family's sample, and its completeness check reflected
//     over method NAMES ending in `Laws` / `LawsWith` / `laws` / `lawsWith` — so `opAlgebra`,
//     `reducer` and `compositionPilot` were invisible to it. Two of those three are the families
//     `certify` and `certifyStream` are BUILT FROM: the most central laws in the kit were the ones
//     the roster could not see.
//   * the claims ladder's `dischargedBy` was held to every public static method of `Conformance`,
//     which is far wider than the law set — so a row could name a helper and pass.
//   * a consumer's generated conformance census, and the projection that reads it, quantify over
//     whatever roster they are handed; a family absent from it is reported unrostered by every
//     consumer and can never be marked adopted by any of them.
//
// A missing row in any of those is silent by construction: every check quantifies over the list,
// so the one thing a list cannot notice is a family nobody added to it. This module is the single
// declared enumeration all three now read, and `ConformanceFamiliesTests` holds it to REFLECTION
// OVER RETURN TYPE — every public entry point of the kit's modules that answers with
// `LawResult list` — rather than over a naming convention. The name shape is what let the three
// escape; the return type is what a law family actually is.
//
// It declares, it does not derive. What each family IS — its witness demands, whether an aggregate
// runs it, which ladder obligation its green run discharges — is a fact about the code that only a
// reader can state; the suite's job is to hold every one of those statements to the tree, and to
// refuse the roster that is merely incomplete. See `docs/conformance-families.md` (generated) and
// the `docs/conformance-families.json` export beside it.

/// The kit's own enumeration of the law families it ships.
///
/// PUBLIC SURFACE. A host reads it to enumerate the families it must answer for; a projection
/// reads the generated JSON export to do the same offline. Adding a law family to the kit means
/// adding a record here in the same commit — the suite fails naming the family otherwise.
module Families =

    /// WHY a family is opt-in — the closed vocabulary, one case per reason the roster actually
    /// carries. It is a DU rather than a string because the set is closed and a reader dispatches
    /// on it; a free-text reason would be a second place for prose to drift from the code.
    ///
    /// **`NeedsSecondFixture` is deliberately absent** (Phase 194). The shard proposed it, and no
    /// family instantiates it: the families that need nothing from the domain need NO fixture at
    /// all rather than a second one, and they are `SeamNotEveryDomainHas`. A case no value inhabits
    /// is a case a reader has to rule out on every encounter, so it is not declared.
    type OptInReason =
        /// The family demands a witness, generator or sink the BASE contract does not — an
        /// `ArtifactWitness`, an `IAttestationSink`, a `LaneGen`. A domain that has not built that
        /// capability cannot run it at all, so `certify` cannot fold it in.
        | NeedsWitnessCapability
        /// The family certifies a SEAM not every domain has — a columnar layer, a capability
        /// registry, a lease axis. It runs from the kit's own fixtures, or (Phase 246) takes only a
        /// witness type the base run already demands — a columnar `StreamGen` — so what makes it
        /// opt-in is relevance, never cost.
        | SeamNotEveryDomainHas
        /// The family takes exactly the base run's witness and asks for MORE than the base
        /// contract promises — footprint independence, concurrent apply, arbitration. A domain
        /// elects it; the base contract does not imply it.
        | StrongerPromise

    /// One law family: a public entry point of the kit that answers with `LawResult list`.
    type LawFamily =
        {
            /// The roster key — `"<Module>.<Entry>"`. This is the name a census cell, a ladder
            /// row's `dischargedBy` and a projection's roster all use, so it is the one spelling
            /// that must not drift.
            Id: string
            /// The kit module the entry point lives in (`Conformance`, `FoldConfluence`,
            /// `IncrementalDelta`).
            Module: string
            /// The entry point's own name, unqualified.
            Entry: string
            /// The witness and generator types a domain must supply to run it, in parameter
            /// order — every parameter whose type name ends in `Witness`, `Gen` or `Sink`. Empty
            /// for a family the kit runs against its own fixtures, which needs nothing but a seed.
            Witness: string list
            /// `true` when a domain must call the family DELIBERATELY: it is not folded into
            /// `Conformance.certify` or `Conformance.certifyStream`, because it certifies a seam
            /// not every domain has. `false` for the five families an aggregate is built from.
            ///
            /// DERIVED from `Reason` at every construction site in this module — `OptIn` is
            /// `Reason.IsSome` — so "opt-in with no reason" is unrepresentable here rather than
            /// merely caught by a test. The field stays published because it is what every reader
            /// of the roster already dispatches on.
            OptIn: bool
            /// Why the family is opt-in, `None` for a base-run family. Phase 194: a census that
            /// says a consumer did not run a family is only actionable if the roster says why the
            /// family was theirs to elect.
            Reason: OptInReason option
            /// The claims-ladder obligations (`proofs.json` row ids) whose `dischargedBy` names
            /// this family — the rows a green run of it at a domain's own witness discharges.
            /// The ladder is the other side of the same relation and the suite holds the two
            /// equal, so this is an index rather than a second declaration.
            Discharges: string list
        }

    /// Where a family's REFUSAL population comes from — Phase 220's audit vocabulary. A refusal
    /// population is the set of refused / rejected / invalid outcomes at least one of the family's
    /// laws branches on or asserts something about. What matters is whether a run can MISS it:
    /// a population the family builds cannot be missed, and one a generator draws can.
    type RefusalPopulation =
        /// No law reads a refused outcome. Either the algebra has none, or refusals occur and every
        /// law passes over them (a rejected op that simply does not extend a chain).
        | NoRefusal
        /// Every refused case a law reads is BUILT — each iteration, or from a fixed fixture — so
        /// no run can miss it.
        | Built
        /// At least part of the refused population is DRAWN, and a run that misses it goes RED:
        /// a law DEMANDS the refused case ("a broken function is caught"), so an unreached refusal
        /// is a failing law rather than a green one. Loud, so it needs no guard.
        | DrawnMissIsRed
        /// At least part of the refused population is DRAWN — by a caller's generator or by the
        /// kit's own roll — and a run that misses it stays GREEN, because the laws that read it are
        /// agreements or implications that hold trivially over an empty population. This is the
        /// vacuity class: a family carrying it must be `Guarded` in `SampleAdequacy.census`, or its
        /// green run can mean nothing on the side the law is about.
        | Drawn

    /// One row of the refusable-family audit: a family, where its refusal population comes from,
    /// and the evidence for that verdict in words a reader can check against the code.
    type RefusalAudit =
        { Family: string
          Population: RefusalPopulation
          Why: string }

    /// Every law family the kit ships, in declaration order. Renderings sort by `Id`, so the order
    /// here is for a reader's benefit and never reaches an artefact.
    let families: LawFamily list =
        // `reason` is `OptInReason option`; `OptIn` is derived from it, so a family cannot be
        // declared opt-in without saying why, and a base-run family cannot carry a reason.
        let f m entry witness reason discharges =
            { Id = m + "." + entry
              Module = m
              Entry = entry
              Witness = witness
              OptIn = Option.isSome reason
              Reason = reason
              Discharges = discharges }

        let c entry witness reason discharges =
            f "Conformance" entry witness reason discharges

        let treeWitness = [ "NodeWitness"; "IdWitness"; "OpGen" ]
        let streamWitness = [ "StreamWitness"; "StreamGen" ]
        let none: string list = []

        [
          // ---- the base run: what `certify` and `certifyStream` are built from ----
          c "witnessLaws" treeWitness None [ "lawful-abstract-witness" ]
          c "opAlgebra" treeWitness None [ "tree-algebra-well-formed-states" ]
          c "diffLaws" treeWitness None []
          c "streamLaws" streamWitness None []
          c "reducer" [ "StreamGen" ] None []

          // ---- opt-in: a seam not every domain has ----
          c "diffContainedLaws" treeWitness (Some StrongerPromise) []
          c "normalizeLaws" treeWitness (Some StrongerPromise) []
          c "containerLaws" treeWitness (Some StrongerPromise) []
          c "mergeConflictLaws" treeWitness (Some StrongerPromise) []
          c "reconcileLaws" treeWitness (Some StrongerPromise) []
          c "footprintLaws" treeWitness (Some StrongerPromise) [ "independence-diamond" ]
          c "concurrencyLaws" treeWitness (Some StrongerPromise) [ "lanes-apply" ]
          c "concurrencyLawsWith" treeWitness (Some StrongerPromise) []
          c "arbitrationLaws" treeWitness (Some StrongerPromise) []
          c "snapshotLaws" streamWitness (Some StrongerPromise) []
          c "snapshotLawsWith" streamWitness (Some StrongerPromise) []
          c "dagLaws" streamWitness (Some StrongerPromise) []
          c "casLaws" streamWitness (Some StrongerPromise) []
          c "idempotencyLaws" streamWitness (Some StrongerPromise) []
          c "hashFnLaws" streamWitness (Some StrongerPromise) []
          c "attributedLaws" streamWitness (Some StrongerPromise) []
          c "codecInjectivityLaws" streamWitness (Some StrongerPromise) []
          c "noAttestationVacuityLaws" streamWitness (Some StrongerPromise) []
          c "attestationLaws" [ "StreamWitness"; "StreamGen"; "IAttestationSink" ] (Some NeedsWitnessCapability) []
          c "compositionLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "compositionPilot" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "memoLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "memoSoundnessLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "functionVerifyLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "verifyHonestyLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "encoderInjectivityLaws" [ "ArtifactWitness" ] (Some NeedsWitnessCapability) []
          c "projectionLaws" [ "ProjectionWitness" ] (Some NeedsWitnessCapability) []
          c "aiSurfaceLaws" [ "AiSurfaceWitness" ] (Some NeedsWitnessCapability) []
          c "aiSurfaceLawsUnderKitPolicy" [ "AiSurfaceWitness" ] (Some NeedsWitnessCapability) []

          // Phase 246 — the seam families at a DOMAIN'S seam. Their fixture-bound forms below take
          // only a seed and certify Core's own fixtures, which is what they are for.
          c "capabilityLawsWith" [ "CapabilitySeamWitness" ] (Some NeedsWitnessCapability) []
          c "queryLawsWith" [ "QuerySeamWitness" ] (Some NeedsWitnessCapability) []
          c "capabilityPipelineLawsWith" [ "CapabilityPipelineWitness" ] (Some NeedsWitnessCapability) []

          c
              "keyedChildrenLaws"
              ([ "KeyedWitness" ] @ treeWitness)
              (Some NeedsWitnessCapability)
              [ "witness-surface-scope" ]

          c
              "propagationEvaluatorLaws"
              [ "EvaluatorWitness" ]
              (Some NeedsWitnessCapability)
              [ "propagation-change-set-and-prior" ]

          c
              "propagationEvaluatorLawsWith"
              [ "EvaluatorWitness" ]
              (Some NeedsWitnessCapability)
              [ "propagation-prior-blind" ]

          c "captureReplayLaws" none (Some SeamNotEveryDomainHas) []
          c "transformLaws" none (Some SeamNotEveryDomainHas) []
          c "constructThenEncodeLaws" none (Some SeamNotEveryDomainHas) []
          c "hashFnAdversarialLaws" none (Some SeamNotEveryDomainHas) []
          c "capabilityLaws" none (Some SeamNotEveryDomainHas) []
          c "queryLaws" none (Some SeamNotEveryDomainHas) []
          c "registryLaws" none (Some SeamNotEveryDomainHas) []
          c "packLoadingLaws" none (Some SeamNotEveryDomainHas) []
          c "aggregateParityLaws" none (Some SeamNotEveryDomainHas) []
          c "columnarOpLaws" none (Some SeamNotEveryDomainHas) []
          // Phase 246 — the columnar pair at a domain's `StreamGen<ColumnOp, Table>`. `StreamGen` is
          // a base-run witness, so the reason stays `SeamNotEveryDomainHas`.
          c "columnarOpLawsWith" [ "StreamGen" ] (Some SeamNotEveryDomainHas) []
          c "columnarValidatorLaws" none (Some SeamNotEveryDomainHas) []
          c "incrementalLaws" none (Some SeamNotEveryDomainHas) []
          c "incrementalLawsWith" [ "StreamGen" ] (Some SeamNotEveryDomainHas) []
          c "paramLaws" none (Some SeamNotEveryDomainHas) []
          c "schemaWalkLaws" none (Some SeamNotEveryDomainHas) []
          c "deferredLaws" none (Some SeamNotEveryDomainHas) []
          c "capabilityPipelineLaws" none (Some SeamNotEveryDomainHas) []
          c "capabilityPipelineIncrementalLaws" none (Some SeamNotEveryDomainHas) []
          c "dirtyPropagationLaws" none (Some SeamNotEveryDomainHas) []
          c "propagationEvalLaws" none (Some SeamNotEveryDomainHas) []
          c "canonicalFloatLaws" none (Some SeamNotEveryDomainHas) []
          c "chainBreakReasonLaws" none (Some SeamNotEveryDomainHas) []
          c "dagBreakReasonLaws" none (Some SeamNotEveryDomainHas) []
          c "nowLaws" none (Some SeamNotEveryDomainHas) []
          c "slotParamLaws" none (Some SeamNotEveryDomainHas) []

          f "FoldConfluence" "laneFoldLaws" [ "StreamWitness"; "LaneGen" ] (Some NeedsWitnessCapability) []
          f "FoldConfluence" "laneFoldLawsWith" [ "StreamWitness"; "LaneGen" ] (Some NeedsWitnessCapability) []

          f "IncrementalDelta" "laws" none (Some SeamNotEveryDomainHas) []
          f "IncrementalDelta" "lawsWith" none (Some SeamNotEveryDomainHas) [] ]

    /// The roster's keys, sorted — the enumeration a census, a ladder or a projection quantifies
    /// over.
    let ids: string list = families |> List.map (fun f -> f.Id) |> List.sort

    /// The modules the roster covers, sorted. A reflection check reads THIS rather than a second
    /// list, so a module added to the kit is covered by adding its families here and nothing else.
    let modules: string list =
        families |> List.map (fun f -> f.Module) |> List.distinct |> List.sort

    /// The family with this id, if the kit ships one.
    let tryFind (id: string) : LawFamily option =
        families |> List.tryFind (fun f -> f.Id = id)

    /// Every ladder obligation the roster claims to discharge, paired with the family that
    /// discharges it, sorted by obligation.
    let obligations: (string * string) list =
        [ for f in families do
              for o in f.Discharges -> o, f.Id ]
        |> List.sortBy fst

    /// Phase 220 — the refusable-family audit, one row per family. It is DATA so the next audit
    /// diffs it rather than re-reading sixty families: a row is a verdict and the evidence for it.
    ///
    /// The question each row answers: does the algebra the family certifies have a REFUSAL
    /// population — a refused / rejected / invalid outcome a law branches on — and can a run miss
    /// it without a law going red? `Drawn` is the class that matters: the suite holds every
    /// `Drawn` row to a `Guarded` census class, so a family whose refusals a run can silently miss
    /// cannot report an unguarded pass. The suite also holds this list equal to the roster in both
    /// directions, so a family added later is audited in the commit that ships it.
    let refusalAudit: RefusalAudit list =
        let r family population why =
            { Family = family
              Population = population
              Why = why }

        [
          // ---- the base run ----
          r "Conformance.witnessLaws" NoRefusal "accessor round-trips only; no apply, no refusal path"
          r
              "Conformance.opAlgebra"
              Drawn
              "canApply ≡ apply and totality read both outcomes; genOp DRAWS refusals, and the collision arm BUILDS them only where the witness can carry a multi-node subtree"
          r "Conformance.diffLaws" NoRefusal "a refused op is skipped while building `after`; no law reads it"
          r
              "Conformance.streamLaws"
              Built
              "the tampered chain it must reject is built each iteration; a rejected append is only skipped"
          r
              "Conformance.reducer"
              Drawn
              "totality (a refusal is typed, not thrown) and the envelope law read refusals that only the caller's StreamGen draws"

          // ---- tree-shaped opt-ins ----
          r
              "Conformance.diffContainedLaws"
              Drawn
              "the refusal IFF reads the drawn pair under the witness's canHold, and a canHold that refuses nothing holds it trivially; the graft probe beside it is built"
          r "Conformance.normalizeLaws" NoRefusal "a refused op is skipped; no law reads it"
          r
              "Conformance.containerLaws"
              Built
              "the interior-offender graft is built and must be refused NotAContainer; its guard covers the built arms"
          r "Conformance.mergeConflictLaws" NoRefusal "conflicts is a report list, never a refused outcome"
          r
              "Conformance.reconcileLaws"
              Drawn
              "a reconcile Error arises from OpGen-drawn scripts; guarded on reconcile outcome"
          r "Conformance.footprintLaws" NoRefusal "a refused op is skipped; no law reads it"
          r "Conformance.concurrencyLaws" NoRefusal "delegates to concurrencyLawsWith"
          r "Conformance.concurrencyLawsWith" NoRefusal "a drawn refusal is skipped; an applyAll Error only fails a law"
          r
              "Conformance.arbitrationLaws"
              Drawn
              "Inapplicable comes from the kit's corruption roll and Conflicts from drawn scripts; guarded on arbitration bucket"
          r
              "Conformance.keyedChildrenLaws"
              Built
              "the keyed-and-surface and double-keyed collisions are built through PlaceKeyedChild; its guard covers the built arms"

          // ---- stream-shaped opt-ins ----
          r "Conformance.snapshotLaws" NoRefusal "delegates to snapshotLawsWith"
          r "Conformance.snapshotLawsWith" NoRefusal "a rejected append is skipped; a compact Error only fails a law"
          r "Conformance.dagLaws" Built "the tampered node it must reject is built each iteration"
          r
              "Conformance.casLaws"
              Drawn
              "match ≡ append compares a domain refusal with a CAS Domain rejection only when StreamGen draws one; the stale-head rejection beside it is built"
          r
              "Conformance.idempotencyLaws"
              Drawn
              "fresh-key ≡ append and the CAS arm compare domain refusals only when StreamGen draws one; Duplicate and StaleHead are built"
          r "Conformance.hashFnLaws" Built "reorder, drop and bit-flip are built and must fail verifyChain"
          r "Conformance.attributedLaws" Built "a re-attributed op is built and must fail verifyChain"
          r "Conformance.codecInjectivityLaws" NoRefusal "a Decode refusal only fails a law"
          r
              "Conformance.noAttestationVacuityLaws"
              Built
              "a plausible attestation is built and the no-op sink must reject it"
          r
              "Conformance.attestationLaws"
              Built
              "op and actor forgeries are built each iteration; its guard is on the signing outcome"

          // ---- artifact-witness opt-ins ----
          r "Conformance.compositionLaws" NoRefusal "a compose Error only fails a law or is compared opaquely"
          r "Conformance.compositionPilot" NoRefusal "as compositionLaws; a memo Error only fails a law"
          r "Conformance.memoLaws" NoRefusal "every Error arm only fails a law"
          r "Conformance.memoSoundnessLaws" NoRefusal "an Error only fails a law; the cache bypass is not a refusal"
          r
              "Conformance.functionVerifyLaws"
              DrawnMissIsRed
              "the broken function is caught only when genParams reaches its bad sub-space, and a broken function that verifies clean is itself a red law"
          r
              "Conformance.verifyHonestyLaws"
              DrawnMissIsRed
              "the broken verdicts depend on genParams, and a broken function verifying under any axis is a red law"
          r "Conformance.encoderInjectivityLaws" NoRefusal "no refused outcome is read"

          // ---- the remaining witnessed opt-ins ----
          r "Conformance.projectionLaws" NoRefusal "a re-import Error only fails a law"
          r
              "Conformance.aiSurfaceLaws"
              Drawn
              "explainRejection and the allowed-submit parity read a reducer rejection only when the caller's op generator draws one, and since Phase 246 the deny and park arms are reached only when the domain's own Decide chooses them; unknown tool and unknown id are built"
          r
              "Conformance.aiSurfaceLawsUnderKitPolicy"
              Drawn
              "explainRejection and the allowed-submit parity read a reducer rejection only when the caller's op generator draws one; the kit rolls the decision, and unknown tool and unknown id are built"
          r
              "Conformance.capabilityLawsWith"
              Drawn
              "every refusal the three laws read is a call the domain's generator draws; guarded on settled, pending and refused"
          r
              "Conformance.queryLawsWith"
              Drawn
              "every refusal the three laws read is a call the domain's generator draws; guarded on settled, pending and refused"
          r
              "Conformance.capabilityPipelineLawsWith"
              Built
              "the unregistered-capability and undeclared-argument pipelines are built from every drawn Invoke node; guarded on invoke node"
          r
              "Conformance.propagationEvaluatorLaws"
              Drawn
              "the failing-evaluator arm comes from the domain's own edits; guarded on evaluator edit"
          r
              "Conformance.propagationEvaluatorLawsWith"
              Drawn
              "runs propagationEvaluatorLaws first, so its failing-evaluator arm is drawn the same way; the prior-aware arms are guarded on prior-aware edit"

          // ---- the fixture-only families ----
          r "Conformance.captureReplayLaws" Built "the tampered capture and the misordered replay are built"
          r
              "Conformance.transformLaws"
              Drawn
              "the Error/Error parity arm is reached only when the caller's generator yields an evaluation error"
          r
              "Conformance.constructThenEncodeLaws"
              NoRefusal
              "Reject corpus cases are filtered out; a Construct Error only fails a law"
          r "Conformance.hashFnAdversarialLaws" NoRefusal "a collision search; no refused outcome"
          r
              "Conformance.capabilityLaws"
              Built
              "out-of-space arg, unknown arg and unregistered id are built each iteration"
          r "Conformance.queryLaws" Built "type mismatch, unknown param, NoSuchQuery and ExecutionFailed are built"
          r "Conformance.registryLaws" Built "an unregistered id and an out-of-space arg are built"
          r "Conformance.packLoadingLaws" Built "a stale-version pack and an unknown base are built"
          r
              "Conformance.aggregateParityLaws"
              NoRefusal
              "an Error only lands in a parity bucket, and the kit draws no type that can raise one"
          r "Conformance.columnarOpLaws" Drawn "delegates to columnarOpLawsWith"
          r
              "Conformance.columnarOpLawsWith"
              Drawn
              "the refused ops are drawn by the caller's StreamGen (columnarOpLaws: the kit's own roll); guarded on invert's refusal population"
          r
              "Conformance.columnarValidatorLaws"
              Drawn
              "null and out-of-range faults are injected by the kit's own roll, and a fault-free draw satisfies the count laws trivially"
          r "Conformance.incrementalLaws" NoRefusal "an Error is only skipped"
          r
              "Conformance.incrementalLawsWith"
              NoRefusal
              "an op the table refuses and a pipeline that does not evaluate are only skipped"
          r "Conformance.paramLaws" Built "one paramsOf member is dropped each iteration and must refuse UnboundParam"
          r "Conformance.schemaWalkLaws" NoRefusal "an evaluator rejection is skipped"
          r "Conformance.deferredLaws" Built "the fixed Failed case must yield Error"
          r "Conformance.capabilityPipelineLaws" Built "the fixed ill-typed pipeline must refuse EdgeTypeMismatch"
          r "Conformance.capabilityPipelineIncrementalLaws" NoRefusal "an eval Error only records a failure"
          r "Conformance.dirtyPropagationLaws" NoRefusal "no refused outcome is read"
          r
              "Conformance.propagationEvalLaws"
              Built
              "the unknown change and the leaky evaluator are built each iteration"
          r "Conformance.canonicalFloatLaws" NoRefusal "no refused outcome is read"
          r "Conformance.chainBreakReasonLaws" Built "all three break kinds are built each iteration"
          r "Conformance.dagBreakReasonLaws" Built "both break kinds are built each iteration"
          r "Conformance.nowLaws" Built "the unpinned clock must refuse UnpinnedClock, built each iteration"
          r "Conformance.slotParamLaws" Built "the unbound and mistyped slots are built each iteration"

          // ---- outside `Conformance` ----
          r "FoldConfluence.laneFoldLaws" Drawn "delegates to laneFoldLawsWith"
          r
              "FoldConfluence.laneFoldLawsWith"
              Drawn
              "LaneHalted and LaneRejected come from the caller's LaneGen; guarded on lane-fold outcome"
          r "IncrementalDelta.laws" Drawn "delegates to lawsWith"
          r
              "IncrementalDelta.lawsWith"
              Drawn
              "declined pipelines are picked from a fixed menu by the kit's roll; guarded on refresh class" ]

    /// The audit row for one family, if the roster audits it (the suite holds that it always does).
    let tryRefusal (id: string) : RefusalAudit option =
        refusalAudit |> List.tryFind (fun a -> a.Family = id)

    // ---- the exports ------------------------------------------------------------------------

    let private quote (s: string) : string =
        let esc (c: char) =
            match c with
            | '"' -> "\\\""
            | '\\' -> "\\\\"
            | '\n' -> "\\n"
            | '\r' -> "\\r"
            | '\t' -> "\\t"
            | c when c < ' ' -> "\\u" + (int c).ToString "x4"
            | c -> string c

        "\"" + (s |> Seq.map esc |> String.concat "") + "\""

    let private jsonArray (xs: string list) : string =
        "[" + (xs |> List.map quote |> String.concat ", ") + "]"

    /// What the `cases` cell reads for a family the caller measured nothing for — Phase 196.
    ///
    /// It is a THIRD state and not a synonym for `vacuous`: "this run exercised no case" and "no
    /// run was handed to the renderer" are different facts, and collapsing them would let a
    /// consumer that never measured anything render as a consumer whose families all ran empty.
    /// The distinction is the whole reason the renderings take the counts rather than deriving
    /// them — a roster cannot run a law.
    [<Literal>]
    let unmeasuredToken = "unmeasured"

    /// The census cell for one family: the measured count, `vacuous`, or `unmeasured`.
    let private casesCell (cases: (string * CaseCount) list) (id: string) : string =
        match cases |> List.tryFind (fun (k, _) -> k = id) with
        | Some(_, c) -> SampleAdequacy.renderCases c
        | None -> unmeasuredToken

    /// The wire spelling of an opt-in reason — the JSON member and the markdown cell both use
    /// it, so the two renderings never disagree about a family. A base-run family has none.
    let reasonToken (r: OptInReason) : string =
        match r with
        | NeedsWitnessCapability -> "needs-witness-capability"
        | SeamNotEveryDomainHas -> "seam-not-every-domain-has"
        | StrongerPromise -> "stronger-promise"

    /// The wire spelling of a refusal-audit verdict — Phase 220. `unaudited` is never rendered for
    /// a shipped family (the suite holds the audit equal to the roster); it exists so the renderer
    /// is total rather than throwing on a roster a consumer extended.
    let refusalToken (id: string) : string =
        match tryRefusal id with
        | Some a ->
            match a.Population with
            | NoRefusal -> "none"
            | Built -> "built"
            | DrawnMissIsRed -> "drawn-miss-is-red"
            | Drawn -> "drawn"
        | None -> "unaudited"

    /// The adequacy cell — Phase 220. What `certify`'s verdict for a family is made of, as a fact
    /// the generated data carries rather than one a reader reconstructs: `unconditional` (every
    /// iteration builds every branch, so a green run is a pass), `guarded-reached` (the family
    /// carries a guard and this run reached every guarded side), `guarded-starved` (the guard went
    /// red — the run tested nothing on a side a law is about), or `guarded-unmeasured` (a guarded
    /// family no run was handed for). Read from `SampleAdequacy.census` and the measured run, so it
    /// cannot disagree with either.
    let adequacyToken (cases: (string * CaseCount) list) (id: string) : string =
        match SampleAdequacy.census |> List.tryFind (fun (k, _) -> k = id) with
        | None -> "unclassified"
        | Some(_, Unconditional _) -> "unconditional"
        | Some(_, Guarded _) ->
            match cases |> List.tryFind (fun (k, _) -> k = id) with
            | None -> "guarded-unmeasured"
            | Some(_, c) when List.isEmpty c.Starved -> "guarded-reached"
            | Some _ -> "guarded-starved"

    /// The roster as JSON — the machine export, and the one an offline projection reads without
    /// building or running anything (it is committed, generated, at `docs/conformance-families.json`).
    ///
    /// The shape is a CONTRACT and is documented in `STABILITY.md`: a top-level object carrying
    /// `kind`, `schema`, `package` and a `families` array sorted by `id`, each member an object
    /// with `id`, `module`, `entry`, `witness` (array), `optIn` (boolean), `reason` (a string from
    /// the [[OptInReason]] vocabulary, **present only for an opt-in family** — this wire model has
    /// no null), `discharges` (array), `cases` (a string: a decimal count, `vacuous`, or
    /// `unmeasured`), `adequacy` (see [[adequacyToken]]) and `refusal` (see [[refusalToken]]).
    ///
    /// `schema` reads 4 since Phase 220 added `adequacy` and `refusal`; it read 3 from Phase 196's
    /// `cases` and 2 from Phase 194's `reason`. The
    /// bump is free and therefore taken: a search of the workspace found no reader of this file
    /// outside this repository's own suite, so nothing keys on the old number, and a shape that
    /// changes under an unmoved stamp is the drift class this estate keeps paying for elsewhere.
    /// Members are written in that order and the array is sorted, so the rendering is byte-stable
    /// across runs and a diff shows only what moved. Two spaces of indent, `\n` line endings, and
    /// a trailing newline.
    ///
    /// `cases` is the ONE member a roster cannot derive — a declaration cannot run a law — so it
    /// is supplied by the caller that did run them. `toJson ()` renders `unmeasured` for every
    /// family, which is the honest cell for a caller that measured nothing.
    let toJsonWith (cases: (string * CaseCount) list) : string =
        let family (f: LawFamily) =
            // `reason` is OMITTED for a base-run family rather than rendered `null`: this wire
            // model has no null (`JVal` cannot represent one, and `no_null_ever` is a proved
            // grammar theorem over the renderer), so a null here would emit a document the kit's
            // own parser refuses. Absence is how this format spells "not applicable".
            [ "      " + quote "id" + ": " + quote f.Id
              "      " + quote "module" + ": " + quote f.Module
              "      " + quote "entry" + ": " + quote f.Entry
              "      " + quote "witness" + ": " + jsonArray f.Witness
              "      " + quote "optIn" + ": " + (if f.OptIn then "true" else "false")
              "      " + quote "discharges" + ": " + jsonArray f.Discharges
              "      " + quote "cases" + ": " + quote (casesCell cases f.Id)
              "      " + quote "adequacy" + ": " + quote (adequacyToken cases f.Id)
              "      " + quote "refusal" + ": " + quote (refusalToken f.Id) ]
            |> fun members ->
                match f.Reason with
                | None -> members
                | Some r ->
                    // after `optIn`, before `discharges` — the documented member order
                    let head, tail = List.splitAt 5 members
                    head @ [ "      " + quote "reason" + ": " + quote (reasonToken r) ] @ tail
            |> String.concat ",\n"
            |> fun body -> "    {\n" + body + "\n    }"

        let body =
            families
            |> List.sortBy (fun f -> f.Id)
            |> List.map family
            |> String.concat ",\n"

        "{\n"
        + "  "
        + quote "kind"
        + ": "
        + quote "fuaran.core.conformance.families"
        + ",\n"
        + "  "
        + quote "schema"
        + ": 4,\n"
        + "  "
        + quote "package"
        + ": "
        + quote "Fuaran.Core.Conformance"
        + ",\n"
        + "  "
        + quote "families"
        + ": [\n"
        + body
        + "\n  ]\n}\n"

    /// The roster as the generated `docs/conformance-families.md` — the human-readable half of the
    /// same export. Sorted by `id`, so the table is byte-stable and a diff shows only what moved.
    ///
    /// `cases` carries what a run measured, per Phase 196; `adequacy` and `refusal` are Phase 220's.
    let toMarkdownWith (cases: (string * CaseCount) list) : string =
        let cell (xs: string list) =
            if List.isEmpty xs then
                "—"
            else
                xs |> List.map (fun x -> "`" + x + "`") |> String.concat ", "

        let row (f: LawFamily) =
            sprintf
                "| `%s` | %s | %s | %s | %s | %s | %s | %s |"
                f.Id
                (if f.OptIn then "opt-in" else "base run")
                (match f.Reason with
                 | Some r -> "`" + reasonToken r + "`"
                 | None -> "—")
                (cell f.Witness)
                (cell f.Discharges)
                (casesCell cases f.Id)
                ("`" + adequacyToken cases f.Id + "`")
                ("`" + refusalToken f.Id + "`")

        let rows = families |> List.sortBy (fun f -> f.Id) |> List.map row

        [ "# The law families this kit ships"
          ""
          "_GENERATED from `Fuaran.Core.Families`. Do not hand-edit — the suite compares this file"
          "against the roster and names the command that rewrites it:_"
          ""
          "```"
          "dotnet run --project tests/Fuaran.Core.Tests -- --emit-families"
          "```"
          ""
          "_The machine-readable half is `conformance-families.json` beside it; `STABILITY.md`"
          "documents its shape._"
          ""
          "A **law family** is a public entry point of this kit that answers with `LawResult list`."
          "Running one at your own witness is how a domain certifies it conforms; this table is the"
          "enumeration of what there is to run, so a family cannot be quietly absent from a"
          "conformance census that quantifies over it. The enumeration is held to reflection over"
          "the shipped assembly BY RETURN TYPE, so it cannot miss a family by how the family is"
          "named."
          ""
          "**Base run / opt-in.** The five `base run` families are the ones `Conformance.certify` and"
          "`Conformance.certifyStream` are built from — a domain gets them by calling an aggregate."
          "Every other family certifies a seam not every domain has, so a domain calls it"
          "deliberately, alongside its base run. Neither is a statement about importance: `reducer`"
          "is a base-run family only for stream-shaped domains, and `footprintLaws` is opt-in while"
          "discharging a ladder obligation."
          ""
          "**Witness.** The witness and generator types the entry point takes, in parameter order. A"
          "family with none runs against the kit's own fixtures and needs nothing but a seed."
          ""
          "**Discharges.** The claims-ladder obligations (`proofs.json` row ids) a green run of the"
          "family at your own witness discharges. Most families discharge none — they certify, they"
          "do not answer for an assumption this repository's proofs leave open."
          ""
          "**Cases.** What a RUN measured, not what the roster declares: the subject-law assertions"
          "the family made at this repository's own reference witness. `vacuous` — never a number —"
          "means the run certified nothing, either because it asserted nothing at all or because a"
          "guarded dimension was starved, and the starved dimension is named in the cell. `vacuous`"
          "is the state a family passing green while exercising nothing used to render as, which is"
          "what this column exists to make impossible to read past. `unmeasured` means no run was"
          "handed to the renderer, which is a different fact and deliberately a different word."
          ""
          "**Adequacy.** How the family's green run is to be read. `unconditional` — every iteration"
          "builds every branch the laws distinguish, so a green run is a pass. `guarded-reached` — the"
          "family carries an adequacy guard and this run reached every guarded side. `guarded-starved`"
          "— the guard went red: the run tested nothing on a side a law is about, and the family is"
          "RED in `certify`'s verdict rather than silently green. `guarded-unmeasured` — a guarded"
          "family no run was handed for."
          ""
          "**Refusal.** The refusable-family audit (Phase 220): where the family's refused outcomes"
          "come from. `none` — no law reads one. `built` — every refused case is constructed, so no"
          "run can miss it. `drawn-miss-is-red` — drawn, but a law demands the refused case, so a"
          "run that misses it fails. `drawn` — drawn, and a run that misses it stays green unless"
          "the family is guarded, which is what the `Adequacy` cell beside it answers."
          ""
          sprintf
              "%d families, across %s."
              (List.length families)
              (modules |> List.map (fun m -> "`" + m + "`") |> String.concat ", ")
          ""
          "| Family | Run by | Why opt-in | Witness | Discharges | Cases | Adequacy | Refusal |"
          "|---|---|---|---|---|---|---|---|" ]
        @ rows
        @ [ "" ]
        |> String.concat "\n"

    /// The roster rendered with no run behind it — every `cases` cell reads `unmeasured`. Kept
    /// beside the counted renderings rather than replaced by them, because a reader that wants the
    /// DECLARATION should not have to produce a run to get it.
    let toJson () : string = toJsonWith []

    /// The markdown half of the same, with no run behind it.
    let toMarkdown () : string = toMarkdownWith []
