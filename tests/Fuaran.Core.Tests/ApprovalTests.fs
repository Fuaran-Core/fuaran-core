/// Phase 396 - the approval switches have ONE reading (Approval.fs), and this holds every site to it.
/// Phase 502 - the same source sweep holds the suite's one spawn site (`ChildProcess.run`).
/// Phase 503 - the `Projections` family: the projection registry (projections.json) held to the tree, and
/// the four records it restates (.fantomasignore, version-derives.json, copies.json, CONTRIBUTING.md's switch
/// table) held as its renders.
module Fuaran.Core.Tests.ApprovalTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core.Tests.Approval

/// Every `.fs` under `src/`, `tests/` and `tools/`, build residue excluded, repository-relative with `/`.
let private sourceFiles () : (string * string) list =
    [ "src"; "tests"; "tools" ]
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

/// A child-process spawn: starting a `Process`, or building its start info by hand. Phase 502 made
/// `ChildProcess.run` the suite's one spawn site; built from parts for the same reason as above.
let private directSpawn =
    Regex(@"\bProcess" + @"\.Start\b|\bProcess" + @"StartInfo\b", RegexOptions.Compiled)

/// The sources that spawn a child anywhere but `ChildProcess.fs`, as `path:line  text`.
let internal spawnOffenders (files: (string * string list) list) : string list =
    [ for rel, lines in files do
          if rel <> "tests/Fuaran.Core.Tests.Fixtures/ChildProcess.fs" then
              for i, line in List.indexed lines do
                  if directSpawn.IsMatch line then
                      yield sprintf "  %s:%d  %s" rel (i + 1) (line.Trim()) ]

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
                  (fun () -> admitsFor (Only [ "elsewhere" ]) Docs Files.Decisions |> ignore)
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

          testCase "a mistyped filter is red even when the file is already current"
          <| fun _ ->
              let dir =
                  Path.Combine(Path.GetTempPath(), "approval-" + Guid.NewGuid().ToString "N")

              Directory.CreateDirectory dir |> ignore
              let path = Path.Combine(dir, "README.md")

              try
                  File.WriteAllText(path, "current")

                  Expect.throws
                      (fun () -> writeFor (Only [ "READM" ]) Readme Files.Readme path "current" |> ignore)
                      "a typo'd filter against unchanged content is refused, not ignored"

                  Expect.throws
                      (fun () -> requireMatchedFor (Only [ "READM" ]) Readme [ Files.Readme ])
                      "validate's check refuses it before any content comparison"

                  Expect.equal (File.ReadAllText path) "current" "and nothing was written"
              finally
                  Directory.Delete(dir, true)


          testCase "no source reads a CORE_APPROVE_* or FUARAN_REGEN variable outside Approval.fs"
          <| fun _ ->
              let files = sourceFiles ()
              Expect.isGreaterThan files.Length 50 "the sweep sees the sources"

              let offenders =
                  [ for rel, full in files do
                        if rel <> "tests/Fuaran.Core.Tests.Fixtures/Approval.fs" then
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
                  "other variables are untouched"

          testCase "no source spawns a child outside ChildProcess.fs: ChildProcess.run is the one spawn site"
          <| fun _ ->
              let files =
                  sourceFiles ()
                  |> List.map (fun (rel, full) -> rel, List.ofArray (File.ReadAllLines full))

              Expect.isTrue
                  (files
                   |> List.exists (fun (rel, _) -> rel = "tests/Fuaran.Core.Tests.Fixtures/ChildProcess.fs"))
                  "the sweep sees the runner it exempts"

              let offenders = spawnOffenders files

              Expect.isEmpty
                  offenders
                  ("a child is spawned through ChildProcess.run (both pipes drained at once, UTF-8 pinned), never by hand:\n"
                   + String.concat "\n" offenders)

          testCase "the spawn sweep bites: a hand-written spawn is named, the runner itself is not"
          <| fun _ ->
              let start = "Process" + ".Start psi"
              let info = "let psi = Process" + "StartInfo(\"git\", \"status\")"

              Expect.equal
                  (spawnOffenders
                      [ "tests/X.fs", [ "let ok = 1"; "use p = Diagnostics." + start ]
                        "tests/Y.fs", [ info ] ])
                  [ "  tests/X.fs:2  use p = Diagnostics." + start; "  tests/Y.fs:1  " + info ]
                  "both spellings, by file and line"

              Expect.isEmpty
                  (spawnOffenders [ "tests/Fuaran.Core.Tests.Fixtures/ChildProcess.fs", [ "use p = " + start ] ])
                  "the runner's own spawn is the one admitted"

              Expect.isEmpty
                  (spawnOffenders [ "tests/X.fs", [ "ChildProcess.run (ChildProcess.command \"git\" \"status\")" ] ])
                  "a call through the runner is not a spawn" ]

