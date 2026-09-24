module Fuaran.Core.Tests.FableSmokeCompletenessTests

// ---------------------------------------------------------------------------
// Phase 185 — the Fable surface's MEMBERSHIP is derived, not remembered.
// Phase 217 — and the Fable COMPILER is not in this repository.
//
// STABILITY.md's claim is "Fable-clean on encode AND decode" for every public
// package. Which packages that claim covers was once a hand-maintained
// `<ProjectReference>` list in an in-repo Fable smoke beside a hand-written
// exclusion COMMENT, and the one thing such a list cannot notice is the package
// nobody added to it: measured by Phase 185, `Fuaran.Core.Column.Ops` and
// `Fuaran.Core.Idl.Cli` were in neither.
//
// Phase 217 moved the compile and the value-parity run to the consumer that owns
// a Fable toolchain (STABILITY.md "Fable cleanliness" names it), because the
// compiler belongs in applications and in the language's .NET implementation,
// not in the substrate. What stays HERE is the half that needs no Fable: the
// DECLARATION of the surface. A packable project either ships the `fable/`
// source distribution (the `Directory.Build.props` convention — which is what a
// Fable consumer actually compiles, and so is the act of making the claim) or it
// carries an entry in `fable-exclusions.json` (package, reason, the phase that
// decided). A new packable project is a FAILING TEST NAMING IT at the moment it
// is added, rather than a silent hole in a published claim.
//
// Three things about the checks below are deliberate:
//
//  * They run in BOTH directions. A packable project in neither set fails, AND an
//    exclusion that has gone stale fails — one naming a project that no longer
//    exists, one that stopped being packable, and one whose project SHIPS the
//    `fable/` sources anyway, so that the package makes the claim its entry denies.
//
//  * The derivation covers every project type under `src/`, not just `.fsproj`.
//    A C# project cannot ship F# sources for Fable, so the temptation is to
//    filter it out — but a packable project excused BY THE SHAPE OF THE
//    DERIVATION is excused with no reason on the record. `Fuaran.Core.CSharp` is
//    therefore an ordinary exclusions entry.
//
//  * "No Fable compiler here" is CHECKED, not asserted (217.F): no script or
//    workflow in the repository invokes `dotnet fable`, the tool manifest does not
//    carry it, and the only authored `Fable.Core` reference is the one library
//    reference sanctioned by DECISIONS.md — `Fuaran.Core.Wire`'s, which powers
//    its own `#if FABLE_COMPILER` float-layout arm and is the FIX for the recorded
//    `Json.render` defect, not tooling.
// ---------------------------------------------------------------------------

open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Expecto

/// A project under `src/`, as this file reads it off disk.
type private SrcProject =
    {
        Name: string
        File: string
        Packable: bool
        /// Ships its F# sources under `fable/` — an `.fsproj` that does not `Content Remove` them.
        ShipsFableSources: bool
    }

/// One `fable-exclusions.json` entry.
type private Exclusion =
    { Package: string
      Reason: string
      Phase: string }

let private root = Snapshots.repoFile ""

let private srcDir = Snapshots.repoFile "src"

let private exclusionsFile = Snapshots.repoFile ("fable-exclusions.json")

let private buildPropsFile = Snapshots.repoFile "Directory.Build.props"

/// The one `<IsPackable>` reading, so the project-level answer and the repository
/// default answer cannot come apart. Matches the PROPERTY element only — the
/// `Condition="'$(IsPackable)' == 'true'"` attribute in `Directory.Build.props` is an
/// ItemGroup guard, not a declaration, and must not be read as one.
let private declaredIsPackable (xml: string) : bool option =
    let m = Regex.Match(xml, @"<IsPackable\s*>\s*([^<]*)\s*</IsPackable\s*>")

    if m.Success then
        Some(m.Groups[1].Value.Trim().ToLowerInvariant() = "true")
    else
        None

/// The opt-out from the `Directory.Build.props` source distribution — the element the two IDL
/// generation projects carry. Read as the ELEMENT, so a comment that mentions it does not count.
let private removesFableSources (xml: string) : bool =
    Regex.IsMatch(xml, @"<Content\s+Remove=""\*\.fs""\s*/>")

