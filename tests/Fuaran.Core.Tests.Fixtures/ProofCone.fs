module Fuaran.Core.Tests.ProofCone

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Fuaran.Core.Tests.ProofLadder
// ===========================================================================
//  Phase 328 — the module-cone selector
// ===========================================================================
//
// `proofs/check.ps1 -Since <tree>` verifies, cold, only the models whose inputs changed against a
// named tree. This section is the half of it that DECIDES — which registered modules are in the
// cone, and why; `check.ps1` builds the tool and calls it through `proof-cone` (tools/Fuaran.Core.Tools) and
// hands the kit exactly the modules named here. The kit, which runs the prover, is not touched.
//
// It lives beside `parseRoster` for the reason the phase names: the roster is read by the SAME
// function the ladder reads it with, so the ladder and the selector cannot disagree about which
// models exist. Since Phase 504 the roster is `proofs/modules.json` — JSON, read by `check.ps1`
// through ConvertFrom-Json and here through `parseRoster`, where it used to be a PowerShell literal
// matched by a regex — and the Proofs.Cone family's `-PlanOnly` cases hold the two readings equal.
//
// SOUNDNESS IS THE WHOLE OF ITS CORRECTNESS. A module is IN whenever something its verdict reads
// changed:
//   * its model `proofs/<M>.fst` (or `.fsti`) — and then every module that REFERENCES it, however
//     transitively, because a module's check re-reads everything it references;
//   * its committed oracle `proofs/oracle/<M>.fs`, which the extraction diff reads;
//   * a production source under `src/<Package>/` for a package its `proofs/modules.json` entry
//     names (Phase 203's `packages` — the map from subject to model lives THERE; `proofs.json`'s
//     rows name models and theorems, never a source path);
//   * its `modules.json` entry — which since Phase 504 is also its registration and its `kind`, so
//     a module newly registered or moved in or out of extraction is an entry that moved.
// A SHARED module (`kind: shared` — `Twin`, `ListAlgebra`) is opened by every model and references
// none, which is `oracle/Prims.fs`'s position: a change to its model is a shared input, so every
// module is in (DECISIONS.md D102, as amended by Phase 504).
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
// AN EMPTY CONE is green only over a recorded strict run: `proofs/last-strict.json` is the RUN
// VERDICT (Phase 522; the kit's header, "THE RUN VERDICT") of the last green `-Strict` FULL leg with
// the tree it verified, that tree is an ancestor of HEAD, and the cone against IT is empty too — so
// an empty cone since some other tree cannot lean on a baseline that predates a change nobody
// verified. A green `-Strict` full run records the file from its own verdict, and refuses to when
// the working tree differs from HEAD in anything a module reads, because then the run verified bytes
// no commit holds.

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

let internal declaredReference =
    Regex(@"\b(?:open|include|friend)\s+([A-Z][\w']*)", RegexOptions.Compiled)

let internal abbreviationReference =
    Regex(@"\bmodule\s+[A-Z][\w']*\s*=\s*([A-Z][\w']*)", RegexOptions.Compiled)

let internal qualifiedReference =
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
let classifyConePath (roster: string list) (shared: Set<string>) (path: string) : ConeInput =
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

        if registered name && not (shared.Contains name) then
            ModelSource name
        else
            SharedInput
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

/// A leg script's CODE: its text without full-line `#` comments, blank lines and trailing blanks.
/// Two versions with equal code differ in prose only.
let scriptCode (text: string) : string =
    text.Replace("\r\n", "\n").Split '\n'
    |> Array.map _.TrimEnd()
    |> Array.filter (fun l ->
        let t = l.TrimStart()
        t <> "" && not (t.StartsWith "#"))
    |> String.concat "\n"

let internal budgetEntries (text: string) : Map<string, JsonElement> =
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
        /// `proofs/modules.json`'s modules in check order, through `parseRoster`.
        Roster: string list
        /// The roster's SHARED modules (`kind: shared`): a change to one is a shared input.
        Shared: Set<string>
        /// Module -> the registered modules its model's code references (`modelReferences`).
        References: Map<string, Set<string>>
        /// Module -> the packages it covers (`budgetPackages`).
        Packages: Map<string, string list>
        /// The changed paths, repository-relative.
        Changed: string list
        /// The leg scripts among `Changed` whose CODE moved (`scriptCode`).
        ScriptCodeMoved: Set<string>
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

