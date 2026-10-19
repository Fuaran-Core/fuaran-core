/// Phase 503 - the release ledger, typed and read once.
///
/// `docs/releases/<version>.md` is the ledger, one file per version slot, and `docs/releases/README.md`
/// its index (DECISIONS.md D130). Two families used to parse the slot files by hand, each its own way:
/// the `Package roster` family held the ledger's shape over a concatenated text, and the
/// `Release record` family held the release gesture (D123) over the same files parsed again. Here
/// each slot file is parsed ONCE into a `Slot`, every clause of both families reads the `Slot list`,
/// and the index is RENDERED from it rather than hand-copied per release.
///
/// The heading grammars are exactly the ones D123 and D130 state, unchanged:
///
///   * a slot file opens with `## <version> — …` and carries no other slot's heading;
///   * a tagged slot is headed `## <v> — [title — ]released <yyyy-mm-dd> as `v<v>``;
///   * the untagged standing `<Version>` is headed `## <v> — DRAFT`, and only it may be;
///   * a released slot from `recordFloor` carries a `**Release record` paragraph and ONE
///     `**Receiving gate run:**` paragraph, naming the run in the fixed grammar, or saying
///     `none cited before the tag` beside the decision that admitted the gap.
///
/// The closed no-run map D123 kept in source (`{0.31.0, 0.35.1}`) is SLOT DATA now: the slot's own
/// no-run paragraph names the decision, and that decision's heading must name the slot's version. A
/// slot joins the set only by a decision that names it, exactly as before; the list just stopped
/// being a second copy of what the two slot files already said.
module Fuaran.Core.Tests.Ledger

open System
open System.IO
open System.Text.RegularExpressions

/// The ledger directory, repo-relative.
[<Literal>]
let Dir = "docs/releases"

/// The index, repo-relative: a render of the slots' headings, newest first.
[<Literal>]
let IndexPath = Dir + "/README.md"

/// The oldest released slot whose entry must carry the receiving gate's record: `0.31.0`, the first
/// version cut after DECISIONS.md D55 moved the Fable gate to the receiving host. Older slots were
/// gated by this repository's own Fable leg at their tag, so there is no external run to cite and
/// demanding one would demand invention.
let recordFloor = (0, 31, 0)

/// What a slot's heading claims about its version.
type State =
    /// `## <v> — [title — ]released <date> as `v<v>``.
    | Released of date: string
    /// `## <v> — DRAFT…`.
    | Draft
    /// Any other spelling (`never released; …`, a lower-case `(draft)`, a malformed release line),
    /// or no heading of its own at all.
    | Other

/// What a slot's `**Receiving gate run:**` paragraphs say.
type RunRecord =
    /// No such paragraph.
    | Absent
    /// One paragraph that does not say `none cited`; the record grammar is the release clause's.
    | Cited of paragraph: string
    /// `none cited before the tag`, with the first decision (`D<n>`) the paragraph names.
    | NotCited of decision: string option
    /// More than one such paragraph.
    | Several of count: int

/// One slot file, parsed once.
type Slot =
    {
        Version: string
        /// Repo-relative, forward-slashed.
        Path: string
        /// LF line endings.
        Text: string
        /// The first non-blank line, which must be the slot's heading.
        FirstLine: string
        /// The slot's own `## <version> …` heading (the first), trailing space trimmed, with its
        /// 1-based line; `None` when the file carries none.
        Heading: (int * string) option
        State: State
        /// The other slots' `## <version>` headings this file carries, `(version, heading)`.
        Foreign: (string * string) list
        /// The paragraphs under the slot's heading, up to the next level-2 heading, each joined to one
        /// line (so a hard wrap at any space cannot hide a record).
        Paragraphs: string list
        Run: RunRecord
    }

let private versionHeadingRe =
    Regex(@"^## (?<v>\d+\.\d+\.\d+)(?:\s|$)", RegexOptions.Compiled)

let private noRunRe =
    Regex(@"^\*\*Receiving gate run:\*\* none cited before the tag\b", RegexOptions.Compiled)

let private decisionRe = Regex(@"\bD\d+\b", RegexOptions.Compiled)

/// `(major, minor, patch)` of a `X.Y.Z` version; `(0, 0, 0)` for anything else.
let versionTriple (v: string) : int * int * int =
    match v.Split('.') |> Array.map int with
    | [| a; b; c |] -> (a, b, c)
    | _ -> (0, 0, 0)

/// Every `## <version> …` heading of a document, `(version, heading)`, in order, trailing space trimmed.
let versionHeadings (text: string) : (string * string) list =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.toList
    |> List.choose (fun l ->
        let m = versionHeadingRe.Match l

        if m.Success then
            Some(m.Groups["v"].Value, l.TrimEnd())
        else
            None)

