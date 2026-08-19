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
    { Name: string
      Type: IdlType
      Opt: Optionality }

/// A node kind — flat `$type`-discriminated on the wire (`Category` is metadata, not serialised).
and IdlKind =
    { Tag: string
      Category: string
      Fields: IdlField list }

and IdlUnionCase = { Tag: string; Fields: IdlField list }

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

/// A "transparent" union case is encoded/decoded as a bare JSON value (its single
/// field's value) rather than a `$type`-tagged object — the Fuaran-UI 0.2.0
/// bare-string canonical `TextSource.Literal` (`{"$type":"Literal","text":"x"}` →
/// `"x"`). Keyed on the well-known union name; the transparent case carries exactly
/// one field. The `Bound` / non-transparent cases stay `$type`-tagged objects.
module internal TransparentUnion =
    /// The transparent case tag for a union, or `None` if the union has none.
    let tag (u: IdlUnion) : string option =
        if u.Name = "TextSource" then Some "Literal" else None

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

                    match TransparentUnion.tag u with
                    | Some ttag when ttag = tag ->
                        // Transparent case (TextSource.Literal): emit the single field's value bare.
                        match caseFields with
                        | [ single ] ->
                            match provided single.Name fields with
                            | Some v -> encodeValue idl single.Type v
                            | (None | Some VAbsent) ->
                                Error(
                                    sprintf "transparent union '%s' case '%s' missing field '%s'" name tag single.Name
                                )
                        | _ -> Error(sprintf "transparent union case '%s' must have exactly one field" tag)
                    | _ -> encodeFields idl caseFields fields |> Result.map (Canon.typed tag)
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

            go [] entries
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
            | Some k -> encodeFields idl k.Fields fields |> Result.map (Canon.typed tag)
        | TOp, VUnion(tag, fields) ->
            match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
            | None -> Error(sprintf "unknown op '%s'" tag)
            | Some o -> encodeFields idl o.Fields fields |> Result.map (Canon.typed tag)
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
            |> Result.map (fun fs -> JObj [ "id", JStr id; "kind", Canon.typed kindTag fs ])

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
                | Ok envFs -> Ok(JObj(("id", JStr id) :: ("kind", Canon.typed kindTag kindFs) :: envFs))

    /// Encode an authored node to canonical wire JSON — byte-identical to the UI host.
    let encode (idl: Idl) (v: IdlValue) : Result<string, string> =
        match v with
        | VNode(id, kindTag, fields) -> encodeNode idl id kindTag fields |> Result.map Canon.render
        | VNodeEnv(id, envelope, kindTag, fields) ->
            encodeNodeEnv idl id envelope kindTag fields |> Result.map Canon.render
        | _ -> Error "top-level authored value must be a node"

    /// Encode an authored TREE OP to canonical wire JSON (Phase 703) — the wire's
    /// second root. Separate from [[encode]] rather than folded into it: the two
    /// roots are distinguishable on the wire (a node carries `id` + `kind`, an op a
    /// top-level `$type`), but which one a caller MEANT is not the codec's guess to
    /// make. The schema states the same thing as `oneOf`.
    let encodeOp (idl: Idl) (v: IdlValue) : Result<string, string> =
        encodeValue idl TOp v |> Result.map Canon.render

/// The symmetric decode leg — the IDL also drives JSON → `IdlValue`, so the codec
/// round-trips (`encode (decode wire) = wire`). Parsing is the shared portable
/// `Fuaran.Core.Json.parse`; the IDL drives the walk. Decoders are key-order and
/// extra-key tolerant by contract (only declared fields are read), so this is the
/// floor the Phase 319 unknown-kind tolerance builds on.
module Decode =

    let private field (name: string) (fields: (string * JVal) list) =
        fields |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

    let private dollarType (fields: (string * JVal) list) =
        match field "$type" fields with
        | Some(JStr t) -> Ok t
        | _ -> Error "missing or non-string $type"

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

                dollarType fs
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
                match TransparentUnion.tag u with
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
            dollarType fs
            |> Result.bind (fun tag ->
                match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
                | None -> Error(sprintf "unknown kind '%s'" tag)
                | Some k -> decodeFields idl k.Fields fs |> Result.map (fun flds -> VUnion(tag, flds)))
        | TOp, JObj fs ->
            dollarType fs
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
        match j with
        | JObj fs ->
            match field "id" fs, field "kind" fs with
            | Some(JStr id), Some(JObj kindFs) ->
                dollarType kindFs
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
        | _ -> Error "node must be an object"

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

/// A code-generation failure on an IDL construct the generator cannot yet emit (GP4: a typed
/// value, not an exception; GP5: each case names the unsupported construct and thereby the set
/// the generator *does* support). Surfaced at *generation* time (build-time blast radius) from
/// `Gen.fsharpModule`, so an unsupported construct is a typed `Error` rather than a `failwith`
/// in the generator or a `failwith` emitted into the generated code.
type CodegenError =
    /// A field default whose (IDL type, value) pair has no emission — only scalars (`TStr`/`TInt`/
    /// `TBool`) and enums (`TEnum`) carry a default expression today; unions / nodes are a later
    /// leg. Names the offending type + value.
    | UnsupportedDefault of ty: IdlType * value: IdlValue
    /// A kind mixing a `Node list` field with other node-bearing fields — `witnessReplaceChildren`
    /// has no unambiguous positional split for it (several single-`Node` fields ARE generated,
    /// re-assigned positionally; no mixed kind exists in the current vocabulary). Names the kind tag.
    | MultiChildFieldKind of kindTag: string

