module Fuaran.Core.Tests.OneDotZeroTests

// The `OneDotZero` family (Phase 386): what "1.0" promises, as a gate output rather than a
// ceremony. It reads the major of `<Version>` from `Directory.Build.props` and holds four laws
// to it:
//
//   1. no public member of a shipped assembly carries `System.ObsoleteAttribute` — a forward
//      leaves at a major, and a major freezes with none standing;
//   2. `Conformance.unfrozenWitnesses` is empty — every public witness record is frozen;
//   3. `Conformance.frozenWitnessFields` is byte-equal to the list the major's `vN.0.0` tag
//      carries — a frozen witness does not grow within a major;
//   4. no baseline under `api/` has moved by a breaking class (`removal`, `retype`, any
//      `*-widening`) since the newest tag unless the major advanced past that tag's.
//
// At major 0 every law is VACUOUS and says so by name, with what it would have asserted — never
// a silent pass: before 1.0 a breaking move rides a minor (STABILITY.md "Versioning policy").
// From major 1 each is live. A law whose reference does not exist yet (no `vN.0.0` tag in this
// clone) is vacuous by name too, and the line says which tag it waited for.
//
// Each law is a pure function over its inputs, so the go-red plants below hand it a planted bad
// input at major 1 and watch it fail; the live test reads the real inputs.
//
// Law 3 reads the list the tag carries from `frozen-witness-fields.txt` beside this file, the
// committed render of `frozenWitnessFields`. The render is held to the live list at every major
// (the companion test below), so the file at the tag IS the list at the tag.

open System
open System.IO
open System.Reflection
open Expecto
open Fuaran.Core

/// What one `OneDotZero` law said. `Vacuous` is evidence, not a pass: it names the law and why
/// it asserted nothing.
type internal Verdict =
    | Held of evidence: string
    | Vacuous of why: string
    | Broken of detail: string

/// The four laws, by the names their verdict lines carry.
[<Literal>]
let internal NoObsoleteLaw = "OneDotZero.noObsoleteMembers"

[<Literal>]
let internal AllFrozenLaw = "OneDotZero.everyWitnessFrozen"

[<Literal>]
let internal FrozenAtTagLaw = "OneDotZero.frozenFieldsAsTagged"

[<Literal>]
let internal NoBreakWithinMajorLaw = "OneDotZero.noBreakingMoveWithinAMajor"

/// The major of a `MAJOR.MINOR.PATCH[-pre]` version, `None` when it does not parse.
let internal majorOf (version: string) : int option =
    match version.Trim().Split('.') with
    | parts when parts.Length >= 1 ->
        match Int32.TryParse parts[0] with
        | true, m when m >= 0 -> Some m
        | _ -> None
    | _ -> None

/// The major of a `vX.Y.Z` tag.
let internal tagMajor (tag: string) : int option =
    if tag.StartsWith("v", StringComparison.Ordinal) then
        majorOf (tag.Substring 1)
    else
        None

let private atMajorZero (law: string) (wouldHave: string) : Verdict =
    Vacuous(sprintf "%s is vacuous at major 0: %s" law wouldHave)

// ---- law 1: no obsolete member ------------------------------------------------------------

/// Every externally visible type or member of `asm` that carries `System.ObsoleteAttribute`, as
/// `Type` or `Type.member` — the assembly read by reflection, never a hand list.
let internal obsoleteMembers (asm: Assembly) : string list =
    let obsolete (m: MemberInfo) =
        m.GetCustomAttributes(typeof<ObsoleteAttribute>, false).Length > 0

    let flags =
        BindingFlags.Public
        ||| BindingFlags.Static
        ||| BindingFlags.Instance
        ||| BindingFlags.DeclaredOnly

    [ for t in asm.GetExportedTypes() do
          if obsolete t then
              yield t.FullName

          for m in t.GetMembers flags do
              if obsolete m then
                  yield t.FullName + "." + m.Name ]
    |> List.distinct
    |> List.sort

/// Law 1 over the offenders found in `assemblies` assemblies.
let internal noObsoleteLaw (major: int) (assemblies: int) (offenders: string list) : Verdict =
    if major = 0 then
        atMajorZero
            NoObsoleteLaw
            (sprintf
                "an obsolete forward may stand until the next major (%d assembl(ies) read, %d obsolete member(s) standing)"
                assemblies
                offenders.Length)
    elif assemblies = 0 then
        Broken(
            sprintf
                "%s read no assembly — a law that read nothing must not read as one that found nothing"
                NoObsoleteLaw
        )
    elif List.isEmpty offenders then
        Held(sprintf "%s: %d assembl(ies) read, no obsolete member" NoObsoleteLaw assemblies)
    else
        Broken(
            sprintf
                "%s: %d obsolete member(s) at major %d — a forward leaves at a major (STABILITY.md \"Versioning policy\"):\n       %s"
                NoObsoleteLaw
                offenders.Length
                major
                (String.concat "\n       " offenders)
        )

