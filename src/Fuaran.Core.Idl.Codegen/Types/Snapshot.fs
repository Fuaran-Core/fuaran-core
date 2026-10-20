namespace Fuaran.Core.Idl.Codegen.Types

open Fuaran.Core

// Phase 524 (DECISIONS.md D125, amended) — the stability classifier's snapshot: one `idl.json` revision read for comparison, every field TYPED (its wire type, its
// host surface and its presence) where it used to be canonical strings three rules re-parsed, lifted out of the
// `Diff` module so the classifier splits one concern per file (`Diff/*.fs`).

/// A field's WIRE type: the type object with every `hostSurface` key removed, at any depth, and
/// every object's members in ordinal order — so two revisions that wrote the same type agree
/// structurally whatever order their emitters chose. A difference here is observable by a
/// third-party codec. Phase 524 — a typed value, read once: the rules that need its structure
/// (the int-to-float widening, the erased-slot walk) read `Shape` and never re-parse a string.
type WireType =
    {
        /// The type object's own `$type` — `str` / `list` / `hosted` / `json` …, or `?` when the
        /// object carries none. Three of them (`hosted`, `json`, `opaque`) are ERASED slots whose
        /// admitted values the artifact deliberately does not state, a classification boundary.
        Tag: string
        /// The type object itself, `hostSurface`-free and member-ordered.
        Shape: JVal
    }

    /// The canonical text of [[Shape]], for a report line.
    member this.Text = Canonical.write Rendering.CrossHost this.Shape

/// A field's HOST SURFACE: every `hostSurface` block of its type, keyed by the path it sits at
/// (`""` for the top level, `/of` inside a list, …), in the order the type declares them — so a
/// change to a nested closure's F# signature is still attributable. Empty for every type that
/// carries none. A change here is a generated-declaration change, invisible on the wire.
type HostSurface =
    {
        /// `(path, block)` pairs, each block member-ordered.
        Blocks: (string * JVal) list
    }

    /// `path=<canonical JSON>` entries joined by `"; "` — empty when there are none. Report text.
    member this.Text =
        this.Blocks
        |> List.map (fun (p, b) -> p + "=" + Canonical.write Rendering.CrossHost b)
        |> String.concat "; "

/// A field's PRESENCE as the artifact declares it: the optionality class, carrying an
/// `omitDefault`'s value, which is wire-visible — moving the identity default moves the bytes of
/// every document that was sitting on it, so two `OmitDefault`s compare unequal when their
/// values do.
[<RequireQualifiedAccess>]
type PresenceSnap =
    /// Always on the wire.
    | Required
    /// Omitted when absent.
    | Optional
    /// Omitted when equal to the default; the default's member-ordered value, `None` when the
    /// artifact gives none.
    | OmitDefault of value: JVal option
    /// Never on the wire (`WIRE_FORMAT.md` §9).
    | HostOnly
    /// A class this reader does not know, by its `$type` — `?` when the optionality object
    /// carries none. Compared and printed, never graded as one of the four.
    | Unrecognised of tag: string

    /// The optionality's bare `$type` (`required` / `optional` / `omitDefault` / `hostOnly`, or
    /// the unrecognised tag).
    member this.Class =
        match this with
        | Required -> "required"
        | Optional -> "optional"
        | OmitDefault _ -> "omitDefault"
        | HostOnly -> "hostOnly"
        | Unrecognised t -> t

    /// The class with an `omitDefault`'s canonical default (`omitDefault ""`), for a report line.
    member this.Label =
        match this with
        | OmitDefault(Some d) -> "omitDefault " + Canonical.write Rendering.CrossHost d
        | other -> other.Class

