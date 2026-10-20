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

/// What a law result IS (Phase 515) — the structural fact the kit used to spell into a result's text
/// and parse back. A reader matches on it; nothing reads the prose to decide it.
[<RequireQualifiedAccess>]
type LawKind =
    /// A law about the witness. `coveredBy` names the adequacy-guard dimension that reports this
    /// law's zero when its arm was never reached (the law then reads green at zero evidence and the
    /// guard beside it red); `None` is a STRICT law, which reds itself at zero evidence.
    | Subject of coveredBy: string option
    /// The adequacy guard over one dimension of the family's sample: red when the sample missed a
    /// verdict the laws distinguish. Its counts are the result's `Reached`.
    | Guard of dimension: string
    /// The non-emptiness law of a fixed corpus or a store — `what` names the item (`vector`,
    /// `record`, `node`, `capture`, `text`). Its `Evidence` is the number of items evaluated.
    | Corpus of what: string
    /// A side of the family that is vacuous BY DECLARATION: the witness declares nothing for it to
    /// reach (`what` names the missing capability, e.g. `keyed position`). Green, because that is the
    /// honest verdict, and its own kind, so a census never counts it as a law certified.
    | Declared of what: string

/// One law's verdict. `Counterexample` carries the seed + iteration so a failure is
/// reproducible (deterministic seed-replay).
///
/// Since Phase 515 the result carries what it is (`Kind`), which family emitted it (`Family`), how many
/// assertions it stands on (`Evidence`) and, for a guard, how often each side was reached (`Reached`) —
/// the four facts the kit used to encode in `Law`'s prose and parse back. Build one with the
/// constructors in the `LawResult` module rather than a record literal: they are the stable surface,
/// and a literal is what a future field would break.
///
/// It lives beside the adequacy guard rather than beside the laws because it is what BOTH produce,
/// and because the guard must be compiled ahead of every family that declares demands through it.
type LawResult =
    {
        /// The law's human-readable statement, stable across runs — the text a report and a
        /// census name the law by. Prose for a reader; no reader in the kit parses it.
        Law: string
        /// `true` when the law held on every assertion made. Zero assertions is not a pass: the
        /// kit's runner reports such a law red unless an adequacy guard covers it.
        Passed: bool
        /// `None` exactly when `Passed`. The FIRST failure recorded, or the "never reached"
        /// remedy when the law was not asserted at all.
        Counterexample: string option
        /// The roster key of the family whose run produced this result — `"<Module>.<Entry>"`, the
        /// spelling a census cell uses. Filled by the kit's runner (or the guard constructors, which
        /// take it), never parsed out of `Law`.
        Family: string
        /// What the result is: a subject law, an adequacy guard, a corpus law or a declared vacuity.
        Kind: LawKind
        /// The assertions the verdict stands on: for a subject law the times it was asserted (a
        /// strict law that failed at zero evidence is the never-reached verdict); for a guard the
        /// samples it counted toward its demand; for a corpus law the items evaluated; zero for a
        /// declared vacuity.
        Evidence: int
        /// A guard's counts — every verdict it demanded, then any it counts beside them without
        /// demanding — on the green path as on the red one, so a green guard says how often each
        /// side was reached. Empty for every other kind.
        Reached: (string * int) list
    }

/// The constructors (Phase 515) — the stable way to build a `LawResult`. A consumer's record literal
/// is one call here, and a field the record gains later moves no consumer.
[<RequireQualifiedAccess>]
module LawResult =

    /// A subject law's verdict from its first counterexample, if any: green exactly when there is
    /// none. `evidence` is the number of times the law was asserted. The shape every hand-built law
    /// takes — `{ Law = l; Passed = cx.IsNone; Counterexample = cx }` is `subject family l n cx`.
    let subject (family: string) (law: string) (evidence: int) (counterexample: string option) : LawResult =
        { Family = family
          Kind = LawKind.Subject None
          Law = law
          Passed = counterexample.IsNone
          Evidence = evidence
          Reached = []
          Counterexample = counterexample }

    /// A subject law that held on each of its `evidence` assertions.
    let pass (family: string) (law: string) (evidence: int) : LawResult = subject family law evidence None

    /// A subject law that failed; `counterexample` is its first failure.
    let fail (family: string) (law: string) (evidence: int) (counterexample: string) : LawResult =
        subject family law evidence (Some counterexample)

    /// An adequacy guard over `dimension`: `law` is its statement, `reached` the counts it took
    /// (demanded, then any counted beside), `counterexample` the shortfall — green exactly when there
    /// is none. Its evidence is the sum of the counts.
    let guard
        (family: string)
        (dimension: string)
        (law: string)
        (reached: (string * int) list)
        (counterexample: string option)
        : LawResult =
        { Family = family
          Kind = LawKind.Guard dimension
          Law = law
          Passed = counterexample.IsNone
          Evidence = reached |> List.sumBy snd
          Reached = reached
          Counterexample = counterexample }

    /// A side vacuous BY DECLARATION: `what` names what the witness declares none of, `law` says so.
    let declared (family: string) (what: string) (law: string) : LawResult =
        { Family = family
          Kind = LawKind.Declared what
          Law = law
          Passed = true
          Evidence = 0
          Reached = []
          Counterexample = None }

    /// The result labelled with `family`, when nothing labelled it yet — how a runner stamps the
    /// subject laws it ran without overwriting a guard a delegate minted under its own id.
    let withFamily (family: string) (r: LawResult) : LawResult =
        if r.Family = "" then { r with Family = family } else r

    /// `true` for the family's adequacy lines — a guard or a declared vacuity — and `false` for
    /// every law about the witness (a subject law or a corpus law).
    let isAdequacy (r: LawResult) : bool =
        match r.Kind with
        | LawKind.Guard _
        | LawKind.Declared _ -> true
        | LawKind.Subject _
        | LawKind.Corpus _ -> false

    /// `true` for a law the run never asserted: a strict law (no guard covers it) or a corpus law,
    /// red at zero evidence. A covered law at zero evidence is green and reported by its guard.
    let isNeverReached (r: LawResult) : bool =
        not r.Passed
        && r.Evidence <= 0
        && (match r.Kind with
            | LawKind.Subject None
            | LawKind.Corpus _ -> true
            | _ -> false)

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
