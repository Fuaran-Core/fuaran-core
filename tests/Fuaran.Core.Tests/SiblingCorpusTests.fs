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
// environment cannot decide the answer. `resolve` puts the opt-in gate and the
// explicit-directory override in front of the anchoring; either would make
// both sides of the comparison trivially equal and prove nothing about where
// the lookup anchors — which is precisely what the CI configuration sets.
//
// Phase 172 — the gate itself is proved at the bottom, in both directions and
// from VALUES rather than the process environment: not asked ⇒ nothing is
// consulted and the leg says so by name; asked and absent ⇒ the Phase 130
// failure, naming the path. Those two are the phase's acceptance ("green with
// no corpus reachable", "fails loudly when asked for and absent") as unit
// tests, so they cannot regress without a red line here.
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

                      Expect.stringContains
                          why
                          SiblingCorpus.askVariable
                          "the failure names the variable that asks for this leg"
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
                      ()

          // ---- Phase 172: the opt-in gate, both directions -------------------------------

          testCase "a leg that was NOT asked for consults nothing and says so by name"
          <| fun _ ->
              // The directory override names a path that does not exist. Were the override
              // consulted at all, the answer would be Absent; the gate reads the ask FIRST, so
              // a checkout with no corpus anywhere is green by construction.
              let nowhere =
                  Path.Combine(Path.GetTempPath(), "fuaran-core-nowhere-" + Guid.NewGuid().ToString("N"))

              for ask in [ None; Some ""; Some "   " ] do
                  match SiblingCorpus.resolveWith ask (Some nowhere) "laws" repoRoot with
                  | SiblingCorpus.NotAsked why ->
                      Expect.stringContains why SiblingCorpus.askVariable "the skip names the variable that asks"
                      Expect.stringContains why "NOT ASKED FOR" "and says the leg was not asked for"
                      Expect.stringContains why "nothing was compared" "and that nothing was compared"
                      Expect.stringContains why "laws/" "and which family's leg it was"
                  | SiblingCorpus.Found root ->
                      failtestf "resolved a corpus at '%s' although the leg was not asked for" root
                  | SiblingCorpus.Absent why ->
                      failtestf "the override was consulted although the leg was not asked for: %s" why

          testCase "a leg that WAS asked for fails when the corpus is absent, naming the path"
          <| fun _ ->
              // The go-red for the acceptance clause "fails loudly when asked for and absent":
              // asked by value, pointed at a directory that does not exist, and the answer is
              // the Phase 130 failure carrying that path and the remedy — never a skip.
              let nowhere =
                  Path.Combine(Path.GetTempPath(), "fuaran-core-nowhere-" + Guid.NewGuid().ToString("N"))

              match SiblingCorpus.resolveWith (Some "1") (Some nowhere) "laws" repoRoot with
              | SiblingCorpus.Absent why ->
                  Expect.stringContains why nowhere "the failure names the directory it was pointed at"
                  Expect.stringContains why SiblingCorpus.dirVariable "and the variable that pointed there"
                  Expect.stringContains why SiblingCorpus.cloneUrl "and carries the clone command"
              | SiblingCorpus.NotAsked why -> failtestf "asked for by value, yet reported not asked: %s" why
              | SiblingCorpus.Found root -> failtestf "a corpus resolved at '%s' from a non-existent override" root ]

// ---------------------------------------------------------------------------
// Phase 394 — the corpus pin, proved in both directions.
//
// The record is read by two readers — the workflow step (`.github/scripts/corpus-pin.ps1`) and the
// suite (`SiblingCorpus.readPin`) — and the first leg below holds them to one answer over a valid
// record and over malformed ones, so a pin the suite accepts is one CI can check out. The drift
// reading is proved against a scratch repository built here, in every direction it distinguishes,
// and the grade table is proved for both values of the ask. The live leg at the foot reads the
// corpus this run would certify against and names both SHAs when it is not at the pin.
// ---------------------------------------------------------------------------

let private validPin =
    """{
  "kind": "copies",
  "corpus": {
    "repository": "fuaran-ui/fuaran-ui-specification",
    "sha": "0123456789abcdef0123456789abcdef01234567",
    "date": "2026-10-07",
    "reason": "a test record"
  },
  "records": []
}"""

