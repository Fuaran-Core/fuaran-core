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
// TEN CLAUSES, each with a go-red fixture beside it — six from Phase 144, four from Phase 174:
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
        /// The law names a `domain-obligation` row's `dischargedBy` may name — the public surface
        /// of the shipped `Fuaran.Core.Conformance` module, for the real file. Read from the
        /// ASSEMBLY for the same reason `Cases` is read from the test tree: a second copy of that
        /// list, restated here, is precisely the drift this family exists to catch.
        Laws: Set<string>
    }

/// The closed level set, in ladder order (a message renders them in this order, not sorted).
let levelOrder = [ "proved"; "tested"; "assumed"; "policy" ]

let private levels = Set.ofList levelOrder

/// `fuaran-core#NNN` and nothing else — not a bare number, not another side's prefix, not a
/// leading zero.
let private phaseForm = Regex(@"^fuaran-core#[1-9][0-9]*$", RegexOptions.Compiled)

/// The closed CLASS set an `assumed` row is drawn from (Phase 174), in contract order — a message
/// renders them in this order, not sorted. `level` says how strong a claim is; `class` says which
/// kind of ASSUMED an assumed row is, which is the question a domain instantiating this substrate
/// actually has: `domain-obligation` is what it owes and what a green kit run at its own witness
/// discharges, `model-bridge` is this repository's own model-to-production gap and is inherited,
/// `premise` is what nothing discharges at all.
let classOrder = [ "domain-obligation"; "model-bridge"; "premise" ]

let private classes = Set.ofList classOrder

/// `Conformance.<law>`, where `<law>` is a name the shipped kit exports. The prefix is required
/// rather than inferred: a bare `witnessLaws` would read as a law of some unnamed kit, and the
/// contract's whole content is WHICH kit's green run discharges the row.
let private lawForm =
    Regex(@"^Conformance\.([A-Za-z][A-Za-z0-9_']*)$", RegexOptions.Compiled)

/// The two literals a `model-bridge`'s `closes` may carry beside a `fuaran-core#NNN` phase.
/// `unscheduled` is a value rather than a rounding to `permanent` on purpose: two of this
/// repository's bridges CAN be closed and nobody has taken the work, and recording those as
/// `permanent` would assert an impossibility the README's own prose contradicts.
let closesLiterals = [ "permanent"; "unscheduled" ]

