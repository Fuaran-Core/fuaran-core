module Fuaran.Core.Tests.PackageRosterTests

// ---------------------------------------------------------------------------
// Phase 199 — the package roster is DERIVED, and STABILITY.md's header is held to
// `<Version>`.
//
// `README.md`'s package table and `STABILITY.md`'s entry headers are hand-maintained
// documents sitting beside the mechanism they describe, so they drift silently and are
// discovered by a reader rather than by a gate. Measured 2026-09-17: the table listed
// twelve packages against twenty-one packable projects, and two RELEASED slots still
// carried the "no `vX.Y.Z` tag exists yet" sentence they were cut with.
//
// The derived-registry rule — declare local, project central — applies inside one
// repository too: the project files declare what ships, and the table is a projection of
// them. So this file asserts the ROSTER and nothing else. What a package is FOR stays
// prose a person writes; a gate that generated that column would be describing the file
// layout rather than the design.
//
// Each property has a go-red case beside it over synthetic input. The live case
// alone cannot show a classifier works — every one of these reads a real file that is
// expected to be correct, so a classifier that matched nothing would pass them all.
//
//   1. README rows = the packable set.
//   2. No packable project sits outside `src/` — the SCOPE property 1's derivation assumes,
//      checked rather than trusted. Without it a package added elsewhere is invisible to
//      property 1 by being out of frame, which is the same drift wearing a different hat.
//   3. Every row of the contract's "Where sections moved" table resolves to a heading of the
//      file it names (Phase 397).
//   4. The README states no bare count of tests (Phase 233) — a figure nothing asserts is a
//      claim the document cannot keep true; the suite's own census is docs/conformance-families.md.
//   5. The post-push registry probe (`.github/scripts/probe-registry.ps1`, Phase 393) derives the
//      same roster — it asks nuget.org about exactly the ids property 1 holds the README to.
//
// The release ledger's clauses (Phases 205 and 397: a slot file per tag and for the standing
// `<Version>`, each with its own heading, DRAFT only on the standing version, no "no tag exists"
// sentence surviving its tag, the contract under its ceiling, the props file pointing at the index)
// left this family in Phase 503 for `LedgerTests`, which reads the slot files once (`Ledger.fs`).
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

/// A project under `src/` that `pack` would produce a package from.
type internal PackableProject =
    {
        /// The package id — `<PackageId>` where declared, else the project file's base name.
        PackageId: string
        /// Repo-relative, forward-slashed.
        ProjectFile: string
    }

let private isPackableRe =
    Regex(@"<IsPackable>([^<]*)</IsPackable>", RegexOptions.Compiled)

let private packageIdRe =
    Regex(@"<PackageId>([^<]*)</PackageId>", RegexOptions.Compiled)

let private firstGroup (re: Regex) (text: string) : string option =
    let m = re.Match text

    if m.Success then
        let v = m.Groups[1].Value.Trim()
        if v = "" then None else Some v
    else
        None