// ---- law 2: every witness frozen ----------------------------------------------------------

/// Law 2 over `Conformance.unfrozenWitnesses`.
let internal allFrozenLaw (major: int) (unfrozen: FreezeExemption list) : Verdict =
    if major = 0 then
        atMajorZero
            AllFrozenLaw
            (sprintf "a witness record may stand outside the freeze until 1.0 (%d standing)" unfrozen.Length)
    elif List.isEmpty unfrozen then
        Held(sprintf "%s: every public witness record is frozen" AllFrozenLaw)
    else
        Broken(
            sprintf
                "%s: %d witness record(s) stand outside the freeze at major %d:\n       %s"
                AllFrozenLaw
                unfrozen.Length
                major
                (unfrozen
                 |> List.map (fun u -> u.Witness + " — " + u.Reason)
                 |> String.concat "\n       ")
        )

// ---- law 3: the frozen field lists as the major's tag carries them ------------------------

/// The committed render of `frozenWitnessFields`: one line per record, `Name: f1, f2`, in the
/// list's order, LF-terminated.
let internal renderFrozen (pins: WitnessFields list) : string =
    pins
    |> List.map (fun pin -> pin.Record + ": " + String.concat ", " pin.Fields + "\n")
    |> String.concat ""

/// What the major's `vN.0.0` tag answered for the committed render.
type internal TagRead =
    /// The tag is not in this clone (not cut yet, or a clone fetched without tags).
    | NoTag of tag: string
    /// The tag's copy of the render.
    | Tagged of tag: string * text: string
    /// The tag exists and its copy could not be read.
    | Unreadable of tag: string * why: string

/// Law 3: the render at HEAD against the render the major's tag carries.
let internal frozenAtTagLaw (major: int) (live: string) (atTag: TagRead) : Verdict =
    if major = 0 then
        atMajorZero FrozenAtTagLaw "a frozen witness may be widened by a named act until 1.0"
    else
        match atTag with
        | NoTag tag ->
            Vacuous(
                sprintf
                    "%s is vacuous: %s is not in this clone, so there is no tagged list to compare with yet — it is compared from that tag on"
                    FrozenAtTagLaw
                    tag
            )
        | Unreadable(tag, why) ->
            Broken(sprintf "%s: %s exists and its frozen-field list could not be read: %s" FrozenAtTagLaw tag why)
        | Tagged(tag, text) when String.Equals(text, live, StringComparison.Ordinal) ->
            Held(sprintf "%s: the frozen field lists are byte-equal to %s's" FrozenAtTagLaw tag)
        | Tagged(tag, text) ->
            Broken(
                sprintf
                    "%s: a frozen witness moved since %s — within a major a frozen witness does not grow; compose a new witness that embeds it.\n       at %s:\n%s\n       at HEAD:\n%s"
                    FrozenAtTagLaw
                    tag
                    tag
                    text
                    live
            )

// ---- law 4: no breaking surface move within a major ----------------------------------------

/// Law 4: `breaking` names each package whose baseline moved by a breaking class since `newest`.
let internal noBreakWithinMajorLaw (major: int) (newest: string option) (breaking: string list) : Verdict =
    if major = 0 then
        atMajorZero
            NoBreakWithinMajorLaw
            (sprintf
                "before 1.0 a breaking move rides a minor (%d package(s) moved by a breaking class since %s)"
                breaking.Length
                (newest |> Option.defaultValue "the newest tag"))
    else
        match newest with
        | None ->
            Vacuous(
                sprintf
                    "%s is vacuous: this clone holds no `vX.Y.Z` tag, so there is no published surface to compare with"
                    NoBreakWithinMajorLaw
            )
        | Some tag ->
            match tagMajor tag with
            | None -> Broken(sprintf "%s: the newest tag %s does not parse" NoBreakWithinMajorLaw tag)
            | Some m when m < major ->
                Held(
                    sprintf
                        "%s: the major advanced from %s to %d, which pays for the %d package(s) moved by a breaking class"
                        NoBreakWithinMajorLaw
                        tag
                        major
                        breaking.Length
                )
            | Some _ when List.isEmpty breaking ->
                Held(sprintf "%s: no baseline moved by a breaking class since %s" NoBreakWithinMajorLaw tag)
            | Some _ ->
                Broken(
                    sprintf
                        "%s: %d package(s) moved by a breaking class since %s without a major advance — advance <Version>'s major, or make the change additive:\n       %s"
                        NoBreakWithinMajorLaw
                        breaking.Length
                        tag
                        (String.concat "\n       " breaking)
                )

// ---- the live inputs ----------------------------------------------------------------------

let private repoRoot () : string = Snapshots.repoFile ""

