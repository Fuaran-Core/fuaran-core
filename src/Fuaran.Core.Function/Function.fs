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

/// Axis 1 — does the artifact touch the world.
type HostEffect =
    | Pure
    | ReadsHost
    | WritesHost

/// One atomic source of non-determinism a body may read beyond its explicit inputs. The
/// declaration order IS the canonical order (`Clock`, then `Random`, then `Network`) — the order
/// `Effect.determinismTag` renders a set in, held by an explicit list there rather than by the
/// derived comparison, so a reordered case cannot silently reorder a wire label.
type DeterminismFactor =
    | ClockFactor
    | RandomFactor
    | NetworkFactor

/// Axis 2 — which non-deterministic sources the artifact reads: a SET of factors, joined by union.
/// `Set.empty` is `Deterministic` (reproducible from inputs). Every factor is a fact: a body that
/// reads the clock AND a random source declares both, so a capture journal can say what a replay
/// must re-seed (Phase 319; the chain this replaced joined by maximum and dropped the clock beside
/// a random read).
type DeterminismSource = Set<DeterminismFactor>

/// The mandatory, total effect signature — two orthogonal axes, never optional.
type EffectClass =
    { Host: HostEffect
      Determinism: DeterminismSource }

/// The effect lattice + the composition join. `compose` propagates the join
/// componentwise (pure ∘ impure = impure; clock ∘ random = clock ∪ random).
module Effect =

    /// The determinism bottom: no factor read.
    let deterministic: DeterminismSource = Set.empty

    /// The determinism of a body that reads exactly the clock.
    let clock: DeterminismSource = Set.singleton ClockFactor

    /// The determinism of a body that reads exactly a random source.
    let random: DeterminismSource = Set.singleton RandomFactor

    /// The determinism of a body that reads exactly the network.
    let network: DeterminismSource = Set.singleton NetworkFactor

    let pureDeterministic =
        { Host = Pure
          Determinism = deterministic }

    let private hostRank =
        function
        | Pure -> 0
        | ReadsHost -> 1
        | WritesHost -> 2

    let private hostOf =
        function
        | 0 -> Pure
        | 1 -> ReadsHost
        | _ -> WritesHost

    /// Host axis: componentwise widest (a chain). Determinism axis: union — associative,
    /// commutative, idempotent, with `deterministic` the identity.
    let join (a: EffectClass) (b: EffectClass) : EffectClass =
        { Host = hostOf (max (hostRank a.Host) (hostRank b.Host))
          Determinism = Set.union a.Determinism b.Determinism }

    /// A declared effect must be at least as wide as the actual, on both axes: a host rank at
    /// least as high, and a determinism set that is a SUPERSET — the declaration names every
    /// factor the actual reads (a `Network`-only declaration no longer covers a clock read).
    let covers (declared: EffectClass) (actual: EffectClass) : bool =
        hostRank declared.Host >= hostRank actual.Host
        && Set.isSubset actual.Determinism declared.Determinism

    let private factorName =
        function
        | ClockFactor -> "clock"
        | RandomFactor -> "random"
        | NetworkFactor -> "network"

    /// The factors in canonical order — the order every rendering of a set uses.
    let private canonicalFactors = [ ClockFactor; RandomFactor; NetworkFactor ]

    /// The canonical wire label for a determinism set (Phase 27, restated by Phase 319) — the single
    /// source of truth the `Fuaran.Core.OpStream` determinism-capture seam keys on. The empty set →
    /// `"deterministic"` (no capture — reproducible from inputs); otherwise the member factors'
    /// names in the fixed order clock, random, network, joined by `+` (`"clock"`, `"clock+random"`,
    /// `"clock+random+network"`). A single factor keeps the label it always had, so a journal keyed
    /// by `"clock"` still names the same effect. Read-only projection. `OpStream` sits below
    /// `Function` and cannot reference `DeterminismSource`, so a consumer threads this label into
    /// `OpStream.captureEffect` / `replayEffect`.
    let determinismTag (d: DeterminismSource) : string =
        match canonicalFactors |> List.filter (fun f -> Set.contains f d) with
        | [] -> "deterministic"
        | fs -> fs |> List.map factorName |> String.concat "+"

    /// The inverse of `determinismTag`: the set a label names, or `None`. Only the CANONICAL label
    /// of a set is accepted — `"random+clock"`, `"clock+clock"`, `"deterministic+clock"`, an empty
    /// member and an unknown factor are all `None` — so a set has exactly one wire spelling.
    let tryDeterminismOfTag (tag: string) : DeterminismSource option =
        let parsed =
            if tag = "deterministic" then
                Some Set.empty
            else
                tag.Split('+')
                |> Array.toList
                |> List.fold
                    (fun acc name ->
                        match acc, canonicalFactors |> List.tryFind (fun f -> factorName f = name) with
                        | Some set, Some f -> Some(Set.add f set)
                        | _ -> None)
                    (Some Set.empty)

        parsed |> Option.filter (fun set -> determinismTag set = tag)

/// The value domain a hole ranges over (value-space projection is the type system
/// for holes). `AnyString` and `SlotTree` are the *unbounded* spaces.
///
/// `SlotTree` (Phase 229) is the value space of a tree-typed slot at the scalar invocation seam:
/// a wire document — a `"kind"`-tagged JSON object, carried as the argument string — whose kind
/// satisfies the slot's constraint when one is declared (any kind otherwise). Core owns no node
/// type, so the space is stated over the WIRE and checked by shape; decoding the document into the
/// domain's node is the host's, per the witness pattern. `Function.signature` enters every
/// `SlotHole` with this space, which is what makes a capability over a slotted artifact invocable.
type ValueSpace =
    | IntRange of lo: int * hi: int
    | FloatRange of lo: float * hi: float
    | StringLen of lo: int * hi: int
    | Enum of string list
    | AnyString
    | SlotTree of kindConstraint: string option

