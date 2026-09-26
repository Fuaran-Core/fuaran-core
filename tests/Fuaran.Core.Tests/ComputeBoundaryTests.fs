/// Phase 257 — the compute boundary, held; Phase 258 — held as an ABSENCE.
///
/// DECISIONS.md D66 rules that `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` are produced by
/// a repository of their own (https://github.com/Fuaran-Core/fuaran-core-compute), together with
/// `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp`, the two assemblies Phase
/// 257 cut beside them (D68). Phase 258 removed all four from this tree. The line runs one way — the
/// compute layer is built over this spine, never the reverse — and the easiest change in the world
/// to make is a helper that brings one of the ids back: a project restored under `src/`, a
/// `ProjectReference` to it, or a `PackageReference` to the compute repository's release, which
/// would make the spine depend on the layer above it. So this test refuses each, on readings that
/// each miss what the others see:
///
///   * the TREE — no project in the solution and no directory under `src/` or `tests/` is named for
///     a compute id;
///   * the PROJECT FILES — every project's `ProjectReference` closure, walked through the projects
///     it names, so a reference that arrives through an intermediate project is caught, and no
///     project or central package file names a compute id as a package;
///   * the BUILT ASSEMBLIES — each spine dll's own assembly-reference table, read from its
///     metadata (the list `Assembly.GetReferencedAssemblies` returns, read without loading the
///     assembly into this process), and this suite's own dependency manifest. The compiler writes a
///     reference for every assembly a compiled construct actually uses, so an `open` that resolved
///     against a transitively available assembly shows up there even where no project file names it.
module Fuaran.Core.Tests.ComputeBoundaryTests

open System
open System.IO
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Text.Json
open System.Xml.Linq
open Expecto

/// The spine: every assembly this repository ships since the compute layer left (D66). Every
/// project under `src/` is on it, which the tree test below holds, so a new project is classified
/// by the commit that adds it.
let spine: string list =
    [ "Fuaran.Core.Tree"
      "Fuaran.Core.Ops"
      "Fuaran.Core.OpStream"
      "Fuaran.Core.OpStream.Dag"
      "Fuaran.Core.Wire"
      "Fuaran.Core.Column"
      "Fuaran.Core.Validator"
      "Fuaran.Core.Observer"
      "Fuaran.Core.Function"
      "Fuaran.Core.Query"
      "Fuaran.Core.Projection"
      "Fuaran.Core.Propagation"
      "Fuaran.Core.AiSurface"
      "Fuaran.Core.Idl"
      "Fuaran.Core.Idl.Codegen"
      "Fuaran.Core.Idl.Spike"
      "Fuaran.Core.Idl.Cli"
      "Fuaran.Core.Conformance"
      "Fuaran.Core.CSharp" ]

/// The compute side of the line: the four ids the compute repository produces.
let compute: Set<string> =
    set
        [ "Fuaran.Core.DataFrame"
          "Fuaran.Core.Column.Ops"
          "Fuaran.Core.DataFrame.Conformance"
          "Fuaran.Core.DataFrame.CSharp" ]

// ---------------------------------------------------------------------------
//  the pure rules, so each has a go-red
// ---------------------------------------------------------------------------

/// Every (assembly, compute assembly) pair such that the compute assembly is reachable from the
/// named one through `edges` — a map from an assembly to the assemblies it references directly.
/// Reachability rather than adjacency, so an intermediate hop cannot launder a crossing.
let crossings (edges: Map<string, string list>) (names: string list) : (string * string) list =
    let rec reach (seen: Set<string>) (frontier: string list) =
        match frontier with
        | [] -> seen
        | x :: rest ->
            let next =
                edges
                |> Map.tryFind x
                |> Option.defaultValue []
                |> List.filter (fun n -> not (Set.contains n seen))

            reach (Set.union seen (Set.ofList next)) (next @ rest)

    [ for s in names do
          for c in reach Set.empty [ s ] |> Set.intersect compute |> Set.toList -> s, c ]

/// The names in a list that ARE compute ids, compared without regard to case (a package id is
/// case-insensitive, and a directory on this repository's development machines is too).
let computeNamed (names: string seq) : string list =
    let lowered = compute |> Set.map (fun c -> c.ToLowerInvariant())

    names
    |> Seq.filter (fun n -> Set.contains (n.ToLowerInvariant()) lowered)
    |> Seq.distinct
    |> Seq.sort
    |> Seq.toList

// ---------------------------------------------------------------------------
//  reading the tree
// ---------------------------------------------------------------------------

let private root () = Snapshots.repoFile ""

let private srcDir () = Snapshots.repoFile "src"

let private subdirNames (dir: string) : string list =
    if Directory.Exists dir then
        Directory.GetDirectories dir |> Array.map Path.GetFileName |> Array.toList
    else
        []

