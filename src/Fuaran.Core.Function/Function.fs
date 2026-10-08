namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Function — the artifact-function protocol (Phase 181).
//
//  A saved typed tree behaves as a *function* of declared holes: declare typed
//  holes (abstraction) -> bind a parameter set (application) -> re-derive
//  (the domain's evaluator). The core owns the abstraction + signature +
//  application protocol as generic functions over a domain-witness record; the
//  evaluator (reduction) stays domain-side.
//
//  Three decide-now laws are baked into the contract:
//    1. Totality   — bounded iteration only (a RepeatHole's count space is
//                    bounded; unbounded repeats are rejected, never run).
//    2. Hygiene    — holes are bound by their absolute lexical address
//                    (id-path), never a bare name, so composition cannot capture.
//    3. Effect sig — a mandatory two-axis effect/determinism class, joined
//                    componentwise through composition.
// ============================================================================

/// The hole flavours. The first three are the *data* axis — a typed value, a tree-typed
/// slot (higher-order), and a bounded repeat (the only iteration the total language permits)
/// — bound by `apply` / `curry` / `compose` with an `Arg<'Node>`. `ActionHole` is the
/// *behaviour* axis (Phase 318): a dispatch/handler slot bound, separately, by `bindHandlers`
/// with a host-supplied handler. The artifact stays a pure tree — an action hole carries no
/// handler and no `'Msg`; it declares only the *effect ceiling* of the handler that will fill
/// it. The handler (the closure) lives host-side and never travels on the wire (closures erase
/// to `<closure>`); when a host binds one, its declared effect must be `covered` by this
/// ceiling. So typed dispatch becomes "bind holes, type-checked against the signature" —
/// uniform across every host — rather than a `Node<'Msg>` type parameter.
type HoleKind =
    /// A scalar value, bound by a `ValueArg` whose string lies in the space.
    | ValueHole of ValueSpace
    /// A tree-typed slot, bound by a `SlotArg` whose kind tag equals the constraint when one is given.
    | SlotHole of kindConstraint: string option
    /// A repeat whose count is a `ValueArg` in `countSpace`; a space that is no count space
    /// (`Space.isCount`) makes the artifact non-total, so every application of it is refused `NonTotal`.
    | RepeatHole of countSpace: ValueSpace
    /// A dispatch slot bound by `bindHandlers`, never by `apply`; `effect` is the widest effect a
    /// handler bound here may declare.
    | ActionHole of effect: EffectClass

/// The hole kinds' wire tags (Phase 295) — the ONE place a kind is spelled. `SigEntry.Kind` carries
/// the tag; every reader of it goes through `HoleKind.tryOf` / `SigEntry.HoleKind` rather than
/// comparing against a literal, and the capability codec refuses a tag outside `tags`.
module HoleKind =

    /// The tag a hole kind is written as: `value`, `slot`, `repeat`, `action`.
    let tag (k: HoleKind) : string =
        match k with
        | ValueHole _ -> "value"
        | SlotHole _ -> "slot"
        | RepeatHole _ -> "repeat"
        | ActionHole _ -> "action"

    /// Every tag, in declaration order — the closed set a decoder admits.
    let tags: string list = [ "value"; "slot"; "repeat"; "action" ]

    /// The hole kind a signature entry's fields project to: its tag and the payload that tag
    /// needs — a value or repeat hole's space, a slot's constraint, an action's effect ceiling.
    /// `None` for a tag outside `tags` or a tag whose payload is absent (a `value` entry with no
    /// space, say), which only a hand-built entry can be.
    let tryOf
        (kind: string)
        (space: ValueSpace option)
        (slot: string option)
        (action: EffectClass option)
        : HoleKind option =
        match kind, space, action with
        | "value", Some s, _ -> Some(ValueHole s)
        | "slot", _, _ -> Some(SlotHole slot)
        | "repeat", Some s, _ -> Some(RepeatHole s)
        | "action", _, Some e -> Some(ActionHole e)
        | _ -> None

/// A declared hole. `Addr` is the absolute lexical address (id-path) — the hygiene
/// surface: binding is by `Addr`, never by `Name`, so two same-named holes at
/// different addresses cannot capture one another.
type HoleDecl =
    {
        /// The absolute id-path every binding keys on.
        Addr: string
        /// The human-facing label; display only, never used to bind.
        Name: string
        /// What the hole accepts, and on which axis (data or behaviour) it is bound.
        Kind: HoleKind
    }

/// A signature entry — the introspectable projection of a hole (the AiTools surface).
/// `Slot` carries a slot hole's kind-constraint (None for non-slots / unconstrained slots),
/// so the schema projection can type a slot faithfully. `Action` carries an action hole's
/// declared effect ceiling (None for non-action holes), so a host's hole-binding can be
/// effect-checked and the LLM-tool schema can advertise the dispatch slots (Phase 318).
///
/// `Required` means the hole must be BOUND. At the capability seam (`Capability.validateArgs`) a
/// bound value is a string that lies in the hole's `Space`, and no `ValueSpace` has an absent
/// marker — there is no `Null` here, unlike a `Query` cell — so a bound required hole always
/// carries a value in its space, and "bound" and "bound to a value" coincide (Phase 226, which
/// made `Required` mean non-`Null` on the query seam, where they did not).
type SigEntry =
    {
        /// The hole's absolute address; the key an argument is bound by.
        Addr: string
        /// The hole's display label; never used to bind.
        Name: string
        /// The hole-kind tag (`value`, `slot`, `repeat`, `action`); read it through `HoleKind`.
        Kind: string
        /// The space an argument must lie in: a value or repeat hole's space, `SlotTree` of the
        /// constraint for a slot, `None` for an action hole.
        Space: ValueSpace option
        /// A slot hole's kind constraint; `None` for every other kind and for an unconstrained slot.
        Slot: string option
        /// An action hole's effect ceiling; `None` for every data hole.
        Action: EffectClass option
        /// `true` for value and slot holes and a bounded repeat; `false` for an action hole, which is
        /// bound on the behaviour axis, and for an unbounded repeat, which no application accepts.
        Required: bool
    }

    /// The entry as the hole kind it projects (Phase 295; `HoleKind.tryOf`) — the typed reading of
    /// `Kind`, so no reader compares the tag against a literal.
    member e.HoleKind: HoleKind option = HoleKind.tryOf e.Kind e.Space e.Slot e.Action

/// The artifact's derived signature: which holes, what spaces, and its effect class.
type Signature =
    {
        /// The caller-chosen artifact name; `toSchema` writes it as `name`, `toJsonSchema` as `title`.
        Name: string
        /// One entry per declared hole, in the witness's declaration order.
        Holes: SigEntry list
        /// The artifact's declared (root) effect, not the observed one — `Function.auditEffect`
        /// checks the two agree.
        Effect: EffectClass
    }