/// The em dash a `premise` row carries in the contract table's third column — it has neither a
/// discharging law nor a closing phase, and an empty cell would not say which.
let private noThirdColumn = "—"

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

                // ---- Phase 174: which KIND of assumed an `assumed` row is ----
                let cls = strMember row "class"
                let dischargedBy = strMember row "dischargedBy"
                let closes = strMember row "closes"

                let misplaced (clause: string) (name: string) (value: string option) (why: string) =
                    match value with
                    | Some v -> [ finding id clause (sprintf "carries `%s`: \"%s\" — %s" name v why) ]
                    | None -> []

                let renderedClasses = String.concat " | " classOrder
                let renderedCloses = String.concat " | " closesLiterals

                let classFindings =
                    if level <> Some "assumed" then
                        // The classification is about the assumed rows and only those: a `class` on
                        // a proved row would be answering a question that row does not raise.
                        misplaced
                            "assumed-class"
                            "class"
                            cls
                            (sprintf
                                "`class` says which kind of ASSUMED a row is, and this row is at level %s"
                                (Option.defaultValue "<none>" level))
                        @ misplaced
                            "assumed-class"
                            "dischargedBy"
                            dischargedBy
                            "only a `domain-obligation` names a discharging law"
                        @ misplaced "assumed-class" "closes" closes "only a `model-bridge` names a closing phase"
                    else
                        match cls with
                        | None ->
                            [ finding
                                  id
                                  "assumed-class"
                                  (sprintf
                                      "an `assumed` row carries no `class` (one of %s) — a domain cannot tell what it owes from what it inherits"
                                      renderedClasses) ]
                        | Some c when not (Set.contains c classes) ->
                            [ finding
                                  id
                                  "assumed-class"
                                  (sprintf "class '%s' is outside the closed set %s" c renderedClasses) ]
                        | Some "domain-obligation" ->
                            let lawFindings =
                                match dischargedBy with
                                | None ->
                                    [ finding
                                          id
                                          "obligation-law"
                                          "a `domain-obligation` carries no `dischargedBy` — an obligation with no named law is one a domain has no way to discharge" ]
                                | Some d ->
                                    let m = lawForm.Match d

                                    if not m.Success then
                                        [ finding
                                              id
                                              "obligation-law"
                                              (sprintf "`dischargedBy` '%s' is not the `Conformance.<law>` form" d) ]
                                    elif not (Set.contains m.Groups[1].Value inputs.Laws) then
                                        [ finding
                                              id
                                              "obligation-law"
                                              (sprintf
                                                  "`dischargedBy` names '%s', which the shipped `Conformance` kit does not export — the row cites a run a domain cannot make"
                                                  d) ]
                                    else
                                        []

                            lawFindings
                            @ misplaced
                                "obligation-law"
                                "closes"
                                closes
                                "a domain obligation is discharged by a law, not closed by a phase"
                        | Some "model-bridge" ->
                            let closesFindings =
                                match closes with
                                | None ->
                                    [ finding
                                          id
                                          "bridge-closes"
                                          (sprintf
                                              "a `model-bridge` carries no `closes` (a `fuaran-core#NNN` phase, or one of %s)"
                                              renderedCloses) ]
                                | Some cl when List.contains cl closesLiterals || phaseForm.IsMatch cl -> []
                                | Some cl ->
                                    [ finding
                                          id
                                          "bridge-closes"
                                          (sprintf
                                              "`closes` '%s' is neither a `fuaran-core#NNN` phase nor one of %s"
                                              cl
                                              renderedCloses) ]

                            closesFindings
                            @ misplaced
                                "bridge-closes"
                                "dischargedBy"
                                dischargedBy
                                "no law a domain runs closes a gap between this repository's model and its own production code"
                        | Some _ ->
                            // `premise`: nothing discharges it and no phase closes it, so either
                            // field on such a row is a citation to something that does not exist.
                            misplaced
                                "assumed-class"
                                "dischargedBy"
                                dischargedBy
                                "a `premise` is discharged by nothing"
                            @ misplaced "assumed-class" "closes" closes "a `premise` is closed by nothing"

                levelFindings @ phaseFindings @ provedFindings @ testedFindings @ classFindings)
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
//  The contract clause — the ONE piece of prose this family parses (Phase 174)
// ---------------------------------------------------------------------------
//
// The note at the head of this file says what it deliberately is not: a README parser. That stands
// with exactly one exception, taken here and nowhere else. `proofs/README.md`'s "Core-to-domain
// proof contract" section is the table a DOMAIN reads to learn what it owes; `proofs.json` is the
// table a TOOL reads for the same question. Two answers to one question that can silently disagree
// are worse than either alone, so this one table is held to the ladder row for row. Everything else
// in that document — including whether a row's prose says what its `claim` says — stays a human
// act, which is the half a check cannot settle.

let contractHeading = "## The Core-to-domain proof contract"

/// A row of the contract table: `` | `id` | `class` | third | ``, where the third cell is a
/// backticked law or `closes` value, or the em dash a `premise` carries.
let private contractRowForm =
    Regex(@"^\|\s*`([^`]+)`\s*\|\s*`([^`]+)`\s*\|\s*(.+?)\s*\|\s*$", RegexOptions.Compiled)

/// The contract table's rows, in document order. `None` means the section is not there at all,
/// which is a different finding from a section whose table disagrees.
let contractRows (readmeText: string) : (string * string * string) list option =
    let lines = readmeText.Replace("\r\n", "\n").Split('\n')

    match lines |> Array.tryFindIndex (fun l -> l.TrimEnd() = contractHeading) with
    | None -> None
    | Some start ->
        let body =
            lines
            |> Array.skip (start + 1)
            |> Array.takeWhile (fun l -> not (l.StartsWith "## "))

        body
        |> Array.choose (fun l ->
            let m = contractRowForm.Match(l.Trim())

            if m.Success && Set.contains m.Groups[2].Value classes then
                Some(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)
            else
                None)
        |> List.ofArray
        |> Some

/// What the contract table must say, derived from the ladder: one entry per `assumed` row, in the
/// ladder's own order, third column being the discharging law, the closing phase, or the em dash.
let contractFromLadder (ladderText: string) : (string * string * string) list =
    use doc = JsonDocument.Parse ladderText

    match doc.RootElement.TryGetProperty "claims" with
    | true, c when c.ValueKind = JsonValueKind.Array ->
        c.EnumerateArray()
        |> Seq.filter (fun r -> strMember r "level" = Some "assumed")
        |> Seq.map (fun r ->
            let id = strMember r "id" |> Option.defaultValue "<no id>"

            let third =
                match strMember r "dischargedBy", strMember r "closes" with
                | Some d, _ -> sprintf "`%s`" d
                | _, Some cl -> sprintf "`%s`" cl
                | None, None -> noThirdColumn

            id, (strMember r "class" |> Option.defaultValue "<no class>"), third)
        |> List.ofSeq
    | _ -> []