// ---- Phase 503 - the projection registry's renders ------------------------------------------
//
// Four records restate the registry for a reader that cannot read it: `.fantomasignore` (the
// formatter), `version-derives.json`'s `derives` (the workspace version ledger), `copies.json`'s
// `records` (the workspace copy registry) and CONTRIBUTING.md's switch table (a contributor). Each is
// rendered here from the rows, spliced into its file's hand-written remainder, and held byte for byte;
// the readers outside this repository read exactly the members and the spelling they read before.

/// A JSON string literal, escaping only what JSON requires (`<` and `>` stay as they are written).
let internal jsonString (s: string) : string =
    if s |> Seq.exists Char.IsControl then
        invalidArg (nameof s) (sprintf "a rendered string carries a control character: %A" s)

    "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""

/// `text` with the array member `name` (a top-level member, two-space indent) replaced by `block`,
/// whose first line opens the member and whose last closes it; the closing line keeps the comma the
/// file had. `None` when the file has no such member.
let internal spliceMember (text: string) (name: string) (block: string list) : string option =
    let lines = text.Replace("\r\n", "\n").Split('\n')
    let opener = sprintf "  %s: [" (jsonString name)

    match lines |> Array.tryFindIndex ((=) opener) with
    | None -> None
    | Some i ->
        match
            lines
            |> Array.indexed
            |> Array.tryFind (fun (j, l) -> j > i && (l = "  ]" || l = "  ],"))
        with
        | None -> None
        | Some(j, close) ->
            let block =
                match List.rev block with
                | last :: rest -> List.rev ((last + (if close.EndsWith "," then "," else "")) :: rest)
                | [] -> []

            Some(String.concat "\n" (Array.toList lines[.. i - 1] @ block @ Array.toList lines[j + 1 ..]))

/// The hand-written head of `.fantomasignore` (its comment lines) and one line per generated F#
/// row, in registry order.
let internal renderFantomasIgnore (current: string) (rows: Projection list) : string =
    let head =
        current.Replace("\r\n", "\n").Split('\n')
        |> Array.takeWhile (fun l -> l.StartsWith "#")
        |> Array.toList

    let paths =
        rows
        |> List.filter (fun r -> r.Path.EndsWith ".fs")
        |> List.map _.Path
        |> List.distinct

    String.concat "\n" (head @ paths) + "\n"

let private member' (indent: string) (name: string) (value: string) =
    sprintf "%s%s: %s" indent (jsonString name) value

/// `version-derives.json`'s `derives`: one entry per copy of a version-stamped row.
let internal derivesBlock (rows: Projection list) : string list =
    let entries =
        [ for r in rows do
              if r.VersionStamped then
                  for c in r.Copies do
                      [ "    {"
                        member' "      " "emit" (jsonString (Tool.render (Option.get r.Render))) + ","
                        member' "      " "writes" (jsonString r.Path) + ","
                        member' "      " "copiedTo" (jsonString c.Spelling)
                        "    }" ] ]

    [ yield member' "  " "derives" "["
      for i, e in List.indexed entries do
          yield!
              (if i < entries.Length - 1 then
                   e[.. e.Length - 2] @ [ "    }," ]
               else
                   e)
      yield "  ]" ]

