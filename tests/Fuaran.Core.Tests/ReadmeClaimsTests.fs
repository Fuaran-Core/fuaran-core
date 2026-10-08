module Fuaran.Core.Tests.ReadmeClaimsTests

// ---------------------------------------------------------------------------
// Phase 254 — a README claim of a surface names the version it arrived in, and the version is
// DERIVED, not written.
//
// The README describes the head of this repository, and a reader holds a released package. Between
// a release and the next, every surface a draft adds is described in the README as present while
// the package the reader restored lacks it — `SampleAdequacy.cases`, `RequiredParamsNull` and
// `ParityVectors` were each read that way by a downstream consumer pinned below the slot that added
// them, and the only remedy available to it was reading STABILITY.md's version headings by hand.
//
// So every surface the README names in code type carries the version it arrived in, and that
// version is computed rather than typed:
//
//   * The INDEX of a release is the committed public-surface baselines (`api/*.txt`) at its tag —
//     the gate holds those files to the built assemblies, so at the tag they ARE the release's
//     public surface. The first release tag that carries them is the FLOOR: a surface already
//     present there shipped "at the floor or earlier", which is all this repository can say from
//     evidence, and the README says it once instead of on every name.
//   * A CLAIM is a code span whose leading identifier path resolves in the CURRENT baselines —
//     qualified (`Tree.wellFormed`), capitalised (`KeyedWitness`) or bare (`evalWith`), read as the
//     span's leading name, so `Tree.traversal nodew keyw` claims `Tree.traversal`. A span naming
//     nothing current (`DataFrame`, which moved to another repository) is not one. The cost of reading
//     bare names is that a code span which is a WORD rather than a member — a wire tag such as `int`,
//     a column header — is read as one when some member shares its spelling; the README writes those
//     as string literals (`"int"`) or in prose, which is also the more accurate spelling for them. The
//     approve run shows each such reading as a stamp in the diff, so it is met before it is committed.
//   * A claim's ARRIVAL is the first release whose index resolves it. One that no release resolves is
//     in the standing `<Version>`, which must then be a DRAFT: untagged, and headed
//     `## <Version> — DRAFT` in STABILITY.md. That is the stamp a reader holding the released package
//     needs most, and it is the one the gate holds hardest.
//   * The STAMP follows the claim's first occurrence in each section: ` (since `0.31.0`)`, or
//     ` (since `0.34.0`, unreleased)` for a draft arrival. Nothing is stamped at or below the floor.
//
// The committed README is held to that rendering, and `CORE_APPROVE_README=1` writes it — the idiom
// of the API baselines and of `proofs/README.md`'s generated blocks. A clone with no git, or no
// release tag carrying baselines, cannot derive anything; it skips by name rather than passing.
//
// The README's "which Fable version" line is held here too: its compiler version is the one the
// receiving Fable gate pins in its tool manifest (Phase 217 moved that gate out of this
// repository), compared whenever that checkout is present, and its `Fable.Core` version is this
// repository's own pin, compared always.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

// ---- the surface index -----------------------------------------------------

let private indexedKinds =
    set [ "type"; "method"; "property"; "field"; "record-field"; "union-case" ]

/// The F# name one baseline line declares — namespace-qualified, generic arity dropped, nested types
/// dotted, a union case's `New` constructor prefix removed — or `None` for a header, a constructor, a
/// compiler-generated `Tags` member or a line of another shape.
let internal declaredName (line: string) : string option =
    let line = line.TrimEnd('\r')
    let sp = line.IndexOf ' '

    if sp <= 0 || not (indexedKinds.Contains(line.Substring(0, sp))) then
        None
    else
        let kind = line.Substring(0, sp)
        let rest = line.Substring(sp + 1)
        let stop = rest.IndexOfAny [| '('; ' ' |]
        let raw = if stop < 0 then rest else rest.Substring(0, stop)

        if raw.Contains("+Tags", StringComparison.Ordinal) then
            None
        else
            let segs = Regex.Replace(raw, @"`\d+", "").Replace('+', '.').Split('.')
            let last = segs[segs.Length - 1]
            // A case with fields is compiled as `New<Case>(...)`; a nullary one keeps its name.
            let withFields = Regex.IsMatch(rest, @"#\d+\([^)]")

            if
                kind = "union-case"
                && withFields
                && last.StartsWith("New", StringComparison.Ordinal)
            then
                segs[segs.Length - 1] <- last.Substring 3

            Some(String.Join(".", segs))

/// Every spelling a reader may use for `qualified`: each trailing run of its segments, with and
/// without the `Module` suffix F# adds to a module that shares a type's name (`ArbitrationModule` is
/// written `Arbitration`).
let internal keysOf (qualified: string) : string list =
    let segs = qualified.Split('.') |> Array.toList

    let strip (s: string) =
        if s.Length > 6 && s.EndsWith("Module", StringComparison.Ordinal) then
            s.Substring(0, s.Length - 6)
        else
            s

    [ for i in 0 .. segs.Length - 1 do
          let suffix = List.skip i segs
          yield String.Join(".", suffix)
          yield String.Join(".", List.map strip suffix) ]
    |> List.distinct

/// The names a set of baseline lines makes resolvable.
let internal indexOf (lines: string seq) : Set<string> =
    lines |> Seq.choose declaredName |> Seq.collect keysOf |> Set.ofSeq

// ---- claims and stamps -------------------------------------------------------

let private identRe =
    Regex(@"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*", RegexOptions.Compiled)

/// The surface name a code span could claim — its leading identifier path, when that is the whole span
/// or is followed by an argument, a type argument or a parenthesis (`Tree.traversal nodew keyw`,
/// `Deferred<'T>`). Whether it IS a claim is the index's question.
let internal candidateOfSpan (span: string) : string option =
    let m = identRe.Match span

    if not m.Success then
        None
    else
        let name = m.Value
        let rest = span.Substring name.Length

        let whole = rest = "" || Char.IsWhiteSpace rest[0] || rest[0] = '(' || rest[0] = '<'

        if whole then Some name else None

/// When a claim arrived, as the README states it.
type internal Arrival =
    /// At the floor release or before it — stated once, not per name.
    | AtFloor
    /// In a tagged release after the floor.
    | Released of version: string
    /// In the standing `<Version>`, which no tag has released.
    | Unreleased of version: string

/// The stamp text that follows a claim, or `""`.
let internal stampText (a: Arrival) : string =
    match a with
    | AtFloor -> ""
    | Released v -> sprintf " (since `%s`)" v
    | Unreleased v -> sprintf " (since `%s`, unreleased)" v

let private stampRe =
    Regex(@" \(since `\d+\.\d+\.\d+`(?:, unreleased)?\)", RegexOptions.Compiled)

let private spanRe = Regex(@"`([^`\n]+)`", RegexOptions.Compiled)

/// Markers around the README's generated statement of the floor.
let floorBegin =
    "<!-- surface-versions:begin — generated by ReadmeClaimsTests from api/ at each release tag; CORE_APPROVE_README=1 rewrites it -->"

let floorEnd = "<!-- surface-versions:end -->"

