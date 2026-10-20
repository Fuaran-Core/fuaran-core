namespace Fuaran.Core.Idl.Codegen.Types

open Fuaran.Core.Idl

// Phase 524 (DECISIONS.md D125, amended) — the PUBLISHED types of the generator live in this
// namespace, ahead of every emitter, so the emitters read them directly and the modules that
// use them split one concern per file. They were nested in the `Gen` facade, which compiles
// last, and every emitter read a field-for-field mirror of them (`Core.Projection`,
// `Core.Support`, `FSharpDerive.Request`) through three converters; the mirrors are gone.

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
        /// Phase 403 — the full `and private mapMsg<Tag>Spec …` member, verbatim, or None.
        /// `Derivation.MapMsg` joins it to the derived message map's recursion group in
        /// place of the record mapper it cannot construct (the projected record is host
        /// source), so it may call `mapMsg f` and every derived `mapMsg<Decl> f`. Its
        /// signature is `('Msg -> 'Msg2) -> <Tag>Spec<'Msg> -> <Tag>Spec<'Msg2>`. With None, a
        /// message map over this kind is refused — the generator does not guess a map.
        MapMsg: string option
        /// Phase 403 — the projected record's fields at their HOST shapes (name, type,
        /// optionality), or None. Each `Name` is spelled as a wire field is, the record's
        /// member being its PascalCase (`on` → `On`), and each type names a declaration the
        /// module emits. The emissions that read a kind's fields — the node witness, the
        /// keyed walk, the slot enumerators — read these in place of the kind's wire fields.
        /// With None they refuse by name wherever they would read the projected record: the
        /// node positions when the kind's wire holds a node directly or within, the slot
        /// enumerators always, because a host record may hold a value its wire spells
        /// otherwise.
        RecordFields: IdlField list option
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

/// Phase 550 (DECISIONS.md D198) — the request for a CODEC-ONLY F# module: encoders and decoders for
/// declarations whose types the HOST declares, with no type declaration of the module's own.
type CodecOnly =
    {
        /// The unions, records and enums the module exposes, in the order their public entries
        /// (`encode<Name>`, `decode<Name>`, `decode<Name>Strict`) are printed. A generic union's
        /// entries take one encoder or decoder per type parameter, in declared order.
        Roots: string list
        /// Each declaration the roots reach → the full path of the host type it IS
        /// (`"Fuaran.Core.InvokeError"`); a generic one is applied to its declared parameters. A
        /// reached declaration with no entry is refused by name. Cases are matched and built through
        /// the module's abbreviation of the path, so the host type may require qualified access or not.
        HostTypes: Map<string, string>
    }
