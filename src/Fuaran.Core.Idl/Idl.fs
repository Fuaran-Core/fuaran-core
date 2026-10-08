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

/// A closed string FORMAT a hosted slot's declared wire form may name (Phase 252; a `string`
/// over [[HostedFormat.known]] before `1.0.0`, a union since Phase 391). The names are JSON
/// Schema's spellings — [[HostedFormat.name]] — and [[HostedFormat.admits]] is the one
/// definition of what each admits.
[<RequireQualifiedAccess>]
type HostedFormat =
    /// RFC 3339 `full-date`, spelled `date`: a real calendar day, years 0001–9999.
    | Date
    /// RFC 3339 `date-time`, spelled `date-time`: an offset is required; `T`/`Z` in either case;
    /// no leap second.
    | DateTime
    /// The 8-4-4-4-12 hex form in either case, spelled `uuid`.
    | Uuid

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
        Format: HostedFormat option
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
