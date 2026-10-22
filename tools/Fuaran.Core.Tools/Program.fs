module Fuaran.Core.Tools.Program

// ============================================================================
//  The tool (Phase 532). Four verbs:
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
//    open-slot <version> --class <breaking|additive|internal> --opened-by <text> --corpus <dir>
//              [--date <yyyy-mm-dd>]
//                              open a version slot in one act (Phase 560, `OpenSlot`): `<Version>`,
//                              the law files' `kitVersion` stamps, a corpus commit of exactly the
//                              declared copies, the corpus pin naming it, and the DRAFT ledger file
//                              with its index; or refuse, writing nothing. It prints the commit this
//                              repository still needs, after the corpus push.
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

    eprintfn
        "       open-slot <version> --class <breaking|additive|internal> --opened-by <text> --corpus <dir> [--date <yyyy-mm-dd>]"

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

let private openSlot (version: string) (rest: string list) : int =
    let rec options (acc: Map<string, string>) =
        function
        | ("--class" | "--opened-by" | "--corpus" | "--date" as name) :: value :: more ->
            options (Map.add name value acc) more
        | [] -> Ok acc
        | other :: _ -> Error(sprintf "unknown or incomplete option `%s`" other)

    match options Map.empty rest with
    | Error why ->
        eprintfn "open-slot: %s" why
        usage ()
    | Ok o ->
        match o.TryFind "--class" |> Option.bind OpenSlot.parseClass, o.TryFind "--opened-by", o.TryFind "--corpus" with
        | Some cls, Some openedBy, Some corpus ->
            let request: OpenSlot.Request =
                { Root = Repo.root ()
                  Corpus = System.IO.Path.GetFullPath corpus
                  Version = version
                  Class = cls
                  OpenedBy = openedBy
                  Date =
                    o.TryFind "--date"
                    |> Option.defaultValue (System.DateTime.UtcNow.ToString "yyyy-MM-dd") }

            match OpenSlot.plan request with
            | Error faults ->
                eprintfn "open-slot %s: refused, nothing was written:" version

                for f in faults do
                    eprintfn "  - %s" f

                2
            | Ok plan ->
                match OpenSlot.apply plan with
                | Error why ->
                    eprintfn "open-slot %s: %s" version why
                    1
                | Ok outcome ->
                    printfn "open-slot %s:" version

                    for line in OpenSlot.nextSteps plan outcome do
                        printfn "%s" line

                    0
        | _ ->
            eprintfn "open-slot: --class (breaking, additive or internal), --opened-by and --corpus are required"
            usage ()

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | "render" :: family :: rest -> render family rest
    | "proof-cone" :: rest -> ProofCone.coneCli rest
    | [ "proofs-inventory" ] -> inventory ()
    | "open-slot" :: version :: rest -> openSlot version rest
    | _ -> usage ()
