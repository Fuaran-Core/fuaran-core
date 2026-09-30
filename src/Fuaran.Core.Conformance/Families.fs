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
// Since Phase 297 it is also the ONE place a family's adequacy class and refusal verdict are
// declared: both sat in further lists (`SampleAdequacy.census`, `refusalAudit`) held equal to this
// one only by tests, and both are projections of the family records now.
//
// It declares, it does not derive. What each family IS — its witness demands, whether an aggregate
// runs it, which ladder obligation its green run discharges, how its sample can starve, where its
// refusals come from — is a fact about the code that only a reader can state; the suite's job is to hold every one of those statements to the tree, and to
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
        /// The family certifies no witness at all (Phase 232): it reads the kit's own knowledge of
        /// the Core it was compiled against — `witnessSurfaceLaws` holds the frozen witness records'
        /// field sets — so an aggregate that certifies a DOMAIN'S witness has nothing to hand it. It
        /// is relevant to every domain, which is what separates it from `SeamNotEveryDomainHas`; a
        /// domain runs it deliberately, typically at a pin bump.
        | NoWitnessToCertify

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
        /// vacuity class: a family carrying it must be `Guarded` (its `Adequacy`), or its
        /// green run can mean nothing on the side the law is about.
        | Drawn

    /// A family's refusal verdict — where its refusal population comes from, and the evidence for
    /// that verdict in words a reader can check against the code. Carried on the family's own
    /// record (`LawFamily.Refusal`, Phase 297), so the audit and the roster cannot disagree.
    type RefusalVerdict =
        { Population: RefusalPopulation
          Why: string }

    /// One row of the refusable-family audit: a family, where its refusal population comes from,
    /// and the evidence for that verdict in words a reader can check against the code. Since Phase
    /// 297 a PROJECTION of the roster (`refusalAudit`, `refusalAuditOf`), never a declaration.
    type RefusalAudit =
        { Family: string
          Population: RefusalPopulation
          Why: string }

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
            /// How the family answers "could this run's sample have missed a verdict the laws
            /// distinguish?" — Phase 121's adequacy class, declared on the family itself since
            /// Phase 297. `SampleAdequacy.census` is its projection.
            Adequacy: AdequacyClass
            /// Where the family's refused outcomes come from — Phase 220's audit verdict, declared
            /// on the family itself since Phase 297. `refusalAudit` is its projection.
            Refusal: RefusalVerdict
        }

    /// Every law family the kit ships, in declaration order. Renderings sort by `Id`, so the order
    /// here is for a reader's benefit and never reaches an artefact.
    let families: LawFamily list =
        // `reason` is `OptInReason option`; `OptIn` is derived from it, so a family cannot be
        // declared opt-in without saying why, and a base-run family cannot carry a reason.
        //
        // Phase 297 — each row also declares the family's adequacy class and its refusal verdict,
        // which were two further lists (`SampleAdequacy.census`, this module's `refusalAudit`)
        // held equal to this one only by tests. Both are projections of these rows now.
        let f m entry witness reason discharges adequacy (population, why) =
            { Id = m + "." + entry
              Module = m
              Entry = entry
              Witness = witness
              OptIn = Option.isSome reason
              Reason = reason
              Discharges = discharges
              Adequacy = adequacy
              Refusal = { Population = population; Why = why } }

        let c entry witness reason discharges adequacy refusal =
            f "Conformance" entry witness reason discharges adequacy refusal

        let treeWitness = [ "NodeWitness"; "IdWitness"; "OpGen" ]
        let streamWitness = [ "StreamWitness"; "StreamGen" ]
        let none: string list = []

        [
          // ---- the base run: what `certify` and `certifyStream` are built from ----
          c
              "witnessLaws"
              treeWitness
              None
              [ "lawful-abstract-witness" ]
              (Unconditional "each iteration rebuilds a drawn node and re-reads every accessor")
              (NoRefusal, "accessor round-trips only; no apply, no refusal path")
          // Phase 184. These three were absent from this census for the whole of its life, and
          // the omission was not a judgement — the completeness check that keeps this list honest
          // reflected over method NAMES ending in `Laws`, and none of the three is spelled that
          // way. Two of them are the families `certify` and `certifyStream` are BUILT FROM. The
          // check now reads `Families`, whose own completeness is quantified over RETURN TYPE, so
          // a family cannot be missing from either list by how it is named.
          //
          // Phase 220 moved the two base-run families out of `Unconditional` (the refusable-family
          // audit, `Families.refusalAudit`). Their BUILT arms are real, but every law that reads the
          // apply OUTCOME — totality, `canApply ≡ apply`, the envelope law, and everything that reads
          // the accepted side — is quantified over a population the domain's generator DRAWS, so
          // whether the run reached an accepted op and a refused one is a property of the run.
          c
              "opAlgebra"
              treeWitness
              None
              [ "tree-algebra-well-formed-states" ]
              (Guarded [ "accepted"; "refused" ])
              (Drawn,
               "canApply ≡ apply and totality read both outcomes; genOp DRAWS refusals, and the collision arm BUILDS them only where the witness can carry a multi-node subtree")
          c
              "diffLaws"
              treeWitness
              None
              []
              (Unconditional "each iteration diffs a pair and re-applies the emitted script")
              (NoRefusal, "a refused op is skipped while building `after`; no law reads it")
          // Phase 245 — moved out of `Unconditional`, where "each iteration applies, replays and
          // tampers the same chain" was true only of a generator whose ops the domain accepts. The
          // chain is built from DRAWN ops and a refused op does not extend it, so a generator that
          // is refused every time leaves every chain empty and all three laws green over nothing;
          // and the tamper is only drawn, so one whose every substitute encodes like the op it
          // replaces never runs the tamper law. Both are the run's to reach.
          c
              "streamLaws"
              streamWitness
              None
              []
              (Guarded [ "accepted"; "tampered chain" ])
              (Drawn,
               "the tampered chain it must reject is built only over a chain the caller's StreamGen fills, and a refused append is skipped; guarded on accepted op and tampered chain (Phase 245)")
          c
              "reducer"
              [ "StreamGen" ]
              None
              []
              (Guarded [ "accepted"; "refused" ])
              (Drawn,
               "totality (a refusal is typed, not thrown) and the envelope law read refusals that only the caller's StreamGen draws")

          // ---- opt-in: a seam not every domain has ----
          // The refusal iff reads the drawn pair (and the minted probe) under the witness's
          // `canHold`; a canHold that refuses nothing, or none at all, exercises only its trivial
          // direction — the "rather than missing a branch" this row used to excuse.
          c
              "diffContainedLaws"
              treeWitness
              (Some StrongerPromise)
              []
              (Guarded [ "accepted"; "refused" ])
              (Drawn,
               "the refusal IFF reads the drawn pair under the witness's canHold, and a canHold that refuses nothing holds it trivially; the graft probe beside it is built")
          c
              "normalizeLaws"
              treeWitness
              (Some StrongerPromise)
              []
              (Unconditional "each iteration normalises a drawn script and compares both ways")
              (NoRefusal, "a refused op is skipped; no law reads it")
          // Phase 161. Every arm is BUILT — a perturbed child list, an operation over a drawn tree,
          // a graft carrying its own interior offender — but whether the WITNESS honours the rebuild
          // is drawn, and a witness whose `ReplaceChildren` is partial on leaves reaches none of
          // them. So the family counts what each arm actually reached and emits the guard, rather
          // than claiming an unconditionality it cannot have over an arbitrary witness.
          c
              "containerLaws"
              treeWitness
              (Some StrongerPromise)
              []
              (Guarded [ "built arm (child perturbation / invariant probe / interior graft)" ])
              (Built,
               "the interior-offender graft is built and must be refused NotAContainer; its guard covers the built arms")
          c
              "mergeConflictLaws"
              treeWitness
              (Some StrongerPromise)
              []
              (Guarded [ "op-pair interference" ])
              (NoRefusal, "conflicts is a report list, never a refused outcome")
          c
              "reconcileLaws"
              treeWitness
              (Some StrongerPromise)
              []
              (Guarded [ "reconcile outcome; delta-pair independence (delegates to reconcileLawsWith)" ])
              (Drawn, "delegates to reconcileLawsWith")
          // Phase 297 — the reconcile laws under the domain's own chain hash, the pinned parameter
          // last before the seed (the naming rule); the bare form pins `OpStream.defaultHash`.
          c
              "reconcileLawsWith"
              treeWitness
              (Some StrongerPromise)
              []
              (Guarded [ "reconcile outcome"; "reconcile shape"; "delta-pair independence" ])
              (Drawn, "a reconcile Error arises from OpGen-drawn scripts; guarded on reconcile outcome")
          c
              "footprintLaws"
              treeWitness
              (Some StrongerPromise)
              [ "independence-diamond" ]
              (Guarded [ "script-pair independence" ])
              (NoRefusal, "a refused op is skipped; no law reads it")
          c
              "concurrencyLaws"
              treeWitness
              (Some StrongerPromise)
              []
              (Guarded [ "independent pair (delegates to concurrencyLawsWith)" ])
              (NoRefusal, "delegates to concurrencyLawsWith")
          c
              "concurrencyLawsWith"
              treeWitness
              (Some StrongerPromise)
              []
              (Guarded [ "independent pair (its own Phase 80 vacuity guard)" ])
              (NoRefusal, "a drawn refusal is skipped; an applyAll Error only fails a law")
          c
              "arbitrationLaws"
              treeWitness
              (Some StrongerPromise)
              []
              (Guarded [ "arbitration bucket" ])
              (Drawn,
               "Inapplicable comes from the kit's corruption roll and Conflicts from drawn scripts; guarded on arbitration bucket")
          c
              "snapshotLaws"
              streamWitness
              (Some StrongerPromise)
              []
              (Unconditional "delegates to snapshotLawsWith")
              (NoRefusal, "delegates to snapshotLawsWith")
          // Phase 297 — the snapshot arm runs only over a chain long enough to cut, which the domain's
          // generator decides. Its two laws are STRICT runner cells: a run that never cuts a snapshot
          // reds them itself ("never reached") and `cases` reads that as starvation, so the family
          // needs no guard of its own to be seen starving.
          c
              "snapshotLawsWith"
              streamWitness
              (Some StrongerPromise)
              []
              (Unconditional
                  "each iteration takes a snapshot and replays across it; a run whose chains are too short to cut one reds both laws as never reached")
              (NoRefusal, "a rejected append is skipped; a compact Error only fails a law")
          // Phase 297 — the tamper arm runs only when a fresh draw differs from the op it replaces;
          // the law is a STRICT runner cell, so a run that never draws a differing op reds it as
          // never reached rather than passing it.
          c
              "dagLaws"
              streamWitness
              (Some StrongerPromise)
              []
              (Unconditional
                  "each iteration builds, replays and round-trips one DAG, and tampers it whenever a fresh draw differs; a run that never tampers reds the tamper law as never reached")
              (DrawnMissIsRed,
               "the tampered node it must reject is built only when a fresh draw differs from the op it replaces; the tamper law is a strict runner cell, so a run that never tampers reds it as never reached (Phase 297)")
          // Phase 223 — the six drawn-refusal families Phase 220's audit (`Families.refusalAudit`)
          // found and left for this phase. Each was `Unconditional` on the strength of what every
          // iteration BUILDS, and each also carries a law that compares a REFUSED outcome — an
          // agreement or an iff that holds trivially when nothing was refused — over a population
          // the run DRAWS. So each counts its accepted and refused cases and emits the guard, and
          // each kit reference generator is stratified so the guard never fires on it.
          //
          // `match ≡ append` compares a domain refusal with a CAS `Domain` rejection only when the
          // caller's StreamGen draws a refused op.
          //
          // Phase 297 — and the race arm (two appendIf calls at one head that BOTH apply) is drawn
          // too, so it is counted beside them.
          c
              "casLaws"
              streamWitness
              (Some StrongerPromise)
              []
              (Guarded [ "accepted"; "refused"; "race arm" ])
              (Drawn,
               "match ≡ append compares a domain refusal with a CAS Domain rejection only when StreamGen draws one; the stale-head rejection beside it is built")
          // `fresh ≡ append` and the true-head CAS arm forward a domain refusal verbatim only when
          // the drawn fresh op is refused; `Duplicate` and `StaleHead` beside them are built.
          c
              "idempotencyLaws"
              streamWitness
              (Some StrongerPromise)
              []
              (Guarded [ "accepted"; "refused" ])
              (Drawn,
               "fresh-key ≡ append and the CAS arm compare domain refusals only when StreamGen draws one; Duplicate and StaleHead are built")
          // Phase 297 — moved out of `Unconditional`: the reorder, drop and bit-flip arms each need a
          // chain long enough to perturb, which the domain's generator decides (a generator refused
          // every time builds none). The family counts each arm and emits the guard.
          c
              "hashFnLaws"
              streamWitness
              (Some StrongerPromise)
              []
              (Guarded [ "tamper arm (reorder / drop / bit-flip)" ])
              (Drawn,
               "reorder, drop and bit-flip are built and must fail verifyChain, but only over a drawn chain long enough to perturb; guarded on tamper arm (Phase 297)")
          // Phase 297 — moved out of `Unconditional`: the re-attribution tamper runs only over a
          // non-empty lifted stream, which the domain's generator decides; the family counts it.
          c
              "attributedLaws"
              streamWitness
              (Some StrongerPromise)
              []
              (Guarded [ "tampered chain (re-attributed)" ])
              (Drawn,
               "a re-attributed op is built and must fail verifyChain, but only over a non-empty drawn stream; guarded on tampered chain (Phase 297)")
          c
              "codecInjectivityLaws"
              streamWitness
              (Some StrongerPromise)
              []
              (Unconditional
                  "the left-inverse law is BUILT by every iteration — one drawn op round-tripped through the domain's own Decode, and a codec with a total left inverse is injective — so the family's weight does not rest on the collision search beside it, whose own third law fails when the draw was too narrow to compare anything")
              (NoRefusal, "a Decode refusal only fails a law")
          c
              "noAttestationVacuityLaws"
              streamWitness
              (Some StrongerPromise)
              []
              (Unconditional "each iteration asks the no-op sink to sign and to verify")
              (Built, "a plausible attestation is built and the no-op sink must reject it")
          // Phase 196 — moved out of `Unconditional`, where it had sat since this census was
          // written, and the reclassification is the finding rather than a tidy-up. "Each
          // iteration signs a head and forges both an op and an attribution" is true of a SIGNING
          // sink and false of `OpStream.noAttestation`, under which four of the five laws assert
          // nothing and all five still report green. The sink is a PARAMETER, so whether the
          // evidence is built is a property of the run — which is what `Guarded` means, and the
          // family that certifies the unsigned path is `noAttestationVacuityLaws` beside it.
          //
          // Phase 297 — the op-forgery arm is demanded by the same guard.
          c
              "attestationLaws"
              [ "StreamWitness"; "StreamGen"; "IAttestationSink" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "signing outcome and op tamper" ])
              (Built, "op and actor forgeries are built each iteration; its guard is on the signing outcome")
          c
              "compositionLaws"
              [ "ArtifactWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Unconditional "each iteration composes a drawn pair and compares against the nested application")
              (NoRefusal, "a compose Error only fails a law or is compared opaquely")
          c
              "compositionPilot"
              [ "ArtifactWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Unconditional
                  "it runs `compositionLaws` (unconditional above) and BUILDS both applyMemo arms across the witness boundary each iteration — a closed inner sub-function memoised, and the composed outer compared against direct apply")
              (NoRefusal, "as compositionLaws; a memo Error only fails a law")
          c
              "memoLaws"
              [ "ArtifactWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Unconditional "each iteration forces a miss then a hit, and an effecting bypass, by construction")
              (NoRefusal, "every Error arm only fails a law")
          c
              "memoSoundnessLaws"
              [ "ArtifactWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Unconditional "each iteration applies the caller-supplied under-declared function twice")
              (NoRefusal, "an Error only fails a law; the cache bypass is not a refusal")
          c
              "functionVerifyLaws"
              [ "ArtifactWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Unconditional "each iteration verifies a SOUND and a BROKEN function, both caller-supplied")
              (DrawnMissIsRed,
               "the broken function is caught only when genParams reaches its bad sub-space, and a broken function that verifies clean is itself a red law")
          c
              "verifyHonestyLaws"
              [ "ArtifactWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Unconditional "each iteration verifies a stochastic and an under-declared function, both caller-supplied")
              (DrawnMissIsRed,
               "the broken verdicts depend on genParams, and a broken function verifying under any axis is a red law")
          // Phase 297 — moved out of `Unconditional`, where "each iteration hashes a drawn pair of
          // trees" was false: each iteration hashes ONE tree against a seen-map, so a generator that
          // draws one tree every time compared nothing and passed green. The family now counts the
          // distinct trees it saw and the pairs it compared, as `codecInjectivityLaws` does.
          c
              "encoderInjectivityLaws"
              [ "ArtifactWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "distinct tree (seen / compared)" ])
              (NoRefusal, "no refused outcome is read")
          c
              "projectionLaws"
              [ "ProjectionWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Unconditional "each iteration projects, re-imports and scopes the same tree")
              (NoRefusal, "a re-import Error only fails a law")
          // `explainRejection` and the rejected arms of the allow / approve parity read a reducer
          // rejection only when the caller's op generator draws one; the decision axis, the unknown
          // tool and the unknown proposal id are built.
          //
          // Phase 246 — `aiSurfaceLaws` runs the DOMAIN'S `Decide`, so which proposal arm a drawn op
          // reaches is the domain's policy's answer: a policy that never parks or never denies
          // leaves those arms untested, and one that allows everything is exactly that. The kit-
          // policy form rolls the decision itself and keeps the two Phase 223 dimensions.
          c
              "aiSurfaceLawsAt"
              [ "AiSurfaceWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "accepted"; "refused"; "allowed"; "parked"; "denied" ])
              (Drawn,
               "explainRejection and the allowed-submit parity read a reducer rejection only when the caller's op generator draws one, and since Phase 246 the deny and park arms are reached only when the domain's own Decide chooses them; unknown tool and unknown id are built")
          // Phase 297 — the obsolete bare name of `aiSurfaceLawsAt`, rostered through the 0.33.0 draft
          // under its own id and guard label.
          c
              "aiSurfaceLaws"
              [ "AiSurfaceWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "accepted"; "refused"; "allowed"; "parked"; "denied" ])
              (Drawn, "an obsolete forward of aiSurfaceLawsAt, under its own id")
          c
              "aiSurfaceLawsUnderKitPolicy"
              [ "AiSurfaceWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "accepted"; "refused" ])
              (Drawn,
               "explainRejection and the allowed-submit parity read a reducer rejection only when the caller's op generator draws one; the kit rolls the decision, and unknown tool and unknown id are built")

          // Phase 246 — the seam families at a DOMAIN'S seam. Their fixture-bound forms below take
          // only a seed and certify Core's own fixtures, which is what they are for.
          // Phase 246 — the seam families at a domain's seam: every outcome is a call the domain's
          // generator DRAWS, so each of the three is a dimension the run can miss.
          c
              "capabilityLawsAt"
              [ "CapabilitySeamWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "settled"; "pending"; "refused" ])
              (Drawn,
               "every refusal the three laws read is a call the domain's generator draws; guarded on settled, pending and refused")
          c
              "queryLawsAt"
              [ "QuerySeamWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "settled"; "pending"; "refused" ])
              (Drawn,
               "every refusal the three laws read is a call the domain's generator draws; guarded on settled, pending and refused")
          c
              "capabilityPipelineLawsAt"
              [ "CapabilityPipelineWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "invoke node" ])
              (Built,
               "the unregistered-capability and undeclared-argument pipelines are built from every drawn Invoke node; guarded on invoke node")
          // Phase 297 — the three `…With` spellings the naming rule renamed `…At`, rostered through
          // the 0.33.0 draft as obsolete forwards under their own ids and guard labels.
          c
              "capabilityLawsWith"
              [ "CapabilitySeamWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "settled"; "pending"; "refused" ])
              (Drawn, "an obsolete forward of capabilityLawsAt, under its own id")
          c
              "queryLawsWith"
              [ "QuerySeamWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "settled"; "pending"; "refused" ])
              (Drawn, "an obsolete forward of queryLawsAt, under its own id")
          // The default-deny arms are BUILT, but per drawn `Invoke` node — a generator of Source-only
          // pipelines builds none of them.
          c
              "capabilityPipelineLawsWith"
              [ "CapabilityPipelineWitness" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "invoke node" ])
              (Built, "an obsolete forward of capabilityPipelineLawsAt, under its own id")

          // Phase 189 — the same shape, one axis further out: the collision arms are BUILT through
          // the domain's own `PlaceKeyedChild`, and whether the witness honours a placement is the
          // domain's to answer. A witness that declares NO keyed position is the one case that is
          // not an unreached arm — it is a declaration that there is nothing to reach — and the
          // family reports that as its adequacy line rather than as a missed verdict.
          c
              "keyedChildrenLaws"
              ([ "KeyedWitness" ] @ treeWitness)
              (Some NeedsWitnessCapability)
              [ "witness-surface-scope" ]
              (Guarded [ "built arm (clean full walk / keyed id in the surface / one id in two keyed positions)" ])
              (Built,
               "the keyed-and-surface and double-keyed collisions are built through PlaceKeyedChild; its guard covers the built arms")

          // Phase 211 — the same contract, at a DOMAIN'S evaluator. Every arm the agreement law
          // distinguishes is DRAWN from the domain's own edits: a change that reached a reader, a
          // clean node reused from `prior`, and an edited evaluator that failed. A domain whose edits
          // all move the dependency map reaches none of them, and the guard says so.
          c
              "propagationEvaluatorLaws"
              [ "EvaluatorWitness" ]
              (Some NeedsWitnessCapability)
              [ "propagation-change-set-and-prior" ]
              (Guarded [ "evaluator edit" ])
              (Drawn, "the failing-evaluator arm comes from the domain's own edits; guarded on evaluator edit")

          // Phase 250 — the prior-aware evaluator: a recomputed node handed its prior (the prior
          // path, not only priming) and a clean node reused from it. It runs the family above first,
          // so the reference arms carry that family's guard too.
          c
              "propagationEvaluatorLawsWith"
              [ "EvaluatorWitness" ]
              (Some NeedsWitnessCapability)
              [ "propagation-prior-blind" ]
              (Guarded [ "evaluator edit"; "prior-aware edit" ])
              (Drawn,
               "runs propagationEvaluatorLaws first, so its failing-evaluator arm is drawn the same way; the prior-aware arms are guarded on prior-aware edit")

          // Phase 297 — moved out of `Unconditional`: the tamper arm runs only when a fresh draw
          // encodes differently from the value it replaces, which the domain's generator decides.
          c
              "captureReplayLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Guarded [ "tampered" ])
              (Drawn,
               "the tampered capture is built only when a fresh draw encodes differently from the value it replaces (guarded on tampered, Phase 297); the misordered replay is built")
          c
              "constructThenEncodeLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional
                  "every corpus document is decoded, rebuilt through the authoring surface and re-encoded on every run — the sample is the caller's own corpus rather than a draw, and an empty one fails the family's own non-vacuity law instead of passing quietly")
              (NoRefusal, "Reject corpus cases are filtered out; a Construct Error only fails a law")
          c
              "hashFnAdversarialLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional "the budget IS the sample size, and it is the caller's own declared parameter")
              (NoRefusal, "a collision search; no refused outcome")
          c
              "capabilityLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional "each iteration exercises accept, reject and unknown-arg on a built declaration")
              (Built, "out-of-space arg, unknown arg and unregistered id are built each iteration")
          c
              "queryLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional "each iteration exercises accept, type-mismatch and unknown-param on a built declaration")
              (Built, "type mismatch, unknown param, NoSuchQuery and ExecutionFailed are built")
          c
              "registryLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional "each iteration queries matching and non-matching signatures on a built registry")
              (Built, "an unregistered id and an out-of-space arg are built")
          c
              "packLoadingLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional "each iteration loads a pack and refuses a stale pin and an unknown base")
              (Built, "a stale-version pack and an unknown base are built")
          // Phase 257 — the `Column.aggregate` half of what was `aggregateParityLaws`; the parity
          // half ships from `Fuaran.Core.DataFrame.Conformance` with the other dataframe families,
          // produced by the compute repository since Phase 258 (DECISIONS.md D66).
          c
              "aggregateNullSkipLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional
                  "each iteration aggregates Count and Sum over a drawn column and its present-only projection")
              (NoRefusal, "an aggregate Error only fails the law, and the kit draws no type that can raise one")
          // The kit draws its own sample here, and a fault-free draw satisfies the soundness law as
          // 0 = 0. The roll is stratified by iteration index, so three iterations reach both faults;
          // a shorter run can still miss them, which is why the class is not `Unconditional`.
          c
              "columnarValidatorLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Guarded [ "null cell"; "out-of-range cell" ])
              (Drawn,
               "null and out-of-range faults are injected by the kit's own roll, and a fault-free draw satisfies the count laws trivially")
          c
              "deferredLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional "each iteration round-trips Pending, Ready and Failed")
              (Built, "the fixed Failed case must yield Error")
          c
              "capabilityPipelineLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional "each iteration type-checks a well-typed and an ill-typed edge")
              (Built, "the fixed ill-typed pipeline must refuse EdgeTypeMismatch")
          c
              "capabilityPipelineIncrementalLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Guarded [ "node reuse" ])
              (NoRefusal, "an eval Error only records a failure")
          c
              "dirtyPropagationLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Guarded [ "dirty frontier" ])
              (NoRefusal, "no refused outcome is read")
          // Phase 209 — the second dimension is the undeclared-read refusal: a real node read
          // without being declared, and an id the map does not hold at all.
          c
              "propagationEvalLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Guarded [ "node reuse"; "undeclared read" ])
              (Built, "the unknown change and the leaky evaluator are built each iteration")
          c
              "canonicalFloatLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional "each iteration renders a drawn float and the three non-finite tokens")
              (NoRefusal, "no refused outcome is read")
          c
              "chainBreakReasonLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional
                  "each iteration BUILDS all three break kinds on both walkers — a renumbered sequence, a repointed prev-link, and a payload tampered with its sequence and link left intact — rather than drawing them, and the family's own last two laws fail if any kind was not actually observed")
              (Built, "all three break kinds are built each iteration")
          c
              "dagBreakReasonLaws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional
                  "each iteration BUILDS both break kinds on the DAG walk — a node whose op is tampered with its map key left alone, and a named parent deleted — rather than drawing them, and the family's own last law fails if either kind was not actually observed")
              (Built, "both break kinds are built each iteration")

          // Phase 232 — the witness-record field freeze. It takes no witness because it certifies
          // the Core a domain compiled against rather than the domain, so no aggregate runs it.
          // Phase 232 — nothing is drawn: every run reads every frozen record's field set and (on
          // .NET) every public record the kit's assemblies export, so its one run is the sample.
          c
              "witnessSurfaceLaws"
              none
              (Some NoWitnessToCertify)
              []
              (Unconditional
                  "every run reads the field set of every frozen witness record, and every public record the kit's assemblies export, by reflection — nothing is drawn, so the one run is the whole sample")
              (NoRefusal, "reads record field sets by reflection; no op is applied and no refused outcome exists")

          // Phase 297 — the null-tolerant read vectors (Phase 102), rostered now that the family
          // answers in `LawResult`s. The corpus is fixed, so the one run is the whole sample.
          f
              "WireNullTolerance"
              "laws"
              none
              (Some SeamNotEveryDomainHas)
              []
              (Unconditional
                  "a fixed vector corpus: every run evaluates every vector under both read policies, so there is no sample that could miss one")
              (Built, "every malformed and no-absence vector is a fixed member of the corpus")

          f
              "FoldConfluence"
              "laneFoldLaws"
              [ "StreamWitness"; "LaneGen" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "lane-fold outcome (delegates to laneFoldLawsWith)" ])
              (Drawn, "delegates to laneFoldLawsWith")
          f
              "FoldConfluence"
              "laneFoldLawsWith"
              [ "StreamWitness"; "LaneGen" ]
              (Some NeedsWitnessCapability)
              []
              (Guarded [ "lane-fold outcome" ])
              (Drawn,
               "LaneHalted and LaneRejected come from the caller's LaneGen; guarded on lane-fold outcome, with rejected lane sets counted beside it (Phase 245)") ]

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

    /// Phase 220 — the refusable-family audit over any list of families, one row per family, in
    /// the order given. A PROJECTION since Phase 297: each family declares its own verdict
    /// (`LawFamily.Refusal`), so a row cannot name a family the roster does not carry, and a family
    /// cannot ship unaudited.
    let refusalAuditOf (fs: LawFamily list) : RefusalAudit list =
        fs
        |> List.map (fun f ->
            { Family = f.Id
              Population = f.Refusal.Population
              Why = f.Refusal.Why })

    /// Phase 220 — the refusable-family audit, one row per family: does the algebra the family
    /// certifies have a REFUSAL population — a refused / rejected / invalid outcome a law branches
    /// on — and can a run miss it without a law going red? `Drawn` is the class that matters: the
    /// suite holds every `Drawn` row to a `Guarded` census class, so a family whose refusals a run
    /// can silently miss cannot report an unguarded pass. This package's share, projected from
    /// `families`.
    let refusalAudit: RefusalAudit list = refusalAuditOf families

    /// The adequacy census over any list of families — `(id, class)` in the order given. A
    /// PROJECTION since Phase 297: each family declares its own class (`LawFamily.Adequacy`).
    let censusOf (fs: LawFamily list) : (string * AdequacyClass) list =
        fs |> List.map (fun f -> f.Id, f.Adequacy)

    /// This package's adequacy census, projected from `families` — what `SampleAdequacy.census`
    /// forwards to.
    let census: (string * AdequacyClass) list = censusOf families

    /// The audit row for one family, if the roster audits it (the suite holds that it always does).
    let tryRefusal (id: string) : RefusalAudit option =
        refusalAudit |> List.tryFind (fun a -> a.Family = id)

    /// Phase 257 — one PACKAGE'S share of the roster: the families it ships, their refusal audit
    /// and their adequacy census, under the package id a consumer references to run them.
    ///
    /// The kit's families are no longer all in one assembly. Those that read the dataframe layer
    /// ship from `Fuaran.Core.DataFrame.Conformance` (DECISIONS.md D68) — since Phase 258 from
    /// the compute repository, which produces that package (D66) — and this package cannot name
    /// them without referencing that one, the upward reference the boundary test refuses. So each
    /// package declares its own share, and a reader that sees several composes them: the
    /// renderings below take a list of rosters. This repository's generated
    /// `docs/conformance-families.*` render this package's share alone.
    type Roster =
        {
            /// The package id the families ship from — the one reference a consumer adds to run them.
            Package: string
            /// The families this package ships, keyed exactly as the combined roster keys them.
            Families: LawFamily list
            /// One audit row per family above — `refusalAuditOf Families` (Phase 297: the families
            /// carry their own verdicts, and the renderings read those).
            RefusalAudit: RefusalAudit list
            /// One adequacy-census row per family above — `censusOf Families` (Phase 297: the
            /// families carry their own classes, and the renderings read those).
            Census: (string * AdequacyClass) list
        }

    /// This package's share: `families`, with `refusalAudit` and `census` projected from it.
    let roster: Roster =
        { Package = "Fuaran.Core.Conformance"
          Families = families
          RefusalAudit = refusalAudit
          Census = census }

    // ---- the exports ------------------------------------------------------------------------

    /// A JSON string literal, through the wire's own escaper (Phase 297) — this module carried a
    /// fourth hand-rolled copy until then, and a copy is where two escapers drift apart.
    let private quote (s: string) : string = "\"" + Json.escape s + "\""

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
        | Some(_, c) -> CaseCell.render c
        | None -> unmeasuredToken

    /// The wire spelling of an opt-in reason — the JSON member and the markdown cell both use
    /// it, so the two renderings never disagree about a family. A base-run family has none.
    let reasonToken (r: OptInReason) : string =
        match r with
        | NeedsWitnessCapability -> "needs-witness-capability"
        | SeamNotEveryDomainHas -> "seam-not-every-domain-has"
        | StrongerPromise -> "stronger-promise"
        | NoWitnessToCertify -> "no-witness-to-certify"

    /// The wire spelling of a refusal-audit verdict over a composed roster — the audit row is read
    /// from whichever package's share carries the family (Phase 257).
    let refusalTokenOf (rosters: Roster list) (id: string) : string =
        match rosters |> List.collect _.Families |> List.tryFind (fun f -> f.Id = id) with
        | Some f ->
            match f.Refusal.Population with
            | NoRefusal -> "none"
            | Built -> "built"
            | DrawnMissIsRed -> "drawn-miss-is-red"
            | Drawn -> "drawn"
        | None -> "unaudited"

    /// The wire spelling of a refusal-audit verdict — Phase 220. `unaudited` is never rendered for
    /// a shipped family (the suite holds the audit equal to the roster); it exists so the renderer
    /// is total rather than throwing on a roster a consumer extended. Reads this package's share;
    /// a family another package ships reads its verdict through the composed renderings.
    let refusalToken (id: string) : string = refusalTokenOf [ roster ] id

    /// The adequacy cell over a composed roster — the census row is read from whichever package's
    /// share carries the family (Phase 257).
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

    /// The adequacy cell — Phase 220. What `certify`'s verdict for a family is made of, as a fact
    /// the generated data carries rather than one a reader reconstructs: `unconditional` (every
    /// iteration builds every branch, so a green run is a pass), `guarded-reached` (the family
    /// carries a guard and this run reached every guarded side), `guarded-starved` (the guard went
    /// red — the run tested nothing on a side a law is about), or `guarded-unmeasured` (a guarded
    /// family no run was handed for). Read from the family's own `Adequacy` and the measured run, so it
    /// cannot disagree with either.
    let adequacyToken (cases: (string * CaseCount) list) (id: string) : string = adequacyTokenOf [ roster ] cases id

    /// Every family of the composed roster, paired with the package share that ships it, sorted by id.
    let private composed (rosters: Roster list) : (Roster * LawFamily) list =
        [ for r in rosters do
              for f in r.Families -> r, f ]
        |> List.sortBy (fun (_, f) -> f.Id)

    /// The roster as JSON — the machine export, and the one an offline projection reads without
    /// building or running anything (it is committed, generated, at `docs/conformance-families.json`,
    /// rendered from every package's share).
    ///
    /// The shape is a CONTRACT and is documented in `STABILITY.md`: a top-level object carrying
    /// `kind`, `schema`, `packages` (the package ids composed, in the order given) and a `families`
    /// array sorted by `id`, each member an object with `id`, `module`, `entry`, `package` (the
    /// package the family ships from), `witness` (array), `optIn` (boolean), `reason` (a string from
    /// the [[OptInReason]] vocabulary, **present only for an opt-in family** — this wire model has
    /// no null), `discharges` (array), `cases` (a string: a decimal count, `vacuous`, or
    /// `unmeasured`), `adequacy` (see [[adequacyToken]]) and `refusal` (see [[refusalToken]]).
    ///
    /// `schema` reads 5 since Phase 257 split the kit across two packages: the top-level `package`
    /// became `packages`, and each family names its own. It read 4 from Phase 220's `adequacy` and
    /// `refusal`, 3 from Phase 196's `cases` and 2 from Phase 194's `reason`. Each bump is free and
    /// therefore taken: a search of the workspace found no reader of this file outside this
    /// repository's own suite, so nothing keys on the old number, and a shape that changes under an
    /// unmoved stamp is the drift class consumers keep paying for elsewhere. Members are written
    /// in that order and the array is sorted, so the rendering is byte-stable across runs and a diff
    /// shows only what moved. Two spaces of indent, `\n` line endings, and a trailing newline.
    ///
    /// `cases` is the ONE member a roster cannot derive — a declaration cannot run a law — so it
    /// is supplied by the caller that did run them. `toJson ()` renders `unmeasured` for every
    /// family, which is the honest cell for a caller that measured nothing.
    let toJsonOf (rosters: Roster list) (cases: (string * CaseCount) list) : string =
        let family (r: Roster, f: LawFamily) =
            // `reason` is OMITTED for a base-run family rather than rendered `null`: this wire
            // model has no null (`JVal` cannot represent one, and `no_null_ever` is a proved
            // grammar theorem over the renderer), so a null here would emit a document the kit's
            // own parser refuses. Absence is how this format spells "not applicable".
            [ "      " + quote "id" + ": " + quote f.Id
              "      " + quote "module" + ": " + quote f.Module
              "      " + quote "entry" + ": " + quote f.Entry
              "      " + quote "package" + ": " + quote r.Package
              "      " + quote "witness" + ": " + jsonArray f.Witness
              "      " + quote "optIn" + ": " + (if f.OptIn then "true" else "false")
              "      " + quote "discharges" + ": " + jsonArray f.Discharges
              "      " + quote "cases" + ": " + quote (casesCell cases f.Id)
              "      " + quote "adequacy" + ": " + quote (adequacyTokenOf rosters cases f.Id)
              "      " + quote "refusal" + ": " + quote (refusalTokenOf rosters f.Id) ]
            |> fun members ->
                match f.Reason with
                | None -> members
                | Some reason ->
                    // after `optIn`, before `discharges` — the documented member order
                    let head, tail = List.splitAt 6 members
                    head @ [ "      " + quote "reason" + ": " + quote (reasonToken reason) ] @ tail
            |> String.concat ",\n"
            |> fun body -> "    {\n" + body + "\n    }"

        let body = composed rosters |> List.map family |> String.concat ",\n"

        "{\n"
        + "  "
        + quote "kind"
        + ": "
        + quote "fuaran.core.conformance.families"
        + ",\n"
        + "  "
        + quote "schema"
        + ": 5,\n"
        + "  "
        + quote "packages"
        + ": "
        + jsonArray (rosters |> List.map _.Package)
        + ",\n"
        + "  "
        + quote "families"
        + ": [\n"
        + body
        + "\n  ]\n}\n"

    /// This package's share as JSON — `toJsonOf [ roster ]`.
    let toJsonWith (cases: (string * CaseCount) list) : string = toJsonOf [ roster ] cases

    /// The roster as the generated `docs/conformance-families.md` — the human-readable half of the
    /// same export, rendered from every package's share. Sorted by `id`, so the table is
    /// byte-stable and a diff shows only what moved.
    ///
    /// `cases` carries what a run measured, per Phase 196; `adequacy` and `refusal` are Phase
    /// 220's; `package` is Phase 257's.
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
          "documents its shape._"
          ""
          "A **law family** is a public entry point of this kit that answers with `LawResult list`."
          "Running one at your own witness is how a domain certifies it conforms; this table is the"
          "enumeration of what there is to run, so a family cannot be quietly absent from a"
          "conformance census that quantifies over it. The enumeration is held to reflection over"
          "the shipped assemblies BY RETURN TYPE, so it cannot miss a family by how the family is"
          "named."
          ""
          "**Base run / opt-in.** The five `base run` families are the ones `Conformance.certify` and"
          "`Conformance.certifyStream` are built from — a domain gets them by calling an aggregate."
          "Every other family is opt-in — its `Why opt-in` cell says which reason — so a domain calls"
          "it deliberately, alongside its base run. Neither is a statement about importance: `reducer`"
          "is a base-run family only for stream-shaped domains, and `footprintLaws` is opt-in while"
          "discharging a ladder obligation."
          ""
          "**Witness.** The witness and generator types the entry point takes, in parameter order. A"
          "family with none runs against the kit's own fixtures and needs nothing but a seed — or,"
          "for `no-witness-to-certify`, against the Core it was compiled against, and needs nothing."
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
          "handed to the renderer, which is a different fact and deliberately a different word. A"
          "green run carries its count on the pass path through `SampleAdequacy.cases`, and an"
          "aggregate's report reads the same way: `certify` and `certifyStream` return every law and"
          "every guard of the families they run, so `cases` over the report is their subject laws"
          "times the iterations, with every starved side named (Phase 245)."
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
          "**Package.** The package a family ships from — the one reference a consumer adds to run it."
          "The families that read the dataframe layer ship from `Fuaran.Core.DataFrame.Conformance`"
          "(Phase 257, DECISIONS.md D68), which the compute repository produces since Phase 258"
          "(D66) and whose own generated census lists them; every family here ships from"
          "`Fuaran.Core.Conformance`."
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

    /// The roster rendered with no run behind it — every `cases` cell reads `unmeasured`. Kept
    /// beside the counted renderings rather than replaced by them, because a reader that wants the
    /// DECLARATION should not have to produce a run to get it.
    let toJson () : string = toJsonWith []

    /// The markdown half of the same, with no run behind it.
    let toMarkdown () : string = toMarkdownWith []
