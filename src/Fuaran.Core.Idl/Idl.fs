namespace Fuaran.Core.Idl

open Fuaran.Core

// ---------------------------------------------------------------------------
// Phase 316 — IDL inversion spike.
//
// The IDL is the *canonical source* a host's structural layer is generated from:
// today F# `Types.fs` is the root and `schema.json` is derived from it; the
// inversion makes a small typed declaration the root and generates the structural
// layer (types + codec + schema + defaults) per host. This module is the minimal
// proof surface — the IDL model, a schema-driven encoder, and an illustrative
// F#-type emitter — enough to prove byte-identity against the wire corpus.
//
// The encoder builds a `Fuaran.Core.JVal` and renders it through the shared
// `Canon` renderer (documented byte-identical to the UI host's `CanonicalJson`),
// so the spike only has to prove the *structural* generation is faithful — the
// canonical number/key/escape rules are inherited, not re-implemented.
// ---------------------------------------------------------------------------

/// The HOST signature of a function-typed slot (Phase 689) — what the generated
/// F# / TypeScript declaration should say the slot's type is.
///
/// A closure is invisible to the wire: the encoder emits the fixed `"<closure>"`
/// sentinel without ever reading the value, and a decoder checks only that the slot
/// holds that sentinel (Phase 347: the interpreter and both generated hosts alike).
/// That is exactly why the slot's HOST type is free — nothing downstream of the
/// declaration depends on it. [[TClosure]] takes the cheapest option and erases the
/// slot to `unit`; [[TFn]] declares the real signature instead, which is what lets
/// the generated layer BE the authoring type rather than a projection of it.
///
/// `FSharp` may mention `'Msg`; a type transitively containing such a slot is
/// emitted generic in `'Msg` (see `Gen.msgCarrying`).
///
/// `Placeholder` is the F# expression the DECODER puts in the slot. A closure
/// cannot be reconstructed from `"<closure>"` — there is nothing on the wire to
/// rebuild it from — so a decoded tree is the storage shape (`'Msg = obj`, the
/// tier's own `decodeNodeObj` / `WireTree` boundary), and the placeholder is what
/// stands in until a host re-attaches behaviour. It is written at `'Msg = obj`
/// for that reason.
type ClosureSig =
    {
        /// The slot's F# type text, spliced verbatim into the generated declaration
        /// (e.g. `int -> 'Msg`).
        FSharp: string
        /// The slot's TypeScript type text, spliced verbatim into the generated
        /// declaration; it plays no part in encoding or decoding.
        TypeScript: string
        /// The F# expression a decoded slot holds, written at `'Msg = obj`.
        Placeholder: string
    }

/// The structural type of a field's value on the wire.
type IdlType =
    /// A JSON string, carried as-is.
    | TStr
    /// A JSON integer in the 32-bit range; a fractional token is refused.
    | TInt
    /// A JSON `true` / `false`.
    | TBool
    /// A JSON number. An integer token is accepted on both encode and decode, and the
    /// quoted tokens `"NaN"`, `"Infinity"` and `"-Infinity"` read back as the non-finite
    /// value (WIRE_FORMAT §7).
    | TFloat
    /// A bare string from the named [[IdlEnum]]'s closed set, checked against its WIRE
    /// strings rather than its host case names.
    | TEnum of enumName: string
    /// A discriminator-tagged object of the named [[IdlUnion]], applied to exactly as many
    /// type arguments as it declares parameters (any other count is refused).
    | TUnion of unionName: string * args: IdlType list
    /// A reference to a type parameter of the enclosing union. Substituted away at every
    /// instantiation; one that reaches the encoder or decoder unsubstituted is refused.
    | TVar of paramName: string
    /// A node — `id` plus a kind body tagged from [[Idl]]'s `Kinds`, laid out as the
    /// vocabulary's [[NodeEnvelopeShape]] declares, with any declared envelope fields.
    | TNode
    /// A JSON array whose every element is a value of the item type, kept in order.
    | TList of IdlType
    /// A function-typed field (Binding accessor, `Action` callback, `onChange`
    /// handler, column projection): unobservable on the wire, rendered as the
    /// fixed sentinel string `"<closure>"`. The real ~40-kind `Fuaran.UI` tier is
    /// full of these (Phase 317 real-tier migration); the spike had none. There is
    /// no authored content — the encoder emits the sentinel unconditionally.
    | TClosure
    /// A function-typed field carrying its **host signature** (Phase 689). Wire
    /// behaviour is identical to [[TClosure]] in every respect — same `"<closure>"`
    /// sentinel, same sentinel-checking decode, same schema. The only difference is the
    /// generated *declaration*: `TClosure` says `unit`, `TFn` says `(int -> 'Msg)`.
    | TFn of ClosureSig
    /// An `obj`-erased field whose CLR shape the encoder cannot see (e.g. a
    /// `Binding<float seq>.Static` value): rendered as the fixed sentinel string
    /// `"<opaque>"`, matching `Fuaran.UI`'s `CanonicalJson` best-effort `obj`
    /// encoder. As with [[TClosure]] there is no authored content to carry.
    | TOpaque
    /// **Arbitrary JSON, carried verbatim in both directions** — `Action.Notify`'s
    /// payload, `SetState`'s value, `AiTool`'s args, `Custom` props. Distinct from
    /// [[TOpaque]] in the way that matters: `TOpaque` ERASES to a sentinel because
    /// the encoder cannot see the value, whereas a `TJson` value is real data the
    /// wire must round-trip faithfully at any nesting depth. Reaching for `TOpaque`
    /// here is silent data loss (Phase 676).
    | TJson
    /// A wire-visible field whose value is a HOST type with its own canonical codec
    /// (see [[HostedCodec]]) — `Binding.Transform`'s `source` / `pipeline`, and the
    /// slot-specific transparent-Static convention of a `Range` control's value.
    /// The generated F# declares the real host type and delegates to the named
    /// codec expressions; every other backend reads the slot's declared wire form, or
    /// carries the JSON verbatim ([[TJson]]) when it declares none.
    | THosted of HostedCodec
    /// A *non-discriminated* object (a plain F# record) — an object with named
    /// fields and **no `$type` tag** (`SelectOption`, `FormField`, `FilterSpec`,
    /// `TabHeader`, a capability-invoke arg …). Distinct from [[TUnion]] (which
    /// tags each case with `$type`) and [[TNode]] (which carries `id` + `kind`).
    /// Names a record declared in [[Idl]]'s `Records`.
    | TRecord of recordName: string
    /// A string-keyed map (`Map<string, 'V>`) rendered as a JSON object whose keys
    /// are the *authored* map keys (Ordinal-sorted by the canonical renderer), not
    /// a fixed field set — `Custom`'s `props`, `FragmentRef`'s `args`, i18n arg
    /// bags. Distinct from [[TRecord]] (fixed field names) — the keys vary per value.
    | TMap of valueType: IdlType
    /// A BARE node kind — the `$type`-discriminated kind object WITHOUT the `id`
    /// envelope a [[TNode]] carries (Phase 703). `TreeOp.EditNode`'s `newKind` is
    /// the wire position that needs it: `{"$type":"Markdown","text":"Edited"}`,
    /// which is a kind, not a node. Distinct from [[TNode]] in exactly the way the
    /// wire is — one has an `id`, the other does not.
    | TKind
    /// A tree op (Phase 703) — the op vocabulary's own recursion, which exists for
    /// exactly one wire position: `TreeOp.Batch`'s `ops` list. Resolves against
    /// [[Idl]]'s `Ops`, the way [[TNode]] resolves against `Kinds`.
    | TOp

/// The host codec of a [[THosted]] slot (Phase 692 gap-closure) — a wire-visible
/// field whose value is a HOST type with its own canonical codec, spliced into the
/// generated module verbatim. The motivating case is `Binding.Transform`: its
/// `source` is a `Fuaran.Core.DataSource` and its `pipeline` a `Fuaran.Core.Transform
/// list`, rendered by Core's own `ColumnCodec` / `DataFrameCodec` under the same
/// `Canon` discipline — re-modelling that vocabulary as IDL unions would mint a
/// second set of types beside the ones the evaluator actually consumes.
///
/// `FSharp` is the slot's host type, verbatim. `Encode` is an F# expression of type
/// `'host -> JVal`; `Decode` an F# expression of type `JVal -> Result<'host, string>`.
/// Both are emitted into the generated module, so (like a [[ClosureSig]] placeholder)
/// they may reference generated-internal declarations (`encBinding`, a record codec)
/// as well as fully-qualified host functions.
///
/// **The slot's WIRE FORM (Phase 252).** `Wire` declares, as an IDL type, what the
/// codec writes — `Some TStr` for a date the codec renders as `"2026-10-03"` — and
/// `Format` names a closed string format on top of it ([[HostedFormat]]: `date`,
/// `date-time`, `uuid`). A declared wire form is what every leg that is not the F#
/// host reads: the sampler draws from it, the schema states it, the TypeScript
/// decoder checks it (and its encoder writes through it), the interpreter refuses a
/// value outside it, and the generated F# decoder checks it before the host codec
/// runs. Without one (`Wire = None`) those legs carry the JSON verbatim exactly as
/// [[TJson]] does, which is what every slot declared before Phase 252 means — and the
/// reason the two generated hosts could disagree on which documents are valid: the
/// codec refused what nothing else knew to refuse.
///
/// BREAKING (Phase 252, record widening): a full literal adds `Wire = None; Format =
/// None` to keep its meaning, or declares the slot's wire form.
and HostedCodec =
    {
        /// The host type the generated F# declares for the slot, verbatim.
        FSharp: string
        /// An F# expression of type `'host -> JVal`, spliced verbatim into the generated
        /// encoder. Only the generated F# runs it; the interpreter carries the slot's JSON as is.
        Encode: string
        /// An F# expression of type `JVal -> Result<'host, string>`, spliced verbatim into the
        /// generated decoder, which checks a declared [[Wire]] form before calling it.
        Decode: string
        /// The IDL type the codec writes on the wire, when declared. A scalar, enum,
        /// record, list or map — never another erased slot (see
        /// [[Declare.hostedWireErrors]]).
        Wire: IdlType option
        /// A closed string format on top of a `Some TStr` wire ([[HostedFormat]]).
        Format: string option
    }

/// Where a node's KIND BODY sits relative to its `id` on the wire (Phase 109).
/// Both readiness spikes (`SecondDomainSpike.fs`, `ScoreDomainSpike.fs`) measured
/// foreign vocabularies whose node is FLAT — and both chose the same flat shape,
/// which is what made the axis declarable rather than speculative.
[<RequireQualifiedAccess>]
type NodeEnvelopeShape =
    /// `{ "id": …, "kind": { <discriminator>: tag, …fields } }` — the kind body
    /// nested under a `kind` member beside `id`. The default; the UI domain's shape.
    | NestedKind
    /// `{ <discriminator>: tag, "id": …, …fields }` — the tag, the id, the kind's
    /// fields (and any declared node envelope) share ONE object. In this shape the
    /// names `id` and the discriminator are RESERVED — see [[Declare.wireShapeErrors]].
    | FlatKind

/// How a vocabulary's canonical form lays each object's KEYS (Phase 111 — the
/// readiness spikes' finding 3). Both foreign vocabularies' own canonical
/// encoders emit DECLARATION order, so a sorted-only engine could never be
/// byte-compatible with their pre-existing corpora — the "§4.1
/// adopt-before-calcification lesson", now declarable instead of priced.
[<RequireQualifiedAccess>]
type KeyOrder =
    /// Ordinal-sorted at render (`Canon.render`) — the default, and the
    /// cross-host discipline every shipped corpus uses.
    | Sorted
    /// DECLARATION order: the discriminator, then `id`, then fields exactly as
    /// the vocabulary declares them (kind fields before the node envelope's).
    /// The ENCODER is the order authority — re-encode of any input key order
    /// NORMALISES to the declared one, so canonical form stays unique. `TMap`
    /// entries stay Ordinal-sorted in both modes (a map has no declared order),
    /// and a `TJson` value is carried in its authored order, per its verbatim
    /// contract.
    | Declared

/// The declared WIRE SHAPE of a vocabulary (Phases 108/109/111): the
/// discriminator key its unions / kinds / ops are tagged with, where a node's
/// kind body sits, and how its canonical form orders object keys. Declared on
/// [[Idl]] rather than hard-coded in the engine, because all three are
/// properties of the DOMAIN's wire, not of the interpreter — the second- and
/// third-vocabulary spikes each stopped at exactly these hard-codings.
type WireShape =
    {
        /// The union/kind/op discriminator key (Phase 108). `"$type"` is the
        /// default and reproduces every pre-declarable encoding byte-for-byte.
        Discriminator: string
        /// The node envelope nesting (Phase 109).
        NodeEnvelope: NodeEnvelopeShape
        /// The canonical key order (Phase 111).
        KeyOrder: KeyOrder
    }

    /// The shape every declaration had before the shape was declarable —
    /// `$type`-discriminated, kind body nested beside `id`, keys Ordinal-sorted.
    static member Default =
        { Discriminator = "$type"
          NodeEnvelope = NodeEnvelopeShape.NestedKind
          KeyOrder = KeyOrder.Sorted }

