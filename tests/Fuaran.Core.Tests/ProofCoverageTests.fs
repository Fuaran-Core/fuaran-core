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
//    shipped-phase and the no-oracle arms. The oracle is a COMMITTED data file,
//    `tests/Fuaran.Core.Tests/open-phases.json` (Phase 294): either the list of open phase ids or a declaration that
//    no list is kept, with the reason. It replaces an environment variable that named a private
//    projection's rendered index — a public test tied to another repository's output, which public
//    CI never set, so the first real scheduling claim would have reddened it permanently. Where a
//    row makes a scheduling claim and the file declares itself inert, the clause FAILS rather than
//    passes, because a check that reads as green without its instrument is worse than an absent one.
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
        /// The phase ids that are OPEN on this side, from `tests/Fuaran.Core.Tests/open-phases.json`. `None` is the
        /// file's declared-inert form — "no list is kept" — which is a finding for any row that makes
        /// a scheduling claim and inert for every row that does not.
        OpenPhases: Set<string> option
        /// The contract section of `proofs/README.md`, whose prose counts are held to the ladder.
        ContractText: string
    }

/// What the predicate says, once computed — the numbers the emitted line renders.
type CoverageTally =
    {
        Modelled: int
        Excluded: int
        Assumed: int
        Premise: int
        DomainDischarged: int
        /// A `domain-obligation` whose law checks the domain's DECLARATION and cannot check past it
        /// (`"discharge": "domain-declared"`, Phase 309) — counted apart, because a green run of its
        /// law is not a discharge.
        DomainDeclared: int
        Permanent: int
        Unscheduled: int
        Scheduled: int
    }

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
            | Some _ when strMember row "discharge" = Some "domain-declared" -> [], Some "domain-declared"
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
                              "schedules `closes: %s` and tests/Fuaran.Core.Tests/open-phases.json declares no list of open phases — record the open phases there; a scheduling claim nothing can check must not read as green"
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
          DomainDeclared = countAccount "domain-declared"
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
        "proofs: %s (%d packages modelled, %d excluded; %d assumed: %d permanent, %d domain-discharged, %d domain-declared, %d unscheduled, %d scheduled)"
        (if exhaustive then "exhaustive" else "NOT exhaustive")
        t.Modelled
        t.Excluded
        t.Assumed
        (t.Premise + t.Permanent)
        t.DomainDischarged
        t.DomainDeclared
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

/// The open phases on this side, from `tests/Fuaran.Core.Tests/open-phases.json`. The roadmap store is not in this
/// repository — it cannot be, since phases routinely name siblings a public repository must not —
/// so what this repository keeps is the one fact the clause needs, as DATA: the ids of the phases
/// that are open. The file has exactly one of two shapes, and a file with both or neither is
/// refused, because a reader that guessed which was meant would make the clause's verdict depend on
/// the guess:
///
///  * `{ "open": ["NNN", ...] }` — the list, written by whoever owns the phases;
///  * `{ "inert": "<why no list is kept>" }` — the declared-inert form. The reason is required and
///    must be prose, so an inert file says what instrument is missing instead of merely being absent.
///
/// `None` is the inert form.
let openPhasesFromJson (text: string) : Result<Set<string> option, string> =
    try
        use doc = JsonDocument.Parse text
        let root = doc.RootElement

        if root.ValueKind <> JsonValueKind.Object then
            Error "open-phases.json is not a JSON object"
        else
            let has (name: string) =
                match root.TryGetProperty name with
                | true, _ -> true
                | _ -> false

            match has "open", has "inert" with
            | true, true -> Error "open-phases.json carries both `open` and `inert` — it must be exactly one"
            | false, false -> Error "open-phases.json carries neither `open` nor `inert` — it must be exactly one"
            | false, true ->
                let inert = root.GetProperty "inert"

                if inert.ValueKind = JsonValueKind.String && inert.GetString().Trim().Length >= 20 then
                    Ok None
                else
                    Error "`inert` must be a reason in prose (a string of at least 20 characters), not a bare flag"
            | true, false ->
                let openNode = root.GetProperty "open"

                if openNode.ValueKind <> JsonValueKind.Array then
                    Error "`open` must be an array of phase-id strings"
                else
                    let ids =
                        [ for e in openNode.EnumerateArray() ->
                              if e.ValueKind = JsonValueKind.String then
                                  e.GetString()
                              else
                                  null ]

                    if
                        ids
                        |> List.exists (fun i -> isNull i || not (Regex.IsMatch(i, @"^[1-9][0-9]*$")))
                    then
                        Error "`open` holds an entry that is not a phase id (a positive integer, as a string)"
                    else
                        Ok(Some(Set.ofList ids))
    with :? JsonException as e ->
        Error(sprintf "open-phases.json is not valid JSON: %s" e.Message)

