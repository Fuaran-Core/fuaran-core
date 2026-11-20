/// Phase 503 - the release ledger as ONE family over `Ledger.Slot list`.
///
/// Until this phase the ledger's clauses lived in two families that each parsed the slot files:
/// `Package roster` (Phases 199, 205, 397: a file per slot, its own heading, the index word for word,
/// DRAFT only on the standing version, the contract's ceiling, the props file pointing at the index,
/// no stale "no tag exists" sentence) and `Release record` (Phase 392, D123: every tag headed released
/// with its receiving-gate record, every released heading with its tag, the untagged standing version
/// headed DRAFT). They read one record, so they are one family, over one parse (`Ledger.read`).
///
/// Two clauses changed shape and none changed meaning. The index is a RENDER of the slots now
/// (`Ledger.renderIndex`), held byte for byte, where it was a hand copy whose headings were compared
/// with the files'; `CORE_APPROVE_DOCS=ledger-index` rewrites it. And the no-run map D123 kept in
/// source is the slots' own no-run paragraphs, admitted only by a decision whose heading names the
/// slot (Ledger.fs has the rule). The heading grammars are exactly D123's and D130's.
///
/// Tags are read through `ChildProcess.Tags.released` (`--merged HEAD`). A clone that cannot answer
/// (no git, or no release tag at all, D168) skips the tag clauses by name; the shape clauses still run.
/// Every clause has a go-red over synthetic slots beside the live case, and the live ledger is planted
/// with the two `0.35.2` failure shapes, so a reader that matched nothing could not pass for one that
/// checks.
module Fuaran.Core.Tests.LedgerTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core.Tests.Ledger

/// The contract's line ceiling: what an adopter reads in one sitting, and what keeps a release's
/// entry from drifting back into it (D130).
let internal contractLineCeiling = 1500

let private props (root: string) =
    File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

let private standing root =
    PackageRosterTests.standingVersion (props root)

let private decisions (root: string) : Map<string, string> =
    match decisionTitles (File.ReadAllText(Path.Combine(root, "DECISIONS.md"))) with
    | Ok m -> m
    | Error es -> failtestf "DECISIONS.md does not parse: %s" (String.concat "; " es)

/// DECISIONS.md D275 (a): while the standing `<Version>`'s slot is not headed DRAFT (the release commit turns it
/// `released`), the decision record carries no missing number. `missing` is `DecisionsTests.missing` of the record.
let internal unrecordedAtRelease (standing: string option) (slots: Slot list) (missing: int list) : string list =
    if slots |> List.exists (fun s -> Some s.Version = standing && s.State = Draft) then
        []
    else
        [ for n in missing ->
              sprintf
                  "DECISIONS.md: D%d is missing and the standing slot %s is not the draft: record D%d `Never-minted`, or land the change that holds it, before the commit that closes the slot (DECISIONS.md D275)"
                  n
                  (defaultArg standing "(no <Version>)")
                  n ]

let private remedy =
    "Remedy: follow the release sequence in STABILITY.md \"Versioning policy\" — corpus re-stamp, the receiving gate green against the packed candidate, a clean pack, then the record, the heading flip (the slot's ledger file, and the index re-rendered with CORE_APPROVE_DOCS=ledger-index) and the README stamps in ONE commit, which is the commit the tag names. Tag it locally and run verify.ps1 on it, and push the commit and the tag together only when that run is green."

let private failOn (what: string) (faults: string list) =
    match faults with
    | [] -> ()
    | fs -> failtestf "%d %s fault(s):\n       %s\n       %s" fs.Length what (String.concat "\n       " fs) remedy

/// The live ledger and the tags this tree's history carries, or the reason the tags cannot be read.
let private live () =
    let root = Repo.root ()
    let slots, strays = read root
    root, slots, strays, ChildProcess.Tags.released root

// ---- synthetic slots ------------------------------------------------------------------------

let private slot (v: string) (text: string) = parse v (sprintf "%s/%s.md" Dir v) text

/// A green synthetic ledger: the untagged draft, a slot with a cited run, one released under a
/// decision that names it, one never released, one below the record floor.
let private sample =
    [ slot "0.36.0" "## 0.36.0 — DRAFT\nDraft entries."
      slot
          "0.35.2"
          (String.concat
              "\n"
              [ "## 0.35.2 — released 2026-10-07 as `v0.35.2`"
                ""
                "**Release record — the receiving gate: GREEN.** Prose about the run."
                ""
                "**Receiving gate run:** `fuaran-dotnet` at `606f76e`, `core-fable.ps1"
                "-CoreVersion 0.35.2 -CoreFeed <folder>`; compile leg 16 packages green; value leg 419/419 vectors"
                "byte-identical." ])
      slot
          "0.35.1"
          (String.concat
              "\n"
              [ "## 0.35.1 — released 2026-10-06 as `v0.35.1`"
                ""
                "**Release record — no run.** Prose."
                ""
                "**Receiving gate run:** none cited before the tag; DECISIONS.md D123." ])
      slot "0.30.1" "## 0.30.1 — never released; its entries ship in `0.31.0`"
      slot "0.30.0" "## 0.30.0 — a title — released 2026-09-23 as `v0.30.0`\nNo record: below the record floor." ]

