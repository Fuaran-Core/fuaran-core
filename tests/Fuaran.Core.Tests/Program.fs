module Fuaran.Core.Tests.Program

open System
open System.IO
open Expecto
open Fuaran.Core.Idl.Codegen.Types

/// Phase 400 - the skip report. Expecto already counts the legs a missing prerequisite ignores; this
/// names them, one family (the test list's first name segment, which Expecto joins with a dot) at a time, so a green run says how much
/// of the suite it did not read. The list is printed after the summary; when `FUARAN_CORE_SKIP_REPORT`
/// names a file it is also written there as one line, which is how `verify.ps1` repeats it on the gate's
/// final line without parsing the runner's output.
module private SkipReport =
    let private skipped = System.Collections.Concurrent.ConcurrentBag<string>()

    let private family (name: string) : string =
        match name.Split([| '.' |], StringSplitOptions.RemoveEmptyEntries) with
        | [||] -> name
        | parts -> parts.[0].Trim()

    /// The default printer, additionally recording each ignored test's name.
    let printer: Impl.TestPrinters =
        { Impl.TestPrinters.defaultPrinter with
            ignored =
                fun name reason ->
                    skipped.Add name
                    Impl.TestPrinters.defaultPrinter.ignored name reason }

    /// "N skipped across M families: A (n), B (m)", or "0 skipped".
    let summary () : string =
        let names = skipped.ToArray()

        if names.Length = 0 then
            "0 skipped"
        else
            let families =
                names
                |> Array.countBy family
                |> Array.sortBy fst
                |> Array.map (fun (f, n) -> sprintf "%s (%d)" f n)

            sprintf "%d skipped across %d families: %s" names.Length families.Length (String.Join(", ", families))

    /// Print the summary, and write it to the file the launcher named, if any.
    let report () : unit =
        let line = summary ()
        printfn "Skipped: %s" line

        match Environment.GetEnvironmentVariable "FUARAN_CORE_SKIP_REPORT" with
        | null
        | "" -> ()
        | path -> File.WriteAllText(path, line)

