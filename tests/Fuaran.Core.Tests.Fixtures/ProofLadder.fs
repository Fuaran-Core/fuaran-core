module Fuaran.Core.Tests.ProofLadder

// The claims ladder as a CHECK (Phase 144; clauses from Phases 174 and 522) and the proof roster it reads
// (`proofs/modules.json`, Phase 504): the parametrised check, the contract table, the README ladder summary
// render, and `parseRoster` — the ONE reading of the roster the cone selector (`ProofCone`) also uses
// (DECISIONS.md D74). `ProofsLadderTests` (the Proofs.Ladder family) is what states the clauses and runs
// the check over the real tree and its go-red fixtures.

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

// ---------------------------------------------------------------------------
//  The inputs a ladder is measured against
// ---------------------------------------------------------------------------

type LadderInputs =
    {
        /// The directory a row's `evidence.model` path resolves against.
        Root: string
        /// The modules the ladder must cover — the roster's models (`proofs/modules.json`), for the real file.
        Modules: string list
        /// The cases of the suite a `tested` row's family names, or `None` when the family names
        /// no suite this test tree holds. For the real file it is reflection over the assembly
        /// (`suiteCases`), so no list of citable suites is restated here.
        Suites: string -> Set<string> option
        /// The law names a `domain-obligation` row's `dischargedBy` may name — the entry points of
        /// the shipped kit's declared law-family ROSTER, for the real file. Read from
        /// `Fuaran.Core.Families` for the same reason `Cases` is read from the test tree: a second
        /// copy of that list, restated here, is precisely the drift this family exists to catch.
        /// Phase 184 narrowed it from the `Conformance` module's whole public static surface, so
        /// an obligation can no longer name a law that no conformance census enumerates.
        Laws: Set<string>
        /// The probe families a `host-premise` row's `probe` may name (Phase 483) — the family ids the
        /// premise exporter writes under `conformance/premises/`, for the real file
        /// (`PremiseProbeExport.familyIds`). Every one must be named by exactly one row: a probe no row
        /// names is a premise the ladder never stated.
        Probes: Set<string>
    }

/// The closed level set, in ladder order (a message renders them in this order, not sorted).
let levelOrder = [ "proved"; "tested"; "assumed"; "policy" ]

let internal levels = Set.ofList levelOrder

/// `fuaran-core#NNN` and nothing else — not a bare number, not another side's prefix, not a
/// leading zero.
let internal phaseForm = Regex(@"^fuaran-core#[1-9][0-9]*$", RegexOptions.Compiled)

/// The closed CLASS set an `assumed` row is drawn from (Phase 174), in contract order — a message
/// renders them in this order, not sorted. `level` says how strong a claim is; `class` says which
/// kind of ASSUMED an assumed row is, which is the question a domain instantiating this substrate
/// actually has: `domain-obligation` is what it owes and what a green kit run at its own witness
/// discharges, `model-bridge` is this repository's own model-to-production gap and is inherited,
/// `premise` is what nothing discharges at all. `host-premise` (Phase 483) is what the models take from
/// the runtime they run on — a string model, float digits, integer width, a hash, a stack — which this
/// implementation's runtime gives it and another host's may not: each names a `probe`, the vector family
/// under `conformance/premises/` whose every vector a HOST reproduces to discharge it on its own runtime.
let classOrder = [ "domain-obligation"; "model-bridge"; "premise"; "host-premise" ]

let internal classes = Set.ofList classOrder

/// `Conformance.<law>`, where `<law>` is a name the shipped kit exports. The prefix is required
/// rather than inferred: a bare `witnessLaws` would read as a law of some unnamed kit, and the
/// contract's whole content is WHICH kit's green run discharges the row.
let internal lawForm =
    Regex(@"^Conformance\.([A-Za-z][A-Za-z0-9_']*)$", RegexOptions.Compiled)

