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
//   3. Some entry header of the release ledger names the standing `<Version>`.
//   4. No "no `vX.Y.Z` tag exists" sentence survives the tag it denies.
//   5. Every release TAG at or above a declared floor has an entry header naming it.
//   8. The release ledger's shape (Phase 397): one `docs/releases/<version>.md` per slot, each
//      opening with its own heading, the index carrying every heading word for word with a link,
//      every tag and the standing version with a file, DRAFT only on the standing version; the
//      contract under its line ceiling and free of version entries; `Directory.Build.props` free
//      of version history and pointing its stability record at the index; and every row of the
//      contract's "Where sections moved" table resolving to a heading of the file it names.
//   6. The README states no bare count of tests (Phase 233) — a figure nothing asserts is a
//      claim the document cannot keep true; the suite's own census is docs/conformance-families.md.
//   7. The post-push registry probe (`.github/scripts/probe-registry.ps1`, Phase 393) derives the
//      same roster — it asks nuget.org about exactly the ids property 1 holds the README to.
//
// Property 5 is Phase 205, and it exists because property 3 structurally cannot see what
// it caught. Property 3 quantifies over the STANDING `<Version>` — one number — so a slot
// that was cut, tagged, released and then left behind as `<Version>` moved on is invisible
// to it: `v0.25.0` was tagged 2026-09-15 and this document carried no entry for it at all,
// while property 3 read green against `0.26.0` the whole time. Quantifying over the tags
// is what closes that. The FLOOR kept it honest while the record lived in STABILITY.md: every
// release below `0.25.0` predated the per-slot classes, so demanding entries for them would have
// demanded invention rather than record. Since Phase 397 each of those tags has a ledger file
// whose heading states only what the tag proves and whose body says no entry was written then,
// so the floor covers every tag.
//
// Properties 4 and 5 read `git tag` LOCALLY, which is the honest limit: they answer "has
// the release gesture been made in this clone", never "is this version published". A clone
// with no git at all skips by name with the reason printed, on the
// `WorkingCopyEolTests` precedent — a check that reads as green when its instrument is
// missing is worse than an absent one.
// ---------------------------------------------------------------------------

open System
open System.Diagnostics
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
    |> Seq.map (fun m -> m.Value)
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

/// The first level-2-or-3 header naming `version`, if any. A version's entry may be its own
/// `## 0.26.0 (draft)` section or a `### … (Phase N, \`0.26.0\`)` entry under an older
/// grouping — both spellings are live in this document, so the property is "an entry header
/// names it", not "a header equals it".
let internal entryHeaderNaming (version: string) (stability: string) : string option =
    stability.Replace("\r\n", "\n").Split('\n')
    |> Array.tryFind (fun l -> Regex.IsMatch(l, @"^#{2,3}\s") && l.Contains(version, StringComparison.Ordinal))

let private noTagRe =
    Regex(@"no `v(?<v>\d+\.\d+\.\d+)` tag exists", RegexOptions.Compiled)

/// Draft-slot preambles that deny a tag the repository holds — `(line number, version)`.
/// The sentence is true when a slot is cut and false the instant the release gesture is
/// made, and nothing but this retires it.
let internal staleDraftSentences (tags: Set<string>) (lines: string list) : (int * string) list =
    lines
    |> List.indexed
    |> List.choose (fun (i, l) ->
        let m = noTagRe.Match l

        if m.Success && tags.Contains("v" + m.Groups["v"].Value) then
            Some(i + 1, m.Groups["v"].Value)
        else
            None)

/// The oldest released slot the per-tag entry rule covers (Phase 205), as
/// `(major, minor, patch)`. ONE named constant, because the number is a judgement, and a
/// judgement spelt in two places is a judgement that will disagree with itself. It was `0.25.0`,
/// the oldest slot whose ENTRY could still be written from evidence; since Phase 397 every release
/// tag has a ledger file whose heading states only what the tag proves (the version, the date,
/// `released`), and a slot cut before the per-slot entries says so in its file rather than having
/// an entry invented for it — so the rule covers every tag (DECISIONS.md D130).
let internal entryHeaderFloor = (0, 0, 0)