/// Why a declaration is refused at the seams' admission gate (Phase 307). `Signature.validate`
/// answers the first three over a signature's entries; `Function.validate` adds the fourth, which
/// only the witness can see. The registries, the capability decoder and `Function.compose` all run
/// the same check, so there is one gate and one vocabulary for what it refuses.
type DeclarationFault =
    /// The hole's space admits no value (`Space.wellFormed` answered `SpaceFault.Empty`).
    | EmptySpace of addr: string * space: ValueSpace
    /// The hole's space has a NaN or infinite bound (`SpaceFault.NonFinite`), which no JSON number
    /// spells — so the fault carries the address alone: the space it names could not travel in it.
    | NonFiniteBound of addr: string
    /// Two holes share this address, so a binding keyed by address cannot say which one it fills;
    /// the second occurrence in declaration order is the one named.
    | DuplicateHoleAddr of addr: string
    /// A hole lies beneath a slot hole: binding the slot replaces the subtree the hole sits in, so
    /// no application can fill both. `node` is the id (`IdW.ToString`) of the node that declares
    /// the slot — the check walks subtrees, whose holes the witness addresses relative to the
    /// subtree it is handed, so a node id is the one name that means the same thing in every frame.
    | HoleUnderSlot of node: string

/// The admission refusal in words (Phase 307).
module DeclarationFault =

    /// One sentence naming the fault and, for a space, what the space says.
    let describe (f: DeclarationFault) : string =
        match f with
        | EmptySpace(addr, space) -> "hole '" + addr + "' ranges over an empty space: " + Space.describe space
        | NonFiniteBound addr -> "hole '" + addr + "' has a bound that is not a finite number"
        | DuplicateHoleAddr addr -> "two holes share the address '" + addr + "'"
        | HoleUnderSlot node -> "the slot declared at node '" + node + "' has a hole beneath it"

/// An argument bound into a hole: a leaf value, or a tree (for slots / composition).
type Arg<'Node> =
    /// A scalar for a value or repeat hole, checked against the hole's space before binding.
    | ValueArg of string
    /// A tree for a slot hole; any other hole kind refuses it `NotASlot`.
    | SlotArg of 'Node

/// Application failure — total, and (per the envelope discipline) it names what was
/// expected.
type ApplyError =
    /// An argument (or a compose target) addresses no declared hole; `declared` lists the addresses
    /// that were open to it — action holes excluded on the data axis.
    | UnknownHoleAddr of addr: string * declared: string list
    /// A value or repeat argument `got` lies outside its hole's `space`.
    | ValueOutOfSpace of addr: string * space: ValueSpace * got: string
    /// A full application left these holes unbound, in declaration order.
    | RequiredHolesUnbound of addrs: string list
    /// The argument's shape does not fit the hole: a tree into a value or repeat hole, a scalar into
    /// a slot, or a compose target that is not a slot.
    | NotASlot of addr: string
    /// A slot's kind constraint `expected` refused a tree whose kind tag is `got`.
    | SlotKindMismatch of addr: string * expected: string * got: string
    /// A repeat hole ranges over a space that is no count space (`Space.isCount`): unbounded,
    /// empty, uncapped, or not an integer range. Checked before any argument is read.
    | NonTotal of addr: string
    /// The witness's `Bind` refused the binding; `reason` is its message, verbatim.
    | BindFailed of addr: string * reason: string
    /// A strict application was handed a slot argument that still has open data holes (Phase 307):
    /// `holes` are their addresses, as the witness enumerates them over the argument. A full
    /// application yields a closed tree, so it refuses an open one rather than returning a result
    /// with holes nobody bound. Partial application (`curry`) still accepts one.
    | SlotArgOpen of addr: string * holes: string list
    /// `compose` built a tree that is not a well-formed declaration (Phase 307) — an inner hole
    /// landing on an address the outer already uses is the case it exists for.
    | IllFormedResult of fault: DeclarationFault

// ---- the behaviour axis: typed handler-table binding (Phase 318) ----

/// A host-supplied handler bound to an action hole. The core treats the handler as opaque
/// (`'Handler` — in F# typically `unit -> 'Msg` or `'Event -> 'Msg`; in a generated C#/VB
/// host the typed delegate the binding surface declares): it is the *behavioural sidecar*
/// that never travels on the wire (closures erase). `Effect` is the handler's declared effect,
/// checked against its action hole's declared ceiling so a bound handler can never do more
/// than the artifact declared.
type HandlerBinding<'Handler> =
    {
        /// The host's handler value; opaque to the core and never serialised.
        Handler: 'Handler
        /// What the handler declares it does; it must be covered by its action hole's ceiling.
        Effect: EffectClass
    }

/// The validated behavioural sidecar — a typed handler table bound to an artifact's action
/// holes by absolute address (hygiene). The artifact tree itself is unchanged (it stays pure,
/// `Node<unit>`-equivalent); this is the host-side companion that supplies dispatch. Producing
/// it is the whole "typed dispatch = bind holes, type-checked against the signature" move.
type HandlerTable<'Handler> =
    {
        /// Keyed by action-hole address: exactly the artifact's action holes, each within its ceiling.
        Handlers: Map<string, HandlerBinding<'Handler>>
    }

/// Why a handler-table binding was refused — total, and (per the envelope discipline) it names
/// the failure and, where a closed set is expected, enumerates the alternatives (GP5).
/// Default-deny by shape: only a declared action hole accepts a handler, and only a handler
/// within the declared effect ceiling binds.
type BindHandlerError =
    /// A handler key addresses no hole at all; `declaredActions` lists the action-hole addresses.
    | UnknownActionAddr of addr: string * declaredActions: string list
    /// A handler key addresses a declared data hole (value, slot or repeat), which takes no handler.
    | NotAnActionHole of addr: string
    /// These action holes received no handler, in declaration order.
    | RequiredActionsUnbound of addrs: string list
    /// The handler's declared effect is not covered by the hole's ceiling on one axis or both.
    | HandlerEffectExceedsCeiling of addr: string * ceiling: EffectClass * handler: EffectClass

/// The domain-witness record the generic functions take. The witness never exposes a
/// concrete `NodeKind`; it exposes accessors. `Bind` is the apply-op emitter — it
/// lowers a hole binding to the domain's own op (SetInput / SetParameter / SetVariable
/// / fragment expansion), returning the re-derived tree.
type ArtifactWitness<'Node, 'Id> =
    {
        /// The domain's tree accessors; the protocol reads a slot argument's kind tag and walks
        /// subtrees through it.
        Tree: NodeWitness<'Node, 'Id>
        /// The domain's id witness. `Function.validate` reads it to name the node a `HoleUnderSlot`
        /// fault is at (Phase 307); nothing else in `Function` does.
        IdW: IdWitness<'Id>
        /// Enumerate the declared holes of a tree, each with its absolute lexical address.
        Holes: 'Node -> HoleDecl list
        /// The artifact's declared effect class.
        Effect: 'Node -> EffectClass
        /// Bind hole@addr := arg, lowering to the domain's apply-op. Total.
        Bind: string -> Arg<'Node> -> 'Node -> Result<'Node, string>
    }

