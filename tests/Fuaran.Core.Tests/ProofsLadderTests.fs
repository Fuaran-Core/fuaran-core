module Fuaran.Core.Tests.ProofsLadderTests

// Phase 144 — `/proofs.json` is CHECKED, not trusted.
//
// `proofs.json` declares `proofs/README.md`'s claims ladder as data, so a tool need not read prose
// to learn what is PROVED, what is only TESTED and what is ASSUMED. It is hand-authored, and until
// this family nothing read it at all: a `proved` row could name a theorem no model declares, a
// model could be added to `proofs/check.ps1`'s `$modules` with no row claiming anything about it,
// and a `tested` row could name a differential family that had since been renamed. Each of the
// three proof phases before this one hand-corrected ladder drift it found on arrival. The repo's
// own rule (D30) is that a committed declared-or-generated artefact is pinned by a check; this
// family is that check.
//
// FOURTEEN CLAUSES, each with a go-red beside it — six from Phase 144, four from Phase 174, four from
// Phase 522:
//
//   theorem-exists          a `proved` row's `evidence.theorem` is a TOP-LEVEL `val` / `let` /
//                           `let rec` of that name in the model it names.
//   model-registered        that model file exists, and its module is one `check.ps1` checks — a
//                           row over a model the leg never runs claims a proof nothing reproduces.
//   tested-case-exists      a `tested` row names at least one case, and every case it names is in
//                           the suite its family names (`suiteCases`).
//   every-module-has-a-row  every model in the roster (`proofs/modules.json`) is named by at least one `proved` row.
//   phase-form              every row's `phase` is the `fuaran-core#NNN` form.
//   closed-level-set        every row's `level` is one of the closed four.
//
//   assumed-class           (174) every `assumed` row carries a `class` from the closed three, and
//                           no other row carries one — `level` says how strong a claim is, `class`
//                           says which KIND of assumed an assumed row is.
//   obligation-law          (174) a `domain-obligation` names `dischargedBy: Conformance.<law>`,
//                           and `<law>` is one the shipped kit actually exports — an obligation
//                           citing a law nobody can run is one a domain cannot discharge.
//   bridge-closes           (174) a `model-bridge` names `closes`: a `fuaran-core#NNN` phase,
//                           `permanent`, or `unscheduled`. A `premise` names neither field.
//   contract-agrees         (174) `proofs/README.md`'s "Core-to-domain proof contract" table is
//                           the ladder's assumed rows, in order, with the same class and the same
//                           third column.
//
//   statement-boundary      (522) every row states what it claims in `statement` and what it does
//                           NOT cover in `boundary`, both non-empty, and neither tells the row's
//                           history: no `Phase NNN` in either — the row's `phase` is its one citation.
//   claim-is-the-join       (522) `claim` is `statement` and `boundary` joined by one space: the
//                           field a registry reading the ladder still reads, held to the two it is made of.
//   tested-family-closed    (522) a `tested` row's family is one of three closed forms —
//                           `Proofs.Oracle`, `Conformance.<law>` (a law the kit exports) or
//                           `Suite:<Module>.<list>` (a test list this assembly declares) — and it
//                           names a suite the test tree holds.
//   evidence-exists         (522) every path LADDER.md says must exist does (a policy row's
//                           `document`, a tested row's `artifact` and `host`), every `supporting`
//                           declaration is top-level in its model like `theorem` (reported under
//                           theorem-exists), and no row carries `repo`: this family holds rows to
//                           THIS tree, so a row resting on another repository's cannot be held here.
//
// WHAT IT DELIBERATELY IS NOT: a README parser — with ONE exception, taken at `contract-agrees`
// and nowhere else, for the reason stated at that clause: the contract table is the same question
// the ladder answers, asked in the document a DOMAIN reads, and two answers that can silently
// disagree are worse than either alone. Everything else about the prose — whether a row's
// surrounding paragraph says what its `claim` says — stays a human act; what is pinned here is
// ROW-TO-TREE agreement, which is the half that can be mechanical.
//
// The check is parametrised over its four inputs — the root a model path resolves against, the
// module list, the case names and the kit's law names — so the real file is ONE input and each
// fixture is another.
// That is not decoration: a fixture ladder measured against the REAL module list would go red the
// moment a sibling appended a model to the roster, and one measured against the REAL case names
// would go red on a renamed case. A go-red family has to stay green while the subject it is about
// changes, or it is not a go-red family for long. For the same reason the case names are read from
// the TEST TREE (`oracleTests`: every `Proofs.Oracle` list the assembly declares) rather than by
// parsing their source, and the module list is read from `proofs/modules.json`, the one roster
// (Phase 504), rather than restated here — a second copy of that list is precisely the drift this
// family exists to catch.

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core.Tests.ProofLadder
open Fuaran.Core.Tests.ProofCone

/// Every `[<Tests>]` value in this assembly whose label is `label`, found by reflection (Phase 504;
/// any label since Phase 522) — so a new differential file, or a new conformance suite, is citable by
/// a `tested` row the moment it declares its list, with no enumeration here to forget it in.
let testsLabelled (label: string) : Test list =
    [ for t in Reflection.Assembly.GetExecutingAssembly().GetTypes() do
          for p in
              t.GetProperties(
                  Reflection.BindingFlags.Static
                  ||| Reflection.BindingFlags.Public
                  ||| Reflection.BindingFlags.NonPublic
              ) do
              if p.PropertyType = typeof<Test> && p.IsDefined(typeof<TestsAttribute>, false) then
                  match p.GetValue null with
                  | :? Test as test ->
                      match test with
                      | TestLabel(l, _, _) when l = label -> yield p.DeclaringType.FullName + "." + p.Name, test
                      | _ -> ()
                  | _ -> () ]
    |> List.sortBy fst
    |> List.map snd

/// The `Proofs.Oracle` family's test tree: every differential list the assembly declares.
let oracleTests () : Test =
    testList "Proofs.Oracle" (testsLabelled "Proofs.Oracle")

/// The leaf case names of an Expecto test tree — the labels a `tested` row may cite.
let rec caseNames (t: Test) : string list =
    match t with
    | TestLabel(label, TestCase _, _) -> [ label ]
    | TestLabel(_, inner, _) -> caseNames inner
    | TestList(ts, _) -> ts |> Seq.collect caseNames |> List.ofSeq
    | TestCase _ -> []
    | Test.Sequenced(_, inner) -> caseNames inner

// ---------------------------------------------------------------------------
//  The family
// ---------------------------------------------------------------------------

let private repoRoot = Repo.root ()
let private ladderPath = Path.Combine(repoRoot, "proofs.json")
let private checkScriptPath = Path.Combine(repoRoot, "proofs", "check.ps1")
let private proofsReadmePath = Path.Combine(repoRoot, "proofs", "README.md")

let private fixtureDir =
    Path.Combine(repoRoot, "tests", "Fuaran.Core.Tests", "fixtures", "proofs-ladder")

let private modulesJsonPath = Path.Combine(repoRoot, "proofs", "modules.json")

/// The real roster; an unreadable one fails the case that asked, naming every bad entry.
let private realRoster () : RosterEntry list =
    match parseRoster (File.ReadAllText modulesJsonPath) with
    | Ok entries -> entries
    | Error errors -> failtestf "%s is not a readable roster: %s" modulesJsonPath (String.concat "; " errors)

/// Every module the leg checks, in check order.
let private realModules () = realRoster () |> List.map _.Module

/// The MODELS — what the ladder's `every-module-has-a-row` is about (the shared modules carry none).
let private realModels () =
    realRoster () |> List.filter isModel |> List.map _.Module

/// The cases of the suite a `tested` row's family names (Phase 522), read from the TREE so a sibling
/// appending a case — or a suite — is picked up with no edit here. Each of the three closed forms
/// resolves by reflection: `Proofs.Oracle` and `Conformance.<law>` to every `[<Tests>]` list carrying
/// that label (a conformance suite is labelled with its law family's name), `Suite:<Module>.<list>`
/// to the `Test` value of that name in that module. `None` is a family that names no suite.
///
/// It replaces an enumeration of eight suites, one line per phase that cited a new one, under which
/// a row's cases were looked for across ALL of them whatever its family said — so a prose family
/// passed and a case was found in a suite its row did not name.
let suiteCases (family: string) : Set<string> option =
    let ofTests (tests: Test list) =
        match tests |> List.collect caseNames with
        | [] -> None
        | cases -> Some(Set.ofList cases)

    let suite = suiteForm.Match family

    if family = "Proofs.Oracle" || family.StartsWith "Conformance." then
        ofTests (testsLabelled family)
    elif suite.Success then
        match Reflection.Assembly.GetExecutingAssembly().GetType("Fuaran.Core.Tests." + suite.Groups[1].Value) with
        | null -> None
        | t ->
            match
                t.GetProperty(
                    suite.Groups[2].Value,
                    Reflection.BindingFlags.Static
                    ||| Reflection.BindingFlags.Public
                    ||| Reflection.BindingFlags.NonPublic
                )
            with
            | null -> None
            | p when p.PropertyType = typeof<Test> -> ofTests [ p.GetValue null :?> Test ]
            | _ -> None
    else
        None

