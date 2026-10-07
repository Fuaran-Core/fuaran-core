/// Phase 402 — a disposable gate-probe tag is out of frame for every family that reads tags.
///
/// The release gates are exercised by pushing a throwaway tag, `v0.0.0-gate-probe-<date>`, whose
/// publish run must be refused by the proof leg (RELEASING.md, "Exercising the release gates"). While
/// that tag exists on the remote, every ci run fetches it (`fetch-tags: true`), so a family that read
/// it as a release would go red — or worse, green over the wrong slot. Read before the probe was
/// designed: none can. `ReleaseRecord`, `ReadmeClaims` and `PackageRoster` parse a tag through
/// `PackageRosterTests.releaseTagVersion`, which admits `vX.Y.Z` and nothing else, or look one up by
/// the exact `v<version>` a document names; the newest-tag readers of the surface baselines drop any
/// tag whose third component is not a number. This family holds that, per reader, so a loosened
/// parser (a pre-release spelling admitted, say) is red here before a probe tag can reach it.
module Fuaran.Core.Tests.GateProbeTagTests

open System.IO
open Expecto

/// The probe tag's shape, as RELEASING.md spells it.
let private probe = "v0.0.0-gate-probe-20261007"

let private released = set [ "v0.25.0"; "v0.31.0"; "v0.35.1"; "v0.35.2" ]

let private stability () =
    File.ReadAllText(Snapshots.repoFile "STABILITY.md").Replace("\r\n", "\n")

[<Tests>]
let tests =
    testList
        "GateProbeTag"
        [ test "the release-tag parser every tag family reads through admits no probe tag" {
              Expect.isNone (PackageRosterTests.releaseTagVersion probe) "a pre-release spelling names no released slot"
              Expect.isNone (PackageRosterTests.releaseTagVersion "v0.0.0-gate-probe") "with or without a date"

              Expect.equal
                  (PackageRosterTests.releaseTagVersion "v0.35.2")
                  (Some(0, 35, 2))
                  "and the released series still parses"
          }

          test "ReleaseRecord reads the same faults with the probe tag present" {
              let text = stability ()

              let faults tags =
                  ReleaseRecordTests.releaseFaults
                      PackageRosterTests.entryHeaderFloor
                      ReleaseRecordTests.releaseRecordFloor
                      ReleaseRecordTests.releasedWithoutCitedRun
                      tags
                      (Some "0.36.0")
                      text

              Expect.equal (faults (Set.add probe released)) (faults released) "the probe tag adds and removes nothing"
          }

          test "PackageRoster's tag properties read the same with the probe tag present" {
              let text = stability ()
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

          test "the newest-tag readers of the surface baselines never pick the probe tag" {
              let tags = Set.toList released

              Expect.equal
                  (PublicSurfaceTests.newestVersionTag (probe :: tags))
                  (PublicSurfaceTests.newestVersionTag tags)
                  "the probe tag is dropped, not sorted"

              Expect.equal
                  (PublicSurfaceTests.newestVersionTag [ probe ])
                  None
                  "a clone holding only the probe tag has no release tag"
          } ]
