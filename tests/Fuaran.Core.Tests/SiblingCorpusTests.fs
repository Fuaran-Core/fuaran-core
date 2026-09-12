module Fuaran.Core.Tests.SiblingCorpusTests

open System
open System.Diagnostics
open System.IO
open Expecto

// ---------------------------------------------------------------------------
// Phase 130 — the anchoring proved from a second worktree of this repository,
// and the go-red that shows what the old climb did from the same place.
//
// The property is not "the corpus is found" — a run in the main working tree
// satisfies that while leaving the defect intact. It is that the lookup
// resolves the SAME directory from a LINKED WORKTREE as from the main tree,
// which is the one thing the replaced climb could not do and the reason four
// worktree gates certified against nothing.
//
// The worktree is created with `--no-checkout`: the question git answers is
// read off the worktree's own `.git` file, so materialising a second copy of
// the tree would buy the proof nothing and cost every run a checkout.
//
// The legs deliberately call `anchoredFrom` rather than `resolve`, so the
// environment cannot decide the answer. `resolve` puts the explicit-directory
// override and the documented opt-out in front of the anchoring; either would
// make both sides of the comparison trivially equal and prove nothing about
// where the lookup anchors — which is precisely what the CI configuration
// sets.
// ---------------------------------------------------------------------------

/// The climb Phase 130 replaced, verbatim. It lives here rather than in the seam because it
/// is the REFUTED mechanism: the proof below has to be able to run it, and nothing else
/// should be able to reach it.
let private legacyClimb (start: string) : string option =
    let candidates (root: string) =
        [ Path.Combine(root, "Fuaran-UI", "wire-format-fixtures", "nodes")
          Path.Combine(root, "wire-format-fixtures", "nodes") ]

    let rec climb (dir: string) (budget: int) =
        if budget < 0 || isNull dir then
            None
        else
            match candidates dir |> List.tryFind Directory.Exists with
            | Some d -> Some d
            | None ->
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    climb start 12

let private repoRoot = Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")

let private runGit (workingDir: string) (arguments: string) : int * string =
    let psi = ChildProcess.redirected "git" arguments
    psi.WorkingDirectory <- workingDir
    use p = Process.Start psi
    let out = p.StandardOutput.ReadToEnd()
    let err = p.StandardError.ReadToEnd()
    p.WaitForExit()
    p.ExitCode, (out + err).Trim()

let private gitOnPath =
    try
        fst (runGit repoRoot "--version") = 0
    with _ ->
        false

/// A second worktree of THIS repository, removed again however the body ends.
let private withTemporaryWorktree (body: string -> unit) : unit =
    let path =
        Path.Combine(Path.GetTempPath(), "fuaran-core-anchor-" + Guid.NewGuid().ToString("N"))

    match runGit repoRoot (sprintf "worktree add --detach --no-checkout \"%s\" HEAD" path) with
    | 0, _ ->
        try
            body path
        finally
            runGit repoRoot (sprintf "worktree remove --force \"%s\"" path) |> ignore
            runGit repoRoot "worktree prune" |> ignore
    | code, output ->
        failtestf "could not create a temporary worktree of this repository (git exited %d): %s" code output

