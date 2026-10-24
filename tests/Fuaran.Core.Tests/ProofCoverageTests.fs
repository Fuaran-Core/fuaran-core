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
//      law it names; a `model-bridge` names `closes` — `permanent` or `unscheduled`. No
//      schedule is kept here (Phase 537), so a phase in `closes` fails.
//
//   3. EVERY DIFFERENTIAL IS PAIRED TO A THEOREM. A `tested` row on the `Proofs.Oracle` family
//      names the model it runs beside (`evidence.model`, Phase 203), and that model must carry at
//      least one `proved` row. A differential with no theorem beside it is a test pretending to be
//      a rung. A `tested` row on a `Conformance.<law>` family is a LAW row, not a differential, and
//      carries no model — the asymmetry is checked in both directions so neither kind can drift
//      into the other. A `tested` row on a `Suite:<Module>.<list>` family (Phase 522: the family is
//      closed by `Proofs.Ladder`'s tested-family-closed, so no prose family reaches this clause) is a
//      suite row: it MAY name the model it is about, and a model it names is held exactly as a
//      differential's is.
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
//  * There is no scheduled half of clause 2 (Phase 537). This repository's roadmap lives outside
//    it and cannot be read from here, so a `closes` naming a phase would be a claim nothing in
//    this tree can check. It was once held to a committed list of open phases that no writer ever
//    filled; that list and its reader are deleted until a writer exists, and a phase-form `closes`
//    is refused as outside the closed set rather than passed or waved through.
//  * The predicate LINE is emitted whatever the verdict, and says which verdict it is. A line that
//    only appeared when green would make its absence the signal, and an absence is the one thing a
//    reader scanning a log does not see.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core.Tests.ProofLadder
open Fuaran.Core.Tests.ProofCoverage

let private repoRoot = Snapshots.repoFile "."

let private readJson (relPath: string) =
    JsonDocument.Parse(File.ReadAllText(Snapshots.repoFile relPath))

let private liveModels () =
    use doc = readJson "proofs/modules.json"

    // A `shared` entry (Phase 504: `Twin`, `ListAlgebra`) is a module every model opens, not a model of
    // anything, so it attributes no package and is not a subject of the coverage clauses.
    doc.RootElement.GetProperty("modules").EnumerateArray()
    |> Seq.filter (fun el -> strMember el "kind" <> Some "shared")
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

let private liveInputs () =
    use exclusionsDoc = liveExclusionsDoc ()

    { Packable = Roster.derive repoRoot |> List.map _.PackageId
      Models = liveModels ()
      Exclusions = liveExclusions exclusionsDoc.RootElement
      Reasons = liveReasons exclusionsDoc.RootElement
      Laws = liveLaws ()
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
    { "id": "t5", "level": "assumed", "class": "model-bridge", "closes": "permanent", "phase": "fuaran-core#1" },
    { "id": "t6", "level": "assumed", "class": "host-premise", "probe": "premiseFixture", "phase": "fuaran-core#1" }
  ]
}
"""

let private fixtureContract =
    """
Every ladder ends at level 3, and the 4 assumed rows across these ladders are four kinds of thing.

