module Fuaran.Core.Tests.ProofCoverageTests

// ---------------------------------------------------------------------------
// Phase 203 — "exhaustively proved" is a GATE PREDICATE, not a sentence.
//
// `proofs/README.md` claims this repository is proved "in the sense a parametric spine admits".
// Nothing computed that, so every survey found another packable package with no model and no
// record of whether it needed one, and filed a phase; thirty-three theorem phases were open the
// day this one was written. A claim nobody can evaluate is a claim that can only grow. This family
// is the stopping rule: three clauses over JSON this repository already keeps, no prover, one line
// of output that says what the claim currently amounts to.
//
//   1. COVERAGE IS TOTAL OR DECLARED. Every packable package — the derived roster Phase 199
//      asserts, reused here rather than re-derived — is either named by a model in
//      `proofs/modules.json` or carries an entry in `proofs/coverage-exclusions.json` with a
//      reason from that file's own closed vocabulary. A package in neither fails. The check runs
//      BOTH WAYS, on the Phase 185 precedent: an entry naming a package that is no longer
//      packable, or one that has SINCE GAINED a model, fails too — a reason that outlives the fact
//      it describes is how a stale comment comes to say something untrue.
//
//   2. EVERY ASSUMED ROW IS ACCOUNTED FOR. Each `assumed` row carries its Phase 174 class, and
//      each class carries its own account of what would close it: a `premise` is closed by nothing
//      and that IS its class; a `domain-obligation` is discharged at a domain's own witness by the
//      law it names; a `model-bridge` names `closes` — `permanent`, `unscheduled`, or a phase,
//      and a phase must be OPEN. A scheduled row naming a shipped or unknown phase fails.
//
//   3. EVERY DIFFERENTIAL IS PAIRED TO A THEOREM. A `tested` row on the `Proofs.Oracle` family
//      names the model it runs beside (`evidence.model`, Phase 203), and that model must carry at
//      least one `proved` row. A differential with no theorem beside it is a test pretending to be
//      a rung. A `tested` row on a `Conformance.<law>` family is a LAW row, not a differential, and
//      carries no model — the asymmetry is checked in both directions so neither kind can drift
//      into the other.
//
// Plus the count clause Phase 187 asked for: the row counts stated in PROSE in the contract
// section of `proofs/README.md` are held to `proofs.json`. Phase 187 found them already stale and
// nothing checked them. The contract TABLE beside them is checked by `Proofs.Ladder`'s
// `contract-agrees`; this is the sentences around it.
//
// THREE THINGS ABOUT THE SHAPE, each answering a way this could go quietly vacuous.
//
//  * `checkCoverage` is a FUNCTION of its inputs, and every go-red below runs it over synthetic
//    input that differs from a clean baseline in exactly one way. The live case alone cannot show
//    a clause works: it reads files expected to be correct, so a clause that computed nothing
//    would pass it.
//  * The scheduled half of clause 2 is VACUOUS ON TODAY'S DATA — no row carries a phase-form
//    `closes`, because every bridge is `permanent` or `unscheduled`. It is stated plainly rather
//    than left to be discovered, and it is not vacuous as CODE: the go-reds exercise both the
//    shipped-phase and the no-oracle arms. The oracle is the roadmap store for this repository's
//    own side, named by `FUARAN_CORE_ROADMAP`; where a row makes a scheduling claim and no oracle
//    is configured, the clause FAILS rather than passes, because a check that reads as green
//    without its instrument is worse than an absent one.
//  * The predicate LINE is emitted whatever the verdict, and says which verdict it is. A line that
//    only appeared when green would make its absence the signal, and an absence is the one thing a
//    reader scanning a log does not see.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

// ---------------------------------------------------------------------------
//  The inputs the predicate is computed from
// ---------------------------------------------------------------------------

/// One entry of `proofs/coverage-exclusions.json`.
type Exclusion =
    {
        Package: string
        Reason: string
        /// The `Conformance.<law>` a `law-tested-by-design` entry names, and that only such an
        /// entry may name.
        Family: string option
        Note: string
        Phase: string
    }

/// One entry of `proofs/modules.json`, as this family reads it: the model, and the packable
/// packages whose production code it is about.
type ModelEntry =
    { Module: string
      Packages: string list }