/// The vocabulary tokens the ENGINE would otherwise HARD-CODE — a domain's own
/// names for the members three engine behaviours have to address by name.
///
/// **Why this exists (Phase 116).** D14 says the engine is generic because a
/// vocabulary is a value the caller supplies. The hardening floor was not: the
/// codegen trust boundary (`Trust.harden`) branched on the kind tag `Custom`,
/// minted its inert placeholder as a `Markdown` node carrying a `Literal` text,
/// sanitised a `Static` binding, and [[TransparentUnion]] keyed bare-value
/// encoding on the union name `TextSource` — all five names belonging to one
/// domain's vocabulary. A vocabulary that wanted the floor therefore had to adopt
/// that domain's spelling, which is the opposite of what D14 promises.
///
/// **There is no default any more (Phase 180).** Phase 116 shipped a `Default`
/// carrying exactly the five names the engine had hard-coded, so a vocabulary that
/// declared nothing kept behaving byte-for-byte as it had. That default was a
/// migration aid with a wire consequence — an artifact with no `harden` block read
/// back as one domain's spelling — and it is retired now that both published
/// artifacts declare their block explicitly (`fuaran#1755`; DECISIONS D40, step
/// two). A vocabulary that declares nothing gets [[Undeclared]], and a hardening run
/// over an undeclared member is the typed refusal `Trust.harden` raises, never a
/// silent fall back to a domain's names.
///
/// **What is NOT here, deliberately.** Which of a domain's `(kind, field)` pairs
/// carry a URL or markdown was ALREADY caller-supplied (`Trust.Policy`), so moving
/// it here would close no leak — and it would move a security floor onto a record
/// whose default is empty, so a vocabulary migrating by writing `Default` would
/// silently stop sanitising. The `Custom` allowlist stays caller-side for a second
/// reason: it is deployment trust state (module ids and content hashes), not
/// vocabulary, and this record is projected into `idl.json`.
type HardenPolicy =
    {
        /// The kind tag the trust boundary GATES — a node that resolves a foreign
        /// component and is therefore inert unless allowlisted and hash-verified.
        /// Empty means undeclared, and the hardener refuses rather than gating
        /// nothing; there is no default spelling since Phase 180.
        GatedKind: string
        /// The kind tag of the inert placeholder a gated-out node becomes — a
        /// benign node that renders text and never a live call.
        PlaceholderKind: string
        /// The placeholder kind's single field, which carries the label text.
        /// Distinct from [[TextLiteralField]] on purpose: this names a KIND's
        /// field, that one a UNION CASE's, and a domain may spell them
        /// differently.
        PlaceholderField: string
        /// The union case carrying literal (already-resolved) TEXT — what the
        /// placeholder label is wrapped in, and what the markdown scrub matches.
        TextLiteralCase: string
        /// [[TextLiteralCase]]'s single field.
        TextLiteralField: string
        /// The union case carrying a literal (inline, not by-name) VALUE — what
        /// the URL sanitiser matches on a declared URL field.
        ValueLiteralCase: string
        /// [[ValueLiteralCase]]'s single field.
        ValueLiteralField: string
        /// The unions that have a TRANSPARENT case, as `(unionName, caseTag)` —
        /// a case encoded and decoded as a BARE JSON value rather than a
        /// discriminator-tagged object (see [[TransparentUnion]]). The transparent
        /// case carries exactly one field; the union's other cases stay tagged.
        ///
        /// Wire-visible, and the one member here that is: a change moves the bytes
        /// of every document using the case, which is why the artifact surfaces the
        /// derived `transparentCase` per union and the stability classifier reports
        /// it as a breaking wire event.
        TransparentUnions: (string * string) list
    }

    /// A policy that declares NO token — every name empty (Phase 178), and since
    /// Phase 180 the only policy the engine mints. A vocabulary carrying this has
    /// said "I have not named these", and `Trust.harden` turns that into a typed
    /// refusal naming the token it needed rather than hardening through it.
    ///
    /// **It is what an absent `harden` block reads back as (Phase 180).** Phase 178
    /// shipped it as an OPT-IN beside a `Default` carrying the engine's old
    /// hard-coded names, because at the time both published `idl.json` artifacts
    /// carried no block and emptying the default would have changed what already
    /// published bytes MEANT (D40). Phase 179 made the writer emit the block for
    /// every policy, `fuaran#1755` confirmed both artifacts carry it, and this is
    /// step two: `Default` is deleted and `Artifact.readHarden` resolves an absent
    /// block HERE. The migration is what made the flip safe; the sequence, not the
    /// flip, was ever the difficult part.
    ///
    /// **Empty strings rather than `string option` fields, deliberately.** Widening
    /// the members to `option` is a retype of a published record — every consumer
    /// meets it, including the ones this change has nothing to say to — and the
    /// absence of a name in a record whose members ARE names is
    /// exactly what an empty one says. What makes the absence non-silent is the
    /// refusal, not the representation: an undeclared [[GatedKind]] matches no node
    /// tag, so a hardening run over this policy would otherwise gate NOTHING and say
    /// nothing about it, which is the Phase 96 fail-open lesson in its purest form.
    static member Undeclared =
        { GatedKind = ""
          PlaceholderKind = ""
          PlaceholderField = ""
          TextLiteralCase = ""
          TextLiteralField = ""
          ValueLiteralCase = ""
          ValueLiteralField = ""
          TransparentUnions = [] }

/// A DEPRECATION note (Phase 113) — the retirement half of the annotation set.
///
/// Both slots are optional, and `Replacement = None` is the ordinary case rather
/// than a degenerate one: the vocabulary-growth charter admits kinds but had no
/// retirement path at all, and most retirements are "this is going away", not
/// "this moved". A required replacement would have made the plain retirement
/// unmodellable, and widening it to optional afterwards is a breaking change to a
/// published shape.
type Deprecation =
    {
        /// The member that supersedes this one, when one does — a case tag or a
        /// field name, in the same namespace as the annotated member.
        Replacement: string option
        /// Free prose for the generated doc comment: why, and what to do instead.
        Message: string option
    }

/// The bounded annotation set declarable on a union case or a field (Phase 113) —
/// what is true ABOUT a member, as distinct from its shape.
///
/// **Bounded, and a record rather than a list, deliberately.** A `list` of
/// annotation cases makes two `Since` stamps or two contradictory `Deprecated`
/// notes representable, and nothing downstream could choose between them. Named
/// slots cannot state that.
///
/// **Nothing here is on the wire.** An annotation changes no encoding in either
/// direction: [[Encode]] and [[Decode]] never read this record, so an annotated
/// vocabulary's bytes are byte-for-byte its unannotated bytes. What it changes is
/// the generated DECLARATION (a doc comment and a `System.Obsolete` attribute on
/// the F# side, a comment on the TypeScript side) and the `idl.json` artifact —
/// which is exactly why the stability classifier can grade a marking as
/// non-breaking and a vocabulary can retire a member across two releases.
type Annotations =
    {
        /// Marked for retirement — see [[Deprecation]].
        Deprecated: Deprecation option
        /// **In-process only** — the member is meaningful inside one host process
        /// and has no wire projection, so a value in it is LOST across any wire
        /// boundary. Distinct from [[Optionality.HostOnly]], which is a statement
        /// about a FIELD's encoding; this is a statement about a member that a
        /// reader of the generated declaration needs and the encoding cannot carry
        /// (a union case whose payload is a host value, for instance).
        InProcessOnly: bool
        /// The vocabulary version the member first appeared in, verbatim. Carried
        /// as a string rather than parsed: the engine is domain-generic and a
        /// domain's version line is its own business.
        Since: string option
        /// What the member IS (Phase 255) — authored prose, the summary a reader of
        /// the generated declaration meets first. The other three slots say what is
        /// true ABOUT a member that already has a meaning; this one states the
        /// meaning, so a vocabulary documents itself once, here, rather than in every
        /// layer generated from it.
        ///
        /// Free text, verbatim: line breaks are kept, and nothing is escaped or
        /// stripped on the way into the artifact. Making it safe inside a generated
        /// comment is each EMITTER's job (the F# backend splits it into `///` lines and
        /// encodes it only where the compiler will not), because what "safe" means is a
        /// property of the target language, not of the prose.
        Doc: string option
    }

    /// No annotations — the default, and what every declaration written before
    /// Phase 113 means. The artifact omits an empty set entirely, so an
    /// unannotated vocabulary's `idl.json` is byte-for-byte what it was.
    static member Empty =
        { Deprecated = None
          InProcessOnly = false
          Since = None
          Doc = None }

    /// Whether this set says nothing. The emitters and the artifact both branch on
    /// it, so the "absent is the default and omitted" rule has one definition.
    member this.IsEmpty =
        this.Deprecated.IsNone
        && not this.InProcessOnly
        && this.Since.IsNone
        && this.Doc.IsNone

/// Whether a field is always present, omitted on the wire when absent, or
/// omitted on the wire when equal to an identity default (omit-on-absence and
/// omit-at-default are both wire-visible). `OmitDefault d`: the field always has a
/// semantic value; the encoder emits it only when it differs from `d`, and the
/// decoder restores `d` on absence — the Fuaran-UI Phase 147 (role/voice) + Phase
/// 460 (tone/weight/emphasis/format/width) omit-when-default wire discipline.
type Optionality =
    /// Always on the wire: the encoder refuses an absent value and the decoder an absent member.
    | Required
    /// Omitted on the wire when absent, and absent again after decode — presence is information.
    | Optional
    /// Always has a value: emitted only when it differs from the default, compared as encoded
    /// values (so `VInt 2` at a float slot whose default is `VFloat 2.0` is omitted), and
    /// restored to the default when the member is absent.
    | OmitDefault of IdlValue
    /// **Never on the wire at all** (Phase 691) — present in the host declaration,
    /// absent from every encoding, restored from the slot's declared placeholder on
    /// decode. `WIRE_FORMAT.md` §9's "wire-omitted fields (by design)": `Node.Motion`
    /// and `Node.ExtraAttributes` are consumer-authored and deliberately not AI-visible,
    /// and `Action.Dispatch`'s `'Msg` payload is a host value with no wire projection.
    ///
    /// Distinct from [[Optional]], which IS wire-visible — its presence is information,
    /// and `WIRE_FORMAT.md` rule 4 turns on exactly that difference.
    ///
    /// A host-only field's type must be a [[TFn]], because that is what carries the
    /// declared host type and the decoder's placeholder. (`TFn` is named for its
    /// commonest use, but what it really means is "a slot whose host type is declared
    /// and whose wire form is fixed" — a host-only slot's wire form being *absence*.)
    | HostOnly

/// One named slot of a kind, op, union case, record or the node envelope — its wire member
/// name, the type of its value, and when it appears on the wire.
and IdlField =
    {
        /// The wire member name, which is also the generated host field name, so it must be an
        /// identifier and unique within its owner ([[Declare.errors]]).
        Name: string
        /// The type the member's value is encoded and decoded at.
        Type: IdlType
        /// When the member is on the wire and what its absence means.
        Opt: Optionality
        /// What is true ABOUT this field, as opposed to its shape (Phase 113).
        /// [[Annotations.Empty]] for a field that says nothing, which is every field
        /// declared before the set existed.
        Annotations: Annotations
    }

/// A node kind — flat `$type`-discriminated on the wire (`Category` is metadata, not serialised).
and IdlKind =
    {
        /// The discriminator value the kind body is tagged with on the wire; unique among
        /// [[Idl.Kinds]] (or among [[Idl.Ops]] for an op) and never empty.
        Tag: string
        /// A free single-line classification for the generated layer (`"op"` on a tree op);
        /// never encoded or decoded.
        Category: string
        /// The kind body's fields, in declaration order — the order `KeyOrder.Declared`
        /// renders them in.
        Fields: IdlField list
        /// What is true ABOUT this kind (Phase 119) — see [[IdlField.Annotations]].
        /// [[Annotations.Empty]] for a kind that says nothing.
        ///
        /// This is the vocabulary-growth charter's RETIREMENT half: the charter admits
        /// kinds, and until a whole kind could be marked, a domain retiring one had no
        /// way to say so that survived into the generated layer — a deprecated kind was
        /// a doc comment somebody remembered. Because [[Idl.Ops]] is an `IdlKind list`
        /// too, a tree-op is annotatable by the same slot and every leg that walks a
        /// kind walks an op unchanged.
        Annotations: Annotations
    }

/// One case of an [[IdlUnion]]: its discriminator tag and the fields beside it.
and IdlUnionCase =
    {
        /// The discriminator value; unique within its union.
        Tag: string
        /// The case's fields, which may mention the union's type parameters as [[TVar]]. A
        /// declared transparent case has exactly one, and it is encoded bare.
        Fields: IdlField list
        /// What is true ABOUT this case (Phase 113) — see [[IdlField.Annotations]].
        Annotations: Annotations
    }

/// A `$type`-discriminated value union (e.g. `Binding` has cases `Static` / `State`).
and IdlUnion =
    {
        /// The type name [[TUnion]] refers to; shares one namespace with enum and record names.
        Name: string
        /// The type parameter names, in order — a [[TUnion]]'s arguments bind to them by
        /// position. Empty for a non-generic union.
        Params: string list
        /// The cases, in declaration order.
        Cases: IdlUnionCase list
    }

