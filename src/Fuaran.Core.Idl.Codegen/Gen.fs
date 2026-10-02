namespace Fuaran.Core.Idl

open Fuaran.Core.Idl.Emit

/// The source generators over an `Idl`: the F# structural layer (`fsharpTypes`,
/// `fsharpModuleWith`), the JSON schema, the TypeScript encoder and declarations, and the
/// scaffold values — each a source string a consumer compiles or publishes, refused as a
/// typed `CodegenError` wherever the vocabulary asks for something the backend cannot emit.
///
/// Phase 293 — a FACADE. The published types (`KindProjection`, `GenSupport`) are declared
/// here, and every entry point is a one-line forward to the `internal` emitter modules under
/// `Emit/` (`Core`, `Reach`, `Annotations`, `FSharpTypes`, `FSharpDefaults`, `FSharpCodec`,
/// `JsonSchema`, `TypeScript`, `Scaffold`), so the public surface is exactly this file and the
/// baseline `api/Fuaran.Core.Idl.Codegen.txt` reads it.
module Gen =


    /// Phase 945 — a per-kind HOST PROJECTION: the F# record, encoder and decoder for one
    /// node kind are supplied verbatim instead of being derived from the kind's IDL fields.
    /// The IDL fields REMAIN the wire truth (the artifact, the schema and the diff still
    /// read them); the projection changes only what the generated F# looks like — the
    /// escape for a kind whose ergonomic host shape is narrower than its wire shape
    /// (`Switch` merges the `on` / `stateKey` wire keys into one required `On` binding).
    type KindProjection =
        {
            /// Keyword-less type-group member body — the `and` keyword and RQA attribute
            /// are the assembler's.
            SpecDecl: string
            /// The full `and private enc<Tag>Spec …` member, verbatim.
            Encoder: string
            /// The full `and private dec<Tag>Spec …` member, verbatim. Since Phase 337 it
            /// answers `Result<<Tag>Spec, DecodeError>`, as the generated decoders it composes
            /// with do.
            Decoder: string
            /// The full `let mk<Tag> …` smart constructor, or None to emit none — the
            /// generated ctor would construct the IDL-derived record, which under a
            /// projection is not the record that exists.
            Mk: string option
        }

    /// Phase 945 — the declared-support channel for `fsharpModuleWith`: doc comments,
    /// verbatim splices and host projections that were previously HAND-EDITS to the
    /// generated artefact (the "hand-added ahead of the IDL backfill" regions). Everything
    /// here is versioned data beside the IDL, so the emission is reproducible and the
    /// tier sync is a byte-copy again. `Docs` is keyed by declaration path —
    /// `type:Name` / `case:Union.Tag` / `field:Owner.Field` / `enc:Name` / `dec:Name` /
    /// `encarm:Union.Tag` / `decarm:Union.Tag` — each value the comment lines VERBATIM
    /// (including their `///` or `//` markers), indented by the emitter.
    type GenSupport =
        {
            Docs: Map<string, string list>
            /// Verbatim `and …` member(s) appended to the type-recursion group.
            TypeSplice: string option
            /// Verbatim `and private …` member(s) appended to the encoder group.
            EncodeSplice: string option
            /// Verbatim `and private …` member(s) appended to the decoder group. A member that
            /// composes with a generated decoder answers `DecodeError`, as they do (Phase 337).
            DecodeSplice: string option
            /// Verbatim module-level lets emitted after the JVal accessor block.
            AccessorSplice: string option
            /// `"Union.Tag"` → the full final expression replacing `Ok(Case(…))` in that
            /// case's decoder — decode POLICY (e.g. `SetState`'s value-XOR-valueFrom) that
            /// the structural inversion cannot express. Field binder names are in scope. The
            /// expression answers `Result<Case, string>`; since Phase 337 its refusal reaches
            /// the caller as `OutOfRange` at the case's object, its sentence unchanged.
            CaseRefines: Map<string, string>
            KindProjections: Map<string, KindProjection>
        }

        static member Empty =
            { Docs = Map.empty
              TypeSplice = None
              EncodeSplice = None
              DecodeSplice = None
              AccessorSplice = None
              CaseRefines = Map.empty
              KindProjections = Map.empty }

    /// The emitters' view of the declared support, field for field.
    let private toProjection (p: KindProjection) : Core.Projection =
        { SpecDecl = p.SpecDecl
          Encoder = p.Encoder
          Decoder = p.Decoder
          Mk = p.Mk }

    let private toSupport (sup: GenSupport) : Core.Support =
        { Docs = sup.Docs
          TypeSplice = sup.TypeSplice
          EncodeSplice = sup.EncodeSplice
          DecodeSplice = sup.DecodeSplice
          AccessorSplice = sup.AccessorSplice
          CaseRefines = sup.CaseRefines
          KindProjections = sup.KindProjections |> Map.map (fun _ p -> toProjection p) }

    /// Which declarations are generic in `'Msg` — see `Core.msgCarrying`.
    let internal msgCarrying (idl: Idl) : Set<string> = Core.msgCarrying idl

    /// The F# type declarations for a whole vocabulary, from the one type emitter (Phase 293).
    let fsharpTypes (idl: Idl) : Result<string, CodegenError> = FSharpTypes.fsharpTypes idl

    /// Emit a compiling, self-contained F# encoder module (`moduleName`) for the named kinds,
    /// drawing in the enums/unions they transitively reference, under the declared support.
    /// The emitted text is LF-terminated whatever the generator was built from.
    let fsharpModuleWith
        (sup: GenSupport)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        FSharpCodec.fsharpModuleWith (toSupport sup) moduleName idl kindTags

    /// The pre-945 entry — `fsharpModuleWith` under an empty declared-support record,
    /// emitting byte-identically to the generator before the support channel existed.
    let fsharpModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        fsharpModuleWith GenSupport.Empty moduleName idl kindTags

    /// The JSON Schema (draft 2020-12) of the vocabulary's wire.
    let jsonSchema (idl: Idl) : Result<string, CodegenError> = JsonSchema.jsonSchema idl

    /// A TypeScript value literal for an IDL value under the given wire shape.
    let internal typescriptValueWith (shape: WireShape) (v: IdlValue) : string = TypeScript.typescriptValueWith shape v

    /// The TypeScript structural encoder/decoder module for the named kinds.
    let typescriptModule (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        TypeScript.typescriptModule idl kindTags

    /// A TypeScript value literal for an authored value of a declared type.
    let typescriptValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        TypeScript.typescriptValue idl t v

    /// The TypeScript type declarations for the named kinds.
    let typescriptDeclarations (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        TypeScript.typescriptDeclarations idl kindTags

    /// An authored value as compilable F# source — the scaffold leg.
    let fsharpValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> = Scaffold.fsharpValue idl t v

    /// The provenance header every scaffold leg emits.
    let provenanceHeader (commentPrefix: string) (sourceWireHash: string) (actor: string) : string =
        Scaffold.provenanceHeader commentPrefix sourceWireHash actor
