namespace Fuaran.Core.Idl.Codegen.Types

open Fuaran.Core

// Phase 524 (DECISIONS.md D125, amended) — the stability classifier's change vocabulary: the `Change` a snapshot pair yields, its wire
// `Severity`, its F# consequence, and the `Verdict` over a pair, lifted out of the
// `Diff` module so the classifier splits one concern per file (`Diff/*.fs`).

/// One difference between two snapshots. `changes` emits them sorted by a fixed rank
/// and key, so the same pair always yields the same list; `classify` grades each.
type Change =
    /// The `idl.json` encoding version moved. Graded host-surface, but a sign to
    /// reconcile the two encodings before trusting any other row.
    | ArtifactVersionChanged of before: int * after: int
    /// The declared wire shape moved (Phases 108/109) — `discriminator/nodeEnvelope/keyOrder`.
    | WireShapeChanged of before: string * after: string
    /// The declared HARDENING vocabulary moved (Phase 116) — which kind the codegen
    /// trust boundary gates, what it mints in its place, which cases it sanitises,
    /// and which unions have a transparent case.
    | HardenPolicyChanged of before: string * after: string
    /// A new `$type` branch: additive on the wire, an exhaustive-match break in F#.
    | KindAdded of tag: string
    /// A `$type` retired: every document using it is invalidated — a wire break.
    | KindRemoved of tag: string
    /// Inferred, never declared — see `renamePairs`. Reported ALONGSIDE the
    /// add + remove it explains, not instead of them.
    | KindRenamed of before: string * after: string
    /// A kind present in both revisions changed `category` — metadata that is never
    /// serialised, so host-surface only.
    | KindCategoryChanged of tag: string * before: string * after: string
    /// A new tree-op: additive; no generated F# shape exists for ops yet.
    | OpAdded of tag: string
    /// A tree-op retired: every persisted op stream carrying it stops decoding — a wire
    /// break.
    | OpRemoved of tag: string
    /// A new value-union: additive, and moves no generated shape (nothing referenced it).
    | UnionAdded of name: string
    /// A value-union retired: a wire break, and a type-name-reference break in F#.
    | UnionRemoved of name: string
    /// A case added to a union present in both revisions: additive, at the same
    /// wire-coupling cost as a new kind.
    | UnionCaseAdded of union: string * case: string
    /// A case removed from a union present in both revisions: a wire break.
    | UnionCaseRemoved of union: string * case: string
    /// The union's type-parameter list moved: host-surface on the wire (no type arguments
    /// are carried), but every reference at the old arity breaks.
    | UnionParamsChanged of name: string * before: string list * after: string list
    /// The union's bare-encoded case moved, appeared or vanished: every document using
    /// it changes bytes — a wire break.
    | UnionTransparencyChanged of name: string * before: string option * after: string option
    /// The union's tag-naming rule moved (Phase 550): every case's wire tag is re-derived — a
    /// wire break.
    | UnionTagRuleChanged of name: string * before: string * after: string
    /// A union case's case-level host codec was declared, retired or moved (Phase 550): the F#
    /// host reads and writes the case through other code, which the artifact cannot see.
    | UnionCaseHostedChanged of union: string * case: string * before: string * after: string
    /// A new closed string set: additive.
    | EnumAdded of name: string
    /// A closed string set retired: a wire break.
    | EnumRemoved of name: string
    /// A wire string added to an existing set: additive, though a decoder predating it
    /// refuses the value (host lag).
    | EnumCaseAdded of enumName: string * wire: string
    /// A wire string removed from an existing set: documents carrying it stop
    /// validating — a wire break.
    | EnumCaseRemoved of enumName: string * wire: string
    /// The generated F# case names moved while the wire strings did not: host-surface,
    /// but every match arm naming an old case breaks.
    | EnumHostMappingChanged of name: string * before: string list * after: string list
    /// A new non-discriminated record: additive.
    | RecordAdded of name: string
    /// A record retired: a wire break.
    | RecordRemoved of name: string
    /// A field new to an existing owner. Its optionality class decides the severity:
    /// `required` breaks the wire (old documents lack it), `hostOnly` is host-surface,
    /// anything else is additive.
    | FieldAdded of Owner * FieldSnap
    /// A field gone from an existing owner: a wire break, or host-surface for a
    /// `hostOnly` field.
    | FieldRemoved of Owner * name: string * was: FieldSnap
    /// The field's wire type moved: a wire break, except an `int` → `float` widening
    /// (additive) or a move across an erased `hosted` / `json` / `opaque` slot at any
    /// depth (undecided).
    | FieldTypeChanged of Owner * name: string * before: FieldSnap * after: FieldSnap
    /// The generated DECLARATION moved; the wire did not. `TFn`'s F#
    /// signature, a `THosted` slot's codec expressions.
    | FieldHostSurfaceChanged of Owner * name: string * before: HostSurface * after: HostSurface
    /// The field's optionality (`Opt`, default value included) moved. Additive only for
    /// `required` → `optional`; every other move, a changed identity default among them,
    /// is a wire break.
    | FieldOptionalityChanged of Owner * name: string * before: FieldSnap * after: FieldSnap
    /// The declared ANNOTATIONS on a field moved (Phase 113) — `""` on either
    /// side means "declared none". Never a wire event: an annotation changes no
    /// encoding in either direction.
    | FieldAnnotationsChanged of Owner * name: string * before: string * after: string
    /// The member's retired spellings moved (Phase 539). Additive when every alias the old
    /// revision accepted is still accepted; a wire break when one is withdrawn — a document
    /// written in that spelling no longer reads.
    | FieldAliasesChanged of Owner * name: string * before: string list * after: string list
    /// The member's presence condition appeared, moved or vanished (Phase 539): the canonical
    /// bytes of every document in the configurations it governs move — a wire break.
    | FieldPresenceChanged of Owner * name: string * before: string * after: string
    /// The declared annotations on a union CASE moved (Phase 113).
    | UnionCaseAnnotationsChanged of union: string * case: string * before: string * after: string
    /// The declared annotations on a KIND or a tree-OP itself moved (Phase 119) —
    /// the vocabulary-growth charter's retirement half, and the one marking that can
    /// say a whole node kind is going away.
    ///
    /// Carried on [[Owner]], which is always `OKind` or `OOp` here — the same
    /// subject vocabulary [[FieldAnnotationsChanged]] already uses, so "kind X" and
    /// "op X" read alike wherever a change is described.
    | KindAnnotationsChanged of Owner * before: string * after: string
    /// The declared annotations on an ENUM CASE moved (Phase 119). `wire` is the
    /// case's wire string, which is how the artifact keys them and what a
    /// third-party reader sees.
    | EnumCaseAnnotationsChanged of enumName: string * wire: string * before: string * after: string
    /// The lenient spellings of an enum case moved (Phase 539), graded as [[FieldAliasesChanged]].
    | EnumCaseAliasesChanged of enumName: string * wire: string * before: string list * after: string list
    /// A smart-constructor (authoring) default appeared — not the wire-visible
    /// omit-at-default. Additive; `value` is canonical JSON.
    | DefaultAdded of kind: string * field: string * value: string
    /// An authoring default was withdrawn: the wire is unchanged, but host code that
    /// relied on it emits a different document — breaks emitters.
    | DefaultRemoved of kind: string * field: string * value: string
    /// An authoring default's value moved: every authoring site that omitted the field
    /// now emits different bytes — breaks emitters.
    | DefaultChanged of kind: string * field: string * before: string * after: string
    /// Phase 293 — a declared SUPPORT entry (`support.json`) moved: a doc block, a verbatim
    /// splice, a case refine, a kind projection or the host prelude, keyed as
    /// [[Snapshot.Support]] keys them. `None` on a side means the entry is absent there.
    /// Never a wire event — support is host-language source — and the one row that used
    /// to read `unchanged` when only the support beside the vocabulary had moved.
    | SupportChanged of key: string * before: string option * after: string option
    /// Phase 548 — the declared annotations on a whole TYPE moved: a union, a record or an
    /// enum (Phase 542), named by `family` (`"union"` / `"record"` / `"enum"`) and `name`.
    /// Graded exactly as a kind's or a member's marking is ([[KindAnnotationsChanged]]):
    /// never a wire event, `Additive` on a first marking, `HostSurfaceOnly` otherwise.
    | TypeAnnotationsChanged of family: string * name: string * before: string * after: string
    /// Phase 474 — a record's or a union's inline layout moved (an inline record, a presence-keyed
    /// union's rules): the members move between the holder's object and an object of their own,
    /// or a case is read from other keys — a wire break. `""` is the declaration's own object.
    | TypeLayoutChanged of family: string * name: string * before: string * after: string
    /// The member was declared a keyed position, or stopped being one (Phase 494): never a wire
    /// event, but the generated structural witness moves the slot between `children` and the keyed
    /// walk — a regeneration-visible change for code walking either. Appended last, so every
    /// earlier case keeps its tag.
    | FieldKeyedChanged of Owner * name: string * before: bool * after: bool

