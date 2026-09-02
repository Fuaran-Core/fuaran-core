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
/// sentinel without ever reading the value, and the decoder reads presence only.
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
    { FSharp: string
      TypeScript: string
      Placeholder: string }

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
/// Everywhere else — the schema, the TypeScript backend, the interpreter's
/// `IdlValue` carrier, the sampler — a hosted slot behaves exactly like [[TJson]]:
/// the JSON is carried verbatim, because its content is the host codec's business,
/// not the schema's.
type HostedCodec =
    { FSharp: string
      Encode: string
      Decode: string }

/// The structural type of a field's value on the wire.
type IdlType =
    | TStr
    | TInt
    | TBool
    | TFloat
    | TEnum of enumName: string
    | TUnion of unionName: string * args: IdlType list
    | TVar of paramName: string
    | TNode
    | TList of IdlType
    /// A function-typed field (Binding accessor, `Action` callback, `onChange`
    /// handler, column projection): unobservable on the wire, rendered as the
    /// fixed sentinel string `"<closure>"`. The real ~40-kind `Fuaran.UI` tier is
    /// full of these (Phase 317 real-tier migration); the spike had none. There is
    /// no authored content — the encoder emits the sentinel unconditionally.
    | TClosure
    /// A function-typed field carrying its **host signature** (Phase 689). Wire
    /// behaviour is identical to [[TClosure]] in every respect — same `"<closure>"`
    /// sentinel, same presence-only decode, same schema. The only difference is the
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
    /// codec expressions; every other backend carries the JSON verbatim ([[TJson]]).
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
/// **[[Default]] is exactly the set the engine hard-coded**, so a vocabulary that
/// declares nothing behaves byte-for-byte as it did and every pre-Phase-116
/// `idl.json` is unchanged (the artifact omits the block at the default).
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
        /// `"Custom"` by default.
        GatedKind: string
        /// The kind tag of the inert placeholder a gated-out node becomes — a
        /// benign node that renders text and never a live call. `"Markdown"`.
        PlaceholderKind: string
        /// The placeholder kind's single field, which carries the label text.
        /// `"text"`. Distinct from [[TextLiteralField]] on purpose: this names a
        /// KIND's field, that one a UNION CASE's, and a domain may spell them
        /// differently.
        PlaceholderField: string
        /// The union case carrying literal (already-resolved) TEXT — what the
        /// placeholder label is wrapped in, and what the markdown scrub matches.
        /// `"Literal"`.
        TextLiteralCase: string
        /// [[TextLiteralCase]]'s single field. `"text"`.
        TextLiteralField: string
        /// The union case carrying a literal (inline, not by-name) VALUE — what
        /// the URL sanitiser matches on a declared URL field. `"Static"`.
        ValueLiteralCase: string
        /// [[ValueLiteralCase]]'s single field. `"value"`.
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

    /// The tokens the engine hard-coded before they were declarable — the
    /// Fuaran-UI vocabulary's names, which is where they came from. A vocabulary
    /// carrying this behaves exactly as every vocabulary did before Phase 116.
    static member Default =
        { GatedKind = "Custom"
          PlaceholderKind = "Markdown"
          PlaceholderField = "text"
          TextLiteralCase = "Literal"
          TextLiteralField = "text"
          ValueLiteralCase = "Static"
          ValueLiteralField = "value"
          TransparentUnions = [ "TextSource", "Literal" ] }

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
/// notes representable, and nothing downstream could choose between them. Three
/// named slots cannot state that.
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
    }

    /// No annotations — the default, and what every declaration written before
    /// Phase 113 means. The artifact omits an empty set entirely, so an
    /// unannotated vocabulary's `idl.json` is byte-for-byte what it was.
    static member Empty =
        { Deprecated = None
          InProcessOnly = false
          Since = None }

    /// Whether this set says nothing. The emitters and the artifact both branch on
    /// it, so the "absent is the default and omitted" rule has one definition.
    member this.IsEmpty =
        this.Deprecated.IsNone && not this.InProcessOnly && this.Since.IsNone