/// The statement between the markers.
let internal floorStatement (floor: string) : string =
    sprintf
        "**Each surface named in code type below carries the version it arrived in** — `(since 0.31.0)`, or `(since 0.34.0, unreleased)` for one only the standing draft slot of the [release ledger](docs/releases/README.md) has — and a surface shown without one shipped in `%s` or earlier, the first release whose public surface this repository records under `api/`. The versions are derived from those records at each release tag, not written by hand, and the suite holds them."
        floor

/// The README as the derivation says it must read: every stamp removed, then one re-inserted after the
/// first occurrence of each claim in each section, and the floor statement re-rendered. Fenced code
/// is left alone, and so is the floor block's own text. `Error` names what could not be located.
let internal stampReadme (arrival: string -> Arrival option) (floor: string) (readme: string) : Result<string, string> =
    let lines = readme.Replace("\r\n", "\n").Split('\n')
    let out = Collections.Generic.List<string>()
    let mutable inFence = false
    let mutable inFloor = false
    let mutable floorSeen = false
    let mutable seen = Set.empty<string>

    for line in lines do
        if line.TrimStart().StartsWith("```", StringComparison.Ordinal) then
            inFence <- not inFence
            out.Add line
        elif inFence then
            out.Add line
        elif line = floorBegin then
            inFloor <- true
            floorSeen <- true
            out.Add line
            out.Add(floorStatement floor)
        elif line = floorEnd then
            inFloor <- false
            out.Add line
        elif inFloor then
            () // re-rendered above
        else
            if line.StartsWith("#", StringComparison.Ordinal) then
                seen <- Set.empty

            let bare = stampRe.Replace(line, "")

            let stamped =
                spanRe.Replace(
                    bare,
                    MatchEvaluator(fun m ->
                        match candidateOfSpan m.Groups[1].Value with
                        | Some name when not (seen.Contains name) ->
                            match arrival name with
                            | Some a ->
                                seen <- seen.Add name
                                m.Value + stampText a
                            | None -> m.Value
                        | _ -> m.Value)
                )

            out.Add stamped

    if not floorSeen then
        Error(sprintf "the README carries no '%s' marker line" floorBegin)
    elif inFloor then
        Error(sprintf "the README's floor block is not closed by '%s'" floorEnd)
    else
        Ok(String.Join("\n", out))

/// Every stamp the README carries, as `(claim, stamp text)`, in file order — for the draft-heading
/// check, which reads the README as committed.
let internal stampsOf (readme: string) : (string * string) list =
    Regex.Matches(readme, @"`([^`\n]+)`( \(since `(\d+\.\d+\.\d+)`(, unreleased)?\))")
    |> Seq.map (fun m -> m.Groups[1].Value, m.Groups[2].Value)
    |> Seq.toList

// ---- the release history ------------------------------------------------------

/// `(version, index)` for every release tag whose tree carries the baselines, oldest first.
let private releaseIndexes (root: string) : Result<(string * Set<string>) list, string> =
    ChildProcess.git root "tag --list"
    |> Result.bind (fun out ->
        let tags =
            out.Split('\n')
            |> Array.map _.Trim()
            |> Array.choose (fun t -> PackageRosterTests.releaseTagVersion t |> Option.map (fun v -> v, t))
            |> Array.sortBy fst
            |> Array.toList

        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | ((ma, mi, pa), tag) :: rest ->
                match ChildProcess.git root (sprintf "ls-tree --name-only %s api/" tag) with
                | Error e -> Error e
                | Ok listing ->
                    let hasBaselines =
                        listing.Split('\n')
                        |> Array.exists (fun p ->
                            let p = p.Trim()

                            p.StartsWith("api/", StringComparison.Ordinal)
                            && p.EndsWith(".txt", StringComparison.Ordinal))

                    if not hasBaselines then
                        go acc rest
                    else
                        match ChildProcess.git root (sprintf "grep -h -e . %s -- \":(glob)api/*.txt\"" tag) with
                        | Error e -> Error e
                        | Ok text -> go ((sprintf "%d.%d.%d" ma mi pa, indexOf (text.Split('\n'))) :: acc) rest

        go [] tags)

let private currentIndex (root: string) : Set<string> =
    Directory.GetFiles(Path.Combine(root, "api"), "*.txt")
    |> Seq.collect File.ReadAllLines
    |> indexOf

/// The arrival function the derivation stamps with: the first release resolving a name, else the
/// standing version, unreleased — `None` for a span that names nothing current. A surface no release
/// resolves while the standing version IS tagged (baselines moved after a tag without `<Version>`
/// advancing) is stamped unreleased all the same, and `stampFaults` refuses it, because its heading is
/// not a draft's.
let internal arrivalOf
    (releases: (string * Set<string>) list)
    (current: Set<string>)
    (standing: string)
    (name: string)
    : Arrival option =
    if not (current.Contains name) then
        None
    else
        match releases |> List.tryFindIndex (fun (_, idx) -> idx.Contains name) with
        | Some 0 -> Some AtFloor
        | Some i -> Some(Released(fst releases[i]))
        | None -> Some(Unreleased standing)

/// What is wrong with the README's stamps against the release ledger (read as one text): an
/// unreleased stamp must name the standing `<Version>` and that version must be headed
/// `## <v> — DRAFT`; a released stamp must name a version some entry header names.
let internal stampFaults (standing: string option) (ledger: string) (readme: string) : string list =
    let headings = ledger.Replace("\r\n", "\n").Split('\n')

    let draftHeading (v: string) =
        headings |> Array.exists (fun l -> l.Trim() = sprintf "## %s — DRAFT" v)

    stampsOf readme
    |> List.choose (fun (claim, stamp) ->
        let m = Regex.Match(stamp, @"`(\d+\.\d+\.\d+)`(, unreleased)?")
        let v = m.Groups[1].Value

        if m.Groups[2].Success then
            if Some v <> standing then
                Some(sprintf "`%s` is stamped unreleased in %s, which is not the standing <Version>" claim v)
            elif not (draftHeading v) then
                Some(
                    sprintf
                        "`%s` is stamped unreleased in %s, and the release ledger has no '## %s — DRAFT' heading"
                        claim
                        v
                        v
                )
            else
                None
        elif (PackageRosterTests.entryHeaderNaming v ledger).IsNone then
            Some(sprintf "`%s` is stamped as arriving in %s, which no release-ledger entry header names" claim v)
        else
            None)

let private repoRoot () : string =
    Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")

let private readmePath () = Snapshots.repoFile "README.md"

let private firstDiff (a: string) (b: string) : string =
    let xs = a.Replace("\r\n", "\n").Split('\n')
    let ys = b.Replace("\r\n", "\n").Split('\n')

    let i =
        Seq.zip xs ys
        |> Seq.tryFindIndex (fun (x, y) -> x <> y)
        |> Option.defaultValue (min xs.Length ys.Length)

    sprintf
        "first differing line %d:\n  committed: %s\n  derived:   %s"
        (i + 1)
        (if i < xs.Length then xs[i] else "<end>")
        (if i < ys.Length then ys[i] else "<end>")

// ---- the Fable line -------------------------------------------------------------

