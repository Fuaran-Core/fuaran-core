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
            /// Declaration path (`type:Name`, `case:Union.Tag`, `field:Owner.Field`, …) → the
            /// comment lines emitted above it, markers included; a path with no entry emits none.
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
            /// Kind tag → the host projection that replaces that kind's derived record, encoder
            /// and decoder; a kind with no entry is emitted from its IDL fields.
            KindProjections: Map<string, KindProjection>
        }

        /// No docs, no splices, no refines, no projections: `fsharpModuleWith` under this record
        /// emits byte-identically to the generator before the support channel existed.
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

    /// Phase 374 — a STRUCTURAL DERIVATION the F# module emits on request: a member that follows
    /// mechanically from what the generator already knows about every kind (which fields hold
    /// nodes, which hold a given declared type, which declarations carry the message parameter,
    /// what each field defaults to, what each kind is tagged with) and that a consuming domain
    /// would otherwise write by hand. Requesting none is `fsharpModuleWith`, byte for byte. A
    /// request the vocabulary cannot honour is refused as a typed `UnsupportedConstruct`.
    [<RequireQualifiedAccess>]
    type Derivation =
        /// Public `wireTag`, `allWireTags`, `children` and `withChildren` (kids first:
        /// `withChildren (children n) n = n`), with `nodeWitness` built on them. A child is a
        /// node or node list a kind always carries; an optional node is a keyed position.
        | StructuralAccess
        /// `keyedChildren`, `withKeyedChildren` (arity-preserving) and `keyedWitness`, a
        /// `KeyedWitness` over every node position that is not a structural child: an optional
        /// node, a node inside a record, a union case, a list or a map, and the node envelope.
        /// Implies `StructuralAccess`.
        | KeyedPositions
        /// `slotsOf<T> : Node -> (string * T) list` — every kind and envelope field declared at
        /// the named enum, record or union `T` (directly, optional or in a list), by wire field
        /// name; `(string * obj) list` when `T` is a generic union.
        | SlotsOf of typeName: string
        /// `mapMsg : ('Msg -> 'Msg2) -> Node<'Msg> -> Node<'Msg2>`, rebuilding every declaration
        /// generic in the message parameter.
        | MapMsg
        /// `<Union>.fold : ('S -> U -> 'S) -> 'S -> U -> 'S` over a self-recursive union: the
        /// value and every nested value of its own type, in preorder.
        | Fold of unionName: string
        /// `<Union>.<field>` for each named field: `U -> T` where every case carries it at one
        /// type, `U -> T option` where only some do.
        | Projections of unionName: string * fieldNames: string list
        /// `default<Tag>Spec` / `default<Record>` for every kind and record whose every field has
        /// a value without the caller — the values the `mk<Kind>` constructors fill.
        | DefaultRecords
        /// `kindCategories`, `kindFieldNames`, `envelopeFieldNames` and `opFieldNames` as `Set`
        /// values.
        | VocabularyConstants
        /// Phase 377 — a COLLECTING decoder beside every short-circuiting one, answering every
        /// defect as a `DecodeError list` in a stated order (field declaration order, each
        /// member's nested defects at its position; list items by index; map entries in document
        /// order — the order is written above the collecting prelude), and the public entries
        /// over them: per kind `decode<Tag>Spec` (the first defect) and `decode<Tag>SpecAll`
        /// (every defect), per node `decodeNodeJson`, `decodeNodeJsonAll` and `decodeNodeAll`.
        /// A collecting decoder's first defect is its short-circuiting twin's defect.
        | SpecDecoders

    let private toRequest (d: Derivation) : FSharpDerive.Request option =
        match d with
        | Derivation.StructuralAccess -> Some FSharpDerive.Request.StructuralAccess
        | Derivation.KeyedPositions -> Some FSharpDerive.Request.KeyedPositions
        | Derivation.SlotsOf t -> Some(FSharpDerive.Request.SlotsOf t)
        | Derivation.MapMsg -> Some FSharpDerive.Request.MapMsg
        | Derivation.Fold u -> Some(FSharpDerive.Request.Fold u)
        | Derivation.Projections(u, fs) -> Some(FSharpDerive.Request.Projections(u, fs))
        | Derivation.DefaultRecords -> Some FSharpDerive.Request.DefaultRecords
        | Derivation.VocabularyConstants -> Some FSharpDerive.Request.VocabularyConstants
        // The decoders are the codec emitter's own, not a structural derivation's.
        | Derivation.SpecDecoders -> None

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
        FSharpCodec.fsharpModuleDerived
            (toSupport sup)
            (derivations |> List.choose toRequest)
            (derivations |> List.contains Derivation.SpecDecoders)
            moduleName
            idl
            kindTags

    /// The pre-945 entry — `fsharpModuleWith` under an empty declared-support record,
    /// emitting byte-identically to the generator before the support channel existed.
    let fsharpModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        fsharpModuleWith GenSupport.Empty moduleName idl kindTags

    /// The JSON Schema (draft 2020-12) of the vocabulary's wire.
    let jsonSchema (idl: Idl) : Result<string, CodegenError> = JsonSchema.jsonSchema idl

    /// A TypeScript value literal for an IDL value under the given wire shape.
    let internal typescriptValueWith (shape: WireShape) (v: IdlValue) : string =
        TypeScriptCodec.typescriptValueWith shape v

    /// The TypeScript structural encoder/decoder module for the named kinds.
    let typescriptModule (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        TypeScriptCodec.typescriptModule idl kindTags

    /// Phase 380 — `typescriptModule` plus the requested derivations, the TypeScript host's
    /// `fsharpModuleDerived`: the same requests, appended after the module's members with one
    /// further `export` naming them. The empty list emits exactly what `typescriptModule` does. A
    /// request is admitted exactly when the F# path admits it, and refused with the F# path's
    /// error. `MapMsg` emits nothing here: this host holds a handler slot as its sentinel's `null`
    /// and carries no message type, so there is nothing to map.
    let typescriptModuleDerived
        (derivations: Derivation list)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        TypeScriptDerived.typescriptModuleDerived
            (derivations |> List.choose toRequest)
            (derivations |> List.contains Derivation.SpecDecoders)
            idl
            kindTags

    /// A TypeScript value literal for an authored value of a declared type.
    let typescriptValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        TypeScriptDeclarations.typescriptValue idl t v

    /// The TypeScript type declarations for the named kinds.
    let typescriptDeclarations (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        TypeScriptDeclarations.typescriptDeclarations idl kindTags

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
        TypeScriptDerived.typescriptDeclarationsDerived
            (derivations |> List.choose toRequest)
            (derivations |> List.contains Derivation.SpecDecoders)
            idl
            kindTags

    /// An authored value as compilable F# source — the scaffold leg: an expression of type
    /// `Result<'T, DecodeError>` since Phase 384, a hosted slot's codec refusal its `Error`.
    let fsharpValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> = Scaffold.fsharpValue idl t v

    /// The provenance header every scaffold leg emits.
    let provenanceHeader (commentPrefix: string) (sourceWireHash: string) (actor: string) : string =
        Scaffold.provenanceHeader commentPrefix sourceWireHash actor
