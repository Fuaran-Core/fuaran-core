module Fuaran.Core.Tests.ReleaseRecordTests

// ---------------------------------------------------------------------------
// Phase 392 — "released" is a gate output: a tagged version cannot stand as DRAFT, and a released
// slot names the receiving gate's run. DECISIONS.md D123 is the ruling.
//
// STABILITY.md's versioning policy says every version cut cites a green run of the receiving Fable
// gate against the candidate, and that a cut whose run is red is not released. Until this family
// nothing read either half: the `Package roster` family's per-tag property (Phase 205) asks only
// that SOME `##`/`###` line CONTAINS the version, so `v0.35.1` was tagged and published while its
// entry was headed `## 0.35.1 — DRAFT` and cited no run, and every test was green.
//
// Since Phase 397 the entries live in the release ledger, one `docs/releases/<version>.md` per slot
// (DECISIONS.md D130), and every clause below reads the slot files; a fault names the file and line.
//
// The heading and the record are therefore read here as what they claim, against the tags this
// tree's history carries (`git tag --merged HEAD` — a tag on a commit outside HEAD's history is
// not this tree's release):
//
//   1. Every release tag at or above the entry floor (`PackageRosterTests.entryHeaderFloor`) has a
//      level-2 entry headed `## <v> — released <yyyy-mm-dd> as `v<v>``, optionally with a title
//      between the version and `released` (the 0.23.0 to 0.25.0 spelling). A tagged version headed
//      DRAFT is red BY NAME: it is the shape a tag placed before the heading flip takes, which is
//      what the publish workflow's own `verify.ps1` caught at `c441e76` in the 0.35.2 release.
//   2. Every entry headed `released … as v<v>` has its tag. This is the other half of the same
//      gesture: a heading flipped before the tag exists claims a release nobody made, which is what
//      `main`'s CI caught at `285751b`.
//   3. The standing `<Version>`, while untagged, is headed `## <v> — DRAFT`, so a downstream version
//      check reading the record `Directory.Build.props` declares (`<FuaranStabilityRecord>`, whose
//      draft marker is the word after the version) and this suite read one marker.
//   4. Every released slot from `releaseRecordFloor` (`0.31.0`, the first cut under D55) carries a
//      `**Release record` paragraph and a `**Receiving gate run:**` paragraph naming the run in a
//      fixed grammar: the `fuaran-dotnet` commit, the `core-fable.ps1 -CoreVersion <v>`
//      invocation, the compile leg's package count and the value leg's `<n>/<n>` count. A slot
//      released WITHOUT a cited run says so in the same paragraph (`none cited before the tag`),
//      and is admitted only when `releasedWithoutCitedRun` names it beside the decision that
//      accepted it; that list is closed, and an entry it names that does cite a run is red too.
//
// The grammar is read per PARAGRAPH (consecutive non-blank lines joined), so the document's hard
// wrap at any space cannot hide a record. Each clause has a go-red plant over synthetic input, and
// the live case is planted twice more over the real document — the two 0.35.2 failure shapes — so
// a reader that matched nothing could not pass for one that checks.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Text.RegularExpressions
open Expecto

/// The oldest released slot whose entry must carry the receiving gate's record: `0.31.0`, the first
/// version cut after DECISIONS.md D55 moved the Fable gate to the receiving host. Older slots were
/// gated by this repository's own Fable leg at their tag, so there is no external run to cite and
/// demanding one would demand invention.
let internal releaseRecordFloor = (0, 31, 0)

/// Released slots whose record states that no receiving-gate run against the released candidate is
/// cited, each with the decision that recorded the gap. CLOSED: a slot joins it only by a decision,
/// and a slot it names whose record does cite a run is reported, so the list cannot go stale.
let internal releasedWithoutCitedRun: Map<string, string> =
    Map.ofList [ "0.31.0", "D123"; "0.35.1", "D123" ]

/// What a level-2 entry heading claims about its version.
type internal HeadingState =
    /// `## <v> — [title — ]released <date> as `v<v>``.
    | Released of date: string
    /// `## <v> — DRAFT…`.
    | Draft
    /// Any other spelling (`never released; …`, a lower-case `(draft)`, a malformed release line).
    | Other

/// One level-2 entry whose heading's first token is a version.
type internal Entry =
    {
        Version: string
        Heading: string
        /// The ledger file the entry is in, repo-relative.
        File: string
        /// 1-based line of the heading in that file.
        Line: int
        State: HeadingState
        /// The entry's paragraphs, up to the next level-2 heading, each joined to one line.
        Paragraphs: string list
    }