let private fableLineRe =
    Regex(
        @"Fable compiler `(?<compiler>\d+\.\d+\.\d+)`.{0,400}?`Fable\.Core` `(?<core>[\d.]+)`",
        RegexOptions.Compiled ||| RegexOptions.Singleline
    )

/// `(compiler, Fable.Core)` as the README's "which Fable version" line states them.
let internal fableLine (readme: string) : (string * string) option =
    let m = fableLineRe.Match readme

    if m.Success then
        Some(m.Groups["compiler"].Value, m.Groups["core"].Value)
    else
        None

/// The `fable` tool version a `dotnet-tools.json` manifest pins.
let internal fableToolPin (manifest: string) : string option =
    try
        use doc = JsonDocument.Parse manifest

        match doc.RootElement.TryGetProperty "tools" with
        | true, tools ->
            match tools.TryGetProperty "fable" with
            | true, f ->
                match f.TryGetProperty "version" with
                | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                | _ -> None
            | _ -> None
        | _ -> None
    with _ ->
        None

/// The `Fable.Core` version `Directory.Packages.props` pins.
let internal fableCorePin (packagesProps: string) : string option =
    let m =
        Regex.Match(packagesProps, @"<PackageVersion\s+Include=""Fable\.Core""\s+Version=""([^""]+)""")

    if m.Success then Some m.Groups[1].Value else None

/// The words the README's Fable line carries for the compiler version this repository's CI cannot
/// compare (Phase 397).
[<Literal>]
let unverifiedMarker = "unverified in this repository's CI"

/// The receiving gate's checkout names this variable when it is not beside this repository.
[<Literal>]
let receivingDirVariable = "FUARAN_DOTNET_DIR"

/// The receiving gate's tool manifest, if its checkout is present: the variable first, then beside
/// this repository's MAIN working tree (a linked worktree is anchored where its main tree is, as the
/// corpus resolver does) under the language's own directory or directly.
let private receivingManifest () : string option =
    let fromVar =
        match Environment.GetEnvironmentVariable receivingDirVariable with
        | null
        | "" -> []
        | d -> [ d ]

    let besides =
        match SiblingCorpus.mainWorkingTreeFrom (repoRoot ()) with
        | Error _ -> []
        | Ok main ->
            let rec ups (dir: DirectoryInfo) n acc =
                if isNull dir || n = 0 then
                    List.rev acc
                else
                    ups dir.Parent (n - 1) (dir.FullName :: acc)

            ups (DirectoryInfo main) 6 []
            |> List.collect (fun d ->
                [ Path.Combine(d, "Fuaran-UI", "fuaran-dotnet")
                  Path.Combine(d, "fuaran-dotnet") ])

    fromVar @ besides
    |> List.map (fun d -> Path.Combine(d, ".config", "dotnet-tools.json"))
    |> List.tryFind File.Exists

// ---- prose held to the tree (Phase 397) ---------------------------------------------
//
// The sentences a public repository is judged on, each read against the file it describes. A claim
// nothing reads drifts the day the tree moves: the 2026-10-07 drift table found a "Fable-compile
// gate" `verify.ps1` had not run since Phase 217, a feed the publish workflow no longer pushed to, a
// format command missing a directory the check covers, and a count of friend grants that was never
// true. Each reader below has a go-red case over synthetic input in the suite.

let private numberWords =
    [| "zero"
       "one"
       "two"
       "three"
       "four"
       "five"
       "six"
       "seven"
       "eight"
       "nine"
       "ten"
       "eleven"
       "twelve"
       "thirteen"
       "fourteen"
       "fifteen"
       "sixteen"
       "seventeen"
       "eighteen"
       "nineteen"
       "twenty" |]

let private tensWords =
    [| "twenty"
       "thirty"
       "forty"
       "fifty"
       "sixty"
       "seventy"
       "eighty"
       "ninety" |]

/// A count as the documents spell it: words below a hundred (`twenty-one`), digits beyond.
let internal numberWord (n: int) : string =
    if n >= 0 && n < numberWords.Length then
        numberWords[n]
    elif n > 20 && n < 100 then
        let tens = tensWords[n / 10 - 2]

        if n % 10 = 0 then
            tens
        else
            tens + "-" + numberWords[n % 10]
    else
        string n

/// A document's paragraphs (blank-line separated), each joined to one line.
let internal paragraphs (text: string) : string list =
    text.Replace("\r\n", "\n").Split("\n\n")
    |> Array.map (fun p -> p.Split('\n') |> Array.map _.Trim() |> String.concat " ")
    |> Array.filter (fun p -> p <> "")
    |> Array.toList

/// The stages `verify.ps1` runs, named as the documents name them, read from its native commands:
/// `(default stages, opt-in stages)`. `Error` names a command this reader does not know, so a stage
/// added to the gate cannot pass undocumented.
let internal verifyStages (script: string) : Result<string list * string list, string> =
    let commands =
        script.Replace("\r\n", "\n").Split('\n')
        |> Array.map _.Trim()
        |> Array.filter (fun l ->
            l.StartsWith("dotnet ", StringComparison.Ordinal)
            || l.StartsWith("pwsh ", StringComparison.Ordinal))
        |> Array.toList

    let classify (l: string) =
        if l = "dotnet tool restore" then
            Ok None
        elif l.StartsWith("dotnet fantomas --check", StringComparison.Ordinal) then
            Ok(Some(false, "format-check"))
        elif l.StartsWith("dotnet build", StringComparison.Ordinal) then
            Ok(Some(false, "build"))
        elif l.StartsWith("dotnet run --project tests/", StringComparison.Ordinal) then
            Ok(Some(false, "test"))
        elif l.StartsWith("dotnet run --project samples/", StringComparison.Ordinal) then
            Ok(Some(false, "sample"))
        elif l.Contains("proofs/check.ps1", StringComparison.Ordinal) then
            Ok(Some(true, "the proof leg"))
        else
            Error(sprintf "verify.ps1 runs a command this reader does not name: `%s`" l)

    let classified = commands |> List.map classify

    let firstError =
        classified
        |> List.tryPick (fun r ->
            match r with
            | Error e -> Some e
            | Ok _ -> None)

    match firstError with
    | Some e -> Error e
    | None when commands.IsEmpty -> Error "verify.ps1 runs no `dotnet` or `pwsh` command this reader found"
    | None ->
        let stages =
            classified
            |> List.choose (fun r ->
                match r with
                | Ok s -> s
                | Error _ -> None)

        Ok(stages |> List.filter (fst >> not) |> List.map snd, stages |> List.filter fst |> List.map snd)

let private verifyLineRe =
    Regex(@"^\./verify\.ps1\s+#\s*(?<desc>[^(\n]+?)\s*(?:\(|$)", RegexOptions.Compiled ||| RegexOptions.Multiline)

/// The stages a document's `./verify.ps1   # a + b + c (…)` line names, if it has one.
let internal documentedStages (doc: string) : string list option =
    let m = verifyLineRe.Match(doc.Replace("\r\n", "\n"))

    if m.Success then
        Some(m.Groups["desc"].Value.Split(" + ") |> Array.map _.Trim() |> Array.toList)
    else
        None

