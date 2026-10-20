/// The committed generated F# modules the suite COMPILES (and the IDL-inversion golden snapshot beside
/// them): every certification vocabulary's F# backend output, rendered by the tool's
/// `render generated`. Each module's family holds the committed file to a fresh generation and names
/// that command when it drifts.
module Fuaran.Core.Tests.GeneratedModules

open System.IO
open Fuaran.Core.Idl.Codegen.Types

/// Rewrite one committed generated module from its vocabulary.
let private writeGen (rel: string) (modName: string) (sup: GenSupport) (idl: Fuaran.Core.Idl.Idl) (kinds: string list) =
    match Fuaran.Core.Idl.Gen.fsharpModuleWith sup modName idl kinds with
    | Ok src ->
        let p = Snapshots.repoFile rel
        File.WriteAllText(p, src)
        printfn "regenerated %s" rel
    | Error e -> failwithf "codegen %s: %A" rel e

/// Re-vendor the IDL-inversion golden snapshots from the authored cases, then rewrite every committed
/// generated module (their encoders embed the omit-when-default emission, so they change with the schema).
let regen () : int =
    Snapshots.regen "spike" MiniIdl.miniIdl MiniIdl.cases |> ignore

    writeGen
        "tests/Fuaran.Core.Tests/MiniGenerated.fs"
        "Fuaran.Core.Tests.MiniGenerated"
        GenSupport.Empty
        MiniIdl.miniIdl
        [ "Heading"; "Badge"; "Button"; "Metric"; "Box"; "Markdown"; "Tabs" ]

    // Phases 108/109 — the second-vocabulary slice's generated F# module, in its DECLARED wire shape
    // (bare-string `kind` discriminator, flat node envelope).
    writeGen
        "tests/Fuaran.Core.Tests/DocGenerated.fs"
        "Fuaran.Core.Tests.DocGenerated"
        GenSupport.Empty
        SecondDomainIdl.docIdl
        (SecondDomainIdl.docIdl.Kinds |> List.map _.Tag)

    // Phase 303 — the other two certification vocabularies' generated F# modules, so every
    // certification vocabulary's F# backend is COMPILED by the suite, not only regenerated. The score
    // vocabulary carries field-less (marker) kinds; the reference one is emitted under its declared
    // support document, whose named host prelude is `ReferencePrelude.fs`.
    writeGen
        "tests/Fuaran.Core.Tests/ScoreGenerated.fs"
        "Fuaran.Core.Tests.ScoreGenerated"
        GenSupport.Empty
        ScoreDomainIdl.scoreIdl
        (ScoreDomainIdl.scoreIdl.Kinds |> List.map _.Tag)

    writeGen
        "tests/Fuaran.Core.Tests/ReferenceGenerated.fs"
        "Fuaran.Core.Tests.ReferenceGenerated"
        ReferenceIdl.support.Support
        ReferenceIdl.refIdl
        (ReferenceIdl.refIdl.Kinds |> List.map _.Tag)

    // Phase 337 — the decode vectors' vocabulary, so the decode error contract is held against a
    // COMPILED generated host as well as the interpreter and the TypeScript one.
    let decodeIdl, _, _ = DecodeVectorsIdl.current ()

    writeGen
        DecodeVectorsIdl.generatedFile
        DecodeVectorsIdl.moduleName
        GenSupport.Empty
        decodeIdl
        (decodeIdl.Kinds |> List.map _.Tag)

    // Phase 347 — the refusal corners' vocabulary, so every corner is read by a COMPILED generated host.
    writeGen
        RefusalCornersIdl.generatedFile
        RefusalCornersIdl.moduleName
        GenSupport.Empty
        RefusalCornersIdl.idl
        (RefusalCornersIdl.idl.Kinds |> List.map _.Tag)

    // Phase 374 — the derivations vocabulary, with every structural derivation requested.
    match DeriveIdl.generate () with
    | Ok src ->
        File.WriteAllText(Snapshots.repoFile DeriveIdl.generatedFile, src)
        printfn "regenerated %s" DeriveIdl.generatedFile
    | Error e -> failwithf "codegen %s: %A" DeriveIdl.generatedFile e

    // Phase 377 — the reference vocabulary with the collecting decoders requested, so every support
    // channel a collecting decoder routes around is COMPILED by the suite.
    match SpecDecodersIdl.generate () with
    | Ok src ->
        File.WriteAllText(Snapshots.repoFile SpecDecodersIdl.generatedFile, src)
        printfn "regenerated %s" SpecDecodersIdl.generatedFile
    | Error e -> failwithf "codegen %s: %A" SpecDecodersIdl.generatedFile e

    0