type internal ConeCause =
    {
        Target: string
        Why: string
        /// Only a change to the MODEL reaches the modules that reference it: an oracle, a covered
        /// source or a budget entry is read by its own module's step and by nobody else's.
        Reaches: bool
    }

/// Every module the cone's MEMBERS reference, however transitively, that is not a member itself,
/// with the reason it is checked: it goes to the kit as a DEPENDENCY, ahead of the module that needs
/// it. On a cold cache the prover checks it anyway before the module that references it, and the
/// extraction of that module needs its checked file; listing it keeps every module's clock its own.
/// The ONE closure both cones use — `-Since`'s and the hand cone `-Modules` names (Phase 522).
let internal coneDependencies (facts: ConeFacts) (members: Set<string>) : Map<string, string> =
    let refs m =
        facts.References |> Map.tryFind m |> Option.defaultValue Set.empty

    let dependencies = System.Collections.Generic.Dictionary<string, string>()

    for m in facts.Roster do
        if members.Contains m then
            let stack = System.Collections.Generic.Stack<string>(refs m)
            let seen = System.Collections.Generic.HashSet<string>()

            while stack.Count > 0 do
                let r = stack.Pop()

                if seen.Add r then
                    if not (members.Contains r) && not (dependencies.ContainsKey r) then
                        dependencies[r] <-
                            sprintf "a dependency of %s, checked ahead of it so every module's clock is its own" m

                    for x in refs r do
                        stack.Push x

    dependencies |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

/// The cone, one row per registered module, in the roster's order.
let computeCone (facts: ConeFacts) : ConeRow list =
    let refs m =
        facts.References |> Map.tryFind m |> Option.defaultValue Set.empty

    let causes = ResizeArray<ConeCause>()
    let shared = ResizeArray<string>()

    for path in facts.Changed do
        match classifyConePath facts.Roster facts.Shared path with
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

        let dependencies = coneDependencies facts (Set.ofSeq reasons.Keys)

        facts.Roster
        |> List.map (fun m ->
            match reasons.TryGetValue m with
            | true, why ->
                { Module = m
                  Verdict = In
                  Reason = why }
            | _ ->
                match dependencies.TryFind m with
                | Some why ->
                    { Module = m
                      Verdict = Dependency
                      Reason = why }
                | _ ->
                    { Module = m
                      Verdict = Out
                      Reason = "nothing it reads changed" })

/// The cone `check.ps1 -Modules` names by hand (Phase 522): the named modules IN, and every module
/// they reference as a DEPENDENCY, by the same closure `-Since` uses. Until Phase 522 a hand cone was
/// exactly what it named, and a named model whose shared modules were not named failed at EXTRACT as
/// an apparatus fault: its own extraction needs their checked files, and nothing had checked them.
let handCone (facts: ConeFacts) (named: string list) : ConeRow list =
    let members = Set.ofList named
    let dependencies = coneDependencies facts members

    facts.Roster
    |> List.map (fun m ->
        if members.Contains m then
            { Module = m
              Verdict = In
              Reason = "named by -Modules" }
        else
            match dependencies.TryFind m with
            | Some why ->
                { Module = m
                  Verdict = Dependency
                  Reason = why }
            | None ->
                { Module = m
                  Verdict = Out
                  Reason = "not named, and no named module references it" })

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

let internal runGit (root: string) (args: string list) : int * string * string =
    match
        ChildProcess.argv "git" ([ "-C"; root; "-c"; "core.quotepath=off" ] @ args)
        |> ChildProcess.run
    with
    | Ok o -> o.Exit, o.Stdout, o.Stderr
    | Error why -> failwith why

let internal gitLines (root: string) (args: string list) : Result<string list, string> =
    match runGit root args with
    | 0, out, _ ->
        Ok(
            out.Replace("\r\n", "\n").Split '\n'
            |> Array.filter (fun l -> l <> "")
            |> List.ofArray
        )
    | code, _, err -> Error(sprintf "git %s exited %d: %s" (String.concat " " args) code (err.Trim()))

let internal resolveCommit (root: string) (tree: string) : Result<string, string> =
    match runGit root [ "rev-parse"; "--verify"; "--quiet"; tree + "^{commit}" ] with
    | 0, out, _ -> Ok(out.Trim())
    | _ -> Error(sprintf "'%s' names no commit this clone holds" tree)

