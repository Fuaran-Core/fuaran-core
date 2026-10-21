module Fuaran.Core.Tools.Program

// ============================================================================
//  The tool (Phase 532). Three verbs:
//
//    render <family> [<dir>]   write one generated artefact family (`Renders.names`): the corpus
//                              families into this repository's committed copies, or into <dir> where
//                              the family takes one (`copies.json` quotes that form for the shared
//                              corpus); the generated F# modules, the classifier fixtures, the F*
//                              models and the oracle project's Compile list in place.
//    proof-cone <arguments>    the half of `proofs/check.ps1 -Since` / `-Modules` that decides which
//                              registered models are in the module cone, and records a green
//                              `-Strict` full run as the baseline (`ProofCone.coneCli` states the
//                              arguments). The caller names the verb.
//    proofs-inventory          print the one sentence stating the proof inventory (models, their
//                              lines, the ladder's rows by level), for a document outside this
//                              repository that quotes it (Phase 533). `render proofs-readme` writes
//                              the README itself.
//
//  The test project used to be this tool: its runner carried a dozen flags whose exporters lived in
//  test files, so running the suite and producing the corpus were one assembly's two jobs. The suite
//  now holds what this writes, and the gate runs only the suite.
// ============================================================================

open Fuaran.Core.Tests

let private usage () =
    eprintfn "usage: render <family> [<dir>]   (families: %s)" (String.concat ", " Renders.names)

    eprintfn
        "       proof-cone --since <tree> --out <file> | --modules <a,b,...> --out <file> | --record-strict --verdict <file> --out <file>"

    eprintfn "       proofs-inventory"
    2

let private inventory () : int =
    match ProofReadme.inventoryLive () with
    | Ok line ->
        printfn "%s" line
        0
    | Error why ->
        eprintfn "proofs-inventory: %s" why
        2

let private render (family: string) (rest: string list) : int =
    let table = Renders.tableWith ConformanceVacuityTests.cases

    match table |> List.tryFind (fun (name, _) -> name = family) with
    | None ->
        eprintfn "render: no family `%s` (the table holds: %s)" family (String.concat ", " Renders.names)
        2
    | Some(_, r) ->
        match rest with
        | [] -> r.Write None
        | [ dir ] when r.TakesDir -> r.Write(Some dir)
        | _ ->
            eprintfn
                "render %s: %s"
                family
                (if r.TakesDir then
                     "takes at most one directory"
                 else
                     "writes in place and takes no argument")

            2

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | "render" :: family :: rest -> render family rest
    | "proof-cone" :: rest -> ProofCone.coneCli rest
    | [ "proofs-inventory" ] -> inventory ()
    | _ -> usage ()
