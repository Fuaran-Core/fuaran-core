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

open System
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
        /// The law names a `domain-obligation` row's `dischargedBy` may name — the entry points of
        /// the shipped kit's declared law-family ROSTER, for the real file. Read from
        /// `Fuaran.Core.Families` for the same reason `Cases` is read from the test tree: a second
        /// copy of that list, restated here, is precisely the drift this family exists to catch.
        /// Phase 184 narrowed it from the `Conformance` module's whole public static surface, so
        /// an obligation can no longer name a law that no conformance census enumerates.
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
                        match ev |> Option.bind (fun e -> strArrayMember e "cases") with
                        | None
                        | Some [] ->
                            [ finding
                                  id
                                  "tested-case-exists"
                                  "a tested row must name its cases in `evidence.cases` (a non-empty array of `Proofs.Oracle` case names) — naming the family in prose alone leaves the claim uncheckable" ]
                        | Some names ->
                            names
                            |> List.filter (fun n -> not (Set.contains n inputs.Cases))
                            |> List.map (fun n ->
                                finding
                                    id
                                    "tested-case-exists"
                                    (sprintf "`evidence.cases` names '%s', which is not a case in the test tree" n))

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

                // ---- Phase 309: an obligation its law checks only against the domain's own word ----
                // `"discharge": "domain-declared"` marks a `domain-obligation` whose named law can
                // check the domain's DECLARATION and cannot check past it (a domain that declares
                // nothing passes it). One form, and only on the class it qualifies.
                let dischargeFindings =
                    match strMember row "discharge" with
                    | None -> []
                    | Some "domain-declared" when level = Some "assumed" && cls = Some "domain-obligation" -> []
                    | Some "domain-declared" ->
                        [ finding
                              id
                              "assumed-class"
                              "carries `discharge`: \"domain-declared\" — only an assumed `domain-obligation` is checked against a domain's own declaration" ]
                    | Some v ->
                        [ finding
                              id
                              "assumed-class"
                              (sprintf "`discharge` is \"%s\"; its one form is `domain-declared` (Phase 309)" v) ]

                levelFindings
                @ phaseFindings
                @ provedFindings
                @ testedFindings
                @ classFindings
                @ dischargeFindings)
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
                | Some d, _ when strMember r "discharge" = Some "domain-declared" ->
                    // Phase 309 — the law is named, and so is the limit of what its run says.
                    sprintf "`%s` (domain-declared, not discharged)" d
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
//  Phase 309 — the README's numbers and contract table, REGENERATED from the ladder
//
//  The header used to say "eight in all" over a ladder that had long since grown past it, and the
//  contract section's counts and table were held to `proofs.json` by checks that could only say
//  they were wrong. The three are now a PROJECTION: `regenerateReadme` renders the header's summary
//  block, the contract table's rows and the contract prose's four counts from the ladder, the
//  family below holds the committed README to that rendering, and `CORE_APPROVE_LADDER=1` writes it
//  — the same idiom as the API baselines. Prose around the blocks is untouched: what a number says
//  is generated, what it means is written.
// ---------------------------------------------------------------------------

let ladderSummaryBegin =
    "<!-- ladder-summary:begin — generated from ../proofs.json by the Proofs.Ladder family; CORE_APPROVE_LADDER=1 rewrites it -->"

let ladderSummaryEnd = "<!-- ladder-summary:end -->"

/// The header's summary line: every level counted, the proved rows' models counted, and the
/// assumed rows by class.
let renderLadderSummary (ladderText: string) : string =
    use doc = JsonDocument.Parse ladderText

    let rows =
        match doc.RootElement.TryGetProperty "claims" with
        | true, c when c.ValueKind = JsonValueKind.Array -> c.EnumerateArray() |> List.ofSeq
        | _ -> []

    let at level =
        rows |> List.filter (fun r -> strMember r "level" = Some level)

    let assumedIn cls =
        at "assumed"
        |> List.filter (fun r -> strMember r "class" = Some cls)
        |> List.length

    let provedModels =
        at "proved"
        |> List.choose (fun r ->
            match r.TryGetProperty "evidence" with
            | true, e -> strMember e "model"
            | _ -> None)
        |> List.distinct
        |> List.length

    sprintf
        "**The ladder, counted:** %d claims — %d proved across %d models, %d tested, %d assumed (%d `domain-obligation`, %d `model-bridge`, %d `premise`), %d policy."
        (List.length rows)
        (List.length (at "proved"))
        provedModels
        (List.length (at "tested"))
        (List.length (at "assumed"))
        (assumedIn "domain-obligation")
        (assumedIn "model-bridge")
        (assumedIn "premise")
        (List.length (at "policy"))