/// A closed set of bare strings on the wire.
///
/// `Cases` are the HOST case identifiers (the F# DU cases the generator emits);
/// `Wires` are their wire strings, positionally parallel. `Wires = []` means the
/// two coincide — each case name IS its wire string, which is every declaration
/// written before Phase 707 and remains the overwhelmingly common shape.
///
/// The split exists because a wire vocabulary is not obliged to respect F#
/// case-name constraints: `liveRegion`'s wire strings are lower-case
/// (`"polite"` / `"assertive"` / `"off"`), and other domains' closed sets will
/// be hyphenated or otherwise unspellable as an F# identifier. Before the split
/// such a set was simply unmodellable as a `TEnum` and had to be left `TStr` (or
/// pushed out to a host codec via [[THosted]]) — "named rather than
/// mis-modelled", but still a hole in the type model.
///
/// **Build these with [[Declare.enumOf]] / [[Declare.enumWith]] rather than by record
/// literal.** `enumWith` takes `(case, wire)` PAIRS, so the parallel-arity
/// invariant cannot be stated wrongly; [[Declare.enumWireErrors]] is the backstop
/// for a record built by hand.
and IdlEnum =
    {
        /// The type name [[TEnum]] refers to; shares one namespace with union and record names.
        Name: string
        /// The host case identifiers, in declaration order; each must be an identifier.
        Cases: string list
        /// The wire string of each case, positionally parallel to [[Cases]], or `[]` when every
        /// case is its own wire string. Read it through [[WireOf]] / [[CaseOf]].
        Wires: string list
        /// What is true ABOUT individual CASES (Phase 119), keyed by HOST case name —
        /// the [[Cases]] entry, not the wire string, because the host name is what the
        /// F# backend attaches the doc block and the attribute to, and [[WireOf]]
        /// resolves the other direction wherever the wire name is wanted.
        ///
        /// **SPARSE, and keyed rather than positional — deliberately, and unlike
        /// [[Wires]].** A parallel `Annotations list` would make annotating one case of
        /// a ten-case enum cost nine `Annotations.Empty` entries, and it would restate
        /// exactly the parallel-arity invariant [[Declare.enumWith]] exists to make
        /// unstatable. Phase 113 put annotations ON the member for the opposite reason —
        /// an `Idl`-level table addressed by owner AND member can name a member the
        /// vocabulary no longer has — and that argument does not reach here: a case is a
        /// bare string, so there is no member to put anything on, and the smallest
        /// namespace available is this enum's own. The residual dangling-name class is
        /// checked by [[Declare.enumWireErrors]].
        ///
        /// `[]` — the default, and every declaration written before Phase 119 — means no
        /// case says anything. Read it through [[AnnotationsOf]] rather than by lookup.
        CaseAnnotations: (string * Annotations) list
    }

    /// The annotation set declared for a HOST case name, or [[Annotations.Empty]] when
    /// the enum says nothing about it. Total, like [[WireOf]]: an unknown case name
    /// answers "nothing declared", and [[Declare.enumWireErrors]] is what reports one.
    member this.AnnotationsOf(case: string) : Annotations =
        this.CaseAnnotations
        |> List.tryPick (fun (c, a) -> if c = case then Some a else None)
        |> Option.defaultValue Annotations.Empty

    /// Whether any case of this enum says anything — the condition the emitters and the
    /// artifact branch on, so "absent is the default and omitted" has one definition.
    member this.HasCaseAnnotations: bool =
        this.CaseAnnotations |> List.exists (fun (_, a) -> not a.IsEmpty)

    /// The wire string for a host case name — the case name itself when the enum
    /// declares no mapping. Unknown case names come back unchanged, which keeps
    /// this total; callers that need rejection check membership of `Cases` first
    /// (`Encode` does exactly that).
    member this.WireOf(case: string) : string =
        match this.Wires with
        | [] -> case
        | ws ->
            match List.tryFindIndex (fun c -> c = case) this.Cases with
            | Some i when i < List.length ws -> ws[i]
            | _ -> case

    /// The host case name for a wire string, or `None` when the wire string is
    /// not in this enum's closed set. The inverse of [[WireOf]].
    member this.CaseOf(wire: string) : string option =
        match this.Wires with
        | [] -> this.Cases |> List.tryFind (fun c -> c = wire)
        | ws ->
            match List.tryFindIndex (fun w -> w = wire) ws with
            | Some i when i < List.length this.Cases -> Some this.Cases[i]
            | _ -> None

    /// Every wire string this enum admits, in declaration order — what the
    /// schema's `enum` array, the TS decoder's case list and the sampler draw on.
    member this.WireCases: string list =
        match this.Wires with
        | [] -> this.Cases
        | ws -> ws

/// A non-discriminated object type — named fields, no `$type` tag (referenced by
/// [[TRecord]]). Fields may be `Optional` (omitted on the wire when absent).
and IdlRecord =
    {
        /// The type name [[TRecord]] refers to; shares one namespace with enum and union names.
        Name: string
        /// The record's fields; a field-less record is well-formed.
        Fields: IdlField list
    }

/// An authored value, checked and encoded against the IDL.
and IdlValue =
    /// A string value (matches [[TStr]]).
    | VStr of string
    /// An integer value (matches [[TInt]]); also accepted at a [[TFloat]] slot, where it
    /// encodes as the float.
    | VInt of int
    /// A boolean value (matches [[TBool]]).
    | VBool of bool
    /// A float value (matches [[TFloat]]); a non-finite one encodes as its quoted §7 token.
    | VFloat of float
    /// An enum value carrying the case's WIRE string, not its host case name — the encoder
    /// checks it against [[IdlEnum.WireCases]].
    | VEnum of string
    /// A tagged value: a union case (matches [[TUnion]]), and also a bare kind ([[TKind]]) or a
    /// tree op ([[TOp]]). `fields` are by name; one the owner does not declare is refused, and
    /// order does not matter.
    | VUnion of tag: string * fields: (string * IdlValue) list
    /// A list value (matches [[TList]]), every element at the list's item type.
    | VList of IdlValue list
    /// A node with no envelope (matches [[TNode]]): its id, its kind tag, and the kind's fields
    /// by name. The decoder answers this case whenever the envelope decodes to nothing.
    | VNode of id: string * kindTag: string * fields: (string * IdlValue) list
    /// A node carrying its ENVELOPE as well as its kind (Phase 698) — the
    /// `WIRE_FORMAT.md` §3.1 fields a node holds beside `id`/`kind`, declared per
    /// domain in [[Idl.NodeFields]].
    ///
    /// **Why a sibling case rather than a fourth slot on [[VNode]].** Widening
    /// `VNode`'s arity would break every authored construction site downstream
    /// — the vocabulary fixtures included — for a slot that is empty in almost all
    /// of them, so the envelope arrived as its own case and `VNode` stayed the
    /// envelope-free form it always was. Producers emit `VNode` when the envelope
    /// is empty and `VNodeEnv` only when it is not, which is why every pre-existing
    /// seeded stream and snapshot is byte-unchanged.
    ///
    /// **Why the envelope is NOT merged into `fields`.** The two are separate
    /// namespaces and they already collide: the UI vocabulary declares an envelope
    /// `style` (a `SemanticStyle`) and a `Drawing.style` kind field (a `DrawStyle`),
    /// so a single flat list could not say which one a `"style"` entry meant.
    | VNodeEnv of id: string * envelope: (string * IdlValue) list * kindTag: string * fields: (string * IdlValue) list
    /// An authored "not provided" — in a field list it is treated as if the field were left
    /// out (so a `Required` field holding it is refused). The decoder never produces it, and
    /// encoding it anywhere but a field position is an error.
    | VAbsent
    /// A function-typed value (matches [[TClosure]]) — carries nothing; the
    /// encoder emits the `"<closure>"` sentinel. Present so a `TClosure` field can
    /// be authored explicitly (the field is required-and-always-sentinel).
    | VClosure
    /// An `obj`-erased value the encoder cannot inspect (matches [[TOpaque]]) —
    /// emits the `"<opaque>"` sentinel.
    | VOpaque
    /// An arbitrary JSON value (matches [[TJson]]) — carried as a canonical `JVal`
    /// so it renders through `Canon` like every other value rather than by a
    /// separate stringifier that could drift on key order, escaping or float layout.
    | VJson of JVal
    /// A non-discriminated object value (matches [[TRecord]]) — named fields, no
    /// `$type`. Encoded as a plain JSON object with the fields Ordinal-sorted.
    | VRecord of fields: (string * IdlValue) list
    /// A string-keyed map value (matches [[TMap]]) — arbitrary keys, each value of
    /// the map's value-type. Encoded as a JSON object, keys Ordinal-sorted.
    | VMap of entries: (string * IdlValue) list

/// A declared default for one kind field — applied by the generated smart
/// constructors (Phase 317 increment 7). `Kind`/`Field` address the field;
/// `Value` is the default authored value. The node ENVELOPE has no kind tag, so a
/// declared envelope default is addressed by the EMPTY `Kind` (Phase 195) — a
/// kind's tag is its wire discriminator and can never be empty, so the address is
/// free and unambiguous.
///
/// **This is an AUTHORING default, and it deliberately does not fill on decode**
/// (Phase 201; the case that holds it is `IdlEnvelopeTests`). What it says is what
/// a CALLER need not pass; what absence on the WIRE means is said by
/// [[Optionality.OmitDefault]], which the encoder's omit test and every emitted
/// decoder's restore already render from one literal.
///
/// The two were nearly collapsed into one. They must not be, and the reason is
/// byte stability. A `Required` member is ALWAYS emitted, so a decoder that filled
/// one from a declared default would re-encode it PRESENT: two distinct
/// byte-streams would decode to one tree and `decode >> encode` would stop being
/// the identity on the wire — the property the conformance corpus compares, that a
/// content digest over a tree depends on, and that a cross-host attestation rests
/// on. The leniency bought is toward documents the vocabulary's own encoder cannot
/// produce; the price is the property everything else is built on. A vocabulary
/// that genuinely wants absence to mean a value declares `OmitDefault`, and gets
/// the fill in every leg.
type IdlDefault =
    {
        /// The kind tag owning the field, or `""` for a node-envelope field.
        Kind: string
        /// The field's name within that owner. An address naming no field is inert and is not
        /// refused; an address declared twice is.
        Field: string
        /// The value a smart constructor supplies when the caller passes none; it must encode
        /// at the field's type.
        Value: IdlValue
    }

/// The whole IDL — kinds, value-unions, enums, non-discriminated records, and
/// field defaults. The canonical root.
type Idl =
    {
        /// The node kinds a [[TNode]] / [[TKind]] tag resolves against; tags are unique.
        Kinds: IdlKind list
        /// The discriminated value unions a [[TUnion]] names.
        Unions: IdlUnion list
        /// The closed string sets a [[TEnum]] names.
        Enums: IdlEnum list
        /// The untagged object types a [[TRecord]] names.
        Records: IdlRecord list
        /// The authoring defaults the generated smart constructors apply; they never fill a
        /// member on decode (see [[IdlDefault]]).
        Defaults: IdlDefault list
        /// The node ENVELOPE — fields a `Node` carries beside `id` and `kind`
        /// (Phase 690). Empty (the default) generates `{ Id; Kind }`, exactly as
        /// before; the Fuaran-UI vocabulary declares `state` / `style` /
        /// `accessibility` here, per `WIRE_FORMAT.md` §3.1.
        ///
        /// Declared rather than hard-coded, because "what a node carries beside its
        /// kind" is a property of the DOMAIN's tree, not of the generator: another
        /// Fuaran domain has a different envelope, or none at all.
        NodeFields: IdlField list
        /// The TREE-OP vocabulary (Phase 703) — `WIRE_FORMAT.md` §3.4's
        /// `$type`-discriminated op cases, the wire's second root beside `Node`.
        /// Empty (the default) means the domain declares no ops, exactly as before.
        ///
        /// **Modelled as [[IdlKind]] rather than a dedicated carrier, deliberately.**
        /// An op is structurally what a node kind is — a flat `$type`-discriminated
        /// object over the same field + optionality model — so every leg that walks
        /// a kind walks an op unchanged, and a second near-identical type would have
        /// duplicated the encoder, decoder, schema and artefact plumbing to express
        /// no difference. `Category` carries `"op"`; it is metadata, never
        /// serialised, and the same slot classifies node kinds by behaviour.
        ///
        /// **Shapes only.** Apply SEMANTICS — §3.4's error mapping, path addressing,
        /// what `UpdateProp`'s `path` means, whether a `target` resolves — stay
        /// hand-written above this, exactly as decode policy does for nodes. The IDL
        /// states what is on the wire, never what applying it does.
        Ops: IdlKind list
        /// The declared wire shape (Phases 108/109) — the discriminator key and
        /// the node-envelope nesting. [[WireShape.Default]] reproduces every
        /// pre-declarable encoding byte-for-byte; a vocabulary whose wire tags
        /// with another key or lays its nodes flat declares that HERE, and every
        /// leg (interpreter, generated F#/TS, schema) derives from it.
        Wire: WireShape
        /// The vocabulary tokens the engine addresses BY NAME (Phase 116) — the
        /// gated kind, the inert placeholder it becomes, the literal text and value
        /// cases the sanitisation floor matches, and which unions have a transparent
        /// case. A vocabulary that declares nothing gets [[HardenPolicy.Undeclared]]
        /// and the hardener refuses it by name (Phase 180) — there is no default
        /// spelling to fall back to.
        ///
        /// Declared rather than hard-coded for the same reason [[NodeFields]] and
        /// [[Wire]] are: what a vocabulary CALLS the node it refuses to resolve live
        /// is a property of the domain, not of the engine.
        Harden: HardenPolicy
    }

/// The closed set of string FORMATS a hosted slot's declared wire form may name
/// (Phase 252, [[HostedCodec.Format]]) — and the one definition of what each admits,
/// so the interpreter, the sampler and the generated hosts cannot disagree on it.
///
/// **Closed, deliberately.** A format the engine does not know is one no leg can
/// check or draw from, which is the disagreement the declaration exists to remove;
/// [[Declare.hostedWireErrors]] reports one, and the generators refuse it. The three
/// are JSON Schema's spellings, read strictly: `date` is RFC 3339 `full-date` (a real
/// calendar day, years 0001–9999), `date-time` is RFC 3339 `date-time` (an offset is
/// required; `T`/`Z` in either case; no leap second), and `uuid` is the 8-4-4-4-12 hex
/// form in either case.
[<RequireQualifiedAccess>]
module HostedFormat =

    /// Every format a hosted slot may declare.
    let known: string list = [ "date"; "date-time"; "uuid" ]

    let private dateRx =
        System.Text.RegularExpressions.Regex("^([0-9]{4})-([0-9]{2})-([0-9]{2})$")

    let private dateTimeRx =
        System.Text.RegularExpressions.Regex(
            "^([0-9]{4})-([0-9]{2})-([0-9]{2})[Tt]([0-9]{2}):([0-9]{2}):([0-9]{2})(\\.[0-9]+)?([Zz]|[+-]([0-9]{2}):([0-9]{2}))$"
        )

    let private uuidRx =
        System.Text.RegularExpressions.Regex(
            "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"
        )

    let private validDay (y: int) (m: int) (d: int) =
        y >= 1 && m >= 1 && m <= 12 && d >= 1 && d <= System.DateTime.DaysInMonth(y, m)

    /// Whether the string `s` is in `format`. An unknown format admits nothing.
    let admits (format: string) (s: string) : bool =
        let num (g: System.Text.RegularExpressions.Group) = int g.Value

        match format with
        | "date" ->
            let m = dateRx.Match s
            m.Success && validDay (num m.Groups[1]) (num m.Groups[2]) (num m.Groups[3])
        | "date-time" ->
            let m = dateTimeRx.Match s

            m.Success
            && validDay (num m.Groups[1]) (num m.Groups[2]) (num m.Groups[3])
            && num m.Groups[4] <= 23
            && num m.Groups[5] <= 59
            && num m.Groups[6] <= 59
            && (not m.Groups[9].Success || (num m.Groups[9] <= 23 && num m.Groups[10] <= 59))
        | "uuid" -> uuidRx.IsMatch s
        | _ -> false