/// A file's text at a commit, or "" where the commit has no such file.
let internal textAt (root: string) (commit: string) (path: string) : string =
    match runGit root [ "show"; commit + ":" + path ] with
    | 0, out, _ -> out
    | _ -> ""

let internal workingText (root: string) (path: string) : string =
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
/// The roster half of the cone's facts — the modules, the shared ones, the reference graph and the
/// packages — with nothing changed. The hand cone needs only this; `-Since` adds what changed.
let coneRosterFacts (root: string) : Result<ConeFacts, string> =
    let budgetNow = workingText root "proofs/modules.json"

    match parseRoster budgetNow with
    | Error errors ->
        Error(
            sprintf
                "proofs/modules.json is not a readable roster, so there is no roster to select from: %s"
                (String.concat "; " errors)
        )
    | Ok [] -> Error "proofs/modules.json registers no module, so there is no roster to select from"
    | Ok entries ->
        let roster = entries |> List.map _.Module

        Ok
            { Roster = roster
              Shared =
                entries
                |> List.filter (fun e -> e.Kind = Shared)
                |> List.map _.Module
                |> Set.ofList
              References = modelReferenceGraph root roster
              Packages = budgetPackages budgetNow
              Changed = []
              ScriptCodeMoved = Set.empty
              EntriesMoved = Set.empty }

let coneFactsSince (root: string) (commit: string) : Result<ConeFacts, string> =
    let tracked =
        gitLines root [ "diff"; "--name-only"; "--no-renames"; "--relative"; commit ]

    let untracked = gitLines root [ "ls-files"; "--others"; "--exclude-standard" ]

    match tracked, untracked with
    | Error e, _
    | _, Error e -> Error e
    | Ok tracked, Ok untracked ->
        let changed = tracked @ untracked |> List.distinct |> List.sort
        let budgetNow = workingText root "proofs/modules.json"

        coneRosterFacts root
        |> Result.map (fun facts ->
            { facts with
                Changed = changed
                ScriptCodeMoved =
                    changed
                    |> List.filter (fun p ->
                        List.contains p legScripts
                        && scriptCode (workingText root p) <> scriptCode (textAt root commit p))
                    |> Set.ofList
                EntriesMoved =
                    if List.contains "proofs/modules.json" changed then
                        budgetEntriesChanged (textAt root commit "proofs/modules.json") budgetNow
                    else
                        Set.empty })

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

let internal short (sha: string) =
    if sha.Length > 12 then sha.Substring(0, 12) else sha

/// What a strict run's verdict record says about its run, for the line an empty cone prints: when it
/// ran, on what machine, each run's contention factor and the modules over budget. Every part is
/// optional, because the record is what a run wrote, not a schema this line enforces.
let describeStrictRun (record: JsonElement) : string =
    let str (e: JsonElement) (name: string) =
        match e.TryGetProperty name with
        | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
        | _ -> None

    let parts =
        [ match str record "recordedAt" with
          | Some at -> sprintf "run %s" at
          | None -> ()
          match record.TryGetProperty "machine" with
          | true, m when m.ValueKind = JsonValueKind.Object ->
              let what =
                  match str m "kind", str m "runner" with
                  | Some "ci", Some runner -> sprintf "CI runner %s" runner
                  | Some "ci", None -> "a CI runner"
                  | Some "local", _ -> "a local machine"
                  | Some other, _ -> other
                  | None, _ -> "a machine"

              let processors =
                  match m.TryGetProperty "processors" with
                  | true, p when p.ValueKind = JsonValueKind.Number -> Some(sprintf "%d processors" (p.GetInt32()))
                  | _ -> None

              match [ str m "os"; processors ] |> List.choose id with
              | [] -> sprintf "on %s" what
              | details -> sprintf "on %s (%s)" what (String.concat ", " details)
          | _ -> ()
          match record.TryGetProperty "contention" with
          | true, c when c.ValueKind = JsonValueKind.Array ->
              let factors =
                  [ for r in c.EnumerateArray() do
                        match r.TryGetProperty "factor" with
                        | true, f when f.ValueKind = JsonValueKind.Number -> sprintf "x%.2f" (f.GetDouble())
                        | _ -> "not computed" ]

              if not (List.isEmpty factors) then
                  sprintf "contention %s" (String.concat ", " factors)
          | _ -> ()
          // A cost overrun is recorded, never red, so the line names the modules that ran over
          // budget and the worst percentage each reached.
          match record.TryGetProperty "costs" with
          | true, c when c.ValueKind = JsonValueKind.Array ->
              let over =
                  [ for r in c.EnumerateArray() do
                        match str r "module", r.TryGetProperty "percent" with
                        | Some m, (true, pc) when pc.ValueKind = JsonValueKind.Number && pc.GetInt32() > 100 ->
                            m, pc.GetInt32()
                        | _ -> () ]
                  |> List.groupBy fst
                  |> List.map (fun (m, xs) -> sprintf "%s %d%%" m (xs |> List.map snd |> List.max))

              if List.isEmpty over then
                  "every module within budget"
              else
                  sprintf "over budget: %s" (String.concat ", " over)
          | _ -> () ]

    if List.isEmpty parts then
        ""
    else
        " — " + String.concat ", " parts

