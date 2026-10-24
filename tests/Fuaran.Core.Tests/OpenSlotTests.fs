module Fuaran.Core.Tests.OpenSlotTests

// Phase 560 - the `open-slot` act, run against SCRATCH checkouts: a copy of this repository's
// release-relevant files (props, pin, registry, law files, ledger) and a scratch corpus, both git
// repositories of their own, so nothing here touches this tree or a real corpus clone. The happy path
// checks that the five artefacts agree; each refusal is planted and shown red, with nothing written.

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core.Tests

let private released = "0.9.0"
let private opening = "0.9.1"

let private git (dir: string) (arguments: string list) : string =
    match ChildProcess.argv "git" arguments |> ChildProcess.inDir dir |> ChildProcess.run with
    | Ok o when o.Exit = 0 -> o.Stdout.Trim()
    | Ok o -> failwithf "git %s exited %d: %s" (String.concat " " arguments) o.Exit (o.Stderr + o.Stdout)
    | Error why -> failwith why

/// `git status --porcelain` in `dir`, one path per changed file (read untrimmed: the status columns
/// are positional).
let private changedIn (dir: string) : string list =
    match
        ChildProcess.argv "git" [ "status"; "--porcelain"; "--untracked-files=all" ]
        |> ChildProcess.inDir dir
        |> ChildProcess.run
    with
    | Ok o when o.Exit = 0 ->
        o.Stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun l -> l.Substring 3)
        |> Array.toList
        |> List.sort
    | other -> failwithf "git status: %A" other

/// `git init` in `dir`, with an identity and no signing, answering the directory as git spells it: a
/// temporary path may be a short (8.3) alias of the one git reports, and the corpus checks compare
/// the two.
let private initRepo (dir: string) : string =
    git dir [ "init"; "-q"; "-b"; "main" ] |> ignore

    for k, v in
        [ "user.name", "open-slot test"
          "user.email", "open-slot@example.invalid"
          "commit.gpgsign", "false"
          "tag.gpgsign", "false"
          "core.autocrlf", "false" ] do
        git dir [ "config"; k; v ] |> ignore

    Path.GetFullPath(git dir [ "rev-parse"; "--show-toplevel" ])

let private commitAll (dir: string) (message: string) =
    git dir [ "add"; "-A" ] |> ignore
    git dir [ "commit"; "-q"; "-m"; message ] |> ignore

let private write (path: string) (text: string) =
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, text)

let private registry =
    lazy
        (match Approval.parseRegistry (File.ReadAllText(Repo.file Approval.RegistryPath)) with
         | Ok rows -> rows
         | Error es -> failwithf "%A" es)

let private lawsRow =
    lazy
        (Exporters.tableWith (fun () -> [])
         |> List.find (fun e -> e.Family = LawVectorExport.familyDirName))

/// The law files at the released stamp, rendered once: (file name, text).
let private lawsAtReleased =
    lazy
        (lawsRow.Value.FilesAt.Value released "x"
         |> List.map (fun (p, t) -> Path.GetFileName p, t))

/// A corpus `laws/manifest.json` with a row per Core family at the released stamp and one foreign row.
let private manifestText (dropRow: string option) =
    let rows =
        [ for id, name in LawVectorExport.manifestRows do
              if Some id <> dropRow then
                  let text = lawsAtReleased.Value |> List.find (fst >> (=) name) |> snd

                  let members =
                      match LawVectorExport.derivedMembers text with
                      | Ok ms -> ms
                      | Error e -> failwith e

                  let rendered =
                      members
                      |> List.map (fun (k, v) ->
                          sprintf "\"%s\": %s" k (Fuaran.Core.Canonical.write Fuaran.Core.Rendering.CrossHost v))
                      |> String.concat ", "

                  yield
                      sprintf "    { \"id\": \"%s\", \"kind\": \"law-vectors\", \"file\": \"%s\", %s }" id name rendered
          yield
              "    { \"id\": \"transformLaws\", \"kind\": \"law-vectors\", \"file\": \"transform-laws.json\", \"kitVersion\": \"0.40.0\", \"vectors\": 1 }" ]

    "{\n  \"version\": 1,\n  \"families\": [\n"
    + String.concat ",\n" rows
    + "\n  ]\n}\n"

type private Fixture =
    { Root: string
      Core: string
      Corpus: string }

    member f.Request(version: string, cls: OpenSlot.SlotClass) : OpenSlot.Request =
        { Root = f.Core
          Corpus = f.Corpus
          Version = version
          Class = cls
          OpenedBy = "Phase 999"
          Date = "2026-10-22" }

