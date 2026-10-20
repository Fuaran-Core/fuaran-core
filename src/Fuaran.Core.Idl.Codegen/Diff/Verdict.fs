namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The stability classifier's VERDICT: each change graded by its rule, the one severity
/// precedence, the F# consequence set, the wire evolution and the class a gate reads.
module internal DiffVerdict =

    /// Grade one change by its rule in the descriptor table. Total over `Change`, and
    /// context-free: the severity depends on the change value alone.
    let classify (c: Change) : Classification =
        let sev, why, cite = (DiffRules.ruleOf c).Verdict

        { Change = c
          Severity = sev
          Rationale = why
          Citation = cite }

    /// Phase 293 — ONE precedence over the severities, read by `stabilityImpact`,
    /// `profileBump` and `verdictClass` alike: an undecided row dominates, because
    /// reporting the decidable remainder as the answer is how a `/v2/` event gets
    /// published as a minor; then the wire break, the emitter break, the addition,
    /// and last the host-surface move.
    let private precedence: Severity list =
        [ Unclassifiable; BreakingWire; BreakingForEmitters; Additive; HostSurfaceOnly ]

    /// The severity that heads a classification list under [[precedence]] — `None` for
    /// an empty list.
    let private headline (cs: Classification list) : Severity option =
        precedence
        |> List.tryFind (fun s -> cs |> List.exists (fun c -> c.Severity = s))

    /// The headline over the DECIDED rows alone — what the verdict becomes once every
    /// undecided row has been checked and found absorbable.
    let private decidedHeadline (cs: Classification list) : Severity option =
        headline (cs |> List.filter (fun c -> c.Severity <> Unclassifiable))

    /// The draft `stability_impact:` value — the roadmap front-matter vocabulary
    /// is `additive` / `breaking` / `null`, so this emits one of the first two.
    let stabilityImpact (cs: Classification list) : string =
        match headline cs with
        | Some BreakingWire
        | Some BreakingForEmitters -> "breaking"
        | Some Unclassifiable ->
            (match decidedHeadline cs with
             | Some BreakingWire
             | Some BreakingForEmitters -> "breaking"
             | _ -> "additive   ← ONLY IF every unclassifiable row below checks out; `breaking` otherwise")
        | _ -> "additive"

    /// The wire-profile recommendation. `core@1.x` is the profile-id grammar
    /// (`STABILITY.md` §15 sentinel strings); `/v1/` is the schema `$id` major.
    let profileBump (cs: Classification list) : string =
        let major =
            "`/v2/` MAJOR — the schema `$id` major segment moves. VOCABULARY.md §4.2 says avoid this after publication; do it pre-launch or not at all."

        match headline cs with
        | Some BreakingWire -> major
        | Some Unclassifiable ->
            (match decidedHeadline cs with
             | Some BreakingWire -> major
             | _ ->
                 "UNDECIDED — at least one change crosses an erased slot the artifact does not describe. `core@1.(x+1)` if the checks below pass; `/v2/` MAJOR if any of them fails.")
        | Some BreakingForEmitters ->
            "`core@1.(x+1)` profile MINOR on paper — but at least one change breaks EMITTERS, so the minor understates it. Treat every downstream emitter as needing a coordinated bump."
        | Some Additive -> "`core@1.(x+1)` profile minor — the `/v1/` major segment does not move (VOCABULARY.md §4.1)."
        | Some HostSurfaceOnly
        | None -> "no wire-profile movement — every change is host-surface only."

    /// The F# consequence set of one classified change, given the kind-field optionality
    /// lookup the snapshot pair supplies (Phase 293: an authoring default's consequence
    /// depends on whether the field is required, which the `Change` does not carry).
    ///
    /// Keyed on the CHANGE rather than on its severity, because the two axes are
    /// independent by construction: `FieldHostSurfaceChanged` is `HostSurfaceOnly`
    /// on the wire and a construction break here, and a `FieldAdded` that is
    /// `Additive` on the wire is a construction break all the same.
    ///
    /// `StalePackageSlot` rides every shape change rather than standing alone — see
    /// its own note.
    let private consequencesWith (ctx: DiffRules.Context) (c: Classification) : FSharpConsequence list =
        (DiffRules.ruleOf c.Change).Consequence ctx

    /// The F# consequence set of one classified change, with no snapshot to ask: an
    /// authoring default is read as a required field's, and a required-ness move as one
    /// with no default to stand in (see `defaultConsequence` / `requiredFlipped`).
    let consequences (c: Classification) : FSharpConsequence list = consequencesWith DiffRules.noContext c

    /// The one-line summary of a change, off its rule.
    let summarise (c: Change) : string = (DiffRules.ruleOf c).Summary

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
    let evolution (cs: Classification list) : Versioning.Evolution =
        let subjects pick =
            cs |> List.filter pick |> List.map (fun c -> summarise c.Change) |> Set.ofList

        let retired = subjects (fun c -> c.Severity = BreakingWire)

        let introduced =
            subjects (fun c ->
                match c.Severity with
                | Additive
                | BreakingForEmitters -> true
                | _ -> false)

        Versioning.classify retired introduced

    /// The verdict over two read snapshots.
    let verdict (before: Snapshot) (after: Snapshot) : Verdict =
        let cs = DiffChanges.changes before after |> List.map classify

        // Phase 293 — an authoring default's F# consequence depends on the field's
        // optionality (a `mk<Kind>` parameter exists for a required field with no default),
        // which the `Change` does not carry and the snapshots do.
        let ctx: DiffRules.Context =
            { Presence =
                fun kind fieldName ->
                    [ after; before ]
                    |> List.tryPick (fun snap ->
                        snap.Kinds
                        |> Map.tryFind kind
                        |> Option.bind (fun fs -> fs |> List.tryFind (fun f -> f.Name = fieldName))
                        |> Option.map _.Presence)
              HasDefault =
                fun kind fieldName ->
                    [ after; before ]
                    |> List.exists (fun snap -> snap.Defaults.ContainsKey(kind, fieldName)) }

        let exhibited = cs |> List.collect (consequencesWith ctx) |> Set.ofList

        { Encoding = before.Version, after.Version
          Changes = cs
          Evolution = evolution cs
          Undecided = cs |> List.filter (fun c -> c.Severity = Unclassifiable)
          BreaksEmitters = cs |> List.exists (fun c -> c.Severity = BreakingForEmitters)
          FSharpConsequences = DiffRules.allConsequences |> List.filter exhibited.Contains
          StabilityImpact = stabilityImpact cs
          ProfileAdvice = profileBump cs }

    /// The class of a verdict, by the one severity precedence the report's drafts also read:
    /// any undecided row makes it `Undecided`, then a wire or emitter break `Breaking`, then
    /// `Additive`, then `HostSurface`; no rows at all is `Unchanged`.
    let verdictClass (v: Verdict) : VerdictClass =
        // Phase 293 — the ONE precedence `stabilityImpact` and `profileBump` read too.
        match headline v.Changes with
        | Some Unclassifiable -> VerdictClass.Undecided
        | None -> VerdictClass.Unchanged
        | Some BreakingWire
        | Some BreakingForEmitters -> VerdictClass.Breaking
        | Some Severity.Additive -> VerdictClass.Additive
        | Some HostSurfaceOnly -> VerdictClass.HostSurface

    /// The stable label of a verdict class — the CLI's `--expect` vocabulary. A
    /// contract: a spec home's gate passes or greps these strings.
    let classLabel =
        function
        | VerdictClass.Unchanged -> "unchanged"
        | VerdictClass.HostSurface -> "host-surface"
        | VerdictClass.Additive -> "additive"
        | VerdictClass.Breaking -> "breaking"
        | VerdictClass.Undecided -> "undecided"

    /// Every verdict class, in the order a report and the CLI usage list them.
    let allClasses: VerdictClass list =
        [ VerdictClass.Unchanged
          VerdictClass.HostSurface
          VerdictClass.Additive
          VerdictClass.Breaking
          VerdictClass.Undecided ]

    /// Read a label back. `None` on anything else — an unrecognised `--expect` is
    /// refused rather than read as a default, because defaulting would make a
    /// misspelled assertion pass.
    let classOfLabel (s: string) : VerdictClass option =
        allClasses |> List.tryFind (fun c -> classLabel c = s)

    /// The process exit code a branching gate reads when it has NOT declared what it
    /// expects: `0` for anything a consumer absorbs by repinning, `3` for a break,
    /// `4` for undecided. Three codes rather than a boolean, because "I cannot tell"
    /// and "this breaks" want different handling — collapsing them is how the
    /// erased-slot case gets treated as a break and the report stops being read.
    let exitCode =
        function
        | VerdictClass.Unchanged
        | VerdictClass.HostSurface
        | VerdictClass.Additive -> 0
        | VerdictClass.Breaking -> 3
        | VerdictClass.Undecided -> 4

    /// The profile a `baseProfile` bumps to under a verdict — `Versioning.bump`
    /// applied to `evolution`'s answer, which is the whole reason this module
    /// produces an `Evolution` at all.
    ///
    /// An UNDECIDED verdict yields no profile, and that is the load-bearing half: a
    /// function that returned `baseProfile` unchanged, or a minor bump, would hand a
    /// caller a number to publish for a revision whose class nobody has established.
    let bumpProfile (baseProfile: Versioning.Capability) (v: Verdict) : Bump =
        match v.Undecided with
        | [] -> Bump.Bumped(Versioning.bump baseProfile v.Evolution)
        | rows -> Bump.Undecided rows
