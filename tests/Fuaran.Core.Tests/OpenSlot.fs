/// Phase 560 - a version slot opens in ONE act: `open-slot <version> --class <c> --opened-by <text>
/// --corpus <dir>` (the tool's verb; DECISIONS.md D218).
///
/// Opening a slot changes five things that are correct only together: `<Version>` in
/// `Directory.Build.props`; the conformance `kitVersion` stamp lines, re-rendered; a corpus commit
/// carrying exactly the corpus's DECLARED copies of those files; Core's corpus pin (`copies.json`),
/// moved to that commit; and the slot's `docs/releases/<v>.md` DRAFT with its rendered index. They
/// were hand steps, and on 2026-10-21 a slot opened without the corpus half kept `main` red for hours.
///
/// The act is a PLAN, read and checked with nothing written, then the writes in one order: the corpus
/// first (its files, staged by name, and its commit), then this repository's files, the pin naming
/// the corpus HEAD that carries the re-stamp. Every refusal is raised by `plan`, so a refused opening
/// writes nothing anywhere. The commit in this repository is left to the caller, who pushes the
/// corpus first: the act prints both.
module Fuaran.Core.Tests.OpenSlot

open System
open System.IO
open System.Text.RegularExpressions
open Fuaran.Core

/// The class a slot is opened for (STABILITY.md, "Versioning policy").
type SlotClass =
    /// A breaking change: the MINOR through the `0.x` line, the MAJOR from `1.0.0`.
    | Breaking
    /// An additive change: the PATCH through the `0.x` line, the MINOR from `1.0.0`.
    | Additive
    /// A change that moves no public surface: the PATCH.
    | Internal

let className =
    function
    | Breaking -> "breaking"
    | Additive -> "additive"
    | Internal -> "internal"

let parseClass (s: string) : SlotClass option =
    [ Breaking; Additive; Internal ] |> List.tryFind (fun c -> className c = s)

/// What the act is asked to do.
type Request =
    {
        /// This repository's root (the tool passes its own; the test a scratch copy).
        Root: string
        /// A checkout of the shared corpus, a git repository of its own.
        Corpus: string
        /// The version the slot opens.
        Version: string
        Class: SlotClass
        /// Who opens it, as the ledger and the pin say it: `Phase 555`, `Phases 487 and 491`.
        OpenedBy: string
        /// Today, `yyyy-MM-dd`: the pin's `date`.
        Date: string
    }

/// Everything the act will write, computed with nothing written.
type Plan =
    {
        Request: Request
        /// The standing `<Version>` the slot replaces, which is the newest release tag's.
        Previous: string
        /// The corpus files, absolute, with their new text: only the declared copies and shared indexes.
        CorpusWrites: (string * string) list
        /// Those files, relative to the corpus and forward-slashed: the set a corpus commit may carry.
        CorpusDeclared: string list
        /// The families the re-stamp names, in the corpus manifest's spelling.
        Families: string list
        /// This repository's files except `copies.json`, absolute, with their new text.
        CoreWrites: (string * string) list
        /// The current `copies.json` text and pin; the new pin is written once the corpus HEAD is known.
        CopiesText: string
        OldPin: SiblingCorpus.CorpusPin
    }

/// What the act did.
type Outcome =
    {
        /// The corpus commit the act made, or `None` when the corpus HEAD already carried the re-stamp.
        CorpusCommit: string option
        /// The corpus HEAD the pin now names.
        CorpusHead: string
        /// The files the corpus commit carries, relative.
        CorpusFiles: string list
        /// This repository's files the act wrote, relative and forward-slashed: the commit to make.
        CoreFiles: string list
    }

let private triple (v: string) : (int * int * int) option = ChildProcess.Tags.version ("v" + v)

let private show (a: int, b: int, c: int) = sprintf "%d.%d.%d" a b c

/// The version a slot of `cls` opens over the release `newest`.
let expectedOver (cls: SlotClass) (a: int, b: int, c: int) : int * int * int =
    match cls with
    | Breaking when a = 0 -> (0, b + 1, 0)
    | Breaking -> (a + 1, 0, 0)
    | Additive when a = 0 -> (0, b, c + 1)
    | Additive -> (a, b + 1, 0)
    | Internal -> (a, b, c + 1)

/// The versions a slot may open over `newest` at all: the next patch or minor, and from `1.0.0` the
/// next major. `1.0.0` itself is a cut with its own laws (STABILITY.md), not a slot this act opens.
let candidatesOver (a: int, b: int, c: int) : (int * int * int) list =
    [ yield (a, b, c + 1)
      yield (a, b + 1, 0)
      if a > 0 then
          yield (a + 1, 0, 0) ]

