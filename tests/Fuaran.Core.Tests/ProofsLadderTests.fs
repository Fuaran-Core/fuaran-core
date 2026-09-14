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
// SIX CLAUSES, each with a go-red fixture beside it:
//
//   theorem-exists          a `proved` row's `evidence.theorem` is a TOP-LEVEL `val` / `let` /
//                           `let rec` of that name in the model it names.
//   model-registered        that model file exists, and its module is one `check.ps1` checks — a
//                           row over a model the leg never runs claims a proof nothing reproduces.
//   tested-case-exists      a `tested` row names at least one case, and every case it names is in
//                           the test tree. The citable set is the differential host `Proofs.Oracle`
//                           plus (Phase 161) the `containerLaws` conformance suite — see
//                           `realCases` for why it is an enumeration of suites and not a walk.
//   every-module-has-a-row  every module in `$modules` is named by at least one `proved` row.
//   phase-form              every row's `phase` is the `fuaran-core#NNN` form.
//   closed-level-set        every row's `level` is one of the closed four.
//
// WHAT IT DELIBERATELY IS NOT: a README parser. Prose-to-row agreement stays a human act; what is
// pinned here is ROW-TO-TREE agreement, which is the half that can be mechanical.
//
// The check is parametrised over its three inputs — the root a model path resolves against, the
// module list, and the case names — so the real file is ONE input and each fixture is another.
// That is not decoration: a fixture ladder measured against the REAL module list would go red the
// moment a sibling appended a model to `$modules`, and one measured against the REAL case names
// would go red on a renamed case. A go-red family has to stay green while the subject it is about
// changes, or it is not a go-red family for long. For the same reason the case names are read from
// the TEST TREE (`ProofOracleTests.proofOracleTests`) rather than by parsing that file's source,
// and the module list is read from `check.ps1`'s own text rather than restated here — a second
// copy of that list is precisely the drift this family exists to catch.

open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

// ---------------------------------------------------------------------------
//  The inputs a ladder is measured against
// ---------------------------------------------------------------------------

type LadderInputs =
    {
        /// The directory a row's `evidence.model` path resolves against.
        Root: string
        /// The modules the ladder must cover — `proofs/check.ps1`'s `$modules`, for the real file.
        Modules: string list
        /// The case names a `tested` row may name.
        Cases: Set<string>
    }

/// The closed level set, in ladder order (a message renders them in this order, not sorted).
let levelOrder = [ "proved"; "tested"; "assumed"; "policy" ]

let private levels = Set.ofList levelOrder

/// `fuaran-core#NNN` and nothing else — not a bare number, not another side's prefix, not a
/// leading zero.
let private phaseForm = Regex(@"^fuaran-core#[1-9][0-9]*$", RegexOptions.Compiled)

// ---------------------------------------------------------------------------
//  The clauses
// ---------------------------------------------------------------------------

