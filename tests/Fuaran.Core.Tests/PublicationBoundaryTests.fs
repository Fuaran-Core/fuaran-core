module Fuaran.Core.Tests.PublicationBoundaryTests

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

// ---------------------------------------------------------------------------
// Phase 294 — the publication boundary, swept.
//
// This repository is public. "Written to a public standard" is a claim, and a claim with no check
// decays one convenient reference at a time, invisibly, because nothing breaks: a `///` comment
// that names a private neighbour ships into the package's XML documentation, and a ledger that
// spells a private workspace path ships with the repository. So the standard is a gate.
//
// EVERY tracked file is swept — source comments, docs, READMEs, ledgers and workflow files alike —
// one line at a time, against the rules in `tests/Fuaran.Core.Tests/publication-boundary.json`. The
// vocabulary lives in that data file and not here, for two reasons: a change to what is banned is
// a change to a list, reviewed as one; and this file can then carry none of the words it refuses.
//
// FOUR THINGS ABOUT THE SHAPE, each answering a way a sweep goes quietly green.
//
//  * The sweep is a FUNCTION of (rules, files), and every go-red runs it over synthetic input that
//    differs from a clean baseline in exactly one way. The live case alone shows nothing: it reads
//    a tree expected to be clean, so a sweep that looked at no lines would pass it.
//  * Each rule carries an `example` it must flag, and the data file is REFUSED when a pattern does
//    not match its own example — a rule cannot be edited into silence, and a typo in a pattern is
//    caught at the moment it is made, not when the word next appears.
//  * A carried exception is exempt only where its FILE and its TEXT both match, and one that matches
//    nothing FAILS. An allowlist by file would let the next reference through on the strength of a
//    decision that was about something else; an exception that outlives its line is a hole with a
//    comment beside it. The carried set is PRINTED on every run, green or not.
//  * The swept set must contain the files whose absence would make a green meaningless (the
//    workflow files, the package properties): a sweep over an empty or mis-rooted list is a pass
//    by construction.
//
// Scope is the git-tracked set plus untracked-and-not-ignored files (`git ls-files --cached
// --others --exclude-standard`): exactly what a commit would publish. Build output is ignored and
// so is not swept. Outside a git checkout (a source archive) the sweep walks the tree instead and
// says so.
// ---------------------------------------------------------------------------

/// One banned-vocabulary rule. `Example` is a line the rule must flag.
type Rule =
    { Id: string
      Pattern: Regex
      Say: string
      Example: string }

/// A knowingly-held exception: exempt only where the rule, the file AND the text all match.
type Carried =
    { Rule: string
      File: string
      Text: string
      Why: string }

type Boundary =
    { Rules: Rule list
      Carried: Carried list }

type Hit =
    { Rule: string
      File: string
      Line: int
      Text: string
      Say: string }

/// The data file, repository-relative. The one tracked file the sweep skips.
let boundaryFile = "tests/Fuaran.Core.Tests/publication-boundary.json"

// ---------------------------------------------------------------------------
//  The data file
// ---------------------------------------------------------------------------