/// A caller-supplied application memo (Phase 49) — the witness pattern applied to caching (GP2: no
/// module-level mutable / global state; the host owns its lifetime and threads it through
/// `Function.applyMemo`). Keys a re-derived result tree by `(function content-hash, canonicalised
/// param-set)`. The counters make the "only the affected subtree re-derives" property observable: a
/// `Hit` means an unchanged sub-function was served from the cache rather than re-applied; a `Miss`
/// computed + stored a fresh result; a `Bypass` means a non-memoisable (effecting / non-deterministic)
/// function was computed directly and never cached (the soundness guard — Fork 3).
///
/// **A cache is PER WITNESS (Phase 307).** The key is the content pre-image of the function and its
/// arguments under the caller's `encode`, and it carries no tag for the witness that applied them:
/// two witnesses over one node type whose `Bind` lowers the same binding differently would read
/// each other's entries. Thread one cache through one witness's applications, as every caller in
/// this package does.
type MemoCache<'Node> =
    {
        /// The result trees, keyed by the full `(function, param-set)` pre-image, not a digest of it.
        Entries: Map<string, 'Node>
        /// Applications served from `Entries` without re-deriving.
        Hits: int
        /// Memoisable applications computed and stored; a refused application counts nowhere.
        Misses: int
        /// Applications computed directly because the observed effect was not pure and deterministic.
        Bypasses: int
    }