// ---- the text the act writes ------------------------------------------------------------------

let private versionRe =
    Regex(@"<Version>(?<v>[^<]*)</Version>", RegexOptions.Compiled)

/// The standing `<Version>` of `Directory.Build.props`, or why there is not exactly one.
let standingVersion (propsText: string) : Result<string, string> =
    match versionRe.Matches propsText |> Seq.toList with
    | [ m ] -> Ok(m.Groups["v"].Value.Trim())
    | ms -> Error(sprintf "Directory.Build.props declares %d <Version> elements, not one" ms.Length)

/// `propsText` with its one `<Version>` set to `version`; nothing else moves.
let withVersion (propsText: string) (version: string) : string =
    versionRe.Replace(propsText, sprintf "<Version>%s</Version>" version, 1)

/// `text` broken at spaces into lines of at most `width` characters (a longer word stands alone).
let wrap (width: int) (text: string) : string list =
    text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
    |> Array.fold
        (fun (lines: string list) word ->
            match lines with
            | current :: rest when current.Length + 1 + word.Length <= width -> (current + " " + word) :: rest
            | [] -> [ word ]
            | _ -> word :: lines)
        []
    |> List.rev

/// The slot file a new slot opens with: its DRAFT heading and the slot paragraph, in the shape the
/// `0.40.1`-`0.40.3` slots were opened with by hand.
let slotText (version: string) (previous: string) (cls: SlotClass) (openedBy: string) : string =
    let (a, b, _) = defaultArg (triple version) (0, 0, 0)
    let (pa, pb, _) = defaultArg (triple previous) (0, 0, 0)

    let kind =
        if a <> pa then "major"
        elif b <> pb then "minor"
        else "patch"

    let why =
        match cls with
        | Breaking when a = 0 ->
            "through the `0.x` line a breaking class advances the MINOR, which makes it a MINOR slot, and every later member rides it"
        | Breaking ->
            "a breaking class advances the MAJOR, which makes it a MAJOR slot, and every later member rides it"
        | _ ->
            let higher, next =
                if kind = "patch" && (a = 0 || cls = Internal) then
                    "a member of a higher class", sprintf "the `%s` minor" (show (a, b + 1, 0))
                else
                    "a breaking member", sprintf "the `%s` major" (show (a + 1, 0, 0))

            sprintf
                "the work in this slot is %s, which makes it a %s slot, and %s advances it to %s rather than riding it"
                (className cls)
                (kind.ToUpperInvariant())
                higher
                next

    let paragraph =
        sprintf
            "**Slot class: %s.** The %s after the released `%s`. It opens over `v%s`, which is a consumer's contract, so nothing rides it; %s ([`STABILITY.md`](../../STABILITY.md), \"Versioning policy\"). %s opens it. The conformance `kitVersion` stamp lines move to `%s` with the slot, and the corpus pin moves to the re-stamp of the declared copies."
            (className cls)
            kind
            previous
            previous
            why
            openedBy
            version

    sprintf "## %s — DRAFT\n\n%s\n" version (String.concat "\n" (wrap 115 paragraph))

/// `names` as prose: `a`, `a and b`, `a, b and c`.
let private prose (names: string list) =
    match List.rev names with
    | [] -> ""
    | [ one ] -> one
    | last :: rest -> String.concat ", " (List.rev rest) + " and " + last

/// The pin's `reason` for the slot.
let pinReason
    (version: string)
    (openedBy: string)
    (families: string list)
    (old: SiblingCorpus.CorpusPin)
    (previous: string)
    =
    sprintf
        "The %s slot (opened by %s) re-stamps %s at kitVersion %s; the slot moved Core's stamps and this pin follows them. Previous pin %s (kitVersion %s)."
        version
        openedBy
        (prose families)
        version
        (old.Sha.Substring(0, 7))
        previous