let parseBoundary (text: string) : Result<Boundary, string> =
    try
        use doc = JsonDocument.Parse text
        let root = doc.RootElement

        let str (e: JsonElement) (name: string) : Result<string, string> =
            match e.TryGetProperty name with
            | true, v when v.ValueKind = JsonValueKind.String && v.GetString().Trim() <> "" -> Ok(v.GetString())
            | _ -> Error(sprintf "a record is missing the non-empty string member `%s`" name)

        let arr (name: string) : Result<JsonElement list, string> =
            match root.TryGetProperty name with
            | true, v when v.ValueKind = JsonValueKind.Array -> Ok [ for e in v.EnumerateArray() -> e ]
            | _ -> Error(sprintf "the file has no array member `%s`" name)

        let traverse (f: 'a -> Result<'b, string>) (xs: 'a list) : Result<'b list, string> =
            (Ok [], xs)
            ||> List.fold (fun acc x ->
                match acc, f x with
                | Ok ys, Ok y -> Ok(y :: ys)
                | Error e, _ -> Error e
                | _, Error e -> Error e)
            |> Result.map List.rev

        let rule (e: JsonElement) : Result<Rule, string> =
            match str e "id", str e "pattern", str e "say", str e "example" with
            | Ok id, Ok pattern, Ok say, Ok example ->
                try
                    let rx = Regex(pattern, RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant)

                    if rx.IsMatch example then
                        Ok
                            { Id = id
                              Pattern = rx
                              Say = say
                              Example = example }
                    else
                        Error(
                            sprintf
                                "rule `%s` does not match its own example %A — a rule that cannot fire is a rule that has been edited into silence"
                                id
                                example
                        )
                with :? ArgumentException as ex ->
                    Error(sprintf "rule `%s` has a pattern that does not compile: %s" id ex.Message)
            | Error e, _, _, _
            | _, Error e, _, _
            | _, _, Error e, _
            | _, _, _, Error e -> Error e

        let carried (e: JsonElement) : Result<Carried, string> =
            match str e "rule", str e "file", str e "text", str e "why" with
            | Ok r, Ok f, Ok t, Ok w ->
                Ok
                    { Rule = r
                      File = f
                      Text = t
                      Why = w }
            | Error e, _, _, _
            | _, Error e, _, _
            | _, _, Error e, _
            | _, _, _, Error e -> Error e

        match arr "rules", arr "carried" with
        | Ok rs, Ok cs ->
            match traverse rule rs, traverse carried cs with
            | Ok rules, Ok carriedList ->
                let ids = rules |> List.map _.Id

                if List.isEmpty rules then
                    Error "the file declares no rules — a sweep with no rules is a pass by construction"
                elif (List.distinct ids).Length <> ids.Length then
                    Error "two rules share an id"
                else
                    match carriedList |> List.tryFind (fun c -> not (List.contains c.Rule ids)) with
                    | Some c -> Error(sprintf "a carried exception names the unknown rule `%s`" c.Rule)
                    | None -> Ok { Rules = rules; Carried = carriedList }
            | Error e, _
            | _, Error e -> Error e
        | Error e, _
        | _, Error e -> Error e
    with :? JsonException as ex ->
        Error(sprintf "the boundary file is not valid JSON: %s" ex.Message)

// ---------------------------------------------------------------------------
//  The sweep — a pure function of (rules, files)
// ---------------------------------------------------------------------------

let sweepText (rules: Rule list) (file: string) (text: string) : Hit list =
    let lines = text.Replace("\r\n", "\n").Split('\n')

    [ for i in 0 .. lines.Length - 1 do
          for r in rules do
              if r.Pattern.IsMatch lines[i] then
                  yield
                      { Rule = r.Id
                        File = file
                        Line = i + 1
                        Text = lines[i].Trim()
                        Say = r.Say } ]

let private isCarriedBy (c: Carried) (h: Hit) =
    c.Rule = h.Rule
    && c.File = h.File
    && h.Text.Contains(c.Text, StringComparison.Ordinal)

/// Sweep `files` (repository-relative path, text). Returns the violations, the hits a carried
/// exception holds, and the carried exceptions that held nothing (stale).
let sweepFiles (b: Boundary) (files: (string * string) list) : Hit list * Hit list * Carried list =
    let hits =
        files
        |> List.filter (fun (path, _) -> path <> boundaryFile)
        |> List.collect (fun (path, text) -> sweepText b.Rules path text)

    let held, violations =
        hits
        |> List.partition (fun h -> b.Carried |> List.exists (fun c -> isCarriedBy c h))

    let stale =
        b.Carried
        |> List.filter (fun c -> not (hits |> List.exists (fun h -> isCarriedBy c h)))

    violations, held, stale

// ---------------------------------------------------------------------------
//  The swept set
// ---------------------------------------------------------------------------

let private repoRoot = Snapshots.repoFile "."

let private binaryExtensions =
    set
        [ ".png"
          ".jpg"
          ".jpeg"
          ".gif"
          ".ico"
          ".dll"
          ".exe"
          ".pdb"
          ".nupkg"
          ".snupkg"
          ".zip"
          ".gz" ]

/// `git ls-files --cached --others --exclude-standard -z` from `root`, or why git could not say.
let private gitFiles (root: string) : Result<string list, string> =
    try
        let psi =
            ChildProcess.redirected "git" "ls-files --cached --others --exclude-standard -z"

        psi.WorkingDirectory <- root
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "`git ls-files` exited %d: %s" p.ExitCode (err.Trim()))
        else
            out.Split('\000', StringSplitOptions.RemoveEmptyEntries) |> Array.toList |> Ok
    with e ->
        Error("`git` could not be run: " + e.Message)