/// One field of a kind / op / union case / record / the node envelope, as one revision declares it.
///
/// Three typed readings rather than one, because they answer three different questions and
/// conflating them is how a host-surface edit gets reported as a wire break: [[Wire]] (what a
/// third-party codec observes), [[Host]] (what the generated declaration looks like) and
/// [[Presence]] (when the member is on the wire). Phase 524 typed them; they were three canonical
/// strings that three rules re-parsed.
type FieldSnap =
    {
        /// The field's wire name — the key fields are paired on across revisions.
        Name: string
        /// A human-readable rendering of the type, for report lines only; no verdict is ever
        /// decided on it.
        Label: string
        /// The structural wire type — a difference here is a wire-type change.
        Wire: WireType
        /// The host-surface blocks alone.
        Host: HostSurface
        /// The declared presence, an `omitDefault`'s value included.
        Presence: PresenceSnap
        /// The declared annotation set (Phase 113), canonically rendered — `""` when the field
        /// declares none, which is what every revision predating the key reads as.
        ///
        /// Apart from the three readings above for the reason they are three: an annotation is
        /// neither wire nor host surface. It changes no encoding and no generated SIGNATURE —
        /// it adds a doc comment and an `Obsolete` attribute — so folding it into [[Host]] would
        /// report a retirement marking as a signature move.
        Annotations: string
        /// The member's retired spellings (Phase 539), in priority order; `[]` when the artifact
        /// declares none, which is also how a revision predating the key reads.
        Aliases: string list
        /// The member's presence condition (Phase 539) as canonical JSON, `""` when it declares none.
        /// Named apart from [[Presence]], the declared optionality.
        PresenceCondition: string
    }

/// What a field belongs to. `ONodeEnvelope` is the per-node field set beside
/// `id` / `kind` (`WIRE_FORMAT.md` §3.1) — it has no name of its own.
type Owner =
    /// A node kind, by its `$type` tag.
    | OKind of tag: string
    /// A tree-op, by its `$type` tag.
    | OOp of tag: string
    /// One case of a value-union; the field belongs to the case, not to the union.
    | OUnionCase of union: string * case: string
    /// A non-discriminated record, by name.
    | ORecord of name: string
    /// The per-node field set every kind shares.
    | ONodeEnvelope

    /// The owner as report prose — `kind X`, `op X`, `union U.C`, `record R` or
    /// `node envelope` — the subject every change summary and rationale names.
    member this.Describe =
        match this with
        | OKind t -> sprintf "kind %s" t
        | OOp t -> sprintf "op %s" t
        | OUnionCase(u, c) -> sprintf "union %s.%s" u c
        | ORecord n -> sprintf "record %s" n
        | ONodeEnvelope -> "node envelope"

    /// Sort key — stable across runs, and groups an owner's changes together.
    member this.Key =
        match this with
        | ONodeEnvelope -> "0:"
        | OKind t -> "1:" + t
        | OOp t -> "2:" + t
        | OUnionCase(u, c) -> "3:" + u + "." + c
        | ORecord n -> "4:" + n

/// One union case — its fields, and (Phase 113) its own annotation set, which
/// belongs to the CASE rather than to any of its fields.
type CaseSnap =
    {
        /// The case's fields in authored order; diffed by name under `OUnionCase`.
        Fields: FieldSnap list
        /// The case's own canonical annotation set — `""` when it declares none or the
        /// revision predates the key.
        Annotations: string
        /// The case's canonical case-level host codec block (Phase 550) — `""` when it declares
        /// none or the revision predates the key.
        Hosted: string
    }

/// One value-union of a revision: its type parameters, its cases and the case it
/// encodes bare, if any.
type UnionSnap =
    {
        /// The declared type-parameter names in order. A move is host-surface (the wire
        /// carries no type arguments) but breaks every reference at the old arity.
        Params: string list
        /// Cases keyed by tag, so a reorder alone never reads as a change.
        Cases: Map<string, CaseSnap>
        /// The case tags in artifact order. Read but never diffed, so reordering cases
        /// produces no change row.
        CaseOrder: string list
        /// The case that encodes as a bare value instead of a `$type`-tagged object, or
        /// `None`. Any move is a wire break.
        TransparentCase: string option
        /// The tag-naming rule (Phase 550), `"caseName"` when the revision states none. Any move
        /// re-tags every case on the wire — a wire break.
        TagRule: string
    }

