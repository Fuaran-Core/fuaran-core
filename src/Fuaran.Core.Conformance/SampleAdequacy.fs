namespace Fuaran.Core

// ============================================================================
//  Sample adequacy (Phase 121) — the guard that asks whether a family's SAMPLE
//  reached the verdicts its laws distinguish, and fails, with the counts, when
//  it did not.
//
//  A `LawResult` records that a law HELD. It cannot record how many samples the
//  law was reached by, so a law gated on a generated condition — "an independent
//  pair", "a halting lane set", "a declined refresh" — reports exactly the same
//  green whether the condition arose two hundred times or never. That is not a
//  hypothetical failure mode in this kit; it is the one it has actually had,
//  twice, and both times the sample was the defect rather than the law:
//
//    - Phase 100 measured the fold-confluence pack producing 150 halting trials
//      out of 150. The folding branch of the law never executed, and a family
//      whose whole purpose is to certify that a CLEAN fold is arrival-order
//      invariant certified nothing about clean folds. It surfaced only because
//      that pack happened to ship a hand-written coverage guard.
//    - Phase 115 measured the incremental-equivalence family drawing tables of
//      one to five rows, most of them holding ONE row. No tie between a named
//      and an unnamed row ever arose, so a merge with no stability tiebreak
//      passed every seed. Nothing in the kit was watching the table SIZE at all.
//
//  Both were found by someone who happened to look. This module makes looking a
//  law: a family DECLARES what its sample must contain, and a sample that does
//  not contain it fails the family loudly rather than certifying it quietly.
//
//  Two demand shapes, because the two findings above are two different questions:
//  a VERDICT the laws branch on must be reached (Phase 100's), and a per-sample
//  MEASURE must reach the width the law needs (Phase 115's — a configurable
//  minimum, named per family, because only the family knows what its law reads).
//
//  ---- What this guard does NOT claim ---------------------------------------
//  Reaching a verdict once is not evidence that the verdict is well covered. A
//  coverage guard is satisfied by one folding trial in three hundred, and Phase
//  106 recorded exactly that trap: re-pinning to a "lucky" seed leaves a law
//  certified by a single trial and reports green. So the remedy for a red guard
//  is to WIDEN THE GENERATOR, never to raise the iteration count or hunt a seed
//  until the counts turn positive — the counterexamples say so in those words.
//
//  FSharp.Core only, Fable-clean.
// ============================================================================

/// One law's verdict. `Counterexample` carries the seed + iteration so a failure is
/// reproducible (deterministic seed-replay).
///
/// It lives beside the adequacy guard rather than beside the laws because it is what BOTH produce,
/// and because the guard must be compiled ahead of every family that declares demands through it.
type LawResult =
    { Law: string
      Passed: bool
      Counterexample: string option }