/// The placeholder a copy's regeneration names for the checkout it writes into.
let private checkout (repo: string) =
    sprintf "<the %s checkout>" (repo.Substring(repo.LastIndexOf '/' + 1))

/// `copies.json`'s `records`: one record per row and copied-to repository, its consumers the copies'
/// workspace-relative paths. `check` is `fingerprint` for every record (the note in the file says
/// why: a copy may differ from its source in end-of-line policy and nothing else).
let internal recordsBlock (rows: Projection list) : string list =
    let entries =
        [ for r in rows do
              for repo, copies in r.Copies |> List.groupBy _.Repo do
                  [ yield "    {"
                    yield member' "      " "source" (jsonString r.Path) + ","
                    yield member' "      " "consumers" "["

                    for i, c in List.indexed copies do
                        yield
                            "        "
                            + jsonString (c.Repo + "/" + c.Path)
                            + (if i < copies.Length - 1 then "," else "")

                    yield "      ],"
                    yield member' "      " "check" (jsonString "fingerprint") + ","
                    yield
                        member' "      " "regen" (jsonString (Tool.render (Option.get r.Render) + " " + checkout repo))
                    yield "    }" ] ]

    [ yield member' "  " "records" "["
      for i, e in List.indexed entries do
          yield!
              (if i < entries.Length - 1 then
                   e[.. e.Length - 2] @ [ "    }," ]
               else
                   e)
      yield "  ]" ]

/// The header row of CONTRIBUTING.md's switch table, the line the render is anchored to.
[<Literal>]
let internal SwitchTableHeader = "| Switch | Governs | A filter value is |"

/// The switch table's rows: one per switch, in registry order, naming the files it governs and the
/// filter it takes.
let internal switchTableRows (rows: Projection list) : string list =
    let tick (s: string) =
        "`" + s.Replace(PackageHole, "<package>") + "`"

    let either (xs: string list) =
        match List.rev xs with
        | [] -> ""
        | [ x ] -> x
        | last :: rest -> String.concat ", " (List.rev rest) + " or " + last

    rows
    |> List.choose (fun r -> r.Switch |> Option.map (fun s -> s, r))
    |> List.groupBy fst
    |> List.map (fun (switch, rs) ->
        let rs = List.map snd rs

        let paths =
            rs |> List.map _.Path |> List.distinct |> List.map tick |> String.concat ", "

        let filter =
            if rs |> List.exists (fun r -> r.Stem = Some PackageHole) then
                "a package id"
            else
                rs |> List.choose _.Stem |> List.distinct |> List.map tick |> either

        sprintf "| `%s` | %s | %s |" switch paths filter)

/// CONTRIBUTING.md with its switch table's body re-rendered under the header and separator rows;
/// `None` when the file has no table.
let internal renderContributing (current: string) (rows: Projection list) : string option =
    let lines = current.Replace("\r\n", "\n").Split('\n')

    match lines |> Array.tryFindIndex ((=) SwitchTableHeader) with
    | None -> None
    | Some h ->
        let body = h + 2

        let after =
            lines
            |> Array.indexed
            |> Array.tryFind (fun (j, l) -> j >= body && not (l.StartsWith "|"))
            |> Option.map fst
            |> Option.defaultValue lines.Length

        Some(
            String.concat
                "\n"
                (Array.toList lines[.. body - 1]
                 @ switchTableRows rows
                 @ Array.toList lines[after..])
        )

/// The four renders, as `(stem, repo-relative path, current text -> the render or why it has none)`.
let internal renders (rows: Projection list) : (string * string * (string -> Result<string, string>)) list =
    let spliced name block current =
        match spliceMember current name block with
        | Some t -> Ok t
        | None -> Error(sprintf "the file has no top-level `%s` array to render into" name)

    [ Files.FantomasIgnore, ".fantomasignore", (fun c -> Ok(renderFantomasIgnore c rows))
      Files.VersionDerives, "version-derives.json", spliced "derives" (derivesBlock rows)
      Files.Copies, "copies.json", spliced "records" (recordsBlock rows)
      Files.Contributing,
      "CONTRIBUTING.md",
      (fun c ->
          match renderContributing c rows with
          | Some t -> Ok t
          | None -> Error(sprintf "the file has no `%s` table" SwitchTableHeader)) ]