/// The law names a `domain-obligation` row may cite — the entry points of the shipped kit's
/// declared law-family ROSTER (`Fuaran.Core.Families`, Phase 184), never restated here.
///
/// It used to be every public static method of `Fuaran.Core.Conformance`, read from the assembly.
/// That answered the renaming half correctly and the ROSTERING half not at all: the module's
/// public surface is far wider than its law set — aggregates, generators, helpers — so a row could
/// cite something no domain can run as a law and pass, and, worse, a row could cite a genuine law
/// that no conformance census enumerates. `Conformance.opAlgebra` was exactly that: the law
/// `tree-algebra-well-formed-states` names, shipped, citable here, and absent from the roster every
/// consumer's census quantifies over, so no consumer could ever mark it adopted. Reading the roster
/// makes the obligation's own clause the guarantee — an obligation cannot name an unrosterable law,
/// because the vocabulary IS the roster. The renaming half is unchanged: the roster is held to
/// reflection over the shipped assembly by `ConformanceFamiliesTests`, so a renamed law still stops
/// answering for a row with no edit in this file.
let private realLaws () =
    KitRoster.families
    |> List.filter (fun f -> f.Module = "Conformance")
    |> List.map _.Entry
    |> Set.ofList

/// A fixture ladder is measured against a FIXTURE module list, a FIXTURE case set and a FIXTURE law
/// set, so nothing a sibling adds to the roster, to `Proofs.Oracle` or to the conformance kit can
/// move a go-red in either direction.
let private fixtureInputs (modules: string list) =
    { Root = fixtureDir
      Modules = modules
      Suites =
        fun family ->
            if family = "Proofs.Oracle" then
                Some(Set.ofList [ "a fixture case" ])
            else
                None
      Laws = Set.ofList [ "fixtureLaws" ] }

let private fixture (name: string) =
    File.ReadAllText(Path.Combine(fixtureDir, name))

/// A go-red: this ladder text yields EXACTLY one finding, and it names the clause and the subject.
let private expectOneFindingIn (modules: string list) (name: string) (text: string) (clause: string) (subject: string) =
    let findings = checkLadder (fixtureInputs modules) text

    Expect.equal
        (List.length findings)
        1
        (sprintf
            "%s carries exactly one defect, so the check must yield exactly one finding — got:\n%s"
            name
            (String.concat "\n" findings))

    let only = List.head findings
    Expect.stringContains only (sprintf "[%s]" clause) (sprintf "%s: the finding names the clause" name)
    Expect.stringContains only subject (sprintf "%s: the finding names what it is about" name)

/// A go-red over a committed fixture file.
let private expectOneFinding (modules: string list) (name: string) (clause: string) (subject: string) =
    expectOneFindingIn modules name (fixture name) clause subject

/// `ladder-ok.json` with ONE row mutated in place (Phase 522): the go-reds of the four clauses Phase
/// 522 added are each one mutation of the baseline, so a go-red that fires is known to be firing on
/// its mutation and on nothing a separate fixture file could drift into.
let private mutated (rowId: string) (mutate: Nodes.JsonObject -> unit) : string =
    let root = Nodes.JsonNode.Parse(fixture "ladder-ok.json")

    let row =
        root["claims"].AsArray()
        |> Seq.map (fun r -> r.AsObject())
        |> Seq.find (fun r -> r["id"].GetValue<string>() = rowId)

    mutate row
    root.ToJsonString()

/// Set a row's statement or boundary and keep `claim` its join, so the mutation is ONE defect.
let private withProse (field: string) (value: string) (row: Nodes.JsonObject) =
    row[field] <- Nodes.JsonValue.Create value

    row["claim"] <-
        Nodes.JsonValue.Create(row["statement"].GetValue<string>() + " " + row["boundary"].GetValue<string>())

let private expectMutationFinding (description: string) (rowId: string) mutate (clause: string) (subject: string) =
    expectOneFindingIn [ "Model" ] description (mutated rowId mutate) clause subject

