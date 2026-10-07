module Fuaran.Core.Tests.ReadmeClaimsTests

// ---------------------------------------------------------------------------
// Phase 254 — a README claim of a surface names the version it arrived in, and the version is
// DERIVED, not written.
//
// The README describes the head of this repository, and a reader holds a released package. Between
// a release and the next, every surface a draft adds is described in the README as present while
// the package the reader restored lacks it — `SampleAdequacy.cases`, `RequiredParamsNull` and
// `ParityVectors` were each read that way by a downstream consumer pinned below the slot that added
// them, and the only remedy available to it was reading STABILITY.md's version headings by hand.
//
// So every surface the README names in code type carries the version it arrived in, and that
// version is computed rather than typed:
//
//   * The INDEX of a release is the committed public-surface baselines (`api/*.txt`) at its tag —
//     the gate holds those files to the built assemblies, so at the tag they ARE the release's
//     public surface. The first release tag that carries them is the FLOOR: a surface already
//     present there shipped "at the floor or earlier", which is all this repository can say from
//     evidence, and the README says it once instead of on every name.
//   * A CLAIM is a code span whose leading identifier path resolves in the CURRENT baselines —
//     qualified (`Tree.wellFormed`), capitalised (`KeyedWitness`) or bare (`evalWith`), read as the
//     span's leading name, so `Tree.traversal nodew keyw` claims `Tree.traversal`. A span naming
//     nothing current (`DataFrame`, which moved to another repository) is not one. The cost of reading
//     bare names is that a code span which is a WORD rather than a member — a wire tag such as `int`,
//     a column header — is read as one when some member shares its spelling; the README writes those
//     as string literals (`"int"`) or in prose, which is also the more accurate spelling for them. The
//     approve run shows each such reading as a stamp in the diff, so it is met before it is committed.
//   * A claim's ARRIVAL is the first release whose index resolves it. One that no release resolves is
//     in the standing `<Version>`, which must then be a DRAFT: untagged, and headed
//     `## <Version> — DRAFT` in STABILITY.md. That is the stamp a reader holding the released package
//     needs most, and it is the one the gate holds hardest.
//   * The STAMP follows the claim's first occurrence in each section: ` (since `0.31.0`)`, or
//     ` (since `0.34.0`, unreleased)` for a draft arrival. Nothing is stamped at or below the floor.
//
// The committed README is held to that rendering, and `CORE_APPROVE_README=1` writes it — the idiom
// of the API baselines and of `proofs/README.md`'s generated blocks. A clone with no git, or no
// release tag carrying baselines, cannot derive anything; it skips by name rather than passing.
//
// The README's "which Fable version" line is held here too: its compiler version is the one the
// receiving Fable gate pins in its tool manifest (Phase 217 moved that gate out of this
// repository), compared whenever that checkout is present, and its `Fable.Core` version is this
// repository's own pin, compared always.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

// ---- the surface index -----------------------------------------------------

let private indexedKinds =
    set [ "type"; "method"; "property"; "field"; "record-field"; "union-case" ]

/// The F# name one baseline line declares — namespace-qualified, generic arity dropped, nested types
/// dotted, a union case's `New` constructor prefix removed — or `None` for a header, a constructor, a
/// compiler-generated `Tags` member or a line of another shape.
let internal declaredName (line: string) : string option =
    let line = line.TrimEnd('\r')
    let sp = line.IndexOf ' '

    if sp <= 0 || not (indexedKinds.Contains(line.Substring(0, sp))) then
        None
    else
        let kind = line.Substring(0, sp)
        let rest = line.Substring(sp + 1)
        let stop = rest.IndexOfAny [| '('; ' ' |]
        let raw = if stop < 0 then rest else rest.Substring(0, stop)

        if raw.Contains("+Tags", StringComparison.Ordinal) then
            None
        else
            let segs = Regex.Replace(raw, @"`\d+", "").Replace('+', '.').Split('.')
            let last = segs[segs.Length - 1]
            // A case with fields is compiled as `New<Case>(...)`; a nullary one keeps its name.
            let withFields = Regex.IsMatch(rest, @"#\d+\([^)]")

            if
                kind = "union-case"
                && withFields
                && last.StartsWith("New", StringComparison.Ordinal)
            then
                segs[segs.Length - 1] <- last.Substring 3

            Some(String.Join(".", segs))