/// Every disagreement between the ladder and the contract table. Empty is clean.
let checkContract (ladderText: string) (readmeText: string) : string list =
    let clause = "contract-agrees"
    let expected = contractFromLadder ladderText

    match contractRows readmeText with
    | None ->
        [ sprintf
              "[%s]: the README carries no `%s` section — the ladder's %d assumed rows are classified in a file no domain reads"
              clause
              contractHeading
              (List.length expected) ]
    | Some actual ->
        let key (id, _, _) = id
        let byId = actual |> List.map (fun r -> key r, r) |> Map.ofList

        let missing =
            expected
            |> List.filter (fun r -> not (Map.containsKey (key r) byId))
            |> List.map (fun (id, cls, _) ->
                sprintf
                    "[%s]: the ladder classifies '%s' as `%s` and the contract table has no row for it"
                    clause
                    id
                    cls)

        let expectedIds = expected |> List.map key |> Set.ofList

        let extra =
            actual
            |> List.filter (fun r -> not (Set.contains (key r) expectedIds))
            |> List.map (fun (id, _, _) ->
                sprintf
                    "[%s]: the contract table carries a row for '%s', which is not an `assumed` row of the ladder"
                    clause
                    id)

        let mismatched =
            expected
            |> List.choose (fun (id, cls, third) ->
                match Map.tryFind id byId with
                | Some(_, aCls, aThird) when aCls <> cls || aThird <> third ->
                    Some(
                        sprintf
                            "[%s]: '%s' is `%s` / %s in the ladder and `%s` / %s in the contract table"
                            clause
                            id
                            cls
                            third
                            aCls
                            aThird
                    )
                | _ -> None)

        let disagreements = missing @ extra @ mismatched

        if not (List.isEmpty disagreements) then
            disagreements
        elif List.map key expected <> List.map key actual then
            // Same rows, different order. Reported once and last: an order finding on top of a
            // missing-row finding would be one defect rendered twice.
            [ sprintf
                  "[%s]: the contract table carries the ladder's rows in a different order — ladder: %s; table: %s"
                  clause
                  (String.concat ", " (List.map key expected))
                  (String.concat ", " (List.map key actual)) ]
        else
            []

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
let private proofsReadmePath = Path.Combine(repoRoot, "proofs", "README.md")

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

/// The law names a `domain-obligation` row may cite — the public surface of the shipped
/// `Fuaran.Core.Conformance` module, read from the ASSEMBLY rather than restated here. An F#
/// module compiles to a static class, so its public let-bound functions are that type's public
/// static methods; a law renamed in the kit therefore stops answering for a row with no edit in
/// this file, which is the drift the clause is for.
let private realLaws () =
    let asm = typeof<Fuaran.Core.LawResult>.Assembly

    match asm.GetType "Fuaran.Core.Conformance" with
    | null -> Set.empty
    | t ->
        t.GetMethods(System.Reflection.BindingFlags.Public ||| System.Reflection.BindingFlags.Static)
        |> Seq.map (fun m -> m.Name)
        |> Set.ofSeq

/// A fixture ladder is measured against a FIXTURE module list, a FIXTURE case set and a FIXTURE law
/// set, so nothing a sibling adds to `$modules`, to `Proofs.Oracle` or to the conformance kit can
/// move a go-red in either direction.
let private fixtureInputs (modules: string list) =
    { Root = fixtureDir
      Modules = modules
      Cases = Set.ofList [ "a fixture case" ]
      Laws = Set.ofList [ "fixtureLaws" ] }

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

          testCase "the law names a domain-obligation row may cite are readable from the assembly"
          <| fun _ ->
              let laws = realLaws ()

              // A reflection lookup that found the wrong type, or no type, returns an EMPTY set —
              // under which obligation-law would report every row as dangling rather than going
              // quietly vacuous. It is asserted anyway: a clause whose input silently collapsed is
              // the failure this family was cut to catch, in whichever direction it points.
              Expect.isGreaterThan
                  (Set.count laws)
                  20
                  "the shipped `Conformance` module's public static surface is readable, and is the kit's law set"

              Expect.isTrue
                  (Set.contains "witnessLaws" laws)
                  "`witnessLaws` is in it — the one law `proofs.json`'s own claim text names in prose"

          // ---- the subject ----

          testCase "the committed proofs.json satisfies every clause"
          <| fun _ ->
              let text = File.ReadAllText ladderPath

              let inputs =
                  { Root = repoRoot
                    Modules = realModules ()
                    Cases = realCases ()
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
