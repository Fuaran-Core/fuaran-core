namespace Fuaran.Core.Idl.Codegen.Types

open Fuaran.Core.Idl

// Phase 524 (DECISIONS.md D125, amended) — the F* target's published types, lifted out of the
// `FStarTarget` module so its sections split one concern per file (`FStar/*.fs`). The partition
// verdict is `KindVerdict` here (it was `FStarTarget.Verdict`), because the namespace also holds
// the stability classifier's `Verdict`.

/// The F* target's verdict on one kind: whether it can model it, and why not when it cannot.
type KindVerdict =
    {
        /// The kind's tag; `partition` yields one verdict per kind, in declaration order.
        Tag: string
        /// The described `CodegenError` explaining why the target cannot model the kind,
        /// or `None` when it can; the emitted header prints it beside the refused kind's tag.
        Refusal: string option
    }

/// Where a generated model COMES FROM, for the emitted header — so the artefact says what
/// it was generated from, what regenerates it and what a theorem over it is a property
/// OF, and a reader of the `.fst` never has to guess which vocabulary the theorem is
/// about (fuaran-core Phase 173: the same generator now serves the engine's certification
/// vocabulary in this repository and a domain's vocabulary in the domain's, and the two
/// headers must not tell the same story). Pre-wrapped lines, indented by the emitter,
/// because the caller knows its own paths and its own remedy and the generator does not.
type Provenance =
    {
        /// The first paragraph: what the model is generated from, what regenerates it, and
        /// what holds the committed file to a fresh generation. Follows "<module> — generated
        /// from" on the header's first line, under the module's stamp (Phase 505).
        Origin: string list
        /// What is proved here is a property of ... — the clause that closes "WHAT THIS IS
        /// NOT". Names the vocabulary, or the backend, or whatever the caller certifies.
        Proves: string list
    }

/// Stock `Provenance` values for callers that have no named source of their own.
[<RequireQualifiedAccess>]
module Provenance =
    /// The provenance the un-suffixed entry points emit: honest about the one thing the
    /// generator knows, which is that an IDL was supplied and the committed file has to be
    /// held to it. A caller with a named source and a named remedy passes its own.
    let supplied: Provenance =
        { Origin =
            [ "the IDL its caller supplied. DO NOT EDIT: regenerate it from that IDL and hold the"
              "committed file to a fresh generation — an IDL that moves without a regeneration is"
              "VOCABULARY DRIFT." ]
          Proves = [ "VOCABULARY the IDL declares." ] }

/// One generated model's vectors for [[vectorsModule]] (Phase 303).
type VectorModel =
    {
        /// The generated model module the facts are about (`DocVocabulary`, …).
        Model: string
        /// The model module's abbreviation (capitalised, as F* requires); its lower-case
        /// form prefixes the bindings.
        Prefix: string
        /// The vocabulary the model was generated from.
        Idl: Idl
        /// The interpreter's canonical bytes, one node per vector.
        Wires: string list
    }
