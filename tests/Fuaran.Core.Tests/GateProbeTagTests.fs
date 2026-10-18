/// Phase 402 — a disposable gate-probe tag is out of frame for every family that reads tags; Phase
/// 499 — and every family reads tags through ONE reader, `ChildProcess.Tags`.
///
/// The release gates are exercised by pushing a throwaway tag, `v0.0.0-gate-probe-<date>`, whose
/// publish run must be refused by the proof leg (RELEASING.md, "Exercising the release gates"). While
/// that tag exists on the remote, every ci run fetches it (`fetch-tags: true`), so a family that read
/// it as a release would go red — or worse, green over the wrong slot. Since Phase 499 the six
/// tag-reading families (`ReleaseRecord`, `PackageRoster`, `ReadmeClaims`, the 1.0 laws and the
/// public and wire surfaces' since-tag reports) read the release tags in HEAD's history through
/// `ChildProcess.Tags.released`, parse one through `ChildProcess.Tags.version`, which admits `vX.Y.Z`
/// and nothing else, and pick the newest through `ChildProcess.Tags.newest`. This family holds that
/// reader: a loosened parser (a pre-release spelling admitted, say) is red here before a probe tag
/// can reach it, and so is a reader that stops reading HEAD's history.
module Fuaran.Core.Tests.GateProbeTagTests

open System
open System.IO
open Expecto

/// The probe tag's shape, as RELEASING.md spells it.
let private probe = "v0.0.0-gate-probe-20261007"

let private released = set [ "v0.25.0"; "v0.31.0"; "v0.35.1"; "v0.35.2" ]

/// The release ledger's slot files (Phase 397), `(path, text)`, newest first.
let private ledger () =
    PackageRosterTests.ledgerFiles (Snapshots.repoFile "")
    |> List.map (fun f -> f.Path, f.Text)

/// Delete a scratch repository; git writes its objects read-only, which `Directory.Delete` refuses.
let private deleteTree (dir: string) =
    try
        for f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) do
            File.SetAttributes(f, FileAttributes.Normal)

        Directory.Delete(dir, true)
    with _ ->
        ()

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

          test "ReleaseRecord reads the same faults with the probe tag present" {
              let docs = ledger ()

              let faults tags =
                  ReleaseRecordTests.releaseFaults
                      PackageRosterTests.entryHeaderFloor
                      ReleaseRecordTests.releaseRecordFloor
                      ReleaseRecordTests.releasedWithoutCitedRun
                      tags
                      (Some "0.36.0")
                      docs

              Expect.equal (faults (Set.add probe released)) (faults released) "the probe tag adds and removes nothing"
          }

          test "PackageRoster's tag properties read the same with the probe tag present" {
              let text =
                  PackageRosterTests.ledgerText (PackageRosterTests.ledgerFiles (Snapshots.repoFile ""))

              let lines = text.Split('\n') |> Array.toList

              Expect.equal
                  (PackageRosterTests.tagsMissingEntryHeader
                      PackageRosterTests.entryHeaderFloor
                      (Set.add probe released)
                      text)
                  (PackageRosterTests.tagsMissingEntryHeader PackageRosterTests.entryHeaderFloor released text)
                  "no entry header is demanded for the probe tag"

              Expect.equal
                  (PackageRosterTests.staleDraftSentences (Set.add probe released) lines)
                  (PackageRosterTests.staleDraftSentences released lines)
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
                          "and the newest tag is the one this tree descends from"
              finally
                  deleteTree dir
          } ]