let private versionHeadingRe =
    Regex(@"^## (?<v>\d+\.\d+\.\d+)(?:\s|$)", RegexOptions.Compiled)

let internal headingState (version: string) (heading: string) : HeadingState =
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

/// Every level-2 entry of one file whose heading starts with a version, in document order.
let private entriesOf (file: string, text: string) : Entry list =
    let lines = text.Replace("\r\n", "\n").Split('\n')

    let starts =
        lines
        |> Array.indexed
        |> Array.filter (fun (_, l) -> l.StartsWith("## ", StringComparison.Ordinal))
        |> Array.map fst

    starts
    |> Array.toList
    |> List.choose (fun i ->
        let m = versionHeadingRe.Match lines[i]

        if not m.Success then
            None
        else
            let next =
                starts |> Array.tryFind (fun j -> j > i) |> Option.defaultValue lines.Length

            let v = m.Groups["v"].Value

            Some
                { Version = v
                  Heading = lines[i].TrimEnd()
                  File = file
                  Line = i + 1
                  State = headingState v lines[i]
                  Paragraphs = paragraphsOf (lines[i + 1 .. next - 1] |> Array.toList) })

/// Every level-2 entry of the ledger's files, `(path, text)`, whose heading starts with a version —
/// the first heading per version, in file then document order.
let internal entries (docs: (string * string) list) : Entry list =
    docs |> List.collect entriesOf |> List.distinctBy _.Version

let private gateRunRe =
    Regex(
        @"^\*\*Receiving gate run:\*\* `fuaran-dotnet` at `(?<sha>[0-9a-f]{7,40})`, `core-fable\.ps1 -CoreVersion (?<v>\d+\.\d+\.\d+) -CoreFeed <folder>`; compile leg (?<pkgs>\d+) packages green; value leg (?<a>\d+)/(?<b>\d+) vectors byte-identical\.",
        RegexOptions.Compiled
    )

let private noRunRe =
    Regex(@"^\*\*Receiving gate run:\*\* none cited before the tag\b", RegexOptions.Compiled)

/// What is wrong with one released entry's record, if anything.
let internal recordFaults (exceptions: Map<string, string>) (e: Entry) : string list =
    let hasRecord =
        e.Paragraphs
        |> List.exists (fun p -> p.StartsWith("**Release record", StringComparison.Ordinal))

    let runs =
        e.Paragraphs
        |> List.filter (fun p -> p.StartsWith("**Receiving gate run:**", StringComparison.Ordinal))

    let at = sprintf "%s:%d (%s)" e.File e.Line e.Version

    let recordFault =
        if hasRecord then
            []
        else
            [ sprintf "%s has no '**Release record' paragraph" at ]

    let runFault =
        match runs, Map.tryFind e.Version exceptions with
        | [], _ ->
            [ sprintf
                  "%s has no '**Receiving gate run:**' paragraph naming the `fuaran-dotnet` commit, the `core-fable.ps1 -CoreVersion %s -CoreFeed <folder>` invocation and both leg counts"
                  at
                  e.Version ]
        | [ p ], None ->
            let m = gateRunRe.Match p

            if noRunRe.IsMatch p then
                [ sprintf
                      "%s says no run is cited, and `releasedWithoutCitedRun` does not name it: a cut whose run is not cited is not released (STABILITY.md \"Versioning policy\")"
                      at ]
            elif not m.Success then
                [ sprintf "%s: the receiving-gate paragraph is not in the record grammar: %s" at p ]
            elif m.Groups["v"].Value <> e.Version then
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
        | [ p ], Some decision ->
            if gateRunRe.IsMatch p then
                [ sprintf "%s cites a run, so `releasedWithoutCitedRun` must not name it — drop it from the list" at ]
            elif not (noRunRe.IsMatch p) then
                [ sprintf "%s: the receiving-gate paragraph is not in the record grammar: %s" at p ]
            elif not (p.Contains(decision, StringComparison.Ordinal)) then
                [ sprintf "%s: the no-run paragraph does not name %s, the decision that recorded the gap" at decision ]
            else
                []
        | many, _ ->
            [ sprintf
                  "%s has %d '**Receiving gate run:**' paragraphs; one names the run the release cites"
                  at
                  many.Length ]

    recordFault @ runFault