- **`domain-obligation`** — what you owe. 1 rows.
- **`model-bridge`** — what this repository has not bridged. 1 rows.
- **`premise`** — what nobody discharges. 1 rows.
- **`host-premise`** — what each host answers on its own runtime. 1 rows.
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
//    (b) a law family whose record lists it (`LawFamily.Operations`) — and a family's own
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
              Expect.equal tally.Assumed 4 "four assumed rows"
              Expect.equal tally.HostProbed 1 "one host premise, probed"

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

              // `model-scheduled` is carried exactly while a package has shipped ahead of its model
              // — Phase 426 filed the first such entry, for `Fuaran.Core.Unit`, and Phase 428's model
              // retired it — and the term stays in the vocabulary for the next such interval
              // (operator decision 2026-10-10), as `obsolete` stays in the operation classes below.
              let exempt = Set.singleton "model-scheduled"

              Expect.equal
                  (Set.difference (Set.difference inputs.Reasons used) exempt)
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

          testCase "go-red: assumed-accounted — a host premise with no probe"
          <| fun _ ->
              expectOneFinding
                  "an unprobed host premise"
                  fixtureBase
                  (fixtureLadder.Replace("\"probe\": \"premiseFixture\", ", ""))
                  "assumed-accounted"
                  "t6"

          testCase "go-red: assumed-accounted — a model bridge with no closes"
          <| fun _ ->
              expectOneFinding
                  "an unaccounted bridge"
                  fixtureBase
                  (fixtureLadder.Replace("\"closes\": \"permanent\", ", ""))
                  "assumed-accounted"
                  "t5"

          testCase "go-red: assumed-accounted — a model bridge scheduled against a phase"
          <| fun _ ->
              expectOneFinding
                  "a scheduling claim nothing here can check"
                  fixtureBase
                  (fixtureLadder.Replace("\"closes\": \"permanent\"", "\"closes\": \"fuaran-core#999\""))
                  "assumed-accounted"
                  "t5"

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

          testCase "go-red: differential-paired — a suite row about a model that carries no theorem"
          <| fun _ ->
              expectOneFinding
                  "a suite row with no theorem beside its model"
                  { fixtureBase with
                      Models =
                          [ { Module = "MA"
                              Packages = [ "Pkg.A" ] }
                            { Module = "MB"
                              Packages = [ "Pkg.A" ] } ] }
                  (fixtureLadder
                      .Replace("\"family\": \"Proofs.Oracle\"", "\"family\": \"Suite:FixtureTests.tests\"")
                      .Replace("\"model\": \"proofs/MA.fst\", \"cases\"", "\"model\": \"proofs/MB.fst\", \"cases\""))
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
                      ContractText = fixtureContract.Replace("the 4 assumed rows", "the 5 assumed rows") }
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
                      ContractText = fixtureContract.Replace("the 4 assumed rows", "the assumed rows") }
                  fixtureLadder
                  "contract-counts"
                  "assumed rows"

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

              // Phase 386: the obsolete class has no subject today — no shipped member carries
              // `System.Obsolete` at 0.37.0, and the `OneDotZero` family asserts that from major 1 —
              // so the attribute reader is probed by the fixture tests below (`Thing.old`), not by the tree.
              Expect.isEmpty
                  (Set.difference inputs.Obsolete (inputs.Census |> List.map _.Name |> Set.ofList))
                  "every operation the obsolete reading names is a published one"

          testCase "every operation class in the closed vocabulary is carried by at least one entry"
          <| fun _ ->
              let inputs = liveOperationInputs ()
              let used = inputs.Exclusions |> List.map _.Class |> Set.ofList

              // `obsolete` is carried exactly when a published operation carries `System.Obsolete`:
              // none does at a major boundary (Phase 386, the `OneDotZero` family), and the class
              // stays in the vocabulary for the forward a later minor deprecates. An obsolete LAW
              // FAMILY forward (Phase 507: `codecLaws`, `propagationEvaluatorLawsWith`) is mapped by
              // its own roster row, which an exclusion may not double, so it does not demand the class.
              let exempt =
                  if
                      inputs.Census
                      |> List.exists (fun o -> carriesObsolete o && not (inputs.Families.Contains o.Name))
                  then
                      Set.empty
                  else
                      Set.singleton "obsolete"

              Expect.equal
                  (Set.difference (Set.difference inputs.Classes used) exempt)
                  Set.empty
                  "a class no entry carries is a vocabulary term nothing tests — carry it or drop it"

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

// ---------------------------------------------------------------------------
// Phase 480 — the host evidence map. For every ladder row, the vector families whose cases its model
// answered and the oracle functions that ask its model, derived (`EvidenceMap.derive`) and written by
// `render evidence-map`. The committed files are held to the render; the derivation is a function of
// its inputs, so every go-red below runs it over the live inputs changed in exactly one way.
// ---------------------------------------------------------------------------

