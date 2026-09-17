module Fuaran.Core.Tests.FableSmokeCompletenessTests

// ---------------------------------------------------------------------------
// Phase 185 — the Fable smoke's MEMBERSHIP is derived, not remembered.
//
// STABILITY.md's claim is "Fable-clean on encode AND decode" for every public
// package, and `tests/fable-smoke` is what makes that checkable in-repo rather
// than downstream. But which packages the smoke reaches was a hand-maintained
// `<ProjectReference>` list beside a hand-written exclusion COMMENT, and the one
// thing a hand-maintained list cannot notice is the package nobody added to it.
// Measured while writing this: two packable projects were in neither —
// `Fuaran.Core.Column.Ops` (whose own Description ends "Fable-clean") and
// `Fuaran.Core.Idl.Cli` — so the blanket claim had two exceptions nobody had
// proved or recorded. The same shape as Phase 97's, recurring for the same
// reason.
//
// So the exclusions are DATA (`tests/fable-smoke/exclusions.json`: package,
// reason, the phase that decided) and this file holds the packable set to the
// union of the smoke's references and that file. A new packable project is then
// a FAILING TEST NAMING IT, at the moment it is added, rather than a silent hole
// in a published claim.
//
// Two things about the checks below are deliberate and worth stating, because
// each answers a way this could go quietly vacuous:
//
//  * They run in BOTH directions. A packable project covered by neither fails
//    (the hole this phase closed), AND an exclusion entry that has gone stale
//    fails — one naming a project that no longer exists, one that stopped being
//    packable, and, the interesting case, one whose project has SINCE JOINED the
//    smoke. Without that second direction a "reason" could outlive the fact it
//    describes, which is how the comment this file replaces came to say that
//    `Observer` was a build-time tool.
//
//  * The derivation covers every project type under `src/`, not just `.fsproj`.
//    A C# project genuinely cannot be Fable-compiled, so the temptation is to
//    filter it out of the question — but that is precisely the escape hatch this
//    phase exists to remove: a packable project excused BY THE SHAPE OF THE
//    DERIVATION is excused with no reason on the record and no test that would
//    ever mention it. `Fuaran.Core.CSharp` is therefore an ordinary exclusions
//    entry carrying the reason STABILITY.md already gives for it.
// ---------------------------------------------------------------------------

open System.IO
open System.Text.Json
open Expecto

/// A project under `src/`, as this file reads it off disk.
type private SrcProject =
    { Name: string
      File: string
      Packable: bool }

/// One `exclusions.json` entry.
type private Exclusion =
    { Package: string
      Reason: string
      Phase: string }

let private srcDir = Snapshots.repoFile "src"

let private smokeProjectFile =
    Snapshots.repoFile (Path.Combine("tests", "fable-smoke", "FableSmoke.fsproj"))

let private exclusionsFile =
    Snapshots.repoFile (Path.Combine("tests", "fable-smoke", "exclusions.json"))

let private buildPropsFile = Snapshots.repoFile "Directory.Build.props"

/// The one `<IsPackable>` reading, so the project-level answer and the repository
/// default answer cannot come apart. Matches the PROPERTY element only — the
/// `Condition="'$(IsPackable)' == 'true'"` attribute in `Directory.Build.props` is an
/// ItemGroup guard, not a declaration, and must not be read as one.
let private declaredIsPackable (xml: string) : bool option =
    let m =
        System.Text.RegularExpressions.Regex.Match(xml, @"<IsPackable\s*>\s*([^<]*)\s*</IsPackable\s*>")

    if m.Success then
        Some(m.Groups[1].Value.Trim().ToLowerInvariant() = "true")
    else
        None

/// THE DERIVATION, in one place: every project directly under `src/`, with whether it
/// is packable. A project's own `.?sproj` declaration wins; absent, the repository
/// default from `Directory.Build.props` applies; absent there too, the SDK's own
/// default, which is `true`. Deliberately NOT filtered by project type — see the
/// header.
let private packableProjects () : SrcProject list =
    let repoDefault =
        declaredIsPackable (File.ReadAllText buildPropsFile) |> Option.defaultValue true

    Directory.GetDirectories srcDir
    |> Array.toList
    |> List.collect (fun dir ->
        Directory.GetFiles(dir, "*sproj")
        |> Array.toList
        |> List.map (fun file ->
            { Name = Path.GetFileNameWithoutExtension file
              File = file
              Packable = declaredIsPackable (File.ReadAllText file) |> Option.defaultValue repoDefault }))
    |> List.filter _.Packable
    |> List.sortBy _.Name

