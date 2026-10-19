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

let private remedy =
    "Remedy: follow the release sequence in STABILITY.md \"Versioning policy\" — corpus re-stamp, the receiving gate green against the packed candidate, a clean pack, then the record, the heading flip (the slot's ledger file, and the index re-rendered with CORE_APPROVE_DOCS=ledger-index) and the README stamps in ONE commit, which is the commit the tag names."

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
          } ]
