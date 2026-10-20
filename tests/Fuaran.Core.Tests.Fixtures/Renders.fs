/// Every generated artefact family the tool writes (Phase 532), by the name `render <family>` takes.
/// The projection registry's `render` field names one of these; `ApprovalTests` holds the two to each
/// other, so a family no row names, or a row naming no family, is red. A new family is one entry here
/// and one row there.
module Fuaran.Core.Tests.Renders

open System.IO

/// One family: whether it takes a target directory (the corpus families, written into the shared
/// corpus's checkout by `copies.json`'s `regen`), and the write, answering an exit code.
type Render =
    { TakesDir: bool
      Write: string option -> int }

let private exporter (e: Exporters.Exporter) : Render =
    { TakesDir = true
      Write =
        fun dir ->
            for path in Exporters.write e dir do
                printfn "Wrote %s" path

            0 }

let private inRepo (write: unit -> int) : Render =
    { TakesDir = false
      Write = fun _ -> write () }

/// The oracle project's generated `Compile` list, rewritten in place.
let private oracleProject () : int =
    let project = Snapshots.repoFile OracleProject.Path
    let roster = File.ReadAllText(Snapshots.repoFile OracleProject.RosterPath)

    match OracleProject.render roster (File.ReadAllText project) with
    | Error e ->
        eprintfn "render oracle-project: %s" e
        2
    | Ok text ->
        File.WriteAllText(project, text)
        printfn "regenerated %s" OracleProject.Path
        0

/// The table, given the reference run that measures the `families` exporter's `cases` column.
let tableWith (measuredCases: unit -> (string * Fuaran.Core.CaseCount) list) : (string * Render) list =
    [ yield! Exporters.tableWith measuredCases |> List.map (fun e -> e.Family, exporter e)
      yield "generated", inRepo GeneratedModules.regen

      yield
          "idl-classify-fixtures",
          inRepo (fun () ->
              IdlClassify.Files.regen ()
              0)

      yield "fstar", inRepo (fun () -> max (FStarModels.emit ()) (CertificationVectors.emitVectors ()))
      yield "oracle-project", inRepo oracleProject ]

/// The family names, in table order (nothing is measured to list them).
let names: string list = tableWith (fun () -> []) |> List.map fst