module private EvidenceMapCase =
    open Fuaran.Core

    let live () = EvidenceMap.liveInputs (Repo.root ())

    let derived (inputs: EvidenceMap.Inputs) =
        match EvidenceMap.derive inputs with
        | Ok m -> m
        | Error findings -> failtestf "the map does not derive:\n%s" (String.concat "\n" findings)

    let findings (inputs: EvidenceMap.Inputs) =
        match EvidenceMap.derive inputs with
        | Ok _ -> []
        | Error findings -> findings

    /// `inputs` with the vector file at `path` rewritten by `edit` over its `vectors` array.
    let withVectors (path: string) (edit: JVal list -> JVal list) (inputs: EvidenceMap.Inputs) =
        let rewrite (text: string) =
            match Json.parse text with
            | Ok(JObj fields) ->
                fields
                |> List.map (fun (k, v) ->
                    match k, v with
                    | "vectors", JArr vs -> k, JArr(edit vs)
                    | _ -> k, v)
                |> JObj
                |> Json.render
            | _ -> failtestf "%s does not read as a vector file" path

        Expect.isTrue (inputs.VectorFiles |> List.exists (fun (p, _) -> p = path)) (sprintf "%s is an input" path)

        { inputs with
            VectorFiles =
                inputs.VectorFiles
                |> List.map (fun (p, t) -> if p = path then p, rewrite t else p, t) }

    let stampedBy (model: string) (v: JVal) =
        match v with
        | JObj fs ->
            fs
            |> List.exists (fun (k, s) -> k = "answeredBy" && s = JArr [ JStr "reference"; JStr("model:" + model) ])
        | _ -> false

    let row (m: EvidenceMap.EvidenceMap) (id: string) =
        match m.Rows |> List.tryFind (fun r -> r.Id = id) with
        | Some r -> r
        | None -> failtestf "no row `%s` in the map" id

    let expectFinding (what: string) (inputs: EvidenceMap.Inputs) (needle: string) =
        let fs = findings inputs

        Expect.isTrue
            (fs |> List.exists (fun f -> f.Contains needle))
            (sprintf "%s: a finding naming `%s`, got:\n%s" what needle (String.concat "\n" fs))

    /// The file the go-reds edit and the model it answers: the one family a DecimalText row is offered.
    let decimalFile = "conformance/laws/decimal-laws.json"

open Fuaran.Core

