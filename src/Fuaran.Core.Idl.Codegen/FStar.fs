namespace Fuaran.Core.Idl

open Fuaran.Core
open Fuaran.Core.Idl.Emit
open Fuaran.Core.Idl.Codegen.Types

// ---------------------------------------------------------------------------
// The F* PROOF-MODEL target (Phase 150) — the fourth backend of `Fuaran.Core.Idl.Codegen`,
// beside the F# structural layer, the TypeScript encoder and the JSON schema.
//
// WHAT IT EMITS, AND WHAT IT DELIBERATELY DOES NOT. It emits a PROOF MODEL: an F*
// module carrying a vocabulary's types, its discriminated encoder and its
// tag-dispatch decoder over Phase 135's `jval` value model and the `Decode`
// combinators modelled beside it — plus a second module carrying the round-trip and
// totality THEOREMS over exactly those definitions. It is not a host backend: no
// runtime package gains code, no host's decoder is replaced, and no build anywhere
// gains a generation step. The one artefact it produces is checked by the prover and
// read by nobody at runtime.
//
// WHY THE THEOREMS ARE GENERATED TOO. Phase 135 proved `decode (encode n) == Ok n` for
// a hand-written four-case reference vocabulary. The real vocabulary is an `idl.json`
// with dozens of kinds, and a theorem about the reference one says nothing about the
// kind that landed last week. A hand-written proof over the real vocabulary would say
// nothing about the one that lands next week either — so the proof script is emitted by
// the same walk that emits the model, and a new kind re-proves itself at the next
// `check.ps1` rather than waiting for a hand-written clause.
//
// WHY MONOMORPHISATION (the measured decision — see the module's own header). The IDL's
// `Binding<'T>` is generic and nearly every kind reaches it. Emitting it as a PARAMETRIC
// F* type would force an encoder/decoder pair to be threaded as a value argument, and the
// round-trip lemma would then need a higher-order hypothesis relating them — an induction
// over an unknown application. Instantiating it once per reached argument keeps every
// definition and every lemma FIRST-ORDER, which is what makes the emitted proof script a
// mechanical shape rather than a research problem.
//
// THE REFUSAL IS TYPED, like every other refusal in this package: an IDL construct with no
// wire-level meaning in the model (a closure in a wire-visible slot, a host codec whose
// wire is not JSON, an op or a bare kind slot) is a `CodegenError.UnmodellableInFStar`
// naming the construct and where it was reached — never a silently dropped member, which
// would make the theorem a statement about a document nobody sends.
//
// ONE CONCERN PER FILE (Phase 524, DECISIONS.md D125 amended). The published types (`Provenance`,
// `KindVerdict`, `VectorModel`) live in `Fuaran.Core.Idl.Codegen.Types`; the model and its walk
// (`FStar/Model.fs`), the vocabulary emitter (`FStar/Vocabulary.fs`), the proofs emitter
// (`FStar/Proofs.fs`) and the vectors (`FStar/Vectors.fs`) are internal modules ahead of this
// facade, which carries the published entries and their contracts and forwards to them. Until
// Phase 524 this was one file over three thousand lines, because the types were nested here.
// ---------------------------------------------------------------------------