/// The source-archive fallback: every file under `root` except build output and other
/// repositories' checkouts. Less precise than git's own list, which is why it is only the fallback.
let walkFiles (root: string) : string list =
    let skipDirs =
        set
            [ "bin"
              "obj"
              ".git"
              "node_modules"
              "fable_modules"
              ".fable"
              ".fstar"
              "wire-format-fixtures" ]

    let rec walk (dir: string) : string list =
        [ for f in Directory.GetFiles dir -> Path.GetRelativePath(root, f).Replace('\\', '/')
          for d in Directory.GetDirectories dir do
              if not (skipDirs.Contains(Path.GetFileName d)) then
                  yield! walk d ]

    walk root

/// Read a file as text, or `None` for a binary file (by extension, or a NUL in the first 8 KiB).
let private readText (root: string) (rel: string) : string option =
    if binaryExtensions.Contains(Path.GetExtension(rel).ToLowerInvariant()) then
        None
    else
        let full = Path.Combine(root, rel)

        if not (File.Exists full) then
            None
        else
            let bytes = File.ReadAllBytes full

            if bytes |> Array.truncate 8192 |> Array.contains 0uy then
                None
            else
                Some(UTF8Encoding(false).GetString bytes)

/// The swept set for `root`: the paths, how they were found, and their text.
let sweptSet (root: string) : string * (string * string) list =
    let how, paths =
        match gitFiles root with
        | Ok paths -> "git", paths |> List.map (fun p -> p.Replace('\\', '/'))
        | Error _ -> "directory walk (not a git checkout)", walkFiles root

    how,
    paths
    |> List.distinct
    |> List.choose (fun p -> readText root p |> Option.map (fun t -> p, t))

// ---------------------------------------------------------------------------
//  Tests
// ---------------------------------------------------------------------------

let private live =
    lazy
        (match parseBoundary (File.ReadAllText(Path.Combine(repoRoot, boundaryFile))) with
         | Ok b -> b
         | Error why -> failwithf "%s is malformed: %s" boundaryFile why)

let private describe (h: Hit) =
    sprintf "  %s:%d  [%s] %s\n      %s" h.File h.Line h.Rule h.Say (h.Text.Substring(0, min h.Text.Length 200))

/// A clean synthetic baseline; each go-red differs from it in exactly one way.
let private cleanBaseline =
    [ "src/Clean.fs", "/// A public thing that names only itself.\nlet x = 1\n"
      "README.md", "# A public readme\n\nSee Phase 294 and fuaran-core#294.\n" ]