/// Every project file in the solution, repository-relative, read from `Fuaran.Core.slnx`.
let private solutionProjects () : string list =
    XDocument.Load(Snapshots.repoFile "Fuaran.Core.slnx").Descendants()
    |> Seq.filter (fun e -> e.Name.LocalName = "Project")
    |> Seq.choose (fun e ->
        match e.Attribute(XName.Get "Path") with
        | null -> None
        | a -> Some(a.Value.Replace('\\', '/')))
    |> Seq.toList

let private nameOf (projectPath: string) =
    Path.GetFileNameWithoutExtension projectPath

/// The elements of one local name in an MSBuild file, read as XML, so a name that appears in a
/// COMMENT (this repository's project files explain in several why they no longer reference the
/// dataframe layer) is not read as a reference.
let private includesOf (localName: string) (file: string) : string list =
    XDocument.Load(file).Descendants()
    |> Seq.filter (fun e -> e.Name.LocalName = localName)
    |> Seq.choose (fun e ->
        match e.Attribute(XName.Get "Include") with
        | null -> None
        | a -> Some a.Value)
    |> Seq.toList

/// The project-reference graph over every project in the solution, keyed by assembly name.
let private projectEdges () : Map<string, string list> =
    solutionProjects ()
    |> List.map (fun p ->
        nameOf p,
        includesOf "ProjectReference" (Path.Combine(root (), p))
        |> List.map (fun r -> Path.GetFileNameWithoutExtension(r.Replace('\\', '/'))))
    |> Map.ofList

/// The assembly-reference table of a built dll: the names `Assembly.GetReferencedAssemblies`
/// would return, read from the metadata so nothing is loaded.
let internal referencedAssemblies (dllPath: string) : string list =
    use stream = File.OpenRead dllPath
    use pe = new PEReader(stream)
    let md = pe.GetMetadataReader()

    [ for h in md.AssemblyReferences -> md.GetString((md.GetAssemblyReference h).Name) ]

/// The project file for an assembly name under `src/` — `.fsproj`, or `.csproj` for the facade.
let private projectFileOf (name: string) : string option =
    [ ".fsproj"; ".csproj" ]
    |> List.map (fun ext -> Path.Combine(srcDir (), name, name + ext))
    |> List.tryFind File.Exists

/// The built dll for a spine assembly: the copy in this test's own output when the suite
/// references it, otherwise the project's own build output, preferring this binary's
/// configuration (`PublicSurfaceTests.assemblyFor`, the surface gate's own locator).
let private builtAssembly (name: string) : Result<string, string> =
    let local = Path.Combine(AppContext.BaseDirectory, name + ".dll")

    if File.Exists local then
        Ok local
    else
        match projectFileOf name with
        | None -> Error(sprintf "%s: no project under src/" name)
        | Some p -> PublicSurfaceTests.assemblyFor (root ()) (Path.GetRelativePath(root (), p)) name

/// The library names this suite's dependency manifest resolves (`<assembly>.deps.json`, the file
/// the host reads to build the load context). A stale dll left in the output directory by an older
/// build is not in it, so this is the reading that cannot be fooled by `bin/` debris.
let private manifestLibraries () : string list =
    let deps =
        Path.Combine(AppContext.BaseDirectory, Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".deps.json")

    use doc = JsonDocument.Parse(File.ReadAllText deps)

    [ for lib in doc.RootElement.GetProperty("libraries").EnumerateObject() ->
          match lib.Name.IndexOf '/' with
          | -1 -> lib.Name
          | i -> lib.Name.Substring(0, i) ]

let private render (pairs: (string * string) list) : string =
    pairs |> List.map (fun (s, c) -> s + " -> " + c) |> String.concat "; "

