namespace Fuaran.Core.Idl

open Fuaran.Core
open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit

// ---------------------------------------------------------------------------
// Phase 700 — the IDL diff classifier + host-strand report.
//
// The stability classification rules are already written and mechanical-shaped
// but hand-applied: `STABILITY.md` (adding a `NodeKind` case = minor; removal /
// `$type` rename = a major wire event) and `VOCABULARY.md` §4 (addition = an
// additive `core@1.(x+1)` profile minor; the host-lag commitment). This module
// applies them to a pair of `idl.json` revisions and emits the **host-strand
// report** — the `WIRE_FORMAT.md` §11 obligation set per host class, derived from
// the §11.0 roster. The charter's §1.3 cost table, computed instead of remembered.
//
// **Advisory, never authoritative.** Nothing here edits a file, bumps a version,
// or gates a build. Its output is text for a phase author to read, argue with,
// and paste a corrected version of. That is deliberate: a classifier that
// auto-applied would make the hand-declared classification unfalsifiable, and the
// retroactive validation (docs/idl-diff-retroactive-validation.md) depends on the
// two being independently produced so a disagreement is visible.
//
// **The input is the ARTIFACT, not the `Idl` record.** `Artifact.render` is a
// lossy-by-design projection: it elides what is not contract (authored ordering
// of sorted collections) and flags what is host-surface rather than wire
// (`hostSurface`). Diffing the artifact therefore diffs exactly the published
// contract, and works across revisions whose F# vocabulary no longer compiles —
// which is the whole point of having committed the artifact.
//
// **One concern per file (Phase 524, DECISIONS.md D125 amended).** The classifier's types live in
// `Fuaran.Core.Idl.Codegen.Types` (`Types/Snapshot.fs`, `Types/Change.fs`, `Types/Roster.fs`);
// the reader (`Diff/Snapshot.fs`), the rules (`Diff/Rules.fs` — ONE match over `Change`), the
// walk (`Diff/Changes.fs`), the verdict (`Diff/Verdict.fs`), the host-strand join
// (`Diff/Obligations.fs`) and the renderings (`Diff/Report.fs`) are internal modules ahead of this
// facade, which carries the published entries. Four doors read an artifact pair — `parse`,
// `ofArtifacts`, `ofValues` and `run` — where there were twelve, and each computes the verdict
// once: the report renders it.
// ---------------------------------------------------------------------------

