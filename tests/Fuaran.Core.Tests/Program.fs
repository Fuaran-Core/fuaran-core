module Fuaran.Core.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Re-vendor the IDL-inversion golden snapshots from the authored cases:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots
    | "--regen-snapshots" :: _ ->
        Snapshots.regen "spike" Fuaran.Core.Idl.Spike.Fixtures.miniIdl Fuaran.Core.Idl.Spike.Fixtures.cases
        |> ignore

        Snapshots.regen
            "ui"
            UiIdl.uiIdl
            (UiIdl.displayCases
             @ UiIdl.layoutCases
             @ UiIdl.inputCases
             @ UiIdl.visCases
             @ UiIdl.metaCases)
        |> ignore

        // Rewrite the committed generated F# modules (their encoders embed the
        // omit-when-default emission, so they change with the schema).
        let writeGen (rel: string) (modName: string) (idl: Fuaran.Core.Idl.Idl) (kinds: string list) =
            match Fuaran.Core.Idl.Gen.fsharpModule modName idl kinds with
            | Ok src ->
                let p = Snapshots.repoFile rel
                System.IO.File.WriteAllText(p, src)
                printfn "regenerated %s" rel
            | Error e -> failwithf "codegen %s: %A" rel e

        writeGen
            "src/Fuaran.Core.Idl.Spike/Generated.fs"
            "Fuaran.Core.Idl.Spike.Generated"
            Fuaran.Core.Idl.Spike.Fixtures.miniIdl
            [ "Heading"; "Badge"; "Button"; "Metric"; "Box"; "Markdown" ]

        writeGen
            "tests/Fuaran.Core.Tests/UiGenerated.fs"
            "Fuaran.Core.Tests.UiGenerated"
            UiIdl.uiIdl
            (UiIdl.uiIdl.Kinds |> List.map (fun k -> k.Tag))

        0
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