type CoverageInputs =
    {
        /// Every packable package. For the real run this is Phase 199's own derivation, called
        /// rather than restated — two spellings of "what ships" would drift exactly the way the
        /// documents that phase gates had drifted.
        Packable: string list
        /// `proofs/modules.json`'s roster, with its Phase 203 `packages` attribution.
        Models: ModelEntry list
        Exclusions: Exclusion list
        /// The reason vocabulary, read from the exclusions file's own `reasons` block rather than
        /// restated here: the vocabulary is closed BY THAT FILE, and a copy of it in this one
        /// would be a second closed set to keep in step.
        Reasons: Set<string>
        /// The law names a `law-tested-by-design` entry may cite — the shipped kit's declared
        /// roster, for the same reason `Proofs.Ladder` reads it there.
        Laws: Set<string>
        /// The phase ids that are OPEN on this side, where an oracle is configured. `None` is "no
        /// oracle", which is a finding for any row that makes a scheduling claim and inert for
        /// every row that does not.
        OpenPhases: Set<string> option
        /// The contract section of `proofs/README.md`, whose prose counts are held to the ladder.
        ContractText: string
    }

/// What the predicate says, once computed — the numbers the emitted line renders.
type CoverageTally =
    { Modelled: int
      Excluded: int
      Assumed: int
      Premise: int
      DomainDischarged: int
      Permanent: int
      Unscheduled: int
      Scheduled: int }

let private finding (subject: string) (clause: string) (detail: string) =
    sprintf "'%s' [%s]: %s" subject clause detail