/// THE DERIVATION, in one place: every project directly under `src/`, with whether it is packable
/// and whether it ships the `fable/` sources. A project's own `.?sproj` declaration wins; absent,
/// the repository default from `Directory.Build.props` applies; absent there too, the SDK's own
/// default, which is `true`. Deliberately NOT filtered by project type — see the header.
let private packableProjects () : SrcProject list =
    let repoDefault =
        declaredIsPackable (File.ReadAllText buildPropsFile) |> Option.defaultValue true

    Directory.GetDirectories srcDir
    |> Array.toList
    |> List.collect (fun dir ->
        Directory.GetFiles(dir, "*sproj")
        |> Array.toList
        |> List.map (fun file ->
            let xml = File.ReadAllText file
            let packable = declaredIsPackable xml |> Option.defaultValue repoDefault

            { Name = Path.GetFileNameWithoutExtension file
              File = file
              Packable = packable
              ShipsFableSources = packable && file.EndsWith ".fsproj" && not (removesFableSources xml) }))
    |> List.filter _.Packable
    |> List.sortBy _.Name

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

/// Every repository file of the given extensions, outside build output and dependency folders.
let private repoFiles (extensions: string list) : string list =
    let skipped = [ "bin"; "obj"; "node_modules"; ".git"; "out" ]

    let rec walk (dir: string) : string list =
        let here =
            Directory.GetFiles dir
            |> Array.toList
            |> List.filter (fun f -> extensions |> List.exists (fun e -> f.EndsWith e))

        let below =
            Directory.GetDirectories dir
            |> Array.toList
            |> List.filter (fun d -> not (skipped |> List.contains (Path.GetFileName d)))
            |> List.collect walk

        here @ below

    walk root

/// A `dotnet fable` INVOCATION: the words on a line that is not a comment. Comments explaining why
/// the compiler is not here are allowed to name it.
let private invokesFable (line: string) : bool =
    let t = line.TrimStart()

    not (t.StartsWith "#" || t.StartsWith "//" || t.StartsWith "<!--")
    && Regex.IsMatch(t, @"\bdotnet\s+fable\b")

let private fableCoreRefRe =
    Regex(@"<PackageReference\s+Include=""Fable\.Core""", RegexOptions.Compiled)

/// The one sanctioned authored `Fable.Core` reference (DECISIONS.md, Phase 217).
let private sanctionedFableCoreRef =
    Path.Combine("src", "Fuaran.Core.Wire", "Fuaran.Core.Wire.fsproj")