/// A change's wire verdict — what an OLD document, or an old emitter, meets under the
/// new vocabulary. Independent of the F# consequence reported beside it.
type Severity =
    /// Every previously-valid document stays valid and every
    /// previously-conformant emitter stays conformant.
    | Additive
    /// Every old document still decodes, to the same value and the same bytes,
    /// but host code written against the old contract now emits a different
    /// document (or stops compiling) — an authoring default removed or moved.
    /// Minor on paper, a break in practice.
    ///
    /// NOT the class of a required field arriving, or of a field becoming
    /// required (Phase 304): there the old documents themselves are refused,
    /// which is `BreakingWire`. A class is a fact about what an old document
    /// does under the new vocabulary, never about the emitter alone.
    | BreakingForEmitters
    /// A `/v2/` major wire event (`VOCABULARY.md` §4.2): a document that was
    /// valid is not, or its bytes moved.
    | BreakingWire
    /// Not observable on the wire at all — a generated-declaration change.
    | HostSurfaceOnly
    /// **The artifact cannot decide this one.** Reserved for changes that
    /// cross an ERASED slot (`hosted` / `json` / `opaque`), whose admitted
    /// values the artifact deliberately does not state — a `THosted` slot's
    /// content "is the host codec's business, not the schema's" (Fuaran.Core.Idl's Encode.fs), so
    /// nothing in `idl.json` says whether the two sides admit the same set.
    ///
    /// This case exists because the Phase 700 retroactive validation found
    /// it: the classifier called Phase 707's `liveRegion` re-model
    /// (`THosted` → `TEnum`) a breaking wire change, and it was not — the
    /// wire strings were already the enum's three and the corpus is
    /// byte-identical either side. Reporting `BREAKING` there is not
    /// conservative, it is wrong, and a classifier that cries wolf on the
    /// commonest kind of IDL tidy-up gets skimmed. Saying "I cannot see
    /// inside that slot, here is what to check" is the honest verdict and
    /// the useful one.
    | Unclassifiable

