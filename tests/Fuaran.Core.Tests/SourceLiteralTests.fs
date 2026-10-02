/// No unpaired surrogate is SPELLED in a literal under `src/`.
///
/// Every public package claims to be Fable-clean, and the claim is certified downstream by the
/// receiving host's Fable gate, not here. Fable cannot write an unpaired UTF-16 surrogate into its
/// output, so a char or string literal spelling one (`'\uD800'`, `"\uDFFF"`) fails the downstream
/// compile outright, and on .NET the same string literal silently becomes U+FFFD. Both have
/// happened: the Wire fuzz alphabet's two atoms (fixed in 0054692) and `Idl.SourceLit`'s surrogate
/// predicates, both found only when the 0.33.0 and 0.34.0 candidates reached the host's gate.
///
/// The rule is textual, so the check is: on every code line under `src/` (comment lines are
/// skipped, since a comment is never compiled), no `\uD800`–`\uDFFF` or `\U0000D800`–`\U0000DFFF`
/// escape. Build a surrogate from its code unit instead (`char 0xD800`, or compare `int c`).
module Fuaran.Core.Tests.SourceLiteralTests

open System.IO
open System.Text.RegularExpressions
open Expecto

let private srcDir () = Snapshots.repoFile "src"

let private surrogateEscape =
    Regex(@"\\u[dD][89a-fA-F][0-9a-fA-F]{2}|\\U0000[dD][89a-fA-F][0-9a-fA-F]{2}", RegexOptions.Compiled)

let private isCommentLine (line: string) =
    let t = line.TrimStart()
    t.StartsWith "//" || t.StartsWith "(*" || t.StartsWith "*"

/// `(file:line, text)` for every code line under `src/` that spells a surrogate escape.
let offenders (files: (string * string[]) list) : (string * string) list =
    [ for (file, lines) in files do
          for i in 0 .. lines.Length - 1 do
              let line = lines[i]

              if not (isCommentLine line) && surrogateEscape.IsMatch line then
                  yield $"{file}:{i + 1}", line.Trim() ]

let private sourceFiles () : (string * string[]) list =
    Directory.EnumerateFiles(srcDir (), "*.fs", SearchOption.AllDirectories)
    |> Seq.filter (fun f ->
        let rel = f.Replace('\\', '/')
        not (rel.Contains "/bin/" || rel.Contains "/obj/"))
    |> Seq.sort
    |> Seq.map (fun f -> Path.GetRelativePath(Snapshots.repoFile "", f).Replace('\\', '/'), File.ReadAllLines f)
    |> Seq.toList

[<Tests>]
let tests =
    testList
        "Source.Literals"
        [ test "no code line under src/ spells an unpaired surrogate" {
              let found = offenders (sourceFiles ())

              let report =
                  found |> List.map (fun (at, text) -> $"  {at}: {text}") |> String.concat "\n"

              Expect.isEmpty
                  found
                  $"an unpaired surrogate is spelled in a literal; Fable cannot emit it and .NET reads a string one as U+FFFD. Build it from its code unit instead:\n{report}"
          }

          test "the check is not vacuous: it finds a char literal and a string literal, and skips a comment" {
              let probe =
                  [ "probe.fs",
                    [| "let a = c >= '\\uD800'"
                       "let b = \"x\\uDFFFy\""
                       "/// a comment may say \"\\uD800\""
                       "let d = int c >= 0xD800" |] ]

              Expect.equal (offenders probe |> List.map fst) [ "probe.fs:1"; "probe.fs:2" ] "two code lines found"
          } ]