/// The stem handles `Approval` declares for the sites (`Regenerated`, `Ladders`, `Files`), read by
/// reflection so the comparison below cannot be a third list.
let internal handles () : string list =
    let approval = typeof<Request>.DeclaringType

    approval.GetNestedTypes()
    |> Array.filter (fun t -> List.contains t.Name [ "Regenerated"; "Ladders"; "Files" ])
    |> Array.collect (fun t -> t.GetFields(Reflection.BindingFlags.Public ||| Reflection.BindingFlags.Static))
    |> Array.filter _.IsLiteral
    |> Array.map (fun f -> string (f.GetRawConstantValue()))
    |> Array.sort
    |> Array.toList

/// The six switches, by their `Approval` names.
let internal switches = [ Api; Wire; Ladder; Readme; Docs; Regen ]

/// What is wrong with the registry against the tree: a row's file missing (a template row's
/// directory holding no file of its shape), a holder that is no test module, a verb the runner
/// does not have, a runner verb no row names, a switch that is not one of the six, a handle that is
/// no row's stem, or a fixed stem with no handle.
let internal registryFaults
    (exists: string -> bool)
    (templateHits: string -> int)
    (verbs: string list)
    (handles: string list)
    (rows: Projection list)
    : string list =

    let fixedRowStems =
        rows
        |> List.choose (fun r ->
            match r.Switch, r.Stem with
            | Some _, Some s when s <> PackageHole -> Some s
            | _ -> None)
        |> List.distinct

    [ for r in rows do
          if r.Path.Contains PackageHole then
              if templateHits r.Path = 0 then
                  yield sprintf "%s: no file of this shape exists" r.Path
          elif not (exists r.Path) then
              yield sprintf "%s is a registry row and does not exist" r.Path

          if not (exists (sprintf "tests/Fuaran.Core.Tests/%s.fs" r.HeldBy)) then
              yield sprintf "%s is held by `%s`, which is no test module of this project" r.Path r.HeldBy

          match r.Render with
          | Some v when not (List.contains v verbs) ->
              yield sprintf "%s names the family `%s`, which the tool's `render` does not have" r.Path v
          | _ -> ()

          match r.Switch with
          | Some s when not (List.contains s switches) ->
              yield sprintf "%s names the switch `%s`, which Approval.fs does not read" r.Path s
          | _ -> ()

      for v in verbs do
          if not (rows |> List.exists (fun r -> r.Render = Some v)) then
              yield sprintf "the tool's `render %s` writes files no registry row names" v

      for h in handles do
          if not (List.contains h fixedRowStems) then
              yield sprintf "the handle `%s` in Approval.fs is no row's stem" h

      for s in fixedRowStems do
          if not (List.contains s handles) then
              yield sprintf "the stem `%s` has no handle in Approval.fs, so no site can name it" s ]

let private repoText (rel: string) =
    File.ReadAllText(Repo.file rel).Replace("\r\n", "\n")

/// How many files under the template's directory have its shape.
let private templateHits (template: string) =
    let dir = Path.GetDirectoryName(template.Replace('/', Path.DirectorySeparatorChar))
    let name = Path.GetFileName template
    let full = Repo.file dir

    if not (Directory.Exists full) then
        0
    else
        let re =
            Regex("^" + Regex.Escape(name).Replace(Regex.Escape PackageHole, "[^/\\\\]+") + "$")

        Directory.GetFiles full
        |> Array.filter (fun f -> re.IsMatch(Path.GetFileName f))
        |> Array.length