/// Two scratch repositories: a corpus carrying the declared law copies and the manifest at the
/// released stamp, and a Core whose `<Version>` is the released `v0.9.0`, pinned at the corpus HEAD.
let private fixture (dropRow: string option) : Fixture =
    let root =
        Path.Combine(Path.GetTempPath(), "fuaran-core-560-" + Guid.NewGuid().ToString("N"))

    // the corpus
    Directory.CreateDirectory(Path.Combine(root, "corpus")) |> ignore
    let corpus = initRepo (Path.Combine(root, "corpus"))

    for name, text in lawsAtReleased.Value do
        if name <> CanonicalFormVectorExport.fileName then
            write (Path.Combine(corpus, "laws", name)) text

    write (Path.Combine(corpus, "laws", "manifest.json")) (manifestText dropRow)
    write (Path.Combine(corpus, "README.md")) "the corpus\n"
    commitAll corpus "corpus"
    let corpusHead = git corpus [ "rev-parse"; "HEAD" ]

    // Core
    Directory.CreateDirectory(Path.Combine(root, "core")) |> ignore
    let core = initRepo (Path.Combine(root, "core"))
    let real (rel: string) = File.ReadAllText(Repo.file rel)

    write (Path.Combine(core, "Directory.Build.props")) (OpenSlot.withVersion (real "Directory.Build.props") released)

    let copies =
        match OpenSlot.withPin (real "copies.json") corpusHead "2026-01-01" "the scratch corpus" with
        | Ok t -> t
        | Error e -> failwith e

    write (Path.Combine(core, "copies.json")) copies
    write (Path.Combine(core, Approval.RegistryPath)) (real Approval.RegistryPath)

    for name, text in lawsAtReleased.Value do
        write (Path.Combine(core, "conformance", "laws", name)) text

    let slot =
        sprintf "## %s — released 2026-01-01 as `v%s`\n\nA release.\n" released released

    write (Path.Combine(core, Ledger.Dir, released + ".md")) slot

    write
        (Path.Combine(core, Ledger.IndexPath))
        (Ledger.renderIndex (real Ledger.IndexPath) [ Ledger.parse released (Ledger.Dir + "/" + released + ".md") slot ])

    commitAll core "core"
    git core [ "tag"; "v" + released ] |> ignore

    { Root = root
      Core = core
      Corpus = corpus }

let private dispose (f: Fixture) =
    try
        // git writes read-only object files; clear the flag so the scratch tree can go.
        for p in Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories) do
            File.SetAttributes(p, FileAttributes.Normal)

        Directory.Delete(f.Root, true)
    with _ ->
        ()

let private using (dropRow: string option) (body: Fixture -> unit) =
    let f = fixture dropRow

    try
        body f
    finally
        dispose f

/// Every file under `dir` outside `.git`, with its bytes: what "nothing was written" is checked against.
let private snapshot (dir: string) : Map<string, string> =
    Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
    |> Seq.filter (fun p ->
        not (p.Contains(string Path.DirectorySeparatorChar + ".git" + string Path.DirectorySeparatorChar)))
    |> Seq.map (fun p -> Path.GetRelativePath(dir, p), File.ReadAllText p)
    |> Map.ofSeq

let private stampsIn (text: string) : string list =
    Regex.Matches(text, "\"kitVersion\"\\s*:\\s*\"([^\"]*)\"")
    |> Seq.map (fun m -> m.Groups[1].Value)
    |> Seq.toList