/// One change with its wire verdict and the reasoning behind it — a report row.
type Classification =
    {
        /// The graded change, carried so a row can be summarised, and joined against a
        /// host roster by `obligations`, on its own.
        Change: Change
        /// Its wire severity, from the change's rule in the descriptor table.
        Severity: Severity
        /// Report prose explaining why the severity applies, naming the subject; for an
        /// `Unclassifiable` row it also says what to check by hand.
        Rationale: string
        /// The rule document(s) the verdict rests on; `—` where none is cited.
        Citation: string
    }

/// What a classified change does to a CONSUMER'S F# SOURCE compiled against the
/// generated structural layer. A different question from what it does to a
/// document, and the two answers diverge routinely — which is the whole reason
/// this axis is reported beside `Severity` rather than derived from it.
///
/// Each case is named for the SITE that stops working, because that is what a
/// consumer reads in the compiler output.
type FSharpConsequence =
    /// **Construction sites.** `Gen.fsharpTypes` emits a kind, a record and a
    /// union case as F# RECORDS, and a record literal must name every field —
    /// so a field arriving, leaving or changing type breaks every full literal
    /// that builds one: `FS0764` ("No assignment given for field") on an
    /// arrival, `FS1129` (no such field) on a departure, `FS0001` on a type
    /// move.
    ///
    /// **Independent of optionality.** `string option` is still a field the
    /// literal must name, so an OPTIONAL field added lands here too while its
    /// wire severity is `Additive`. That divergence is the row this table
    /// exists for: reading the wire verdict alone says a consumer repins
    /// without source changes, and it does not.
    | FullLiteralConstruction
    /// **Match sites.** An enum, a value-union and the per-vocabulary node-kind
    /// discriminator each emit as a closed F# DU, so a case arriving makes every
    /// exhaustive `match` incomplete — `FS0025`, a WARNING under this repo's
    /// `TreatWarningsAsErrors=false` and an error wherever a consumer sets it,
    /// and a `MatchFailureException` at run time on the first value carrying the
    /// new case — and a case leaving makes the arm naming it undefined
    /// (`FS0039`).
    | ExhaustiveMatch
    /// **Reference sites.** A whole generated TYPE left, or its type parameters
    /// moved, so a consumer naming it no longer resolves (`FS0039`) or applies
    /// the wrong arity. A type ARRIVING is not this: nothing could have
    /// referenced it.
    | TypeNameReference
    /// **Neither — until the package slot is reused.** The generated shape
    /// moved, so a consumer whose extracted package cache still holds the
    /// previous assembly for the SAME version compiles against one shape and
    /// runs against the other. It surfaces as an `InvalidCastException` thrown
    /// from code that type-checked, at the first value crossing the boundary,
    /// with nothing in the source to read.
    ///
    /// It ACCOMPANIES the three classes above rather than replacing them: those
    /// are what a clean rebuild reports, this is what a stale restore reports
    /// instead of them. Which is why a shape change wants a fresh version
    /// rather than a repack of a slot consumers already hold.
    | StalePackageSlot
    /// The generated shape did not move: the change is on the wire only, in the
    /// host-surface declarations, in an annotation, or in an authoring default.
    | NoGeneratedShapeChange
    /// The change crosses an ERASED slot (`hosted` / `json` / `opaque`), so
    /// nothing in the artifact says whether the generated shape moved.
    /// Reported, never guessed — `Unclassifiable`'s counterpart on this axis,
    /// and for the same reason: a confident wrong answer on the commonest kind
    /// of IDL tidy-up gets the whole report skimmed.
    | GeneratedShapeUnreadable