/// `Suite:<Module>.<list>` — a test list this assembly declares, named by its module and its value
/// (Phase 522). The third closed form a `tested` row's family takes, beside `Proofs.Oracle` and
/// `Conformance.<law>`: a suite's LABEL is prose and moves with its wording, its value's name does not.
let suiteForm =
    Regex(@"^Suite:([A-Za-z][A-Za-z0-9_]*)\.([A-Za-z][A-Za-z0-9_']*)$", RegexOptions.Compiled)

/// History told in a row's prose (Phase 522): the row's `phase` is its one citation, and what a
/// change did lives in that change's record.
let internal historyForm = Regex(@"\bPhases?\s+[0-9]", RegexOptions.Compiled)

/// The closed set a `model-bridge`'s `closes` is drawn from. A phase is not a member: this
/// repository keeps no schedule, because its roadmap lives outside it and cannot be read from here
/// (Phase 537, which deleted the scheduled half of the coverage clause until a writer for a list of
/// open phases exists).
/// `unscheduled` is a value rather than a rounding to `permanent` on purpose: two of this
/// repository's bridges CAN be closed and nobody has taken the work, and recording those as
/// `permanent` would assert an impossibility the README's own prose contradicts.
let closesLiterals = [ "permanent"; "unscheduled" ]

/// The em dash a `premise` row carries in the contract table's third column — it has neither a
/// discharging law nor a closing phase, and an empty cell would not say which.
let internal noThirdColumn = "—"

// ---------------------------------------------------------------------------
//  The clauses
// ---------------------------------------------------------------------------

let internal strMember (el: JsonElement) (name: string) : string option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

let internal objMember (el: JsonElement) (name: string) : JsonElement option =
    match el.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.Object -> Some v
    | _ -> None

let internal strArrayMember (el: JsonElement) (name: string) : string list option =
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