[<Tests>]
let proofsLadderTests =
    testList
        "Proofs.Ladder"
        [

          // ---- the inputs are real, and non-vacuous ----

          testCase "modules.json is the roster: every entry has a kind and an opens list, and a model file"
          <| fun _ ->
              // `realRoster` fails on an entry with no kind or no opens; this asserts what it read is
              // not vacuous, because a roster read as EMPTY makes every-module-has-a-row silently
              // vacuous, which is worse than a red.
              let roster = realRoster ()

              Expect.isNonEmpty roster (sprintf "%s names no module" modulesJsonPath)
              Expect.isNonEmpty (roster |> List.filter isModel) "the roster names models"

              Expect.equal
                  (roster |> List.map _.Module |> List.distinct |> List.length)
                  roster.Length
                  "no module is registered twice"

              for e in roster do
                  let model = Path.Combine(repoRoot, "proofs", e.Module + ".fst")
                  Expect.isTrue (File.Exists model) (sprintf "the leg checks '%s' but %s is not there" e.Module model)

          testCase "check.ps1 enumerates no module: the roster is modules.json's (Phase 504)"
          <| fun _ ->
              // The script used to carry the roster as two one-line literals this family parsed out
              // of its text with a regex. It reads modules.json now, and a literal that came back
              // would be a second roster nothing holds to the first.
              let script = File.ReadAllText checkScriptPath

              for name in [ "modules"; "proofOnly" ] do
                  Expect.isFalse
                      (Regex.IsMatch(script, @"^\$" + name + @"\s*=\s*@\(\s*'", RegexOptions.Multiline))
                      (sprintf "check.ps1 declares a `$%s` list literal — the roster is proofs/modules.json alone" name)

              Expect.stringContains script "modules.json" "and it reads the roster file"

          testCase "the twin roster runs exactly the modules the leg extracts (Phase 309)"
          <| fun _ ->
              // The host half of twin evaluation's coverage. The kit's TWIN step holds each extracted
              // module's SOURCE to declaring `twins`; this holds the HOST to running them — a model
              // whose fixtures the normaliser certified and nothing ran would read as covered. Both
              // directions: an extracted module the oracle assembly carries no twins for, and twins
              // in the oracle for a module the leg no longer extracts.
              let extracted =
                  realRoster () |> List.filter isExtracted |> List.map _.Module |> Set.ofList

              let rostered = ProofOracle.TwinTests.twinRoster |> List.map fst |> Set.ofList

              Expect.isNonEmpty extracted "the roster's extracted set is read"

              Expect.equal
                  rostered
                  extracted
                  (sprintf
                      "the twin roster and the extracted set differ — unrun: %A; stale: %A"
                      (Set.difference extracted rostered)
                      (Set.difference rostered extracted))

          testCase "the oracle project compiles exactly the extracted modules, each after what it opens"
          <| fun _ ->
              // Generated since Phase 532 (`render oracle-project`), and held here to that render: the
              // project's `Compile` items, less the hand-written runtime floor, are the roster's extracted
              // set in check order, and each comes after every extracted module its `opens` names.
              let projectPath =
                  Path.Combine(repoRoot, "proofs", "oracle", "Fuaran.Core.Proofs.Oracle.fsproj")

              let committed = File.ReadAllText(projectPath).Replace("\r\n", "\n")

              match OracleProject.render (File.ReadAllText modulesJsonPath) committed with
              | Error e -> failtest e
              | Ok rendered ->
                  Expect.equal
                      committed
                      rendered
                      (sprintf
                          "%s is not what `%s` writes from the roster; run it and commit the result"
                          OracleProject.Path
                          (Tool.render "oracle-project"))

              let floor = set [ "Prims"; "FStar_Pervasives_Native" ]

              let compiled =
                  Regex.Matches(committed, @"<Compile\s+Include=""([^""]+)\.fs""")
                  |> Seq.map (fun m -> m.Groups[1].Value)
                  |> Seq.filter (fun m -> not (floor.Contains m))
                  |> List.ofSeq

              let roster = realRoster ()
              let extracted = roster |> List.filter isExtracted

              Expect.equal
                  (Set.ofList compiled)
                  (extracted |> List.map _.Module |> Set.ofList)
                  "the oracle project compiles the roster's extracted modules and nothing else"

              let position m = List.findIndex ((=) m) compiled

              for e in extracted do
                  for o in e.Opens do
                      if List.contains o compiled then
                          Expect.isLessThan
                              (position o)
                              (position e.Module)
                              (sprintf "%s opens %s, so the oracle project must compile %s first" e.Module o o)

          testCase "the case names a tested row may cite are readable from the test tree"
          <| fun _ ->
              // Read from the TREE, not from the source text, so a sibling appending a case is
              // picked up with no edit here. If this ever collapsed to a handful, the
              // tested-case-exists clause would have quietly stopped measuring anything.
              Expect.isGreaterThan
                  (suiteCases "Proofs.Oracle" |> Option.map Set.count |> Option.defaultValue 0)
                  20
                  "the Proofs.Oracle differential host's case names are readable"

              // and EACH closed form resolves — a form whose reflection silently degenerated would
              // leave its rows reported as naming no suite, or worse, never asked.
              for family in
                  [ "Conformance.containerLaws"
                    "Conformance.checkpointLaws"
                    "Suite:ColumnTests.fieldTests" ] do
                  Expect.isNonEmpty
                      (suiteCases family |> Option.defaultValue Set.empty)
                      (sprintf "%s resolves to a suite with cases" family)

              // and a family that names nothing resolves to nothing, in every form.
              for family in
                  [ "Conformance.noSuchLaws"
                    "Suite:ColumnTests.noSuchTests"
                    "Suite:NoSuchModule.tests"
                    "Column fields: unit, label, description and extensions" ] do
                  Expect.isNone (suiteCases family) (sprintf "%s resolves to no suite" family)

          testCase "the law names a domain-obligation row may cite are the kit's declared roster"
          <| fun _ ->
              let laws = realLaws ()

              // A roster read that collapsed — an empty `Families`, a module name that moved —
              // returns an EMPTY set, under which obligation-law would report every row as
              // dangling rather than going quietly vacuous. It is asserted anyway: a clause whose
              // input silently collapsed is the failure this family was cut to catch, in whichever
              // direction it points.
              Expect.isGreaterThan
                  (Set.count laws)
                  20
                  "the kit's declared law-family roster is readable, and is the vocabulary an obligation draws from"

              Expect.isTrue
                  (Set.contains "witnessLaws" laws)
                  "`witnessLaws` is in it — the one law `proofs.json`'s own claim text names in prose"

          // ---- the subject ----

          testCase "the kit's template ladder is written to the schema it ships beside (Phase 522)"
          <| fun _ ->
              // The template's rows are placeholders, so the clauses about the TREE cannot hold of
              // it; the clauses about a row's SHAPE can, and an adopter copies the template's shape.
              let template =
                  File.ReadAllText(Path.Combine(repoRoot, "proofs", "kit", "templates", "proofs.json"))

              let shapeClauses =
                  [ "[statement-boundary]"; "[claim-is-the-join]"; "[tested-family-closed]" ]

              let findings =
                  checkLadder
                      { Root = repoRoot
                        Modules = []
                        Suites = fun _ -> Some(Set.ofList [ "<a case name, as the family spells it>" ])
                        Laws = Set.empty }
                      template
                  |> List.filter (fun f -> shapeClauses |> List.exists f.Contains)

              Expect.isEmpty findings "every template row has a statement, a boundary, their join and a closed family"

          testCase "the committed proofs.json satisfies every clause"
          <| fun _ ->
              let text = File.ReadAllText ladderPath

              let inputs =
                  { Root = repoRoot
                    Modules = realModels ()
                    Suites = suiteCases
                    Laws = realLaws () }

              let findings = checkLadder inputs text

              if not (List.isEmpty findings) then
                  failtestf
                      "proofs.json and the tree disagree — the ladder is a claim about THIS repository, so fix whichever of the two is wrong:\n%s"
                      (String.concat "\n" findings)

              // A clean verdict over an empty ladder would be worth nothing.
              use doc = JsonDocument.Parse text

              let rows = doc.RootElement.GetProperty("claims").EnumerateArray() |> List.ofSeq

              let atLevel lvl =
                  rows |> List.filter (fun r -> strMember r "level" = Some lvl) |> List.length

              Expect.isGreaterThan (atLevel "proved") 4 "the ladder carries the proved rows the clauses are about"

              Expect.isGreaterThan
                  (atLevel "tested")
                  0
                  "the ladder carries tested rows, so tested-case-exists is not vacuous"

              Expect.isGreaterThan
                  (atLevel "assumed")
                  0
                  "the ladder carries assumed rows, so assumed-class is not vacuous"

              // and all three classes are used — a ladder that had quietly become one class would
              // leave two clauses measuring nothing while reporting clean.
              let classesUsed = rows |> List.choose (fun r -> strMember r "class") |> Set.ofList

              Expect.equal
                  classesUsed
                  (Set.ofList classOrder)
                  "every class in the closed set is carried by at least one row, so obligation-law and bridge-closes both bite"

          testCase "proofs/README.md is the render of the ladder, the roster and the model headers"
          <| fun _ ->
              // Phase 309 generated the counts and the contract table; Phase 533 makes the whole of
              // the README's claims a render (`ProofReadme.render`): the summary, the contract table,
              // the operation table and every model section. The tool's `render proofs-readme` is the
              // file's one writer; this holds the committed file to it and writes nothing.
              let readmeText = File.ReadAllText proofsReadmePath

              match ProofReadme.renderLive () with
              | Error why -> failtestf "the README's generated blocks could not be located: %s" why
              | Ok expected when expected = readmeText.Replace("\r\n", "\n") -> ()
              | Ok expected ->
                  let a = readmeText.Replace("\r\n", "\n").Split('\n')
                  let b = expected.Split('\n')

                  let firstDiff =
                      Seq.zip a b
                      |> Seq.tryFindIndex (fun (x, y) -> x <> y)
                      |> Option.defaultValue (min a.Length b.Length)

                  failtestf
                      "proofs/README.md is not the render — first differing line %d:\n  committed: %s\n  rendered:  %s\nRe-render with `%s` and commit the README."
                      (firstDiff + 1)
                      (if firstDiff < a.Length then a[firstDiff] else "<end>")
                      (if firstDiff < b.Length then b[firstDiff] else "<end>")
                      (Tool.render ProofReadme.Family)

          testCase "the committed proofs.json and proofs/README.md's contract table agree, row for row"
          <| fun _ ->
              let disagreements =
                  checkContract (File.ReadAllText ladderPath) (File.ReadAllText proofsReadmePath)

              if not (List.isEmpty disagreements) then
                  failtestf
                      "the contract a domain READS and the ladder a tool READS disagree — fix whichever of the two is wrong:\n%s"
                      (String.concat "\n" disagreements)

              // A clean verdict over an empty table would be worth nothing: the section could have
              // been renamed, or its table replaced by prose, and the comparison would be vacuous.
              let parsed =
                  contractRows (File.ReadAllText proofsReadmePath) |> Option.defaultValue []

              Expect.isGreaterThan
                  (List.length parsed)
                  10
                  "the contract table parses, and carries the ladder's assumed rows rather than a handful"

          testCase "the baseline fixture is clean, so a go-red below is firing on its own mutation"
          <| fun _ ->
              let findings = checkLadder (fixtureInputs [ "Model" ]) (fixture "ladder-ok.json")

              Expect.isEmpty
                  findings
                  (sprintf
                      "ladder-ok.json is the unmutated baseline and must yield nothing:\n%s"
                      (String.concat "\n" findings))

          // ---- one go-red per clause: the check can fail, and says what and where ----

          testCase "go-red: theorem-exists — a proved row naming a theorem the model does not declare"
          <| fun _ -> expectOneFinding [ "Model" ] "theorem-missing.json" "theorem-exists" "fixture-theorem"

          testCase "go-red: theorem-exists — a proved row naming an indented binding, not a declaration"
          <| fun _ -> expectOneFinding [ "Model" ] "theorem-indented.json" "theorem-exists" "fixture-theorem"

          testCase "go-red: model-registered — a proved row over a model the proof leg never checks"
          <| fun _ -> expectOneFinding [ "Model" ] "model-unregistered.json" "model-registered" "fixture-unchecked"

          testCase "go-red: model-registered — a proved row citing a model file that is not there"
          <| fun _ -> expectOneFinding [ "Model"; "Gone" ] "model-missing.json" "model-registered" "fixture-vanished"

          testCase "go-red: tested-case-exists — a tested row naming a case that does not exist"
          <| fun _ ->
              expectOneFinding [ "Model" ] "tested-case-missing.json" "tested-case-exists" "fixture-differential"

          testCase "go-red: tested-case-exists — a tested row naming no case at all"
          <| fun _ ->
              expectOneFinding [ "Model" ] "tested-tests-absent.json" "tested-case-exists" "fixture-differential"

          testCase "go-red: every-module-has-a-row — a checked module with no proved row"
          <| fun _ -> expectOneFinding [ "Model" ] "module-without-row.json" "every-module-has-a-row" "Model"

          testCase "go-red: phase-form — a row whose phase is not the fuaran-core#NNN form"
          <| fun _ -> expectOneFinding [ "Model" ] "phase-form.json" "phase-form" "fixture-theorem"

          testCase "go-red: closed-level-set — a row at a level outside the four"
          <| fun _ -> expectOneFinding [ "Model" ] "level-unknown.json" "closed-level-set" "fixture-invented"

          testCase "go-red: assumed-class — an assumed row carrying no class at all"
          <| fun _ -> expectOneFinding [ "Model" ] "class-absent.json" "assumed-class" "fixture-premise"

          testCase "go-red: assumed-class — an assumed row at a class outside the closed three"
          <| fun _ -> expectOneFinding [ "Model" ] "class-unknown.json" "assumed-class" "fixture-premise"

          testCase "go-red: obligation-law — a domain obligation naming a law the kit does not export"
          <| fun _ -> expectOneFinding [ "Model" ] "obligation-law-dangling.json" "obligation-law" "fixture-obligation"

          testCase "go-red: obligation-law — a domain obligation naming no law at all"
          <| fun _ -> expectOneFinding [ "Model" ] "obligation-law-absent.json" "obligation-law" "fixture-obligation"

          testCase "go-red: bridge-closes — a model bridge whose closes is neither a phase nor a literal"
          <| fun _ -> expectOneFinding [ "Model" ] "bridge-closes-form.json" "bridge-closes" "fixture-bridge"

          testCase "go-red: bridge-closes — a model bridge carrying no closes at all"
          <| fun _ -> expectOneFinding [ "Model" ] "bridge-closes-absent.json" "bridge-closes" "fixture-bridge"

          // ---- Phase 522: statement and boundary, the join, the closed family, the paths ----

          testCase "go-red: statement-boundary — a row with no boundary"
          <| fun _ ->
              expectMutationFinding
                  "no boundary"
                  "fixture-premise"
                  (fun r -> r.Remove "boundary" |> ignore)
                  "statement-boundary"
                  "no `boundary`"

          testCase "go-red: statement-boundary — a row whose statement tells its history"
          <| fun _ ->
              expectMutationFinding
                  "history in the statement"
                  "fixture-policy"
                  (withProse "statement" "A policy row, recorded by Phase 12 after a review.")
                  "statement-boundary"
                  "Phase 12"

          testCase "go-red: claim-is-the-join — a claim that says something its two fields do not"
          <| fun _ ->
              expectMutationFinding
                  "claim not the join"
                  "fixture-obligation"
                  (fun r -> r["claim"] <- Nodes.JsonValue.Create "Something the statement never said.")
                  "claim-is-the-join"
                  "fixture-obligation"

          testCase "go-red: tested-family-closed — a family written as prose"
          <| fun _ ->
              expectMutationFinding
                  "prose family"
                  "fixture-differential"
                  (fun r -> r["evidence"]["family"] <- Nodes.JsonValue.Create "A differential described in prose")
                  "tested-family-closed"
                  "A differential described in prose"

          testCase "go-red: tested-family-closed — a law family the kit does not export"
          <| fun _ ->
              expectMutationFinding
                  "unexported law family"
                  "fixture-differential"
                  (fun r -> r["evidence"]["family"] <- Nodes.JsonValue.Create "Conformance.ghostLaws")
                  "tested-family-closed"
                  "Conformance.ghostLaws"

          testCase "go-red: tested-family-closed — a well-formed suite the test tree does not declare"
          <| fun _ ->
              expectMutationFinding
                  "undeclared suite"
                  "fixture-differential"
                  (fun r -> r["evidence"]["family"] <- Nodes.JsonValue.Create "Suite:NoSuchModule.tests")
                  "tested-family-closed"
                  "Suite:NoSuchModule.tests"

          testCase "go-red: theorem-exists — a supporting declaration that is not top-level"
          <| fun _ ->
              expectMutationFinding
                  "indented supporting"
                  "fixture-theorem"
                  (fun r ->
                      r["evidence"]["supporting"] <-
                          Nodes.JsonArray(Nodes.JsonValue.Create "beta", Nodes.JsonValue.Create "delta"))
                  "theorem-exists"
                  "`delta`"

          testCase "go-red: evidence-exists — a policy row whose document is not there"
          <| fun _ ->
              expectMutationFinding
                  "missing document"
                  "fixture-policy"
                  (fun r -> r["evidence"]["document"] <- Nodes.JsonValue.Create "no-such-document.md")
                  "evidence-exists"
                  "no-such-document.md"

          testCase "go-red: evidence-exists — a tested row whose artifact is not there"
          <| fun _ ->
              expectMutationFinding
                  "missing artifact"
                  "fixture-differential"
                  (fun r -> r["evidence"]["artifact"] <- Nodes.JsonValue.Create "oracle/NoSuch.fs")
                  "evidence-exists"
                  "oracle/NoSuch.fs"

          testCase "go-red: evidence-exists — a row resting on another repository's tree"
          <| fun _ ->
              expectMutationFinding
                  "foreign repo"
                  "fixture-theorem"
                  (fun r -> r["repo"] <- Nodes.JsonValue.Create "Elsewhere/other-repo")
                  "evidence-exists"
                  "Elsewhere/other-repo"

          // ---- the contract clause: the README table and the ladder, row for row ----

          testCase "the contract fixture is clean, so the two go-reds below fire on their own mutation"
          <| fun _ ->
              let findings = checkContract (fixture "ladder-ok.json") (fixture "contract-ok.md")

              Expect.isEmpty
                  findings
                  (sprintf
                      "contract-ok.md is the unmutated baseline for ladder-ok.json:\n%s"
                      (String.concat "\n" findings))

              // and it is measuring something: the baseline ladder carries all three classes.
              Expect.equal
                  (List.length (contractFromLadder (fixture "ladder-ok.json")))
                  3
                  "the fixture ladder's three assumed rows are what the fixture table is compared against"

          testCase "go-red: contract-agrees — the table drops a row the ladder classifies"
          <| fun _ ->
              let findings =
                  checkContract (fixture "ladder-ok.json") (fixture "contract-row-missing.md")

              Expect.equal (List.length findings) 1 (sprintf "one finding — got:\n%s" (String.concat "\n" findings))

              Expect.stringContains (List.head findings) "[contract-agrees]" "the finding names the clause"
              Expect.stringContains (List.head findings) "fixture-bridge" "the finding names the dropped row"

          testCase "go-red: contract-agrees — the table and the ladder disagree about a row's class"
          <| fun _ ->
              let findings =
                  checkContract (fixture "ladder-ok.json") (fixture "contract-class-differs.md")

              Expect.equal (List.length findings) 1 (sprintf "one finding — got:\n%s" (String.concat "\n" findings))

              Expect.stringContains (List.head findings) "[contract-agrees]" "the finding names the clause"
              Expect.stringContains (List.head findings) "fixture-premise" "the finding names the row"

          testCase "go-red: contract-agrees — a README with no contract section at all"
          <| fun _ ->
              let findings =
                  checkContract (fixture "ladder-ok.json") "# a README that never wrote one\n"

              Expect.equal (List.length findings) 1 (sprintf "one finding — got:\n%s" (String.concat "\n" findings))
              Expect.stringContains (List.head findings) "[contract-agrees]" "the finding names the clause"

              Expect.stringContains
                  (List.head findings)
                  contractHeading
                  "the finding names the section that is not there"

          testCase "go-red: shape — a ladder with no claims array is reported as that, and nothing else"
          <| fun _ ->
              let findings = checkLadder (fixtureInputs [ "Model" ]) (fixture "no-claims.json")

              Expect.equal (List.length findings) 1 (sprintf "one finding — got:\n%s" (String.concat "\n" findings))

              Expect.stringContains (List.head findings) "[shape]" "the finding names the shape clause" ]