/// The IDL stability classifier over two `idl.json` revisions (and optionally each side's
/// `support.json`): the change list, each change's wire severity and F# consequence, the
/// host obligations it raises, and the verdict a gate branches on. Advisory: it writes no
/// file, bumps no version and gates no build.
module Diff =

    /// Read one `idl.json` revision from its text, with the `support.json` text beside it or `None`.
    /// Tolerant of keys the revision predates (`ops`, `hostCases`, `transparentCase` are all
    /// emitted conditionally) — their absence is read as empty, which is what the emitter means
    /// by it — and a malformed member is skipped, so the refusals are a text that is not JSON, a
    /// root that is not an object, and a support document that does not read (named `support:`).
    let parse (text: string) (supportText: string option) : Result<Snapshot, string> =
        DiffSnapshot.parse text supportText

    /// Every difference between two snapshots, sorted by rule rank then by the change's
    /// own key, so identical inputs give an identical list. A kind rename is INFERRED and
    /// reported beside its add and remove, never instead of them; annotation and category
    /// moves are reported only for members both revisions carry.
    let changes (before: Snapshot) (after: Snapshot) : Change list = DiffChanges.changes before after

    /// Grade one change by its rule in the descriptor table. Total over `Change`, and
    /// context-free: the severity depends on the change value alone.
    let classify (c: Change) : Classification = DiffVerdict.classify c

    /// The F# consequence set of one classified change, with no snapshot to ask: an
    /// authoring default is read as a required field's, and a required-ness move as one
    /// with no default to stand in (see `defaultConsequence` / `requiredFlipped`).
    let consequences (c: Classification) : FSharpConsequence list = DiffVerdict.consequences c

    /// The `Versioning.Evolution` a classification list amounts to.
    ///
    /// It DELEGATES the additive-vs-breaking decision to `Versioning.classify`
    /// rather than restating that rule, by handing it the two subject sets that
    /// function compares: the members a revision RETIRES and the members it
    /// INTRODUCES. A change to the rule therefore changes this answer too, which is
    /// the point of routing through it — one rule, in one place, with `bump` reading
    /// it.
    ///
    /// The split is the severity's:
    ///
    ///  - `BreakingWire` RETIRES — a document that was valid is not, or its bytes
    ///    moved, so an older consumer cannot interpret the result and the major must
    ///    move.
    ///  - `Additive` and `BreakingForEmitters` INTRODUCE. The second reads oddly
    ///    until you ask whose profile it is: every existing document still decodes,
    ///    so a consumer negotiating the profile is `Behind` rather than `Foreign`,
    ///    and the minor is the honest answer to THAT question. What the minor does
    ///    not say is that emitters need a coordinated bump — which is why the
    ///    verdict carries `BreaksEmitters` separately instead of folding it in here
    ///    and calling the format broken.
    ///  - `HostSurfaceOnly` moves no profile at all.
    ///  - `Unclassifiable` moves nothing here either, and stops the bump outright —
    ///    see `bumpProfile`.
    let evolution (cs: Classification list) : Versioning.Evolution = DiffVerdict.evolution cs

    /// THE classification of a snapshot pair, computed once: every change graded, the wire
    /// evolution, the undecided rows, the F# consequence set (each change's consequence read with
    /// what the pair knows about its field) and the two drafts. Every door and every rendering
    /// reads this; none recomputes it.
    let verdict (before: Snapshot) (after: Snapshot) : Verdict = DiffVerdict.verdict before after

    /// The obligation set for one change, joined against the roster.
    ///
    /// The two rows worth reading rather than skimming are the veneer rows and
    /// the native render-arm row, because both are conditional and both have
    /// been got wrong in a downstream consumer before:
    ///
    /// - Phase 801 recorded that a payload-FIELD addition binds neither the C#
    ///   `Coverage` reflection nor the VB analyzer's `Vocabulary.cs`, because
    ///   both pin `NodeKind`. §11 step 6 nonetheless speaks of "attribute rows",
    ///   so a field change is `Check`, not `NotBound` — the two authorities do
    ///   not quite agree and the phase author is the one who can settle it.
    /// - Swift's `switch` with no `default:` and Kotlin's `when` over a sealed
    ///   type are exhaustiveness ERRORS, so a case added to a family those tiers
    ///   model is a compiler-forced arm (the 745 precedent, restated by 864) —
    ///   but only for the families they actually model, which the artifact does
    ///   not record.
    ///
    /// **Which rows a roster earns (Phase 252).** Every row naming a corpus, a spec
    /// document, a veneer or an analyzer is the UI tier's §11 checklist, and is emitted
    /// only when the roster declares that tier's reference codec host (`fuaran`). A
    /// roster without it — another vocabulary's, or the EMPTY roster a vocabulary with
    /// no manifest `hosts` gets — is obliged by the rows that hold for any vocabulary:
    /// each declared host, the generated layer, the artifact, and the profile and
    /// emitter rows a breaking verdict carries.
    let obligations (roster: Host list) (c: Classification) : Obligation list = DiffObligations.obligations roster c

    /// The advisory report of a verdict, against a host roster (`rosterSource` names where the
    /// roster came from). Deterministic — byte-identical for identical inputs, which is what makes
    /// it diffable. It renders the verdict and computes nothing (Phase 524).
    let report (rosterSource: string) (roster: Host list) (v: Verdict) : string =
        DiffReport.report rosterSource roster v

    /// The branchable block the CLI prints under the advisory report: the class, the
    /// wire evolution, the emitter warning the minor cannot carry, and the F#
    /// consequence set with the reason each applies.
    ///
    /// A function of the `Verdict` alone rather than of the snapshots, so it composes
    /// onto `report`'s output without a second roster resolution — see `runVerdict`.
    let verdictBlock (v: Verdict) : string = DiffReport.verdictBlock v

    /// The class of a verdict, by the one severity precedence the report's drafts also read:
    /// any undecided row makes it `Undecided`, then a wire or emitter break `Breaking`, then
    /// `Additive`, then `HostSurface`; no rows at all is `Unchanged`.
    let verdictClass (v: Verdict) : VerdictClass = DiffVerdict.verdictClass v

    /// The stable label of a verdict class — the CLI's `--expect` vocabulary. A
    /// contract: a spec home's gate passes or greps these strings.
    let classLabel (c: VerdictClass) : string = DiffVerdict.classLabel c

    /// Every verdict class, in the order a report and the CLI usage list them.
    let allClasses: VerdictClass list = DiffVerdict.allClasses

    /// Read a label back. `None` on anything else — an unrecognised `--expect` is
    /// refused rather than read as a default, because defaulting would make a
    /// misspelled assertion pass.
    let classOfLabel (s: string) : VerdictClass option = DiffVerdict.classOfLabel s

    /// The process exit code a branching gate reads when it has NOT declared what it
    /// expects: `0` for anything a consumer absorbs by repinning, `3` for a break,
    /// `4` for undecided. Three codes rather than a boolean, because "I cannot tell"
    /// and "this breaks" want different handling — collapsing them is how the
    /// erased-slot case gets treated as a break and the report stops being read.
    let exitCode (c: VerdictClass) : int = DiffVerdict.exitCode c

    /// The profile a `baseProfile` bumps to under a verdict — `Versioning.bump`
    /// applied to `evolution`'s answer, which is the whole reason this module
    /// produces an `Evolution` at all.
    ///
    /// An UNDECIDED verdict yields no profile, and that is the load-bearing half: a
    /// function that returned `baseProfile` unchanged, or a minor bump, would hand a
    /// caller a number to publish for a revision whose class nobody has established.
    let bumpProfile (baseProfile: Versioning.Capability) (v: Verdict) : Bump = DiffVerdict.bumpProfile baseProfile v

    /// Phase 587 — `bumpProfile` for a profile whose decoders follow `rule`. Under
    /// `Versioning.DecodeRule.RefusesUnknownMembers` it is `bumpProfile`, answer for answer. Under
    /// `IgnoresUnknownMembers` a row that a consumer behind it absorbs moves nothing — an optional member
    /// added above all — while a new tag, a loosened required member and a widened `int` slot still move
    /// the minor, and a wire break the major. An undecided verdict yields no profile under either rule.
    let bumpProfileUnder (rule: Versioning.DecodeRule) (baseProfile: Versioning.Capability) (v: Verdict) : Bump =
        DiffVerdict.bumpProfileUnder rule baseProfile v

    /// Phase 587 — is this row absorbed by a consumer behind it whose decoders ignore an undeclared member,
    /// so that it moves no profile under `Versioning.DecodeRule.IgnoresUnknownMembers`? Total over `Change`;
    /// `false` for every row that is not `Additive` or `BreakingForEmitters`.
    let absorbedWhenUnknownMembersIgnored (c: Classification) : bool =
        DiffVerdict.absorbedWhenUnknownMembersIgnored c

    /// The stable label of a consequence class. A contract: these strings are what
    /// an external gate greps and what the `docs/` table names.
    let consequenceLabel (c: FSharpConsequence) : string = DiffRules.consequenceLabel c

    /// One sentence per consequence class — the reason it applies, for a report that
    /// has to stand on its own beside a compiler message.
    let consequenceWhy (c: FSharpConsequence) : string = DiffRules.consequenceWhy c

    /// Every consequence class, in report order — so a renderer and a document
    /// enumerate the table rather than each restating it.
    let allConsequences: FSharpConsequence list = DiffRules.allConsequences

    /// The F# consequence table, rendered from the code that decides it. `docs/`
    /// carries the same table for a reader with no build; a test asserts the
    /// document names every class, so the two cannot drift silently.
    let consequenceTable: string = DiffReport.consequenceTable

    /// Phase 293 — the mapping table `docs/idl-stability-classes.md` carries, RENDERED from the
    /// descriptor table the classifier runs on, so the document and the code cannot drift: the
    /// test suite holds the document's generated section byte-equal to this string. One row per
    /// documented shape of each `Change` case, in the classifier's own rank order, with the case
    /// name beside it so a reader can go from a report line to the rule and back.
    let mappingTable: string = DiffReport.mappingTable

    /// THE verdict over two artifact texts, each with its `support.json` text or `None`. `Error`
    /// names the side (`old:` / `new:`) that did not read.
    let ofArtifacts (before: string * string option) (after: string * string option) : Result<Verdict, string> =
        let read (prefix: string) (text, support) =
            DiffSnapshot.parse text support |> Result.mapError (fun e -> prefix + e)

        read "old: " before
        |> Result.bind (fun b -> read "new: " after |> Result.map (DiffVerdict.verdict b))

    /// THE verdict over two `Idl` VALUES, each with its support document or `None` — the artifact
    /// door over `Artifact.render` and `SupportArtifact.render`, so a value and its committed
    /// artifact classify alike.
    let ofValues
        (before: Idl * SupportDocument option)
        (after: Idl * SupportDocument option)
        : Result<Verdict, string> =
        let render (idl, support) =
            Artifact.render idl, support |> Option.map SupportArtifact.render

        ofArtifacts (render before) (render after)

    /// The whole pipeline a CLI runs: two artifact texts (each with its support text or `None`) and
    /// an optional `manifest.json` text, used only for the host roster — the manifest's `hosts` and
    /// nothing else (Phase 252): absent, the roster is EMPTY and the report says so. Answers the
    /// report followed by the verdict block, and the verdict, which is computed ONCE (each side
    /// read once; Phase 524 removed the second parse). `Error` names the side (`old:` / `new:`).
    let run
        (manifestText: string option)
        (before: string * string option)
        (after: string * string option)
        : Result<string * Verdict, string> =
        let source, roster =
            match
                manifestText
                |> Option.bind (fun t -> Json.parse t |> Result.toOption)
                |> Option.bind DiffObligations.rosterFrom
            with
            | Some hs -> "manifest.json `hosts`", hs
            | None ->
                (match manifestText with
                 | None -> "none declared (no manifest given)"
                 | Some _ -> "none declared (the manifest carries no `hosts` key)"),
                []

        ofArtifacts before after
        |> Result.map (fun v -> DiffReport.report source roster v + DiffReport.verdictBlock v, v)
