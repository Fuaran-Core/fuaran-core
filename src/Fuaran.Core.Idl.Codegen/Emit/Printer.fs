namespace Fuaran.Core.Idl.Emit

open Fuaran.Core.Idl
open Fuaran.Core.Idl.Emit.Core

// ---------------------------------------------------------------------------
// Phase 524 (B §8 S10) — ONE TRAVERSAL, A PRINTER PER BACKEND. The F# and TypeScript codecs each
// carried their own walk for the same questions — which encoder or decoder a slot takes, how a
// member is read under its presence, how a union's cases and a transparent case are laid out —
// agreeing on the partition, the recursion, the order and the refusals, and differing only in the
// text each arm prints. The decisions are stated once here (the traversals); what each backend
// prints is a record of strings and string builders (the printers), one value per backend, kept
// beside that backend's emitter. A further backend costs a printer, not a walk.
// ---------------------------------------------------------------------------

/// How a backend reads or writes a HOSTED slot: verbatim through a named host expression, through
/// the slot's declared wire type (the result wrapped by the backend), or not at all.
[<RequireQualifiedAccess>]
type internal HostedSlot =
    /// The backend's own expression for the slot, verbatim.
    | Verbatim of string
    /// THROUGH the declared wire type (Phase 252), so the bytes are that type's; the wire type's
    /// own encoder or decoder is handed to `wrap`.
    | Through of wire: IdlType * wrap: (string -> string)
    /// The backend cannot handle it; the refusal names why.
    | Refused of CodegenError

/// What one backend prints for a slot's point-free ENCODER or DECODER — the function a container
/// or a generic argument is handed. One record type serves both directions: an encoder printer and
/// a decoder printer are two values of it.
type internal SlotPrinter =
    {
        /// The backend's name in an op-vocabulary refusal.
        Backend: string
        Str: string
        Int: string
        Bool: string
        Float: string
        /// An enum's codec, by the enum's name.
        Enum: string -> string
        /// A type variable's codec, by the variable's name.
        Var: string -> string
        /// A declared name's codec — a non-generic union, a record.
        Named: string -> string
        Node: string
        /// A generic union's codec, by name, over its argument codecs.
        Union: string -> string list -> string
        /// A list's codec over its element codec.
        List: string -> string
        /// A string-keyed map's codec over its value codec.
        Map: string -> string
        /// A closure slot's codec.
        Closure: string
        /// A `TFn` slot's codec, from its declared signature.
        Fn: ClosureSig -> string
        /// An opaque slot's codec.
        Opaque: string
        /// A verbatim-JSON slot's codec.
        Json: string
        Hosted: HostedCodec -> HostedSlot
    }

/// What a backend prints for an encoder APPLIED to a value, beside its point-free forms.
type internal AppliedPrinter =
    {
        Slot: SlotPrinter
        /// An encoder applied to a value.
        Apply: string -> string -> string
        /// A list's element encoder applied over a list value.
        ListApplied: string -> string -> string
        /// A node value encoded.
        NodeApplied: string -> string
        /// A generic union's encoder, by name over its argument encoders, applied to a value.
        UnionApplied: string -> string list -> string -> string
        /// A closure slot's sentinel for a value.
        ClosureApplied: string -> string
        /// An opaque slot's sentinel for a value.
        OpaqueApplied: string -> string
    }