/// Why a run verdict record cannot stand behind an empty cone, or `None` when it can: it must be the
/// kit's verdict (`kind: proofRunVerdict`) of a GREEN, STRICT, FULL run (`cone` null) — the shape
/// `recordStrictBaseline` writes, and the one the strict workflow hands over.
let strictVerdictFault (record: JsonElement) : string option =
    let member' (name: string) =
        match record.TryGetProperty name with
        | true, v -> Some v
        | _ -> None

    let text name =
        member' name
        |> Option.filter (fun v -> v.ValueKind = JsonValueKind.String)
        |> Option.map (fun v -> v.GetString())

    if text "kind" <> Some "proofRunVerdict" then
        Some "is not a run verdict (`kind: proofRunVerdict`)"
    elif text "verdict" <> Some "green" then
        Some(sprintf "records a %s run, not a green one" (defaultArg (text "verdict") "verdict-less"))
    elif not (member' "strict" |> Option.exists (fun v -> v.ValueKind = JsonValueKind.True)) then
        Some "records a run that was not -Strict"
    elif member' "cone" |> Option.exists (fun v -> v.ValueKind <> JsonValueKind.Null) then
        Some "records a cone run, not the FULL leg"
    else
        None

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
        let tree, fault, run =
            try
                use doc = JsonDocument.Parse(File.ReadAllText file)

                let tree =
                    match doc.RootElement.TryGetProperty "tree" with
                    | true, t when t.ValueKind = JsonValueKind.String -> Some(t.GetString())
                    | _ -> None

                tree, strictVerdictFault doc.RootElement, describeStrictRun doc.RootElement
            with :? JsonException ->
                None, Some "is not JSON", ""

        match tree, fault with
        | _, Some why -> Error(sprintf "the leg has NO STRICT BASELINE: %s %s" coneBaselinePath why)
        | None, None -> Error(sprintf "the leg has NO STRICT BASELINE: %s names no `tree`" coneBaselinePath)
        | Some tree, None ->
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
                                    "the cone is EMPTY, and the last -Strict full run (%s%s, recorded in %s) stands behind it: nothing a registered module reads has changed since the tree it verified"
                                    (short commit)
                                    run
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

let internal writeJson (path: string) (write: Utf8JsonWriter -> unit) =
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
///
/// The record IS the run's verdict (Phase 522): `verdictFile` is the record the kit wrote and
/// `check.ps1` completed (`proofs/obj/last-run.json`), copied member for member beneath the tree it
/// verified and when, so a baseline and any other run are read by one reader. A verdict that is not
/// a green, strict, full run is refused rather than recorded: the baseline must be what it says.
let recordStrictBaseline (root: string) (recordedAt: DateTimeOffset) (verdictFile: string) : Result<string, string> =
    let verdict =
        try
            let doc = JsonDocument.Parse(File.ReadAllText verdictFile)

            match strictVerdictFault doc.RootElement with
            | None -> Ok doc
            | Some why ->
                doc.Dispose()
                Error(sprintf "NOT recorded: the run verdict in %s %s" verdictFile why)
        with
        | :? JsonException
        | :? IOException as e ->
            Error(sprintf "NOT recorded: the run verdict in %s could not be read: %s" verdictFile e.Message)

    match verdict with
    | Error why -> Error why
    | Ok doc ->
        use doc = doc

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
                    writeJson (Path.Combine(root, coneBaselinePath)) (fun w ->
                        w.WriteString("kind", "proofRunVerdict")

                        w.WriteString(
                            "$comment",
                            "The run verdict of the last green -Strict FULL run of the proof leg, with the tree it verified, written by proofs/check.ps1 and committed by whoever ran it. An EMPTY module cone (check.ps1 -Since <tree>) is green only when `tree` is an ancestor of HEAD and the cone against it is empty too. Never hand-edited: a record no run produced is exactly the stale evidence the selector must not lean on."
                        )

                        w.WriteString("tree", head)
                        w.WriteString("recordedAt", recordedAt.UtcDateTime.ToString "yyyy-MM-ddTHH:mm:ssZ")

                        for p in doc.RootElement.EnumerateObject() do
                            if p.Name <> "kind" && p.Name <> "tree" && p.Name <> "recordedAt" then
                                p.WriteTo w)

                    let runs =
                        match doc.RootElement.TryGetProperty "runs" with
                        | true, r when r.ValueKind = JsonValueKind.Number -> r.GetInt32()
                        | _ -> 0

                    Ok(
                        sprintf
                            "strict baseline recorded in %s: the -Strict full run of %s (%d run(s), %d modules). Commit the file."
                            coneBaselinePath
                            (short head)
                            runs
                            facts.Roster.Length
                    )

/// `dotnet run --project tools/Fuaran.Core.Tools -- proof-cone ...`, the half of
/// `proofs/check.ps1 -Since` that decides. Its answer goes to the `--out` file as JSON, printed
/// lines included, so `check.ps1` prints them itself and no console code page sits between the two.
///   --since <tree> --out <file>          the cone, and for an empty one the baseline verdict
///   --modules <a,b,...> --out <file>     the hand cone: the named modules and what they reference
///   --record-strict --verdict <file> --out <file>
///                                        record a green -Strict full run's verdict (Phase 522) as
///                                        the baseline
/// Exit 0 when the answer was computed, whatever it is; 2 when it could not be.
let coneCli (args: string list) : int =
    match args with
    | [ "--since"; since; "--out"; out ] ->
        match coneSince (Repo.root ()) since with
        | Error e ->
            eprintfn "==== proofs: the cone could not be computed: %s" e
            2
        | Ok report ->
            let checkedModules = coneChecked report.Rows

            let verdict, message =
                if not (List.isEmpty checkedModules) then
                    "verify", ""
                else
                    match emptyConeBaseline (Repo.root ()) with
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
    | [ "--modules"; named; "--out"; out ] ->
        match coneRosterFacts (Repo.root ()) with
        | Error e ->
            eprintfn "==== proofs: the cone could not be computed: %s" e
            2
        | Ok facts ->
            let names =
                named.Split([| ','; ' ' |], StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                |> List.ofArray

            let unknown = names |> List.filter (fun n -> not (List.contains n facts.Roster))

            let rows = handCone facts names
            let checkedModules = coneChecked rows

            let verdict, message =
                if not (List.isEmpty unknown) then
                    "refused",
                    sprintf
                        "-Modules names %s, which modules.json does not register; the registered modules are %s"
                        (String.concat ", " unknown)
                        (String.concat ", " facts.Roster)
                elif List.isEmpty names then
                    "refused", "-Modules names no module"
                else
                    "verify", ""

            writeJson out (fun w ->
                w.WriteString("verdict", verdict)
                w.WriteString("message", message)
                w.WriteStartArray "verify"

                for m in checkedModules do
                    w.WriteStringValue m

                w.WriteEndArray()
                w.WriteStartArray "lines"

                for line in renderCone "named by hand (-Modules)" rows do
                    w.WriteStringValue line

                w.WriteEndArray())

            0
    | [ "--record-strict"; "--verdict"; verdictFile; "--out"; out ] ->
        let recorded, message =
            match recordStrictBaseline (Repo.root ()) DateTimeOffset.UtcNow verdictFile with
            | Ok line -> true, line
            | Error why -> false, why

        writeJson out (fun w ->
            w.WriteBoolean("recorded", recorded)
            w.WriteString("message", message))

        0
    | _ ->
        eprintfn
            "usage: proof-cone --since <tree> --out <file> | proof-cone --modules <a,b,...> --out <file> | proof-cone --record-strict --verdict <file> --out <file>"

        2