/// Every spelling a reader may use for `qualified`: each trailing run of its segments, with and
/// without the `Module` suffix F# adds to a module that shares a type's name (`ArbitrationModule` is
/// written `Arbitration`).
let internal keysOf (qualified: string) : string list =
    let segs = qualified.Split('.') |> Array.toList

    let strip (s: string) =
        if s.Length > 6 && s.EndsWith("Module", StringComparison.Ordinal) then
            s.Substring(0, s.Length - 6)
        else
            s

    [ for i in 0 .. segs.Length - 1 do
          let suffix = List.skip i segs
          yield String.Join(".", suffix)
          yield String.Join(".", List.map strip suffix) ]
    |> List.distinct

/// The names a set of baseline lines makes resolvable.
let internal indexOf (lines: string seq) : Set<string> =
    lines |> Seq.choose declaredName |> Seq.collect keysOf |> Set.ofSeq

// ---- claims and stamps -------------------------------------------------------

let private identRe =
    Regex(@"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*", RegexOptions.Compiled)

/// The surface name a code span could claim — its leading identifier path, when that is the whole span
/// or is followed by an argument, a type argument or a parenthesis (`Tree.traversal nodew keyw`,
/// `Deferred<'T>`). Whether it IS a claim is the index's question.
let internal candidateOfSpan (span: string) : string option =
    let m = identRe.Match span

    if not m.Success then
        None
    else
        let name = m.Value
        let rest = span.Substring name.Length

        let whole = rest = "" || Char.IsWhiteSpace rest[0] || rest[0] = '(' || rest[0] = '<'

        if whole then Some name else None

/// When a claim arrived, as the README states it.
type internal Arrival =
    /// At the floor release or before it — stated once, not per name.
    | AtFloor
    /// In a tagged release after the floor.
    | Released of version: string
    /// In the standing `<Version>`, which no tag has released.
    | Unreleased of version: string

/// The stamp text that follows a claim, or `""`.
let internal stampText (a: Arrival) : string =
    match a with
    | AtFloor -> ""
    | Released v -> sprintf " (since `%s`)" v
    | Unreleased v -> sprintf " (since `%s`, unreleased)" v

let private stampRe =
    Regex(@" \(since `\d+\.\d+\.\d+`(?:, unreleased)?\)", RegexOptions.Compiled)

let private spanRe = Regex(@"`([^`\n]+)`", RegexOptions.Compiled)

/// Markers around the README's generated statement of the floor.
let floorBegin =
    "<!-- surface-versions:begin — generated by ReadmeClaimsTests from api/ at each release tag; CORE_APPROVE_README=1 rewrites it -->"

let floorEnd = "<!-- surface-versions:end -->"

/// The statement between the markers.
let internal floorStatement (floor: string) : string =
    sprintf
        "**Each surface named in code type below carries the version it arrived in** — `(since 0.31.0)`, or `(since 0.34.0, unreleased)` for one only the draft at the head of [`STABILITY.md`](STABILITY.md) has — and a surface shown without one shipped in `%s` or earlier, the first release whose public surface this repository records under `api/`. The versions are derived from those records at each release tag, not written by hand, and the suite holds them."
        floor

/// The README as the derivation says it must read: every stamp removed, then one re-inserted after the
/// first occurrence of each claim in each section, and the floor statement re-rendered. Fenced code
/// is left alone, and so is the floor block's own text. `Error` names what could not be located.
let internal stampReadme (arrival: string -> Arrival option) (floor: string) (readme: string) : Result<string, string> =
    let lines = readme.Replace("\r\n", "\n").Split('\n')
    let out = Collections.Generic.List<string>()
    let mutable inFence = false
    let mutable inFloor = false
    let mutable floorSeen = false
    let mutable seen = Set.empty<string>

    for line in lines do
        if line.TrimStart().StartsWith("```", StringComparison.Ordinal) then
            inFence <- not inFence
            out.Add line
        elif inFence then
            out.Add line
        elif line = floorBegin then
            inFloor <- true
            floorSeen <- true
            out.Add line
            out.Add(floorStatement floor)
        elif line = floorEnd then
            inFloor <- false
            out.Add line
        elif inFloor then
            () // re-rendered above
        else
            if line.StartsWith("#", StringComparison.Ordinal) then
                seen <- Set.empty

            let bare = stampRe.Replace(line, "")

            let stamped =
                spanRe.Replace(
                    bare,
                    MatchEvaluator(fun m ->
                        match candidateOfSpan m.Groups[1].Value with
                        | Some name when not (seen.Contains name) ->
                            match arrival name with
                            | Some a ->
                                seen <- seen.Add name
                                m.Value + stampText a
                            | None -> m.Value
                        | _ -> m.Value)
                )

            out.Add stamped

    if not floorSeen then
        Error(sprintf "the README carries no '%s' marker line" floorBegin)
    elif inFloor then
        Error(sprintf "the README's floor block is not closed by '%s'" floorEnd)
    else
        Ok(String.Join("\n", out))

