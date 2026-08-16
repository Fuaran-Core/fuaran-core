module Fuaran.Core.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Phase 696 — write the canonical `idl.json` vocabulary artifact into the
    // wire-format-fixtures corpus clone:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --emit-idl ..\Fuaran-UI\wire-format-fixtures
    // The emitter lives here rather than beside the corpus regen in the UI tier
    // because the IDL engine ships in no package and the vocabulary is local to
    // this test project — see IdlArtifactTests.fs. The drift guard in that file
    // fails whenever the committed artifact and a fresh emission disagree.
    | "--emit-idl" :: dir :: _ ->
        IdlArtifactTests.emit dir
        0
    // Phase 700 — classify the delta between two `idl.json` revisions and print
    // the host-strand report:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --idl-diff <old.json> <new.json> [<manifest.json>]
    // Advisory output only; nothing is written and nothing is gated. The optional
    // third argument is the corpus manifest, read solely for the §11.0 host
    // roster once it carries one — until then the declared roster is used and the
    // report says so.
    | "--idl-diff" :: oldPath :: newPath :: rest ->
        let read (p: string) = System.IO.File.ReadAllText p

        let manifest =
            rest |> List.tryHead |> Option.filter System.IO.File.Exists |> Option.map read

        match Fuaran.Core.Idl.Diff.run manifest (read oldPath) (read newPath) with
        | Ok text ->
            printf "%s" text
            0
        | Error e ->
            eprintfn "idl-diff: %s" e
            1
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
            [ "Heading"; "Badge"; "Button"; "Metric"; "Box"; "Markdown"; "Tabs" ]

        writeGen
            "tests/Fuaran.Core.Tests/UiGenerated.fs"
            "Fuaran.Core.Tests.UiGenerated"
            UiIdl.uiIdl
            (UiIdl.uiIdl.Kinds |> List.map (fun k -> k.Tag))

        0
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