[<Tests>]
let tests =
    testList
        "FableSmokeCompleteness"
        [ test "every packable project ships the fable/ sources or is excluded with a reason" {
              let excluded = exclusions () |> List.map _.Package |> Set.ofList

              let uncovered =
                  packableProjects ()
                  |> List.filter (fun p -> not (p.ShipsFableSources || excluded.Contains p.Name))
                  |> List.map _.Name

              Expect.equal
                  uncovered
                  []
                  ("These packable projects neither ship the fable/ source distribution nor appear in "
                   + "fable-exclusions.json. A package that ships cannot make STABILITY.md's Fable-clean "
                   + "claim without one or the other: ship the sources (the Directory.Build.props default) "
                   + "or add an exclusions entry saying why not.")
          }

          test "the derivation is not vacuous — it sees projects, it filters, and it tells the two kinds apart" {
              // Each half matters. An empty packable set would make the check above pass over nothing
              // (a wrong `src` path reads exactly like a clean repository); a filter that excludes
              // nothing has never been shown to work; and a source-distribution reading that said
              // "ships" of everything would make every exclusion look stale, or of nothing, every
              // package look excluded.
              let all = Directory.GetDirectories srcDir |> Array.length
              let packable = packableProjects ()

              Expect.isGreaterThan packable.Length 10 "the packable set should hold most of src/"
              Expect.isLessThan packable.Length all "at least one project under src/ declares IsPackable=false"

              Expect.isTrue (packable |> List.exists _.ShipsFableSources) "most packages ship the fable/ sources"

              Expect.isTrue
                  (packable
                   |> List.exists (fun p -> p.File.EndsWith ".fsproj" && not p.ShipsFableSources))
                  "the two IDL generation projects opt out of the source distribution, and the reading sees it"
          }

          test "every exclusion is well formed" {
              let entries = exclusions ()

              Expect.isNonEmpty entries "fable-exclusions.json holds at least one entry"

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
              // The direction a one-way check misses. An entry survives its own reason three ways, and
              // all three are silent: the project was deleted, it stopped being packable (so it is no
              // longer a claim anyone can make), or it SHIPS the fable/ sources after all, so every
              // Fable consumer compiles a package whose entry says it makes no portability promise.
              let packable = packableProjects ()
              let byName = packable |> List.map (fun p -> p.Name, p) |> Map.ofList

              let onDisk =
                  Directory.GetDirectories srcDir |> Array.map Path.GetFileName |> Set.ofArray

              let stale =
                  exclusions ()
                  |> List.choose (fun e ->
                      if not (onDisk.Contains e.Package) then
                          Some(e.Package + " — no such project under src/")
                      else
                          match byName.TryFind e.Package with
                          | None -> Some(e.Package + " — not packable, so it needs no exclusion")
                          | Some p when p.ShipsFableSources ->
                              Some(e.Package + " — it ships the fable/ sources, which is the claim; drop the entry")
                          | Some _ -> None)

              Expect.equal stale [] "every fable-exclusions.json entry still describes the tree it sits in"
          }

          // ---- 217.F — no Fable compiler in this repository, checked ----------------------

          test "no script or workflow in this repository invokes dotnet fable" {
              let offenders =
                  repoFiles [ ".ps1"; ".sh"; ".yml"; ".yaml"; ".cmd" ]
                  |> List.collect (fun f ->
                      File.ReadAllLines f
                      |> Array.toList
                      |> List.indexed
                      |> List.filter (snd >> invokesFable)
                      |> List.map (fun (i, _) -> sprintf "%s:%d" (Path.GetRelativePath(root, f)) (i + 1)))

              Expect.equal
                  offenders
                  []
                  "the Fable compiler runs in the consumer that owns the toolchain (STABILITY.md \"Fable cleanliness\"), never here"
          }

          test "the tool manifest does not carry the Fable compiler" {
              let manifest = Snapshots.repoFile (Path.Combine(".config", "dotnet-tools.json"))
              use doc = JsonDocument.Parse(File.ReadAllText manifest)

              let tools =
                  match doc.RootElement.TryGetProperty "tools" with
                  | true, t -> t.EnumerateObject() |> Seq.map _.Name |> Seq.toList
                  | _ -> []

              Expect.isNonEmpty tools "the manifest was read (it carries fantomas at least)"
              Expect.isFalse (tools |> List.contains "fable") "no `fable` local tool"
          }

          test "the only authored Fable.Core reference is the sanctioned library one" {
              // A TRANSITIVE Fable.Core is a restore artefact and not a breach; the line is an
              // AUTHORED reference. Wire's is sanctioned by DECISIONS.md: it powers Wire's own
              // `#if FABLE_COMPILER` float-layout emit, which is the fix for `Json.render` throwing
              // under Fable, and a Fable consumer of Wire needs it to compile that arm at all.
              let authored =
                  repoFiles [ "proj"; ".props"; ".targets" ]
                  |> List.filter (fun f -> fableCoreRefRe.IsMatch(File.ReadAllText f))
                  |> List.map (fun f -> Path.GetRelativePath(root, f))

              Expect.equal
                  authored
                  [ sanctionedFableCoreRef ]
                  "Fable.Core is referenced by Fuaran.Core.Wire alone — no test, probe or gate project carries it"
          }

          test "the invocation reading is not vacuous — it sees an invocation and passes a comment" {
              Expect.isTrue (invokesFable "dotnet fable x.fsproj -o out") "a bare invocation is one"
              Expect.isTrue (invokesFable "    dotnet  fable watch app") "indented, with a verb, is one"
              Expect.isFalse (invokesFable "# `dotnet fable` does not run here") "a comment is not"
              Expect.isFalse (invokesFable "dotnet fantomas --check src") "another tool is not"
          } ]