/// Every fault of the release gesture against the ledger: the per-tag heading and record (1, 4), the
/// released heading without its tag (2), and the untagged standing version's DRAFT heading (3).
let internal releaseFaults
    (headerFloor: int * int * int)
    (recordFloor: int * int * int)
    (exceptions: Map<string, string>)
    (tags: Set<string>)
    (standing: string option)
    (docs: (string * string) list)
    : string list =
    let es = entries docs
    let byVersion = es |> List.map (fun e -> e.Version, e) |> Map.ofList

    let tagged =
        tags
        |> Set.toList
        |> List.choose (fun t -> PackageRosterTests.releaseTagVersion t |> Option.map (fun v -> v, t))
        |> List.sortBy fst

    let perTag =
        tagged
        |> List.filter (fun (v, _) -> v >= headerFloor)
        |> List.collect (fun (v, t) ->
            let version = t.Substring 1

            match Map.tryFind version byVersion with
            | None ->
                [ sprintf
                      "%s is tagged and no level-2 entry is headed `## %s — released <yyyy-mm-dd> as `%s``"
                      t
                      version
                      t ]
            | Some e ->
                match e.State with
                | Draft ->
                    [ sprintf
                          "%s:%d heads %s DRAFT, and %s is tagged — the tag was placed before the heading turned (flip the heading in the commit the tag names)"
                          e.File
                          e.Line
                          version
                          t ]
                | Other ->
                    [ sprintf
                          "%s:%d heads tagged %s as '%s', not `## %s — released <yyyy-mm-dd> as `%s``"
                          e.File
                          e.Line
                          version
                          e.Heading
                          version
                          t ]
                | Released _ when v >= recordFloor -> recordFaults exceptions e
                | Released _ -> [])

    let untaggedReleased =
        es
        |> List.choose (fun e ->
            match e.State with
            | Released _ when not (tags.Contains("v" + e.Version)) ->
                Some(
                    sprintf
                        "%s:%d heads %s released, and no `v%s` tag is in this tree's history — the heading turned before the tag (tag the commit that turns it)"
                        e.File
                        e.Line
                        e.Version
                        e.Version
                )
            | _ -> None)

    let standingDraft =
        match standing with
        | Some v when not (tags.Contains("v" + v)) ->
            match Map.tryFind v byVersion with
            | Some e when e.State <> Draft ->
                [ sprintf
                      "%s:%d heads the untagged standing <Version> %s as '%s', not `## %s — DRAFT`"
                      e.File
                      e.Line
                      v
                      e.Heading
                      v ]
            | _ -> []
        | _ -> []

    let staleExceptions =
        exceptions
        |> Map.toList
        |> List.choose (fun (v, _) ->
            if tags.Contains("v" + v) then
                None
            else
                Some(sprintf "`releasedWithoutCitedRun` names %s, which no tag in this tree's history releases" v))

    perTag @ untaggedReleased @ standingDraft @ staleExceptions

// ---- the instrument ---------------------------------------------------------

let private repoRoot () : string =
    Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")

/// The release tags in HEAD's history. `--merged HEAD` includes a tag on HEAD itself, which is the
/// release commit's own case in the publish workflow.
let private mergedTags (root: string) : Result<Set<string>, string> =
    ChildProcess.git root "tag --list --merged HEAD"
    |> Result.map (fun out ->
        out.Split('\n')
        |> Array.map (fun l -> l.Trim())
        |> Array.filter (fun l -> l <> "")
        |> Set.ofArray)

let private remedy =
    "Remedy: follow the release sequence in STABILITY.md \"Versioning policy\" — corpus re-stamp, the receiving gate green against the packed candidate, a clean pack, then the record, the heading flip (the slot's ledger file and its index entry) and the README stamps in ONE commit, which is the commit the tag names."

let private failOn (faults: string list) =
    match faults with
    | [] -> ()
    | fs ->
        failtestf "%d release-record fault(s):\n       %s\n       %s" fs.Length (String.concat "\n       " fs) remedy

// ---- the suite --------------------------------------------------------------

/// A green synthetic document: two tagged slots (one before the record floor), the untagged draft.
let private sample =
    String.concat
        "\n"
        [ "# Stability"
          "## 0.36.0 — DRAFT"
          "Draft entries."
          ""
          "## 0.35.2 — released 2026-10-07 as `v0.35.2`"
          ""
          "**Release record — the receiving gate: GREEN.** Prose about the run."
          ""
          "**Receiving gate run:** `fuaran-dotnet` at `606f76e`, `core-fable.ps1"
          "-CoreVersion 0.35.2 -CoreFeed <folder>`; compile leg 16 packages green; value leg 419/419 vectors"
          "byte-identical."
          ""
          "## 0.35.1 — released 2026-10-06 as `v0.35.1`"
          ""
          "**Release record — no run.** Prose."
          ""
          "**Receiving gate run:** none cited before the tag; DECISIONS.md D123."
          ""
          "## 0.30.1 — never released; its entries ship in `0.31.0`"
          "## 0.30.0 — a title — released 2026-09-23 as `v0.30.0`"
          "No record: below the record floor." ]

