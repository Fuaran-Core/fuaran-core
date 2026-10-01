module Fuaran.Core.Tests.Program

open Expecto

/// Where an exporter writes: the directory named after the flag, else this repository's
/// committed `conformance/` (Phase 172). A following flag is not a directory.
let private emitTarget (rest: string list) : string =
    match rest with
    | dir :: _ when not (dir.StartsWith "--") -> dir
    | _ -> OwnedConformance.root ()

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
    // Write every law set Core is the reference for — (Phase 235, moved from
    // the UI tier) the capabilityLaws vectors and (Phase 276) the exact
    // decimal's documents; the transform-parity vectors left with the compute
    // strand in Phase 258:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --emit-laws [<dir>]
    // With no argument (Phase 172) the target is THIS repository's committed
    // `conformance/` — the source of truth the default suite certifies against.
    // With a directory it writes there instead, which is how the shared corpus's
    // declared copy is refreshed (`copies.json` quotes that form). Deliberately a
    // flag rather than a test side-effect: the corpus is a separate repository,
    // and a suite that wrote into it on every run would dirty a shared clone; the
    // suite COMPARES and names this command.
    | "--emit-laws" :: rest ->
        let dir = emitTarget rest
        LawVectorExport.write dir

        for path, _ in LawVectorExport.emitted dir do
            printfn "Wrote %s" path

        0
    // Phase 139 — write the `apply/` family (the skeleton-op apply contract: one
    // vector per validator clause per op, Batch atomicity, the id-collision
    // shapes); same target rule as `--emit-laws`:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --emit-apply [<dir>]
    // A flag rather than a test side-effect, for the reason `--emit-laws` above
    // is one. Note the name: the UI language repo's `--emit-corpus` renders THAT
    // domain's node vectors and knows nothing about this engine; the apply
    // semantics are Core's, so Core emits them.
    | "--emit-apply" :: rest ->
        let dir = emitTarget rest
        ApplyVectorExport.write dir
        printfn "Wrote %s" (ApplyVectorExport.vectorsPath dir)
        printfn "Wrote %s" (ApplyVectorExport.manifestPath dir)
        0
    // Phase 299 — write the `refusals/` family (the codec refusal vectors: the JSON number grammar,
    // well-formed UTF-16 strings, and the columnar codec's cell-type, canonical-text, duplicate-name
    // and ragged-table refusals); same target rule as `--emit-laws`:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --emit-refusals [<dir>]
    | "--emit-refusals" :: rest ->
        let dir = emitTarget rest
        RefusalVectorTests.RefusalCorpus.write dir
        printfn "Wrote %s" (RefusalVectorTests.RefusalCorpus.vectorsPath dir)
        printfn "Wrote %s" (RefusalVectorTests.RefusalCorpus.manifestPath dir)
        0
    // Phase 310 — write the `decode/` family (the decode reject vectors: every decode code at the path
    // it names, over a shape grammar each host reads with its own combinators); same target rule as
    // `--emit-laws`:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --emit-decode [<dir>]
    | "--emit-decode" :: rest ->
        let dir = emitTarget rest
        DecodeLayerTests.DecodeRejectCorpus.write dir
        printfn "Wrote %s" (DecodeLayerTests.DecodeRejectCorpus.vectorsPath dir)
        printfn "Wrote %s" (DecodeLayerTests.DecodeRejectCorpus.manifestPath dir)
        0
    // Phase 184 — write the law-family roster's two generated artefacts (the human-readable
    // `docs/conformance-families.md` and the machine-readable `docs/conformance-families.json`
    // an offline projection reads):
    //   dotnet run --project tests/Fuaran.Core.Tests -- --emit-families [<dir>]
    // With no argument the target is THIS repository's committed `docs/`. A flag rather than a
    // test side-effect for the `--emit-laws` reason above; the suite COMPARES both files against
    // what the roster renders and names this command.
    | "--emit-families" :: rest ->
        let dir =
            match rest with
            | d :: _ when not (d.StartsWith "--") -> d
            | _ -> ConformanceFamiliesTests.Export.root ()

        ConformanceFamiliesTests.Export.write dir
        printfn "Wrote %s" (ConformanceFamiliesTests.Export.markdownPath dir)
        printfn "Wrote %s" (ConformanceFamiliesTests.Export.jsonPath dir)
        0
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
    // The Phase 702 `--spike-proposal` flag is now the `spike-proposal` verb of the
    // `fuaran-core-idl` command (Phase 230), with the same flags, report and exit codes:
    //   dotnet run --project src/Fuaran.Core.Idl.Cli -- spike-proposal <proposal.json> --idl <idl.json>
    // Re-vendor the IDL-inversion golden snapshots from the authored cases:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --regen-snapshots
    | "--regen-snapshots" :: _ ->
        Snapshots.regen "spike" Fuaran.Core.Tests.MiniIdl.miniIdl Fuaran.Core.Tests.MiniIdl.cases
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
            "tests/Fuaran.Core.Tests/MiniGenerated.fs"
            "Fuaran.Core.Tests.MiniGenerated"
            Fuaran.Core.Idl.Gen.GenSupport.Empty
            Fuaran.Core.Tests.MiniIdl.miniIdl
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
    // Phase 127 — rewrite the committed `idl.json` fixtures the repository gate runs
    // the `fuaran-core-idl` command over:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --regen-idl-classify-fixtures
    // They are rendered from the `Idl` declarations in IdlStabilityClassTests, and a
    // guard there fails naming this command when a committed file and its declaration
    // disagree — so the gate can never certify the command against bytes nothing
    // produces.
    | "--regen-idl-classify-fixtures" :: _ ->
        IdlStabilityClassTests.regen ()
        0
    // Phase 150 — regenerate the committed F* proof model and its theorems from the pinned
    // wire-format IDL:
    //   dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar
    // The generation diff in IdlFStarTargetTests fails naming this command when the corpus
    // has moved and the committed `.fst` files have not, on exactly the discipline the proof
    // leg already applies to the extracted oracle. A flag rather than a test side-effect, for
    // the `--emit-laws` reason: a suite that rewrote a committed artefact on every run could
    // not also be the thing that notices it has changed.
    | "--emit-fstar" :: _ -> IdlFStarTargetTests.emit ()
    // Phase 328 — the half of `proofs/check.ps1 -Since <tree>` that decides which registered
    // models are in the module cone, and records a green `-Strict` full run as the baseline an
    // empty cone may lean on. `check.ps1` calls it; the arguments are in `coneCli`'s own comment.
    | "--proof-cone" :: rest -> ProofsLadderTests.coneCli rest
    | _ -> runTestsInAssemblyWithCLIArgs [] argv
