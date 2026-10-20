namespace Fuaran.Core.Idl

open Fuaran.Core

/// A name a type reference asked the vocabulary to resolve and the vocabulary does not declare
/// (Phase 513). Kept apart from a resolved form because every reader answers it differently: the
/// encoder and decoder refuse it only where the value is of the right shape, the sampler refuses it
/// outright, the validator names it, and the depth floor still reads a union reference's arguments.
[<RequireQualifiedAccess>]
type internal Undeclared =
    /// `TEnum name`.
    | Enum of name: string
    /// `TUnion(name, args)` — the arguments are kept because the depth floor and the validator read
    /// them whether or not the union resolves.
    | Union of name: string * args: IdlType list
    /// `TRecord name`.
    | Record of name: string

/// A union reference resolved against the vocabulary (Phase 513): the declaration, the binding its
/// arguments make, and the case the vocabulary declares transparent. The three facts the encoder,
/// the decoder, the sampler and the validator each used to look up for themselves.
type internal UnionForm =
    {
        /// The union's declared name.
        Name: string
        /// The type arguments the reference applies it to.
        Args: IdlType list
        /// The declaration.
        Decl: IdlUnion
        /// Each parameter bound by name to its argument ([[TypeParams.bind]]), or `None` when the
        /// reference applies the union to the wrong number of arguments. Each reader decides where
        /// in its own refusal order that fault is reported; the lowering only states it.
        Binding: Map<string, IdlType> option
        /// The tag of the case [[HardenPolicy.TransparentUnions]] declares transparent for this
        /// union — written BARE, its one field's value without a tag — or `None`.
        Transparent: string option
    }

/// **The IDL's lowered wire form (Phase 513, DECISIONS.md D179)** — what a value of one declared
/// [[IdlType]] looks like on the wire, decided once. [[Encode]], [[Decode]], [[Sample]] and
/// [[Declare]] switch on this rather than on the declared type, so the questions each of them used to
/// answer for itself are answered here and nowhere else: which declaration a name resolves to, how a
/// union's arguments bind and which of its cases is transparent, that a closure and a host-signature
/// function are one sentinel on the wire, that a hosted slot is its declared wire form or verbatim
/// JSON, and that an unbound type variable has no wire form.
///
/// **One level, not a tree.** The lowering is the type's HEAD: the item type of a list, a map's value
/// type, a hosted slot's declared wire type and a record's or union case's field types stay declared
/// [[IdlType]]s, lowered when a reader reaches them. A vocabulary is recursive (a node holds nodes, a
/// union names itself), so a fully lowered tree would not be finite, and lowering a child only when the
/// walk reaches it keeps every reader's refusal order exactly as it was: a fault in a slot the value
/// never reaches is still not reported.
///
/// **What it is not.** A declaration's spelling — `idl.json`'s type objects, the generated host
/// declarations — keeps the distinctions the wire erases (a `closure` is not a `fn`, a union is named
/// and applied rather than substituted), so the artifact writes the declared type and reads only the
/// sentinel spellings from here. Field PRESENCE stays [[Optionality]], read where each reader walks a
/// field list.
///
/// Internal: the code generator reads it through this package's existing `InternalsVisibleTo`, from
/// `NoInlining` functions (D129), when its emitters adopt it in their own phase.
[<RequireQualifiedAccess>]
type internal WireForm =
    /// A JSON string (`TStr`).
    | Str
    /// A JSON integer in the 32-bit range (`TInt`).
    | Int
    /// A JSON `true` / `false` (`TBool`).
    | Bool
    /// A JSON number, or WIRE_FORMAT §7's quoted non-finite token (`TFloat`).
    | Float
    /// A bare string from the enum's closed set of wire strings (`TEnum`, resolved).
    | Enum of IdlEnum
    /// The fixed string `wire`, standing for the one authored value `value` (`TClosure` and `TFn` →
    /// `"<closure>"` / `VClosure`; `TOpaque` → `"<opaque>"` / `VOpaque`).
    | Sentinel of value: IdlValue * wire: string
    /// Any JSON, carried verbatim (`TJson`, and a hosted slot that declares no wire form).
    | Verbatim
    /// A hosted slot that declares its wire form: the type it writes, and a string format on top.
    /// At least one of the two is `Some`; a slot declaring neither is [[Verbatim]].
    | Hosted of wire: IdlType option * format: HostedFormat option
    /// A `$type`-tagged object of a resolved union, or its transparent case written bare (`TUnion`).
    | Union of UnionForm
    /// An untagged object of a resolved record's fields (`TRecord`).
    | Record of IdlRecord
    /// A JSON array of the item type (`TList`).
    | List of item: IdlType
    /// A JSON object whose authored keys map to the value type (`TMap`).
    | Map of value: IdlType
    /// A node, laid out as the vocabulary's node envelope declares (`TNode`).
    | Node
    /// A bare kind body, tagged against the vocabulary's kinds (`TKind`).
    | Kind
    /// A tree op, tagged against the vocabulary's ops (`TOp`).
    | Op
    /// A type variable no instantiation bound (`TVar`): it has no wire form.
    | Unbound of name: string
    /// A reference to a name the vocabulary does not declare.
    | Undeclared of Undeclared