/// `.github/scripts/corpus-pin.ps1 -CopiesJson <path>`, run the way the workflows run it, with a
/// scratch `GITHUB_OUTPUT` so the step's real output channel is read too (and a CI run's own output
/// file is never written by the suite): (exit code, stdout+stderr, the GITHUB_OUTPUT file's text).
let private runPinStep (copiesPath: string) : int * string * string =
    let output = Path.GetTempFileName()

    try
        let psi = ChildProcess.redirected "pwsh" ""

        for a in
            [ "-NoProfile"
              "-NonInteractive"
              "-File"
              Path.Combine(repoRoot, ".github", "scripts", "corpus-pin.ps1")
              "-CopiesJson"
              copiesPath ] do
            psi.ArgumentList.Add a

        psi.Environment["GITHUB_OUTPUT"] <- output
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEndAsync()
        let err = p.StandardError.ReadToEndAsync()
        p.WaitForExit()
        p.ExitCode, (out.Result + err.Result).Trim(), File.ReadAllText(output).Replace("\r\n", "\n").Trim()
    finally
        File.Delete output

let private withScratchFile (text: string) (body: string -> unit) =
    let path =
        Path.Combine(Path.GetTempPath(), "fuaran-core-pin-" + Guid.NewGuid().ToString("N") + ".json")

    File.WriteAllText(path, text)

    try
        body path
    finally
        File.Delete path

/// A scratch git repository, removed however the body ends. `git` is run with an identity of its
/// own so the commits need nothing from the machine's configuration.
let private withScratchRepo (body: string -> (string -> string) -> unit) =
    let dir =
        Path.Combine(Path.GetTempPath(), "fuaran-core-pinrepo-" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory dir |> ignore

    let git (arguments: string) =
        match
            runGit
                dir
                ("-c user.name=pin -c user.email=pin@example.invalid -c commit.gpgsign=false "
                 + arguments)
        with
        | 0, out -> out
        | code, out -> failtestf "`git %s` exited %d in the scratch repository: %s" arguments code out

    try
        git "-c init.defaultBranch=main init" |> ignore
        body dir git
    finally
        // git marks its object files read-only; clear that so the scratch tree can be removed.
        for f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) do
            File.SetAttributes(f, FileAttributes.Normal)

        Directory.Delete(dir, true)

let private commit (git: string -> string) (message: string) : string =
    git (sprintf "commit --allow-empty -m \"%s\"" message) |> ignore
    git "rev-parse HEAD"

