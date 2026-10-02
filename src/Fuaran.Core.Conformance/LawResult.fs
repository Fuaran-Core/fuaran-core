namespace Fuaran.Core

// ============================================================================
//  The kit's verdict and adequacy types (Phase 297 — moved out of
//  SampleAdequacy.fs, unchanged). They compile FIRST because two modules read
//  them and one of those reads the other: the roster (`Families`) declares
//  every family's adequacy class, and the guard module (`SampleAdequacy`)
//  projects its census from the roster. See SampleAdequacy.fs for what the
//  guard is and why it exists.
//
//  FSharp.Core only, Fable-clean.
// ============================================================================

/// One law's verdict. `Counterexample` carries the seed + iteration so a failure is
/// reproducible (deterministic seed-replay).
///
/// It lives beside the adequacy guard rather than beside the laws because it is what BOTH produce,
/// and because the guard must be compiled ahead of every family that declares demands through it.
type LawResult =
    {
        /// The law's human-readable statement, stable across runs — the text a report and a
        /// census name the law by.
        Law: string
        /// `true` when the law held on every assertion made. Zero assertions is not a pass: the
        /// kit's runner reports such a law red unless an adequacy guard covers it.
        Passed: bool
        /// `None` exactly when `Passed`. The FIRST failure recorded, or the "never reached"
        /// remedy when the law was not asserted at all.
        Counterexample: string option
    }

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
        /// sample never reached, named without their `sample adequacy (<family>): ` prefix — and,
        /// since Phase 297, every subject law the runner reports never reached, named by its text.
        /// Empty on a run that reached everything it declared.
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

/// How a `CaseCount` renders as one census cell — shared by the guard module's public
/// `SampleAdequacy.renderCases` and the roster's generated renderings, which compile on either side
/// of it. Internal: the public spelling is `SampleAdequacy.renderCases` / `vacuousToken`.
module internal CaseCell =

    /// What a census cell reads when the count is zero, or when a guarded dimension was starved.
    [<Literal>]
    let vacuous = "vacuous"

    /// One census cell. `vacuous` — never a number — when the run certified nothing, naming the
    /// starved dimensions when it can. A `|` in a dimension name is escaped, so the cell cannot
    /// break the table it is rendered into.
    let render (c: CaseCount) : string =
        let escape (s: string) = s.Replace("|", "\\|")

        if not (List.isEmpty c.Starved) then
            vacuous + " (" + (c.Starved |> List.map escape |> String.concat "; ") + ")"
        elif c.Cases <= 0 then
            vacuous
        else
            string c.Cases