// ---------------------------------------------------------------------------
//  Phase 175 — the kit's instantiation template, and `Skeleton.fst` as its first instance
// ---------------------------------------------------------------------------
//
// `proofs/kit/templates/Instance.fst.template` is `Skeleton.fst` with holes: the fold-confluence
// theorem instantiated at a domain by filling fourteen `{{HOLE}}`s. A template whose instance can
// drift from it is two files that USED to agree, so this family holds the two together — the
// committed `Skeleton.fst` is reproduced from the template, byte for byte, and a wrong value or an
// unfilled hole is shown NOT to reproduce it. The substitution itself is the contract the template's
// preamble states (drop through the marker line; replace every hole; refuse a `{{` that remains),
// implemented here once so an adopter reading the test reads the rule.
//
// Two of the holes are prose — the header comment's interior and everything after the theorem —
// and their values for `Skeleton.fst` are recovered FROM `Skeleton.fst` by matching the template's
// fixed text around them, rather than restated here: a second copy of forty lines of prose would be
// the drift this file exists to catch. The identifier holes are the map below, which is the map an
// adopter writes.

/// The line that ends the template's preamble; the instance is everything after it.
let templateMarker = "(* ==== END OF PREAMBLE ==== *)"

let private holeForm = Regex(@"\{\{([A-Z_]+)\}\}", RegexOptions.Compiled)

/// The holes a template body carries, in first-occurrence order, each once.
let templateHoles (body: string) : string list =
    holeForm.Matches body
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.distinct
    |> List.ofSeq

/// The template's body — everything after the marker line, which must occur exactly once.
let templateBody (template: string) : Result<string, string> =
    let lines = template.Split '\n'

    match lines |> Array.indexed |> Array.filter (fun (_, l) -> l = templateMarker) with
    | [| (i, _) |] -> Ok(String.Join("\n", lines[i + 1 ..]))
    | hits ->
        Error(sprintf "the marker line `%s` occurs %d times; the contract needs exactly one" templateMarker hits.Length)

let private fill (values: (string * string) list) (body: string) : string =
    values
    |> List.fold (fun (acc: string) (k, v) -> acc.Replace("{{" + k + "}}", v)) body