let private sampleTags = Set.ofList [ "v0.30.0"; "v0.35.1"; "v0.35.2" ]

let private faultsOf tags standing text =
    releaseFaults (0, 25, 0) (0, 31, 0) (Map.ofList [ "0.35.1", "D123" ]) tags standing [ "sample.md", text ]

/// The ledger's slot files, `(path, text)`, newest first.
let private ledgerDocs (root: string) =
    PackageRosterTests.ledgerFiles root |> List.map (fun f -> f.Path, f.Text)

[<Tests>]
let tests =
    testList
        "Release record"
        [ test "every tag is headed released with its receiving-gate record, and every released heading has its tag" {
              let root = repoRoot ()

              match mergedTags root with
              | Error why ->
                  printfn "Release-record check SKIPPED: %s" why
                  skiptestf "release-record check skipped — %s" why
              | Ok tags ->
                  let covered =
                      tags
                      |> Set.toList
                      |> List.choose PackageRosterTests.releaseTagVersion
                      |> List.filter (fun v -> v >= releaseRecordFloor)

                  Expect.isNonEmpty
                      covered
                      "at least one tag in HEAD's history sits at or above the record floor — with none every clause below passes vacuously"

                  let ledger = ledgerDocs root
                  let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

                  Expect.isNonEmpty ledger "the release ledger's slot files were read"

                  releaseFaults
                      PackageRosterTests.entryHeaderFloor
                      releaseRecordFloor
                      releasedWithoutCitedRun
                      tags
                      (PackageRosterTests.standingVersion props)
                      ledger
                  |> failOn
          }

          test "the live document reds on both 0.35.2 failure shapes" {
              // Planted over the REAL document and tags, so the live clause above is shown to read
              // what it claims to: the newest tagged slot's heading put back to DRAFT (a tag placed
              // before the flip — `c441e76`), and the same slot's tag withheld (a flip before the tag
              // — `285751b`).
              let root = repoRoot ()

              match mergedTags root with
              | Error why -> skiptestf "release-record plants skipped — %s" why
              | Ok tags ->
                  let ledger = ledgerDocs root
                  let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))
                  let standing = PackageRosterTests.standingVersion props

                  let run tags text =
                      releaseFaults
                          PackageRosterTests.entryHeaderFloor
                          releaseRecordFloor
                          releasedWithoutCitedRun
                          tags
                          standing
                          text

                  let newest =
                      tags
                      |> Set.toList
                      |> List.choose (fun t -> PackageRosterTests.releaseTagVersion t |> Option.map (fun v -> v, t))
                      |> List.maxBy fst
                      |> snd

                  let e = entries ledger |> List.find (fun e -> e.Version = newest.Substring 1)

                  Expect.isEmpty (run tags ledger) "the live ledger is green before the plants"

                  let drafted =
                      ledger
                      |> List.map (fun (path, text) ->
                          path,
                          (if path = e.File then
                               text.Replace(e.Heading, sprintf "## %s — DRAFT" e.Version)
                           else
                               text))

                  Expect.exists
                      (run tags drafted)
                      (fun f -> f.Contains("DRAFT") && f.Contains newest)
                      "a tagged slot headed DRAFT is red by name"

                  Expect.exists
                      (run (Set.remove newest tags) ledger)
                      (fun f -> f.Contains "heads" && f.Contains "released, and no")
                      "a slot headed released with its tag withheld is red"
          }

          test "the synthetic record grammar is green as written" {
              Expect.isEmpty (faultsOf sampleTags (Some "0.36.0") sample) "the sample is the green shape"

              Expect.equal
                  (entries [ "sample.md", sample ] |> List.map (fun e -> e.Version, e.State))
                  [ "0.36.0", Draft
                    "0.35.2", Released "2026-10-07"
                    "0.35.1", Released "2026-10-06"
                    "0.30.1", Other
                    "0.30.0", Released "2026-09-23" ]
                  "each level-2 version heading is classified, the titled spelling included"
          }

          test "a DRAFT, a malformed or a missing heading for a tag is red" {
              let drafted =
                  sample.Replace("## 0.35.2 — released 2026-10-07 as `v0.35.2`", "## 0.35.2 — DRAFT")

              Expect.exists
                  (faultsOf sampleTags (Some "0.36.0") drafted)
                  (fun f -> f.Contains "heads 0.35.2 DRAFT, and v0.35.2 is tagged")
                  "DRAFT over a tag is red by name"

              let malformed = sample.Replace("as `v0.35.2`", "as v0.35.2")

              Expect.exists
                  (faultsOf sampleTags (Some "0.36.0") malformed)
                  (fun f -> f.Contains "not `## 0.35.2 — released")
                  "a released line outside the grammar is red"

              Expect.exists
                  (faultsOf (Set.add "v0.33.0" sampleTags) (Some "0.36.0") sample)
                  (fun f -> f.StartsWith "v0.33.0 is tagged and no level-2 entry")
                  "a tag with no level-2 entry is red"

              Expect.isEmpty
                  (faultsOf (Set.add "v0.24.0" sampleTags) (Some "0.36.0") sample)
                  "a tag below the entry floor is out of frame"
          }

          test "a released heading without its tag, and an untagged standing version not headed DRAFT, are red" {
              Expect.exists
                  (faultsOf (Set.remove "v0.35.2" sampleTags) (Some "0.36.0") sample)
                  (fun f -> f.Contains "heads 0.35.2 released, and no `v0.35.2` tag")
                  "the flip before the tag is red"

              let undrafted = sample.Replace("## 0.36.0 — DRAFT", "## 0.36.0 (draft)")

              Expect.exists
                  (faultsOf sampleTags (Some "0.36.0") undrafted)
                  (fun f -> f.Contains "untagged standing <Version> 0.36.0")
                  "the untagged standing version must carry the DRAFT marker"

              Expect.isEmpty
                  (faultsOf sampleTags (Some "0.35.2") sample)
                  "a TAGGED standing version is held by the per-tag clause, not the draft one"
          }

          test "a released slot from the record floor up names its run, in the grammar, green on both legs" {
              let reds (text: string) =
                  faultsOf sampleTags (Some "0.36.0") text

              Expect.exists
                  (reds (sample.Replace("**Release record — the receiving gate: GREEN.**", "**Record.**")))
                  (fun f -> f.Contains "no '**Release record' paragraph")
                  "a slot without a release record is red"

              Expect.exists
                  (reds (sample.Replace("**Receiving gate run:** `fuaran-dotnet`", "Run: `fuaran-dotnet`")))
                  (fun f -> f.Contains "(0.35.2) has no '**Receiving gate run:**' paragraph")
                  "a record that names no run is red"

              Expect.exists
                  (reds (sample.Replace("-CoreVersion 0.35.2", "-CoreVersion 0.35.1")))
                  (fun f -> f.Contains "names -CoreVersion 0.35.1")
                  "a run against another version is red"

              Expect.exists
                  (reds (sample.Replace("value leg 419/419", "value leg 418/419")))
                  (fun f -> f.Contains "not green on both legs")
                  "a value leg short of its total is red"

              Expect.exists
                  (reds (sample.Replace("at `606f76e`", "at `HEAD`")))
                  (fun f -> f.Contains "not in the record grammar")
                  "a run that names no host commit is red"

              let unlisted =
                  sample
                  + "\n## 0.31.0 — released 2026-09-26 as `v0.31.0`\n**Release record.** x\n\n**Receiving gate run:** none cited before the tag; D123."

              Expect.exists
                  (faultsOf (Set.add "v0.31.0" sampleTags) (Some "0.36.0") unlisted)
                  (fun f -> f.Contains "(0.31.0) says no run is cited")
                  "a no-run record the closed list does not name is red"

              Expect.isEmpty
                  (faultsOf (Set.add "v0.29.9" sampleTags) (Some "0.36.0") (unlisted.Replace("0.31.0", "0.29.9")))
                  "below the record floor a tagged, released slot needs no receiving-gate record"
          }

          test "the closed no-run list admits only what it names, and only while the record says so" {
              let withList list tags text =
                  releaseFaults (0, 25, 0) (0, 31, 0) (Map.ofList list) tags (Some "0.36.0") [ "sample.md", text ]

              Expect.exists
                  (withList [ "0.35.1", "D123"; "0.35.2", "D123" ] sampleTags sample)
                  (fun f -> f.Contains "(0.35.2) cites a run, so `releasedWithoutCitedRun` must not name it")
                  "a listed slot whose record cites a run is red — the list cannot go stale"

              Expect.exists
                  (withList [ "0.35.1", "D999" ] sampleTags sample)
                  (fun f -> f.Contains "does not name D999")
                  "the no-run paragraph names the decision that recorded the gap"

              Expect.exists
                  (withList [ "0.35.1", "D123"; "0.34.0", "D123" ] sampleTags sample)
                  (fun f -> f.Contains "names 0.34.0, which no tag")
                  "a listed version nothing released is red"
          } ]