/// `--emit <family> [<dir>]` over the exporter table: write the row's files and name each one.
let private emit (family: string) (rest: string list) : int =
    match Exporters.tryFind family with
    | None ->
        eprintfn
            "--emit: no family `%s` (the table holds: %s)"
            family
            (Exporters.table |> List.map _.Family |> String.concat ", ")

        1
    | Some e ->
        let dir =
            match rest with
            | dir :: _ when not (dir.StartsWith "--") -> Some dir
            | _ -> None

        for path in Exporters.write e dir do
            printfn "Wrote %s" path

        0

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
    // Phase 508 — write one emitted family's committed files, dispatched over the exporter table
    // (`Exporters.table`, one row per family: laws, apply, refusals, decode, escape, encoding,
    // families):
    //   dotnet run --project tests/Fuaran.Core.Tests -- --emit <family> [<dir>]
    // With no directory a row writes into its own root — THIS repository's committed
    // `conformance/` (Phase 172), or `docs/` for the roster's two artefacts — the source of truth
    // the default suite certifies against. With one it writes there instead, which is how the shared
    // corpus's declared copy is refreshed (`copies.json` quotes that form). Deliberately a flag
    // rather than a test side-effect: the corpus is a separate repository, and a suite that wrote
    // into it on every run would dirty a shared clone; the suite COMPARES (`Exporters.tests`) and
    // names this command.
    | "--emit" :: family :: rest -> emit family rest
    // The `--emit-<family>` spellings each family had before the table, kept as aliases because
    // `copies.json`, `projections.json` and the shared corpus's own producer record quote them.
    | "--emit-laws" :: rest -> emit "laws" rest
    | "--emit-apply" :: rest -> emit "apply" rest
    | "--emit-refusals" :: rest -> emit "refusals" rest
    | "--emit-decode" :: rest -> emit "decode" rest
    | "--emit-escape" :: rest -> emit "escape" rest
    | "--emit-encoding" :: rest -> emit "encoding" rest
    | "--emit-families" :: rest -> emit "families" rest
    | "--idl-diff" :: oldPath :: newPath :: rest ->
        let read (p: string) = System.IO.File.ReadAllText p

        let manifest =
            rest |> List.tryHead |> Option.filter System.IO.File.Exists |> Option.map read

        match Fuaran.Core.Idl.Diff.run manifest (read oldPath, None) (read newPath, None) with
        | Ok(text, _) ->
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
        let writeGen (rel: string) (modName: string) (sup: GenSupport) (idl: Fuaran.Core.Idl.Idl) (kinds: string list) =
            match Fuaran.Core.Idl.Gen.fsharpModuleWith sup modName idl kinds with
            | Ok src ->
                let p = Snapshots.repoFile rel
                System.IO.File.WriteAllText(p, src)
                printfn "regenerated %s" rel
            | Error e -> failwithf "codegen %s: %A" rel e

        writeGen
            "tests/Fuaran.Core.Tests/MiniGenerated.fs"
            "Fuaran.Core.Tests.MiniGenerated"
            GenSupport.Empty
            Fuaran.Core.Tests.MiniIdl.miniIdl
            [ "Heading"; "Badge"; "Button"; "Metric"; "Box"; "Markdown"; "Tabs" ]

        // Phases 108/109 — the second-vocabulary slice's generated F# module, in
        // its DECLARED wire shape (bare-string `kind` discriminator, flat node
        // envelope). The leg the original readiness spike skipped as blocked.
        writeGen
            "tests/Fuaran.Core.Tests/DocGenerated.fs"
            "Fuaran.Core.Tests.DocGenerated"
            GenSupport.Empty
            SecondDomainSpike.docIdl
            (SecondDomainSpike.docIdl.Kinds |> List.map _.Tag)

        // Phase 303 — the other two certification vocabularies' generated F# modules, so
        // every certification vocabulary's F# backend is COMPILED by the suite, not only
        // regenerated. The score vocabulary carries field-less (marker) kinds; the reference
        // one is emitted under its declared support document, whose named host prelude is
        // `ReferencePrelude.fs`.
        writeGen
            "tests/Fuaran.Core.Tests/ScoreGenerated.fs"
            "Fuaran.Core.Tests.ScoreGenerated"
            GenSupport.Empty
            ScoreDomainSpike.scoreIdl
            (ScoreDomainSpike.scoreIdl.Kinds |> List.map _.Tag)

        writeGen
            "tests/Fuaran.Core.Tests/ReferenceGenerated.fs"
            "Fuaran.Core.Tests.ReferenceGenerated"
            ReferenceIdl.support.Support
            ReferenceIdl.refIdl
            (ReferenceIdl.refIdl.Kinds |> List.map _.Tag)

        // Phase 337 — the decode vectors' vocabulary, so the decode error contract is held
        // against a COMPILED generated host as well as the interpreter and the TypeScript one.
        let decodeIdl, _, _ = DecodeVectorsIdl.current ()

        writeGen
            DecodeVectorsIdl.generatedFile
            DecodeVectorsIdl.moduleName
            GenSupport.Empty
            decodeIdl
            (decodeIdl.Kinds |> List.map _.Tag)

        // Phase 347 — the refusal corners' vocabulary, so every corner is read by a COMPILED
        // generated host beside the interpreter and the TypeScript one.
        writeGen
            RefusalCornersIdl.generatedFile
            RefusalCornersIdl.moduleName
            GenSupport.Empty
            RefusalCornersIdl.idl
            (RefusalCornersIdl.idl.Kinds |> List.map _.Tag)

        // Phase 374 — the derivations vocabulary, with every structural derivation requested, so
        // each one is COMPILED by the suite and held to its laws over the compiled module.
        match DeriveIdl.generate () with
        | Ok src ->
            System.IO.File.WriteAllText(Snapshots.repoFile DeriveIdl.generatedFile, src)
            printfn "regenerated %s" DeriveIdl.generatedFile
        | Error e -> failwithf "codegen %s: %A" DeriveIdl.generatedFile e

        // Phase 377 — the reference vocabulary with the collecting decoders requested, so every
        // support channel a collecting decoder routes around is COMPILED by the suite.
        match SpecDecodersIdl.generate () with
        | Ok src ->
            System.IO.File.WriteAllText(Snapshots.repoFile SpecDecodersIdl.generatedFile, src)
            printfn "regenerated %s" SpecDecodersIdl.generatedFile
        | Error e -> failwithf "codegen %s: %A" SpecDecodersIdl.generatedFile e

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
    // Phase 303 — and the interpreter's vectors as normaliser facts over those models.
    | "--emit-fstar" :: _ -> max (IdlFStarTargetTests.emit ()) (IdlThreeHostTests.emitVectors ())
    // Phase 328 — the half of `proofs/check.ps1 -Since <tree>` that decides which registered
    // models are in the module cone, and records a green `-Strict` full run as the baseline an
    // empty cone may lean on. `check.ps1` calls it; the arguments are in `coneCli`'s own comment.
    | "--proof-cone" :: rest -> ProofsLadderTests.coneCli rest
    | _ ->
        let code = runTestsInAssemblyWithCLIArgs [ Printer SkipReport.printer ] argv
        SkipReport.report ()
        code
