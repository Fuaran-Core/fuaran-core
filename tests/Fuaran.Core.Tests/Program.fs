module Fuaran.Core.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    // Phase 696's `--emit-idl` is GONE (Phase 123). It rendered ONE domain's
    // vocabulary artifact into that domain's corpus clone, and lived here only
    // because the engine shipped in no package and the vocabulary was local to
    // this test project. Neither is true now: `Fuaran.Core.Idl` is packable from
    // 0.4.0 and a domain holds its own vocabulary (DECISIONS.md D14), so a domain
    // renders its own artifact in its own repository against the packaged engine.
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
    // The vocabulary is an ARGUMENT (`--idl <idl.json>`), read through
    // `Artifact.parse` — Phase 114's inversion is what makes that possible, and
    // Phase 123 is where it was needed: the entry point used to name a domain's
    // vocabulary because that vocabulary happened to live in this test project,
    // which is exactly the coupling D14 removes. It is branchless by construction — the delta is applied to an
    // in-memory `Idl` value that exists for the duration of the call — so an
    // abandoned spike leaves no residue anywhere, which is exactly the property that
    // makes spiking every candidate affordable.
    //
    // The corpus leg reads the `nodes/` family of the corpus directory named by
    // `--corpus <dir>`. When none is named the leg reports "not checked" and the run
    // is not green: a spike whose additive claim went unexamined must not read as a
    // spike that examined it and found nothing.
    //
    // Exit: 0 every leg passed · 1 a leg failed · 2 the document did not read. A
    // green exit is the removal of one objection, never a recommendation — nothing
    // downstream of this command may treat 0 as an admission.
    //   dotnet run --project tests/Fuaran.Core.Tests -- --spike-proposal <proposal.json>
    //       --idl <idl.json> [--corpus <nodes-parent-dir>]
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
            match flag "--corpus" with
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

        let baseIdl =
            match flag "--idl" with
            | None -> Error "no --idl <idl.json> given — the spike prices a proposal AGAINST a vocabulary"
            | Some path ->
                if System.IO.File.Exists path then
                    Fuaran.Core.Idl.Artifact.parse (System.IO.File.ReadAllText path)
                else
                    Error(sprintf "--idl names no file: %s" path)

        match baseIdl, Fuaran.Core.Idl.Proposal.parse (System.IO.File.ReadAllText proposalPath) with
        | Error e, _ ->
            eprintfn "spike-proposal: the vocabulary did not read — %s" e
            2
        | _, Error e ->
            eprintfn "spike-proposal: the document did not read — %s" e
            2
        | Ok baseVocabulary, Ok proposal ->
            match
                Fuaran.Core.Idl.ProposalSpike.run
                    { Base = baseVocabulary
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

        // Phases 108/109 — the second-vocabulary slice's generated F# module, in
        // its DECLARED wire shape (bare-string `kind` discriminator, flat node
        // envelope). The leg the original readiness spike skipped as blocked.
        writeGen
            "tests/Fuaran.Core.Tests/DocGenerated.fs"
            "Fuaran.Core.Tests.DocGenerated"
            Fuaran.Core.Idl.Gen.GenSupport.Empty
            SecondDomainSpike.docIdl
            (SecondDomainSpike.docIdl.Kinds |> List.map (fun k -> k.Tag))

        0
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