[<Tests>]
let evidenceMapTests =
    testList
        "Proofs.Coverage.EvidenceMap"
        [ testCase "the committed map and its rendering are the render"
          <| fun _ ->
              match EvidenceMap.render (EvidenceMapCase.live ()) with
              | Error findings -> failtestf "the map does not derive:\n%s" (String.concat "\n" findings)
              | Ok files ->
                  for path, expected in files do
                      let committed = File.ReadAllText(Snapshots.repoFile path).Replace("\r\n", "\n")

                      if committed <> expected then
                          failtestf
                              "%s is not the render. Re-render with `%s` and commit both files."
                              path
                              (Tool.render EvidenceMap.Family)

          testCase "every ladder row is in the map once, in ladder order, and every uncovered one is named"
          <| fun _ ->
              let inputs = EvidenceMapCase.live ()
              let m = EvidenceMapCase.derived inputs

              let ladderIds =
                  use doc = JsonDocument.Parse inputs.Ladder

                  [ for r in doc.RootElement.GetProperty("claims").EnumerateArray() -> r.GetProperty("id").GetString() ]

              Expect.equal (m.Rows |> List.map _.Id) ladderIds "the map's rows are the ladder's, in its order"
              let markdown = EvidenceMap.renderMarkdown m

              let unnamed =
                  m.Rows
                  |> List.filter (fun r -> not r.Offered && not (markdown.Contains("`" + r.Id + "`")))
                  |> List.map _.Id

              Expect.isEmpty unnamed "every uncovered row is named on the rendered page"

          testCase "the live map offers something by both routes (or every check here is vacuous)"
          <| fun _ ->
              let m = EvidenceMapCase.derived (EvidenceMapCase.live ())

              Expect.isTrue
                  (m.Rows
                   |> List.exists (fun r -> r.Level = "proved" && not (List.isEmpty r.Families)))
                  "a proved row is offered a vector family"

              Expect.isTrue
                  (m.Rows
                   |> List.exists (fun r -> r.Level = "proved" && not (List.isEmpty r.Oracle)))
                  "a proved row is offered an oracle function"

              Expect.isNonEmpty
                  (EvidenceMap.uncovered "proved" m)
                  "the worklist is the measured one, not empty by accident"

          testCase
              "go-red: a family's case deleted for one row's model moves its count, and the last one leaves the row uncovered"
          <| fun _ ->
              let inputs = EvidenceMapCase.live ()
              let before = EvidenceMapCase.derived inputs

              let target =
                  before.Rows
                  |> List.find (fun r -> r.Model = Some "DecimalText" && r.Level = "proved")

              let decimalCases (r: EvidenceMap.RowOffer) =
                  r.Families
                  |> List.tryFind (fun f -> f.File = EvidenceMapCase.decimalFile)
                  |> Option.map _.Cases

              let n =
                  match decimalCases target with
                  | Some n -> n
                  | None ->
                      failtestf "`%s` is offered no case of %s on the live tree" target.Id EvidenceMapCase.decimalFile

              Expect.isEmpty target.Oracle "the measurement needs a row whose one offer is the family"

              // One stamped case deleted: still offered, one case fewer.
              let dropOne =
                  EvidenceMapCase.withVectors
                      EvidenceMapCase.decimalFile
                      (fun vs ->
                          let i = vs |> List.findIndex (EvidenceMapCase.stampedBy "DecimalText")
                          vs |> List.removeAt i)
                      inputs

              let one = EvidenceMapCase.row (EvidenceMapCase.derived dropOne) target.Id
              Expect.equal (decimalCases one) (Some(n - 1)) "one case fewer"
              Expect.isTrue one.Offered "still offered"

              // Every stamped case deleted: the row drops to uncovered and joins the worklist.
              let dropAll =
                  EvidenceMapCase.withVectors
                      EvidenceMapCase.decimalFile
                      (List.filter (EvidenceMapCase.stampedBy "DecimalText" >> not))
                      inputs

              let after = EvidenceMapCase.derived dropAll
              Expect.isFalse (EvidenceMapCase.row after target.Id).Offered "the row is uncovered"

              Expect.isTrue
                  (EvidenceMap.uncovered "proved" after
                   |> List.exists (fun (model, ids) -> model = "DecimalText" && List.contains target.Id ids))
                  "the row is on the worklist under its model"

              Expect.isFalse
                  (EvidenceMap.uncovered "proved" before
                   |> List.exists (fun (_, ids) -> List.contains target.Id ids))
                  "and was not before (the probe moved the verdict)"

          testCase "go-red: an oracle function removed leaves a row whose one offer it was uncovered"
          <| fun _ ->
              let inputs = EvidenceMapCase.live ()
              let before = EvidenceMapCase.derived inputs

              let target =
                  before.Rows
                  // Phase 563: the chain rows now carry vector families too, so the row is a DAG-fold one.
                  |> List.find (fun r -> r.Level = "proved" && List.isEmpty r.Families && r.Oracle = [ "dag.fold" ])

              let after =
                  EvidenceMapCase.derived
                      { inputs with
                          Oracle = inputs.Oracle |> List.filter (fun (name, _) -> name <> "dag.fold") }

              Expect.isFalse (EvidenceMapCase.row after target.Id).Offered "the row is uncovered without the function"

          testCase "go-red: a stamp naming a model no ladder row states is refused"
          <| fun _ ->
              let inputs =
                  EvidenceMapCase.live ()
                  |> EvidenceMapCase.withVectors EvidenceMapCase.decimalFile (fun vs ->
                      match vs with
                      | JObj fs :: rest ->
                          JObj(
                              fs
                              |> List.map (fun (k, v) ->
                                  if k = "answeredBy" then
                                      k, JArr [ JStr "reference"; JStr "model:Nowhere" ]
                                  else
                                      k, v)
                          )
                          :: rest
                      | _ -> failtest "the decimal file has an object vector")

              EvidenceMapCase.expectFinding "a stale stamp" inputs "model:Nowhere"

          testCase "go-red: an oracle function asking a model no ladder row states is refused"
          <| fun _ ->
              let inputs = EvidenceMapCase.live ()

              EvidenceMapCase.expectFinding
                  "a stale oracle model"
                  { inputs with
                      Oracle = inputs.Oracle @ [ "nowhere.ask", [ "Nowhere" ] ] }
                  "nowhere.ask"

          testCase "go-red: an unstamped vector, and two files claiming one family, are refused"
          <| fun _ ->
              let inputs = EvidenceMapCase.live ()

              let unstamped =
                  inputs
                  |> EvidenceMapCase.withVectors EvidenceMapCase.decimalFile (fun vs ->
                      match vs with
                      | JObj fs :: rest -> JObj(fs |> List.filter (fun (k, _) -> k <> "answeredBy")) :: rest
                      | _ -> failtest "the decimal file has an object vector")

              EvidenceMapCase.expectFinding "an unstamped vector" unstamped "carries no `answeredBy`"

              let copy =
                  inputs.VectorFiles
                  |> List.find (fun (p, _) -> p = EvidenceMapCase.decimalFile)
                  |> snd

              EvidenceMapCase.expectFinding
                  "a family claimed twice"
                  { inputs with
                      VectorFiles = inputs.VectorFiles @ [ "conformance/laws/decimal-laws-copy.json", copy ] }
                  "is claimed by" ]
