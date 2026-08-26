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
    // Phase 702 — the SPIKE harness: price a vocabulary-change proposal against the
    // live vocabulary without cutting a branch or writing a declaration.
    //   dotnet run --project tests/Fuaran.Core.Tests -- --spike-proposal <proposal.json>
    //       [--out <report.md>] [--seed <int>] [--vectors <int>]
    //
    // The entry point lives here for the same reason `--emit-idl` does: the spike
    // needs BOTH the engine and the vocabulary, and `UiIdl.uiIdl` is local to this
    // test project. It is branchless by construction — the delta is applied to an
    // in-memory `Idl` value that exists for the duration of the call — so an
    // abandoned spike leaves no residue anywhere, which is exactly the property that
    // makes spiking every candidate affordable.
    //
    // The corpus leg reads the `nodes/` family of the wire-format corpus clone when
    // one is present. When it is ABSENT the leg reports "not checked" and the run is
    // not green: a spike whose additive claim went unexamined must not read as a
    // spike that examined it and found nothing.
    //
    // Exit: 0 every leg passed · 1 a leg failed · 2 the document did not read. A
    // green exit is the removal of one objection, never a recommendation — nothing
    // downstream of this command may treat 0 as an admission.
    | "--spike-proposal" :: proposalPath :: rest ->
        let flag name =
            rest
            |> List.pairwise
            |> List.tryPick (fun (a, b) -> if a = name then Some b else None)

        let intFlag name fallback =
            match flag name with
            | Some v ->
                match System.Int32.TryParse v with
                | true, n -> n
                | _ -> fallback
            | None -> fallback

        let corpus =
            match IdlArtifactTests.tryFindCorpusRoot () with
            | None -> []
            | Some root ->
                let dir = System.IO.Path.Combine(root, "nodes")

                if not (System.IO.Directory.Exists dir) then
                    []
                else
                    System.IO.Directory.GetFiles(dir, "*.json")
                    |> Array.filter (fun p -> not ((System.IO.Path.GetFileName p).EndsWith ".expected.json"))
                    |> Array.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
                    |> Array.map (fun p -> System.IO.Path.GetFileName p, System.IO.File.ReadAllText p)
                    |> List.ofArray

        match Fuaran.Core.Idl.Proposal.parse (System.IO.File.ReadAllText proposalPath) with
        | Error e ->
            eprintfn "spike-proposal: the document did not read — %s" e
            2
        | Ok proposal ->
            match
                Fuaran.Core.Idl.ProposalSpike.run
                    { Base = UiIdl.uiIdl
                      Proposal = proposal
                      Corpus = corpus
                      // Pinned, not clock-derived: a divergence a spike finds has to
                      // reproduce from the report alone on another machine.
                      FuzzSeed = intFlag "--seed" 20260826
                      FuzzVectors = intFlag "--vectors" 200
                      External = [] }
            with
            | Error e ->
                eprintfn "spike-proposal: %s" e
                2
            | Ok report ->
                let text = Fuaran.Core.Idl.ProposalSpike.render report

                match flag "--out" with
                | Some out ->
                    System.IO.File.WriteAllText(out, text)
                    printfn "wrote %s" out
                | None -> printf "%s" text

                if report.Green then 0 else 1
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
        let writeGen
            (rel: string)
            (modName: string)
            (sup: Fuaran.Core.Idl.Gen.GenSupport)
            (idl: Fuaran.Core.Idl.Idl)
            (kinds: string list)
            =
            match Fuaran.Core.Idl.Gen.fsharpModuleWith sup modName idl kinds with
            | Ok src ->
                let p = Snapshots.repoFile rel
                System.IO.File.WriteAllText(p, src)
                printfn "regenerated %s" rel
            | Error e -> failwithf "codegen %s: %A" rel e

        writeGen
            "src/Fuaran.Core.Idl.Spike/Generated.fs"
            "Fuaran.Core.Idl.Spike.Generated"
            Fuaran.Core.Idl.Gen.GenSupport.Empty
            Fuaran.Core.Idl.Spike.Fixtures.miniIdl
            [ "Heading"; "Badge"; "Button"; "Metric"; "Box"; "Markdown"; "Tabs" ]

        writeGen
            "tests/Fuaran.Core.Tests/UiGenerated.fs"
            "Fuaran.Core.Tests.UiGenerated"
            UiIdlSupport.support
            UiIdl.uiIdl
            (UiIdl.uiIdl.Kinds |> List.map (fun k -> k.Tag))

        0
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
