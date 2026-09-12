module Fuaran.Core.Tests.WorkingCopyEolTests

open System
open System.Diagnostics
open System.IO
open Expecto

// ---------------------------------------------------------------------------
// Phase 129 — the working copy's line endings, as a test rather than as a habit.
//
// `.gitattributes` pins `* text=auto eol=lf`, which governs the index and the
// checkout. It does NOT govern the working copy afterwards: a formatter resolves
// its output line ending from EditorConfig and falls back to the platform's, so
// before `.editorconfig` pinned `end_of_line = lf` a format run on Windows
// rewrote every `.fs` file to CRLF. The result was a tree `git status` called
// dirty while `git diff` called clean — and, the part that reached a consumer, a
// package whose compiled multi-line string templates carried whichever ending the
// machine that built it happened to use.
//
// `IdlCodegenEolTests` closes that at the generator: its emitters normalise, so
// the emitted artefact no longer depends on the build machine's checkout style.
// This file measures the state the generator no longer trusts, because the drift
// has other consequences — a phantom-dirty tree blocks a pull on any file the
// incoming commits touch, and `git status` stops being usable as a close-out
// check. Catching it here means catching it where it happens.
//
// Why `git ls-files --eol` and not a byte scan of the tree: the question is about
// TRACKED text files, and only git knows which files those are and which of them
// its own attributes call text. A scan would have to re-implement that
// classification and would then be measuring something slightly different from
// what git will do on the next checkout.
// ---------------------------------------------------------------------------

/// The repository root — the directory holding the solution file. Located by the
/// same climb the fixture resolvers use, from the CWD and from the test binary,
/// rather than a build-output-relative path.
let private repoRoot () : string option =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        elif File.Exists(Path.Combine(dir, "Fuaran.Core.slnx")) then
            Some dir
        else
            match Directory.GetParent dir with
            | null -> None
            | parent -> climb parent.FullName (budget - 1)

    [ Directory.GetCurrentDirectory(); AppContext.BaseDirectory ]
    |> List.tryPick (fun start -> climb start 12)

/// The tracked paths whose WORKING-COPY ending is not LF, read off
/// `git ls-files --eol` output. A line is
/// `i/<index>  w/<worktree>  attr/<attrs><TAB><path>` — the three verdict columns
/// separated by SPACES, and a single tab before the path — so the verdict is
/// everything before the tab and the path is everything after it.
///
/// `internal` so the go-red case below can hand it a synthetic listing: the real
/// repository is expected to be clean, and a check whose only case is the clean
/// one has never been shown to detect anything.
let internal driftedPaths (lines: string list) : string list =
    lines
    |> List.filter (fun l -> l.StartsWith("i/", StringComparison.Ordinal))
    |> List.choose (fun l ->
        let fields = l.Split('\t')

        if fields.Length < 2 then
            None
        else
            let verdict = fields[0]

            if verdict.Contains "w/crlf" || verdict.Contains "w/mixed" then
                Some((fields |> Array.last).Trim())
            else
                None)

/// `git ls-files --eol` from `root`, or why it could not be run. Spawned through
/// `ChildProcess.redirected` so the child's stdout decodes as UTF-8 whatever code
/// page the runner inherited — a path in this listing can carry any character.
let private lsFilesEol (root: string) : Result<string list, string> =
    try
        let psi = ChildProcess.redirected "git" "ls-files --eol"
        psi.WorkingDirectory <- root
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "`git ls-files --eol` exited %d: %s" p.ExitCode (err.Trim()))
        else
            out.Split('\n')
            |> Array.toList
            |> List.map (fun l -> l.TrimEnd('\r'))
            |> List.filter (fun l -> l <> "")
            |> Ok
    with e ->
        Error("`git` could not be run: " + e.Message)

let private remedy =
    "Remedy: `dotnet fantomas src tests` for F# sources (it writes LF per .editorconfig), then "
    + "`git add --renormalize .` followed by `git checkout -- .` for anything left."

[<Tests>]
let tests =
    testList
        "Working copy — line endings"
        [

          test "no tracked text file is CRLF in the working copy" {
              match repoRoot () with
              | None ->
                  failtest "could not locate the repository root (Fuaran.Core.slnx) from the CWD or the test binary"
              | Some root ->
                  match lsFilesEol root with
                  | Error why ->
                      // Skipped by NAME with the reason printed, never passed silently: a check
                      // that reads as green when its instrument is missing is worse than absent.
                      // This is the source-archive / no-git case, not a defect in the tree.
                      printfn "working-copy line-ending check SKIPPED: %s" why
                      skiptestf "working-copy line-ending check skipped — %s" why
                  | Ok lines ->
                      Expect.isNonEmpty lines "`git ls-files --eol` listed at least one tracked file"

                      match driftedPaths lines with
                      | [] -> ()
                      | drifted ->
                          let shown = drifted |> List.truncate 20 |> String.concat "\n       "

                          let tail =
                              if List.length drifted > 20 then
                                  sprintf "\n       … and %d more" (List.length drifted - 20)
                              else
                                  ""

                          failtestf
                              "%d tracked file(s) are CRLF in the working copy:\n       %s%s\n%s"
                              (List.length drifted)
                              shown
                              tail
                              remedy
          }

          test "the check detects drift — a CRLF listing is reported, an LF one is not" {
              // The go-red control for the test above, over a synthetic listing rather than by
              // perturbing the repository: the clean case alone cannot show the classifier works.
              //
              // The line shape is git's own and it is easy to get wrong: the three verdict
              // columns are separated by SPACES and only the path is preceded by a tab. A
              // fixture that tabbed between the columns instead passed the clean case and
              // reported nothing for the drifted one — the classifier looked right and was
              // reading the wrong field.
              let clean =
                  [ "i/lf    w/lf    attr/text=auto eol=lf \tREADME.md"
                    "i/lf    w/lf    attr/text=auto eol=lf \tsrc/Fuaran.Core.Tree/Tree.fs"
                    "i/-text w/       attr/binary \tdocs/logo.png" ]

              let drifted =
                  [ "i/lf    w/crlf  attr/text=auto eol=lf \tREADME.md"
                    "i/lf    w/lf    attr/text=auto eol=lf \tsrc/Fuaran.Core.Tree/Tree.fs"
                    "i/lf    w/mixed attr/text=auto eol=lf \tdocs/ADOPTION.md" ]

              Expect.isEmpty (driftedPaths clean) "an all-LF listing reports nothing"

              Expect.equal
                  (driftedPaths drifted)
                  [ "README.md"; "docs/ADOPTION.md" ]
                  "both a CRLF and a mixed working copy are reported, and the LF file is not"
          } ]
