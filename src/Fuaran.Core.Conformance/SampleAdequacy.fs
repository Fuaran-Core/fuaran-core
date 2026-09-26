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

/// What one law family's RUN actually exercised — Phase 196. The adequacy guard above asserts
/// adequacy *inside* a run and then throws the measurement away, so a green family and a family
/// that ran nothing report the same thing to anyone reading the outside. This record is that
/// measurement, emitted.
///
/// `Cases` is the number of law assertions the run MADE: the subject laws it reported, times the
/// iterations each was driven over. For an `Unconditional` family that is exactly the number of
/// cases exercised, because every iteration builds the evidence for every branch — which is what
/// the class asserts and what the suite holds to the tree. For a `Guarded` family it is the run's
/// SIZE, and `Starved` is the measurement that matters there: a dimension the sample never
/// reached is vacuity on the side the law is about, and no total can show it. That is why both
/// fields are carried and why `isVacuous` reads both.
type CaseCount =
    {
        /// The roster key — `"<Module>.<Entry>"`, the spelling a census cell uses.
        Family: string
        /// Subject-law assertions made: laws reported that are not the guard's own, times
        /// iterations.
        Cases: int
        /// The guarded dimensions whose own adequacy law went red — the sides of the family the
        /// sample never reached, named without their `sample adequacy (<family>): ` prefix. Empty
        /// on a run that reached everything it declared.
        Starved: string list
    }

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

    /// The opening of every law THIS module emits, and the one structural mark that separates a
    /// guard's own verdict from a family's subject laws. Phase 196 reads it back rather than
    /// parsing law prose: the guard writes the opening, so the guard can recognise it.
    ///
    /// The recognition is on the OPENING and not on the whole `sample adequacy (<family>): `
    /// prefix, because a delegating family emits its delegate's name — `columnarOpLaws` runs
    /// `columnarOpLawsWith`, `IncrementalDelta.laws` runs `lawsWith`, `snapshotLaws` runs
    /// `snapshotLawsWith` — and that guard is still the caller's own verdict, which is exactly
    /// what the census's "(delegates to …)" rows already say. Keying on the name would read those
    /// as subject laws and lose the starvation they report.
    [<Literal>]
    let guardOpening = "sample adequacy ("

    /// The full opening one family's own guard laws carry.
    let lawPrefix (family: string) : string = guardOpening + family + "): "

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
            lawPrefix family
            + "the sample reached every "
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
            lawPrefix family
            + "the sample spans the "
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

    // ---- Phase 196: the measurement, emitted ---------------------------------------------------
    //
    //  Everything above asserts adequacy INSIDE a run and then discards what it measured. A
    //  consumer's conformance census therefore renders the same "adopted" cell for a family that
    //  exercised twelve hundred cases and one that exercised none, and the estate's own memory
    //  names that class twice already. What follows is the measurement leaving the run: a family's
    //  results and the iterations they were driven over, read through the family's OWN census
    //  class, into one record a census can render as a column.
    //
    //  It is a DERIVATION and deliberately not a second registry. Nothing new declares which
    //  families exist or what they guard — `census` above is still the only answer to both — and
    //  nothing in a family's signature changes, which is what makes the column additive for every
    //  consumer already pinned to this kit.

    /// A plain prefix test, spelled out rather than taken from `String.StartsWith` so it behaves
    /// identically under Fable and under .NET with no culture in the argument list.
    let private hasPrefix (p: string) (s: string) : bool =
        s.Length >= p.Length && s.Substring(0, p.Length) = p

    /// What a census cell reads when the count is zero, or when a guarded dimension was starved.
    /// One spelling, exported, because a consumer's census and the registry that grades it must
    /// agree on the word without either of them inventing it.
    [<Literal>]
    let vacuousToken = "vacuous"

    /// The cases one family's run exercised — the measurement this module has always taken and
    /// never emitted.
    ///
    /// `results` is exactly what the family answered with, `iterations` the count it was driven
    /// over (for a family whose sample is a corpus rather than a draw, the corpus size — the
    /// caller knows which). The family's own `AdequacyClass` decides how the two are read, which
    /// is what "built on the census, not beside it" means here: a class that changes changes this.
    let cases (family: string) (klass: AdequacyClass) (iterations: int) (results: LawResult list) : CaseCount =
        let isGuard (r: LawResult) = hasPrefix guardOpening r.Law

        /// The guard's sentence without the `sample adequacy (<family>): ` it opens with — a
        /// census cell wants the dimension, not the sentence it failed in. The family name inside
        /// the parentheses is whichever entry point actually ran (see `guardOpening`), so the cut
        /// is at the closing `): ` rather than at a name this function assumes.
        let dimension (law: string) =
            let at = law.IndexOf "): "

            if at < 0 then law else law.Substring(at + 3)

        let subject = results |> List.filter (isGuard >> not)

        let starved =
            match klass with
            // An `Unconditional` family declares that every iteration BUILDS the evidence for
            // every branch, and the suite holds that declaration to the tree. So the only way it
            // can be vacuous is by running nothing at all, which `Cases` already says.
            | Unconditional _ -> []
            // A `Guarded` family's guard is the measurement. A red guard law names a dimension the
            // sample never reached — vacuity on the side the law is about, which a total cannot
            // show. The prefix is stripped because a census cell wants the dimension, not the
            // sentence the guard failed in.
            | Guarded _ ->
                results
                |> List.filter (fun r -> isGuard r && not r.Passed)
                |> List.map (fun r -> dimension r.Law)

        { Family = family
          Cases = List.length subject * (max 0 iterations)
          Starved = starved }

    /// Did this run certify nothing? Either it made no assertion at all, or a guarded dimension
    /// was starved — both are green runs that tested nothing on the side they exist for, and a
    /// census that renders either as a pass is the defect this phase closes.
    let isVacuous (c: CaseCount) : bool =
        c.Cases <= 0 || not (List.isEmpty c.Starved)

    /// One census cell. `vacuous` — never a number — when the run certified nothing, naming the
    /// starved dimensions when it can, because "vacuous" without them sends a reader to the wrong
    /// remedy (see `remedy` above: widen the generator, do not iterate harder).
    ///
    /// A `|` in a dimension name is escaped, so the cell cannot break the table it is rendered
    /// into.
    let renderCases (c: CaseCount) : string =
        let escape (s: string) = s.Replace("|", "\\|")

        if not (List.isEmpty c.Starved) then
            vacuousToken + " (" + (c.Starved |> List.map escape |> String.concat "; ") + ")"
        elif c.Cases <= 0 then
            vacuousToken
        else
            string c.Cases

    /// Every law family this kit ships, and how it answers the adequacy question. A family that
    /// distinguishes a verdict its sample can miss is `Guarded`; one whose evidence is BUILT each
    /// iteration rather than drawn is `Unconditional`, with the reason stated so the classification
    /// can be checked rather than trusted.
    ///
    /// It is a DECLARATION rather than a derivation, so the one thing it cannot do on its own is
    /// notice a family nobody enrolled — the blind spot any manifest-quantified check structurally
    /// has. The kit's own suite closes that half by holding this list equal to `Families` — the
    /// roster whose own completeness is quantified over RETURN TYPE across the shipped assembly
    /// (Phase 184) — so a family added without answering the question fails to ship rather than
    /// passing silently. Until that phase the same half was closed by reflecting over method NAMES
    /// ending in `Laws`, which is why three families sat outside this list unnoticed.
    ///
    /// `WireNullTolerance` is deliberately absent: it runs a FIXED vector corpus, so it has no
    /// sample that could miss anything, and enrolling a family with no sample would make the census
    /// claim something weaker than it does.
    let census: (string * AdequacyClass) list =
        [
          // ---- guarded: a law branches on something the sample can miss ----
          // Phase 212 — the third dimension is the shape that let a wrong answer reach a published
          // release: a ROW-LOCAL step reading a column a cross-row step appended, per producer
          // class. The corpus carried ten window-bearing pipelines and not one of them, so the
          // family that exists to see that defect certified green against an evaluator carrying it.
          "IncrementalDelta.lawsWith", Guarded [ "refresh class"; "cross-row column read"; "source rows" ]
          "IncrementalDelta.laws",
          Guarded
              [ "refresh class"
                "cross-row column read"
                "source rows (delegates to lawsWith)" ]
          "FoldConfluence.laneFoldLawsWith", Guarded [ "lane-fold outcome" ]
          "FoldConfluence.laneFoldLaws", Guarded [ "lane-fold outcome (delegates to laneFoldLawsWith)" ]
          "Conformance.footprintLaws", Guarded [ "script-pair independence" ]
          "Conformance.mergeConflictLaws", Guarded [ "op-pair interference" ]
          "Conformance.reconcileLaws", Guarded [ "reconcile outcome"; "delta-pair independence" ]
          "Conformance.arbitrationLaws", Guarded [ "arbitration bucket" ]
          "Conformance.capabilityPipelineIncrementalLaws", Guarded [ "node reuse" ]
          "Conformance.dirtyPropagationLaws", Guarded [ "dirty frontier" ]
          // Phase 209 — the second dimension is the undeclared-read refusal: a real node read
          // without being declared, and an id the map does not hold at all.
          "Conformance.propagationEvalLaws", Guarded [ "node reuse"; "undeclared read" ]
          // Phase 211 — the same contract, at a DOMAIN'S evaluator. Every arm the agreement law
          // distinguishes is DRAWN from the domain's own edits: a change that reached a reader, a
          // clean node reused from `prior`, and an edited evaluator that failed. A domain whose edits
          // all move the dependency map reaches none of them, and the guard says so.
          "Conformance.propagationEvaluatorLaws", Guarded [ "evaluator edit" ]
          // Phase 250 — the prior-aware evaluator: a recomputed node handed its prior (the prior
          // path, not only priming) and a clean node reused from it. It runs the family above first,
          // so the reference arms carry that family's guard too.
          "Conformance.propagationEvaluatorLawsWith", Guarded [ "evaluator edit"; "prior-aware edit" ]
          // Phase 181. Every other arm is BUILT each iteration — an op applied, inverted, chained
          // and replayed — but the inverse-only-for-applicable law is about the ops the table
          // REFUSES, and whether the generator refused an INVERTIBLE one is a property of the run.
          "Conformance.columnarOpLawsWith", Guarded [ "invert's refusal population" ]
          "Conformance.columnarOpLaws", Guarded [ "invert's refusal population (delegates to columnarOpLawsWith)" ]
          "Conformance.concurrencyLawsWith", Guarded [ "independent pair (its own Phase 80 vacuity guard)" ]
          "Conformance.concurrencyLaws", Guarded [ "independent pair (delegates to concurrencyLawsWith)" ]
          "Conformance.schemaWalkLaws", Guarded [ "derivation verdict (its own parity vacuity guard)" ]
          // Phase 161. Every arm is BUILT — a perturbed child list, an operation over a drawn tree,
          // a graft carrying its own interior offender — but whether the WITNESS honours the rebuild
          // is drawn, and a witness whose `ReplaceChildren` is partial on leaves reaches none of
          // them. So the family counts what each arm actually reached and emits the guard, rather
          // than claiming an unconditionality it cannot have over an arbitrary witness.
          "Conformance.containerLaws", Guarded [ "built arm (child perturbation / invariant probe / interior graft)" ]

          // Phase 189 — the same shape, one axis further out: the collision arms are BUILT through
          // the domain's own `PlaceKeyedChild`, and whether the witness honours a placement is the
          // domain's to answer. A witness that declares NO keyed position is the one case that is
          // not an unreached arm — it is a declaration that there is nothing to reach — and the
          // family reports that as its adequacy line rather than as a missed verdict.
          "Conformance.keyedChildrenLaws",
          Guarded [ "built arm (clean full walk / keyed id in the surface / one id in two keyed positions)" ]

          // Phase 196 — moved out of `Unconditional`, where it had sat since this census was
          // written, and the reclassification is the finding rather than a tidy-up. "Each
          // iteration signs a head and forges both an op and an attribution" is true of a SIGNING
          // sink and false of `OpStream.noAttestation`, under which four of the five laws assert
          // nothing and all five still report green. The sink is a PARAMETER, so whether the
          // evidence is built is a property of the run — which is what `Guarded` means, and the
          // family that certifies the unsigned path is `noAttestationVacuityLaws` beside it.
          "Conformance.attestationLaws", Guarded [ "signing outcome" ]

          // Phase 223 — the six drawn-refusal families Phase 220's audit (`Families.refusalAudit`)
          // found and left for this phase. Each was `Unconditional` on the strength of what every
          // iteration BUILDS, and each also carries a law that compares a REFUSED outcome — an
          // agreement or an iff that holds trivially when nothing was refused — over a population
          // the run DRAWS. So each counts its accepted and refused cases and emits the guard, and
          // each kit reference generator is stratified so the guard never fires on it.
          //
          // `match ≡ append` compares a domain refusal with a CAS `Domain` rejection only when the
          // caller's StreamGen draws a refused op.
          "Conformance.casLaws", Guarded [ "accepted"; "refused" ]
          // `fresh ≡ append` and the true-head CAS arm forward a domain refusal verbatim only when
          // the drawn fresh op is refused; `Duplicate` and `StaleHead` beside them are built.
          "Conformance.idempotencyLaws", Guarded [ "accepted"; "refused" ]
          // `explainRejection` and the rejected arms of the allow / approve parity read a reducer
          // rejection only when the caller's op generator draws one; the decision axis, the unknown
          // tool and the unknown proposal id are built.
          //
          // Phase 246 — `aiSurfaceLaws` runs the DOMAIN'S `Decide`, so which proposal arm a drawn op
          // reaches is the domain's policy's answer: a policy that never parks or never denies
          // leaves those arms untested, and one that allows everything is exactly that. The kit-
          // policy form rolls the decision itself and keeps the two Phase 223 dimensions.
          "Conformance.aiSurfaceLaws", Guarded [ "accepted"; "refused"; "allowed"; "parked"; "denied" ]
          "Conformance.aiSurfaceLawsUnderKitPolicy", Guarded [ "accepted"; "refused" ]
          // Phase 246 — the seam families at a domain's seam: every outcome is a call the domain's
          // generator DRAWS, so each of the three is a dimension the run can miss.
          "Conformance.capabilityLawsWith", Guarded [ "settled"; "pending"; "refused" ]
          "Conformance.queryLawsWith", Guarded [ "settled"; "pending"; "refused" ]
          // The default-deny arms are BUILT, but per drawn `Invoke` node — a generator of Source-only
          // pipelines builds none of them.
          "Conformance.capabilityPipelineLawsWith", Guarded [ "invoke node" ]
          // `evalFrom` answers every change but a value edit by evaluating in full, so only a value
          // edit can tell the incremental path from the full one.
          "Conformance.incrementalLawsWith", Guarded [ "value edit" ]
          // The Error/Error arm of the parity law is reached only when the caller's generator yields
          // a pipeline the reference refuses.
          "Conformance.transformLaws", Guarded [ "accepted"; "refused" ]
          // The kit draws its own sample here, and a fault-free draw satisfies the soundness law as
          // 0 = 0. The roll is stratified by iteration index, so three iterations reach both faults;
          // a shorter run can still miss them, which is why the class is not `Unconditional`.
          "Conformance.columnarValidatorLaws", Guarded [ "null cell"; "out-of-range cell" ]
          // The refusal iff reads the drawn pair (and the minted probe) under the witness's
          // `canHold`; a canHold that refuses nothing, or none at all, exercises only its trivial
          // direction — the "rather than missing a branch" this row used to excuse.
          "Conformance.diffContainedLaws", Guarded [ "accepted"; "refused" ]

          // ---- unconditional: every iteration builds the evidence for every branch ----
          "Conformance.witnessLaws", Unconditional "each iteration rebuilds a drawn node and re-reads every accessor"
          "Conformance.streamLaws", Unconditional "each iteration applies, replays and tampers the same chain"
          "Conformance.diffLaws", Unconditional "each iteration diffs a pair and re-applies the emitted script"
          "Conformance.normalizeLaws", Unconditional "each iteration normalises a drawn script and compares both ways"
          "Conformance.snapshotLawsWith", Unconditional "each iteration takes a snapshot and replays across it"
          "Conformance.snapshotLaws", Unconditional "delegates to snapshotLawsWith"
          "Conformance.dagLaws", Unconditional "each iteration builds, replays, tampers and round-trips one DAG"
          "Conformance.captureReplayLaws", Unconditional "each iteration records, replays and tampers one session"
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
          "Conformance.codecInjectivityLaws",
          Unconditional
              "the left-inverse law is BUILT by every iteration — one drawn op round-tripped through the domain's own Decode, and a codec with a total left inverse is injective — so the family's weight does not rest on the collision search beside it, whose own third law fails when the draw was too narrow to compare anything"
          "Conformance.projectionLaws", Unconditional "each iteration projects, re-imports and scopes the same tree"
          "Conformance.noAttestationVacuityLaws",
          Unconditional "each iteration asks the no-op sink to sign and to verify"
          "Conformance.hashFnLaws", Unconditional "each iteration reorders, drops and bit-flips the same chain"
          "Conformance.hashFnAdversarialLaws",
          Unconditional "the budget IS the sample size, and it is the caller's own declared parameter"
          "Conformance.attributedLaws",
          Unconditional "each iteration lifts, re-attributes and round-trips the same stream"
          "Conformance.slotParamLaws",
          Unconditional
              "each iteration BUILDS the bound, substituted, unbound, mistyped and literal-only runs over the same drawn table — the draw varies the table, the page size and which column is ordered on, never which branch is taken"
          "Conformance.nowLaws",
          Unconditional
              "each iteration BUILDS both grains, a clock-bearing pipeline and a clock-free one over the same input, and runs the constant-witness, counting-witness and unpinned cases — the draw varies the reading and the row count, never which branch is taken"
          "Conformance.chainBreakReasonLaws",
          Unconditional
              "each iteration BUILDS all three break kinds on both walkers — a renumbered sequence, a repointed prev-link, and a payload tampered with its sequence and link left intact — rather than drawing them, and the family's own last two laws fail if any kind was not actually observed"
          "Conformance.constructThenEncodeLaws",
          Unconditional
              "every corpus document is decoded, rebuilt through the authoring surface and re-encoded on every run — the sample is the caller's own corpus rather than a draw, and an empty one fails the family's own non-vacuity law instead of passing quietly"
          "Conformance.dagBreakReasonLaws",
          Unconditional
              "each iteration BUILDS both break kinds on the DAG walk — a node whose op is tampered with its map key left alone, and a named parent deleted — rather than drawing them, and the family's own last law fails if either kind was not actually observed"

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
          "Conformance.opAlgebra", Guarded [ "accepted"; "refused" ]
          "Conformance.reducer", Guarded [ "accepted"; "refused" ]
          "Conformance.compositionPilot",
          Unconditional
              "it runs `compositionLaws` (unconditional above) and BUILDS both applyMemo arms across the witness boundary each iteration — a closed inner sub-function memoised, and the composed outer compared against direct apply" ]