let private releaseTagRe = Regex(@"^v(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled)

/// `(major, minor, patch)` for a plain release tag, else `None`. The shape is deliberately
/// STRICT: a pre-release spelling (`v0.0.1-alpha.8`) names no released slot in the sense
/// this rule is about, and every such tag this repository holds sits below the floor in any
/// case. A tag shape this does not parse is out of frame rather than reported — the
/// alternative is reddening on someone else's tagging convention.
let internal releaseTagVersion (tag: string) : (int * int * int) option =
    let m = releaseTagRe.Match tag

    if m.Success then
        Some(int m.Groups[1].Value, int m.Groups[2].Value, int m.Groups[3].Value)
    else
        None

/// Release tags at or above `floor` that no entry header names, oldest first.
///
/// Ordering is NUMERIC, over the parsed triple, not lexical over the tag string: `0.9.0`
/// sorts BELOW `0.25.0` and a string comparison gets that backwards, which would silently
/// admit every pre-`0.10` tag the floor exists to exclude. It reuses `entryHeaderNaming` —
/// the same seam property 3 asserts the standing version through — so the two properties
/// cannot disagree about what counts as an entry.
let internal tagsMissingEntryHeader (floor: int * int * int) (tags: Set<string>) (stability: string) : string list =
    tags
    |> Set.toList
    |> List.choose (fun t -> releaseTagVersion t |> Option.map (fun v -> v, t))
    |> List.filter (fun (v, _) -> v >= floor)
    |> List.sortBy fst
    |> List.filter (fun (_, t) -> (entryHeaderNaming (t.Substring 1) stability).IsNone)
    |> List.map snd

// ---- the release ledger (Phase 397) ---------------------------------------
//
// STABILITY.md is the contract; `docs/releases/<version>.md` is the ledger, one file per version
// slot, and `docs/releases/README.md` its index (DECISIONS.md D130). The per-tag properties above
// read the ledger's slot files; what follows holds the ledger's own shape, so a slot without a
// file, a file without an index entry, or an index heading that disagrees with its file is red.

/// The ledger directory, repo-relative.
let internal ledgerDir = "docs/releases"

/// The contract's line ceiling: what an adopter reads in one sitting, and what keeps a release's
/// entry from drifting back into it (D130).
let internal contractLineCeiling = 1500

/// One slot file of the ledger.
type internal LedgerFile =
    {
        Version: string
        /// Repo-relative, forward-slashed.
        Path: string
        Text: string
    }

let private levelTwoVersionRe =
    Regex(@"^## (?<v>\d+\.\d+\.\d+)(?:\s|$)", RegexOptions.Compiled)

let internal versionTriple (v: string) : int * int * int =
    match v.Split('.') |> Array.map int with
    | [| a; b; c |] -> (a, b, c)
    | _ -> (0, 0, 0)

/// Every `## <version> …` heading of a document, in order, trailing space trimmed.
let internal versionHeadings (text: string) : (string * string) list =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.toList
    |> List.choose (fun l ->
        let m = levelTwoVersionRe.Match l

        if m.Success then
            Some(m.Groups["v"].Value, l.TrimEnd())
        else
            None)

/// The ledger read as one text, newest slot first — the shape the per-tag properties read.
let internal ledgerText (files: LedgerFile list) : string =
    files |> List.map _.Text |> String.concat "\n"

/// What is wrong with the ledger's shape: each slot file opens with its own version's level-2
/// heading and carries no other; the index carries exactly the slot files' headings, newest first,
/// each with a link to its file; every release tag and the standing `<Version>` has a file; a
/// `DRAFT` heading names only the standing version; and nothing else sits in the directory.
let internal ledgerFaults
    (tags: Set<string>)
    (standing: string option)
    (index: string)
    (files: LedgerFile list)
    (strays: string list)
    : string list =
    let perFile =
        files
        |> List.collect (fun f ->
            let firstLine =
                f.Text.Replace("\r\n", "\n").Split('\n')
                |> Array.tryFind (fun l -> l.Trim() <> "")
                |> Option.defaultValue ""

            let opens =
                let m = levelTwoVersionRe.Match firstLine

                if m.Success && m.Groups["v"].Value = f.Version then
                    []
                else
                    [ sprintf
                          "%s does not open with its slot's heading `## %s — …` (it opens '%s')"
                          f.Path
                          f.Version
                          firstLine ]

            let others =
                versionHeadings f.Text
                |> List.filter (fun (v, _) -> v <> f.Version)
                |> List.map (fun (v, h) -> sprintf "%s carries a heading for another slot, %s: '%s'" f.Path v h)

            let draft =
                versionHeadings f.Text
                |> List.filter (fun (v, h) ->
                    v = f.Version
                    && h.StartsWith(sprintf "## %s — DRAFT" v, StringComparison.Ordinal))
                |> List.choose (fun (v, _) ->
                    if Some v = standing then
                        None
                    else
                        Some(
                            sprintf
                                "%s is headed DRAFT, and %s is not the standing <Version>: a draft heading turns when its slot is released or advanced"
                                f.Path
                                v
                        ))

            opens @ others @ draft)

    let fileHeadings =
        files
        |> List.sortByDescending (fun f -> versionTriple f.Version)
        |> List.choose (fun f ->
            versionHeadings f.Text
            |> List.tryFind (fun (v, _) -> v = f.Version)
            |> Option.map snd)

    let indexHeadings = versionHeadings index |> List.map snd

    let indexFault =
        if indexHeadings = fileHeadings then
            []
        else
            let a = Set.ofList indexHeadings
            let b = Set.ofList fileHeadings

            let show label (xs: string seq) =
                if Seq.isEmpty xs then
                    []
                else
                    [ sprintf "%s:\n         %s" label (String.concat "\n         " xs) ]

            let parts =
                show "an index heading no slot file opens with" (Set.difference a b)
                @ show "a slot file heading the index does not carry" (Set.difference b a)

            [ sprintf
                  "%s/README.md does not carry the slot files' headings word for word, newest first%s"
                  ledgerDir
                  (if parts.IsEmpty then
                       " (the order differs)"
                   else
                       "\n       " + String.concat "\n       " parts) ]

    let links =
        files
        |> List.filter (fun f -> not (index.Contains(sprintf "(%s.md)" f.Version, StringComparison.Ordinal)))
        |> List.map (fun f -> sprintf "%s/README.md does not link `%s.md`" ledgerDir f.Version)

    let have = files |> List.map _.Version |> Set.ofList

    let missing =
        let fromTags =
            tags
            |> Set.toList
            |> List.choose (fun t -> releaseTagVersion t |> Option.map (fun _ -> t.Substring 1))

        (fromTags @ Option.toList standing)
        |> List.distinct
        |> List.filter (have.Contains >> not)
        |> List.sortBy versionTriple
        |> List.map (fun v -> sprintf "%s has no ledger file: %s/%s.md is missing" v ledgerDir v)

    let stray =
        strays
        |> List.map (fun s -> sprintf "%s/%s is neither the index nor a slot file named `<version>.md`" ledgerDir s)

    perFile @ indexFault @ links @ missing @ stray

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

let private repoRoot () : string option =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        elif File.Exists(Path.Combine(dir, "Fuaran.Core.slnx")) then
            Some dir
        else
            match Directory.GetParent dir with
            | null -> None
            | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)