/// `IsPackable` for one project: what the project declares, else what
/// `Directory.Build.props` declares, else MSBuild's own default (true). Only the literal
/// `false` excludes — anything else, including a value this reader does not understand,
/// leaves the project IN the roster, so a misread widens the table rather than silently
/// dropping a shipping package from it.
let internal isPackable (fallback: string option) (projectText: string) : bool =
    match firstGroup isPackableRe projectText |> Option.orElse fallback with
    | Some v -> not (String.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
    | None -> true

/// THE derivation, kept in one small function on purpose: the Fable surface's completeness
/// check (Phase 185) derives the same set, and two spellings of "what ships" would drift
/// exactly the way the documents this file gates drifted. `src/*/*.fsproj` +
/// `src/*/*.csproj` — a C# project is a published package too (the C# facade was one, from
/// Phase 128 until Phase 231 removed it at `0.33.0`), and a roster scoped to F# would leave one
/// undocumented by construction, which is the same excusal-by-the-shape-of-the-derivation that
/// 185's own header names. Checked against that sibling: it covers every project type under
/// `src/` and names a packable C# project as an explicit exclusion, so the two rosters range
/// over one set.
let internal packableProjects (root: string) : PackableProject list =
    let srcDir = Path.Combine(root, "src")

    let fallback =
        let props = Path.Combine(root, "Directory.Build.props")

        if File.Exists props then
            firstGroup isPackableRe (File.ReadAllText props)
        else
            None

    if not (Directory.Exists srcDir) then
        []
    else
        Directory.GetDirectories srcDir
        |> Array.collect (fun d -> Array.append (Directory.GetFiles(d, "*.fsproj")) (Directory.GetFiles(d, "*.csproj")))
        |> Array.toList
        |> List.choose (fun path ->
            let text = File.ReadAllText path

            if isPackable fallback text then
                Some
                    { PackageId =
                        firstGroup packageIdRe text
                        |> Option.defaultValue (Path.GetFileNameWithoutExtension path)
                      ProjectFile = Path.GetRelativePath(root, path).Replace('\\', '/') }
            else
                None)
        |> List.sortBy _.PackageId

/// Every project file in the tree that is NOT under `src/`, paired with its packability —
/// the instrument for the scope property.
let internal projectsOutsideSrc (root: string) : (string * bool) list =
    let fallback =
        let props = Path.Combine(root, "Directory.Build.props")

        if File.Exists props then
            firstGroup isPackableRe (File.ReadAllText props)
        else
            None

    [ yield! Directory.GetFiles(root, "*.fsproj", SearchOption.AllDirectories)
      yield! Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories) ]
    |> List.map (fun p -> Path.GetRelativePath(root, p).Replace('\\', '/'), p)
    |> List.filter (fun (rel, _) -> not (rel.StartsWith("src/", StringComparison.Ordinal)))
    |> List.map (fun (rel, full) -> rel, isPackable fallback (File.ReadAllText full))
    |> List.sortBy fst

// ---- README ---------------------------------------------------------------

/// The package ids the README's `## Packages` table lists, in file order. The first cell's
/// backticked token is the id; the header row and the `|---|` separator carry no backticks
/// and drop out on their own.
let internal readmePackageIds (readme: string) : string list =
    let lines = readme.Replace("\r\n", "\n").Split('\n') |> Array.toList

    let rec collect (acc: string list) (inSection: bool) (ls: string list) =
        match ls with
        | [] -> List.rev acc
        | (l: string) :: rest ->
            if l.StartsWith("## ", StringComparison.Ordinal) then
                if l.Trim() = "## Packages" then collect acc true rest
                elif inSection then List.rev acc
                else collect acc false rest
            elif inSection then
                collect (l :: acc) true rest
            else
                collect acc false rest

    collect [] false lines
    |> List.filter (fun l -> l.TrimStart().StartsWith("|", StringComparison.Ordinal))
    |> List.choose (fun l ->
        let cells = l.Trim().Trim('|').Split('|')

        if cells.Length = 0 then
            None
        else
            let m = Regex.Match(cells[0], "`([^`]+)`")
            if m.Success then Some(m.Groups[1].Value.Trim()) else None)

/// Every bare count of tests a document states — a number, or a number word, then at most two
/// words, then `tests` / `test cases` / `assertions` ("398 conformance tests", "1,334 testCases").
/// The suite cannot hold such a figure to what it actually runs, so the README states none
/// (Phase 233); the claim it makes instead — every law family is exercised and non-vacuous at the
/// reference witness — is asserted by `ConformanceVacuityTests`, and the generated
/// `docs/conformance-families.md` is the roster.
let internal bareTestCounts (text: string) : string list =
    let number =
        @"\d[\d,]*|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|dozen|hundred|thousand"

    Regex.Matches(
        text,
        @"\b(?:"
        + number
        + @")\s+(?:[\w`.\-]+\s+){0,2}(?:tests|test\s+cases|testcases|assertions)\b",
        RegexOptions.IgnoreCase
    )
    |> Seq.map _.Value
    |> Seq.toList

/// `(shipping but undocumented, documented but not shipping)`.
let internal rosterDiff (packable: string list) (documented: string list) : string list * string list =
    let p = Set.ofList packable
    let d = Set.ofList documented
    Set.difference p d |> Set.toList, Set.difference d p |> Set.toList

// ---- STABILITY ------------------------------------------------------------

/// The standing `<Version>` from `Directory.Build.props`.
let internal standingVersion (propsText: string) : string option =
    firstGroup (Regex @"<Version>([^<]*)</Version>") propsText