[<Tests>]
let publicationBoundaryTests =
    testList
        "Publication boundary"
        [ testCase "the boundary file is well-formed, and every rule fires on its own example"
          <| fun _ ->
              let b = live.Value
              Expect.isNonEmpty b.Rules "the file declares rules"

              for r in b.Rules do
                  let hits = sweepText b.Rules "probe.txt" r.Example

                  Expect.isTrue
                      (hits |> List.exists (fun h -> h.Rule = r.Id))
                      (sprintf "rule `%s` flags its own example" r.Id)

          testCase "the sweep is green over every tracked file, and prints what it carries"
          <| fun _ ->
              let b = live.Value
              let how, files = sweptSet repoRoot
              let paths = files |> List.map fst |> Set.ofList

              // The sweep must have looked at something. A sweep over an empty or mis-rooted list is
              // a pass by construction, so the files whose absence would make green meaningless are
              // named, and so is a floor on the count.
              for must in
                  [ "Directory.Build.props"
                    ".github/workflows/ci.yml"
                    ".github/workflows/publish-packages.yml"
                    "README.md"
                    boundaryFile ] do
                  Expect.isTrue
                      (Set.contains must paths)
                      (sprintf "the swept set (%s) includes %s — a sweep that cannot see it proves nothing" how must)

              Expect.isGreaterThan files.Length 100 (sprintf "the swept set (%s) is a real tree" how)

              let violations, held, stale = sweepFiles b files

              printfn "publication boundary: swept %d text file(s) via %s, %d rule(s)." files.Length how b.Rules.Length
              printfn "carried exceptions (knowingly held):"

              for c in b.Carried do
                  printfn "  [%s] %s  %s" c.Rule c.File c.Text
                  printfn "      why: %s" c.Why

              for h in held do
                  printfn "  carried: %s:%d" h.File h.Line

              Expect.isEmpty
                  stale
                  (sprintf
                      "a carried exception matched nothing — delete it, it is a hole with a comment beside it:\n%s"
                      (stale
                       |> List.map (fun c -> sprintf "  [%s] %s  %s" c.Rule c.File c.Text)
                       |> String.concat "\n"))

              Expect.isEmpty
                  violations
                  (sprintf
                      "%d publication-boundary violation(s) — rewrite each in generic terms (\"a downstream consumer\", \"the workspace registry\"):\n%s"
                      violations.Length
                      (violations |> List.map describe |> String.concat "\n"))

          testCase "go-red: each rule, over a probe file carrying its example, is refused"
          <| fun _ ->
              let b = live.Value

              for r in b.Rules do
                  let probe = "src/Probe.fs", sprintf "let y = 2\n/// %s\nlet z = 3\n" r.Example
                  let violations, _, _ = sweepFiles b (probe :: cleanBaseline)

                  match violations with
                  | [ h ] ->
                      Expect.equal h.Rule r.Id (sprintf "the probe for `%s` is refused by that rule" r.Id)
                      Expect.equal h.Line 2 "on the line that carries it"
                  | other ->
                      failtestf
                          "the probe for `%s` produced %d violations:\n%s"
                          r.Id
                          other.Length
                          (other |> List.map describe |> String.concat "\n")

          testCase "the clean baseline passes, so the go-reds differ from it by one thing only"
          <| fun _ ->
              let violations, held, stale = sweepFiles live.Value cleanBaseline
              Expect.isEmpty violations "no violations"
              Expect.isEmpty held "nothing carried"

              Expect.equal
                  stale.Length
                  live.Value.Carried.Length
                  "every carried exception is stale over a tree that carries none"

          testCase "a phase citation is vocabulary-free: a number is not a name"
          <| fun _ ->
              let violations, _, _ =
                  sweepFiles
                      live.Value
                      [ "docs/x.md", "Phase 259 and fuaran-core#259 and fuaran#1600 tie a change to its record.\n" ]

              Expect.isEmpty violations "bare phase citations are allowed everywhere"

          testCase "go-red: a carried exception is exempt only where its file AND its text match"
          <| fun _ ->
              let b = live.Value
              let c = b.Carried |> List.head
              let rule = b.Rules |> List.find (fun r -> r.Id = c.Rule)

              // The carried line: held. The same text in another file, and another line in the
              // carried file, are both still violations.
              let carriedLine = sprintf "x %s y %s" c.Text rule.Example
              let otherLine = rule.Example

              let violations, held, _ =
                  sweepFiles
                      b
                      [ c.File, carriedLine + "\n" + otherLine + "\n"
                        "docs/elsewhere.md", carriedLine + "\n" ]

              Expect.equal held.Length 1 "the carried line in the carried file is held"
              Expect.equal held.Head.File c.File "in the carried file"

              Expect.equal
                  (violations |> List.map (fun h -> h.File, h.Line) |> List.sort)
                  [ c.File, 2; "docs/elsewhere.md", 1 ]
                  "a new hit in the carried file and the same text elsewhere both still fail"

          testCase "go-red: a carried exception that matches nothing is reported stale"
          <| fun _ ->
              let b = live.Value
              let _, _, stale = sweepFiles b cleanBaseline
              Expect.equal stale b.Carried "over a tree with none of the carried text, every exception is stale"

          testCase "go-red: the boundary file refuses a rule that cannot fire, a bad pattern and an empty rule set"
          <| fun _ ->
              let file (rules: string) =
                  sprintf """{ "rules": [ %s ], "carried": [] }""" rules

              let expectError what text =
                  match parseBoundary text with
                  | Error _ -> ()
                  | Ok _ -> failtestf "%s was accepted" what

              expectError
                  "a pattern that does not match its own example"
                  (file """{ "id": "r", "pattern": "zzz", "say": "s", "example": "aaa" }""")

              expectError
                  "a pattern that does not compile"
                  (file """{ "id": "r", "pattern": "(", "say": "s", "example": "(" }""")

              expectError "an empty rule set" (file "")
              expectError "a rule with no say" (file """{ "id": "r", "pattern": "a", "example": "a" }""")

              expectError
                  "a carried exception naming an unknown rule"
                  """{ "rules": [ { "id": "r", "pattern": "a", "say": "s", "example": "a" } ], "carried": [ { "rule": "q", "file": "f", "text": "t", "why": "w" } ] }"""

              expectError
                  "two rules sharing an id"
                  (file
                      """{ "id": "r", "pattern": "a", "say": "s", "example": "a" }, { "id": "r", "pattern": "b", "say": "s", "example": "b" }""")

          testCase "the boundary file itself is the one file the sweep skips"
          <| fun _ ->
              let b = live.Value
              let r = b.Rules.Head

              let violations, _, _ =
                  sweepFiles b [ boundaryFile, r.Example + "\n"; "docs/y.md", r.Example + "\n" ]

              Expect.equal (violations |> List.map _.File) [ "docs/y.md" ] "only the data file is exempt, by exact path"

          testCase "go-red: over an on-disk tree the real discovery path refuses a probe file"
          <| fun _ ->
              let b = live.Value
              let r = b.Rules.Head

              let dir =
                  Path.Combine(Path.GetTempPath(), "publication-boundary-" + Guid.NewGuid().ToString("N"))

              Directory.CreateDirectory dir |> ignore

              try
                  File.WriteAllText(Path.Combine(dir, "clean.md"), "nothing to see\n")
                  File.WriteAllText(Path.Combine(dir, "probe.md"), "line one\n" + r.Example + "\n")

                  // Not a git checkout: the directory-walk fallback must find the probe, and say it walked.
                  let how, files = sweptSet dir
                  Expect.stringContains how "directory walk" "an untracked directory is swept by walking it"
                  let violations, _, _ = sweepFiles { b with Carried = [] } files

                  Expect.equal
                      (violations |> List.map (fun h -> h.File, h.Line))
                      [ "probe.md", 2 ]
                      "the probe is found on its line and the clean file is not"

                  // A git checkout: the same probe, discovered through `git ls-files` (untracked and
                  // not ignored counts, because that is what a commit would publish).
                  let psi = ChildProcess.redirected "git" "init -q"
                  psi.WorkingDirectory <- dir
                  use p = Process.Start psi
                  p.StandardOutput.ReadToEnd() |> ignore
                  p.StandardError.ReadToEnd() |> ignore
                  p.WaitForExit()

                  if p.ExitCode = 0 then
                      File.WriteAllText(Path.Combine(dir, ".gitignore"), "ignored.md\n")
                      File.WriteAllText(Path.Combine(dir, "ignored.md"), r.Example + "\n")
                      let how2, files2 = sweptSet dir
                      Expect.equal how2 "git" "a git checkout is swept through git"
                      let violations2, _, _ = sweepFiles { b with Carried = [] } files2

                      Expect.equal
                          (violations2 |> List.map (fun h -> h.File, h.Line))
                          [ "probe.md", 2 ]
                          "through git the probe is found and an ignored file is not swept"
              finally
                  try
                      Directory.Delete(dir, true)
                  with _ ->
                      ()

          testCase "binary files are skipped, not misread as text"
          <| fun _ ->
              let dir =
                  Path.Combine(Path.GetTempPath(), "publication-boundary-bin-" + Guid.NewGuid().ToString("N"))

              Directory.CreateDirectory dir |> ignore

              try
                  File.WriteAllBytes(Path.Combine(dir, "blob.dat"), [| 0uy; 1uy; 2uy; 0uy |])
                  File.WriteAllBytes(Path.Combine(dir, "image.png"), Encoding.ASCII.GetBytes "text in a png")
                  File.WriteAllText(Path.Combine(dir, "note.md"), "text\n")
                  let _, files = sweptSet dir
                  Expect.equal (files |> List.map fst) [ "note.md" ] "only the text file is read"
              finally
                  try
                      Directory.Delete(dir, true)
                  with _ ->
                      () ]