/// The whole classification of one revision pair: the rows, the wire evolution,
/// the F# consequence set, and the two prose drafts the report already emitted.
type Verdict =
    {
        /// Phase 524 — the artifact ENCODING versions of the two revisions, `(before, after)`:
        /// what the report heads its body with, so the report renders a verdict and reads no
        /// snapshot. A move is also an `ArtifactVersionChanged` row.
        Encoding: int * int
        /// Every classified row, in `changes` order; empty means the two revisions
        /// describe the same contract (`VerdictClass.Unchanged`).
        Changes: Classification list
        /// The wire evolution as `Versioning.classify` decides it — the input
        /// `Versioning.bump` takes.
        Evolution: Versioning.Evolution
        /// The rows the artifact cannot decide. Non-empty means no profile
        /// answer is available — NOT that the answer is "additive".
        Undecided: Classification list
        /// At least one row keeps every existing document valid AND stops a
        /// conformant emitter conforming. Carried separately because the profile
        /// minor cannot express it — see `evolution`.
        BreaksEmitters: bool
        /// Every F# consequence class any row exhibits, de-duplicated, in table
        /// order.
        FSharpConsequences: FSharpConsequence list
        /// The roadmap front-matter draft — `stabilityImpact`.
        StabilityImpact: string
        /// The prose wire-profile recommendation — `profileBump`.
        ProfileAdvice: string
    }

/// The one-word class a gate branches on.
[<RequireQualifiedAccess>]
type VerdictClass =
    /// No rows: the two revisions describe the same contract.
    | Unchanged
    /// Rows, none of them observable on the wire.
    | HostSurface
    /// Every wire-observable row is additive.
    | Additive
    /// At least one row breaks the wire, or breaks emitters.
    | Breaking
    /// At least one row the artifact cannot decide. Takes precedence over every
    /// other class: an undecided row makes the whole verdict undecided, because
    /// reporting the decidable remainder as the answer is how a `/v2/` event
    /// gets published as a minor.
    | Undecided

/// What `bumpProfile` answers: a profile to publish, or the undecided rows that stop one
/// being named.
[<RequireQualifiedAccess>]
type Bump =
    /// Every row was decided: the base profile under `Versioning.bump` of the verdict's
    /// evolution (unchanged when nothing on the wire moved).
    | Bumped of Versioning.Capability
    /// At least one row crosses an erased slot; `rows` are those rows, to check by hand
    /// before any profile can be named.
    | Undecided of rows: Classification list