let private strMember (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

let private objMember (el: JsonElement) (name: string) : JsonElement option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.Object -> Some v
    | _ -> None

/// `fuaran-core#NNN`, the one phase form this repository's ladder uses.
let private phaseForm = Regex(@"^fuaran-core#([1-9][0-9]*)$", RegexOptions.Compiled)

/// A model path as a `proved` row states it: `proofs/<Module>.fst`.
let private moduleOfPath (path: string) = Path.GetFileNameWithoutExtension path

// ---------------------------------------------------------------------------
//  The predicate
// ---------------------------------------------------------------------------

/// Every finding the three clauses yield against these inputs and this ladder, with the tally the
/// emitted line renders. Empty findings is exhaustive.
let checkCoverage (inputs: CoverageInputs) (ladderText: string) : string list * CoverageTally =
    use doc = JsonDocument.Parse ladderText

    let rows =
        match doc.RootElement.TryGetProperty "claims" with
        | true, c when c.ValueKind = JsonValueKind.Array -> c.EnumerateArray() |> List.ofSeq
        | _ -> []

    let atLevel lvl =
        rows |> List.filter (fun r -> strMember r "level" = Some lvl)

    let packable = Set.ofList inputs.Packable

    // ---- clause 1: coverage is total or declared -------------------------------------------

    let modelFindings =
        inputs.Models
        |> List.collect (fun m ->
            let empty =
                if List.isEmpty m.Packages then
                    [ finding
                          m.Module
                          "model-package"
                          "names no `packages` — a model attributed to nothing cannot cover anything, and reads as coverage in the roster" ]
                else
                    []

            let unknown =
                m.Packages
                |> List.filter (fun p -> not (Set.contains p packable))
                |> List.map (fun p ->
                    finding
                        m.Module
                        "model-package"
                        (sprintf
                            "names package '%s', which is not packable — the attribution has outlived the package"
                            p))

            empty @ unknown)

    let modelled =
        inputs.Models
        |> List.collect _.Packages
        |> List.filter (fun p -> Set.contains p packable)
        |> Set.ofList

    let excluded = inputs.Exclusions |> List.map _.Package |> Set.ofList

    let uncovered =
        inputs.Packable
        |> List.filter (fun p -> not (Set.contains p modelled) && not (Set.contains p excluded))
        |> List.map (fun p ->
            finding
                p
                "coverage-declared"
                "is packable and is named by no model in `modules.json` and no entry in `coverage-exclusions.json` — there is no third state, so state which one it is")

    let exclusionFindings =
        inputs.Exclusions
        |> List.collect (fun e ->
            let stale =
                if not (Set.contains e.Package packable) then
                    [ finding
                          e.Package
                          "exclusion-stale"
                          "is excluded and is not packable — the reason has outlived the package it describes" ]
                elif Set.contains e.Package modelled then
                    [ finding
                          e.Package
                          "exclusion-stale"
                          "is excluded and HAS a model — delete the entry; an exclusion beside a model is a decision that has already been reversed" ]
                else
                    []

            let reason =
                if Set.contains e.Reason inputs.Reasons then
                    []
                else
                    [ finding
                          e.Package
                          "exclusion-reason"
                          (sprintf
                              "reason '%s' is outside the closed vocabulary %s"
                              e.Reason
                              (inputs.Reasons |> Set.toList |> String.concat " | ")) ]

            let family =
                match e.Reason, e.Family with
                | "law-tested-by-design", None ->
                    [ finding
                          e.Package
                          "exclusion-family"
                          "is `law-tested-by-design` and names no `family` — a declined theorem must say what stands in its place, or the entry is an excuse" ]
                | "law-tested-by-design", Some f when not (f.StartsWith "Conformance.") ->
                    [ finding
                          e.Package
                          "exclusion-family"
                          (sprintf "`family` '%s' is not the `Conformance.<law>` form" f) ]
                | "law-tested-by-design", Some f when
                    not (Set.contains (f.Substring "Conformance.".Length) inputs.Laws)
                    ->
                    [ finding
                          e.Package
                          "exclusion-family"
                          (sprintf
                              "`family` names '%s', which the shipped law-family roster does not declare — the entry cites a run no census enumerates"
                              f) ]
                | "law-tested-by-design", Some _ -> []
                | _, Some f ->
                    [ finding
                          e.Package
                          "exclusion-family"
                          (sprintf "carries `family` '%s' — only a `law-tested-by-design` entry names a law" f) ]
                | _, None -> []

            let recorded =
                let blank (v: string) = String.IsNullOrWhiteSpace v

                if blank e.Note then
                    [ finding
                          e.Package
                          "exclusion-reason"
                          "carries no `note` — the reason token says which KIND of decision it is, never the decision" ]
                elif not (phaseForm.IsMatch e.Phase) then
                    [ finding
                          e.Package
                          "exclusion-reason"
                          (sprintf "phase '%s' is not the `fuaran-core#NNN` form" e.Phase) ]
                else
                    []

            stale @ reason @ family @ recorded)

    // ---- clause 2: every assumed row is accounted for ---------------------------------------

    let assumedRows = atLevel "assumed"

    let accountOf (row: JsonElement) =
        let id = strMember row "id" |> Option.defaultValue "<no id>"

        match strMember row "class" with
        | None ->
            [ finding
                  id
                  "assumed-accounted"
                  "carries no `class` — an assumed row with no class is one no reader can act on, and it drops silently out of the predicate's own count" ],
            None
        | Some "premise" -> [], Some "premise"
        | Some "domain-obligation" ->
            match strMember row "dischargedBy" with
            | Some _ -> [], Some "domain-discharged"
            | None ->
                [ finding
                      id
                      "assumed-accounted"
                      "is a `domain-obligation` and names no `dischargedBy` — an obligation with no law is one a domain has no way to discharge" ],
                None
        | Some "model-bridge" ->
            match strMember row "closes" with
            | Some "permanent" -> [], Some "permanent"
            | Some "unscheduled" -> [], Some "unscheduled"
            | Some c when phaseForm.IsMatch c ->
                let n = phaseForm.Match(c).Groups[1].Value

                match inputs.OpenPhases with
                | None ->
                    [ finding
                          id
                          "closes-open"
                          (sprintf
                              "schedules `closes: %s` and no roadmap oracle is configured — set FUARAN_CORE_ROADMAP; a scheduling claim nothing can check must not read as green"
                              c) ],
                    Some "scheduled"
                | Some openSet when Set.contains n openSet -> [], Some "scheduled"
                | Some _ ->
                    [ finding
                          id
                          "closes-open"
                          (sprintf
                              "schedules `closes: %s`, which is not an OPEN phase on this side — a row scheduled against a shipped or unknown phase is an assumption nobody is going to close"
                              c) ],
                    Some "scheduled"
            | Some c ->
                [ finding
                      id
                      "assumed-accounted"
                      (sprintf "`closes` '%s' is neither a phase nor `permanent` / `unscheduled`" c) ],
                None
            | None ->
                [ finding
                      id
                      "assumed-accounted"
                      "is a `model-bridge` and names no `closes` — the bridge is unaccounted: nobody can tell a gap nothing could close from one nobody has taken" ],
                None
        | Some c -> [ finding id "assumed-accounted" (sprintf "class '%s' is outside the closed three" c) ], None

    let accounts = assumedRows |> List.map accountOf
    let assumedFindings = accounts |> List.collect fst

    let countAccount name =
        accounts |> List.filter (fun (_, a) -> a = Some name) |> List.length

    // ---- clause 3: every differential is paired to a theorem ---------------------------------

    let provedModels =
        atLevel "proved"
        |> List.choose (fun r -> objMember r "evidence" |> Option.bind (fun e -> strMember e "model"))
        |> List.map moduleOfPath
        |> Set.ofList

    let knownModules = inputs.Models |> List.map _.Module |> Set.ofList

    let differentialFindings =
        atLevel "tested"
        |> List.collect (fun row ->
            let id = strMember row "id" |> Option.defaultValue "<no id>"
            let ev = objMember row "evidence"
            let family = ev |> Option.bind (fun e -> strMember e "family")
            let model = ev |> Option.bind (fun e -> strMember e "model")

            match family, model with
            | Some "Proofs.Oracle", None ->
                [ finding
                      id
                      "differential-paired"
                      "is a `Proofs.Oracle` differential and names no `evidence.model` — a differential that does not say which model it ran beside cannot be paired to a theorem, only assumed to have one" ]
            | Some "Proofs.Oracle", Some m ->
                let mn = moduleOfPath m

                if not (Set.contains mn knownModules) then
                    [ finding
                          id
                          "differential-paired"
                          (sprintf "`evidence.model` names '%s', which is not a model the leg checks" m) ]
                elif not (Set.contains mn provedModels) then
                    [ finding
                          id
                          "differential-paired"
                          (sprintf
                              "runs beside '%s', which carries NO `proved` row — a differential with no theorem beside it is a test pretending to be a rung"
                              m) ]
                else
                    []
            | Some f, Some m when f.StartsWith "Conformance." ->
                [ finding
                      id
                      "differential-paired"
                      (sprintf
                          "is a law row on '%s' and carries `evidence.model` '%s' — a law is sampled at a witness and stands beside no model of ours"
                          f
                          m) ]
            | _ -> [])

    // ---- the count clause Phase 187 asked for -------------------------------------------------

    let countIn (label: string) (pattern: string) (actual: int) =
        let m = Regex.Match(inputs.ContractText, pattern, RegexOptions.Singleline)

        if not m.Success then
            [ finding
                  label
                  "contract-counts"
                  "the contract section states no count this clause can find — a count that cannot be located is a count that cannot be checked, and the prose has been reworded out of the gate" ]
        elif m.Groups[1].Value <> string actual then
            [ finding
                  label
                  "contract-counts"
                  (sprintf "the contract section says %s and `proofs.json` carries %d" m.Groups[1].Value actual) ]
        else
            []

    let classCount cls =
        assumedRows
        |> List.filter (fun r -> strMember r "class" = Some cls)
        |> List.length

    let countFindings =
        countIn "assumed rows" @"the (\d+) assumed rows" (List.length assumedRows)
        @ ([ "domain-obligation"; "model-bridge"; "premise" ]
           |> List.collect (fun cls ->
               countIn cls (sprintf @"\*\*`%s`.*?(\d+) rows\." (Regex.Escape cls)) (classCount cls)))

    let tally =
        { Modelled = Set.count modelled
          Excluded = List.length inputs.Exclusions
          Assumed = List.length assumedRows
          Premise = countAccount "premise"
          DomainDischarged = countAccount "domain-discharged"
          Permanent = countAccount "permanent"
          Unscheduled = countAccount "unscheduled"
          Scheduled = countAccount "scheduled" }

    modelFindings
    @ uncovered
    @ exclusionFindings
    @ assumedFindings
    @ differentialFindings
    @ countFindings,
    tally

/// THE LINE. Emitted whatever the verdict and saying which verdict it is: a line that only
/// appeared when green would make its absence the signal, and an absence is the one thing a reader
/// scanning a log does not see. `permanent` counts the rows nothing on any side closes — the
/// premises, and the bridges whose `closes` says so; `domain-discharged` is what a domain closes
/// at its own witness; `unscheduled` is kept separate from `permanent` deliberately, because
/// rounding the one into the other asserts an impossibility the contract section argues against at
/// length.
let renderPredicate (exhaustive: bool) (t: CoverageTally) : string =
    sprintf
        "proofs: %s (%d packages modelled, %d excluded; %d assumed: %d permanent, %d domain-discharged, %d unscheduled, %d scheduled)"
        (if exhaustive then "exhaustive" else "NOT exhaustive")
        t.Modelled
        t.Excluded
        t.Assumed
        (t.Premise + t.Permanent)
        t.DomainDischarged
        t.Unscheduled
        t.Scheduled

// ---------------------------------------------------------------------------
//  The real inputs
// ---------------------------------------------------------------------------

let private repoRoot = Snapshots.repoFile "."

let private readJson (relPath: string) =
    JsonDocument.Parse(File.ReadAllText(Snapshots.repoFile relPath))

let private liveModels () =
    use doc = readJson "proofs/modules.json"

    doc.RootElement.GetProperty("modules").EnumerateArray()
    |> Seq.map (fun el ->
        { Module = strMember el "module" |> Option.defaultValue ""
          Packages =
            match el.TryGetProperty "packages" with
            | true, v when v.ValueKind = JsonValueKind.Array ->
                v.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
            | _ -> [] })
    |> List.ofSeq

let private liveExclusionsDoc () =
    readJson "proofs/coverage-exclusions.json"

let private liveExclusions (root: JsonElement) =
    root.GetProperty("exclusions").EnumerateArray()
    |> Seq.map (fun el ->
        { Package = strMember el "package" |> Option.defaultValue ""
          Reason = strMember el "reason" |> Option.defaultValue ""
          Family = strMember el "family"
          Note = strMember el "note" |> Option.defaultValue ""
          Phase = strMember el "phase" |> Option.defaultValue "" })
    |> List.ofSeq

let private liveReasons (root: JsonElement) =
    root.GetProperty("reasons").EnumerateObject() |> Seq.map _.Name |> Set.ofSeq

let private liveLaws () =
    KitRoster.families
    |> List.filter (fun f -> f.Module = "Conformance")
    |> List.map _.Entry
    |> Set.ofList

/// The contract section of `proofs/README.md` — from its heading to the next one. Sliced rather
/// than searched whole, so a count elsewhere in a five-thousand-line document cannot answer for
/// the one this clause is about.
let contractSection (readmeText: string) : string =
    let head = "## The Core-to-domain proof contract"

    match readmeText.IndexOf head with
    | -1 -> ""
    | i ->
        let rest = readmeText.Substring(i + head.Length)

        match rest.IndexOf "\n## " with
        | -1 -> rest
        | j -> rest.Substring(0, j)

/// The open phases on this side, where an oracle is configured. The roadmap store is not in this
/// repository — it cannot be, since phases routinely name siblings a public repository must not —
/// so the path is named by `FUARAN_CORE_ROADMAP` and its absence is only a finding for a row that
/// actually makes a scheduling claim. The index rows read `| NNN | title | Status | ...`; OPEN is
/// every status but `Shipped`.
let openPhasesFrom (indexText: string) : Set<string> =
    Regex.Matches(indexText, @"^\|\s*(\d+)\s*\|[^|]*\|\s*([^|]+?)\s*\|", RegexOptions.Multiline)
    |> Seq.filter (fun m -> m.Groups[2].Value <> "Shipped")
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Set.ofSeq

let private liveOpenPhases () =
    match Environment.GetEnvironmentVariable "FUARAN_CORE_ROADMAP" with
    | null
    | "" -> None
    | dir ->
        let index = Path.Combine(dir, "INDEX.md")

        if File.Exists index then
            Some(openPhasesFrom (File.ReadAllText index))
        else
            None

let private liveInputs () =
    use exclusionsDoc = liveExclusionsDoc ()

    { Packable = PackageRosterTests.packableProjects repoRoot |> List.map _.PackageId
      Models = liveModels ()
      Exclusions = liveExclusions exclusionsDoc.RootElement
      Reasons = liveReasons exclusionsDoc.RootElement
      Laws = liveLaws ()
      OpenPhases = liveOpenPhases ()
      ContractText = contractSection (File.ReadAllText(Snapshots.repoFile "proofs/README.md")) }

// ---------------------------------------------------------------------------
//  The synthetic baseline, and one perturbation per go-red
// ---------------------------------------------------------------------------

let private fixtureLadder =
    """
{
  "claims": [
    { "id": "t1", "level": "proved", "phase": "fuaran-core#1",
      "evidence": { "theorem": "t", "model": "proofs/MA.fst" } },
    { "id": "t2", "level": "tested", "phase": "fuaran-core#1",
      "evidence": { "family": "Proofs.Oracle", "model": "proofs/MA.fst", "cases": [ "a case" ] } },
    { "id": "t3", "level": "assumed", "class": "premise", "phase": "fuaran-core#1" },
    { "id": "t4", "level": "assumed", "class": "domain-obligation", "dischargedBy": "Conformance.fixtureLaws", "phase": "fuaran-core#1" },
    { "id": "t5", "level": "assumed", "class": "model-bridge", "closes": "permanent", "phase": "fuaran-core#1" }
  ]
}
"""

let private fixtureContract =
    """
Every ladder ends at level 3, and the 3 assumed rows across these ladders are three kinds of thing.

- **`domain-obligation`** — what you owe. 1 rows.
- **`model-bridge`** — what this repository has not bridged. 1 rows.
- **`premise`** — what nobody discharges. 1 rows.
"""

let private fixtureBase =
    { Packable = [ "Pkg.A"; "Pkg.B" ]
      Models =
        [ { Module = "MA"
            Packages = [ "Pkg.A" ] } ]
      Exclusions =
        [ { Package = "Pkg.B"
            Reason = "facade"
            Family = None
            Note = "a fixture facade"
            Phase = "fuaran-core#203" } ]
      Reasons = Set.ofList [ "facade"; "law-tested-by-design"; "content-free-seam" ]
      Laws = Set.ofList [ "fixtureLaws" ]
      OpenPhases = Some(Set.ofList [ "999" ])
      ContractText = fixtureContract }

/// A go-red: this perturbation yields EXACTLY one finding, naming the clause and its subject.
let private expectOneFinding
    (name: string)
    (inputs: CoverageInputs)
    (ladder: string)
    (clause: string)
    (subject: string)
    =
    let findings, _ = checkCoverage inputs ladder

    Expect.equal
        (List.length findings)
        1
        (sprintf
            "%s carries exactly one defect, so exactly one finding is expected — got:\n%s"
            name
            (String.concat "\n" findings))

    let only = List.head findings
    Expect.stringContains only (sprintf "[%s]" clause) (sprintf "%s: the finding names the clause" name)
    Expect.stringContains only subject (sprintf "%s: the finding names what it is about" name)

// ---------------------------------------------------------------------------

[<Tests>]
let proofCoverageTests =
    testList
        "Proofs.Coverage"
        [

          // ---- the baseline is clean, or every go-red below is measuring the baseline ----

          testCase "the synthetic baseline is exhaustive"
          <| fun _ ->
              let findings, tally = checkCoverage fixtureBase fixtureLadder

              Expect.isEmpty findings (sprintf "the baseline must be clean — got:\n%s" (String.concat "\n" findings))
              Expect.equal tally.Modelled 1 "one package modelled"
              Expect.equal tally.Excluded 1 "one package excluded"
              Expect.equal tally.Assumed 3 "three assumed rows"

          // ---- THE PREDICATE, on this tree ----

          testCase "the predicate is exhaustive on this tree, and says so in one line"
          <| fun _ ->
              let inputs = liveInputs ()
              let ladder = File.ReadAllText(Snapshots.repoFile "proofs.json")
              let findings, tally = checkCoverage inputs ladder

              printfn "%s" (renderPredicate (List.isEmpty findings) tally)

              Expect.isEmpty
                  findings
                  (sprintf
                      "\"exhaustively proved\" is this predicate, and it is not met — each finding names the clause and the subject:\n%s"
                      (String.concat "\n" findings))

              Expect.equal
                  (tally.Modelled + tally.Excluded)
                  (List.length inputs.Packable)
                  "every packable package is modelled or excluded, and no package is both"

          testCase "the inputs are real, so the clauses are not quantifying over nothing"
          <| fun _ ->
              let inputs = liveInputs ()

              Expect.isGreaterThan (List.length inputs.Packable) 10 "the packable roster is read, not empty"
              Expect.isGreaterThan (List.length inputs.Models) 10 "the model roster is read, not empty"
              Expect.isNonEmpty inputs.Exclusions "the exclusions file is read, not empty"
              Expect.isNonEmpty inputs.Reasons "the reason vocabulary is read from the file that closes it"
              Expect.isGreaterThan (Set.count inputs.Laws) 10 "the shipped law roster is read"
              Expect.isNonEmpty inputs.ContractText "the contract section was located in the README"

          testCase "every reason in the closed vocabulary is carried by at least one entry"
          <| fun _ ->
              let inputs = liveInputs ()
              let used = inputs.Exclusions |> List.map _.Reason |> Set.ofList

              Expect.equal
                  (Set.difference inputs.Reasons used)
                  Set.empty
                  "a reason no entry carries is a vocabulary term nothing tests — carry it or drop it"

          // ---- clause 1 ----

          testCase "go-red: coverage-declared — a packable package in neither list"
          <| fun _ ->
              expectOneFinding
                  "an uncovered package"
                  { fixtureBase with
                      Packable = [ "Pkg.A"; "Pkg.B"; "Pkg.C" ] }
                  fixtureLadder
                  "coverage-declared"
                  "Pkg.C"

          testCase "go-red: exclusion-stale — an exclusion for a package that now has a model"
          <| fun _ ->
              expectOneFinding
                  "an exclusion beside a model"
                  { fixtureBase with
                      Models =
                          [ { Module = "MA"
                              Packages = [ "Pkg.A"; "Pkg.B" ] } ] }
                  fixtureLadder
                  "exclusion-stale"
                  "Pkg.B"

          testCase "go-red: exclusion-stale — an exclusion naming a package that is not packable"
          <| fun _ ->
              expectOneFinding
                  "an exclusion for a vanished package"
                  { fixtureBase with
                      Packable = [ "Pkg.A" ]
                      Exclusions =
                          [ { fixtureBase.Exclusions.Head with
                                Package = "Pkg.Gone" } ] }
                  fixtureLadder
                  "exclusion-stale"
                  "Pkg.Gone"

          testCase "go-red: exclusion-reason — a reason outside the closed vocabulary"
          <| fun _ ->
              expectOneFinding
                  "an invented reason"
                  { fixtureBase with
                      Exclusions =
                          [ { fixtureBase.Exclusions.Head with
                                Reason = "too-hard" } ] }
                  fixtureLadder
                  "exclusion-reason"
                  "Pkg.B"

          testCase "go-red: exclusion-family — a declined theorem naming a law the roster does not declare"
          <| fun _ ->
              expectOneFinding
                  "an unrosterable law"
                  { fixtureBase with
                      Exclusions =
                          [ { fixtureBase.Exclusions.Head with
                                Reason = "law-tested-by-design"
                                Family = Some "Conformance.inventedLaws" } ] }
                  fixtureLadder
                  "exclusion-family"
                  "Pkg.B"

          testCase "go-red: exclusion-family — a non-law reason carrying a family"
          <| fun _ ->
              expectOneFinding
                  "a facade citing a law"
                  { fixtureBase with
                      Exclusions =
                          [ { fixtureBase.Exclusions.Head with
                                Family = Some "Conformance.fixtureLaws" } ] }
                  fixtureLadder
                  "exclusion-family"
                  "Pkg.B"

          testCase "go-red: model-package — a model attributed to a package that is not packable"
          <| fun _ ->
              expectOneFinding
                  "a stale attribution"
                  { fixtureBase with
                      Models =
                          [ { Module = "MA"
                              Packages = [ "Pkg.A"; "Pkg.Gone" ] } ] }
                  fixtureLadder
                  "model-package"
                  "Pkg.Gone"

          // ---- clause 2 ----

          // The contract count moves WITH the class here, and is moved with it: an unclassed row
          // leaves its class's count, so leaving the prose at 1 would red two clauses and this
          // go-red would be measuring the count clause as much as the one it names.
          testCase "go-red: assumed-accounted — an assumed row stripped of its class"
          <| fun _ ->
              expectOneFinding
                  "an unclassed assumption"
                  { fixtureBase with
                      ContractText =
                          fixtureContract.Replace("what nobody discharges. 1 rows.", "what nobody discharges. 0 rows.") }
                  (fixtureLadder.Replace("\"class\": \"premise\", ", ""))
                  "assumed-accounted"
                  "t3"

          testCase "go-red: assumed-accounted — a model bridge with no closes"
          <| fun _ ->
              expectOneFinding
                  "an unaccounted bridge"
                  fixtureBase
                  (fixtureLadder.Replace("\"closes\": \"permanent\", ", ""))
                  "assumed-accounted"
                  "t5"

          testCase "go-red: closes-open — a scheduled row naming a phase that is not open"
          <| fun _ ->
              expectOneFinding
                  "a row scheduled against a shipped phase"
                  fixtureBase
                  (fixtureLadder.Replace("\"closes\": \"permanent\"", "\"closes\": \"fuaran-core#7\""))
                  "closes-open"
                  "t5"

          testCase "go-red: closes-open — a scheduling claim with no oracle to check it against"
          <| fun _ ->
              expectOneFinding
                  "a scheduling claim and no oracle"
                  { fixtureBase with OpenPhases = None }
                  (fixtureLadder.Replace("\"closes\": \"permanent\"", "\"closes\": \"fuaran-core#999\""))
                  "closes-open"
                  "t5"

          testCase "a scheduled row naming an OPEN phase is clean, so the clause can pass"
          <| fun _ ->
              let findings, tally =
                  checkCoverage
                      fixtureBase
                      (fixtureLadder.Replace("\"closes\": \"permanent\"", "\"closes\": \"fuaran-core#999\""))

              Expect.isEmpty findings (sprintf "an open phase closes cleanly — got:\n%s" (String.concat "\n" findings))
              Expect.equal tally.Scheduled 1 "the row counts as scheduled"

          // ---- clause 3 ----

          testCase "go-red: differential-paired — a differential whose model carries no theorem"
          <| fun _ ->
              expectOneFinding
                  "a differential with no theorem"
                  { fixtureBase with
                      Models =
                          [ { Module = "MA"
                              Packages = [ "Pkg.A" ] }
                            { Module = "MB"
                              Packages = [ "Pkg.A" ] } ] }
                  (fixtureLadder.Replace(
                      "\"model\": \"proofs/MA.fst\", \"cases\"",
                      "\"model\": \"proofs/MB.fst\", \"cases\""
                  ))
                  "differential-paired"
                  "t2"

          testCase "go-red: differential-paired — a differential naming no model at all"
          <| fun _ ->
              expectOneFinding
                  "an unpaired differential"
                  fixtureBase
                  (fixtureLadder.Replace("\"model\": \"proofs/MA.fst\", \"cases\"", "\"cases\""))
                  "differential-paired"
                  "t2"

          testCase "go-red: differential-paired — a law row carrying a model"
          <| fun _ ->
              expectOneFinding
                  "a law row pretending to be a differential"
                  fixtureBase
                  (fixtureLadder.Replace("\"family\": \"Proofs.Oracle\"", "\"family\": \"Conformance.fixtureLaws\""))
                  "differential-paired"
                  "t2"

          // ---- the count clause ----

          testCase "go-red: contract-counts — the prose total disagrees with the ladder"
          <| fun _ ->
              expectOneFinding
                  "a stale total"
                  { fixtureBase with
                      ContractText = fixtureContract.Replace("the 3 assumed rows", "the 4 assumed rows") }
                  fixtureLadder
                  "contract-counts"
                  "assumed rows"

          testCase "go-red: contract-counts — a per-class count disagrees with the ladder"
          <| fun _ ->
              expectOneFinding
                  "a stale class count"
                  { fixtureBase with
                      ContractText = fixtureContract.Replace("what you owe. 1 rows.", "what you owe. 2 rows.") }
                  fixtureLadder
                  "contract-counts"
                  "domain-obligation"

          testCase "go-red: contract-counts — the prose reworded out of the gate"
          <| fun _ ->
              expectOneFinding
                  "a count nothing can find"
                  { fixtureBase with
                      ContractText = fixtureContract.Replace("the 3 assumed rows", "the assumed rows") }
                  fixtureLadder
                  "contract-counts"
                  "assumed rows"

          // ---- the oracle reader ----

          testCase "the open-phase reader takes the status column, not the row's presence"
          <| fun _ ->
              let index =
                  "| 12 | a shipped thing | Shipped | fuaran-core | [x](y) |\n\
                   | 13 | an open thing | Not started | fuaran-core | [x](y) |\n\
                   | 14 | a live thing | In progress | fuaran-core | [x](y) |\n"

              Expect.equal
                  (openPhasesFrom index)
                  (Set.ofList [ "13"; "14" ])
                  "Shipped is closed, everything else is open" ]