/// GitHub's anchor for a heading: the text lower-cased, link targets and backslash escapes dropped,
/// every character that is not a letter, digit, space, `-` or `_` removed, spaces made `-`.
let internal slugBase (heading: string) : string =
    let t = heading.TrimStart('#').Trim()

    let t =
        Regex.Replace(t, @"\[([^\]]*)\]\([^)]*\)", "$1").Replace("\\", "").ToLowerInvariant()

    let kept =
        t
        |> Seq.filter (fun c -> Char.IsLetterOrDigit c || c = ' ' || c = '-' || c = '_')
        |> Seq.toArray

    String(kept).Replace(' ', '-')

/// Every heading anchor of a document, numbered for duplicates as GitHub numbers them; fenced code
/// is not read.
let internal headingAnchors (text: string) : Set<string> =
    let seen = Collections.Generic.Dictionary<string, int>()

    let folder (fence: bool, acc: string list) (l: string) =
        if l.TrimStart().StartsWith("```", StringComparison.Ordinal) then
            not fence, acc
        elif fence || not (Regex.IsMatch(l, @"^#{1,6} ")) then
            fence, acc
        else
            let b = slugBase l

            let n =
                match seen.TryGetValue b with
                | true, n -> n
                | _ -> 0

            seen[b] <- n + 1
            fence, (if n = 0 then b else sprintf "%s-%d" b n) :: acc

    text.Replace("\r\n", "\n").Split('\n')
    |> Array.fold folder (false, [])
    |> snd
    |> Set.ofList

let private tableAnchorRe =
    Regex(@"`#(?<old>[^`]+)`(?:\s*→\s*`#(?<new>[^`]+)`)?", RegexOptions.Compiled)

let private tableTargetRe =
    Regex(@"^\| \[`(?<file>[^`]+)`\]\([^)]*\) \|", RegexOptions.Compiled)

/// The rows of the contract's "Where sections moved" table: `(target file, old anchor, the anchor
/// it has there)`. A row naming `this file` targets the contract itself.
let internal movedAnchorRows (contract: string) : (string * string * string) list =
    let lines = contract.Replace("\r\n", "\n").Split('\n')

    match lines |> Array.tryFindIndex (fun l -> l = "## Where sections moved") with
    | None -> []
    | Some start ->
        lines[start + 1 ..]
        |> Array.takeWhile (fun l -> not (l.StartsWith("## ", StringComparison.Ordinal)))
        |> Array.filter (fun l -> l.StartsWith("| ", StringComparison.Ordinal))
        |> Array.toList
        |> List.collect (fun l ->
            let target =
                if l.StartsWith("| this file |", StringComparison.Ordinal) then
                    Some "STABILITY.md"
                else
                    let m = tableTargetRe.Match l
                    if m.Success then Some m.Groups["file"].Value else None

            match target with
            | None -> []
            | Some file ->
                [ for m in tableAnchorRe.Matches l ->
                      let old = m.Groups["old"].Value

                      let there =
                          if m.Groups["new"].Success then
                              m.Groups["new"].Value
                          else
                              old

                      file, old, there ])

/// Every row of the moved-anchor table whose anchor is not a heading of the file it names, given
/// each file's text (`None` for a file that does not exist); and every old anchor mapped twice.
let internal movedAnchorFaults (read: string -> string option) (rows: (string * string * string) list) : string list =
    let unresolved =
        rows
        |> List.choose (fun (file, old, there) ->
            match read file with
            | None -> Some(sprintf "`#%s` is mapped to %s, which does not exist" old file)
            | Some text when not ((headingAnchors text).Contains there) ->
                Some(sprintf "`#%s` is mapped to %s#%s, which is not a heading anchor there" old file there)
            | Some _ -> None)

    let twice =
        rows
        |> List.countBy (fun (_, old, _) -> old)
        |> List.filter (fun (_, n) -> n > 1)
        |> List.map (fun (old, n) -> sprintf "`#%s` is mapped %d times" old n)

    unresolved @ twice

/// Lines of `Directory.Build.props` that open a version-history note (`<!-- 0.12.0 (2026-08-21): …`)
/// — the second ledger the props file kept by hand until Phase 397.
let internal propsHistoryLines (props: string) : string list =
    props.Replace("\r\n", "\n").Split('\n')
    |> Array.toList
    |> List.filter (fun l -> Regex.IsMatch(l, @"^\s*<!--\s*\d+\.\d+\.\d+\s*\(\d{4}-\d{2}-\d{2}\)"))

// ---- the instruments ------------------------------------------------------

let private withRoot (f: string -> unit) =
    match Repo.tryRoot () with
    | None -> failtest "could not locate the repository root (Fuaran.Core.slnx) from the CWD or the test binary"
    | Some root -> f root