/// Every stamp the README carries, as `(claim, stamp text)`, in file order — for the draft-heading
/// check, which reads the README as committed.
let internal stampsOf (readme: string) : (string * string) list =
    Regex.Matches(readme, @"`([^`\n]+)`( \(since `(\d+\.\d+\.\d+)`(, unreleased)?\))")
    |> Seq.map (fun m -> m.Groups[1].Value, m.Groups[2].Value)
    |> Seq.toList

// ---- the release history ------------------------------------------------------

/// `(version, index)` for every release tag whose tree carries the baselines, oldest first.
let private releaseIndexes (root: string) : Result<(string * Set<string>) list, string> =
    ChildProcess.git root "tag --list"
    |> Result.bind (fun out ->
        let tags =
            out.Split('\n')
            |> Array.map (fun t -> t.Trim())
            |> Array.choose (fun t -> PackageRosterTests.releaseTagVersion t |> Option.map (fun v -> v, t))
            |> Array.sortBy fst
            |> Array.toList

        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | ((ma, mi, pa), tag) :: rest ->
                match ChildProcess.git root (sprintf "ls-tree --name-only %s api/" tag) with
                | Error e -> Error e
                | Ok listing ->
                    let hasBaselines =
                        listing.Split('\n')
                        |> Array.exists (fun p ->
                            let p = p.Trim()

                            p.StartsWith("api/", StringComparison.Ordinal)
                            && p.EndsWith(".txt", StringComparison.Ordinal))

                    if not hasBaselines then
                        go acc rest
                    else
                        match ChildProcess.git root (sprintf "grep -h -e . %s -- \":(glob)api/*.txt\"" tag) with
                        | Error e -> Error e
                        | Ok text -> go ((sprintf "%d.%d.%d" ma mi pa, indexOf (text.Split('\n'))) :: acc) rest

        go [] tags)

let private currentIndex (root: string) : Set<string> =
    Directory.GetFiles(Path.Combine(root, "api"), "*.txt")
    |> Seq.collect File.ReadAllLines
    |> indexOf

/// The arrival function the derivation stamps with: the first release resolving a name, else the
/// standing version, unreleased — `None` for a span that names nothing current. A surface no release
/// resolves while the standing version IS tagged (baselines moved after a tag without `<Version>`
/// advancing) is stamped unreleased all the same, and `stampFaults` refuses it, because its heading is
/// not a draft's.
let internal arrivalOf
    (releases: (string * Set<string>) list)
    (current: Set<string>)
    (standing: string)
    (name: string)
    : Arrival option =
    if not (current.Contains name) then
        None
    else
        match releases |> List.tryFindIndex (fun (_, idx) -> idx.Contains name) with
        | Some 0 -> Some AtFloor
        | Some i -> Some(Released(fst releases[i]))
        | None -> Some(Unreleased standing)

/// What is wrong with the README's stamps against STABILITY.md: an unreleased stamp must name the
/// standing `<Version>` and that version must be headed `## <v> — DRAFT`; a released stamp must name
/// a version some entry header names.
let internal stampFaults (standing: string option) (stability: string) (readme: string) : string list =
    let headings = stability.Replace("\r\n", "\n").Split('\n')

    let draftHeading (v: string) =
        headings |> Array.exists (fun l -> l.Trim() = sprintf "## %s — DRAFT" v)

    stampsOf readme
    |> List.choose (fun (claim, stamp) ->
        let m = Regex.Match(stamp, @"`(\d+\.\d+\.\d+)`(, unreleased)?")
        let v = m.Groups[1].Value

        if m.Groups[2].Success then
            if Some v <> standing then
                Some(sprintf "`%s` is stamped unreleased in %s, which is not the standing <Version>" claim v)
            elif not (draftHeading v) then
                Some(
                    sprintf
                        "`%s` is stamped unreleased in %s, and STABILITY.md has no '## %s — DRAFT' heading"
                        claim
                        v
                        v
                )
            else
                None
        elif (PackageRosterTests.entryHeaderNaming v stability).IsNone then
            Some(sprintf "`%s` is stamped as arriving in %s, which no STABILITY.md entry header names" claim v)
        else
            None)