let private liveOpenPhases () =
    match openPhasesFromJson (File.ReadAllText(Snapshots.repoFile "tests/Fuaran.Core.Tests/open-phases.json")) with
    | Ok phases -> phases
    | Error why -> failwithf "tests/Fuaran.Core.Tests/open-phases.json is malformed: %s" why

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
//  Phase 335 — the OPERATION clause
//
//  Clause 1 stops at PACKAGE granularity: a package with a model is covered, so a public operation
//  inside it could ship with no proved row, no law family and no recorded reason, and nothing said
//  so. The census here is the committed API baselines (`api/*.txt`), which already enumerate every
//  public operation by signature: every `method` line maps to at least one of
//
//    (a) a `proofs.json` row whose `evidence.operations` names it (a `proved` or `tested` row);
//    (b) a law family whose operation roster (`Families.operations`) lists it — and a family's own
//        entry point is mapped by its roster row;
//    (c) an entry in `proofs/coverage-exclusions.json`'s `operations` block, with a class from that
//        file's closed `operationClasses` vocabulary and a one-line reason.
//
//  An unmapped operation reds the suite and names itself, and because the census IS the baselines,
//  a newly published operation reds at the commit that publishes it. The check runs both ways, on
//  clause 1's precedent: an entry for an operation that is no longer published, or that has since
//  gained a ladder row or a roster line, fails too. And each class is held to what it says, as far
//  as a file can be: a `forward` names a mapped target, an `obsolete` entry's member carries
//  `System.Obsolete`, and a `measured-elsewhere` entry names a test file that mentions the member or
//  the measured operation it is reached `through`.
// ---------------------------------------------------------------------------

/// One public operation of the census: the package whose baseline publishes it, its spelling in the
/// coverage records, and the baseline's CLR name.
type Operation =
    { Package: string
      Name: string
      Clr: string }

/// One entry of `coverage-exclusions.json`'s `operations` block.
type OperationExclusion =
    { Members: string list
      Class: string
      Reason: string
      ForwardsTo: string option
      StandIn: string option
      Through: string option }

type OperationInputs =
    {
        Census: Operation list
        /// `proofs.json` rows that carry `evidence.operations`: (row id, level, operations).
        Ladder: (string * string * string list) list
        /// The kit's operation roster: (family id, operations).
        Rosters: (string * string list) list
        /// Every declared law-family id — an entry point named here is mapped by its own row.
        Families: Set<string>
        Exclusions: OperationExclusion list
        /// The class vocabulary, read from the exclusions file's own `operationClasses` block.
        Classes: Set<string>
        /// The text of each stand-in a `measured-elsewhere` entry names, by its repo-relative path. A
        /// path absent from the map does not exist (or is not under `tests/`).
        StandIns: Map<string, string>
        /// The census operations that carry `System.Obsolete`, on itself or its declaring type.
        Obsolete: Set<string>
    }

/// How the census divides, each operation counted once, by the first route that maps it.
type OperationTally =
    { Operations: int
      Ladder: int
      Rostered: int
      ByClass: (string * int) list
      ByPackage: (string * int * int * int * (string * int) list) list }

/// The class order the records and the README table use.
let operationClassOrder =
    [ "trivial"; "forward"; "obsolete"; "host-seam"; "measured-elsewhere" ]

/// A baseline's CLR member name as the coverage records spell it: `Fuaran.Core.` dropped, generic
/// arity dropped, nested types joined by `.`, and the compiler's `Module` suffix dropped from every
/// type segment (`Fuaran.Core.Dag+ReachModule.ancestors` is `Dag.Reach.ancestors`).
let operationName (clr: string) : string =
    let rest =
        if clr.StartsWith "Fuaran.Core." then
            clr.Substring "Fuaran.Core.".Length
        else
            clr

    let segs = Regex.Replace(rest, "`[0-9]+", "").Replace('+', '.').Split('.')

    segs
    |> Array.mapi (fun i s ->
        if i < segs.Length - 1 && s.Length > 6 && s.EndsWith "Module" then
            s.Substring(0, s.Length - 6)
        else
            s)
    |> String.concat "."

/// The `method` lines of one package's baseline, as operations.
let censusOf (package: string) (baselineText: string) : Operation list =
    baselineText.Split('\n')
    |> Array.choose (fun raw ->
        let line = raw.TrimEnd '\r'

        if line.StartsWith "method " && line.IndexOf '(' > 7 then
            let clr = line.Substring(7, line.IndexOf '(' - 7)

            Some
                { Package = package
                  Name = operationName clr
                  Clr = clr }
        else
            None)
    |> List.ofArray

/// `<Owner>.<member>` — the last two segments, which is how a test file names an operation.
let private twoSegments (op: string) =
    let s = op.Split '.'

    if s.Length >= 2 then
        s[s.Length - 2] + "." + s[s.Length - 1]
    else
        op

/// Whether `text` mentions `token`, bounded as a word on each side where the token has a word
/// character there (a `.Member` token may follow an identifier).
let private mentions (text: string) (token: string) =
    let isWord (c: char) = Char.IsLetterOrDigit c || c = '_'

    token.Length > 0
    && Regex.IsMatch(
        text,
        (if isWord token[0] then @"(?<![\w])" else "")
        + Regex.Escape token
        + (if isWord token[token.Length - 1] then @"(?![\w])" else "")
    )