// ---- the registry probe's roster (Phase 393) ------------------------------

/// The roster `.github/scripts/probe-registry.ps1 -ListRoster` derives from `root`, run the way
/// the publish workflow runs it, or why it could not be read. The probe asks nuget.org about
/// exactly these ids after a push, so a probe whose derivation drifted from `packableProjects`
/// would verify a different list than the one the README table is gated on.
let private probeRoster (repo: string) (root: string) : Result<string list, string> =
    let probe = Path.Combine(repo, ".github", "scripts", "probe-registry.ps1")

    match
        ChildProcess.argv
            "pwsh"
            [ "-NoProfile"
              "-NonInteractive"
              "-File"
              probe
              "-ListRoster"
              "-Root"
              root ]
        |> ChildProcess.run
    with
    | Error why -> Error why
    | Ok o when o.Exit <> 0 ->
        Error(sprintf "the probe's -ListRoster exited %d: %s" o.Exit ((o.Stderr + o.Stdout).Trim()))
    | Ok o ->
        o.Stdout.Split('\n')
        |> Array.toList
        |> List.map _.Trim()
        |> List.filter (fun l -> l <> "")
        |> Ok

// ---- the Fable-surface cross-check ---------------------------------------

/// The package ids `fable-exclusions.json` declares (Phase 185; at the repository
/// root since Phase 217), or `None` when its shape is not one this reader recognises.
/// Tolerant of the shapes it was written against before that file existed; the live check fails
/// on `None`.
let internal declaredExclusions (json: string) : Set<string> option =
    let stringsOf (el: JsonElement) =
        if el.ValueKind = JsonValueKind.Array then
            let items = el.EnumerateArray() |> Seq.toList

            if items |> List.forall (fun i -> i.ValueKind = JsonValueKind.String) then
                Some(items |> List.map _.GetString() |> Set.ofList)
            else
                let named =
                    items
                    |> List.choose (fun i ->
                        if i.ValueKind <> JsonValueKind.Object then
                            None
                        else
                            [ "package"; "packageId"; "id"; "project" ]
                            |> List.tryPick (fun k ->
                                match i.TryGetProperty k with
                                | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                                | _ -> None))

                if named.Length = items.Length && not items.IsEmpty then
                    Some(Set.ofList named)
                else
                    None
        else
            None

    try
        use doc = JsonDocument.Parse json
        let root = doc.RootElement

        match stringsOf root with
        | Some s -> Some s
        | None when root.ValueKind = JsonValueKind.Object ->
            root.EnumerateObject() |> Seq.tryPick (fun p -> stringsOf p.Value)
        | None -> None
    with _ ->
        None