/// The act refuses `request` (after `plant`), every reason naming `expected`, and neither checkout moves.
let private refuses
    (dropRow: string option)
    (plant: Fixture -> unit)
    (request: Fixture -> OpenSlot.Request)
    (expected: string)
    =
    using dropRow (fun f ->
        plant f
        let core, corpus = snapshot f.Core, snapshot f.Corpus

        let coreHead, corpusHead =
            git f.Core [ "rev-parse"; "HEAD" ], git f.Corpus [ "rev-parse"; "HEAD" ]

        match OpenSlot.plan (request f) with
        | Ok _ -> failtestf "the act was admitted; it should refuse with '%s'" expected
        | Error faults ->
            // One planted defect, one reason: a refusal for a reason other than the plant proves nothing.
            match faults with
            | [ only ] -> Expect.stringContains only expected "the refusal names the planted defect"
            | _ -> failtestf "one planted defect, %d reasons: %A" faults.Length faults

        Expect.equal (snapshot f.Core) core "nothing in Core was written"
        Expect.equal (snapshot f.Corpus) corpus "nothing in the corpus was written"
        Expect.equal (git f.Core [ "rev-parse"; "HEAD" ]) coreHead "no Core commit"
        Expect.equal (git f.Corpus [ "rev-parse"; "HEAD" ]) corpusHead "no corpus commit")

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Open slot"
        [ testCase "one act opens a slot whose five artefacts agree, and the corpus commit is exactly the declared set"
          <| fun _ ->
              using None (fun f ->
                  let plan =
                      match OpenSlot.plan (f.Request(opening, OpenSlot.Additive)) with
                      | Ok p -> p
                      | Error es -> failtestf "refused: %A" es

                  let outcome =
                      match OpenSlot.apply plan with
                      | Ok o -> o
                      | Error e -> failtest e

                  // 1. <Version>
                  Expect.equal
                      (OpenSlot.standingVersion (File.ReadAllText(Path.Combine(f.Core, "Directory.Build.props"))))
                      (Ok opening)
                      "<Version> is the opened slot"

                  // 2. Core's law files, every one it emits, each stamped
                  let coreLaws = Directory.GetFiles(Path.Combine(f.Core, "conformance", "laws"))

                  Expect.equal
                      coreLaws.Length
                      lawsAtReleased.Value.Length
                      "Core's own conformance/ keeps every file it emits"

                  for p in coreLaws do
                      Expect.equal (stampsIn (File.ReadAllText p)) [ opening ] (sprintf "%s is stamped %s" p opening)

                  // 3. the corpus commit: exactly the declared copies and the shared index, each stamped
                  let declared =
                      (Exporters.declared registry.Value lawsRow.Value |> Set.toList)
                      @ [ "laws/" + LawVectorExport.manifestFileName ]
                      |> List.sort

                  let changed =
                      git f.Corpus [ "diff"; "--name-only"; "HEAD~1"; "HEAD" ]
                      |> fun s -> s.Replace("\r\n", "\n").Split('\n') |> Array.toList |> List.sort

                  Expect.equal changed declared "the corpus commit carries exactly the declared set"
                  Expect.equal (Some(git f.Corpus [ "rev-parse"; "HEAD" ])) outcome.CorpusCommit "the act made it"
                  Expect.equal (changedIn f.Corpus) [] "and left the corpus clean"

                  Expect.isFalse
                      (File.Exists(Path.Combine(f.Corpus, "laws", CanonicalFormVectorExport.fileName)))
                      "an emitted file the corpus does not declare never lands there"

                  for rel in declared do
                      let stamps = stampsIn (File.ReadAllText(Path.Combine(f.Corpus, rel)))

                      if rel.EndsWith LawVectorExport.manifestFileName then
                          Expect.equal
                              stamps
                              ((LawVectorExport.manifestRows |> List.map (fun _ -> opening)) @ [ "0.40.0" ])
                              "every Core row is restamped, and the foreign row is not"
                      else
                          Expect.equal stamps [ opening ] (sprintf "%s is stamped %s" rel opening)

                  // 4. the pin names the corpus HEAD that carries the re-stamp
                  match SiblingCorpus.readPin (File.ReadAllText(Path.Combine(f.Core, "copies.json"))) with
                  | Error es -> failtestf "%A" es
                  | Ok pin ->
                      Expect.equal pin.Sha (git f.Corpus [ "rev-parse"; "HEAD" ]) "the pin names the corpus HEAD"
                      Expect.equal pin.Date "2026-10-22" "dated"

                      Expect.stringContains
                          pin.Reason
                          (sprintf "The %s slot (opened by Phase 999)" opening)
                          "and says why"

                  // 5. the ledger: the DRAFT file and its rendered index, a well-shaped ledger
                  let slots, strays = Ledger.read f.Core

                  Expect.equal
                      (Ledger.shapeFaults (Set.ofList [ "v" + released ]) (Some opening) slots strays)
                      []
                      "the ledger is well shaped with the new DRAFT standing"

                  Expect.equal
                      (File.ReadAllText(Path.Combine(f.Core, Ledger.IndexPath)))
                      (Ledger.renderIndex (File.ReadAllText(Path.Combine(f.Core, Ledger.IndexPath))) slots)
                      "the index is the render of the slots"

                  Expect.equal
                      (slots |> List.find (fun s -> s.Version = opening)).State
                      Ledger.Draft
                      "the slot opens headed DRAFT"

                  // Core is left with exactly the files the act names for its commit.
                  Expect.equal
                      (changedIn f.Core)
                      (List.sort outcome.CoreFiles)
                      "Core's change is the commit the act prints"

                  // And a second opening is refused: the slot now stands as a DRAFT.
                  match OpenSlot.plan (f.Request("0.9.2", OpenSlot.Additive)) with
                  | Ok _ -> failtest "a second opening over an open DRAFT was admitted"
                  | Error es -> Expect.exists es (fun s -> s.Contains "a DRAFT slot stands") "named")

          testCase "go-red: a version that is not the next patch or minor over the newest tag"
          <| fun _ ->
              refuses None ignore (fun f -> f.Request("0.9.2", OpenSlot.Additive)) "is not the next patch or minor"

          testCase "go-red: a version the stated class does not open"
          <| fun _ ->
              refuses
                  None
                  ignore
                  (fun f -> f.Request("0.10.0", OpenSlot.Additive))
                  "the version and the class you state disagree"

          testCase "go-red: a DRAFT slot already stands"
          <| fun _ ->
              refuses
                  None
                  (fun f ->
                      let props = Path.Combine(f.Core, "Directory.Build.props")
                      File.WriteAllText(props, OpenSlot.withVersion (File.ReadAllText props) opening)
                      commitAll f.Core "draft")
                  (fun f -> f.Request("0.9.2", OpenSlot.Additive))
                  "a DRAFT slot stands"

          testCase "go-red: the slot's ledger file already exists"
          <| fun _ ->
              refuses
                  None
                  (fun f ->
                      write (Path.Combine(f.Core, Ledger.Dir, opening + ".md")) (sprintf "## %s — DRAFT\n" opening)
                      commitAll f.Core "slot file")
                  (fun f -> f.Request(opening, OpenSlot.Additive))
                  "already exists"

          testCase "go-red: the corpus checkout has an uncommitted file outside the declared set"
          <| fun _ ->
              refuses
                  None
                  (fun f -> write (Path.Combine(f.Corpus, "laws", CanonicalFormVectorExport.fileName)) "{}\n")
                  (fun f -> f.Request(opening, OpenSlot.Additive))
                  "uncommitted changes outside the declared set (laws/canonical-form-laws.json)"

          testCase "go-red: the corpus checkout is behind the pin"
          <| fun _ ->
              refuses
                  None
                  (fun f ->
                      write (Path.Combine(f.Corpus, "README.md")) "the corpus, moved\n"
                      commitAll f.Corpus "later"
                      let later = git f.Corpus [ "rev-parse"; "HEAD" ]
                      let copies = Path.Combine(f.Core, "copies.json")

                      match OpenSlot.withPin (File.ReadAllText copies) later "2026-01-02" "later" with
                      | Ok t -> File.WriteAllText(copies, t)
                      | Error e -> failwith e

                      commitAll f.Core "pin later"
                      git f.Corpus [ "checkout"; "-q"; "HEAD~1" ] |> ignore)
                  (fun f -> f.Request(opening, OpenSlot.Additive))
                  "is not the pinned commit"

          testCase "go-red: a corpus copy whose vectors differ from the render is a content change, not a re-stamp"
          <| fun _ ->
              refuses
                  None
                  (fun f ->
                      let p = Path.Combine(f.Corpus, "laws", "decimal-laws.json")

                      File.WriteAllText(
                          p,
                          File.ReadAllText(p).Replace("canonical-refused-empty", "canonical-refused-blank")
                      )

                      commitAll f.Corpus "content")
                  (fun f -> f.Request(opening, OpenSlot.Additive))
                  "laws/decimal-laws.json differs from this kit's render by more than its kitVersion stamp"

          testCase "go-red: the corpus manifest lacks a row the re-stamp must move"
          <| fun _ ->
              refuses (Some "propagationLaws") ignore (fun f -> f.Request(opening, OpenSlot.Additive)) "propagationLaws"

          testCase "go-red: Core has uncommitted changes in a file the act writes"
          <| fun _ ->
              refuses
                  None
                  (fun f ->
                      let copies = Path.Combine(f.Core, "copies.json")
                      File.WriteAllText(copies, File.ReadAllText(copies) + "\n"))
                  (fun f -> f.Request(opening, OpenSlot.Additive))
                  "uncommitted changes in files the act writes (copies.json)"

          testCase "go-red: malformed inputs are refused together"
          <| fun _ ->
              using None (fun f ->
                  match
                      OpenSlot.plan
                          { f.Request("0.9", OpenSlot.Additive) with
                              Date = "22/10/2026"
                              OpenedBy = " " }
                  with
                  | Ok _ -> failtest "admitted"
                  | Error es ->
                      Expect.equal es.Length 3 (sprintf "three defects, three reasons: %A" es)
                      Expect.stringContains es[0] "is not a version" "the version"
                      Expect.stringContains es[1] "is not a date" "the date"
                      Expect.stringContains es[2] "--opened-by" "the opener")

          testCase "the slot's version follows the class, through 0.x and from 1.0.0"
          <| fun _ ->
              Expect.equal (OpenSlot.expectedOver OpenSlot.Additive (0, 40, 2)) (0, 40, 3) "additive: the patch on 0.x"
              Expect.equal (OpenSlot.expectedOver OpenSlot.Breaking (0, 40, 2)) (0, 41, 0) "breaking: the minor on 0.x"
              Expect.equal (OpenSlot.expectedOver OpenSlot.Internal (0, 40, 2)) (0, 40, 3) "internal: the patch"
              Expect.equal (OpenSlot.expectedOver OpenSlot.Additive (1, 2, 3)) (1, 3, 0) "additive: the minor from 1.0"
              Expect.equal (OpenSlot.expectedOver OpenSlot.Breaking (1, 2, 3)) (2, 0, 0) "breaking: the major from 1.0"
              Expect.equal (OpenSlot.candidatesOver (0, 40, 2)) [ (0, 40, 3); (0, 41, 0) ] "no major on 0.x"

          testCase "the pin edit moves the three members and nothing else"
          <| fun _ ->
              let real = File.ReadAllText(Repo.file "copies.json")
              let sha = String('a', 40)

              match OpenSlot.withPin real sha "2026-10-22" "a \"quoted\" reason" with
              | Error e -> failtest e
              | Ok edited ->
                  match SiblingCorpus.readPin edited with
                  | Ok pin ->
                      Expect.equal (pin.Sha, pin.Date, pin.Reason) (sha, "2026-10-22", "a \"quoted\" reason") "moved"
                  | Error es -> failtestf "%A" es

                  let outside (t: string) = t.Substring(t.IndexOf "\"$note\"")
                  Expect.equal (outside edited) (outside real) "every byte after the pin is kept"

          testCase "the next steps push the branch each checkout is on, to its remote"
          <| fun _ ->
              using None (fun f ->
                  // different branches, so a line naming the wrong checkout's branch is red
                  git f.Corpus [ "checkout"; "-b"; "corpus-topic" ] |> ignore
                  git f.Core [ "checkout"; "-b"; "core-topic" ] |> ignore

                  let plan =
                      match OpenSlot.plan (f.Request(opening, OpenSlot.Additive)) with
                      | Ok p -> p
                      | Error es -> failtestf "refused: %A" es

                  let outcome =
                      match OpenSlot.apply plan with
                      | Ok o -> o
                      | Error e -> failtest e

                  let steps = OpenSlot.nextSteps plan outcome

                  Expect.contains
                      steps
                      (sprintf "  1. git -C \"%s\" push origin corpus-topic" f.Corpus)
                      "the corpus push names the remote and the corpus branch"

                  Expect.isTrue
                      (steps
                       |> List.exists (fun l -> l.EndsWith(sprintf "git -C \"%s\" push origin core-topic" f.Core)))
                      "the Core push names the remote and the Core branch")

          testCase "the class sentence agrees in number with who opens the slot"
          <| fun _ ->
              let sentence (openedBy: string) =
                  OpenSlot.slotText "0.9.1" "0.9.0" OpenSlot.Additive openedBy
                  |> fun t -> t.Replace("\n", " ")

              Expect.stringContains (sentence "Phase 555") "Phase 555 opens it." "one phase opens"
              Expect.stringContains (sentence "Phases 474 and 549") "Phases 474 and 549 open it." "several phases open"

          testCase "the draft carries the headings its class admits, empty, in the entry's reading order"
          <| fun _ ->
              let draft (cls: OpenSlot.SlotClass) =
                  OpenSlot.slotText "0.10.0" "0.9.0" cls "Phase 555"

              let sections (text: string) =
                  text.Replace("\r\n", "\n").Split('\n')
                  |> Array.filter (fun l -> l.StartsWith "### ")
                  |> Array.toList

              Expect.equal (sections (draft OpenSlot.Breaking)) [ "### Breaking"; "### Additive" ] "breaking: both"
              Expect.equal (sections (draft OpenSlot.Additive)) [ "### Additive" ] "additive: one"
              Expect.equal (sections (draft OpenSlot.Internal)) [] "internal: none"

              // empty: nothing follows the last heading but its own spacing
              Expect.isTrue
                  ((draft OpenSlot.Breaking).EndsWith "### Breaking\n\n### Additive\n")
                  "the sections hold no body"

              // the draft stays a slot file the ledger reads
              Expect.equal
                  (Ledger.versionHeadings (draft OpenSlot.Breaking))
                  [ "0.10.0", "## 0.10.0 — DRAFT" ]
                  "one DRAFT heading, no other slot's" ]