/// What `heading` claims about `version`.
let headingState (version: string) (heading: string) : State =
    let v = Regex.Escape version

    let released =
        Regex.Match(heading.TrimEnd(), sprintf @"^## %s — (?:.+ — )?released (\d{4}-\d{2}-\d{2}) as `v%s`$" v v)

    if released.Success then
        Released released.Groups[1].Value
    elif Regex.IsMatch(heading, sprintf @"^## %s — DRAFT\b" v) then
        Draft
    else
        Other

let private paragraphsOf (lines: string list) : string list =
    let flush (acc: string list) (cur: string list) =
        match cur with
        | [] -> acc
        | _ -> (cur |> List.rev |> List.map _.Trim() |> String.concat " ") :: acc

    let acc, cur =
        lines
        |> List.fold
            (fun (acc, cur) (l: string) ->
                if String.IsNullOrWhiteSpace l then
                    flush acc cur, []
                else
                    acc, l :: cur)
            ([], [])

    flush acc cur |> List.rev

let private runOf (paragraphs: string list) : RunRecord =
    match
        paragraphs
        |> List.filter (fun p -> p.StartsWith("**Receiving gate run:**", StringComparison.Ordinal))
    with
    | [] -> Absent
    | [ p ] when noRunRe.IsMatch p ->
        let d = decisionRe.Match p
        NotCited(if d.Success then Some d.Value else None)
    | [ p ] -> Cited p
    | many -> Several many.Length

/// The slot file for `version` at `path`, parsed.
let parse (version: string) (path: string) (text: string) : Slot =
    let text = text.Replace("\r\n", "\n")
    let lines = text.Split('\n')

    let own =
        lines
        |> Array.tryFindIndex (fun l ->
            let m = versionHeadingRe.Match l
            m.Success && m.Groups["v"].Value = version)

    let paragraphs =
        match own with
        | None -> []
        | Some i ->
            let next =
                lines
                |> Array.indexed
                |> Array.tryFind (fun (j, l) -> j > i && l.StartsWith("## ", StringComparison.Ordinal))
                |> Option.map fst
                |> Option.defaultValue lines.Length

            paragraphsOf (lines[i + 1 .. next - 1] |> Array.toList)

    let heading = own |> Option.map (fun i -> i + 1, lines[i].TrimEnd())

    { Version = version
      Path = path
      Text = text
      FirstLine = lines |> Array.tryFind (fun l -> l.Trim() <> "") |> Option.defaultValue ""
      Heading = heading
      State =
        match heading with
        | Some(_, h) -> headingState version h
        | None -> Other
      Foreign = versionHeadings text |> List.filter (fun (v, _) -> v <> version)
      Paragraphs = paragraphs
      Run = runOf paragraphs }

/// The slot files under `root`, newest first, and the names of any other file in the directory
/// beside the index.
let read (root: string) : Slot list * string list =
    let dir = Path.Combine(root, Dir)

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
                parse v (Dir + "/" + n) (File.ReadAllText(Path.Combine(dir, n))))
            |> List.sortByDescending (fun s -> versionTriple s.Version)

        slots, names |> List.filter (fun n -> not (isSlot n) && n <> "README.md") |> List.sort

/// The ledger read as one text, newest slot first: the view the README's version stamps are read
/// against (`ReadmeClaimsTests`).
let text (slots: Slot list) : string =
    slots |> List.map _.Text |> String.concat "\n"

/// The first level-2-or-3 header of `text` naming `version`, if any.
let entryHeaderNaming (version: string) (text: string) : string option =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.tryFind (fun l -> Regex.IsMatch(l, @"^#{2,3}\s") && l.Contains(version, StringComparison.Ordinal))

// ---- the index ------------------------------------------------------------------------------

/// The index rendered from `slots` under the hand-written preamble of `current` (everything above
/// its first `## ` line): each slot's own heading, newest first, and a link to its file.
let renderIndex (current: string) (slots: Slot list) : string =
    let lines = current.Replace("\r\n", "\n").Split('\n')

    let head =
        match
            lines
            |> Array.tryFindIndex (fun l -> l.StartsWith("## ", StringComparison.Ordinal))
        with
        | Some i -> String.concat "\n" lines[.. i - 1] + "\n"
        | None -> current.Replace("\r\n", "\n").TrimEnd('\n') + "\n\n"

    let entries =
        slots
        |> List.sortByDescending (fun s -> versionTriple s.Version)
        |> List.map (fun s ->
            let heading =
                match s.Heading with
                | Some(_, h) -> h
                | None -> sprintf "## %s" s.Version

            sprintf "%s\n\n[`%s.md`](%s.md)\n" heading s.Version s.Version)

    head + String.concat "\n" entries