/// The instantiation contract: drop the preamble, fill every hole, refuse any that remains.
let instantiate (template: string) (values: (string * string) list) : Result<string, string> =
    templateBody template
    |> Result.bind (fun body ->
        let filled = fill values body

        match templateHoles filled with
        | [] -> Ok filled
        | left -> Error(sprintf "unfilled holes: %s" (String.concat ", " left)))

/// The identifier holes at the skeleton-op tree algebra — the map that makes `Skeleton.fst`.
let skeletonHoles =
    [ "MODULE", "Skeleton"
      "DOMAIN", "TreeOps"
      "OP", "op"
      "STATE", "tree"
      "REJ", "rejection"
      "APPLY", "wapply"
      "FOOTPRINT", "op_fp"
      "FOLD", "skeleton_fold"
      "DIAMOND", "op_independence_diamond"
      "PROD_APPLY", "Ops.apply"
      "PROD_FOOTPRINT", "Ops.footprint"
      "PROD_FOLD", "FoldConfluence.foldOnce" ]

let private proseHoles = [ "HEADER"; "TRAILER" ]

/// Recover the two prose holes' values from an instance, by matching the template's fixed text
/// around them: with the identifiers filled the body is `pre {{HEADER}} mid {{TRAILER}} post`, and
/// an instance of it is `pre` + header + `mid` + trailer + `post`.
let recoverProse
    (template: string)
    (identifiers: (string * string) list)
    (instance: string)
    : Result<(string * string) list, string> =
    templateBody template
    |> Result.map (fill identifiers)
    |> Result.bind (fun filled ->
        let ih = filled.IndexOf("{{HEADER}}", StringComparison.Ordinal)
        let it = filled.IndexOf("{{TRAILER}}", StringComparison.Ordinal)

        if ih < 0 || it < ih then
            Error "the template must carry {{HEADER}} before {{TRAILER}}"
        else
            let pre = filled.Substring(0, ih)
            let mid = filled.Substring(ih + "{{HEADER}}".Length, it - ih - "{{HEADER}}".Length)
            let post = filled.Substring(it + "{{TRAILER}}".Length)

            if not (instance.StartsWith(pre, StringComparison.Ordinal)) then
                Error "the instance does not begin as the template does"
            elif not (instance.EndsWith(post, StringComparison.Ordinal)) then
                Error "the instance does not end as the template does"
            else
                let inner =
                    instance.Substring(pre.Length, instance.Length - pre.Length - post.Length)

                let j = inner.IndexOf(mid, StringComparison.Ordinal)

                if j < 0 then
                    // Name the first fixed line the instance has lost, so a drift reads as a diff.
                    let lost =
                        mid.Split '\n'
                        |> Array.tryFind (fun l -> l <> "" && not (inner.Contains l))
                        |> Option.defaultValue "(every line is present; their order or spacing differs)"

                    Error(sprintf "the template's fixed text is not in the instance; first line missing: %s" lost)
                else
                    Ok [ "HEADER", inner.Substring(0, j); "TRAILER", inner.Substring(j + mid.Length) ])

let private templatePath =
    Path.Combine(repoRoot, "proofs", "kit", "templates", "Instance.fst.template")

let private skeletonPath = Path.Combine(repoRoot, "proofs", "Skeleton.fst")
let private treeOpsPath = Path.Combine(repoRoot, "proofs", "TreeOps.fst")

let private expectOk (r: Result<'a, string>) (what: string) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failtestf "%s: %s" what e

[<Tests>]
let proofsKitTemplateTests =
    testList
        "Proofs.Kit"
        [

          testCase "the template's body carries exactly the holes the Skeleton map and the two prose holes fill"
          <| fun _ ->
              let body = expectOk (templateBody (File.ReadAllText templatePath)) "template"
              let declared = (List.map fst skeletonHoles) @ proseHoles |> Set.ofList
              let carried = templateHoles body |> Set.ofList

              Expect.equal
                  carried
                  declared
                  "a hole the map does not fill would be refused at instantiation; a map key the template lacks is a stale map"

              // and the fixed text is a theorem, not a frame around two prose holes
              Expect.stringContains
                  body
                  "fold_confluence_total {{APPLY}} {{FOOTPRINT}} s0 ls1 ls2 p"
                  "the theorem's discharge line is fixed text"

              Expect.stringContains body "{{DIAMOND}} ();" "the obligation's discharge is fixed text"

              Expect.stringContains
                  body
                  "requires lanes_apply {{APPLY}} ls1 s0"
                  "what remains in the requires is fixed text"

          testCase "Skeleton.fst is the template's first instance — reproduced byte for byte after hole substitution"
          <| fun _ ->
              let template = File.ReadAllText templatePath
              let skeleton = File.ReadAllText skeletonPath

              Expect.isFalse
                  (skeleton.Contains "{{")
                  "the instance carries no hole, so 'no {{ remains' is measuring something"

              let prose =
                  expectOk (recoverProse template skeletonHoles skeleton) "recovering Skeleton.fst's prose holes"

              let reproduced =
                  expectOk (instantiate template (skeletonHoles @ prose)) "instantiating"

              Expect.equal reproduced skeleton "the template, holes filled at TreeOps, IS the committed Skeleton.fst"

              // the recovered prose is prose: the header is the comment interior, and the trailer
              // carries the non-vacuity witness the preamble asks every instance for
              let header = prose |> List.find (fun (k, _) -> k = "HEADER") |> snd
              let trailer = prose |> List.find (fun (k, _) -> k = "TRAILER") |> snd
              Expect.isFalse (header.Contains "*)") "the header hole is the interior of one comment"
              Expect.stringContains trailer "batch_lanes_fold" "Skeleton.fst's trailer is its widening witness"

          testCase "the Skeleton map names what the tree carries — the obligation is a declaration of the domain module"
          <| fun _ ->
              let treeOps = File.ReadAllText treeOpsPath

              for name in [ "op_independence_diamond"; "wapply"; "op_fp" ] do
                  Expect.isTrue
                      (declaresTopLevel treeOps name)
                      (sprintf
                          "`%s` is a top-level declaration of TreeOps.fst, as the template's DIAMOND / APPLY / FOOTPRINT holes require"
                          name)

              Expect.contains (realModules ()) "Skeleton" "the instance is a module the proof leg checks"

          testCase "go-red: a wrong obligation name does not reproduce the instance"
          <| fun _ ->
              let template = File.ReadAllText templatePath
              let skeleton = File.ReadAllText skeletonPath

              let wrong =
                  skeletonHoles
                  |> List.map (fun (k, v) ->
                      if k = "DIAMOND" then
                          k, "tree_independence_diamond"
                      else
                          k, v)

              match recoverProse template wrong skeleton with
              | Error _ -> ()
              | Ok prose ->
                  let reproduced = expectOk (instantiate template (wrong @ prose)) "instantiating"
                  Expect.notEqual reproduced skeleton "a different discharge lemma is a different instance"

          testCase "go-red: an unfilled hole is refused, never defaulted"
          <| fun _ ->
              let template = File.ReadAllText templatePath
              let partial = skeletonHoles |> List.filter (fun (k, _) -> k <> "FOLD")

              match instantiate template (partial @ [ "HEADER", "x"; "TRAILER", "" ]) with
              | Ok _ -> failtest "an instance with {{FOLD}} unfilled was accepted"
              | Error e -> Expect.stringContains e "FOLD" "the refusal names the hole"

          testCase "go-red: a template with no marker line, or two, is refused"
          <| fun _ ->
              let template = File.ReadAllText templatePath

              match templateBody (template.Replace(templateMarker, "(* not the marker *)")) with
              | Ok _ -> failtest "a template with no marker was accepted"
              | Error e -> Expect.stringContains e "0 times" "the refusal counts the marker"

              match templateBody (template + "\n" + templateMarker + "\n") with
              | Ok _ -> failtest "a template with two markers was accepted"
              | Error e -> Expect.stringContains e "2 times" "the refusal counts the marker" ]

// ---- the family ------------------------------------------------------------------------------

/// A four-module toy tree for the exact assertions: B opens A, C opens B, D stands alone; A and C
/// cover `Pkg.A`, D covers `Pkg.D`. The real tree's assertions are properties rather than pinned
/// sets, so a sibling adding a model cannot turn them red.
let private toyFacts (changed: string list) : ConeFacts =
    { Roster = [ "A"; "B"; "C"; "D" ]
      Shared = Set.empty
      References = Map.ofList [ "A", Set.empty; "B", set [ "A" ]; "C", set [ "B" ]; "D", Set.empty ]
      Packages = Map.ofList [ "A", [ "Pkg.A" ]; "B", []; "C", [ "Pkg.A" ]; "D", [ "Pkg.D" ] ]
      Changed = changed
      ScriptCodeMoved = Set.empty
      EntriesMoved = Set.empty }

let private realConeFacts (changed: string list) : ConeFacts =
    match coneRosterFacts repoRoot with
    | Ok facts -> { facts with Changed = changed }
    | Error e -> failtestf "%s" e

let private withVerdict (v: ConeVerdict) (rows: ConeRow list) : Set<string> =
    rows |> List.filter (fun r -> r.Verdict = v) |> List.map _.Module |> Set.ofList

let private reasonOf (m: string) (rows: ConeRow list) : string =
    rows |> List.find (fun r -> r.Module = m) |> _.Reason

/// An INDEPENDENT reading of a model's declared edges — whole-line `open X` and `module Y = X`,
/// nothing else — against which the selector's wider reading is held: it may find more, never less.
let private declaredOpens (roster: string list) (source: string) : Set<string> =
    Regex.Matches(source, @"^(?:open\s+([A-Z]\w*)|module\s+\w+\s*=\s*([A-Z]\w*))\s*$", RegexOptions.Multiline)
    |> Seq.map (fun m ->
        if m.Groups[1].Success then
            m.Groups[1].Value
        else
            m.Groups[2].Value)
    |> Seq.filter (fun n -> List.contains n roster)
    |> Set.ofSeq

/// `target` and every module that reaches it along `edges`, however transitively.
let private reaching (roster: string list) (edges: string -> Set<string>) (target: string) : Set<string> =
    let rec grow (acc: Set<string>) =
        let next =
            roster
            |> List.filter (fun d -> not (acc.Contains d) && (edges d |> Set.exists acc.Contains))

        if List.isEmpty next then
            acc
        else
            grow (Set.union acc (Set.ofList next))

    grow (Set.singleton target)

let private realModelText (m: string) =
    File.ReadAllText(Path.Combine(repoRoot, "proofs", m + ".fst"))

// A scratch repository for the end-to-end case: git is the input the selector reads, so the case
// that proves the git half builds a real history rather than describing one.
let private gitOk (root: string) (args: string list) : string =
    match runGit root args with
    | 0, out, _ -> out
    | code, _, err -> failtestf "git %s exited %d: %s" (String.concat " " args) code err

let private writeAt (root: string) (rel: string) (text: string) =
    let path = Path.Combine(root, rel)
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, text)