let private renderPath () : string =
    Path.Combine(repoRoot (), "tests", "Fuaran.Core.Tests", "frozen-witness-fields.txt")

[<Literal>]
let private RenderRepoPath = "tests/Fuaran.Core.Tests/frozen-witness-fields.txt"

let private standingMajor () : string * int =
    let props = File.ReadAllText(Path.Combine(repoRoot (), "Directory.Build.props"))

    match PackageRosterTests.standingVersion props with
    | None -> failtest "Directory.Build.props declares no <Version>"
    | Some v ->
        match majorOf v with
        | Some m -> v, m
        | None -> failtestf "<Version> %s does not parse as MAJOR.MINOR.PATCH" v

let private tags () : Result<string list, string> =
    ChildProcess.git (repoRoot ()) "tag --list"
    |> Result.map (fun out ->
        out.Split('\n')
        |> Array.toList
        |> List.map _.Trim()
        |> List.filter (fun l -> l <> ""))

/// The four verdicts over the live tree, in law order.
let private liveVerdicts () : string * (string * Verdict) list =
    let version, major = standingMajor ()

    let ids = PackageRosterTests.packableProjects (repoRoot ()) |> List.map _.PackageId

    let assemblies = ids |> List.map (fun id -> Assembly.Load(AssemblyName id))
    let offenders = assemblies |> List.collect obsoleteMembers

    let tagList =
        match tags () with
        | Ok ts -> ts
        | Error _ -> []

    let majorTag = sprintf "v%d.0.0" major

    let atTag =
        if not (List.contains majorTag tagList) then
            NoTag majorTag
        else
            match ChildProcess.git (repoRoot ()) (sprintf "show %s:%s" majorTag RenderRepoPath) with
            | Ok text -> Tagged(majorTag, text.Replace("\r\n", "\n"))
            | Error why -> Unreadable(majorTag, why)

    let newest = PublicSurfaceTests.newestVersionTag tagList

    let breaking =
        match newest with
        | None -> []
        | Some tag ->
            PublicSurfaceTests.sinceTag (repoRoot ()) tag
            |> List.choose (fun r ->
                match r.Moves with
                | Some moves ->
                    match PublicSurfaceTests.headline moves with
                    | Some c when PublicSurfaceTests.isBreaking c ->
                        Some(sprintf "%s — %s (%d move(s))" r.PackageId (PublicSurfaceTests.className c) moves.Length)
                    | _ -> None
                | None -> None)

    version,
    [ NoObsoleteLaw, noObsoleteLaw major assemblies.Length offenders
      AllFrozenLaw, allFrozenLaw major Conformance.unfrozenWitnesses
      FrozenAtTagLaw, frozenAtTagLaw major (renderFrozen Conformance.frozenWitnessFields) atTag
      NoBreakWithinMajorLaw, noBreakWithinMajorLaw major newest breaking ]

// ---- the go-red plants' decoys --------------------------------------------------------------

/// A decoy carrying an obsolete member, for law 1's plant: the scan must find it in this assembly.
type OneDotZeroObsoleteDecoy() =
    [<Obsolete("a planted forward — the OneDotZero go-red plant reads it")>]
    member _.Forward = 1