/// Every finding the operation clause yields, with the tally the README table renders.
let checkOperations (inputs: OperationInputs) : string list * OperationTally =
    let census = inputs.Census |> List.map _.Name |> Set.ofList

    let ladderOps =
        inputs.Ladder
        |> List.filter (fun (_, level, _) -> level = "proved" || level = "tested")
        |> List.collect (fun (_, _, ops) -> ops)
        |> Set.ofList

    let rosterOps = inputs.Rosters |> List.collect snd |> Set.ofList

    let entryOps = Set.intersect census inputs.Families

    let excluded =
        inputs.Exclusions
        |> List.collect (fun e -> e.Members |> List.map (fun m -> m, e))
        |> List.groupBy fst
        |> List.map (fun (m, es) -> m, es |> List.map snd)
        |> Map.ofList

    let directlyMapped (op: string) =
        ladderOps.Contains op
        || rosterOps.Contains op
        || entryOps.Contains op
        || (match Map.tryFind op excluded with
            | Some(e :: _) -> e.Class <> "forward" && e.Class <> "obsolete"
            | _ -> false)

    // A forward's coverage is its target's: follow `forwardsTo` until a directly mapped operation,
    // refusing a cycle and a chain that ends nowhere.
    let rec resolves (seen: Set<string>) (op: string) =
        if directlyMapped op then
            true
        elif seen.Contains op then
            false
        else
            match Map.tryFind op excluded with
            | Some(e :: _) when (e.Class = "forward" || e.Class = "obsolete") ->
                match e.ForwardsTo with
                | Some t -> resolves (seen.Add op) t
                | None -> false
            | _ -> false

    let ladderFindings =
        inputs.Ladder
        |> List.collect (fun (id, level, ops) ->
            [ if level <> "proved" && level <> "tested" then
                  yield
                      finding
                          id
                          "operation-ladder"
                          (sprintf
                              "a %s row names operations; only a proved or tested row is evidence an operation is covered"
                              level)
              for op in ops do
                  if not (census.Contains op) then
                      yield
                          finding
                              id
                              "operation-ladder"
                              (sprintf "`evidence.operations` names %s, which no API baseline publishes" op) ])

    let rosterFindings =
        [ for fam, ops in inputs.Rosters do
              if not (inputs.Families.Contains fam) then
                  yield
                      finding fam "operation-roster" "an operation-roster row for a family the roster does not declare"

              for op in ops do
                  if not (census.Contains op) then
                      yield finding fam "operation-roster" (sprintf "lists %s, which no API baseline publishes" op)
          for fam, n in inputs.Rosters |> List.countBy fst do
              if n > 1 then
                  yield finding fam "operation-roster" (sprintf "%d operation-roster rows; one per family" n) ]

    let exclusionFindings =
        [ for KeyValue(m, es) in excluded do
              if List.length es > 1 then
                  yield
                      finding
                          m
                          "operation-exclusion-stale"
                          (sprintf "listed by %d entries; one decision per operation" es.Length)

              if not (census.Contains m) then
                  yield finding m "operation-exclusion-stale" "an exclusion for an operation no API baseline publishes"
              elif ladderOps.Contains m || rosterOps.Contains m || entryOps.Contains m then
                  yield
                      finding
                          m
                          "operation-exclusion-stale"
                          "an exclusion for an operation that has since gained a ladder row or a roster line — delete the entry"
          for e in inputs.Exclusions do
              let subject = String.concat ", " e.Members

              if List.isEmpty e.Members then
                  yield finding "(empty entry)" "operation-exclusion-class" "an entry naming no operation"

              if not (inputs.Classes.Contains e.Class) then
                  yield
                      finding
                          subject
                          "operation-exclusion-class"
                          (sprintf "class '%s' is not in the closed vocabulary %A" e.Class (Set.toList inputs.Classes))

              if String.IsNullOrWhiteSpace e.Reason then
                  yield finding subject "operation-exclusion-reason" "an entry with no reason"

              let forwarding = e.Class = "forward" || e.Class = "obsolete"

              match forwarding, e.ForwardsTo with
              | true, None ->
                  yield finding subject "operation-forward" (sprintf "a %s entry names no `forwardsTo`" e.Class)
              | false, Some _ ->
                  yield finding subject "operation-forward" "`forwardsTo` belongs to a forward or obsolete entry"
              | true, Some t when not (census.Contains t) ->
                  yield
                      finding subject "operation-forward" (sprintf "forwards to %s, which no API baseline publishes" t)
              | true, Some t ->
                  for m in e.Members do
                      if census.Contains m && not (resolves Set.empty m) then
                          yield
                              finding
                                  m
                                  "operation-forward"
                                  (sprintf "forwards to %s, which is not itself mapped (or the chain is a cycle)" t)
              | false, None -> ()

              if e.Class = "obsolete" then
                  for m in e.Members do
                      if census.Contains m && not (inputs.Obsolete.Contains m) then
                          yield
                              finding
                                  m
                                  "operation-obsolete"
                                  "an obsolete entry for an operation that carries no System.Obsolete"

              match e.Class = "measured-elsewhere", e.StandIn with
              | true, None -> yield finding subject "operation-stand-in" "a measured-elsewhere entry names no `standIn`"
              | false, Some _ ->
                  yield finding subject "operation-stand-in" "`standIn` belongs to a measured-elsewhere entry"
              | true, Some path ->
                  match Map.tryFind path inputs.StandIns with
                  | None ->
                      yield
                          finding
                              subject
                              "operation-stand-in"
                              (sprintf "stand-in %s is not a test file under tests/ in this repository" path)
                  | Some text ->
                      for m in e.Members do
                          let token = e.Through |> Option.defaultValue (twoSegments m)

                          if not (mentions text token) then
                              yield
                                  finding m "operation-stand-in" (sprintf "stand-in %s never mentions `%s`" path token)
              | false, None -> ()

              if e.Class <> "measured-elsewhere" && e.Through.IsSome then
                  yield finding subject "operation-stand-in" "`through` belongs to a measured-elsewhere entry" ]

    let unmapped =
        inputs.Census
        |> List.filter (fun o -> not (resolves Set.empty o.Name) && not (Map.containsKey o.Name excluded))
        |> List.map (fun o ->
            finding
                o.Name
                "operation-mapped"
                (sprintf
                    "published by %s and mapped to no ladder row, law-family roster line or recorded exclusion"
                    o.Package))

    let routeOf (op: string) =
        if ladderOps.Contains op then
            "ladder"
        elif rosterOps.Contains op || entryOps.Contains op then
            "family"
        else
            match Map.tryFind op excluded with
            | Some(e :: _) -> e.Class
            | _ -> "unmapped"

    let countIn (ops: Operation list) (route: string) =
        ops |> List.filter (fun o -> routeOf o.Name = route) |> List.length

    let tally =
        { Operations = List.length inputs.Census
          Ladder = countIn inputs.Census "ladder"
          Rostered = countIn inputs.Census "family"
          ByClass = operationClassOrder |> List.map (fun c -> c, countIn inputs.Census c)
          ByPackage =
            inputs.Census
            |> List.groupBy _.Package
            |> List.sortBy fst
            |> List.map (fun (p, ops) ->
                p,
                List.length ops,
                countIn ops "ladder",
                countIn ops "family",
                operationClassOrder |> List.map (fun c -> c, countIn ops c)) }

    ladderFindings @ rosterFindings @ exclusionFindings @ unmapped, tally