let private repoRoot () : string =
    Path.GetDirectoryName(Snapshots.repoFile "Fuaran.Core.slnx")

let private readmePath () = Snapshots.repoFile "README.md"

let private firstDiff (a: string) (b: string) : string =
    let xs = a.Replace("\r\n", "\n").Split('\n')
    let ys = b.Replace("\r\n", "\n").Split('\n')

    let i =
        Seq.zip xs ys
        |> Seq.tryFindIndex (fun (x, y) -> x <> y)
        |> Option.defaultValue (min xs.Length ys.Length)

    sprintf
        "first differing line %d:\n  committed: %s\n  derived:   %s"
        (i + 1)
        (if i < xs.Length then xs[i] else "<end>")
        (if i < ys.Length then ys[i] else "<end>")

// ---- the Fable line -------------------------------------------------------------

let private fableLineRe =
    Regex(
        @"Fable compiler `(?<compiler>\d+\.\d+\.\d+)`.{0,400}?`Fable\.Core` `(?<core>[\d.]+)`",
        RegexOptions.Compiled ||| RegexOptions.Singleline
    )

/// `(compiler, Fable.Core)` as the README's "which Fable version" line states them.
let internal fableLine (readme: string) : (string * string) option =
    let m = fableLineRe.Match readme

    if m.Success then
        Some(m.Groups["compiler"].Value, m.Groups["core"].Value)
    else
        None

/// The `fable` tool version a `dotnet-tools.json` manifest pins.
let internal fableToolPin (manifest: string) : string option =
    try
        use doc = JsonDocument.Parse manifest

        match doc.RootElement.TryGetProperty "tools" with
        | true, tools ->
            match tools.TryGetProperty "fable" with
            | true, f ->
                match f.TryGetProperty "version" with
                | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
                | _ -> None
            | _ -> None
        | _ -> None
    with _ ->
        None

/// The `Fable.Core` version `Directory.Packages.props` pins.
let internal fableCorePin (packagesProps: string) : string option =
    let m =
        Regex.Match(packagesProps, @"<PackageVersion\s+Include=""Fable\.Core""\s+Version=""([^""]+)""")

    if m.Success then Some m.Groups[1].Value else None

/// The receiving gate's checkout names this variable when it is not beside this repository.
[<Literal>]
let receivingDirVariable = "FUARAN_DOTNET_DIR"

/// The receiving gate's tool manifest, if its checkout is present: the variable first, then beside
/// this repository's MAIN working tree (a linked worktree is anchored where its main tree is, as the
/// corpus resolver does) under the language's own directory or directly.
let private receivingManifest () : string option =
    let fromVar =
        match Environment.GetEnvironmentVariable receivingDirVariable with
        | null
        | "" -> []
        | d -> [ d ]

    let besides =
        match SiblingCorpus.mainWorkingTreeFrom (repoRoot ()) with
        | Error _ -> []
        | Ok main ->
            let rec ups (dir: DirectoryInfo) n acc =
                if isNull dir || n = 0 then
                    List.rev acc
                else
                    ups dir.Parent (n - 1) (dir.FullName :: acc)

            ups (DirectoryInfo main) 6 []
            |> List.collect (fun d ->
                [ Path.Combine(d, "Fuaran-UI", "fuaran-dotnet")
                  Path.Combine(d, "fuaran-dotnet") ])

    fromVar @ besides
    |> List.map (fun d -> Path.Combine(d, ".config", "dotnet-tools.json"))
    |> List.tryFind File.Exists

// ---- the suite -----------------------------------------------------------------

