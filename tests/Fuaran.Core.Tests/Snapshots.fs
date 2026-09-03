module Fuaran.Core.Tests.Snapshots

// ---------------------------------------------------------------------------
// Golden-snapshot store for the IDL-inversion vendored wire fixtures.
//
// The IDL drift-guard / authoring / round-trip legs (IdlSpikeTests) need a
// *frozen* canonical-wire snapshot per fixture — self-contained, so the gate
// never goes vacuous. Previously those snapshots were hand-maintained F# string
// literals, so every upstream wire change (Box unification, Phase 460
// omit-when-default, …) meant an error-prone manual edit of ~15 literals.
//
// This store externalises them to one `snapshots/<surface>.json` map per IDL
// surface (`spike` = the miniIdl 8-kind slice — the only surface left here since
// Phase 123 took the domain vocabulary to its own repository), and makes
// re-vendoring one command:
//
//     dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots
//
// which re-encodes every authored `case` through its IDL and rewrites the maps.
// The authoring leg then *freezes* the result (catches an accidental encoder
// change) and the round-trip leg proves decode∘encode.
// ---------------------------------------------------------------------------

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Fuaran.Core.Idl

/// Locate `tests/Fuaran.Core.Tests` by climbing from the CWD / test binary and
/// probing for a stable marker file, then ensure its `snapshots/` subdir exists.
let private snapshotsDir () : string =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        else
            let cand = Path.Combine(dir, "tests", "Fuaran.Core.Tests")

            if File.Exists(Path.Combine(cand, "Fuaran.Core.Tests.fsproj")) then
                Some cand
            else
                match Directory.GetParent dir with
                | null -> None
                | parent -> climb parent.FullName (budget - 1)

    let projectDir =
        [ Directory.GetCurrentDirectory(); System.AppContext.BaseDirectory ]
        |> List.tryPick (fun start -> climb start 12)
        |> Option.defaultWith (fun () ->
            failwith
                "Snapshots: could not locate tests/Fuaran.Core.Tests (Fuaran.Core.Tests.fsproj marker) from CWD or the test binary")

    let dir = Path.Combine(projectDir, "snapshots")
    Directory.CreateDirectory dir |> ignore
    dir

let private mapPath (surface: string) : string =
    Path.Combine(snapshotsDir (), surface + ".json")

/// Resolve a repo-relative path by climbing to the Fuaran-Core root (`Fuaran.Core.slnx`
/// marker) — used to rewrite the committed generated F# modules on `--regen-snapshots`.
let repoFile (relPath: string) : string =
    let rec climb (dir: string) (budget: int) : string option =
        if budget < 0 || isNull dir then
            None
        elif File.Exists(Path.Combine(dir, "Fuaran.Core.slnx")) then
            Some dir
        else
            match Directory.GetParent dir with
            | null -> None
            | parent -> climb parent.FullName (budget - 1)

    let root =
        [ Directory.GetCurrentDirectory(); System.AppContext.BaseDirectory ]
        |> List.tryPick (fun start -> climb start 12)
        |> Option.defaultWith (fun () -> failwith "Snapshots.repoFile: Fuaran-Core root (Fuaran.Core.slnx) not found")

    Path.Combine(root, relPath)

/// Read a surface's golden map (fixture name → canonical wire string).
let private readMap (surface: string) : Map<string, string> =
    let path = mapPath surface

    if not (File.Exists path) then
        failwithf "Snapshots: %s missing — run `dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots`" path

    use doc = JsonDocument.Parse(File.ReadAllText path)

    doc.RootElement.EnumerateObject()
    |> Seq.map (fun p -> p.Name, p.Value.GetString())
    |> Map.ofSeq

/// The vendored wire for each authored case, keyed by name — the frozen golden
/// the IDL gate asserts against. Fails loudly if a case has no snapshot.
let loadPaired (surface: string) (cases: (string * 'a) list) : (string * string) list =
    let m = readMap surface

    cases
    |> List.map (fun (name, _) ->
        match Map.tryFind name m with
        | Some wire -> name, wire
        | None -> failwithf "Snapshots: no '%s' entry in %s.json — run --regen-snapshots" name surface)

/// Regenerate a surface's golden map from `encode(idl, case)`. Deterministic;
/// the authoring leg freezes it, round-trip + drift-guard legs then check it.
let regen (surface: string) (idl: Idl) (cases: (string * IdlValue) list) : int =
    let entries =
        cases
        |> List.map (fun (name, v) ->
            match Encode.encode idl v with
            | Ok wire -> name, wire
            | Error e -> failwithf "Snapshots.regen %s/%s: encode failed: %s" surface name e)

    // A stable, human-diffable map file (keys sorted, indented).
    let obj = JsonObject()

    for name, wire in entries |> List.sortBy fst do
        obj[name] <- JsonValue.Create wire

    let opts = JsonSerializerOptions(WriteIndented = true)
    File.WriteAllText(mapPath surface, obj.ToJsonString opts + "\n")
    printfn "regenerated %s.json — %d fixtures" surface (List.length entries)
    List.length entries