/// The directories `verify.ps1`'s format check covers.
let internal checkedFormatDirs (script: string) : string list =
    let m = Regex.Match(script, @"dotnet fantomas --check (?<d>[^\r\n#]+)")

    if m.Success then
        m.Groups["d"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
    else
        []

/// The directories a document's `` `dotnet fantomas …` `` command names.
let internal documentedFormatDirs (doc: string) : string list option =
    let m = Regex.Match(doc, @"`dotnet fantomas (?<d>[^`]+)`")

    if m.Success then
        Some(
            m.Groups["d"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            |> Array.toList
        )
    else
        None

/// The package sources a workflow pushes to (`--source <url>`).
let internal pushSources (workflow: string) : Set<string> =
    Regex.Matches(workflow, @"--source\s+(\S+)")
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Set.ofSeq

/// The package feeds a document names: a NuGet v3 service index URL, or the GitHub Packages host.
let internal namedFeeds (doc: string) : Set<string> =
    Regex.Matches(doc, @"https://[^\s`)<>]+/index\.json|https://nuget\.pkg\.github\.com[^\s`)<>]*")
    |> Seq.map _.Value
    |> Set.ofSeq

/// The `InternalsVisibleTo` declarations under `src/`: `(granting project, friend)`.
let internal friendGrants (root: string) : (string * string) list =
    Directory.GetDirectories(Path.Combine(root, "src"))
    |> Array.collect (fun d -> Directory.GetFiles(d, "*.fsproj"))
    |> Array.toList
    |> List.collect (fun f ->
        Regex.Matches(File.ReadAllText f, @"<InternalsVisibleTo\s+Include=""([^""]+)""")
        |> Seq.map (fun m -> Path.GetFileNameWithoutExtension f, m.Groups[1].Value)
        |> Seq.toList)
    |> List.sort

/// What is wrong with the contract's friend-grant paragraph: it states the packable count, the
/// count of grants between shipped assemblies and of the rest, and names every party to a shipped
/// grant.
let internal friendGrantFaults
    (contract: string)
    (packable: Set<string>)
    (grants: (string * string) list)
    : string list =
    let paragraph =
        paragraphs contract
        |> List.tryFind (fun p -> p.Contains("grants `InternalsVisibleTo` in", StringComparison.Ordinal))

    match paragraph with
    | None -> [ "STABILITY.md has no paragraph stating where `InternalsVisibleTo` is granted" ]
    | Some p ->
        let shipped = grants |> List.filter (fun (_, friend) -> packable.Contains friend)
        let rest = grants.Length - shipped.Length

        let expect (claim: string) =
            if p.Contains(claim, StringComparison.Ordinal) then
                []
            else
                [ sprintf "the friend-grant paragraph does not say \"%s\"" claim ]

        let restClaim =
            if rest = 1 then
                "the one further declaration"
            else
                sprintf "the %s further declarations" (numberWord rest)

        let parties =
            shipped
            |> List.collect (fun (g, f) -> [ g; f ])
            |> List.distinct
            |> List.filter (fun n -> not (p.Contains(sprintf "`%s`" n, StringComparison.Ordinal)))
            |> List.map (sprintf "the friend-grant paragraph does not name `%s`, a party to a grant")

        expect (sprintf "is %s packable assemblies" (numberWord packable.Count))
        @ expect (
            sprintf "exactly %s declaration%s" (numberWord shipped.Length) (if shipped.Length = 1 then "" else "s")
        )
        @ (if rest = 0 then [] else expect restClaim)
        @ parties

/// The packages the contract's Fable section names as off the surface, if it names any.
let internal namedFableExclusions (contract: string) : Set<string> option =
    paragraphs contract
    |> List.tryPick (fun p ->
        match p.IndexOf("Off the surface today:", StringComparison.Ordinal) with
        | -1 -> None
        | i ->
            let rest = p.Substring i

            let sentence =
                match rest.IndexOf(". ", StringComparison.Ordinal) with
                | -1 -> rest
                | j -> rest.Substring(0, j)

            Regex.Matches(sentence, @"`(Fuaran\.Core\.[A-Za-z.]*[A-Za-z])`")
            |> Seq.map (fun m -> m.Groups[1].Value)
            |> Set.ofSeq
            |> Some)

/// Paragraphs naming the typed actor's arrival by its phase without its version — one event, one
/// spelling (`Phase 320`, `0.0.1-alpha.13`).
let internal typedActorSpellingFaults (doc: string) : string list =
    paragraphs doc
    |> List.filter (fun p ->
        p.Contains("Phase 320", StringComparison.Ordinal)
        && not (p.Contains("0.0.1-alpha.13", StringComparison.Ordinal)))

/// The members the contract's bulleted lists name as `**`Fuaran.Core.…`**`.
let internal contractBulletMembers (contract: string) : string list =
    Regex.Matches(contract, @"(?m)^- \*\*`(Fuaran\.Core\.[\w.]+)`\*\*")
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.distinct
    |> Seq.toList

/// Of `names`, those no public-surface baseline carries.
let internal absentFromBaselines (baselines: string) (names: string list) : string list =
    names
    |> List.filter (fun n ->
        not (
            [ "("; " "; "`"; "\n" ]
            |> List.exists (fun after -> baselines.Contains(n + after, StringComparison.Ordinal))
        ))

// ---- counts held to the tree (Phase 389) -----------------------------------------
//
// The counts the 2026-10-07 design review found wrong — "twelve frozen records" over sixteen, "eight
// topic modules" over twenty-one, a dependency order four rows stale — each read here against the
// thing it counts, so the next record frozen or module split is a red gate rather than a review
// finding.

/// What is wrong with the contract's witness-freeze counts: the freeze paragraph and the enforcement
/// paragraph each state the frozen list's length, and the freeze paragraph names every frozen record.
let internal frozenCountFaults (contract: string) (frozen: string list) : string list =
    let n = numberWord frozen.Length
    let ps = paragraphs contract

    let find (marker: string) =
        ps |> List.tryFind (fun p -> p.Contains(marker, StringComparison.Ordinal))

    let freeze =
        match find "public witness records (" with
        | None -> [ "STABILITY.md has no paragraph naming the public witness records" ]
        | Some p ->
            [ if not (p.Contains(sprintf "The %s public witness records" n, StringComparison.Ordinal)) then
                  sprintf "the freeze paragraph does not say \"The %s public witness records\"" n
              for r in frozen do
                  if not (p.Contains(sprintf "`%s`" r, StringComparison.Ordinal)) then
                      sprintf "the freeze paragraph does not name `%s`, a frozen record" r ]

    let enforcement =
        match find "frozen records is a" with
        | None -> [ "STABILITY.md has no enforcement paragraph counting the frozen records" ]
        | Some p when p.Contains(sprintf "any of the %s frozen records" n, StringComparison.Ordinal) -> []
        | Some _ -> [ sprintf "the enforcement paragraph does not say \"any of the %s frozen records\"" n ]

    freeze @ enforcement

/// The Conformance kit's topic modules: the compile items strictly between the runner (`LawKit.fs`)
/// and the facade (`Conformance.fs`), in compile order.
let internal topicModules (fsproj: string) : string list =
    let items =
        Regex.Matches(fsproj, @"<Compile\s+Include=""([^""]+)\.fs""")
        |> Seq.map (fun m -> m.Groups[1].Value)
        |> Seq.toList

    match List.tryFindIndex ((=) "LawKit") items, List.tryFindIndex ((=) "Conformance") items with
    | Some a, Some b when a < b -> items[a + 1 .. b - 1]
    | _ -> []

/// What is wrong with the two sentences that count the topic modules: the project's compile-order
/// comment states the count, and the facade's header states it and names every module.
let internal topicModuleFaults (fsproj: string) (facade: string) (modules: string list) : string list =
    let claim = sprintf "the %s topic modules" (numberWord modules.Length)

    let joined (text: string) =
        Regex.Replace(text.Replace("\r\n", "\n"), @"\n\s*(//\s*)?", " ")

    let comment = joined fsproj
    let header = joined facade

    [ if not (comment.Contains(claim, StringComparison.Ordinal)) then
          sprintf "Fuaran.Core.Conformance.fsproj's compile-order comment does not say \"%s\"" claim
      if not (header.Contains(claim, StringComparison.Ordinal)) then
          sprintf "Conformance.fs's header does not say \"%s\"" claim
      for m in modules do
          if not (header.Contains(sprintf "`%s`" m, StringComparison.Ordinal)) then
              sprintf "Conformance.fs's header does not name `%s`, a topic module" m ]

/// The project graph under `src/`: each project's name without its `Fuaran.Core.` prefix, and the
/// same names of its direct project references.
let internal projectGraph (root: string) : Map<string, Set<string>> =
    let short (name: string) =
        if name.StartsWith("Fuaran.Core.", StringComparison.Ordinal) then
            name.Substring "Fuaran.Core.".Length
        else
            name

    Directory.GetDirectories(Path.Combine(root, "src"))
    |> Array.collect (fun d -> Array.append (Directory.GetFiles(d, "*.fsproj")) (Directory.GetFiles(d, "*.csproj")))
    |> Array.map (fun f ->
        let refs =
            Regex.Matches(File.ReadAllText f, @"<ProjectReference\s+Include=""([^""]+)""")
            |> Seq.map (fun m -> short (Path.GetFileNameWithoutExtension(m.Groups[1].Value.Replace('\\', '/'))))
            |> Set.ofSeq

        short (Path.GetFileNameWithoutExtension f), refs)
    |> Map.ofArray

/// The README's dependency sentence, read as data: each package and the packages it is said to be
/// over. A clause this reader does not know is an `Error`, so a reworded sentence cannot pass unread.
let internal documentedDependencies (readme: string) : Result<Map<string, Set<string>>, string> =
    let name = @"`([A-Za-z][\w.]*)`"

    let names (s: string) =
        Regex.Matches(s, name) |> Seq.map (fun m -> m.Groups[1].Value) |> Seq.toList

    let over = Regex(@"^" + name + @" over ((?:`[A-Za-z][\w.]*`(?: \+ )?)+)$")
    let alone = Regex(@"^((?:`[A-Za-z][\w.]*`(?:, | and )?)+) standalone$")

    let read (clause: string) =
        let o = over.Match clause
        let a = alone.Match clause

        if o.Success then
            Ok [ o.Groups[1].Value, Set.ofList (names o.Groups[2].Value) ]
        elif a.Success then
            Ok(names a.Groups[1].Value |> List.map (fun n -> n, Set.empty))
        else
            Error(sprintf "a clause of the dependency sentence the reader does not know: '%s'" clause)

    match
        paragraphs readme
        |> List.tryFind (fun p -> p.StartsWith("Dependency order", StringComparison.Ordinal))
    with
    | None -> Error "README.md has no paragraph opening `Dependency order`"
    | Some p ->
        let body =
            match p.IndexOf(": ", StringComparison.Ordinal) with
            | -1 -> p
            | i -> p.Substring(i + 2)

        let clauses =
            Regex.Replace(body, @" \(since `[^`]+`(, unreleased)?\)", "").Trim().TrimEnd('.').Split("; ")
            |> Array.toList

        (Ok [], clauses)
        ||> List.fold (fun acc clause ->
            match acc, read clause with
            | Error e, _
            | _, Error e -> Error e
            | Ok xs, Ok ys -> Ok(xs @ ys))
        |> Result.bind (fun pairs ->
            match pairs |> List.countBy fst |> List.filter (fun (_, c) -> c > 1) with
            | [] -> Ok(Map.ofList pairs)
            | twice ->
                Error(
                    sprintf
                        "the dependency sentence places %s more than once"
                        (twice |> List.map fst |> String.concat ", ")
                ))

/// Where the README's dependency sentence and the project graph disagree, one line per package.
let internal dependencyFaults (documented: Map<string, Set<string>>) (graph: Map<string, Set<string>>) : string list =
    let missing =
        graph
        |> Map.toList
        |> List.filter (fun (p, _) -> not (documented.ContainsKey p))
        |> List.map (fun (p, _) -> sprintf "`%s` is a project under src/ the sentence does not place" p)

    let unknown =
        documented
        |> Map.toList
        |> List.filter (fun (p, _) -> not (graph.ContainsKey p))
        |> List.map (fun (p, _) -> sprintf "`%s` is placed by the sentence and is no project under src/" p)

    let wrong =
        documented
        |> Map.toList
        |> List.choose (fun (p, said) ->
            match graph.TryFind p with
            | Some refs when refs <> said ->
                Some(
                    sprintf
                        "`%s` is said to be over {%s} and references {%s}"
                        p
                        (String.concat ", " said)
                        (String.concat ", " refs)
                )
            | _ -> None)

    missing @ unknown @ wrong

// ---- the suite -----------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "ReadmeClaims"
        [ test
              "every README claim of a surface carries the version it arrived in (CORE_APPROVE_README=1 regenerates them)" {
              let root = repoRoot ()

              match releaseIndexes root with
              | Error why -> skiptest ("the release history could not be read, so no arrival can be derived: " + why)
              | Ok [] -> skiptest "no release tag carries api/ baselines in this clone, so no arrival can be derived"
              | Ok releases ->
                  let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

                  let standing =
                      PackageRosterTests.standingVersion props
                      |> Option.defaultWith (fun () -> failtest "Directory.Build.props carries no <Version>")

                  let current = currentIndex root
                  let readme = File.ReadAllText(readmePath ())
                  let floor = fst releases.Head

                  Approval.validate Approval.Readme

                  match stampReadme (arrivalOf releases current standing) floor readme with
                  | Error why -> failtestf "the README's generated parts could not be located: %s" why
                  | Ok derived when derived = readme.Replace("\r\n", "\n") -> ()
                  | Ok derived when Approval.admits Approval.Readme Approval.Files.Readme ->
                      Approval.write Approval.Readme Approval.Files.Readme (readmePath ()) derived
                      |> ignore
                  | Ok derived ->
                      failtestf
                          "README.md's version stamps are not the derivation's — %s\nRe-run with CORE_APPROVE_README=1 and commit the README."
                          (firstDiff readme derived)
          }

          test
              "a stamp naming an unreleased version names the DRAFT heading in the release ledger, and a released one an entry" {
              let root = repoRoot ()
              let readme = File.ReadAllText(readmePath ())
              let ledger = PackageRosterTests.ledgerText (PackageRosterTests.ledgerFiles root)
              let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

              Expect.isNonEmpty ledger "the release ledger was read"

              Expect.isEmpty
                  (stampFaults (PackageRosterTests.standingVersion props) ledger readme)
                  "a README stamp must name a version the release ledger heads as what it is"
          }

          test
              "the stamp check refuses an unreleased stamp without a DRAFT heading, off the standing version, and a released one without an entry" {
              let stability =
                  "## 0.34.0 — DRAFT\n\n## 0.33.0 — released 2026-10-01 as `v0.33.0`\n"

              let ok = "`A.b` (since `0.34.0`, unreleased) and `C.d` (since `0.33.0`)"

              Expect.isEmpty
                  (stampFaults (Some "0.34.0") stability ok)
                  "draft and released stamps over their headings pass"

              Expect.isNonEmpty
                  (stampFaults (Some "0.34.0") (stability.Replace("— DRAFT", "— released")) ok)
                  "an unreleased stamp over a released heading is refused"

              Expect.isNonEmpty
                  (stampFaults (Some "0.35.0") stability ok)
                  "an unreleased stamp naming a version other than the standing one is refused"

              Expect.isNonEmpty
                  (stampFaults (Some "0.34.0") stability "`C.d` (since `0.32.0`)")
                  "a released stamp no entry header names is refused"
          }

          test
              "the stamp derivation stamps once per section, skips fences and the floor, and refuses a README without the floor block" {
              let arrival name =
                  match name with
                  | "Tree.old" -> Some AtFloor
                  | "Tree.mid" -> Some(Released "0.31.0")
                  | "Tree.fresh"
                  | "Fresh" -> Some(Unreleased "0.34.0")
                  | _ -> None

              let readme =
                  String.concat
                      "\n"
                      [ "# T"
                        floorBegin
                        "stale text"
                        floorEnd
                        "`Tree.old` and `Tree.mid` (since `0.29.0`) and `Tree.mid` again, `Tree.fresh`, `prose`."
                        "```"
                        "`Tree.mid` in a fence"
                        "```"
                        "## Next"
                        "`Tree.mid` again in a new section, and `Fresh<'T>`, `Tree.fresh nodew` and `Unknown.name`." ]

              match stampReadme arrival "0.27.0" readme with
              | Error why -> failtestf "derivation refused a well-formed README: %s" why
              | Ok derived ->
                  let ls = derived.Split('\n')
                  Expect.equal ls[2] (floorStatement "0.27.0") "the floor statement is re-rendered"

                  Expect.equal
                      ls[4]
                      "`Tree.old` and `Tree.mid` (since `0.31.0`) and `Tree.mid` again, `Tree.fresh` (since `0.34.0`, unreleased), `prose`."
                      "a wrong stamp is replaced, a repeat is not stamped, a floor arrival carries none, prose is not a claim"

                  Expect.equal ls[6] "`Tree.mid` in a fence" "fenced code is untouched"

                  Expect.equal
                      ls[9]
                      "`Tree.mid` (since `0.31.0`) again in a new section, and `Fresh<'T>` (since `0.34.0`, unreleased), `Tree.fresh nodew` (since `0.34.0`, unreleased) and `Unknown.name`."
                      "a new section stamps again; a type argument and a call keep the claim; an unknown name is not one"

              Expect.isError
                  (stampReadme arrival "0.27.0" "# T\n`Tree.mid`")
                  "a README without the floor block is refused"
          }

          test "the surface index reads the baseline line shapes it is fed" {
              let lines =
                  [ "# a header line"
                    "type Fuaran.Core.Tree+WellFormed`1 (union)"
                    "method Fuaran.Core.ArbitrationModule.arbitrate`2(x) : y"
                    "union-case Fuaran.Core.Deferred`1.NewReady #1(Item: !0)"
                    "union-case Fuaran.Core.Deferred`1.Pending #0()"
                    "record-field Fuaran.Core.KeyedWitness`2.IdsUnique #4 : z"
                    "field Fuaran.Core.Deferred`1+Tags.Ready : System.Int32 (literal)"
                    "ctor Fuaran.Core.IdWitness`1..ctor(a)" ]

              let idx = indexOf lines

              for k in
                  [ "Tree.WellFormed"
                    "Fuaran.Core.Tree.WellFormed"
                    "Arbitration.arbitrate"
                    "ArbitrationModule.arbitrate"
                    "Deferred.Ready"
                    "Ready"
                    "Pending"
                    "KeyedWitness.IdsUnique" ] do
                  Expect.isTrue (idx.Contains k) (sprintf "'%s' resolves" k)

              for k in [ "NewReady"; "Tags"; "IdWitness"; "Tags.Ready" ] do
                  Expect.isFalse (idx.Contains k) (sprintf "'%s' does not resolve" k)
          }

          test "a claim candidate is the span's leading name, and a literal, a path or a version is none" {
              Expect.equal
                  (candidateOfSpan "Tree.traversal nodew keyw")
                  (Some "Tree.traversal")
                  "a call claims its function"

              Expect.equal (candidateOfSpan "Deferred<'T>") (Some "Deferred") "a type argument keeps the claim"
              Expect.equal (candidateOfSpan "KeyedWitness") (Some "KeyedWitness") "a capitalised name is a claim"

              Expect.equal
                  (candidateOfSpan "evalWith")
                  (Some "evalWith")
                  "a bare name is a candidate; the index decides"

              Expect.equal (candidateOfSpan "\"int\"") None "a string literal is not a name"
              Expect.equal (candidateOfSpan "a-b") None "a hyphenated word is not a name"
              Expect.equal (candidateOfSpan "proofs/Chain.fst") None "a path is not a name"
              Expect.equal (candidateOfSpan "0.31.0") None "a version is not a name"
          }

          test "the README names the Fable versions the receiving gate and this repository pin" {
              let root = repoRoot ()
              let readme = File.ReadAllText(readmePath ())

              match fableLine readme with
              | None ->
                  failtest
                      "README.md states no \"which Fable version\" line (\"… Fable compiler `X.Y.Z` … `Fable.Core` `A.B` …\")"
              | Some(compiler, core) ->
                  let ownPin =
                      fableCorePin (File.ReadAllText(Path.Combine(root, "Directory.Packages.props")))

                  Expect.equal (Some core) ownPin "the README's Fable.Core version is Directory.Packages.props' pin"

                  // The compiler version is a claim about another repository's pin. Where that checkout
                  // is absent (every CI run) the claim cannot be compared, so the README must SAY so —
                  // held here — and the run reports it rather than skipping: a skipped check reads as
                  // a passing one in a green report.
                  Expect.isTrue
                      (readme.Replace("\r\n", "\n").Replace("\n", " ").Contains(unverifiedMarker))
                      (sprintf "the README's Fable line marks its compiler version \"%s\"" unverifiedMarker)

                  match receivingManifest () with
                  | None ->
                      printfn
                          "README Fable compiler version %s: UNVERIFIED on this run — the receiving gate's checkout was not found (set %s to it), as the README states"
                          compiler
                          receivingDirVariable
                  | Some manifest ->
                      Expect.equal
                          (fableToolPin (File.ReadAllText manifest))
                          (Some compiler)
                          (sprintf "the README's Fable compiler version is the receiving gate's pin (%s)" manifest)
          }

          test "the Fable readers find the line and the pins, and refuse what is not there" {
              Expect.equal
                  (fableLine "built with the Fable compiler `5.0.0`, against `Fable.Core` `5.0`.")
                  (Some("5.0.0", "5.0"))
                  "the line is read"

              Expect.isNone (fableLine "Fable is supported.") "a line without versions is not one"

              Expect.equal
                  (fableToolPin """{"version":1,"tools":{"fable":{"version":"5.0.0","commands":["fable"]}}}""")
                  (Some "5.0.0")
                  "the manifest pin is read"

              Expect.isNone (fableToolPin """{"tools":{"fantomas":{"version":"7.0.5"}}}""") "no fable tool, no pin"

              Expect.equal
                  (fableCorePin """<PackageVersion Include="Fable.Core" Version="5.0" />""")
                  (Some "5.0")
                  "the package pin is read"
          }

          // ---- prose held to the tree (Phase 397) ------------------------------------

          test "CONTRIBUTING and the README name the stages verify.ps1 runs, and no other" {
              let root = repoRoot ()

              match verifyStages (File.ReadAllText(Path.Combine(root, "verify.ps1"))) with
              | Error e -> failtest e
              | Ok(stages, optIn) ->
                  Expect.isNonEmpty stages "verify.ps1's default stages were read"

                  for doc in [ "CONTRIBUTING.md"; "README.md" ] do
                      let text = File.ReadAllText(Path.Combine(root, doc))

                      match documentedStages text with
                      | None -> failtestf "%s has no `./verify.ps1   # <stages>` line" doc
                      | Some named ->
                          Expect.equal
                              named
                              stages
                              (sprintf "%s's `./verify.ps1` line names the stages verify.ps1 runs, in order" doc)

                  let readme = File.ReadAllText(readmePath ())

                  for stage in optIn do
                      Expect.isTrue
                          (readme.Contains("./verify.ps1 -Proofs", StringComparison.Ordinal))
                          (sprintf "the README documents the opt-in stage (%s) with its switch" stage)
          }

          test "the format command CONTRIBUTING names covers the directories the format check covers" {
              let root = repoRoot ()
              let dirs = checkedFormatDirs (File.ReadAllText(Path.Combine(root, "verify.ps1")))

              Expect.isNonEmpty dirs "verify.ps1's format-check directories were read"

              Expect.equal
                  (documentedFormatDirs (File.ReadAllText(Path.Combine(root, "CONTRIBUTING.md"))))
                  (Some dirs)
                  "CONTRIBUTING.md's `dotnet fantomas …` names the directories `verify.ps1` checks"
          }

          test "the package feed STABILITY.md names is the one the publish workflow pushes to" {
              let root = repoRoot ()

              let sources =
                  pushSources (File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-packages.yml")))

              Expect.isNonEmpty sources "the publish workflow's `--source` was read"

              Expect.equal
                  (namedFeeds (File.ReadAllText(Path.Combine(root, "STABILITY.md"))))
                  sources
                  "STABILITY.md names exactly the feeds the publish workflow pushes to"
          }

          test "the friend-grant paragraph states the tree's counts and names every party" {
              let root = repoRoot ()

              let packable =
                  PackageRosterTests.packableProjects root |> List.map _.PackageId |> Set.ofList

              let grants = friendGrants root
              Expect.isNonEmpty grants "the `InternalsVisibleTo` declarations under src/ were read"

              Expect.isEmpty
                  (friendGrantFaults (File.ReadAllText(Path.Combine(root, "STABILITY.md"))) packable grants)
                  "STABILITY.md's friend-grant paragraph is the tree's"
          }

          test "the Fable exclusions STABILITY.md names are fable-exclusions.json's, and each is a project here" {
              let root = repoRoot ()

              let excluded =
                  PackageRosterTests.declaredExclusions (File.ReadAllText(Path.Combine(root, "fable-exclusions.json")))
                  |> Option.defaultWith (fun () -> failtest "fable-exclusions.json was not read")

              let projects =
                  Directory.GetDirectories(Path.Combine(root, "src"))
                  |> Array.collect (fun d ->
                      Array.append (Directory.GetFiles(d, "*.fsproj")) (Directory.GetFiles(d, "*.csproj")))
                  |> Array.map Path.GetFileNameWithoutExtension
                  |> Set.ofArray

              Expect.isNonEmpty excluded "fable-exclusions.json declares at least one package"

              Expect.isEmpty
                  (Set.difference excluded projects)
                  "every package fable-exclusions.json names is a project under src/"

              Expect.equal
                  (namedFableExclusions (File.ReadAllText(Path.Combine(root, "STABILITY.md"))))
                  (Some excluded)
                  "STABILITY.md's \"Off the surface today\" sentence names exactly fable-exclusions.json's packages"
          }

          test "the typed actor's arrival is spelt one way in STABILITY.md and docs/ADOPTION.md" {
              let root = repoRoot ()

              for doc in [ "STABILITY.md"; Path.Combine("docs", "ADOPTION.md") ] do
                  let text = File.ReadAllText(Path.Combine(root, doc))

                  Expect.isTrue
                      (text.Contains("Phase 320", StringComparison.Ordinal))
                      (sprintf "%s names the event" doc)

                  Expect.isEmpty
                      (typedActorSpellingFaults text)
                      (sprintf "%s names Phase 320 without `0.0.1-alpha.13`" doc)
          }

          test "every member a contract bullet names is on a public-surface baseline" {
              let root = repoRoot ()

              let names =
                  contractBulletMembers (File.ReadAllText(Path.Combine(root, "STABILITY.md")))

              Expect.isNonEmpty names "the contract's member bullets were read"

              let baselines =
                  Directory.GetFiles(Path.Combine(root, "api"), "*.txt")
                  |> Array.map File.ReadAllText
                  |> String.concat "\n"

              Expect.isEmpty
                  (absentFromBaselines baselines names)
                  "STABILITY.md names a member no api/ baseline carries — a member that left the repository belongs in the release ledger, not the contract"
          }

          test "the prose readers red what drifted (go-red over synthetic input)" {
              let script =
                  "dotnet tool restore\n    dotnet fantomas --check src tests samples\ndotnet build X.slnx\ndotnet run --project tests/T --no-build\ndotnet run --project samples/adoption\n    pwsh ./proofs/check.ps1 -Runs 3"

              Expect.equal
                  (verifyStages script)
                  (Ok([ "format-check"; "build"; "test"; "sample" ], [ "the proof leg" ]))
                  "the stages are read in order, the opt-in leg apart"

              Expect.isError
                  (verifyStages (script + "\ndotnet fable src"))
                  "a command the reader does not name is red, so a new stage cannot pass undocumented"

              Expect.equal
                  (documentedStages
                      "```\n./verify.ps1     # format-check + build + Fable-compile gate + test (the green gate)\n```")
                  (Some [ "format-check"; "build"; "Fable-compile gate"; "test" ])
                  "the drifted line Phase 397 corrected reads as the stages it named"

              Expect.equal (checkedFormatDirs script) [ "src"; "tests"; "samples" ] "the format-check directories"

              Expect.equal
                  (documentedFormatDirs "(or `dotnet fantomas src tests`)")
                  (Some [ "src"; "tests" ])
                  "the drifted command reads as two directories"

              Expect.equal
                  (namedFeeds "published to `https://nuget.pkg.github.com/fuaran-ui/index.json`")
                  (set [ "https://nuget.pkg.github.com/fuaran-ui/index.json" ])
                  "a GitHub Packages feed is a feed this reader sees"

              let contract =
                  "A\n\n`Fuaran.Core.*` is eighteen packable assemblies. It grants `InternalsVisibleTo` in exactly one declaration — `A` to `B`; the one further declaration is a test's.\n"

              let packable = set [ "A"; "B" ]

              Expect.isNonEmpty
                  (friendGrantFaults contract packable [ "A", "B"; "C", "Tests" ])
                  "a packable count the tree does not have is red"

              Expect.exists
                  (friendGrantFaults
                      (contract.Replace("eighteen", "three"))
                      (Set.add "C" packable)
                      [ "A", "B"; "A", "C"; "C", "Tests" ])
                  (fun f -> f.Contains "exactly two declarations")
                  "a grant count the tree does not have is red"

              Expect.isEmpty
                  (friendGrantFaults (contract.Replace("eighteen", "two")) packable [ "A", "B"; "C", "Tests" ])
                  "the paragraph that matches the tree is green"

              Expect.equal
                  (namedFableExclusions
                      "Prose. Off the surface today: `Fuaran.Core.Idl.Codegen` and `Fuaran.Core.CSharp` (a C# assembly). `Fuaran.Core.Other` is later.")
                  (Some(set [ "Fuaran.Core.Idl.Codegen"; "Fuaran.Core.CSharp" ]))
                  "the sentence's names are read, and the next sentence's are not"

              Expect.equal
                  (typedActorSpellingFaults
                      "folded in (Phase 320, `0.0.1-alpha.13`).\n\nfolded into the hash since Phase 320, so")
                  [ "folded into the hash since Phase 320, so" ]
                  "the spelling without the version is red"

              Expect.equal
                  (absentFromBaselines
                      "method Fuaran.Core.Memo.isMemoisable(Fuaran.Core.EffectClass) : System.Boolean\n"
                      (contractBulletMembers
                          "- **`Fuaran.Core.Memo.isMemoisable`** — kept\n- **`Fuaran.Core.DataFrame.cellString`** — moved"))
                  [ "Fuaran.Core.DataFrame.cellString" ]
                  "a member no baseline carries is red"
          }

          test "the witness-freeze counts are the frozen list's length, and the freeze names every frozen record" {
              let root = repoRoot ()
              let frozen = Fuaran.Core.SurfaceLaws.frozenWitnessFields |> List.map _.Record
              Expect.isNonEmpty frozen "the frozen witness list was read"

              Expect.isEmpty
                  (frozenCountFaults (File.ReadAllText(Path.Combine(root, "STABILITY.md"))) frozen)
                  "STABILITY.md's witness-freeze counts are the tree's"
          }

          test "the Conformance topic-module count is the project's compile list, and the facade names each" {
              let root = repoRoot ()
              let dir = Path.Combine(root, "src", "Fuaran.Core.Conformance")
              let fsproj = File.ReadAllText(Path.Combine(dir, "Fuaran.Core.Conformance.fsproj"))
              let modules = topicModules fsproj
              Expect.isNonEmpty modules "the topic modules were read from the compile list"

              Expect.isEmpty
                  (topicModuleFaults fsproj (File.ReadAllText(Path.Combine(dir, "Conformance.fs"))) modules)
                  "the compile-order comment and the facade header count and name the topic modules"
          }

          test "the README's dependency sentence is the project graph under src/" {
              let root = repoRoot ()
              let graph = projectGraph root
              Expect.isNonEmpty graph "the projects under src/ were read"

              match documentedDependencies (File.ReadAllText(readmePath ())) with
              | Error why -> failtest why
              | Ok documented ->
                  Expect.isEmpty
                      (dependencyFaults documented graph)
                      "README.md places each package over exactly its direct project references"
          }

          test "the count readers red what drifted (go-red over synthetic input)" {
              Expect.equal (numberWord 21) "twenty-one" "a compound count is spelt as the documents spell it"
              Expect.equal (numberWord 16) "sixteen" "a count below twenty is one word"

              let contract =
                  "The two public witness records (`AWitness`, `BWitness`) are plain.\n\n**Enforcement.** Code that adds a field to any of the two frozen records is a regression.\n"

              Expect.isEmpty (frozenCountFaults contract [ "AWitness"; "BWitness" ]) "the matching contract is green"

              Expect.equal
                  (frozenCountFaults contract [ "AWitness"; "BWitness"; "CWitness" ] |> List.length)
                  3
                  "a third frozen record reds both counts and the missing name"

              let fsproj =
                  "<Compile Include=\"LawKit.fs\" />\n<Compile Include=\"ALaws.fs\" />\n<Compile Include=\"BLaws.fs\" />\n<Compile Include=\"Conformance.fs\" />\n<!-- then the two topic modules,\n     each internal -->"

              let facade = "//  live in the two topic\n//  modules ahead of it — `ALaws`, `BLaws`"
              Expect.equal (topicModules fsproj) [ "ALaws"; "BLaws" ] "the modules between the runner and the facade"
              Expect.isEmpty (topicModuleFaults fsproj facade [ "ALaws"; "BLaws" ]) "a count split across lines is read"

              Expect.equal
                  (topicModuleFaults fsproj facade [ "ALaws"; "BLaws"; "CLaws" ] |> List.length)
                  3
                  "a third module reds both counts and the unnamed module"

              let readme =
                  "Dependency order — prose: `A` and `B` standalone; `C` (since `0.35.2`) over `A` + `B`.\n"

              let graph = Map.ofList [ "A", Set.empty; "B", Set.empty; "C", set [ "A"; "B" ] ]

              match documentedDependencies readme with
              | Error why -> failtest why
              | Ok documented ->
                  Expect.isEmpty (dependencyFaults documented graph) "the matching sentence is green, stamps read past"

                  Expect.equal
                      (dependencyFaults documented (Map.add "C" (set [ "A" ]) graph))
                      [ "`C` is said to be over {A, B} and references {A}" ]
                      "a reference the graph does not have is red"

                  Expect.equal
                      (dependencyFaults documented (Map.add "D" Set.empty graph))
                      [ "`D` is a project under src/ the sentence does not place" ]
                      "an unplaced project is red"

              Expect.isError
                  (documentedDependencies "Dependency order: `A` sits under `B`.")
                  "a clause the reader does not know is red, so a reworded sentence cannot pass unread"

              Expect.isError
                  (documentedDependencies "Dependency order: `A` standalone; `A` over `B`.")
                  "a package placed twice is red"
          } ]
