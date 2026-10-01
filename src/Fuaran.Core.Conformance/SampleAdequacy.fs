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

    /// `reached`, with a second set of counts the family KEEPS but does not DEMAND rendered beside
    /// the demanded ones — Phase 245. An outcome the laws tolerate without being about it (a lane
    /// set the reducer rejects under every arrival order) must not be demanded, or every domain
    /// that never produces one starves; but it must be COUNTED, or a sample made of little else
    /// reads the same as one that exercised the laws. The law text is `reached`'s, so a guard does
    /// not change its name by counting more. Internal until a second family needs it.
    let internal reachedBeside
        (family: string)
        (dimension: string)
        (seed: int)
        (counts: (string * int) list)
        (beside: (string * int) list)
        : LawResult =
        let missed = counts |> List.filter (fun (_, n) -> n <= 0) |> List.map fst

        let besideText =
            if List.isEmpty beside then
                ""
            else
                " (counted beside them, not demanded: " + renderCounts beside + ")"

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
                    + besideText
                    + " — never reached "
                    + String.concat ", " missed
                    + remedy
                ) }

    /// `reached`, with every count also held to a FRACTION of the attempts it was drawn out of —
    /// Phase 302. A count that is positive is not evidence the arm is well covered: an arm built
    /// once in two hundred attempts satisfies `reached`, and a run that skipped it 199 times read
    /// adequate. Each entry is `(verdict, reached, outOf, oneIn)` and demands `reached ≥ 1` and
    /// `reached × oneIn ≥ outOf` — at least one in `oneIn`. The law text is `reached`'s, so a guard
    /// does not change its name by demanding more. Internal until a second family needs it.
    let internal reachedFraction
        (family: string)
        (dimension: string)
        (seed: int)
        (counts: (string * int * int * int) list)
        : LawResult =
        let short (_, n, outOf, oneIn) = n <= 0 || n * oneIn < outOf
        let missed = counts |> List.filter short

        let render (v, n, outOf, oneIn) =
            v
            + "="
            + string n
            + " of "
            + string outOf
            + " (needs at least 1 in "
            + string oneIn
            + ")"

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
                    + (counts |> List.map render |> String.concat " ")
                    + " — too rarely: "
                    + (missed |> List.map (fun (v, _, _, _) -> v) |> String.concat ", ")
                    + remedy
                ) }

    /// A verdict-coverage law over counts the family already keeps. Fails when any declared verdict
    /// was reached zero times — and when the family declared NO verdicts at all, which is a demand
    /// that demands nothing rather than a family with nothing to demand.
    let reached (family: string) (dimension: string) (seed: int) (counts: (string * int) list) : LawResult =
        reachedBeside family dimension seed counts []

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
    //  exercised twelve hundred cases and one that exercised none, and the maintainers' own records
    //  name that class twice already. What follows is the measurement leaving the run: a family's
    //  results and the iterations they were driven over, read through the family's OWN census
    //  class, into one record a census can render as a column.
    //
    //  It is a DERIVATION and deliberately not a second registry. Nothing new declares which
    //  families exist or what they guard — the roster (`Families`) is the only answer to both — and
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
    let vacuousToken = CaseCell.vacuous

    /// The opening of the counterexample a law carries when it asserted NOTHING in its run — the
    /// runner's non-degeneracy rule (Phase 302, carried by Phase 297's `LawCell`): a law asserted
    /// only inside an arm no guard counts, and never reached, reds ITSELF rather than reporting a
    /// green it never earned. `cases` reads it back as starvation, whatever the family's class,
    /// because a subject law that was never asserted is vacuity on the side that law is about.
    [<Literal>]
    let neverReached = "never reached"

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

        // Phase 297 — a subject law the runner reports NEVER REACHED is starved in its own right,
        // under either class: it is the one red a gated arm with no counting guard can show, and a
        // census cell that rendered a number over it would claim cases the law never saw.
        let unreached =
            subject
            |> List.filter (fun r ->
                not r.Passed
                && (match r.Counterexample with
                    | Some cx -> hasPrefix neverReached cx
                    | None -> false))
            |> List.map (fun r -> r.Law)

        let guardStarved =
            match klass with
            // An `Unconditional` family declares that every iteration BUILDS the evidence for
            // every branch, and the suite holds that declaration to the tree. So it carries no
            // guard to starve: it is vacuous by running nothing at all, which `Cases` already says,
            // or by a law the runner reports never reached, which `unreached` above says.
            | Unconditional _ -> []
            // A `Guarded` family's guard is the measurement. A red guard law names a dimension the
            // sample never reached — vacuity on the side the law is about, which a total cannot
            // show. The prefix is stripped because a census cell wants the dimension, not the
            // sentence the guard failed in.
            | Guarded _ ->
                results
                |> List.filter (fun r -> isGuard r && not r.Passed)
                |> List.map (fun r -> dimension r.Law)

        let starved = guardStarved @ unreached

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
    let renderCases (c: CaseCount) : string = CaseCell.render c

    /// Every law family this kit ships, and how it answers the adequacy question. A family that
    /// distinguishes a verdict its sample can miss is `Guarded`; one whose evidence is BUILT each
    /// iteration rather than drawn is `Unconditional`, with the reason stated so the classification
    /// can be checked rather than trusted.
    ///
    /// A PROJECTION since Phase 297 — `Families.census`, read off each family's own `Adequacy`
    /// field. Until then it was a declaration beside the roster, held equal to it only by a test, and
    /// a family could be enrolled in one and not the other; now there is one row per family and this
    /// list cannot name a family the roster does not carry. The suite also holds each class to what
    /// the family EMITS at the reference witness: a `Guarded` family emits a guard labelled with its
    /// roster id, and an `Unconditional` family emits none.
    ///
    /// Since Phase 257 this is THIS package's share. The families that read the dataframe layer
    /// ship from `Fuaran.Core.DataFrame.Conformance`, and their rows ship with them, in
    /// `DataFrameFamilies.roster` — a package the compute repository produces since Phase 258
    /// (DECISIONS.md D66), whose suite holds that share.
    let census: (string * AdequacyClass) list = Families.census