/// Type-parameter substitution (Phase 292) — the ONE definition the encoder, the decoder,
/// the sampler and the generator share.
///
/// It was written four times, and the copies had drifted: the sampler's mapped EVERY type
/// variable to the union's FIRST argument, so a two-parameter union sampled its second
/// parameter's slots at the first's type, and the decoder's bare-value arm zipped the
/// parameter list against the argument list without checking their lengths. Keyed by
/// parameter NAME, and bound only when the counts agree.
[<RequireQualifiedAccess>]
module TypeParams =

    /// `t` with every type variable the map names replaced by its binding, at any depth. A
    /// variable the map does not name is left in place, so an unbound one stays visible to
    /// the caller (the encoder refuses it by name; [[Declare.errors]] refuses it at
    /// declaration).
    let rec substitute (subst: Map<string, IdlType>) (t: IdlType) : IdlType =
        match t with
        | TVar v ->
            match Map.tryFind v subst with
            | Some r -> r
            | None -> t
        | TList inner -> TList(substitute subst inner)
        | TMap inner -> TMap(substitute subst inner)
        | TUnion(n, args) -> TUnion(n, List.map (substitute subst) args)
        | other -> other

    /// The substitution an instantiation `TUnion(u.Name, args)` binds — each parameter keyed
    /// by its name to the argument at its position — or `None` when the counts differ, so no
    /// caller ever zips two lists of different length.
    let bind (u: IdlUnion) (args: IdlType list) : Map<string, IdlType> option =
        if List.length u.Params = List.length args then
            Some(Map.ofList (List.zip u.Params args))
        else
            None

/// Source LITERALS for IDL-authored text (Phase 292) — the one escaper every emitter
/// splices a vocabulary's text through, with one policy per target.
///
/// **Why one module.** The generator held five escapers (`fsAttrStr`, `fsDefaultStr`,
/// `tsSourceStr`, `fsStringLit`, the F* target's `lit`), each written for the site that
/// first needed it and each a little different: the default-value one escaped neither CR nor
/// LF (so the module's own line-ending normalisation then rewrote a CR INSIDE the literal,
/// and the F# and TypeScript omit tests compared against different strings), and several
/// sites spliced text with no escaper at all — the discriminator into F# and JavaScript
/// string literals, deprecation prose and a kind's category into comments, where a line
/// break ends the comment and the rest of the text is live source. An `idl.json` is
/// UNTRUSTED input (DECISIONS D95): [[Declare.errors]] refuses a vocabulary that carries
/// such text at every loading path, and this module is the second half of the same rule —
/// a vocabulary built in code, which no loader sees, still cannot put a byte of source into
/// a generated module THROUGH THE TEXT THE IDL AUTHORS: its identifiers, wire spellings,
/// discriminator, categories, docs and deprecation prose.
///
/// **The trust boundary — what this module does NOT cover (Phase 387, DECISIONS D124).** Some
/// declared text is not IDL-authored data but HOST SOURCE, spliced verbatim by design: a
/// [[THosted]] slot's `FSharp` type and its `Encode` / `Decode` expressions, a [[TFn]] slot's
/// [[ClosureSig]] host types and placeholder, and every `support.json` entry (a doc block, a
/// splice, a kind projection, the host prelude). Escaping them would destroy them — they are
/// code — so none passes through here; [[Declare.errors]] checks a hosted slot only for its
/// declared wire form and format, and `SupportArtifact.ofJson` checks only shape. Whoever
/// supplies them supplies source to the generated module, trusted exactly as far as the project
/// that compiles it trusts its own code, so a vocabulary or support file from an untrusted
/// party must not carry them. The guarantee above is scoped to IDL-authored text and stops
/// there; `IdlCertificationTests` pins the boundary by planting a hosted body that would be
/// unsafe as data and asserting it reaches the generated module verbatim.
///
/// **The policies.** A string LITERAL (F#, an F# attribute argument, TypeScript) escapes the
/// quote and the backslash, names `\n` `\r` `\t`, and writes every other C0 control, U+0085,
/// U+2028, U+2029 and an unpaired surrogate as `\uXXXX`, so the literal's VALUE is the
/// authored string exactly and its source text holds no line break and nothing a UTF-8 file
/// cannot carry. Neither F# nor F* can spell an unpaired surrogate in a string literal (both
/// probed: the F# compiler reads `"\uD800"` as U+FFFD, and the pinned F* prover refuses it as
/// a syntax error), so in those two policies one becomes U+FFFD; TypeScript spells it. A
/// declaration that would need it is refused by [[Declare.errors]], and the F# and F*
/// backends refuse such a VALUE ([[isWellFormed]]) before it reaches here. A COMMENT (an F# `///` or `//` line, a
/// TypeScript `//` line) cannot escape anything, so the text is split at every line break an
/// author can type or an editor may break on, one comment line per authored line, and a
/// character no comment can carry (a C0 control other than tab, an unpaired surrogate,
/// U+FFFE, U+FFFF) becomes U+FFFD. A TypeScript object KEY is bare when JavaScript can spell
/// it and a string literal otherwise.
///
/// Every policy is the identity on the text the generator has always emitted — a name, a
/// plain wire string — so a vocabulary that carries none of these characters emits
/// byte-for-byte what it did.
[<RequireQualifiedAccess>]
module SourceLit =

    // Compared as code-unit values: Fable cannot write an unpaired surrogate char literal into its
    // output, and the guard in SourceLiteralTests holds every literal under src/ to that.
    let private isHigh (c: char) = int c >= 0xD800 && int c <= 0xDBFF
    let private isLow (c: char) = int c >= 0xDC00 && int c <= 0xDFFF

    let private isLineBreak (c: char) =
        c = '\n' || c = '\r' || c = '\u0085' || c = '\u2028' || c = '\u2029'

    /// `\uXXXX`, lower-case hex — the spelling the generator's literals have always used.
    let private uEscape (c: char) : string =
        let hex = "0123456789abcdef"
        let n = int c

        "\\u"
        + string hex[(n >>> 12) &&& 0xF]
        + string hex[(n >>> 8) &&& 0xF]
        + string hex[(n >>> 4) &&& 0xF]
        + string hex[n &&& 0xF]

    /// The body of a literal delimited by `quote`. `unpaired` spells an unpaired surrogate.
    let private quotedBody (quote: char) (unpaired: char -> string) (s: string) : string =
        let b = System.Text.StringBuilder()
        let mutable i = 0

        while i < s.Length do
            let c = s[i]

            if isHigh c && i + 1 < s.Length && isLow s[i + 1] then
                b.Append(c).Append(s[i + 1]) |> ignore
                i <- i + 1
            elif isHigh c || isLow c then
                b.Append(unpaired c) |> ignore
            else
                match c with
                | c when c = quote -> b.Append('\\').Append(c) |> ignore
                | '\\' -> b.Append("\\\\") |> ignore
                | '\n' -> b.Append("\\n") |> ignore
                | '\r' -> b.Append("\\r") |> ignore
                | '\t' -> b.Append("\\t") |> ignore
                | c when c < ' ' || isLineBreak c -> b.Append(uEscape c) |> ignore
                | c -> b.Append(c) |> ignore

            i <- i + 1

        b.ToString()

    /// The body of a double-quoted literal.
    let private literalBody (unpaired: char -> string) (s: string) : string = quotedBody '"' unpaired s

    /// Whether `s` is well-formed UTF-16 — no unpaired surrogate. F# and F* cannot spell an
    /// unpaired surrogate in a string literal (measured: the F# compiler reads `"\uD800"` as
    /// U+FFFD), so a caller that must reproduce a value exactly refuses one that fails this.
    let isWellFormed (s: string) : bool =
        let mutable ok = true
        let mutable i = 0

        while ok && i < s.Length do
            if isHigh s[i] && i + 1 < s.Length && isLow s[i + 1] then
                i <- i + 2
            elif isHigh s[i] || isLow s[i] then
                ok <- false
            else
                i <- i + 1

        ok

    /// An F# string literal, quotes included, whose value is `s` exactly when `s` is
    /// [[isWellFormed]]. An unpaired surrogate becomes U+FFFD, written as the escape, which
    /// is what the F# compiler would make of any spelling of it.
    let fsString (s: string) : string =
        "\"" + literalBody (fun _ -> uEscape '\uFFFD') s + "\""

    /// An F# ATTRIBUTE argument (`System.Obsolete("…")`) — an F# string literal, so the
    /// [[fsString]] policy; named for its site so an attribute splice reads as one.
    let fsAttribute (s: string) : string = fsString s

    /// A TypeScript (JavaScript) double-quoted string literal whose value is `s` exactly.
    let tsString (s: string) : string = "\"" + literalBody uEscape s + "\""

    /// The single-quoted spelling of [[tsString]] — the same policy with `'` as the escaped
    /// delimiter — for the generated runtime's own single-quoted literals, which a vocabulary
    /// whose text needs no escaping keeps byte-for-byte.
    let tsStringSingle (s: string) : string = "'" + quotedBody '\'' uEscape s + "'"

    /// An F* string literal. F* cannot spell an unpaired surrogate, which becomes U+FFFD.
    let fstarString (s: string) : string =
        "\"" + literalBody (fun _ -> uEscape '\uFFFD') s + "\""

    /// Whether JavaScript can spell `s` as a bare identifier (`$type`, `kind`).
    let tsIsIdentifier (s: string) : bool =
        s.Length > 0
        && (System.Char.IsLetter s[0] || s[0] = '_' || s[0] = '$')
        && s |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_' || c = '$')

    /// A TypeScript object-literal / interface KEY: bare when JavaScript can spell it, a
    /// [[tsString]] otherwise.
    let tsKey (s: string) : string =
        if tsIsIdentifier s then s else tsString s

    /// One comment line's text with every character no comment can carry replaced by U+FFFD.
    let private commentSafe (s: string) : string =
        let b = System.Text.StringBuilder()
        let mutable i = 0

        while i < s.Length do
            let c = s[i]

            if isHigh c && i + 1 < s.Length && isLow s[i + 1] then
                b.Append(c).Append(s[i + 1]) |> ignore
                i <- i + 1
            elif (c < ' ' && c <> '\t') || isHigh c || isLow c || c = '\uFFFE' || c = '\uFFFF' then
                b.Append('\uFFFD') |> ignore
            else
                b.Append(c) |> ignore

            i <- i + 1

        b.ToString()

    /// Text as COMMENT LINES, one per authored line: split at `\r\n`, `\r`, `\n`, U+0085,
    /// U+2028 and U+2029, trailing whitespace dropped, blank lines dropped at either end and
    /// kept inside. Text that is only whitespace yields nothing.
    let private commentLines (text: string) : string list =
        let breaks = [| '\r'; '\n'; '\u0085'; '\u2028'; '\u2029' |]

        text.Replace("\r\n", "\n").Split(breaks)
        |> Array.map (fun l -> commentSafe (l.TrimEnd()))
        |> Array.toList
        |> List.skipWhile (fun l -> l = "")
        |> List.rev
        |> List.skipWhile (fun l -> l = "")
        |> List.rev

    /// The text of F# comment lines (`///` doc lines, `//` lines) — the caller writes the
    /// marker. Whether a `///` block is XML is the caller's decision; [[fsDocXml]] is the
    /// encoding for one that is.
    let fsDocLines (text: string) : string list = commentLines text

    /// `<` and `&` encoded, for a `///` block the F# compiler reads as XML.
    let fsDocXml (line: string) : string =
        line.Replace("&", "&amp;").Replace("<", "&lt;")

    /// The text of TypeScript `//` comment lines — the caller writes the marker.
    let tsCommentLines (text: string) : string list = commentLines text

/// A "transparent" union case is encoded/decoded as a bare JSON value (its single
/// field's value) rather than a `$type`-tagged object — the Fuaran-UI 0.2.0
/// bare-string canonical `TextSource.Literal` (`{"$type":"Literal","text":"x"}` →
/// `"x"`). Keyed on the vocabulary's declared `HardenPolicy.TransparentUnions`; the transparent
/// case carries exactly one field. The `Bound` / non-transparent cases stay `$type`-tagged
/// objects.
///
/// **Public rather than internal since Phase 97**, because the split made the
/// dependency real: the emitters moved to `Fuaran.Core.Idl.Codegen`, and an emitter
/// must agree with this codec about which cases are bare or it generates a host that
/// disagrees with the reference implementation on the wire. `internal` had been
/// hiding a genuine contract behind an assembly boundary that no longer holds — and
/// the same fact is what any third-party emitter needs, so stating it is right.
///
/// **The wart is closed since Phase 116.** The rule used to be keyed on a hard-coded
/// vocabulary name (`TextSource`) inside an engine that is otherwise domain-generic
/// (D14); it is now read from [[HardenPolicy.TransparentUnions]], which the vocabulary
/// declares on its own [[Idl]] value and the artifact carries. Phase 116 kept the old
/// name reachable as a DEFAULT so every shipped corpus stayed byte-identical; Phase 180
/// deleted that default once both published artifacts declared their block, so a
/// vocabulary that names no transparent union now has none — which is what the empty
/// list has always said, and now the only thing it can say.
module TransparentUnion =
    /// The transparent case tag for a union under a declared policy, or `None` if the
    /// union has none. Pass the owning vocabulary's `idl.Harden`.
    let tag (policy: HardenPolicy) (u: IdlUnion) : string option =
        policy.TransparentUnions
        |> List.tryPick (fun (name, case) -> if name = u.Name then Some case else None)