/// `git tag --list` from `root`, or why it could not be run. Spawned through
/// `ChildProcess.redirected` so the child's stdout decodes as UTF-8 whatever code page the
/// runner inherited.
let private gitTags (root: string) : Result<Set<string>, string> =
    try
        let psi = ChildProcess.redirected "git" "tag --list"
        psi.WorkingDirectory <- root
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "`git tag --list` exited %d: %s" p.ExitCode (err.Trim()))
        else
            out.Split('\n')
            |> Array.toList
            |> List.map (fun l -> l.Trim())
            |> List.filter (fun l -> l <> "")
            |> Set.ofList
            |> Ok
    with e ->
        Error("`git` could not be run: " + e.Message)

let private withRoot (f: string -> unit) =
    match repoRoot () with
    | None -> failtest "could not locate the repository root (Fuaran.Core.slnx) from the CWD or the test binary"
    | Some root -> f root

let private fileLines (path: string) =
    File.ReadAllText(path).Replace("\r\n", "\n").Split('\n') |> Array.toList

/// The ledger's slot files under `root`, newest slot first, and the names of any other file in the
/// directory beside the index.
let internal readLedger (root: string) : LedgerFile list * string list =
    let dir = Path.Combine(root, ledgerDir)

    if not (Directory.Exists dir) then
        [], []
    else
        let names = Directory.GetFiles dir |> Array.map Path.GetFileName |> Array.toList

        let isSlot (n: string) =
            Regex.IsMatch(n, @"^\d+\.\d+\.\d+\.md$")

        let slots =
            names
            |> List.filter isSlot
            |> List.map (fun n ->
                let v = n.Substring(0, n.Length - 3)

                { Version = v
                  Path = ledgerDir + "/" + n
                  Text = File.ReadAllText(Path.Combine(dir, n)).Replace("\r\n", "\n") })
            |> List.sortByDescending (fun f -> versionTriple f.Version)

        slots, names |> List.filter (fun n -> not (isSlot n) && n <> "README.md") |> List.sort