let private sampleTags = Set.ofList [ "v0.30.0"; "v0.35.1"; "v0.35.2" ]

let private sampleDecisions =
    Map.ofList
        [ "D123", "`0.31.0` and `0.35.1` were released without a cited run"
          "D9", "something else" ]

let private faultsOf tags standing slots =
    releaseFaults (0, 31, 0) sampleDecisions tags standing slots

/// `sample` with one slot's text edited.
let private editing (v: string) (f: string -> string) =
    sample |> List.map (fun s -> if s.Version = v then slot v (f s.Text) else s)

// ---- scratch ledgers (Phase 702, D274) ----------------------------------------------------------

/// `git <arguments>` in `dir`: its exit code and what it wrote. The merge clause reads the code itself.
let private gitIn (dir: string) (arguments: string list) : int * string =
    match ChildProcess.argv "git" arguments |> ChildProcess.inDir dir |> ChildProcess.run with
    | Ok o -> o.Exit, (o.Stdout + o.Stderr).Trim()
    | Error why -> failtestf "git could not run: %s" why

let private gitOk (dir: string) (arguments: string list) =
    match gitIn dir arguments with
    | 0, _ -> ()
    | code, out -> failtestf "git %s exited %d: %s" (String.concat " " arguments) code out

let private writeFile (path: string) (text: string) =
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, text)

/// `body` over a fresh scratch directory, removed after (git's read-only object files cleared first).
let private scratch (body: string -> unit) =
    let root =
        Path.Combine(Path.GetTempPath(), "fuaran-core-702-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory root |> ignore

    try
        body root
    finally
        try
            for p in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) do
                File.SetAttributes(p, FileAttributes.Normal)

            Directory.Delete(root, true)
        with _ ->
            ()

/// The standing draft every scratch ledger opens with.
let private scratchDraft =
    "## 0.9.1 — DRAFT\n\n**Slot class: additive.** A scratch slot over the released `0.9.0`.\n"

let private entryA =
    "- **A first sample entry** (Phase 901, `A.fs`) — class **internal**: no `api/` row, wire byte or\n  refusal moves.\n"

let private entryB =
    "- **A second sample entry** (Phase 902, `B.fs`) — class **internal**: no `api/` row, wire byte or\n  refusal moves.\n"

/// The entries a slot file's own text holds, each as an entry file holds it: a `- ` line and the wrapped lines
/// after it (two spaces in), ending in one line break.
let private bulletsOf (text: string) : string list =
    let rec go (acc: string list) (lines: string list) =
        match lines with
        | [] -> List.rev acc
        | l :: rest when l.StartsWith("- ", StringComparison.Ordinal) ->
            let wrapped =
                rest |> List.takeWhile (fun x -> x.StartsWith("  ", StringComparison.Ordinal))

            go ((String.concat "\n" (l :: wrapped) + "\n") :: acc) (List.skip wrapped.Length rest)
        | _ :: rest -> go acc rest

    go [] (text.Replace("\r\n", "\n").Split('\n') |> Array.toList)