/// One thing a family's SAMPLE must contain for the family's laws to have been tested at all.
/// A family declares its demands beside its laws; the kit runs them alongside.
type AdequacyDemand<'Sample> =
    /// Every verdict the laws distinguish along `dimension` must be reached by at least one
    /// sample. `classify` names the verdict(s) one sample reached (empty = none of them).
    | ReachesEvery of dimension: string * verdicts: string list * classify: ('Sample -> string list)
    /// Some sample must measure at least `atLeast` on `measure` — the width the law needs, which
    /// only the family knows. A generator whose widest draw falls short has not tested it.
    | Spans of measure: string * atLeast: int * measureOf: ('Sample -> int)

/// How a law family in this kit answers "could this run's sample have missed a verdict the laws
/// distinguish?". Every family answers it — see `SampleAdequacy.census`.
type AdequacyClass =
    /// The family carries an adequacy guard: one of its own laws goes red when the sample missed a
    /// verdict. The strings name the guarded dimensions / measures.
    | Guarded of dimensions: string list
    /// Every iteration exercises every verdict the laws distinguish, by construction — the laws are
    /// unconditional per-iteration assertions, or the evidence for each branch is BUILT rather than
    /// drawn. `why` says what makes that true, so the next reader can check it rather than trust it.
    | Unconditional of why: string

/// The adequacy guard. A family supplies either its whole sample plus `AdequacyDemand`s
/// (`check`), or — when it never materialises a sample list — the counts it already keeps
/// (`reached` / `spanned`).
module SampleAdequacy =

    let private renderCounts (counts: (string * int) list) : string =
        counts |> List.map (fun (v, n) -> v + "=" + string n) |> String.concat " "

    /// The standing remedy, in the words Phase 106 had to learn: a coverage guard is satisfied by
    /// one trial in three hundred, so turning it green by re-seeding or by iterating harder leaves
    /// the law certified by that one trial.
    let private remedy =
        " — the law that reads it was never tested; WIDEN THE GENERATOR (raising the iteration count, or hunting a seed until the count turns positive, leaves the law certified by one trial)"

    /// A verdict-coverage law over counts the family already keeps. Fails when any declared verdict
    /// was reached zero times — and when the family declared NO verdicts at all, which is a demand
    /// that demands nothing rather than a family with nothing to demand.
    let reached (family: string) (dimension: string) (seed: int) (counts: (string * int) list) : LawResult =
        let missed = counts |> List.filter (fun (_, n) -> n <= 0) |> List.map fst

        { Law =
            "sample adequacy ("
            + family
            + "): the sample reached every "
            + dimension
            + " the laws distinguish"
          Passed = not (List.isEmpty counts) && List.isEmpty missed
          Counterexample =
            if List.isEmpty counts then
                Some(
                    "seed="
                    + string seed
                    + ": "
                    + dimension
                    + " declared no verdicts, so it demands nothing"
                )
            elif List.isEmpty missed then
                None
            else
                Some(
                    "seed="
                    + string seed
                    + ": "
                    + dimension
                    + " reached "
                    + renderCounts counts
                    + " — never reached "
                    + String.concat ", " missed
                    + remedy
                ) }

    /// A span law over a measure the family already keeps: the widest sample must reach `atLeast`.
    let spanned (family: string) (measure: string) (atLeast: int) (seed: int) (widest: int) (n: int) : LawResult =
        { Law =
            "sample adequacy ("
            + family
            + "): the sample spans the "
            + measure
            + " range the laws need (at least "
            + string atLeast
            + ")"
          Passed = widest >= atLeast
          Counterexample =
            if widest >= atLeast then
                None
            else
                Some(
                    "seed="
                    + string seed
                    + ": over "
                    + string n
                    + " sample(s) the widest "
                    + measure
                    + " was "
                    + string widest
                    + ", and the law needs at least "
                    + string atLeast
                    + remedy
                ) }

    /// Run a family's declared demands over its sample. One `LawResult` per demand, in declaration
    /// order, carrying the COUNTS — a vacuous family fails saying what it did reach, which is what
    /// tells the reader which way to widen.
    let check
        (family: string)
        (seed: int)
        (demands: AdequacyDemand<'Sample> list)
        (samples: 'Sample list)
        : LawResult list =
        demands
        |> List.map (fun demand ->
            match demand with
            | ReachesEvery(dimension, verdicts, classify) ->
                let tagged = samples |> List.map classify

                let counts =
                    verdicts
                    |> List.map (fun v -> v, (tagged |> List.filter (List.contains v) |> List.length))

                reached family dimension seed counts
            | Spans(measure, atLeast, measureOf) ->
                let widest = samples |> List.fold (fun acc s -> max acc (measureOf s)) 0

                spanned family measure atLeast seed widest (List.length samples))

    /// Every law family this kit ships, and how it answers the adequacy question. A family that
    /// distinguishes a verdict its sample can miss is `Guarded`; one whose evidence is BUILT each
    /// iteration rather than drawn is `Unconditional`, with the reason stated so the classification
    /// can be checked rather than trusted.
    ///
    /// It is a DECLARATION rather than a derivation, so the one thing it cannot do on its own is
    /// notice a family nobody enrolled — the blind spot any manifest-quantified check structurally
    /// has. The kit's own suite closes that half by reflecting over the public law entry points and
    /// refusing any name this list does not carry, so a family added without answering the question
    /// fails to ship rather than passing silently.
    ///
    /// `WireNullTolerance` is deliberately absent: it runs a FIXED vector corpus, so it has no
    /// sample that could miss anything, and enrolling a family with no sample would make the census
    /// claim something weaker than it does.
    let census: (string * AdequacyClass) list =
        [
          // ---- guarded: a law branches on something the sample can miss ----
          "IncrementalDelta.lawsWith", Guarded [ "refresh class"; "source rows" ]
          "IncrementalDelta.laws", Guarded [ "refresh class"; "source rows (delegates to lawsWith)" ]
          "FoldConfluence.laneFoldLawsWith", Guarded [ "lane-fold outcome" ]
          "FoldConfluence.laneFoldLaws", Guarded [ "lane-fold outcome (delegates to laneFoldLawsWith)" ]
          "Conformance.footprintLaws", Guarded [ "script-pair independence" ]
          "Conformance.mergeConflictLaws", Guarded [ "op-pair interference" ]
          "Conformance.reconcileLaws", Guarded [ "reconcile outcome"; "delta-pair independence" ]
          "Conformance.arbitrationLaws", Guarded [ "arbitration bucket" ]
          "Conformance.capabilityPipelineIncrementalLaws", Guarded [ "node reuse" ]
          "Conformance.dirtyPropagationLaws", Guarded [ "dirty frontier" ]
          "Conformance.propagationEvalLaws", Guarded [ "node reuse" ]
          "Conformance.concurrencyLawsWith", Guarded [ "independent pair (its own Phase 80 vacuity guard)" ]
          "Conformance.concurrencyLaws", Guarded [ "independent pair (delegates to concurrencyLawsWith)" ]
          "Conformance.schemaWalkLaws", Guarded [ "derivation verdict (its own parity vacuity guard)" ]

          // ---- unconditional: every iteration builds the evidence for every branch ----
          "Conformance.witnessLaws", Unconditional "each iteration rebuilds a drawn node and re-reads every accessor"
          "Conformance.streamLaws", Unconditional "each iteration applies, replays and tampers the same chain"
          "Conformance.diffLaws", Unconditional "each iteration diffs a pair and re-applies the emitted script"
          "Conformance.normalizeLaws", Unconditional "each iteration normalises a drawn script and compares both ways"
          "Conformance.snapshotLawsWith", Unconditional "each iteration takes a snapshot and replays across it"
          "Conformance.snapshotLaws", Unconditional "delegates to snapshotLawsWith"
          "Conformance.dagLaws", Unconditional "each iteration builds, replays, tampers and round-trips one DAG"
          "Conformance.captureReplayLaws", Unconditional "each iteration records, replays and tampers one session"
          "Conformance.transformLaws",
          Unconditional "each iteration compares the host evaluator against the reference on the same input"
          "Conformance.capabilityLaws",
          Unconditional "each iteration exercises accept, reject and unknown-arg on a built declaration"
          "Conformance.queryLaws",
          Unconditional "each iteration exercises accept, type-mismatch and unknown-param on a built declaration"
          "Conformance.compositionLaws",
          Unconditional "each iteration composes a drawn pair and compares against the nested application"
          "Conformance.functionVerifyLaws",
          Unconditional "each iteration verifies a SOUND and a BROKEN function, both caller-supplied"
          "Conformance.memoLaws",
          Unconditional "each iteration forces a miss then a hit, and an effecting bypass, by construction"
          "Conformance.registryLaws",
          Unconditional "each iteration queries matching and non-matching signatures on a built registry"
          "Conformance.packLoadingLaws",
          Unconditional "each iteration loads a pack and refuses a stale pin and an unknown base"
          "Conformance.aggregateParityLaws",
          Unconditional "each iteration compares aggregate against a single-group groupBy on the same column"
          "Conformance.columnarOpLaws",
          Unconditional "each iteration applies, inverts, chains and replays the same table edit"
          "Conformance.columnarValidatorLaws", Unconditional "each iteration injects a known fault count and validates"
          "Conformance.incrementalLaws",
          Unconditional "each iteration compares evalFrom against a full evalPipeline over the same change"
          "Conformance.paramLaws",
          Unconditional "each iteration binds a param, leaves one unbound, and round-trips the pipeline"
          "Conformance.deferredLaws", Unconditional "each iteration round-trips Pending, Ready and Failed"
          "Conformance.capabilityPipelineLaws",
          Unconditional "each iteration type-checks a well-typed and an ill-typed edge"
          "Conformance.verifyHonestyLaws",
          Unconditional "each iteration verifies a stochastic and an under-declared function, both caller-supplied"
          "Conformance.memoSoundnessLaws",
          Unconditional "each iteration applies the caller-supplied under-declared function twice"
          "Conformance.canonicalFloatLaws",
          Unconditional "each iteration renders a drawn float and the three non-finite tokens"
          "Conformance.encoderInjectivityLaws", Unconditional "each iteration hashes a drawn pair of trees"
          "Conformance.projectionLaws", Unconditional "each iteration projects, re-imports and scopes the same tree"
          "Conformance.aiSurfaceLaws",
          Unconditional "each iteration walks the catalogue and exercises approved, denied and unknown"
          "Conformance.attestationLaws",
          Unconditional "each iteration signs a head and forges both an op and an attribution"
          "Conformance.noAttestationVacuityLaws",
          Unconditional "each iteration asks the no-op sink to sign and to verify"
          "Conformance.hashFnLaws", Unconditional "each iteration reorders, drops and bit-flips the same chain"
          "Conformance.hashFnAdversarialLaws",
          Unconditional "the budget IS the sample size, and it is the caller's own declared parameter"
          "Conformance.attributedLaws",
          Unconditional "each iteration lifts, re-attributes and round-trips the same stream"
          "Conformance.leaseLaws", Unconditional "the conflict and expiry witnesses are BUILT each iteration, not drawn"
          "Conformance.casLaws", Unconditional "each iteration appends at the true head, at a stale head, and races two"
          "Conformance.idempotencyLaws",
          Unconditional "each iteration appends a fresh key then re-sends it under both heads" ]