// ---- the clauses ----------------------------------------------------------------------------

/// What is wrong with the ledger's shape: each slot file opens with its own version's level-2
/// heading and carries no other; every release tag and the standing `<Version>` has a file; a
/// `DRAFT` heading names only the standing version; nothing else sits in the directory.
let shapeFaults (tags: Set<string>) (standing: string option) (slots: Slot list) (strays: string list) : string list =
    let perSlot =
        slots
        |> List.collect (fun s ->
            let opens =
                let m = versionHeadingRe.Match s.FirstLine

                if m.Success && m.Groups["v"].Value = s.Version then
                    []
                else
                    [ sprintf
                          "%s does not open with its slot's heading `## %s — …` (it opens '%s')"
                          s.Path
                          s.Version
                          s.FirstLine ]

            let others =
                s.Foreign
                |> List.map (fun (v, h) -> sprintf "%s carries a heading for another slot, %s: '%s'" s.Path v h)

            let draft =
                if s.State = Draft && Some s.Version <> standing then
                    [ sprintf
                          "%s is headed DRAFT, and %s is not the standing <Version>: a draft heading turns when its slot is released or advanced"
                          s.Path
                          s.Version ]
                else
                    []

            opens @ others @ draft)

    let have = slots |> List.map _.Version |> Set.ofList

    let missing =
        let fromTags =
            tags
            |> Set.toList
            |> List.choose (fun t -> ChildProcess.Tags.version t |> Option.map (fun _ -> t.Substring 1))

        (fromTags @ Option.toList standing)
        |> List.distinct
        |> List.filter (have.Contains >> not)
        |> List.sortBy versionTriple
        |> List.map (fun v -> sprintf "%s has no ledger file: %s/%s.md is missing" v Dir v)

    let stray =
        strays
        |> List.map (fun s -> sprintf "%s/%s is neither the index nor a slot file named `<version>.md`" Dir s)

    perSlot @ missing @ stray

let private noTagRe =
    Regex(@"no `v(?<v>\d+\.\d+\.\d+)` tag exists", RegexOptions.Compiled)

/// Draft-slot preambles that deny a tag the repository holds, `(path, line, version)`. The sentence
/// is true when a slot is cut and false the instant the release gesture is made, and nothing but
/// this retires it.
let staleDraftSentences (tags: Set<string>) (slots: Slot list) : (string * int * string) list =
    slots
    |> List.collect (fun s ->
        s.Text.Split('\n')
        |> Array.toList
        |> List.indexed
        |> List.choose (fun (i, l) ->
            let m = noTagRe.Match l

            if m.Success && tags.Contains("v" + m.Groups["v"].Value) then
                Some(s.Path, i + 1, m.Groups["v"].Value)
            else
                None))

let private gateRunRe =
    Regex(
        @"^\*\*Receiving gate run:\*\* `fuaran-dotnet` at `(?<sha>[0-9a-f]{7,40})`, `core-fable\.ps1 -CoreVersion (?<v>\d+\.\d+\.\d+) -CoreFeed <folder>`; compile leg (?<pkgs>\d+) packages green; value leg (?<a>\d+)/(?<b>\d+) vectors byte-identical\.",
        RegexOptions.Compiled
    )

/// Does `text` name `version` as a whole version (not as the tail of a longer one)?
let private namesVersion (version: string) (text: string) =
    Regex.IsMatch(text, sprintf @"(?<![\d.])%s(?![\d.]*\d)" (Regex.Escape version))