/// The schema-driven encoder: an authored `IdlValue`, validated against the IDL,
/// becomes a canonical `JVal`; `Canon.render` then gives the wire bytes.
module Encode =

    let private findEnum (name: string) (idl: Idl) =
        idl.Enums |> List.tryFind (fun e -> e.Name = name)

    let private findUnion (name: string) (idl: Idl) =
        idl.Unions |> List.tryFind (fun u -> u.Name = name)

    let private findKind (tag: string) (idl: Idl) =
        idl.Kinds |> List.tryFind (fun k -> k.Tag = tag)

    let private findRecord (name: string) (idl: Idl) =
        idl.Records |> List.tryFind (fun r -> r.Name = name)

    /// [[Canon.typed]] under the DECLARED discriminator key (Phase 108) — the
    /// default key reproduces `Canon.typed` byte-for-byte.
    let private typedWith (key: string) (tag: string) (fields: (string * JVal) list) : JVal =
        JObj((key, JStr tag) :: fields)

    let private provided (name: string) (fields: (string * IdlValue) list) =
        fields |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

    /// Phase 292 — a verbatim JSON value (a `json` slot, or a hosted slot carried as one)
    /// holding a NON-FINITE float has no canonical rendering of its own: `Canon.render`
    /// spells it as the quoted token, the bytes of the STRING `"NaN"`, and nothing at a
    /// verbatim slot can tell a reader it was a number. A `float` slot is different — its
    /// type says how to read the token back (WIRE_FORMAT §7) — so the refusal is made here,
    /// at the slots where the token aliases, and never at a typed float.
    let private verbatim (j: JVal) : Result<JVal, string> =
        match Json.firstNonFinite j with
        | Some(path, tok) ->
            Error(
                "non-finite float has no canonical rendering of its own inside a verbatim json value: "
                + tok
                + " at "
                + path
            )
        | None -> Ok j

    /// Whether two encoded values are one canonical value under the vocabulary's declared
    /// key order — the omit-at-default test's notion of equality (Phase 292).
    let private sameCanonical (idl: Idl) (a: JVal) (b: JVal) : bool =
        match idl.Wire.KeyOrder with
        | KeyOrder.Sorted -> Canon.render a = Canon.render b
        | KeyOrder.Declared -> Canon.renderOrdered a = Canon.renderOrdered b

    let rec private encodeValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<JVal, string> =
        match t, v with
        | TStr, VStr s -> Ok(JStr s)
        | TInt, VInt i -> Ok(JInt i)
        | TBool, VBool b -> Ok(JBool b)
        | TFloat, VFloat f -> Ok(JFloat f)
        | TFloat, VInt i -> Ok(JFloat(float i))
        | TEnum name, VEnum case ->
            match findEnum name idl with
            | None -> Error(sprintf "unknown enum '%s'" name)
            // `VEnum` carries the WIRE string, exactly as `VUnion` carries the wire
            // `$type` tag — so an enum that declares a case↔wire mapping is checked
            // against its wire strings here, and only the F# emitter maps back.
            | Some e when List.contains case e.WireCases -> Ok(JStr case)
            | Some _ -> Error(sprintf "enum '%s' has no case '%s'" name case)
        | TUnion(name, args), VUnion(tag, fields) ->
            match findUnion name idl with
            | None -> Error(sprintf "unknown union '%s'" name)
            | Some u ->
                match TypeParams.bind u args, u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | None, _ ->
                    Error(
                        sprintf
                            "union '%s' given %d type args, expects %d"
                            name
                            (List.length args)
                            (List.length u.Params)
                    )
                | Some _, None -> Error(sprintf "union '%s' has no case '%s'" name tag)
                | Some subst, Some c ->
                    let caseFields =
                        c.Fields
                        |> List.map (fun f ->
                            { f with
                                Type = TypeParams.substitute subst f.Type })

                    match TransparentUnion.tag idl.Harden u with
                    | Some ttag when ttag = tag ->
                        // Transparent case (the declared one): emit the single field's value bare.
                        match caseFields with
                        | [ single ] ->
                            match provided single.Name fields with
                            | Some v -> encodeValue idl single.Type v
                            | (None | Some VAbsent) ->
                                Error(
                                    sprintf "transparent union '%s' case '%s' missing field '%s'" name tag single.Name
                                )
                        | _ -> Error(sprintf "transparent union case '%s' must have exactly one field" tag)
                    | _ ->
                        encodeFields idl caseFields fields
                        |> Result.map (typedWith idl.Wire.Discriminator tag)
        | TVar v, _ -> Error(sprintf "unsubstituted type variable '%s'" v)
        | TClosure, VClosure
        | TFn _, VClosure -> Ok(JStr "<closure>")
        | TOpaque, VOpaque -> Ok(JStr "<opaque>")
        // Phase 676 — verbatim passthrough. Emitting the `JVal` unchanged is what
        // keeps the bytes canonical: `Canon.render` already sorts keys Ordinal,
        // escapes per rule 6 and lays floats out per rule 5, so a passthrough
        // inherits all three instead of re-implementing them.
        | TJson, VJson j -> verbatim j
        // A hosted slot's content is the host codec's business — the interpreter
        // carries it verbatim, exactly as TJson (see [[HostedCodec]]). A declared wire
        // form (Phase 252) is checked on DECODE, the direction a document arrives from.
        | THosted _, VJson j -> verbatim j
        | TRecord name, VRecord fields ->
            match findRecord name idl with
            | None -> Error(sprintf "unknown record '%s'" name)
            | Some r -> encodeFields idl r.Fields fields |> Result.map JObj
        | TMap vt, VMap entries ->
            let rec go acc =
                function
                | [] -> Ok(JObj(List.rev acc))
                | (k, v) :: rest ->
                    match encodeValue idl vt v with
                    | Ok j -> go ((k, j) :: acc) rest
                    | Error m -> Error m

            // Phase 111 — a map has no DECLARED order, so its entries are
            // Ordinal-sorted at encode: a no-op under `Sorted` rendering, and
            // what keeps `Declared`-order canonical form deterministic.
            go
                []
                (entries
                 |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b)))
        | TNode, VNode(id, kindTag, fields) -> encodeNode idl id kindTag fields
        // Phase 698 — the enveloped form, at ANY depth: a nested child carries its
        // envelope through exactly this arm, so the sweep is not root-only.
        | TNode, VNodeEnv(id, envelope, kindTag, fields) -> encodeNodeEnv idl id envelope kindTag fields
        // A bare kind and an op are both `$type`-tagged objects with named fields —
        // structurally what a union case is — so `VUnion` carries them, and the wire
        // difference is only which vocabulary the tag resolves against.
        | TKind, VUnion(tag, fields) ->
            match findKind tag idl with
            | None -> Error(sprintf "unknown kind '%s'" tag)
            | Some k ->
                encodeFields idl k.Fields fields
                |> Result.map (typedWith idl.Wire.Discriminator tag)
        | TOp, VUnion(tag, fields) ->
            match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
            | None -> Error(sprintf "unknown op '%s'" tag)
            | Some o ->
                encodeFields idl o.Fields fields
                |> Result.map (typedWith idl.Wire.Discriminator tag)
        | TList inner, VList xs ->
            let rec go acc =
                function
                | [] -> Ok(JArr(List.rev acc))
                | x :: rest ->
                    match encodeValue idl inner x with
                    | Ok j -> go (j :: acc) rest
                    | Error m -> Error m

            go [] xs
        | _, VAbsent -> Error "absent value reached the encoder (should be omitted at the field level)"
        | _ -> Error(sprintf "authored value does not match IDL type %A" t)

    and private encodeFields
        (idl: Idl)
        (fields: IdlField list)
        (authored: (string * IdlValue) list)
        : Result<(string * JVal) list, string> =
        let known = fields |> List.map (fun f -> f.Name) |> Set.ofList

        let extra =
            authored |> List.filter (fun (n, v) -> v <> VAbsent && not (known.Contains n))

        if not (List.isEmpty extra) then
            Error(sprintf "authored fields not in IDL: %s" (extra |> List.map fst |> String.concat ", "))
        else
            let rec go acc =
                function
                | [] -> Ok(List.rev acc)
                | (f: IdlField) :: rest ->
                    match provided f.Name authored, f.Opt with
                    | _, HostOnly -> go acc rest
                    | (None | Some VAbsent), (Optional | OmitDefault _) -> go acc rest
                    | (None | Some VAbsent), Required -> Error(sprintf "required field '%s' is absent" f.Name)
                    // omit-at-default: a present value equal to the field's identity default
                    // emits nothing. Equal IN THE SLOT'S VALUE SPACE (Phase 292), not as
                    // authored terms: `VInt 2` and `VFloat 2.0` at a float slot are one value
                    // with one encoding, and comparing the terms gave that value two — present
                    // under one spelling, omitted under the other.
                    | Some v, OmitDefault d ->
                        match encodeValue idl f.Type v with
                        | Error m -> Error m
                        | Ok j ->
                            let atDefault =
                                v = d
                                || (match encodeValue idl f.Type d with
                                    | Ok jd -> sameCanonical idl j jd
                                    | Error _ -> false)

                            if atDefault then
                                go acc rest
                            else
                                go ((f.Name, j) :: acc) rest
                    | Some v, _ ->
                        match encodeValue idl f.Type v with
                        | Ok j -> go ((f.Name, j) :: acc) rest
                        | Error m -> Error m

            go [] fields

    /// A node with no envelope as its `JVal`, laid out by the declared [[NodeEnvelopeShape]] —
    /// the tree, not the bytes: [[encode]] adds the declared key order and the refusal of an
    /// ill-formed string. An unknown kind tag, a field the kind does not declare, or an absent
    /// `Required` field is an `Error` naming it.
    and encodeNode (idl: Idl) (id: string) (kindTag: string) (fields: (string * IdlValue) list) : Result<JVal, string> =
        match findKind kindTag idl with
        | None -> Error(sprintf "unknown kind '%s'" kindTag)
        | Some k ->
            encodeFields idl k.Fields fields
            |> Result.map (fun fs ->
                // Phase 109 — the declared node envelope shape. Nested is the
                // default and byte-identical to the pre-declarable emission; flat
                // puts the tag, the id and the kind fields in ONE object (key
                // order is irrelevant — `Canon.render` sorts Ordinal).
                match idl.Wire.NodeEnvelope with
                | NodeEnvelopeShape.NestedKind ->
                    JObj [ "id", JStr id; "kind", typedWith idl.Wire.Discriminator kindTag fs ]
                | NodeEnvelopeShape.FlatKind -> JObj((idl.Wire.Discriminator, JStr kindTag) :: ("id", JStr id) :: fs))

    /// The enveloped partner of [[encodeNode]] (Phase 698) — `id` + `kind` + the
    /// declared node envelope. The envelope rides the SAME [[encodeFields]] the kind
    /// fields ride, so `Optional`-absent, `OmitDefault`-at-default and `HostOnly`
    /// behave identically on a node field and on a kind field; that shared path is
    /// the whole reason the generated hosts and the interpreter can be expected to
    /// agree. Key order is irrelevant — `Canon.render` sorts Ordinal.
    and internal encodeNodeEnv
        (idl: Idl)
        (id: string)
        (envelope: (string * IdlValue) list)
        (kindTag: string)
        (fields: (string * IdlValue) list)
        : Result<JVal, string> =
        match findKind kindTag idl with
        | None -> Error(sprintf "unknown kind '%s'" kindTag)
        | Some k ->
            match encodeFields idl k.Fields fields with
            | Error m -> Error m
            | Ok kindFs ->
                match encodeFields idl idl.NodeFields envelope with
                | Error m -> Error(sprintf "node envelope: %s" m)
                | Ok envFs ->
                    match idl.Wire.NodeEnvelope with
                    | NodeEnvelopeShape.NestedKind ->
                        Ok(
                            JObj(
                                ("id", JStr id)
                                :: ("kind", typedWith idl.Wire.Discriminator kindTag kindFs)
                                :: envFs
                            )
                        )
                    // Flat: envelope and kind fields share the node object — the
                    // collision [[Declare.wireShapeErrors]] refuses at declaration.
                    // Discriminator first, then id, kind fields, envelope: the
                    // Phase 111 declared order (irrelevant under Sorted rendering).
                    | NodeEnvelopeShape.FlatKind ->
                        Ok(JObj((idl.Wire.Discriminator, JStr kindTag) :: ("id", JStr id) :: (kindFs @ envFs)))

    /// A value of `t` as its canonical `JVal` — what the sampler draws a hosted slot's
    /// declared wire form through (Phase 252), so the drawn JSON is exactly what the
    /// interpreter would write for that type, and what `Declare.errors` checks a declared
    /// default against.
    let internal valueJson (idl: Idl) (t: IdlType) (v: IdlValue) : Result<JVal, string> = encodeValue idl t v

    /// The declared canonical renderer (Phase 111): Ordinal-sorted by default,
    /// authored order under `KeyOrder.Declared` — where the encoder's own
    /// construction order (discriminator, id, declared fields) is normative.
    ///
    /// The guarded render (Phase 292): the declared key order, with the refusal
    /// `Canon.tryRender` makes for a string that is not well-formed UTF-16 — a lone
    /// surrogate has no UTF-8 encoding, so its bytes would be some other string's, and the
    /// parser refuses them on read. Non-finite floats are NOT refused here: at a `float`
    /// slot the quoted token is WIRE_FORMAT §7's spelling, which the slot's type reads back,
    /// and inside a verbatim value [[verbatim]] has already refused it with its path.
    let private render (idl: Idl) (j: JVal) : Result<string, string> =
        match Json.firstIllFormedString j with
        | Some(path, what) ->
            Error(
                "ill-formed string has no canonical rendering of its own: "
                + what
                + " at "
                + path
            )
        | None ->
            match idl.Wire.KeyOrder with
            | KeyOrder.Sorted -> Ok(Canon.render j)
            | KeyOrder.Declared -> Ok(Canon.renderOrdered j)

    /// Encode an authored node to canonical wire JSON — byte-identical to the UI host.
    let encode (idl: Idl) (v: IdlValue) : Result<string, string> =
        match v with
        | VNode(id, kindTag, fields) -> encodeNode idl id kindTag fields |> Result.bind (render idl)
        | VNodeEnv(id, envelope, kindTag, fields) ->
            encodeNodeEnv idl id envelope kindTag fields |> Result.bind (render idl)
        | _ -> Error "top-level authored value must be a node"

    /// Encode an authored TREE OP to canonical wire JSON (Phase 703) — the wire's
    /// second root. Separate from [[encode]] rather than folded into it: the two
    /// roots are distinguishable on the wire (a node carries `id` + `kind`, an op a
    /// top-level `$type`), but which one a caller MEANT is not the codec's guess to
    /// make. The schema states the same thing as `oneOf`.
    let encodeOp (idl: Idl) (v: IdlValue) : Result<string, string> =
        encodeValue idl TOp v |> Result.bind (render idl)