let private sampleRows =
    [ { Path = "conformance/laws/a-laws.json"
        Render = Some "laws"
        Switch = None
        Stem = None
        HeldBy = "LawVectorTests"
        Copies =
          [ { Repo = "x/corpus"
              Path = "laws/a-laws.json" } ]
        VersionStamped = true }
      { Path = "conformance/apply/manifest.json"
        Render = Some "apply"
        Switch = None
        Stem = None
        HeldBy = "ApplyVectorTests"
        Copies =
          [ { Repo = "x/corpus"
              Path = "apply/manifest.json" } ]
        VersionStamped = false }
      { Path = "tests/X/Gen.fs"
        Render = None
        Switch = Some Regen
        Stem = Some "Gen"
        HeldBy = "IdlSpikeTests"
        Copies = []
        VersionStamped = false }
      { Path = "api/{package}.txt"
        Render = None
        Switch = Some Api
        Stem = Some PackageHole
        HeldBy = "PublicSurfaceTests"
        Copies = []
        VersionStamped = false } ]

[<Tests>]
let projectionTests =
    testList
        "Projections"
        [ testCase "the registry is well formed" (fun _ ->
              match parseRegistry (repoText RegistryPath) with
              | Ok rows -> Expect.isGreaterThan rows.Length 30 "the registry was read, every generated artefact a row"
              | Error es -> failtestf "%s is not well formed:\n  %s" RegistryPath (String.concat "\n  " es))

          testCase
              "every row's file exists, its holder is a test module, and every runner verb and switch stem is a row"
              (fun _ ->
                  let faults =
                      registryFaults
                          (fun rel -> File.Exists(Repo.file rel))
                          templateHits
                          Renders.names
                          (handles ())
                          registry.Value

                  Expect.isEmpty
                      faults
                      (sprintf
                          "the projection registry (%s) disagrees with the tree:\n  %s\nRemedy: a generated artefact is one row there; add or correct the row, never a second list."
                          RegistryPath
                          (String.concat "\n  " faults)))

          testCase "the four records the registry restates are its renders, byte for byte" (fun _ ->
              validate Docs

              let stale =
                  [ for stem, rel, render in renders registry.Value do
                        let current = repoText rel

                        match render current with
                        | Error why -> yield sprintf "%s: %s" rel why
                        | Ok rendered when rendered = current -> ()
                        | Ok rendered ->
                            if not (write Docs stem (Repo.file rel) rendered) then
                                yield
                                    sprintf
                                        "%s is not the render of the registry (CORE_APPROVE_DOCS=%s rewrites it)"
                                        rel
                                        stem ]

              Expect.isEmpty
                  stale
                  (sprintf
                      "%s\nRemedy: change the row in %s and re-render; these files are its projections, read by the formatter, the version ledger, the copy registry and contributors."
                      (String.concat "\n" stale)
                      RegistryPath))

          testCase "go-red: the registry reader refuses each malformed row by name" (fun _ ->
              let reds (row: string) (expect: string) =
                  match parseRegistry (sprintf """{"rows":[%s]}""" row) with
                  | Ok _ -> failtestf "accepted %s" row
                  | Error es ->
                      Expect.exists es (fun e -> e.Contains expect) (sprintf "%s names '%s': %A" row expect es)

              reds """{"path":"a.fs","heldBy":"T","colour":"red"}""" "unknown member `colour`"
              reds """{"heldBy":"T"}""" "has no `path`"
              reds """{"path":"a.fs"}""" "has no `heldBy`"
              reds """{"path":"a.fs","heldBy":"T","switch":"FUARAN_REGEN"}""" "a switch without its stem"
              reds """{"path":"api/{package}.txt","heldBy":"T","switch":"CORE_APPROVE_API","stem":"x"}""" "go together"
              reds """{"path":"a.json","heldBy":"T","render":"--emit-laws"}""" "is not a family name"

              reds
                  """{"path":"a.json","heldBy":"T","render":"laws","copies":["corpus/laws/a.json"]}"""
                  "is not `<repo>:<path>`"

              reds """{"path":"a.json","heldBy":"T","copies":["r:a.json"]}""" "no verb to regenerate it with"
              reds """{"path":"a.json","heldBy":"T","render":"laws","versionStamped":true}""" "declares no copy"

              match parseRegistry """{"rows":[{"path":"a","heldBy":"T"},{"path":"a","heldBy":"U"}]}""" with
              | Error es -> Expect.exists es (fun e -> e.Contains "declared twice") "a repeated row is refused"
              | Ok _ -> failtest "a repeated row was accepted")

          testCase
              "go-red: the walk names a missing file, a stray holder, an unknown verb, an unlisted verb and an orphan handle"
              (fun _ ->
                  let program = [ "laws"; "apply" ]

                  let ok = registryFaults (fun _ -> true) (fun _ -> 1) program [ "Gen" ] sampleRows
                  Expect.isEmpty ok "the sample registry is green against a tree that has it"

                  let faults exists hits program handles rows =
                      registryFaults exists hits program handles rows |> String.concat "\n"

                  Expect.stringContains
                      (faults (fun p -> p <> "tests/X/Gen.fs") (fun _ -> 1) program [ "Gen" ] sampleRows)
                      "tests/X/Gen.fs is a registry row and does not exist"
                      "a missing generated file is red, never a skip"

                  Expect.stringContains
                      (faults (fun _ -> true) (fun _ -> 0) program [ "Gen" ] sampleRows)
                      "api/{package}.txt: no file of this shape exists"
                      "a template row with no file is red"

                  Expect.stringContains
                      (faults (fun p -> not (p.EndsWith "IdlSpikeTests.fs")) (fun _ -> 1) program [ "Gen" ] sampleRows)
                      "which is no test module"
                      "a holder that is not a test module is red"

                  Expect.stringContains
                      (faults (fun _ -> true) (fun _ -> 1) (program @ [ "other" ]) [ "Gen" ] sampleRows)
                      "`render other` writes files no registry row names"
                      "a tool family no row names is red"

                  Expect.stringContains
                      (faults (fun _ -> true) (fun _ -> 1) [ "laws" ] [ "Gen" ] sampleRows)
                      "names the family `apply`, which the tool's `render` does not have"
                      "a row naming a family the tool lost is red"

                  Expect.stringContains
                      (faults (fun _ -> true) (fun _ -> 1) program [ "Gen"; "Orphan" ] sampleRows)
                      "the handle `Orphan` in Approval.fs is no row's stem"
                      "a handle with no row is red"

                  Expect.stringContains
                      (faults (fun _ -> true) (fun _ -> 1) program [] sampleRows)
                      "the stem `Gen` has no handle"
                      "a fixed stem no site can name is red")

          testCase "go-red: the renders follow the rows" (fun _ ->
              Expect.equal
                  (renderFantomasIgnore "# head\n# two\nstale.fs\n" sampleRows)
                  "# head\n# two\ntests/X/Gen.fs\n"
                  "the formatter's ignore list is the generated F# rows under the hand comment"

              Expect.equal
                  (derivesBlock sampleRows)
                  [ "  \"derives\": ["
                    "    {"
                    "      \"emit\": \"dotnet run --project tools/Fuaran.Core.Tools -- render laws\","
                    "      \"writes\": \"conformance/laws/a-laws.json\","
                    "      \"copiedTo\": \"x/corpus:laws/a-laws.json\""
                    "    }"
                    "  ]" ]
                  "only the version-stamped row is a derived file, its copy in the one spelling"

              let records = recordsBlock sampleRows |> String.concat "\n"
              Expect.stringContains records "\"x/corpus/apply/manifest.json\"" "a copy record names the workspace path"

              Expect.stringContains
                  records
                  "render apply <the corpus checkout>\""
                  "and the verb, with the checkout it writes into"

              Expect.equal
                  (spliceMember
                      "{\n  \"a\": 1,\n  \"records\": [\n    old\n  ]\n}\n"
                      "records"
                      [ "  \"records\": ["; "  ]" ])
                  (Some "{\n  \"a\": 1,\n  \"records\": [\n  ]\n}\n")
                  "the splice replaces the member and nothing else"

              Expect.isNone (spliceMember "{}" "records" []) "a file with no such member has no render"

              Expect.equal
                  (switchTableRows sampleRows)
                  [ "| `FUARAN_REGEN` | `tests/X/Gen.fs` | `Gen` |"
                    "| `CORE_APPROVE_API` | `api/<package>.txt` | a package id |" ]
                  "one table row per switch, in registry order") ]
