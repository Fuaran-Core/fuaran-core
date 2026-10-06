module Fuaran.Core.Tests.SpecDecodersIdl

open Fuaran.Core.Idl

// ---------------------------------------------------------------------------
// Phase 377 — the collecting decoders over the support channel. The reference vocabulary under
// its declared support document exercises every channel a collecting decoder must route around:
// a hosted slot (a leaf, lifted), a case REFINE (`Text.Lookup`), a host PROJECTION (`Note`), a
// verbatim decoder splice, and — declared here, and only here — a TRANSPARENT case (`Text.Inline`
// encodes bare), so the bare arm of a collecting union decoder is compiled too.
//
// `SpecDecodersGenerated.fs` is the F# emitted from it with `Gen.Derivation.SpecDecoders`
// requested, committed and checked for drift by `IdlSpecDecoderTests`. The derivations
// vocabulary (`DeriveIdl`) requests the decoders as well; that module carries the field-shape
// laws, this one the support channel.
// ---------------------------------------------------------------------------

/// The reference vocabulary, `Text.Inline` declared transparent.
let idl: Idl =
    { ReferenceIdl.refIdl with
        Harden =
            { ReferenceIdl.refIdl.Harden with
                TransparentUnions = [ "Text", "Inline" ] } }

/// The module name the committed generated file declares.
[<Literal>]
let moduleName = "Fuaran.Core.Tests.SpecDecodersGenerated"

/// The committed generated file, repository-relative.
[<Literal>]
let generatedFile = "tests/Fuaran.Core.Tests/SpecDecodersGenerated.fs"

/// The module text the generator emits for this vocabulary with the decoders requested.
let generate () : Result<string, CodegenError> =
    Gen.fsharpModuleDerived
        ReferenceIdl.support.Support
        [ Gen.Derivation.SpecDecoders ]
        moduleName
        idl
        (idl.Kinds |> List.map _.Tag)
