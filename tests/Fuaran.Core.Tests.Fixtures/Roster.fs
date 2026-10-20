/// What ships (Phase 532; the derivation since Phase 185): the packable projects under `src/`, derived
/// by the ONE rule — the project's `IsPackable`, else `Directory.Build.props`'s, else MSBuild's default.
/// `PackageRosterTests` holds the README to it and holds `.github/scripts/probe-registry.ps1`, which must
/// run with no build and so keeps its own copy of the rule, to the same set; every family that asks
/// "which packages" reads `Roster.derive`.
module Fuaran.Core.Tests.Roster

open System
open System.IO
open System.Text.RegularExpressions

/// A project under `src/` that `pack` would produce a package from.
type PackableProject =
    {
        /// The package id — `<PackageId>` where declared, else the project file's base name.
        PackageId: string
        /// Repo-relative, forward-slashed.
        ProjectFile: string
    }

let private isPackableRe =
    Regex(@"<IsPackable>([^<]*)</IsPackable>", RegexOptions.Compiled)

let private packageIdRe =
    Regex(@"<PackageId>([^<]*)</PackageId>", RegexOptions.Compiled)

let internal firstGroup (re: Regex) (text: string) : string option =
    let m = re.Match text

    if m.Success then
        let v = m.Groups[1].Value.Trim()
        if v = "" then None else Some v
    else
        None

/// `IsPackable` for one project: what the project declares, else what
/// `Directory.Build.props` declares, else MSBuild's own default (true). Only the literal
/// `false` excludes — anything else, including a value this reader does not understand,
/// leaves the project IN the roster, so a misread widens the table rather than silently
/// dropping a shipping package from it.
let isPackable (fallback: string option) (projectText: string) : bool =
    match firstGroup isPackableRe projectText |> Option.orElse fallback with
    | Some v -> not (String.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
    | None -> true

/// What `Directory.Build.props` under `root` declares for `IsPackable`, the fallback a project that
/// declares nothing inherits.
let propsFallback (root: string) : string option =
    let props = Path.Combine(root, "Directory.Build.props")

    if File.Exists props then
        firstGroup isPackableRe (File.ReadAllText props)
    else
        None

/// THE derivation, kept in one small function on purpose: the Fable surface's completeness
/// check (Phase 185) derives the same set, and two spellings of "what ships" would drift
/// exactly the way the documents this file gates drifted. `src/*/*.fsproj` +
/// `src/*/*.csproj` — a C# project is a published package too (the C# facade was one, from
/// Phase 128 until Phase 231 removed it at `0.33.0`), and a roster scoped to F# would leave one
/// undocumented by construction, which is the same excusal-by-the-shape-of-the-derivation that
/// 185's own header names. Checked against that sibling: it covers every project type under
/// `src/` and names a packable C# project as an explicit exclusion, so the two rosters range
/// over one set.
let derive (root: string) : PackableProject list =
    let srcDir = Path.Combine(root, "src")
    let fallback = propsFallback root

    if not (Directory.Exists srcDir) then
        []
    else
        Directory.GetDirectories srcDir
        |> Array.collect (fun d -> Array.append (Directory.GetFiles(d, "*.fsproj")) (Directory.GetFiles(d, "*.csproj")))
        |> Array.toList
        |> List.choose (fun path ->
            let text = File.ReadAllText path

            if isPackable fallback text then
                Some
                    { PackageId =
                        firstGroup packageIdRe text
                        |> Option.defaultValue (Path.GetFileNameWithoutExtension path)
                      ProjectFile = Path.GetRelativePath(root, path).Replace('\\', '/') }
            else
                None)
        |> List.sortBy _.PackageId