/// Whether a field is always present, omitted on the wire when absent, or
/// omitted on the wire when equal to an identity default (omit-on-absence and
/// omit-at-default are both wire-visible). `OmitDefault d`: the field always has a
/// semantic value; the encoder emits it only when it differs from `d`, and the
/// decoder restores `d` on absence — the Fuaran-UI Phase 147 (role/voice) + Phase
/// 460 (tone/weight/emphasis/format/width) omit-when-default wire discipline.
type Optionality =
    | Required
    | Optional
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

and IdlField =
    {
        Name: string
        Type: IdlType
        Opt: Optionality
        /// What is true ABOUT this field, as opposed to its shape (Phase 113).
        /// [[Annotations.Empty]] for a field that says nothing, which is every field
        /// declared before the set existed.
        Annotations: Annotations
    }

/// A node kind — flat `$type`-discriminated on the wire (`Category` is metadata, not serialised).
and IdlKind =
    { Tag: string
      Category: string
      Fields: IdlField list }

and IdlUnionCase =
    {
        Tag: string
        Fields: IdlField list
        /// What is true ABOUT this case (Phase 113) — see [[IdlField.Annotations]].
        Annotations: Annotations
    }

/// A `$type`-discriminated value union (e.g. `Binding` has cases `Static` / `State`).
and IdlUnion =
    { Name: string
      Params: string list
      Cases: IdlUnionCase list }

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
/// **Build these with [[Idl.enumOf]] / [[Idl.enumWith]] rather than by record
/// literal.** `enumWith` takes `(case, wire)` PAIRS, so the parallel-arity
/// invariant cannot be stated wrongly; [[Idl.enumWireErrors]] is the backstop
/// for a record built by hand.
and IdlEnum =
    { Name: string
      Cases: string list
      Wires: string list }

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
and IdlRecord = { Name: string; Fields: IdlField list }

/// An authored value, checked and encoded against the IDL.
and IdlValue =
    | VStr of string
    | VInt of int
    | VBool of bool
    | VFloat of float
    | VEnum of string
    | VUnion of tag: string * fields: (string * IdlValue) list
    | VList of IdlValue list
    | VNode of id: string * kindTag: string * fields: (string * IdlValue) list
    /// A node carrying its ENVELOPE as well as its kind (Phase 698) — the
    /// `WIRE_FORMAT.md` §3.1 fields a node holds beside `id`/`kind`, declared per
    /// domain in [[Idl.NodeFields]].
    ///
    /// **Why a sibling case rather than a fourth slot on [[VNode]].** Widening
    /// `VNode`'s arity would break every authored construction site in the estate
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
/// `Value` is the default authored value.
type IdlDefault =
    { Kind: string
      Field: string
      Value: IdlValue }

/// The whole IDL — kinds, value-unions, enums, non-discriminated records, and
/// field defaults. The canonical root.
type Idl =
    {
        Kinds: IdlKind list
        Unions: IdlUnion list
        Enums: IdlEnum list
        Records: IdlRecord list
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
        /// case. [[HardenPolicy.Default]] is the set the engine hard-coded, so a
        /// vocabulary that declares nothing is byte-for-byte unchanged.
        ///
        /// Declared rather than hard-coded for the same reason [[NodeFields]] and
        /// [[Wire]] are: what a vocabulary CALLS the node it refuses to resolve live
        /// is a property of the domain, not of the engine.
        Harden: HardenPolicy
    }

/// Declaration helpers for the IDL's hand-authored parts.
[<RequireQualifiedAccess>]
module Declare =

    /// An enum whose wire strings ARE its case names — the common case, and the
    /// shape every declaration had before Phase 707.
    let enumOf (name: string) (cases: string list) : IdlEnum =
        { Name = name
          Cases = cases
          Wires = [] }

    /// An enum whose wire strings differ from its host case names, declared as
    /// `(case, wire)` pairs. Taking PAIRS rather than two lists is the point: the
    /// parallel-arity invariant [[Idl.IdlEnum]] carries cannot be stated wrongly
    /// here, so the only way to violate it is to build the record by hand — which
    /// [[enumWireErrors]] then catches.
    let enumWith (name: string) (cases: (string * string) list) : IdlEnum =
        { Name = name
          Cases = cases |> List.map fst
          Wires = cases |> List.map snd }

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
                  sprintf "enum '%s': two cases share a wire string — decoding would not be invertible" e.Name ]

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