/// One closed string set of a revision.
type EnumSnap =
    {
        /// The admitted wire strings, in artifact order. Cases pair by string, so a
        /// rename reads as a removal plus an addition.
        WireCases: string list
        /// The generated F# case names (`hostCases`, a host-surface key); empty when the
        /// artifact omits the key. Compared as a whole list, order included.
        HostCases: string list
        /// Per-case annotation sets (Phase 119), keyed by the WIRE string the artifact
        /// keys them on and canonically rendered — a case that says nothing is absent
        /// from the map, which is also how a revision predating the key reads.
        CaseAnnotations: Map<string, string>
        /// Per-case aliases (Phase 539), keyed by the WIRE string as [[CaseAnnotations]] is; a case
        /// declaring none is absent from the map.
        CaseAliases: Map<string, string list>
    }

/// One `idl.json` revision, keyed for lookup. Collections the artifact sorts
/// are read into maps (order carries nothing); field lists keep their
/// authored order, which the artifact preserves deliberately.
type Snapshot =
    {
        /// The artifact ENCODING version (the shape of `idl.json` itself, not the
        /// vocabulary); `0` when the key is missing or not an integer.
        Version: int
        /// Kind tag → its fields in authored order.
        Kinds: Map<string, FieldSnap list>
        /// Kind tag → its `category`, `""` when absent. Metadata only — never serialised,
        /// so a move is host-surface.
        KindCategory: Map<string, string>
        /// A kind's OWN annotation set (Phase 119), canonically rendered — `""` when
        /// it declares none. A map beside [[Kinds]] rather than a field on it, for
        /// the reason [[KindCategory]] is one: the diff walk pairs field LISTS by
        /// tag, and a per-tag fact that is not a field belongs beside that walk
        /// rather than inside it.
        KindAnnotations: Map<string, string>
        /// Tree-op tag → its fields in authored order; empty when the revision predates
        /// the `ops` key.
        Ops: Map<string, FieldSnap list>
        /// A tree-op's own annotation set (Phase 119) — the same slot as
        /// [[KindAnnotations]], read from the `ops` collection. Ops are `IdlKind`s,
        /// so they are annotatable on identical terms.
        OpAnnotations: Map<string, string>
        /// Value-unions by name.
        Unions: Map<string, UnionSnap>
        /// Closed string sets by name.
        Enums: Map<string, EnumSnap>
        /// Non-discriminated records by name → their fields in authored order.
        Records: Map<string, FieldSnap list>
        /// Phase 548 — a union's, a record's or an enum's OWN annotation set (Phase 542),
        /// keyed `(family, name)` with `family` one of `"union"` / `"record"` / `"enum"`, and
        /// canonically rendered — `""` when it declares none or the revision predates the
        /// key, so every pre-542 artifact reads as it always did. A map beside the type maps
        /// for the reason [[KindAnnotations]] is one beside [[Kinds]].
        TypeAnnotations: Map<string * string, string>
        /// `(kind, field) -> canonical value` — the smart-constructor defaults
        /// (`IdlDefault`), NOT the wire-visible `omitDefault` optionality.
        Defaults: Map<string * string, string>
        /// The node-envelope fields (`nodeFields`) every kind carries beside `id` / `kind`,
        /// diffed under `ONodeEnvelope`.
        NodeFields: FieldSnap list
        /// Phase 293 — the declared SUPPORT beside the vocabulary (`support.json`), keyed
        /// `doc:<path>` / `splice:<slot>` / `refine:<Union.Tag>` / `projection:<Kind>` /
        /// `prelude`, each value its canonical text. Empty when the snapshot was taken
        /// without a support document, so every pre-existing pair reads exactly as it did;
        /// with one, a projection or refine edit classifies instead of reading `unchanged`.
        Support: Map<string, string>
        /// The declared wire shape (Phases 108/109), as `discriminator/nodeEnvelope/keyOrder`
        /// — `"$type/nestedKind/sorted"` when the artifact predates the key, and each
        /// segment its default when the `wire` block omits it.
        Wire: string
        /// The declared hardening vocabulary (Phase 116), rendered canonically — the
        /// all-empty block when the artifact predates the key, since Phase 180 made
        /// an absent block and an undeclared policy the same claim. So the two read
        /// alike, which is what they mean.
        ///
        /// Carried as ONE string rather than a member-per-field for the reason
        /// [[Wire]] is: the classifier's job here is to say the declaration moved
        /// and why that matters, and the WIRE consequence of the only wire-visible
        /// member is already reported per union as `UnionTransparencyChanged`.
        Harden: string
    }