[<Tests>]
let pinTests =
    testList
        "SiblingCorpus.pin"
        [ testCase "the committed copies.json carries a well-formed corpus pin"
          <| fun _ ->
              match SiblingCorpus.pin () with
              | Ok p -> Expect.equal p.Repository SiblingCorpus.pinRepository "the pin names the corpus repository"
              | Error errs -> failtestf "the committed corpus pin does not read: %s" (String.concat "; " errs)

          testCase "an absent or malformed pin is refused, naming every defect"
          <| fun _ ->
              let refused (text: string) =
                  match SiblingCorpus.readPin text with
                  | Ok p -> failtestf "accepted a malformed pin: %A" p
                  | Error errs -> errs

              Expect.isOk (SiblingCorpus.readPin validPin) "the well-formed record reads"

              let absent = refused """{ "kind": "copies", "records": [] }"""
              Expect.stringContains (List.head absent) "declares no `corpus` record" "an absent record is named"

              let notObject = refused """{ "kind": "copies", "corpus": "main", "records": [] }"""

              Expect.stringContains
                  (List.head notObject)
                  "is not an object"
                  "a branch name in place of a record is refused"

              let three =
                  refused (
                      validPin
                          .Replace("0123456789abcdef0123456789abcdef01234567", "8725ce4")
                          .Replace("2026-10-07", "07/10/2026")
                          .Replace("a test record", "  ")
                  )

              Expect.equal (List.length three) 3 "every defect is collected, not the first"
              Expect.stringContains (String.concat "\n" three) "40 lowercase hex" "an abbreviated SHA is refused"
              Expect.stringContains (String.concat "\n" three) "yyyy-MM-dd" "a non-ISO date is refused"
              Expect.stringContains (String.concat "\n" three) "non-blank" "a blank reason is refused"

              let otherRepo =
                  refused (validPin.Replace("fuaran-ui/fuaran-ui-specification", "someone/fork"))

              Expect.stringContains (List.head otherRepo) "corpus.repository" "a different repository is refused"

              let upper =
                  refused (validPin.Replace("abcdef0123456789abcdef01234567", "ABCDEF0123456789ABCDEF01234567"))

              Expect.stringContains (List.head upper) "corpus.sha" "an uppercase SHA is refused"

          testCase "the workflow pin step and the suite read copies.json alike"
          <| fun _ ->
              // Valid: both accept, and the step's output channel carries exactly what the suite read.
              withScratchFile validPin (fun path ->
                  let code, out, ghOutput = runPinStep path
                  Expect.equal code 0 (sprintf "the step accepts the record the suite accepts: %s" out)

                  Expect.equal
                      ghOutput
                      "repository=fuaran-ui/fuaran-ui-specification\nsha=0123456789abcdef0123456789abcdef01234567"
                      "the step hands the checkout the repository and the SHA, through GITHUB_OUTPUT")

              // The repository's own record: the SHA the workflows check out is the SHA the suite holds.
              let code, out, ghOutput = runPinStep (SiblingCorpus.pinFile ())
              Expect.equal code 0 (sprintf "the step accepts the committed pin: %s" out)

              match SiblingCorpus.pin () with
              | Ok p -> Expect.stringContains ghOutput ("sha=" + p.Sha) "the step and the suite name one SHA"
              | Error errs -> failtestf "the committed pin does not read: %s" (String.concat "; " errs)

              // Malformed, each way the suite refuses: the step refuses too, names the defect, and
              // writes nothing a checkout could use.
              for text, defect in
                  [ """{ "kind": "copies", "records": [] }""", "declares no `corpus` record"
                    validPin.Replace("0123456789abcdef0123456789abcdef01234567", "main"), "corpus.sha"
                    validPin.Replace("2026-10-07", "yesterday"), "corpus.date"
                    validPin.Replace("a test record", ""), "corpus.reason"
                    validPin.Replace("fuaran-ui/fuaran-ui-specification", "someone/fork"), "corpus.repository"
                    "not json at all", "not JSON" ] do
                  Expect.isError (SiblingCorpus.readPin text) (sprintf "the suite refuses the record naming %s" defect)

                  withScratchFile text (fun path ->
                      let code, out, ghOutput = runPinStep path
                      Expect.equal code 1 (sprintf "the step refuses the record naming %s: %s" defect out)
                      Expect.stringContains out defect "and names the defect"
                      Expect.equal ghOutput "" "and hands the checkout nothing")

          testCase "the drift reading tells every direction apart, from a real repository"
          <| fun _ ->
              withScratchRepo (fun dir git ->
                  let first = commit git "first"
                  let second = commit git "second"

                  Expect.equal (SiblingCorpus.driftAt second dir) (SiblingCorpus.AtPin second) "HEAD at the pin"

                  Expect.equal
                      (SiblingCorpus.driftAt first dir)
                      (SiblingCorpus.Ahead(first, second))
                      "HEAD past the pin"

                  let unknown = String.replicate 40 "e"

                  Expect.equal
                      (SiblingCorpus.driftAt unknown dir)
                      (SiblingCorpus.PinUnknown(unknown, second))
                      "a pin the clone has never fetched"

                  git (sprintf "checkout --quiet --detach %s" first) |> ignore

                  Expect.equal
                      (SiblingCorpus.driftAt second dir)
                      (SiblingCorpus.Behind(second, first))
                      "HEAD before the pin"

                  let beside = commit git "beside"

                  Expect.equal
                      (SiblingCorpus.driftAt second dir)
                      (SiblingCorpus.Diverged(second, beside))
                      "HEAD on another line from the pin"

                  // A directory inside a repository is not a checkout of its own: git would answer with
                  // the enclosing repository's HEAD, which means nothing about a corpus copied there.
                  let nested = Path.Combine(dir, "wire-format-fixtures")
                  Directory.CreateDirectory nested |> ignore

                  match SiblingCorpus.driftAt second nested with
                  | SiblingCorpus.Unreadable why -> Expect.stringContains why "not a git checkout of its own" "named"
                  | other -> failtestf "a nested plain directory read as %A" other)

              let plain =
                  Path.Combine(Path.GetTempPath(), "fuaran-core-notgit-" + Guid.NewGuid().ToString("N"))

              Directory.CreateDirectory plain |> ignore

              try
                  match SiblingCorpus.driftAt (String.replicate 40 "a") plain with
                  | SiblingCorpus.Unreadable _ -> ()
                  | other -> failtestf "a directory outside any repository read as %A" other
              finally
                  Directory.Delete plain

          testCase "the grade depends on the direction and, past the pin's ancestry, on the ask"
          <| fun _ ->
              let pinned = String.replicate 40 "1"
              let head = String.replicate 40 "2"
              let file = "copies.json"

              let gradeOf fatal drift = SiblingCorpus.grade fatal file drift

              for fatal in [ false; true ] do
                  Expect.equal (gradeOf fatal (SiblingCorpus.AtPin pinned)) SiblingCorpus.PinHolds "at the pin holds"

                  match gradeOf fatal (SiblingCorpus.Ahead(pinned, head)) with
                  | SiblingCorpus.PinWarn report ->
                      Expect.stringContains report pinned "an ahead clone names the pin"
                      Expect.stringContains report head "and its own HEAD"
                      Expect.stringContains report "AHEAD" "and the direction"
                  | other -> failtestf "an ahead clone graded %A with the ask %b — ahead is never fatal" other fatal

              for drift, word in
                  [ SiblingCorpus.Behind(pinned, head), "BEHIND"
                    SiblingCorpus.Diverged(pinned, head), "DIVERGED"
                    SiblingCorpus.PinUnknown(pinned, head), "NOT CARRYING" ] do
                  match gradeOf true drift, gradeOf false drift with
                  | SiblingCorpus.PinFail asked, SiblingCorpus.PinWarn unasked ->
                      for report in [ asked; unasked ] do
                          Expect.stringContains report pinned (sprintf "%s names the pin" word)
                          Expect.stringContains report head (sprintf "%s names the HEAD" word)
                          Expect.stringContains report word "and the direction"

                      Expect.stringContains
                          unasked
                          SiblingCorpus.askVariable
                          "the unasked report says why it did not fail"
                  | a, u -> failtestf "%s graded %A asked and %A unasked; wanted FAIL and WARN" word a u

              match
                  gradeOf true (SiblingCorpus.Unreadable "no git"), gradeOf false (SiblingCorpus.Unreadable "no git")
              with
              | SiblingCorpus.PinFail _, SiblingCorpus.PinWarn _ -> ()
              | a, u -> failtestf "an unreadable position graded %A asked and %A unasked" a u

          testCase "the corpus this run reads is at copies.json's pin, or the run says which side moved"
          <| fun _ ->
              // Decided by PRESENCE, as the laws/ copy-freshness legs are (Phase 216): a corpus checked
              // out beside this one is read whether or not the live leg was asked for, and the ask
              // decides only whether a corpus that is not at (or past) the pin FAILS.
              match SiblingCorpus.freshness "laws" with
              | SiblingCorpus.NotChecked(why, true) -> failtest why
              | SiblingCorpus.NotChecked(why, false) -> skiptest why
              | SiblingCorpus.Compare(root, fatal) ->
                  match SiblingCorpus.pin () with
                  | Error errs -> failtestf "the committed corpus pin does not read: %s" (String.concat "; " errs)
                  | Ok p ->
                      match SiblingCorpus.grade fatal (SiblingCorpus.pinFile ()) (SiblingCorpus.driftAt p.Sha root) with
                      | SiblingCorpus.PinHolds -> ()
                      | SiblingCorpus.PinWarn report ->
                          printfn "\nCORPUS PIN — %s\n  corpus  %s\n" report root
                          Console.Out.Flush()
                      | SiblingCorpus.PinFail report -> failtestf "CORPUS PIN — %s\n  corpus  %s" report root ]