let private commitAll (root: string) (message: string) =
    gitOk root [ "add"; "-A" ] |> ignore

    gitOk
        root
        [ "-c"
          "user.name=cone"
          "-c"
          "user.email=cone@example.invalid"
          "-c"
          "commit.gpgsign=false"
          "commit"
          "-q"
          "-m"
          message ]
    |> ignore

let private deleteScratch (dir: string) =
    // git writes its objects read-only, which Directory.Delete refuses on Windows.
    try
        for f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) do
            File.SetAttributes(f, FileAttributes.Normal)

        Directory.Delete(dir, true)
    with _ ->
        ()

let private toyRepository () : string =
    let root =
        Path.Combine(Path.GetTempPath(), "fuaran-core-cone-" + Guid.NewGuid().ToString("N").Substring(0, 12))

    Directory.CreateDirectory root |> ignore
    gitOk root [ "init"; "-q" ] |> ignore
    writeAt root "proofs/check.ps1" "# the leg\nWrite-Host 'leg'\n"
    writeAt root "proofs/A.fst" "module A\nlet a = 1\n"
    writeAt root "proofs/B.fst" "module B\nopen A\nlet b = a\n"
    writeAt root "proofs/C.fst" "module C\n(* names B.b in prose only *)\nlet c = 2\n"

    writeAt
        root
        "proofs/modules.json"
        """{ "modules": [ { "module": "A", "kind": "extracted", "opens": [], "packages": [ "Pkg.A" ] }, { "module": "B", "kind": "extracted", "opens": [ "A" ], "packages": [] }, { "module": "C", "kind": "extracted", "opens": [], "packages": [] } ] }"""

    writeAt root "proofs/fstar-pin.json" """{ "fstar": "v0" }"""
    writeAt root "src/Pkg.A/X.fs" "module X\n"
    commitAll root "base"
    root

let private coneIn (root: string) : Set<string> =
    match coneSince root "HEAD" with
    | Ok report -> withVerdict In report.Rows
    | Error e -> failtestf "the cone could not be computed: %s" e

// `proofs/check.ps1` run the way a person runs it, for the two cases about what the SCRIPT hands
// the kit. `-PlanOnly` prints the plan and exits before the prover, the build or git is touched.
let private runCheckScript (args: string list) : int * string =
    match
        ChildProcess.argv "pwsh" ([ "-NoProfile"; "-NonInteractive"; "-File"; checkScriptPath ] @ args)
        |> ChildProcess.run
    with
    | Ok o -> o.Exit, Regex.Replace(o.Stdout + o.Stderr, "\u001b" + @"\[[0-9;]*m", "")
    | Error why -> failwith why

let private planLine (prefix: string) (output: string) : string =
    output.Replace("\r\n", "\n").Split '\n'
    |> Array.tryFind (fun l -> l.StartsWith prefix)
    |> Option.defaultWith (fun () -> failtestf "no '%s' line in:\n%s" prefix output)

let private plannedModules (output: string) : string list =
    let line = planLine "==== proofs: plan -- the kit checks" output
    let listed = line.Substring(line.IndexOf(": ", line.IndexOf "module(s)") + 2)

    listed.Split(',', StringSplitOptions.TrimEntries ||| StringSplitOptions.RemoveEmptyEntries)
    |> List.ofArray