/// WIRE_FORMAT §7's non-finite float spelling, read back (Phase 303) — the inverse of
/// `JVal.nonFiniteToken`, which is the one place the spine writes it. Internal: the
/// interpreter's float slot and the artifact's float value read through it, and nothing
/// else in the tier may widen a slot to the quoted token.
module internal FloatToken =
    /// The non-finite float a §7 token names, or `None` for any other string.
    let tryNonFinite (s: string) : float option =
        match s with
        | "NaN" -> Some System.Double.NaN
        | "Infinity" -> Some System.Double.PositiveInfinity
        | "-Infinity" -> Some System.Double.NegativeInfinity
        | _ -> None

    /// [[tryNonFinite]] as a pattern, for a decoder's float arm.
    let (|NonFinite|_|) (s: string) : float option = tryNonFinite s

/// The symmetric decode leg — the IDL also drives JSON → `IdlValue`, so the codec
/// round-trips (`encode (decode wire) = wire`). Parsing is the shared portable
/// `Fuaran.Core.Json.parse`; the IDL drives the walk. Decoders are key-order and
/// extra-key tolerant by contract (only declared fields are read), so this is the
/// floor the Phase 319 unknown-kind tolerance builds on.
module Decode =

    // Phase 310 — the walk reports a `DecodeError`: a code, the path through the document the
    // vocabulary drove it down, and the sentence this module has always returned. The string forms
    // at the foot (`value`, `decode`, `decodeOp`) answer that sentence, byte for byte; the
    // `…Detailed` forms answer the whole refusal.

    let private err (code: DecodeCode) (expected: string) (message: string) : Result<'T, DecodeError> =
        Error(DecodeError.make code expected message)

    let private under (step: PathSegment) (r: Result<'T, DecodeError>) : Result<'T, DecodeError> =
        r |> Result.mapError (DecodeError.under step)

    let private field (name: string) (fields: (string * JVal) list) : JVal option = Decoder.tryMember name (JObj fields)

    /// The tag under the DECLARED discriminator key (Phase 108) — `"$type"` on a
    /// default-shape vocabulary, so the error text is byte-identical there. An absent tag is
    /// `MissingField` and a non-string one `WrongKind`, both at the discriminator.
    let private tagUnder (key: string) (fields: (string * JVal) list) : Result<string, DecodeError> =
        match field key fields with
        | Some(JStr t) -> Ok t
        | Some _ ->
            Error(
                DecodeError.under
                    (PathSegment.Key key)
                    (DecodeError.make DecodeCode.WrongKind "string" ("missing or non-string " + key))
            )
        | None ->
            Error(
                { Decoder.missing key with
                    Message = "missing or non-string " + key }
            )

    let private dollarType (idl: Idl) (fields: (string * JVal) list) = tagUnder idl.Wire.Discriminator fields

    /// An unknown case under the discriminator: `UnknownTag`, at the discriminator, naming the known cases.
    let private unknownTag (idl: Idl) (known: string list) (message: string) : Result<'T, DecodeError> =
        Error(
            DecodeError.under
                (PathSegment.Key idl.Wire.Discriminator)
                (DecodeError.make
                    DecodeCode.UnknownTag
                    ("one of " + (known |> List.map (fun k -> "'" + k + "'") |> String.concat ", "))
                    message)
        )

    /// The arity refusal both union arms share (Phase 292 — the bare arm used to zip the
    /// two lists unchecked and throw where the object arm refused).
    let private arity (name: string) (u: IdlUnion) (args: IdlType list) =
        err
            DecodeCode.SchemaFault
            (sprintf "%d type args for union '%s'" (List.length u.Params) name)
            (sprintf "union '%s' given %d type args, expects %d" name (List.length args) (List.length u.Params))

    /// The JSON kind a type's wire form takes, for a `WrongKind` refusal's `Expected`.
    let private wireKind (t: IdlType) : string =
        match t with
        | TStr
        | TEnum _
        | TClosure
        | TFn _
        | TOpaque -> "string"
        | TInt -> "int"
        | TBool -> "bool"
        | TFloat -> "number"
        | TList _ -> "array"
        | TRecord _
        | TMap _
        | TNode
        | TKind
        | TOp
        | TUnion _ -> "object"
        | TJson
        | THosted _
        | TVar _ -> "any value"

    let rec private decodeValue (idl: Idl) (t: IdlType) (j: JVal) : Result<IdlValue, DecodeError> =
        match t, j with
        | TStr, JStr s -> Ok(VStr s)
        | TInt, JInt i -> Ok(VInt i)
        | TBool, JBool b -> Ok(VBool b)
        | TFloat, JFloat f -> Ok(VFloat f)
        | TFloat, JInt i -> Ok(VFloat(float i))
        // Phase 303 — WIRE_FORMAT §7: at a FLOAT slot the quoted tokens `"NaN"`, `"Infinity"` and
        // `"-Infinity"` are the spelling of a non-finite value, and the encoder writes exactly them
        // (`Canon` renders a non-finite `JFloat` as the quoted token). The generated F# `dFloat`, the
        // generated TypeScript decoder and the emitted JSON schema already read them back; the
        // reference interpreter was the one host refusing its own output. Only these three strings,
        // and only at a float slot — §7 stops there, so an `int` slot and a verbatim `json` slot are
        // untouched (the encoder refuses a non-finite float inside a verbatim value, Phase 292).
        | TFloat, JStr(FloatToken.NonFinite f) -> Ok(VFloat f)
        | TEnum name, JStr s ->
            match idl.Enums |> List.tryFind (fun e -> e.Name = name) with
            | Some e when List.contains s e.WireCases -> Ok(VEnum s)
            | Some e ->
                err
                    DecodeCode.UnknownTag
                    ("one of "
                     + (e.WireCases |> List.map (fun c -> "'" + c + "'") |> String.concat ", "))
                    (sprintf "enum '%s' has no case '%s'" name s)
            | None -> err DecodeCode.SchemaFault ("a declared enum '" + name + "'") (sprintf "unknown enum '%s'" name)
        | TUnion(name, args), JObj fs ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | None -> err DecodeCode.SchemaFault ("a declared union '" + name + "'") (sprintf "unknown union '%s'" name)
            | Some u ->
                match TypeParams.bind u args with
                | None -> arity name u args
                | Some subst ->
                    dollarType idl fs
                    |> Result.bind (fun tag ->
                        match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                        | None ->
                            unknownTag
                                idl
                                (u.Cases |> List.map (fun c -> c.Tag))
                                (sprintf "union '%s' has no case '%s'" name tag)
                        | Some c ->
                            let caseFields =
                                c.Fields
                                |> List.map (fun f ->
                                    { f with
                                        Type = TypeParams.substitute subst f.Type })

                            decodeFields idl caseFields fs |> Result.map (fun fields -> VUnion(tag, fields)))
        // A transparent union decoded from a BARE (non-object) wire value — the
        // Fuaran-UI 0.2.0 bare-string `TextSource.Literal` (`"x"` → `Literal{text="x"}`).
        | TUnion(name, args), j when
            (match j with
             | JObj _ -> false
             | _ -> true)
            ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | None -> err DecodeCode.SchemaFault ("a declared union '" + name + "'") (sprintf "unknown union '%s'" name)
            | Some u ->
                match TransparentUnion.tag idl.Harden u with
                | None -> err DecodeCode.WrongKind "object" (sprintf "union '%s' expects an object" name)
                | Some ttag ->
                    match TypeParams.bind u args, u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                    | None, _ -> arity name u args
                    | Some _, None ->
                        err
                            DecodeCode.SchemaFault
                            ("a declared transparent case '" + ttag + "'")
                            (sprintf "union '%s' has no transparent case '%s'" name ttag)
                    | Some subst, Some c ->
                        match
                            c.Fields
                            |> List.map (fun f ->
                                { f with
                                    Type = TypeParams.substitute subst f.Type })
                        with
                        | [ single ] ->
                            decodeValue idl single.Type j
                            |> Result.map (fun v -> VUnion(ttag, [ single.Name, v ]))
                        | _ ->
                            err
                                DecodeCode.SchemaFault
                                "a transparent case of exactly one field"
                                (sprintf "transparent union case '%s' must have exactly one field" ttag)
        | TVar v, _ -> err DecodeCode.SchemaFault "a substituted type" (sprintf "unsubstituted type variable '%s'" v)
        | TClosure, JStr "<closure>"
        | TFn _, JStr "<closure>" -> Ok VClosure
        | TOpaque, JStr "<opaque>" -> Ok VOpaque
        // Phase 676 — accept any JSON at this position, verbatim and unvalidated.
        // A shape check here would be wrong by definition: the field's whole
        // contract is that its content is not the schema's business.
        | TJson, j -> Ok(VJson j)
        // A hosted slot decodes verbatim in the interpreter — only the generated
        // F# runs the real host codec (see [[HostedCodec]]). Since Phase 252 a slot
        // that declares its wire form is checked against it first, so the interpreter
        // refuses what the host codec and the TypeScript host refuse.
        | THosted h, j ->
            match h.Wire with
            | None -> Ok(VJson j)
            | Some w ->
                decodeValue idl w j
                |> Result.bind (fun _ ->
                    match h.Format, j with
                    | None, _ -> Ok(VJson j)
                    | Some fmt, JStr s when HostedFormat.admits fmt s -> Ok(VJson j)
                    | Some fmt, _ ->
                        err
                            DecodeCode.OutOfRange
                            ("a '" + fmt + "' string")
                            (sprintf "hosted value is not a '%s' string" fmt))
        | TRecord name, JObj fs ->
            match idl.Records |> List.tryFind (fun r -> r.Name = name) with
            | None ->
                err DecodeCode.SchemaFault ("a declared record '" + name + "'") (sprintf "unknown record '%s'" name)
            | Some r -> decodeFields idl r.Fields fs |> Result.map VRecord
        | TMap vt, JObj fs ->
            let rec go acc =
                function
                | [] -> Ok(VMap(List.rev acc))
                | (k, jv) :: rest ->
                    match decodeValue idl vt jv |> under (PathSegment.Key k) with
                    | Ok v -> go ((k, v) :: acc) rest
                    | Error e -> Error e

            go [] fs
        | TNode, JObj _ -> decodeNode idl j
        | TKind, JObj fs ->
            dollarType idl fs
            |> Result.bind (fun tag ->
                match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
                | None -> unknownTag idl (idl.Kinds |> List.map (fun k -> k.Tag)) (sprintf "unknown kind '%s'" tag)
                | Some k -> decodeFields idl k.Fields fs |> Result.map (fun flds -> VUnion(tag, flds)))
        | TOp, JObj fs ->
            dollarType idl fs
            |> Result.bind (fun tag ->
                match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
                | None -> unknownTag idl (idl.Ops |> List.map (fun o -> o.Tag)) (sprintf "unknown op '%s'" tag)
                | Some o -> decodeFields idl o.Fields fs |> Result.map (fun flds -> VUnion(tag, flds)))
        | TList inner, JArr xs ->
            let rec go i acc =
                function
                | [] -> Ok(VList(List.rev acc))
                | x :: rest ->
                    match decodeValue idl inner x |> under (PathSegment.Index i) with
                    | Ok v -> go (i + 1) (v :: acc) rest
                    | Error e -> Error e

            go 0 [] xs
        | _ ->
            let expected = wireKind t
            // A sentinel position given a string that is not its sentinel holds the right KIND and
            // the wrong value; every other fall-through is a kind the type does not take.
            let code =
                match t, j with
                | (TClosure | TFn _ | TOpaque), JStr _ -> DecodeCode.OutOfRange
                | _ -> DecodeCode.WrongKind

            err code expected (sprintf "wire value does not match IDL type %A" t)

    and private decodeFields
        (idl: Idl)
        (fields: IdlField list)
        (jfields: (string * JVal) list)
        : Result<(string * IdlValue) list, DecodeError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | (f: IdlField) :: rest ->
                match field f.Name jfields, f.Opt with
                | _, HostOnly -> go acc rest
                | None, Optional -> go acc rest
                // omit-at-default: an absent field restores its identity default
                | None, OmitDefault d -> go ((f.Name, d) :: acc) rest
                | None, Required ->
                    Error(
                        { Decoder.missing f.Name with
                            Message = sprintf "required field '%s' is absent" f.Name }
                    )
                | Some j, _ ->
                    match decodeValue idl f.Type j |> under (PathSegment.Key f.Name) with
                    | Ok v -> go ((f.Name, v) :: acc) rest
                    | Error e -> Error e

        go [] fields

    /// The node envelope's own refusal: the member at fault is named by the path, the sentence is
    /// the one this module has always returned for the whole envelope.
    and private envelopeFault
        (message: string)
        (fs: (string * JVal) list)
        (members: (string * (JVal -> bool) * string) list)
        : DecodeError =
        members
        |> List.tryPick (fun (name, ok, kind) ->
            match field name fs with
            | None ->
                Some(
                    { Decoder.missing name with
                        Message = message }
                )
            | Some v when not (ok v) ->
                Some(DecodeError.under (PathSegment.Key name) (DecodeError.make DecodeCode.WrongKind kind message))
            | Some _ -> None)
        |> Option.defaultValue (DecodeError.make DecodeCode.WrongKind "a node object" message)

    and private decodeNode (idl: Idl) (j: JVal) : Result<IdlValue, DecodeError> =
        let isStr =
            function
            | JStr _ -> true
            | _ -> false

        let isObj =
            function
            | JObj _ -> true
            | _ -> false

        match j, idl.Wire.NodeEnvelope with
        | JObj fs, NodeEnvelopeShape.NestedKind ->
            match field "id" fs, field "kind" fs with
            | Some(JStr id), Some(JObj kindFs) ->
                dollarType idl kindFs
                |> under (PathSegment.Key "kind")
                |> Result.bind (fun kindTag ->
                    match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
                    | None ->
                        unknownTag idl (idl.Kinds |> List.map (fun k -> k.Tag)) (sprintf "unknown kind '%s'" kindTag)
                        |> under (PathSegment.Key "kind")
                    | Some k ->
                        decodeFields idl k.Fields kindFs
                        |> under (PathSegment.Key "kind")
                        |> Result.bind (fun fields ->
                            // Phase 698 — the envelope decodes through the same
                            // `decodeFields` the kind fields do, so it is the encoder's
                            // inverse by construction. An empty result (nothing on the
                            // wire, and no `OmitDefault` to restore) yields the bare
                            // `VNode` this returned before the envelope existed, which is
                            // why every pre-existing decode round-trip is byte-unchanged.
                            decodeFields idl idl.NodeFields fs
                            |> Result.map (function
                                | [] -> VNode(id, kindTag, fields)
                                | envelope -> VNodeEnv(id, envelope, kindTag, fields))))
            | _ ->
                Error(
                    envelopeFault
                        "node must have a string 'id' and an object 'kind'"
                        fs
                        [ "id", isStr, "string"; "kind", isObj, "object" ]
                )
        // Phase 109 — the FLAT envelope: the tag, the id, the kind's fields (and
        // any declared node envelope) share this one object. `decodeFields` reads
        // only declared names, so the discriminator and the id are tolerated as
        // the extra keys they are.
        | JObj fs, NodeEnvelopeShape.FlatKind ->
            match field "id" fs, dollarType idl fs with
            | Some(JStr id), Ok kindTag ->
                match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
                | None -> unknownTag idl (idl.Kinds |> List.map (fun k -> k.Tag)) (sprintf "unknown kind '%s'" kindTag)
                | Some k ->
                    decodeFields idl k.Fields fs
                    |> Result.bind (fun fields ->
                        decodeFields idl idl.NodeFields fs
                        |> Result.map (function
                            | [] -> VNode(id, kindTag, fields)
                            | envelope -> VNodeEnv(id, envelope, kindTag, fields)))
            | _ ->
                Error(
                    envelopeFault
                        (sprintf "node must have a string 'id' and a string '%s' discriminator" idl.Wire.Discriminator)
                        fs
                        [ "id", isStr, "string"; idl.Wire.Discriminator, isStr, "string" ]
                )
        | _, _ -> Error(DecodeError.make DecodeCode.WrongKind "object" "node must be an object")

    /// The parser's refusal under this module's own prefix (`parse failed: …`).
    let private parsed (json: string) : Result<JVal, DecodeError> =
        Decoder.parse json
        |> Result.mapError (DecodeError.reword (fun m -> "parse failed: " + m))

    /// Decode one parsed JSON value at a declared type, answering a typed refusal (Phase 310): its
    /// code, its path from `j`, what the position expected, and [[value]]'s sentence.
    let valueDetailed (idl: Idl) (t: IdlType) (j: JVal) : Result<IdlValue, DecodeError> = decodeValue idl t j

    /// [[decode]] answering a typed refusal (Phase 310) — the path runs through the vocabulary
    /// from the node's root.
    let decodeDetailed (idl: Idl) (json: string) : Result<IdlValue, DecodeError> =
        parsed json |> Result.bind (decodeNode idl)

    /// [[decodeOp]] answering a typed refusal (Phase 310).
    let decodeOpDetailed (idl: Idl) (json: string) : Result<IdlValue, DecodeError> =
        parsed json |> Result.bind (decodeValue idl TOp)

    /// Decode one parsed JSON value at a declared type (Phase 252) — the per-slot face of
    /// [[decode]], for a caller holding a value rather than a node: a hosted slot's JSON
    /// read through its declared wire form, for instance. The sentence of [[valueDetailed]]'s
    /// refusal.
    let value (idl: Idl) (t: IdlType) (j: JVal) : Result<IdlValue, string> =
        valueDetailed idl t j |> Result.mapError DecodeError.describe

    /// Decode canonical wire JSON to an authored `IdlValue`, driven by the IDL. The sentence of
    /// [[decodeDetailed]]'s refusal.
    let decode (idl: Idl) (json: string) : Result<IdlValue, string> =
        decodeDetailed idl json |> Result.mapError DecodeError.describe

    /// Decode canonical wire JSON as a TREE OP (Phase 703) — the symmetric partner
    /// of [[Encode.encodeOp]], and the wire's second root. The sentence of
    /// [[decodeOpDetailed]]'s refusal.
    let decodeOp (idl: Idl) (json: string) : Result<IdlValue, string> =
        decodeOpDetailed idl json |> Result.mapError DecodeError.describe

/// Declaration helpers for the IDL's hand-authored parts.
[<RequireQualifiedAccess>]
module Declare =

    /// An enum whose wire strings ARE its case names — the common case, and the
    /// shape every declaration had before Phase 707.
    let enumOf (name: string) (cases: string list) : IdlEnum =
        { Name = name
          Cases = cases
          Wires = []
          CaseAnnotations = [] }

    /// An enum whose wire strings differ from its host case names, declared as
    /// `(case, wire)` pairs. Taking PAIRS rather than two lists is the point: the
    /// parallel-arity invariant [[Idl.IdlEnum]] carries cannot be stated wrongly
    /// here, so the only way to violate it is to build the record by hand — which
    /// [[enumWireErrors]] then catches.
    let enumWith (name: string) (cases: (string * string) list) : IdlEnum =
        { Name = name
          Cases = cases |> List.map fst
          Wires = cases |> List.map snd
          CaseAnnotations = [] }

    /// Declare what is true ABOUT some of an enum's cases (Phase 119), by HOST case
    /// name. Sparse — name only the cases that say something; the rest read as
    /// [[Annotations.Empty]] through [[IdlEnum.AnnotationsOf]].
    ///
    /// Present as a helper rather than left to `{ e with CaseAnnotations = … }` for the
    /// reason [[enumWith]] is: it is the one construction site that can check the case
    /// exists at the moment the claim is made, rather than leaving it to
    /// [[enumWireErrors]] to find later. A name the enum does not declare is refused
    /// here — an annotation on nothing is a typo, not a declaration.
    ///
    /// Phase 384 — the refusal is an ERROR LIST, as [[enumWireErrors]]' is and in its
    /// sentences: one per annotation naming a case the enum does not declare, and one when
    /// two entries name the same case. It used to raise, the one `Declare.*` function
    /// that did.
    let enumAnnotate (annotations: (string * Annotations) list) (e: IdlEnum) : Result<IdlEnum, string list> =
        let annotated = annotations |> List.map fst

        let errors =
            [ for c in annotated do
                  if not (List.contains c e.Cases) then
                      sprintf "enum '%s': annotation names case '%s', which the enum does not declare" e.Name c

              if List.length (List.distinct annotated) <> List.length annotated then
                  sprintf "enum '%s': two annotation entries name the same case" e.Name ]

        match errors with
        | [] -> Ok { e with CaseAnnotations = annotations }
        | _ -> Error errors

    /// Well-formedness of every enum's case↔wire mapping — the backstop for a
    /// record built by literal rather than through [[enumOf]] / [[enumWith]].
    /// Empty list ⇒ well-formed. Checks the arity the pair-taking constructor
    /// makes unrepresentable, plus the two duplicate classes that would make the
    /// mapping non-invertible (a repeated case name, or two cases sharing one
    /// wire string — the latter silently collapses on decode).
    let enumWireErrors (idl: Idl) : string list =
        [ for e in idl.Enums do
              let cases, wires = e.Cases, e.Wires

              if not (List.isEmpty wires) && List.length wires <> List.length cases then
                  sprintf
                      "enum '%s': %d case(s) but %d wire string(s) — the lists must be parallel (or Wires empty)"
                      e.Name
                      (List.length cases)
                      (List.length wires)

              if List.length (List.distinct cases) <> List.length cases then
                  sprintf "enum '%s': duplicate case name" e.Name

              if
                  not (List.isEmpty wires)
                  && List.length (List.distinct wires) <> List.length wires
              then
                  sprintf "enum '%s': two cases share a wire string — decoding would not be invertible" e.Name

              // Phase 119 — the one class the sparse, keyed [[IdlEnum.CaseAnnotations]]
              // shape admits and the positional one could not: a name that is not a case.
              // [[Declare.enumAnnotate]] refuses it at the construction site; this is the
              // backstop for a record built by literal, exactly as the arity check above is.
              let annotated = e.CaseAnnotations |> List.map fst

              for c in annotated do
                  if not (List.contains c cases) then
                      sprintf "enum '%s': annotation names case '%s', which the enum does not declare" e.Name c

              if List.length (List.distinct annotated) <> List.length annotated then
                  sprintf "enum '%s': two annotation entries name the same case" e.Name ]

    /// Well-formedness of every hosted slot's declared wire form (Phase 252). Empty
    /// list ⇒ well-formed. A wire form is a type the other legs can read without the
    /// host codec — a scalar, a declared enum, record or union, or a list or map of
    /// those — so another erased slot (`json`, `hosted`, a closure, a sentinel), a
    /// node, a tree-op or a type variable is refused; a format needs a string wire and
    /// must be one [[HostedFormat]] knows.
    let hostedWireErrors (idl: Idl) : string list =
        let rec readable (t: IdlType) : string option =
            match t with
            | TStr
            | TInt
            | TBool
            | TFloat -> None
            | TEnum n when idl.Enums |> List.exists (fun e -> e.Name = n) -> None
            | TRecord n when idl.Records |> List.exists (fun r -> r.Name = n) -> None
            | TUnion(n, args) when idl.Unions |> List.exists (fun u -> u.Name = n) -> args |> List.tryPick readable
            | TEnum n
            | TRecord n
            | TUnion(n, _) -> Some(sprintf "names '%s', which the vocabulary does not declare" n)
            | TList inner
            | TMap inner -> readable inner
            | other -> Some(sprintf "is %A, which no leg but the host codec can read" other)

        let rec hostedIn (t: IdlType) : HostedCodec list =
            match t with
            | THosted h -> [ h ]
            | TList inner
            | TMap inner -> hostedIn inner
            | TUnion(_, args) -> args |> List.collect hostedIn
            | _ -> []

        let fieldSets =
            [ for k in idl.Kinds -> "kind " + k.Tag, k.Fields
              for o in idl.Ops -> "op " + o.Tag, o.Fields
              for r in idl.Records -> "record " + r.Name, r.Fields
              for u in idl.Unions do
                  for c in u.Cases -> sprintf "union %s.%s" u.Name c.Tag, c.Fields
              yield "the node envelope", idl.NodeFields ]

        [ for owner, fields in fieldSets do
              for f in fields do
                  for h in hostedIn f.Type do
                      let at = sprintf "%s, field '%s' (hosted %s)" owner f.Name h.FSharp

                      match h.Wire |> Option.bind readable with
                      | Some why -> sprintf "%s: the declared wire form %s" at why
                      | None -> ()

                      match h.Format, h.Wire with
                      | Some fmt, _ when not (List.contains fmt HostedFormat.known) ->
                          sprintf
                              "%s: format '%s' is not one the engine knows (%s)"
                              at
                              fmt
                              (String.concat ", " HostedFormat.known)
                      | Some fmt, w when w <> Some TStr ->
                          sprintf "%s: format '%s' needs a string wire form (Wire = Some TStr)" at fmt
                      | _ -> () ]

    /// Well-formedness of the declared wire shape (Phases 108/109). Empty list ⇒
    /// well-formed. The discriminator shares an object with a tagged body's own
    /// fields in EVERY shape, and in [[NodeEnvelopeShape.FlatKind]] the node's
    /// `id`, its kind fields and its declared envelope all share one object — so
    /// the reserved names are checked here rather than colliding silently on the
    /// wire.
    let wireShapeErrors (idl: Idl) : string list =
        let disc = idl.Wire.Discriminator
        let flat = idl.Wire.NodeEnvelope = NodeEnvelopeShape.FlatKind

        let fieldClash (owner: string) (fields: IdlField list) =
            [ for f in fields do
                  if f.Name = disc then
                      sprintf "%s: field '%s' collides with the declared discriminator key" owner f.Name

                  if flat && f.Name = "id" then
                      sprintf "%s: field 'id' is reserved in the flat node envelope" owner ]

        [ if System.String.IsNullOrWhiteSpace disc then
              "wire shape: the discriminator key must be a non-empty string"

          // The emitters splice the key into generated JS/F# source literals, so
          // quote-class characters are refused at declaration rather than emitted.
          if
              disc
              |> Seq.exists (fun c -> c = '"' || c = '\'' || c = '\\' || System.Char.IsControl c)
          then
              "wire shape: the discriminator key must not contain quotes, backslashes or control characters"

          if disc = "id" then
              "wire shape: the discriminator key 'id' collides with the node id"

          yield!
              idl.Kinds
              |> List.collect (fun k -> fieldClash ("kind '" + k.Tag + "'") k.Fields)
          yield! idl.Ops |> List.collect (fun o -> fieldClash ("op '" + o.Tag + "'") o.Fields)

          yield!
              idl.Unions
              |> List.collect (fun u ->
                  u.Cases
                  |> List.collect (fun c ->
                      [ for f in c.Fields do
                            if f.Name = disc then
                                sprintf
                                    "union '%s' case '%s': field '%s' collides with the declared discriminator key"
                                    u.Name
                                    c.Tag
                                    f.Name ]))

          yield! fieldClash "node envelope" idl.NodeFields

          // Flat only: the envelope and every kind body share one object, so an
          // envelope name reappearing as a kind field is ambiguous on the wire.
          if flat then
              let envNames = idl.NodeFields |> List.map (fun f -> f.Name) |> Set.ofList

              yield!
                  idl.Kinds
                  |> List.collect (fun k ->
                      [ for f in k.Fields do
                            if envNames.Contains f.Name then
                                sprintf
                                    "kind '%s': field '%s' collides with a node-envelope field in the flat shape"
                                    k.Tag
                                    f.Name ]) ]

    /// Whether `s` is a name every backend can spell bare as an identifier: an ASCII letter
    /// or `_`, then ASCII letters, digits and `_`. Every declared NAME the generators emit
    /// as an identifier (a kind or op tag, a type name, a case, a field, a type parameter)
    /// is held to it — an identifier cannot be escaped, so a name that is not one is a
    /// splice of source text into the generated module.
    let private isIdentifier (s: string) : bool =
        let letter (c: char) =
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '_'

        s.Length > 0
        && letter s[0]
        && s |> Seq.forall (fun c -> letter c || (c >= '0' && c <= '9'))

    /// Whether `s` holds an unpaired surrogate — text with no UTF-8 encoding, so no canonical
    /// bytes, and no spelling in an F# or F* literal.
    let private illFormed (s: string) : bool = not (SourceLit.isWellFormed s)

    /// The field names no vocabulary may declare (Phase 348, DECISIONS.md D114): every name a
    /// JavaScript object carries before anything is written to it — `Object.prototype`'s own
    /// members, `__proto__` among them. A generated TypeScript host holds a node, a spec, a
    /// record and a union case as plain objects, so a field of one of these names is not data
    /// there: `__proto__` written in an object literal SETS the prototype (the member is never
    /// held, and the encoder reads the prototype back), and an absent optional member of any
    /// other reads as the inherited function (the encoder then throws). The rule is the IDL's,
    /// so it binds every host: a vocabulary is one document, and a name one host cannot carry
    /// is a name the vocabulary does not have. `prototype` is NOT here — a plain object has no
    /// such member to inherit, and it measured clean on every path.
    let private inheritedMemberNames: Set<string> =
        set
            [ "__proto__"
              "__defineGetter__"
              "__defineSetter__"
              "__lookupGetter__"
              "__lookupSetter__"
              "constructor"
              "hasOwnProperty"
              "isPrototypeOf"
              "propertyIsEnumerable"
              "toLocaleString"
              "toString"
              "valueOf" ]

    /// Whether `s` carries a control character or a line break (U+0085, U+2028, U+2029
    /// included) — what a single-line slot spliced into a comment must not carry.
    let private controlOrBreak (s: string) : bool =
        s
        |> Seq.exists (fun c -> System.Char.IsControl c || c = '\u2028' || c = '\u2029')

    /// Whether a value of `t` can encode to a JSON OBJECT — the shape a transparent case
    /// must not have (Phase 292 amendment): the case goes out BARE, and a bare object is
    /// what every other case looks like, so the hosts disagree on which case it is.
    let rec private objectCapable (t: IdlType) : bool =
        match t with
        | TJson
        | TRecord _
        | TMap _
        | TUnion _
        | TNode
        | TKind
        | TOp -> true
        | THosted h ->
            match h.Wire with
            | None -> true
            | Some w -> objectCapable w
        | _ -> false

    /// **Every well-formedness rule a vocabulary is held to, in one call (Phase 292)** —
    /// the union of [[wireShapeErrors]], [[enumWireErrors]] and [[hostedWireErrors]] with
    /// the referential and lexical checks nothing used to make. Empty list ⇒ well-formed.
    ///
    /// An `idl.json` is UNTRUSTED input (DECISIONS D95): `Artifact.ofJson` and
    /// `Proposal.applyDelta` run this and refuse a vocabulary it reports, naming every
    /// error, so a hand-edited artifact never reaches an emitter. The checks beyond the
    /// three it unions:
    ///
    /// - **references** — every `TEnum` / `TRecord` / `TUnion` name resolves, every union is
    ///   applied to as many type arguments as it declares parameters, and a `TVar` names a
    ///   parameter of the union whose case declares it;
    /// - **duplicates** — kind tags, op tags, type names (enums, unions and records share
    ///   one namespace in every generated host), case tags, type parameters, field names
    ///   within one owner, and default entries;
    /// - **defaults** — an `OmitDefault` value, and an [[IdlDefault]] addressing a declared
    ///   field, are values of the slot's type (checked by the encoder itself, so "fits"
    ///   means "encodes"); a slot whose type mentions a type variable is checked where it is
    ///   instantiated, by the encoder;
    /// - **slots** — a `HostOnly` field is a `TFn`; a declared transparent case carries
    ///   exactly one field, and that field cannot encode to an object, at its declaration
    ///   or at any instantiation of its union;
    /// - **the envelope** — `id` is reserved in every shape and `kind` beside it under
    ///   [[NodeEnvelopeShape.NestedKind]], where an envelope field of either name emitted a
    ///   duplicate key or an undecodable node;
    /// - **inherited member names** (Phase 348) — no field anywhere is named `__proto__` or
    ///   any other member every JavaScript object carries (`constructor`, `toString`, …);
    /// - **text** — every emitted NAME is an identifier; a category and a deprecation's
    ///   replacement, message and version are single-line text; and no declared string is
    ///   ill-formed UTF-16. A `Doc` (Phase 255) is free prose and may span lines.
    ///
    /// A field-less kind or record is well-formed: a marker carries its meaning in its tag.
    let errors (idl: Idl) : string list =
        let enumNames = idl.Enums |> List.map (fun e -> e.Name) |> Set.ofList
        let recordNames = idl.Records |> List.map (fun r -> r.Name) |> Set.ofList

        let unionOf (n: string) =
            idl.Unions |> List.tryFind (fun u -> u.Name = n)

        let transparentField (u: IdlUnion) : IdlField option =
            match TransparentUnion.tag idl.Harden u with
            | Some tag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | Some { Fields = [ single ] } -> Some single
                | _ -> None
            | None -> None

        let rec mentionsVar (t: IdlType) =
            match t with
            | TVar _ -> true
            | TList inner
            | TMap inner -> mentionsVar inner
            | TUnion(_, args) -> args |> List.exists mentionsVar
            | _ -> false

        let rec typeErrors (at: string) (pars: string list) (t: IdlType) : string list =
            match t with
            | TEnum n when not (enumNames.Contains n) ->
                [ sprintf "%s: names enum '%s', which the vocabulary does not declare" at n ]
            | TRecord n when not (recordNames.Contains n) ->
                [ sprintf "%s: names record '%s', which the vocabulary does not declare" at n ]
            | TUnion(n, args) ->
                let inner = args |> List.collect (typeErrors at pars)

                match unionOf n with
                | None ->
                    sprintf "%s: names union '%s', which the vocabulary does not declare" at n
                    :: inner
                | Some u ->
                    match TypeParams.bind u args with
                    | None ->
                        sprintf
                            "%s: applies union '%s' to %d type argument(s); it declares %d"
                            at
                            n
                            (List.length args)
                            (List.length u.Params)
                        :: inner
                    | Some subst ->
                        match transparentField u with
                        | Some f when mentionsVar f.Type && objectCapable (TypeParams.substitute subst f.Type) ->
                            sprintf
                                "%s: instantiates union '%s' so that its transparent case's field '%s' can encode to an object — a bare object is indistinguishable from a tagged case"
                                at
                                n
                                f.Name
                            :: inner
                        | _ -> inner
            | TVar v when not (List.contains v pars) ->
                [ sprintf "%s: type variable '%s' is not a parameter of the union declaring it" at v ]
            | TList inner
            | TMap inner -> typeErrors at pars inner
            | _ -> []

        let annotationErrors (at: string) (a: Annotations) : string list =
            [ let single (slot: string) (v: string option) =
                  [ match v with
                    | Some s when controlOrBreak s ->
                        sprintf "%s: the annotation's %s carries a line break or control character" at slot
                    | Some s when illFormed s -> sprintf "%s: the annotation's %s is ill-formed UTF-16" at slot
                    | _ -> () ]

              match a.Deprecated with
              | Some d ->
                  yield! single "deprecation replacement" d.Replacement
                  yield! single "deprecation message" d.Message
              | None -> ()

              yield! single "version (since)" a.Since

              match a.Doc with
              | Some d when illFormed d -> sprintf "%s: the annotation's doc is ill-formed UTF-16" at
              | _ -> () ]

        let nameErrors (what: string) (names: string list) : string list =
            [ for n in names do
                  if not (isIdentifier n) then
                      sprintf "%s '%s' is not an identifier (an ASCII letter or '_', then letters, digits, '_')" what n

              for n in names |> List.countBy id |> List.filter (fun (_, c) -> c > 1) |> List.map fst do
                  sprintf "%s '%s' is declared more than once" what n ]

        let fieldErrors (owner: string) (pars: string list) (fields: IdlField list) : string list =
            [ yield! nameErrors (owner + ": field") (fields |> List.map (fun f -> f.Name))

              for f in fields do
                  if inheritedMemberNames.Contains f.Name then
                      sprintf
                          "%s: field '%s' is reserved — every JavaScript object already carries a member of that name, so a generated TypeScript host cannot hold it as data"
                          owner
                          f.Name

              for f in fields do
                  let at = sprintf "%s, field '%s'" owner f.Name
                  yield! typeErrors at pars f.Type
                  yield! annotationErrors at f.Annotations

                  match f.Opt, f.Type with
                  | HostOnly, TFn _ -> ()
                  | HostOnly, other ->
                      sprintf
                          "%s: a HostOnly field must be a TFn (it carries the host type and the placeholder), not %A"
                          at
                          other
                  | OmitDefault d, t when not (mentionsVar t) ->
                      match Encode.valueJson idl t d with
                      | Ok j when (Json.firstIllFormedString j).IsSome ->
                          sprintf "%s: the omit-at-default value holds an ill-formed UTF-16 string" at
                      | Ok _ -> ()
                      | Error m -> sprintf "%s: the omit-at-default value does not fit the field's type (%s)" at m
                  | _ -> () ]

        let kindErrors (what: string) (k: IdlKind) : string list =
            let owner = sprintf "%s '%s'" what k.Tag

            [ if controlOrBreak k.Category || illFormed k.Category then
                  sprintf "%s: the category carries a line break, a control character or ill-formed UTF-16" owner

              yield! annotationErrors owner k.Annotations
              yield! fieldErrors owner [] k.Fields ]

        [ yield! wireShapeErrors idl
          yield! enumWireErrors idl
          yield! hostedWireErrors idl

          // Names, and the namespaces they share.
          yield! nameErrors "kind" (idl.Kinds |> List.map (fun k -> k.Tag))
          yield! nameErrors "op" (idl.Ops |> List.map (fun o -> o.Tag))

          yield!
              nameErrors
                  "type name"
                  ((idl.Enums |> List.map (fun e -> e.Name))
                   @ (idl.Unions |> List.map (fun u -> u.Name))
                   @ (idl.Records |> List.map (fun r -> r.Name)))

          for k in idl.Kinds do
              yield! kindErrors "kind" k

          for o in idl.Ops do
              yield! kindErrors "op" o

          for r in idl.Records do
              yield! fieldErrors (sprintf "record '%s'" r.Name) [] r.Fields

          for u in idl.Unions do
              let owner = sprintf "union '%s'" u.Name
              yield! nameErrors (owner + ": type parameter") u.Params
              yield! nameErrors (owner + ": case") (u.Cases |> List.map (fun c -> c.Tag))

              for c in u.Cases do
                  let caseOwner = sprintf "union '%s' case '%s'" u.Name c.Tag
                  yield! annotationErrors caseOwner c.Annotations
                  yield! fieldErrors caseOwner u.Params c.Fields

          for e in idl.Enums do
              let owner = sprintf "enum '%s'" e.Name
              // A case NAME is the host identifier; its WIRE string is data, escaped where
              // it is spliced, and held only to well-formedness.
              yield!
                  e.Cases
                  |> List.filter (fun c -> not (isIdentifier c))
                  |> List.map (fun c -> sprintf "%s: case '%s' is not an identifier" owner c)

              for w in e.WireCases do
                  if illFormed w then
                      sprintf "%s: a wire string is ill-formed UTF-16" owner

              for c, a in e.CaseAnnotations do
                  yield! annotationErrors (sprintf "%s case '%s'" owner c) a

          yield! fieldErrors "the node envelope" [] idl.NodeFields

          // The envelope's reserved names (Phase 292 amendment): `id` beside the node's own
          // id in every shape — the flat shape's rule is [[wireShapeErrors]]' — and `kind`
          // beside the nested kind body.
          if idl.Wire.NodeEnvelope = NodeEnvelopeShape.NestedKind then
              for f in idl.NodeFields do
                  if f.Name = "id" || f.Name = "kind" then
                      sprintf "the node envelope: field '%s' is reserved beside the nested kind body" f.Name

          if illFormed idl.Wire.Discriminator then
              "wire shape: the discriminator key is ill-formed UTF-16"

          // Declared (authoring) defaults: a value of its slot's type, once per address. An
          // address naming no field is inert — no constructor reads it — and the artifact
          // carries it verbatim, so it is not refused here.
          for d in idl.Defaults do
              let owner =
                  if d.Kind = "" then
                      Some idl.NodeFields
                  else
                      idl.Kinds
                      |> List.tryFind (fun k -> k.Tag = d.Kind)
                      |> Option.map (fun k -> k.Fields)

              let at =
                  if d.Kind = "" then
                      sprintf "default for envelope field '%s'" d.Field
                  else
                      sprintf "default for '%s.%s'" d.Kind d.Field

              match owner |> Option.bind (List.tryFind (fun f -> f.Name = d.Field)) with
              | None -> ()
              | Some f when mentionsVar f.Type -> ()
              | Some f ->
                  match Encode.valueJson idl f.Type d.Value with
                  | Ok j when (Json.firstIllFormedString j).IsSome ->
                      sprintf "%s: the value holds an ill-formed UTF-16 string" at
                  | Ok _ -> ()
                  | Error m -> sprintf "%s: the value does not fit the field's type (%s)" at m

          for (k, f), c in idl.Defaults |> List.countBy (fun d -> d.Kind, d.Field) do
              if c > 1 then
                  sprintf "default for '%s.%s' is declared more than once" k f

          // Declared transparent cases — their WIRE consequence. Whether a harden token names
          // a member at all is the hardener's to check where it uses one (`Trust`), since a
          // token naming nothing changes no encoding.
          for name, tag in idl.Harden.TransparentUnions do
              let at = sprintf "harden policy: transparent case '%s.%s'" name tag

              match
                  unionOf name
                  |> Option.bind (fun u -> u.Cases |> List.tryFind (fun c -> c.Tag = tag))
              with
              | None -> ()
              | Some c ->
                  match c with
                  | { Fields = [ single ] } ->
                      if not (mentionsVar single.Type) && objectCapable single.Type then
                          sprintf
                              "%s: its field '%s' can encode to an object — a bare object is indistinguishable from a tagged case, and the hosts disagree on it"
                              at
                              single.Name
                  | _ -> sprintf "%s must carry exactly one field — it is on the wire BARE" at

          for name, c in idl.Harden.TransparentUnions |> List.countBy fst do
              if c > 1 then
                  sprintf "harden policy: union '%s' declares more than one transparent case" name ]