/// The lowering and the questions answered over it.
[<RequireQualifiedAccess>]
module internal WireForm =

    /// The sentinel a function-typed slot (`TClosure`, `TFn`) is written as.
    [<Literal>]
    let closure = "<closure>"

    /// The sentinel an erased `obj` slot (`TOpaque`) is written as.
    [<Literal>]
    let opaque = "<opaque>"

    /// The lowered form of `t` in `idl`: its head resolved against the vocabulary's declarations and
    /// policy, its children left declared (see [[WireForm]]).
    let ofType (idl: Idl) (t: IdlType) : WireForm =
        match t with
        | TStr -> WireForm.Str
        | TInt -> WireForm.Int
        | TBool -> WireForm.Bool
        | TFloat -> WireForm.Float
        | TEnum name ->
            match IdlLookup.tryEnum idl name with
            | Some e -> WireForm.Enum e
            | None -> WireForm.Undeclared(Undeclared.Enum name)
        | TUnion(name, args) ->
            match IdlLookup.tryUnion idl name with
            | Some u ->
                WireForm.Union
                    { Name = name
                      Args = args
                      Decl = u
                      Binding = TypeParams.bind u args
                      Transparent = TransparentUnion.tag idl.Harden u }
            | None -> WireForm.Undeclared(Undeclared.Union(name, args))
        | TVar v -> WireForm.Unbound v
        | TNode -> WireForm.Node
        | TList inner -> WireForm.List inner
        | TClosure
        | TFn _ -> WireForm.Sentinel(VClosure, closure)
        | TOpaque -> WireForm.Sentinel(VOpaque, opaque)
        | TJson
        | THosted { Wire = None; Format = None } -> WireForm.Verbatim
        | THosted h -> WireForm.Hosted(h.Wire, h.Format)
        | TRecord name ->
            match IdlLookup.tryRecord idl name with
            | Some r -> WireForm.Record r
            | None -> WireForm.Undeclared(Undeclared.Record name)
        | TMap valueType -> WireForm.Map valueType
        | TKind -> WireForm.Kind
        | TOp -> WireForm.Op

    /// A union case's fields at an instantiation: each field's type with the binding's arguments
    /// substituted for the union's parameters.
    let caseFields (binding: Map<string, IdlType>) (c: IdlUnionCase) : IdlField list =
        c.Fields
        |> List.map (fun f ->
            { f with
                Type = TypeParams.substitute binding f.Type })

    /// The JSON kind a value of the form takes — a `WrongKind` refusal's `Expected`.
    let jsonKind (form: WireForm) : string =
        match form with
        | WireForm.Str
        | WireForm.Enum _
        | WireForm.Undeclared(Undeclared.Enum _)
        | WireForm.Sentinel _ -> "string"
        | WireForm.Int -> "int"
        | WireForm.Bool -> "bool"
        | WireForm.Float -> "number"
        | WireForm.List _ -> "array"
        | WireForm.Record _
        | WireForm.Map _
        | WireForm.Node
        | WireForm.Kind
        | WireForm.Op
        | WireForm.Union _
        | WireForm.Undeclared(Undeclared.Union _ | Undeclared.Record _) -> "object"
        | WireForm.Verbatim
        | WireForm.Hosted _
        | WireForm.Unbound _ -> "any value"

    /// Whether a value of `t` can encode to a JSON OBJECT — the shape a transparent case must not
    /// have (Phase 292 amendment): the case goes out bare, and a bare object is what every other case
    /// looks like, so the hosts would disagree on which case it is. A reference the vocabulary does
    /// not declare answers by its shape, so the rule holds before the reference is resolved.
    let rec objectCapable (idl: Idl) (t: IdlType) : bool =
        match ofType idl t with
        | WireForm.Verbatim
        | WireForm.Record _
        | WireForm.Map _
        | WireForm.Union _
        | WireForm.Node
        | WireForm.Kind
        | WireForm.Op
        | WireForm.Hosted(None, _)
        | WireForm.Undeclared(Undeclared.Union _ | Undeclared.Record _) -> true
        | WireForm.Hosted(Some w, _) -> objectCapable idl w
        | WireForm.Str
        | WireForm.Int
        | WireForm.Bool
        | WireForm.Float
        | WireForm.Enum _
        | WireForm.Undeclared(Undeclared.Enum _)
        | WireForm.Sentinel _
        | WireForm.List _
        | WireForm.Unbound _ -> false

    /// **The sampler's depth floor, read off the lowering** — whether sampling a value of `t` AT the
    /// floor can still reach a node (Phase 698; here since Phase 513, so the predicate and the draw
    /// switch on one form). At the floor [[Sample]] draws a list or map EMPTY, a union from its nullary
    /// cases when it has any, and an optional field absent; so a node, a bare kind and an op reach
    /// (none has a floor arm of its own), a record reaches through a required field that does, a union
    /// through a required field of a case the floor may draw, and nothing else reaches.
    ///
    /// Why the floor exists at all: a bare node had no floor arm, so a vocabulary that reaches a node
    /// from a node by any non-list path (a kind's required child, the node envelope's optional state
    /// behaviour holding nodes) recursed until the stack went once the sampler drew envelopes.
    ///
    /// A union also reaches when one of its type ARGUMENTS does, whether or not a case the floor draws
    /// holds it, and its cases' fields are read as declared (an unbound variable reaches nothing). That
    /// is wider than the draw in exactly one corner — an argument that reaches a node only behind a
    /// case the floor never draws — and it is kept: an answer that moves here moves which draws the
    /// sampler takes, and so every seeded stream over a vocabulary with that shape. `seen` breaks
    /// declaration cycles.
    let rec reachesNodeAtFloor (idl: Idl) (seen: Set<string>) (t: IdlType) : bool =
        match ofType idl t with
        | WireForm.Node
        | WireForm.Kind
        | WireForm.Op -> true
        | WireForm.Record r when not (Set.contains ("r:" + r.Name) seen) ->
            requiredReachesNode idl (Set.add ("r:" + r.Name) seen) r.Fields
        | WireForm.Union u when not (Set.contains ("u:" + u.Name) seen) ->
            let seen' = Set.add ("u:" + u.Name) seen

            // The floor prefers a nullary case when the union has one, and a nullary case has no
            // fields — so only a union WITHOUT one can still reach a node through a case.
            let candidates =
                match u.Decl.Cases |> List.filter (fun c -> List.isEmpty c.Fields) with
                | [] -> u.Decl.Cases
                | nullary -> nullary

            u.Args |> List.exists (reachesNodeAtFloor idl seen')
            || candidates |> List.exists (fun c -> requiredReachesNode idl seen' c.Fields)
        | WireForm.Undeclared(Undeclared.Union(name, args)) when not (Set.contains ("u:" + name) seen) ->
            args |> List.exists (reachesNodeAtFloor idl (Set.add ("u:" + name) seen))
        | _ -> false

    /// Whether a REQUIRED one of `fields` reaches a node at the depth floor — the fields an owner
    /// cannot leave out there ([[reachesNodeAtFloor]]). An owner (a kind, an op) for which this is
    /// false is a leaf the floor may draw.
    and requiredReachesNode (idl: Idl) (seen: Set<string>) (fields: IdlField list) : bool =
        fields
        |> List.exists (fun f -> f.Opt = Required && reachesNodeAtFloor idl seen f.Type)