/// The operation clause's line, emitted whatever the verdict.
let renderOperationPredicate (mapped: bool) (t: OperationTally) : string =
    sprintf
        "proofs: operations %s (%d public operations: %d on the ladder, %d by a law family, %d excluded — %s)"
        (if mapped then "mapped" else "NOT mapped")
        t.Operations
        t.Ladder
        t.Rostered
        (t.ByClass |> List.sumBy snd)
        (t.ByClass |> List.map (fun (c, n) -> sprintf "%d %s" n c) |> String.concat ", ")

let operationTableBegin =
    "<!-- operation-coverage:begin — generated from ../api/*.txt, ../proofs.json, the kit's operation roster and coverage-exclusions.json by the Proofs.Coverage family; CORE_APPROVE_LADDER=1 rewrites it -->"

let operationTableEnd = "<!-- operation-coverage:end -->"

/// The README's per-package table: each package's operations by the route that maps them.
let renderOperationTable (t: OperationTally) : string =
    let header =
        [ "| Package | Operations | Ladder | Law family | "
          + (operationClassOrder |> String.concat " | ")
          + " |"
          "|---|---:|---:|---:|"
          + (operationClassOrder |> List.map (fun _ -> "---:|") |> String.concat "") ]

    let row (name: string) (ops: int) (lad: int) (fam: int) (classes: (string * int) list) =
        sprintf
            "| %s | %d | %d | %d | %s |"
            name
            ops
            lad
            fam
            (classes |> List.map (snd >> string) |> String.concat " | ")

    let rows =
        t.ByPackage
        |> List.map (fun (p, ops, lad, fam, cls) -> row ("`" + p + "`") ops lad fam cls)

    header @ rows @ [ row "**Total**" t.Operations t.Ladder t.Rostered t.ByClass ]
    |> String.concat "\n"

/// The README with its operation table replaced by `table`, or why the markers were not found.
let regenerateOperationTable (table: string) (readmeText: string) : Result<string, string> =
    let text = readmeText.Replace("\r\n", "\n")

    match text.IndexOf operationTableBegin, text.IndexOf operationTableEnd with
    | -1, _
    | _, -1 -> Error "the operation-coverage markers are missing from proofs/README.md"
    | b, e when e < b -> Error "the operation-coverage end marker precedes its begin marker"
    | b, e ->
        Ok(
            text.Substring(0, b + operationTableBegin.Length)
            + "\n"
            + table
            + "\n"
            + text.Substring e
        )

// ---- the live inputs ----

let private liveCensus () : Operation list =
    Directory.GetFiles(Snapshots.repoFile "api", "*.txt")
    |> Array.sort
    |> Array.toList
    |> List.collect (fun path -> censusOf (Path.GetFileNameWithoutExtension path) (File.ReadAllText path))