let internal finding (row: string) (clause: string) (detail: string) =
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

                            // Phase 522: a `supporting` declaration is checked like `theorem`, as
                            // LADDER.md has always said it is.
                            let supportingFindings =
                                match ev |> Option.bind (fun e -> strArrayMember e "supporting") with
                                | Some names when File.Exists full ->
                                    let text = File.ReadAllText full

                                    names
                                    |> List.filter (fun n -> not (declaresTopLevel text n))
                                    |> List.map (fun n ->
                                        finding
                                            id
                                            "theorem-exists"
                                            (sprintf
                                                "%s declares no top-level `%s`, which `evidence.supporting` names"
                                                path
                                                n))
                                | _ -> []

                            registered @ present @ theoremFindings @ supportingFindings

                let testedFindings =
                    if level <> Some "tested" then
                        []
                    else
                        // Phase 522: the family is CLOSED, and it names the suite the cases are
                        // looked for in — a prose family used to pass every clause.
                        let familyFindings, suite =
                            match ev |> Option.bind (fun e -> strMember e "family") with
                            | None ->
                                [ finding id "tested-family-closed" "a tested row carries no `evidence.family`" ], None
                            | Some f ->
                                let law = lawForm.Match f

                                let closed =
                                    f = "Proofs.Oracle"
                                    || suiteForm.IsMatch f
                                    || (law.Success && Set.contains law.Groups[1].Value inputs.Laws)

                                if not closed then
                                    [ finding
                                          id
                                          "tested-family-closed"
                                          (sprintf
                                              "family '%s' is none of `Proofs.Oracle`, `Conformance.<law>` (a law the shipped kit exports) or `Suite:<Module>.<list>`"
                                              f) ],
                                    None
                                else
                                    match inputs.Suites f with
                                    | Some cases -> [], Some(f, cases)
                                    | None ->
                                        [ finding
                                              id
                                              "tested-family-closed"
                                              (sprintf "family '%s' names no suite the test tree declares" f) ],
                                        None

                        let caseFindings =
                            match ev |> Option.bind (fun e -> strArrayMember e "cases"), suite with
                            | None, _
                            | Some [], _ ->
                                [ finding
                                      id
                                      "tested-case-exists"
                                      "a tested row must name its cases in `evidence.cases` (a non-empty array of the family's case names) — naming the family alone leaves the claim uncheckable" ]
                            // A family that names no suite is already a finding; its cases have
                            // nowhere to be looked for, and a second finding would say nothing new.
                            | Some _, None -> []
                            | Some names, Some(family, cases) ->
                                names
                                |> List.filter (fun n -> not (Set.contains n cases))
                                |> List.map (fun n ->
                                    finding
                                        id
                                        "tested-case-exists"
                                        (sprintf "`evidence.cases` names '%s', which is not a case of %s" n family))

                        familyFindings @ caseFindings

                // ---- Phase 522: what is claimed, what is not, and no history ----
                let statement = strMember row "statement"
                let boundary = strMember row "boundary"

                let proseFindings =
                    [ for name, value in [ "statement", statement; "boundary", boundary ] do
                          match value with
                          | Some v when not (String.IsNullOrWhiteSpace v) ->
                              let m = historyForm.Match v

                              if m.Success then
                                  yield
                                      finding
                                          id
                                          "statement-boundary"
                                          (sprintf
                                              "`%s` tells history ('%s') — the row's `phase` is its one citation, and what a change did is that change's record"
                                              name
                                              (v.Substring(m.Index, min 24 (v.Length - m.Index))))
                          | _ ->
                              yield
                                  finding
                                      id
                                      "statement-boundary"
                                      (sprintf
                                          "no `%s` — a row says what it claims (`statement`) and what it does NOT cover (`boundary`)"
                                          name) ]

                let joinFindings =
                    match statement, boundary, strMember row "claim" with
                    | Some s, Some b, Some c when c = s + " " + b -> []
                    | Some _, Some _, _ ->
                        [ finding
                              id
                              "claim-is-the-join"
                              "`claim` is not `statement` and `boundary` joined by one space — the registry reads `claim`, so it must say exactly what the two fields say" ]
                    | _ -> []

                // ---- Phase 522: every path LADDER.md says must exist, and only this tree ----
                let pathFindings =
                    let mustExist (field: string) =
                        match ev |> Option.bind (fun e -> strMember e field) with
                        | Some p when not (File.Exists(Path.Combine(inputs.Root, p))) ->
                            [ finding
                                  id
                                  "evidence-exists"
                                  (sprintf "`evidence.%s` %s is not a file (looked under %s)" field p inputs.Root) ]
                        | _ -> []

                    (match level with
                     | Some "policy" -> mustExist "document"
                     | Some "tested" -> mustExist "artifact" @ mustExist "host"
                     | _ -> [])
                    @ (match strMember row "repo" with
                       | Some r ->
                           [ finding
                                 id
                                 "evidence-exists"
                                 (sprintf
                                     "carries `repo`: \"%s\" — this family holds every row to this repository's own tree, so a row resting on another's cannot be checked here"
                                     r) ]
                       | None -> [])

                // ---- Phase 174: which KIND of assumed an `assumed` row is ----
                let cls = strMember row "class"
                let dischargedBy = strMember row "dischargedBy"
                let closes = strMember row "closes"
                let probe = strMember row "probe"

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
                        @ misplaced "assumed-class" "probe" probe "only a `host-premise` names a probe"
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
                            @ misplaced
                                "obligation-law"
                                "probe"
                                probe
                                "a domain discharges an obligation at its witness, not a host on its runtime"
                        | Some "model-bridge" ->
                            let closesFindings =
                                match closes with
                                | None ->
                                    [ finding
                                          id
                                          "bridge-closes"
                                          (sprintf "a `model-bridge` carries no `closes` (one of %s)" renderedCloses) ]
                                | Some cl when List.contains cl closesLiterals -> []
                                | Some cl ->
                                    [ finding
                                          id
                                          "bridge-closes"
                                          (sprintf
                                              "`closes` '%s' is not one of %s — no schedule is kept here, so a phase is not a value"
                                              cl
                                              renderedCloses) ]

                            closesFindings
                            @ misplaced
                                "bridge-closes"
                                "dischargedBy"
                                dischargedBy
                                "no law a domain runs closes a gap between this repository's model and its own production code"
                            @ misplaced
                                "bridge-closes"
                                "probe"
                                probe
                                "no host's run closes a gap between this repository's model and its own production code"
                        | Some "host-premise" ->
                            // Phase 483: discharged by a HOST reproducing its probe, so it names the
                            // probe, and neither a law (no domain runs it) nor a `closes`.
                            let probeFindings =
                                match probe with
                                | None ->
                                    [ finding
                                          id
                                          "premise-probe"
                                          "a `host-premise` carries no `probe` — a premise a host inherits with no probe is one no host can show it answered" ]
                                | Some p when not (Set.contains p inputs.Probes) ->
                                    [ finding
                                          id
                                          "premise-probe"
                                          (sprintf
                                              "`probe` names '%s', which is no probe family under conformance/premises/ — the row cites vectors no host can run"
                                              p) ]
                                | Some _ -> []

                            probeFindings
                            @ misplaced
                                "premise-probe"
                                "dischargedBy"
                                dischargedBy
                                "a host premise is discharged by a host's run of its probe, not by a domain's law"
                            @ misplaced
                                "premise-probe"
                                "closes"
                                closes
                                "a host premise is answered per host, not closed"
                        | Some _ ->
                            // `premise`: nothing discharges it and no phase closes it, so either
                            // field on such a row is a citation to something that does not exist.
                            misplaced
                                "assumed-class"
                                "dischargedBy"
                                dischargedBy
                                "a `premise` is discharged by nothing"
                            @ misplaced "assumed-class" "closes" closes "a `premise` is closed by nothing"
                            @ misplaced "assumed-class" "probe" probe "a `premise` is probed by nothing"

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
                @ proseFindings
                @ joinFindings
                @ provedFindings
                @ testedFindings
                @ pathFindings
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

        // Phase 483: every probe family is named by exactly one `host-premise` row, so a probe is
        // never shipped for a premise the ladder does not state, nor stated twice.
        let probeNames =
            rows
            |> List.filter (fun r -> strMember r "class" = Some "host-premise")
            |> List.choose (fun r -> strMember r "probe")

        let probeFindings =
            [ for p in inputs.Probes do
                  match probeNames |> List.filter ((=) p) |> List.length with
                  | 0 ->
                      yield
                          sprintf
                              "[premise-probe]: the probe family '%s' is named by no `host-premise` row — vectors for a premise the ladder never stated"
                              p
                  | 1 -> ()
                  | n ->
                      yield
                          sprintf
                              "[premise-probe]: the probe family '%s' is named by %d `host-premise` rows — one premise, one probe"
                              p
                              n ]

        perRow @ uncovered @ probeFindings

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
let internal contractRowForm =
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
                | _ when strMember r "class" = Some "host-premise" ->
                    // Phase 483 — the probe a host reproduces, or the em dash where it names none.
                    strMember r "probe"
                    |> Option.map (sprintf "`%s` (probe)")
                    |> Option.defaultValue noThirdColumn
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
//  block, the contract table's rows and the contract prose's four counts from the ladder. Since Phase
//  533 it is one step of `ProofReadme.render`, which the tool's `render proofs-readme` writes and the
//  Proofs.Ladder family holds. Prose around the blocks is untouched: what a number says is generated,
//  what it means is written.
// ---------------------------------------------------------------------------