[<Tests>]
let tests =
    testList
        "Compute boundary"
        [

          testCase "the rules go red on a direct crossing, an indirect one, a named id, and neither"
          <| fun _ ->
              // The go-red, over a synthetic graph: a spine project gaining a DataFrame reference,
              // one gaining it through an intermediate hop, and the clean graph beside them.
              let clean =
                  Map.ofList
                      [ "Fuaran.Core.Query", [ "Fuaran.Core.Column"; "Fuaran.Core.Function" ]
                        "Fuaran.Core.Column", [ "Fuaran.Core.Wire" ] ]

              Expect.isEmpty (crossings clean [ "Fuaran.Core.Query" ]) "a clean graph crosses nothing"

              let direct = clean |> Map.add "Fuaran.Core.Query" [ "Fuaran.Core.DataFrame" ]

              Expect.equal
                  (crossings direct [ "Fuaran.Core.Query" ])
                  [ "Fuaran.Core.Query", "Fuaran.Core.DataFrame" ]
                  "a direct reference to DataFrame is a crossing"

              let indirect =
                  clean
                  |> Map.add "Fuaran.Core.Query" [ "Fuaran.Core.Hop" ]
                  |> Map.add "Fuaran.Core.Hop" [ "Fuaran.Core.Column.Ops" ]

              Expect.equal
                  (crossings indirect [ "Fuaran.Core.Query" ])
                  [ "Fuaran.Core.Query", "Fuaran.Core.Column.Ops" ]
                  "a reference through an intermediate project is a crossing too"

              Expect.isEmpty
                  (computeNamed [ "Fuaran.Core.Column"; "Fuaran.Core.Columns.Ops"; "Fuaran.Core.DataFrames" ])
                  "a near-miss is not a compute id"

              Expect.equal
                  (computeNamed
                      [ "Fuaran.Core.Column"
                        "fuaran.core.dataframe"
                        "Fuaran.Core.DataFrame.CSharp" ])
                  [ "Fuaran.Core.DataFrame.CSharp"; "fuaran.core.dataframe" ]
                  "a compute id is named whatever its case"

          testCase "no project in the solution, and no directory under src/ or tests/, is a compute id"
          <| fun _ ->
              let projects = solutionProjects ()

              Expect.isGreaterThan
                  (List.length projects)
                  (List.length spine)
                  "the solution reading found the spine and more — a reader that found nothing would pass vacuously"

              let named =
                  computeNamed (
                      (projects |> List.map nameOf)
                      @ subdirNames (srcDir ())
                      @ subdirNames (Snapshots.repoFile "tests")
                  )

              Expect.isEmpty
                  named
                  (sprintf
                      "the compute strand is back in this tree: %A. D66: those ids are produced by https://github.com/Fuaran-Core/fuaran-core-compute from 0.33.0; the spine does not carry them."
                      named)

          testCase "every project under src/ is on the spine, and every spine name is a project there"
          <| fun _ ->
              let under =
                  subdirNames (srcDir ())
                  |> List.filter (fun n -> Option.isSome (projectFileOf n))

              Expect.equal
                  (List.sort under)
                  (List.sort spine)
                  "the spine list is the src/ tree: a new project is classified by the commit that adds it"

          testCase "no project in the solution references the compute side, directly or through another project"
          <| fun _ ->
              let edges = projectEdges ()
              let found = crossings edges (edges |> Map.keys |> Seq.toList)

              Expect.isEmpty
                  found
                  (sprintf
                      "project(s) reach the compute side through their ProjectReferences: %s. D66: the compute layer is built over this spine, never the reverse; a spine project that needs it is a design question for the compute repository's seam, not a reference to add here."
                      (render found))

              // The reading is not vacuous: the suite's own project names the kit it runs.
              Expect.contains
                  (Map.find "Fuaran.Core.Tests" edges)
                  "Fuaran.Core.Conformance"
                  "the test project's references were read"

          testCase "no project and no central package file names a compute id as a package"
          <| fun _ ->
              let files =
                  Snapshots.repoFile "Directory.Packages.props"
                  :: (solutionProjects () |> List.map (fun p -> Path.Combine(root (), p)))

              let packages =
                  [ for f in files do
                        yield! includesOf "PackageReference" f
                        yield! includesOf "PackageVersion" f ]

              Expect.contains packages "FSharp.Core" "the package reading found the packages that are there"

              Expect.isEmpty
                  (computeNamed packages)
                  "a package reference to the compute repository's release would make the spine depend on the layer built over it (D66)"

          testCase "no built spine assembly, and nothing this suite loads, references the compute side"
          <| fun _ ->
              let refs =
                  [ for name in spine ->
                        match builtAssembly name with
                        | Ok dll -> name, referencedAssemblies dll
                        | Error e -> failtestf "%s" e ]

              let found =
                  [ for name, rs in refs do
                        for r in rs do
                            if Set.contains r compute then
                                yield name, r ]

              Expect.isEmpty
                  found
                  (sprintf
                      "built spine assembly(ies) carry a reference to the compute side in their metadata: %s. The compiler writes a reference for every assembly a compiled construct uses, so an `open` that resolved against a transitively available assembly lands here even where no project file names it."
                      (render found))

              // The reading is not vacuous: the kit's own dll references the spine it runs over,
              // so a table that read as empty would be a broken reader rather than a clean line.
              let kit = refs |> List.find (fun (n, _) -> n = "Fuaran.Core.Conformance") |> snd
              Expect.contains kit "Fuaran.Core.Column" "the kit's reference table names Column, which it reads"

              let libraries = manifestLibraries ()
              Expect.contains libraries "Fuaran.Core.Column" "the manifest reading found the spine"

              Expect.isEmpty (computeNamed libraries) "this suite's dependency manifest resolves a compute assembly" ]