// Sequenced as a group: three of these legs add and remove a worktree, which writes into the
// repository's shared administrative directory. Expecto runs a list's cases in parallel by
// default, and concurrent `worktree add` / `remove` / `prune` against one repository is a race
// this suite has no reason to take.
[<Tests>]
let tests =
    testSequencedGroup "sibling-corpus-worktree"
    <| testList
        "SiblingCorpus"
        [

          testCase "the corpus resolves to the same directory from a linked worktree as from the main tree"
          <| fun _ ->
              if not gitOnPath then
                  skiptest "git not on PATH — the main-tree anchoring proof needs git to create a worktree"
              else
                  withTemporaryWorktree (fun worktree ->
                      let fromMainTree = SiblingCorpus.anchoredFrom "nodes" repoRoot
                      let fromWorktree = SiblingCorpus.anchoredFrom "nodes" worktree

                      match fromMainTree, fromWorktree with
                      | Ok expected, Ok actual ->
                          Expect.equal
                              actual
                              expected
                              "the anchored lookup must resolve the same corpus from a linked worktree as from the main tree"
                      | Error why, _ ->
                          // Not this leg's subject: with no corpus at all there is nothing to compare,
                          // and the suites that consume it fail on their own with this same reason.
                          skiptest (
                              "the corpus does not resolve from the main tree either, so the anchoring cannot be compared: "
                              + why
                          )
                      | Ok expected, Error why ->
                          failtestf
                              "the corpus resolves from the main tree ('%s') but NOT from a linked worktree — this is the defect Phase 130 closed: %s"
                              expected
                              why)

          testCase "git reports the same main working tree from a linked worktree as from the main tree"
          <| fun _ ->
              // The mechanism under the leg above, on its own, so a failure says which half broke.
              if not gitOnPath then
                  skiptest "git not on PATH — the main-tree anchoring proof needs git to create a worktree"
              else
                  withTemporaryWorktree (fun worktree ->
                      match SiblingCorpus.mainWorkingTreeFrom repoRoot, SiblingCorpus.mainWorkingTreeFrom worktree with
                      | Ok fromHere, Ok fromWorktree ->
                          Expect.equal
                              (Path.GetFullPath fromWorktree)
                              (Path.GetFullPath fromHere)
                              "the common directory's parent is the main working tree, asked from either place"

                          // And it is a working tree OF THIS REPOSITORY — not merely some
                          // directory. Note it is NOT necessarily `repoRoot`: when this suite
                          // itself runs from a linked worktree, `repoRoot` is that worktree and
                          // the main tree is somewhere else entirely, which is the whole point.
                          Expect.isTrue
                              (File.Exists(Path.Combine(fromHere, "Fuaran.Core.slnx")))
                              (sprintf
                                  "the reported main working tree '%s' carries this repository's solution"
                                  fromHere)
                      | a, b ->
                          failtestf
                              "git did not report the main working tree: from this checkout %A, from a worktree %A"
                              a
                              b)

          testCase "the climb this replaced finds nothing from that worktree"
          <| fun _ ->
              // The go-red. Without it the leg above could pass for the wrong reason — on a
              // machine where the old climb happened to work from a worktree too, there would be
              // no defect to have closed.
              if not gitOnPath then
                  skiptest "git not on PATH — the main-tree anchoring proof needs git to create a worktree"
              else
                  // Climbed from the MAIN working tree, not from `repoRoot`: when this suite runs
                  // from a linked worktree those are different directories, and the contrast being
                  // drawn is between the main tree (where the old climb worked) and a worktree
                  // (where it did not).
                  let mainTree =
                      match SiblingCorpus.mainWorkingTreeFrom repoRoot with
                      | Ok tree -> tree
                      | Error why -> failtestf "git could not say where the main working tree is: %s" why

                  withTemporaryWorktree (fun worktree ->
                      match legacyClimb mainTree, legacyClimb worktree with
                      | None, _ ->
                          skiptest
                              "the old climb finds no corpus from the main working tree either, so there is nothing to contrast it with here"
                      | Some found, None ->
                          Expect.isTrue (Directory.Exists found) "the main-tree climb found a real directory"
                      | Some _, Some alsoFound ->
                          failtestf
                              "the replaced climb found a corpus at '%s' from a temporary worktree under '%s' — either the defect does not reproduce on this machine, or the temporary directory happens to sit beneath a checkout that carries the corpus, in which case this probe is inconclusive rather than passing"
                              alsoFound
                              (Path.GetTempPath()))

          testCase "an absent corpus is a named failure carrying every path tried and the remedy"
          <| fun _ ->
              // The loudness, tested where it is produced rather than by mutating the process's
              // environment — these tests run beside others in the same process.
              let anchor =
                  Path.Combine(Path.GetTempPath(), "fuaran-core-absent-" + Guid.NewGuid().ToString("N"))

              Directory.CreateDirectory anchor |> ignore

              try
                  match SiblingCorpus.underAnchor "nodes" anchor "a directory with no corpus above it" with
                  | Ok found ->
                      failtestf
                          "a corpus resolved at '%s' beneath a fresh temporary directory — the absent-corpus message cannot be exercised here"
                          found
                  | Error why ->
                      Expect.stringContains why "was not found" "the failure says the corpus was not found"
                      Expect.stringContains why anchor "the failure names the anchor it looked beneath"

                      Expect.stringContains
                          why
                          (Path.Combine(anchor, SiblingCorpus.directoryName))
                          "the failure lists the paths it tried"

                      Expect.stringContains why SiblingCorpus.cloneUrl "the failure carries the clone command"

                      Expect.stringContains
                          why
                          SiblingCorpus.dirVariable
                          "the failure names the variable that points at an existing clone"

                      Expect.stringContains why SiblingCorpus.skipVariable "the failure names the documented opt-out"
              finally
                  try
                      Directory.Delete(anchor, true)
                  with _ ->
                      ()

          testCase "a directory that is not the corpus is refused by name, not accepted"
          <| fun _ ->
              // The identity check: a directory of the right NAME is not evidence. Anchored on
              // what the corpus's manifest declares, so an index that merely mentions the
              // families cannot pass for one.
              let root =
                  Path.Combine(Path.GetTempPath(), "fuaran-core-notcorpus-" + Guid.NewGuid().ToString("N"))

              Directory.CreateDirectory(Path.Combine(root, "nodes")) |> ignore

              try
                  match SiblingCorpus.fault "nodes" root with
                  | None -> failtest "a directory carrying only a nodes/ subdirectory was accepted as the corpus"
                  | Some why ->
                      Expect.stringContains why "manifest.json" "the refusal names the document it could not read"

                  File.WriteAllText(Path.Combine(root, "manifest.json"), "{ \"description\": \"nodes idl schema\" }")

                  match SiblingCorpus.fault "nodes" root with
                  | None ->
                      failtest
                          "a manifest that merely MENTIONS the index documents in its prose was accepted — the identity check has decayed into a containment probe"
                  | Some why ->
                      Expect.stringContains why "\"schema\"" "the refusal names the member it expected to be declared"

                  File.WriteAllText(
                      Path.Combine(root, "manifest.json"),
                      "{ \"schema\": \"schema.json\", \"idl\": \"idl.json\" }"
                  )

                  Expect.isNone
                      (SiblingCorpus.fault "nodes" root)
                      "a manifest DECLARING the index documents, with the family beside it, is the corpus"

                  Expect.isSome
                      (SiblingCorpus.fault "laws" root)
                      "and a family it does not carry is refused, naming that family"
              finally
                  try
                      Directory.Delete(root, true)
                  with _ ->
                      () ]
