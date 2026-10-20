namespace Fuaran.Core.Idl.Emit

open Fuaran.Core
open Fuaran.Core.Idl
open Fuaran.Core.Idl.Codegen.Types

/// The stability classifier's RENDERINGS: the advisory report, the verdict block a gate reads,
/// and the two tables `docs/idl-stability-classes.md` pastes. Each renders a `Verdict`; none
/// computes one (Phase 524).
module internal DiffReport =

    let private severityLabel =
        function
        | Additive -> "ADDITIVE"
        | BreakingForEmitters -> "BREAKING (emitters)"
        | BreakingWire -> "BREAKING (wire)"
        | HostSurfaceOnly -> "host-surface only"
        | Unclassifiable -> "UNDECIDED — needs a human"

    let private strengthLabel =
        function
        | Strength.Required -> "MUST"
        | Strength.Check -> "CHECK"
        | Strength.NotBound -> "n/a"

    /// The advisory report. Deterministic — byte-identical for identical inputs,
    /// which is what makes it diffable and what its test asserts.
    let report (rosterSource: string) (roster: Host list) (v: Verdict) : string =
        let cs = v.Changes
        let before, after = v.Encoding
        let sb = System.Text.StringBuilder()
        let line (s: string) = sb.Append(s).Append('\n') |> ignore

        line "# idl-diff report"
        line ""
        line "Advisory. Nothing here has been applied; every verdict is a draft for a phase author to"
        line "confirm or correct. See fuaran-core `Fuaran.Core.Idl.Diff`."
        line ""
        line (sprintf "Artifact encoding: %d -> %d" before after)
        line (sprintf "Host roster source: %s" rosterSource)
        line ""

        if cs.IsEmpty then
            line "## Verdict"
            line ""
            line "No change. The two revisions describe the same vocabulary."
            line ""
        else
            line "## Verdict"
            line ""
            line (sprintf "Draft front-matter:   stability_impact: %s" v.StabilityImpact)
            line (sprintf "Wire profile:         %s" v.ProfileAdvice)
            line ""

            let count sev =
                cs |> List.filter (fun c -> c.Severity = sev) |> List.length

            line (
                sprintf
                    "%d change(s): %d additive, %d breaking-for-emitters, %d breaking-wire, %d host-surface only, %d undecided."
                    cs.Length
                    (count Additive)
                    (count BreakingForEmitters)
                    (count BreakingWire)
                    (count HostSurfaceOnly)
                    (count Unclassifiable)
            )

            line ""
            line "## Changes"
            line ""

            for c in cs do
                line (sprintf "### [%s] %s" (severityLabel c.Severity) (DiffVerdict.summarise c.Change))
                line ""
                line c.Rationale
                line ""
                line (sprintf "Rule: %s" c.Citation)
                line ""
                line "Obligations:"

                for o in DiffObligations.obligations roster c do
                    line (sprintf "  %-5s %s" (strengthLabel o.Strength) o.Surface)
                    line (sprintf "        %s" o.Note)

                line ""

            line "## Consolidated obligation set"
            line ""
            line "Every surface named above, de-duplicated, strongest strength wins."
            line ""

            let rank =
                function
                | Strength.Required -> 2
                | Strength.Check -> 1
                | Strength.NotBound -> 0

            let consolidated =
                cs
                |> List.collect (DiffObligations.obligations roster)
                |> List.groupBy _.Surface
                |> List.map (fun (surface, os) -> surface, os |> List.maxBy (fun o -> rank o.Strength))
                |> List.sortBy (fun (surface, o) -> -(rank o.Strength), surface)

            for (surface, o) in consolidated do
                line (sprintf "  %-5s %s" (strengthLabel o.Strength) surface)

            line ""

        sb.ToString()

    /// The branchable block the CLI prints under the advisory report: the class, the
    /// wire evolution, the emitter warning the minor cannot carry, and the F#
    /// consequence set with the reason each applies.
    ///
    /// A function of the `Verdict` alone rather than of the snapshots, so it composes
    /// onto `report`'s output without a second roster resolution — see `runVerdict`.
    let verdictBlock (v: Verdict) : string =
        let sb = System.Text.StringBuilder()
        let line (s: string) = sb.Append(s).Append('\n') |> ignore

        line "## Verdict class"
        line ""
        line (sprintf "class:            %s" (DiffVerdict.classLabel (DiffVerdict.verdictClass v)))

        line (
            sprintf
                "wire evolution:   %s"
                (match v.Evolution with
                 | Versioning.Evolution.Additive [] -> "no movement"
                 | Versioning.Evolution.Additive added -> sprintf "additive — %d introduced" (List.length added)
                 | Versioning.Evolution.Breaking(removed, added) ->
                     sprintf "BREAKING — %d retired, %d introduced" (List.length removed) (List.length added))
        )

        line (sprintf "breaks emitters:  %s" (if v.BreaksEmitters then "yes" else "no"))

        line (
            sprintf
                "F# consequences:  %s"
                (if v.FSharpConsequences.IsEmpty then
                     "none"
                 else
                     v.FSharpConsequences
                     |> List.map DiffRules.consequenceLabel
                     |> String.concat ", ")
        )

        line ""

        for c in v.FSharpConsequences do
            line (sprintf "  %s" (DiffRules.consequenceLabel c))
            line (sprintf "        %s" (DiffRules.consequenceWhy c))

        line ""
        sb.ToString()

    /// The F# consequence table, rendered from the code that decides it. `docs/`
    /// carries the same table for a reader with no build; a test asserts the
    /// document names every class, so the two cannot drift silently.
    let consequenceTable: string =
        let sb = System.Text.StringBuilder()
        sb.Append("F# consequence classes\n\n") |> ignore

        sb.Append(
            "What a classified IDL change does to a consumer's F# source compiled against the\n"
            + "generated structural layer. Independent of the wire severity reported beside it.\n\n"
        )
        |> ignore

        for c in DiffRules.allConsequences do
            sb.Append("  ").Append(DiffRules.consequenceLabel c).Append("\n        ")
            |> ignore

            sb.Append(DiffRules.consequenceWhy c).Append("\n") |> ignore

        sb.ToString()

    /// Phase 293 — the mapping table `docs/idl-stability-classes.md` carries, RENDERED from the
    /// descriptor table the classifier runs on, so the document and the code cannot drift: the
    /// test suite holds the document's generated section byte-equal to this string. One row per
    /// documented shape of each `Change` case, in the classifier's own rank order, with the case
    /// name beside it so a reader can go from a report line to the rule and back.
    let mappingTable: string =
        let header =
            [ "| Change | Wire severity | F# consequence | Classifier case |"
              "|---|---|---|---|" ]

        let rows =
            [ for r in DiffRules.exemplars |> List.map DiffRules.ruleOf do
                  for d in r.Doc do
                      sprintf "| %s | %s | %s | `%s` |" d.Subject d.Wire d.FSharp r.Case ]

        String.concat "\n" (header @ rows) + "\n"