/// What is wrong with one released slot's record, given the decisions' titles by id (`D123` ->
/// its heading's title).
let recordFaults (decisions: Map<string, string>) (s: Slot) : string list =
    let line = s.Heading |> Option.map fst |> Option.defaultValue 1
    let at = sprintf "%s:%d (%s)" s.Path line s.Version

    let recordFault =
        if
            s.Paragraphs
            |> List.exists (fun p -> p.StartsWith("**Release record", StringComparison.Ordinal))
        then
            []
        else
            [ sprintf "%s has no '**Release record' paragraph" at ]

    let runFault =
        match s.Run with
        | Absent ->
            [ sprintf
                  "%s has no '**Receiving gate run:**' paragraph naming the `fuaran-dotnet` commit, the `core-fable.ps1 -CoreVersion %s -CoreFeed <folder>` invocation and both leg counts"
                  at
                  s.Version ]
        | Several n ->
            [ sprintf "%s has %d '**Receiving gate run:**' paragraphs; one names the run the release cites" at n ]
        | Cited p ->
            let m = gateRunRe.Match p

            if not m.Success then
                [ sprintf "%s: the receiving-gate paragraph is not in the record grammar: %s" at p ]
            elif m.Groups["v"].Value <> s.Version then
                [ sprintf "%s: the receiving-gate run names -CoreVersion %s" at m.Groups["v"].Value ]
            elif
                int m.Groups["pkgs"].Value = 0
                || int m.Groups["b"].Value = 0
                || m.Groups["a"].Value <> m.Groups["b"].Value
            then
                [ sprintf
                      "%s: the receiving-gate run is not green on both legs (compile %s packages, value %s/%s)"
                      at
                      m.Groups["pkgs"].Value
                      m.Groups["a"].Value
                      m.Groups["b"].Value ]
            else
                []
        | NotCited None ->
            [ sprintf
                  "%s says no run is cited and names no decision: a cut whose run is not cited is not released (STABILITY.md \"Versioning policy\") unless a decision recorded for that slot admits it"
                  at ]
        | NotCited(Some d) ->
            match Map.tryFind d decisions with
            | None -> [ sprintf "%s says no run is cited under %s, which DECISIONS.md does not carry" at d ]
            | Some title when not (namesVersion s.Version title) ->
                [ sprintf
                      "%s says no run is cited under %s, whose heading does not name %s: a release without a cited run is admitted only by a decision recorded for that slot"
                      at
                      d
                      s.Version ]
            | Some _ -> []

    recordFault @ runFault

/// Every fault of the release gesture against the ledger (D123): each tag's slot headed released
/// (DRAFT over a tag red by name) with its record from `recordFloor`; every released heading with its
/// tag; the untagged standing version headed DRAFT.
let releaseFaults
    (recordFloor: int * int * int)
    (decisions: Map<string, string>)
    (tags: Set<string>)
    (standing: string option)
    (slots: Slot list)
    : string list =
    let byVersion = slots |> List.map (fun s -> s.Version, s) |> Map.ofList

    let lineOf (s: Slot) =
        s.Heading |> Option.map fst |> Option.defaultValue 1

    let headingOf (s: Slot) =
        s.Heading |> Option.map snd |> Option.defaultValue s.FirstLine

    let perTag =
        tags
        |> Set.toList
        |> List.choose (fun t -> ChildProcess.Tags.version t |> Option.map (fun v -> v, t))
        |> List.sortBy fst
        |> List.collect (fun (v, t) ->
            let version = t.Substring 1

            match Map.tryFind version byVersion with
            | None ->
                [ sprintf
                      "%s is tagged and no level-2 entry is headed `## %s — released <yyyy-mm-dd> as `%s``"
                      t
                      version
                      t ]
            | Some s ->
                match s.State with
                | Draft ->
                    [ sprintf
                          "%s:%d heads %s DRAFT, and %s is tagged — the tag was placed before the heading turned (flip the heading in the commit the tag names)"
                          s.Path
                          (lineOf s)
                          version
                          t ]
                | Other ->
                    [ sprintf
                          "%s:%d heads tagged %s as '%s', not `## %s — released <yyyy-mm-dd> as `%s``"
                          s.Path
                          (lineOf s)
                          version
                          (headingOf s)
                          version
                          t ]
                | Released _ when v >= recordFloor -> recordFaults decisions s
                | Released _ -> [])

    let untaggedReleased =
        slots
        |> List.choose (fun s ->
            match s.State with
            | Released _ when not (tags.Contains("v" + s.Version)) ->
                Some(
                    sprintf
                        "%s:%d heads %s released, and no `v%s` tag is in this tree's history — the heading turned before the tag (tag the commit that turns it)"
                        s.Path
                        (lineOf s)
                        s.Version
                        s.Version
                )
            | _ -> None)

    let standingDraft =
        match standing with
        | Some v when not (tags.Contains("v" + v)) ->
            match Map.tryFind v byVersion with
            | Some s when s.State <> Draft ->
                [ sprintf
                      "%s:%d heads the untagged standing <Version> %s as '%s', not `## %s — DRAFT`"
                      s.Path
                      (lineOf s)
                      v
                      (headingOf s)
                      v ]
            | _ -> []
        | _ -> []

    perTag @ untaggedReleased @ standingDraft

/// The decisions' titles by id (`D123` -> its heading's title), from `DECISIONS.md`'s text.
let decisionTitles (decisionsText: string) : Result<Map<string, string>, string list> =
    DecisionsTests.parse (decisionsText.Replace("\r\n", "\n"))
    |> Result.map (fun es -> es |> List.map (fun e -> sprintf "D%d" e.Id, e.Title) |> Map.ofList)