[<Tests>]
let proofsConeTests =
    testList
        "Proofs.Cone"
        [

          // ---- the edges ----

          testCase
              "an edge is read from CODE: open, include, abbreviation and qualified name, never a comment or a string"
          <| fun _ ->
              let roster = [ "A"; "B"; "C"; "D"; "E"; "F"; "G"; "H"; "I"; "X" ]

              let source =
                  String.concat
                      "\n"
                      [ "module X"
                        "open A"
                        "(* open B, and C.x in prose (* nested: D.y *) still prose *)"
                        "let s = \"E.f and open E\""
                        "let q = '\"'"
                        "module J = F"
                        "let v = G.h 1"
                        "// H.i is a line comment"
                        "include I" ]

              Expect.equal
                  (modelReferences roster "X" source)
                  (set [ "A"; "F"; "G"; "I" ])
                  "the edges are the open, the abbreviation, the qualified name and the include, and nothing in a comment or a string"

          testCase "the real models' reference graph honours every declared open and abbreviation, and is acyclic"
          <| fun _ ->
              let roster = realModules ()
              let graph = modelReferenceGraph repoRoot roster

              for m in roster do
                  let declared = declaredOpens roster (realModelText m)
                  let read = graph[m]

                  Expect.isTrue
                      (Set.isSubset declared read)
                      (sprintf
                          "%s declares %A but the selector reads %A: a declared edge the cone does not follow is a module it would leave stale"
                          m
                          declared
                          read)

              Expect.isGreaterThan
                  (graph |> Map.toList |> List.sumBy (fun (_, s) -> s.Count))
                  0
                  "the graph has edges at all (non-vacuous)"

              // F* refuses a cycle, so a cycle here means prose was read as code.
              let rec reaches (seen: Set<string>) (m: string) (target: string) =
                  graph[m]
                  |> Set.exists (fun r -> r = target || (not (seen.Contains r) && reaches (seen.Add r) r target))

              for m in roster do
                  Expect.isFalse
                      (reaches Set.empty m m)
                      (sprintf "%s reaches itself: the reading found a cycle F* would refuse" m)

          // ---- the cone, exactly, on a toy tree ----

          testCase
              "a changed model puts that module and every module that references it, transitively, in the cone, and nothing else"
          <| fun _ ->
              let rows = computeCone (toyFacts [ "proofs/A.fst" ])

              Expect.equal
                  (withVerdict In rows)
                  (set [ "A"; "B"; "C" ])
                  "A and its dependants, B directly and C through B"

              Expect.equal (withVerdict Out rows) (set [ "D" ]) "D reads nothing that changed"
              Expect.equal (reasonOf "A" rows) "proofs/A.fst changed" "A's reason names the path"
              Expect.stringContains (reasonOf "C" rows) "B -> A" "C's reason names the chain back to the change"

          testCase "a module the cone references is checked as a DEPENDENCY, never claimed as changed"
          <| fun _ ->
              let rows = computeCone (toyFacts [ "proofs/C.fst" ])

              Expect.equal (withVerdict In rows) (set [ "C" ]) "only C changed, and nothing references it"
              Expect.equal (withVerdict Dependency rows) (set [ "A"; "B" ]) "C's references are checked ahead of it"
              Expect.equal (coneChecked rows) [ "A"; "B"; "C" ] "the kit is handed them in the roster's order"

          testCase "a covered production source puts exactly its covering models in, and reaches no dependant"
          <| fun _ ->
              let rows = computeCone (toyFacts [ "src/Pkg.A/Decode.fs" ])

              Expect.equal (withVerdict In rows) (set [ "A"; "C" ]) "A and C cover Pkg.A"

              Expect.equal
                  (withVerdict Dependency rows)
                  (set [ "B" ])
                  "B is only C's dependency: a source change is no model change"

              Expect.stringContains (reasonOf "C" rows) "src/Pkg.A/Decode.fs" "the reason names the path"

          testCase
              "a SHARED module's model is a shared input: every module in, as for the oracle's Prims.fs position (Phase 504)"
          <| fun _ ->
              // `Twin` and `ListAlgebra` are registered (they are checked and extracted) and opened by
              // every model; the cone reads a change to one as it reads the prover pin.
              let facts =
                  { toyFacts [ "proofs/A.fst" ] with
                      Shared = set [ "A" ] }

              Expect.equal
                  (withVerdict In (computeCone facts))
                  (set [ "A"; "B"; "C"; "D" ])
                  "a change to a shared module's model moves every module, D included though it references nothing"

              Expect.equal
                  (withVerdict In (computeCone (toyFacts [ "proofs/A.fst" ])))
                  (set [ "A"; "B"; "C" ])
                  "the same change to an ordinary model moves it and what references it, transitively: D stays out"

          testCase "a shared input puts every module in; documentation, templates, kit tests and the ladder put none in"
          <| fun _ ->
              for shared in
                  [ "proofs/fstar-pin.json"
                    "proofs/Unregistered.fst"
                    "proofs/something-new.txt"
                    "proofs/kit/new-engine-part.ps1" ] do
                  Expect.equal
                      (withVerdict In (computeCone (toyFacts [ shared ])))
                      (set [ "A"; "B"; "C"; "D" ])
                      (sprintf
                          "%s is read by every module's check (or is unclassified, which is the same answer)"
                          shared)

              let inert =
                  [ "proofs/README.md"
                    "proofs/kit/LADDER.md"
                    "proofs/kit/templates/check.ps1"
                    "proofs/kit/check-proof-leg.tests.ps1"
                    "proofs/coverage-exclusions.json"
                    "proofs/last-strict.json"
                    "proofs/oracle/Prims.fs"
                    "proofs.json"
                    "tests/Fuaran.Core.Tests/Program.fs"
                    "DECISIONS.md" ]

              Expect.isEmpty
                  (withVerdict In (computeCone (toyFacts inert)))
                  "none of these is read by the prover or the extraction"

          testCase "a leg script's comment-only edit is prose; any other edit is code"
          <| fun _ ->
              let before = "#Requires -Version 7.0\n# prose\n$x = 1\n"

              let prose =
                  "#Requires -Version 7.0\n# prose, reworded\n\n    # and indented\n$x = 1   \r\n"

              let code = before + "$legArgs.ZRlimit = 80\n"

              Expect.equal (scriptCode prose) (scriptCode before) "comments, blanks and line endings are not code"

              Expect.notEqual (scriptCode code) (scriptCode before) "a new statement is"

              let moved =
                  { toyFacts [ "proofs/check.ps1" ] with
                      ScriptCodeMoved = set [ "proofs/check.ps1" ] }

              Expect.equal
                  (withVerdict In (computeCone moved))
                  (set [ "A"; "B"; "C"; "D" ])
                  "code that moved moves every module"

              Expect.isEmpty
                  (withVerdict In (computeCone (toyFacts [ "proofs/check.ps1" ])))
                  "prose that moved moves none"

          testCase "a modules.json edit puts in exactly the modules whose ENTRY moved"
          <| fun _ ->
              let before =
                  """{ "seeding": { "rule": "x" }, "modules": [ { "module": "A", "floorSeconds": 4 }, { "module": "B", "floorSeconds": 0 } ] }"""

              let reseeded =
                  """{ "seeding": { "rule": "x, reworded" }, "modules": [ { "module": "A", "floorSeconds": 3 }, { "module": "B",   "floorSeconds": 0 } ] }"""

              Expect.equal
                  (budgetEntriesChanged before reseeded)
                  (set [ "A" ])
                  "A's floor moved; B was only re-spaced and the seeding block is prose"

              Expect.equal (budgetEntriesChanged before before) Set.empty "an unchanged file moves nothing"

              let rows =
                  computeCone
                      { toyFacts [ "proofs/modules.json" ] with
                          EntriesMoved = set [ "A" ] }

              Expect.equal (withVerdict In rows) (set [ "A" ]) "a budget entry is read by its own module's step alone"

          // ---- the cone on the real tree ----

          testCase "perturbing each real model: the cone is it and every module that opens it, transitively"
          <| fun _ ->
              let roster = realModules ()
              let graph = modelReferenceGraph repoRoot roster

              let declared =
                  roster
                  |> List.map (fun m -> m, declaredOpens roster (realModelText m))
                  |> Map.ofList

              let shared = (realConeFacts []).Shared
              Expect.isNonEmpty shared "the roster declares its shared modules"

              for e in realRoster () do
                  Expect.equal
                      (Set.ofList e.Opens)
                      graph[e.Module]
                      (sprintf
                          "%s's `opens` in modules.json is not what its code references — declared %A, read %A"
                          e.Module
                          e.Opens
                          graph[e.Module])

              for m in roster |> List.filter shared.Contains do
                  Expect.equal
                      (withVerdict In (computeCone (realConeFacts [ "proofs/" + m + ".fst" ])))
                      (Set.ofList roster)
                      (sprintf "perturbing the shared module %s puts every module in" m)

              for m in roster |> List.filter (shared.Contains >> not) do
                  let path = "proofs/" + m + ".fst"
                  let inCone = withVerdict In (computeCone (realConeFacts [ path ]))
                  let expected = reaching roster (fun d -> declared[d]) m

                  Expect.isTrue
                      (Set.isSubset expected inCone)
                      (sprintf
                          "perturbing %s: the cone %A misses a module that opens it, %A"
                          path
                          inCone
                          (Set.difference expected inCone))

                  Expect.isTrue
                      (Set.isSubset inCone (reaching roster (fun d -> graph[d]) m))
                      (sprintf "perturbing %s: the cone %A holds a module that does not reference it" path inCone)

          testCase "perturbing an opened real model: every module that opens it, however transitively, is in"
          <| fun _ ->
              let roster = realModules ()

              let declared =
                  roster
                  |> List.map (fun m -> m, declaredOpens roster (realModelText m))
                  |> Map.ofList

              // A target with a dependant that reaches it ONLY through another module, so the case
              // is about transitivity and not merely about direct opens.
              let shared = (realConeFacts []).Shared

              let witness =
                  roster
                  |> List.filter (shared.Contains >> not)
                  |> List.tryPick (fun target ->
                      reaching roster (fun d -> declared[d]) target
                      |> Set.toList
                      |> List.tryFind (fun d -> d <> target && not (declared[d].Contains target))
                      |> Option.map (fun indirect -> target, indirect))

              match witness with
              | None -> failtest "no real model is opened only transitively by another; the case would be vacuous"
              | Some(target, indirect) ->
                  let rows = computeCone (realConeFacts [ "proofs/" + target + ".fst" ])
                  let inCone = withVerdict In rows

                  Expect.isTrue
                      (Set.isSubset (reaching roster (fun d -> declared[d]) target) inCone)
                      (sprintf "perturbing %s: every module opening it transitively is in" target)

                  Expect.stringContains
                      (reasonOf indirect rows)
                      " -> "
                      (sprintf
                          "%s reaches %s only through another module, and its reason names the chain"
                          indirect
                          target)

          testCase
              "perturbing a covered real production source: exactly the models whose modules.json packages name it are in"
          <| fun _ ->
              let packages = (realConeFacts []).Packages
              let package = "Fuaran.Core.Wire"

              let covering =
                  packages
                  |> Map.filter (fun _ ps -> List.contains package ps)
                  |> Map.keys
                  |> Set.ofSeq

              Expect.isNonEmpty covering "some real model covers the package (non-vacuous)"
              let path = "src/" + package + "/Decode.fs"
              let rows = computeCone (realConeFacts [ path ])

              Expect.equal (withVerdict In rows) covering "the covering models, and no dependant of theirs"

              for m in covering do
                  Expect.stringContains (reasonOf m rows) path (sprintf "%s's reason names the path" m)

          // ---- git, end to end ----

          testCase
              "end to end over a real history: the cone, the empty cone's baseline, and a record refused over a dirty tree"
          <| fun _ ->
              let root = toyRepository ()

              try
                  Expect.isEmpty (coneIn root) "nothing changed since HEAD"

                  match emptyConeBaseline root with
                  | Ok line -> failtestf "an empty cone with no recorded strict run was green: %s" line
                  | Error why ->
                      Expect.stringContains why "NO STRICT BASELINE" "and it says the leg has no strict baseline"

                  // Phase 522 — the record IS the run's verdict, and the empty cone's line names the
                  // run it describes. Written outside the toy tree, as check.ps1 does.
                  let verdictFile =
                      Path.Combine(Path.GetTempPath(), sprintf "run-verdict-%s.json" (Guid.NewGuid().ToString "N"))

                  let strictVerdict =
                      """{ "kind": "proofRunVerdict", "verdict": "green", "exit": 0, "message": null, "strict": true, "runs": 3, "prover": "v0", "modules": [ "A", "B", "C" ], "machine": { "kind": "ci", "runner": "quiet-box", "os": "Toy OS 1", "processors": 4 }, "contention": [ { "run": 1, "factor": 0.31 }, { "run": 2, "factor": null } ], "costs": [ { "module": "A", "run": 1, "percent": 50 }, { "module": "B", "run": 1, "percent": 110 }, { "module": "B", "run": 2, "percent": 125 } ], "cone": null, "refusals": "held" }"""

                  try
                      // Not a green, strict, full run: each is refused, naming why.
                      for find, replace, why in
                          [ "\"verdict\": \"green\"", "\"verdict\": \"refuted\"", "refuted run"
                            "\"strict\": true", "\"strict\": false", "not -Strict"
                            "\"cone\": null", "\"cone\": [ \"A\" ]", "cone run"
                            "\"kind\": \"proofRunVerdict\"", "\"kind\": \"proofStrictBaseline\"", "not a run verdict" ] do
                          File.WriteAllText(verdictFile, strictVerdict.Replace(find, replace))

                          match recordStrictBaseline root DateTimeOffset.UtcNow verdictFile with
                          | Ok line -> failtestf "a verdict that is no green strict full run was recorded: %s" line
                          | Error message -> Expect.stringContains message why (sprintf "and the refusal says %s" why)

                      Expect.isFalse (File.Exists(Path.Combine(root, coneBaselinePath))) "nothing was written"

                      File.WriteAllText(verdictFile, strictVerdict)

                      match
                          recordStrictBaseline root (DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero)) verdictFile
                      with
                      | Error why -> failtestf "a clean tree's strict run was not recorded: %s" why
                      | Ok _ -> ()
                  finally
                      File.Delete verdictFile

                  let recorded = File.ReadAllText(Path.Combine(root, coneBaselinePath))
                  let head = (gitOk root [ "rev-parse"; "HEAD" ]).Trim()
                  Expect.stringContains recorded head "the record names the tree the run verified"
                  Expect.stringContains recorded "quiet-box" "the record names the machine the run was on"
                  Expect.stringContains recorded "0.31" "and each run's contention factor"
                  Expect.stringContains recorded "\"refusals\": \"held\"" "and every other member of the verdict"
                  Expect.isFalse (recorded.Contains "\r") "the record is written LF"
                  commitAll root "record the strict baseline"

                  match emptyConeBaseline root with
                  | Error why -> failtestf "an empty cone over a recorded ancestor was not green: %s" why
                  | Ok line ->
                      Expect.stringContains
                          line
                          "run 2026-09-30T00:00:00Z, on CI runner quiet-box (Toy OS 1, 4 processors), contention x0.31, not computed, over budget: B 125%"
                          "the empty cone's line says over which run it is green"

                  // A model edit: A and B (which opens A) are in; C only names B in prose.
                  File.AppendAllText(Path.Combine(root, "proofs/A.fst"), "let a2 = 2\n")
                  Expect.equal (coneIn root) (set [ "A"; "B" ]) "the working tree's model edit, and its dependant"

                  File.WriteAllText(verdictFile, strictVerdict)

                  try
                      match recordStrictBaseline root DateTimeOffset.UtcNow verdictFile with
                      | Ok line -> failtestf "a strict run over an uncommitted model edit was recorded: %s" line
                      | Error why -> Expect.stringContains why "NOT recorded" "a dirty tree is never recorded"
                  finally
                      File.Delete verdictFile

                  // An untracked covered source counts too.
                  writeAt root "src/Pkg.A/Y.fs" "module Y\n"
                  Expect.isTrue ((coneIn root).Contains "A") "an untracked covered source is a change"

                  commitAll root "move A"
                  Expect.isEmpty (coneIn root) "nothing changed since the new HEAD"

                  match emptyConeBaseline root with
                  | Ok line -> failtestf "an empty cone leaned on a baseline that predates a model change: %s" line
                  | Error why ->
                      Expect.stringContains
                          why
                          "predates changes to A, B"
                          "and it names what the baseline does not cover"

                  // check.ps1: prose moves nothing, code moves everything.
                  File.AppendAllText(Path.Combine(root, "proofs/check.ps1"), "# more prose\n")
                  Expect.isEmpty (coneIn root) "a comment-only edit to check.ps1"
                  File.AppendAllText(Path.Combine(root, "proofs/check.ps1"), "Write-Host 'more code'\n")
                  Expect.equal (coneIn root) (set [ "A"; "B"; "C" ]) "a code edit to check.ps1 moves every module"

                  // A baseline naming a tree this clone does not hold is no baseline.
                  writeAt
                      root
                      coneBaselinePath
                      (strictVerdict.Replace(
                          "\"kind\": \"proofRunVerdict\",",
                          "\"kind\": \"proofRunVerdict\", \"tree\": \"0000000000000000000000000000000000000001\","
                      ))

                  match emptyConeBaseline root with
                  | Ok line -> failtestf "a baseline naming an unknown tree was accepted: %s" line
                  | Error why -> Expect.stringContains why "does not hold" "and it says so"
              finally
                  deleteScratch root

          // ---- what the script hands the kit ----

          testCase
              "with neither switch the kit is handed the whole roster and the declared budget file: the full leg is the leg it was"
          <| fun _ ->
              let code, output = runCheckScript [ "-PlanOnly" ]
              Expect.equal code 0 (sprintf "-PlanOnly exits 0:\n%s" output)
              Expect.equal (plannedModules output) (realModules ()) "every registered module, in the roster's order"

              Expect.equal
                  (planLine "==== proofs: plan -- budget file" output)
                  "==== proofs: plan -- budget file: the declared proofs/modules.json"
                  "the budget file is the declared one, unfiltered"

          testCase "a hand cone is the named modules IN and what they reference as DEP, by -Since's closure (Phase 522)"
          <| fun _ ->
              // The toy tree: C opens B opens A; D stands alone.
              let rows = handCone (toyFacts []) [ "C" ]
              Expect.equal (withVerdict In rows) (set [ "C" ]) "the named module is in"

              Expect.equal
                  (withVerdict Dependency rows)
                  (set [ "A"; "B" ])
                  "and everything it references, transitively, as DEP"

              Expect.equal (coneChecked rows) [ "A"; "B"; "C" ] "handed to the kit in the roster's order"
              Expect.equal (withVerdict Out rows) (set [ "D" ]) "and nothing else"

              // The real tree, and the defect it closes: `-Modules Limits` used to hand the kit Limits
              // alone, and Limits' extraction failed as an APPARATUS fault because nothing had checked
              // the shared Twin it opens.
              let facts = realConeFacts []
              let real = handCone facts [ "Limits" ]
              Expect.equal (withVerdict In real) (set [ "Limits" ]) "Limits is in"

              Expect.isTrue
                  (Set.isSubset (facts.References |> Map.find "Limits") (withVerdict Dependency real))
                  "and every module it opens is checked ahead of it"

              Expect.isTrue (withVerdict Dependency real |> Set.contains "Twin") "the shared Twin among them"

              Expect.equal
                  (coneChecked real)
                  (facts.Roster
                   |> List.filter (fun m -> m = "Limits" || (withVerdict Dependency real).Contains m))
                  "in the roster's order, so every dependency is checked before the module that needs it"

          testCase
              "-Modules is answered by the cone tool; an unregistered name, or -Since and -Modules together, is refused"
          <| fun _ ->
              // The tool's answer, in-process: the same function `check.ps1 -Modules` asks.
              let out =
                  Path.Combine(Path.GetTempPath(), sprintf "hand-cone-%s.json" (Guid.NewGuid().ToString "N"))

              try
                  Expect.equal (coneCli [ "--modules"; "Chain,Limits"; "--out"; out ]) 0 "the tool answers"
                  use doc = JsonDocument.Parse(File.ReadAllText out)

                  Expect.equal
                      (doc.RootElement.GetProperty("verdict").GetString())
                      "verify"
                      "a registered hand cone verifies"

                  Expect.equal
                      [ for m in doc.RootElement.GetProperty("verify").EnumerateArray() -> m.GetString() ]
                      (coneChecked (handCone (realConeFacts []) [ "Chain"; "Limits" ]))
                      "the named modules and their closure"

                  Expect.equal (coneCli [ "--modules"; "NoSuchModel"; "--out"; out ]) 0 "the tool answers a refusal too"
                  use refused = JsonDocument.Parse(File.ReadAllText out)

                  Expect.equal
                      (refused.RootElement.GetProperty("verdict").GetString())
                      "refused"
                      "an unregistered name is refused"

                  Expect.stringContains
                      (refused.RootElement.GetProperty("message").GetString())
                      "NoSuchModel"
                      "and the refusal names it"
              finally
                  File.Delete out

              let script = File.ReadAllText checkScriptPath
              Expect.stringContains script "'--modules'" "check.ps1 asks the cone tool for a hand cone"

              let code, _ = runCheckScript [ "-Since"; "HEAD"; "-Modules"; "Chain"; "-PlanOnly" ]
              Expect.notEqual code 0 "a cone named two ways is refused, before anything is built" ]