module Space =

    /// The kind tag of a tree argument (Phase 229): `Some kind` when the string is a well-formed
    /// wire document whose top level is a `"kind"`-tagged object, `None` otherwise — a scalar, a
    /// malformed document, or an object with no string `"kind"`. The one reader `SlotTree` reaches
    /// for; it decodes nothing below the tag.
    let slotKindOf (s: string) : string option =
        match Json.parse s with
        | Ok(JObj _ as el) ->
            match Decode.kindOf el with
            | Ok k -> Some k
            | Error _ -> None
        | _ -> None

    /// Is a candidate value within the space?
    let validate (space: ValueSpace) (s: string) : bool =
        match space with
        | IntRange(lo, hi) ->
            match System.Int32.TryParse s with
            | true, v -> v >= lo && v <= hi
            | _ -> false
        | FloatRange(lo, hi) ->
            match
                System.Double.TryParse(
                    s,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, v -> v >= lo && v <= hi
            | _ -> false
        | StringLen(lo, hi) -> s.Length >= lo && s.Length <= hi
        | Enum xs -> List.contains s xs
        | AnyString -> true
        | SlotTree constraintOpt ->
            match slotKindOf s, constraintOpt with
            | None, _ -> false
            | Some _, None -> true
            | Some k, Some c -> k = c

    /// A space is bounded unless it is `AnyString` or `SlotTree` — the totality criterion for
    /// repeats (a tree space is no count space, so a repeat over one is refused as non-total).
    let internal isBounded (space: ValueSpace) : bool =
        match space with
        | AnyString
        | SlotTree _ -> false
        | _ -> true

    /// Values in single quotes, comma-separated — how a refusal names a closed set.
    let internal quoteAll (xs: string list) : string =
        xs |> List.map (fun x -> "'" + x + "'") |> String.concat ", "

    /// A value space in words, for a model to read (Phase 251): what a value must be to lie in the
    /// space, phrased so the sentence `InvokeError.describe` builds around it says what WOULD be
    /// accepted. An `Enum` names its members (the name-the-alternatives rule); a float bound is
    /// written in the canonical float layout, so the sentence is the same on every host.
    let describe (space: ValueSpace) : string =
        match space with
        | IntRange(lo, hi) -> "an integer from " + string lo + " to " + string hi
        | FloatRange(lo, hi) -> "a number from " + Canon.canonicalFloat lo + " to " + Canon.canonicalFloat hi
        | StringLen(lo, hi) -> "a string of " + string lo + " to " + string hi + " characters"
        | Enum [] -> "a member of an empty set, so no value is accepted"
        | Enum xs -> "one of " + quoteAll xs
        | AnyString -> "any string"
        | SlotTree None -> "a tree: a JSON object with a \"kind\""
        | SlotTree(Some k) -> "a tree of kind '" + k + "': a JSON object whose \"kind\" is '" + k + "'"

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
    | ValueHole of ValueSpace
    | SlotHole of kindConstraint: string option
    | RepeatHole of countSpace: ValueSpace
    | ActionHole of effect: EffectClass

/// A declared hole. `Addr` is the absolute lexical address (id-path) — the hygiene
/// surface: binding is by `Addr`, never by `Name`, so two same-named holes at
/// different addresses cannot capture one another.
type HoleDecl =
    { Addr: string
      Name: string
      Kind: HoleKind }

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
    { Addr: string
      Name: string
      Kind: string
      Space: ValueSpace option
      Slot: string option
      Action: EffectClass option
      Required: bool }

/// The artifact's derived signature: which holes, what spaces, and its effect class.
type Signature =
    { Name: string
      Holes: SigEntry list
      Effect: EffectClass }

/// An argument bound into a hole: a leaf value, or a tree (for slots / composition).
type Arg<'Node> =
    | ValueArg of string
    | SlotArg of 'Node

/// Application failure — total, and (per the envelope discipline) it names what was
/// expected.
type ApplyError =
    | UnknownHoleAddr of addr: string * declared: string list
    | ValueOutOfSpace of addr: string * space: ValueSpace * got: string
    | RequiredHolesUnbound of addrs: string list
    | NotASlot of addr: string
    | SlotKindMismatch of addr: string * expected: string * got: string
    | NonTotal of addr: string
    | BindFailed of addr: string * reason: string

// ---- the behaviour axis: typed handler-table binding (Phase 318) ----

/// A host-supplied handler bound to an action hole. The core treats the handler as opaque
/// (`'Handler` — in F# typically `unit -> 'Msg` or `'Event -> 'Msg`; in a generated C#/VB
/// host the typed delegate the binding surface declares): it is the *behavioural sidecar*
/// that never travels on the wire (closures erase). `Effect` is the handler's declared effect,
/// checked against its action hole's declared ceiling so a bound handler can never do more
/// than the artifact declared.
type HandlerBinding<'Handler> =
    { Handler: 'Handler
      Effect: EffectClass }

/// The validated behavioural sidecar — a typed handler table bound to an artifact's action
/// holes by absolute address (hygiene). The artifact tree itself is unchanged (it stays pure,
/// `Node<unit>`-equivalent); this is the host-side companion that supplies dispatch. Producing
/// it is the whole "typed dispatch = bind holes, type-checked against the signature" move.
type HandlerTable<'Handler> =
    { Handlers: Map<string, HandlerBinding<'Handler>> }

/// Why a handler-table binding was refused — total, and (per the envelope discipline) it names
/// the failure and, where a closed set is expected, enumerates the alternatives (GP5).
/// Default-deny by shape: only a declared action hole accepts a handler, and only a handler
/// within the declared effect ceiling binds.
type BindHandlerError =
    | UnknownActionAddr of addr: string * declaredActions: string list
    | NotAnActionHole of addr: string
    | RequiredActionsUnbound of addrs: string list
    | HandlerEffectExceedsCeiling of addr: string * ceiling: EffectClass * handler: EffectClass

/// The domain-witness record the generic functions take. The witness never exposes a
/// concrete `NodeKind`; it exposes accessors. `Bind` is the apply-op emitter — it
/// lowers a hole binding to the domain's own op (SetInput / SetParameter / SetVariable
/// / fragment expansion), returning the re-derived tree.
type ArtifactWitness<'Node, 'Id> =
    {
        Tree: NodeWitness<'Node, 'Id>
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
type MemoCache<'Node> =
    { Entries: Map<string, 'Node>
      Hits: int
      Misses: int
      Bypasses: int }

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

/// The generic artifact-function operations — `signature` / `apply` / `curry` /
/// `compose` — over the witness, under the three laws. UI's parameterised fragment and
/// Calc's parameterised model both express their `apply` through these.
module Function =

    /// Derive the introspectable signature from a tree.
    let signature (w: ArtifactWitness<'Node, 'Id>) (name: string) (node: 'Node) : Signature =
        let entry (h: HoleDecl) =
            let kindStr, space, slot, action, required =
                match h.Kind with
                | ValueHole s -> "value", Some s, None, None, true
                // Phase 229: a slot is entered WITH its value space — a wire tree of the constrained
                // kind — so a capability over a slotted artifact is invocable at the scalar seam.
                | SlotHole c -> "slot", Some(SlotTree c), c, None, true
                | RepeatHole s -> "repeat", Some s, None, None, false
                // An action hole is non-required on the *data* binding axis (no value/slot arg fills it);
                // it is bound on the *behaviour* axis by `bindHandlers`, which enforces its own coverage.
                | ActionHole e -> "action", None, None, Some e, false

            { Addr = h.Addr
              Name = h.Name
              Kind = kindStr
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

    /// Totality law: no repeat hole may range over an unbounded count space.
    let isTotal (sg: Signature) : bool =
        sg.Holes
        |> List.forall (fun e ->
            e.Kind <> "repeat"
            || (match e.Space with
                | Some s -> Space.isBounded s
                | None -> false))

    /// First totality violation among a hole set, if any.
    let private guardTotal (holes: HoleDecl list) : ApplyError option =
        holes
        |> List.tryPick (fun h ->
            match h.Kind with
            | RepeatHole s when not (Space.isBounded s) -> Some(NonTotal h.Addr)
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
            if not (Space.isBounded space) then Error(NonTotal addr)
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
        let holes =
            w.Holes node
            |> List.filter (fun h ->
                match h.Kind with
                | ActionHole _ -> false
                | _ -> true)

        match guardTotal holes with
        | Some e -> Error e
        | None ->
            let declared = holes |> List.map (fun h -> h.Addr)

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

                go node [] holes

    /// Apply the artifact-function to a full argument set — every declared hole must be
    /// bound. Hygiene: args are keyed by absolute address.
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
    /// slot's kind constraint (if any) is checked; the result's effect is the join.
    let compose
        (w: ArtifactWitness<'Node, 'Id>)
        (slotAddr: string)
        (inner: 'Node)
        (outer: 'Node)
        : Result<'Node, ApplyError> =
        let holes = w.Holes outer

        match holes |> List.tryFind (fun h -> h.Addr = slotAddr) with
        | None -> Error(UnknownHoleAddr(slotAddr, holes |> List.map (fun h -> h.Addr)))
        | Some h ->
            match h.Kind with
            | SlotHole constraintOpt ->
                match constraintOpt with
                | Some k when w.Tree.KindTag inner <> k -> Error(SlotKindMismatch(slotAddr, k, w.Tree.KindTag inner))
                | _ ->
                    match w.Bind slotAddr (SlotArg inner) outer with
                    | Ok n -> Ok n
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
    /// `compose` (Phase 04) wires within a single witness; this is the "trees into holes ACROSS
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
                | None -> Error(UnknownHoleAddr(slotAddr, holes |> List.map (fun h -> h.Addr)))
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
                            match wa.Bind slotAddr (SlotArg embedded) outer with
                            | Ok n -> Ok n
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
        let allAddrs = w.Holes node |> List.map (fun h -> h.Addr)

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

    /// Effect-soundness audit (Phase 248) — the teeth on the mandatory effect signature.
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

    // ---- signature → JSON tool-schema projection (Phase 247) ----

    let private spaceToJson (s: ValueSpace) : JVal =
        match s with
        | IntRange(lo, hi) -> Json.kindObj "intRange" [ "min", JInt lo; "max", JInt hi ]
        | FloatRange(lo, hi) -> Json.kindObj "floatRange" [ "min", JFloat lo; "max", JFloat hi ]
        | StringLen(lo, hi) -> Json.kindObj "stringLen" [ "minLength", JInt lo; "maxLength", JInt hi ]
        | Enum xs -> Json.kindObj "enum" [ "values", JArr(xs |> List.map JStr) ]
        | AnyString -> Json.kindObj "anyString" []
        | SlotTree c ->
            Json.kindObj
                "slotTree"
                (match c with
                 | Some k -> [ "slotKind", JStr k ]
                 | None -> [])

    /// A slot entry's space is DERIVED from its `Slot` constraint (Phase 229) — `signature` enters
    /// every `SlotHole` as `Some(SlotTree c)` beside `Slot = c` — so the wire projections omit it,
    /// and a pre-229 slot entry and a post-229 one project to the same bytes (and the same
    /// `signatureFingerprint`). Only a space that says something the entry does not is written.
    let internal derivedSlotSpace (e: SigEntry) : bool =
        e.Kind = "slot" && e.Space = Some(SlotTree e.Slot)

    /// An entry's space with a slot's derived space filled in — so an entry built by hand before
    /// Phase 229 (a spaceless slot) and one `signature` derives compare equal where shape matters.
    let internal slotSpaceOf (e: SigEntry) : ValueSpace option =
        match e.Kind, e.Space with
        | "slot", None -> Some(SlotTree e.Slot)
        | _, sp -> sp

    let private hostStr =
        function
        | Pure -> "pure"
        | ReadsHost -> "readsHost"
        | WritesHost -> "writesHost"

    let private detStr = Effect.determinismTag

    let private effectJson (e: EffectClass) : JVal =
        JObj [ "host", JStr(hostStr e.Host); "determinism", JStr(detStr e.Determinism) ]

    let private entryJson (e: SigEntry) : JVal =
        // base fields always present; the constraint fields appear only when they apply
        // (the wire JVal model has no null — absence is omission, not a null field).
        [ "addr", JStr e.Addr
          "name", JStr e.Name
          "kind", JStr e.Kind
          "required", JBool e.Required ]
        @ (match e.Space with
           | Some _ when derivedSlotSpace e -> []
           | Some s -> [ "space", spaceToJson s ]
           | None -> [])
        @ (match e.Slot with
           | Some k -> [ "slotKind", JStr k ]
           | None -> [])
        @ (match e.Action with
           | Some eff -> [ "actionEffect", effectJson eff ]
           | None -> [])
        |> JObj

    /// Project a derived signature into a canonical `"kind":"signature"` JSON object
    /// (Phase 247) — the generic artifact-function-as-tool schema. Each hole becomes a
    /// typed property (value-space → min/max/enum/length constraints, slot → its
    /// kind-constraint), the two-axis effect class travels alongside, and the required
    /// addresses are listed — so the orchestrator-facing tool descriptor is produced once
    /// in the core, not re-derived per domain. `Json.render` of the result is canonical +
    /// stable for a fixed signature.
    let toSchema (sg: Signature) : JVal =
        Json.kindObj
            "signature"
            [ "name", JStr sg.Name
              "effect", effectJson sg.Effect
              "holes", JArr(sg.Holes |> List.map entryJson)
              "required", JArr(sg.Holes |> List.filter (fun h -> h.Required) |> List.map (fun h -> JStr h.Addr)) ]

    // ---- signature → standard JSON Schema projection (Phase 04) ----

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
        match e.Kind with
        | "slot" ->
            match e.Slot with
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
           | Some eff -> [ "effect", effectJson eff ]
           | None -> [])
        |> JObj

    /// Project a derived signature into a STANDARD JSON Schema `object` (Phase 04) — the shape
    /// LLM tool-use APIs consume, so a saved artifact-function is a directly-registerable tool
    /// without per-orchestrator translation of `toSchema`'s bespoke shape. Each *data* hole
    /// (value / slot / repeat) becomes a property keyed by its absolute address (hygiene —
    /// addresses, never bare names); the required ones (value + slot; repeats optional) are
    /// listed; the two-axis effect class travels alongside as `x-effect`, OUTSIDE the parameter
    /// schema so it never pollutes the argument properties. Action holes (Phase 318) are NOT
    /// arguments the LLM fills — they are the *host's* hole-binding surface (a human binds a
    /// handler) — so they travel under `x-actions`, also OUTSIDE `properties`/`required`. This is
    /// the trust split made wire-visible: the parameter schema is the inert structure the AI
    /// emits; `x-actions` is the behaviour a human binds. `x-actions` is omitted entirely when
    /// the artifact declares no action holes, so a data-only signature is byte-identical to
    /// before. `Json.render` of the result is canonical + stable for a fixed signature.
    let toJsonSchema (sg: Signature) : JVal =
        let dataHoles = sg.Holes |> List.filter (fun e -> e.Kind <> "action")
        let actionHoles = sg.Holes |> List.filter (fun e -> e.Kind = "action")

        JObj(
            [ "type", JStr "object"
              "title", JStr sg.Name
              "x-effect", effectJson sg.Effect
              "properties", JObj(dataHoles |> List.map (fun e -> e.Addr, propSchema e))
              "required",
              JArr(
                  dataHoles
                  |> List.filter (fun h -> h.Required)
                  |> List.map (fun h -> JStr h.Addr)
              ) ]
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
    /// present over the whole subtree), NOT the declared root: a function whose root declares
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
        if not (Memo.isMemoisable (observedEffect w node)) then
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
    /// the outer, whose composed content then changed). Single-witness `compose` (Phase 04); the
    /// cross-witness `composeAcross` (Phase 47) analogue threads the same memo identically — the memo is
    /// witness-agnostic, keyed on content hashes, not on which witness produced the node.
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

// ============================================================================
//  Invocable capability (Phase 30) — `Fuaran.Core.Function` generalised from "a
//  signature you can project to a tool schema" into a *registrable, invocable
//  runtime capability* with a typed registry an agent can enumerate. This is how
//  arbitrary compute (model inference, scipy, custom numerics, any host body)
//  enters a Fuaran app without code on the wire: the wire carries a typed
//  declaration + invocation; the host provides the body, placed where it must run.
//  A non-`Deterministic` invocation's realized value is journaled through the
//  Phase 27 determinism-capture seam for exact replay. (Compute Layer spec §3–§5.)
// ============================================================================

/// Where a capability's body runs (spec §5) — a typed, portable placement contract, not a
/// deploy-time accident. A `ClientIsland` carries its sub-kind.
type IslandKind =
    | Pyodide
    | Fable
    | Js

type Placement =
    | BuildTime
    | Server
    | ClientDeclarative
    | ClientIsland of IslandKind
    | Precomputed

/// A registrable, invocable runtime capability. `Signature` (incl. its `Effect`) is reused
/// verbatim from the artifact-function surface; `Determinism` is the capture-keying axis (always
/// `= Signature.Effect.Determinism`, the smart constructor enforces it); `Placement` routes the
/// host body. The wire carries this declaration + a typed invocation, never the body.
type Capability =
    { Id: string
      Signature: Signature
      Determinism: DeterminismSource
      Placement: Placement }

/// Why a typed invocation (or a registration) was refused — total, names the failure and, where a
/// closed set is expected, enumerates the alternatives (GP5). Default-deny by shape: only a
/// registered id with in-space args dispatches.
type InvokeError =
    | NoSuchCapability of id: string * known: string list
    | DuplicateCapability of id: string
    | UnknownArg of addr: string * declared: string list
    | ArgOutOfSpace of addr: string * space: ValueSpace * got: string
    | RequiredArgsUnbound of addrs: string list
    | UninvocableArg of addr: string
    | BodyFailed of reason: string

/// What a model reads when an invocation is refused (Phase 251). The union names the failure and
/// carries the alternatives in its fields; `describe` turns a case into one plain sentence that
/// keeps both — every case that refuses against a closed set (`NoSuchCapability`, `UnknownArg`,
/// `ArgOutOfSpace`, `RequiredArgsUnbound`) names the members of that set, so the next attempt has
/// something to aim at. The wire form of the same value is `CapabilityCodec.invokeErrorJson`.
module InvokeError =

    /// One sentence a model can act on, naming the failure and what would be accepted.
    let describe (e: InvokeError) : string =
        match e with
        | NoSuchCapability(id, []) ->
            "Refused: there is no tool '"
            + id
            + "' you may call. There are no tools you may call."
        | NoSuchCapability(id, known) ->
            "Refused: there is no tool '"
            + id
            + "' you may call. The tools you may call are "
            + Space.quoteAll known
            + "."
        | DuplicateCapability id -> "Refused: the tool '" + id + "' is registered twice."
        | UnknownArg(addr, []) ->
            "Refused: '"
            + addr
            + "' is not an argument of this tool. It takes no arguments."
        | UnknownArg(addr, declared) ->
            "Refused: '"
            + addr
            + "' is not an argument of this tool. Its arguments are "
            + Space.quoteAll declared
            + "."
        | ArgOutOfSpace(addr, space, got) ->
            "Refused: argument '"
            + addr
            + "' must be "
            + Space.describe space
            + "; you sent '"
            + got
            + "'."
        | RequiredArgsUnbound addrs -> "Refused: required arguments missing: " + Space.quoteAll addrs + "."
        | UninvocableArg addr ->
            "Refused: argument '"
            + addr
            + "' cannot take the value sent. A tree argument takes a JSON object with a \"kind\"; an action is bound by the host and never by a caller."
        | BodyFailed reason -> "Refused: the tool ran and failed: " + reason + "."

    /// Every refusal, one sentence per line, in the order given — the reading of
    /// `Capability.validateArgsAll`'s answer, so a call with two bad arguments is answered once.
    let describeAll (es: InvokeError list) : string =
        es |> List.map describe |> String.concat "\n"

/// A domain-general async-result envelope for a capability invocation (Phase 32) — the Compute Layer
/// spec's `Deferred<'T> = Pending | Ready of 'T | Error of e` (§4), put in the SUBSTRATE so every host
/// (Mail network-send, Legal LLM-extraction, CAD server mesh-ops — none of which may depend on
/// `Fuaran.UI`) gets the async-invocation envelope from Core; the UI surface *renders* it
/// (`onLoading`/`onError`) rather than defining it. The failure rides as a rendered `string` (the
/// `BodyFailed` / `InvokeError` text) so the envelope is one-type-parameter + serialisable, and the case
/// is named `Failed` (not `Error`) so it never shadows `Result.Error` in a consumer that opens
/// `Fuaran.Core`.
type Deferred<'T> =
    | Pending
    | Ready of 'T
    | Failed of message: string

/// Total combinators over `Deferred` (Phase 32). `map`/`bind` operate on a `Ready`; `Pending`/`Failed`
/// propagate unchanged. `toResult` projects to a `Result` (`Pending` → `Error "pending"`).
module Deferred =

    let map (f: 'a -> 'b) (d: Deferred<'a>) : Deferred<'b> =
        match d with
        | Ready v -> Ready(f v)
        | Pending -> Pending
        | Failed m -> Failed m

    let bind (f: 'a -> Deferred<'b>) (d: Deferred<'a>) : Deferred<'b> =
        match d with
        | Ready v -> f v
        | Pending -> Pending
        | Failed m -> Failed m

    /// `Ready v` → `Ok v`; `Failed m` → `Error m`; `Pending` → `Error "pending"`.
    let toResult (d: Deferred<'T>) : Result<'T, string> =
        match d with
        | Ready v -> Ok v
        | Failed m -> Error m
        | Pending -> Error "pending"

    /// The realized value, if `Ready` (the value the Phase 27 capture seam journals; `Pending`/`Failed`
    /// are not captured — replay re-issues the invocation).
    let tryValue (d: Deferred<'T>) : 'T option =
        match d with
        | Ready v -> Some v
        | _ -> None

/// A validated argument, TYPED by the value space it was checked against (Phase 251) — what
/// `Capability.invokeWithArgs` hands a body, so the body reads the value `validateArgs` already
/// accepted instead of parsing the string again. `IntRange` arguments arrive as `IntValue`,
/// `FloatRange` as `FloatValue`, `StringLen` / `Enum` / `AnyString` as `TextValue`, and a
/// `SlotTree` argument as `TreeValue`, the parsed wire document (decoding it into the domain's node
/// stays the host's, per the witness pattern).
type ArgValue =
    | IntValue of int
    | FloatValue of float
    | TextValue of string
    | TreeValue of JVal

/// The invocable-capability surface: the typed registry (populate + enumerate + dispatch), the
/// arg-validation contract, the Phase 27 capture keying, and the wire codec. Additive over the
/// `Function` surface; FSharp.Core-only, Fable-clean.
module Capability =

    /// Build a capability, deriving `Determinism` from the signature's effect class (the two are
    /// never allowed to disagree).
    let create (id: string) (sg: Signature) (placement: Placement) : Capability =
        { Id = id
          Signature = sg
          Determinism = sg.Effect.Determinism
          Placement = placement }

    /// The Phase 27 determinism label this capability keys its captures on (`"deterministic"` /
    /// `"clock"` / `"random"` / `"network"`).
    let determinismTag (c: Capability) : string = Effect.determinismTag c.Determinism

    /// The effect-identity key the Phase 27 capture seam journals a non-deterministic invocation
    /// under: the capability id + a hash of the canonical (addr-sorted) pre-image, built through
    /// `Hash.canonicalFields` — two fields per binding (addr, value). So two invocations with the
    /// same args replay the same captured value, and the pre-image is INJECTIVE (Phase 225,
    /// `invocation_key_injective` in `proofs/Capability.fst`): distinct argument sets never share
    /// one, whatever their values contain. That two distinct pre-images hash apart is a property
    /// of `Hash.fnv1a` and is not claimed. A consumer threads this as `OpStream.captureEffect`'s
    /// `eff` argument.
    let invocationKey (c: Capability) (args: (string * string) list) : string =
        let canonical =
            args
            |> List.sortBy fst
            |> List.collect (fun (a, v) -> [ a; v ])
            |> Hash.canonicalFields

        c.Id + "#" + Hash.fnv1a canonical

    /// Validate typed `args` (addr → string value) against the capability's signature *before*
    /// dispatch: every arg must address a declared data hole and lie in its space; every required
    /// hole must be bound. A slot hole is invocable since Phase 229: its space is `SlotTree`, so its
    /// argument is a wire document (a `"kind"`-tagged object) whose kind satisfies the constraint —
    /// a tree of the wrong kind is `ArgOutOfSpace` naming the addr and the `SlotTree` constraint,
    /// and an argument that is no tree at all (a scalar, a malformed document) is `UninvocableArg`.
    /// A spaceless entry (an action hole) stays `UninvocableArg` for every argument. The host validates this
    /// before running any body (default-deny by shape, FGP 3). `Required` is checked as the hole's
    /// PRESENCE among the args, which here is presence of a value: an arg is a string checked
    /// against its space, and a value space has no absent marker, so a required hole cannot be
    /// bound to "no value" the way a `Query` param bound to `Null` could before Phase 226.
    let validateArgs (c: Capability) (args: (string * string) list) : Result<unit, InvokeError> =
        let holes = c.Signature.Holes
        let declared = holes |> List.map (fun h -> h.Addr)
        let argMap = Map.ofList args

        // 1. every arg addresses a declared hole, and that hole takes a scalar value in-space.
        let rec checkArgs =
            function
            | [] -> Ok()
            | (addr, value) :: rest ->
                match holes |> List.tryFind (fun h -> h.Addr = addr) with
                | None -> Error(UnknownArg(addr, declared))
                | Some h ->
                    match h.Space with
                    | None -> Error(UninvocableArg addr) // a spaceless (action) hole
                    | Some(SlotTree _) when (Space.slotKindOf value).IsNone -> Error(UninvocableArg addr) // no tree
                    | Some space ->
                        if Space.validate space value then
                            checkArgs rest
                        else
                            Error(ArgOutOfSpace(addr, space, value))

        checkArgs args
        |> Result.bind (fun () ->
            // 2. every required hole is bound.
            let unbound =
                holes
                |> List.filter (fun h -> h.Required && not (Map.containsKey h.Addr argMap))
                |> List.map (fun h -> h.Addr)

            if List.isEmpty unbound then
                Ok()
            else
                Error(RequiredArgsUnbound unbound))

    /// Invoke a capability: validate the args, then run the host `body`. Deterministic capabilities
    /// re-evaluate freely; for a non-`Deterministic` capability the caller journals the realized
    /// value via `OpStream.captureEffect` keyed by `invocationKey` + `determinismTag` (Phase 27),
    /// so the invocation replays exactly. A body failure is a named `BodyFailed`, never a throw.
    ///
    /// Since Phase 210 the body answers in the `Deferred` envelope declared above — a body placed on
    /// a `Server` or in a `ClientIsland` cannot settle synchronously, and `Deferred` was put in this
    /// package for exactly that. A synchronous body wraps its value in `Ready`. The ASYNC axis rides
    /// the envelope; the ERROR axis stays typed on the outer `Result`, with a body's `Failed m`
    /// projected into the enumerated `BodyFailed m`. So an invocation has exactly three outcomes —
    /// SETTLED (`Ok(Ready v)`), PENDING (`Ok Pending`) and REFUSED (`Error e`, typed) — and
    /// `Ok(Failed _)` is unreachable by construction, which `capabilityLaws` certifies and
    /// `proofs/Capability.fst`'s `invoke_never_ok_failed` proves rather than this comment asserting.
    /// The same shape `Query.invoke` carries (Phase 198): one async shape across both seams.
    let invoke
        (c: Capability)
        (args: (string * string) list)
        (body: unit -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        validateArgs c args
        |> Result.bind (fun () ->
            match body () with
            | Ready v -> Ok(Ready v)
            | Pending -> Ok Pending
            | Failed m -> Error(BodyFailed m))

    // ---- every refusal at once, and the validated arguments handed on (Phase 251) ----

    /// The refusal one argument earns on its own, by the rules `validateArgs` applies to it.
    let private argFault (c: Capability) (declared: string list) (addr: string, value: string) : InvokeError option =
        match c.Signature.Holes |> List.tryFind (fun h -> h.Addr = addr) with
        | None -> Some(UnknownArg(addr, declared))
        | Some h ->
            match h.Space with
            | None -> Some(UninvocableArg addr)
            | Some(SlotTree _) when (Space.slotKindOf value).IsNone -> Some(UninvocableArg addr)
            | Some space ->
                if Space.validate space value then
                    None
                else
                    Some(ArgOutOfSpace(addr, space, value))

    /// Validate as `validateArgs` does, but answer with EVERY refusal rather than the first: one
    /// per refused argument, in argument order, then `RequiredArgsUnbound` naming every required
    /// hole left out. So a call with two bad arguments is refused naming both, and a model needs one
    /// round trip, not two. The first-failure form is kept, and the two agree by construction of
    /// their order: the head of this list is exactly `validateArgs`'s refusal, and `Ok ()` here is
    /// `Ok ()` there.
    let validateArgsAll (c: Capability) (args: (string * string) list) : Result<unit, InvokeError list> =
        let holes = c.Signature.Holes
        let declared = holes |> List.map (fun h -> h.Addr)
        let argMap = Map.ofList args
        let faults = args |> List.choose (argFault c declared)

        let unbound =
            holes
            |> List.filter (fun h -> h.Required && not (Map.containsKey h.Addr argMap))
            |> List.map (fun h -> h.Addr)

        let all =
            if List.isEmpty unbound then
                faults
            else
                faults @ [ RequiredArgsUnbound unbound ]

        if List.isEmpty all then Ok() else Error all

    /// One argument as the value its space admits, or `None` where the string is not in it.
    let private typedValue (space: ValueSpace option) (value: string) : ArgValue option =
        match space with
        | Some(IntRange _) ->
            match System.Int32.TryParse value with
            | true, v -> Some(IntValue v)
            | _ -> None
        | Some(FloatRange _) ->
            match
                System.Double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture
                )
            with
            | true, v -> Some(FloatValue v)
            | _ -> None
        | Some(StringLen _)
        | Some(Enum _)
        | Some AnyString -> Some(TextValue value)
        | Some(SlotTree _) ->
            match Json.parse value with
            | Ok el -> Some(TreeValue el)
            | Error _ -> None
        | None -> None

    /// Validate `args` (`validateArgs`), then type each one by the space it was checked against —
    /// the list `invokeWithArgs` hands a body, in argument order. The parses are the ones
    /// `Space.validate` made, so an argument that validated always types; were one ever not to, it
    /// is refused as `ArgOutOfSpace` rather than handed on untyped.
    let typeArgs (c: Capability) (args: (string * string) list) : Result<(string * ArgValue) list, InvokeError> =
        validateArgs c args
        |> Result.bind (fun () ->
            let rec go (acc: (string * ArgValue) list) =
                function
                | [] -> Ok(List.rev acc)
                | (addr, value) :: rest ->
                    let space =
                        c.Signature.Holes
                        |> List.tryPick (fun h -> if h.Addr = addr then h.Space else None)

                    match typedValue space value with
                    | Some v -> go ((addr, v) :: acc) rest
                    | None ->
                        match space with
                        | Some sp -> Error(ArgOutOfSpace(addr, sp, value))
                        | None -> Error(UninvocableArg addr)

            go [] args)

    /// `invoke`, with the body handed the validated arguments, typed (`typeArgs`) — so the body
    /// reads the values validation accepted instead of closing over the caller's list and parsing it
    /// again, and the list it reads is the list that was checked. Additive beside `invoke`; the same
    /// three outcomes, and a body's `Failed m` is projected into `BodyFailed m` exactly as there.
    let invokeWithArgs
        (c: Capability)
        (args: (string * string) list)
        (body: (string * ArgValue) list -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        typeArgs c args
        |> Result.bind (fun typed ->
            match body typed with
            | Ready v -> Ok(Ready v)
            | Pending -> Ok Pending
            | Failed m -> Error(BodyFailed m))

/// A typed capability registry — the discovery surface an agent enumerates (the compute analogue of
/// node-introspection): "what compute may I invoke, with what typed args". Default-deny by shape on
/// dispatch — only a registered id resolves.
type CapabilityRegistry =
    { Capabilities: Map<string, Capability> }

module Registry =

    let empty: CapabilityRegistry = { Capabilities = Map.empty }

    /// Register a capability — additive, no silent overwrite (a duplicate id is a named error).
    let register (c: Capability) (r: CapabilityRegistry) : Result<CapabilityRegistry, InvokeError> =
        if Map.containsKey c.Id r.Capabilities then
            Error(DuplicateCapability c.Id)
        else
            Ok
                { r with
                    Capabilities = Map.add c.Id c r.Capabilities }

    let tryFind (id: string) (r: CapabilityRegistry) : Capability option = Map.tryFind id r.Capabilities

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part
    /// of the contract (`capabilityLaws` certifies it).
    let enumerate (r: CapabilityRegistry) : Capability list =
        r.Capabilities |> Map.toList |> List.map snd

    /// Dispatch an invocation through the registry: resolve the id (default-deny — an unregistered
    /// id is `NoSuchCapability`), then `Capability.invoke`. The host body is supplied by the caller
    /// per the resolved capability's placement, and answers in the `Deferred` envelope (Phase 210 —
    /// the same three outcomes `Capability.invoke` documents).
    let dispatch
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> unit -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        match Map.tryFind id r.Capabilities with
        | None -> Error(NoSuchCapability(id, r.Capabilities |> Map.toList |> List.map fst))
        | Some c -> Capability.invoke c args (body c)

    /// `dispatch`, with the body handed the resolved capability and the validated arguments, typed
    /// (`Capability.invokeWithArgs`, Phase 251). Additive beside `dispatch`; default-deny the same.
    let dispatchWithArgs
        (r: CapabilityRegistry)
        (id: string)
        (args: (string * string) list)
        (body: Capability -> (string * ArgValue) list -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        match Map.tryFind id r.Capabilities with
        | None -> Error(NoSuchCapability(id, r.Capabilities |> Map.toList |> List.map fst))
        | Some c -> Capability.invokeWithArgs c args (body c)

/// How a seam codec treats a member it does not know (Phase 251). `Lenient` — the default, and
/// what every decoder without a policy argument does — ignores it, so a reader tolerates a writer
/// one version ahead. `Strict` refuses it, naming the member and the members that WOULD be read:
/// for input from a model, an ignored member is a claim that silently went unread (an `"actor"`
/// beside an invocation, say), and the refusal is what tells the model so.
[<RequireQualifiedAccess>]
type ReadPolicy =
    | Lenient
    | Strict

/// The canonical wire codec for a `Capability` declaration + a typed invocation record. Round-trips
/// the full `Signature` (so an enumerated capability re-serialises identically), the determinism
/// tag, and the placement. Fable-clean (`Json` / `Decode`).
module CapabilityCodec =

    // ---- value-space ----

    let private spaceJson (s: ValueSpace) : JVal =
        match s with
        | IntRange(lo, hi) -> Canon.typed "intRange" [ "min", JInt lo; "max", JInt hi ]
        | FloatRange(lo, hi) -> Canon.typed "floatRange" [ "min", JFloat lo; "max", JFloat hi ]
        | StringLen(lo, hi) -> Canon.typed "stringLen" [ "min", JInt lo; "max", JInt hi ]
        | Enum xs -> Canon.typed "enum" [ "values", JArr(xs |> List.map JStr) ]
        | AnyString -> Canon.typed "anyString" []
        | SlotTree c ->
            Canon.typed
                "slotTree"
                (match c with
                 | Some k -> [ "slotKind", JStr k ]
                 | None -> [])

    let private spaceOf (el: JVal) : Result<ValueSpace, string> =
        Decode.strField "$type" el
        |> Result.bind (fun k ->
            match k with
            | "intRange" ->
                Decode.intField "min" el
                |> Result.bind (fun lo -> Decode.intField "max" el |> Result.map (fun hi -> IntRange(lo, hi)))
            | "floatRange" ->
                Decode.getProp "min" el
                |> Result.bind Decode.asFloat
                |> Result.bind (fun lo ->
                    Decode.getProp "max" el
                    |> Result.bind Decode.asFloat
                    |> Result.map (fun hi -> FloatRange(lo, hi)))
            | "stringLen" ->
                Decode.intField "min" el
                |> Result.bind (fun lo -> Decode.intField "max" el |> Result.map (fun hi -> StringLen(lo, hi)))
            | "enum" ->
                Decode.getProp "values" el
                |> Result.bind (Decode.mapList Decode.asString)
                |> Result.map Enum
            | "anyString" -> Ok AnyString
            | "slotTree" ->
                Ok(
                    SlotTree(
                        match Decode.strField "slotKind" el with
                        | Ok k -> Some k
                        | Error _ -> None
                    )
                )
            | other -> Error("unknown value-space kind: " + other))

    // ---- effect class ----

    let private hostStr =
        function
        | Pure -> "pure"
        | ReadsHost -> "readsHost"
        | WritesHost -> "writesHost"

    let private hostOf =
        function
        | "pure" -> Ok Pure
        | "readsHost" -> Ok ReadsHost
        | "writesHost" -> Ok WritesHost
        | other -> Error("unknown host effect: " + other)

    let private detOf (tag: string) =
        match Effect.tryDeterminismOfTag tag with
        | Some set -> Ok set
        | None -> Error("unknown determinism: " + tag)

    let private effectJson (e: EffectClass) : JVal =
        JObj
            [ "host", JStr(hostStr e.Host)
              "determinism", JStr(Effect.determinismTag e.Determinism) ]

    let private effectOf (el: JVal) : Result<EffectClass, string> =
        Decode.strField "host" el
        |> Result.bind hostOf
        |> Result.bind (fun host ->
            Decode.strField "determinism" el
            |> Result.bind detOf
            |> Result.map (fun det -> { Host = host; Determinism = det }))

    // ---- signature entry + signature ----

    let private entryJson (e: SigEntry) : JVal =
        [ "addr", JStr e.Addr
          "name", JStr e.Name
          "kind", JStr e.Kind
          "required", JBool e.Required ]
        @ (match e.Space with
           | Some _ when Function.derivedSlotSpace e -> []
           | Some s -> [ "space", spaceJson s ]
           | None -> [])
        @ (match e.Slot with
           | Some k -> [ "slotKind", JStr k ]
           | None -> [])
        @ (match e.Action with
           | Some eff -> [ "actionEffect", effectJson eff ]
           | None -> [])
        |> JObj

    let private entryOf (el: JVal) : Result<SigEntry, string> =
        Decode.strField "addr" el
        |> Result.bind (fun addr ->
            Decode.strField "name" el
            |> Result.bind (fun name ->
                Decode.strField "kind" el
                |> Result.bind (fun kind ->
                    Decode.getProp "required" el
                    |> Result.bind Decode.asBool
                    |> Result.bind (fun required ->
                        let space =
                            match Decode.getProp "space" el with
                            | Ok s -> spaceOf s |> Result.map Some
                            | Error _ -> Ok None

                        let action =
                            match Decode.getProp "actionEffect" el with
                            | Ok a -> effectOf a |> Result.map Some
                            | Error _ -> Ok None

                        space
                        |> Result.bind (fun sp ->
                            action
                            |> Result.map (fun ac ->
                                let slot =
                                    match Decode.strField "slotKind" el with
                                    | Ok k -> Some k
                                    | Error _ -> None

                                // A slot entry travels without its derived space (Phase 229), so
                                // decoding restores it from the constraint.
                                let sp =
                                    match sp with
                                    | None when kind = "slot" -> Some(SlotTree slot)
                                    | other -> other

                                { Addr = addr
                                  Name = name
                                  Kind = kind
                                  Space = sp
                                  Slot = slot
                                  Action = ac
                                  Required = required }))))))

    let internal signatureJson (sg: Signature) : JVal =
        JObj
            [ "name", JStr sg.Name
              "effect", effectJson sg.Effect
              "holes", JArr(sg.Holes |> List.map entryJson) ]

    let signatureOf (el: JVal) : Result<Signature, string> =
        Decode.strField "name" el
        |> Result.bind (fun name ->
            Decode.getProp "effect" el
            |> Result.bind effectOf
            |> Result.bind (fun eff ->
                Decode.getProp "holes" el
                |> Result.bind (Decode.mapList entryOf)
                |> Result.map (fun holes ->
                    { Name = name
                      Holes = holes
                      Effect = eff })))

    // ---- placement ----

    let private islandTag =
        function
        | Pyodide -> "pyodide"
        | Fable -> "fable"
        | Js -> "js"

    let private islandOf =
        function
        | "pyodide" -> Ok Pyodide
        | "fable" -> Ok Fable
        | "js" -> Ok Js
        | other -> Error("unknown island kind: " + other)

    let private placementJson (p: Placement) : JVal =
        match p with
        | BuildTime -> Canon.typed "buildTime" []
        | Server -> Canon.typed "server" []
        | ClientDeclarative -> Canon.typed "clientDeclarative" []
        | Precomputed -> Canon.typed "precomputed" []
        | ClientIsland k -> Canon.typed "clientIsland" [ "island", JStr(islandTag k) ]

    let private placementOf (el: JVal) : Result<Placement, string> =
        Decode.strField "$type" el
        |> Result.bind (fun k ->
            match k with
            | "buildTime" -> Ok BuildTime
            | "server" -> Ok Server
            | "clientDeclarative" -> Ok ClientDeclarative
            | "precomputed" -> Ok Precomputed
            | "clientIsland" -> Decode.strField "island" el |> Result.bind islandOf |> Result.map ClientIsland
            | other -> Error("unknown placement: " + other))

    // ---- capability declaration ----

    /// Encode a `Capability` declaration to a `JVal` (`"$type":"capability"`).
    let encodeJson (c: Capability) : JVal =
        Canon.typed
            "capability"
            [ "id", JStr c.Id
              "signature", signatureJson c.Signature
              "determinism", JStr(Effect.determinismTag c.Determinism)
              "placement", placementJson c.Placement ]

    let encode (c: Capability) : string = Canon.render (encodeJson c)

    let decodeJson (el: JVal) : Result<Capability, string> =
        Decode.strField "id" el
        |> Result.bind (fun id ->
            Decode.getProp "signature" el
            |> Result.bind signatureOf
            |> Result.bind (fun sg ->
                // Cross-check the wire `determinism` tag against the signature's effect determinism
                // (Phase 44). The tag is written on encode but was previously ignored on decode, so a
                // payload whose tag disagrees with its signature — tampered, or from a divergent host —
                // decoded silently to the signature-derived value, mis-keying the Phase 27 replay seam.
                let expectedTag = Effect.determinismTag sg.Effect.Determinism

                Decode.strField "determinism" el
                |> Result.bind (fun wireTag ->
                    if wireTag <> expectedTag then
                        Error(
                            "capability determinism disagrees with signature effect: wire '"
                            + wireTag
                            + "' vs signature '"
                            + expectedTag
                            + "'"
                        )
                    else
                        Ok())
                |> Result.bind (fun () ->
                    Decode.getProp "placement" el
                    |> Result.bind placementOf
                    |> Result.map (fun placement ->
                        { Id = id
                          Signature = sg
                          Determinism = sg.Effect.Determinism
                          Placement = placement }))))

    let decode (s: string) : Result<Capability, string> =
        Decode.parse s |> Result.bind decodeJson

    // ---- typed invocation record ----

    /// Encode a typed invocation `(capabilityId, args)` to a `JVal` (`"$type":"invocation"`).
    let internal encodeInvocationJson (capabilityId: string) (args: (string * string) list) : JVal =
        Canon.typed
            "invocation"
            [ "capabilityId", JStr capabilityId
              "args", JArr(args |> List.map (fun (a, v) -> JObj [ "addr", JStr a; "value", JStr v ])) ]

    let encodeInvocation (capabilityId: string) (args: (string * string) list) : string =
        Canon.render (encodeInvocationJson capabilityId args)

    let decodeInvocation (s: string) : Result<string * (string * string) list, string> =
        Decode.parse s
        |> Result.bind (fun el ->
            Decode.strField "capabilityId" el
            |> Result.bind (fun cid ->
                Decode.getProp "args" el
                |> Result.bind (
                    Decode.mapList (fun a ->
                        Decode.strField "addr" a
                        |> Result.bind (fun addr -> Decode.strField "value" a |> Result.map (fun v -> addr, v)))
                )
                |> Result.map (fun args -> cid, args)))

    // ---- Deferred<'T> async-result envelope (Phase 32) ----

    /// Encode a `Deferred<'T>` to a `JVal` (`"$type"`-tagged `pending` / `ready` / `failed`), using
    /// `encodeT` for a `Ready` payload. The async-invocation result wire shape every host shares.
    let deferredJson (encodeT: 'T -> JVal) (d: Deferred<'T>) : JVal =
        match d with
        | Pending -> Canon.typed "pending" []
        | Ready v -> Canon.typed "ready" [ "value", encodeT v ]
        | Failed m -> Canon.typed "failed" [ "message", JStr m ]

    let encodeDeferred (encodeT: 'T -> JVal) (d: Deferred<'T>) : string = Canon.render (deferredJson encodeT d)

    /// Decode a `Deferred<'T>` from a `JVal`, using `decodeT` for a `ready` payload — `Result`-typed with
    /// a named error (the codec envelope discipline).
    let deferredOf (decodeT: JVal -> Result<'T, string>) (el: JVal) : Result<Deferred<'T>, string> =
        Decode.strField "$type" el
        |> Result.bind (fun k ->
            match k with
            | "pending" -> Ok Pending
            | "ready" -> Decode.getProp "value" el |> Result.bind decodeT |> Result.map Ready
            | "failed" -> Decode.strField "message" el |> Result.map Failed
            | other -> Error("unknown deferred kind: " + other))

    let decodeDeferred (decodeT: JVal -> Result<'T, string>) (s: string) : Result<Deferred<'T>, string> =
        Decode.parse s |> Result.bind (deferredOf decodeT)

    // ---- typed refusal (Phase 251) ----
    // The refusal crosses the wire like every other seam type, under the same `$type` envelope
    // (one case, one tag; the case name in camelCase). A value space travels in its codec form, so
    // the document decodes back to the same `InvokeError`. The sentence a model reads is
    // `InvokeError.describe`; this is the structured form beside it.

    let private strs (xs: string list) : JVal = JArr(xs |> List.map JStr)

    /// Encode an `InvokeError` to a `JVal` (`"$type"` is the case: `noSuchCapability`, `argOutOfSpace`, …).
    let invokeErrorJson (e: InvokeError) : JVal =
        match e with
        | NoSuchCapability(id, known) -> Canon.typed "noSuchCapability" [ "id", JStr id; "known", strs known ]
        | DuplicateCapability id -> Canon.typed "duplicateCapability" [ "id", JStr id ]
        | UnknownArg(addr, declared) -> Canon.typed "unknownArg" [ "addr", JStr addr; "declared", strs declared ]
        | ArgOutOfSpace(addr, space, got) ->
            Canon.typed "argOutOfSpace" [ "addr", JStr addr; "space", spaceJson space; "got", JStr got ]
        | RequiredArgsUnbound addrs -> Canon.typed "requiredArgsUnbound" [ "addrs", strs addrs ]
        | UninvocableArg addr -> Canon.typed "uninvocableArg" [ "addr", JStr addr ]
        | BodyFailed reason -> Canon.typed "bodyFailed" [ "reason", JStr reason ]

    let encodeInvokeError (e: InvokeError) : string = Canon.render (invokeErrorJson e)

    /// Decode an `InvokeError` from a `JVal` — `Result`-typed with a named error.
    let invokeErrorOf (el: JVal) : Result<InvokeError, string> =
        let strList (name: string) =
            Decode.getProp name el |> Result.bind (Decode.mapList Decode.asString)

        Decode.strField "$type" el
        |> Result.bind (fun k ->
            match k with
            | "noSuchCapability" ->
                Decode.strField "id" el
                |> Result.bind (fun id -> strList "known" |> Result.map (fun known -> NoSuchCapability(id, known)))
            | "duplicateCapability" -> Decode.strField "id" el |> Result.map DuplicateCapability
            | "unknownArg" ->
                Decode.strField "addr" el
                |> Result.bind (fun addr -> strList "declared" |> Result.map (fun d -> UnknownArg(addr, d)))
            | "argOutOfSpace" ->
                Decode.strField "addr" el
                |> Result.bind (fun addr ->
                    Decode.getProp "space" el
                    |> Result.bind spaceOf
                    |> Result.bind (fun sp ->
                        Decode.strField "got" el |> Result.map (fun got -> ArgOutOfSpace(addr, sp, got))))
            | "requiredArgsUnbound" -> strList "addrs" |> Result.map RequiredArgsUnbound
            | "uninvocableArg" -> Decode.strField "addr" el |> Result.map UninvocableArg
            | "bodyFailed" -> Decode.strField "reason" el |> Result.map BodyFailed
            | other -> Error("unknown invoke error: " + other))

    let decodeInvokeError (s: string) : Result<InvokeError, string> =
        Decode.parse s |> Result.bind invokeErrorOf

    // ---- strict read policy (Phase 251) ----
    // `Strict` is a members check over the document BEFORE the ordinary decoder runs: every object
    // this codec reads may carry only the members this codec reads from it. The decoders above are
    // untouched, so `Lenient` is byte-for-byte the old behaviour. A shape fault (a string where an
    // object belongs) is left to the decoder to name; the check only ever adds the unknown-member
    // refusal. A `ready` envelope's payload is the caller's `decodeT`'s to read, so its members are
    // the caller's to police.

    let private tagOf (el: JVal) : string option =
        match el with
        | JObj fields ->
            fields
            |> List.tryPick (fun (k, v) ->
                match k, v with
                | "$type", JStr t -> Some t
                | _ -> None)
        | _ -> None

    /// The first member of `el` outside `known`, as a refusal naming it and the members read.
    let private members (where: string) (known: string list) (el: JVal) : Result<unit, string> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (k, _) -> not (List.contains k known)) with
            | Some(k, _) ->
                Error(
                    "unknown member '"
                    + k
                    + "' in "
                    + where
                    + "; its members are "
                    + Space.quoteAll (List.sort known)
                )
            | None -> Ok()
        | _ -> Ok()

    /// Check the member `name` of `el`, where it is present.
    let private within (name: string) (check: JVal -> Result<unit, string>) (el: JVal) : Result<unit, string> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (k, _) -> k = name) with
            | Some(_, v) -> check v
            | None -> Ok()
        | _ -> Ok()

    /// Check every element of an array.
    let private each (check: JVal -> Result<unit, string>) (el: JVal) : Result<unit, string> =
        match el with
        | JArr xs -> xs |> List.fold (fun acc x -> acc |> Result.bind (fun () -> check x)) (Ok())
        | _ -> Ok()

    let private strictSpace (el: JVal) =
        let extra =
            match tagOf el with
            | Some "intRange"
            | Some "floatRange"
            | Some "stringLen" -> [ "min"; "max" ]
            | Some "enum" -> [ "values" ]
            | Some "slotTree" -> [ "slotKind" ]
            | _ -> []

        members "value space" ("$type" :: extra) el

    let private strictEffect (el: JVal) =
        members "effect" [ "host"; "determinism" ] el

    let private strictEntry (el: JVal) =
        members "signature hole" [ "addr"; "name"; "kind"; "required"; "space"; "slotKind"; "actionEffect" ] el
        |> Result.bind (fun () -> within "space" strictSpace el)
        |> Result.bind (fun () -> within "actionEffect" strictEffect el)

    let private strictSignature (el: JVal) =
        members "signature" [ "name"; "effect"; "holes" ] el
        |> Result.bind (fun () -> within "effect" strictEffect el)
        |> Result.bind (fun () -> within "holes" (each strictEntry) el)

    let private strictPlacement (el: JVal) =
        let extra =
            match tagOf el with
            | Some "clientIsland" -> [ "island" ]
            | _ -> []

        members "placement" ("$type" :: extra) el

    let private strictCapability (el: JVal) =
        members "capability" [ "$type"; "id"; "signature"; "determinism"; "placement" ] el
        |> Result.bind (fun () -> within "signature" strictSignature el)
        |> Result.bind (fun () -> within "placement" strictPlacement el)

    let private strictInvocation (el: JVal) =
        members "invocation" [ "$type"; "capabilityId"; "args" ] el
        |> Result.bind (fun () -> within "args" (each (members "invocation argument" [ "addr"; "value" ])) el)

    let private strictDeferred (el: JVal) =
        let extra =
            match tagOf el with
            | Some "ready" -> [ "value" ]
            | Some "failed" -> [ "message" ]
            | _ -> []

        members "deferred" ("$type" :: extra) el

    let private under (policy: ReadPolicy) (check: JVal -> Result<unit, string>) (el: JVal) : Result<unit, string> =
        match policy with
        | ReadPolicy.Lenient -> Ok()
        | ReadPolicy.Strict -> check el

    /// `decodeJson` under a read policy: `Strict` refuses an unknown member anywhere in the
    /// declaration (its signature, holes, value spaces, effects and placement included).
    let decodeJsonWith (policy: ReadPolicy) (el: JVal) : Result<Capability, string> =
        under policy strictCapability el |> Result.bind (fun () -> decodeJson el)

    /// `decode` under a read policy.
    let decodeWith (policy: ReadPolicy) (s: string) : Result<Capability, string> =
        Decode.parse s |> Result.bind (decodeJsonWith policy)

    /// `decodeInvocation` under a read policy: `Strict` refuses an unknown member of the
    /// invocation or of any of its arguments — an `"actor"` beside the `capabilityId`, say, which
    /// `Lenient` reads past.
    let decodeInvocationWith (policy: ReadPolicy) (s: string) : Result<string * (string * string) list, string> =
        Decode.parse s
        |> Result.bind (under policy strictInvocation)
        |> Result.bind (fun () -> decodeInvocation s)

    /// `deferredOf` under a read policy: `Strict` refuses an unknown member of the envelope; the
    /// `ready` payload is `decodeT`'s to read.
    let deferredOfWith
        (policy: ReadPolicy)
        (decodeT: JVal -> Result<'T, string>)
        (el: JVal)
        : Result<Deferred<'T>, string> =
        under policy strictDeferred el |> Result.bind (fun () -> deferredOf decodeT el)

    /// `decodeDeferred` under a read policy.
    let decodeDeferredWith
        (policy: ReadPolicy)
        (decodeT: JVal -> Result<'T, string>)
        (s: string)
        : Result<Deferred<'T>, string> =
        Decode.parse s |> Result.bind (deferredOfWith policy decodeT)

// ============================================================================
//  Signature-typed function registry (Phase 50) — the artifact-function
//  catalogue an orchestrator (and the content-pack marketplace) enumerate and
//  query BY SIGNATURE: "find functions producing a Document from a {parties,
//  amounts} context", "find an app-archetype with these holes". The tool
//  catalogue and the content-pack library become the SAME object — a typed,
//  queryable index keyed by what a function *produces* (its result type) and
//  what it *requires* (its hole shape), not by name.
//
//  Additive over the FROZEN witnesses (GP1/GP7); it EXTENDS the Phase-30
//  `Capability` registry pattern rather than introducing a parallel one — a
//  registry entry IS an invocable `Capability` (reused verbatim for the typed
//  signature + arg-validated, default-deny dispatch via `Capability.invoke`)
//  annotated with the node-kind it produces. No new trust posture; no new
//  dispatch path. A partially-applied function (a content pack) is a first-
//  class entry whose `Capability.Signature` has been narrowed
//  (`Function.signatureExcluding`, Phase 24) — fewer required holes, no special
//  case. Totality (GP4): typed failures (`InvokeError`), never exceptions.
// ============================================================================

/// A registry entry: an invocable `Capability` (its `Signature` carries the REQUIRED holes — what
/// the function needs) annotated with the node-kind it PRODUCES (`ResultType`). Keying by
/// `(ResultType, required-hole shape)` is what makes the catalogue queryable "by what it produces and
/// what it requires", not by name. The entry's id is its `Capability.Id`.
type FunctionEntry =
    { Capability: Capability
      ResultType: string }

/// A signature query (Phase 50): the desired result type (`None` = any result type — a wildcard on the
/// produce-axis) plus the available-context shape — the holes the caller can fill, each keyed by its
/// absolute address (hygiene). `findBySignature` returns the entries whose REQUIRED holes are all
/// satisfiable from this context.
type SignatureQuery =
    { ResultType: string option
      Available: SigEntry list }

/// How strictly an entry's signature must match a query (Phase 50).
type MatchMode =
    /// The entry's required-hole set is SUBSUMED by the available context — every required hole is
    /// satisfiable (the context supplies it, shape-compatibly) and the context may supply more. The
    /// "find everything I can run with the context I have" query (structural subsumption).
    | Subsumes
    /// The entry's required-hole set EXACTLY equals the available context (same addresses + same hole
    /// shapes) and the result type matches. The "find the function with precisely these holes" query.
    | Exact

/// A signature-typed function registry (Phase 50) — the artifact-function catalogue, queried BY
/// SIGNATURE. `ByResult` is the result-type index (result-kind → the ids producing it), maintained
/// additively so a "produces a Document" query narrows before the hole-shape filter runs. Default-deny
/// by shape on dispatch (only a registered id resolves) — the same trust posture as
/// `CapabilityRegistry`, reusing `Capability.invoke`.
type FunctionRegistry =
    { Entries: Map<string, FunctionEntry>
      ByResult: Map<string, Set<string>> }

/// Populate / enumerate / query / dispatch the signature-typed registry. Additive over the `Capability`
/// surface; FSharp.Core-only, Fable-clean. (`ModuleSuffix` so the module and the `FunctionRegistry`
/// type can share a name — the same idiom as `Option`/`List`.)
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FunctionRegistry =

    let empty: FunctionRegistry =
        { Entries = Map.empty
          ByResult = Map.empty }

    /// Build an entry from the node-kind it produces + an invocable capability.
    let entry (resultType: string) (cap: Capability) : FunctionEntry =
        { Capability = cap
          ResultType = resultType }

    /// Register an entry — additive, no silent overwrite (a duplicate id is a named
    /// `DuplicateCapability`, reusing the Capability registry's error vocabulary). Maintains both the
    /// id map and the result-type index.
    let register (e: FunctionEntry) (r: FunctionRegistry) : Result<FunctionRegistry, InvokeError> =
        let id = e.Capability.Id

        if Map.containsKey id r.Entries then
            Error(DuplicateCapability id)
        else
            let ids =
                r.ByResult
                |> Map.tryFind e.ResultType
                |> Option.defaultValue Set.empty
                |> Set.add id

            Ok
                { Entries = Map.add id e r.Entries
                  ByResult = Map.add e.ResultType ids r.ByResult }

    let tryFind (id: string) (r: FunctionRegistry) : FunctionEntry option = Map.tryFind id r.Entries

    /// Enumerate the registry in a stable order (by id) — the discovery surface; stability is part of
    /// the contract (`registryLaws` certifies it).
    let enumerate (r: FunctionRegistry) : FunctionEntry list = r.Entries |> Map.toList |> List.map snd

    /// Does `required` value-space subsume `available` — is every value the context can supply (a value
    /// in `available`) acceptable to the function (a value in `required`)? I.e. `available ⊆ required`,
    /// the direction that makes the function runnable from the context. Same-constructor numeric/length
    /// ranges compare by bounds; an `Enum` subsumes a subset `Enum`; an `AnyString` required space
    /// subsumes any string-valued space (it accepts all strings). Cross-type never subsumes.
    let private spaceSubsumes (required: ValueSpace) (available: ValueSpace) : bool =
        match required, available with
        | IntRange(rl, rh), IntRange(al, ah) -> rl <= al && ah <= rh
        | FloatRange(rl, rh), FloatRange(al, ah) -> rl <= al && ah <= rh
        | StringLen(rl, rh), StringLen(al, ah) -> rl <= al && ah <= rh
        | Enum rs, Enum als -> als |> List.forall (fun v -> List.contains v rs)
        | AnyString, (StringLen _ | Enum _ | AnyString) -> true
        | SlotTree None, SlotTree _ -> true
        | SlotTree(Some rk), SlotTree(Some ak) -> rk = ak
        | _ -> false

    /// Is a required slot constraint satisfied by an available slot? An unconstrained required slot
    /// (`None`) accepts any available slot; a constrained one needs the same kind.
    let private slotSubsumes (required: string option) (available: string option) : bool =
        match required, available with
        | None, _ -> true
        | Some rk, Some ak -> rk = ak
        | Some _, None -> false

    /// Is a single required hole satisfied by the matching available-context entry (same address)?
    /// value/repeat: kinds agree and the available value-space ⊆ the required space; slot: kinds agree
    /// and the available slot constraint satisfies the required one.
    let private holeSatisfied (req: SigEntry) (av: SigEntry) : bool =
        req.Kind = av.Kind
        && (match req.Kind with
            | "slot" -> slotSubsumes req.Slot av.Slot
            | _ ->
                match req.Space, av.Space with
                | Some rs, Some avs -> spaceSubsumes rs avs
                | _ -> false)

    /// The core signature-search predicate (Phase 50) — does `entry` match `query` under `mode`? Only
    /// the entry's REQUIRED holes gate a match (optional holes never block); hygiene — holes match by
    /// absolute address. `Subsumes`: result type matches (or query wildcard) and every required hole is
    /// satisfiable from the context. `Exact`: result type matches AND the required-hole address set
    /// equals the context address set AND each pair is shape-EQUAL (not merely subsumed).
    let private matchesQuery (mode: MatchMode) (query: SignatureQuery) (entry: FunctionEntry) : bool =
        let availByAddr = query.Available |> List.map (fun e -> e.Addr, e) |> Map.ofList
        let required = entry.Capability.Signature.Holes |> List.filter (fun h -> h.Required)

        let resultMatches =
            match query.ResultType with
            | None -> true
            | Some t -> t = entry.ResultType

        match mode with
        | Subsumes ->
            resultMatches
            && required
               |> List.forall (fun req ->
                   match Map.tryFind req.Addr availByAddr with
                   | Some av -> holeSatisfied req av
                   | None -> false)
        | Exact ->
            let reqAddrs = required |> List.map (fun h -> h.Addr) |> Set.ofList
            let avAddrs = query.Available |> List.map (fun e -> e.Addr) |> Set.ofList

            resultMatches
            && reqAddrs = avAddrs
            && required
               |> List.forall (fun req ->
                   match Map.tryFind req.Addr availByAddr with
                   | Some av ->
                       req.Kind = av.Kind
                       && Function.slotSpaceOf req = Function.slotSpaceOf av
                       && req.Slot = av.Slot
                   | None -> false)

    /// Find every registered function whose signature matches the query under `mode` (Phase 50). A
    /// `Some` result type narrows the candidate set via the `ByResult` index first (the "produces a
    /// Document" key); a `None` result type scans all entries. The surviving candidates are filtered by
    /// the hole-shape predicate (exact or structural-subsumption) and returned id-stable (the same
    /// enumeration-stability contract as `enumerate`).
    let findBySignature (mode: MatchMode) (query: SignatureQuery) (r: FunctionRegistry) : FunctionEntry list =
        let candidateIds =
            match query.ResultType with
            | Some t -> r.ByResult |> Map.tryFind t |> Option.defaultValue Set.empty |> Set.toList
            | None -> r.Entries |> Map.toList |> List.map fst

        candidateIds
        |> List.sort
        |> List.choose (fun id -> Map.tryFind id r.Entries)
        |> List.filter (matchesQuery mode query)

    /// Dispatch an invocation through the registry (Phase 50): resolve the id (default-deny — an
    /// unregistered id is `NoSuchCapability`), then invoke via `Capability.invoke` (arg-validated — the
    /// SAME trust posture as `Registry.dispatch`, no parallel path). The host supplies the body per the
    /// resolved entry's placement, and answers in the `Deferred` envelope.
    ///
    /// It moved with `Capability.invoke` in Phase 210 because it IS `Capability.invoke` — there is no
    /// total projection from `Result<Deferred<'v>, InvokeError>` back to `Result<'v, InvokeError>`
    /// (`Pending` has no `InvokeError` case, and minting one would widen a published union to avoid
    /// carrying the envelope), and a parallel path is the one thing this registry was built not to be.
    let dispatch
        (r: FunctionRegistry)
        (id: string)
        (args: (string * string) list)
        (body: FunctionEntry -> unit -> Deferred<'v>)
        : Result<Deferred<'v>, InvokeError> =
        match Map.tryFind id r.Entries with
        | None -> Error(NoSuchCapability(id, r.Entries |> Map.toList |> List.map fst))
        | Some e -> Capability.invoke e.Capability args (body e)

    /// Partially apply a registered function (Phase 50) — the content-pack formalism. Produce a NEW
    /// entry under `newId` whose signature is `Function.signatureExcluding boundAddrs` of the source's
    /// (the bound holes drop out — a narrowed signature, fewer required holes), with the source's result
    /// type + placement. Registering it makes the content pack a first-class registry entry findable by
    /// its narrowed hole shape (a smaller available context now subsumes it, while the un-narrowed
    /// original still demands the dropped holes). Hygiene: holes drop by absolute address. Does NOT
    /// mutate the source entry.
    let partiallyApply (newId: string) (boundAddrs: Set<string>) (source: FunctionEntry) : FunctionEntry =
        let narrowed = Function.signatureExcluding boundAddrs source.Capability.Signature

        { Capability = Capability.create newId narrowed source.Capability.Placement
          ResultType = source.ResultType }

// ============================================================================
//  Content-pack packaging contract (Phase 57) — a content pack distributes as a
//  set of CURRIED artifact-functions (`FunctionRegistry.partiallyApply`, Phase
//  24/50 — the content-pack formalism) plus a manifest, loading into the
//  signature-typed registry through ONE mechanism across domains (music Artist
//  Packs, legal house-style, CAD manufacturability, a future Model domain's
//  regulatory packs, …) rather than a per-domain bespoke format.
//
//  The contract carries NO pack CONTENT (FGP 6, open-core boundary): a
//  `PackedFunction` names a base function by id, the holes it binds (by absolute
//  address — hygiene), and the base signature VERSION it was curried against. The
//  curried bodies + rules + payload stay registry-side / domain-side — none of it
//  rides on the manifest, which is just ids, addresses, and version tags. The
//  abstractions package therefore depends on no domain payload.
//
//  A pack pins the base signature's FINGERPRINT (its canonical tool-schema shape).
//  Loading recomputes the live fingerprint and refuses a mismatch, so a pack
//  authored against a base signature that has since changed shape fails the load
//  LOUDLY rather than binding against addresses that no longer mean what the pack
//  assumed ("never a silent stale binding" — the Fork-2 hygiene contract at the
//  distribution boundary).
//
//  Additive over the FROZEN registry (GP1/GP7): loading is `partiallyApply` +
//  `register`, the registry's existing default-deny posture, no new dispatch path.
//  FSharp.Core only, Fable-clean. Totality (GP4): a typed `PackLoadError`, never an
//  exception.
// ============================================================================

/// One curried artifact-function in a content pack: curry the base function `BaseId` (a registered
/// `FunctionEntry`) by binding `BoundAddrs` (absolute addresses — hygiene), registering the narrowed
/// result under `NewId` (its nominal tag in the catalogue). `BaseSignatureVersion` is the fingerprint of
/// the base signature the pack was authored against (canonically `ContentPack.signatureFingerprint`);
/// loading recomputes the live fingerprint and refuses a mismatch. Carries NO node / body — content-free
/// (FGP 6): it is the typed DECLARATION of a partial application, not the curried tree itself.
type PackedFunction =
    { NewId: string
      BaseId: string
      BaseSignatureVersion: string
      BoundAddrs: Set<string> }

/// A content-pack manifest (Phase 57): a set of curried artifact-functions + metadata (`Domain`,
/// `PackId`, `PackVersion`). The whole distribution surface a domain ships — and it carries no pack
/// CONTENT (FGP 6): only ids, addresses, version tags. Loadable into any `FunctionRegistry` that holds
/// the pack's base functions, through one mechanism shared across every domain.
type PackManifest =
    { PackId: string
      Domain: string
      PackVersion: int
      Functions: PackedFunction list }

/// Why a content pack was refused at load time — total, and (per the envelope discipline, GP5) it names
/// the failure and enumerates the alternatives where a closed set is expected. Default-deny by shape:
/// only a known base + a matching signature version + a non-duplicate id loads.
type PackLoadError =
    | UnknownBaseFunction of packId: string * baseId: string * known: string list
    | SignatureVersionMismatch of packId: string * baseId: string * declared: string * actual: string
    | PackRegisterFailed of packId: string * newId: string * reason: InvokeError

/// Build / fingerprint / load content packs over the Phase-50 signature-typed registry. Additive over
/// `FunctionRegistry`; FSharp.Core-only, Fable-clean.
module ContentPack =

    /// A stable fingerprint of a signature's SHAPE — the canonical "signature version" a pack pins.
    /// Derived from the canonical tool-schema projection (`Function.toSchema` → `Json.render`) hashed
    /// with the substrate's portable FNV-1a, so it changes iff the base signature's name / holes /
    /// value-spaces / effect change. A pack curried against an old shape therefore fails the load-time
    /// version check (a renamed / re-typed / dropped hole shifts the fingerprint) rather than binding
    /// against addresses that no longer mean what the pack assumed. Deterministic + Fable-clean (the
    /// same FNV-1a arithmetic class as the rest of the substrate's portable hashing).
    let signatureFingerprint (sg: Signature) : string =
        Function.toSchema sg |> Json.render |> Hash.fnv1a

    /// Build a `PackedFunction` against a base ENTRY, fingerprinting its current signature — the
    /// authoring helper, so a pack pins the live shape it was actually curried from (the honest path; a
    /// hand-written `BaseSignatureVersion` is still accepted by the `PackedFunction` literal, and a wrong
    /// one is exactly what the load-time check catches).
    let pack (newId: string) (boundAddrs: Set<string>) (baseEntry: FunctionEntry) : PackedFunction =
        { NewId = newId
          BaseId = baseEntry.Capability.Id
          BaseSignatureVersion = signatureFingerprint baseEntry.Capability.Signature
          BoundAddrs = boundAddrs }

    /// Load a content pack into a signature-typed registry (Phase 57). Each `PackedFunction` curries its
    /// base function (`FunctionRegistry.partiallyApply` — the content-pack formalism) and registers the
    /// narrowed entry under its `NewId`, through the registry's existing default-deny posture (no new
    /// dispatch path). Three guards, default-deny by shape:
    ///   1. the base function must be registered (an unknown base is `UnknownBaseFunction`, enumerating
    ///      the known ids) — a pack cannot conjure a function the host did not register;
    ///   2. the pack's declared base signature version must equal the registry's LIVE fingerprint for
    ///      that base (a mismatch is `SignatureVersionMismatch`, naming both declared + actual) — a pack
    ///      authored against a since-changed signature fails loudly, never binds stale;
    ///   3. the narrowed entry must register without collision (a duplicate `NewId` surfaces the
    ///      registry's own `DuplicateCapability` wrapped as `PackRegisterFailed`).
    /// Total — returns the extended registry or the FIRST `PackLoadError`; the registry threads by value
    /// (no global state, GP2). All-or-nothing: a failing entry returns the error WITHOUT handing back a
    /// partially-extended registry, so a rejected pack never half-loads (the caller keeps its original).
    let load (manifest: PackManifest) (reg: FunctionRegistry) : Result<FunctionRegistry, PackLoadError> =
        let rec go (acc: FunctionRegistry) =
            function
            | [] -> Ok acc
            | (pf: PackedFunction) :: rest ->
                match Map.tryFind pf.BaseId acc.Entries with
                | None ->
                    Error(UnknownBaseFunction(manifest.PackId, pf.BaseId, acc.Entries |> Map.toList |> List.map fst))
                | Some baseEntry ->
                    let actual = signatureFingerprint baseEntry.Capability.Signature

                    if actual <> pf.BaseSignatureVersion then
                        Error(SignatureVersionMismatch(manifest.PackId, pf.BaseId, pf.BaseSignatureVersion, actual))
                    else
                        let curried = FunctionRegistry.partiallyApply pf.NewId pf.BoundAddrs baseEntry

                        match FunctionRegistry.register curried acc with
                        | Ok acc' -> go acc' rest
                        | Error e -> Error(PackRegisterFailed(manifest.PackId, pf.NewId, e))

        go reg manifest.Functions

// ============================================================================
//  Serializable capability pipeline (Phase 35) — a typed capability-DAG declared
//  as DATA, so a multi-step compute (load → invoke → derive → invoke) is
//  replayable end-to-end through the Phase-27 capture seam, not host-side glue.
//  A node's typed output feeds a downstream node's typed arg; an ill-typed edge
//  is a named error at composition time, never a runtime throw.
//
//  Nodes are `Source` (a named data ref) + `Invoke` (a registered capability).
//  A DataFrame `Transform` step participates as a `Capability` whose body runs the
//  evaluator — so the pipeline needs NO dedicated transform node and `Function`
//  takes no `DataFrame` dependency (GP1/GP2: additive over the Capability surface;
//  the data strand stays downstream). FSharp.Core only, Fable-clean.
// ============================================================================

/// Where an `Invoke` node's argument comes from: a literal scalar, or the output of an upstream node
/// (the edge that wires the DAG).
type ArgSource =
    | Literal of value: string
    | FromNode of nodeId: string

/// A node in a capability pipeline. `Source` is a named data ref (the `DataSource.Ref` precedent) with a
/// declared output value-space; `Invoke` runs a registered capability, declaring its output value-space
/// and wiring each arg (by hole address) to a `Literal` or an upstream node's output.
type PipelineNode =
    | Source of id: string * dataRef: string * outputType: ValueSpace
    | Invoke of id: string * capabilityId: string * outputType: ValueSpace * args: (string * ArgSource) list

/// A serializable, typed capability-DAG (Phase 35) — nodes in topological (declaration) order; edges are
/// the `FromNode` arg references. `OpStream.Dag` can host it; type-checked at composition.
type CapabilityPipeline = { Nodes: PipelineNode list }

/// Why a pipeline was rejected — recoverable + enumerated (GP5), never a throw (GP4). Default-deny by
/// shape: only a registered capability + a type-compatible, fully-bound edge set composes.
type PipelineError =
    | DuplicateNode of id: string
    | UnknownNode of id: string
    | PipelineNoSuchCapability of id: string * known: string list
    | PipelineUnknownArg of node: string * arg: string
    | PipelineArgOutOfSpace of node: string * arg: string
    | EdgeTypeMismatch of node: string * arg: string * producer: string * consumer: string
    | PipelineRequiredUnbound of node: string * addrs: string list

/// A pipeline node's argument, resolved for evaluation (Phase 62): an upstream node's realised output, or
/// a declared literal. The engine resolves each `ArgSource` edge into one of these before handing the arg
/// list to the host `body` — so the body sees values, never the wire-level `FromNode` reference.
type PipelineArg<'v> =
    | FromUpstream of 'v
    | LiteralArg of string

/// Why pipeline evaluation failed — recoverable + named (GP5), never a throw (GP4). A `FromNode` edge that
/// references a node not yet evaluated (a forward reference — the pipeline is out of topological order) is
/// `EvalUnknownNode`; a host `body` failure is `EvalNodeFailed`.
type PipelineEvalError =
    | EvalUnknownNode of node: string * missing: string
    | EvalNodeFailed of node: string * message: string

/// Build / type-check / key / serialise / **evaluate** a `CapabilityPipeline`. Additive over the
/// `Capability` surface; FSharp.Core-only, Fable-clean.
module CapabilityPipeline =

    let nodeId (n: PipelineNode) : string =
        match n with
        | Source(id, _, _) -> id
        | Invoke(id, _, _, _) -> id

    let internal nodeOutputType (n: PipelineNode) : ValueSpace =
        match n with
        | Source(_, _, ty) -> ty
        | Invoke(_, _, ty, _) -> ty

    let private spaceTag =
        function
        | IntRange _ -> "int"
        | FloatRange _ -> "float"
        | StringLen _ -> "string"
        | Enum _ -> "enum"
        | AnyString -> "anyString"
        | SlotTree _ -> "slotTree"

    /// Does a producer output value-space `feed` a consumer arg value-space — every value the producer
    /// can emit is acceptable to the consumer (int feeds int or widens to float; a string space feeds
    /// `AnyString`; otherwise same family).
    let private spaceFeeds (producer: ValueSpace) (consumer: ValueSpace) : bool =
        match producer, consumer with
        | IntRange _, (IntRange _ | FloatRange _) -> true
        | FloatRange _, FloatRange _ -> true
        | StringLen _, (StringLen _ | AnyString) -> true
        | Enum _, (Enum _ | AnyString) -> true
        | AnyString, AnyString -> true
        | SlotTree _, SlotTree None -> true
        | a, b -> a = b

    /// Type-check the pipeline against the registry (Phase 35): node ids are unique; every `Invoke`'s
    /// capability resolves (default-deny); every arg addresses a declared scalar hole; a `Literal` is
    /// in-space; a `FromNode` edge's producer output-type `feeds` the arg's value-space (an ill-typed
    /// edge is a named `EdgeTypeMismatch`); every required hole is bound. Total.
    let typeCheck (reg: CapabilityRegistry) (p: CapabilityPipeline) : Result<unit, PipelineError> =
        let ids = p.Nodes |> List.map nodeId
        let nodeById = p.Nodes |> List.map (fun n -> nodeId n, n) |> Map.ofList

        let dup =
            ids
            |> List.countBy id
            |> List.tryPick (fun (k, c) -> if c > 1 then Some k else None)

        match dup with
        | Some d -> Error(DuplicateNode d)
        | None ->
            let rec go =
                function
                | [] -> Ok()
                | Source _ :: rest -> go rest
                | Invoke(nid, capId, _, args) :: rest ->
                    match Registry.tryFind capId reg with
                    | None -> Error(PipelineNoSuchCapability(capId, reg.Capabilities |> Map.toList |> List.map fst))
                    | Some cap ->
                        let holes = cap.Signature.Holes

                        let argFault (addr, src) =
                            match holes |> List.tryFind (fun h -> h.Addr = addr) with
                            | None -> Some(PipelineUnknownArg(nid, addr))
                            | Some h ->
                                match h.Space with
                                | None -> Some(PipelineUnknownArg(nid, addr)) // a spaceless (action) hole — not feedable
                                | Some argSpace ->
                                    match src with
                                    | Literal v ->
                                        if Space.validate argSpace v then
                                            None
                                        else
                                            Some(PipelineArgOutOfSpace(nid, addr))
                                    | FromNode up ->
                                        match Map.tryFind up nodeById with
                                        | None -> Some(UnknownNode up)
                                        | Some upNode ->
                                            if spaceFeeds (nodeOutputType upNode) argSpace then
                                                None
                                            else
                                                Some(
                                                    EdgeTypeMismatch(
                                                        nid,
                                                        addr,
                                                        spaceTag (nodeOutputType upNode),
                                                        spaceTag argSpace
                                                    )
                                                )

                        match args |> List.tryPick argFault with
                        | Some e -> Error e
                        | None ->
                            let bound = args |> List.map fst |> Set.ofList

                            let unbound =
                                holes
                                |> List.filter (fun h -> h.Required && not (bound.Contains h.Addr))
                                |> List.map (fun h -> h.Addr)

                            if List.isEmpty unbound then
                                go rest
                            else
                                Error(PipelineRequiredUnbound(nid, unbound))

            go p.Nodes

    /// Enumerate the pipeline's nodes in declaration (topological) order — the stable discovery surface.
    let enumerate (p: CapabilityPipeline) : PipelineNode list = p.Nodes

    /// The Phase-27 capture key a node's realized result is journalled under: a readable prefix
    /// (`source#<id>#` / `<capId>#<id>#`) + a hash of the node's canonical pre-image. A consumer
    /// threads this as `OpStream.captureEffect`'s `eff` argument, so the whole dataflow replays
    /// byte-identically. Since Phase 225 the hashed pre-image goes through `Hash.canonicalFields`
    /// and covers EVERY component — the ids as well as the arg references, each binding three
    /// fields (addr, `L`/`N`, value) and the bindings in sorted order — so it is injective: two
    /// nodes share a pre-image only when they share every component, whatever `#` or `=` an id or
    /// a literal contains. (It joined `addr=L:value` on the empty string before, the collision
    /// `Query.invocationKey`'s `key_collision` finding named.)
    let nodeInvocationKey (n: PipelineNode) : string =
        match n with
        | Source(id, dref, _) -> "source#" + id + "#" + Hash.fnv1a (Hash.canonicalFields [ id; dref ])
        | Invoke(id, capId, _, args) ->
            let bindings =
                args
                |> List.map (fun (a, s) ->
                    match s with
                    | Literal v -> Hash.canonicalFields [ a; "L"; v ]
                    | FromNode up -> Hash.canonicalFields [ a; "N"; up ])
                |> List.sort
                |> String.concat ""

            capId
            + "#"
            + id
            + "#"
            + Hash.fnv1a (Hash.canonicalFields [ capId; id ] + bindings)

    // ---- wire codec ----

    let private spaceToJ (s: ValueSpace) : JVal =
        match s with
        | IntRange(lo, hi) -> Canon.typed "intRange" [ "min", JInt lo; "max", JInt hi ]
        | FloatRange(lo, hi) -> Canon.typed "floatRange" [ "min", JFloat lo; "max", JFloat hi ]
        | StringLen(lo, hi) -> Canon.typed "stringLen" [ "min", JInt lo; "max", JInt hi ]
        | Enum xs -> Canon.typed "enum" [ "values", JArr(xs |> List.map JStr) ]
        | AnyString -> Canon.typed "anyString" []
        | SlotTree c ->
            Canon.typed
                "slotTree"
                (match c with
                 | Some k -> [ "slotKind", JStr k ]
                 | None -> [])

    let private spaceFromJ (el: JVal) : Result<ValueSpace, string> =
        Decode.strField "$type" el
        |> Result.bind (fun k ->
            match k with
            | "intRange" ->
                Decode.intField "min" el
                |> Result.bind (fun lo -> Decode.intField "max" el |> Result.map (fun hi -> IntRange(lo, hi)))
            | "floatRange" ->
                Decode.getProp "min" el
                |> Result.bind Decode.asFloat
                |> Result.bind (fun lo ->
                    Decode.getProp "max" el
                    |> Result.bind Decode.asFloat
                    |> Result.map (fun hi -> FloatRange(lo, hi)))
            | "stringLen" ->
                Decode.intField "min" el
                |> Result.bind (fun lo -> Decode.intField "max" el |> Result.map (fun hi -> StringLen(lo, hi)))
            | "enum" ->
                Decode.getProp "values" el
                |> Result.bind (Decode.mapList Decode.asString)
                |> Result.map Enum
            | "anyString" -> Ok AnyString
            | "slotTree" ->
                Ok(
                    SlotTree(
                        match Decode.strField "slotKind" el with
                        | Ok k -> Some k
                        | Error _ -> None
                    )
                )
            | other -> Error("unknown value-space: " + other))

    let private argSrcToJ (s: ArgSource) : JVal =
        match s with
        | Literal v -> Canon.typed "literal" [ "value", JStr v ]
        | FromNode n -> Canon.typed "fromNode" [ "node", JStr n ]

    let private argSrcFromJ (el: JVal) : Result<ArgSource, string> =
        Decode.strField "$type" el
        |> Result.bind (fun k ->
            match k with
            | "literal" -> Decode.strField "value" el |> Result.map Literal
            | "fromNode" -> Decode.strField "node" el |> Result.map FromNode
            | other -> Error("unknown arg source: " + other))

    let private argToJ (addr: string, s: ArgSource) : JVal =
        JObj [ "addr", JStr addr; "source", argSrcToJ s ]

    let private argFromJ (el: JVal) : Result<string * ArgSource, string> =
        Decode.strField "addr" el
        |> Result.bind (fun addr ->
            Decode.getProp "source" el
            |> Result.bind argSrcFromJ
            |> Result.map (fun s -> addr, s))

    let private nodeToJ (n: PipelineNode) : JVal =
        match n with
        | Source(id, dref, ty) ->
            Canon.typed "source" [ "id", JStr id; "dataRef", JStr dref; "outputType", spaceToJ ty ]
        | Invoke(id, capId, ty, args) ->
            Canon.typed
                "invoke"
                [ "id", JStr id
                  "capabilityId", JStr capId
                  "outputType", spaceToJ ty
                  "args", JArr(args |> List.map argToJ) ]

    let private nodeFromJ (el: JVal) : Result<PipelineNode, string> =
        Decode.strField "$type" el
        |> Result.bind (fun k ->
            match k with
            | "source" ->
                Decode.strField "id" el
                |> Result.bind (fun id ->
                    Decode.strField "dataRef" el
                    |> Result.bind (fun dref ->
                        Decode.getProp "outputType" el
                        |> Result.bind spaceFromJ
                        |> Result.map (fun ty -> Source(id, dref, ty))))
            | "invoke" ->
                Decode.strField "id" el
                |> Result.bind (fun id ->
                    Decode.strField "capabilityId" el
                    |> Result.bind (fun capId ->
                        Decode.getProp "outputType" el
                        |> Result.bind spaceFromJ
                        |> Result.bind (fun ty ->
                            Decode.getProp "args" el
                            |> Result.bind (Decode.mapList argFromJ)
                            |> Result.map (fun args -> Invoke(id, capId, ty, args)))))
            | other -> Error("unknown pipeline node: " + other))

    /// Encode a pipeline to its canonical wire string.
    let encode (p: CapabilityPipeline) : string =
        Canon.render (JObj [ "nodes", JArr(p.Nodes |> List.map nodeToJ) ])

    /// Decode a pipeline from a wire string (`Result`-typed, named errors).
    let decode (s: string) : Result<CapabilityPipeline, string> =
        Decode.parse s
        |> Result.bind (fun el ->
            Decode.getProp "nodes" el
            |> Result.bind (Decode.mapList nodeFromJ)
            |> Result.map (fun nodes -> { Nodes = nodes }))

    // ---- evaluation + incremental re-evaluation (Phase 62) ----
    // The reference evaluator the incremental path is certified byte-identical to. Core runs NO capability
    // body (GP6 — render/recompute stays domain-side); it topologically walks the DAG (nodes are in
    // declaration = topological order), resolves each `FromNode` edge into the upstream node's realised
    // value, and hands the resolved arg list to the caller-supplied host `body`. This is the capability-DAG
    // analogue of `DataFrame.evalPipelineWith resolve` (Phase 34) — the pipeline plumbing is Core's, the
    // compute is the host's.

    /// Resolve one node's args against the results-so-far, then run the host `body`. Shared by `eval` and
    /// `evalFrom` so the two agree by construction. A `Literal` passes through as a `LiteralArg`; a
    /// `FromNode up` resolves to `up`'s realised value (a forward reference is `EvalUnknownNode`).
    let private runNode
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (results: Map<string, 'v>)
        (n: PipelineNode)
        : Result<'v, PipelineEvalError> =
        let nid = nodeId n

        let resolved =
            match n with
            | Source _ -> Ok []
            | Invoke(_, _, _, args) ->
                (Ok [], args)
                ||> List.fold (fun acc (addr, src) ->
                    acc
                    |> Result.bind (fun xs ->
                        match src with
                        | Literal s -> Ok((addr, LiteralArg s) :: xs)
                        | FromNode up ->
                            match Map.tryFind up results with
                            | Some v -> Ok((addr, FromUpstream v) :: xs)
                            | None -> Error(EvalUnknownNode(nid, up))))
                |> Result.map List.rev

        resolved
        |> Result.bind (fun args ->
            match body n args with
            | Ok v -> Ok v
            | Error m -> Error(EvalNodeFailed(nid, m)))

    /// The reference evaluator (Phase 62): fold the host `body` over the pipeline in declaration
    /// (topological) order, threading an `id → value` result map. `body` receives each node and its args
    /// with every `FromNode` edge already resolved to the upstream value. Total — a forward `FromNode`
    /// reference or a body failure is a named `PipelineEvalError`, never a throw. The cross-host compute
    /// contract the incremental `evalFrom` is certified byte-identical to.
    ///
    /// **It stays SYNCHRONOUS, by decision (Phase 210's routed-out question, operator decision
    /// 2026-09-19).** `Capability.invoke` and both dispatchers carry the `Deferred` envelope; `body`
    /// here returns a plain `Result` and does not. Enveloping it would be a RESUMPTION model rather
    /// than a retype — a fold that meets `Pending` at one node must say what becomes of every node
    /// after it, and `evalFrom`'s byte-identical contract would have to hold across the suspension —
    /// and no demand for one has been measured. Asynchrony belongs at the leaves: a host that must
    /// wait resolves its `Deferred` invocations before it folds the pipeline.
    let eval
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (p: CapabilityPipeline)
        : Result<Map<string, 'v>, PipelineEvalError> =
        let rec go (results: Map<string, 'v>) =
            function
            | [] -> Ok results
            | n :: rest ->
                runNode body results n
                |> Result.bind (fun v -> go (Map.add (nodeId n) v results) rest)

        go Map.empty p.Nodes

    /// The dirty set for a changed-input set: `changed` ∪ every node transitively downstream of it via
    /// `FromNode` edges (the reverse-reachability closure). Minimal — a node not reachable from any change
    /// is never included. Pure over the DAG's edge structure.
    let dirtySet (changed: Set<string>) (p: CapabilityPipeline) : Set<string> =
        // dependents: for each `FromNode up` edge into a node, `up` gains that node as a dependent.
        let dependents =
            [ for n in p.Nodes do
                  match n with
                  | Invoke(id, _, _, args) ->
                      for _, src in args do
                          match src with
                          | FromNode up -> yield up, id
                          | Literal _ -> ()
                  | Source _ -> () ]
            |> List.groupBy fst
            |> List.map (fun (k, vs) -> k, vs |> List.map snd |> Set.ofList)
            |> Map.ofList

        let rec grow (frontier: Set<string>) (acc: Set<string>) =
            if Set.isEmpty frontier then
                acc
            else
                let next =
                    (Set.empty, frontier)
                    ||> Set.fold (fun s node ->
                        match Map.tryFind node dependents with
                        | Some deps -> Set.union s deps
                        | None -> s)

                let fresh = Set.difference next acc
                grow fresh (Set.union acc fresh)

        grow changed changed

    /// Incrementally re-evaluate a pipeline (Phase 62) given the PRIOR evaluation and the set of
    /// changed-input node ids: re-invoke only the nodes downstream-of-change (`dirtySet`), reusing each
    /// clean node's prior result. **Byte-identical to a full `eval` over the same inputs** (the Phase-34
    /// discipline, `Conformance.capabilityPipelineIncrementalLaws`) — a clean node's inputs are unchanged,
    /// so its `eval` result is exactly its prior result; a dirty node re-invokes the body against the new
    /// upstream values. Effect-honesty on the dirty path: a clean node reuses its recorded value (the
    /// Phase-53 gate), an uncaptured live node on the dirty path re-invokes, so incrementality never serves
    /// a stale effect result. A node absent from `prior` (never evaluated) is always (re-)evaluated.
    let evalFrom
        (body: PipelineNode -> (string * PipelineArg<'v>) list -> Result<'v, string>)
        (prior: Map<string, 'v>)
        (changed: Set<string>)
        (p: CapabilityPipeline)
        : Result<Map<string, 'v>, PipelineEvalError> =
        let dirty = dirtySet changed p

        let rec go (results: Map<string, 'v>) =
            function
            | [] -> Ok results
            | n :: rest ->
                let nid = nodeId n

                if not (Set.contains nid dirty) && Map.containsKey nid prior then
                    go (Map.add nid (Map.find nid prior) results) rest
                else
                    runNode body results n |> Result.bind (fun v -> go (Map.add nid v results) rest)

        go Map.empty p.Nodes