let private strMember (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

let private objMember (el: JsonElement) (name: string) : JsonElement option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.Object -> Some v
    | _ -> None

let private strArrayMember (el: JsonElement) (name: string) : string list option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.Array ->
        v.EnumerateArray()
        |> Seq.map (fun x ->
            if x.ValueKind = JsonValueKind.String then
                x.GetString()
            else
                "")
        |> List.ofSeq
        |> Some
    | _ -> None

let private finding (row: string) (clause: string) (detail: string) =
    sprintf "row '%s' [%s]: %s" row clause detail

/// A TOP-LEVEL `val <name>`, `let <name>` or `let rec <name>` in an F* module's text — anchored at
/// column 0, so a binding indented inside a proof body does not answer for a row.
let declaresTopLevel (moduleText: string) (name: string) : bool =
    Regex.IsMatch(moduleText, @"^(?:val|let(?:\s+rec)?)\s+" + Regex.Escape name + @"\b", RegexOptions.Multiline)

/// Every finding the ladder text yields against these inputs. Empty is clean.
let checkLadder (inputs: LadderInputs) (ladderText: string) : string list =
    use doc = JsonDocument.Parse ladderText

    let claims =
        match doc.RootElement.TryGetProperty "claims" with
        | true, c when c.ValueKind = JsonValueKind.Array -> Some(c.EnumerateArray() |> List.ofSeq)
        | _ -> None

    match claims with
    // Short-circuit: every clause below quantifies over the rows, so a file with none would
    // otherwise read as a ladder that merely covers no module.
    | None -> [ "[shape]: the ladder carries no `claims` ARRAY — it is truncated or half-edited" ]
    | Some rows ->
        let idOf (i: int) (row: JsonElement) =
            strMember row "id" |> Option.defaultValue (sprintf "<no id, claims[%d]>" i)

        let moduleOf (path: string) = Path.GetFileNameWithoutExtension path
        let renderedLevels = String.concat " | " levelOrder

        let perRow =
            rows
            |> List.mapi (fun i row ->
                let id = idOf i row
                let level = strMember row "level"
                let ev = objMember row "evidence"

                let levelFindings =
                    match level with
                    | Some lvl when Set.contains lvl levels -> []
                    | Some lvl ->
                        [ finding
                              id
                              "closed-level-set"
                              (sprintf "level '%s' is outside the closed set %s" lvl renderedLevels) ]
                    | None ->
                        [ finding id "closed-level-set" (sprintf "no `level` member (one of %s)" renderedLevels) ]

                let phaseFindings =
                    match strMember row "phase" with
                    | Some p when phaseForm.IsMatch p -> []
                    | Some p -> [ finding id "phase-form" (sprintf "phase '%s' is not the `fuaran-core#NNN` form" p) ]
                    | None -> [ finding id "phase-form" "no `phase` member" ]

                let provedFindings =
                    if level <> Some "proved" then
                        []
                    else
                        let theorem = ev |> Option.bind (fun e -> strMember e "theorem")

                        match ev |> Option.bind (fun e -> strMember e "model") with
                        | None -> [ finding id "model-registered" "a proved row carries no `evidence.model`" ]
                        | Some path ->
                            let m = moduleOf path
                            let full = Path.Combine(inputs.Root, path)

                            let registered =
                                if List.contains m inputs.Modules then
                                    []
                                else
                                    [ finding
                                          id
                                          "model-registered"
                                          (sprintf
                                              "`evidence.model` is %s, but '%s' is not a module the proof leg checks (%s) — the row claims a proof nothing reproduces"
                                              path
                                              m
                                              (String.concat ", " inputs.Modules)) ]

                            let present =
                                if File.Exists full then
                                    []
                                else
                                    [ finding
                                          id
                                          "model-registered"
                                          (sprintf
                                              "`evidence.model` %s is not a file (looked under %s)"
                                              path
                                              inputs.Root) ]

                            let theoremFindings =
                                match theorem with
                                | None -> [ finding id "theorem-exists" "a proved row carries no `evidence.theorem`" ]
                                | Some name when File.Exists full ->
                                    if declaresTopLevel (File.ReadAllText full) name then
                                        []
                                    else
                                        [ finding
                                              id
                                              "theorem-exists"
                                              (sprintf
                                                  "%s declares no top-level `val %s` / `let %s` / `let rec %s`"
                                                  path
                                                  name
                                                  name
                                                  name) ]
                                // The absence of the file is already reported by model-registered;
                                // a second finding about the same absence would say nothing new.
                                | Some _ -> []

                            registered @ present @ theoremFindings

                let testedFindings =
                    if level <> Some "tested" then
                        []
                    else
                        match ev |> Option.bind (fun e -> strArrayMember e "tests") with
                        | None
                        | Some [] ->
                            [ finding
                                  id
                                  "tested-case-exists"
                                  "a tested row must name its cases in `evidence.tests` (a non-empty array of `Proofs.Oracle` case names) — naming the family in prose alone leaves the claim uncheckable" ]
                        | Some names ->
                            names
                            |> List.filter (fun n -> not (Set.contains n inputs.Cases))
                            |> List.map (fun n ->
                                finding
                                    id
                                    "tested-case-exists"
                                    (sprintf "`evidence.tests` names '%s', which is not a case in the test tree" n))

                levelFindings @ phaseFindings @ provedFindings @ testedFindings)
            |> List.concat

        let covered =
            rows
            |> List.filter (fun r -> strMember r "level" = Some "proved")
            |> List.choose (fun r -> objMember r "evidence" |> Option.bind (fun e -> strMember e "model"))
            |> List.map moduleOf
            |> Set.ofList

        let uncovered =
            inputs.Modules
            |> List.filter (fun m -> not (Set.contains m covered))
            |> List.map (fun m ->
                sprintf
                    "[every-module-has-a-row]: the proof leg checks '%s' and no `proved` row names it — a model whose claim was never written down"
                    m)

        perRow @ uncovered

// ---------------------------------------------------------------------------
//  Reading the two inputs out of the tree
// ---------------------------------------------------------------------------

/// `proofs/check.ps1`'s `$modules` list, parsed from the script's own text.
let parseModules (checkScript: string) : string list =
    let line =
        Regex.Match(checkScript, @"^\$modules\s*=\s*@\(([^)]*)\)", RegexOptions.Multiline)

    if not line.Success then
        []
    else
        Regex.Matches(line.Groups[1].Value, "['\"]([^'\"]*)['\"]")
        |> Seq.map (fun m -> m.Groups[1].Value)
        |> List.ofSeq

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

let private repoRoot = Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")
let private ladderPath = Path.Combine(repoRoot, "proofs.json")
let private checkScriptPath = Path.Combine(repoRoot, "proofs", "check.ps1")

let private fixtureDir =
    Path.Combine(repoRoot, "tests", "Fuaran.Core.Tests", "fixtures", "proofs-ladder")

let private realModules () =
    parseModules (File.ReadAllText checkScriptPath)

/// The case names a `tested` row may cite. `Proofs.Oracle` is where every differential row's
/// evidence lives, and it was the whole set until Phase 161 — whose `container-laws` row is
/// evidenced by a CONFORMANCE FAMILY rather than by a differential, and whose cases therefore live
/// in the suite that certifies that family. Read from the TREE in both cases, so a sibling
/// appending a case is picked up with no edit here.
///
/// Widening the set rather than moving the cases is deliberate: the clause's content is "a `tested`
/// row's evidence names a case that exists", and a row evidenced by a conformance family is as
/// checkable as one evidenced by a differential. What the clause must never become is a set so wide
/// that any string is in it — which is why this is an enumeration of suites and not a walk of the
/// whole tree.
let private realCases () =
    Set.union
        (caseNames ProofOracleTests.proofOracleTests |> Set.ofList)
        (caseNames ContainedOpsTests.containerLawTests |> Set.ofList)

/// A fixture ladder is measured against a FIXTURE module list and a FIXTURE case set, so nothing a
/// sibling adds to `$modules` or to `Proofs.Oracle` can move a go-red in either direction.
let private fixtureInputs (modules: string list) =
    { Root = fixtureDir
      Modules = modules
      Cases = Set.ofList [ "a fixture case" ] }

let private fixture (name: string) =
    File.ReadAllText(Path.Combine(fixtureDir, name))

/// A go-red: this fixture yields EXACTLY one finding, and it names the clause and the subject.
let private expectOneFinding (modules: string list) (name: string) (clause: string) (subject: string) =
    let findings = checkLadder (fixtureInputs modules) (fixture name)

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

[<Tests>]
let proofsLadderTests =
    testList
        "Proofs.Ladder"
        [

          // ---- the inputs are real, and non-vacuous ----

          testCase "check.ps1's $modules parses, and every module it names has a model file"
          <| fun _ ->
              let modules = realModules ()

              Expect.isNonEmpty
                  modules
                  (sprintf
                      "%s's `$modules` line did not parse — a module list read as EMPTY makes every-module-has-a-row silently vacuous, which is worse than a red"
                      checkScriptPath)

              for m in modules do
                  let model = Path.Combine(repoRoot, "proofs", m + ".fst")
                  Expect.isTrue (File.Exists model) (sprintf "the leg checks '%s' but %s is not there" m model)

          testCase "the case names a tested row may cite are readable from the test tree"
          <| fun _ ->
              // Read from the TREE, not from the source text, so a sibling appending a case is
              // picked up with no edit here. If this ever collapsed to a handful, the
              // tested-case-exists clause would have quietly stopped measuring anything.
              Expect.isGreaterThan
                  (Set.count (realCases ()))
                  20
                  "the cited suites' case names are readable, and there are as many as the differential host declares"

              // and BOTH suites are in it — a union that silently degenerated to one of its
              // members would leave the other's rows uncheckable while reporting nothing.
              Expect.isNonEmpty
                  (caseNames ProofOracleTests.proofOracleTests)
                  "the Proofs.Oracle differential host contributes cases"

              Expect.isNonEmpty
                  (caseNames ContainedOpsTests.containerLawTests)
                  "the containerLaws conformance suite contributes cases"

          // ---- the subject ----

          testCase "the committed proofs.json satisfies every clause"
          <| fun _ ->
              let text = File.ReadAllText ladderPath

              let inputs =
                  { Root = repoRoot
                    Modules = realModules ()
                    Cases = realCases () }

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

          testCase "go-red: shape — a ladder with no claims array is reported as that, and nothing else"
          <| fun _ ->
              let findings = checkLadder (fixtureInputs [ "Model" ]) (fixture "no-claims.json")

              Expect.equal (List.length findings) 1 (sprintf "one finding — got:\n%s" (String.concat "\n" findings))

              Expect.stringContains (List.head findings) "[shape]" "the finding names the shape clause" ]