[<Tests>]
let tests =
    testList
        "Release ledger"
        [ test
              "the release ledger: every slot opens with its own heading, every tag and the standing version has a file, only the standing version is DRAFT" {
              let root, slots, strays, tags = live ()

              Expect.isTrue
                  (File.Exists(Path.Combine(root, IndexPath)))
                  "docs/releases/README.md, the ledger's index, exists"

              Expect.isNonEmpty
                  slots
                  "the ledger holds at least one slot file — an empty read would make every clause vacuous"

              let tags =
                  match tags with
                  | Ok tags -> tags
                  | Error why ->
                      // Without the tags the per-tag half has nothing to read; the shape still runs.
                      printfn "release-ledger tag clause SKIPPED: %s" why
                      Set.empty

              shapeFaults tags (standing root) slots strays |> failOn "release-ledger"
          }

          test "the ledger index is the render of the slots' headings, newest first" {
              let root, slots, _, _ = live ()
              let path = Path.Combine(root, IndexPath)
              Approval.validate Approval.Docs

              // D274.3: the release gesture's switch also closes each slot the gesture closed, rendering its
              // entries into its file and removing their files; the shape clause reds one it has not.
              let slots =
                  match slots |> List.filter (fun s -> s.State <> Draft && s.Text <> s.Own) with
                  | closing when not closing.IsEmpty && Approval.admits Approval.Docs Approval.Files.LedgerIndex ->
                      for s in closing do
                          for p in closeSlot root s do
                              printfn "%s: closed %s (%s)" Approval.Docs s.Version p

                      fst (read root)
                  | _ -> slots

              let current = File.ReadAllText(path).Replace("\r\n", "\n")
              let rendered = renderIndex current slots

              if current <> rendered then
                  if Approval.admits Approval.Docs Approval.Files.LedgerIndex then
                      Approval.write Approval.Docs Approval.Files.LedgerIndex path rendered |> ignore
                  else
                      failtestf
                          "%s is not the render of the slot files' headings (each slot's own heading, newest first, with a link to its file). Re-render it: CORE_APPROVE_DOCS=%s, then stage it by name. A heading is changed in its slot file, never here."
                          IndexPath
                          Approval.Files.LedgerIndex
          }

          test "every tag is headed released with its receiving-gate record, and every released heading has its tag" {
              let root, slots, _, tags = live ()

              match tags with
              | Error why ->
                  printfn "Release-record check SKIPPED: %s" why
                  skiptestf "release-record check skipped — %s" why
              | Ok tags ->
                  Expect.isNonEmpty
                      (tags
                       |> Set.toList
                       |> List.choose ChildProcess.Tags.version
                       |> List.filter (fun v -> v >= recordFloor))
                      "at least one tag in HEAD's history sits at or above the record floor — with none every clause below passes vacuously"

                  releaseFaults recordFloor (decisions root) tags (standing root) slots
                  |> failOn "release-record"
          }

          test "no 'no tag exists' sentence survives the tag it denies" {
              let _, slots, _, tags = live ()

              match tags with
              | Error why ->
                  // Skipped by NAME with the reason printed, never passed silently: the source-archive
                  // / no-git case, or a clone holding no release tag at all (D168).
                  printfn "draft-slot tag check SKIPPED: %s" why
                  skiptestf "draft-slot tag check skipped — %s" why
              | Ok tags ->
                  // A clone holding release tags of which HEAD descends from none FAILS here rather
                  // than skipping: with no tag the comparison below passes vacuously.
                  Expect.isNonEmpty (Set.toList tags) "HEAD's history holds at least one `vX.Y.Z` release tag"

                  match staleDraftSentences tags slots with
                  | [] -> ()
                  | stale ->
                      failtestf
                          "%d draft-slot preamble(s) deny a tag this repository holds:\n       %s\n       Remedy: retire the sentence — the slot is released, so say so and say when. Do not delete the entries under it."
                          stale.Length
                          (stale
                           |> List.map (fun (path, n, v) -> sprintf "%s:%d says no `v%s` tag exists — it does" path n v)
                           |> String.concat "\n       ")
          }

          test "the two releases without a cited run are slot data: each names D123, and D123 names both" {
              // The operator's ruling of 2026-10-17: D123's closed no-run map moved from source onto
              // the two slot records. This pins the move, so the data cannot quietly leave them.
              let root, slots, _, _ = live ()
              let ds = decisions root

              let notCited =
                  slots
                  |> List.choose (fun s ->
                      match s.Run with
                      | NotCited d -> Some(s.Version, d)
                      | _ -> None)

              Expect.equal
                  notCited
                  [ "0.35.1", Some "D123"; "0.31.0", Some "D123" ]
                  "exactly 0.35.1 and 0.31.0 say no run is cited, each under D123"

              for v, _ in notCited do
                  Expect.isEmpty
                      (recordFaults ds (slots |> List.find (fun s -> s.Version = v)))
                      (sprintf "%s's no-run record is admitted by D123's heading" v)
          }

          test "STABILITY.md is the contract, held to the line ceiling, with every slot's entry in the ledger" {
              let root = Repo.root ()

              let lines =
                  File.ReadAllText(Path.Combine(root, "STABILITY.md")).Replace("\r\n", "\n").Split('\n')

              Expect.isTrue
                  (lines |> Array.exists (fun l -> l = "## Versioning policy"))
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
                  "STABILITY.md carries no `## <version>` entry: every slot's entry is in the ledger"
          }

          test
              "Directory.Build.props keeps <Version> and points the stability record at the ledger index, with no version history" {
              let root = Repo.root ()
              let props = props root

              Expect.isSome (PackageRosterTests.standingVersion props) "the props file declares <Version>"

              Expect.isEmpty
                  (PackageRosterTests.propsHistoryLines props)
                  "Directory.Build.props carries a version-history note — the history is the ledger's, docs/releases/<version>.md"

              let record =
                  Regex.Match(props, @"<FuaranStabilityRecord>([^<]*)</FuaranStabilityRecord>")

              Expect.isTrue record.Success "the props file declares <FuaranStabilityRecord>"

              Expect.equal
                  (record.Groups[1].Value.Trim())
                  IndexPath
                  "the declared stability record is the ledger's index, which carries every slot's heading"

              Expect.isTrue
                  (File.Exists(Path.Combine(root, record.Groups[1].Value.Trim())))
                  "the declared stability record exists"
          }

          test "the live ledger reds on both 0.35.2 failure shapes" {
              // Planted over the REAL slots and tags, so the live clause is shown to read what it
              // claims to: the newest tagged slot's heading put back to DRAFT (a tag placed before the
              // flip, `c441e76`), and the same slot's tag withheld (a flip before the tag, `285751b`).
              let root, slots, _, tags = live ()

              match tags with
              | Error why -> skiptestf "release-record plants skipped — %s" why
              | Ok tags ->
                  let ds = decisions root
                  let standing = standing root

                  let run tags slots =
                      releaseFaults recordFloor ds tags standing slots

                  let newest = ChildProcess.Tags.newest tags |> Option.get
                  let s = slots |> List.find (fun s -> s.Version = newest.Substring 1)
                  let heading = s.Heading |> Option.get |> snd

                  Expect.isEmpty (run tags slots) "the live ledger is green before the plants"

                  let drafted =
                      slots
                      |> List.map (fun x ->
                          if x.Version = s.Version then
                              parse x.Version x.Path (x.Text.Replace(heading, sprintf "## %s — DRAFT" x.Version))
                          else
                              x)

                  Expect.exists
                      (run tags drafted)
                      (fun f -> f.Contains("DRAFT") && f.Contains newest)
                      "a tagged slot headed DRAFT is red by name"

                  Expect.exists
                      (run (Set.remove newest tags) slots)
                      (fun f -> f.Contains "heads" && f.Contains "released, and no")
                      "a slot headed released with its tag withheld is red"
          }

          // ---- go-red over synthetic slots -------------------------------------------------

          test "the synthetic ledger is green as written, and each heading is classified" {
              Expect.isEmpty (faultsOf sampleTags (Some "0.36.0") sample) "the sample is the green shape"
              Expect.isEmpty (shapeFaults sampleTags (Some "0.36.0") sample []) "and the green shape"

              Expect.equal
                  (sample |> List.map (fun s -> s.Version, s.State))
                  [ "0.36.0", Draft
                    "0.35.2", Released "2026-10-07"
                    "0.35.1", Released "2026-10-06"
                    "0.30.1", Other
                    "0.30.0", Released "2026-09-23" ]
                  "each slot's heading is classified, the titled spelling included"

              Expect.equal
                  (sample |> List.map _.Run)
                  [ Absent
                    Cited
                        "**Receiving gate run:** `fuaran-dotnet` at `606f76e`, `core-fable.ps1 -CoreVersion 0.35.2 -CoreFeed <folder>`; compile leg 16 packages green; value leg 419/419 vectors byte-identical."
                    NotCited(Some "D123")
                    Absent
                    Absent ]
                  "the run record is read per paragraph, so the hard wrap cannot hide it"
          }

          test "a DRAFT, a malformed or a missing heading for a tag is red" {
              let drafted =
                  editing "0.35.2" (fun t ->
                      t.Replace("## 0.35.2 — released 2026-10-07 as `v0.35.2`", "## 0.35.2 — DRAFT"))

              Expect.exists
                  (faultsOf sampleTags (Some "0.36.0") drafted)
                  (fun f -> f.Contains "heads 0.35.2 DRAFT, and v0.35.2 is tagged")
                  "DRAFT over a tag is red by name"

              Expect.exists
                  (faultsOf
                      sampleTags
                      (Some "0.36.0")
                      (editing "0.35.2" (fun t -> t.Replace("as `v0.35.2`", "as v0.35.2"))))
                  (fun f -> f.Contains "not `## 0.35.2 — released")
                  "a released line outside the grammar is red"

              Expect.exists
                  (faultsOf (Set.add "v0.33.0" sampleTags) (Some "0.36.0") sample)
                  (fun f -> f.StartsWith "v0.33.0 is tagged and no level-2 entry")
                  "a tag with no slot is red"
          }

          test "a released heading without its tag, and an untagged standing version not headed DRAFT, are red" {
              Expect.exists
                  (faultsOf (Set.remove "v0.35.2" sampleTags) (Some "0.36.0") sample)
                  (fun f -> f.Contains "heads 0.35.2 released, and no `v0.35.2` tag")
                  "the flip before the tag is red"

              Expect.exists
                  (faultsOf
                      sampleTags
                      (Some "0.36.0")
                      (editing "0.36.0" (fun t -> t.Replace("## 0.36.0 — DRAFT", "## 0.36.0 (draft)"))))
                  (fun f -> f.Contains "untagged standing <Version> 0.36.0")
                  "the untagged standing version must carry the DRAFT marker"

              Expect.isEmpty
                  (faultsOf sampleTags (Some "0.35.2") sample)
                  "a TAGGED standing version is held by the per-tag clause, not the draft one"
          }

          test "a released slot from the record floor up names its run, in the grammar, green on both legs" {
              let reds f =
                  faultsOf sampleTags (Some "0.36.0") (editing "0.35.2" f)

              Expect.exists
                  (reds (fun t -> t.Replace("**Release record — the receiving gate: GREEN.**", "**Record.**")))
                  (fun f -> f.Contains "no '**Release record' paragraph")
                  "a slot without a release record is red"

              Expect.exists
                  (reds (fun t -> t.Replace("**Receiving gate run:** `fuaran-dotnet`", "Run: `fuaran-dotnet`")))
                  (fun f -> f.Contains "(0.35.2) has no '**Receiving gate run:**' paragraph")
                  "a record that names no run is red"

              Expect.exists
                  (reds (fun t -> t.Replace("-CoreVersion 0.35.2", "-CoreVersion 0.35.1")))
                  (fun f -> f.Contains "names -CoreVersion 0.35.1")
                  "a run against another version is red"

              Expect.exists
                  (reds (fun t -> t.Replace("value leg 419/419", "value leg 418/419")))
                  (fun f -> f.Contains "not green on both legs")
                  "a value leg short of its total is red"

              Expect.exists
                  (reds (fun t -> t.Replace("at `606f76e`", "at `HEAD`")))
                  (fun f -> f.Contains "not in the record grammar")
                  "a run that names no host commit is red"

              Expect.exists
                  (reds (fun t -> t + "\n\n**Receiving gate run:** none cited before the tag; D123."))
                  (fun f -> f.Contains "has 2 '**Receiving gate run:**' paragraphs")
                  "two run paragraphs are red"

              Expect.isEmpty
                  (faultsOf
                      (Set.add "v0.29.9" sampleTags)
                      (Some "0.36.0")
                      (sample
                       @ [ slot
                               "0.29.9"
                               "## 0.29.9 — released 2026-09-20 as `v0.29.9`\n\n**Receiving gate run:** none cited before the tag." ]))
                  "below the record floor a tagged, released slot needs no receiving-gate record"
          }

          test "a release without a cited run is admitted only by a decision whose heading names the slot" {
              let reds f =
                  faultsOf sampleTags (Some "0.36.0") (editing "0.35.1" f)

              Expect.exists
                  (reds (fun t -> t.Replace("; DECISIONS.md D123.", ".")))
                  (fun f -> f.Contains "(0.35.1) says no run is cited and names no decision")
                  "a no-run record that names no decision is red"

              Expect.exists
                  (reds (fun t -> t.Replace("D123", "D999")))
                  (fun f -> f.Contains "under D999, which DECISIONS.md does not carry")
                  "a no-run record naming a decision that does not exist is red"

              Expect.exists
                  (reds (fun t -> t.Replace("D123", "D9")))
                  (fun f -> f.Contains "under D9, whose heading does not name 0.35.1")
                  "a decision that does not name the slot does not admit it — the set is still closed by decisions"

              let unlisted =
                  slot
                      "0.34.0"
                      "## 0.34.0 — released 2026-10-02 as `v0.34.0`\n\n**Release record.** x\n\n**Receiving gate run:** none cited before the tag; DECISIONS.md D123."

              Expect.exists
                  (faultsOf (Set.add "v0.34.0" sampleTags) (Some "0.36.0") (sample @ [ unlisted ]))
                  (fun f -> f.Contains "(0.34.0) says no run is cited under D123, whose heading does not name 0.34.0")
                  "a slot the decision's heading does not name cannot borrow it"

              Expect.isFalse
                  (recordFaults (Map.ofList [ "D1", "released 10.35.1 without a run" ]) sample[2]
                   |> List.isEmpty)
                  "a decision naming 10.35.1 does not name 0.35.1"
          }

          test "the shape reader reds a missing file, a foreign heading, a stale DRAFT and a stray file" {
              let tags = set [ "v0.30.0"; "v0.0.0-gate-probe" ]
              let faults standing slots = shapeFaults tags standing slots []

              let green =
                  [ slot "0.31.0" "## 0.31.0 — DRAFT\n\nentries"
                    slot "0.30.0" "## 0.30.0 — released 2026-09-23 as `v0.30.0`\n\nrecord" ]

              Expect.isEmpty (faults (Some "0.31.0") green) "the synthetic ledger is the green shape"

              Expect.exists
                  (shapeFaults (Set.add "v0.1.0" tags) (Some "0.31.0") green [])
                  (fun f -> f.Contains "0.1.0 has no ledger file")
                  "a tag with no slot file is red"

              Expect.exists
                  (faults (Some "0.32.0") green)
                  (fun f -> f.Contains "0.32.0 has no ledger file")
                  "a standing version with no slot file is red"

              Expect.exists
                  (faults (Some "0.31.0") [ slot "0.31.0" "## 0.30.0 — DRAFT"; green[1] ])
                  (fun f -> f.Contains "does not open with its slot's heading")
                  "a file opening with another slot's heading is red"

              Expect.exists
                  (faults (Some "0.31.0") [ slot "0.31.0" "## 0.31.0 — DRAFT\n\n## 0.29.0 — x"; green[1] ])
                  (fun f -> f.Contains "carries a heading for another slot, 0.29.0")
                  "a file carrying another slot's heading is red"

              Expect.exists
                  (faults (Some "0.32.0") (green @ [ slot "0.32.0" "## 0.32.0 — DRAFT" ]))
                  (fun f -> f.Contains "is headed DRAFT, and 0.31.0 is not the standing <Version>")
                  "a DRAFT heading on a slot that is no longer the standing version is red"

              Expect.exists
                  (shapeFaults tags (Some "0.31.0") green [ "notes.txt" ])
                  (fun f -> f.Contains "notes.txt is neither the index nor a slot file")
                  "a stray file in the ledger directory is red"
          }

          test "the draft-slot check reports a denied tag and stays quiet on a genuine draft" {
              let slots =
                  [ slot
                        "0.26.0"
                        "## 0.26.0 — DRAFT\n**This section describes a DRAFT slot.** `<Version>` reads `0.26.0` and no `v0.26.0` tag exists\nyet."
                    slot
                        "0.24.0"
                        "## 0.24.0 — released\n**This section describes a DRAFT slot.** `<Version>` reads `0.24.0` and no `v0.24.0` tag exists\nyet." ]

              Expect.equal
                  (staleDraftSentences (Set.ofList [ "v0.24.0"; "v0.23.0" ]) slots)
                  [ sprintf "%s/0.24.0.md" Dir, 2, "0.24.0" ]
                  "the sentence denying a tag that exists is reported, by file and line, and the genuine draft is not"

              Expect.isEmpty
                  (staleDraftSentences (Set.ofList [ "v0.23.0" ]) slots)
                  "with neither version tagged, both sentences are true and nothing is reported"
          }

          test "a tag reads as a release by its numeric version, and only the plain vX.Y.Z spelling does" {
              Expect.equal
                  (ChildProcess.Tags.version "v0.9.0" |> Option.map (fun v -> v < (0, 25, 0)))
                  (Some true)
                  "ordering is numeric: 0.9.0 is BELOW 0.25.0, which a lexical comparison on the tag string gets backwards"

              Expect.isNone
                  (ChildProcess.Tags.version "v0.0.1-alpha.8")
                  "a pre-release spelling parses to nothing, so it is out of frame rather than reported"

              Expect.isNone
                  (ChildProcess.Tags.version "release/v1.2.3")
                  "a foreign tagging convention is out of frame too"
          }

          test
              "the index renders each slot's heading newest first under the preamble, and a hand edit is not the render" {
              let slots =
                  [ slot "0.2.0" "## 0.2.0 — released 2026-07-26 as `v0.2.0`\n\nrecord"
                    slot "0.10.0" "## 0.10.0 — DRAFT\n\nentries" ]

              let index =
                  "# Ledger\n\nPreamble.\n\n## 0.10.0 — DRAFT\n\n[`0.10.0.md`](0.10.0.md)\n\n## 0.2.0 — released 2026-07-26 as `v0.2.0`\n\n[`0.2.0.md`](0.2.0.md)\n"

              Expect.equal
                  (renderIndex index slots)
                  index
                  "the index is the render: numeric order, newest first, the preamble kept"

              Expect.notEqual
                  (renderIndex (index.Replace("## 0.2.0 — released", "## 0.2.0 — DRAFT released")) slots)
                  (index.Replace("## 0.2.0 — released", "## 0.2.0 — DRAFT released"))
                  "an index heading that disagrees with its slot is not the render"

              Expect.equal
                  (renderIndex "# Ledger\n\nPreamble.\n" slots)
                  index
                  "an index with no entries renders them under its preamble"
          }

          // Phase 702 (D274): the measurement it starts from, as a test. Two changes cut from one commit
          // each add one entry, the one way an entry is added; git must merge them in either order.
          test "two changes that each add an entry merge clean in either order, and the merged ledger reads both" {
              scratch (fun root ->
                  gitOk root [ "init"; "-q"; "-b"; "main" ]

                  for k, v in
                      [ "user.name", "ledger test"
                        "user.email", "ledger@example.invalid"
                        "commit.gpgsign", "false"
                        "core.autocrlf", "false" ] do
                      gitOk root [ "config"; k; v ]

                  writeFile (Path.Combine(root, Dir, "0.9.1.md")) scratchDraft
                  gitOk root [ "add"; "-A" ]
                  gitOk root [ "commit"; "-q"; "-m"; "the draft" ]

                  for branch, name, entry in [ "a", "phase-901", entryA; "b", "phase-902", entryB ] do
                      gitOk root [ "checkout"; "-q"; "-b"; branch; "main" ]
                      addEntry root "0.9.1" "internal" name entry |> ignore
                      gitOk root [ "add"; "-A" ]
                      gitOk root [ "commit"; "-q"; "-m"; "an entry" ]

                  for ours, theirs in [ "a", "b"; "b", "a" ] do
                      gitOk root [ "checkout"; "-q"; ours ]

                      match gitIn root [ "merge"; "--no-edit"; theirs ] with
                      | 0, _ ->
                          let draft = fst (read root) |> List.find (fun s -> s.Version = "0.9.1")

                          Expect.stringContains
                              draft.Text
                              entryA
                              (sprintf "on %s, the merged ledger reads entry A" ours)

                          Expect.stringContains
                              draft.Text
                              entryB
                              (sprintf "on %s, the merged ledger reads entry B" ours)

                          gitOk root [ "reset"; "-q"; "--hard"; "ORIG_HEAD" ]
                      | code, out ->
                          gitIn root [ "merge"; "--abort" ] |> ignore
                          failtestf "on %s, `git merge %s` exited %d:\n%s" ours theirs code out)
          }

          test
              "go-red: an entry or a section heading in the standing draft's own file names the file the entry moves to" {
              scratch (fun root ->
                  writeFile (Path.Combine(root, Dir, "0.9.1.md")) (scratchDraft + "\n### Internal\n\n" + entryA)
                  let slots, strays = read root
                  let faults = shapeFaults Set.empty (Some "0.9.1") slots strays

                  Expect.exists
                      faults
                      (fun f ->
                          f.Contains "docs/releases/0.9.1.md:7 carries an entry"
                          && f.Contains "docs/releases/0.9.1/internal/phase-901.md"
                          && f.Contains "unchanged")
                      "the bullet is red, naming the file to create and that the bullet goes there unchanged"

                  Expect.exists
                      faults
                      (fun f -> f.Contains "docs/releases/0.9.1.md:5 carries a section heading, '### Internal'")
                      "the section heading is red: the close writes the sections")
          }

          test "go-red: an entry beside a closed slot names the standing draft's file it moves to" {
              scratch (fun root ->
                  writeFile
                      (Path.Combine(root, Dir, "0.9.0.md"))
                      "## 0.9.0 — released 2026-01-01 as `v0.9.0`\n\nA release.\n"

                  writeFile (Path.Combine(root, Dir, "0.9.0", "internal", "phase-902.md")) entryB
                  writeFile (Path.Combine(root, Dir, "0.9.1.md")) scratchDraft
                  let slots, strays = read root

                  Expect.exists
                      (shapeFaults (set [ "v0.9.0" ]) (Some "0.9.1") slots strays)
                      (fun f ->
                          f.Contains "docs/releases/0.9.0/internal/phase-902.md is an entry of 0.9.0, which is closed"
                          && f.Contains "CORE_APPROVE_DOCS=ledger-index"
                          && f.Contains "move it, unchanged, to docs/releases/0.9.1/internal/phase-902.md")
                      "the entry is red, naming both remedies")
          }

          test "go-red: an entry directory holds only the three class directories and non-empty .md files" {
              scratch (fun root ->
                  writeFile (Path.Combine(root, Dir, "0.9.1.md")) scratchDraft
                  writeFile (Path.Combine(root, Dir, "0.9.1", "internal", "phase-901.md")) entryA

                  for rel, text in
                      [ "0.9.1/notes.txt", "x"
                        "0.9.1/fixes/phase-1.md", entryB
                        "0.9.1/internal/phase-2.txt", entryB
                        "0.9.1/internal/phase-3.md", "  \n"
                        "0.9.7/internal/phase-4.md", entryB ] do
                      writeFile (Path.Combine(root, Dir, rel)) text

                  let slots, strays = read root
                  let faults = shapeFaults Set.empty (Some "0.9.1") slots strays

                  for named in
                      [ "docs/releases/0.9.1/notes.txt is not an entry"
                        "docs/releases/0.9.1/fixes/ is not an entry"
                        "docs/releases/0.9.1/internal/phase-2.txt is not an entry"
                        "docs/releases/0.9.1/internal/phase-3.md is not an entry"
                        "docs/releases/0.9.7/ is an entry directory beside no slot file" ] do
                      Expect.exists faults (fun f -> f.Contains named) (sprintf "red: %s" named)

                  Expect.isFalse
                      (faults |> List.exists (fun f -> f.Contains "phase-901.md"))
                      "and the well-formed entry is not")
          }

          test
              "a draft renders to the bytes its close writes: a section per class, each class's entries in their names' order" {
              scratch (fun root ->
                  let paragraph = String.concat "\n" (entryParagraph "0.9.1")
                  writeFile (Path.Combine(root, Dir, "0.9.1.md")) (scratchDraft + "\n" + paragraph + "\n")

                  for rel, text in
                      [ "breaking/phase-12.md", "- **A breaking sample** (Phase 12, `C.fs`) — class **breaking**.\n"
                        "additive/pr-3.md",
                        "- **A contributed sample** (pull request 3, `E.fs`) — class **additive**.\n"
                        "additive/phase-7.md", "- **An additive sample** (Phase 7, `D.fs`) — class **additive**.\n"
                        "internal/phase-10.md",
                        "- **A later internal sample** (Phase 10, `G.fs`) — class **internal**.\n"
                        "internal/phase-5-tests.md",
                        "- **Its second entry** (Phase 5, `F.fs`) — class **internal**: the tests,\n  on two lines.\n"
                        "internal/phase-5.md", "- **An internal sample** (Phase 5, `F.fs`) — class **internal**.\n" ] do
                      writeFile (Path.Combine(root, Dir, "0.9.1", rel)) text

                  let expected =
                      String.concat
                          "\n"
                          [ "## 0.9.1 — DRAFT"
                            ""
                            "**Slot class: additive.** A scratch slot over the released `0.9.0`."
                            ""
                            "### Breaking"
                            ""
                            "- **A breaking sample** (Phase 12, `C.fs`) — class **breaking**."
                            ""
                            "### Additive"
                            ""
                            "- **An additive sample** (Phase 7, `D.fs`) — class **additive**."
                            "- **A contributed sample** (pull request 3, `E.fs`) — class **additive**."
                            ""
                            "### Internal"
                            ""
                            "- **An internal sample** (Phase 5, `F.fs`) — class **internal**."
                            "- **Its second entry** (Phase 5, `F.fs`) — class **internal**: the tests,"
                            "  on two lines."
                            "- **A later internal sample** (Phase 10, `G.fs`) — class **internal**."
                            "" ]

                  let before = fst (read root) |> List.find (fun s -> s.Version = "0.9.1")
                  Expect.equal before.Text expected "the ledger reads the draft as its close will print it"

                  closeSlot root before |> ignore
                  let closed = File.ReadAllText(Path.Combine(root, Dir, "0.9.1.md"))
                  Expect.equal closed expected "the close writes those bytes into the slot's file"
                  Expect.equal closed before.Text "the draft read before the close is the closed file after it"

                  Expect.isFalse (Directory.Exists(Path.Combine(root, Dir, "0.9.1"))) "and the entry directory is gone")
          }
          // Phase 702 (D274): the open slot's entries moved into their own files, word for word, by two commits, each
          // made on the commit named here: the first moved the twelve entries the slot held, the second the three a
          // merge of main brought in the old shape (Phase 703's among them, as main had come to word it, replacing the
          // text the first move had taken). A guard written after the moves, so it cannot be red first; it holds each
          // move against the tree it was made from (a phase moved twice by the later move) for as long as the slot
          // stands, and its entries' words once the slot closes.
          test "the 0.44.1 slot's entries moved into their own files word for word" {
              let root = Repo.root ()

              let movedFrom =
                  [ "50bc140e9a02a7d9cd8145e8948e7a01b9232d7f"
                    "223e4813b808080b420120ed0d56c486a41398e8" ]

              let phaseOf (b: string) =
                  Regex.Match(b, @"\(Phases? (\d+)").Groups[1].Value

              let befores =
                  movedFrom
                  |> List.map (fun sha -> sha, ChildProcess.git root (sprintf "show %s:%s/0.44.1.md" sha Dir))

              match
                  befores
                  |> List.tryPick (fun (sha, r) ->
                      match r with
                      | Error why -> Some(sha, why)
                      | Ok _ -> None)
              with
              | Some(sha, why) -> skiptestf "the commit %s a move was made on is not in this clone: %s" sha why
              | None ->
                  // each phase's entry as the last move took it
                  let bullets =
                      befores
                      |> List.collect (fun (_, r) ->
                          match r with
                          | Ok before -> bulletsOf before
                          | Error _ -> [])
                      |> List.fold (fun (m: Map<string, string>) b -> m.Add(phaseOf b, b)) Map.empty

                  Expect.equal bullets.Count 14 "the two moves took fourteen phases' entries, Phase 703's twice"
                  let slot = fst (read root) |> List.find (fun s -> s.Version = "0.44.1")

                  for KeyValue(phase, b) in bullets do
                      Expect.stringContains slot.Text b "the slot reads every moved entry, word for word"

                      if slot.State = Draft then
                          match slot.Entries |> List.filter (fun e -> e.Text = b) with
                          | [ e ] ->
                              Expect.equal e.Name ("phase-" + phase) (sprintf "%s holds the bullet it came from" e.Path)
                          | es -> failtestf "%d entry files hold the bullet '%s'" es.Length (b.Substring(0, 60))
          }

          test "the decision record carries no missing number once the standing slot is not the draft (D275)" {
              let root, slots, _, _ = live ()

              let entries =
                  match
                      DecisionsTests.parse (File.ReadAllText(Path.Combine(root, "DECISIONS.md")).Replace("\r\n", "\n"))
                  with
                  | Ok es -> es
                  | Error es -> failtestf "DECISIONS.md does not parse: %s" (String.concat "; " es)

              unrecordedAtRelease (standing root) slots (DecisionsTests.missing entries)
              |> failOn "decision-record"
          }

          test "go-red: a slot turned released refuses each missing decision number, by number, with its remedy" {
              let draft = slot "0.9.1" "## 0.9.1 — DRAFT\nDraft entries."
              let released = slot "0.9.1" "## 0.9.1 — released 2026-11-19 as `v0.9.1`\nReleased."
              Expect.equal draft.State Draft "the scratch draft reads as the draft"
              Expect.isEmpty (unrecordedAtRelease (Some "0.9.1") [ draft ] [ 5; 7 ]) "the draft admits them"
              let faults = unrecordedAtRelease (Some "0.9.1") [ released ] [ 5; 7 ]
              Expect.hasLength faults 2 "one refusal per missing number"

              for n in [ 5; 7 ] do
                  Expect.exists
                      faults
                      (fun f ->
                          f.Contains(sprintf "D%d is missing and the standing slot 0.9.1 is not the draft" n)
                          && f.Contains(sprintf "record D%d `Never-minted`, or land the change that holds it" n))
                      (sprintf "D%d is named with its remedy" n)

              Expect.isEmpty (unrecordedAtRelease (Some "0.9.1") [ released ] []) "a whole record passes"

              Expect.isNonEmpty
                  (unrecordedAtRelease (Some "0.9.2") [ draft ] [ 5 ])
                  "a standing version with no slot of its own is not the draft"
          } ]