/// The type-generation leg: emit illustrative F# type source from the IDL — the
/// "generate Types.fs" half of the inversion. Spike-grade (a source string, not a
/// compiled artefact); proves the IDL carries enough to project a host's types.
module Gen =

    /// Sequence a list of codegen results, short-circuiting on the first `CodegenError` (order
    /// preserved). The generator assembles source from many per-kind / per-field fragments; this
    /// threads a single typed failure up through the fragment lists without exceptions.
    let private sequenceR (results: Result<'a, CodegenError> list) : Result<'a list, CodegenError> =
        (Ok [], results)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | Ok _, Error e -> Error e
            | Ok xs, Ok x -> Ok(x :: xs))
        |> Result.map List.rev

    let private pascal (s: string) =
        if s.Length = 0 then
            s
        else
            string (System.Char.ToUpperInvariant s.[0]) + s.Substring 1

    /// F# reserved keywords that can collide with an IDL field name used *verbatim* as an
    /// identifier. Spec / record fields are `pascal`-cased (first letter upper — no F# keyword
    /// is upper-case), so they are always safe; **union-case fields are positional bindings used
    /// lower-case as-authored** (`| Value of ``default``: Scalar option * …`), so a keyword-named
    /// one (`default` on `HoleDecl.Value`) must be back-tick escaped in every identifier position
    /// (the field label, the match-pattern binding, the value reference) — but NOT in the wire
    /// *key string*, which stays the raw name. Back-tick quoting a non-keyword is harmless F#, so
    /// over-inclusion is safe; the set is the real reserved words so unaffected names stay bare.
    let private fsKeywords =
        set
            [ "abstract"
              "and"
              "as"
              "assert"
              "base"
              "begin"
              "class"
              "default"
              "delegate"
              "do"
              "done"
              "downcast"
              "downto"
              "elif"
              "else"
              "end"
              "exception"
              "extern"
              "false"
              "finally"
              "fixed"
              "for"
              "fun"
              "function"
              "global"
              "if"
              "in"
              "inherit"
              "inline"
              "interface"
              "internal"
              "lazy"
              "let"
              "match"
              "member"
              "module"
              "mutable"
              "namespace"
              "new"
              "null"
              "of"
              "open"
              "or"
              "override"
              // Reserved-for-future (FS0046 warns on bare use) — hit by
              // `Binding.Transform`'s `params` field; escaping is harmless.
              "params"
              "private"
              "public"
              "rec"
              "return"
              "sig"
              "static"
              "struct"
              "then"
              "to"
              "true"
              "try"
              "type"
              "upcast"
              "use"
              "val"
              "void"
              "when"
              "while"
              "with"
              "yield" ]

    /// A field name in F#-identifier position — back-tick-escaped if it is a reserved keyword.
    let private ident (s: string) : string =
        if fsKeywords.Contains s then "``" + s + "``" else s

    // -----------------------------------------------------------------------
    // Phase 689 — `'Msg` threading.
    //
    // A [[TFn]] slot whose `FSharp` signature mentions `'Msg` makes its owning
    // type generic in `'Msg`, and that propagates: `TabsSpec` carries a handler,
    // so `NodeKind` carries `TabsSpec`, so `Node` carries `NodeKind`. The set is
    // the least fixpoint of "mentions `'Msg` directly, or references something
    // that does". Computing it is what lets the generated declarations BE the
    // authoring types instead of a `'Msg`-erased projection of them.
    //
    // `Binding<'T>` is deliberately NOT in the set on the real UI IDL: the tier
    // obj-erases exactly where a `'Msg` parameter would be inconvenient
    // (`LocalBinding.OnCommit: 'T -> obj`, `Action.Call`'s `onResult: obj -> 'Msg`),
    // so the parameter stays confined to the kinds that genuinely dispatch.
    // -----------------------------------------------------------------------

    /// The type names emitted generic in `'Msg` — union names, record names,
    /// `<Tag>Spec` names, plus `NodeKind` / `Node` when any kind qualifies.
    let msgCarrying (idl: Idl) : Set<string> =
        let rec mentions (seen: Set<string>) (t: IdlType) =
            match t with
            | TFn s -> s.FSharp.Contains "'Msg"
            | TList inner -> mentions seen inner
            | TMap vt -> mentions seen vt
            | TNode -> seen.Contains "Node"
            | TUnion(n, args) -> seen.Contains n || args |> List.exists (mentions seen)
            | TRecord n -> seen.Contains n
            | _ -> false

        let step (seen: Set<string>) =
            let fieldsMention (fs: IdlField list) =
                fs |> List.exists (fun f -> mentions seen f.Type)

            let unions =
                idl.Unions
                |> List.filter (fun u -> u.Cases |> List.exists (fun c -> fieldsMention c.Fields))
                |> List.map _.Name

            let records =
                idl.Records |> List.filter (fun r -> fieldsMention r.Fields) |> List.map _.Name

            let kinds =
                idl.Kinds
                |> List.filter (fun k -> fieldsMention k.Fields)
                |> List.map (fun k -> k.Tag + "Spec")

            // `NodeKind` wraps every spec, and `Node` wraps `NodeKind` — so one
            // dispatching kind makes the whole tree generic. That is the point.
            let tree =
                if
                    idl.Kinds
                    |> List.exists (fun k -> Set.contains (k.Tag + "Spec") (Set.ofList kinds))
                then
                    [ "NodeKind"; "Node" ]
                else
                    []

            Set.unionMany
                [ seen
                  Set.ofList unions
                  Set.ofList records
                  Set.ofList kinds
                  Set.ofList tree ]

        let rec fix (seen: Set<string>) =
            let next = step seen
            if next = seen then seen else fix next

        fix Set.empty

    /// The `<…>` parameter list for a declaration, with `'Msg` appended when the
    /// type is msg-carrying. Declared params come first so an existing
    /// `Binding<'T>` keeps its shape if it ever gains a handler.
    let private declParams (msg: Set<string>) (name: string) (ps: string list) =
        let ps =
            (ps |> List.map (fun p -> "'" + p))
            @ (if msg.Contains name then [ "'Msg" ] else [])

        if List.isEmpty ps then
            ""
        else
            "<" + String.concat ", " ps + ">"

    /// The same suffix with `'Msg` instantiated to `obj` — the DECODER's shape.
    /// A closure cannot be rebuilt from `"<closure>"`, so a decoded tree is the
    /// storage shape (the tier's own `decodeNodeObj` / `WireTree` boundary), and a
    /// host re-attaches typed behaviour above it.
    let private objParams (msg: Set<string>) (name: string) (ps: string list) =
        let ps =
            (ps |> List.map (fun p -> "'" + p))
            @ (if msg.Contains name then [ "obj" ] else [])

        if List.isEmpty ps then
            ""
        else
            "<" + String.concat ", " ps + ">"

    let rec private fsTypeIn (msg: Set<string>) (t: IdlType) =
        let fsType = fsTypeIn msg

        let applied (n: string) (args: string list) =
            let args = args @ (if msg.Contains n then [ "'Msg" ] else [])

            if List.isEmpty args then
                n
            else
                n + "<" + String.concat ", " args + ">"

        match t with
        | TStr -> "string"
        | TInt -> "int"
        | TBool -> "bool"
        | TFloat -> "float"
        | TEnum n -> n
        | TUnion(n, args) -> applied n (args |> List.map fsType)
        | TVar v -> "'" + v
        | TNode -> applied "Node" []
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the F# type emitter is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the F# type emitter does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TList inner -> fsType inner + " list"
        // Closure / opaque fields carry no observable data — the generated structural layer is
        // ENCODER-ONLY and `'Msg`-erased (Phase 317 real-tier boundary): a function-typed field
        // (`Binding.Query`'s accessor, every `onChange` / `onClick`) and an `obj`-erased field
        // (`Sparkline.source`'s seq, `Select.value`) both collapse to `unit`. There is no host
        // behaviour or CLR shape to reconstruct here — the encoder emits the fixed `"<closure>"` /
        // `"<opaque>"` sentinel regardless of the (unit) value, so authoring stays trivial (`()`).
        // The real `Fuaran.UI` `Types.fs` keeps the `'Msg`-generic closures; the switch-over
        // re-attaches behaviour on the domain side (documented in docs/migrations/317-*).
        | TClosure -> "unit"
        | TOpaque -> "unit"
        // Phase 689 — the declared host signature, verbatim. This is the whole
        // difference from `TClosure`, and the reason the generated layer can be
        // the authoring type: the encoder never reads the value, so the slot's
        // host type was always free.
        | TFn s -> "(" + s.FSharp + ")"
        // Phase 676 — a JSON slot is a real `JVal`, NOT erased to `unit`: it carries
        // data in both directions, which is the whole difference from `TOpaque`.
        | TJson -> "JVal"
        // A hosted slot declares the real host type — that is its whole point.
        | THosted h -> h.FSharp
        | TRecord n -> applied n []
        | TMap vt -> "Map<string, " + fsType vt + ">"

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
            /// The full `and private dec<Tag>Spec …` member, verbatim.
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
            /// Verbatim `and private …` member(s) appended to the decoder group.
            DecodeSplice: string option
            /// Verbatim module-level lets emitted after the JVal accessor block.
            AccessorSplice: string option
            /// `"Union.Tag"` → the full final expression replacing `Ok(Case(…))` in that
            /// case's decoder — decode POLICY (e.g. `SetState`'s value-XOR-valueFrom) that
            /// the structural inversion cannot express. Field binder names are in scope.
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

    let private fsField (msg: Set<string>) (f: IdlField) =
        let fsType = fsTypeIn msg

        let ty =
            match f.Opt with
            | Optional -> fsType f.Type + " option"
            // OmitDefault fields always carry a value (the default is restored on
            // absence at decode) — a non-option field, like Required. HostOnly takes
            // the declared type verbatim: its `TFn` signature already says whether it
            // is an option (`Motion option`) or a bare value (`'Msg`).
            | Required
            | HostOnly
            | OmitDefault _ -> fsType f.Type

        sprintf "      %s: %s" (pascal f.Name) ty

    let private enumDecl (e: IdlEnum) =
        sprintf "type %s =\n%s" e.Name (e.Cases |> List.map (sprintf "    | %s") |> String.concat "\n")

    let private unionCaseDecl (msg: Set<string>) (c: IdlUnionCase) =
        let fsType = fsTypeIn msg

        let fieldDecl (f: IdlField) =
            let ty =
                match f.Opt with
                | Optional -> fsType f.Type + " option"
                | Required
                | HostOnly
                | OmitDefault _ -> fsType f.Type

            sprintf "%s: %s" (ident f.Name) ty

        let fields = c.Fields |> List.map fieldDecl |> String.concat " * "

        if fields = "" then
            sprintf "    | %s" c.Tag
        else
            sprintf "    | %s of %s" c.Tag fields

    let private unionDecl (msg: Set<string>) (u: IdlUnion) =
        sprintf
            "type %s%s =\n%s"
            u.Name
            (declParams msg u.Name u.Params)
            (u.Cases |> List.map (unionCaseDecl msg) |> String.concat "\n")

    let private kindDecl (msg: Set<string>) (k: IdlKind) =
        sprintf
            "// %s\ntype %sSpec%s =\n    {\n%s\n    }"
            k.Category
            k.Tag
            (declParams msg (k.Tag + "Spec") [])
            (k.Fields |> List.map (fsField msg) |> String.concat "\n")

    /// Emit F# type declarations (enums, value-unions, per-kind spec records) from the IDL.
    let fsharpTypes (idl: Idl) : string =
        let msg = msgCarrying idl

        [ idl.Enums |> List.map enumDecl
          idl.Unions |> List.map (unionDecl msg)
          idl.Kinds |> List.map (kindDecl msg) ]
        |> List.concat
        |> String.concat "\n\n"

    // -----------------------------------------------------------------------
    // Phase 317 increment 3 — *feature-complete* code emission: emit a
    // self-contained, compiling F# encoder module for a set of kinds, handling
    // every feature class the interpreter does — Required + Optional fields
    // (omit-on-absence via List.choose), parameterised unions (`Binding<'T>`, by
    // codec-passing), lists, and node nesting (a recursive `encNode`). The proof
    // that the generator — not just the interpreter — covers the whole surface.
    // -----------------------------------------------------------------------

    /// Point-free encoder *function* for a type (`'a -> JVal`) — used where an encoder
    /// must be passed (a generic union's type-parameter codec).
    let rec private encFn (t: IdlType) : string =
        match t with
        | TStr -> "JStr"
        | TInt -> "JInt"
        | TBool -> "JBool"
        | TFloat -> "JFloat"
        | TEnum n -> "enc" + n
        | TVar v -> "enc" + v
        | TUnion(n, []) -> "enc" + n
        | TUnion(n, args) -> "(enc" + n + " " + (args |> List.map encFn |> String.concat " ") + ")"
        | TNode -> "encNode"
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the F# encoder emitter is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the F# encoder emitter does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TList inner -> sprintf "(fun __xs -> JArr(List.map %s __xs))" (encFn inner)
        // A closure/opaque codec ignores its argument and emits the fixed sentinel.
        | TClosure
        | TFn _ -> "(fun _ -> JStr \"<closure>\")"
        | TOpaque -> "(fun _ -> JStr \"<opaque>\")"
        // Phase 676 — verbatim passthrough. `Canon.render` already sorts keys Ordinal,
        // escapes per rule 6 and lays floats out per rule 5, so identity inherits all
        // three rather than re-implementing them — the risk this phase named.
        | TJson -> "id"
        // The named host encode expression, verbatim ('host -> JVal). Canonicality is
        // inherited: the host codec builds a JVal that renders through the same Canon.
        | THosted h -> h.Encode
        | TRecord n -> "enc" + n
        | TMap vt -> sprintf "(fun __m -> JObj(Map.toList __m |> List.map (fun (k, v) -> k, %s v)))" (encFn vt)

    /// The applied JVal expression for a value of `t` bound to `var`.
    let private encApplied (var: string) (t: IdlType) : string =
        match t with
        | TList inner -> sprintf "JArr(List.map %s %s)" (encFn inner) var
        | TNode -> sprintf "encNode %s" var
        | TClosure
        | TFn _ -> "JStr \"<closure>\""
        | TOpaque -> "JStr \"<opaque>\""
        | _ -> sprintf "%s %s" (encFn t) var

    /// The host case name behind a `VEnum`'s WIRE string (Phase 707). `VEnum`
    /// carries the wire form like every other `IdlValue` case, so the F# emitter —
    /// alone among the backends — has to map back to the identifier it declared.
    /// Falls through to the wire string when the enum is unknown or declares no
    /// mapping, which is the identity every pre-707 declaration already had.
    let private fsEnumCase (enums: IdlEnum list) (enumName: string) (wire: string) : string =
        enums
        |> List.tryFind (fun e -> e.Name = enumName)
        |> Option.bind _.CaseOf(wire)
        |> Option.defaultValue wire

    /// The F# literal for an omit-when-default field's identity default — enums
    /// (`ToneVariant.Default`) and nullary unions (`CellFormat.None`), the only
    /// default shapes the omit-when-default wire (Phase 147 / 460) uses. `None` ⇒
    /// the emitter can't render it, and the encoder falls back to always-emit.
    let private fsDefaultLit (enums: IdlEnum list) (t: IdlType) (v: IdlValue) : string option =
        match t, v with
        | TEnum n, VEnum c -> Some(n + "." + fsEnumCase enums n c)
        | TUnion(n, _), VUnion(tag, []) -> Some(n + "." + tag)
        | TBool, VBool b -> Some(if b then "true" else "false")
        | _ -> None

    /// One field of a record-spec encoder, as a `(string * JVal) option` for `List.choose id`
    /// (Required → always `Some`; Optional → omit-on-`None`; OmitDefault → omit-at-default).
    /// The F# expression a HostOnly field takes on decode — its `TFn` placeholder.
    /// A host-only field must be a `TFn`, because that is what carries both the
    /// declared host type and the value to restore; anything else is an IDL defect
    /// the generator refuses rather than guesses at.
    let private hostOnlyLit (f: IdlField) : string =
        match f.Type with
        | TFn sg -> sg.Placeholder
        | _ -> failwithf "field '%s' is HostOnly but not a TFn — it declares no host type or placeholder" f.Name

    /// `recv` is the bound record variable — `s` for a spec/record encoder, `n` for
    /// the node envelope (Phase 690), which reuses this presence machinery unchanged.
    let private specPieceOf (enums: IdlEnum list) (recv: string) (f: IdlField) : string =
        let src = recv + "." + pascal f.Name

        match f.Opt with
        | Required -> sprintf "Some(\"%s\", %s)" f.Name (encApplied src f.Type)
        | Optional -> sprintf "(%s |> Option.map (fun v -> \"%s\", %s))" src f.Name (encApplied "v" f.Type)
        // Phase 691 — never on the wire, in any state.
        | HostOnly -> "None"
        | OmitDefault d ->
            match fsDefaultLit enums f.Type d with
            // A UNION default is tested by pattern-match, not `=`. Phase 691: typing a
            // closure slot gives its owning union a function-typed field, and F#
            // functions support no equality, so the union stops supporting the
            // `equality` constraint entirely — `CellFormat.Custom of (obj -> string)`
            // broke `s.Format = CellFormat.None` for every column. A match is also
            // simply the better test: it needs no constraint, and reads as what it is.
            | Some dexpr when
                (match f.Type with
                 | TUnion _ -> true
                 | _ -> false)
                ->
                sprintf "(match %s with | %s -> None | _ -> Some(\"%s\", %s))" src dexpr f.Name (encApplied src f.Type)
            | Some dexpr ->
                sprintf "(if %s = %s then None else Some(\"%s\", %s))" src dexpr f.Name (encApplied src f.Type)
            | None -> sprintf "Some(\"%s\", %s)" f.Name (encApplied src f.Type)

    /// One `"key", <enc>` pair of a *required* union-case field (positional binding; the wire
    /// key is the raw field name, the value reference is keyword-escaped).
    let private casePair (f: IdlField) : string =
        sprintf "\"%s\", %s" f.Name (encApplied (ident f.Name) f.Type)

    /// One `(string * JVal) option` piece of a union-case encoder — `Some` for a required field,
    /// omit-on-`None` for an optional one (`CellFormat.Number`'s `decimals`, `Format.Percent`,
    /// `FormFieldKind.RangedNumber`'s `min`/`max`/`step`, `HoleDecl.Value`'s `default`). Mirrors
    /// [[specPieceOf]] for the `List.choose id` shape, but binds the *positional* case field.
    let private casePiece (enums: IdlEnum list) (f: IdlField) : string =
        let src = ident f.Name

        match f.Opt with
        | Required -> sprintf "Some(\"%s\", %s)" f.Name (encApplied src f.Type)
        | Optional -> sprintf "(%s |> Option.map (fun v -> \"%s\", %s))" src f.Name (encApplied "v" f.Type)
        // Phase 691 — never on the wire, in any state.
        | HostOnly -> "None"
        | OmitDefault d ->
            match fsDefaultLit enums f.Type d with
            // A UNION default is tested by pattern-match, not `=`. Phase 691: typing a
            // closure slot gives its owning union a function-typed field, and F#
            // functions support no equality, so the union stops supporting the
            // `equality` constraint entirely — `CellFormat.Custom of (obj -> string)`
            // broke `s.Format = CellFormat.None` for every column. A match is also
            // simply the better test: it needs no constraint, and reads as what it is.
            | Some dexpr when
                (match f.Type with
                 | TUnion _ -> true
                 | _ -> false)
                ->
                sprintf "(match %s with | %s -> None | _ -> Some(\"%s\", %s))" src dexpr f.Name (encApplied src f.Type)
            | Some dexpr ->
                sprintf "(if %s = %s then None else Some(\"%s\", %s))" src dexpr f.Name (encApplied src f.Type)
            | None -> sprintf "Some(\"%s\", %s)" f.Name (encApplied src f.Type)

    let private enumEncoder (e: IdlEnum) =
        // The case name is the F# identifier, the wire string is what goes on the
        // wire — identical unless the enum declares a mapping (Phase 707).
        let arms =
            e.Cases
            |> List.map (fun c -> sprintf "    | %s.%s -> JStr \"%s\"" e.Name c (e.WireOf c))
            |> String.concat "\n"

        sprintf "let private enc%s (v: %s) : JVal =\n    match v with\n%s" e.Name e.Name arms

    /// A union encoder (an `and`-member of the recursive group). Generic unions take one
    /// `encX : 'X -> JVal` codec per type parameter.
    let private unionEncoder
        (docFn: string -> string -> string)
        (msg: Set<string>)
        (enums: IdlEnum list)
        (u: IdlUnion)
        : string =
        let encArgs =
            u.Params
            |> List.map (fun p -> sprintf " (enc%s: '%s -> JVal)" p p)
            |> String.concat ""

        let tyArgs = declParams msg u.Name u.Params

        let arm (c: IdlUnionCase) =
            let pat =
                match c.Fields with
                | [] -> ""
                | [ f ] -> " " + ident f.Name
                | fs -> " (" + (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ") + ")"

            // A transparent case (TextSource.Literal) emits its single field's value BARE — no
            // `Canon.typed` wrapper — the Fuaran-UI 0.2.0 bare-string canonical literal.
            match TransparentUnion.tag u with
            | Some ttag when ttag = c.Tag ->
                match c.Fields with
                | [ f ] -> sprintf "    | %s.%s%s -> %s" u.Name c.Tag pat (encApplied (ident f.Name) f.Type)
                | _ -> failwithf "transparent union case '%s' must have exactly one field" c.Tag
            | _ ->
                // All-required cases keep the simple literal list (byte-identical to the pre-optional
                // emission); any optional field switches to the `List.choose id` omit-on-absence form.
                if c.Fields |> List.forall (fun f -> f.Opt = Required) then
                    let pairs = c.Fields |> List.map casePair |> String.concat "; "
                    sprintf "    | %s.%s%s -> Canon.typed \"%s\" [ %s ]" u.Name c.Tag pat c.Tag pairs
                else
                    let pieces = c.Fields |> List.map (casePiece enums) |> String.concat "; "

                    sprintf
                        "    | %s.%s%s -> Canon.typed \"%s\" ([ %s ] |> List.choose id)"
                        u.Name
                        c.Tag
                        pat
                        c.Tag
                        pieces

        let arms =
            u.Cases
            |> List.map (fun c -> docFn ("encarm:" + u.Name + "." + c.Tag) "    " + arm c)
            |> String.concat "\n"
        // The explicit `<'T>` type-parameter list (not just `'T` free in the signature) is
        // load-bearing for a generic union: `Binding.Format.source` is a fixed `Binding<float>`
        // *inside* `Binding<'T>`, so the generated `encBinding` recurses at a concrete type ≠ the
        // ambient `'T` — **polymorphic recursion**, which F# permits only under an explicit
        // generic-parameter declaration. Without it, `encBinding` monomorphises to the first use
        // (string) and the `float` recursion fails to type-check.
        sprintf
            "and private enc%s%s%s (v: %s%s) : JVal =\n    match v with\n%s"
            u.Name
            tyArgs
            encArgs
            u.Name
            tyArgs
            arms

    let private specEncoder (msg: Set<string>) (enums: IdlEnum list) (k: IdlKind) : string =
        // Single-line list literal — avoids F# offside-rule pitfalls in generated code.
        let pieces = k.Fields |> List.map (specPieceOf enums "s") |> String.concat "; "

        sprintf
            "and private enc%sSpec%s (s: %sSpec%s) : JVal =\n    Canon.typed \"%s\" ([ %s ] |> List.choose id)"
            k.Tag
            (declParams msg (k.Tag + "Spec") [])
            k.Tag
            (declParams msg (k.Tag + "Spec") [])
            k.Tag
            pieces

    /// A non-discriminated *record* encoder — a plain `JObj` (no `$type`), fields via `List.choose
    /// id` (omit-on-absence for optionals). `Canon.render` Ordinal-sorts keys, so emission order is
    /// irrelevant. Reuses [[specPieceOf]] (`s.<Pascal>` field access). New for the real tier
    /// (`InvokeArg`, `FormField`, `FilterSpec`, `TabHeader`, `ColumnErased`, `ContentHash`, …).
    let private recordEncoder (msg: Set<string>) (enums: IdlEnum list) (r: IdlRecord) : string =
        let pieces = r.Fields |> List.map (specPieceOf enums "s") |> String.concat "; "
        let ps = declParams msg r.Name []
        sprintf "and private enc%s%s (s: %s%s) : JVal =\n    JObj([ %s ] |> List.choose id)" r.Name ps r.Name ps pieces

    // -----------------------------------------------------------------------
    // Phase 672 — the structural DECODER leg.
    //
    // The inverse of the encoder emitters above, and deliberately only the
    // STRUCTURAL half: which `$type` maps to which case, which fields, which
    // types. The decode-side *policy* a schema cannot describe — the canonical
    // diagnostic codes with `$`-rooted paths, §16 lenient-accept normalisation,
    // and the reject set — stays hand-written ABOVE this, exactly as the
    // `'Msg`-generic author facades sit above the generated encoder. So the
    // generated error is a plain string: enough to locate a structural fault,
    // deliberately not competing with the hand-written envelope.
    //
    // Two inversions are NOT symmetric with the encoder, and are the ones to get
    // right:
    //   * §16 sentinel omission — the no-information closure sentinels
    //     (`Binding.Query.accessor`, `Selection.accessor`, `Action.Dispatch.msg`)
    //     are OFF the wire, so a `TClosure`/`TOpaque` field decodes from
    //     *absence* and must never look for its key.
    //   * a whole-valued float renders without a decimal point, so it parses
    //     back as `JInt` — `dFloat` accepts both.
    // -----------------------------------------------------------------------

    /// The decoder expression for a type — a `JVal -> Result<'T, string>`.
    /// Mirrors [[encFn]] arm for arm.
    let rec private decFn (t: IdlType) : string =
        match t with
        | TStr -> "dStr"
        | TInt -> "dInt"
        | TBool -> "dBool"
        | TFloat -> "dFloat"
        | TEnum n -> "dec" + n
        | TVar v -> "dec" + v
        | TUnion(n, []) -> "dec" + n
        | TUnion(n, args) -> "(dec" + n + " " + (args |> List.map decFn |> String.concat " ") + ")"
        | TNode -> "decNode"
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the F# decoder emitter is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the F# decoder emitter does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TList inner -> sprintf "(dList %s)" (decFn inner)
        | TClosure
        | TOpaque -> "dUnit"
        // Phase 689 — a `TFn` slot decodes to its declared placeholder. There is
        // nothing on the wire to rebuild a closure from, so the decoded tree is the
        // storage shape and the placeholder is what a host re-attaches over.
        | TFn s -> sprintf "(fun _ -> Ok (%s))" s.Placeholder
        // Phase 676 — accept any JSON verbatim; a shape check would contradict the
        // field's contract.
        | TJson -> "dJson"
        // The named host decode expression, verbatim (JVal -> Result<'host, string>).
        | THosted h -> h.Decode
        | TRecord n -> "dec" + n
        | TMap vt -> sprintf "(dMap %s)" (decFn vt)

    /// Reading one field back out, honouring the presence rules [[specPieceOf]] /
    /// [[casePiece]] wrote it under.
    let private decField (enums: IdlEnum list) (f: IdlField) : string =
        match f.Type with
        | TClosure
        | TOpaque ->
            // The VALUE is a sentinel and carries nothing, so it is never read. But an
            // OPTIONAL sentinel field's PRESENCE is real wire information — the encoder
            // omits the key when `None` and emits the sentinel when `Some ()`. Reading
            // presence is what makes the decode a structural inverse; a flat `Ok None`
            // silently drops the field (caught by the corpus round-trip gate on
            // `grid-1`'s optional `rowKey`).
            match f.Opt with
            | Optional -> sprintf "dPresent \"%s\" __fs" f.Name
            | _ -> "Ok()"
        // Phase 689 — same presence rule, but the slot is typed, so the value put
        // back is the declared placeholder rather than `()`.
        | TFn s ->
            match f.Opt with
            | Optional ->
                sprintf "(dPresent \"%s\" __fs |> Result.map (Option.map (fun () -> %s)))" f.Name s.Placeholder
            | _ -> sprintf "Ok (%s)" s.Placeholder
        | _ ->
            match f.Opt with
            | Required -> sprintf "dReq \"%s\" __fs %s" f.Name (decFn f.Type)
            | Optional -> sprintf "dOpt \"%s\" __fs %s" f.Name (decFn f.Type)
            // Never on the wire — nothing to read, so take the declared placeholder.
            | HostOnly -> sprintf "Ok (%s)" (hostOnlyLit f)
            | OmitDefault d ->
                match fsDefaultLit enums f.Type d with
                | Some dexpr -> sprintf "dDef \"%s\" __fs %s (%s)" f.Name (decFn f.Type) dexpr
                // The encoder fell back to always-emit, so decode is required too.
                | None -> sprintf "dReq \"%s\" __fs %s" f.Name (decFn f.Type)

    /// Nest one `Result.bind` per field over `final`, then close the lot. F# has
    /// no applicative sugar for this, and the generated file is Fantomas-exempt,
    /// so the nesting is emitted explicitly rather than prettified.
    let private bindChain
        (indent: string)
        (binders: (string * string) list)
        (final: string)
        (extraCloses: int)
        : string =
        let opens =
            binders
            |> List.map (fun (v, e) -> sprintf "%s%s |> Result.bind (fun %s ->" indent e v)

        let closes = String.replicate (List.length binders + extraCloses) ")"
        (opens @ [ indent + final + closes ]) |> String.concat "\n"

    let private fieldBinders (enums: IdlEnum list) (fs: IdlField list) =
        fs |> List.map (fun f -> ident f.Name, decField enums f)

    let private enumDecoder (e: IdlEnum) =
        let arms =
            e.Cases
            |> List.map (fun c -> sprintf "    | JStr \"%s\" -> Ok %s.%s" (e.WireOf c) e.Name c)
            |> String.concat "\n"

        sprintf
            "let private dec%s (j: JVal) : Result<%s, string> =\n    match j with\n%s\n    | _ -> Error \"not a %s\""
            e.Name
            e.Name
            arms
            e.Name

    /// A union decoder. Generic unions take one `decX` codec per type parameter,
    /// with the explicit type-parameter list [[unionEncoder]] needs for the same
    /// polymorphic-recursion reason (`Binding.Format.source` recurses at `float`).
    let private unionDecoder
        (docFn: string -> string -> string)
        (refines: Map<string, string>)
        (msg: Set<string>)
        (enums: IdlEnum list)
        (u: IdlUnion)
        : string =
        let decArgs =
            u.Params
            |> List.map (fun p -> sprintf " (dec%s: JVal -> Result<'%s, string>)" p p)
            |> String.concat ""

        // Declared params stay generic; `'Msg` alone is pinned to `obj`.
        let declArgs =
            if List.isEmpty u.Params then
                ""
            else
                "<" + (u.Params |> List.map (fun p -> "'" + p) |> String.concat ", ") + ">"

        let tyArgs = objParams msg u.Name u.Params

        let ctor (c: IdlUnionCase) =
            match c.Fields with
            | [] -> sprintf "%s.%s" u.Name c.Tag
            | fs -> sprintf "%s.%s(%s)" u.Name c.Tag (fs |> List.map (fun f -> ident f.Name) |> String.concat ", ")

        let arm (c: IdlUnionCase) =
            // Phase 945 — a declared refine replaces the plain `Ok(Case(…))` final with a
            // policy expression (field binder names in scope); the binder chain around it
            // is untouched, so a refine cannot change WHICH fields decode, only what is
            // accepted once they have.
            let final =
                match refines.TryFind(u.Name + "." + c.Tag) with
                | Some r -> r
                | None -> sprintf "Ok(%s)" (ctor c)

            let body =
                if List.isEmpty c.Fields then
                    sprintf "        | \"%s\" -> Ok %s" c.Tag (ctor c)
                else
                    sprintf
                        "        | \"%s\" ->\n%s"
                        c.Tag
                        (bindChain "            " (fieldBinders enums c.Fields) final 0)

            docFn ("decarm:" + u.Name + "." + c.Tag) "        " + body

        // The transparent case (TextSource.Literal) is on the wire BARE, so it is
        // recognised by the ABSENCE of a `$type`, not by a tag.
        let transparent =
            match TransparentUnion.tag u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some c when c.Fields.Length = 1 ->
                    let f = c.Fields.Head

                    Some(
                        sprintf
                            "    | __bare ->\n        %s __bare |> Result.bind (fun %s -> Ok(%s))"
                            (decFn f.Type)
                            (ident f.Name)
                            (ctor c)
                    )
                | _ -> None
            | None -> None

        let tagged =
            let arms = u.Cases |> List.map arm |> String.concat "\n"

            sprintf
                "    | JObj __fs when (__fs |> List.exists (fun (k, _) -> k = \"$type\")) ->\n        dTag __fs |> Result.bind (fun __t ->\n        match __t with\n%s\n        | __other -> Error (\"unknown %s case: \" + __other))"
                arms
                u.Name

        let fallthrough =
            match transparent with
            | Some t -> t
            | None -> sprintf "    | _ -> Error \"expected a %s object\"" u.Name

        sprintf
            "and private dec%s%s%s (j: JVal) : Result<%s%s, string> =\n    match j with\n%s\n%s"
            u.Name
            declArgs
            decArgs
            u.Name
            tyArgs
            tagged
            fallthrough

    let private specDecoder (msg: Set<string>) (enums: IdlEnum list) (k: IdlKind) : string =
        let assigns =
            k.Fields
            |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))
            |> String.concat "; "

        sprintf
            "and private dec%sSpec (j: JVal) : Result<%sSpec%s, string> =\n    dObj j |> Result.bind (fun __fs ->\n%s"
            k.Tag
            k.Tag
            (objParams msg (k.Tag + "Spec") [])
            (bindChain "    " (fieldBinders enums k.Fields) (sprintf "Ok { %s }" assigns) 1)

    let private recordDecoder (msg: Set<string>) (enums: IdlEnum list) (r: IdlRecord) : string =
        let assigns =
            r.Fields
            |> List.map (fun f -> sprintf "%s = %s" (pascal f.Name) (ident f.Name))
            |> String.concat "; "

        sprintf
            "and private dec%s (j: JVal) : Result<%s%s, string> =\n    dObj j |> Result.bind (fun __fs ->\n%s"
            r.Name
            r.Name
            (objParams msg r.Name [])
            (bindChain "    " (fieldBinders enums r.Fields) (sprintf "Ok { %s }" assigns) 1)

    /// The decode-side helper prelude, emitted once per module.
    let private decodeHelpers () : string =
        String.concat
            "\n\n"
            [ "let private dObj (j: JVal) : Result<(string * JVal) list, string> =\n    match j with\n    | JObj fs -> Ok fs\n    | _ -> Error \"expected an object\""
              "let private dTag (fs: (string * JVal) list) : Result<string, string> =\n    match fs |> List.tryFind (fun (k, _) -> k = \"$type\") with\n    | Some(_, JStr t) -> Ok t\n    | _ -> Error \"missing or non-string $type\""
              "let private dStr (j: JVal) : Result<string, string> =\n    match j with\n    | JStr s -> Ok s\n    | _ -> Error \"expected a string\""
              "let private dInt (j: JVal) : Result<int, string> =\n    match j with\n    | JInt i -> Ok i\n    | _ -> Error \"expected an int\""
              "let private dBool (j: JVal) : Result<bool, string> =\n    match j with\n    | JBool b -> Ok b\n    | _ -> Error \"expected a bool\""
              "// A whole-valued float renders without a decimal point, so it parses back as JInt.\nlet private dFloat (j: JVal) : Result<float, string> =\n    match j with\n    | JFloat f -> Ok f\n    | JInt i -> Ok(float i)\n    | _ -> Error \"expected a number\""
              "let private dUnit (_: JVal) : Result<unit, string> = Ok()"
              "// Phase 676 — arbitrary JSON, kept verbatim. No shape check: the field's
// contract is that its content is not the schema's business.
let private dJson (j: JVal) : Result<JVal, string> = Ok j"
              "let private dList (dec: JVal -> Result<'T, string>) (j: JVal) : Result<'T list, string> =\n    match j with\n    | JArr xs ->\n        (Ok [], xs)\n        ||> List.fold (fun acc x ->\n            match acc with\n            | Error e -> Error e\n            | Ok items -> dec x |> Result.map (fun v -> v :: items))\n        |> Result.map List.rev\n    | _ -> Error \"expected an array\""
              "let private dMap (dec: JVal -> Result<'T, string>) (j: JVal) : Result<Map<string, 'T>, string> =\n    match j with\n    | JObj fs ->\n        (Ok [], fs)\n        ||> List.fold (fun acc (k, v) ->\n            match acc with\n            | Error e -> Error e\n            | Ok items -> dec v |> Result.map (fun d -> (k, d) :: items))\n        |> Result.map (List.rev >> Map.ofList)\n    | _ -> Error \"expected an object\""
              "let private dReq (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T, string> =\n    match fs |> List.tryFind (fun (k, _) -> k = name) with\n    | Some(_, v) -> dec v\n    | None -> Error(\"missing required field '\" + name + \"'\")"
              "let private dOpt (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) : Result<'T option, string> =\n    match fs |> List.tryFind (fun (k, _) -> k = name) with\n    | Some(_, v) -> dec v |> Result.map Some\n    | None -> Ok None"
              "let private dDef (name: string) (fs: (string * JVal) list) (dec: JVal -> Result<'T, string>) (dflt: 'T) : Result<'T, string> =\n    match fs |> List.tryFind (fun (k, _) -> k = name) with\n    | Some(_, v) -> dec v\n    | None -> Ok dflt"
              "// An optional closure / opaque field: the value is a sentinel carrying nothing,\n// but its PRESENCE distinguishes `Some ()` from `None` and must be read back.\nlet private dPresent (name: string) (fs: (string * JVal) list) : Result<unit option, string> =\n    Ok(fs |> List.tryFind (fun (k, _) -> k = name) |> Option.map (fun _ -> ()))" ]

    /// Transitive closure of the enum / union / record types referenced from a set of kinds —
    /// through union case fields, record fields, list elements, map value-types, and union
    /// type-args. Records ↔ unions are mutually recursive (`CellKindErased.ButtonGroup` holds a
    /// `ButtonGroupItem` record; `FormField` holds a `FormFieldKind` union), so the walk visits
    /// both. Returns each set filtered to IDL declaration order.
    let private referenced (idl: Idl) (kinds: IdlKind list) : IdlEnum list * IdlUnion list * IdlRecord list =
        let enums = System.Collections.Generic.HashSet<string>()
        let unions = System.Collections.Generic.HashSet<string>()
        let records = System.Collections.Generic.HashSet<string>()

        let rec visit (t: IdlType) =
            match t with
            | TEnum n -> enums.Add n |> ignore
            | TUnion(n, args) ->
                args |> List.iter visit

                if unions.Add n then
                    match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
                    | Some u -> u.Cases |> List.iter (fun c -> c.Fields |> List.iter (fun f -> visit f.Type))
                    | None -> ()
            | TRecord n ->
                if records.Add n then
                    match idl.Records |> List.tryFind (fun r -> r.Name = n) with
                    | Some r -> r.Fields |> List.iter (fun f -> visit f.Type)
                    | None -> ()
            | TList inner -> visit inner
            | TMap vt -> visit vt
            // Phase 691 — a declared type NAMED IN A SIGNATURE is reachable. `TFn.FSharp`
            // is free text the walker cannot parse, so this searches it for the names the
            // IDL already declares. Crude, but self-limiting (it can only ever mark
            // something the IDL defines), and without it a type reached ONLY through a
            // signature — `Motion`, on the host-only node fields — is declared in the IDL
            // and then never emitted, so the generated module names a type it lacks.
            | TFn sg ->
                for e in idl.Enums do
                    if sg.FSharp.Contains e.Name then
                        enums.Add e.Name |> ignore

                for r in idl.Records do
                    if sg.FSharp.Contains r.Name then
                        visit (TRecord r.Name)

                for u in idl.Unions do
                    if sg.FSharp.Contains u.Name then
                        visit (TUnion(u.Name, []))
            // Same name-scan for a hosted slot: its type and codec expressions may
            // reference declared types AND generated codecs (`encRangePair`,
            // `decBinding`) — a type reached only that way must still be emitted.
            | THosted h ->
                let text = h.FSharp + " " + h.Encode + " " + h.Decode

                for e in idl.Enums do
                    if text.Contains e.Name then
                        enums.Add e.Name |> ignore

                for r in idl.Records do
                    if text.Contains r.Name then
                        visit (TRecord r.Name)

                for u in idl.Unions do
                    if text.Contains u.Name then
                        visit (TUnion(u.Name, []))
            | _ -> ()

        kinds |> List.iter (fun k -> k.Fields |> List.iter (fun f -> visit f.Type))
        // Phase 690 — the node envelope is a reachability ROOT too. Its records are
        // reachable from no kind (nothing nests a `SemanticStyle`), so walking only
        // the kinds emits a `Node` whose field types were never declared.
        idl.NodeFields |> List.iter (fun f -> visit f.Type)

        idl.Enums |> List.filter (fun e -> enums.Contains e.Name),
        idl.Unions |> List.filter (fun u -> unions.Contains u.Name),
        idl.Records |> List.filter (fun r -> records.Contains r.Name)

    // -----------------------------------------------------------------------
    // Phase 317 increment 5 — the Core witness-record leg. Emit a
    // `NodeWitness<Node, string>` for the generated `Node`, so the generated
    // structural layer plugs straight into `Fuaran.Core.Tree` / `.Validator` /
    // `.Observer` — the "serves every domain via the Core witness, not just UI"
    // promise. `Children` / `ReplaceChildren` are derived from the IDL: a field
    // is node-bearing iff its type is `TNode` or `TList TNode`.
    // -----------------------------------------------------------------------

    /// `Some (pascalName, isList)` when a field holds a `Node` (single) or a
    /// `Node list`; `None` otherwise.
    let private nodeBearing (f: IdlField) : (string * bool) option =
        match f.Type with
        | TNode -> Some(pascal f.Name, false)
        | TList TNode -> Some(pascal f.Name, true)
        | _ -> None

    /// Emit the `NodeWitness<Node, string>` + its three helper projections. Top-level
    /// `match` functions (not record-literal lambdas) to dodge offside pitfalls in
    /// generated code. `Error` on a kind mixing a `Node list` field with other node-bearing
    /// fields (`ReplaceChildren` not generable — GP4/GP5) rather than emitting a runtime
    /// `failwith` guard; kinds whose node-bearing fields are all single `Node` are generated
    /// with positional re-assignment.
    let private witnessDecl (msg: Set<string>) (kinds: IdlKind list) : Result<string, CodegenError> =
        let nodeArgs = declParams msg "Node" []

        let childBearing =
            kinds
            |> List.filter (fun k -> k.Fields |> List.exists (nodeBearing >> Option.isSome))

        let allBearing = List.length childBearing = List.length kinds

        let kindTagArms =
            kinds
            |> List.map (fun k -> sprintf "    | NodeKind.%s _ -> \"%s\"" k.Tag k.Tag)
            |> String.concat "\n"

        let childArm (k: IdlKind) =
            let exprs =
                k.Fields
                |> List.choose nodeBearing
                |> List.map (fun (name, isList) -> if isList then "s." + name else "[ s." + name + " ]")
                |> String.concat " @ "

            sprintf "    | NodeKind.%s s -> %s" k.Tag exprs

        let replaceArm (k: IdlKind) : Result<string, CodegenError> =
            match k.Fields |> List.choose nodeBearing with
            | [ (name, true) ] ->
                Ok(sprintf "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s = kids } }" k.Tag k.Tag name)
            | [ (name, false) ] ->
                Ok(
                    sprintf
                        "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s = List.head kids } }"
                        k.Tag
                        k.Tag
                        name
                )
            | fields when fields |> List.forall (fun (_, isList) -> not isList) ->
                // Several single-`Node` fields (real tier: `ErrorBoundary` has `child` + `fallback`).
                // `witnessChildren` returns them in field order, so re-assign `kids` positionally.
                let assigns =
                    fields
                    |> List.mapi (fun i (name, _) -> sprintf "%s = List.item %d kids" name i)
                    |> String.concat "; "

                Ok(sprintf "    | NodeKind.%s s -> { n with Kind = NodeKind.%s { s with %s } }" k.Tag k.Tag assigns)
            | _ ->
                // A kind mixing a `Node list` field with other node-bearing fields has no
                // unambiguous positional split; none exists in the vocabulary, so the generator
                // refuses at generation time (GP4) rather than emitting a runtime `failwith`
                // guard into the generated code.
                Error(CodegenError.MultiChildFieldKind k.Tag)

        let childArms =
            (childBearing |> List.map childArm)
            @ (if allBearing then [] else [ "    | _ -> []" ])
            |> String.concat "\n"

        let replaceArms =
            childBearing
            |> List.map replaceArm
            |> sequenceR
            |> Result.map (fun arms -> (arms @ (if allBearing then [] else [ "    | _ -> n" ])) |> String.concat "\n")

        replaceArms
        |> Result.map (fun replaceArmsStr ->
            String.concat
                "\n"
                [ sprintf "let private witnessKindTag (n: Node%s) : string =" nodeArgs
                  "    match n.Kind with"
                  kindTagArms
                  ""
                  sprintf "let private witnessChildren (n: Node%s) : Node%s list =" nodeArgs nodeArgs
                  "    match n.Kind with"
                  childArms
                  ""
                  sprintf
                      "let private witnessReplaceChildren (n: Node%s) (kids: Node%s list) : Node%s ="
                      nodeArgs
                      nodeArgs
                      nodeArgs
                  "    match n.Kind with"
                  replaceArmsStr
                  ""
                  sprintf "let nodeWitness: NodeWitness<Node%s, string> =" nodeArgs
                  "    { Id = fun n -> n.Id"
                  "      KindTag = witnessKindTag"
                  "      Children = witnessChildren"
                  "      ReplaceChildren = witnessReplaceChildren }" ])

    // -----------------------------------------------------------------------
    // Phase 317 increment 6 — the validator-rule-scaffold leg. Emit a
    // `Fuaran.Core.Validator`-ready entry point wired through the generated
    // `nodeWitness`. Rule *content* stays domain-side (that is the whole point
    // of `Core.Validator` — `RuleFamily` packs are domain-supplied); what the
    // generator owns is the scaffold: a `runValidator` that runs any registry
    // over the generated `Node` via the witness. A domain registers its own
    // families and gets build-time verification over generated nodes for free.
    // -----------------------------------------------------------------------

    /// Emit the validator scaffold — independent of the kind set (it wires the
    /// generic `Validator.runAll` to the generated `Node` + `nodeWitness`).
    let private validatorDecl (msg: Set<string>) : string =
        let nodeArgs = declParams msg "Node" []

        String.concat
            "\n"
            [ "// Validator scaffold — register domain RuleFamilies into `reg`; rule content stays domain-side."
              sprintf
                  "let runValidator (reg: Validator.Registry<Node%s, string>) (root: Node%s) : Defect<string> list ="
                  nodeArgs
                  nodeArgs
              "    Validator.runAll nodeWitness reg root" ]

    // -----------------------------------------------------------------------
    // Phase 317 increment 7 — the IDL-declared-defaults leg. Emit a smart
    // constructor per kind: required fields *without* a declared default are
    // parameters; IDL-declared defaults are filled (the Phase 307 ARIA / variant
    // case — a field the author shouldn't have to repeat), and other optionals
    // default to `None`. The authoring ergonomics half of the structural set.
    // -----------------------------------------------------------------------

    /// The F# source expression for a declared default value, in the context of the
    /// field's IDL type (so a `VEnum "Standard"` on a `HeadingVariant` field emits
    /// `HeadingVariant.Standard`). Scalars + enums are supported — the spike's default
    /// classes; richer default values (unions / nodes) are a later leg.
    let private defaultExpr (enums: IdlEnum list) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        match t, v with
        | TStr, VStr s -> Ok("\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")
        | TInt, VInt i -> Ok(string i)
        | TBool, VBool b -> Ok(if b then "true" else "false")
        | TEnum n, VEnum c -> Ok(n + "." + fsEnumCase enums n c)
        | TUnion(n, _), VUnion(tag, []) -> Ok(n + "." + tag)
        | _ -> Error(CodegenError.UnsupportedDefault(t, v))

    /// Emit the smart constructors (`mk<Kind>`) over the generated `Node`. `Error` on a kind whose
    /// IDL-declared default has no code emission (`defaultExpr` — GP4/GP5).
    let private defaultsDecl
        (projections: Map<string, KindProjection>)
        (msg: Set<string>)
        (idl: Idl)
        (kinds: IdlKind list)
        : Result<string, CodegenError> =
        let nodeArgs = declParams msg "Node" []
        let fsType = fsTypeIn msg

        let defaultFor (kindTag: string) (fieldName: string) : IdlValue option =
            idl.Defaults
            |> List.tryPick (fun d ->
                if d.Kind = kindTag && d.Field = fieldName then
                    Some d.Value
                else
                    None)

        let ctor (k: IdlKind) : Result<string, CodegenError> =
            let parms =
                "(id: string)"
                :: (k.Fields
                    |> List.filter (fun f -> f.Opt = Required && (defaultFor k.Tag f.Name).IsNone)
                    |> List.map (fun f -> sprintf "(%s: %s)" (ident f.Name) (fsType f.Type)))
                |> String.concat " "

            let fieldExpr (f: IdlField) : Result<string, CodegenError> =
                match defaultFor k.Tag f.Name, f.Opt with
                | Some v, Required -> defaultExpr idl.Enums f.Type v
                | Some v, Optional -> defaultExpr idl.Enums f.Type v |> Result.map (fun e -> "Some(" + e + ")")
                | None, Required -> Ok(ident f.Name)
                | None, Optional -> Ok "None"
                // HostOnly: not a ctor param either — the field takes its placeholder.
                | _, HostOnly -> Ok(hostOnlyLit f)
                // OmitDefault: not a ctor param — the field takes its identity default.
                | _, OmitDefault d -> defaultExpr idl.Enums f.Type d

            k.Fields
            |> List.map (fun f -> fieldExpr f |> Result.map (fun e -> sprintf "%s = %s" (pascal f.Name) e))
            |> sequenceR
            |> Result.map (fun fieldStrs ->
                let record = String.concat "; " fieldStrs

                // Phase 690 — a smart constructor fills the envelope with its identity
                // value, so the common case stays `mkHeading "h" 2 text`. An envelope
                // field that is neither optional nor defaulted would have to become a
                // parameter; none is, and the generator says so rather than guessing.
                let envelope =
                    idl.NodeFields
                    |> List.map (fun f ->
                        match f.Opt with
                        | Optional -> sprintf "; %s = None" (pascal f.Name)
                        | OmitDefault d ->
                            match fsDefaultLit idl.Enums f.Type d with
                            | Some e -> sprintf "; %s = %s" (pascal f.Name) e
                            | None -> failwithf "node envelope field '%s' has an unrenderable default" f.Name
                        | HostOnly -> sprintf "; %s = %s" (pascal f.Name) (hostOnlyLit f)
                        | Required -> failwithf "node envelope field '%s' is Required — not yet supported" f.Name)
                    |> String.concat ""

                sprintf
                    "let mk%s %s : Node%s =\n    { Id = id; Kind = NodeKind.%s { %s }%s }"
                    k.Tag
                    parms
                    nodeArgs
                    k.Tag
                    record
                    envelope)

        kinds
        |> List.map (fun k ->
            // Phase 945 — a projected kind's ctor is the projection's own (or absent):
            // the generated one would construct the IDL-derived record, which under a
            // projection is not the record that exists.
            match projections.TryFind k.Tag with
            | Some p -> Ok(p.Mk |> Option.toList)
            | None -> ctor k |> Result.map List.singleton)
        |> sequenceR
        |> Result.map List.concat
        |> Result.map (fun ctors ->
            "// Smart constructors — required-without-default fields are parameters; IDL-declared\n// defaults are filled, other optionals default to None."
            + "\n\n"
            + String.concat "\n\n" ctors)

    /// Emit a compiling, self-contained F# encoder module (`moduleName`) for the named kinds,
    /// drawing in the enums/unions they transitively reference. `encodeNode : Node -> string`
    /// returns canonical wire via the shared `Canon.render`. Also emits a `nodeWitness`
    /// (`NodeWitness<Node, string>`) so the generated layer plugs into `Fuaran.Core.Tree`, a
    /// `runValidator` scaffold wiring `Fuaran.Core.Validator` over the generated `Node`, and
    /// `mk<Kind>` smart constructors applying the IDL-declared field defaults. `Error` on a
    /// construct the generator cannot yet emit (`CodegenError` — GP4/GP5), reported at generation
    /// time rather than as a `failwith`.
    let fsharpModuleWith
        (sup: GenSupport)
        (moduleName: string)
        (idl: Idl)
        (kindTags: string list)
        : Result<string, CodegenError> =
        // Phase 945 — declared-doc lookup: a block of comment lines (markers included,
        // "///" or "//" alike) attached to the named declaration path, indented to the
        // emission site. Absent path ⇒ empty string, so an IDL with no docs emits
        // byte-identically to the pre-945 generator.
        let doc (path: string) (indent: string) : string =
            match sup.Docs.TryFind path with
            | Some lines -> (lines |> List.map (fun l -> indent + l) |> String.concat "\n") + "\n"
            | None -> ""

        // The same block as a type-group member's comment slot (no trailing newline —
        // the renderer adds it).
        let docOpt (path: string) : string option =
            sup.Docs.TryFind path |> Option.map (fun lines -> lines |> String.concat "\n")

        let kinds =
            kindTags
            |> List.choose (fun t -> idl.Kinds |> List.tryFind (fun k -> k.Tag = t))

        let enums, unions, records = referenced idl kinds

        // Phase 689 — which declarations are generic in `'Msg`. Empty unless the IDL
        // uses `TFn`, so an IDL that has not adopted it generates exactly as before.
        let msg = msgCarrying idl

        /// `"<'Msg>"` where the tree is msg-carrying, `""` otherwise — the suffix every
        /// emitted `Node` / `NodeKind` annotation needs.
        let nodeArgs = declParams msg "Node" []
        let kindArgs = declParams msg "NodeKind" []

        let rqaEnum (e: IdlEnum) =
            // Phase 945 — declared docs on the enum type and its cases.
            let cases =
                e.Cases
                |> List.map (fun c -> doc ("case:" + e.Name + "." + c) "    " + sprintf "    | %s" c)
                |> String.concat "\n"

            doc ("type:" + e.Name) ""
            + "[<RequireQualifiedAccess>]\n"
            + sprintf "type %s =\n%s" e.Name cases

        // Value-unions, non-discriminated records, per-kind specs, `NodeKind` and `Node` form ONE
        // type-recursion cycle in the real tier — a union can hold a record (`CellKindErased`
        // holds `ButtonGroupItem`) or a `Node` (`FragmentArg.SlotArg`), a record holds unions, a
        // spec holds `Node list`. So all of them are emitted as a single `type … and …` group
        // (enums stay standalone before it — they reference nothing). Unions + `NodeKind` are
        // `[<RequireQualifiedAccess>]` (case-name collisions across `Number` / `Text` / `Static` /
        // `Date` / … demand it); records are plain (their `pascal`-cased fields never collide with
        // a keyword, and construction sites disambiguate by annotation).
        let typeGroup =
            let unionBody (u: IdlUnion) =
                docOpt ("type:" + u.Name),
                true,
                sprintf
                    "%s%s =\n%s"
                    u.Name
                    (declParams msg u.Name u.Params)
                    (u.Cases
                     |> List.map (fun c -> doc ("case:" + u.Name + "." + c.Tag) "    " + unionCaseDecl msg c)
                     |> String.concat "\n")

            // Phase 945 — field docs land above the field line, at field indent.
            let fieldDecls (owner: string) (fields: IdlField list) =
                fields
                |> List.map (fun f -> doc ("field:" + owner + "." + pascal f.Name) "      " + fsField msg f)
                |> String.concat "\n"

            let recordBody (r: IdlRecord) =
                docOpt ("type:" + r.Name),
                false,
                sprintf "%s%s =\n    {\n%s\n    }" r.Name (declParams msg r.Name []) (fieldDecls r.Name r.Fields)

            let specBody (k: IdlKind) =
                let comment =
                    match docOpt ("type:" + k.Tag + "Spec") with
                    | Some d -> Some("// " + k.Category + "\n" + d)
                    | None -> Some("// " + k.Category)

                // Phase 945 — a projected kind's record body is the projection's, verbatim.
                match sup.KindProjections.TryFind k.Tag with
                | Some proj -> comment, false, proj.SpecDecl
                | None ->
                    comment,
                    false,
                    sprintf
                        "%sSpec%s =\n    {\n%s\n    }"
                        k.Tag
                        (declParams msg (k.Tag + "Spec") [])
                        (fieldDecls (k.Tag + "Spec") k.Fields)

            let nodeKindBody =
                None,
                true,
                "NodeKind"
                + declParams msg "NodeKind" []
                + " =\n"
                + (kinds
                   |> List.map (fun k ->
                       sprintf "    | %s of %sSpec%s" k.Tag k.Tag (declParams msg (k.Tag + "Spec") []))
                   |> String.concat "\n")

            // Phase 690 — `id` + `kind` + the declared envelope. An IDL declaring no
            // envelope keeps the original one-liner, so nothing about it changes.
            let nodeBody =
                if List.isEmpty idl.NodeFields then
                    None, false, sprintf "Node%s = { Id: string; Kind: NodeKind%s }" nodeArgs kindArgs
                else
                    let fields =
                        "      Id: string"
                        :: sprintf "      Kind: NodeKind%s" kindArgs
                        :: (idl.NodeFields |> List.map (fsField msg))

                    None, false, sprintf "Node%s =\n    {\n%s\n    }" nodeArgs (String.concat "\n" fields)

            // (comment, requiresQualifiedAccess, keyword-less body). The first member leads with
            // `type` (RQA attribute on its own preceding line); the rest are `and`-joined.
            let members =
                (unions |> List.map unionBody)
                @ (records |> List.map recordBody)
                @ (kinds |> List.map specBody)
                @ [ nodeKindBody; nodeBody ]

            let render i (comment: string option, rqa: bool, body: string) =
                let commentPrefix =
                    match comment with
                    | Some c -> c + "\n"
                    | None -> ""

                let keyword =
                    match i = 0, rqa with
                    | true, true -> "[<RequireQualifiedAccess>]\ntype"
                    | true, false -> "type"
                    | false, true -> "and [<RequireQualifiedAccess>]"
                    | false, false -> "and"

                commentPrefix + keyword + " " + body

            let rendered = members |> List.mapi render |> String.concat "\n\n"

            // Phase 945 — verbatim members appended to the SAME type-recursion group
            // (`and`-joined), so a spliced type may reference generated types freely.
            match sup.TypeSplice with
            | Some t -> rendered + "\n\n" + t
            | None -> rendered

        let encNodeDecl =
            let arms =
                kinds
                |> List.map (fun k -> sprintf "    | NodeKind.%s s -> enc%sSpec s" k.Tag k.Tag)
                |> String.concat "\n"

            // Phase 690 — the envelope rides the same `List.choose id` presence
            // machinery every spec field uses, with `s.` rebound to `n.`, so
            // omit-on-absence / omit-at-default behave identically on a node field
            // and on a kind field. No envelope ⇒ the original two-key literal.
            let body =
                if List.isEmpty idl.NodeFields then
                    "\n    JObj [ \"id\", JStr n.Id; \"kind\", kind ]"
                else
                    let pieces =
                        idl.NodeFields |> List.map (specPieceOf idl.Enums "n") |> String.concat "; "

                    sprintf "\n    JObj([ Some(\"id\", JStr n.Id); Some(\"kind\", kind); %s ] |> List.choose id)" pieces

            // Phase 694 — the kind dispatch is its own function (was inline in
            // encNode) so the JVal accessors below can expose it: a host codec
            // splicing a bare NodeKind (a TreeOp `EditNode.newKind`) reaches the
            // same single encoder the node envelope uses.
            sprintf "let rec private encNodeKind (k: NodeKind%s) : JVal =\n    match k with\n" nodeArgs
            + arms
            + sprintf "\n\nand private encNode (n: Node%s) : JVal =\n    let kind = encNodeKind n.Kind\n" nodeArgs
            + body

        // encNode + every union / record / spec encoder form one mutually-recursive group.
        let recGroup =
            (encNodeDecl :: (unions |> List.map (unionEncoder doc msg idl.Enums))
             @ (records |> List.map (recordEncoder msg idl.Enums))
             @ (kinds
                |> List.map (fun k ->
                    // Phase 945 — a projected kind's encoder is the projection's, verbatim.
                    match sup.KindProjections.TryFind k.Tag with
                    | Some proj -> doc ("enc:" + k.Tag) "" + proj.Encoder
                    | None -> doc ("enc:" + k.Tag) "" + specEncoder msg idl.Enums k))
             @ (sup.EncodeSplice |> Option.toList))
            |> String.concat "\n\n"

        let header =
            sprintf
                "// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 3). Do not edit by hand.\nmodule %s\n\nopen Fuaran.Core"
                moduleName

        // Phase 672: the decoder's mutually-recursive group, mirroring `recGroup`.
        // `decNodeKind` dispatches `$type` to the per-kind spec decoder; `decNode`
        // reads the `{ id, kind }` envelope `encNode` writes.
        let decGroup =
            let decNodeKindDecl =
                let arms =
                    kinds
                    |> List.map (fun k ->
                        sprintf "    | \"%s\" -> dec%sSpec j |> Result.map NodeKind.%s" k.Tag k.Tag k.Tag)
                    |> String.concat "\n"

                sprintf
                    "let rec private decNodeKind (j: JVal) : Result<NodeKind%s, string> =\n"
                    (objParams msg "NodeKind" [])
                + "    dObj j |> Result.bind (fun __fs ->\n"
                + "    dTag __fs |> Result.bind (fun __t ->\n"
                + "    match __t with\n"
                + arms
                + "\n    | __other -> Error (\"unknown node kind: \" + __other)))"

            let decNodeDecl =
                // Phase 690 — the envelope binds through the same `bindChain` /
                // `decField` machinery a spec record uses, so its presence rules are
                // the encoder's inverse by construction rather than by hand.
                let envelopeAssigns =
                    idl.NodeFields
                    |> List.map (fun f -> sprintf "; %s = %s" (pascal f.Name) (ident f.Name))
                    |> String.concat ""

                let final = sprintf "Ok { Id = id; Kind = kind%s }" envelopeAssigns

                let binders =
                    [ "id", "dReq \"id\" __fs dStr"; "kind", "dReq \"kind\" __fs decNodeKind" ]
                    @ fieldBinders idl.Enums idl.NodeFields

                sprintf "and private decNode (j: JVal) : Result<Node%s, string> =\n" (objParams msg "Node" [])
                + "    dObj j |> Result.bind (fun __fs ->\n"
                + bindChain "    " binders final 1

            (decNodeKindDecl
             :: decNodeDecl
             :: (unions |> List.map (unionDecoder doc sup.CaseRefines msg idl.Enums))
             @ (records |> List.map (recordDecoder msg idl.Enums))
             @ (kinds
                |> List.map (fun k ->
                    // Phase 945 — a projected kind's decoder is the projection's, verbatim.
                    match sup.KindProjections.TryFind k.Tag with
                    | Some proj -> doc ("dec:" + k.Tag) "" + proj.Decoder
                    | None -> doc ("dec:" + k.Tag) "" + specDecoder msg idl.Enums k))
             @ (sup.DecodeSplice |> Option.toList))
            |> String.concat "\n\n"

        match witnessDecl msg kinds, defaultsDecl sup.KindProjections msg idl kinds with
        | Ok witness, Ok defaults ->
            [ [ header ]
              enums |> List.map rqaEnum
              [ typeGroup ]
              enums |> List.map (fun e -> doc ("enc:" + e.Name) "" + enumEncoder e)
              [ recGroup ]
              [ sprintf "let encodeNode (n: Node%s) : string = Canon.render (encNode n)" nodeArgs ]
              // Phase 694 — JVal-level accessors for host codecs that splice
              // generated encodings into a larger canonical document (the
              // tier's TreeOp codec re-points at these when the hand-written
              // node encoder is deleted). Node + kind always; the two envelope
              // records only when the vocabulary declares them (the spike
              // vocabulary has neither).
              [ sprintf
                    "/// JVal-level accessors (Phase 694) — for host codecs that splice generated\n/// encodings into a larger canonical document (e.g. a TreeOp codec).\nlet encodeNodeJson (n: Node%s) : JVal = encNode n"
                    nodeArgs
                sprintf "let encodeNodeKindJson (k: NodeKind%s) : JVal = encNodeKind k" nodeArgs ]
              (if records |> List.exists (fun r -> r.Name = "StateBehaviour") then
                   [ sprintf "let encodeStateBehaviourJson (s: StateBehaviour%s) : JVal = encStateBehaviour s" nodeArgs ]
               else
                   [])
              (if records |> List.exists (fun r -> r.Name = "SemanticStyle") then
                   [ "let encodeSemanticStyleJson (s: SemanticStyle) : JVal = encSemanticStyle s" ]
               else
                   [])
              (sup.AccessorSplice |> Option.toList)
              [ decodeHelpers () ]
              enums |> List.map (fun e -> doc ("dec:" + e.Name) "" + enumDecoder e)
              [ decGroup ]
              [ sprintf
                    "/// Structural decode. The policy layer (diagnostics, §16 lenient-accept,\n/// the reject set) composes ABOVE this — see the Phase 672 note in the generator.\nlet decodeNode (s: string) : Result<Node%s, string> =\n    Json.parse s |> Result.bind decNode"
                    (objParams msg "Node" []) ]
              [ witness ]
              [ validatorDecl msg ]
              [ defaults ] ]
            |> List.concat
            |> String.concat "\n\n"
            |> Ok
        | Error e, _
        | _, Error e -> Error e

    /// The pre-945 entry — `fsharpModuleWith` under an empty declared-support record,
    /// emitting byte-identically to the generator before the support channel existed.
    let fsharpModule (moduleName: string) (idl: Idl) (kindTags: string list) : Result<string, CodegenError> =
        fsharpModuleWith GenSupport.Empty moduleName idl kindTags

    // -----------------------------------------------------------------------
    // Phase 317 increment 4 — the `schema.json` leg: emit a Draft 2020-12 JSON
    // Schema describing the canonical wire, from the same IDL. The third of the
    // §11 "triple mirror" (encoder + decoder already IDL-driven), so one IDL now
    // drives all three. (JSON Schema has no type parameters; a generic union's
    // `'T`-typed fields are emitted as permissive `{}` — noted in the findings.)
    // -----------------------------------------------------------------------

    let rec private schemaOf (t: IdlType) : JVal =
        match t with
        | TStr -> JObj [ "type", JStr "string" ]
        | TInt -> JObj [ "type", JStr "integer" ]
        | TBool -> JObj [ "type", JStr "boolean" ]
        | TFloat -> JObj [ "type", JStr "number" ]
        | TEnum n -> JObj [ "$ref", JStr("#/$defs/" + n) ]
        | TUnion(n, _) -> JObj [ "$ref", JStr("#/$defs/" + n) ]
        | TVar _ -> JObj []
        | TNode -> JObj [ "$ref", JStr "#/$defs/Node" ]
        | TKind -> JObj [ "$ref", JStr "#/$defs/NodeKind" ]
        | TOp -> JObj [ "$ref", JStr "#/$defs/TreeOp" ]
        | TList inner -> JObj [ "type", JStr "array"; "items", schemaOf inner ]
        // Closure / opaque fields are sentinel strings on the wire.
        | TClosure
        | TFn _ -> JObj [ "type", JStr "string"; "const", JStr "<closure>" ]
        | TOpaque -> JObj [ "type", JStr "string"; "const", JStr "<opaque>" ]
        // Phase 676 — "any JSON": the schema deliberately does not constrain content
        // the encoder does not decompose, matching how the hand-written schema already
        // renders the rule-12 structured-payload positions. A hosted slot is the same
        // deliberate abstention: its content belongs to the host codec's own spec.
        | TJson
        | THosted _ -> JBool true
        | TRecord n -> JObj [ "$ref", JStr("#/$defs/" + n) ]
        | TMap vt -> JObj [ "type", JStr "object"; "additionalProperties", schemaOf vt ]

    /// The wire-visible fields of a declaration. A [[HostOnly]] field is never on
    /// the wire in any state (Phase 691), so it is not a property the schema
    /// describes — listing it would advertise a key no encoder emits.
    let private wireFields (fields: IdlField list) =
        fields |> List.filter (fun f -> f.Opt <> HostOnly)

    /// The property/required pair shared by every object-shaped schema.
    ///
    /// **`additionalProperties` is deliberately absent** (Phase 697). The decoder
    /// tolerates unknown keys — it looks fields up by name, `WIRE_FORMAT.md` §2.1
    /// rule 2 — and the published `schema.json` matches that tolerance. A generated
    /// schema that set `additionalProperties: false` would reject payloads the
    /// format accepts and the decoder round-trips, which makes it a fourth mirror
    /// disagreeing with the spec rather than a projection of it. Forward
    /// compatibility is the point: an older host validating a newer producer's
    /// output must not fail on a key it has not learned yet.
    let private objectBody (fields: IdlField list) =
        let wire = wireFields fields

        [ "required",
          JArr(
              wire
              |> List.filter (fun f -> f.Opt = Required)
              |> List.map (fun f -> JStr f.Name)
          )
          "properties", JObj(wire |> List.map (fun f -> f.Name, schemaOf f.Type)) ]

    /// An object schema with a `$type` const (for a kind / union case) + its fields.
    let private objectSchema (typeConst: string) (fields: IdlField list) : JVal =
        let wire = wireFields fields

        let props =
            ("$type", JObj [ "const", JStr typeConst ])
            :: (wire |> List.map (fun f -> f.Name, schemaOf f.Type))

        let required =
            "$type"
            :: (wire |> List.filter (fun f -> f.Opt = Required) |> List.map (fun f -> f.Name))

        JObj
            [ "type", JStr "object"
              "required", JArr(required |> List.map JStr)
              "properties", JObj props ]

    /// A NON-discriminated object schema — a [[TRecord]] (`FormField`, `FilterSpec`,
    /// `TabHeader`, `ColumnErased`, …). No `$type` const: that is exactly what
    /// distinguishes a record from a union case on the wire.
    let private recordSchema (r: IdlRecord) : JVal =
        JObj(("type", JStr "object") :: objectBody r.Fields)

    /// Emit a Draft 2020-12 JSON Schema for the whole IDL's canonical wire.
    let jsonSchema (idl: Idl) : string =
        let enumDef (e: IdlEnum) =
            // The `enum` array is a WIRE contract — wire strings, not host case names.
            e.Name, JObj [ "type", JStr "string"; "enum", JArr(e.WireCases |> List.map JStr) ]

        let unionDef (u: IdlUnion) =
            let tagged = u.Cases |> List.map (fun c -> objectSchema c.Tag c.Fields)

            // A transparent case is on the wire BARE — its single field's value with
            // no `$type` envelope (`TextSource.Literal`: `"x"`, not
            // `{"$type":"Literal","text":"x"}`). The codec legs already special-case
            // it; without reflecting it here the schema rejects the CANONICAL form of
            // every literal string in the corpus. The tagged branch stays: §16
            // lenient-accept admits the envelope on input.
            let bare =
                match TransparentUnion.tag u with
                | None -> []
                | Some ttag ->
                    u.Cases
                    |> List.tryFind (fun c -> c.Tag = ttag)
                    |> Option.map (fun c -> c.Fields |> List.map (fun f -> schemaOf f.Type))
                    |> Option.defaultValue []

            u.Name, JObj [ "oneOf", JArr(bare @ tagged) ]

        let recordDef (r: IdlRecord) = r.Name, recordSchema r

        let kindDef (k: IdlKind) = k.Tag, objectSchema k.Tag k.Fields

        let nodeDef =
            "Node",
            JObj
                [ "type", JStr "object"
                  // Phase 690 — the envelope is optional by construction (`state` /
                  // `style` / `accessibility` are omitted when empty), so `required`
                  // stays `id` + `kind` unless an envelope field is declared Required.
                  "required",
                  JArr(
                      JStr "id"
                      :: JStr "kind"
                      :: (idl.NodeFields
                          |> List.filter (fun f -> f.Opt = Required)
                          |> List.map (fun f -> JStr f.Name))
                  )
                  "properties",
                  JObj(
                      [ "id", JObj [ "type", JStr "string" ]
                        "kind", JObj [ "$ref", JStr "#/$defs/NodeKind" ] ]
                      @ (wireFields idl.NodeFields |> List.map (fun f -> f.Name, schemaOf f.Type))
                  ) ]

        // The kind alternation, named (Phase 703). It was inlined into `Node.kind`,
        // which is equivalent for a node but leaves `TKind` — `EditNode.newKind`'s
        // type — with nothing to reference. Naming it also matches the published
        // schema, which has always carried a `NodeKind` definition.
        let nodeKindDef =
            "NodeKind",
            JObj [ "oneOf", JArr(idl.Kinds |> List.map (fun k -> JObj [ "$ref", JStr("#/$defs/" + k.Tag) ])) ]

        let opDef (o: IdlKind) = o.Tag, objectSchema o.Tag o.Fields

        /// The op alternation. Absent when the domain declares no ops, so an
        /// op-free IDL's schema is exactly what it was.
        let treeOpDefs =
            if List.isEmpty idl.Ops then
                []
            else
                (idl.Ops |> List.map opDef)
                @ [ "TreeOp",
                    JObj [ "oneOf", JArr(idl.Ops |> List.map (fun o -> JObj [ "$ref", JStr("#/$defs/" + o.Tag) ])) ] ]

        // The wire has TWO roots once ops are declared (`WIRE_FORMAT.md` §3.4) —
        // a payload is a Node or a TreeOp. They are distinguishable on structure
        // (a node carries `id` + `kind`, an op a top-level `$type`), so `oneOf`
        // states it exactly.
        let root =
            if List.isEmpty idl.Ops then
                "$ref", JStr "#/$defs/Node"
            else
                "oneOf", JArr [ JObj [ "$ref", JStr "#/$defs/Node" ]; JObj [ "$ref", JStr "#/$defs/TreeOp" ] ]

        // Records join the assembly (Phase 697). Every `TRecord` slot emits a
        // `$ref` into `#/$defs/`, so omitting them left a dangling reference for
        // `FormField` / `FilterSpec` / `TabHeader` / `ColumnErased` / … — under a
        // strict validator an unresolvable `$ref` is an error, not a permissive
        // skip, so the leg could never have certified against the corpus.
        let defs =
            (idl.Enums |> List.map enumDef)
            @ (idl.Unions |> List.map unionDef)
            @ (idl.Records |> List.map recordDef)
            @ (idl.Kinds |> List.map kindDef)
            @ [ nodeKindDef; nodeDef ]
            @ treeOpDefs

        JObj
            [ "$schema", JStr "https://json-schema.org/draft/2020-12/schema"
              root
              "$defs", JObj defs ]
        |> Json.render

    // -----------------------------------------------------------------------
    // Phase 317 increment 8 — the SECOND BACKEND (TypeScript). The same IDL now
    // generates an *independent* host's structural encoder (no FSharp.Core /
    // .NET dependency — plain JS string-building). A node run of it over the
    // corpus is byte-identical to the F# generated encoder, establishing the
    // cross-host byte-identity that is the precondition for cross-host
    // attestation (Phase 320). Encode-only, mirroring the F# encoder leg.
    // -----------------------------------------------------------------------

    /// A JS double-quoted SOURCE-string literal (escapes for TS source).
    let private tsSourceStr (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore
        sb.ToString()

    let private invariantFloat (f: float) : string =
        f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)

    /// Emit a TypeScript value literal for an authored `IdlValue` — unions/nodes
    /// become `$type`-tagged objects (matching the generated encoder's dispatch),
    /// fields keyed by name. Builds the conformance fixtures the node harness runs.
    // -----------------------------------------------------------------------
    // Phase 317 — GENERATIVE conformance vectors.
    //
    // The fixed corpus proves the hosts agree on the shapes someone thought to
    // write down. It cannot prove they agree on the shapes nobody did — and
    // independent hosts diverge in exactly two places the corpus under-samples:
    // **string escaping** and **float formatting**. So the pools below are
    // adversarial by construction rather than uniform: quotes, backslashes, a
    // control character, an astral-plane codepoint, and floats that render with
    // no decimal point (so a re-parse sees an integer).
    //
    // Determinism is the whole point of a failing vector, so this uses an
    // explicit LCG rather than `System.Random`, whose sequence is not
    // contractually stable across runtimes — a vector that fails elsewhere has
    // to reproduce here from its seed alone.
    // -----------------------------------------------------------------------

    type private Rng = { mutable State: uint64 }

    let private nextInt (r: Rng) : int =
        r.State <- r.State * 6364136223846793005UL + 1442695040888963407UL
        int ((r.State >>> 33) &&& 0x7FFFFFFFUL)

    let private pick (r: Rng) (xs: 'a list) : 'a = xs.[nextInt r % List.length xs]

    /// Strings chosen to break a hand-rolled escaper: the two characters JSON
    /// must escape, a control character (the \u00xx path), a surrogate pair, and
    /// a payload that would terminate an unescaped script context.
    let private stringPool =
        [ ""
          "plain"
          "quote\" inside"
          "back\\slash"
          "ctrlhere"
          "new\nline"
          "tab\there"
          "accent-é"
          "astral-\U0001F600"
          "</script>" ]

    /// Both Int32 extremes (digit-count boundaries) plus zero.
    let private intPool = [ 0; 1; -1; 42; -7; 2147483647; -2147483648 ]

    /// Whole-valued floats are the hazard; mixed with values that exercise
    /// round-trip ("R") formatting.
    let private floatPool = [ 0.0; 1.0; -1.0; 3.0; 2.5; -0.125; 1234.5; 1e10; 1e-7 ]

    /// Whether sampling a value of `t` **at the depth floor** can still reach a
    /// `TNode` — the sampler's termination predicate (Phase 698).
    ///
    /// It mirrors [[sampleType]]'s own floor behaviour exactly rather than being a
    /// conservative over-approximation: a list/map is EMPTY at the floor and so
    /// reaches nothing, a union prefers its nullary cases, and an optional field is
    /// forced absent there — which leaves bare nodes and required record fields as
    /// the only surviving paths. Mirroring is what keeps the guard from changing a
    /// single draw on a vocabulary that never needed it: `miniIdl`'s only
    /// node-bearing field is `Box.children`, a LIST, so it is floor-safe and its
    /// seeded stream is untouched.
    ///
    /// **Why this is needed at all.** A bare `TNode` was the one recursion site with
    /// no floor arm — `TList` and `TUnion` both have one — so a vocabulary that
    /// reaches a node from a node by any non-list path recursed until the stack went.
    /// The real vocabulary has exactly that: `ErrorBoundary.child`/`.fallback` and
    /// `Switch.default` on the kind side, and `StateBehaviour.onEmpty`/`.onLoading`
    /// reached through the node ENVELOPE. The envelope path is why this surfaced
    /// only when the sampler learned to draw envelopes — with `state` present 2 in 3
    /// and two node slots behind it, the branching factor crosses 1 and the sampled
    /// tree does not terminate.
    let rec private reachesNodeAtFloor (idl: Idl) (seen: Set<string>) (t: IdlType) : bool =
        match t with
        // A kind / op carries whatever its fields carry, and neither has a floor arm
        // of its own; treat both as reaching, which is also true in practice.
        | TNode
        | TKind
        | TOp -> true
        // Empty at the floor, so nothing inside them is ever sampled there.
        | TList _
        | TMap _ -> false
        | TRecord n when not (Set.contains ("r:" + n) seen) ->
            match idl.Records |> List.tryFind (fun rc -> rc.Name = n) with
            | Some rc ->
                rc.Fields
                |> List.exists (fun f -> f.Opt = Required && reachesNodeAtFloor idl (Set.add ("r:" + n) seen) f.Type)
            | None -> false
        | TUnion(n, args) when not (Set.contains ("u:" + n) seen) ->
            let seen' = Set.add ("u:" + n) seen

            args |> List.exists (reachesNodeAtFloor idl seen')
            || (match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
                | Some u ->
                    // The floor prefers a nullary case when the union has one, and a
                    // nullary case has no fields — so only a union WITHOUT one can
                    // still reach a node here.
                    let candidates =
                        match u.Cases |> List.filter (fun c -> List.isEmpty c.Fields) with
                        | [] -> u.Cases
                        | nullary -> nullary

                    candidates
                    |> List.exists (fun c ->
                        c.Fields
                        |> List.exists (fun f -> f.Opt = Required && reachesNodeAtFloor idl seen' f.Type))
                | None -> false)
        | _ -> false

    /// The kind tags a node may take AT THE DEPTH FLOOR: those whose required fields
    /// reach no further node, so the recursion stops there. Falls back to the whole
    /// vocabulary when a domain declares no such kind — the sampler must still
    /// produce a node for a required slot, and a domain with no leaf kind has no
    /// finite node at all, which is its own defect rather than one to hide here.
    let private floorKindTags (idl: Idl) : string list =
        let leaves =
            idl.Kinds
            |> List.filter (fun k ->
                k.Fields
                |> List.forall (fun f -> f.Opt <> Required || not (reachesNodeAtFloor idl Set.empty f.Type)))

        match leaves with
        | [] -> idl.Kinds |> List.map (fun k -> k.Tag)
        | ks -> ks |> List.map (fun k -> k.Tag)

    let rec private sampleType (idl: Idl) (r: Rng) (depth: int) (t: IdlType) : IdlValue =
        match t with
        | TStr -> VStr(pick r stringPool)
        | TInt -> VInt(pick r intPool)
        | TBool -> VBool(nextInt r % 2 = 0)
        | TFloat -> VFloat(pick r floatPool)
        | TClosure
        | TFn _ -> VClosure
        | TOpaque -> VOpaque
        // Phase 676 — sample real JSON, built from the SAME adversarial pools, so the
        // passthrough is stressed on escaping and float layout like every other leg.
        // A hosted slot samples the same way: both the interpreter and the TS backend
        // carry it verbatim, so arbitrary JSON stresses exactly what they share.
        | TJson
        | THosted _ ->
            VJson(
                match nextInt r % 4 with
                | 0 -> JStr(pick r stringPool)
                | 1 -> JFloat(pick r floatPool)
                | 2 -> JArr [ JInt(pick r intPool); JStr(pick r stringPool) ]
                | _ -> JObj [ "z", JInt(pick r intPool); "a", JStr(pick r stringPool) ]
            )
        | TVar _ -> VStr(pick r stringPool)
        | TEnum name ->
            match idl.Enums |> List.tryFind (fun e -> e.Name = name) with
            | Some e -> VEnum(pick r e.WireCases)
            | None -> VStr "?"
        | TList inner ->
            // Bounded, and empty is a legitimate sample — an empty collection is
            // NOT absence, and the two must stay distinguishable on the wire.
            let n = if depth <= 0 then 0 else nextInt r % 3
            VList [ for _ in 1..n -> sampleType idl r (depth - 1) inner ]
        | TMap vt ->
            let n = if depth <= 0 then 0 else nextInt r % 3
            VMap [ for i in 1..n -> (sprintf "k%d" i), sampleType idl r (depth - 1) vt ]
        | TRecord name ->
            match idl.Records |> List.tryFind (fun rc -> rc.Name = name) with
            | Some rc -> VRecord(sampleFields idl r (depth - 1) rc.Fields)
            | None -> VRecord []
        | TUnion(name, args) ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | Some u ->
                // At the depth floor prefer a nullary case when one exists, so a
                // recursive union terminates rather than being truncated.
                let candidates =
                    if depth <= 0 then
                        match u.Cases |> List.filter (fun c -> List.isEmpty c.Fields) with
                        | [] -> u.Cases
                        | nullary -> nullary
                    else
                        u.Cases

                let c = pick r candidates

                // Substitute the type parameter RECURSIVELY. A shallow swap leaves
                // `TList (TVar "T")` alone, so the sampler would generate a string
                // where the slot's codec expects a float — which surfaces as an
                // unrelated "not iterable" inside the TS escaper rather than as a
                // real divergence.
                let rec subst (ft: IdlType) =
                    match ft with
                    | TVar _ ->
                        match args with
                        | a :: _ -> a
                        | [] -> ft
                    | TList inner -> TList(subst inner)
                    | TMap vt -> TMap(subst vt)
                    | TUnion(n, uargs) -> TUnion(n, uargs |> List.map subst)
                    | _ -> ft

                VUnion(
                    c.Tag,
                    sampleFields idl r (depth - 1) (c.Fields |> List.map (fun f -> { f with Type = subst f.Type }))
                )
            | None -> VUnion("?", [])
        | TNode ->
            // At the floor a REQUIRED node cannot be omitted, so the shallowest legal
            // one is produced instead: a kind whose required fields reach no further
            // node. See [[reachesNodeAtFloor]] for why the guard exists.
            let tags =
                if depth <= 0 then
                    floorKindTags idl
                else
                    idl.Kinds |> List.map (fun k -> k.Tag)

            sampleNode idl r (depth - 1) (pick r tags)
        | TKind ->
            let k = pick r idl.Kinds
            VUnion(k.Tag, [ for f in k.Fields -> f.Name, sampleType idl r (depth - 1) f.Type ])
        | TOp when depth <= 0 || List.isEmpty idl.Ops -> VStr "?"
        | TOp ->
            let o = pick r idl.Ops
            VUnion(o.Tag, [ for f in o.Fields -> f.Name, sampleType idl r (depth - 1) f.Type ])

    and private sampleFields (idl: Idl) (r: Rng) (depth: int) (fields: IdlField list) : (string * IdlValue) list =
        fields
        |> List.map (fun f ->
            let v =
                match f.Opt with
                // Host-only fields have no wire projection, so there is nothing to sample.
                | HostOnly -> VAbsent
                | Required -> sampleType idl r depth f.Type
                // At the depth floor a node-reaching OPTIONAL is forced to its absent
                // form — the other half of the termination guard, and the half that
                // stops the node ENVELOPE recursing (`state` → `StateBehaviour` →
                // `onEmpty`/`onLoading`). No RNG is drawn, which is what keeps a
                // floor-safe vocabulary's seeded stream byte-identical.
                | Optional when depth <= 0 && reachesNodeAtFloor idl Set.empty f.Type -> VAbsent
                | OmitDefault d when depth <= 0 && reachesNodeAtFloor idl Set.empty f.Type -> d
                // Sample BOTH sides of every presence rule: an optional that is
                // sometimes absent, and an omit-when-default that sits at its
                // default often enough to exercise the omission path.
                | Optional ->
                    if nextInt r % 3 = 0 then
                        VAbsent
                    else
                        sampleType idl r depth f.Type
                | OmitDefault d ->
                    if nextInt r % 2 = 0 then
                        d
                    else
                        sampleType idl r depth f.Type

            f.Name, v)

    /// Phase 698 — the node ENVELOPE, sampled from [[Idl.NodeFields]] through the
    /// same [[sampleFields]] the kind fields use, so both presence polarities are
    /// drawn on a node field exactly as on a kind field (`Optional` absent 1-in-3,
    /// `OmitDefault` at its default 1-in-2, `HostOnly` never present).
    ///
    /// `VAbsent` entries are dropped so "the envelope is empty" is a shape, not a
    /// list of absences — which is what lets an envelope-free draw stay a plain
    /// [[VNode]] and keeps every seeded stream that predates this byte-identical.
    /// An IDL declaring no envelope draws nothing at all from the RNG.
    ///
    /// **This closes the Phase 690 limitation that stood here.** That note recorded
    /// that a sampled node carried no envelope, so `state` / `style` /
    /// `accessibility` were reachable only by the GENERATED codecs and cross-host
    /// envelope parity was unproven — covered by one corpus fixture rather than by
    /// the generative sweep. It is proven now: `IdlFullVocabularyFuzzTests` compares
    /// the envelope across the interpreter, the generated F# module and the
    /// generated TypeScript module on every vector, and removing it from any one leg
    /// fails the sweep from vector 0.
    and private sampleEnvelope (idl: Idl) (r: Rng) (depth: int) : (string * IdlValue) list =
        sampleFields idl r depth idl.NodeFields
        |> List.filter (fun (_, v) -> v <> VAbsent)

    and private sampleNode (idl: Idl) (r: Rng) (depth: int) (kindTag: string) : IdlValue =
        let id = pick r [ "n"; "node-1"; "a\"b"; "" ]
        let envelope = sampleEnvelope idl r depth

        let fields =
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | Some k -> sampleFields idl r depth k.Fields
            | None -> []

        match envelope with
        | [] -> VNode(id, kindTag, fields)
        | env -> VNodeEnv(id, env, kindTag, fields)

    /// `count` deterministic sample nodes over `kindTags`, cycling the tags so the
    /// vocabulary is covered evenly rather than by chance. Same seed gives the
    /// same vectors on any host and any runtime.
    let sampleNodes (idl: Idl) (kindTags: string list) (seed: int) (count: int) : IdlValue list =
        let r = { State = uint64 seed * 2862933555777941757UL + 3037000493UL }

        [ for i in 0 .. count - 1 -> sampleNode idl r 3 (kindTags.[i % List.length kindTags]) ]

    let rec typescriptValue (v: IdlValue) : string =
        match v with
        | VStr s -> tsSourceStr s
        | VInt i -> string i
        | VBool b -> if b then "true" else "false"
        | VFloat f -> invariantFloat f
        | VEnum s -> tsSourceStr s
        | VUnion(tag, fields) ->
            "{ $type: "
            + tsSourceStr tag
            + (fields
               |> List.map (fun (n, fv) -> ", " + n + ": " + typescriptValue fv)
               |> String.concat "")
            + " }"
        | VList xs -> "[" + (xs |> List.map typescriptValue |> String.concat ", ") + "]"
        | VRecord fields ->
            "{ "
            + (fields
               |> List.map (fun (n, fv) -> n + ": " + typescriptValue fv)
               |> String.concat ", ")
            + " }"
        | VMap entries ->
            "{ "
            + (entries
               |> List.map (fun (k, fv) -> tsSourceStr k + ": " + typescriptValue fv)
               |> String.concat ", ")
            + " }"
        | VNode(id, kindTag, fields) ->
            "{ id: "
            + tsSourceStr id
            + ", kind: { $type: "
            + tsSourceStr kindTag
            + (fields
               |> List.map (fun (n, fv) -> ", " + n + ": " + typescriptValue fv)
               |> String.concat "")
            + " } }"
        // Phase 698 — the envelope sits BESIDE `kind` on the emitted object, which is
        // where the generated `encodeNode` reads it from (`n.style`, `n.state`, …).
        // Nothing else changes: the generated TS node encoder has read the envelope
        // since Phase 690; only the VALUE emitter could not express one.
        | VNodeEnv(id, envelope, kindTag, fields) ->
            "{ id: "
            + tsSourceStr id
            + (envelope
               |> List.map (fun (n, fv) -> ", " + n + ": " + typescriptValue fv)
               |> String.concat "")
            + ", kind: { $type: "
            + tsSourceStr kindTag
            + (fields
               |> List.map (fun (n, fv) -> ", " + n + ": " + typescriptValue fv)
               |> String.concat "")
            + " } }"
        | VAbsent -> "undefined"
        // Closure / opaque values carry no data the TS encoder reads — its codec
        // emits the sentinel regardless, so any placeholder operand serialises right.
        //
        // But it must not be `undefined`, which is how [[VAbsent]] says "this field
        // is NOT on the wire": `tsSpecPieceOf` omits an optional field on exactly that
        // test, so an OPTIONAL closure/opaque slot silently vanished from the TS
        // encoding while F# emitted its sentinel. Latent since the TS backend landed
        // — no fixture and no sampled vector had an optional sentinel field until the
        // Phase 689 spike added `Tabs.onSelect`, at which point the generative
        // conformance test failed at vector 6. A stand-in that is PRESENT keeps the
        // presence test honest without giving the codec anything to read.
        | VClosure
        | VOpaque -> "(() => undefined)"
        // Phase 676 — a JSON value emits as its canonical literal.
        | VJson j -> Canon.render j

    /// The point-free TS encoder reference for a type (used where a codec must be
    /// passed — a generic union's type-parameter codec).
    let rec private tsEncFn (t: IdlType) : string =
        match t with
        | TStr -> "encStr"
        | TInt -> "encInt"
        | TBool -> "encBool"
        | TFloat -> "encFloat"
        | TEnum _ -> "encStr" // an enum value IS its wire string
        | TVar v -> "enc" + v
        | TNode -> "encodeNode"
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the TypeScript encoder backend is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the TypeScript encoder backend does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TUnion(n, []) -> "enc" + n
        | TUnion(n, args) ->
            "((x) => enc"
            + n
            + "("
            + (args |> List.map tsEncFn |> String.concat ", ")
            + ", x))"
        | TList inner -> "((xs) => '[' + xs.map(" + tsEncFn inner + ").join(',') + ']')"
        // A closure/opaque codec ignores its argument and emits the fixed sentinel.
        | TClosure
        | TFn _ -> "(() => '\"<closure>\"')"
        | TOpaque -> "(() => '\"<opaque>\"')"
        // Phase 676 — `encJson` renders the parsed value canonically (see the prelude).
        // A hosted slot is verbatim JSON to the TS backend, like the interpreter.
        | TJson
        | THosted _ -> "encJson"
        | TRecord n -> "enc" + n
        | TMap vt ->
            "((m) => '{' + Object.keys(m).sort().map((k) => encStr(k) + ':' + ("
            + tsEncFn vt
            + ")(m[k])).join(',') + '}')"

    /// The applied TS encode expression for a value of `t` bound to `var`.
    let private tsEncApplied (var: string) (t: IdlType) : string =
        match t with
        | TList inner -> "'[' + " + var + ".map(" + tsEncFn inner + ").join(',') + ']'"
        | TUnion(n, (_ :: _ as args)) ->
            "enc"
            + n
            + "("
            + (args |> List.map tsEncFn |> String.concat ", ")
            + ", "
            + var
            + ")"
        | TNode -> "encodeNode(" + var + ")"
        | _ -> tsEncFn t + "(" + var + ")"

    /// One field of a spec encoder — a `[key, enc]` pair, or `null` (filtered) for
    /// an absent optional.
    /// TS boolean predicate: is `src` at the omit-when-default field's identity default?
    /// Enums render as wire strings (`s.tone === "Default"`); nullary unions as
    /// `{ $type: … }` objects (`s.format.$type === "None"`).
    let private tsIsDefault (src: string) (t: IdlType) (d: IdlValue) : string option =
        match t, d with
        | TEnum _, VEnum c -> Some(src + " === " + tsSourceStr c)
        | TUnion _, VUnion(tag, []) -> Some(src + ".$type === " + tsSourceStr tag)
        | TBool, VBool b -> Some(src + " === " + (if b then "true" else "false"))
        | _ -> None

    /// `recv` is the bound JS variable — `s` for a spec/record encoder, `n` for the
    /// node envelope (Phase 690), mirroring [[specPieceOf]] on the F# side.
    let private tsSpecPieceOf (recv: string) (f: IdlField) : string =
        let src = recv + "." + f.Name
        let pair = "[" + tsSourceStr f.Name + ", " + tsEncApplied src f.Type + "]"

        match f.Opt with
        | Required -> pair
        | Optional -> "(" + src + " === undefined ? null : " + pair + ")"
        | HostOnly -> "null"
        | OmitDefault d ->
            match tsIsDefault src f.Type d with
            | Some pred -> "(" + pred + " ? null : " + pair + ")"
            | None -> pair

    /// A union CASE field, honouring the same presence rules as a spec field.
    /// Phase 317 generative conformance caught this ignoring `f.Opt` entirely:
    /// TS emitted every optional union-case field unconditionally while F# omitted
    /// it, so `LayoutMode.Grid` without `templateColumns` diverged (and threw in
    /// the escaper). No fixed fixture carried that shape.
    let private tsCasePair (f: IdlField) : string =
        let src = "v." + f.Name
        let pair = "[" + tsSourceStr f.Name + ", " + tsEncApplied src f.Type + "]"

        match f.Opt with
        | Required -> pair
        | Optional -> "(" + src + " === undefined ? null : " + pair + ")"
        | HostOnly -> "null"
        | OmitDefault d ->
            match tsIsDefault src f.Type d with
            | Some pred -> "(" + pred + " ? null : " + pair + ")"
            | None -> pair

    let private tsUnionEncoder (u: IdlUnion) : string =
        let argList =
            match u.Params with
            | [] -> "v"
            | ps -> (ps |> List.map (fun p -> "enc" + p) |> String.concat ", ") + ", v"

        let arm (c: IdlUnionCase) =
            match TransparentUnion.tag u with
            | Some ttag when ttag = c.Tag ->
                // Transparent case: return the single field's value bare (no `typed(...)`).
                match c.Fields with
                | [ f ] ->
                    "    case "
                    + tsSourceStr c.Tag
                    + ": return "
                    + tsEncApplied ("v." + f.Name) f.Type
                    + ";"
                | _ -> failwithf "transparent union case '%s' must have exactly one field" c.Tag
            | _ ->
                let pairs = c.Fields |> List.map tsCasePair |> String.concat ", "

                "    case "
                + tsSourceStr c.Tag
                + ": return typed("
                + tsSourceStr c.Tag
                + ", ["
                + pairs
                + "]);"

        "function enc"
        + u.Name
        + "("
        + argList
        + ") {\n  switch (v.$type) {\n"
        + (u.Cases |> List.map arm |> String.concat "\n")
        + "\n  }\n}"

    /// A non-discriminated *record* encoder — a plain object, no `$type`, mirroring
    /// `recordEncoder` on the F# side. Added by Phase 690: the TS backend decoded
    /// records but could not ENCODE them, so `tsEncFn`'s `TRecord n -> "enc" + n`
    /// named a function that was never emitted. Harmless while the only IDL the TS
    /// backend ran on had no records, and a `ReferenceError` waiting for the first
    /// one — the node envelope is three of them.
    let private tsRecordEncoder (r: IdlRecord) : string =
        let pieces = r.Fields |> List.map (tsSpecPieceOf "s") |> String.concat ", "
        "function enc" + r.Name + "(s) {\n  return plain([" + pieces + "]);\n}"

    let private tsSpecEncoder (k: IdlKind) : string =
        let pieces = k.Fields |> List.map (tsSpecPieceOf "s") |> String.concat ", "

        "function enc"
        + k.Tag
        + "Spec(s) {\n  return typed("
        + tsSourceStr k.Tag
        + ", ["
        + pieces
        + "]);\n}"

    // ---- Phase 672 task 4: the TS decoder backend ----
    // The JS host's in-memory shape IS the wire shape (plain objects), so decoding
    // is validation plus rebuilding the positions the encoder writes implicitly:
    // omit-when-default fields (refilled so the encoder omits them again),
    // transparent union cases (re-wrapped so the encoder can re-flatten them), and
    // closure/opaque sentinels (whose PRESENCE is the only information they carry).

    /// The point-free TS decoder reference for a type.
    let rec private tsDecFn (t: IdlType) : string =
        match t with
        | TStr -> "dStr"
        | TInt -> "dInt"
        | TBool -> "dBool"
        | TFloat -> "dFloat"
        | TEnum n -> "dec" + n
        | TVar v -> "dec" + v
        | TNode -> "decNode"
        // Phase 703 models the OP vocabulary and certifies the interpreter leg
        // against the corpus; emitting an op family from the TypeScript decoder backend is a separate,
        // larger piece of work (`TreeOp` is msg-carrying through `TKind`/`TNode`,
        // so it lands as a generic type group). Nothing walks `idl.Ops` in this
        // backend yet, so these arms are unreachable today — explicit and loud so
        // that wiring ops in gets a precise signal instead of a match failure.
        | TKind
        | TOp ->
            failwithf
                "the TypeScript decoder backend does not emit the op vocabulary yet (Phase 703 leaves that leg unshipped): %A"
                t
        | TUnion(n, []) -> "dec" + n
        | TUnion(n, args) ->
            "((x) => dec"
            + n
            + "("
            + (args |> List.map tsDecFn |> String.concat ", ")
            + ", x))"
        | TList inner -> "dList(" + tsDecFn inner + ")"
        // The value carries nothing; only its presence matters (see tsDecField).
        // A `TFn` slot is the same on the wire — the TS tier has no `'Msg` to
        // rebuild into, so it stays `null` there regardless of the declared signature.
        | TClosure
        | TFn _
        | TOpaque -> "(() => null)"
        // Phase 676 — keep the parsed JSON as-is. Hosted slots identically.
        | TJson
        | THosted _ -> "((x) => x)"
        | TRecord n -> "dec" + n
        | TMap vt -> "dMap(" + tsDecFn vt + ")"

    /// The TS literal for an omit-when-default value, in its WIRE representation —
    /// refilled on decode so the encoder's omit test fires again and the bytes match.
    let private tsDefaultLit (t: IdlType) (d: IdlValue) : string option =
        match t, d with
        | TEnum _, VEnum c -> Some(tsSourceStr c)
        | TUnion _, VUnion(tag, []) -> Some("{ $type: " + tsSourceStr tag + " }")
        | TBool, VBool b -> Some(if b then "true" else "false")
        | _ -> None

    let private tsDecField (f: IdlField) : string =
        let key = tsSourceStr f.Name

        match f.Type with
        | TClosure
        | TFn _
        | TOpaque ->
            match f.Opt with
            | Optional -> "dPresent(" + key + ", fs)"
            | _ -> "null"
        | _ ->
            match f.Opt with
            | Required -> "dReq(" + key + ", fs, " + tsDecFn f.Type + ")"
            | Optional -> "dOpt(" + key + ", fs, " + tsDecFn f.Type + ")"
            | HostOnly -> "undefined"
            | OmitDefault d ->
                match tsDefaultLit f.Type d with
                | Some lit -> "dDef(" + key + ", fs, " + tsDecFn f.Type + ", " + lit + ")"
                // `tsSpecPieceOf` fell back to always-emit, so decode is required too.
                | None -> "dReq(" + key + ", fs, " + tsDecFn f.Type + ")"

    let private tsFieldObject (extra: (string * string) list) (fields: IdlField list) : string =
        let pairs =
            (extra |> List.map (fun (k, v) -> k + ": " + v))
            @ (fields |> List.map (fun f -> tsSourceStr f.Name + ": " + tsDecField f))

        "{ " + String.concat ", " pairs + " }"

    let private tsEnumDecoder (e: IdlEnum) =
        // TS holds an enum AS its wire string — there is no second representation
        // on this side, so the decoder's closed set is the wire strings.
        let cases = e.WireCases |> List.map tsSourceStr |> String.concat ", "

        "const dec" + e.Name + " = dEnum(" + tsSourceStr e.Name + ", [" + cases + "]);"

    let private tsUnionDecoder (u: IdlUnion) =
        let argList =
            match u.Params with
            | [] -> "j"
            | ps -> (ps |> List.map (fun p -> "dec" + p) |> String.concat ", ") + ", j"

        let arm (c: IdlUnionCase) =
            "    case "
            + tsSourceStr c.Tag
            + ": return "
            + tsFieldObject [ "$type", tsSourceStr c.Tag ] c.Fields
            + ";"

        let tagged =
            "  if (isTagged(j)) {\n    const fs = j;\n    switch (j.$type) {\n"
            + (u.Cases |> List.map arm |> String.concat "\n")
            + "\n      default: return dFail("
            + tsSourceStr ("unknown " + u.Name + " case: ")
            + " + j.$type);\n    }\n  }"

        // A transparent union also accepts its single-field case bare, and re-wraps
        // it so the encoder can flatten it back to the same bytes.
        let untagged =
            match TransparentUnion.tag u with
            | Some ttag ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
                | Some({ Fields = [ f ] }) ->
                    "  return { $type: "
                    + tsSourceStr ttag
                    + ", "
                    + tsSourceStr f.Name
                    + ": "
                    + tsDecFn f.Type
                    + "(j) };"
                | _ -> failwithf "transparent union case '%s' must have exactly one field" ttag
            | None -> "  return dFail(" + tsSourceStr ("expected a " + u.Name + " object") + ");"

        "function dec"
        + u.Name
        + "("
        + argList
        + ") {\n"
        + tagged
        + "\n"
        + untagged
        + "\n}"

    let private tsRecordDecoder (r: IdlRecord) =
        "function dec"
        + r.Name
        + "(j) {\n  const fs = dObj(j);\n  return "
        + tsFieldObject [] r.Fields
        + ";\n}"

    let private tsSpecDecoder (k: IdlKind) =
        "function dec"
        + k.Tag
        + "Spec(j) {\n  const fs = dObj(j);\n  return "
        + tsFieldObject [ "$type", tsSourceStr k.Tag ] k.Fields
        + ";\n}"

    /// The decode runtime prelude — the JS mirror of the F# `dObj`/`dReq`/… helpers.
    let private tsDecodePrelude =
        """const dFail = (m) => { throw new Error(m); };
const isTagged = (j) => j !== null && typeof j === 'object' && !Array.isArray(j) && '$type' in j;
const dObj = (j) => (j !== null && typeof j === 'object' && !Array.isArray(j)) ? j : dFail('expected an object');
const dStr = (j) => (typeof j === 'string') ? j : dFail('expected a string');
const dInt = (j) => (typeof j === 'number' && Number.isInteger(j)) ? j : dFail('expected an int');
const dFloat = (j) => (typeof j === 'number') ? j : dFail('expected a number');
const dBool = (j) => (typeof j === 'boolean') ? j : dFail('expected a bool');
const dList = (dec) => (j) => Array.isArray(j) ? j.map(dec) : dFail('expected an array');
const dMap = (dec) => (j) => {
  const o = dObj(j);
  const out = {};
  for (const k of Object.keys(o)) out[k] = dec(o[k]);
  return out;
};
const dEnum = (name, cases) => (j) =>
  (typeof j === 'string' && cases.indexOf(j) >= 0) ? j : dFail('not a ' + name);
const dReq = (name, fs, dec) => (name in fs) ? dec(fs[name]) : dFail("missing required field '" + name + "'");
const dOpt = (name, fs, dec) => (name in fs) ? dec(fs[name]) : undefined;
const dDef = (name, fs, dec, dflt) => (name in fs) ? dec(fs[name]) : dflt;
// An optional closure/opaque field: the value is a sentinel carrying nothing, but
// its PRESENCE distinguishes present-from-absent and must survive the round trip.
const dPresent = (name, fs) => (name in fs) ? null : undefined;"""

    /// Emit a self-contained TypeScript (ESM) structural encoder for the named
    /// kinds — `encodeNode(n)` returns canonical wire byte-identical to the F#
    /// generated `encodeNode`. Plain JS string-building: no FSharp.Core, no .NET,
    /// no imports — a genuinely independent host.
    let typescriptModule (idl: Idl) (kindTags: string list) : string =
        let kinds =
            kindTags
            |> List.choose (fun t -> idl.Kinds |> List.tryFind (fun k -> k.Tag = t))

        let _, unions, _ = referenced idl kinds

        // Runtime prelude — escaping mirrors Fuaran.Core.Canon.escape (WIRE_FORMAT §2
        // rule 6: only " and \ and control chars as \u00xx — NO \n/\r/\t shortcuts),
        // and object-field order is author order (Canon.render does not sort keys),
        // so the bytes match the F# host across ALL strings (incl. control chars).
        let prelude =
            """// AUTO-GENERATED from the IDL by Fuaran.Core.Idl.Gen (Phase 317 increment 8 — TS backend). Do not edit by hand.
const encStr = (s) => {
  let out = '"';
  for (const ch of s) {
    const code = ch.codePointAt(0);
    if (ch === '"') out += '\\"';
    else if (ch === '\\') out += '\\\\';
    else if (code < 0x20) out += '\\u' + code.toString(16).padStart(4, '0');
    else out += ch;
  }
  return out + '"';
};
const encInt = (n) => String(n);
const encBool = (b) => (b ? 'true' : 'false');
// §2 rule 5 — floats render in the .NET `ToString("R")` LAYOUT, which is not
// what JS `String(x)` produces: JS uses a lowercase `e`, an unsigned exponent,
// and a wider fixed-point threshold. The shortest-round-trip DIGITS agree (both
// .NET Core 3.0+ and V8 emit them); only the layout differs, so this normalises
// layout without touching the digits.
const formatFiniteDouble = (n) => {
  if (n === 0) return '0';
  const neg = n < 0;
  const s = Math.abs(n).toString();
  let digits;
  let exp;
  const eIdx = s.indexOf('e');
  if (eIdx >= 0) {
    const mant = s.slice(0, eIdx);
    const mantExp = parseInt(s.slice(eIdx + 1), 10);
    const dot = mant.indexOf('.');
    if (dot < 0) {
      digits = mant;
      exp = mantExp + (mant.length - 1);
    } else {
      digits = mant.slice(0, dot) + mant.slice(dot + 1);
      exp = mantExp + (dot - 1);
    }
  } else {
    const dot = s.indexOf('.');
    if (dot < 0) {
      digits = s;
      exp = s.length - 1;
    } else {
      const intPart = s.slice(0, dot);
      const fracPart = s.slice(dot + 1);
      if (intPart === '0') {
        const leadingZeros = fracPart.length - fracPart.replace(/^0+/, '').length;
        digits = fracPart.slice(leadingZeros);
        exp = -(leadingZeros + 1);
      } else {
        digits = intPart + fracPart;
        exp = intPart.length - 1;
      }
    }
  }
  digits = digits.replace(/0+$/, '') || '0';
  let out;
  if (exp >= -4 && exp <= 16) {
    if (exp >= 0) {
      out =
        digits.length <= exp + 1
          ? digits + '0'.repeat(exp + 1 - digits.length)
          : digits.slice(0, exp + 1) + '.' + digits.slice(exp + 1);
    } else {
      out = '0.' + '0'.repeat(-exp - 1) + digits;
    }
  } else {
    const mantissa = digits.length === 1 ? digits : digits[0] + '.' + digits.slice(1);
    out = mantissa + 'E' + (exp >= 0 ? '+' : '-') + Math.abs(exp).toString().padStart(2, '0');
  }
  return neg ? '-' + out : out;
};
const encFloat = (n) => {
  if (Number.isNaN(n)) return '"NaN"';
  if (n === Infinity) return '"Infinity"';
  if (n === -Infinity) return '"-Infinity"';
  return formatFiniteDouble(n);
};
// Phase 676 — arbitrary JSON rendered CANONICALLY: keys Ordinal-sorted, strings
// through the same escaper, numbers through the same float layout. Reusing those
// three is what stops a passthrough drifting from the rest of the wire.
const encJson = (v) => {
  if (v === null || v === undefined) return 'null';
  if (typeof v === 'string') return encStr(v);
  if (typeof v === 'boolean') return encBool(v);
  if (typeof v === 'number') return Number.isInteger(v) ? encInt(v) : encFloat(v);
  if (Array.isArray(v)) return '[' + v.map(encJson).join(',') + ']';
  const keys = Object.keys(v).sort();
  return '{' + keys.map((k) => encStr(k) + ':' + encJson(v[k])).join(',') + '}';
};
// Phase 698 — the Ordinal key sort is done HERE, at emission, not left to the
// caller's declaration order. `Canon.render` sorts every object's keys Ordinal on
// the F# side unconditionally, and JS `<` on strings compares UTF-16 code units,
// which is the same order — so sorting here is what makes the two hosts agree by
// construction. It previously relied on "pairs arrive Ordinal by convention", and
// the convention did not hold: the full-vocabulary sweep's very first vector
// diverged on `TextSource.I18n`, declared `key` then `args` and therefore emitted
// in that order against F#'s `args` then `key`. Every union case, spec and record
// whose fields are not already declared alphabetically had the same defect; no
// fixed fixture and no 8-kind sampled vector had ever contained one.
const ordinal = (a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0);
const typed = (tag, pairs) =>
  '{"$type":' + encStr(tag) + pairs.filter((p) => p !== null).sort(ordinal).map(([k, v]) => ',' + encStr(k) + ':' + v).join('') + '}';
// `typed` without the discriminator: a plain object (a non-discriminated record, or
// the node envelope).
const plain = (pairs) =>
  '{' + pairs.filter((p) => p !== null).sort(ordinal).map(([k, v]) => encStr(k) + ':' + v).join(',') + '}';"""

        let kindDispatch =
            let arms =
                kinds
                |> List.map (fun k -> "    case " + tsSourceStr k.Tag + ": return enc" + k.Tag + "Spec(k);")
                |> String.concat "\n"

            // Phase 690 — `id` / `kind` / the envelope, merged and sorted Ordinal so
            // the TS emission order matches F#'s canonical key sort. With no envelope
            // this is `id` then `kind`, i.e. exactly the previous hand-built literal.
            let nodePairs =
                ("id", "[\"id\", encStr(n.id)]")
                :: ("kind", "[\"kind\", encKind(n.kind)]")
                :: (idl.NodeFields |> List.map (fun f -> f.Name, tsSpecPieceOf "n" f))
                |> List.sortWith (fun (a, _) (b, _) -> System.String.CompareOrdinal(a, b))
                |> List.map snd
                |> String.concat ", "

            "function encKind(k) {\n  switch (k.$type) {\n"
            + arms
            + "\n  }\n}\n\nfunction encodeNode(n) {\n  return plain(["
            + nodePairs
            + "]);\n}"

        let enums, _, records = referenced idl kinds

        let kindDecodeDispatch =
            let arms =
                kinds
                |> List.map (fun k -> "    case " + tsSourceStr k.Tag + ": return dec" + k.Tag + "Spec(j);")
                |> String.concat "\n"

            "function decKind(j) {\n  if (!isTagged(j)) return dFail('expected a kind object');\n  switch (j.$type) {\n"
            + arms
            + "\n    default: return dFail('unknown node kind: ' + j.$type);\n  }\n}\n\n"
            + "function decNode(j) {\n  const fs = dObj(j);\n  return { id: dReq('id', fs, dStr), kind: dReq('kind', fs, decKind)"
            + (idl.NodeFields
               |> List.map (fun f -> ", " + f.Name + ": " + tsDecField f)
               |> String.concat "")
            + " };\n}\n\n"
            + "// Structural decode. The policy layer (diagnostics, §16 lenient-accept, the\n"
            + "// reject set) composes ABOVE this — see the Phase 672 note in the generator.\n"
            + "function decodeNode(s) {\n  try {\n    return { ok: true, value: decNode(JSON.parse(s)) };\n  } catch (e) {\n    return { ok: false, error: String(e && e.message ? e.message : e) };\n  }\n}"

        [ [ prelude ]
          records |> List.map tsRecordEncoder
          unions |> List.map tsUnionEncoder
          kinds |> List.map tsSpecEncoder
          [ kindDispatch ]
          [ tsDecodePrelude ]
          enums |> List.map tsEnumDecoder
          records |> List.map tsRecordDecoder
          unions |> List.map tsUnionDecoder
          kinds |> List.map tsSpecDecoder
          [ kindDecodeDispatch ]
          [ "export { encodeNode, decodeNode };" ] ]
        |> List.concat
        |> String.concat "\n\n"

    // -----------------------------------------------------------------------
    // Phase 317 syntax-tree-emission leg + Phase 321 trust boundary. The
    // wire→source SCAFFOLD mode: emit host SOURCE that constructs a specific
    // node tree (the AI-emitted wire becomes compilable host code). This is the
    // only path where wire-derived VALUES land in source, so it is the
    // template-injection surface — every wire string goes through an escaped
    // literal (`fsStringLit`), and an unsupported feature ERRORS rather than
    // mis-emitting. A hostile string therefore cannot break out of a literal:
    // proven by the breakout tests (a value crafted to inject code emerges only
    // as escaped data). The encoder MODULES above carry no wire data (only
    // IDL-derived identifiers), so they have no injection surface at all.
    // -----------------------------------------------------------------------

    /// An escaped F# string literal — the injection-proof "escaped data node"
    /// for the scaffold mode.
    let private fsStringLit (s: string) : string =
        let sb = System.Text.StringBuilder()
        sb.Append '"' |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append "\\\"" |> ignore
            | '\\' -> sb.Append "\\\\" |> ignore
            | '\n' -> sb.Append "\\n" |> ignore
            | '\r' -> sb.Append "\\r" |> ignore
            | '\t' -> sb.Append "\\t" |> ignore
            | c when c < ' ' -> sb.Append(sprintf "\\u%04x" (int c)) |> ignore
            | c -> sb.Append c |> ignore

        sb.Append '"' |> ignore
        sb.ToString()

    let rec private substG (subst: Map<string, IdlType>) (t: IdlType) : IdlType =
        match t with
        | TVar v ->
            match Map.tryFind v subst with
            | Some r -> r
            | None -> t
        | TList inner -> TList(substG subst inner)
        | TMap inner -> TMap(substG subst inner)
        | TUnion(n, args) -> TUnion(n, List.map (substG subst) args)
        | other -> other

    /// Does this authored value POPULATE a hosted slot ([[THosted]]) anywhere?
    ///
    /// A hosted slot's CONTENT belongs to the host codec's own specification — the
    /// IDL says only that the position carries verbatim JSON (see [[HostedCodec]]) —
    /// so a value-generic sampler cannot draw content a host codec is obliged to
    /// accept, and the real vocabulary's codecs are genuinely strict: an aria role
    /// is a string, a row feed is an array of row objects or the legacy sentinel, a
    /// `DataSource` is an object carrying `columns`. Any consumer that runs a
    /// sampled value THROUGH a host codec therefore has to know which of its own
    /// vectors it put beyond that codec's reach; this answers exactly that, so the
    /// answer is a stated boundary rather than a swallowed failure.
    ///
    /// Precise, not conservative: it walks the value ALONGSIDE its declared type and
    /// only reports a slot that is actually populated, so an optional hosted field
    /// sampled absent does not count.
    let usesHosted (idl: Idl) (v: IdlValue) : bool =
        let rec go (t: IdlType) (value: IdlValue) : bool =
            match t, value with
            | THosted _, _ -> true
            | TList inner, VList xs -> xs |> List.exists (go inner)
            | TMap vt, VMap entries -> entries |> List.exists (fun (_, ev) -> go vt ev)
            | TRecord n, VRecord fs ->
                match idl.Records |> List.tryFind (fun r -> r.Name = n) with
                | Some r -> fields r.Fields fs
                | None -> false
            | TUnion(n, args), VUnion(tag, fs) ->
                match idl.Unions |> List.tryFind (fun u -> u.Name = n) with
                | Some u when List.length u.Params = List.length args ->
                    match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                    | Some c ->
                        let subst = Map.ofList (List.zip u.Params args)

                        fields (c.Fields |> List.map (fun f -> { f with Type = substG subst f.Type })) fs
                    | None -> false
                | _ -> false
            | TKind, VUnion(tag, fs) ->
                match idl.Kinds |> List.tryFind (fun k -> k.Tag = tag) with
                | Some k -> fields k.Fields fs
                | None -> false
            | TOp, VUnion(tag, fs) ->
                match idl.Ops |> List.tryFind (fun o -> o.Tag = tag) with
                | Some o -> fields o.Fields fs
                | None -> false
            | TNode, VNode(_, kindTag, fs) -> node kindTag fs []
            | TNode, VNodeEnv(_, envelope, kindTag, fs) -> node kindTag fs envelope
            | _ -> false

        and fields (declared: IdlField list) (authored: (string * IdlValue) list) : bool =
            declared
            |> List.exists (fun f ->
                match authored |> List.tryFind (fun (n, _) -> n = f.Name) with
                | Some(_, av) when av <> VAbsent -> go f.Type av
                | _ -> false)

        and node (kindTag: string) (kindFields: (string * IdlValue) list) (envelope: (string * IdlValue) list) : bool =
            fields idl.NodeFields envelope
            || (match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
                | Some k -> fields k.Fields kindFields
                | None -> false)

        go TNode v

    let private combineR (results: Result<string, string> list) : Result<string list, string> =
        (Ok [], results)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | Ok xs, Ok x -> Ok(xs @ [ x ])
            | Ok _, Error e -> Error e)

    /// Emit an F# value-construction expression for an authored `IdlValue` of
    /// type `t`, building values of the GENERATED types. Wire-derived strings
    /// route through `fsStringLit`; an unsupported shape ERRORS rather than
    /// mis-emitting (the syntax-tree-emission contract). `Result` so a hostile or
    /// malformed value is rejected, never silently mangled.
    let rec fsharpValue (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, string> =
        match t, v with
        | TStr, VStr s -> Ok(fsStringLit s)
        | TInt, VInt i -> Ok(string i)
        | TBool, VBool b -> Ok(if b then "true" else "false")
        | TFloat, VFloat f -> Ok(invariantFloat f)
        | TFloat, VInt i -> Ok(invariantFloat (float i))
        | TEnum name, VEnum case -> Ok(name + "." + case)
        | TUnion(name, args), VUnion(tag, fields) ->
            match idl.Unions |> List.tryFind (fun u -> u.Name = name) with
            | None -> Error(sprintf "fsharpValue: unknown union '%s'" name)
            | Some u when List.length u.Params <> List.length args ->
                Error(sprintf "fsharpValue: union '%s' arity mismatch" name)
            | Some u ->
                match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                | None -> Error(sprintf "fsharpValue: union '%s' has no case '%s'" name tag)
                | Some c ->
                    let subst = Map.ofList (List.zip u.Params args)

                    let argResults =
                        c.Fields
                        |> List.map (fun f ->
                            match fields |> List.tryFind (fun (n, _) -> n = f.Name) with
                            | Some(_, fv) -> fsharpValue idl (substG subst f.Type) fv
                            | None -> Error(sprintf "fsharpValue: union case '%s' missing field '%s'" tag f.Name))

                    match combineR argResults with
                    | Error e -> Error e
                    | Ok [] -> Ok(name + "." + tag)
                    | Ok parts -> Ok(sprintf "%s.%s(%s)" name tag (String.concat ", " parts))
        | TNode, VNode(id, kindTag, fields) ->
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | None -> Error(sprintf "fsharpValue: unknown kind '%s'" kindTag)
            | Some k ->
                match fsAssignments idl ("kind '" + kindTag + "'") k.Fields fields with
                | Error e -> Error e
                | Ok recFields ->
                    Ok(
                        sprintf
                            "{ Id = %s; Kind = NodeKind.%s { %s } }"
                            (fsStringLit id)
                            kindTag
                            (String.concat "; " recFields)
                    )
        // Phase 698 — the enveloped form. The envelope's assignments sit on the
        // `Node` record itself (`Style = Some …`), the kind's on the spec record, and
        // both go through the SAME `fsAssignments`, so the presence rules cannot
        // drift between the two halves of a node.
        | TNode, VNodeEnv(id, envelope, kindTag, fields) ->
            match idl.Kinds |> List.tryFind (fun k -> k.Tag = kindTag) with
            | None -> Error(sprintf "fsharpValue: unknown kind '%s'" kindTag)
            | Some k ->
                match
                    fsAssignments idl ("kind '" + kindTag + "'") k.Fields fields,
                    fsAssignments idl "node envelope" idl.NodeFields envelope
                with
                | Error e, _
                | _, Error e -> Error e
                | Ok recFields, Ok envFields ->
                    Ok(
                        sprintf
                            "{ Id = %s; Kind = NodeKind.%s { %s }%s }"
                            (fsStringLit id)
                            kindTag
                            (String.concat "; " recFields)
                            (envFields |> List.map (fun a -> "; " + a) |> String.concat "")
                    )
        | TList inner, VList xs ->
            match combineR (xs |> List.map (fsharpValue idl inner)) with
            | Error e -> Error e
            | Ok items -> Ok("[ " + String.concat "; " items + " ]")
        | _, VAbsent -> Error "fsharpValue: VAbsent reached the emitter"
        | _ -> Error(sprintf "fsharpValue: value does not match IDL type %A" t)

    /// Record-field assignments (`Label = …; Icon = Some …`) for one declared field
    /// list against one authored field list, honouring every presence rule. Shared
    /// by a kind's spec record and (Phase 698) by the node envelope — `where`
    /// names the owner for the error messages only.
    and private fsAssignments
        (idl: Idl)
        (where: string)
        (declared: IdlField list)
        (authored: (string * IdlValue) list)
        : Result<string list, string> =
        declared
        |> List.map (fun f ->
            match authored |> List.tryFind (fun (n, _) -> n = f.Name) with
            | (None | Some(_, VAbsent)) ->
                match f.Opt with
                | Optional -> Ok(pascal f.Name + " = None")
                | HostOnly -> Ok(pascal f.Name + " = " + hostOnlyLit f)
                // OmitDefault absent → restore the identity default value.
                | OmitDefault d ->
                    match fsDefaultLit idl.Enums f.Type d with
                    | Some e -> Ok(pascal f.Name + " = " + e)
                    | None -> Error(sprintf "fsharpValue: unrenderable omit-default for '%s' on %s" f.Name where)
                | Required -> Error(sprintf "fsharpValue: %s missing required field '%s'" where f.Name)
            | Some(_, fv) ->
                match f.Opt, fsharpValue idl f.Type fv with
                | _, Error e -> Error e
                // A host-only field has no wire projection, so a wire value
                // at its name is not its value — take the placeholder.
                | HostOnly, Ok _ -> Ok(pascal f.Name + " = " + hostOnlyLit f)
                | Optional, Ok s -> Ok(pascal f.Name + " = Some(" + s + ")")
                // OmitDefault present → the raw (non-option) value, like Required.
                | OmitDefault _, Ok s
                | Required, Ok s -> Ok(pascal f.Name + " = " + s))
        |> combineR

    /// A provenance-stamp header for AI-scaffolded source (Phase 321) — records
    /// the source wire hash + the typed actor so generated code is auditable +
    /// reproducible, and states the trust-split invariant: generated code is
    /// inert structure; behaviour is human-bound (named holes, Phase 318).
    let provenanceHeader (commentPrefix: string) (sourceWireHash: string) (actor: string) : string =
        String.concat
            "\n"
            [ commentPrefix
              + " AI-scaffolded by Fuaran.Core.Idl.Gen — INERT structure; behaviour is human-bound (named holes, Phase 318)."
              commentPrefix + " source-wire-hash: " + sourceWireHash
              commentPrefix + " actor: " + actor ]

// ---------------------------------------------------------------------------
// Phase 696 — the IDL as a canonical DATA artifact (`idl.json`).
//
// Every other leg in this file projects the IDL into some HOST's shape — F#
// source, a TypeScript codec, a JSON Schema. This one projects the IDL into
// *itself*: a faithful, language-neutral rendering of the `Idl` record, so the
// vocabulary can be read, diffed and anchored to without an F# toolchain.
//
// Why it is worth its own leg, given `Gen.jsonSchema` exists: a JSON Schema is a
// VALIDATION surface, and validation loses exactly the information a vocabulary
// consumer needs. Optionality collapses (Draft 2020-12 has `required`, but no way
// to say "omitted when equal to this value" — so every `OmitDefault` becomes
// indistinguishable from `Optional`, and the default VALUE is gone); unions
// flatten into `oneOf`; the host-surface declarations (`TFn` / `THosted`) have
// nowhere to live at all. The schema answers "is this payload legal?"; this
// artifact answers "what is the vocabulary?" — and only the second can be
// diffed into a stability classification.
// ---------------------------------------------------------------------------

/// The IDL rendered as canonical JSON — the `idl.json` spec artifact.
///
/// **Ordering contract**, which is what makes the artifact diffable rather than
/// merely readable:
///
/// - **Across** entries, the top-level collections are Ordinal-sorted by identity
///   (kinds by tag, unions / enums / records by name, defaults by kind-then-field).
///   The authored order of a vocabulary file is incidental grouping, so a reshuffle
///   there must produce no diff, and an addition must land as one clean insert.
/// - **Within** an entry, the authored order is preserved verbatim — field lists,
///   union cases, union type parameters, enum cases. That order IS significant:
///   `Gen` emits union-case fields as POSITIONAL bindings and type parameters
///   positionally, so a reorder is a real host-surface change a reviewer should see.
///
/// Object keys need no such rule — [[Canon.render]] Ordinal-sorts them recursively.
module Artifact =

    /// The artifact ENCODING version — bumped when this module's output shape
    /// changes (a new key, a renamed discriminator), never when the vocabulary it
    /// describes changes. A consumer pins the encoding, not the contents.
    [<Literal>]
    let version = 1

    let private ordinal (a: string) (b: string) = System.String.CompareOrdinal(a, b)

    /// Render a `JVal` with two-space indentation.
    ///
    /// Only WHITESPACE is added here: every scalar and every key string is rendered
    /// by [[Canon.render]] itself, so canonical escaping (rule 6), the pinned float
    /// layout (rule 5) and Ordinal key order are all INHERITED rather than
    /// re-implemented — the drift risk `TJson`'s verbatim passthrough names, in the
    /// one place a second stringifier would otherwise appear.
    ///
    /// Indented rather than compact because this artifact exists to be read and
    /// DIFFED, and a single-line file diffs as "everything changed". `schema.json`
    /// beside it in the corpus takes the same posture.
    let rec private indent (depth: int) (v: JVal) : string =
        let pad n = String.replicate n "  "

        match v with
        | JObj [] -> "{}"
        | JObj fields ->
            let body =
                fields
                |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
                |> List.map (fun (k, fv) -> pad (depth + 1) + Canon.render (JStr k) + ": " + indent (depth + 1) fv)
                |> String.concat ",\n"

            "{\n" + body + "\n" + pad depth + "}"
        | JArr [] -> "[]"
        | JArr xs ->
            let body =
                xs
                |> List.map (fun x -> pad (depth + 1) + indent (depth + 1) x)
                |> String.concat ",\n"

            "[\n" + body + "\n" + pad depth + "]"
        | scalar -> Canon.render scalar

    /// The structural type of a field. `$type` carries the [[IdlType]] case; the
    /// `wire` key, where present, states the FIXED wire form a third party will see
    /// for that case, so a sentinel is never mistaken for authored content.
    ///
    /// `hostSurface` (on [[TFn]] / [[THosted]]) carries the host-language strings
    /// from [[ClosureSig]] / [[HostedCodec]]. They are included because they are
    /// genuinely part of the contract a host must satisfy — and flagged under their
    /// own key because they are **host-surface spec, not wire spec**: nothing in
    /// them is observable on the wire, and a non-F# consumer reading this artifact
    /// to build a codec must ignore them entirely.
    let rec private typeJson (t: IdlType) : JVal =
        match t with
        | TStr -> Canon.typed "str" []
        | TInt -> Canon.typed "int" []
        | TBool -> Canon.typed "bool" []
        | TFloat -> Canon.typed "float" []
        | TEnum name -> Canon.typed "enum" [ "name", JStr name ]
        | TUnion(name, args) -> Canon.typed "union" [ "name", JStr name; "args", JArr(args |> List.map typeJson) ]
        | TVar name -> Canon.typed "var" [ "name", JStr name ]
        | TNode -> Canon.typed "node" []
        | TKind -> Canon.typed "kind" []
        | TOp -> Canon.typed "op" []
        | TList inner -> Canon.typed "list" [ "of", typeJson inner ]
        | TMap valueType -> Canon.typed "map" [ "values", typeJson valueType ]
        | TRecord name -> Canon.typed "record" [ "name", JStr name ]
        | TClosure -> Canon.typed "closure" [ "wire", JStr "<closure>" ]
        | TOpaque -> Canon.typed "opaque" [ "wire", JStr "<opaque>" ]
        | TJson -> Canon.typed "json" []
        | TFn sg ->
            Canon.typed
                "fn"
                [ "wire", JStr "<closure>"
                  "hostSurface",
                  JObj
                      [ "fsharp", JStr sg.FSharp
                        "typescript", JStr sg.TypeScript
                        "placeholder", JStr sg.Placeholder ] ]
        | THosted h ->
            Canon.typed
                "hosted"
                [ "wire", JStr "json"
                  "hostSurface", JObj [ "fsharp", JStr h.FSharp; "encode", JStr h.Encode; "decode", JStr h.Decode ] ]

    /// An authored value — a field default, or a nested part of one.
    let rec private valueJson (v: IdlValue) : JVal =
        match v with
        | VStr s -> Canon.typed "str" [ "value", JStr s ]
        | VInt i -> Canon.typed "int" [ "value", JInt i ]
        | VBool b -> Canon.typed "bool" [ "value", JBool b ]
        | VFloat f -> Canon.typed "float" [ "value", JFloat f ]
        | VEnum case -> Canon.typed "enum" [ "case", JStr case ]
        | VUnion(tag, fields) -> Canon.typed "union" [ "tag", JStr tag; "fields", namedValues fields ]
        | VList xs -> Canon.typed "list" [ "items", JArr(xs |> List.map valueJson) ]
        | VNode(id, kindTag, fields) ->
            Canon.typed "node" [ "id", JStr id; "kind", JStr kindTag; "fields", namedValues fields ]
        // Phase 698 — the enveloped form records its envelope under its own key
        // rather than merged into `fields`: the two namespaces can collide (an
        // envelope `style` and `Drawing.style` both exist), so merging them would
        // make the artefact ambiguous and the Phase 700 diff classifier wrong.
        | VNodeEnv(id, envelope, kindTag, fields) ->
            Canon.typed
                "node"
                [ "envelope", namedValues envelope
                  "fields", namedValues fields
                  "id", JStr id
                  "kind", JStr kindTag ]
        | VAbsent -> Canon.typed "absent" []
        | VClosure -> Canon.typed "closure" []
        | VOpaque -> Canon.typed "opaque" []
        // Verbatim — a `TJson` value is real data, and `Canon.render` already lays it
        // out canonically, so passing the `JVal` through inherits every rule.
        | VJson j -> Canon.typed "json" [ "value", j ]
        | VRecord fields -> Canon.typed "record" [ "fields", namedValues fields ]
        | VMap entries -> Canon.typed "map" [ "entries", namedValues entries ]

    /// Named sub-values of a composite default (union / record / node / map fields).
    /// Ordinal-sorted by name: these are wire KEYS, which `Canon.render` sorts anyway,
    /// so authored order carries no information to preserve here.
    and private namedValues (fields: (string * IdlValue) list) : JVal =
        fields
        |> List.sortWith (fun (a, _) (b, _) -> ordinal a b)
        |> List.map (fun (name, v) -> JObj [ "name", JStr name; "value", valueJson v ])
        |> JArr

    /// Whether a field is on the wire, and under what condition. `omitDefault`
    /// carries the identity default VALUE — the single thing a JSON Schema
    /// projection of the same vocabulary cannot express.
    let private optionalityJson (o: Optionality) : JVal =
        match o with
        | Required -> Canon.typed "required" []
        | Optional -> Canon.typed "optional" []
        | HostOnly -> Canon.typed "hostOnly" []
        | OmitDefault d -> Canon.typed "omitDefault" [ "default", valueJson d ]

    /// Field lists keep their AUTHORED order (see the module's ordering contract).
    let private fieldsJson (fs: IdlField list) : JVal =
        fs
        |> List.map (fun f ->
            JObj
                [ "name", JStr f.Name
                  "type", typeJson f.Type
                  "optionality", optionalityJson f.Opt ])
        |> JArr

    let private kindJson (k: IdlKind) : JVal =
        JObj
            [ "tag", JStr k.Tag
              "category", JStr k.Category
              "fields", fieldsJson k.Fields ]

    let private unionJson (u: IdlUnion) : JVal =
        let cases =
            u.Cases
            |> List.map (fun c -> JObj [ "tag", JStr c.Tag; "fields", fieldsJson c.Fields ])
            |> JArr

        let baseFields =
            [ "name", JStr u.Name
              // Positional — never sorted.
              "params", JArr(u.Params |> List.map JStr)
              "cases", cases ]

        // A transparent case encodes as a BARE value rather than a `$type`-tagged
        // object, so a consumer that missed it would decode the union wrongly. The
        // engine hard-codes the set by name ([[TransparentUnion]]) rather than
        // declaring it in the vocabulary; surfacing it here keeps the artifact
        // faithful instead of inheriting that gap.
        match TransparentUnion.tag u with
        | Some tag -> JObj(baseFields @ [ "transparentCase", JStr tag ])
        | None -> JObj baseFields

    /// The whole IDL as a `JVal`.
    let json (idl: Idl) : JVal =
        let sortedBy (key: 'a -> string) (xs: 'a list) =
            xs |> List.sortWith (fun a b -> ordinal (key a) (key b))

        JObj(
            [ "version", JInt version
              "description",
              JStr(
                  "Canonical data rendering of the Fuaran UI wire vocabulary — kinds, unions, enums, "
                  + "records, field defaults and the node envelope. This is the STRUCTURAL source: it "
                  + "states what the vocabulary is, including optionality classes and omit-at-default "
                  + "values that a JSON Schema cannot express. schema.json beside it is the VALIDATION "
                  + "surface, derived from the same contract. Keys marked hostSurface are host-language "
                  + "declarations, not wire spec, and carry nothing observable on the wire. See "
                  + "WIRE_FORMAT.md section 13."
              )
              "kinds", JArr(idl.Kinds |> sortedBy _.Tag |> List.map kindJson)
              "unions", JArr(idl.Unions |> sortedBy _.Name |> List.map unionJson)
              "enums",
              JArr(
                  idl.Enums
                  |> sortedBy _.Name
                  |> List.map (fun e ->
                      // `cases` is the wire contract (what a decoder must accept).
                      // `hostCases` appears ONLY for a Phase 707 wire-mapped enum and
                      // is a hostSurface key in the §13 sense — a host-language
                      // declaration carrying nothing observable on the wire. Omitting
                      // it for the identity mapping is what keeps every pre-707
                      // artefact byte-identical.
                      JObj(
                          [ "name", JStr e.Name; "cases", JArr(e.WireCases |> List.map JStr) ]
                          @ (if List.isEmpty e.Wires then
                                 []
                             else
                                 [ "hostCases", JArr(e.Cases |> List.map JStr) ])
                      ))
              )
              "records",
              JArr(
                  idl.Records
                  |> sortedBy _.Name
                  |> List.map (fun r -> JObj [ "name", JStr r.Name; "fields", fieldsJson r.Fields ])
              )
              "defaults",
              JArr(
                  idl.Defaults
                  |> List.sortWith (fun a b ->
                      match ordinal a.Kind b.Kind with
                      | 0 -> ordinal a.Field b.Field
                      | c -> c)
                  |> List.map (fun d -> JObj [ "kind", JStr d.Kind; "field", JStr d.Field; "value", valueJson d.Value ])
              )
              "nodeFields", fieldsJson idl.NodeFields ]
            // The op vocabulary (Phase 703) — the wire's second root. Emitted only
            // when the domain declares ops, so an op-free vocabulary's artefact is
            // byte-for-byte what it was, the same posture `hostCases` takes.
            @ (if List.isEmpty idl.Ops then
                   []
               else
                   [ "ops", JArr(idl.Ops |> sortedBy _.Tag |> List.map kindJson) ])
        )

    /// The `idl.json` bytes — indented, canonically ordered, newline-terminated
    /// (matching `schema.json`'s convention in the same corpus).
    let render (idl: Idl) : string = indent 0 (json idl) + "\n"
