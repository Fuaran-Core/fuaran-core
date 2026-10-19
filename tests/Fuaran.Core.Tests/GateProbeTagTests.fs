/// Phase 402 — a disposable gate-probe tag is out of frame for every family that reads tags; Phase
/// 499 — and every family reads tags through ONE reader, `ChildProcess.Tags`.
///
/// The release gates are exercised by pushing a throwaway tag, `v0.0.0-gate-probe-<date>`, whose
/// publish run must be refused by the proof leg (RELEASING.md, "Exercising the release gates"). While
/// that tag exists on the remote, every ci run fetches it (`fetch-tags: true`), so a family that read
/// it as a release would go red — or worse, green over the wrong slot. Since Phase 499 the six
/// tag-reading families (the release ledger's two halves, `ReadmeClaims`, the 1.0 laws and the
/// public and wire surfaces' since-tag reports) read the release tags in HEAD's history through
/// `ChildProcess.Tags.released`, parse one through `ChildProcess.Tags.version`, which admits `vX.Y.Z`
/// and nothing else, and pick the newest through `ChildProcess.Tags.newest`. This family holds that
/// reader: a loosened parser (a pre-release spelling admitted, say) is red here before a probe tag
/// can reach it, and so is a reader that stops reading HEAD's history.
///
/// Phase 502 — the reader is the ONLY reader (a source asking git for tags anywhere else is red
/// here), and a clone holding no release tag reads as "no released tag", which every family skips
/// by name, rather than as the empty set the release record and the roster's tag checks are red on.
module Fuaran.Core.Tests.GateProbeTagTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto

/// The probe tag's shape, as RELEASING.md spells it.
let private probe = "v0.0.0-gate-probe-20261007"

let private released = set [ "v0.25.0"; "v0.31.0"; "v0.35.1"; "v0.35.2" ]

/// The release ledger's slots (Phase 397), read once (`Ledger.read`, Phase 503), newest first.
let private ledger () =
    fst (Ledger.read (Snapshots.repoFile ""))

/// Delete a scratch repository; git writes its objects read-only, which `Directory.Delete` refuses.
let private deleteTree (dir: string) =
    try
        for f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) do
            File.SetAttributes(f, FileAttributes.Normal)

        Directory.Delete(dir, true)
    with _ ->
        ()

/// A scratch git repository for one test, removed however the body ends: `body dir run commit`,
/// where `run` fails the test on a git error and `commit` makes an empty commit with an identity of
/// its own. Skipped by name where git cannot create a repository.
let private withScratchRepo (body: string -> (string -> unit) -> (string -> unit) -> unit) =
    let dir =
        Path.Combine(Path.GetTempPath(), "fuaran-core-tags-" + Guid.NewGuid().ToString "N")

    Directory.CreateDirectory dir |> ignore

    try
        let run args =
            match ChildProcess.git dir args with
            | Ok _ -> ()
            | Error why -> failtestf "setting up the scratch repository: %s" why

        match ChildProcess.git dir "init -q" with
        | Error why -> skiptestf "git could not create a scratch repository — %s" why
        | Ok _ ->
            let commit message =
                run (
                    sprintf
                        "-c user.name=probe -c user.email=probe@example.invalid -c commit.gpgsign=false commit -q --allow-empty -m %s"
                        message
                )

            body dir run commit
    finally
        deleteTree dir

/// A git invocation that reads the tags, inside a string literal: a tag listing, or the tag refs
/// read another way. Built from parts so this file does not match the sweep it runs.
let private tagRead =
    Regex(
        "\"[^\"]*(\\btag\\s+(--"
        + "list|-l)\\b|for-each-ref[^\"]*refs/"
        + "tags|ls-remote[^\"]*--"
        + "tags)",
        RegexOptions.Compiled
    )

/// The sources reading git's tags anywhere but `ChildProcess.fs` — a second tag reader — as
/// `path:line`.
let private secondReaders (files: (string * string list) list) : string list =
    [ for rel, lines in files do
          if rel <> "tests/Fuaran.Core.Tests/ChildProcess.fs" then
              for i, line in List.indexed lines do
                  if tagRead.IsMatch line then
                      yield sprintf "%s:%d" rel (i + 1) ]