[<Tests>]
let tests =
    testList
        "OneDotZero"
        [ test "the four laws over the live tree: vacuous by name at major 0, asserted from major 1" {
              let version, verdicts = liveVerdicts ()
              let _, major = standingMajor ()

              printfn ""
              printfn "==== OneDotZero at <Version> %s (major %d)" version major

              for law, v in verdicts do
                  match v with
                  | Held e -> printfn "  HELD     %s" e
                  | Vacuous why -> printfn "  VACUOUS  %s" why
                  | Broken d -> printfn "  BROKEN   %s" d

              Expect.equal
                  (verdicts |> List.map fst)
                  [ NoObsoleteLaw; AllFrozenLaw; FrozenAtTagLaw; NoBreakWithinMajorLaw ]
                  "all four laws ran"

              for law, v in verdicts do
                  match v with
                  | Broken d -> failtest d
                  | Vacuous why ->
                      Expect.stringContains why law "a vacuous verdict names its law"

                      if major = 0 then
                          Expect.stringContains why "vacuous at major 0" "at major 0 the vacuity says why"
                  | Held _ ->
                      if major = 0 then
                          failtestf "%s claims to have held at major 0, where it asserts nothing" law
          }

          test "the committed render of frozenWitnessFields is the live list" {
              // Held at every major, so the file a tag carries IS the list at that tag — the
              // reference law 3 compares with.
              let path = renderPath ()
              let live = renderFrozen Conformance.frozenWitnessFields

              Expect.isTrue
                  (File.Exists path)
                  (sprintf "%s exists — write it with the render of Conformance.frozenWitnessFields" RenderRepoPath)

              let committed = File.ReadAllText(path).Replace("\r\n", "\n")

              Expect.equal
                  committed
                  live
                  (sprintf
                      "%s is the render of Conformance.frozenWitnessFields. A frozen witness changed: before 1.0 that is a named widening (STABILITY.md \"Witness-record field freeze\") and this file moves with it; from 1.0 it does not move at all."
                      RenderRepoPath)
          }

          test "go-red: law 1 finds a planted obsolete member and is red on it at major 1" {
              let found = obsoleteMembers (Assembly.GetExecutingAssembly())

              Expect.contains
                  found
                  (typeof<OneDotZeroObsoleteDecoy>.FullName + ".Forward")
                  "the reflection scan finds the planted forward"

              match noObsoleteLaw 1 1 found with
              | Broken d -> Expect.stringContains d "OneDotZeroObsoleteDecoy" "the red names the member"
              | v -> failtestf "a planted obsolete member at major 1 must be red, got %A" v

              match noObsoleteLaw 1 0 [] with
              | Broken _ -> ()
              | v -> failtestf "a scan that read no assembly must be red, got %A" v

              match noObsoleteLaw 0 1 found with
              | Vacuous why -> Expect.stringContains why NoObsoleteLaw "vacuous by name at major 0"
              | v -> failtestf "at major 0 the law is vacuous, got %A" v
          }

          test "go-red: law 2 is red on a planted unfrozen witness at major 1" {
              match
                  allFrozenLaw
                      1
                      [ { Witness = "DecoyWitness"
                          Reason = "planted" } ]
              with
              | Broken d -> Expect.stringContains d "DecoyWitness" "the red names the record"
              | v -> failtestf "a planted unfrozen witness at major 1 must be red, got %A" v

              match allFrozenLaw 1 [] with
              | Held _ -> ()
              | v -> failtestf "an empty list at major 1 holds, got %A" v
          }

          test "go-red: law 3 is red on a planted widening against the tag, vacuous with no tag" {
              let tagged =
                  renderFrozen
                      [ { Record = "NodeWitness"
                          Fields = [ "Id"; "KindTag"; "Children"; "ReplaceChildren" ] } ]

              let widened =
                  renderFrozen
                      [ { Record = "NodeWitness"
                          Fields = [ "Id"; "KindTag"; "Children"; "ReplaceChildren"; "Meta" ] } ]

              match frozenAtTagLaw 1 widened (Tagged("v1.0.0", tagged)) with
              | Broken d -> Expect.stringContains d "Meta" "the red shows the widened list"
              | v -> failtestf "a planted widening at major 1 must be red, got %A" v

              match frozenAtTagLaw 1 tagged (Tagged("v1.0.0", tagged)) with
              | Held _ -> ()
              | v -> failtestf "an unchanged list holds, got %A" v

              match frozenAtTagLaw 1 tagged (NoTag "v1.0.0") with
              | Vacuous why -> Expect.stringContains why "v1.0.0" "the vacuity names the tag it waits for"
              | v -> failtestf "with no tag the law is vacuous by name, got %A" v

              match frozenAtTagLaw 1 tagged (Unreadable("v1.0.0", "planted")) with
              | Broken _ -> ()
              | v -> failtestf "a tag whose list cannot be read is red, got %A" v
          }

          test "go-red: law 4 is red on a planted breaking move within a major, held across one" {
              let moved = [ "Fuaran.Core.Decoy — removal (1 move(s))" ]

              match noBreakWithinMajorLaw 1 (Some "v1.0.0") moved with
              | Broken d -> Expect.stringContains d "Fuaran.Core.Decoy" "the red names the package"
              | v -> failtestf "a breaking move within major 1 must be red, got %A" v

              match noBreakWithinMajorLaw 1 (Some "v0.36.0") moved with
              | Held e -> Expect.stringContains e "advanced" "the advance pays for the move"
              | v -> failtestf "a breaking move across a major advance holds, got %A" v

              match noBreakWithinMajorLaw 2 (Some "v1.4.0") moved with
              | Held _ -> ()
              | v -> failtestf "a later major advance pays too, got %A" v

              match noBreakWithinMajorLaw 1 (Some "v1.0.0") [] with
              | Held _ -> ()
              | v -> failtestf "no breaking move holds, got %A" v
          }

          test "the major reader" {
              Expect.equal (majorOf "0.36.0") (Some 0) "a 0.x version"
              Expect.equal (majorOf "1.0.0") (Some 1) "1.0.0"
              Expect.equal (majorOf "12.3.4-rc.1") (Some 12) "a pre-release"
              Expect.equal (majorOf "x.1.0") None "a non-number"
              Expect.equal (tagMajor "v1.2.3") (Some 1) "a tag"
              Expect.equal (tagMajor "1.2.3") None "a tag without its v"
          } ]