let private liveLadderOperations () =
    use doc = readJson "proofs.json"

    doc.RootElement.GetProperty("claims").EnumerateArray()
    |> Seq.choose (fun row ->
        match objMember row "evidence" with
        | Some ev ->
            match ev.TryGetProperty "operations" with
            | true, ops when ops.ValueKind = JsonValueKind.Array ->
                Some(
                    strMember row "id" |> Option.defaultValue "",
                    strMember row "level" |> Option.defaultValue "",
                    ops.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
                )
            | _ -> None
        | None -> None)
    |> List.ofSeq

let private operationExclusionsOf (root: JsonElement) : OperationExclusion list =
    match root.TryGetProperty "operations" with
    | true, ops when ops.ValueKind = JsonValueKind.Array ->
        ops.EnumerateArray()
        |> Seq.map (fun el ->
            { Members =
                match el.TryGetProperty "members" with
                | true, v when v.ValueKind = JsonValueKind.Array ->
                    v.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq
                | _ -> []
              Class = strMember el "class" |> Option.defaultValue ""
              Reason = strMember el "reason" |> Option.defaultValue ""
              ForwardsTo = strMember el "forwardsTo"
              StandIn = strMember el "standIn"
              Through = strMember el "through" })
        |> List.ofSeq
    | _ -> []

let private operationClassesOf (root: JsonElement) : Set<string> =
    match root.TryGetProperty "operationClasses" with
    | true, v when v.ValueKind = JsonValueKind.Object -> v.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
    | _ -> Set.empty

/// Whether the baseline member carries `System.Obsolete`, on itself or on a declaring type —
/// read from the loaded assembly, so an `obsolete` entry cannot claim an attribute the code lacks.
let private carriesObsolete (o: Operation) : bool =
    try
        let asm = Reflection.Assembly.Load(Reflection.AssemblyName o.Package)
        let cut = o.Clr.LastIndexOf '.'
        let ty = asm.GetType(o.Clr.Substring(0, cut))
        let name = Regex.Replace(o.Clr.Substring(cut + 1), "`[0-9]+$", "")

        let rec typeObsolete (t: Type) =
            not (isNull t)
            && (t.IsDefined(typeof<ObsoleteAttribute>, false) || typeObsolete t.DeclaringType)

        not (isNull ty)
        && (typeObsolete ty
            || ty.GetMethods(
                Reflection.BindingFlags.Public
                ||| Reflection.BindingFlags.Static
                ||| Reflection.BindingFlags.Instance
               )
               |> Array.exists (fun m -> m.Name = name && m.IsDefined(typeof<ObsoleteAttribute>, false)))
    with _ ->
        false

let private liveOperationInputs () : OperationInputs =
    use exclusionsDoc = liveExclusionsDoc ()
    let exclusions = operationExclusionsOf exclusionsDoc.RootElement
    let census = liveCensus ()

    let claimedObsolete =
        exclusions
        |> List.filter (fun e -> e.Class = "obsolete")
        |> List.collect _.Members
        |> Set.ofList

    let standIns =
        exclusions
        |> List.choose _.StandIn
        |> List.distinct
        |> List.choose (fun p ->
            let full = Snapshots.repoFile p

            if p.StartsWith "tests/" && not (p.Contains "..") && File.Exists full then
                Some(p, File.ReadAllText full)
            else
                None)
        |> Map.ofList

    { Census = census
      Ladder = liveLadderOperations ()
      Rosters = Fuaran.Core.Families.operations |> List.map (fun r -> r.Family, r.Operations)
      Families = KitRoster.ids |> Set.ofList
      Exclusions = exclusions
      Classes = operationClassesOf exclusionsDoc.RootElement
      StandIns = standIns
      Obsolete =
        census
        |> List.filter (fun o -> claimedObsolete.Contains o.Name && carriesObsolete o)
        |> List.map _.Name
        |> Set.ofList }

// ---- the synthetic baseline the go-reds perturb ----

let private opFixture: OperationInputs =
    let op pkg name =
        { Package = pkg
          Name = name
          Clr = "Fuaran.Core." + name }

    { Census =
        [ op "Pkg.A" "Thing.proved"
          op "Pkg.A" "Thing.lawed"
          op "Pkg.A" "Conformance.fixtureLaws"
          op "Pkg.B" "Thing.tiny"
          op "Pkg.B" "Thing.alias"
          op "Pkg.B" "Thing.old"
          op "Pkg.B" "Thing.tested"
          op "Pkg.B" "Thing.reached" ]
      Ladder = [ "t1", "proved", [ "Thing.proved" ] ]
      Rosters = [ "Conformance.fixtureLaws", [ "Thing.lawed" ] ]
      Families = Set.ofList [ "Conformance.fixtureLaws" ]
      Exclusions =
        [ { Members = [ "Thing.tiny" ]
            Class = "trivial"
            Reason = "builds a record"
            ForwardsTo = None
            StandIn = None
            Through = None }
          { Members = [ "Thing.alias" ]
            Class = "forward"
            Reason = "the older name"
            ForwardsTo = Some "Thing.proved"
            StandIn = None
            Through = None }
          { Members = [ "Thing.old" ]
            Class = "obsolete"
            Reason = "renamed"
            ForwardsTo = Some "Thing.lawed"
            StandIn = None
            Through = None }
          { Members = [ "Thing.tested" ]
            Class = "measured-elsewhere"
            Reason = "example-tested"
            ForwardsTo = None
            StandIn = Some "tests/ThingTests.fs"
            Through = None }
          { Members = [ "Thing.reached" ]
            Class = "measured-elsewhere"
            Reason = "reached through tested"
            ForwardsTo = None
            StandIn = Some "tests/ThingTests.fs"
            Through = Some "Thing.via" } ]
      Classes = Set.ofList operationClassOrder
      StandIns = Map.ofList [ "tests/ThingTests.fs", "let t = Thing.tested 1 |> Thing.via" ]
      Obsolete = Set.ofList [ "Thing.old" ] }