let ladderSummaryBegin =
    "<!-- ladder-summary:begin — rendered from ../proofs.json by `render proofs-readme`; held by the Proofs.Ladder family -->"

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
        "**The ladder, counted:** %d claims — %d proved across %d models, %d tested, %d assumed (%d `domain-obligation`, %d `model-bridge`, %d `premise`, %d `host-premise`), %d policy."
        (List.length rows)
        (List.length (at "proved"))
        provedModels
        (List.length (at "tested"))
        (List.length (at "assumed"))
        (assumedIn "domain-obligation")
        (assumedIn "model-bridge")
        (assumedIn "premise")
        (assumedIn "host-premise")
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

/// How the proof leg treats a module — `proofs/modules.json`'s `kind` (Phase 504).
type ModelKind =
    /// Checked, extracted to the oracle, twinned, and run beside production by the host.
    | Extracted
    /// Checked only: its production side cannot be run beside an extraction (the reason is in
    /// `check.ps1`'s exemption paragraphs).
    | ProofOnly
    /// Emitted by `Fuaran.Core.Idl.Codegen`'s F* target and checked only; the `Proofs.Vocabulary`
    /// family holds it to a fresh generation.
    | Generated
    /// Not a model: a module every model opens and that references no model (`Twin`, `ListAlgebra`).
    /// Checked and extracted like a model, carries no ladder row, and is a SHARED input of the cone.
    | Shared