[<Tests>]
let tests =
    testList
        "GateProbeTag"
        [ test "the release-tag parser every tag family reads through admits no probe tag" {
              Expect.isNone (ChildProcess.Tags.version probe) "a pre-release spelling names no released slot"
              Expect.isNone (ChildProcess.Tags.version "v0.0.0-gate-probe") "with or without a date"

              Expect.equal (ChildProcess.Tags.version "v0.35.2") (Some(0, 35, 2)) "and the released series still parses"

              let listing = String.concat "\n" (probe :: Set.toList released) + "\n"

              Expect.equal
                  (ChildProcess.Tags.ofListing listing)
                  released
                  "a listing carrying the probe tag reads as the released series alone"
          }

          test "the release-record clauses read the same faults with the probe tag present" {
              let slots = ledger ()

              let decisions =
                  match Ledger.decisionTitles (File.ReadAllText(Snapshots.repoFile "DECISIONS.md")) with
                  | Ok m -> m
                  | Error es -> failtestf "DECISIONS.md does not parse: %s" (String.concat "; " es)

              let faults tags =
                  Ledger.releaseFaults Ledger.recordFloor decisions tags (Some "0.36.0") slots

              Expect.equal (faults (Set.add probe released)) (faults released) "the probe tag adds and removes nothing"
          }

          test "the ledger's shape and draft-sentence clauses read the same with the probe tag present" {
              let slots = ledger ()

              Expect.equal
                  (Ledger.shapeFaults (Set.add probe released) (Some "0.36.0") slots [])
                  (Ledger.shapeFaults released (Some "0.36.0") slots [])
                  "no slot file is demanded for the probe tag"

              Expect.equal
                  (Ledger.staleDraftSentences (Set.add probe released) slots)
                  (Ledger.staleDraftSentences released slots)
                  "no draft sentence is read as released by the probe tag"
          }

          test "the newest tag is chosen by version, and never the probe tag" {
              let tags = Set.toList released

              Expect.equal
                  (ChildProcess.Tags.newest (probe :: tags))
                  (ChildProcess.Tags.newest tags)
                  "the probe tag is dropped, not sorted"

              Expect.equal
                  (ChildProcess.Tags.newest [ probe ])
                  None
                  "a clone holding only the probe tag has no release tag"

              Expect.equal
                  (ChildProcess.Tags.newest [ "v0.9.0"; "v0.26.0"; "v0.25.0" ])
                  (Some "v0.26.0")
                  "`v0.26.0` is newer than `v0.9.0` — a lexical sort would name the wrong baseline to compare against"

              Expect.equal
                  (ChildProcess.Tags.newest [ "v1.0.0"; "not-a-tag"; "v0.26.0"; "v1.0" ])
                  (Some "v1.0.0")
                  "a tag this does not parse is dropped rather than ordered by accident"
          }

          test "the reader reads HEAD's history: a checkout behind the newest tag reads the tag it descends from" {
              // Phase 475's shape, built for real: two release tags on a line of history and the
              // probe tag beside the older one. At the tip the reader sees both release tags; checked
              // out at the older tag it sees that tag alone, so "since the newest tag" there means since
              // the tag this tree descends from. A reader that went back to a bare `git tag --list`
              // would read `v0.2.0` from the older checkout, and is red here.
              withScratchRepo (fun dir run commit ->
                  commit "first"
                  run "-c tag.gpgsign=false tag v0.1.0"
                  run (sprintf "-c tag.gpgsign=false tag %s" probe)
                  commit "second"
                  run "-c tag.gpgsign=false tag v0.2.0"

                  Expect.equal
                      (ChildProcess.Tags.released dir)
                      (Ok(set [ "v0.1.0"; "v0.2.0" ]))
                      "at the tip, both release tags and not the probe tag"

                  run "-c advice.detachedHead=false checkout -q v0.1.0"

                  Expect.equal
                      (ChildProcess.Tags.released dir)
                      (Ok(set [ "v0.1.0" ]))
                      "behind the newest tag, only the tag in HEAD's history"

                  Expect.equal
                      (ChildProcess.Tags.released dir |> Result.map ChildProcess.Tags.newest)
                      (Ok(Some "v0.1.0"))
                      "and the newest tag is the one this tree descends from")
          }

          test "a tagless clone reads as no released tag, a tagged one reads its tags, and history without a tag is red" {
              // Phase 502, the tagless-clone contract CONTRIBUTING.md states. A fork or a clone made
              // without tags is not a defect in the tree, so the reader answers "no released tag" as an
              // `Error`, which every tag-reading family skips by NAME; it is not an empty set, which
              // the release record and the two roster tag checks would be red on. Holding release
              // tags of which HEAD descends from none is a different state, and stays the empty set
              // those three checks are red on.
              withScratchRepo (fun dir run commit ->
                  commit "first"

                  Expect.equal
                      (ChildProcess.Tags.released dir)
                      (Error ChildProcess.Tags.NoReleaseTag)
                      "no tag at all: no released tag, never an empty set"

                  run (sprintf "-c tag.gpgsign=false tag %s" probe)

                  Expect.equal
                      (ChildProcess.Tags.released dir)
                      (Error ChildProcess.Tags.NoReleaseTag)
                      "a probe tag alone names no release either"

                  run "-c tag.gpgsign=false tag v0.1.0"
                  Expect.equal (ChildProcess.Tags.released dir) (Ok(set [ "v0.1.0" ])) "tagged: the release tag"

                  run "checkout -q --orphan unrelated"
                  commit "unrelated"

                  Expect.equal
                      (ChildProcess.Tags.released dir)
                      (Ok Set.empty)
                      "release tags held, none in HEAD's history: the empty set the vacuity guards are red on")
          }

          test "a clone of this repository made without its tags reads as no released tag" {
              // The contributor's case, on this repository's own history rather than a synthetic one:
              // `git clone --no-tags` is what a fork's first clone, or a clone that skipped
              // `fetch-tags`, looks like.
              let source = Repo.root ()

              let dir =
                  Path.Combine(Path.GetTempPath(), "fuaran-core-tagless-" + Guid.NewGuid().ToString "N")

              try
                  match
                      ChildProcess.git
                          (Path.GetTempPath())
                          (sprintf "clone -q --no-checkout --no-tags \"%s\" \"%s\"" source dir)
                  with
                  | Error why -> skiptestf "git could not clone this repository without tags — %s" why
                  | Ok _ ->
                      Expect.equal
                          (ChildProcess.Tags.released dir)
                          (Error ChildProcess.Tags.NoReleaseTag)
                          "the tagless clone reads as no released tag, so every tag-reading family skips by name"
              finally
                  deleteTree dir
          }

          test "one tag reader: no source but ChildProcess.fs asks git for tags" {
              let files =
                  [ "src"; "tests" ]
                  |> List.collect (fun d ->
                      Directory.EnumerateFiles(Repo.file d, "*.fs", SearchOption.AllDirectories)
                      |> Seq.filter (fun f ->
                          let rel = f.Replace(Path.DirectorySeparatorChar, '/')
                          not (rel.Contains "/bin/" || rel.Contains "/obj/"))
                      |> Seq.map (fun f ->
                          Path.GetRelativePath(Repo.root (), f).Replace('\\', '/'), List.ofArray (File.ReadAllLines f))
                      |> List.ofSeq)

              Expect.isGreaterThan files.Length 50 "the sweep sees the sources"

              let readers = secondReaders files

              Expect.isEmpty
                  readers
                  ("the release tags are read through ChildProcess.Tags.released only (Phase 499, Phase 502); a second reader at:\n  "
                   + String.concat "\n  " readers)
          }

          test "the second-reader sweep bites" {
              let line = "    match git root \"" + "tag --" + "list\" with"

              Expect.equal
                  (secondReaders [ "tests/X.fs", [ "let a = 1"; line ] ])
                  [ "tests/X.fs:2" ]
                  "a hand-written tag listing is a second reader, named by file and line"

              Expect.equal
                  (secondReaders [ "tests/X.fs", [ "ChildProcess.git root \"" + "for-each-ref refs/" + "tags\"" ] ])
                  [ "tests/X.fs:1" ]
                  "so is reading the tag refs another way"

              Expect.isEmpty
                  (secondReaders [ "tests/Fuaran.Core.Tests/ChildProcess.fs", [ line ] ])
                  "the one reader is admitted"

              Expect.isEmpty
                  (secondReaders
                      [ "tests/X.fs", [ "// `git " + "tag --" + "list` in a comment"; "run \"tag v0.1.0\"" ] ])
                  "a comment naming the command, and creating a tag, are not readers"
          } ]