/// The ledger's slot files under `root`, newest first.
let internal ledgerFiles (root: string) : LedgerFile list = fst (readLedger root)

// ---- the registry probe's roster (Phase 393) ------------------------------

/// The roster `.github/scripts/probe-registry.ps1 -ListRoster` derives from `root`, run the way
/// the publish workflow runs it, or why it could not be read. The probe asks nuget.org about
/// exactly these ids after a push, so a probe whose derivation drifted from `packableProjects`
/// would verify a different list than the one the README table is gated on.
let private probeRoster (repo: string) (root: string) : Result<string list, string> =
    try
        let psi = ChildProcess.redirected "pwsh" ""

        for a in
            [ "-NoProfile"
              "-NonInteractive"
              "-File"
              Path.Combine(repo, ".github", "scripts", "probe-registry.ps1")
              "-ListRoster"
              "-Root"
              root ] do
            psi.ArgumentList.Add a

        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEndAsync()
        let err = p.StandardError.ReadToEndAsync()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "the probe's -ListRoster exited %d: %s" p.ExitCode ((err.Result + out.Result).Trim()))
        else
            out.Result.Split('\n')
            |> Array.toList
            |> List.map (fun l -> l.Trim())
            |> List.filter (fun l -> l <> "")
            |> Ok
    with e ->
        Error("`pwsh` could not be run: " + e.Message)

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

          test "the release ledger carries an entry header naming the standing <Version>" {
              withRoot (fun root ->
                  let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

                  match standingVersion props with
                  | None -> failtest "Directory.Build.props declares no <Version>"
                  | Some version ->
                      let ledger = ledgerText (ledgerFiles root)

                      match entryHeaderNaming version ledger with
                      | Some _ -> ()
                      | None ->
                          failtestf
                              "`<Version>` is `%s` and no entry header of the release ledger (docs/releases/) names it.\n       Remedy: open the slot — docs/releases/%s.md headed `## %s — DRAFT` with the draft-slot preamble, and its index entry — and append this change's entry under it. The version is what tells a consumer what adopting it costs; a number with no entry says nothing."
                              version
                              version
                              version)
          }

          test "no 'no tag exists' sentence survives the tag it denies" {
              withRoot (fun root ->
                  match gitTags root with
                  | Error why ->
                      // Skipped by NAME with the reason printed, never passed silently.
                      // This is the source-archive / no-git case, not a defect in the tree.
                      printfn "STABILITY draft-slot tag check SKIPPED: %s" why
                      skiptestf "STABILITY draft-slot tag check skipped — %s" why
                  | Ok tags ->
                      Expect.isNonEmpty (Set.toList tags) "`git tag --list` listed at least one tag"

                      let stale =
                          ledgerFiles root
                          |> List.collect (fun f ->
                              staleDraftSentences tags (f.Text.Split('\n') |> Array.toList)
                              |> List.map (fun (n, v) -> f.Path, n, v))

                      match stale with
                      | [] -> ()
                      | stale ->
                          let shown =
                              stale
                              |> List.map (fun (path, n, v) ->
                                  sprintf "%s:%d says no `v%s` tag exists — it does" path n v)
                              |> String.concat "\n       "

                          failtestf
                              "%d draft-slot preamble(s) deny a tag this repository holds:\n       %s\n       Remedy: retire the sentence — the slot is released, so say so and say when. Do not delete the entries under it."
                              stale.Length
                              shown)
          }

          test "every release tag at or above the floor has a release-ledger entry header" {
              // Phase 205. Property 3 asserts the STANDING version only, so a released slot
              // that `<Version>` has moved past is outside its frame entirely — which is
              // how `v0.25.0` came to be tagged with no entry while this file read green.
              withRoot (fun root ->
                  match gitTags root with
                  | Error why ->
                      printfn "STABILITY per-tag entry check SKIPPED: %s" why
                      skiptestf "STABILITY per-tag entry check skipped — %s" why
                  | Ok tags ->
                      let covered =
                          tags
                          |> Set.toList
                          |> List.choose releaseTagVersion
                          |> List.filter (fun v -> v >= entryHeaderFloor)

                      Expect.isNonEmpty
                          covered
                          "at least one release tag sits at or above the floor — with none the comparison below would pass vacuously, which is the shape a mis-parsed tag would take"

                      let ledger = ledgerText (ledgerFiles root)

                      match tagsMissingEntryHeader entryHeaderFloor tags ledger with
                      | [] -> ()
                      | missing ->
                          failtestf
                              "%d released tag(s) at or above the floor have no release-ledger entry header: %s\n       Remedy: open docs/releases/<version>.md headed with the version — the shape the released slots use is '## 0.25.0 — released <date> as `v0.25.0`' — add its index entry, and record under it what shipped in that slot, each change classed. A released version with no entry tells a consumer nothing about what adopting it costs.\n       The floor is `entryHeaderFloor` in this file."
                              missing.Length
                              (String.concat ", " missing))
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

          // ---- the release ledger (Phase 397) --------------------------------

          test "the release ledger: every slot has its file, its index entry and its own heading" {
              withRoot (fun root ->
                  let files, strays = readLedger root
                  let indexPath = Path.Combine(root, ledgerDir, "README.md")

                  Expect.isTrue (File.Exists indexPath) "docs/releases/README.md, the ledger's index, exists"

                  Expect.isNonEmpty
                      files
                      "the ledger holds at least one slot file — an empty read would make every clause vacuous"

                  let tags =
                      match gitTags root with
                      | Ok tags -> tags
                      | Error why ->
                          // Without git the tag clause has nothing to read; the shape clauses still run.
                          printfn "release-ledger tag clause SKIPPED: %s" why
                          Set.empty

                  let standing =
                      standingVersion (File.ReadAllText(Path.Combine(root, "Directory.Build.props")))

                  match ledgerFaults tags standing (File.ReadAllText indexPath) files strays with
                  | [] -> ()
                  | faults ->
                      failtestf
                          "%d release-ledger fault(s):\n       %s\n       Remedy: one file per version slot, docs/releases/<version>.md, opening with `## <version> — …`; its heading copied word for word into docs/releases/README.md, newest first, with a link to the file. A tagged slot is headed `released <date> as `v<version>``; only the standing <Version> is headed DRAFT."
                          faults.Length
                          (String.concat "\n       " faults))
          }

          test "STABILITY.md is the contract, held to the line ceiling" {
              withRoot (fun root ->
                  let lines = fileLines (Path.Combine(root, "STABILITY.md"))

                  Expect.isTrue
                      (lines |> List.exists (fun l -> l = "## Versioning policy"))
                      "the contract was read — it carries its versioning policy"

                  Expect.isLessThanOrEqual
                      lines.Length
                      contractLineCeiling
                      (sprintf
                          "STABILITY.md is %d lines, over the %d-line ceiling DECISIONS.md D130 sets. A release's entry belongs in its slot file, docs/releases/<version>.md; the contract keeps what every version is held to"
                          lines.Length
                          contractLineCeiling)

                  Expect.isEmpty
                      (versionHeadings (String.concat "\n" lines))
                      "STABILITY.md carries no `## <version>` entry: every slot's entry is in the ledger")
          }

          test
              "Directory.Build.props keeps <Version> and points the stability record at the ledger index, with no version history" {
              withRoot (fun root ->
                  let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

                  Expect.isSome (standingVersion props) "the props file declares <Version>"

                  Expect.isEmpty
                      (propsHistoryLines props)
                      "Directory.Build.props carries a version-history note — the history is the ledger's, docs/releases/<version>.md"

                  let record =
                      Regex.Match(props, @"<FuaranStabilityRecord>([^<]*)</FuaranStabilityRecord>")

                  Expect.isTrue record.Success "the props file declares <FuaranStabilityRecord>"

                  Expect.equal
                      (record.Groups[1].Value.Trim())
                      (ledgerDir + "/README.md")
                      "the declared stability record is the ledger's index, which carries every slot's heading"

                  Expect.isTrue
                      (File.Exists(Path.Combine(root, record.Groups[1].Value.Trim())))
                      "the declared stability record exists")
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

          test "the STABILITY header check finds a slot header and refuses a missing one" {
              let stability =
                  String.concat
                      "\n"
                      [ "# Stability"
                        "Prose mentioning 0.27.0 outside a heading."
                        "## 0.26.0 (draft)"
                        "### Something (Phase 1, `0.26.0`)"
                        "## 0.24.0 — released" ]

              Expect.isSome (entryHeaderNaming "0.26.0" stability) "the standing version's own header is found"
              Expect.isSome (entryHeaderNaming "0.24.0" stability) "an older released slot is found too"

              Expect.isNone
                  (entryHeaderNaming "0.27.0" stability)
                  "a version named only in prose is NOT an entry — this is the `<Version>` bumped without an entry case"
          }

          test "the draft-slot check reports a denied tag and stays quiet on a genuine draft" {
              let lines =
                  [ "## 0.26.0 (draft)"
                    "**This section describes a DRAFT slot.** `<Version>` reads `0.26.0` and no `v0.26.0` tag exists"
                    "yet."
                    "## 0.24.0 — released"
                    "**This section describes a DRAFT slot.** `<Version>` reads `0.24.0` and no `v0.24.0` tag exists"
                    "yet." ]

              Expect.equal
                  (staleDraftSentences (Set.ofList [ "v0.24.0"; "v0.23.0" ]) lines)
                  [ (5, "0.24.0") ]
                  "the sentence denying a tag that exists is reported, by line, and the genuine draft is not"

              Expect.isEmpty
                  (staleDraftSentences (Set.ofList [ "v0.23.0" ]) lines)
                  "with neither version tagged, both sentences are true and nothing is reported"
          }

          test "the per-tag entry check reports an un-entried release and respects the floor" {
              let stability =
                  String.concat
                      "\n"
                      [ "# Stability"
                        "## 0.26.0 — released 2026-09-17 as `v0.26.0`"
                        "## 0.24.0 — released 2026-09-15 as `v0.24.0`"
                        "Prose mentioning 0.25.0 outside a heading." ]

              let tags =
                  Set.ofList [ "v0.0.1-alpha.8"; "v0.17.0"; "v0.24.0"; "v0.25.0"; "v0.26.0" ]

              Expect.equal
                  (tagsMissingEntryHeader (0, 25, 0) tags stability)
                  [ "v0.25.0" ]
                  "the released tag with no entry header is reported — and a version named only in prose is not an entry"

              Expect.isEmpty
                  (tagsMissingEntryHeader (0, 27, 0) tags stability)
                  "a floor above every tag reports nothing, which is exactly why the live case asserts the covered set is non-empty first"

              Expect.equal
                  (tagsMissingEntryHeader (0, 17, 0) tags stability)
                  [ "v0.17.0"; "v0.25.0" ]
                  "lowering the floor pulls older un-entried slots in, oldest first — the FLOOR is what excludes them, not a gap in the reader"

              Expect.equal
                  (releaseTagVersion "v0.9.0" |> Option.map (fun v -> v < (0, 25, 0)))
                  (Some true)
                  "ordering is numeric: 0.9.0 is BELOW 0.25.0, which a lexical comparison on the tag string gets backwards"

              Expect.isNone
                  (releaseTagVersion "v0.0.1-alpha.8")
                  "a pre-release spelling parses to nothing, so it is out of frame rather than reported"

              Expect.isNone (releaseTagVersion "release/v1.2.3") "a foreign tagging convention is out of frame too"
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

          test "the ledger reader reds a missing file, a stray index row, a foreign heading and a stale DRAFT" {
              let file v (text: string) =
                  { Version = v
                    Path = sprintf "%s/%s.md" ledgerDir v
                    Text = text }

              let files =
                  [ file "0.3.0" "## 0.3.0 — DRAFT\n\nentries"
                    file "0.2.0" "## 0.2.0 — released 2026-07-26 as `v0.2.0`\n\nrecord" ]

              let index =
                  "# Ledger\n\n## 0.3.0 — DRAFT\n\n[`0.3.0.md`](0.3.0.md)\n\n## 0.2.0 — released 2026-07-26 as `v0.2.0`\n\n[`0.2.0.md`](0.2.0.md)\n"

              let tags = set [ "v0.2.0"; "v0.0.0-gate-probe" ]
              let faults = ledgerFaults tags (Some "0.3.0")

              Expect.isEmpty (faults index files []) "the synthetic ledger is the green shape"

              Expect.exists
                  (ledgerFaults (Set.add "v0.1.0" tags) (Some "0.3.0") index files [])
                  (fun f -> f.Contains "0.1.0 has no ledger file")
                  "a tag with no slot file is red"

              Expect.exists
                  (faults (index.Replace("## 0.2.0 — released", "## 0.2.0 — DRAFT released")) files [])
                  (fun f -> f.Contains "does not carry the slot files' headings")
                  "an index heading that disagrees with its file is red"

              Expect.exists
                  (faults (index.Replace("(0.2.0.md)", "(elsewhere.md)")) files [])
                  (fun f -> f.Contains "does not link `0.2.0.md`")
                  "an index entry without its link is red"

              Expect.exists
                  (faults index (files @ [ file "0.1.0" "## 0.1.0 — released 2026-07-01 as `v0.1.0`" ]) [])
                  (fun f -> f.Contains "a slot file heading the index does not carry")
                  "a slot file the index does not list is red"

              Expect.exists
                  (faults index [ file "0.3.0" "## 0.2.0 — DRAFT"; files[1] ] [])
                  (fun f -> f.Contains "does not open with its slot's heading")
                  "a file opening with another slot's heading is red"

              Expect.exists
                  (ledgerFaults tags (Some "0.4.0") index files [])
                  (fun f -> f.Contains "is headed DRAFT, and 0.3.0 is not the standing <Version>")
                  "a DRAFT heading on a slot that is no longer the standing version is red"

              Expect.exists
                  (faults index files [ "notes.txt" ])
                  (fun f -> f.Contains "notes.txt is neither the index nor a slot file")
                  "a stray file in the ledger directory is red"
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