/// The README as the ladder says it must read: the summary block, the contract table's rows and the
/// contract prose's counts replaced; everything else byte for byte. `Error` names what could not be
/// located, because a block that cannot be found is a block that is no longer generated.
let regenerateReadme (ladderText: string) (readmeText: string) : Result<string, string> =
    let nl = if readmeText.Contains "\r\n" then "\r\n" else "\n"
    let lines = readmeText.Replace("\r\n", "\n").Split('\n') |> List.ofArray

    // 1. the summary block
    let summaryBegin = lines |> List.tryFindIndex (fun l -> l = ladderSummaryBegin)

    let summaryEnd = lines |> List.tryFindIndex (fun l -> l = ladderSummaryEnd)

    match summaryBegin, summaryEnd with
    | Some b, Some e when e > b ->
        let lines = lines[..b] @ [ renderLadderSummary ladderText ] @ lines[e..]

        // 2. the contract table's rows: every `| \`id\` | ...` line in the contract section
        match lines |> List.tryFindIndex (fun l -> l.TrimEnd() = contractHeading) with
        | None -> Error(sprintf "the README carries no `%s` section" contractHeading)
        | Some start ->
            let sectionEnd =
                lines
                |> List.indexed
                |> List.tryFind (fun (i, l) -> i > start && l.StartsWith "## ")
                |> Option.map fst
                |> Option.defaultValue (List.length lines)

            let isRow (l: string) =
                let m = contractRowForm.Match(l.Trim())
                m.Success && Set.contains m.Groups[2].Value classes

            let rowIdx = [ start .. sectionEnd - 1 ] |> List.filter (fun i -> isRow lines[i])

            match rowIdx with
            | [] -> Error "the contract table carries no row this family can parse"
            | first :: _ ->
                let last = List.last rowIdx

                let rendered =
                    contractFromLadder ladderText
                    |> List.map (fun (id, cls, third) -> sprintf "| `%s` | `%s` | %s |" id cls third)

                let lines = lines[.. first - 1] @ rendered @ lines[last + 1 ..]
                let text = String.Join("\n", lines)

                // 3. the contract prose's counts, inside the section only
                let head = text.IndexOf(contractHeading, StringComparison.Ordinal)

                let next =
                    text.IndexOf("\n## ", head + contractHeading.Length, StringComparison.Ordinal)

                let stop = if next < 0 then text.Length else next
                let section = text.Substring(head, stop - head)

                use doc = JsonDocument.Parse ladderText

                let assumed =
                    doc.RootElement.GetProperty("claims").EnumerateArray()
                    |> Seq.filter (fun r -> strMember r "level" = Some "assumed")
                    |> List.ofSeq

                let count cls =
                    assumed |> List.filter (fun r -> strMember r "class" = Some cls) |> List.length

                let section' =
                    let s =
                        Regex.Replace(
                            section,
                            @"the \d+ assumed rows",
                            sprintf "the %d assumed rows" (List.length assumed)
                        )

                    classOrder
                    |> List.fold
                        (fun (acc: string) cls ->
                            let rx =
                                Regex(
                                    sprintf @"(\*\*`%s`.*?)(\d+)( rows\.)" (Regex.Escape cls),
                                    RegexOptions.Singleline
                                )

                            rx.Replace(
                                acc,
                                (fun (m: Match) -> m.Groups[1].Value + string (count cls) + m.Groups[3].Value),
                                1
                            ))
                        s

                Ok((text.Substring(0, head) + section' + text.Substring(stop)).Replace("\n", nl))
    | _ -> Error(sprintf "the README carries no `%s` … `%s` block" ladderSummaryBegin ladderSummaryEnd)

// ---------------------------------------------------------------------------
//  Reading the two inputs out of the tree
// ---------------------------------------------------------------------------

/// One of `proofs/check.ps1`'s one-line list literals (`$<name> = @('A', 'B')`), parsed from the
/// script's own text. `parseModules` is this at `modules`; the Phase 328 cone selector also reads
/// `$proofOnly` through it, so the ladder and the selector share ONE reading of the script.
let parseScriptList (name: string) (checkScript: string) : string list =
    let line =
        Regex.Match(checkScript, @"^\$" + Regex.Escape name + @"\s*=\s*@\(([^)]*)\)", RegexOptions.Multiline)

    if not line.Success then
        []
    else
        Regex.Matches(line.Groups[1].Value, "['\"]([^'\"]*)['\"]")
        |> Seq.map (fun m -> m.Groups[1].Value)
        |> List.ofSeq

/// `proofs/check.ps1`'s `$modules` list, parsed from the script's own text.
let parseModules (checkScript: string) : string list = parseScriptList "modules" checkScript

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
/// whole tree. Phase 288 adds the second conformance suite on the same footing: the checkpoint rows
/// are evidenced by `Conformance.checkpointLaws`, whose cases live in `CheckpointTests`.
let private realCases () =
    Set.union
        (caseNames ProofOracleTests.proofOracleTests |> Set.ofList)
        (caseNames ContainedOpsTests.containerLawTests |> Set.ofList)
    |> Set.union (caseNames CheckpointTests.checkpointLawTests |> Set.ofList)
    |> Set.union (caseNames ProofOracleCompactedTests.compactedOracleTests |> Set.ofList)
    |> Set.union (caseNames ProofOracleCompactedTests.captureOracleTests |> Set.ofList)
    // Phase 304: the old-document evolution differential, evidenced by the hosts that decode old
    // documents rather than by the oracle.
    |> Set.union (caseNames IdlStabilityClassTests.evolutionDifferential |> Set.ofList)
    // Phase 311: the lane store's tested row, evidenced by `Conformance.laneLaws`, whose cases live in
    // `LaneTests`.
    |> Set.union (caseNames LaneTests.laneLawTests |> Set.ofList)

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
    |> List.map (fun f -> f.Entry)
    |> Set.ofList

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

          testCase "the twin roster runs exactly the models check.ps1 extracts (Phase 309)"
          <| fun _ ->
              // The host half of twin evaluation's coverage. The kit's TWIN step holds each extracted
              // model's SOURCE to declaring `twins`; this holds the HOST to running them — a model
              // whose fixtures the normaliser certified and nothing ran would read as covered. Both
              // directions: an extracted model the roster misses, and a roster entry for a model the
              // leg no longer extracts.
              let script = File.ReadAllText checkScriptPath
              let proofOnly = parseScriptList "proofOnly" script |> Set.ofList

              let extracted =
                  parseModules script
                  |> List.filter (fun m -> not (Set.contains m proofOnly))
                  |> Set.ofList

              let rostered = ProofOracleTests.twinRoster |> List.map fst |> Set.ofList

              Expect.isNonEmpty extracted "check.ps1's extracted set parses"

              Expect.equal
                  rostered
                  extracted
                  (sprintf
                      "the twin roster and the extracted set differ — unrun: %A; stale: %A"
                      (Set.difference extracted rostered)
                      (Set.difference rostered extracted))

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

          testCase
              "proofs/README.md's counts and contract table are the ladder's projection (CORE_APPROVE_LADDER=1 regenerates them)"
          <| fun _ ->
              // Phase 309 — generated, not hand-kept. Red here means the README's summary block,
              // contract rows or contract counts are not what `proofs.json` renders to; with
              // CORE_APPROVE_LADDER=1 the family writes the rendering instead (commit the result).
              let ladderText = File.ReadAllText ladderPath
              let readmeText = File.ReadAllText proofsReadmePath

              match regenerateReadme ladderText readmeText with
              | Error why -> failtestf "the README's generated blocks could not be located: %s" why
              | Ok expected when expected = readmeText -> ()
              | Ok expected when Approval.admits Approval.Ladder Approval.Ladders.Summary ->
                  Approval.write Approval.Ladder Approval.Ladders.Summary proofsReadmePath expected
                  |> ignore
              | Ok expected ->
                  let a = readmeText.Replace("\r\n", "\n").Split('\n')
                  let b = expected.Replace("\r\n", "\n").Split('\n')

                  let firstDiff =
                      Seq.zip a b
                      |> Seq.tryFindIndex (fun (x, y) -> x <> y)
                      |> Option.defaultValue (min a.Length b.Length)

                  failtestf
                      "proofs/README.md is not the ladder's projection — first differing line %d:\n  committed: %s\n  generated: %s\nRe-run with CORE_APPROVE_LADDER=1 and commit the README."
                      (firstDiff + 1)
                      (if firstDiff < a.Length then a[firstDiff] else "<end>")
                      (if firstDiff < b.Length then b[firstDiff] else "<end>")

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
                  "fold_confluence {{APPLY}} {{FOOTPRINT}} s0 ls1 ls2 p"
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

// ===========================================================================
//  Phase 328 — the module-cone selector
// ===========================================================================
//
// `proofs/check.ps1 -Since <tree>` verifies, cold, only the models whose inputs changed against a
// named tree. This section is the half of it that DECIDES — which registered modules are in the
// cone, and why; `check.ps1` builds this project, calls it through `--proof-cone` (Program.fs) and
// hands the kit exactly the modules named here. The kit, which runs the prover, is not touched.
//
// It lives beside `parseModules` for the reason the phase names: the roster is read by the SAME
// function the ladder reads it with, so the ladder and the selector cannot disagree about which
// models exist. A second roster parser in the script would be a second answer to drift.
//
// SOUNDNESS IS THE WHOLE OF ITS CORRECTNESS. A module is IN whenever something its verdict reads
// changed:
//   * its model `proofs/<M>.fst` (or `.fsti`) — and then every module that REFERENCES it, however
//     transitively, because a module's check re-reads everything it references;
//   * its committed oracle `proofs/oracle/<M>.fs`, which the extraction diff reads;
//   * a production source under `src/<Package>/` for a package its `proofs/modules.json` entry
//     names (Phase 203's `packages` — the map from subject to model lives THERE; `proofs.json`'s
//     rows name models and theorems, never a source path);
//   * its `modules.json` entry (the floor there is a gate), its registration in `$modules`, or its
//     membership of `$proofOnly`.
// And EVERY module is in when a shared input of the whole leg moved: the prover pin, the kit's
// engine or post-pass, `check.ps1`'s CODE (an edit to its comments alone is not a code change), an
// unregistered model, or any path under `proofs/` this section does not classify. The default for
// an unknown input is "it matters", because a cone that missed one would pass stale evidence — the
// one outcome Phase 164 exists to prevent.
//
// "References" is read, never guessed, and it is WIDER than `open`: F* also resolves a module named
// by an abbreviation (`module JP = JsonParse`, which is how WireCanon reaches JsonParse) or by a
// qualified name, with no `open` anywhere. So an edge is every `open` / `include` / `friend`, every
// abbreviation and every qualified `M.x`, read from the model's CODE — comments and string literals
// are blanked first, since these models name each other constantly in prose. The reading
// over-approximates F*'s own dependency scan, which can cost prover time and never soundness.
//
// A module the cone's members reference, whose own inputs did not change, is not IN: it is listed
// as a DEPENDENCY and checked ahead of them. On a cold cache the prover must check it anyway before
// it can check the module that references it; listing it keeps every module's clock its own, so a
// budget and a floor are compared against the module they were seeded for.
//
// AN EMPTY CONE is green only over a recorded strict run: `proofs/last-strict.json` names the tree
// the last green `-Strict` full leg verified, that tree is an ancestor of HEAD, and the cone
// against IT is empty too — so an empty cone since some other tree cannot lean on a baseline that
// predates a change nobody verified. A green `-Strict` full run records the file, and refuses to
// when the working tree differs from HEAD in anything a module reads, because then the run
// verified bytes no commit holds.

/// Where the last green `-Strict` full run is recorded, relative to the repository root.
let coneBaselinePath = "proofs/last-strict.json"

/// The leg's own scripts: a change to their CODE moves every module's verdict.
let legScripts =
    [ "proofs/check.ps1"
      "proofs/kit/check-proof-leg.ps1"
      "proofs/kit/extraction-post-pass.ps1" ]

/// An F* source with every comment and string literal blanked (newlines kept), so that a module
/// name in prose is never read as a reference. Block comments nest, as F*'s do.
let blankFStarNonCode (source: string) : string =
    let sb = System.Text.StringBuilder(source.Length)
    let n = source.Length
    let at k = if k < n then source[k] else '\000'

    let identChar (c: char) =
        Char.IsLetterOrDigit c || c = '_' || c = '\''

    let freeQuote k = k = 0 || not (identChar source[k - 1])
    let mutable i = 0
    let mutable depth = 0

    while i < n do
        let c = source[i]

        if depth > 0 then
            if c = '(' && at (i + 1) = '*' then
                depth <- depth + 1
                i <- i + 2
            elif c = '*' && at (i + 1) = ')' then
                depth <- depth - 1
                i <- i + 2
            else
                if c = '\n' then
                    sb.Append '\n' |> ignore

                i <- i + 1
        elif c = '(' && at (i + 1) = '*' then
            depth <- 1
            sb.Append ' ' |> ignore
            i <- i + 2
        elif c = '/' && at (i + 1) = '/' then
            while i < n && source[i] <> '\n' do
                i <- i + 1
        elif c = '"' then
            sb.Append ' ' |> ignore
            i <- i + 1

            while i < n && source[i] <> '"' do
                if source[i] = '\\' then
                    i <- i + 1
                elif source[i] = '\n' then
                    sb.Append '\n' |> ignore

                i <- i + 1

            i <- i + 1
        elif c = '\'' && freeQuote i && at (i + 1) <> '\\' && at (i + 2) = '\'' then
            // A character literal such as '"', which would otherwise open a string.
            sb.Append ' ' |> ignore
            i <- i + 3
        elif c = '\'' && freeQuote i && at (i + 1) = '\\' && at (i + 3) = '\'' then
            sb.Append ' ' |> ignore
            i <- i + 4
        else
            sb.Append c |> ignore
            i <- i + 1

    sb.ToString()

let private declaredReference =
    Regex(@"\b(?:open|include|friend)\s+([A-Z][\w']*)", RegexOptions.Compiled)

let private abbreviationReference =
    Regex(@"\bmodule\s+[A-Z][\w']*\s*=\s*([A-Z][\w']*)", RegexOptions.Compiled)

let private qualifiedReference =
    Regex(@"(?<![\w.'])([A-Z][\w']*)\.(?=[\w(])", RegexOptions.Compiled)

/// The registered modules a model's CODE references — by `open` / `include` / `friend`, by a
/// module abbreviation, or by a qualified name — never counting itself.
let modelReferences (roster: string list) (self: string) (source: string) : Set<string> =
    let code = blankFStarNonCode source
    let registered = Set.ofList roster

    [ for m in declaredReference.Matches code -> m.Groups[1].Value
      for m in abbreviationReference.Matches code -> m.Groups[1].Value
      for m in qualifiedReference.Matches code -> m.Groups[1].Value ]
    |> List.filter (fun name -> name <> self && registered.Contains name)
    |> Set.ofList

/// What a changed path is to the proof leg.
type ConeInput =
    /// `proofs/<M>.fst` or `.fsti` of a registered module.
    | ModelSource of string
    /// `proofs/oracle/<M>.fs` of a registered module.
    | OracleSource of string
    /// A path under `src/<Package>/`.
    | CoveredSource of package: string
    /// One of `legScripts` — whether its CODE moved is a separate question.
    | LegScript
    /// `proofs/modules.json` — which ENTRIES moved is a separate question.
    | BudgetDeclaration
    /// Something every module's check reads.
    | SharedInput
    /// Nothing the prover or the extraction reads.
    | NotAnInput

/// Classify a repository-relative path. Anything under `proofs/` this does not recognise is a
/// SHARED input — the sound default, stated here so that nobody mistakes it for an oversight.
/// The oracle project's own files (`Prims.fs`, the `.fsproj`) are not inputs: the prover never
/// reads them, and the host step that does is never narrowed.
let classifyConePath (roster: string list) (path: string) : ConeInput =
    let p = path.Replace('\\', '/')
    let segments = p.Split '/'
    let registered name = List.contains name roster

    if p.StartsWith "src/" then
        if segments.Length >= 3 then
            CoveredSource segments[1]
        else
            NotAnInput
    elif not (p.StartsWith "proofs/") then
        NotAnInput
    elif List.contains p legScripts then
        LegScript
    elif p = "proofs/modules.json" then
        BudgetDeclaration
    elif p = "proofs/fstar-pin.json" then
        SharedInput
    elif segments.Length = 2 && (p.EndsWith ".fst" || p.EndsWith ".fsti") then
        let name = Path.GetFileNameWithoutExtension segments[1]
        if registered name then ModelSource name else SharedInput
    elif segments.Length >= 3 && segments[1] = "oracle" then
        let name = Path.GetFileNameWithoutExtension segments[segments.Length - 1]

        if segments.Length = 3 && p.EndsWith ".fs" && registered name then
            OracleSource name
        else
            NotAnInput
    elif
        p.EndsWith ".md"
        || p.StartsWith "proofs/kit/templates/"
        || p.EndsWith ".tests.ps1"
        || List.contains
            p
            [ "proofs/kit/measure-relocation-halts.ps1"
              "proofs/coverage-exclusions.json"
              coneBaselinePath ]
    then
        NotAnInput
    else
        SharedInput

/// A leg script's CODE: its text without full-line `#` comments, blank lines and trailing blanks,
/// and without `check.ps1`'s `$modules` / `$proofOnly` literals, whose changes are read member by
/// member instead. Two versions with equal code differ in prose only.
let scriptCode (text: string) : string =
    text.Replace("\r\n", "\n").Split '\n'
    |> Array.map (fun l -> l.TrimEnd())
    |> Array.filter (fun l ->
        let t = l.TrimStart()

        t <> ""
        && not (t.StartsWith "#")
        && not (Regex.IsMatch(l, @"^\$(modules|proofOnly)\s*=\s*@\(")))
    |> String.concat "\n"

let private budgetEntries (text: string) : Map<string, JsonElement> =
    if String.IsNullOrWhiteSpace text then
        Map.empty
    else
        try
            use doc = JsonDocument.Parse text

            match doc.RootElement.TryGetProperty "modules" with
            | true, arr when arr.ValueKind = JsonValueKind.Array ->
                arr.EnumerateArray()
                |> Seq.choose (fun el ->
                    match el.TryGetProperty "module" with
                    | true, m when m.ValueKind = JsonValueKind.String -> Some(m.GetString(), el.Clone())
                    | _ -> None)
                |> Map.ofSeq
            | _ -> Map.empty
        with :? JsonException ->
            Map.empty

/// The modules whose `modules.json` ENTRY differs between two versions of the file (an entry on
/// one side only counts). Compared as JSON, so re-indenting moves nothing. The blocks outside
/// `modules` — the seeding rules and the contention threshold — are prose and a label on cost
/// findings, never an input to a verdict, and are not read.
let budgetEntriesChanged (oldText: string) (newText: string) : Set<string> =
    let before =
        budgetEntries oldText |> Map.map (fun _ el -> JsonSerializer.Serialize el)

    let after =
        budgetEntries newText |> Map.map (fun _ el -> JsonSerializer.Serialize el)

    Set.union (before |> Map.keys |> Set.ofSeq) (after |> Map.keys |> Set.ofSeq)
    |> Set.filter (fun m -> Map.tryFind m before <> Map.tryFind m after)

/// Phase 203's attribution: the packages each registered model covers, from `modules.json`.
let budgetPackages (text: string) : Map<string, string list> =
    budgetEntries text
    |> Map.map (fun _ el ->
        match el.TryGetProperty "packages" with
        | true, v when v.ValueKind = JsonValueKind.Array ->
            v.EnumerateArray()
            |> Seq.filter (fun p -> p.ValueKind = JsonValueKind.String)
            |> Seq.map _.GetString()
            |> List.ofSeq
        | _ -> [])

/// Everything the cone is computed from, so the computation itself is a pure function a test can
/// hand any tree it likes.
type ConeFacts =
    {
        /// `check.ps1`'s `$modules`, through `parseModules`.
        Roster: string list
        /// Module -> the registered modules its model's code references (`modelReferences`).
        References: Map<string, Set<string>>
        /// Module -> the packages it covers (`budgetPackages`).
        Packages: Map<string, string list>
        /// The changed paths, repository-relative.
        Changed: string list
        /// The leg scripts among `Changed` whose CODE moved (`scriptCode`).
        ScriptCodeMoved: Set<string>
        /// Modules in `$modules` now that were not in it at the named tree.
        NewlyRegistered: Set<string>
        /// Modules that entered or left `$proofOnly`.
        ExemptionMoved: Set<string>
        /// Modules whose `modules.json` entry moved (`budgetEntriesChanged`).
        EntriesMoved: Set<string>
    }

type ConeVerdict =
    | In
    | Dependency
    | Out

type ConeRow =
    { Module: string
      Verdict: ConeVerdict
      Reason: string }

type private ConeCause =
    {
        Target: string
        Why: string
        /// Only a change to the MODEL reaches the modules that reference it: an oracle, a covered
        /// source or a budget entry is read by its own module's step and by nobody else's.
        Reaches: bool
    }

/// The cone, one row per registered module, in the roster's order.
let computeCone (facts: ConeFacts) : ConeRow list =
    let refs m =
        facts.References |> Map.tryFind m |> Option.defaultValue Set.empty

    let causes = ResizeArray<ConeCause>()
    let shared = ResizeArray<string>()

    for path in facts.Changed do
        match classifyConePath facts.Roster path with
        | ModelSource m ->
            causes.Add
                { Target = m
                  Why = path + " changed"
                  Reaches = true }
        | OracleSource m ->
            causes.Add
                { Target = m
                  Why = "its oracle " + path + " changed"
                  Reaches = false }
        | CoveredSource package ->
            for m in facts.Roster do
                if
                    facts.Packages
                    |> Map.tryFind m
                    |> Option.defaultValue []
                    |> List.contains package
                then
                    causes.Add
                        { Target = m
                          Why = sprintf "%s changed, in %s, a package it covers" path package
                          Reaches = false }
        | LegScript ->
            if facts.ScriptCodeMoved.Contains path then
                shared.Add(sprintf "%s's code changed, not only its comments: the leg itself moved" path)
        | SharedInput -> shared.Add(sprintf "%s changed: a shared input of every module's check" path)
        | BudgetDeclaration
        | NotAnInput -> ()

    for m in facts.Roster do
        if facts.NewlyRegistered.Contains m then
            causes.Add
                { Target = m
                  Why = "newly registered in check.ps1's $modules"
                  Reaches = false }

        if facts.ExemptionMoved.Contains m then
            causes.Add
                { Target = m
                  Why = "it entered or left check.ps1's $proofOnly"
                  Reaches = false }

        if facts.EntriesMoved.Contains m then
            causes.Add
                { Target = m
                  Why = "its entry in proofs/modules.json changed"
                  Reaches = false }

    if shared.Count > 0 then
        let why =
            if shared.Count = 1 then
                shared[0]
            else
                sprintf "%s (and %d more shared input(s))" shared[0] (shared.Count - 1)

        facts.Roster
        |> List.map (fun m ->
            { Module = m
              Verdict = In
              Reason = why })
    else
        let reasons = System.Collections.Generic.Dictionary<string, string>()

        for target, group in causes |> Seq.groupBy _.Target do
            let whys = group |> Seq.map _.Why |> List.ofSeq

            reasons[target] <-
                match whys with
                | [ one ] -> one
                | first :: rest -> sprintf "%s (and %d more)" first rest.Length
                | [] -> ""

        let referencedBy m =
            facts.Roster |> List.filter (fun d -> (refs d).Contains m)

        // The dependants, breadth first from each model change, so a reason names the SHORTEST
        // chain of references back to the path that moved.
        for cause in causes do
            if cause.Reaches then
                let queue = System.Collections.Generic.Queue<string * string list>()
                let seen = System.Collections.Generic.HashSet<string>([ cause.Target ])
                queue.Enqueue(cause.Target, [ cause.Target ])

                while queue.Count > 0 do
                    let m, trail = queue.Dequeue()

                    for d in referencedBy m do
                        if seen.Add d then
                            if not (reasons.ContainsKey d) then
                                reasons[d] <- sprintf "references %s; %s" (String.concat " -> " trail) cause.Why

                            queue.Enqueue(d, d :: trail)

        let dependencies = System.Collections.Generic.Dictionary<string, string>()

        for m in facts.Roster do
            if reasons.ContainsKey m then
                let stack = System.Collections.Generic.Stack<string>(refs m)
                let seen = System.Collections.Generic.HashSet<string>()

                while stack.Count > 0 do
                    let r = stack.Pop()

                    if seen.Add r then
                        if not (reasons.ContainsKey r) && not (dependencies.ContainsKey r) then
                            dependencies[r] <-
                                sprintf "a dependency of %s, checked ahead of it so every module's clock is its own" m

                        for x in refs r do
                            stack.Push x

        facts.Roster
        |> List.map (fun m ->
            match reasons.TryGetValue m with
            | true, why ->
                { Module = m
                  Verdict = In
                  Reason = why }
            | _ ->
                match dependencies.TryGetValue m with
                | true, why ->
                    { Module = m
                      Verdict = Dependency
                      Reason = why }
                | _ ->
                    { Module = m
                      Verdict = Out
                      Reason = "nothing it reads changed" })

/// The modules the kit is handed for a cone: IN and DEPENDENCY, in the roster's order.
let coneChecked (rows: ConeRow list) : string list =
    rows |> List.filter (fun r -> r.Verdict <> Out) |> List.map _.Module

/// The cone as the leg prints it: a heading, then every registered module with its verdict and
/// the reason. ASCII only, so no console code page can garble it.
let renderCone (heading: string) (rows: ConeRow list) : string list =
    let count v =
        rows |> List.filter (fun r -> r.Verdict = v) |> List.length

    let width = rows |> List.map _.Module.Length |> List.fold max 0

    let tag =
        function
        | In -> "IN "
        | Dependency -> "DEP"
        | Out -> "OUT"

    sprintf
        "==== proofs: cone %s -- %d of %d registered module(s) IN, %d checked as a dependency, %d OUT"
        heading
        (count In)
        rows.Length
        (count Dependency)
        (count Out)
    :: [ for r in rows -> sprintf "     %s  %s  %s" (tag r.Verdict) (r.Module.PadRight width) r.Reason ]

// ---- the git half ---------------------------------------------------------------------------

let private runGit (root: string) (args: string list) : int * string * string =
    let psi = ChildProcess.redirected "git" ""

    for a in [ "-C"; root; "-c"; "core.quotepath=off" ] @ args do
        psi.ArgumentList.Add a

    use p = Diagnostics.Process.Start psi
    let out = p.StandardOutput.ReadToEndAsync()
    let err = p.StandardError.ReadToEndAsync()
    p.WaitForExit()
    p.ExitCode, out.Result, err.Result

let private gitLines (root: string) (args: string list) : Result<string list, string> =
    match runGit root args with
    | 0, out, _ ->
        Ok(
            out.Replace("\r\n", "\n").Split '\n'
            |> Array.filter (fun l -> l <> "")
            |> List.ofArray
        )
    | code, _, err -> Error(sprintf "git %s exited %d: %s" (String.concat " " args) code (err.Trim()))

let private resolveCommit (root: string) (tree: string) : Result<string, string> =
    match runGit root [ "rev-parse"; "--verify"; "--quiet"; tree + "^{commit}" ] with
    | 0, out, _ -> Ok(out.Trim())
    | _ -> Error(sprintf "'%s' names no commit this clone holds" tree)

/// A file's text at a commit, or "" where the commit has no such file.
let private textAt (root: string) (commit: string) (path: string) : string =
    match runGit root [ "show"; commit + ":" + path ] with
    | 0, out, _ -> out
    | _ -> ""

let private workingText (root: string) (path: string) : string =
    let file = Path.Combine(root, path)
    if File.Exists file then File.ReadAllText file else ""

/// The reference graph of the models in a working tree: each registered module's `.fst` and, where
/// one exists, its `.fsti`.
let modelReferenceGraph (root: string) (roster: string list) : Map<string, Set<string>> =
    roster
    |> List.map (fun m ->
        let source =
            workingText root ("proofs/" + m + ".fst")
            + "\n"
            + workingText root ("proofs/" + m + ".fsti")

        m, modelReferences roster m source)
    |> Map.ofList

/// The facts for the cone of a working tree against a commit: `git diff --name-only` (tracked,
/// staged or not, a rename split into both of its paths) plus the untracked files git does not
/// ignore.
let coneFactsSince (root: string) (commit: string) : Result<ConeFacts, string> =
    let tracked =
        gitLines root [ "diff"; "--name-only"; "--no-renames"; "--relative"; commit ]

    let untracked = gitLines root [ "ls-files"; "--others"; "--exclude-standard" ]

    match tracked, untracked with
    | Error e, _
    | _, Error e -> Error e
    | Ok tracked, Ok untracked ->
        let changed = tracked @ untracked |> List.distinct |> List.sort
        let checkNow = workingText root "proofs/check.ps1"
        let checkThen = textAt root commit "proofs/check.ps1"
        let roster = parseModules checkNow

        if List.isEmpty roster then
            Error "proofs/check.ps1's $modules could not be read, so there is no roster to select from"
        else
            let rosterThen = parseModules checkThen |> Set.ofList
            let exemptNow = parseScriptList "proofOnly" checkNow |> Set.ofList
            let exemptThen = parseScriptList "proofOnly" checkThen |> Set.ofList
            let budgetNow = workingText root "proofs/modules.json"

            Ok
                { Roster = roster
                  References = modelReferenceGraph root roster
                  Packages = budgetPackages budgetNow
                  Changed = changed
                  ScriptCodeMoved =
                    changed
                    |> List.filter (fun p ->
                        List.contains p legScripts
                        && scriptCode (workingText root p) <> scriptCode (textAt root commit p))
                    |> Set.ofList
                  NewlyRegistered = roster |> List.filter (fun m -> not (rosterThen.Contains m)) |> Set.ofList
                  ExemptionMoved = Set.difference (Set.union exemptNow exemptThen) (Set.intersect exemptNow exemptThen)
                  EntriesMoved =
                    if List.contains "proofs/modules.json" changed then
                        budgetEntriesChanged (textAt root commit "proofs/modules.json") budgetNow
                    else
                        Set.empty }

type ConeReport =
    { Since: string
      Commit: string
      Rows: ConeRow list }

/// The cone of the working tree at `root` against the commit `since` names.
let coneSince (root: string) (since: string) : Result<ConeReport, string> =
    resolveCommit root since
    |> Result.bind (fun commit ->
        coneFactsSince root commit
        |> Result.map (fun facts ->
            { Since = since
              Commit = commit
              Rows = computeCone facts }))

let private short (sha: string) =
    if sha.Length > 12 then sha.Substring(0, 12) else sha

/// Whether an EMPTY cone may exit green. `Ok` is the line that says what stands behind it; `Error`
/// is why nothing does.
let emptyConeBaseline (root: string) : Result<string, string> =
    let file = Path.Combine(root, coneBaselinePath)

    if not (File.Exists file) then
        Error(
            sprintf
                "the leg has NO STRICT BASELINE: %s is absent, so no recorded -Strict full run stands behind an empty cone. Run the full leg; a green `check.ps1 -Strict` over a committed tree records the baseline (commit the file it writes)"
                coneBaselinePath
        )
    else
        let tree =
            try
                use doc = JsonDocument.Parse(File.ReadAllText file)

                match doc.RootElement.TryGetProperty "tree" with
                | true, t when t.ValueKind = JsonValueKind.String -> Some(t.GetString())
                | _ -> None
            with :? JsonException ->
                None

        match tree with
        | None -> Error(sprintf "the leg has NO STRICT BASELINE: %s names no `tree`" coneBaselinePath)
        | Some tree ->
            match resolveCommit root tree with
            | Error _ ->
                Error(
                    sprintf
                        "the leg has NO STRICT BASELINE this clone can reach: %s names %s, which this clone does not hold"
                        coneBaselinePath
                        tree
                )
            | Ok commit ->
                match runGit root [ "merge-base"; "--is-ancestor"; commit; "HEAD" ] with
                | 1, _, _ ->
                    Error(
                        sprintf
                            "the leg has NO STRICT BASELINE on this history: %s names %s, which is not an ancestor of HEAD"
                            coneBaselinePath
                            (short commit)
                    )
                | 0, _, _ ->
                    match coneSince root commit with
                    | Error e -> Error e
                    | Ok report ->
                        let moved =
                            report.Rows |> List.filter (fun r -> r.Verdict = In) |> List.map _.Module

                        if List.isEmpty moved then
                            Ok(
                                sprintf
                                    "the cone is EMPTY, and the last -Strict full run (%s, recorded in %s) stands behind it: nothing a registered module reads has changed since the tree it verified"
                                    (short commit)
                                    coneBaselinePath
                            )
                        else
                            Error(
                                sprintf
                                    "the cone is empty, but the last -Strict full run (%s) predates changes to %s that no recorded run verified; run -Since %s"
                                    (short commit)
                                    (String.concat ", " moved)
                                    (short commit)
                            )
                | code, _, err -> Error(sprintf "git merge-base --is-ancestor exited %d: %s" code (err.Trim()))

let private writeJson (path: string) (write: Utf8JsonWriter -> unit) =
    use buffer = new MemoryStream()

    do
        // LF whatever the platform, and the relaxed encoder so a backtick or a `<tree>` in a
        // message stays readable rather than becoming `.
        let options =
            JsonWriterOptions(
                Indented = true,
                NewLine = "\n",
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            )

        use w = new Utf8JsonWriter(buffer, options)

        w.WriteStartObject()
        write w
        w.WriteEndObject()

    File.WriteAllText(path, Text.Encoding.UTF8.GetString(buffer.ToArray()) + "\n")

/// Record a green `-Strict` full run as the baseline an empty cone may lean on — but only when the
/// working tree matches HEAD in everything a module reads, since otherwise the run verified bytes
/// no commit holds. `Ok` is the line to print; `Error` says why nothing was written.
let recordStrictBaseline (root: string) (runs: int) (recordedAt: DateTimeOffset) : Result<string, string> =
    match resolveCommit root "HEAD" with
    | Error e -> Error e
    | Ok head ->
        match coneFactsSince root head with
        | Error e -> Error e
        | Ok facts ->
            let moved = computeCone facts |> List.filter (fun r -> r.Verdict = In)

            if not (List.isEmpty moved) then
                Error(
                    sprintf
                        "NOT recorded: the working tree differs from HEAD in what %d module(s) read (first: %s, %s), so this run verified bytes no commit holds. Commit, then re-run -Strict to record a baseline"
                        moved.Length
                        moved.Head.Module
                        moved.Head.Reason
                )
            else
                let prover =
                    try
                        use pin = JsonDocument.Parse(workingText root "proofs/fstar-pin.json")

                        match pin.RootElement.TryGetProperty "fstar" with
                        | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                        | _ -> "unknown"
                    with :? JsonException ->
                        "unknown"

                writeJson (Path.Combine(root, coneBaselinePath)) (fun w ->
                    w.WriteString("kind", "proofStrictBaseline")

                    w.WriteString(
                        "$comment",
                        "The last green -Strict FULL run of the proof leg, written by proofs/check.ps1 (Phase 328) and committed by whoever ran it. An EMPTY module cone (check.ps1 -Since <tree>) is green only when `tree` is an ancestor of HEAD and the cone against it is empty too. Never hand-edited: a record no run produced is exactly the stale evidence the selector must not lean on."
                    )

                    w.WriteString("tree", head)
                    w.WriteStartObject "run"
                    w.WriteBoolean("strict", true)
                    w.WriteNumber("runs", runs)
                    w.WriteNumber("modules", facts.Roster.Length)
                    w.WriteString("prover", prover)
                    w.WriteString("recordedAt", recordedAt.UtcDateTime.ToString "yyyy-MM-ddTHH:mm:ssZ")
                    w.WriteEndObject())

                Ok(
                    sprintf
                        "strict baseline recorded in %s: the -Strict full run of %s (%d run(s), %d modules). Commit the file."
                        coneBaselinePath
                        (short head)
                        runs
                        facts.Roster.Length
                )

/// `dotnet run --project tests/Fuaran.Core.Tests -- --proof-cone ...`, the half of
/// `proofs/check.ps1 -Since` that decides. Its answer goes to the `--out` file as JSON, printed
/// lines included, so `check.ps1` prints them itself and no console code page sits between the two.
///   --since <tree> --out <file>          the cone, and for an empty one the baseline verdict
///   --record-strict <runs> --out <file>  record a green -Strict full run as the baseline
/// Exit 0 when the answer was computed, whatever it is; 2 when it could not be.
let coneCli (args: string list) : int =
    match args with
    | [ "--since"; since; "--out"; out ] ->
        match coneSince repoRoot since with
        | Error e ->
            eprintfn "==== proofs: the cone could not be computed: %s" e
            2
        | Ok report ->
            let checkedModules = coneChecked report.Rows

            let verdict, message =
                if not (List.isEmpty checkedModules) then
                    "verify", ""
                else
                    match emptyConeBaseline repoRoot with
                    | Ok line -> "empty-green", line
                    | Error why -> "empty-red", why

            writeJson out (fun w ->
                w.WriteString("verdict", verdict)
                w.WriteString("commit", report.Commit)
                w.WriteString("message", message)
                w.WriteStartArray "verify"

                for m in checkedModules do
                    w.WriteStringValue m

                w.WriteEndArray()
                w.WriteStartArray "lines"

                for line in renderCone (sprintf "since %s (%s)" since (short report.Commit)) report.Rows do
                    w.WriteStringValue line

                w.WriteEndArray())

            0
    | [ "--record-strict"; runs; "--out"; out ] ->
        match Int32.TryParse runs with
        | true, n when n >= 1 ->
            let recorded, message =
                match recordStrictBaseline repoRoot n DateTimeOffset.UtcNow with
                | Ok line -> true, line
                | Error why -> false, why

            writeJson out (fun w ->
                w.WriteBoolean("recorded", recorded)
                w.WriteString("message", message))

            0
        | _ ->
            eprintfn "==== proofs: --record-strict takes a positive run count, not '%s'" runs
            2
    | _ ->
        eprintfn "usage: --proof-cone --since <tree> --out <file> | --proof-cone --record-strict <runs> --out <file>"
        2

// ---- the family ------------------------------------------------------------------------------

/// A four-module toy tree for the exact assertions: B opens A, C opens B, D stands alone; A and C
/// cover `Pkg.A`, D covers `Pkg.D`. The real tree's assertions are properties rather than pinned
/// sets, so a sibling adding a model cannot turn them red.
let private toyFacts (changed: string list) : ConeFacts =
    { Roster = [ "A"; "B"; "C"; "D" ]
      References = Map.ofList [ "A", Set.empty; "B", set [ "A" ]; "C", set [ "B" ]; "D", Set.empty ]
      Packages = Map.ofList [ "A", [ "Pkg.A" ]; "B", []; "C", [ "Pkg.A" ]; "D", [ "Pkg.D" ] ]
      Changed = changed
      ScriptCodeMoved = Set.empty
      NewlyRegistered = Set.empty
      ExemptionMoved = Set.empty
      EntriesMoved = Set.empty }

let private realConeFacts (changed: string list) : ConeFacts =
    let roster = realModules ()

    { Roster = roster
      References = modelReferenceGraph repoRoot roster
      Packages = budgetPackages (File.ReadAllText(Path.Combine(repoRoot, "proofs", "modules.json")))
      Changed = changed
      ScriptCodeMoved = Set.empty
      NewlyRegistered = Set.empty
      ExemptionMoved = Set.empty
      EntriesMoved = Set.empty }

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
    writeAt root "proofs/check.ps1" "# the roster\n$modules = @('A', 'B', 'C')\n$proofOnly = @()\nWrite-Host 'leg'\n"
    writeAt root "proofs/A.fst" "module A\nlet a = 1\n"
    writeAt root "proofs/B.fst" "module B\nopen A\nlet b = a\n"
    writeAt root "proofs/C.fst" "module C\n(* names B.b in prose only *)\nlet c = 2\n"

    writeAt
        root
        "proofs/modules.json"
        """{ "modules": [ { "module": "A", "packages": [ "Pkg.A" ] }, { "module": "B", "packages": [] }, { "module": "C", "packages": [] } ] }"""

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
    let psi = ChildProcess.redirected "pwsh" ""

    for a in [ "-NoProfile"; "-NonInteractive"; "-File"; checkScriptPath ] @ args do
        psi.ArgumentList.Add a

    use p = Diagnostics.Process.Start psi
    let out = p.StandardOutput.ReadToEndAsync()
    let err = p.StandardError.ReadToEndAsync()
    p.WaitForExit()
    p.ExitCode, Regex.Replace(out.Result + err.Result, "\u001b" + @"\[[0-9;]*m", "")

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
              let before = "#Requires -Version 7.0\n# prose\n$modules = @('A')\n$x = 1\n"

              let prose =
                  "#Requires -Version 7.0\n# prose, reworded\n\n    # and indented\n$modules = @('A', 'B')\n$x = 1   \r\n"

              let code = before + "$legArgs.ZRlimit = 80\n"

              Expect.equal
                  (scriptCode prose)
                  (scriptCode before)
                  "comments, blanks, line endings and the roster literal are not code"

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

              for m in roster do
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
              let witness =
                  roster
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

                  match recordStrictBaseline root 3 (DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero)) with
                  | Error why -> failtestf "a clean tree's strict run was not recorded: %s" why
                  | Ok _ -> ()

                  let recorded = File.ReadAllText(Path.Combine(root, coneBaselinePath))
                  let head = (gitOk root [ "rev-parse"; "HEAD" ]).Trim()
                  Expect.stringContains recorded head "the record names the tree the run verified"
                  Expect.isFalse (recorded.Contains "\r") "the record is written LF"
                  commitAll root "record the strict baseline"

                  match emptyConeBaseline root with
                  | Error why -> failtestf "an empty cone over a recorded ancestor was not green: %s" why
                  | Ok _ -> ()

                  // A model edit: A and B (which opens A) are in; C only names B in prose.
                  File.AppendAllText(Path.Combine(root, "proofs/A.fst"), "let a2 = 2\n")
                  Expect.equal (coneIn root) (set [ "A"; "B" ]) "the working tree's model edit, and its dependant"

                  match recordStrictBaseline root 1 DateTimeOffset.UtcNow with
                  | Ok line -> failtestf "a strict run over an uncommitted model edit was recorded: %s" line
                  | Error why -> Expect.stringContains why "NOT recorded" "a dirty tree is never recorded"

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
                      """{ "kind": "proofStrictBaseline", "tree": "0000000000000000000000000000000000000001" }"""

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

          testCase "-Modules names a cone by hand; an unregistered name, or -Since and -Modules together, is refused"
          <| fun _ ->
              let code, output = runCheckScript [ "-Modules"; "Chain,Limits"; "-PlanOnly" ]
              Expect.equal code 0 (sprintf "a registered hand cone plans:\n%s" output)

              Expect.equal
                  (plannedModules output)
                  (realModules () |> List.filter (fun m -> m = "Chain" || m = "Limits"))
                  "exactly the named modules, in the roster's order"

              let code, output = runCheckScript [ "-Modules"; "NoSuchModel"; "-PlanOnly" ]
              Expect.notEqual code 0 "an unregistered name is refused"
              Expect.stringContains output "NoSuchModel" "and the refusal names it"

              let code, _ = runCheckScript [ "-Since"; "HEAD"; "-Modules"; "Chain"; "-PlanOnly" ]
              Expect.notEqual code 0 "a cone named two ways is refused" ]