/// A "transparent" union case is encoded/decoded as a bare JSON value (its single
/// field's value) rather than a `$type`-tagged object — the Fuaran-UI 0.2.0
/// bare-string canonical `TextSource.Literal` (`{"$type":"Literal","text":"x"}` →
/// `"x"`). Keyed on the well-known union name; the transparent case carries exactly
/// one field. The `Bound` / non-transparent cases stay `$type`-tagged objects.
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
/// declares on its own [[Idl]] value and the artifact carries. `HardenPolicy.Default`
/// still names that union, so a vocabulary that declares nothing encodes exactly as it
/// did — the hard-coding became a DEFAULT rather than disappearing, which is what keeps
/// every shipped corpus byte-identical.
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

    /// Substitute a union's type parameters into a case field's type (`'T` → the type arg).
    let rec private substitute (subst: Map<string, IdlType>) (t: IdlType) : IdlType =
        match t with
        | TVar v ->
            match Map.tryFind v subst with
            | Some r -> r
            | None -> t
        | TList inner -> TList(substitute subst inner)
        | TMap inner -> TMap(substitute subst inner)
        | TUnion(n, args) -> TUnion(n, List.map (substitute subst) args)
        | other -> other

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
            | Some u when List.length u.Params <> List.length args ->
                Error(
                    sprintf "union '%s' given %d type args, expects %d" name (List.length args) (List.length u.Params)
                )
            | Some u ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | None -> Error(sprintf "union '%s' has no case '%s'" name tag)
                | Some c ->
                    let subst = Map.ofList (List.zip u.Params args)

                    let caseFields =
                        c.Fields
                        |> List.map (fun f ->
                            { f with
                                Type = substitute subst f.Type })

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
        | TJson, VJson j -> Ok j
        // A hosted slot's content is the host codec's business — the interpreter
        // carries it verbatim, exactly as TJson (see [[HostedCodec]]).
        | THosted _, VJson j -> Ok j
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
                    // omit-at-default: a present value equal to the field's identity default emits nothing
                    | Some v, OmitDefault d when v = d -> go acc rest
                    | Some v, _ ->
                        match encodeValue idl f.Type v with
                        | Ok j -> go ((f.Name, j) :: acc) rest
                        | Error m -> Error m

            go [] fields

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
    and encodeNodeEnv
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

    /// The declared canonical renderer (Phase 111): Ordinal-sorted by default,
    /// authored order under `KeyOrder.Declared` — where the encoder's own
    /// construction order (discriminator, id, declared fields) is normative.
    let private render (idl: Idl) : JVal -> string =
        match idl.Wire.KeyOrder with
        | KeyOrder.Sorted -> Canon.render
        | KeyOrder.Declared -> Canon.renderOrdered

    /// Encode an authored node to canonical wire JSON — byte-identical to the UI host.
    let encode (idl: Idl) (v: IdlValue) : Result<string, string> =
        match v with
        | VNode(id, kindTag, fields) -> encodeNode idl id kindTag fields |> Result.map (render idl)
        | VNodeEnv(id, envelope, kindTag, fields) ->
            encodeNodeEnv idl id envelope kindTag fields |> Result.map (render idl)
        | _ -> Error "top-level authored value must be a node"

    /// Encode an authored TREE OP to canonical wire JSON (Phase 703) — the wire's
    /// second root. Separate from [[encode]] rather than folded into it: the two
    /// roots are distinguishable on the wire (a node carries `id` + `kind`, an op a
    /// top-level `$type`), but which one a caller MEANT is not the codec's guess to
    /// make. The schema states the same thing as `oneOf`.
    let encodeOp (idl: Idl) (v: IdlValue) : Result<string, string> =
        encodeValue idl TOp v |> Result.map (render idl)