[<Tests>]
let tests =
    testList
        "ReadmeClaims"
        [ test
              "every README claim of a surface carries the version it arrived in (CORE_APPROVE_README=1 regenerates them)" {
              let root = repoRoot ()

              match releaseIndexes root with
              | Error why -> skiptest ("the release history could not be read, so no arrival can be derived: " + why)
              | Ok [] -> skiptest "no release tag carries api/ baselines in this clone, so no arrival can be derived"
              | Ok releases ->
                  let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

                  let standing =
                      PackageRosterTests.standingVersion props
                      |> Option.defaultWith (fun () -> failtest "Directory.Build.props carries no <Version>")

                  let current = currentIndex root
                  let readme = File.ReadAllText(readmePath ())
                  let floor = fst releases.Head

                  Approval.validate Approval.Readme

                  match stampReadme (arrivalOf releases current standing) floor readme with
                  | Error why -> failtestf "the README's generated parts could not be located: %s" why
                  | Ok derived when derived = readme.Replace("\r\n", "\n") -> ()
                  | Ok derived when Approval.admits Approval.Readme Approval.Files.Readme ->
                      Approval.write Approval.Readme Approval.Files.Readme (readmePath ()) derived
                      |> ignore
                  | Ok derived ->
                      failtestf
                          "README.md's version stamps are not the derivation's — %s\nRe-run with CORE_APPROVE_README=1 and commit the README."
                          (firstDiff readme derived)
          }

          test
              "a stamp naming an unreleased version names the DRAFT heading in STABILITY.md, and a released one an entry" {
              let root = repoRoot ()
              let readme = File.ReadAllText(readmePath ())
              let stability = File.ReadAllText(Path.Combine(root, "STABILITY.md"))
              let props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"))

              Expect.isEmpty
                  (stampFaults (PackageRosterTests.standingVersion props) stability readme)
                  "a README stamp must name a version STABILITY.md heads as what it is"
          }

          test
              "the stamp check refuses an unreleased stamp without a DRAFT heading, off the standing version, and a released one without an entry" {
              let stability =
                  "## 0.34.0 — DRAFT\n\n## 0.33.0 — released 2026-10-01 as `v0.33.0`\n"

              let ok = "`A.b` (since `0.34.0`, unreleased) and `C.d` (since `0.33.0`)"

              Expect.isEmpty
                  (stampFaults (Some "0.34.0") stability ok)
                  "draft and released stamps over their headings pass"

              Expect.isNonEmpty
                  (stampFaults (Some "0.34.0") (stability.Replace("— DRAFT", "— released")) ok)
                  "an unreleased stamp over a released heading is refused"

              Expect.isNonEmpty
                  (stampFaults (Some "0.35.0") stability ok)
                  "an unreleased stamp naming a version other than the standing one is refused"

              Expect.isNonEmpty
                  (stampFaults (Some "0.34.0") stability "`C.d` (since `0.32.0`)")
                  "a released stamp no entry header names is refused"
          }

          test
              "the stamp derivation stamps once per section, skips fences and the floor, and refuses a README without the floor block" {
              let arrival name =
                  match name with
                  | "Tree.old" -> Some AtFloor
                  | "Tree.mid" -> Some(Released "0.31.0")
                  | "Tree.fresh"
                  | "Fresh" -> Some(Unreleased "0.34.0")
                  | _ -> None

              let readme =
                  String.concat
                      "\n"
                      [ "# T"
                        floorBegin
                        "stale text"
                        floorEnd
                        "`Tree.old` and `Tree.mid` (since `0.29.0`) and `Tree.mid` again, `Tree.fresh`, `prose`."
                        "```"
                        "`Tree.mid` in a fence"
                        "```"
                        "## Next"
                        "`Tree.mid` again in a new section, and `Fresh<'T>`, `Tree.fresh nodew` and `Unknown.name`." ]

              match stampReadme arrival "0.27.0" readme with
              | Error why -> failtestf "derivation refused a well-formed README: %s" why
              | Ok derived ->
                  let ls = derived.Split('\n')
                  Expect.equal ls[2] (floorStatement "0.27.0") "the floor statement is re-rendered"

                  Expect.equal
                      ls[4]
                      "`Tree.old` and `Tree.mid` (since `0.31.0`) and `Tree.mid` again, `Tree.fresh` (since `0.34.0`, unreleased), `prose`."
                      "a wrong stamp is replaced, a repeat is not stamped, a floor arrival carries none, prose is not a claim"

                  Expect.equal ls[6] "`Tree.mid` in a fence" "fenced code is untouched"

                  Expect.equal
                      ls[9]
                      "`Tree.mid` (since `0.31.0`) again in a new section, and `Fresh<'T>` (since `0.34.0`, unreleased), `Tree.fresh nodew` (since `0.34.0`, unreleased) and `Unknown.name`."
                      "a new section stamps again; a type argument and a call keep the claim; an unknown name is not one"

              Expect.isError
                  (stampReadme arrival "0.27.0" "# T\n`Tree.mid`")
                  "a README without the floor block is refused"
          }

          test "the surface index reads the baseline line shapes it is fed" {
              let lines =
                  [ "# a header line"
                    "type Fuaran.Core.Tree+WellFormed`1 (union)"
                    "method Fuaran.Core.ArbitrationModule.arbitrate`2(x) : y"
                    "union-case Fuaran.Core.Deferred`1.NewReady #1(Item: !0)"
                    "union-case Fuaran.Core.Deferred`1.Pending #0()"
                    "record-field Fuaran.Core.KeyedWitness`2.IdsUnique #4 : z"
                    "field Fuaran.Core.Deferred`1+Tags.Ready : System.Int32 (literal)"
                    "ctor Fuaran.Core.IdWitness`1..ctor(a)" ]

              let idx = indexOf lines

              for k in
                  [ "Tree.WellFormed"
                    "Fuaran.Core.Tree.WellFormed"
                    "Arbitration.arbitrate"
                    "ArbitrationModule.arbitrate"
                    "Deferred.Ready"
                    "Ready"
                    "Pending"
                    "KeyedWitness.IdsUnique" ] do
                  Expect.isTrue (idx.Contains k) (sprintf "'%s' resolves" k)

              for k in [ "NewReady"; "Tags"; "IdWitness"; "Tags.Ready" ] do
                  Expect.isFalse (idx.Contains k) (sprintf "'%s' does not resolve" k)
          }

          test "a claim candidate is the span's leading name, and a literal, a path or a version is none" {
              Expect.equal
                  (candidateOfSpan "Tree.traversal nodew keyw")
                  (Some "Tree.traversal")
                  "a call claims its function"

              Expect.equal (candidateOfSpan "Deferred<'T>") (Some "Deferred") "a type argument keeps the claim"
              Expect.equal (candidateOfSpan "KeyedWitness") (Some "KeyedWitness") "a capitalised name is a claim"

              Expect.equal
                  (candidateOfSpan "evalWith")
                  (Some "evalWith")
                  "a bare name is a candidate; the index decides"

              Expect.equal (candidateOfSpan "\"int\"") None "a string literal is not a name"
              Expect.equal (candidateOfSpan "a-b") None "a hyphenated word is not a name"
              Expect.equal (candidateOfSpan "proofs/Chain.fst") None "a path is not a name"
              Expect.equal (candidateOfSpan "0.31.0") None "a version is not a name"
          }

          test "the README names the Fable versions the receiving gate and this repository pin" {
              let root = repoRoot ()
              let readme = File.ReadAllText(readmePath ())

              match fableLine readme with
              | None ->
                  failtest
                      "README.md states no \"which Fable version\" line (\"… Fable compiler `X.Y.Z` … `Fable.Core` `A.B` …\")"
              | Some(compiler, core) ->
                  let ownPin =
                      fableCorePin (File.ReadAllText(Path.Combine(root, "Directory.Packages.props")))

                  Expect.equal (Some core) ownPin "the README's Fable.Core version is Directory.Packages.props' pin"

                  match receivingManifest () with
                  | None ->
                      skiptest (
                          sprintf
                              "the receiving Fable gate's checkout was not found (set %s to it), so the README's compiler version %s was compared with nothing"
                              receivingDirVariable
                              compiler
                      )
                  | Some manifest ->
                      Expect.equal
                          (fableToolPin (File.ReadAllText manifest))
                          (Some compiler)
                          (sprintf "the README's Fable compiler version is the receiving gate's pin (%s)" manifest)
          }

          test "the Fable readers find the line and the pins, and refuse what is not there" {
              Expect.equal
                  (fableLine "built with the Fable compiler `5.0.0`, against `Fable.Core` `5.0`.")
                  (Some("5.0.0", "5.0"))
                  "the line is read"

              Expect.isNone (fableLine "Fable is supported.") "a line without versions is not one"

              Expect.equal
                  (fableToolPin """{"version":1,"tools":{"fable":{"version":"5.0.0","commands":["fable"]}}}""")
                  (Some "5.0.0")
                  "the manifest pin is read"

              Expect.isNone (fableToolPin """{"tools":{"fantomas":{"version":"7.0.5"}}}""") "no fable tool, no pin"

              Expect.equal
                  (fableCorePin """<PackageVersion Include="Fable.Core" Version="5.0" />""")
                  (Some "5.0")
                  "the package pin is read"
          } ]
