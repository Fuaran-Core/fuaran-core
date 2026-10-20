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
        /// A required member read with a decoder.
        Req: string -> string -> string
        /// An optional member read with a decoder.
        Opt: string -> string -> string
        /// An omit-at-default member read with a decoder, restored to a literal when absent.
        Def: string -> string -> string -> string
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
    let decMember (p: MemberDecPrinter) (f: IdlField) : Result<string, CodegenError> =
        let key = p.Key f.Name

        match f.Type with
        | TClosure
        | TFn _
        | TOpaque ->
            let placeholder = p.Placeholder f.Type

            SlotTraversal.slotFn p.Slot f.Type
            |> Result.map (fun d ->
                match f.Opt with
                | Required -> p.Req key d
                | Optional -> p.Opt key d
                | OmitDefault _ -> p.Def key d placeholder
                // Never on the wire — nothing to read, so take the placeholder.
                | HostOnly -> p.HostOnlySentinel placeholder)
        | _ ->
            match f.Opt with
            | Required -> SlotTraversal.slotFn p.Slot f.Type |> Result.map (p.Req key)
            | Optional -> SlotTraversal.slotFn p.Slot f.Type |> Result.map (p.Opt key)
            | HostOnly -> p.HostOnly f
            | OmitDefault d ->
                p.DefaultLit f.Type d
                |> Result.bind (fun lit ->
                    SlotTraversal.slotFn p.Slot f.Type |> Result.map (fun dfn -> p.Def key dfn lit))

    /// Every member of an object read, in declaration order, each paired with its field.
    let decMembers (p: MemberDecPrinter) (fields: IdlField list) : Result<(IdlField * string) list, CodegenError> =
        fields
        |> List.map (fun f -> decMember p f |> Result.map (fun e -> f, e))
        |> sequenceR

    /// Reading the member `f` COLLECTING (Phase 377): a leaf member, and a host-only one, which
    /// reads nothing, is the short-circuiting reader's expression (`dec`) lifted by `lift`, so its
    /// presence rules are that reader's by construction; a member with positions beneath it is
    /// read by `col`, whose slot printer is the collecting one.
    let colMember
        (dec: MemberDecPrinter)
        (col: MemberDecPrinter)
        (lift: string -> string)
        (f: IdlField)
        : Result<string, CodegenError> =
        match f.Opt with
        | HostOnly -> decMember dec f |> Result.map lift
        | _ when not (SlotTraversal.collectsOver f.Type) -> decMember dec f |> Result.map lift
        | _ -> decMember col f

    /// Every member of an object read collecting, in declaration order.
    let colMembers
        (dec: MemberDecPrinter)
        (col: MemberDecPrinter)
        (lift: string -> string)
        (fields: IdlField list)
        : Result<(IdlField * string) list, CodegenError> =
        fields
        |> List.map (fun f -> colMember dec col lift f |> Result.map (fun e -> f, e))
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
    }

/// The one traversal over a member's presence when it is WRITTEN, printed by a [[MemberEncPrinter]]
/// (Phase 511's presence rule: Required → always; Optional → omitted when absent; HostOnly → never
/// on the wire, in any state (Phase 691); OmitDefault → omitted at its default).
module internal MemberEncTraversal =

    /// The member `f`, its value read from `src`.
    let encMember (p: MemberEncPrinter) (src: string) (f: IdlField) : Result<MemberPiece, CodegenError> =
        let key = p.Key f.Name

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

    /// A piece as it reads in a list where some members may be absent.
    let print (p: MemberEncPrinter) (piece: MemberPiece) : string =
        match piece with
        | MemberPiece.Always(key, e) -> p.Present key e
        | MemberPiece.Conditional s -> s

    /// Every member of an object read off the bound variable `recv`, in declaration order.
    let encMembers
        (p: MemberEncPrinter)
        (recv: string)
        (fields: IdlField list)
        : Result<MemberPiece list, CodegenError> =
        fields |> List.map (fun f -> encMember p (p.Access recv f) f) |> sequenceR

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
                |> List.map (fun f -> MemberEncTraversal.encMember p.Member (p.CaseVar f) f)
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