/// An operation go-red: this perturbation yields EXACTLY one finding, naming the clause and subject.
let private expectOneOperationFinding (name: string) (inputs: OperationInputs) (clause: string) (subject: string) =
    let findings, _ = checkOperations inputs

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

/// A go-red whose subject a forward in the baseline leans on: the operation names itself under
/// `operation-mapped`, and every other finding is the forward that pointed at it, naming it.
let private expectNamedUnmapped (name: string) (inputs: OperationInputs) (subject: string) =
    let findings, _ = checkOperations inputs

    Expect.exists
        findings
        (fun f -> f.StartsWith(sprintf "'%s' [operation-mapped]" subject))
        (sprintf
            "%s: the unmapped operation names itself — got:
%s"
            name
            (String.concat
                "
"
                findings))

    for f in findings do
        Expect.stringContains f subject (sprintf "%s: every finding is about %s" name subject)

let private withExclusion
    (members: string list)
    (f: OperationExclusion -> OperationExclusion)
    (inputs: OperationInputs)
    =
    { inputs with
        Exclusions = inputs.Exclusions |> List.map (fun e -> if e.Members = members then f e else e) }


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

          testCase "the open-phase reader reads the list form and the declared-inert form"
          <| fun _ ->
              Expect.equal
                  (openPhasesFromJson """{ "open": ["13", "14"] }""")
                  (Ok(Some(Set.ofList [ "13"; "14" ])))
                  "the list form is the set of ids"

              Expect.equal
                  (openPhasesFromJson """{ "open": [] }""")
                  (Ok(Some Set.empty))
                  "an empty list is a list: nothing is open, so every scheduling claim fails as closed"

              Expect.equal
                  (openPhasesFromJson """{ "inert": "no roadmap side writes this list yet, so none is kept" }""")
                  (Ok None)
                  "the inert form is None, which the clause reads as no oracle"

          testCase "go-red: the open-phase reader refuses every malformed file rather than guessing"
          <| fun _ ->
              for bad in
                  [ "[]"
                    "{}"
                    """{ "open": ["1"], "inert": "both shapes at once is ambiguous" }"""
                    """{ "inert": true }"""
                    """{ "inert": "short" }"""
                    """{ "open": "13" }"""
                    """{ "open": [13] }"""
                    """{ "open": ["thirteen"] }"""
                    """{ "open": ["013"] }"""
                    "{ not json" ] do
                  match openPhasesFromJson bad with
                  | Error _ -> ()
                  | Ok v -> failtestf "a malformed file was accepted: %s -> %A" bad v

          testCase "the committed open-phases file is well-formed and needs no environment"
          <| fun _ ->
              let before = Environment.GetEnvironmentVariable "FUARAN_CORE_ROADMAP"

              try
                  Environment.SetEnvironmentVariable("FUARAN_CORE_ROADMAP", null)

                  match
                      openPhasesFromJson (
                          File.ReadAllText(Snapshots.repoFile "tests/Fuaran.Core.Tests/open-phases.json")
                      )
                  with
                  | Ok _ -> ()
                  | Error why -> failtestf "tests/Fuaran.Core.Tests/open-phases.json is malformed: %s" why
              finally
                  Environment.SetEnvironmentVariable("FUARAN_CORE_ROADMAP", before)

          // ---- Phase 335: the operation clause ----

          testCase "the synthetic operation baseline is mapped, by every route"
          <| fun _ ->
              let findings, tally = checkOperations opFixture

              Expect.isEmpty findings (sprintf "the baseline must be clean — got:\n%s" (String.concat "\n" findings))
              Expect.equal tally.Ladder 1 "one on the ladder"
              Expect.equal tally.Rostered 2 "one by a roster line, one as a family entry point"

              Expect.equal
                  tally.ByClass
                  [ "trivial", 1
                    "forward", 1
                    "obsolete", 1
                    "host-seam", 0
                    "measured-elsewhere", 2 ]
                  "and each exclusion counted under its class"

          testCase "every public operation is mapped on this tree, and the clause says so in one line"
          <| fun _ ->
              let inputs = liveOperationInputs ()
              let findings, tally = checkOperations inputs

              printfn "%s" (renderOperationPredicate (List.isEmpty findings) tally)

              Expect.isEmpty
                  findings
                  (sprintf
                      "every public operation maps to a ladder row, a law family or a recorded exclusion — each finding names the clause and the subject:\n%s"
                      (String.concat "\n" findings))

          testCase "the operation inputs are real, so the clause is not quantifying over nothing"
          <| fun _ ->
              let inputs = liveOperationInputs ()

              Expect.isGreaterThan (List.length inputs.Census) 900 "the census reads every baseline's method lines"
              Expect.isGreaterThan (List.length inputs.Ladder) 50 "ladder rows name the operations they cover"
              Expect.isGreaterThan (List.length inputs.Rosters) 40 "the kit's operation roster is read"
              Expect.isNonEmpty inputs.Exclusions "the operation exclusions are read"

              Expect.equal
                  inputs.Classes
                  (Set.ofList operationClassOrder)
                  "the closed class vocabulary is read from the file"

              Expect.equal
                  (inputs.Census |> List.map _.Name |> List.distinct |> List.length)
                  (List.length inputs.Census)
                  "no two published operations share a spelling, so a name maps one operation"

              Expect.isNonEmpty inputs.Obsolete "the obsolete entries' attribute is read by reflection"

          testCase "every operation class in the closed vocabulary is carried by at least one entry"
          <| fun _ ->
              let inputs = liveOperationInputs ()
              let used = inputs.Exclusions |> List.map _.Class |> Set.ofList

              Expect.equal
                  (Set.difference inputs.Classes used)
                  Set.empty
                  "a class no entry carries is a vocabulary term nothing tests — carry it or drop it"

          testCase
              "proofs/README.md's operation table is the census's projection (CORE_APPROVE_LADDER=1 regenerates it)"
          <| fun _ ->
              let _, tally = checkOperations (liveOperationInputs ())
              let path = Snapshots.repoFile "proofs/README.md"
              let committed = File.ReadAllText path

              match regenerateOperationTable (renderOperationTable tally) committed with
              | Error why -> failtest why
              | Ok expected when expected = committed.Replace("\r\n", "\n") -> ()
              | Ok expected when Environment.GetEnvironmentVariable "CORE_APPROVE_LADDER" = "1" ->
                  File.WriteAllText(path, expected)
              | Ok _ ->
                  failtest
                      "proofs/README.md's operation-coverage table is not the census's projection — re-run with CORE_APPROVE_LADDER=1 and commit the README"

          testCase "operation names drop the prefix, the arity and the Module suffix, and join nested types"
          <| fun _ ->
              Expect.equal
                  (operationName "Fuaran.Core.Dag+ReachModule.ancestors`1")
                  "Dag.Reach.ancestors"
                  "nested module"

              Expect.equal (operationName "Fuaran.Core.FootprintModule.union") "Footprint.union" "module suffix"
              Expect.equal (operationName "Fuaran.Core.Idl.Diff.run") "Idl.Diff.run" "namespace kept"

              Expect.equal
                  (operationName "Fuaran.Core.Observer.IObserver`2.Observe")
                  "Observer.IObserver.Observe"
                  "interface"

              Expect.equal
                  (censusOf
                      "P"
                      "# header\nmethod Fuaran.Core.Ops.apply`2(A, B) : C\nproperty Fuaran.Core.X.y : Z { get }\n")
                  [ { Package = "P"
                      Name = "Ops.apply"
                      Clr = "Fuaran.Core.Ops.apply`2" } ]
                  "only method lines are operations"

          testCase "go-red: operation-mapped — a newly published operation that maps to nothing names itself"
          <| fun _ ->
              expectOneOperationFinding
                  "a new census line"
                  { opFixture with
                      Census =
                          opFixture.Census
                          @ [ { Package = "Pkg.B"
                                Name = "Thing.brandNew"
                                Clr = "Fuaran.Core.Thing.brandNew" } ] }
                  "operation-mapped"
                  "Thing.brandNew"

          testCase "go-red: operation-mapped — a family's roster mention deleted, the operation names itself"
          <| fun _ ->
              // The probe Phase 335 was asked to prove: the operation names itself, and the
              // obsolete forward that leaned on it is refused naming it too.
              expectNamedUnmapped
                  "a roster line removed"
                  { opFixture with
                      Rosters = [ "Conformance.fixtureLaws", [] ] }
                  "Thing.lawed"

          testCase "go-red: operation-mapped — a ladder row's operations removed"
          <| fun _ ->
              expectNamedUnmapped
                  "a ladder mention removed"
                  { opFixture with
                      Ladder = [ "t1", "proved", [] ] }
                  "Thing.proved"

          testCase "go-red: operation-ladder — an assumed row naming operations"
          <| fun _ ->
              expectOneOperationFinding
                  "an assumed row"
                  { opFixture with
                      Ladder = opFixture.Ladder @ [ "t3", "assumed", [] ] }
                  "operation-ladder"
                  "t3"

          testCase "go-red: operation-ladder — a row naming an operation no baseline publishes"
          <| fun _ ->
              expectOneOperationFinding
                  "a stale ladder name"
                  { opFixture with
                      Ladder = [ "t1", "proved", [ "Thing.proved"; "Thing.gone" ] ] }
                  "operation-ladder"
                  "Thing.gone"

          testCase "go-red: operation-roster — a roster row for an undeclared family, and one naming nothing published"
          <| fun _ ->
              expectOneOperationFinding
                  "an undeclared family"
                  { opFixture with
                      Rosters = opFixture.Rosters @ [ "Conformance.ghostLaws", [] ] }
                  "operation-roster"
                  "Conformance.ghostLaws"

              expectOneOperationFinding
                  "a stale roster name"
                  { opFixture with
                      Rosters = [ "Conformance.fixtureLaws", [ "Thing.lawed"; "Thing.gone" ] ] }
                  "operation-roster"
                  "Thing.gone"

          testCase "go-red: operation-exclusion-stale — an entry for an operation that has since gained a mapping"
          <| fun _ ->
              expectOneOperationFinding
                  "an excluded operation now on the ladder"
                  { opFixture with
                      Ladder = [ "t1", "proved", [ "Thing.proved"; "Thing.tiny" ] ] }
                  "operation-exclusion-stale"
                  "Thing.tiny"

          testCase "go-red: operation-exclusion-stale — an entry for an operation no longer published"
          <| fun _ ->
              expectOneOperationFinding
                  "a retired operation still excluded"
                  { opFixture with
                      Census = opFixture.Census |> List.filter (fun o -> o.Name <> "Thing.tiny") }
                  "operation-exclusion-stale"
                  "Thing.tiny"

          testCase "go-red: operation-exclusion-class — a class outside the closed vocabulary"
          <| fun _ ->
              expectOneOperationFinding
                  "an invented class"
                  (withExclusion [ "Thing.tiny" ] (fun e -> { e with Class = "harmless" }) opFixture)
                  "operation-exclusion-class"
                  "harmless"

          testCase "go-red: operation-exclusion-reason — an entry with no reason"
          <| fun _ ->
              expectOneOperationFinding
                  "a blank reason"
                  (withExclusion [ "Thing.tiny" ] (fun e -> { e with Reason = " " }) opFixture)
                  "operation-exclusion-reason"
                  "Thing.tiny"

          testCase "go-red: operation-forward — a forward whose target maps to nothing, and one naming no target"
          <| fun _ ->
              let toBare =
                  { (withExclusion
                        [ "Thing.alias" ]
                        (fun e ->
                            { e with
                                ForwardsTo = Some "Thing.bare" })
                        opFixture) with
                      Census =
                          opFixture.Census
                          @ [ { Package = "Pkg.B"
                                Name = "Thing.bare"
                                Clr = "Fuaran.Core.Thing.bare" } ] }

              let findings, _ = checkOperations toBare

              Expect.exists
                  findings
                  (fun f -> f.Contains "[operation-forward]" && f.Contains "Thing.alias")
                  "a forward's coverage is its target's, so a forward to an unmapped operation is refused"

              let untargeted, _ =
                  checkOperations (withExclusion [ "Thing.alias" ] (fun e -> { e with ForwardsTo = None }) opFixture)

              Expect.exists
                  untargeted
                  (fun f -> f.Contains "[operation-forward]" && f.Contains "Thing.alias")
                  "a forward naming no target is refused"

          testCase "go-red: operation-forward — two forwards that name each other are a cycle, not coverage"
          <| fun _ ->
              let cyc =
                  opFixture
                  |> withExclusion [ "Thing.alias" ] (fun e -> { e with ForwardsTo = Some "Thing.old" })
                  |> withExclusion [ "Thing.old" ] (fun e ->
                      { e with
                          ForwardsTo = Some "Thing.alias" })

              let findings, _ = checkOperations cyc

              Expect.exists
                  findings
                  (fun f -> f.Contains "[operation-forward]" && f.Contains "Thing.alias")
                  "the cycle is named"

          testCase "go-red: operation-obsolete — an obsolete entry for an operation the attribute is not on"
          <| fun _ ->
              expectOneOperationFinding
                  "an unmarked obsolete claim"
                  { opFixture with Obsolete = Set.empty }
                  "operation-obsolete"
                  "Thing.old"

          testCase "go-red: operation-stand-in — a stand-in that does not exist, and one that never mentions the member"
          <| fun _ ->
              expectOneOperationFinding
                  "a missing stand-in"
                  (withExclusion
                      [ "Thing.tested" ]
                      (fun e ->
                          { e with
                              StandIn = Some "tests/Nowhere.fs" })
                      opFixture)
                  "operation-stand-in"
                  "tests/Nowhere.fs"

              expectOneOperationFinding
                  "a stand-in that never touches the member"
                  { opFixture with
                      StandIns = Map.ofList [ "tests/ThingTests.fs", "let t = Thing.testedLater 1 |> Thing.via" ] }
                  "operation-stand-in"
                  "Thing.tested"

              expectOneOperationFinding
                  "a measured-elsewhere entry with no stand-in"
                  (withExclusion [ "Thing.reached" ] (fun e -> { e with StandIn = None }) opFixture)
                  "operation-stand-in"
                  "Thing.reached" ]