/// The projects the smoke compiles — read off its `<ProjectReference>` elements, which
/// is what actually decides what Fable pulls into the compilation.
let private smokeReferences () : string list =
    System.Text.RegularExpressions.Regex.Matches(
        File.ReadAllText smokeProjectFile,
        @"<ProjectReference\s+Include=""([^""]+)"""
    )
    |> Seq.map (fun m -> Path.GetFileNameWithoutExtension(m.Groups[1].Value.Replace('\\', '/')))
    |> Seq.toList
    |> List.sort

let private exclusions () : Exclusion list =
    use doc = JsonDocument.Parse(File.ReadAllText exclusionsFile)

    doc.RootElement.EnumerateArray()
    |> Seq.map (fun el ->
        let get name =
            match el.TryGetProperty(name: string) with
            | true, v -> v.GetString()
            | _ -> null

        { Package = get "package"
          Reason = get "reason"
          Phase = get "phase" })
    |> Seq.toList

[<Tests>]
let tests =
    testList
        "FableSmokeCompleteness"
        [ test "every packable project is compiled by the Fable smoke or excluded with a reason" {
              let referenced = smokeReferences () |> Set.ofList
              let excluded = exclusions () |> List.map _.Package |> Set.ofList

              let uncovered =
                  packableProjects ()
                  |> List.map _.Name
                  |> List.filter (fun n -> not (referenced.Contains n || excluded.Contains n))

              Expect.equal
                  uncovered
                  []
                  ("These packable projects are neither referenced by tests/fable-smoke/FableSmoke.fsproj "
                   + "nor listed in tests/fable-smoke/exclusions.json. A package that ships cannot make "
                   + "STABILITY.md's Fable-clean claim without one or the other: add the ProjectReference "
                   + "and a touch in the smoke's Program.fs, or add an exclusions.json entry saying why not.")
          }

          test "the derivation is not vacuous — it sees projects, and it filters" {
              // Both halves matter. An empty packable set would make the check above pass over
              // nothing at all (a wrong `src` path reads exactly like a clean repository), and a
              // filter that excludes nothing has never been shown to work.
              let all = Directory.GetDirectories srcDir |> Array.length
              let packable = packableProjects () |> List.length

              Expect.isGreaterThan packable 10 "the packable set should hold most of src/"
              Expect.isLessThan packable all "at least one project under src/ declares IsPackable=false"
          }

          test "the smoke's project references all resolve" {
              // A typo in a `<ProjectReference>` Include would make the check above pass for the
              // wrong reason: the name it reads off would cover nothing, and the real project would
              // report as uncovered rather than as misspelled.
              let names = packableProjects () |> List.map _.Name |> Set.ofList

              let dangling =
                  smokeReferences ()
                  |> List.filter (fun n ->
                      not (names.Contains n)
                      && not (File.Exists(Path.Combine(srcDir, n, n + ".fsproj"))))

              Expect.equal dangling [] "every <ProjectReference> in FableSmoke.fsproj names a project under src/"
          }

          test "every exclusion is well formed" {
              let entries = exclusions ()

              Expect.isNonEmpty entries "exclusions.json holds at least one entry"

              let malformed =
                  entries
                  |> List.filter (fun e ->
                      System.String.IsNullOrWhiteSpace e.Package
                      || System.String.IsNullOrWhiteSpace e.Reason
                      || System.String.IsNullOrWhiteSpace e.Phase)
                  |> List.map _.Package

              Expect.equal malformed [] "each entry carries a package, a reason and the phase that decided it"

              let duplicated =
                  entries |> List.countBy _.Package |> List.filter (snd >> (<) 1) |> List.map fst

              Expect.equal duplicated [] "no package is listed twice"
          }

          test "no exclusion has gone stale" {
              // The direction a one-way check misses. An entry survives its own reason three ways,
              // and all three are silent: the project was deleted, it stopped being packable (so it
              // is no longer a claim anyone can make), or — the one that actually happens — it JOINED
              // the smoke and the entry now contradicts the gate standing beside it.
              let packable = packableProjects () |> List.map _.Name |> Set.ofList
              let referenced = smokeReferences () |> Set.ofList

              let onDisk =
                  Directory.GetDirectories srcDir |> Array.map Path.GetFileName |> Set.ofArray

              let stale =
                  exclusions ()
                  |> List.choose (fun e ->
                      if not (onDisk.Contains e.Package) then
                          Some(e.Package + " — no such project under src/")
                      elif not (packable.Contains e.Package) then
                          Some(e.Package + " — not packable, so it needs no exclusion")
                      elif referenced.Contains e.Package then
                          Some(e.Package + " — the smoke compiles it now; drop the entry")
                      else
                          None)

              Expect.equal stale [] "every exclusions.json entry still describes the tree it sits in"
          } ]
