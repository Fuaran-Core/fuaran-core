/// Phase 396 - the approval switches have ONE reading (Approval.fs), and this holds every site to it.
module Fuaran.Core.Tests.ApprovalTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core.Tests.Approval

/// Every `.fs` under `src/` and `tests/`, build residue excluded, repository-relative with `/`.
let private sourceFiles () : (string * string) list =
    [ "src"; "tests" ]
    |> List.collect (fun dir ->
        Directory.EnumerateFiles(Snapshots.repoFile dir, "*.fs", SearchOption.AllDirectories)
        |> Seq.filter (fun f ->
            let rel = f.Replace(Path.DirectorySeparatorChar, '/')
            not (rel.Contains "/bin/" || rel.Contains "/obj/"))
        |> List.ofSeq)
    |> List.sort
    |> List.map (fun f -> Path.GetRelativePath(Snapshots.repoFile "", f).Replace(Path.DirectorySeparatorChar, '/'), f)

/// A direct read of one of the switches, in either call spelling. Built from parts so this file
/// does not itself match the sweep it runs.
let private directRead =
    Regex(@"GetEnvironmentVariable\s*\(?\s*""(CORE_" + "APPROVE|FUARAN_" + @"REGEN)", RegexOptions.Compiled)

[<Tests>]
let tests =
    testList
        "Approval switches"
        [ testCase "one truthiness: absent, empty, 0 and false are no; 1 and true are everything; a value is a filter"
          <| fun _ ->
              for v in [ null; ""; "  "; "0"; "false"; "FALSE" ] do
                  Expect.equal (parse v) NotRequested (sprintf "%A is not a request" v)

              for v in [ "1"; "true"; "True"; " 1 " ] do
                  Expect.equal (parse v) Everything (sprintf "%A requests everything" v)

              Expect.equal (parse "Fuaran.Core.Wire") (Only [ "Fuaran.Core.Wire" ]) "a stem is a filter"

              Expect.equal
                  (parse " Fuaran.Core.Wire , Fuaran.Core.Query,")
                  (Only [ "Fuaran.Core.Wire"; "Fuaran.Core.Query" ])
                  "a comma list is several stems, trimmed"

          testCase "a filter admits only its own stems, case-insensitively; a bare 1 admits all"
          <| fun _ ->
              Expect.isTrue (admitsFor Everything Regen Regenerated.Mini) "1 admits any stem"

              let only = Only [ "minigenerated" ]
              Expect.isTrue (admitsFor only Regen Regenerated.Mini) "the named stem"
              Expect.isFalse (admitsFor only Regen Regenerated.Doc) "another stem is left alone"
              Expect.isFalse (admitsFor NotRequested Regen Regenerated.Mini) "0 admits nothing"

              Expect.isTrue
                  (admitsFor (Only [ Ladders.Operations ]) Ladder Ladders.Operations)
                  "the ladder's second projection is addressable on its own"

              Expect.isFalse
                  (admitsFor (Only [ Ladders.Operations ]) Ladder Ladders.Summary)
                  "and leaves the first alone"

          testCase "a filter that matches nothing is red by name, never a quiet no-op"
          <| fun _ ->
              Expect.throws
                  (fun () -> admitsFor (Only [ "NoSuchFile" ]) Regen Regenerated.Mini |> ignore)
                  "an unknown generated-module stem is refused at its own site"

              Expect.throws
                  (fun () -> admitsFor (Only [ "elsewhere" ]) Docs Files.DocCoverage |> ignore)
                  "so is a stem a single-file switch does not govern"

              Expect.equal
                  (unmatchedFor (Only [ "Fuaran.Core.Nope"; "Fuaran.Core.Wire" ]) [ "Fuaran.Core.Wire" ])
                  [ "Fuaran.Core.Nope" ]
                  "a roster-governed switch names exactly the stems it could not place"

              Expect.isEmpty (unmatchedFor Everything [ "Fuaran.Core.Wire" ]) "1 names nothing"
              Expect.isEmpty (unmatchedFor NotRequested [ "Fuaran.Core.Wire" ]) "unset names nothing"

              Expect.throws
                  (fun () -> requireMatchedFor (Only [ "Fuaran.Core.Nope" ]) Api [ "Fuaran.Core.Wire" ])
                  "requireMatched is red when the filter matches nothing"

          testCase "the fixed stems cover every switch with a fixed roster and no other"
          <| fun _ ->
              for name in [ Regen; Ladder; Readme; Docs ] do
                  Expect.isSome (fixedStems name) (name + " has a fixed roster")

              for name in [ Api; Wire ] do
                  Expect.isNone (fixedStems name) (name + " is governed by the packable roster, known at run time")

          testCase "write rewrites only an admitted, changed file, and says which"
          <| fun _ ->
              let dir =
                  Path.Combine(Path.GetTempPath(), "approval-" + Guid.NewGuid().ToString "N")

              Directory.CreateDirectory dir |> ignore
              let path = Path.Combine(dir, "x.txt")

              try
                  Expect.isFalse (writeFor NotRequested Readme Files.Readme path "a") "unrequested writes nothing"
                  Expect.isFalse (File.Exists path) "and creates nothing"
                  Expect.isTrue (writeFor Everything Readme Files.Readme path "a") "requested and new: written"
                  Expect.equal (File.ReadAllText path) "a" "the bytes"
                  Expect.isFalse (writeFor Everything Readme Files.Readme path "a") "unchanged is not rewritten"
                  Expect.isTrue (writeFor Everything Readme Files.Readme path "b") "changed is rewritten"

                  Expect.isTrue
                      (writeFor (Only [ "other" ]) Api "other" path "c")
                      "an admitted stem of an open roster writes"
              finally
                  Directory.Delete(dir, true)

          testCase "no source reads a CORE_APPROVE_* or FUARAN_REGEN variable outside Approval.fs"
          <| fun _ ->
              let files = sourceFiles ()
              Expect.isGreaterThan files.Length 50 "the sweep sees the sources"

              let offenders =
                  [ for rel, full in files do
                        if rel <> "tests/Fuaran.Core.Tests/Approval.fs" then
                            for i, line in File.ReadAllLines full |> Array.indexed do
                                if directRead.IsMatch line then
                                    yield sprintf "  %s:%d  %s" rel (i + 1) (line.Trim()) ]

              Expect.isEmpty
                  offenders
                  ("approval switches are read through Approval.requested / scope / admits / write only:\n"
                   + String.concat "\n" offenders)

          testCase "the sweep's pattern bites: it matches both call spellings of a direct read"
          <| fun _ ->
              let v = "CORE_" + "APPROVE_API"
              Expect.isTrue (directRead.IsMatch(sprintf "Environment.GetEnvironmentVariable \"%s\"" v)) "juxtaposed"
              Expect.isTrue (directRead.IsMatch(sprintf "Environment.GetEnvironmentVariable(\"%s\")" v)) "parenthesised"
              Expect.isTrue (directRead.IsMatch "x.GetEnvironmentVariable \"FUARAN_REGEN\"") "regen"

              Expect.isFalse
                  (directRead.IsMatch "Environment.GetEnvironmentVariable \"FUARAN_CORE_TSC\"")
                  "other variables are untouched" ]