/// One roster entry: the module, how it is treated, and the registered modules its code references.
type RosterEntry =
    { Module: string
      Kind: ModelKind
      Opens: string list }

/// The modules `kind` names, by their spelling in `modules.json`.
let modelKinds =
    [ "extracted", Extracted
      "proofOnly", ProofOnly
      "generated", Generated
      "shared", Shared ]

/// THE PROOF ROSTER — `proofs/modules.json`'s `modules`, in check order. Since Phase 504 this file
/// is the only place a model is enumerated: `check.ps1` reads it for the modules it hands the kit and
/// the ones it exempts from extraction, this family reads it for the ladder, the twin roster and the
/// oracle project, and the cone selector reads it for the roster it selects from. An entry with no
/// `kind`, a kind not in `modelKinds`, or no `opens` list is an ERROR naming the entry, never a
/// default: a roster read as a guess is the silent drift this file replaced.
let parseRoster (modulesJson: string) : Result<RosterEntry list, string list> =
    try
        use doc = JsonDocument.Parse modulesJson

        match doc.RootElement.TryGetProperty "modules" with
        | true, arr when arr.ValueKind = JsonValueKind.Array ->
            let read (el: JsonElement) =
                let str name =
                    match el.TryGetProperty(name: string) with
                    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                    | _ -> None

                match str "module" with
                | None -> Error "an entry names no `module`"
                | Some m ->
                    let kind =
                        str "kind"
                        |> Option.bind (fun k -> modelKinds |> List.tryFind (fun (s, _) -> s = k) |> Option.map snd)

                    let opens =
                        match el.TryGetProperty "opens" with
                        | true, v when v.ValueKind = JsonValueKind.Array ->
                            v.EnumerateArray()
                            |> Seq.filter (fun x -> x.ValueKind = JsonValueKind.String)
                            |> Seq.map _.GetString()
                            |> List.ofSeq
                            |> Some
                        | _ -> None

                    match kind, opens with
                    | Some k, Some o -> Ok { Module = m; Kind = k; Opens = o }
                    | None, _ ->
                        Error(
                            sprintf "'%s' has no `kind` among %s" m (modelKinds |> List.map fst |> String.concat ", ")
                        )
                    | _, None -> Error(sprintf "'%s' has no `opens` list" m)

            let results = arr.EnumerateArray() |> Seq.map read |> List.ofSeq

            match
                results
                |> List.choose (function
                    | Error e -> Some e
                    | Ok _ -> None)
            with
            | [] ->
                Ok(
                    results
                    |> List.choose (function
                        | Ok e -> Some e
                        | Error _ -> None)
                )
            | errors -> Error errors
        | _ -> Error [ "the file has no `modules` array" ]
    with :? JsonException as e ->
        Error [ sprintf "the file is not JSON: %s" e.Message ]

/// A module the leg extracts to the oracle (and so twins and compiles in the oracle project).
let isExtracted (e: RosterEntry) = e.Kind = Extracted || e.Kind = Shared

/// A MODEL — a roster entry that is not a shared module. Every model carries a `proved` row.
let isModel (e: RosterEntry) = e.Kind <> Shared