// ---- the suite ------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "Package roster"
        [

          test "the README package table lists exactly the packable projects" {
              withRoot (fun root ->
                  let packable = packableProjects root |> List.map _.PackageId

                  let documented =
                      readmePackageIds (File.ReadAllText(Path.Combine(root, "README.md")))

                  Expect.isNonEmpty packable "at least one packable project was found under src/"

                  Expect.isNonEmpty
                      documented
                      "the README's `## Packages` table was located and at least one row parsed — an empty read here would make the comparison below vacuous"

                  match rosterDiff packable documented with
                  | [], [] -> ()
                  | missing, surplus ->
                      let part label items =
                          if List.isEmpty items then
                              ""
                          else
                              sprintf "\n       %s: %s" label (String.concat ", " items)

                      failtestf
                          "the README package table is not the packable set.%s%s\n       Remedy: edit the table in README.md — its rows are a projection of `src/*/*.fsproj` + `src/*/*.csproj` whose `IsPackable` is not `false`. A project that genuinely should not ship declares `<IsPackable>false</IsPackable>` instead of being left out of the table."
                          (part "shipping but undocumented" missing)
                          (part "documented but not shipping" surplus))
          }

          test "no packable project sits outside src/" {
              // The scope property 1 assumes. Left unchecked, a packable project added
              // anywhere else is outside the roster's frame and so can never be reported
              // missing from the README — the same drift, invisible to the check written
              // to catch it.
              withRoot (fun root ->
                  let outside = projectsOutsideSrc root
                  Expect.isNonEmpty outside "project files outside src/ were found at all (tests, samples, proofs)"

                  match outside |> List.filter snd |> List.map fst with
                  | [] -> ()
                  | packable ->
                      failtestf
                          "%d project(s) outside src/ are packable, so the README roster derivation cannot see them: %s\n       Remedy: declare `<IsPackable>false</IsPackable>`, or move the project under src/ and add its README row."
                          packable.Length
                          (String.concat ", " packable))
          }

          test "the registry probe derives the same roster as the README gate" {
              // Phase 393. The post-push probe in publish-packages.yml runs without the suite, so
              // it carries its own derivation in PowerShell; this holds the two to one set.
              withRoot (fun root ->
                  let packable = packableProjects root |> List.map _.PackageId |> Set.ofList

                  match probeRoster root root with
                  | Error e -> failtest e
                  | Ok probed ->
                      Expect.isNonEmpty probed "the probe listed at least one id"

                      Expect.equal
                          (Set.ofList probed)
                          packable
                          "the registry probe's roster is the packable set — edit .github/scripts/probe-registry.ps1 to match `packableProjects`, never the reverse")
          }

          test "the registry probe's derivation agrees on the edge cases (go-red over a synthetic tree)" {
              // The live case reads a tree in which every project declares IsPackable and
              // PackageId explicitly, so it cannot see the fallbacks. This tree exercises each:
              // the Directory.Build.props fallback (False, any case, excludes), an explicit true
              // overriding it, a C# project, a PackageId differing from the file name, and the
              // file name standing in for an absent PackageId.
              withRoot (fun repo ->
                  let dir =
                      Path.Combine(Path.GetTempPath(), sprintf "probe-roster-%d" Environment.ProcessId)

                  try
                      let write (rel: string) (text: string) =
                          let path = Path.Combine(dir, rel)
                          Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
                          File.WriteAllText(path, text)

                      write
                          "Directory.Build.props"
                          "<Project><PropertyGroup><IsPackable>False</IsPackable></PropertyGroup></Project>"

                      write "src/Hidden/Hidden.fsproj" "<Project></Project>"

                      write
                          "src/Named/Named.fsproj"
                          "<Project><PropertyGroup><IsPackable>true</IsPackable><PackageId>Other.Id</PackageId></PropertyGroup></Project>"

                      write
                          "src/Bare/Bare.csproj"
                          "<Project><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>"

                      write
                          "tests/Outside/Outside.fsproj"
                          "<Project><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>"

                      let expected = set [ "Bare"; "Other.Id" ]

                      Expect.equal
                          (packableProjects dir |> List.map _.PackageId |> Set.ofList)
                          expected
                          "packableProjects over the synthetic tree"

                      match probeRoster repo dir with
                      | Error e -> failtest e
                      | Ok probed -> Expect.equal (Set.ofList probed) expected "the probe over the synthetic tree"
                  finally
                      if Directory.Exists dir then
                          Directory.Delete(dir, true))
          }

          test "the Fable surface's declared roster — packable minus its exclusions — is documented in the README" {
              // Phase 185's cross-check, over the DECLARED surface since Phase 217 moved the Fable
              // compile out of this repository: the packages a Fable consumer can compile are the
              // packable ones minus `fable-exclusions.json`, and each is a package a
              // reader must be able to find. The file is this repository's own now, so its absence
              // or an unrecognised shape is a failure rather than a skip.
              withRoot (fun root ->
                  let exclusionsPath = Path.Combine(root, "fable-exclusions.json")

                  Expect.isTrue (File.Exists exclusionsPath) "fable-exclusions.json exists"

                  match declaredExclusions (File.ReadAllText exclusionsPath) with
                  | None -> failtest "fable-exclusions.json holds no array of package ids this reader recognises"
                  | Some excluded ->
                      let documented =
                          readmePackageIds (File.ReadAllText(Path.Combine(root, "README.md")))
                          |> Set.ofList

                      let declared =
                          packableProjects root
                          |> List.map _.PackageId
                          |> List.filter (excluded.Contains >> not)

                      Expect.isNonEmpty declared "the declared Fable surface is not empty"

                      match declared |> List.filter (documented.Contains >> not) with
                      | [] -> ()
                      | absent ->
                          failtestf
                              "%d package(s) on the declared Fable surface are absent from the README table: %s"
                              absent.Length
                              (String.concat ", " absent))
          }

          test "the moved-anchor table resolves: every old anchor is a heading of the file it names" {
              withRoot (fun root ->
                  let contract = File.ReadAllText(Path.Combine(root, "STABILITY.md"))
                  let rows = movedAnchorRows contract

                  Expect.isGreaterThan
                      rows.Length
                      0
                      "the \"Where sections moved\" table was found and parsed — an empty read would pass vacuously"

                  let read (file: string) =
                      let path = Path.Combine(root, file)

                      if File.Exists path then
                          Some(File.ReadAllText path)
                      else
                          None

                  match movedAnchorFaults read rows with
                  | [] -> ()
                  | faults ->
                      failtestf
                          "%d moved-anchor row(s) do not resolve:\n       %s\n       Remedy: the table is permanent — a heading that moves again is re-pointed in its row, never dropped from it."
                          faults.Length
                          (String.concat "\n       " faults))
          }

          // ---- the go-red controls ------------------------------------------
          //
          // Each live case above reads a file expected to be correct, so all four would
          // pass against a classifier that matched nothing at all. These feed the same
          // functions input that must be refused.

          test "the roster diff reports a removed README row and a surplus one" {
              let packable = [ "Fuaran.Core.Tree"; "Fuaran.Core.Ops"; "Fuaran.Core.Wire" ]

              Expect.equal
                  (rosterDiff packable [ "Fuaran.Core.Tree"; "Fuaran.Core.Ops"; "Fuaran.Core.Wire" ])
                  ([], [])
                  "an exact table reports nothing"

              Expect.equal
                  (rosterDiff packable [ "Fuaran.Core.Tree"; "Fuaran.Core.Ops" ])
                  ([ "Fuaran.Core.Wire" ], [])
                  "a packable project removed from the table is reported as undocumented"

              Expect.equal
                  (rosterDiff
                      packable
                      [ "Fuaran.Core.Tree"
                        "Fuaran.Core.Ops"
                        "Fuaran.Core.Wire"
                        "Fuaran.Core.Gone" ])
                  ([], [ "Fuaran.Core.Gone" ])
                  "a row for a package that no longer ships is reported as surplus"
          }

          test "packability reads the project, then the props fallback, then MSBuild's default" {
              Expect.isFalse (isPackable None "<IsPackable>false</IsPackable>") "an explicit false excludes"
              Expect.isFalse (isPackable None "<IsPackable>FALSE</IsPackable>") "case does not matter"
              Expect.isTrue (isPackable None "<IsPackable>true</IsPackable>") "an explicit true includes"
              Expect.isTrue (isPackable None "<PropertyGroup></PropertyGroup>") "undeclared with no fallback includes"
              Expect.isFalse (isPackable (Some "false") "<PropertyGroup></PropertyGroup>") "the props fallback applies"

              Expect.isTrue
                  (isPackable (Some "false") "<IsPackable>true</IsPackable>")
                  "the project's own declaration wins over the fallback"
          }

          test "the README states no bare count of tests" {
              withRoot (fun root ->
                  let readme = File.ReadAllText(Path.Combine(root, "README.md"))

                  Expect.isGreaterThan
                      readme.Length
                      0
                      "the README was read — an empty read would make the absence below vacuous"

                  Expect.isEmpty
                      (bareTestCounts readme)
                      "the README states a test count that nothing asserts. Remedy: remove it, or reword it as a dated historical note; the suite already asserts that every law family is exercised and non-vacuous at the reference witness (docs/conformance-families.md).")
          }

          test "the bare-test-count reader flags a count and passes the claims the README makes instead" {
              Expect.equal
                  (bareTestCounts "398 conformance tests exercise every layer against an in-repo reference witness")
                  [ "398 conformance tests" ]
                  "the sentence Phase 233 removed is found"

              Expect.equal
                  (bareTestCounts "the suite declares 1,334 testCases across 90 files")
                  [ "1,334 testCases" ]
                  "a thousands-separated number and the testCase spelling are found"

              Expect.equal
                  (bareTestCounts "Nine spike tests are green")
                  [ "Nine spike tests" ]
                  "a number word is found, at the start of a sentence too"

              Expect.isEmpty
                  (bareTestCounts
                      "every law family is exercised and non-vacuous at the reference witness; five green laws over zero cases")
                  "a count of laws, families or packages is not a count of tests"

              Expect.isEmpty
                  (bareTestCounts "The conformance tests run against a reference witness.")
                  "no number, no count"
          }

          test "the README reader takes the Packages table and stops at the next heading" {
              let readme =
                  String.concat
                      "\n"
                      [ "# Title"
                        "Prose with a `Fuaran.Core.NotARow` backtick in it."
                        ""
                        "## Packages"
                        ""
                        "| Package | What it owns | Generic over |"
                        "|---|---|---|"
                        "| **`Fuaran.Core.Tree`** | addressing | `'Node` + `'Id` |"
                        "| **`Fuaran.Core.Ops`** | the ops | node witness |"
                        ""
                        "## The witness pattern"
                        ""
                        "| **`Fuaran.Core.Beyond`** | not a package row | — |" ]

              Expect.equal
                  (readmePackageIds readme)
                  [ "Fuaran.Core.Tree"; "Fuaran.Core.Ops" ]
                  "the header row, the separator, surrounding prose and a table in a later section are all excluded"

              Expect.isEmpty
                  (readmePackageIds "# Title\n\nno packages section here\n")
                  "a document with no Packages section yields nothing — which is why the live case asserts the read is non-empty before comparing"
          }

          test "the exclusions reader accepts the plausible shapes and refuses the rest" {
              Expect.equal
                  (declaredExclusions """["Fuaran.Core.Idl.Codegen","Fuaran.Core.Observer"]""")
                  (Some(Set.ofList [ "Fuaran.Core.Idl.Codegen"; "Fuaran.Core.Observer" ]))
                  "a bare array of ids"

              Expect.equal
                  (declaredExclusions """{"kind":"fableSmokeExclusions","exclusions":["Fuaran.Core.Observer"]}""")
                  (Some(Set.ofList [ "Fuaran.Core.Observer" ]))
                  "an object whose first array-of-strings property carries them"

              Expect.equal
                  (declaredExclusions """{"exclusions":[{"package":"Fuaran.Core.Observer","why":"build-time"}]}""")
                  (Some(Set.ofList [ "Fuaran.Core.Observer" ]))
                  "an array of objects naming the package"

              Expect.isNone (declaredExclusions "{ not json") "unparseable input is a named skip, never a failure"

              Expect.isNone
                  (declaredExclusions """{"note":"nothing excluded yet"}""")
                  "a shape carrying no id array is a named skip too — a sibling's newer format must not redden this"
          }

          test "the anchor reader numbers duplicates as GitHub does, and the table reader reds an unresolved row" {
              Expect.equal
                  (slugBase "## 0.36.0 — released 2026-10-08 as `v0.36.0`")
                  "0360--released-2026-10-08-as-v0360"
                  "an em dash leaves a double hyphen, and dots and backticks go"

              Expect.equal
                  (headingAnchors "## Class\n\n```\n## not a heading\n```\n### Class\n# Other")
                  (set [ "class"; "class-1"; "other" ])
                  "the second heading of one text is numbered; fenced code is not read"

              let contract =
                  "# C\n\n## Where sections moved\n\n| New home | Anchors |\n|---|---|\n| this file | `#old-name` → `#new-name` |\n| [`docs/releases/0.2.0.md`](docs/releases/0.2.0.md) | `#class`, `#gone` |\n\n## New name\n"

              let rows = movedAnchorRows contract

              Expect.equal
                  rows
                  [ "STABILITY.md", "old-name", "new-name"
                    "docs/releases/0.2.0.md", "class", "class"
                    "docs/releases/0.2.0.md", "gone", "gone" ]
                  "both row shapes parse, the renamed anchor carrying its new name"

              let read =
                  function
                  | "STABILITY.md" -> Some contract
                  | "docs/releases/0.2.0.md" -> Some "## 0.2.0 — x\n### Class"
                  | _ -> None

              Expect.equal
                  (movedAnchorFaults read rows)
                  [ "`#gone` is mapped to docs/releases/0.2.0.md#gone, which is not a heading anchor there" ]
                  "only the anchor its file does not carry is red"

              Expect.exists
                  (movedAnchorFaults read [ "docs/releases/9.9.9.md", "x", "x" ])
                  (fun f -> f.Contains "which does not exist")
                  "a row naming a missing file is red"

              Expect.isNonEmpty
                  (propsHistoryLines "<!-- 0.12.0 (2026-08-21): a note -->\n<Version>0.12.0</Version>")
                  "a version-history note in the props file is found"

              Expect.isEmpty
                  (propsHistoryLines "<!-- Phase 294: an incomplete match -->")
                  "an ordinary comment is not history"
          } ]