/// The symmetric decode leg — the IDL also drives JSON → `IdlValue`, so the codec
/// round-trips (`encode (decode wire) = wire`). Parsing is the shared portable
/// `Fuaran.Core.Json.parse`; the IDL drives the walk. Decoders are key-order and
/// extra-key tolerant by contract (only declared fields are read), so this is the
/// floor the Phase 319 unknown-kind tolerance builds on.
module Decode =

    let private field (name: string) (fields: (string * JVal) list) =
        fields |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

    /// The tag under the DECLARED discriminator key (Phase 108) — `"$type"` on a
    /// default-shape vocabulary, so the error text is byte-identical there.
    let private tagUnder (key: string) (fields: (string * JVal) list) =
        match field key fields with
        | Some(JStr t) -> Ok t
        | _ -> Error("missing or non-string " + key)

    let private dollarType (idl: Idl) (fields: (string * JVal) list) = tagUnder idl.Wire.Discriminator fields

    let rec private substitute (subst: Map<string, IdlType>) (t: IdlType) : IdlType =
        match t with
        | TVar v ->
            match Map.tryFind v subst with
            | Some r -> r
            | None -> t
        | TList inner -> TList(substitute subst inner)
        | TMap inner -> TMap(substitute subst inner)
        | TUnion(n, args) -> TUnion(n, List.map (substitute subst) args)
        | other -> other

    let rec private decodeValue (idl: Idl) (t: IdlType) (j: JVal) : Result<IdlValue, string> =
        match t, j with
        | TStr, JStr s -> Ok(VStr s)
        | TInt, JInt i -> Ok(VInt i)
        | TBool, JBool b -> Ok(VBool b)
        | TFloat, JFloat f -> Ok(VFloat f)
        | TFloat, JInt i -> Ok(VFloat(float i))
        | TEnum name, JStr s ->
            match idl.Enums |> List.tryFind (fun e -> e.Name = name) with
            | Some e when List.contains s e.WireCases -> Ok(VEnum s)
            | Some _ -> Error(sprintf "enum '%s' has no case '%s'" name s)
            | None -> Error(sprintf "unknown enum '%s'" name)
        | TUnion(name, args), JObj fs ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | None -> Error(sprintf "unknown union '%s'" name)
            | Some u when List.length u.Params <> List.length args ->
                Error(
                    sprintf "union '%s' given %d type args, expects %d" name (List.length args) (List.length u.Params)
                )
            | Some u ->
                let subst = Map.ofList (List.zip u.Params args)

                dollarType idl fs
                |> Result.bind (fun tag ->
                    match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                    | None -> Error(sprintf "union '%s' has no case '%s'" name tag)
                    | Some c ->
                        let caseFields =
                            c.Fields
                            |> List.map (fun f ->
                                { f with
                                    Type = substitute subst f.Type })

                        decodeFields idl caseFields fs |> Result.map (fun fields -> VUnion(tag, fields)))
        // A transparent union decoded from a BARE (non-object) wire value — the
        // Fuaran-UI 0.2.0 bare-string `TextSource.Literal` (`"x"` → `Literal{text="x"}`).
        | TUnion(name, args), j when
            (match j with
             | JObj _ -> false
             | _ -> true)
            ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | None -> Error(sprintf "unknown union '%s'" name)
            | Some u ->
                match TransparentUnion.tag idl.Harden u with
                | None -> Error(sprintf "union '%s' expects an object" name)
                | Some ttag ->
                    let subst = Map.ofList (List.zip u.Params args)

                    match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                    | None -> Error(sprintf "union '%s' has no transparent case '%s'" name ttag)
                    | Some c ->
                        match
                            c.Fields
                            |> List.map (fun f ->
                                { f with
                                    Type = substitute subst f.Type })
                        with
                        | [ single ] ->
                            decodeValue idl single.Type j
                            |> Result.map (fun v -> VUnion(ttag, [ single.Name, v ]))
                        | _ -> Error(sprintf "transparent union case '%s' must have exactly one field" ttag)
        | TVar v, _ -> Error(sprintf "unsubstituted type variable '%s'" v)
        | TClosure, JStr "<closure>"
        | TFn _, JStr "<closure>" -> Ok VClosure
        | TOpaque, JStr "<opaque>" -> Ok VOpaque
        // Phase 676 — accept any JSON at this position, verbatim and unvalidated.
        // A shape check here would be wrong by definition: the field's whole
        // contract is that its content is not the schema's business.
        | TJson, j -> Ok(VJson j)
        // A hosted slot decodes verbatim in the interpreter — only the generated
        // F# runs the real host codec (see [[HostedCodec]]).
        | THosted _, j -> Ok(VJson j)
        | TRecord name, JObj fs ->
            match idl.Records |> List.tryFind (fun r -> r.Name = name) with
            | None -> Error(sprintf "unknown record '%s'" name)
            | Some r -> decodeFields idl r.Fields fs |> Result.map VRecord
        | TMap vt, JObj fs ->
            let rec go acc =
                function
                | [] -> Ok(VMap(List.rev acc))
                | (k, jv) :: rest ->
                    match decodeValue idl vt jv with
                    | Ok v -> go ((k, v) :: acc) rest
                    | Error m -> Error m

            go [] fs
        | TNode, JObj _ -> decodeNode idl j
        | TKind, JObj fs ->
            dollarType idl fs
            |> Result.bind (fun tag ->
                match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
                | None -> Error(sprintf "unknown kind '%s'" tag)
                | Some k -> decodeFields idl k.Fields fs |> Result.map (fun flds -> VUnion(tag, flds)))
        | TOp, JObj fs ->
            dollarType idl fs
            |> Result.bind (fun tag ->
                match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
                | None -> Error(sprintf "unknown op '%s'" tag)
                | Some o -> decodeFields idl o.Fields fs |> Result.map (fun flds -> VUnion(tag, flds)))
        | TList inner, JArr xs ->
            let rec go acc =
                function
                | [] -> Ok(VList(List.rev acc))
                | x :: rest ->
                    match decodeValue idl inner x with
                    | Ok v -> go (v :: acc) rest
                    | Error m -> Error m

            go [] xs
        | _ -> Error(sprintf "wire value does not match IDL type %A" t)

    and private decodeFields
        (idl: Idl)
        (fields: IdlField list)
        (jfields: (string * JVal) list)
        : Result<(string * IdlValue) list, string> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | (f: IdlField) :: rest ->
                match field f.Name jfields, f.Opt with
                | _, HostOnly -> go acc rest
                | None, Optional -> go acc rest
                // omit-at-default: an absent field restores its identity default
                | None, OmitDefault d -> go ((f.Name, d) :: acc) rest
                | None, Required -> Error(sprintf "required field '%s' is absent" f.Name)
                | Some j, _ ->
                    match decodeValue idl f.Type j with
                    | Ok v -> go ((f.Name, v) :: acc) rest
                    | Error m -> Error m

        go [] fields

    and private decodeNode (idl: Idl) (j: JVal) : Result<IdlValue, string> =
        match j, idl.Wire.NodeEnvelope with
        | JObj fs, NodeEnvelopeShape.NestedKind ->
            match field "id" fs, field "kind" fs with
            | Some(JStr id), Some(JObj kindFs) ->
                dollarType idl kindFs
                |> Result.bind (fun kindTag ->
                    match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
                    | None -> Error(sprintf "unknown kind '%s'" kindTag)
                    | Some k ->
                        decodeFields idl k.Fields kindFs
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
            | _ -> Error "node must have a string 'id' and an object 'kind'"
        // Phase 109 — the FLAT envelope: the tag, the id, the kind's fields (and
        // any declared node envelope) share this one object. `decodeFields` reads
        // only declared names, so the discriminator and the id are tolerated as
        // the extra keys they are.
        | JObj fs, NodeEnvelopeShape.FlatKind ->
            match field "id" fs, dollarType idl fs with
            | Some(JStr id), Ok kindTag ->
                match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
                | None -> Error(sprintf "unknown kind '%s'" kindTag)
                | Some k ->
                    decodeFields idl k.Fields fs
                    |> Result.bind (fun fields ->
                        decodeFields idl idl.NodeFields fs
                        |> Result.map (function
                            | [] -> VNode(id, kindTag, fields)
                            | envelope -> VNodeEnv(id, envelope, kindTag, fields)))
            | _ -> Error(sprintf "node must have a string 'id' and a string '%s' discriminator" idl.Wire.Discriminator)
        | _, _ -> Error "node must be an object"

    /// Decode canonical wire JSON to an authored `IdlValue`, driven by the IDL.
    let decode (idl: Idl) (json: string) : Result<IdlValue, string> =
        match Json.parse json with
        | Error m -> Error("parse failed: " + m)
        | Ok j -> decodeNode idl j

    /// Decode canonical wire JSON as a TREE OP (Phase 703) — the symmetric partner
    /// of [[Encode.encodeOp]], and the wire's second root.
    let decodeOp (idl: Idl) (json: string) : Result<IdlValue, string> =
        match Json.parse json with
        | Error m -> Error("parse failed: " + m)
        | Ok j -> decodeValue idl TOp j