/// The F* proof-model backend. `Gen` emits source a consumer compiles and ships; this
/// emits source a PROVER checks, which is why it is a module of its own rather than a
/// fourth function beside the three in `Gen`.
[<RequireQualifiedAccess>]
module FStarTarget =

    /// Every kind of the vocabulary, each with the reason the target cannot model it, or
    /// `None` when it can. The envelope is common to all of them, so a vocabulary whose
    /// node envelope is unmodellable has no expressible kind at all and says so on each.
    /// Each verdict is the model of that kind ALONE (Phase 525: a projection of the one
    /// resolution per selection, where it used to be the target's own walk per kind).
    let partition (idl: Idl) : KindVerdict list = FStarModel.partition idl

    /// Why a kind the target CAN express is nonetheless outside the proof vocabulary — the
    /// declared types it would add beyond the node envelope's own closure. Empty when it is
    /// inside it (or cannot be expressed at all, which `partition` already answers).
    /// [[proofKinds]] filters on this answer being empty, so the set difference lives in one
    /// place (Phase 348).
    let beyondEnvelope (idl: Idl) (tag: string) : string list = FStarModel.beyondEnvelope idl tag

    /// The kinds the PROOF VOCABULARY covers: every kind the target can express **that
    /// introduces no declared type beyond the node envelope's own closure**, in the
    /// vocabulary's declaration order.
    ///
    /// **The second clause is a cost control, and it is stated as one.** The proof leg checks
    /// every module three times from a cold cache with every query proved three times over
    /// varying seeds, and the emitted model's cost turns superlinear in the size of the mutual
    /// family: measured on the pinned prover, the whole expressible vocabulary (42 of this
    /// corpus's 43 kinds) did not finish a single check in twenty-five minutes, where the set
    /// this rule selects costs about what one kind costs — because the node envelope's closure
    /// is paid by ANY kind at all, and a kind that reaches only types already in it adds one
    /// constructor to three definitions and nothing else.
    ///
    /// It is a RULE rather than a list, which is the property that matters: a kind added to the
    /// IDL over types the model already carries enters the proof vocabulary at the next
    /// regeneration and re-proves itself, and one that brings a new object of its own is NAMED
    /// in the emitted header with the reason. Widening it is a measured decision, not an edit —
    /// see the `vocabulary-generated-model` row of Fuaran.Core's `proofs.json`.
    ///
    /// A kind is in exactly when it is expressible and [[beyondEnvelope]] answers it empty.
    let proofKinds (idl: Idl) : string list = FStarModel.proofKinds idl

    /// RE-PURPOSED by Phase 182, not retired: it was the count at which the round trip was split
    /// one lemma per presence PATTERN, and it is now the count at which the LINEAR per-member
    /// split is used instead of proving the whole constructor in one query — and, since Phase 204,
    /// the count at which the MODEL binds each conditional member's suffix once (8a below) rather
    /// than writing the tail into both arms of its test. Two, so that both are exercised — and
    /// proved to discharge — by the certification set itself (the reference vocabulary's `Embed`
    /// and its node envelope both carry exactly two), not first met at an adopter's scale. A
    /// constructor below it is still proved in one query, which Phase 182's k=5 measurement shows
    /// is the cheaper shape while it holds, and still encodes inline: with one conditional member
    /// the tail is written twice, a constant factor and not an exponent.
    /// Phase 204 — a constructor whose member list is emitted as bound SUFFIXES (8a below).
    let presenceSplitAt = FStarVocabulary.presenceSplitAt

    /// The whole model module, with its header naming where it came from.
    let vocabularyModuleFrom
        (provenance: Provenance)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        FStarVocabulary.vocabularyModuleFrom provenance moduleName idl kindTags

    /// The whole model module under the generator's own provenance — the entry point a caller
    /// with no named source uses. `vocabularyModuleFrom` is the one this repository calls,
    /// because its source is named (fuaran-core Phase 173).
    let vocabularyModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        FStarVocabulary.vocabularyModule moduleName idl kindTags

    /// The theorems over a generated model — same IDL, same kind selection, or the two files
    /// are about different vocabularies — with the header naming where the model came from.
    let proofsModuleFrom
        (provenance: Provenance)
        (moduleName: string)
        (modelName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        FStarProofs.proofsModuleFrom provenance moduleName modelName idl kindTags

    /// The theorems over a generated model under the generator's own provenance — the entry
    /// point a caller with no named source uses. `proofsModuleFrom` is the one this repository
    /// calls, because its source is named (fuaran-core Phase 173).
    let proofsModule
        (moduleName: string)
        (modelName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        FStarProofs.proofsModule moduleName modelName idl kindTags

    /// The interpreter's vectors as NORMALISER facts over generated models (Phase 303): one
    /// top-level `<prefix>_vectors_agree` per model, asserting for every vector that the model's
    /// `enc_node`, applied to the model value of what the interpreter DECODES the bytes to,
    /// normalises to the `jval` the bytes PARSE to — the generated model and the reference
    /// interpreter writing the same object, member for member and key for key. The model's
    /// numeric carriers are instantiated at `num = int`, `flt = string` (a float as its canonical
    /// layout), so the facts see structure, presence and key order, and not the float layout or
    /// the WIRE_FORMAT §7 sentinels. A wire the interpreter refuses, or that holds what the model
    /// cannot express (an unmodelled kind, a non-finite float, a string F* cannot spell, a
    /// transparent union), is REFUSED naming the vector — never dropped; the caller selects.
    let vectorsModule (moduleName: string) (models: VectorModel list) : Result<string, CodegenError> =
        FStarVectors.vectorsModule moduleName models