/// Companion helpers for `MemoCache` — the empty memo, its size, and the memoisability predicate.
module Memo =

    /// The empty memo.
    let empty: MemoCache<'Node> =
        { Entries = Map.empty
          Hits = 0
          Misses = 0
          Bypasses = 0 }

    /// The number of distinct cached result trees.
    let count (c: MemoCache<'Node>) : int = Map.count c.Entries

    /// The soundness predicate (Phase 49 — Fork 3 + host purity): a function may be served from / stored
    /// in the memo only when its effect is fully pure & deterministic. A non-`Deterministic` source
    /// (clock / random / network) would make a cached result stale (replay must re-read the live source,
    /// not a snapshot), and a host effect (reads / writes) would be silently skipped on a hit — so
    /// neither is ever memoised. Equivalent to `e = Effect.pureDeterministic`, written as the explicit
    /// two-axis check. As of Phase 53 `applyMemo` feeds this the **observed** effect
    /// (`Function.observedEffect` — the widest effect over the whole subtree), not the declared root, so
    /// the cache stays sound even when a root *under-declares* an impure descendant; combined with the
    /// artifact contract's Fork-1 totality, a memoisable function's result is reproducible from
    /// `(function-hash, param-set)` alone.
    let isMemoisable (e: EffectClass) : bool =
        e.Host = Pure && Set.isEmpty e.Determinism

/// The admission check over a signature's entries (Phase 307).
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Signature =

    /// The first reason the signature is not a well-formed declaration, in declaration order, or
    /// `Ok ()`: an entry whose space `Space.wellFormed` refuses (`EmptySpace`, `NonFiniteBound`),
    /// or an entry whose address an earlier entry already holds (`DuplicateHoleAddr`). Totality is
    /// the registries' other admission check (`Function.isTotal`, refused `NonTotalCapability`);
    /// the two together are the gate `CapabilityRegistry.register`, `FunctionRegistry.register`
    /// and `CapabilityCodec`'s readers run, so a declaration a seam admits has distinct addresses
    /// and spaces a value can lie in and JSON can spell — and `Function.toJsonSchema` over it has
    /// distinct property keys and finite bounds.
    let validate (sg: Signature) : Result<unit, DeclarationFault> =
        let rec go (seen: Set<string>) =
            function
            | [] -> Ok()
            | (e: SigEntry) :: rest ->
                if seen.Contains e.Addr then
                    Error(DuplicateHoleAddr e.Addr)
                else
                    match e.Space |> Option.map (fun sp -> sp, Space.wellFormed sp) with
                    | Some(sp, Error SpaceFault.Empty) -> Error(EmptySpace(e.Addr, sp))
                    | Some(_, Error SpaceFault.NonFinite) -> Error(NonFiniteBound e.Addr)
                    | _ -> go (seen.Add e.Addr) rest

        go Set.empty sg.Holes

/// The generic artifact-function operations — `signature` / `apply` / `curry` /
/// `compose` — over the witness, under the three laws. UI's parameterised fragment and
/// Calc's parameterised model both express their `apply` through these.
module Function =

    /// Derive the introspectable signature from a tree.
    let signature (w: ArtifactWitness<'Node, 'Id>) (name: string) (node: 'Node) : Signature =
        let entry (h: HoleDecl) =
            let space, slot, action, required =
                match h.Kind with
                | ValueHole s -> Some s, None, None, true
                // Phase 229: a slot is entered WITH its value space — a wire tree of the constrained
                // kind — so a capability over a slotted artifact is invocable at the scalar seam.
                | SlotHole c -> Some(SlotTree c), c, None, true
                // Phase 295: a TOTAL repeat is required, because strict `apply` demands it — the
                // signature and the artifact protocol answer one question the same way. A repeat
                // over anything but a count space (`Space.isCount`, Phase 307) is non-total: `apply`
                // refuses it `NonTotal` whatever is bound, and `CapabilityRegistry.register` refuses
                // a capability over one.
                | RepeatHole s -> Some s, None, None, Space.isCount s
                // An action hole is non-required on the *data* binding axis (no value/slot arg fills it);
                // it is bound on the *behaviour* axis by `bindHandlers`, which enforces its own coverage.
                | ActionHole e -> None, None, Some e, false

            { Addr = h.Addr
              Name = h.Name
              Kind = HoleKind.tag h.Kind
              Space = space
              Slot = slot
              Action = action
              Required = required }

        { Name = name
          Holes = w.Holes node |> List.map entry
          Effect = w.Effect node }

    /// Drop the given (already-bound) hole addresses from a signature (Phase 24) — for projecting a
    /// curried artifact whose domain `Bind` does not itself clear bound holes from `w.Holes`. A
    /// witness whose `Bind` clears the hole already gets a narrowed `signature` for free (re-deriving
    /// `signature` over the curried tree omits the bound addresses); this is the explicit path for
    /// witnesses that don't, so `toSchema` / `toJsonSchema` over the result advertise only the
    /// still-open holes (both `properties` and `required`). Hygiene: addresses, never bare names.
    let signatureExcluding (boundAddrs: Set<string>) (sg: Signature) : Signature =
        { sg with
            Holes = sg.Holes |> List.filter (fun e -> not (boundAddrs.Contains e.Addr)) }

    /// One entry's half of the totality law: a repeat hole ranges over a count space, and the entry
    /// is a hole kind. What `isTotal` asks of every entry and `Capability`'s admission gate asks of
    /// each, to name the ones that fail.
    let internal isTotalEntry (e: SigEntry) : bool =
        match e.HoleKind with
        | Some(RepeatHole s) -> Space.isCount s
        | Some _ -> true
        // An entry that projects to no hole kind — an unknown tag, or a tag without the payload
        // it needs, which only a hand-built entry can be — is not certified total (Phase 295).
        | None -> false

    /// Totality law: every repeat hole ranges over a count space (`Space.isCount` — a capped,
    /// non-empty, non-negative `IntRange`, Phase 307), and every entry is a hole kind
    /// (`SigEntry.HoleKind`).
    let isTotal (sg: Signature) : bool = sg.Holes |> List.forall isTotalEntry

    /// The data holes of a tree — every declared hole but the action holes, which `bindHandlers`
    /// fills on the behaviour axis.
    let private dataHoles (w: ArtifactWitness<'Node, 'Id>) (node: 'Node) : HoleDecl list =
        w.Holes node
        |> List.filter (fun h ->
            match h.Kind with
            | ActionHole _ -> false
            | _ -> true)

    /// Is the tree CLOSED (Phase 307) — does it declare no data hole? An action hole does not open a
    /// tree: it is bound on the behaviour axis, after application. Strict `apply` answers a closed
    /// tree when every slot argument it was handed is closed (`SlotArgOpen` otherwise).
    let isClosed (w: ArtifactWitness<'Node, 'Id>) (node: 'Node) : bool = List.isEmpty (dataHoles w node)

    /// The number of slot holes among `hs`.
    let private slotCount (hs: HoleDecl list) : int =
        hs
        |> List.filter (fun h ->
            match h.Kind with
            | SlotHole _ -> true
            | _ -> false)
        |> List.length

    /// The first node, in preorder, that declares a slot hole and has a hole beneath it. The witness
    /// enumerates the holes of any subtree it is handed, addressed relative to that subtree, so the
    /// check COUNTS and never compares addresses or names (a name is inert by the hygiene law): a
    /// node declares a slot of its own when its subtree holds more slots than its children's
    /// subtrees do, and has a hole beneath it when its children's subtrees hold any hole at all.
    let private holeUnderSlot (w: ArtifactWitness<'Node, 'Id>) (node: 'Node) : DeclarationFault option =
        Tree.preorder w.Tree node
        |> List.tryPick (fun n ->
            let below = w.Tree.Children n |> List.map w.Holes

            if
                List.exists (List.isEmpty >> not) below
                && slotCount (w.Holes n) > List.sumBy slotCount below
            then
                Some(HoleUnderSlot(w.IdW.ToString(w.Tree.Id n)))
            else
                None)

    /// The admission check over an ARTIFACT (Phase 307): `Signature.validate` over its derived
    /// signature, then the check only the witness can make — no hole lies beneath a slot hole
    /// (`HoleUnderSlot`). `Ok ()` is a declaration whose holes have distinct addresses, spaces a
    /// value can lie in, and no binding that another binding erases. `compose` runs it over the
    /// tree it builds; a host runs it over an artifact before deriving the capability it registers.
    let validate (w: ArtifactWitness<'Node, 'Id>) (node: 'Node) : Result<unit, DeclarationFault> =
        Signature.validate (signature w "" node)
        |> Result.bind (fun () ->
            match holeUnderSlot w node with
            | Some fault -> Error fault
            | None -> Ok())

    /// First totality violation among a hole set, if any.
    let private guardTotal (holes: HoleDecl list) : ApplyError option =
        holes
        |> List.tryPick (fun h ->
            match h.Kind with
            | RepeatHole s when not (Space.isCount s) -> Some(NonTotal h.Addr)
            | _ -> None)

    /// Validate an argument against a hole kind before lowering it.
    let private validateArg
        (w: ArtifactWitness<'Node, 'Id>)
        (addr: string)
        (kind: HoleKind)
        (arg: Arg<'Node>)
        : Result<unit, ApplyError> =
        match kind, arg with
        | ValueHole space, ValueArg s ->
            if Space.validate space s then
                Ok()
            else
                Error(ValueOutOfSpace(addr, space, s))
        | RepeatHole space, ValueArg s ->
            if not (Space.isCount space) then Error(NonTotal addr)
            elif Space.validate space s then Ok()
            else Error(ValueOutOfSpace(addr, space, s))
        | SlotHole constraintOpt, SlotArg node ->
            match constraintOpt with
            | Some k when w.Tree.KindTag node <> k -> Error(SlotKindMismatch(addr, k, w.Tree.KindTag node))
            | _ -> Ok()
        | (ValueHole _ | RepeatHole _), SlotArg _ -> Error(NotASlot addr)
        | SlotHole _, ValueArg _ -> Error(NotASlot addr)
        // Defensive: action holes live on the behaviour axis and are filtered out before `bindArgs`
        // reaches `validateArg`, so no data arg ever targets one — refuse if one slips through.
        | ActionHole _, _ -> Error(NotASlot addr)

    /// Bind the provided args into the tree, by absolute address (hygiene). `strict`
    /// requires every declared hole to be bound (full application); non-strict leaves
    /// unbound holes in place (partial application). Shared by `apply` and `curry`.
    let private bindArgs
        (strict: bool)
        (w: ArtifactWitness<'Node, 'Id>)
        (args: Map<string, Arg<'Node>>)
        (node: 'Node)
        : Result<'Node, ApplyError> =
        // Action holes live on the *behaviour* axis (bound by `bindHandlers`), not the data axis —
        // exclude them here so the artifact stays apply-able and a strict apply does not demand a
        // value/slot arg for a dispatch slot. The tree itself remains pure regardless.
        let holes = dataHoles w node

        match guardTotal holes with
        | Some e -> Error e
        | None ->
            let declared = holes |> List.map _.Addr

            // Reject args that don't address a declared hole.
            match
                args
                |> Map.toList
                |> List.map fst
                |> List.tryFind (fun a -> not (List.contains a declared))
            with
            | Some unknown -> Error(UnknownHoleAddr(unknown, declared))
            | None ->
                // Fold over holes in declaration order, threading the (re-derived) tree.
                let rec go (cur: 'Node) (unbound: string list) =
                    function
                    | [] ->
                        if strict && not (List.isEmpty unbound) then
                            Error(RequiredHolesUnbound(List.rev unbound))
                        else
                            Ok cur
                    | (h: HoleDecl) :: rest ->
                        match Map.tryFind h.Addr args with
                        | None -> go cur (h.Addr :: unbound) rest
                        | Some arg ->
                            match validateArg w h.Addr h.Kind arg with
                            | Error e -> Error e
                            | Ok() ->
                                match w.Bind h.Addr arg cur with
                                | Ok cur' -> go cur' unbound rest
                                | Error m -> Error(BindFailed(h.Addr, m))

                // Phase 307: a full application binds closed trees only — the first slot argument,
                // in address order, that still has open data holes is refused before any binding.
                let openSlot =
                    if not strict then
                        None
                    else
                        args
                        |> Map.toList
                        |> List.tryPick (fun (a, arg) ->
                            match arg with
                            | SlotArg sub ->
                                match dataHoles w sub with
                                | [] -> None
                                | hs -> Some(SlotArgOpen(a, hs |> List.map _.Addr))
                            | ValueArg _ -> None)

                match openSlot with
                | Some e -> Error e
                | None -> go node [] holes

    /// Apply the artifact-function to a full argument set — every declared hole must be
    /// bound, and every slot argument must be closed (`isClosed`; `SlotArgOpen` otherwise, Phase
    /// 307), so an `Ok` result has no open data hole the arguments put there. Hygiene: args are
    /// keyed by absolute address.
    let apply
        (w: ArtifactWitness<'Node, 'Id>)
        (args: Map<string, Arg<'Node>>)
        (node: 'Node)
        : Result<'Node, ApplyError> =
        bindArgs true w args node

    /// Partial application — bind a subset of holes, returning a narrower artifact
    /// (the content-pack formalism). The remaining holes stay open.
    let curry
        (w: ArtifactWitness<'Node, 'Id>)
        (args: Map<string, Arg<'Node>>)
        (node: 'Node)
        : Result<'Node, ApplyError> =
        bindArgs false w args node

    /// The effect class of composing `inner` into `outer` — the join law, enforced and
    /// never optional.
    let composedEffect (w: ArtifactWitness<'Node, 'Id>) (inner: 'Node) (outer: 'Node) : EffectClass =
        Effect.join (w.Effect outer) (w.Effect inner)

    /// Compose: wire `inner`'s tree into `outer`'s tree-typed slot at `slotAddr`. The
    /// slot's kind constraint (if any) is checked; the result's effect is the join. Totality is
    /// checked first, on both parts, exactly as `composeAcross` checks it (Phase 295): a part
    /// carrying a repeat over no count space makes the composed function non-total, so the
    /// composition is refused `NonTotal`, never built. And the RESULT is checked (Phase 307): a
    /// composed tree `validate` refuses — an inner hole on an address the outer already uses, which
    /// would let one argument fill both — is refused `IllFormedResult`, never returned.
    let compose
        (w: ArtifactWitness<'Node, 'Id>)
        (slotAddr: string)
        (inner: 'Node)
        (outer: 'Node)
        : Result<'Node, ApplyError> =
        let holes = w.Holes outer

        match guardTotal holes |> Option.orElse (guardTotal (w.Holes inner)) with
        | Some e -> Error e
        | None ->
            match holes |> List.tryFind (fun h -> h.Addr = slotAddr) with
            | None -> Error(UnknownHoleAddr(slotAddr, holes |> List.map _.Addr))
            | Some h ->
                match h.Kind with
                | SlotHole constraintOpt ->
                    match constraintOpt with
                    | Some k when w.Tree.KindTag inner <> k ->
                        Error(SlotKindMismatch(slotAddr, k, w.Tree.KindTag inner))
                    | _ ->
                        match w.Bind slotAddr (SlotArg inner) outer with
                        | Ok n ->
                            match validate w n with
                            | Ok() -> Ok n
                            | Error fault -> Error(IllFormedResult fault)
                        | Error m -> Error(BindFailed(slotAddr, m))
                | _ -> Error(NotASlot slotAddr)

    /// The effect class of composing a `'B`-witness `inner` ACROSS the boundary into a
    /// `'A`-witness `outer` (Phase 47) — the join law carried across heterogeneous witnesses:
    /// the composed function's effect = the componentwise join of its two parts' (Fork 3). The
    /// cross-witness analogue of `composedEffect`; surfaced as a function (not a witness field)
    /// so the join is recomputable on demand without `'A` having to declare it.
    let composedEffectAcross
        (wa: ArtifactWitness<'A, 'IdA>)
        (wb: ArtifactWitness<'B, 'IdB>)
        (inner: 'B)
        (outer: 'A)
        : EffectClass =
        Effect.join (wa.Effect outer) (wb.Effect inner)

    /// Compose ACROSS witnesses (Phase 47) — the higher-order, heterogeneous step: wire a
    /// `'B`-witness artifact-function's output into the typed slot of an `'A`-witness
    /// artifact-function, yielding ONE composed `'A` artifact-function of the combined signature.
    /// `compose` wires within a single witness; this is the "trees into holes ACROSS
    /// domains" move — an app-function whose slot binds a UI-function whose slot binds a
    /// data-function, as one typed, replayable, verifiable artifact.
    ///
    /// The two witnesses + the cross-witness slot binding ride as PER-CALL PARAMETERS (GP2),
    /// never a new witness field: `wb` is the inner witness, and `embed : 'B -> 'A` lifts the
    /// inner's output into a node the outer's slot can hold — the one genuinely cross-domain step
    /// (how a data subtree becomes a UI node), supplied by the host, not assumed by Core (D5: the
    /// core mints nothing and assumes no embedding; the domain owns it).
    ///
    /// The three Forks carry across the boundary:
    ///   • Fork 1 (totality)  — rejected, never run, if EITHER part carries an unbounded repeat
    ///                          hole, since the combined function's holes are the union of the
    ///                          two parts' and a single unbounded repeat makes that union non-total.
    ///   • Fork 2 (hygiene)   — the slot is bound by its absolute lexical address, never a name;
    ///                          the embedded inner's holes re-root UNDER that address, so two
    ///                          cross-witness compositions into distinct slots cannot capture.
    ///   • Fork 3 (effect)    — surfaced via `composedEffectAcross` (the componentwise join).
    ///
    /// The composed result is checked exactly as `compose` checks its own (Phases 307, 383): a tree
    /// `validate` refuses under `wa` — an embedded hole on an address the outer already uses — is
    /// refused `IllFormedResult`, never returned.
    let composeAcross
        (wa: ArtifactWitness<'A, 'IdA>)
        (wb: ArtifactWitness<'B, 'IdB>)
        (embed: 'B -> 'A)
        (slotAddr: string)
        (inner: 'B)
        (outer: 'A)
        : Result<'A, ApplyError> =
        // Fork 1 — totality across the boundary: a part carrying an unbounded repeat would make
        // the combined function non-total, so reject it (never run) rather than compose.
        match guardTotal (wa.Holes outer) with
        | Some e -> Error e
        | None ->
            match guardTotal (wb.Holes inner) with
            | Some e -> Error e
            | None ->
                let holes = wa.Holes outer

                match holes |> List.tryFind (fun h -> h.Addr = slotAddr) with
                | None -> Error(UnknownHoleAddr(slotAddr, holes |> List.map _.Addr))
                | Some h ->
                    match h.Kind with
                    | SlotHole constraintOpt ->
                        // the cross-witness slot binding: lift the inner B-output into an A-node.
                        let embedded = embed inner

                        match constraintOpt with
                        | Some k when wa.Tree.KindTag embedded <> k ->
                            Error(SlotKindMismatch(slotAddr, k, wa.Tree.KindTag embedded))
                        | _ ->
                            // Fork 2 — bind by absolute address; the inner's holes re-root under it.
                            // And the RESULT is checked, as `compose` checks it (Phase 383): a
                            // composed tree `validate` refuses is `IllFormedResult`, never returned.
                            match wa.Bind slotAddr (SlotArg embedded) outer with
                            | Ok n ->
                                match validate wa n with
                                | Ok() -> Ok n
                                | Error fault -> Error(IllFormedResult fault)
                            | Error m -> Error(BindFailed(slotAddr, m))
                    | _ -> Error(NotASlot slotAddr)

    /// Bind a typed handler table to the artifact's declared action holes, validated against the
    /// signature (Phase 318) — the behaviour-axis analogue of `apply`. The artifact tree is never
    /// touched: it stays a pure `Node<unit>`-equivalent; `bindHandlers` returns the validated
    /// host-side sidecar. Three checks, default-deny by shape:
    ///   1. every handler key addresses a *declared action hole* (a non-action hole is `NotAnActionHole`;
    ///      an unknown address is `UnknownActionAddr`, enumerating the declared actions);
    ///   2. each bound handler's declared effect is `covered` by its hole's declared ceiling
    ///      (`HandlerEffectExceedsCeiling` names both) — a handler can never out-effect the artifact;
    ///   3. every action hole is bound (an unbound dispatch slot is a dead control — `RequiredActionsUnbound`).
    /// The *message typing* (`'Msg`) is NOT checked here — it lives in the host language: in F# the
    /// `'Handler` map is statically typed at the binding site; a generated C#/VB host emits a typed
    /// hole-binding surface (one typed parameter per action hole) from this same signature. So there is
    /// no per-language typed-dispatch special case — the signature is the single source.
    let bindHandlers
        (w: ArtifactWitness<'Node, 'Id>)
        (handlers: Map<string, HandlerBinding<'Handler>>)
        (node: 'Node)
        : Result<HandlerTable<'Handler>, BindHandlerError> =
        let allAddrs = w.Holes node |> List.map _.Addr

        let actionHoles =
            w.Holes node
            |> List.choose (fun h ->
                match h.Kind with
                | ActionHole eff -> Some(h.Addr, eff)
                | _ -> None)

        let actionAddrs = actionHoles |> List.map fst

        // 1. every handler key addresses a declared action hole.
        let rec checkKeys =
            function
            | [] -> Ok()
            | (addr, _) :: rest ->
                if List.contains addr actionAddrs then
                    checkKeys rest
                elif List.contains addr allAddrs then
                    Error(NotAnActionHole addr)
                else
                    Error(UnknownActionAddr(addr, actionAddrs))

        checkKeys (Map.toList handlers)
        |> Result.bind (fun () ->
            // 2. each bound handler's effect is covered by its hole's declared ceiling.
            let rec checkEffects =
                function
                | [] -> Ok()
                | (addr, ceiling) :: rest ->
                    match Map.tryFind addr handlers with
                    | Some hb when not (Effect.covers ceiling hb.Effect) ->
                        Error(HandlerEffectExceedsCeiling(addr, ceiling, hb.Effect))
                    | _ -> checkEffects rest

            checkEffects actionHoles)
        |> Result.bind (fun () ->
            // 3. every action hole is bound (default-deny by shape).
            let unbound = actionAddrs |> List.filter (fun a -> not (Map.containsKey a handlers))

            if List.isEmpty unbound then
                Ok { Handlers = handlers }
            else
                Error(RequiredActionsUnbound unbound))

    /// The widest effect ACTUALLY present in the artifact subtree — the componentwise join of every
    /// node's declared `EffectClass` over a preorder walk (`Effect.pureDeterministic` is the join
    /// identity). This is the *observed* effect: it sees past a root that under-declares, because it
    /// walks every descendant. `auditEffect` compares the declared root against it, and `applyMemo`
    /// (Phase 53) gates memoisation on it — keying on what the function *actually does*, never on a
    /// root declaration that may lie.
    let observedEffect (w: ArtifactWitness<'Node, 'Id>) (node: 'Node) : EffectClass =
        Tree.preorder w.Tree node
        |> List.map w.Effect
        |> List.fold Effect.join Effect.pureDeterministic

    /// Effect-soundness audit (fuaran#248) — the teeth on the mandatory effect signature.
    /// Walk the artifact subtree (`observedEffect`), join every node's declared `EffectClass`
    /// componentwise into the *actual* effect, and verify the artifact's top-level declared class
    /// `covers` it. `Ok` when the declaration is at least as wide as the truth on both
    /// axes; `Error (declared, actual)` names both when a descendant leaks an effect the
    /// top under-declares (exactly how impurity slips in past a `Pure`-declared artifact).
    let auditEffect (w: ArtifactWitness<'Node, 'Id>) (node: 'Node) : Result<unit, EffectClass * EffectClass> =
        let declared = w.Effect node
        let actual = observedEffect w node

        if Effect.covers declared actual then
            Ok()
        else
            Error(declared, actual)

    // ---- THE TAG CONVENTION (Phase 251) ----
    // Three JSON spellings leave this package and its `Query` sibling, one per kind of artefact,
    // and the choice is by artefact, never by taste:
    //
    //   * A WIRE DOCUMENT — something a codec encodes and decodes back (`CapabilityCodec`,
    //     `QueryCodec`, `CapabilityPipeline.encode`: declarations, invocations, the `Deferred`
    //     envelope, the typed refusals) — is discriminated by `"$type"` (`Canon.typed`), the
    //     DU-position convention: `"$type"` sorts first under `Canon.render`, and a reader dispatches
    //     on it.
    //   * A DESCRIPTOR in the substrate's own vocabulary that is read and never decoded back —
    //     `toSchema` below — is tagged by `"kind"` (`Json.kindObj`), the convention of every
    //     domain node and op on the tree wire, which is what such a descriptor is read beside.
    //   * A STANDARD JSON SCHEMA for a model — `Function.toJsonSchema`, `Query.toJsonSchema` — carries
    //     no tag at all: it is JSON Schema, so it says `"type": "object"`, and everything the
    //     standard has no keyword for (the effect class, action holes, a query's result row) rides
    //     under an `x-` key OUTSIDE `properties`, so it never reads as an argument to fill.

    // ---- signature → JSON tool-schema projection (fuaran#247) ----
    // The value space is written in its frozen descriptor spelling (`SpaceCodec.descriptorJson`)
    // and the effect by `EffectCodec` (Phase 295): one writer each, shared with the codecs.

    /// A slot entry's space is DERIVED from its `Slot` constraint (Phase 229) — `signature` enters
    /// every `SlotHole` as `Some(SlotTree c)` beside `Slot = c` — so the wire projections omit it,
    /// and a pre-229 slot entry and a post-229 one project to the same bytes (and the same
    /// `signatureFingerprint`). Only a space that says something the entry does not is written.
    let internal derivedSlotSpace (e: SigEntry) : bool =
        match e.HoleKind with
        | Some(SlotHole c) -> e.Space = Some(SlotTree c)
        | _ -> false

    /// An entry's space with a slot's derived space filled in — so an entry built by hand before
    /// Phase 229 (a spaceless slot) and one `signature` derives compare equal where shape matters.
    let internal slotSpaceOf (e: SigEntry) : ValueSpace option =
        match e.HoleKind, e.Space with
        | Some(SlotHole c), None -> Some(SlotTree c)
        | _, sp -> sp

    let private entryJson (e: SigEntry) : JVal =
        // base fields always present; the constraint fields appear only when they apply
        // (the wire JVal model has no null — absence is omission, not a null field).
        [ "addr", JStr e.Addr
          "name", JStr e.Name
          "kind", JStr e.Kind
          "required", JBool e.Required ]
        @ (match e.Space with
           | Some _ when derivedSlotSpace e -> []
           | Some s -> [ "space", SpaceCodec.descriptorJson s ]
           | None -> [])
        @ (match e.Slot with
           | Some k -> [ "slotKind", JStr k ]
           | None -> [])
        @ (match e.Action with
           | Some eff -> [ "actionEffect", EffectCodec.toJson eff ]
           | None -> [])
        |> JObj

    /// Project a derived signature into a canonical `"kind":"signature"` JSON object
    /// (fuaran#247) — the generic artifact-function-as-tool schema. Each hole becomes a
    /// typed property (value-space → min/max/enum/length constraints, slot → its
    /// kind-constraint), the two-axis effect class travels alongside, and the required
    /// addresses are listed — so the orchestrator-facing tool descriptor is produced once
    /// in the core, not re-derived per domain. `Json.render` of the result is canonical +
    /// stable for a fixed signature.
    let toSchema (sg: Signature) : JVal =
        Json.kindObj
            "signature"
            [ "name", JStr sg.Name
              "effect", EffectCodec.toJson sg.Effect
              "holes", JArr(sg.Holes |> List.map entryJson)
              "required", JArr(sg.Holes |> List.filter _.Required |> List.map (fun h -> JStr h.Addr)) ]

    // ---- signature → standard JSON Schema projection ----

    /// The standard JSON-Schema keywords for a value space.
    let private spaceSchema (s: ValueSpace) : (string * JVal) list =
        match s with
        | IntRange(lo, hi) -> [ "type", JStr "integer"; "minimum", JInt lo; "maximum", JInt hi ]
        | FloatRange(lo, hi) -> [ "type", JStr "number"; "minimum", JFloat lo; "maximum", JFloat hi ]
        | StringLen(lo, hi) -> [ "type", JStr "string"; "minLength", JInt lo; "maxLength", JInt hi ]
        | Enum xs -> [ "enum", JArr(xs |> List.map JStr) ]
        | AnyString -> [ "type", JStr "string" ]
        | SlotTree c ->
            [ "type", JStr "object" ]
            @ (match c with
               | Some k -> [ "description", JStr("slot of kind: " + k) ]
               | None -> [])

    /// The JSON-Schema property for one signature entry. A slot hole projects an `object`
    /// carrying its kind-constraint in `description` (a tree-typed argument has no scalar JSON
    /// type); a value / repeat hole projects its value-space keywords.
    let private propSchema (e: SigEntry) : JVal =
        match e.HoleKind with
        | Some(SlotHole c) ->
            match c with
            | Some k -> JObj [ "type", JStr "object"; "description", JStr("slot of kind: " + k) ]
            | None -> JObj [ "type", JStr "object" ]
        | _ ->
            match e.Space with
            | Some s -> JObj(spaceSchema s)
            | None -> JObj [ "type", JStr "string" ] // defensive: value/repeat always carry a space

    /// The `x-actions` entry for one action hole — addr, name, and the declared effect ceiling.
    let private actionEntryJson (e: SigEntry) : JVal =
        [ "addr", JStr e.Addr; "name", JStr e.Name ]
        @ (match e.Action with
           | Some eff -> [ "effect", EffectCodec.toJson eff ]
           | None -> [])
        |> JObj

    /// Project a derived signature into a STANDARD JSON Schema `object` — the shape
    /// LLM tool-use APIs consume, so a saved artifact-function is a directly-registerable tool
    /// without per-orchestrator translation of `toSchema`'s bespoke shape. Each *data* hole
    /// (value / slot / repeat) becomes a property keyed by its absolute address (hygiene —
    /// addresses, never bare names); the required ones (value + slot + bounded repeat — Phase 295) are
    /// listed; the two-axis effect class travels alongside as `x-effect`, OUTSIDE the parameter
    /// schema so it never pollutes the argument properties. Action holes (Phase 318) are NOT
    /// arguments the LLM fills — they are the *host's* hole-binding surface (a human binds a
    /// handler) — so they travel under `x-actions`, also OUTSIDE `properties`/`required`. This is
    /// the trust split made wire-visible: the parameter schema is the inert structure the AI
    /// emits; `x-actions` is the behaviour a human binds. `x-actions` is omitted entirely when
    /// the artifact declares no action holes, so a data-only signature is byte-identical to
    /// before. `Json.render` of the result is canonical + stable for a fixed signature.
    let toJsonSchema (sg: Signature) : JVal =
        let isAction (e: SigEntry) =
            match e.HoleKind with
            | Some(ActionHole _) -> true
            | _ -> false

        let dataHoles = sg.Holes |> List.filter (isAction >> not)
        let actionHoles = sg.Holes |> List.filter isAction

        JObj(
            [ "type", JStr "object"
              "title", JStr sg.Name
              "x-effect", EffectCodec.toJson sg.Effect
              "properties", JObj(dataHoles |> List.map (fun e -> e.Addr, propSchema e))
              "required", JArr(dataHoles |> List.filter _.Required |> List.map (fun h -> JStr h.Addr)) ]
            @ (match actionHoles with
               | [] -> []
               | _ -> [ "x-actions", JArr(actionHoles |> List.map actionEntryJson) ])
        )

    // ---- memoised application (Phase 49) ----
    // A pure, total artifact-function over typed value-spaces has a well-defined content-addressed key
    // `(function-identity, param-set) -> result-tree`. Memoise application against it so unchanged
    // subtrees are not re-derived (fast regeneration at scale), and so op-stream replay collapses into a
    // special case of re-application (replay = apply the recorded ops, served from the same cache —
    // exposed via `OpStream.replay` folding a memo-carrying state, which needs NO change to `OpStream`:
    // the replay seam is already generic over `(apply, encode, decode)`, and `OpStream` deliberately
    // takes no `Function` dependency). Additive over the frozen witness (GP1/GP7); the cache is
    // caller-supplied (GP2 — no global state).

    /// Canonicalise a bound argument for the cache key as TWO fields: a tag saying which case it
    /// is, then the payload — a leaf value verbatim; a slot subtree as its content PRE-IMAGE
    /// (`Tree.encodePreimage`, the unhashed string `Tree.encodeHash` digests), so two
    /// structurally-identical slot args key identically and two different ones do not, with no
    /// 32-bit digest between them. The encoder is the caller's canonical node-encoder (GP2 — the
    /// same one `Tree.encodeHash` takes), so the core needs no equality / codec seam.
    let private argFields (w: ArtifactWitness<'Node, 'Id>) (encode: 'Node -> string) (arg: Arg<'Node>) : string list =
        match arg with
        | ValueArg s -> [ "v"; s ]
        | SlotArg n -> [ "s"; Tree.encodePreimage w.Tree encode n ]

    /// The content-addressed application key (Phase 49; injective since Phase 290): the function's
    /// content PRE-IMAGE (`Tree.encodePreimage` — not the shape-only `Tree.contentHash`, so two
    /// functions that differ only in fixed literal content key distinctly — soundness) and the
    /// canonicalised, address-sorted param-set, each binding three fields (address, case tag,
    /// payload), the whole through `Hash.canonicalFields`. Hygiene: args are keyed by absolute
    /// address. Deterministic — identical `(function, param-set)` always yields the identical key
    /// (a hit), and any function- or param-change yields a different one (a miss).
    ///
    /// **The key is the pre-image, not a hash of it.** Until Phase 290 it was two 32-bit FNV-1a
    /// halves joined on a bare separator, and `applyMemo` served the cached tree on a key hit alone
    /// — so a value spelling `U+0001` (`{a = "X\u0001b=vY"}` keyed as `{a = "X"; b = "Y"}`), two
    /// argument sets colliding under FNV-1a, or two functions one `MoveNode` apart (the preorder
    /// alias `Tree.encodePreimage` closes) each served the WRONG tree. A `Map` keyed on the full
    /// pre-image compares it on every lookup, so a hit IS an equality of `(function, param-set)`
    /// under the caller's `encode`: no collision bound to record, and nothing to compare after the
    /// hit. The cost is the key's length — a function's whole encoding rather than eight hex
    /// characters — paid once per entry beside the result tree the entry already holds.
    let internal memoKey
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (args: Map<string, Arg<'Node>>)
        (node: 'Node)
        : string =
        let argCanon =
            args
            |> Map.toList
            |> List.sortBy fst
            |> List.collect (fun (a, arg) -> a :: argFields w encode arg)
            |> Hash.canonicalFields

        Hash.canonicalFields [ Tree.encodePreimage w.Tree encode node; argCanon ]

    /// Apply the artifact-function with a caller-supplied content-addressed memo (Phase 49 / 53). The
    /// memoisability gate keys on the **observed** effect (`observedEffect` — the widest effect actually
    /// present over the whole subtree, joined since Phase 307 with the observed effect of every
    /// `SlotArg`, which the result contains), NOT the declared root: a function whose root declares
    /// `Pure`/`Deterministic` while a descendant leaks `Clock`/`Random`/`ReadsHost` is treated as
    /// effecting and bypassed, so the cache can never serve a stale result for an actually-impure
    /// function even when the root under-declares (Phase 53 soundness gate — the same walk `auditEffect`
    /// uses). For a memoisable function (observed effect fully pure & deterministic), a key HIT returns
    /// the cached result tree without re-deriving it (the loop-speed-at-scale unlock) and a MISS computes
    /// via `apply` + stores; for a non-memoisable function the cache is BYPASSED — it computes directly
    /// via `apply`, never serving or storing (the soundness guard, Fork 3: a non-deterministic result
    /// must never be served stale, a host effect must never be silently skipped on a hit). Total —
    /// returns the same `ApplyError` set as `apply`; the cache is threaded by value (no global state,
    /// GP2). The result tree is byte-for-byte what `apply` would produce, so `applyMemo` is
    /// observationally equal to `apply` modulo the (caller-owned) cache.
    ///
    /// **Precondition (Phase 56):** `encode` must be *injective* over a node's own content — the key
    /// is built on `Tree.encodePreimage w.Tree encode node` (Phase 290: the full pre-image, so the
    /// key is injective in the function AND the arguments given that), and a lossy `encode` (two
    /// distinct nodes → one string) would let the cache serve the WRONG tree. Certify a domain's
    /// encoder with `Conformance.encoderInjectivityLaws` before trusting the memo; `memoLaws`
    /// carries the separator-in-value case that must miss.
    let applyMemo
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (args: Map<string, Arg<'Node>>)
        (node: 'Node)
        (cache: MemoCache<'Node>)
        : Result<'Node * MemoCache<'Node>, ApplyError> =
        // Phase 307: the gate is the join over the function AND every tree argument — a slot
        // argument is part of the result, so an effect it carries is the result's effect, exactly as
        // it is once `applyMemoComposed` has composed it in.
        let gate =
            (observedEffect w node, args)
            ||> Map.fold (fun acc _ arg ->
                match arg with
                | SlotArg sub -> Effect.join acc (observedEffect w sub)
                | ValueArg _ -> acc)

        if not (Memo.isMemoisable gate) then
            apply w args node
            |> Result.map (fun r ->
                r,
                { cache with
                    Bypasses = cache.Bypasses + 1 })
        else
            let key = memoKey w encode args node

            match Map.tryFind key cache.Entries with
            | Some hit -> Ok(hit, { cache with Hits = cache.Hits + 1 })
            | None ->
                apply w args node
                |> Result.map (fun r ->
                    r,
                    { cache with
                        Entries = Map.add key r cache.Entries
                        Misses = cache.Misses + 1 })

    /// Apply a COMPOSED function with subtree-level memo (Phase 49) — the "single-hole edit re-derives
    /// only the affected path" property. Each `(slotAddr, innerFn, innerArgs)` is applied through
    /// `applyMemo` (so an unchanged inner sub-function is served from the cache, not re-derived) and
    /// `compose`d into the outer; then the outer's own holes are bound, also through `applyMemo`. An edit
    /// to an outer hole leaves every inner's key unchanged — the inner subtrees come straight from the
    /// cache (hits) and only the outer re-derives; an edit to one inner misses only that inner's key (and
    /// the outer, whose composed content then changed). Single-witness `compose` only: there is no
    /// memoised `composeAcross`, though the memo itself is witness-agnostic — keyed on content hashes,
    /// not on which witness produced the node.
    let applyMemoComposed
        (w: ArtifactWitness<'Node, 'Id>)
        (encode: 'Node -> string)
        (inners: (string * 'Node * Map<string, Arg<'Node>>) list)
        (outerArgs: Map<string, Arg<'Node>>)
        (outer: 'Node)
        (cache: MemoCache<'Node>)
        : Result<'Node * MemoCache<'Node>, ApplyError> =
        let rec go (acc: 'Node) (c: MemoCache<'Node>) =
            function
            | [] -> Ok(acc, c)
            | (slotAddr, innerFn, innerArgs) :: rest ->
                applyMemo w encode innerArgs innerFn c
                |> Result.bind (fun (innerResult, c') ->
                    compose w slotAddr innerResult acc
                    |> Result.bind (fun composed -> go composed c' rest))

        go outer cache inners
        |> Result.bind (fun (composedOuter, c') -> applyMemo w encode outerArgs composedOuter c')
