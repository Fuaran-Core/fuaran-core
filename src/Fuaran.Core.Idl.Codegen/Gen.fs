namespace Fuaran.Core.Idl

open Fuaran.Core.Idl.Codegen.Types
open Fuaran.Core.Idl.Emit

/// The source generators over an `Idl`: the F# structural layer (`fsharpTypes`,
/// `fsharpModuleWith`), the JSON schema, the TypeScript encoder and declarations, and the
/// scaffold values — each a source string a consumer compiles or publishes, refused as a
/// typed `CodegenError` wherever the vocabulary asks for something the backend cannot emit.
///
/// Phase 293 — a FACADE: every entry point is a one-line forward to the `internal` emitter
/// modules under `Emit/`, each over the one `Decl` resolution. The types it takes and answers
/// (`GenSupport`, `KindProjection`, `Derivation`) are declared in `Fuaran.Core.Idl.Codegen.Types`
/// since Phase 524, ahead of every emitter, so no emitter reads a mirror of them.
module Gen =

    /// Which declarations are generic in `'Msg` — see `Decl.msgCarrying`.
    let internal msgCarrying (idl: Idl) : Set<string> = Decl.msgCarrying idl

    /// The F# type declarations for a whole vocabulary, from the one type emitter (Phase 293).
    let fsharpTypes (idl: Idl) : Result<string, CodegenError> =
        FSharpTypes.fsharpTypes (Decl.declared idl)

    /// Emit a compiling, self-contained F# encoder module (`moduleName`) for the named kinds,
    /// drawing in the enums/unions they transitively reference, under the declared support.
    /// The emitted text is LF-terminated whatever the generator was built from.
    let fsharpModuleWith
        (sup: GenSupport)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        FSharpCodec.fsharpModuleWith sup moduleName (Decl.resolve idl sup.KindProjections kindTags)

    /// `fsharpModuleWith` plus the requested structural derivations (Phase 374) and, under
    /// `Derivation.SpecDecoders`, the collecting decoders and public per-spec entries (Phase 377),
    /// appended after the module's existing members. The empty list emits exactly what
    /// `fsharpModuleWith` does.
    let fsharpModuleDerived
        (sup: GenSupport)
        (derivations: Derivation list)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        FSharpCodec.fsharpModuleDerived sup derivations moduleName (Decl.resolve idl sup.KindProjections kindTags)

    /// The pre-945 entry — `fsharpModuleWith` under an empty declared-support record,
    /// emitting byte-identically to the generator before the support channel existed.
    let fsharpModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        fsharpModuleWith GenSupport.Empty moduleName idl kindTags

    /// The JSON Schema (draft 2020-12) of the vocabulary's wire.
    let jsonSchema (idl: Idl) : Result<string, CodegenError> =
        JsonSchema.jsonSchema (Decl.declared idl)

    /// A TypeScript value literal for an IDL value under the given wire shape.
    let internal typescriptValueWith (shape: WireShape) (v: IdlValue) : string =
        TypeScriptCodec.typescriptValueWith shape v

    /// The TypeScript structural encoder/decoder module for the named kinds.
    let typescriptModule (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        TypeScriptCodec.typescriptModule (Decl.resolve idl Map.empty kindTags)

    /// Phase 380 — `typescriptModule` plus the requested derivations, the TypeScript host's
    /// `fsharpModuleDerived`: the same requests, appended after the module's members with one
    /// further `export` naming them. The empty list emits exactly what `typescriptModule` does. A
    /// request is admitted by the one derivation admission the F# path performs, over this host's
    /// resolution — which declares no host projections, because a projection is F# source — and
    /// refused with that admission's error (Phase 511). `MapMsg` emits nothing here: this host holds a handler slot as its sentinel's `null`
    /// and carries no message type, so there is nothing to map.
    let typescriptModuleDerived
        (derivations: Derivation list)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        TypeScriptDerived.typescriptModuleDerived derivations (Decl.resolve idl Map.empty kindTags)

    /// A TypeScript value literal for an authored value of a declared type.
    let typescriptValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        TypeScriptDeclarations.typescriptValue idl t v

    /// The TypeScript type declarations for the named kinds.
    let typescriptDeclarations (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        TypeScriptDeclarations.typescriptDeclarations (Decl.resolve idl Map.empty kindTags)

    /// Phase 381 — `typescriptDeclarations` plus one declaration per member
    /// `typescriptModuleDerived` exports under the same requests, so the derived module and its
    /// declaration file agree: every exported name is declared and every declared name exported.
    /// The empty list emits exactly what `typescriptDeclarations` does; a request is admitted and
    /// refused exactly as `typescriptModuleDerived` admits and refuses it. `MapMsg` declares
    /// nothing, as the module exports nothing for it, and the file's header comment says why.
    let typescriptDeclarationsDerived
        (derivations: Derivation list)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        TypeScriptDerived.typescriptDeclarationsDerived derivations (Decl.resolve idl Map.empty kindTags)

    /// An authored value as compilable F# source — the scaffold leg: an expression of type
    /// `Result<'T, DecodeError>` since Phase 384, a hosted slot's codec refusal its `Error`.
    let fsharpValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> = Scaffold.fsharpValue idl t v

    /// The provenance header every scaffold leg emits.
    let provenanceHeader (commentPrefix: string) (sourceWireHash: string) (actor: string) : string =
        Scaffold.provenanceHeader commentPrefix sourceWireHash actor
