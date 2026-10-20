namespace Fuaran.Core

// Phase 184 — the law-family ROSTER, exported by the kit as data; Phase 506 — one record per family.
//
// A census, the ladder's `dischargedBy`, the proof leg's operation coverage and a consumer's projection
// all quantify over this one declaration, which `ConformanceFamiliesTests` holds to reflection over
// RETURN TYPE. Every other reading of a family is a member of its record, so no second list can
// disagree with it. Why each row reads as it does: `docs/families-history.md`.

/// The kit's own enumeration of the law families it ships.
///
/// PUBLIC SURFACE. A host reads it to enumerate the families it must answer for; a projection
/// reads the generated JSON export to do the same offline. Adding a law family to the kit means
/// adding a record here in the same commit — the suite fails naming the family otherwise.
module Families =

    /// WHY a family is opt-in — the closed vocabulary, one case per reason the roster carries. A DU
    /// rather than a string because the set is closed and a reader dispatches on it.
    type OptInReason =
        /// The family demands a witness, generator or sink the BASE contract does not — an
        /// `ArtifactWitness`, an `IAttestationSink`, a `LaneGen`. A domain that has not built that
        /// capability cannot run it at all, so `certify` cannot fold it in.
        | NeedsWitnessCapability
        /// The family certifies a SEAM not every domain has — a columnar layer, a capability
        /// registry, a lease axis. It runs from the kit's own fixtures, or takes only a witness the
        /// base run already demands, so what makes it opt-in is relevance, never cost.
        | SeamNotEveryDomainHas
        /// The family takes exactly the base run's witness and asks for MORE than the base contract
        /// promises — footprint independence, concurrent apply, arbitration. A domain elects it.
        | StrongerPromise
        /// The family certifies no witness at all: it reads the kit's own knowledge of the Core it
        /// was compiled against (`witnessSurfaceLaws`), so a domain runs it deliberately, typically
        /// at a pin bump.
        | NoWitnessToCertify

    /// Where a family's REFUSAL population comes from — the set of refused / rejected / invalid
    /// outcomes at least one of its laws reads. What matters is whether a run can MISS it.
    type RefusalPopulation =
        /// No law reads a refused outcome.
        | NoRefusal
        /// Every refused case a law reads is BUILT — each iteration, or from a fixed fixture — so
        /// no run can miss it.
        | Built
        /// Part of the refused population is DRAWN, and a run that misses it goes RED: a law demands
        /// the refused case. Loud, so it needs no guard.
        | DrawnMissIsRed
        /// Part of the refused population is DRAWN, and a run that misses it stays GREEN, because
        /// the laws that read it hold trivially over an empty population. A family carrying it must
        /// be `Guarded`, or its green run can mean nothing on the side the law is about.
        | Drawn

    /// A family's refusal verdict: where its refusal population comes from, and the evidence for
    /// that verdict in prose a reader checks against the family's laws (never parsed).
    type RefusalVerdict =
        {
            /// Where the refused cases the family's laws read come from: none, built, or drawn.
            Population: RefusalPopulation
            /// The evidence for `Population` — which arms build their refused cases, which draw them.
            Why: string
        }

    /// One row of the refusable-family audit — a PROJECTION of a family's record
    /// (`LawFamily.RefusalAudit`), never a declaration.
    type RefusalAudit =
        {
            /// The audited family's id.
            Family: string
            /// The family's declared `Refusal.Population`; a `Drawn` row must be `Guarded`.
            Population: RefusalPopulation
            /// The family's declared `Refusal.Why`.
            Why: string
        }

    /// A witness, generator or sink type a domain supplies to run a family — the kit's own public
    /// type of that name, one case per type any family takes (Phase 506).
    [<RequireQualifiedAccess>]
    type Takes =
        /// The tree witness, `NodeWitness`.
        | NodeWitness
        /// The id witness, `IdWitness`.
        | IdWitness
        /// The op generator, `OpGen`.
        | OpGen
        /// The op-stream witness, `StreamWitness`.
        | StreamWitness
        /// The op-stream generator, `StreamGen`.
        | StreamGen
        /// The lane generator, `LaneGen`.
        | LaneGen
        /// The keyed-position witness, `KeyedWitness`.
        | KeyedWitness
        /// The cross-node reference witness, `RefWitness`.
        | RefWitness
        /// The artifact witness, `ArtifactWitness`.
        | ArtifactWitness
        /// The AI surface witness, `AiSurfaceWitness`.
        | AiSurfaceWitness
        /// The guarded AI surface witness, `GuardedSurfaceWitness`.
        | GuardedSurfaceWitness
        /// The evaluator witness, `EvaluatorWitness`.
        | EvaluatorWitness
        /// The observer witness, `ObserverWitness`.
        | ObserverWitness
        /// The projection witness, `ProjectionWitness`.
        | ProjectionWitness
        /// The capability seam witness, `CapabilitySeamWitness`.
        | CapabilitySeamWitness
        /// The capability pipeline witness, `CapabilityPipelineWitness`.
        | CapabilityPipelineWitness
        /// The query seam witness, `QuerySeamWitness`.
        | QuerySeamWitness
        /// The sanitisation floor witness, `SanitizeWitness`.
        | SanitizeWitness
        /// The attestation sink, `IAttestationSink`.
        | IAttestationSink
        /// The domain's own codec, `Codec` (Phase 507).
        | Codec

        /// The type's name, without its generic arity — the name a signature, the export's
        /// `witness` member and `STABILITY.md` use.
        member t.TypeName: string =
            match t with
            | NodeWitness -> "NodeWitness"
            | IdWitness -> "IdWitness"
            | OpGen -> "OpGen"
            | StreamWitness -> "StreamWitness"
            | StreamGen -> "StreamGen"
            | LaneGen -> "LaneGen"
            | KeyedWitness -> "KeyedWitness"
            | RefWitness -> "RefWitness"
            | ArtifactWitness -> "ArtifactWitness"
            | AiSurfaceWitness -> "AiSurfaceWitness"
            | GuardedSurfaceWitness -> "GuardedSurfaceWitness"
            | EvaluatorWitness -> "EvaluatorWitness"
            | ObserverWitness -> "ObserverWitness"
            | ProjectionWitness -> "ProjectionWitness"
            | CapabilitySeamWitness -> "CapabilitySeamWitness"
            | CapabilityPipelineWitness -> "CapabilityPipelineWitness"
            | QuerySeamWitness -> "QuerySeamWitness"
            | SanitizeWitness -> "SanitizeWitness"
            | IAttestationSink -> "IAttestationSink"
            | Codec -> "Codec"

    /// Who runs a family: an aggregate, or a domain that elects it for a stated reason.
    type Election =
        /// Folded into `Conformance.certify` or `Conformance.certifyStream`.
        | BaseRun
        /// Called deliberately, beside the base run, for this reason.
        | OptIn of OptInReason

    /// One law family: a public entry point of the kit that answers with `LawResult list`, declared
    /// once (Phase 506). Its other readings are members, so none can drift from the record.
    type LawFamily =
        {
            /// The roster key — `"<Module>.<Entry>"`, the one spelling a census cell, a ladder row's
            /// `dischargedBy` and a projection's roster use.
            Id: string
            /// The witness, generator and sink types a domain supplies, in parameter order. Empty for
            /// a family the kit runs against its own fixtures, which needs nothing but a seed.
            Takes: Takes list
            /// Who runs it: an aggregate (`BaseRun`), or a domain that elects it and why.
            Election: Election
            /// How the family answers "could this run's sample have missed a verdict the laws
            /// distinguish?" — `SampleAdequacy.census` is its projection.
            Adequacy: AdequacyClass
            /// Where the family's refused outcomes come from — `refusalAudit` is its projection.
            Refusal: RefusalVerdict
            /// The claims-ladder obligations (`proofs.json` row ids) a green run of the family at a
            /// domain's own witness discharges; the suite holds it equal to the ladder's `dischargedBy`.
            Discharges: string list
            /// The public Core operations whose contract the family's laws state, spelled
            /// `<Owner>.<member>` as `proofs/coverage-exclusions.json` spells them — the law-family leg
            /// of the proof leg's operation coverage. An operation the family merely CALLS as harness
            /// is not listed.
            Operations: string list
            /// Further entry points that run this family with one parameter pinned to its default,
            /// each spelled as a roster key (`<Module>.<Entry>`) — one family, one census cell, one
            /// reference run, several names (Phase 507). Each stays a public entry point answering
            /// `LawResult list`, so reflection finds it; the roster reads it as this family's.
            Spellings: string list
        }

        /// The kit module the entry point lives in — the id before its first `.`.
        member f.Module: string = f.Id.Substring(0, f.Id.IndexOf '.')

        /// The entry point's own name — the id after its first `.`.
        member f.Entry: string = f.Id.Substring(f.Id.IndexOf '.' + 1)

        /// The type names of `Takes`, in parameter order — what the export's `witness` member carries.
        member f.Witness: string list = f.Takes |> List.map _.TypeName

        /// `true` when a domain must call the family deliberately rather than through an aggregate.
        member f.OptIn: bool =
            match f.Election with
            | BaseRun -> false
            | OptIn _ -> true

        /// Why the family is opt-in, `None` for a base-run family.
        member f.Reason: OptInReason option =
            match f.Election with
            | BaseRun -> None
            | OptIn reason -> Some reason

        /// The family's refusable-family audit row.
        member f.RefusalAudit: RefusalAudit =
            { Family = f.Id
              Population = f.Refusal.Population
              Why = f.Refusal.Why }

    /// Every law family the kit ships, in declaration order. Renderings sort by `Id`, so the order
    /// here is for a reader's benefit and never reaches an artefact.
    let families: LawFamily list =
        // One row per family: `(id, takes, election, discharges)`, its adequacy class, its refusal
        // verdict, and its operations as ONE space-separated string (no operation name holds a
        // space). Each position has its own type, so a row cannot shift a field into its neighbour.
        // The pinned-default spellings (Phase 507), keyed by the family they spell: the bare or `…At`
        // entry point that IS the `…With` form with its parameter at the default. A `…With` that runs
        // another family and adds laws is a different law set and carries a stem of its own instead.
        let spellingsOf (id: string) : string list =
            match id with
            | "Conformance.reconcileLawsWith" -> [ "Conformance.reconcileLaws" ]
            | "Conformance.concurrencyLawsWith" -> [ "Conformance.concurrencyLaws" ]
            | "Conformance.snapshotLawsWith" -> [ "Conformance.snapshotLaws" ]
            | "Conformance.keyedArbitrationLawsWith" -> [ "Conformance.keyedArbitrationLawsAt" ]
            | "Conformance.columnOwnershipLawsWith" -> [ "Conformance.columnOwnershipLaws" ]
            | "FoldConfluence.laneFoldLawsWith" -> [ "FoldConfluence.laneFoldLawsAt" ]
            | _ -> []

        let row ((id, takes, election, discharges), adequacy, refusal, operations: string) =
            { Id = id
              Takes = takes
              Election = election
              Adequacy = adequacy
              Refusal = refusal
              Discharges = discharges
              Operations = operations.Split ' ' |> Array.filter (fun s -> s <> "") |> List.ofArray
              Spellings = spellingsOf id }

        let verdict population why = { Population = population; Why = why }
        let none = verdict NoRefusal
        let built = verdict Built
        let drawnMissIsRed = verdict DrawnMissIsRed
        let drawn = verdict Drawn
        let capability = OptIn NeedsWitnessCapability
        let seam = OptIn SeamNotEveryDomainHas
        let stronger = OptIn StrongerPromise
        let noWitness = OptIn NoWitnessToCertify
        let tree = [ Takes.NodeWitness; Takes.IdWitness; Takes.OpGen ]
        let stream = [ Takes.StreamWitness; Takes.StreamGen ]

        [ ("Conformance.witnessLaws", tree, BaseRun, [ "lawful-abstract-witness" ]),
          Unconditional "each iteration rebuilds a drawn node and re-reads every accessor",
          none "accessor round-trips only; no apply, no refusal path",
          ""
          ("Conformance.opAlgebra", tree, BaseRun, [ "tree-algebra-well-formed-states" ]),
          Guarded [ "accepted"; "refused" ],
          drawn
              "canApply ≡ apply and totality read both outcomes; genOp DRAWS refusals, and the collision arm BUILDS them only where the witness can carry a multi-node subtree",
          "Ops.apply Ops.canApply Ops.invert"
          ("Conformance.diffLaws", tree, BaseRun, []),
          Guarded [ "non-identity pair" ],
          none "a refused op is skipped while building `after`; no law reads it",
          "Diff.toOps Ops.applyAll Ops.canApplyAll"
          ("Conformance.streamLaws", stream, BaseRun, [ "op-stream-load-premises" ]),
          Guarded [ "accepted"; "tampered chain" ],
          drawn
              "the tampered chain it must reject is built only over a chain the caller's StreamGen fills, and a refused append is skipped; guarded on accepted op and tampered chain (Phase 245)",
          "OpStream.empty OpStream.append OpStream.replay OpStream.verifyChain OpStream.toJsonl OpStream.fromJsonl OpStream.tryToJsonl"
          ("Conformance.reducer", [ Takes.StreamGen ], BaseRun, []),
          Guarded [ "accepted"; "refused" ],
          drawn
              "totality (a refusal is typed, not thrown) and the envelope law read refusals that only the caller's StreamGen draws",
          ""
          ("Conformance.diffContainedLaws", tree, stronger, []),
          Guarded [ "accepted"; "refused" ],
          drawn
              "the refusal IFF reads the drawn pair under the witness's canHold, and a canHold that refuses nothing holds it trivially; the graft probe beside it is built",
          "Diff.toOps Ops.applyAll Ops.canApplyAll"
          ("Conformance.normalizeLaws", tree, stronger, []),
          Guarded [ "non-identity script" ],
          none "a refused op is skipped; no law reads it",
          "Ops.normalize"
          ("Conformance.containerLaws", tree, stronger, []),
          Guarded [ "built arm (child perturbation / invariant probe / interior graft)" ],
          built
              "the interior-offender graft is built and must be refused NotAContainer; its guard covers the built arms",
          "Ops.apply Ops.canApply"
          ("Conformance.mergeConflictLaws", tree, stronger, []),
          Guarded [ "op-pair interference" ],
          none "conflicts is a report list, never a refused outcome",
          "Dag.conflicts"
          ("Conformance.reconcileLawsWith", tree, stronger, []),
          Guarded [ "reconcile outcome"; "reconcile shape"; "delta-pair independence" ],
          drawn "a reconcile Error arises from OpGen-drawn scripts; guarded on reconcile outcome",
          "Dag.reconcile Dag.mergeBase Dag.tryReplayTo"
          ("Conformance.footprintLaws", tree, stronger, []),
          Guarded [ "script-pair independence"; "encode-distinguished node" ],
          none "a refused op is skipped; no law reads it",
          "Ops.footprint Ops.independent"
          ("Conformance.footprintLawsAt", stream, stronger, [ "independence-diamond" ]),
          Guarded [ "script-pair independence" ],
          none
              "a refused draw is skipped while threading and counted beside the guard; an Apply error on an independent pair only fails the soundness law",
          "Ops.interference Dag.conflicts Footprint.slotNodes"
          ("Conformance.footprintInLawsAt", stream, stronger, []),
          Guarded [ "script-pair independence and base-aware precision" ],
          none
              "a refused draw is skipped while threading and counted beside the guard; an Apply error on an independent pair only fails the soundness law",
          "Ops.footprintIn Dag.conflictsIn Dag.reconcileIn"
          ("Conformance.concurrencyLawsWith", tree, stronger, []),
          Guarded [ "independent pair (its own Phase 80 vacuity guard)" ],
          none "a drawn refusal is skipped; an applyAll Error only fails a law",
          "Ops.applyAll Ops.independent"
          ("Conformance.arbitrationLaws", tree, stronger, []),
          Guarded [ "arbitration bucket" ],
          drawn
              "Inapplicable comes from the kit's corruption roll and Conflicts from drawn scripts; guarded on arbitration bucket",
          "Arbitration.arbitrate Arbitration.arbitrateWith Arbitration.duplicateIds"
          ("Conformance.snapshotLawsWith", stream, stronger, []),
          Unconditional
              "each iteration takes a snapshot and replays across it; a run whose chains are too short to cut one reds both laws as never reached",
          none "a rejected append is skipped; a compact Error only fails a law",
          "OpStream.Snapshots.compact OpStream.Snapshots.replayFrom OpStream.Snapshots.verify OpStream.appendWith"
          ("Conformance.streamConfigLaws", stream, stronger, []),
          Unconditional
              "each iteration builds a configured chain and journal and checks them under their own and perturbed parameters; a run that never chains an op reds the read law as never reached",
          built
              "every refused verification (another genesis, payload or hash, a tampered record, a stale head) is built from the iteration's own chain; a domain-refused append only ends the fold",
          "OpStream.appendIfWith OpStream.appendManyWith OpStream.captureEffectWith OpStream.captureHeadWith OpStream.firstCaptureBreakWith OpStream.headWith OpStream.verifyCapturesAtWith OpStream.verifyChainWith"
          ("Conformance.dagLaws", stream, stronger, []),
          Unconditional
              "each iteration builds, replays and round-trips one DAG, and tampers it whenever a fresh draw differs; a run that never tampers reds the tamper law as never reached",
          drawnMissIsRed
              "the tampered node it must reject is built only when a fresh draw differs from the op it replaces; the tamper law is a strict runner cell, so a run that never tampers reds it as never reached (Phase 297)",
          "Dag.empty Dag.append Dag.appendChecked Dag.appendVerified Dag.merge Dag.mergeChecked Dag.mergeVerified Dag.toJsonl Dag.fromJsonl Dag.verifyDag Dag.tryReplayTo"
          ("Conformance.reachLaws", stream, stronger, []),
          Guarded [ "DAG shape" ],
          drawn
              "the unknown head and the cyclic load are built every iteration, but a replay rejection and a reconcile refusal come from the caller's drawn ops and their agreement laws hold when none is drawn; the refusals are counted beside the DAG-shape guard, not demanded, since a domain whose ops never reject cannot produce one",
          "Dag.Reach.ofDag Dag.Reach.ancestors Dag.Reach.reaches Dag.Reach.tryTopoOrder Dag.Reach.mergeBase Dag.Reach.between Dag.Reach.dag Dag.appendIndexed Dag.mergeIndexed Dag.reconcileManyWith Dag.tryReplayToWith"
          ("Conformance.checkpointLaws", stream, stronger, []),
          Guarded [ "DAG shape" ],
          drawn
              "the uncovered refusal comes from the kit's drawn shape and is demanded by the DAG-shape guard, and a refused compaction is counted beside it; a replay rejection comes from the caller's drawn ops, and its agreement law holds when none is drawn, so it is counted beside the guard, not demanded",
          "Dag.checkpointAt Dag.compactAt Dag.compactFrom Dag.replayFrom Dag.replayFromWith Dag.verifyCheckpoint Dag.verifyDagFrom Dag.toJsonlWithCheckpoints"
          ("Conformance.laneLaws", stream, stronger, []),
          Guarded [ "lane shape" ],
          drawn
              "the lane fork and the cross-lane content collision are built from a replacement op the caller's generator must draw distinct, and are demanded by the lane-shape guard; the refusals the store's own draws produce (a collision skipping a step or a fold) are counted beside them, not demanded",
          "Dag.loadLanes Dag.verifyLanes Dag.laneCollisions Dag.appendOnLane Dag.lanesToJsonl Dag.totalOrderBy Dag.totalOrder Dag.replayAll Dag.appendOn Dag.mergeAll Dag.commonBase Dag.Reach.commonBase Dag.rehashWith Dag.prunable"
          ("Conformance.casLaws", stream, stronger, []),
          Guarded [ "accepted"; "refused"; "race arm" ],
          drawn
              "match ≡ append compares a domain refusal with a CAS Domain rejection only when StreamGen draws one; the stale-head rejection beside it is built",
          "OpStream.appendIf"
          ("Conformance.idempotencyLaws", stream, stronger, []),
          Guarded [ "accepted"; "refused" ],
          drawn
              "fresh-key ≡ append and the CAS arm compare domain refusals only when StreamGen draws one; Duplicate and StaleHead are built",
          "OpStream.appendIdempotent OpStream.appendIdempotentIf KeyIndex.add KeyIndex.ofStream KeyIndex.tryFind"
          ("Conformance.hashFnLaws", stream, stronger, []),
          Guarded [ "tamper arm (reorder / drop / bit-flip / op-tamper / re-mint / distinguish)" ],
          drawn
              "every tamper is built and must fail verifyChain (or move the head), but only over a drawn chain long enough to perturb and a replacement op that encodes differently; guarded on tamper arm (Phases 297, 302)",
          "OpStream.append OpStream.verifyChain OpStream.head"
          ("Conformance.attributedLaws", stream, stronger, []),
          Guarded [ "tampered chain (re-attributed)" ],
          drawn
              "a re-attributed op is built and must fail verifyChain, but only over a non-empty drawn stream; guarded on tampered chain (Phase 297)",
          "OpStream.Attributed.liftWitness"
          ("Conformance.codecInjectivityLaws", stream, stronger, []),
          Unconditional
              "the left-inverse law is BUILT by every iteration — one drawn op round-tripped through the domain's own Decode, and a codec with a total left inverse is injective — so the family's weight does not rest on the collision search beside it, whose own third law fails when the draw was too narrow to compare anything",
          none "a Decode refusal only fails a law",
          ""
          ("Conformance.noAttestationVacuityLaws", stream, stronger, []),
          Unconditional "each iteration asks the no-op sink to sign and to verify",
          built "a plausible attestation is built and the no-op sink must reject it",
          "OpStream.verifyAttestation"
          ("Conformance.attestationLawsAt", stream @ [ Takes.IAttestationSink ], capability, []),
          Guarded [ "signing outcome and op tamper" ],
          built "op and actor forgeries are built each iteration; its guard is on the signing outcome",
          "OpStream.attestHead OpStream.verifyAttestation"
          ("Conformance.compositionLawsAt", [ Takes.ArtifactWitness ], capability, []),
          Unconditional "each iteration composes a drawn pair and compares against the nested application",
          none "a compose Error only fails a law or is compared opaquely",
          "Function.composeAcross Function.composedEffectAcross Function.curry Function.apply Effect.join Effect.covers"
          ("Conformance.compositionPilotAt", [ Takes.ArtifactWitness ], capability, []),
          Unconditional
              "it runs `compositionLaws` (unconditional above) and BUILDS both applyMemo arms across the witness boundary each iteration — a closed inner sub-function memoised, and the composed outer compared against direct apply",
          none "as compositionLaws; a memo Error only fails a law",
          ""
          ("Conformance.memoLawsAt", [ Takes.ArtifactWitness ], capability, []),
          Unconditional "each iteration forces a miss then a hit, and an effecting bypass, by construction",
          none "every Error arm only fails a law",
          "Function.applyMemo Memo.empty"
          ("Conformance.memoSoundnessLawsAt", [ Takes.ArtifactWitness ], capability, []),
          Unconditional "each iteration applies the caller-supplied under-declared function twice",
          none "an Error only fails a law; the cache bypass is not a refusal",
          "Memo.isMemoisable Function.observedEffect Function.applyMemo"
          ("Conformance.functionVerifyLawsAt", [ Takes.ArtifactWitness ], capability, []),
          Unconditional "each iteration verifies a SOUND and a BROKEN function, both caller-supplied",
          drawnMissIsRed
              "the broken function is caught only when genParams reaches its bad sub-space, and a broken function that verifies clean is itself a red law",
          "Conformance.verifyFunction Conformance.verifyFunctionSymbolic Function.auditEffect"
          ("Conformance.verifyHonestyLawsAt", [ Takes.ArtifactWitness ], capability, []),
          Unconditional "each iteration verifies a stochastic and an under-declared function, both caller-supplied",
          drawnMissIsRed
              "the broken verdicts depend on genParams, and a broken function verifying under any axis is a red law",
          "Conformance.verifyFunction Conformance.renderCounterexample"
          ("Conformance.encoderInjectivityLawsAt", [ Takes.ArtifactWitness ], capability, []),
          Guarded [ "distinct tree (seen / compared)" ],
          none "no refused outcome is read",
          ""
          ("Conformance.projectionLawsAt", [ Takes.ProjectionWitness ], capability, []),
          Unconditional "each iteration projects, re-imports and scopes the same tree",
          none "a re-import Error only fails a law",
          "Projection.project Projection.render Projection.parseBack Projection.renderLine Projection.lineText Projection.sizeOf Projection.snapshot Projection.idKey"
          ("Conformance.observerLawsAt", [ Takes.ObserverWitness ], capability, []),
          Unconditional "each iteration drives a drawn registration script and a parent ring",
          none "no refused outcome is read",
          "ObserverWitness.empty ObserverWitness.register ObserverWitness.unregister ObserverWitness.update ObserverWitness.derive ObserverWitness.snapshot ObserverWitness.observeTree"
          ("Conformance.aiSurfaceLawsAt", [ Takes.AiSurfaceWitness ], capability, []),
          Guarded [ "accepted"; "refused"; "allowed"; "parked"; "denied" ],
          drawn
              "explainRejection and the allowed-submit parity read a reducer rejection only when the caller's op generator draws one, and since Phase 246 the deny and park arms are reached only when the domain's own Decide chooses them; unknown tool and unknown id are built",
          "AiSurface.runTool PatternBank.resolve PatternBank.literalSegments Proposals.submit Proposals.approve Proposals.reject Proposals.explainRejection Proposals.Queue.empty PolicyDecision.deny"
          ("Conformance.aiSurfaceKitPolicyLawsAt", [ Takes.AiSurfaceWitness ], capability, []),
          Guarded [ "accepted"; "refused" ],
          drawn
              "explainRejection and the allowed-submit parity read a reducer rejection only when the caller's op generator draws one; the kit rolls the decision, and unknown tool and unknown id are built",
          ""
          ("Conformance.capabilityLawsAt", [ Takes.CapabilitySeamWitness ], capability, []),
          Guarded [ "settled"; "pending"; "refused" ],
          drawn
              "every refusal the three laws read is a call the domain's generator draws; guarded on settled, pending and refused",
          "CapabilityRegistry.tryFind CapabilityRegistry.dispatch CapabilityRegistry.enumerate Capability.validateArgs"
          ("Conformance.queryLawsAt", [ Takes.QuerySeamWitness ], capability, []),
          Guarded [ "settled"; "pending"; "refused" ],
          drawn
              "every refusal the three laws read is a call the domain's generator draws; guarded on settled, pending and refused",
          "QueryRegistry.enumerate QueryRegistry.tryFind Query.validateParams"
          ("Conformance.capabilityPipelineLawsAt", [ Takes.CapabilityPipelineWitness ], capability, []),
          Guarded [ "invoke node" ],
          built
              "the unregistered-capability and undeclared-argument pipelines are built from every drawn Invoke node; guarded on invoke node",
          ""
          ("Conformance.keyedChildrenLawsAt", Takes.KeyedWitness :: tree, capability, []),
          Guarded [ "built arm (clean full walk / keyed id in the surface / one id in two keyed positions)" ],
          built
              "the keyed-and-surface and double-keyed collisions are built through PlaceKeyedChild; its guard covers the built arms",
          "Ops.apply Tree.keyedIds"
          ("Conformance.placementLaws", tree, stronger, []),
          Guarded [ "placement arm (place / move across parents / move within a parent / non-final position)" ],
          built
              "the unknown anchor, the out-of-range index and the three engine refusals (absent parent, duplicate id, move of the root) are built every iteration a holder is drawn",
          "TreePlacement.place TreePlacement.move"
          ("Conformance.digestLaws", tree, stronger, []),
          Guarded [ "pair shape" ],
          none "no refused outcome is read; a drawn pair that is refused only fails a law",
          "Tree.digests Tree.ownDigest Tree.frameDigest Tree.Digests.diff Tree.Digests.subtreeEqual Diff.changes"
          ("Conformance.introducedLaws", tree, seam, []),
          Guarded [ "gate arm" ],
          none "the gate's block is a verdict over findings, not a refused outcome; no law reads a refusal",
          "Validator.introduced Validator.introducedDefects Validator.verdict Validator.gate Validator.encodeVerdict"
          ("Conformance.containmentLaws", tree, seam, []),
          Guarded [ "grammar refusal" ],
          drawn
              "IllegalChild is reached by drawn ops and by a graft built from the drawn tree's first illegal pair; a grammar the generator never violates reaches neither, which the grammar-refusal guard reports",
          "Ops.apply Ops.canApply Ops.applyAll Ops.canApplyAll Ops.illegalChildren Diff.toOps Arbitration.arbitrate Validator.containment"
          ("Conformance.referenceLawsAt", Takes.RefWitness :: tree, capability, []),
          Guarded [ "reference arm" ],
          drawn
              "StillReferenced is reached only when a drawn remove destroys a declaration a survivor references, and the race only when the tree holds a resolved reference; the reference-arm guard counts both",
          "Ops.apply Ops.footprint Validator.referenceIntegrity Validator.referenceOrder Validator.referenceDefects Validator.forwardReferences"
          ("Conformance.loweringLaws", tree, stronger, []),
          Guarded [ "lowered tree (well-formed / in the containment invariant / filled shell / built repeated id)" ],
          built
              "the repeated-id tree whose script must be refused DuplicateId is built by appending a copy of a drawn node under a holder; its guard counts it",
          "Ops.lower Ops.shellOf Ops.skeletonRoot"
          ("Conformance.freshIdLaws", tree, stronger, []),
          Guarded [ "built arm (repeated id built / clone placed)" ],
          none
              "no law reads a refused outcome: a minted id, a repaired tree and an accepted clone are the subjects, and a refused clone fails the clone law outright",
          "FreshIds.repairDuplicates TreePlacement.clone"
          ("Conformance.keyedApplyLawsAt", Takes.KeyedWitness :: tree, capability, [ "witness-surface-scope" ]),
          Guarded
              [ "built arm (keyed holder / clean insert / keyed-vs-structural collision in either direction / keyed-vs-keyed collision / accepted op over the keyed walk) and op kind" ],
          built
              "the three keyed collisions are built through PlaceKeyedChild and must be refused DuplicateId; its guard covers the built arms",
          "Ops.apply Ops.canApply Tree.wellFormedKeyed Tree.idsKeyed Tree.keyedIds Tree.traversal"
          ("Conformance.keyedDiffLawsAt", Takes.KeyedWitness :: tree, capability, []),
          Guarded [ "built arm (change below a keyed position / relocation across a keyed boundary)" ],
          built
              "the relocation across a keyed boundary is built through PlaceKeyedChild and must be refused KeyedPosition; its guard covers the built arms",
          "Diff.toOpsWith Ops.applyAll Tree.traversal"
          ("Conformance.keyedArbitrationLawsWith", Takes.KeyedWitness :: tree, capability, []),
          Guarded [ "arbitration bucket, built arm (container-illegal proposal / keyed-id clash) and op kind" ],
          drawn
              "the container-illegal proposal and the keyed-id clash are built and must be refused; Inapplicable also comes from the kit's corruption roll and Conflicts from drawn scripts, so the guard covers the arbitration bucket",
          "Ops.footprint Ops.canApplyAll"
          ("Conformance.propagationEvaluatorLawsAt",
           [ Takes.EvaluatorWitness ],
           capability,
           [ "propagation-change-set-and-prior" ]),
          Guarded [ "evaluator edit" ],
          drawn "the failing-evaluator arm comes from the domain's own edits; guarded on evaluator edit",
          ""
          ("Conformance.propagationPriorLawsAt", [ Takes.EvaluatorWitness ], capability, [ "propagation-prior-blind" ]),
          Guarded
              [ "evaluator edit (delegates to propagationEvaluatorLawsAt)"
                "prior-aware edit" ],
          drawn
              "runs propagationEvaluatorLawsAt first, so its failing-evaluator arm is drawn the same way; the prior-aware arms are guarded on prior-aware edit",
          "Propagation.evalWith Propagation.evalFromWith Propagation.sort"
          ("Conformance.propagationEvaluatorLawsWith", [ Takes.EvaluatorWitness ], capability, []),
          Guarded
              [ "evaluator edit (delegates to propagationEvaluatorLawsAt)"
                "prior-aware edit" ],
          drawn "the obsolete forward of propagationPriorLawsAt (Phase 507); the same laws, labelled with its own id",
          ""
          ("Conformance.captureReplayLaws", [], seam, []),
          Guarded [ "tampered" ],
          drawn
              "the tampered capture is built only when a fresh draw encodes differently from the value it replaces (guarded on tampered, Phase 297); the misordered replay is built",
          "OpStream.captureEffect OpStream.replayEffect OpStream.verifyCaptures OpStream.captureToJsonl OpStream.captureFromJsonl OpStream.tryCaptureToJsonl"
          ("Conformance.constructThenEncodeLaws", [], seam, []),
          Unconditional
              "every corpus document is decoded, rebuilt through the authoring surface and re-encoded on every run — the sample is the caller's own corpus rather than a draw, and an empty one fails the family's own non-vacuity law instead of passing quietly",
          none "Reject corpus cases are filtered out; a Construct Error only fails a law",
          "Corpus.roundTrip Corpus.runCorpus"
          ("Conformance.hashFnAdversarialLaws", [], seam, []),
          Unconditional "the budget IS the sample size, and it is the caller's own declared parameter",
          none "a collision search; no refused outcome",
          "OpStream.defaultHash"
          ("Conformance.capabilityLaws", [], seam, []),
          Unconditional "each iteration exercises accept, reject and unknown-arg on a built declaration",
          built "out-of-space arg, unknown arg and unregistered id are built each iteration",
          "Capability.create Capability.invoke Capability.invocationKey Capability.determinismTag Capability.validateArgs CapabilityCodec.encode CapabilityCodec.decode CapabilityRegistry.register CapabilityRegistry.dispatch CapabilityRegistry.enumerate"
          ("Conformance.queryLaws", [], seam, []),
          Unconditional "each iteration exercises accept, type-mismatch and unknown-param on a built declaration",
          built
              "type mismatch, unknown param, NoSuchQuery and ExecutionFailed are built, and so is a declaration naming a parameter twice and a typed resolver fault on every iteration's first page",
          "Query.invocationKey Query.determinismTag Query.validateParams QueryCodec.encode QueryCodec.decode QueryCodec.encodeResult QueryCodec.decodeResult QueryCodec.encodeDeferredResult QueryCodec.decodeDeferredResult QueryRegistry.register QueryRegistry.dispatch QueryRegistry.dispatchWithArgs QueryRegistry.enumerate Query.invokePage Query.invocationKeyPage QueryRegistry.dispatchPage QueryRegistry.dispatchPageWith QueryRegistry.dispatchCapturedWith QueryRegistry.dispatchPageCapturedWith QueryRegistry.dispatchReplayedWith"
          ("Conformance.registryLaws", [], seam, []),
          Unconditional
              "each iteration queries matching and non-matching signatures on a built registry, and runs the four lifecycle verbs on all four registries",
          built
              "an unregistered id and an out-of-space arg are built, and so are an unheld id, an ill-formed replacement and a repeated parameter",
          "FunctionRegistry.register FunctionRegistry.dispatch FunctionRegistry.entry FunctionRegistry.findBySignature FunctionRegistry.narrow FunctionRegistry.ids FunctionRegistry.unregister FunctionRegistry.replace FunctionRegistry.restrict FunctionRegistry.union CapabilityRegistry.unregister CapabilityRegistry.replace CapabilityRegistry.restrict CapabilityRegistry.union QueryRegistry.unregister QueryRegistry.replace QueryRegistry.restrict QueryRegistry.union Validator.unregister Validator.replace Validator.restrict Validator.union"
          ("Conformance.policyLaws", [], seam, []),
          Unconditional
              "each iteration runs every decision — allow, park and two denials — through a gate on all three registries, and a drawn triple through the lattice",
          built "a denial, a park and an out-of-space argument are built through all three registries each iteration",
          "PolicyDecision.rank PolicyDecision.join PolicyDecision.meet PolicyDecision.all PolicyDecision.any PolicyDecision.guidance RegistryPolicy.none RegistryPolicy.withGate RegistryPolicy.onDenied RegistryPolicy.combine RegistryPolicy.gates RegistryPolicy.decide RegistryPolicy.decideNamed RegistryPolicy.Equals RegistryPolicy.GetHashCode CapabilityRegistry.withGate CapabilityRegistry.onDenied CapabilityRegistry.decide FunctionRegistry.withGate FunctionRegistry.onDenied FunctionRegistry.decide QueryRegistry.withGate QueryRegistry.onDenied QueryRegistry.decide"
          ("Conformance.policyLawsAt", [ Takes.GuardedSurfaceWitness ], capability, []),
          Guarded [ "unprivileged actor"; "host-writing op"; "refused dry run" ],
          drawn
              "a host-writing op, an unprivileged actor and a sequence whose dry run refuses are reached only when the domain's generator and actor list draw them",
          "Proposals.decideGuarded Proposals.submitGuarded Proposals.approveGuarded"
          ("Conformance.writeGateLaws", tree, seam, []),
          Guarded [ "applied op"; "denied op" ],
          drawn
              "the cover law reads an applied op and the subtree law a denied one, and both are drawn: the op from the domain's generator, the lock from its tree",
          "WriteGate.lockOnly WriteGate.allowOnly WriteGate.targetsOf WriteGate.decide WriteGate.applyGated WriteGate.guidance"
          ("Conformance.keyedCaptureLaws", [], seam, []),
          Unconditional
              "each iteration records a drawn session, replays it shuffled across keys, tampers with it and dispatches through the seam",
          built "NoCapture, Exhausted, NotAttempted and a policy refusal at the seam are built each iteration",
          "OpStream.beginEffectKeyed OpStream.beginEffectKeyedWith OpStream.settleEffectKeyed OpStream.settleEffectKeyedWith OpStream.captureEffectKeyed OpStream.captureEffectKeyedWith OpStream.replayEffectKeyed OpStream.firstKeyedCaptureBreak OpStream.firstKeyedCaptureBreakWith OpStream.verifyKeyedCaptures OpStream.verifyKeyedCapturesWith CapabilityRegistry.dispatchCaptured CapabilityRegistry.dispatchReplayed QueryRegistry.dispatchCaptured QueryRegistry.dispatchPageCaptured QueryRegistry.dispatchReplayed"
          ("Conformance.packLoadingLaws", [], seam, []),
          Unconditional "each iteration loads a pack and refuses a stale pin and an unknown base",
          built "a stale-version pack and an unknown base are built",
          "ContentPack.load ContentPack.pack ContentPack.signatureFingerprint ContentPack.unload FunctionRegistry.findBySignature"
          ("Conformance.aggregateNullSkipLaws", [], seam, []),
          Guarded [ "int cell"; "float cell"; "decimal cell" ],
          none "an aggregate Error only fails the law, and no Count or Sum over the kit's drawn columns can raise one",
          "Column.aggregate Cell.isNull"
          ("Conformance.columnarValidatorLaws", [], seam, []),
          Guarded [ "null cell"; "out-of-range cell"; "int cell"; "decimal cell" ],
          drawn
              "null and out-of-range faults are injected by the kit's own roll, and a fault-free draw satisfies the count laws trivially",
          "ColumnValidator.inRange ColumnValidator.notNull ColumnValidator.ofRules ColumnValidator.ofType ColumnValidator.unique ColumnValidator.validate Validator.canonicalCodes"
          ("Conformance.columnVectorLaws", [], seam, []),
          Unconditional
              "each iteration builds a NaN pair, a signed-zero pair and a pair differing only under an absent row, beside the drawn pair",
          none "equality and the reads answer values; no refusal path exists to reach",
          "Cell.compare ColumnData.Equals ColumnData.GetHashCode Vector.Equals Vector.GetHashCode Column.cell Column.isPresent Column.length Column.validity Column.mask Column.ofCells Column.toCells Column.ofInts Column.ofFloats Column.ofBools Column.ofStrs Column.ofDates Column.ofTimestamps Column.ofDecimals Column.tryInts Column.tryFloats Column.tryBools Column.tryStrs Column.tryDates Column.tryTimestamps Column.tryDecimals Validity.ofVector Validity.ofArray Validity.ofList Validity.isPresent Validity.toMask Validity.presentCount Vector.empty Vector.length Vector.isEmpty Vector.item Vector.tryItem Vector.ofArray Vector.adopt Vector.ofList Vector.ofSeq Vector.init Vector.toArray Vector.toList Vector.iter Vector.iteri Vector.fold Vector.map Vector.mapi Vector.exists Vector.tryFindIndex Vector.slice Vector.Unsafe.borrow"
          ("Conformance.columnOwnershipLawsWith", [], seam, []),
          Unconditional
              "one assertion per column the caller's draw builds, over every element; a draw that builds none takes no evidence and reds as never reached (the default draw builds a column of every type and two columns sharing one backing array and one mask array)",
          none "the law compares digests taken before and after the operation; no refusal path exists to reach",
          "Vector.adopt Vector.slice Vector.Unsafe.borrow"
          ("Conformance.deferredLaws", [], seam, []),
          Unconditional "each iteration round-trips Pending, Ready and Failed",
          built "the fixed Failed case must yield Error",
          "Deferred.map Deferred.toResult CapabilityCodec.encodeDeferred CapabilityCodec.decodeDeferred"
          ("Conformance.capabilityPipelineLaws", [], seam, []),
          Unconditional "each iteration type-checks a well-typed and an ill-typed edge",
          built "the fixed ill-typed pipeline must refuse EdgeTypeMismatch",
          "CapabilityPipeline.typeCheck CapabilityPipeline.eval CapabilityPipeline.encode CapabilityPipeline.decode CapabilityPipeline.nodeInvocationKey CapabilityPipeline.nodeId CapabilityLookup.ofRegistry"
          ("Conformance.capabilityPipelineIncrementalLaws", [], seam, []),
          Guarded [ "node reuse" ],
          none "an eval Error only records a failure",
          "CapabilityPipeline.dirtySet CapabilityPipeline.evalFrom CapabilityPipeline.eval"
          ("Conformance.dirtyPropagationLaws", [], seam, []),
          Guarded [ "dirty frontier" ],
          none "no refused outcome is read",
          "Propagation.dirtyFromChangedIds Propagation.sort Propagation.cycleThrough"
          ("Conformance.propagationEvalLaws", [], seam, []),
          Guarded [ "node reuse"; "undeclared read" ],
          built "the unknown change and the leaky evaluator are built each iteration",
          "Propagation.eval Propagation.evalFrom Propagation.dirtyFromChangedIds"
          ("Conformance.canonicalFloatLaws", [], seam, []),
          Unconditional
              "each iteration BUILDS one float in each of four strata — a small-magnitude spread, the whole normal range, the subnormals and the integral range past 2^53 — and every run asserts the edges (the largest finite, the smallest subnormal, the neighbours of 2^53) and the three non-finite tokens (Phase 253)",
          none "no refused outcome is read",
          "Canon.canonicalFloat FloatLayout.finite"
          ("Conformance.chainBreakReasonLaws", [], seam, []),
          Unconditional
              "each iteration BUILDS all three break kinds on both walkers — a renumbered sequence, a repointed prev-link, and a payload tampered with its sequence and link left intact — rather than drawing them, and the family's own last two laws fail if any kind was not actually observed",
          built "all three break kinds are built each iteration",
          "ChainBreakReason.ofString ChainBreakReason.toString OpStream.firstChainBreakWith OpStream.firstCaptureBreak"
          ("Conformance.dagBreakReasonLaws", [], seam, []),
          Unconditional
              "each iteration BUILDS both break kinds on the DAG walk — a node whose op is tampered with its map key left alone, and a named parent deleted — rather than drawing them, and the family's own last law fails if either kind was not actually observed",
          built "both break kinds are built each iteration",
          "DagBreakReason.ofString DagBreakReason.toString Dag.firstBreak"
          ("Conformance.witnessSurfaceLaws", [], noWitness, []),
          Unconditional
              "every run reads the field set of every frozen witness record, and every public record the kit's assemblies export, by reflection — nothing is drawn, so the one run is the whole sample",
          none "reads record field sets by reflection; no op is applied and no refused outcome exists",
          ""
          ("Conformance.sanitizeLawsAt", [ Takes.SanitizeWitness ], capability, []),
          Unconditional
              "every iteration scrubs drawn splices and benign text; the first also runs every pinned URL, markdown and attribute vector",
          built
              "the refused URLs, the rejected attribute keys and values and the dangerous markdown are fixed vectors the first iteration always runs",
          "Idl.Sanitize.isAllowedAttributeKey Idl.Sanitize.isSafeAttributeValue Idl.Sanitize.sanitizeAttributes Idl.Sanitize.sanitizeUrl Idl.Sanitize.sanitizeUrlOrBlank Idl.Sanitize.scrubMarkdown"
          ("Conformance.codecLawsAt",
           [ Takes.Codec ],
           capability,
           [ "decode-layer-refusal-premises"; "decode-layer-round-trip-premises" ]),
          Guarded [ "mutation outcome"; "distinct written values" ],
          drawn
              "the refusal law reads the codec's refusals of mutations built from each drawn value, and a codec that refuses none holds it over nothing; guarded on mutation outcome",
          ""
          ("Conformance.codecLaws", [ Takes.Codec ], capability, []),
          Guarded [ "mutation outcome"; "distinct written values" ],
          drawn "the obsolete forward of codecLawsAt (Phase 507); the same laws, labelled with its own id",
          ""
          ("StringEscapeVectors.laws", [], seam, []),
          Unconditional
              "a fixed vector corpus: every run evaluates every character and actor vector and the rendered table, so there is no sample that could miss one",
          none "every vector is an accepted spelling; no refused outcome is read",
          "StringEscapeVectors.lines"
          ("EncodingProfileVectors.laws", [], seam, []),
          Unconditional
              "a fixed vector corpus: every run evaluates every value, actor and capture vector under both profiles and the rendered table, so there is no sample that could miss one",
          none "every vector is an accepted rendering; no refused outcome is read",
          "Json.renderWith Json.escapeWith EncodingProfile.name EncodingProfile.tryParse OpStream.profileName OpStream.tryProfile OpStream.encodeActorWith OpStream.configFor Dag.nodeIdWith EncodingProfileVectors.lines Canonical.write Canonical.tryWrite Canonical.isCanonical"
          ("EncodingProfileVectors.storedCodecLawsAt", [ Takes.Codec ], capability, []),
          Unconditional
              "the consumer's own stored texts, walked whole: every text is read, re-rendered under the declared profile and under every other, so there is no sample that could miss one",
          none
              "reads a stored corpus; a defect in a stored text is a red law about that store, not a sampled refused outcome",
          "Canonical.read Codec.read Codec.write"
          ("EncodingProfileVectors.storedCodecLaws", [ Takes.Codec ], capability, []),
          Unconditional "the obsolete forward of storedCodecLawsAt (Phase 507); the same laws over the same store",
          none "the obsolete forward of storedCodecLawsAt; its refusal verdict is that family's",
          ""
          ("StoredIdentity.linearLaws", [ Takes.StreamWitness ], seam, []),
          Unconditional
              "the consumer's own stored chain, walked whole: every record is recomputed and every hash round-trips, so there is no sample that could miss one",
          none "reads a stored chain; a rehash refusal is a red law about that store, not a sampled refused outcome",
          "OpStream.rehashEncoding"
          ("StoredIdentity.dagLaws", [ Takes.StreamWitness ], seam, []),
          Unconditional
              "the consumer's own stored DAG, walked whole: every node id is recomputed and every id round-trips, so there is no sample that could miss one",
          none "reads a stored DAG; a rehash refusal is a red law about that store, not a sampled refused outcome",
          "Dag.firstBreakWith Dag.verifyDagWith Dag.rehashEncoding"
          ("StoredIdentity.captureLaws", [], seam, []),
          Unconditional
              "the consumer's own stored capture log, walked whole: every hash is recomputed and round-trips, so there is no sample that could miss one",
          none
              "reads a stored capture log; a rehash refusal is a red law about that store, not a sampled refused outcome",
          "OpStream.firstCaptureBreakEncoding OpStream.rehashCapturesEncoding"
          ("WireNullTolerance.laws", [], seam, []),
          Unconditional
              "a fixed vector corpus: every run evaluates every vector under both read policies, so there is no sample that could miss one",
          built "every malformed and no-absence vector is a fixed member of the corpus",
          "Json.read"
          ("WireNullTolerance.lawsWith", [], seam, []),
          Unconditional
              "the handed vector set, walked whole: every vector is evaluated under both read policies, and the corpus law reds a set with none",
          built "the malformed and no-absence vectors are members of the handed set, each evaluated",
          ""
          ("StringEscapeVectors.lawsWith", [], seam, []),
          Unconditional
              "the handed character and actor vectors, walked whole, and their rendered table; the corpus law reds a set with none",
          none "every vector is an accepted spelling; no refused outcome is read",
          ""
          ("EncodingProfileVectors.lawsWith", [], seam, []),
          Unconditional
              "the handed value and actor vectors, walked whole under both profiles, the shared checks and the rendered table; the corpus law reds a set with none",
          none "every vector is an accepted rendering; no refused outcome is read",
          ""
          ("ParityVectors.laws", [], seam, []),
          Unconditional
              "a fixed table: every run evaluates every named row and every row of both sweeps, so there is no sample that could miss one",
          none "every row is a computed value; no refused outcome is read",
          "ParityVectors.lines"
          ("ParityVectors.lawsWith", [], seam, []),
          Unconditional "the handed rows, walked whole: every row is evaluated, and the corpus law reds a set with none",
          none "every row is a computed value; no refused outcome is read",
          ""
          ("FoldConfluence.laneFoldLawsWith", [ Takes.StreamWitness; Takes.LaneGen ], capability, []),
          Guarded [ "lane-fold outcome" ],
          drawn
              "LaneHalted and LaneRejected come from the caller's LaneGen; guarded on lane-fold outcome, with rejected lane sets counted beside it (Phase 245)",
          "FoldConfluence.foldOnce Dag.reconcileMany" ]
        |> List.map row

    /// The roster's keys, sorted — the enumeration a census, a ladder or a projection quantifies
    /// over.
    let ids: string list = families |> List.map _.Id |> List.sort

    /// The modules the roster covers, sorted.
    let modules: string list =
        families |> List.map _.Module |> List.distinct |> List.sort

    /// The family with this id, if the kit ships one.
    let tryFind (id: string) : LawFamily option =
        families |> List.tryFind (fun f -> f.Id = id)

    /// Every ladder obligation the roster claims to discharge, paired with the family that
    /// discharges it, sorted by obligation.
    let obligations: (string * string) list =
        [ for f in families do
              for o in f.Discharges -> o, f.Id ]
        |> List.sortBy fst

    /// The refusable-family audit, one row per family (Phase 220): does the algebra the family
    /// certifies have a refusal population, and can a run miss it without a law going red? The suite
    /// holds every `Drawn` row to a `Guarded` census class. Projected from `families`.
    let refusalAudit: RefusalAudit list = families |> List.map _.RefusalAudit

    /// This package's adequacy census, `(id, class)`, projected from `families` — what
    /// `SampleAdequacy.census` forwards to.
    let census: (string * AdequacyClass) list =
        families |> List.map (fun f -> f.Id, f.Adequacy)

    /// The audit row for one family, if the roster audits it (the suite holds that it always does).
    let tryRefusal (id: string) : RefusalAudit option =
        refusalAudit |> List.tryFind (fun a -> a.Family = id)

    /// One PACKAGE'S share of the roster (Phase 257): the families it ships, under the package id a
    /// consumer references to run them. The families that read the dataframe layer ship from
    /// `Fuaran.Compute.Conformance` (DECISIONS.md D68, D66), which this package cannot name, so each
    /// package declares its own share and the renderings below compose a list of them.
    type Roster =
        {
            /// The package id the families ship from — the one reference a consumer adds to run them.
            Package: string
            /// The families this package ships, keyed exactly as the combined roster keys them.
            Families: LawFamily list
        }

        /// One audit row per family, projected from `Families`.
        member r.RefusalAudit: RefusalAudit list = r.Families |> List.map _.RefusalAudit

        /// One adequacy-census row per family, projected from `Families`.
        member r.Census: (string * AdequacyClass) list =
            r.Families |> List.map (fun f -> f.Id, f.Adequacy)

    /// This package's share.
    let roster: Roster =
        { Package = "Fuaran.Core.Conformance"
          Families = families }

    // ---- the exports ------------------------------------------------------------------------

    /// What the `cases` cell reads for a family the caller measured nothing for (Phase 196). A third
    /// state and not a synonym for `vacuous`: "this run exercised no case" and "no run was handed to
    /// the renderer" are different facts.
    [<Literal>]
    let unmeasuredToken = "unmeasured"

    /// The census cell for one family: the measured count, `vacuous`, or `unmeasured`.
    let private casesCell (cases: (string * CaseCount) list) (id: string) : string =
        match cases |> List.tryFind (fun (k, _) -> k = id) with
        | Some(_, c) -> CaseCell.render c
        | None -> unmeasuredToken

    /// The wire spelling of an opt-in reason — the JSON member and the markdown cell both use it.
    let reasonToken (r: OptInReason) : string =
        match r with
        | NeedsWitnessCapability -> "needs-witness-capability"
        | SeamNotEveryDomainHas -> "seam-not-every-domain-has"
        | StrongerPromise -> "stronger-promise"
        | NoWitnessToCertify -> "no-witness-to-certify"

    /// The wire spelling of a refusal verdict over a composed roster; `unaudited` for an id no share
    /// carries, so the renderer is total over a roster a consumer extended.
    let refusalTokenOf (rosters: Roster list) (id: string) : string =
        match rosters |> List.collect _.Families |> List.tryFind (fun f -> f.Id = id) with
        | Some f ->
            match f.Refusal.Population with
            | NoRefusal -> "none"
            | Built -> "built"
            | DrawnMissIsRed -> "drawn-miss-is-red"
            | Drawn -> "drawn"
        | None -> "unaudited"

    /// The wire spelling of a refusal verdict, over this package's share.
    let refusalToken (id: string) : string = refusalTokenOf [ roster ] id

    /// The adequacy cell over a composed roster (Phase 220): `unconditional` (a green run is a pass),
    /// `guarded-reached` (this run reached every guarded side), `guarded-starved` (the guard went
    /// red), `guarded-unmeasured` (no run was handed for it), or `unclassified` for an id no share
    /// carries.
    let adequacyTokenOf (rosters: Roster list) (cases: (string * CaseCount) list) (id: string) : string =
        match rosters |> List.collect _.Families |> List.tryFind (fun f -> f.Id = id) with
        | None -> "unclassified"
        | Some f ->
            match f.Adequacy with
            | Unconditional _ -> "unconditional"
            | Guarded _ ->
                match cases |> List.tryFind (fun (k, _) -> k = id) with
                | None -> "guarded-unmeasured"
                | Some(_, c) when List.isEmpty c.Starved -> "guarded-reached"
                | Some _ -> "guarded-starved"

    /// The adequacy cell, over this package's share.
    let adequacyToken (cases: (string * CaseCount) list) (id: string) : string = adequacyTokenOf [ roster ] cases id

    /// Every family of the composed roster, paired with the package share that ships it, sorted by id.
    let private composed (rosters: Roster list) : (Roster * LawFamily) list =
        [ for r in rosters do
              for f in r.Families -> r, f ]
        |> List.sortBy (fun (_, f) -> f.Id)

    /// The `schema` the export declares. A bump is a RECORDED contract move (Phase 498): the file
    /// has a reader outside this repository, which finds it BY NAME, keys on `kind` (never on
    /// `schema`) and reads `families[].id` and `families[].cases`, so every bump is entered in
    /// `STABILITY.md` naming that reader. 6 since Phase 506 (members in canonical order, `spellings`
    /// added); 5 from Phase 257's `packages`, 4 from Phase 220, 3 from Phase 196, 2 from Phase 194.
    [<Literal>]
    let schema = 6

    /// The roster as JSON — the machine export an offline projection reads, committed at
    /// `docs/conformance-families.json` and documented in `STABILITY.md`.
    ///
    /// Every value is written by `Canon.render`, so object members are in ordinal order and every
    /// string carries the wire's one escaping. The document is `Canon.render` of the whole export
    /// broken into lines at the `families` array, one family per line, so a diff shows only the
    /// families that moved: the lines, trimmed and joined, ARE the canonical rendering. Top level:
    /// `families`, `kind`, `packages`, `schema`. Each family: `adequacy`, `cases`, `discharges`,
    /// `entry`, `id`, `module`, `optIn`, `package`, `reason` (present only for an opt-in family —
    /// this wire model has no null), `refusal`, `spellings`, `witness`.
    ///
    /// `cases` is the one member a roster cannot derive, so it is supplied by the caller that ran the
    /// laws; `toJson ()` renders `unmeasured` for every family.
    let toJsonOf (rosters: Roster list) (cases: (string * CaseCount) list) : string =
        let strings (xs: string list) = JArr(xs |> List.map JStr)

        let family (r: Roster, f: LawFamily) =
            JObj(
                [ "id", JStr f.Id
                  "module", JStr f.Module
                  "entry", JStr f.Entry
                  "package", JStr r.Package
                  "witness", strings f.Witness
                  "optIn", JBool f.OptIn
                  "discharges", strings f.Discharges
                  "spellings", strings f.Spellings
                  "cases", JStr(casesCell cases f.Id)
                  "adequacy", JStr(adequacyTokenOf rosters cases f.Id)
                  "refusal", JStr(refusalTokenOf rosters f.Id) ]
                @ (f.Reason
                   |> Option.map (fun reason -> "reason", JStr(reasonToken reason))
                   |> Option.toList)
            )
            |> Canon.render

        let rows = composed rosters |> List.map family
        let last = List.length rows - 1

        let mem (key: string) (v: JVal) =
            "  " + Canon.render (JStr key) + ":" + Canon.render v

        [ yield "{"
          yield "  " + Canon.render (JStr "families") + ":["
          for i, line in List.indexed rows -> "    " + line + (if i < last then "," else "")
          yield "  ],"
          yield mem "kind" (JStr "fuaran.core.conformance.families") + ","
          yield mem "packages" (strings (rosters |> List.map _.Package)) + ","
          yield mem "schema" (JInt schema)
          yield "}"
          yield "" ]
        |> String.concat "\n"

    /// This package's share as JSON — `toJsonOf [ roster ]`.
    let toJsonWith (cases: (string * CaseCount) list) : string = toJsonOf [ roster ] cases

    /// The roster as the generated `docs/conformance-families.md` — the human-readable half of the
    /// same export, rendered from every package's share and sorted by `id`.
    let toMarkdownOf (rosters: Roster list) (cases: (string * CaseCount) list) : string =
        let cell (xs: string list) =
            if List.isEmpty xs then
                "—"
            else
                xs |> List.map (fun x -> "`" + x + "`") |> String.concat ", "

        let rows = composed rosters

        let row (r: Roster, f: LawFamily) =
            sprintf
                "| `%s` | `%s` | %s | %s | %s | %s | %s | %s | %s |"
                f.Id
                r.Package
                (if f.OptIn then "opt-in" else "base run")
                (match f.Reason with
                 | Some reason -> "`" + reasonToken reason + "`"
                 | None -> "—")
                (cell f.Witness)
                (cell f.Discharges)
                (casesCell cases f.Id)
                ("`" + adequacyTokenOf rosters cases f.Id + "`")
                ("`" + refusalTokenOf rosters f.Id + "`")

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
          "documents its shape. Why each row reads as it does is recorded, phase by phase, in"
          "`families-history.md`._"
          ""
          "A **law family** is a public entry point of this kit that answers with `LawResult list`; running"
          "one at your own witness is how a domain certifies it conforms. The table is held to reflection"
          "over the shipped assemblies BY RETURN TYPE, so it cannot miss a family by how it is named."
          ""
          "**Base run / opt-in.** `base run` families are the ones `Conformance.certify` and"
          "`Conformance.certifyStream` are built from; every other family is opt-in, for the reason its"
          "`Why opt-in` cell gives, and a domain calls it deliberately beside its base run."
          ""
          "**Witness.** The witness, generator and sink types the entry point takes, in parameter order;"
          "a family with none runs against the kit's own fixtures, or the Core it was compiled against."
          ""
          "**Discharges.** The claims-ladder obligations (`proofs.json` row ids) a green run of the"
          "family at your own witness discharges."
          ""
          "**Cases.** What a RUN measured at this repository's reference witness: the subject-law"
          "assertions made. `vacuous` — never a number — means the run certified nothing (it asserted"
          "nothing, or a guarded dimension starved, named in the cell); `unmeasured` means no run was"
          "handed to the renderer, a different fact and a different word."
          ""
          "**Adequacy.** `unconditional` — every iteration builds every branch the laws distinguish."
          "`guarded-reached` — the run reached every guarded side. `guarded-starved` — the guard went red."
          "`guarded-unmeasured` — a guarded family no run was handed for."
          ""
          "**Refusal.** Where refused outcomes come from: `none`, `built` (no run can miss one),"
          "`drawn-miss-is-red` (a law demands one, so a run that misses it fails) or `drawn` (a run that"
          "misses one stays green unless the family is guarded)."
          ""
          "**Package.** The package a family ships from. The families that read the dataframe layer ship"
          "from `Fuaran.Compute.Conformance` (DECISIONS.md D68, D66), whose own census lists them."
          ""
          sprintf
              "%d families, across %s, from %s."
              (List.length rows)
              (rows
               |> List.map (fun (_, f) -> f.Module)
               |> List.distinct
               |> List.sort
               |> List.map (fun m -> "`" + m + "`")
               |> String.concat ", ")
              (rosters |> List.map (fun r -> "`" + r.Package + "`") |> String.concat ", ")
          ""
          "| Family | Package | Run by | Why opt-in | Witness | Discharges | Cases | Adequacy | Refusal |"
          "|---|---|---|---|---|---|---|---|---|" ]
        @ (rows |> List.map row)
        @ [ "" ]
        |> String.concat "\n"

    /// This package's share as markdown — `toMarkdownOf [ roster ]`.
    let toMarkdownWith (cases: (string * CaseCount) list) : string = toMarkdownOf [ roster ] cases

    /// The roster rendered with no run behind it — every `cases` cell reads `unmeasured`.
    let toJson () : string = toJsonWith []

    /// The markdown half of the same, with no run behind it.
    let toMarkdown () : string = toMarkdownWith []
