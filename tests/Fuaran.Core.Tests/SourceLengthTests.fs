/// No `.fs` file under `src/` is longer than 2,000 lines, except the files DECISIONS.md D125 and D139
/// name.
///
/// Phase 388 split the internal law files along their banners, and Phase 401 divided the four long
/// multi-module files (`Wire.fs`, `Ops.fs`, `Idl.fs`, `Column.fs`) at their top-level module
/// boundaries, one module per file, and `Query.fs` at the two boundaries its dependency order allows
/// (D139). The files on the exception list below stay whole by ruling: each is ONE public module
/// with public types nested inside it, F# compiles one module from one file, so dividing any of them
/// would rename public types in the `api/` baselines. A file's length is not a reason to move a
/// public surface.
///
/// The list is exact in both directions: a file over the line that is not on it fails by name, and
/// so does an entry that names a file that no longer exists or no longer needs the exception.
module Fuaran.Core.Tests.SourceLengthTests

open System.IO
open Expecto

/// The line every `.fs` under `src/` stays at or under.
let limit = 2000

/// The files that stay whole over `limit` (DECISIONS.md D125, D139), repository-relative, each with
/// the reason it is not divided.
let exceptions: (string * string) list =
    [ "src/Fuaran.Core.Idl.Codegen/Diff.fs",
      "the public module `Diff` holds its public types nested (`Diff+Snapshot`, `Diff+Change`, `Diff+Verdict`), and F# compiles one module from one file; dividing it renames them in api/ (D125)"
      "src/Fuaran.Core.Idl.Codegen/FStar.fs",
      "the public module `FStarTarget` holds its public types nested (`FStarTarget+Slot`, `FStarTarget+VectorModel`), and every section is written over them; dividing it renames them in api/ (D125)"
      "src/Fuaran.Core.OpStream.Dag/DagOpStream.fs",
      "the public module `Dag` holds its public types nested, and F# compiles one module from one file; dividing it renames them in api/ (D125)"
      "src/Fuaran.Core.Conformance/Families.fs",
      "the public module `Families` holds its public types nested (`Families+LawFamily`, `Families+Roster`), and F# compiles one module from one file; dividing it renames them in api/ (D125's class, named by D139)" ]

/// Every finding over `(path, lineCount)` pairs: a file over `limit` the exceptions do not name, an
/// exception naming no file, and an exception naming a file at or under `limit`.
let findings (excepted: string list) (files: (string * int) list) : string list =
    let excused = Set.ofList excepted
    let present = files |> Map.ofList

    [ for (path, count) in files do
          if count > limit && not (excused.Contains path) then
              yield $"{path} is {count} lines, over the {limit}-line rule; divide it at its module boundaries"
      for path in excepted do
          match present.TryFind path with
          | None -> yield $"{path} is excepted from the {limit}-line rule but no longer exists; remove the entry"
          | Some count when count <= limit ->
              yield $"{path} is excepted from the {limit}-line rule but is {count} lines; remove the entry"
          | Some _ -> () ]

let private sourceFiles () : (string * int) list =
    Directory.EnumerateFiles(Snapshots.repoFile "src", "*.fs", SearchOption.AllDirectories)
    |> Seq.filter (fun f ->
        let rel = f.Replace('\\', '/')
        not (rel.Contains "/bin/" || rel.Contains "/obj/"))
    |> Seq.sort
    |> Seq.map (fun f -> Path.GetRelativePath(Snapshots.repoFile "", f).Replace('\\', '/'), File.ReadAllLines(f).Length)
    |> Seq.toList

[<Tests>]
let tests =
    testList
        "Source.Length"
        [ test "every .fs under src/ is at most 2,000 lines, except the files D125 names" {
              let files = sourceFiles ()

              Expect.isGreaterThan
                  (List.length files)
                  100
                  "the scan read the source tree (a vacuous scan would pass any rule)"

              Expect.contains
                  (files |> List.map fst)
                  "src/Fuaran.Core.Wire/Json.fs"
                  "the scan reads repository-relative paths in the spelling the exception list uses"

              let found = findings (List.map fst exceptions) files

              Expect.isEmpty found (String.concat "\n" found)
          }

          test "every exception states its reason" {
              for (path, reason) in exceptions do
                  Expect.isTrue (reason.Contains "D125") $"{path}'s reason cites the ruling it stands on"
          }

          test "the check refuses a long file, a stale exception and a needless one" {
              let files =
                  [ "src/A/Long.fs", limit + 1
                    "src/A/Short.fs", limit
                    "src/A/Ruled.fs", limit + 500 ]

              Expect.equal (findings [ "src/A/Ruled.fs" ] files |> List.length) 1 "only the unexcepted long file"
              Expect.stringContains (findings [ "src/A/Ruled.fs" ] files |> List.head) "src/A/Long.fs" "named"

              Expect.isEmpty
                  (findings [ "src/A/Ruled.fs"; "src/A/Long.fs" ] files)
                  "a file at exactly the limit passes; excepted files pass"

              Expect.stringContains
                  (findings [ "src/A/Ruled.fs"; "src/A/Long.fs"; "src/A/Gone.fs" ] files
                   |> List.head)
                  "no longer exists"
                  "a stale entry is named"

              Expect.stringContains
                  (findings [ "src/A/Ruled.fs"; "src/A/Long.fs"; "src/A/Short.fs" ] files
                   |> List.head)
                  "remove the entry"
                  "a needless entry is named"
          } ]