/// The one traversal over a slot's type, printed by a backend's [[SlotPrinter]].
module internal SlotTraversal =

    /// The point-free codec for a slot of type `t`.
    let rec slotFn (p: SlotPrinter) (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TStr -> Ok p.Str
        | TInt -> Ok p.Int
        | TBool -> Ok p.Bool
        | TFloat -> Ok p.Float
        | TEnum n -> Ok(p.Enum n)
        | TVar v -> Ok(p.Var v)
        | TUnion(n, []) -> Ok(p.Named n)
        | TUnion(n, args) -> args |> List.map (slotFn p) |> sequenceR |> Result.map (p.Union n)
        | TNode -> Ok p.Node
        // Phase 195 — the op vocabulary is REFUSED AS DATA rather than thrown at. See
        // [[opVocabularySlot]] for why the arm exists and what it says.
        | TKind
        | TOp -> Error(opVocabularySlot p.Backend t)
        | TList inner -> slotFn p inner |> Result.map p.List
        | TClosure -> Ok p.Closure
        | TFn s -> Ok(p.Fn s)
        | TOpaque -> Ok p.Opaque
        // Phase 676 — verbatim passthrough.
        | TJson -> Ok p.Json
        | THosted h ->
            match p.Hosted h with
            | HostedSlot.Verbatim s -> Ok s
            | HostedSlot.Through(wire, wrap) -> slotFn p wire |> Result.map wrap
            | HostedSlot.Refused e -> Error e
        | TRecord n -> Ok(p.Named n)
        | TMap vt -> slotFn p vt |> Result.map p.Map

    /// The encoder applied to the value `var` of type `t`.
    let encApplied (p: AppliedPrinter) (var: string) (t: IdlType) : Result<string, CodegenError> =
        match t with
        | TList inner -> slotFn p.Slot inner |> Result.map (fun e -> p.ListApplied e var)
        | TNode -> Ok(p.NodeApplied var)
        | TUnion(n, (_ :: _ as args)) ->
            args
            |> List.map (slotFn p.Slot)
            |> sequenceR
            |> Result.map (fun a -> p.UnionApplied n a var)
        | TClosure
        | TFn _ -> Ok(p.ClosureApplied var)
        | TOpaque -> Ok(p.OpaqueApplied var)
        | _ -> slotFn p.Slot t |> Result.map (fun e -> p.Apply e var)

    /// Whether a type has independent positions a COLLECTING decoder walks (an object, a list, a
    /// map, a type parameter's codec); every other type is a leaf, which reports at most one defect.
    let collectsOver (t: IdlType) : bool =
        match t with
        | TList _
        | TMap _
        | TRecord _
        | TUnion _
        | TNode
        | TVar _ -> true
        | _ -> false

    /// Phase 377 / 524 — the COLLECTING decoder printer, derived from the short-circuiting one
    /// (`dec`): a leaf is `dec`'s decoder lifted by `leaf`, so a leaf's collecting form cannot drift
    /// from its decoder; only the positions [[collectsOver]] names take the printer `positions`'
    /// own text. One traversal ([[slotFn]]) walks both.
    let collecting (dec: SlotPrinter) (leaf: string -> string) (positions: SlotPrinter) : SlotPrinter =
        { positions with
            Backend = dec.Backend
            Str = leaf dec.Str
            Int = leaf dec.Int
            Bool = leaf dec.Bool
            Float = leaf dec.Float
            Enum = fun n -> leaf (dec.Enum n)
            Closure = leaf dec.Closure
            Fn = fun s -> leaf (dec.Fn s)
            Opaque = leaf dec.Opaque
            Json = leaf dec.Json
            Hosted =
                fun h ->
                    match slotFn dec (THosted h) with
                    | Ok d -> HostedSlot.Verbatim(leaf d)
                    | Error e -> HostedSlot.Refused e }

/// What a backend prints for reading one MEMBER back out under its presence.
type internal MemberDecPrinter =
    {
        /// The point-free decoders.
        Slot: SlotPrinter
        /// The member's wire key as a literal.
        Key: string -> string
        /// The object a member of `fields` is read from (Phase 539: a member declaring an alias or a
        /// presence condition is read from its canonical view of the object).
        Object: IdlField list -> IdlField -> string
        /// A required member read with a decoder, from its key, its object and its decoder.
        Req: string -> string -> string -> string
        /// An optional member read with a decoder.
        Opt: string -> string -> string -> string
        /// An omit-at-default member read with a decoder, restored to a literal when absent.
        Def: string -> string -> string -> string -> string
        /// The value a SENTINEL slot (closure, `TFn`, opaque) decodes to.
        Placeholder: IdlType -> string
        /// A host-only sentinel slot, from its placeholder.
        HostOnlySentinel: string -> string
        /// A host-only member of any other type.
        HostOnly: IdlField -> Result<string, CodegenError>
        /// The declared default `d` at `t`, as the literal the decoder restores.
        DefaultLit: IdlType -> IdlValue -> Result<string, CodegenError>
    }

/// The one traversal over a member's presence when it is READ, printed by a [[MemberDecPrinter]].
module internal MemberTraversal =

    /// Reading the member `f` back out, honouring the presence rule it was written under.
    ///
    /// A SENTINEL slot's value carries nothing, so what decodes is its placeholder; but the slot IS
    /// read (Phase 347), as the interpreter reads it: a required one absent is `MissingField`, a
    /// present one must be its sentinel, and an optional one's PRESENCE is real wire information.
    /// Phase 124 — an omit-at-default member's restore and the encoder's omit test are rendered from
    /// ONE literal, so they cannot disagree.
    let decMember (p: MemberDecPrinter) (fields: IdlField list) (f: IdlField) : Result<string, CodegenError> =
        let key = p.Key f.Name
        let o = p.Object fields f

        match f.Type with
        | TClosure
        | TFn _
        | TOpaque ->
            let placeholder = p.Placeholder f.Type

            SlotTraversal.slotFn p.Slot f.Type
            |> Result.map (fun d ->
                match f.Opt with
                | Required -> p.Req key o d
                | Optional -> p.Opt key o d
                | OmitDefault _ -> p.Def key o d placeholder
                // Never on the wire — nothing to read, so take the placeholder.
                | HostOnly -> p.HostOnlySentinel placeholder)
        | _ ->
            match f.Opt with
            | Required -> SlotTraversal.slotFn p.Slot f.Type |> Result.map (p.Req key o)
            | Optional -> SlotTraversal.slotFn p.Slot f.Type |> Result.map (p.Opt key o)
            | HostOnly -> p.HostOnly f
            | OmitDefault d ->
                p.DefaultLit f.Type d
                |> Result.bind (fun lit ->
                    SlotTraversal.slotFn p.Slot f.Type
                    |> Result.map (fun dfn -> p.Def key o dfn lit))

    /// Every member of an object read, in declaration order, each paired with its field.
    let decMembers (p: MemberDecPrinter) (fields: IdlField list) : Result<(IdlField * string) list, CodegenError> =
        fields
        |> List.map (fun f -> decMember p fields f |> Result.map (fun e -> f, e))
        |> sequenceR

    /// Reading the member `f` COLLECTING (Phase 377): a leaf member, and a host-only one, which
    /// reads nothing, is the short-circuiting reader's expression (`dec`) lifted by `lift`, so its
    /// presence rules are that reader's by construction; a member with positions beneath it is
    /// read by `col`, whose slot printer is the collecting one.
    let colMember
        (dec: MemberDecPrinter)
        (col: MemberDecPrinter)
        (lift: string -> string)
        (fields: IdlField list)
        (f: IdlField)
        : Result<string, CodegenError> =
        match f.Opt with
        | HostOnly -> decMember dec fields f |> Result.map lift
        | _ when not (SlotTraversal.collectsOver f.Type) -> decMember dec fields f |> Result.map lift
        | _ -> decMember col fields f

    /// Every member of an object read collecting, in declaration order.
    let colMembers
        (dec: MemberDecPrinter)
        (col: MemberDecPrinter)
        (lift: string -> string)
        (fields: IdlField list)
        : Result<(IdlField * string) list, CodegenError> =
        fields
        |> List.map (fun f -> colMember dec col lift fields f |> Result.map (fun e -> f, e))
        |> sequenceR

/// What a backend prints for a union DECODER, around the members [[MemberTraversal]] reads.
type internal UnionDecPrinter =
    {
        /// The members of a case, read in declaration order.
        Read: IdlField list -> Result<(IdlField * string) list, CodegenError>
        /// The point-free decoders the bare transparent case is read with.
        Slot: SlotPrinter
        /// One tagged case's arm, from its members as read.
        Arm: IdlUnion -> IdlUnionCase -> (IdlField * string) list -> string
        /// The fallthrough reading the declared transparent case BARE, from its one field's decoder.
        Bare: IdlUnion -> IdlUnionCase -> IdlField -> string -> string
        /// The fallthrough of a union with no transparent case.
        NoBare: IdlUnion -> string
        /// The whole decoder, from its arms and its fallthrough.
        Union: IdlUnion -> string list -> string -> string
    }

/// The one traversal over a union's cases when it is DECODED.
module internal UnionTraversal =

    /// The fallthrough a union's decoder takes for a value that is not a tagged object: the
    /// declared transparent case read BARE, through `bare` (its field's decoder), or none. A
    /// declared transparent tag that names no case, or a case without exactly one field, is
    /// refused: the bare form is the field, so without one the declaration cannot be read.
    let fallthrough
        (decl: Decl)
        (u: IdlUnion)
        (bare: IdlUnionCase -> IdlField -> Result<string, CodegenError>)
        (none: string)
        : Result<string, CodegenError> =
        match Decl.transparentTag decl u with
        | None -> Ok none
        | Some ttag ->
            match u.Cases |> List.tryFind (fun c -> c.Tag = ttag) with
            | None -> Error(transparentArity u.Name ttag)
            | Some c ->
                match Decl.caseLayout decl u c with
                | CaseLayout.Bare(Ok f) -> bare c f
                | CaseLayout.Bare(Error e) -> Error e
                | CaseLayout.Tagged -> Ok none

    /// A union's decoder — short-circuiting or collecting, by the reader the printer carries: each
    /// case's members read in declaration order, then the transparent fallthrough.
    let decoder (p: UnionDecPrinter) (decl: Decl) (u: IdlUnion) : Result<string, CodegenError> =
        u.Cases
        |> List.map (fun c -> p.Read c.Fields |> Result.map (p.Arm u c))
        |> sequenceR
        |> Result.bind (fun arms ->
            fallthrough
                decl
                u
                (fun c f -> SlotTraversal.slotFn p.Slot f.Type |> Result.map (p.Bare u c f))
                (p.NoBare u)
            |> Result.map (p.Union u arms))

/// One member's piece of an encoded object: ALWAYS on the wire (its key and its encoder applied),
/// or a backend expression that decides at run time whether it is.
[<RequireQualifiedAccess>]
type internal MemberPiece =
    | Always of key: string * enc: string
    | Conditional of string

/// What a backend prints for writing one MEMBER under its presence.
type internal MemberEncPrinter =
    {
        /// The encoders, point-free and applied.
        Applied: AppliedPrinter
        /// The member's wire key as a literal.
        Key: string -> string
        /// A member read off a bound object variable.
        Access: string -> IdlField -> string
        /// An always-present member in a piece list, from its key and its encoder applied.
        Present: string -> string -> string
        /// The variable an optional member's PRESENT value is bound to, from the member's source.
        OptionalVar: string -> string
        /// An optional member, from its source, its key and its encoder applied to [[OptionalVar]].
        Optional: string -> string -> string -> string
        /// A member that is never on the wire.
        Absent: string
        /// The omission at the declared default `d` of the member read from `src` at type `t`: a
        /// wrapper around the member's present piece.
        OmitAt: string -> IdlType -> IdlValue -> Result<string -> string, CodegenError>
        /// Phase 539 — a member's presence condition over its siblings in `fields` (each read from
        /// the expression the name maps to): a wrapper around the member's piece that omits it
        /// whenever the condition fails.
        When: IdlField list -> (string -> string) -> PresenceCondition -> Result<string -> string, CodegenError>
    }

/// The one traversal over a member's presence when it is WRITTEN, printed by a [[MemberEncPrinter]]
/// (Phase 511's presence rule: Required → always; Optional → omitted when absent; HostOnly → never
/// on the wire, in any state (Phase 691); OmitDefault → omitted at its default).
module internal MemberEncTraversal =

    /// A piece as it reads in a list where some members may be absent.
    let print (p: MemberEncPrinter) (piece: MemberPiece) : string =
        match piece with
        | MemberPiece.Always(key, e) -> p.Present key e
        | MemberPiece.Conditional s -> s

    /// The member `f` of `fields`, each member's value read from `at` (its binding or its access).
    /// A member declaring a presence condition (Phase 539) is omitted whenever the condition fails.
    let encMember
        (p: MemberEncPrinter)
        (fields: IdlField list)
        (at: IdlField -> string)
        (f: IdlField)
        : Result<MemberPiece, CodegenError> =
        let key = p.Key f.Name
        let src = at f

        let sibling (name: string) =
            match fields |> List.tryFind (fun g -> g.Name = name) with
            | Some g -> at g
            | None -> name

        let piece =
            match f.Opt with
            | HostOnly -> Ok(MemberPiece.Conditional p.Absent)
            | Required ->
                SlotTraversal.encApplied p.Applied src f.Type
                |> Result.map (fun e -> MemberPiece.Always(key, e))
            | Optional ->
                SlotTraversal.encApplied p.Applied (p.OptionalVar src) f.Type
                |> Result.map (fun e -> MemberPiece.Conditional(p.Optional src key e))
            | OmitDefault d ->
                SlotTraversal.encApplied p.Applied src f.Type
                |> Result.bind (fun e ->
                    p.OmitAt src f.Type d
                    |> Result.map (fun omit -> MemberPiece.Conditional(omit (p.Present key e))))

        piece
        |> Result.bind (fun piece ->
            match f.Presence with
            | None -> Ok piece
            | Some c ->
                p.When fields sibling c
                |> Result.map (fun guard -> MemberPiece.Conditional(guard (print p piece))))

    /// Every member of an object read off the bound variable `recv`, in declaration order.
    let encMembers
        (p: MemberEncPrinter)
        (recv: string)
        (fields: IdlField list)
        : Result<MemberPiece list, CodegenError> =
        fields |> List.map (encMember p fields (p.Access recv)) |> sequenceR

/// What a backend prints for a union ENCODER, around the members [[MemberEncTraversal]] writes.
type internal UnionEncPrinter =
    {
        Member: MemberEncPrinter
        /// The binding a case's member is read from.
        CaseVar: IdlField -> string
        /// The declared transparent case's arm, from its one field's encoder applied.
        Bare: IdlUnion -> IdlUnionCase -> string -> string
        /// A tagged case's arm, from its members' pieces.
        Tagged: IdlUnion -> IdlUnionCase -> MemberPiece list -> string
        /// The whole encoder, from its arms.
        Union: IdlUnion -> string list -> string
    }

/// The one traversal over a union's cases when it is ENCODED.
module internal UnionEncTraversal =

    /// A union's encoder: the declared transparent case BARE (its single field's value, no
    /// discriminator), every other case tagged with its members under their presence.
    let encoder (p: UnionEncPrinter) (decl: Decl) (u: IdlUnion) : Result<string, CodegenError> =
        u.Cases
        |> List.map (fun c ->
            match Decl.caseLayout decl u c with
            | CaseLayout.Bare(Ok f) ->
                SlotTraversal.encApplied p.Member.Applied (p.CaseVar f) f.Type
                |> Result.map (p.Bare u c)
            | CaseLayout.Bare(Error e) -> Error e
            | CaseLayout.Tagged ->
                c.Fields
                |> List.map (MemberEncTraversal.encMember p.Member c.Fields p.CaseVar)
                |> sequenceR
                |> Result.map (p.Tagged u c))
        |> sequenceR
        |> Result.map (p.Union u)

/// What a backend prints for the COLLECTING decoder group (Phase 377's `SpecDecoders`).
type internal CollectingPrinter =
    {
        /// The short-circuiting member reader, whose expressions a leaf member lifts.
        Dec: MemberDecPrinter
        /// The collecting member reader.
        Col: MemberDecPrinter
        /// A short-circuiting reader's expression lifted to a collecting one.
        Lift: string -> string
        /// The collecting decoder of a union, from a reader of its members.
        Union:
            (IdlField list -> Result<(IdlField * string) list, CodegenError>)
                -> IdlUnion
                -> Result<string, CodegenError>
        /// The node's kind dispatch.
        NodeKind: string
        /// The node, from its envelope members as read.
        Node: (IdlField * string) list -> string
        /// A record, from its members as read.
        Record: IdlRecord -> (IdlField * string) list -> string
        /// Whether a kind's spec is read member by member here (a projected kind is not).
        ReadsSpec: IdlKind -> bool
        /// A kind's spec, from its members as read, or `None` where [[ReadsSpec]] says it is not.
        Spec: IdlKind -> (IdlField * string) list option -> string
    }

/// The one traversal over the collecting decoder group: the node's kind, the node, the unions, the
/// records and the kind specs, in that order, every member read by [[MemberTraversal.colMember]].
module internal CollectingTraversal =

    let group (p: CollectingPrinter) (decl: Decl) : Result<string, CodegenError> =
        let read = MemberTraversal.colMembers p.Dec p.Col p.Lift

        (Ok p.NodeKind
         :: (read decl.Envelope.Members |> Result.map p.Node)
         :: (decl.Unions |> List.map (p.Union read))
         @ (decl.Records |> List.map (fun r -> read r.Fields |> Result.map (p.Record r)))
         @ (decl.Kinds
            |> List.map (fun k ->
                if p.ReadsSpec k then
                    read k.Fields |> Result.map (Some >> p.Spec k)
                else
                    Ok(p.Spec k None))))
        |> concatR "\n\n"

/// What a backend prints for an ADMITTED declared literal ([[Decl.literal]]) — a value in source,
/// or a test that a value read from `src` is that literal. Every field takes `src`, the expression
/// the literal is compared against; a printer that writes a value ignores it.
type internal LiteralPrinter =
    {
        Str: string -> string -> string
        Int: string -> int -> string
        Float: string -> float -> string
        Bool: string -> bool -> string
        /// An enum literal, from the enum's name, the host case and the wire spelling.
        Enum: string -> string -> string -> string -> string
        EmptyList: string -> string
        /// The empty map (Phase 539).
        EmptyMap: string -> string
        /// A JSON value at a `TJson` slot (Phase 539).
        Json: string -> Fuaran.Core.JVal -> string
        /// A record literal from its declared members, printed in declaration order.
        Record: string -> IdlRecord -> (IdlField * string) list -> string
        /// A union case literal from its declared members, printed in declaration order.
        Union: string -> IdlUnion -> IdlUnionCase -> (IdlField * string) list -> string
        /// An optional member carrying a value, from the value printed.
        Some: string -> string
        /// An optional member carrying none.
        Absent: string -> string
        /// Where a member's value is read from, under the object read from `src`.
        Path: string -> IdlField -> string
    }

/// Phase 524 (511.t4) — the one traversal over a declared literal: [[Decl.literal]] decides what is
/// admitted (the admissibility every backend shares, Phase 525), [[Decl.memberValue]] at the
/// LITERAL site decides each member's value (a host-only member is refused rather than filled
/// with its placeholder, a required one with no value is refused), and a backend's
/// [[LiteralPrinter]] only prints. The F# literal, the TypeScript omit test and the F* constructor
/// are its printers, so no backend can admit a default another refuses.
///
/// Phase 539 — the empty map and a JSON value are admitted where the member's OWN type is the map
/// or the JSON slot, and refused as a member of a record or union-case literal: neither has a
/// PATTERN spelling, and the union encoder's omit test matches a default as a pattern. Refused
/// here, once, so the F# literal and the TypeScript test agree on it by construction.
module internal LiteralTraversal =

    let rec private printAt
        (nested: bool)
        (p: LiteralPrinter)
        (idl: Idl)
        (src: string)
        (t: IdlType)
        (v: IdlValue)
        : Result<string, CodegenError> =
        Decl.literal idl t v
        |> Result.bind (fun l ->
            match l with
            | Literal.EmptyMap
            | Literal.Json _ when nested -> Error(CodegenError.UnsupportedDefault(t, v))
            | Literal.EmptyMap -> Ok(p.EmptyMap src)
            | Literal.Json j -> Ok(p.Json src j)
            | Literal.Str s -> Ok(p.Str src s)
            | Literal.Int i -> Ok(p.Int src i)
            | Literal.Bool b -> Ok(p.Bool src b)
            // A whole-valued float authored as an integer reads as a float here — the canonical
            // artifact writes one that way (the same asymmetry `dFloat` carries on decode).
            | Literal.Float f -> Ok(p.Float src f)
            | Literal.Enum(n, case) ->
                let wire =
                    match v with
                    | VEnum spelling -> spelling
                    | _ -> case

                Ok(p.Enum src n case wire)
            | Literal.EmptyList -> Ok(p.EmptyList src)
            | Literal.Record(r, authored) ->
                members p idl src Map.empty r.Fields authored |> Result.map (p.Record src r)
            | Literal.Union(u, args, c, authored) ->
                // The DECLARED fields decide the arity, never the authored ones: `Slot.Fixed` is a
                // one-argument constructor whatever an `IdlValue` happens to carry (Phase 124).
                let subst = TypeParams.bind u args |> Option.defaultValue Map.empty
                members p idl src subst c.Fields authored |> Result.map (p.Union src u c))

    and private members
        (p: LiteralPrinter)
        (idl: Idl)
        (src: string)
        (subst: Map<string, IdlType>)
        (fields: IdlField list)
        (authored: (string * IdlValue) list)
        : Result<(IdlField * string) list, CodegenError> =
        fields
        |> List.map (fun f ->
            let ft = TypeParams.substitute subst f.Type
            let at = p.Path src f

            let refuse v =
                Error(CodegenError.UnsupportedDefault(ft, v))

            authored
            |> List.tryFind (fun (n, _) -> n = f.Name)
            |> Option.map snd
            |> Decl.memberValue ValueSite.Literal f.Opt
            |> function
                | MemberValue.Carries v
                | MemberValue.IdentityDefault v -> printAt true p idl at ft v
                | MemberValue.Optional v -> printAt true p idl at ft v |> Result.map p.Some
                | MemberValue.Absent -> Ok(p.Absent at)
                | MemberValue.Refused v -> refuse v
                // Neither is answered at the literal site; a member with no value is refused.
                | MemberValue.Placeholder
                | MemberValue.Parameter -> refuse VAbsent
            |> Result.map (fun e -> f, e))
        |> sequenceR

    /// A declared literal at its member's own type.
    let print (p: LiteralPrinter) (idl: Idl) (src: string) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        printAt false p idl src t v

/// The traversal's own recursion, handed to a backend's leaf arm so it can read a nested value
/// (a map's entries, a hosted slot's declared wire form) by the same walk.
type internal ValueWalk =
    {
        /// A nested value at its type.
        Value: IdlType -> IdlValue -> Result<string, CodegenError>
    }

/// What a backend prints for an AUTHORED value in source (the scaffold leg, Phase 252/317): a value
/// of the generated types, built under every presence rule its declaration states.
type internal ValuePrinter =
    {
        /// A value that does not fit its declaration, named by `what`.
        Mismatch: string -> CodegenError
        /// The backend's arm for a leaf pair (a scalar, a map, JSON, a hosted or sentinel slot), or
        /// `None` when it has none for the pair.
        Leaf: ValueWalk -> IdlType -> IdlValue -> Result<string, CodegenError> option
        /// An admitted enum value, from its declaration and its wire spelling.
        Enum: IdlType -> IdlValue -> Result<string, CodegenError>
        List: string list -> string
        /// A member's value under the one presence rule at the SCAFFOLD site, or `None` where the
        /// backend writes nothing for it; handed the walk at the member's type.
        Member:
            IdlField -> MemberValue -> (IdlValue -> Result<string, CodegenError>) -> Result<string option, CodegenError>
        Record: IdlRecord -> (IdlField * string) list -> string
        Union: IdlUnion -> IdlUnionCase -> (IdlField * string) list -> string
        /// A node, from its id, its kind, the kind's members and the envelope's members.
        Node: string -> IdlKind -> (IdlField * string) list -> (IdlField * string) list -> string
        /// What remains, refused by the backend's own construct.
        Unmatched: IdlType -> IdlValue -> CodegenError
    }

/// The one traversal over an authored value against its declaration: the lookups and their
/// refusals, the union case and its arity, the members under [[Decl.memberValue]] at the SCAFFOLD
/// site (a host-only member has no wire projection, so a wire value at its name is not its value),
/// a node and its envelope; a backend's [[ValuePrinter]] prints.
module internal ValueTraversal =

    let rec value (p: ValuePrinter) (idl: Idl) (t: IdlType) (v: IdlValue) : Result<string, CodegenError> =
        let mismatch what = Error(p.Mismatch what)

        match p.Leaf { Value = value p idl } t v with
        | Some r -> r
        | None ->
            match t, v with
            | TEnum name, VEnum wire ->
                match CodegenLookup.tryEnum idl name with
                | None -> mismatch (sprintf "a value of the undeclared enum '%s'" name)
                | Some e when (e.CaseOf wire).IsNone ->
                    mismatch (sprintf "the wire string %A, which enum '%s' does not admit" wire name)
                | Some _ -> p.Enum t v
            | TList inner, VList xs -> xs |> List.map (value p idl inner) |> sequenceR |> Result.map p.List
            | TUnion(name, args), VUnion(tag, fields) ->
                match CodegenLookup.tryUnion idl name with
                | None -> mismatch (sprintf "a value of the undeclared union '%s'" name)
                | Some u when List.length u.Params <> List.length args ->
                    mismatch (sprintf "union '%s' applied to %d type arguments" name (List.length args))
                | Some u ->
                    match u.Cases |> List.tryFind (fun c -> c.Tag = tag) with
                    | None -> mismatch (sprintf "the case '%s', which union '%s' does not declare" tag name)
                    | Some c ->
                        let subst = TypeParams.bind u args |> Option.defaultValue Map.empty

                        members p idl (sprintf "union case '%s.%s'" name tag) subst c.Fields fields
                        |> Result.map (p.Union u c)
            | TRecord name, VRecord fields ->
                match CodegenLookup.tryRecord idl name with
                | None -> mismatch (sprintf "a value of the undeclared record '%s'" name)
                | Some r ->
                    members p idl ("record '" + name + "'") Map.empty r.Fields fields
                    |> Result.map (p.Record r)
            // Phase 698 — the enveloped form. The envelope's members and the kind's go through the
            // SAME presence rule, so the two halves of a node cannot drift.
            | TNode, VNode(id, kindTag, fields) -> node p idl id [] kindTag fields
            | TNode, VNodeEnv(id, envelope, kindTag, fields) -> node p idl id envelope kindTag fields
            | _, VAbsent -> mismatch "an absent value (VAbsent) in a value position"
            | _ -> Error(p.Unmatched t v)

    and private node p idl (id: string) envelope (kindTag: string) fields : Result<string, CodegenError> =
        match CodegenLookup.tryKind idl kindTag with
        | None -> Error(p.Mismatch(sprintf "a node of the undeclared kind '%s'" kindTag))
        | Some k ->
            match
                members p idl ("kind '" + kindTag + "'") Map.empty k.Fields fields,
                members p idl "node envelope" Map.empty idl.NodeFields envelope
            with
            | Error e, _
            | _, Error e -> Error e
            | Ok kindMembers, Ok envMembers -> Ok(p.Node id k kindMembers envMembers)

    /// The members of an object under their declared presence, in declaration order, each with the
    /// value the backend writes for it (a member it writes nothing for is left out).
    and private members
        (p: ValuePrinter)
        (idl: Idl)
        (where: string)
        (subst: Map<string, IdlType>)
        (declared: IdlField list)
        (authored: (string * IdlValue) list)
        : Result<(IdlField * string) list, CodegenError> =
        declared
        |> List.map (fun f ->
            let f =
                { f with
                    Type = TypeParams.substitute subst f.Type }

            let offered = authored |> List.tryFind (fun (n, _) -> n = f.Name) |> Option.map snd

            match Decl.memberValue ValueSite.Scaffold f.Opt offered with
            | MemberValue.Refused _
            | MemberValue.Parameter -> Error(p.Mismatch(sprintf "%s without its required field '%s'" where f.Name))
            | mv -> p.Member f mv (value p idl f.Type) |> Result.map (Option.map (fun e -> f, e)))
        |> sequenceR
        |> Result.map (List.choose id)