let private jsonString = Regex("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled)

/// `copiesText` with the corpus pin's `sha`, `date` and `reason` replaced, or why not. The edit is
/// surgical — every byte outside the three values is kept — and proves itself: the result must parse
/// to the original with exactly those three members replaced.
let withPin (copiesText: string) (sha: string) (date: string) (reason: string) : Result<string, string> =
    let opening = Regex("\"" + SiblingCorpus.pinMember + "\"\\s*:\\s*\\{")
    let m = opening.Match copiesText

    if not m.Success then
        Error(sprintf "copies.json has no `%s` object" SiblingCorpus.pinMember)
    else
        // The record's end: the first `}` outside a JSON string.
        let start = m.Index + m.Length

        let rec close (i: int) =
            if i >= copiesText.Length then
                None
            else
                match copiesText[i] with
                | '}' -> Some i
                | '"' ->
                    let s = jsonString.Match(copiesText, i)

                    if s.Success && s.Index = i then
                        close (i + s.Length)
                    else
                        None
                | _ -> close (i + 1)

        match close start with
        | None -> Error(sprintf "copies.json's `%s` object does not close" SiblingCorpus.pinMember)
        | Some stop ->
            let block = copiesText.Substring(start, stop - start)

            let quoted (s: string) =
                Canonical.write Rendering.CrossHost (JStr s)

            let replaceMember (name: string) (value: string) (text: Result<string, string>) =
                text
                |> Result.bind (fun t ->
                    let re = Regex("(\"" + name + "\"\\s*:\\s*)\"(?:[^\"\\\\]|\\\\.)*\"")

                    match re.Matches(t).Count with
                    | 1 -> Ok(re.Replace(t, (fun (x: Match) -> x.Groups[1].Value + quoted value), 1))
                    | n ->
                        Error(
                            sprintf "copies.json's `%s.%s` appears %d times, not once" SiblingCorpus.pinMember name n
                        ))

            Ok block
            |> replaceMember "sha" sha
            |> replaceMember "date" date
            |> replaceMember "reason" reason
            |> Result.bind (fun edited ->
                let result = copiesText.Substring(0, start) + edited + copiesText.Substring stop

                let substituted =
                    match Json.parse copiesText with
                    | Ok(JObj top) ->
                        Ok(
                            JObj(
                                top
                                |> List.map (fun (k, v) ->
                                    match k, v with
                                    | key, JObj pin when key = SiblingCorpus.pinMember ->
                                        key,
                                        JObj(
                                            pin
                                            |> List.map (fun (pk, pv) ->
                                                match pk with
                                                | "sha" -> pk, JStr sha
                                                | "date" -> pk, JStr date
                                                | "reason" -> pk, JStr reason
                                                | _ -> pk, pv)
                                        )
                                    | _ -> k, v)
                            )
                        )
                    | Ok _ -> Error "copies.json is not a JSON object"
                    | Error e -> Error(sprintf "copies.json is not JSON (%s)" e)

                match substituted, Json.parse result with
                | Ok want, Ok got when want = got -> Ok result
                | Error e, _ -> Error e
                | _ ->
                    Error
                        "the edited copies.json differs from the original by more than the pin's sha, date and reason — refused")

// ---- reading the two checkouts -----------------------------------------------------------------

let private git (dir: string) (arguments: string list) : Result<string, string> =
    match ChildProcess.argv "git" arguments |> ChildProcess.inDir dir |> ChildProcess.run with
    | Error why -> Error why
    | Ok o when o.Exit <> 0 ->
        Error(sprintf "`git %s` exited %d: %s" (String.concat " " arguments) o.Exit ((o.Stderr + o.Stdout).Trim()))
    | Ok o -> Ok o.Stdout

/// The paths `git status` reports as changed, staged or untracked under `dir`, relative and
/// forward-slashed; with `paths`, only among them.
let private dirty (dir: string) (paths: string list) : Result<string list, string> =
    git dir ([ "status"; "--porcelain=v1"; "--untracked-files=all"; "--" ] @ paths)
    |> Result.map (fun out ->
        out.Replace("\r\n", "\n").Split('\n')
        |> Array.filter (fun l -> l.Length > 3)
        |> Array.collect (fun l -> l.Substring(3).Split(" -> ") |> Array.map (fun p -> p.Trim().Trim('"')))
        |> Array.distinct
        |> Array.toList)

let private relative (root: string) (path: string) =
    Path.GetRelativePath(root, path).Replace('\\', '/')

/// A law file's text with its `kitVersion` values blanked: two texts equal under it differ by the
/// stamp alone (the derived-lockstep case `docs/conformance-corpus.md` names).
let private unstamped (text: string) =
    Regex.Replace(text.Replace("\r\n", "\n"), "\"kitVersion\"\\s*:\\s*\"[^\"]*\"", "\"kitVersion\": \"\"")


let private errors (results: Result<'a, string list> list) : string list =
    results
    |> List.collect (function
        | Error es -> es
        | Ok _ -> [])

/// The request's own fields, read without touching either checkout.
let private checkInputs (r: Request) : Result<int * int * int, string list> =
    let version =
        match triple r.Version with
        | Some v -> Ok v
        | None -> Error [ sprintf "`%s` is not a version `X.Y.Z`" r.Version ]

    let date =
        match
            DateTime.TryParseExact(
                r.Date,
                "yyyy-MM-dd",
                Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.None
            )
        with
        | true, _ -> Ok()
        | _ -> Error [ sprintf "`%s` is not a date `yyyy-MM-dd`" r.Date ]

    let openedBy =
        if String.IsNullOrWhiteSpace r.OpenedBy || r.OpenedBy.Contains '\n' then
            Error [ "--opened-by names who opens the slot, on one line (`Phase 555`)" ]
        else
            Ok()

    match version, errors [ date; openedBy ] with
    | Ok v, [] -> Ok v
    | v, rest -> Error(errors [ v |> Result.map ignore ] @ rest)

/// The version against the tags and the standing `<Version>`: the standing version, which the slot
/// replaces, when `wanted` is the one version the stated class opens over the newest release tag.
let private checkVersion (r: Request) (wanted: int * int * int) : Result<string, string list> =
    let fail (why: string) = Error [ why ]

    match standingVersion (File.ReadAllText(Path.Combine(r.Root, "Directory.Build.props"))) with
    | Error e -> fail e
    | Ok standing ->
        match ChildProcess.Tags.released r.Root with
        | Error why -> fail (sprintf "the release tags could not be read (%s)" why)
        | Ok tags ->
            match ChildProcess.Tags.newest tags with
            | None -> fail "no release tag: a slot opens over a released version"
            | Some newestTag ->
                let newest = Option.get (ChildProcess.Tags.version newestTag)
                let (major, _, _) = newest

                if standing <> show newest then
                    fail (
                        sprintf
                            "the standing <Version> %s is not the newest release %s: a DRAFT slot stands, and a change rides it or advances it (STABILITY.md, \"Versioning policy\"); this act opens a slot over a released version only"
                            standing
                            newestTag
                    )
                elif not (List.contains wanted (candidatesOver newest)) then
                    fail (
                        sprintf
                            "%s is not the next %s over %s (%s)"
                            r.Version
                            (if major = 0 then
                                 "patch or minor"
                             else
                                 "patch, minor or major")
                            newestTag
                            (candidatesOver newest |> List.map show |> String.concat ", ")
                    )
                elif expectedOver r.Class newest <> wanted then
                    fail (
                        sprintf
                            "a %s slot over %s opens at %s, not %s: the version and the class you state disagree"
                            (className r.Class)
                            newestTag
                            (show (expectedOver r.Class newest))
                            r.Version
                    )
                elif File.Exists(Path.Combine(r.Root, Ledger.Dir, r.Version + ".md")) then
                    fail (sprintf "%s/%s.md already exists: the %s slot is open" Ledger.Dir r.Version r.Version)
                else
                    Ok standing

/// The corpus's laws manifest rows restamped from `files`, exactly as `LawVectorExport.writeFiles`
/// restamps them; `[]` where the checkout carries no manifest.
let private manifestWrite (corpus: string) (files: (string * string) list) =
    let manifest = LawVectorExport.manifestPath corpus

    if not (File.Exists manifest) then
        []
    else
        let rows =
            LawVectorExport.manifestRows
            |> List.choose (fun (id, name) ->
                files
                |> List.tryFind (fun (p, _) -> Path.GetFileName p = name)
                |> Option.bind (fun (_, text) ->
                    LawVectorExport.derivedMembers text
                    |> Result.toOption
                    |> Option.map (fun m -> id, m)))

        [ manifest, LawVectorExport.restampManifest (File.ReadAllText manifest) rows ]

/// Everything the act writes, once the version is admitted.
let private planWith (r: Request) (previous: string) : Result<Plan, string list> =
    let file (rel: string) = Path.Combine(r.Root, rel)
    let read (rel: string) = File.ReadAllText(file rel)

    match Approval.parseRegistry (read Approval.RegistryPath), SiblingCorpus.readPin (read "copies.json") with
    | Error es, _ -> Error(es |> List.map (sprintf "%s: %s" Approval.RegistryPath))
    | _, Error es -> Error es
    | Ok rows, Ok pin ->
        // The families whose files carry the version: the ones a slot re-stamps.
        let stamped =
            Exporters.tableWith (fun () -> []) |> List.filter (fun e -> e.FilesAt.IsSome)

        let realRoot = Repo.root ()

        let coreLaws =
            stamped
            |> List.collect (fun e ->
                let own = Path.Combine(r.Root, Path.GetRelativePath(realRoot, e.Root()))
                Exporters.filesFor rows (Some r.Version) e (Exporters.Own own))

        let corpusFiles =
            stamped
            |> List.map (fun e -> e, Exporters.filesFor rows (Some r.Version) e (Exporters.Elsewhere r.Corpus))

        let corpusDeclared =
            corpusFiles
            |> List.collect (fun (e, files) -> (files |> List.map fst) @ e.SharedIndexes r.Corpus)
            |> List.map (relative r.Corpus)
            |> List.distinct
            |> List.sort

        let manifests =
            corpusFiles
            |> List.collect (fun (e, files) ->
                if e.Family = LawVectorExport.familyDirName then
                    manifestWrite r.Corpus files
                else
                    [])

        let families =
            corpusFiles
            |> List.collect (fun (_, files) ->
                files
                |> List.map (fun (p, _) ->
                    let name = Path.GetFileName p

                    LawVectorExport.manifestRows
                    |> List.tryFind (fun (_, f) -> f = name)
                    |> Option.map fst
                    |> Option.defaultValue name))

        let corpusFaults =
            if not (Directory.Exists r.Corpus) then
                [ sprintf "--corpus '%s' does not exist" r.Corpus ]
            else
                [ match SiblingCorpus.driftAt pin.Sha r.Corpus with
                  | SiblingCorpus.AtPin _
                  | SiblingCorpus.Ahead _ -> ()
                  | SiblingCorpus.Unreadable why -> yield why
                  | drift ->
                      yield
                          sprintf
                              "the corpus at '%s' is not the pinned commit %s or a descendant of it (%A): a re-stamp on it would pin a corpus that drops what this repository certifies against"
                              r.Corpus
                              pin.Sha
                              drift

                  match dirty r.Corpus [] with
                  | Error why -> yield why
                  | Ok changed ->
                      let outside = changed |> List.filter (fun p -> not (List.contains p corpusDeclared))

                      if not outside.IsEmpty then
                          yield
                              sprintf
                                  "the corpus checkout carries uncommitted changes outside the declared set (%s): commit, stash or remove them first; the corpus commit this act makes carries the declared copies and nothing else"
                                  (String.concat ", " outside)

                  for manifest, restamp in manifests do
                      match restamp with
                      | Error why -> yield sprintf "%s (%s)" why manifest
                      | Ok _ -> ()

                  // A copy whose vectors differ from this kit's render is a CONTENT divergence: a change
                  // with a reason of its own, never a slot's re-stamp.
                  for _, files in corpusFiles do
                      for path, text in files do
                          if File.Exists path && unstamped (File.ReadAllText path) <> unstamped text then
                              yield
                                  sprintf
                                      "%s differs from this kit's render by more than its kitVersion stamp: re-copy it as a corpus change of its own (%s %s), then open the slot"
                                      (relative r.Corpus path)
                                      (Tool.render LawVectorExport.familyDirName)
                                      r.Corpus ]

        let slotRel = sprintf "%s/%s.md" Ledger.Dir r.Version
        let slot = slotText r.Version previous r.Class r.OpenedBy

        let index =
            let slots, _ = Ledger.read r.Root
            Ledger.renderIndex (read Ledger.IndexPath) (Ledger.parse r.Version slotRel slot :: slots)

        let coreWrites =
            [ file "Directory.Build.props", withVersion (read "Directory.Build.props") r.Version
              file slotRel, slot
              file Ledger.IndexPath, index ]
            @ coreLaws

        let coreFaults =
            match dirty r.Root ("copies.json" :: (coreWrites |> List.map (fst >> relative r.Root))) with
            | Error why -> [ why ]
            | Ok [] -> []
            | Ok changed ->
                [ sprintf
                      "this repository has uncommitted changes in files the act writes (%s): commit or stash them first"
                      (String.concat ", " changed) ]

        // The pin edit is proved on a placeholder commit now, so it cannot refuse after the corpus moved.
        let pinFaults =
            match withPin (read "copies.json") (String('0', 40)) r.Date "-" with
            | Ok _ -> []
            | Error why -> [ why ]

        match corpusFaults @ coreFaults @ pinFaults with
        | [] ->
            Ok
                { Request = r
                  Previous = previous
                  CorpusWrites =
                    (corpusFiles |> List.collect snd)
                    @ (manifests
                       |> List.choose (fun (p, t) -> t |> Result.toOption |> Option.map (fun text -> p, text)))
                  CorpusDeclared = corpusDeclared
                  Families = families
                  CoreWrites = coreWrites
                  CopiesText = read "copies.json"
                  OldPin = pin }
        | faults -> Error faults

/// The plan for `request`, or every reason the act refuses. Nothing is written.
let plan (request: Request) : Result<Plan, string list> =
    checkInputs request
    |> Result.bind (checkVersion request)
    |> Result.bind (planWith request)

/// The corpus commit's message, subject and body: the shape the `0.40.1`-`0.40.3` re-stamps were
/// committed with.
let corpusMessage (p: Plan) : string * string =
    let v = p.Request.Version

    sprintf "chore(laws): re-stamp %s at kitVersion %s (fuaran-core %s slot)" (prose p.Families) v v,
    sprintf
        "The fuaran-core %s slot moves the conformance kitVersion stamp. The declared copies, and the rows\nthe producer owns in their shared index, are re-stamped by the producer's own render\n(open-slot); no vector moves."
        v

/// Carry out `p`: the corpus first (its declared files, staged by name, and its commit when any of
/// them moved), then this repository's files and the pin naming the corpus HEAD that carries them.
let apply (p: Plan) : Result<Outcome, string> =
    let r = p.Request

    for path, text in p.CorpusWrites do
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        File.WriteAllText(path, text)

    let committed =
        match dirty r.Corpus p.CorpusDeclared with
        | Error why -> Error why
        | Ok [] -> Ok(None, [])
        | Ok changed ->
            let subject, body = corpusMessage p

            git r.Corpus ([ "add"; "--" ] @ changed)
            |> Result.bind (fun _ -> git r.Corpus [ "diff"; "--cached"; "--name-only" ])
            |> Result.bind (fun staged ->
                let staged =
                    staged.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.map _.Trim()
                    |> Array.sort
                    |> Array.toList

                if staged <> List.sort changed then
                    Error(
                        sprintf
                            "the corpus index holds %s, not exactly the declared files that moved (%s): nothing was committed"
                            (String.concat ", " staged)
                            (String.concat ", " changed)
                    )
                else
                    git r.Corpus [ "commit"; "-m"; subject; "-m"; body ])
            |> Result.bind (fun _ -> git r.Corpus [ "rev-parse"; "HEAD" ])
            |> Result.map (fun sha -> Some(sha.Trim()), changed)

    committed
    |> Result.bind (fun (commit, files) ->
        git r.Corpus [ "rev-parse"; "HEAD" ]
        |> Result.bind (fun head ->
            let head = head.Trim()
            let reason = pinReason r.Version r.OpenedBy p.Families p.OldPin p.Previous

            withPin p.CopiesText head r.Date reason
            |> Result.map (fun copies ->
                for path, text in p.CoreWrites do
                    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
                    File.WriteAllText(path, text)

                File.WriteAllText(Path.Combine(r.Root, "copies.json"), copies)

                { CorpusCommit = commit
                  CorpusHead = head
                  CorpusFiles = files
                  CoreFiles =
                    (p.CoreWrites |> List.map (fst >> relative r.Root)) @ [ "copies.json" ]
                    |> List.distinct })))

/// What the caller does next: the corpus commit pushed FIRST, then this repository's commit.
let nextSteps (p: Plan) (o: Outcome) : string list =
    let r = p.Request

    [ match o.CorpusCommit with
      | Some sha -> sprintf "corpus commit %s carries %s" (sha.Substring(0, 7)) (String.concat ", " o.CorpusFiles)
      | None ->
          sprintf
              "the corpus HEAD %s already carries the re-stamp; no corpus commit was made"
              (o.CorpusHead.Substring(0, 7))
      sprintf "the corpus pin names %s; written here: %s" o.CorpusHead (String.concat ", " o.CoreFiles)
      "the two commits, the corpus FIRST (a pin naming a commit only one clone holds fails every CI checkout):"
      sprintf "  1. git -C \"%s\" push" r.Corpus
      sprintf
          "  2. git -C \"%s\" add %s; git -C \"%s\" commit -m \"release(core): open the %s slot\"; git -C \"%s\" push"
          r.Root
          (String.concat " " o.CoreFiles)
          r.Root
          r.Version
          r.Root ]
