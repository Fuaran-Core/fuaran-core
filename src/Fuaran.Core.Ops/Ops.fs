namespace Fuaran.Core

/// The skeleton edit ops shared by every domain — five structural, and since Phase 250 one
/// generic in-place content edit (`UpdateNode`). Finer per-kind property edits (`SetInput`,
/// `SetParameter`, `SetVariable`, `UpdateProp`, …) stay domain-side and compose with these. `Batch` is all-or-nothing.
///
/// **Membership and order are separate concerns.** `InsertChild` and `MoveNode` change
/// which children a parent has, and both APPEND; `ReorderChildren` states the order, by
/// naming ids. Placing a node anywhere but last is therefore
/// `Batch [InsertChild …; ReorderChildren …]`.
///
/// The ordinal these two ops used to carry was removed deliberately (2026-07-26). Every
/// node has an id, every other op addresses by it, and `ReorderChildren` already stated
/// order that way — so the integer was the one place the structural surface departed from
/// the identity model the rest of the tree is built on. It also named something that does
/// not exist in the tree: children are a list, so order is structural and no index is
/// stored anywhere. An index is a projection over that list, derivable from it and only
/// meaningful against one snapshot of it, which makes it silently wrong after any
/// concurrent or preceding edit. An id is checkable; an ordinal is not.
///
/// **Content and structure are separate concerns too (Phase 250).** `UpdateNode node` rewrites
/// one node IN PLACE: the node whose id is `w.Id node` takes `node`'s own content and KEEPS the
/// children it already has — the payload's children are not read, because changing membership or
/// order is what the other four ops are for. The target is the payload's own id rather than a
/// second field, for the reason the ordinal above was removed: an id stated twice can disagree,
/// and an id stated once cannot. It is declared LAST so every existing case keeps its tag. Before
/// it, redefining a node was `Batch [RemoveNode id; InsertChild(parent, node')]`, which moved the
/// node to the end of its parent and dirtied the parent as well as the node.
type SkeletonOp<'Node, 'Id> =
    /// Append `node` — a whole subtree — as `parent`'s LAST child. An id the subtree repeats, or
    /// one the tree already carries, is refused first (`DuplicateId`); then an absent `parent`.
    | InsertChild of parent: 'Id * node: 'Node
    /// Remove `target` and its whole subtree. The root is refused (`CannotRemoveRoot`), as is an
    /// absent id (`UnknownNode`).
    | RemoveNode of target: 'Id
    /// Detach `target` with its subtree and APPEND it under `newParent`. Refused for the root, an
    /// absent id on either side, and a `newParent` inside the moved subtree (`WouldNestUnderSelf`).
    | MoveNode of target: 'Id * newParent: 'Id
    /// Put `parent`'s children in `order`, which must be a permutation of their ids
    /// (`ReorderMismatch`, carrying the current list, otherwise). Nothing is added or dropped.
    | ReorderChildren of parent: 'Id * order: 'Id list
    /// The ops in order, each against the tree the previous one left. All-or-nothing: the first
    /// refusal is the batch's, and no earlier step survives it.
    | Batch of SkeletonOp<'Node, 'Id> list
    /// Rewrite the node whose id is `w.Id node` in place: it takes `node`'s content and keeps its
    /// own children (the payload's are not read). An absent id is `UnknownNode`.
    | UpdateNode of node: 'Node

/// A domain's REFERENCES (Phase 313): which ids a node refers to, and which ids it declares for
/// others to refer to — the cross-node links a calculation, a feature tree, a set of defined terms or
/// a cross-referenced document carries beside its containment. A witness of its own for the reason
/// `KeyedWitness` is (Phase 189): the engine never rebuilds through it, it only reads it.
///
/// - `RefsOf` — the ids this node refers to, in the domain's own order. `[]` for a node that refers
///   to nothing.
/// - `DeclsOf` — the ids this node declares as reference targets. A node that is itself the target
///   declares its own id (`fun n -> [ w.Id n ]` for a domain where every node is referable); a
///   domain whose names are not node ids declares the names.
///
/// A reference RESOLVES when some node of the tree declares its id. What Core does with the witness:
/// `Validator.referenceIntegrity` reports the dangling, unused, forward and cyclic references;
/// `Ops.footprint` under a `Policy` carrying it reads every id a script writes a reference to; and the
/// engine under that policy refuses a `RemoveNode` that would leave a reference dangling.
type RefWitness<'Node, 'Id> =
    {
        /// The ids this node refers to, in the domain's order; each resolves when some node's
        /// `DeclsOf` carries it.
        RefsOf: 'Node -> 'Id list
        /// The ids this node offers as reference targets — its own id where every node is referable,
        /// the names it defines where names are not node ids.
        DeclsOf: 'Node -> 'Id list
    }

/// A script's refusal (Phase 391): the first op of an op-script the engine refused, with the tree
/// the accepted prefix reached. A script is not a `Batch` — it stops at the first refusal and KEEPS
/// the prefix — so the refusal names how far it got. `Ops.applyAll` answers it on `Error`,
/// under every policy and scope.
type ScriptRejection<'Node, 'Id> =
    {
        /// How many ops were applied before the refusal — so also the 0-based position of the
        /// refused op in the script.
        Applied: int
        /// Why that op was refused.
        Rejection: Rejection<'Id>
        /// The tree the accepted prefix reached: the state the refused op was offered against.
        Tree: 'Node
    }

/// How the engine sees a domain's tree (Phase 517): the structural witness it walks and rebuilds
/// through, the id witness, which nodes can hold children, and the keyed positions when the domain
/// declares them. The engine's entries take one where they used to take a name per combination
/// (`Ops.apply` under `OpScope.contained`, `Ops.apply` under `OpScope.keyed`, `Ops.canApplyAll` under `OpScope.keyed`, ...): a contained or a keyed write
/// is the same entry under another scope, so no check the plain path makes can be bypassed by
/// choosing a spelling. Build one with `OpScope.plain`, `OpScope.contained` or `OpScope.keyed`.
///
/// It is the domain's DECLARATION; the walks the engine derives from it (the keyed traversal) are
/// computed by the engine, so a scope cannot name a locate walk that disagrees with its keyed
/// positions. Named `OpScope` because `Fuaran.Core.Scope<'Id>` (a projection's scope) already
/// shares the namespace, and two types of one name are ambiguous to a caller opening both.
[<NoEquality; NoComparison>]
type OpScope<'Node, 'Id> =
    {
        /// The structural witness: what the engine edits through (`Children` / `ReplaceChildren`),
        /// and what it walks when no keyed position is declared.
        Nodes: NodeWitness<'Node, 'Id>
        /// The id witness every address is keyed by.
        Ids: IdWitness<'Id>
        /// Which nodes can hold children at all (Phase 251). `None` admits every node, which is
        /// what the plain scope means. Asked of every parent an op writes: an insert's parent, a
        /// move's destination, a reorder's parent (Phase 517), an update's rewritten node, and every
        /// interior node of an inserted graft (Phase 161, D38).
        CanHold: ('Node -> bool) option
        /// The keyed positions (Phase 286). `Some` makes the engine locate through
        /// `Tree.traversal Nodes keyw` and see the keyed ids in its duplicate check; it still edits
        /// through `Nodes` alone, so a keyed position is never added to, vacated or reordered.
        Keyed: KeyedWitness<'Node, 'Id> option
    }

/// Building an `OpScope` (Phase 517).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module OpScope =

    /// Every node can hold children; no keyed position.
    let plain (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) : OpScope<'Node, 'Id> =
        { Nodes = w
          Ids = idw
          CanHold = None
          Keyed = None }

    /// Only the nodes `canHold` accepts can hold children (Phase 251); no keyed position.
    let contained (canHold: 'Node -> bool) (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) : OpScope<'Node, 'Id> =
        { plain w idw with
            CanHold = Some canHold }

    /// The keyed scope (Phase 286): `contained` with the domain's keyed positions declared.
    let keyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        : OpScope<'Node, 'Id> =
        { contained canHold w idw with
            Keyed = Some keyw }

    /// The walk the engine locates nodes through: `Tree.traversal` over the keyed positions when
    /// they are declared, the structural witness otherwise.
    let locate (s: OpScope<'Node, 'Id>) : NodeWitness<'Node, 'Id> =
        match s.Keyed with
        | Some keyw -> Tree.traversal s.Nodes keyw
        | None -> s.Nodes

    /// The ids of `node`'s subtree in the walk the scope locates through, preorder.
    let ids (s: OpScope<'Node, 'Id>) (node: 'Node) : 'Id list = Tree.ids (locate s) node

/// Which checks the engine runs beyond its own (Phase 517): the domain's containment grammar
/// (Phase 313) and its references (Phase 313). The refusal order inside one step is the engine's,
/// whichever fields are set: the engine's checks (D38's order), then the grammar (`IllegalChild`),
/// then the references (`StillReferenced`). `Policy.none` is the engine alone.
[<NoEquality; NoComparison>]
type Policy<'Node, 'Id> =
    {
        /// What a parent of each kind may hold (`None` from the function = unconstrained). A step
        /// that would leave a child under a parent whose kind may not hold it is `IllegalChild`.
        Grammar: (string -> string list option) option
        /// The domain's references. A step that would take away a declaration a remaining node still
        /// refers to is `StillReferenced`; the footprint reads every id a script writes a reference to.
        Refs: RefWitness<'Node, 'Id> option
    }

/// Building a `Policy` (Phase 517).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Policy =

    /// The engine's own checks and nothing more.
    let none<'Node, 'Id> : Policy<'Node, 'Id> = { Grammar = None; Refs = None }

    /// The engine's checks, then the containment grammar.
    let grammar (allowedChildren: string -> string list option) : Policy<'Node, 'Id> =
        { Grammar = Some allowedChildren
          Refs = None }

    /// The engine's checks, then the references (no grammar).
    let referenced (refw: RefWitness<'Node, 'Id>) : Policy<'Node, 'Id> = { Grammar = None; Refs = Some refw }

/// The walks the engine derives from an `OpScope` for one entry (Phase 512; named `OpScope` until
/// Phase 517 made that the public declaration): the witness it EDITS through, the one it LOCATES
/// through, the keyed positions, and the id witness. Internal.
[<NoEquality; NoComparison>]
type internal Walks<'Node, 'Id> =
    { Edit: NodeWitness<'Node, 'Id>
      Locate: NodeWitness<'Node, 'Id>
      Keyed: ('Node -> 'Node list) option
      Ids: IdWitness<'Id> }

/// What one non-batch op finds in one tree (Phase 512), located ONCE: the engine checks and edits
/// from it, and the base-aware footprint, the write gate, the grammar and the inverse read it rather
/// than walk the tree again. A field is `None` where the tree does not hold what it names. What an op
/// NAMES, tree-blind, is the pure footprint (`Ops.footprint`, pinned by the `ops/` vectors).
[<NoEquality; NoComparison>]
type internal OpView<'Node, 'Id> =
    {
        Op: SkeletonOp<'Node, 'Id>
        /// An insert's or a reorder's parent; a remove's, a move's or an update's target (subtree).
        Located: 'Node option
        /// A move's new parent.
        Destination: 'Node option
        /// The node whose STRUCTURAL child list holds a remove's or a move's target.
        SourceParent: 'Node option
        /// The keyed position holding a remove's or a move's target when it has no structural
        /// parent: the holder and the target's index in its `KeyedChildren` (keyed scope only).
        KeyedPosition: ('Node * int) option
    }

/// The one script runner's account of a run (Phase 512): the steps taken, what each produced, the
/// tree reached, and the refusal that stopped it.
[<NoEquality; NoComparison>]
type internal ScriptRun<'Node, 'Out, 'E> =
    { Applied: int
      Outs: 'Out list
      Tree: 'Node
      Refused: 'E option }

/// The generic apply engine over the skeleton ops. Total: every failure is a typed
/// `Rejection` envelope. Generic over the `NodeWitness` / `IdWitness` — no domain
/// `NodeKind` is ever in scope.
module Ops =

    // ---- validation (Phase 246) ----
    // One check per op, shared by the dry-run `canApply` and the mutating `apply`, so the two can
    // never diverge; only a `Batch` is order-dependent and simulates through `apply`. The witnesses
    // the checks read are an `OpScope` (Phase 512): `Edit` is what a node's children are for an
    // edit, `Locate` what the engine finds nodes through (`Tree.traversal` under the keyed engine,
    // Phase 286), and `Keyed` the domain's keyed positions, read only by the keyed engine.

    /// The first id in `node`'s subtree (preorder) that breaks uniqueness — one already carried by
    /// `root`, or one the subtree repeats within itself. `None` when the graft is clean.
    ///
    /// **Phase 137** widened this from the graft's own id to its whole subtree; **Phase 139** moved
    /// the scan itself into `Tree.graftWellFormed`, so this is now a projection of Core's ONE named
    /// definition of structural validity rather than a second, separately-maintained copy of it.
    /// The behaviour is unchanged — same seed, same preorder, same first offender — and the point of
    /// the move is that it can no longer drift from `Tree.wellFormed`, which is what a caller
    /// checks the RESULT with. **Phase 286**: over `t`, so under a keyed scope it is
    /// `Tree.graftWellFormedKeyed` and sees keyed positions on both sides of the graft.
    ///
    /// Scope, damage and precedence are all stated where the predicate is defined
    /// (`Tree.WellFormed`); this comment deliberately does not restate them.
    let private firstDuplicateId
        (t: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (node: 'Node)
        (root: 'Node)
        : 'Id option =
        match Tree.graftWellFormed t idw node root with
        | Tree.RepeatedId d -> Some d
        | Tree.Structural -> None

    /// The first node of a graft (preorder) that HOLDS children while `canHold` refuses it — the
    /// interior offender an inserted subtree carries in. `None` when the graft's own interior
    /// already satisfies the invariant a contained scope exists to keep ("every node with children
    /// satisfies `canHold`"). A childless node is unconstrained: the predicate answers "can this
    /// node hold children AT ALL", so a leaf that holds none says nothing.
    ///
    /// **Phase 161 (DECISIONS D38).** `canHold` used to be applied to the PARENT of an insert and
    /// to nothing inside the subtree being inserted, so a graft whose own interior node was a
    /// non-container carried the violation in and the invariant broke across an ACCEPTED operation
    /// — machine-checked as `contained_needs_op_hypothesis` in `proofs/Preservation.fst` before it
    /// was a refusal. The operator's ruling was to inspect the graft.
    ///
    /// The predicate is the contained `Diff.toOps`'s, deliberately: the diff path has walked an `after`
    /// tree for exactly this shape since Phase 09, and the accept path was the one place the check
    /// was missing — word for word Phase 137's situation with the diff's repeated-id refusal.
    ///
    /// **The single definition of the graft-containment shape (Phase 228).** Both refusals of that
    /// shape are raised from THIS function, `Rejection.NotAContainer` on the apply path (through
    /// `validateGraftContainment`) and on the diff path (through `Diff.toOps` under a scope with a
    /// `CanHold`, which calls it over the whole `after` tree; `DiffError.TargetNotAContainer` until
    /// Phase 518), and both name the node it returns under one payload, `target * kindTag`.
    ///
    /// **The walk is the caller's (Phase 286).** It reports the first node `t` reaches that holds
    /// STRUCTURAL children (`w.Children`, the list `canHold` is about) while `canHold` refuses it:
    /// the keyed engine and the keyed diff pass `Tree.traversal`, since a node the keyed walk reaches
    /// is a node of the tree, and every other caller passes `w` itself. It is `internal` rather than
    /// `private` only so the `Diff` module can call it instead of re-stating it; it is not public
    /// surface.
    let internal firstUncontained
        (t: NodeWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (node: 'Node)
        : 'Node option =
        Tree.preorder t node
        |> List.tryFind (fun n -> not (List.isEmpty (w.Children n)) && not (canHold n))

    /// The graft-containment clause of `validateInsert` (Phase 161), factored out so the check has
    /// one name and one home. `NotAContainer` names the offending node in the GRAFT — not the
    /// parent in the tree — reporting that node's own id and kind tag, which is what a caller needs
    /// to repair a subtree it authored.
    ///
    /// **Not called from the `MoveNode` arm, and that is a decision rather than an omission
    /// (D38).** A move relocates a subtree that is already in the tree, so it introduces no interior
    /// structure the tree did not already hold: a violation found inside it was carried in by an
    /// earlier insert, and refusing the move for it would be an invariant-REPAIR gate rather than a
    /// graft check. The machine-checked form of that argument is `contained_preserves`' move clause,
    /// which derives the moved subtree's containment from the tree's own and needs no hypothesis
    /// about the operation at all.
    let private validateGraftContainment
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (node: 'Node)
        : Result<unit, Rejection<'Id>> =
        match firstUncontained t canHold w node with
        | Some offender -> Error(NotAContainer(w.Id offender, w.KindTag offender))
        | None -> Ok()

    // ---- the scope and the per-op view (Phase 512) ----
    // The engine's checks, the base-aware footprint, the gate, the grammar and the inverse each
    // located the op in the tree for themselves; they now read one `OpView`.

    /// The plain scope: locate through the edit witness, no keyed position.
    let internal plainScope (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) : Walks<'Node, 'Id> =
        { Edit = w
          Locate = w
          Keyed = None
          Ids = idw }

    /// The keyed scope (Phase 286's engine): locate through `Tree.traversal`, edit through `nodew`.
    let internal keyedScope
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        : Walks<'Node, 'Id> =
        { Edit = nodew
          Locate = Tree.traversal nodew keyw
          Keyed = Some keyw.KeyedChildren
          Ids = idw }

    /// The walks an `OpScope` declares (Phase 517): the keyed traversal when it declares keyed
    /// positions, the structural witness otherwise.
    let internal walksOf (s: OpScope<'Node, 'Id>) : Walks<'Node, 'Id> =
        match s.Keyed with
        | Some keyw -> keyedScope keyw s.Nodes s.Ids
        | None -> plainScope s.Nodes s.Ids

    /// The scope's container capability; `None` admits every node.
    let internal canHoldOf (s: OpScope<'Node, 'Id>) : 'Node -> bool = defaultArg s.CanHold (fun _ -> true)

    /// The node whose STRUCTURAL child list holds `target`, over every node the locate walk reaches:
    /// `Tree.parentOf w` in the plain scope, and below keyed positions too in the keyed one.
    let internal structuralParentOf (s: Walks<'Node, 'Id>) (target: 'Id) (root: 'Node) : 'Node option =
        Tree.preorder s.Locate root
        |> List.tryFind (fun n -> s.Edit.Children n |> List.exists (fun c -> s.Ids.Equals (s.Edit.Id c) target))

    /// What `op` finds in `root` (Phase 512) — one match on the op, one locate. It refuses nothing.
    let internal view (s: Walks<'Node, 'Id>) (op: SkeletonOp<'Node, 'Id>) (root: 'Node) : OpView<'Node, 'Id> =
        let find (i: 'Id) = Tree.tryFind s.Locate s.Ids i root

        let blank =
            { Op = op
              Located = None
              Destination = None
              SourceParent = None
              KeyedPosition = None }

        // a remove's or a move's target: its structural parent, else the keyed position holding it
        let relocated (target: 'Id) =
            match find target with
            | None -> blank
            | Some sub ->
                let parent = structuralParentOf s target root

                let keyed =
                    match parent, s.Keyed with
                    | None, Some keyedOf ->
                        Tree.preorder s.Locate root
                        |> List.tryPick (fun n ->
                            keyedOf n
                            |> List.tryFindIndex (fun c -> s.Ids.Equals (s.Edit.Id c) target)
                            |> Option.map (fun i -> n, i))
                    | _ -> None

                { blank with
                    Located = Some sub
                    SourceParent = parent
                    KeyedPosition = keyed }

        match op with
        | InsertChild(parent, _)
        | ReorderChildren(parent, _) -> { blank with Located = find parent }
        | UpdateNode node ->
            { blank with
                Located = find (s.Edit.Id node) }
        | RemoveNode target -> relocated target
        | MoveNode(target, newParent) ->
            { relocated target with
                Destination = find newParent }
        | Batch _ -> blank

    /// The ids of the subtree a `RemoveNode` destroys, read through `ids` from the located target.
    let internal destroyedBy (ids: 'Node -> 'Id list) (v: OpView<'Node, 'Id>) : 'Id list =
        match v.Op, v.Located with
        | RemoveNode _, Some sub -> ids sub
        | _ -> []

    /// The nodes an `InsertChild` brings in, preorder over the edit witness: the graft the grammar
    /// reads its created pairs from and the index re-indexes. Tree-blind.
    let internal grafted (w: NodeWitness<'Node, 'Id>) (op: SkeletonOp<'Node, 'Id>) : 'Node list =
        match op with
        | InsertChild(_, node) -> Tree.preorder w node
        | _ -> []

    // ---- the engine: check what the view found, then edit through it ----

    let private validateInsert
        (canHold: 'Node -> bool)
        (s: Walks<'Node, 'Id>)
        (v: OpView<'Node, 'Id>)
        (parent: 'Id)
        (node: 'Node)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        // `Tree.ids` is preorder, so its head is the inserted node's OWN id: the widened scan
        // subsumes the pre-137 root-id check and keeps its precedence over `UnknownNode` rather
        // than quietly reordering the envelope a caller already handles.
        match firstDuplicateId s.Locate s.Ids node root with
        | Some d -> Error(DuplicateId d)
        | None ->
            match v.Located with
            | None -> Error(UnknownNode(parent, Tree.ids s.Locate root))
            | Some p when not (canHold p) -> Error(NotAContainer(parent, s.Edit.KindTag p))
            // Phase 161 — the graft's own interior, checked LAST. The ordering is D38's: no
            // operation that was REFUSED before this phase changes its class, because every
            // earlier clause still fires first. Only operations that were ACCEPTED can now be
            // refused, which is what makes this a widening rather than a re-shuffling — and it
            // is what keeps Phase 137's built-collision conformance arm reaching `DuplicateId`.
            | Some _ -> validateGraftContainment canHold s.Edit s.Locate node

    /// The node an `UpdateNode` leaves behind (Phase 250): the payload's own content over the
    /// children `existing` already holds. The payload's children are never read. Its keyed
    /// positions ARE its content — `ReplaceChildren` does not touch them — so under the keyed engine
    /// they arrive with it, and `validateUpdate` checks what they carry (Phase 286).
    let private updated (w: NodeWitness<'Node, 'Id>) (existing: 'Node) (node: 'Node) : 'Node =
        w.ReplaceChildren node (w.Children existing)

    /// `UpdateNode`'s checks (Phase 250), in order: the target — the payload's own id — must be in
    /// the tree (`UnknownNode`, enumerating the ids that are); and, when the node it rewrites holds
    /// children, the rewritten node must be able to hold them (`NotAContainer`, naming the target
    /// and the NEW kind tag, since that is the kind that refuses). The second check is the
    /// container-aware engine's only: plain `apply` passes a `canHold` that admits everything.
    /// There is no duplicate-id check to make on the structural surface: the rewritten node keeps
    /// its id and its children, so the tree's id set is unchanged by construction.
    ///
    /// **Under the keyed engine (Phase 286) the payload's keyed subtrees are new content**, so two
    /// clauses follow, LAST, in the insert's order: their ids against the tree the target keeps —
    /// every id the keyed walk reaches except those below the target's own outgoing keyed positions
    /// (`DuplicateId`) — and their interior against `canHold` (`NotAContainer`). A payload that
    /// holds no keyed node skips both, which is why a domain with no keyed position is unaffected.
    let private validateUpdate
        (canHold: 'Node -> bool)
        (s: Walks<'Node, 'Id>)
        (v: OpView<'Node, 'Id>)
        (node: 'Node)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        let w = s.Edit
        let t = s.Locate
        let target = w.Id node

        match v.Located with
        | None -> Error(UnknownNode(target, Tree.ids t root))
        | Some existing ->
            let result = updated w existing node

            if not (List.isEmpty (w.Children result)) && not (canHold result) then
                Error(NotAContainer(target, w.KindTag result))
            else
                match s.Keyed with
                | None -> Ok()
                | Some keyedOf ->
                    match keyedOf node with
                    | [] -> Ok()
                    | incoming ->
                        // the tree as the rewrite keeps it: the keyed walk everywhere, except that
                        // the target itself is walked structurally — its outgoing keyed positions
                        // are the ones the payload replaces.
                        let kept =
                            { t with
                                Children =
                                    fun n ->
                                        if s.Ids.Equals (w.Id n) target then
                                            w.Children n
                                        else
                                            t.Children n }

                        match
                            Tree.firstRepeatedId s.Ids (Tree.ids kept root) (incoming |> List.collect (Tree.ids t))
                        with
                        | Some d -> Error(DuplicateId d)
                        | None ->
                            match incoming |> List.tryPick (firstUncontained t canHold w) with
                            | Some offender -> Error(NotAContainer(w.Id offender, w.KindTag offender))
                            | None -> Ok()

    /// Would the step `v` describes be accepted against `root`? One dispatch over the view (Phase
    /// 512), read by `apply` before it edits and by `canApply` instead of editing; nothing here walks
    /// the tree to find a node the view located. A `Batch` view has nothing to check: the runner
    /// checks its steps one by one.
    let private check
        (canHold: 'Node -> bool)
        (s: Walks<'Node, 'Id>)
        (v: OpView<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        let unknown (i: 'Id) =
            Error(UnknownNode(i, Tree.ids s.Locate root))

        let isRoot (i: 'Id) = s.Ids.Equals (s.Edit.Id root) i
        // the nesting check compares string keys, as it always has
        let same (a: 'Id) (b: 'Id) = s.Ids.ToString a = s.Ids.ToString b

        match v.Op with
        | InsertChild(parent, node) -> validateInsert canHold s v parent node root
        | UpdateNode node -> validateUpdate canHold s v node root
        | RemoveNode target when isRoot target -> Error CannotRemoveRoot
        | RemoveNode target ->
            match v.Located, v.KeyedPosition with
            | None, _ -> unknown target
            | Some _, Some(holder, slot) -> Error(KeyedPosition(target, s.Edit.Id holder, slot))
            | Some _, None -> Ok()
        | ReorderChildren(parent, order) ->
            match v.Located with
            | None -> unknown parent
            | Some p ->
                let current = s.Edit.Children p |> List.map s.Edit.Id

                let key xs =
                    xs |> List.map s.Ids.ToString |> List.sort

                if key current <> key order then
                    Error(ReorderMismatch(parent, current, order))
                // Phase 517 (and fuaran-core#492's rule): the engine asks `canHold` of EVERY parent the
                // view says an op writes, and a reorder writes its parent's child list. Checked LAST,
                // D38's order: no reorder refused before this clause changes class, so the only
                // verdict that moves is an ACCEPTED reorder on a node that cannot hold children —
                // the empty reorder of a leaf, which now refuses as a reorder under any other parent
                // the predicate rejects would.
                elif not (canHold p) then
                    Error(NotAContainer(parent, s.Edit.KindTag p))
                else
                    Ok()
        // A move's checks, no tree built (Phase 298), in the order the apply path has always refused
        // in: the root (`CannotRemoveRoot`); an absent target, then an absent new parent
        // (`UnknownNode`); a new parent that cannot hold children (`NotAContainer`); a new parent
        // that is the target or below it (`WouldNestUnderSelf`); a target held directly in a keyed
        // position (`KeyedPosition`, the remove half's refusal).
        | MoveNode(target, _) when isRoot target -> Error CannotRemoveRoot
        | MoveNode(target, newParent) ->
            match v.Located, v.Destination with
            | None, _ -> unknown target
            | Some _, None -> unknown newParent
            | Some _, Some np when not (canHold np) -> Error(NotAContainer(newParent, s.Edit.KindTag np))
            | Some sub, Some _ when Tree.ids s.Locate sub |> List.exists (fun i -> same i newParent) ->
                let relation =
                    if same newParent target then
                        NestRelation.Self
                    else
                        NestRelation.Descendant

                Error(WouldNestUnderSelf(target, relation))
            | Some _, Some _ ->
                match v.KeyedPosition with
                | Some(holder, slot) -> Error(KeyedPosition(target, s.Edit.Id holder, slot))
                | None -> Ok()
        | Batch _ -> Ok()

    /// The edit of a step `check` accepted, through the nodes the view located. The `UnknownNode`
    /// fallback is unreachable for a view of `root` itself, and total rather than a throw (GP4).
    let private edit (s: Walks<'Node, 'Id>) (v: OpView<'Node, 'Id>) (root: 'Node) : Result<'Node, Rejection<'Id>> =
        let w = s.Edit
        let eq = s.Ids.Equals

        let at (id: 'Id) (f: 'Node -> 'Node) (tree: 'Node) =
            Tree.updateNode s.Locate s.Ids id f tree
            |> Option.map Ok
            |> Option.defaultValue (Error(UnknownNode(id, Tree.ids s.Locate tree)))

        let detach (target: 'Id) (tree: 'Node) =
            match v.SourceParent with
            | None -> Error(UnknownNode(target, Tree.ids s.Locate tree))
            | Some p ->
                at
                    (w.Id p)
                    (fun p -> w.ReplaceChildren p (w.Children p |> List.filter (fun c -> not (eq (w.Id c) target))))
                    tree

        match v.Op with
        | InsertChild(parent, node) -> at parent (fun p -> w.ReplaceChildren p (w.Children p @ [ node ])) root
        | RemoveNode target -> detach target root
        | ReorderChildren(parent, order) ->
            // the permutation is computed from the parent `updateNode` hands over, so the parent
            // is located once (Phase 298)
            let permute (p: 'Node) =
                let byId =
                    w.Children p |> List.map (fun c -> s.Ids.ToString(w.Id c), c) |> Map.ofList

                w.ReplaceChildren p (order |> List.map (fun i -> byId[s.Ids.ToString i]))

            at parent permute root
        | MoveNode(target, newParent) ->
            // remove, then graft the subtree the view located. newParent is not below the target
            // (`validateMove`), so the removal leaves it in the tree for the graft.
            match v.Located with
            | None -> Error(UnknownNode(target, Tree.ids s.Locate root))
            | Some sub ->
                detach target root
                |> Result.bind (at newParent (fun np -> w.ReplaceChildren np (w.Children np @ [ sub ])))
        | UpdateNode node -> at (w.Id node) (fun existing -> updated w existing node) root
        | Batch _ -> Ok root

    /// One step of the engine over a view already located: check, then edit.
    let internal applyView
        (canHold: 'Node -> bool)
        (s: Walks<'Node, 'Id>)
        (v: OpView<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        check canHold s v root |> Result.bind (fun () -> edit s v root)

    // ---- the one script runner (Phase 512) ----
    // Ten loops spelled one fold. `run` is it; `batch` reads it all-or-nothing, `script` first
    // refusal wins with the prefix kept, and a reader collecting each step's output reads `run`.

    /// Thread `step` through `ops` from `root`: each step sees the tree the steps before it left, and
    /// the first refusal stops the run. Total; iterative over the script.
    let internal run
        (step: 'Node -> 'Op -> Result<'Node * 'Out, 'E>)
        (ops: 'Op list)
        (root: 'Node)
        : ScriptRun<'Node, 'Out, 'E> =
        let rec go i (outs: 'Out list) node =
            function
            | [] ->
                { Applied = i
                  Outs = List.rev outs
                  Tree = node
                  Refused = None }
            | o :: rest ->
                match step node o with
                | Ok(next, out) -> go (i + 1) (out :: outs) next rest
                | Error e ->
                    { Applied = i
                      Outs = List.rev outs
                      Tree = node
                      Refused = Some e }

        go 0 [] root ops

    /// All-or-nothing: the tree every step reached, or the first refusal alone (`Batch`'s contract).
    let internal batch (step: 'Node -> 'Op -> Result<'Node, 'E>) (ops: 'Op list) (root: 'Node) : Result<'Node, 'E> =
        let r = run (fun node o -> step node o |> Result.map (fun n -> n, ())) ops root

        match r.Refused with
        | None -> Ok r.Tree
        | Some e -> Error e

    /// First refusal wins and the prefix is kept: the tree every step reached, or the
    /// `ScriptRejection` naming how many applied, why the next was refused and the tree it was
    /// offered against (`applyAll`'s contract, Phase 391).
    let internal script
        (step: 'Node -> SkeletonOp<'Node, 'Id> -> Result<'Node, Rejection<'Id>>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, ScriptRejection<'Node, 'Id>> =
        let r = run (fun node o -> step node o |> Result.map (fun n -> n, ())) ops root

        match r.Refused with
        | None -> Ok r.Tree
        | Some e ->
            Error
                { Applied = r.Applied
                  Rejection = e
                  Tree = r.Tree }

    /// The shared apply engine (Phase 251) over the walks (Phase 512) and a container capability:
    /// every public entry derives both from its `OpScope` (Phase 517). A `Batch` is the runner's
    /// all-or-nothing form.
    let rec private applyWith
        (canHold: 'Node -> bool)
        (s: Walks<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        match op with
        | Batch ops -> batch (fun node o -> applyWith canHold s o node) ops root
        | _ -> applyView canHold s (view s op root) root

    /// A script's dry run in the `canApplyAll` shape: the 0-based position of the first refused op
    /// and its envelope, or `Ok ()`. The tree is threaded (each step is checked against the tree
    /// the steps before it leave) and discarded.
    let private dryRun
        (engine: SkeletonOp<'Node, 'Id> -> 'Node -> Result<'Node, Rejection<'Id>>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        match script (fun node o -> engine o node) ops root with
        | Ok _ -> Ok()
        | Error r -> Error(r.Applied, r.Rejection)

    // ---- the index, maintained through an edit (Phase 317) ----
    // `Tree.Index.build` is O(n) and `isFreshFor` reports staleness after every op, so a consumer
    // holding an index across an edit session either rebuilt it per op or read it stale. `afterOp`
    // carries it through the op instead, touching what the op touched. It lives here, not beside
    // `build`, because `SkeletonOp` is declared here and `Tree` precedes this module.

    /// Index maintenance across the skeleton ops (Phase 317).
    [<RequireQualifiedAccess>]
    module Index =

        // What an op (or a batch of them) did to the tree, tracked by id without the intermediate
        // trees: the child-id lists it rewrote, the nodes it grafted, the parent links it moved, the
        // indexed nodes it removed, and every id whose own record or presence may differ.
        type private Track<'Node, 'Id> =
            { Kids: Map<string, 'Id list>
              Grafted: Map<string, 'Node>
              Parent: Map<string, 'Id>
              Gone: Set<string>
              Touched: Set<string> }

        /// The index of `post` — the tree `apply` returned for the tree `ix` indexes —
        /// carried through `op` rather than rebuilt (Phase 317). Every op kind is handled, a
        /// `Batch` folded through its steps without the intermediate trees: the op names the
        /// containers it rewrote and the subtrees it grafted or removed, and only those, their
        /// ancestors (whose node values every edit rebuilds) and the grafted and removed subtrees
        /// are re-indexed, through `Tree.Index.rebind`.
        ///
        /// **The law:** for `Ok ix = Tree.Index.build w idw pre`, `afterOp w idw op post ix ≡
        /// Tree.Index.build w idw post` — the same `Root` and `ParentOf`, the same `ById` keys, and at each key a node
        /// equal to `post`'s (a node outside the edit keeps the value the index held, which `apply`
        /// rebuilt from the same content). So `isFreshFor w idw encode post` holds of it under any
        /// `encode`, a content-only `UpdateNode` included (the updated node is re-indexed).
        ///
        /// **The cost** is the op's, not the tree's: per step, the depth times the fanout along each
        /// rewritten container's path, plus the size of each grafted, removed or rewritten subtree's
        /// root record — and never a walk of the tree. **The guard:** what the tracking predicts is
        /// checked against `post` on the nodes it re-indexes (the root's id, every re-indexed node
        /// found where the tracking puts it, with the children the tracking says it has); a `post`
        /// that disagrees — an op `apply` refused, a tree from elsewhere, an index built under
        /// another witness — falls back to `Tree.Index.build w idw post`, so the law holds whatever
        /// the caller hands it, and only the cost depends on the contract. A `Result` since Phase
        /// 518, because that fallback is `build`'s, which refuses a `post` carrying an id twice
        /// (`Error d`, the id) rather than index it.
        let afterOp
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (op: SkeletonOp<'Node, 'Id>)
            (post: 'Node)
            (ix: Tree.NodeIndex<'Node, 'Id>)
            : Result<Tree.NodeIndex<'Node, 'Id>, 'Id> =
            let key (i: 'Id) = idw.ToString i
            let keyOf (n: 'Node) = key (w.Id n)

            let exists (st: Track<'Node, 'Id>) (k: string) =
                st.Grafted.ContainsKey k || (ix.ById.ContainsKey k && not (st.Gone.Contains k))

            let kidsOf (st: Track<'Node, 'Id>) (k: string) : 'Id list option =
                match Map.tryFind k st.Kids with
                | Some ks -> Some ks
                | None ->
                    match Map.tryFind k st.Grafted with
                    | Some n -> Some(w.Children n |> List.map w.Id)
                    | None ->
                        if st.Gone.Contains k then
                            None
                        else
                            Map.tryFind k ix.ById |> Option.map (fun n -> w.Children n |> List.map w.Id)

            let without (target: 'Id) (ks: 'Id list) =
                ks |> List.filter (fun c -> not (idw.Equals c target))

            let rec step (st: Track<'Node, 'Id>) (op: SkeletonOp<'Node, 'Id>) : Track<'Node, 'Id> option =
                match op with
                | InsertChild(parent, node) ->
                    let pk = key parent

                    match (if exists st pk then kidsOf st pk else None) with
                    | None -> None
                    | Some ks ->
                        let graft = grafted w op

                        Some
                            { st with
                                Kids = Map.add pk (ks @ [ w.Id node ]) st.Kids
                                Grafted = (st.Grafted, graft) ||> List.fold (fun m n -> Map.add (keyOf n) n m)
                                Parent =
                                    (Map.add (keyOf node) parent st.Parent, graft)
                                    ||> List.fold (fun m n ->
                                        (m, w.Children n) ||> List.fold (fun m c -> Map.add (keyOf c) (w.Id n) m))
                                Touched = (Set.add pk st.Touched, graft) ||> List.fold (fun s n -> Set.add (keyOf n) s) }

                | RemoveNode target ->
                    let tk = key target

                    match Map.tryFind tk st.Parent with
                    | Some parent when exists st tk ->
                        let pk = key parent

                        match kidsOf st pk with
                        | None -> None
                        | Some ks ->
                            // the subtree as the tracking holds it NOW (earlier steps included)
                            let rec collect (acc: string list) (stack: string list) =
                                match stack with
                                | [] -> Some acc
                                | k :: rest ->
                                    match kidsOf st k with
                                    | None -> None
                                    | Some cs -> collect (k :: acc) ((cs |> List.map key) @ rest)

                            match collect [] [ tk ] with
                            | None -> None
                            | Some sub ->
                                Some
                                    { Kids =
                                        (Map.add pk (without target ks) st.Kids, sub)
                                        ||> List.fold (fun m k -> Map.remove k m)
                                      Grafted = (st.Grafted, sub) ||> List.fold (fun m k -> Map.remove k m)
                                      Parent = (st.Parent, sub) ||> List.fold (fun m k -> Map.remove k m)
                                      Gone =
                                        (st.Gone, sub)
                                        ||> List.fold (fun s k -> if ix.ById.ContainsKey k then Set.add k s else s)
                                      Touched = (Set.add pk st.Touched, sub) ||> List.fold (fun s k -> Set.add k s) }
                    | _ -> None

                | MoveNode(target, newParent) ->
                    let tk = key target
                    let nk = key newParent

                    match Map.tryFind tk st.Parent with
                    | Some parent when exists st tk && exists st nk ->
                        let pk = key parent

                        match kidsOf st pk with
                        | None -> None
                        | Some ks ->
                            // remove, then append — `apply`'s order, which matters when the two parents are one
                            let kids1 = Map.add pk (without target ks) st.Kids

                            match kidsOf { st with Kids = kids1 } nk with
                            | None -> None
                            | Some nks ->
                                Some
                                    { st with
                                        Kids = Map.add nk (nks @ [ target ]) kids1
                                        Parent = Map.add tk newParent st.Parent
                                        Touched = st.Touched |> Set.add pk |> Set.add nk }
                    | _ -> None

                | ReorderChildren(parent, order) ->
                    let pk = key parent

                    if exists st pk then
                        Some
                            { st with
                                Kids = Map.add pk order st.Kids
                                Touched = Set.add pk st.Touched }
                    else
                        None

                | UpdateNode node ->
                    let tk = keyOf node

                    if exists st tk then
                        Some
                            { st with
                                Touched = Set.add tk st.Touched }
                    else
                        None

                | Batch ops ->
                    (Some st, ops)
                    ||> List.fold (fun acc o -> acc |> Option.bind (fun s -> step s o))

            let start =
                { Kids = Map.empty
                  Grafted = Map.empty
                  Parent = ix.ParentOf
                  Gone = Set.empty
                  Touched = Set.empty }

            let incremental (st: Track<'Node, 'Id>) : Tree.NodeIndex<'Node, 'Id> option =
                // every touched id still present, and its ancestors, are re-indexed from `post`
                let affected =
                    (Set.empty, st.Touched)
                    ||> Set.fold (fun acc k ->
                        if not (exists st k) then
                            acc
                        else
                            let rec up (a: Set<string>) (cur: string) =
                                if a.Contains cur then
                                    a
                                else
                                    let a' = Set.add cur a

                                    match Map.tryFind cur st.Parent with
                                    | Some p -> up a' (key p)
                                    | None -> a'

                            up acc k)

                let rootKey = key ix.Root

                if not (idw.Equals (w.Id post) ix.Root) then
                    None
                elif Set.isEmpty affected then
                    Some(Tree.Index.rebind w idw [] [] ix)
                elif not (affected.Contains rootKey) then
                    None
                else
                    // find each affected node in `post` by descending from the root through the
                    // affected set — every affected node's ancestors are affected
                    let rec locate (found: 'Node list) (queue: 'Node list) =
                        match queue with
                        | [] -> found
                        | n :: rest ->
                            let next = w.Children n |> List.filter (fun c -> affected.Contains(keyOf c))
                            locate (n :: found) (next @ rest)

                    let arriving = locate [] [ post ]

                    let agrees (n: 'Node) =
                        match kidsOf st (keyOf n) with
                        | Some ks -> (ks |> List.map key) = (w.Children n |> List.map keyOf)
                        | None -> false

                    if List.length arriving <> Set.count affected || not (List.forall agrees arriving) then
                        None
                    else
                        let leaving =
                            Set.union affected st.Touched
                            |> Set.toList
                            |> List.choose (fun k -> Map.tryFind k ix.ById)

                        Some(Tree.Index.rebind w idw leaving arriving ix)

            match step start op |> Option.bind incremental with
            | Some ix' -> Ok ix'
            | None -> Tree.Index.build w idw post

    /// Derive the inverse of an op from the **pre-state** tree (the tree the op applied to)
    /// — Phase 242. Every skeleton op's inverse is recoverable from the pre-state:
    /// insert↔remove, remove↔insert (capturing the removed subtree, its parent and its sibling order),
    /// move↔move-back (prior parent and sibling order), reorder↔reorder (prior order), update↔update (the
    /// pre-state node, whose content the undo restores — Phase 250). `Batch` inverts to its
    /// inverses in reverse order (each derived against the state that op saw). Total: a non-applyable op has no inverse — its `Rejection` is
    /// returned. The defining law: `apply (invert op pre) (apply op pre) = pre`. Undo/redo
    /// becomes a generic capability over the witness, not a per-domain re-implementation.
    let rec invert
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (pre: 'Node)
        : Result<SkeletonOp<'Node, 'Id>, Rejection<'Id>> =

        // The pre-state order of a parent's children, by id. `InsertChild` / `MoveNode`
        // append, so restoring a node to where it was is two steps: put it back, then
        // restate the order it was part of. The order is read from the pre-state, exactly
        // as the index used to be — the same information, named rather than counted.
        let orderIn (parentNode: 'Node) =
            parentNode |> w.Children |> List.map w.Id

        // Restore `target` under `parentNode` at the position it held in `pre`. A single
        // op when it was already last (the append lands it correctly); otherwise the
        // append plus the order it belonged to.
        let restoring (parentNode: 'Node) (target: 'Id) (put: SkeletonOp<'Node, 'Id>) =
            let order = orderIn parentNode

            let wasLast =
                match List.tryLast order with
                | Some lastId -> idw.Equals lastId target
                | None -> false

            if wasLast then
                put
            else
                Batch [ put; ReorderChildren(w.Id parentNode, order) ]

        match op with
        | Batch ops ->
            // Thread `pre` through the forward ops; invert each against the state it saw. The
            // runner collects the inverses in forward order, and the undo batch runs them reversed.
            let r = run (inverting w idw) ops pre

            match r.Refused with
            | Some e -> Error e
            | None -> Ok(Batch(List.rev r.Outs))

        | _ ->
            // A single op is invertible iff it would apply to `pre`; its inverse is read from the
            // view `check` accepted (Phase 512), so the pre-state reads are the nodes it located.
            let s = plainScope w idw
            let v = view s op pre

            match check (fun _ -> true) s v pre with
            | Error e -> Error e
            | Ok() ->
                match op, v.Located, v.SourceParent with
                | InsertChild(_, node), _, _ -> Ok(RemoveNode(w.Id node))
                | RemoveNode target, Some sub, Some parent ->
                    Ok(restoring parent target (InsertChild(w.Id parent, sub)))
                | MoveNode(target, _), _, Some parent -> Ok(restoring parent target (MoveNode(target, w.Id parent)))
                | ReorderChildren(parent, _), Some p, _ -> Ok(ReorderChildren(parent, orderIn p))
                // The pre-state node's CONTENT restores the content; the inverse carries it as a
                // shell (`ReplaceChildren old []`, Phase 305), because `UpdateNode` never reads its
                // payload's children — the children the tree holds when the undo runs are kept —
                // and an undo stack that stored the whole pre-state subtree per edit held, and
                // never read, a copy of everything below the node.
                | UpdateNode _, Some existing, _ -> Ok(UpdateNode(w.ReplaceChildren existing []))
                // unreachable after `check` (the view located every node read above): total
                // refusals where the five `Option.get` this replaced would have thrown (GP4)
                | RemoveNode target, _, _
                | MoveNode(target, _), _, _ -> Error(UnknownNode(target, Tree.ids w pre))
                | ReorderChildren(parent, _), _, _ -> Error(UnknownNode(parent, Tree.ids w pre))
                | UpdateNode node, _, _ -> Error(UnknownNode(w.Id node, Tree.ids w pre))
                | Batch _, _, _ -> Ok op // handled above

    /// One step of an inverse script: the inverse of `o` against `state`, and the state `o` leaves.
    and private inverting
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (state: 'Node)
        (o: SkeletonOp<'Node, 'Id>)
        : Result<'Node * SkeletonOp<'Node, 'Id>, Rejection<'Id>> =
        invert w idw o state
        |> Result.bind (fun inv ->
            applyWith (fun _ -> true) (plainScope w idw) o state
            |> Result.map (fun next -> next, inv))

    /// The script-level inverse (Phase 305): the inverses of `ops`, each derived against the state
    /// the forward op saw, in REVERSE order — so, under the plain scope, `applyAll (invertAll w idw
    /// ops pre) (applyAll ops pre) = pre`, the law `invert` states per op lifted to the sequence. It refuses as
    /// `applyAll` does, with the 0-based index of the first op that does not apply (or cannot be
    /// inverted, which is the same op: `invert` refuses exactly what `canApply` refuses) and its
    /// envelope.
    ///
    /// **Why a script and not `invert (Batch ops)`.** That form returns a `Batch`, and a `Batch` is
    /// all-or-nothing INSIDE one operation where a script stops at the first refusal and keeps the
    /// accepted prefix — so an undo stack that recorded the SCRIPT it applied, and inverted it as a
    /// batch, would undo with a different failure shape from the one it did. `invertAll` stores the
    /// shape it applied. The elements are the per-op inverses, so a `Batch` inside `ops` inverts to
    /// one `Batch` as `invert` has always made it.
    let invertAll
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (pre: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, int * Rejection<'Id>> =
        let r = run (inverting w idw) ops pre

        match r.Refused with
        | Some e -> Error(r.Applied, e)
        | None -> Ok(List.rev r.Outs)

    /// Normalise an op script (Phase 23): a conservative, structural peephole that collapses the
    /// redundancy classes it can prove safe *without the tree*, leaving everything else untouched.
    /// The defining law (certified by `Conformance.normalizeLaws`): for any script applyable to a
    /// tree, `applyAll (normalize ops) = applyAll ops` — normalisation never changes the result.
    /// Collapses, all on adjacent ops so no intervening op observes the discarded state:
    ///   - `InsertChild(p, node)` then `RemoveNode(id node)` — insert-then-remove of the same node
    ///     nets to nothing (the insert appends and the remove takes it back, so later ops are unaffected);
    ///   - `MoveNode(t, _)` then `MoveNode(t, p)` — the first relocation is superseded by the
    ///     second (the intermediate parent is restored, since `t` is not in `p`'s subtree in any
    ///     applyable script), so only the net move survives;
    ///   - `ReorderChildren(p,_)` then `ReorderChildren(p,o)` — a reorder sets the full order, so the
    ///     last one on a parent wins;
    ///   - (Phase 305, D65's case) `UpdateNode a` then `UpdateNode a'` of the same id — the last
    ///     rewrite wins, since each keeps the children and replaces the content whole;
    ///     `InsertChild(p, n)` then `UpdateNode n'` of `n`'s id — one insert carrying `n'`'s content
    ///     over `n`'s children (what the pair leaves in the tree); `UpdateNode n` then
    ///     `RemoveNode (id n)` — the remove, since a rewrite of a node about to leave is unobservable;
    ///   - an empty `Batch []` is dropped, and a `Batch` is normalised recursively.
    /// It is **idempotent** (`normalize ∘ normalize = normalize`) and never lengthens a script.
    /// `'Node` needs no equality (it compares ids only). **Caveat:** preservation is guaranteed only
    /// for a script that is *applyable* to the tree — collapsing an insert/remove pair can turn an
    /// `applyAll` that would have *failed* at that pair into one that succeeds, so normalise after
    /// validating, not before.
    ///
    /// **One left fold with an output stack (Phase 305).** Each op is pushed onto the ops already
    /// committed; a push that collapses with the top replaces or drops it and re-examines the new
    /// top, so a collapse that newly adjoins two collapsible ops (a cancelled insert/remove between
    /// two same-target moves) is caught in the same pass. Linear in the script, a loop rather than a
    /// recursion over it — the recursive peephole it replaces, iterated to a fixpoint, overflowed the
    /// stack at about 2,000 flat ops — and the output has no adjacent collapsible pair by
    /// construction, which is what makes a second pass the identity.
    let rec normalize
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : SkeletonOp<'Node, 'Id> list =
        let eq = idw.Equals

        // What the committed top and the incoming op collapse to, if they collapse: `None` keeps
        // both, `Some []` drops both, `Some [y]` replaces the top with `y`.
        let collapse (top: SkeletonOp<'Node, 'Id>) (x: SkeletonOp<'Node, 'Id>) =
            match top, x with
            | InsertChild(_, node), RemoveNode target when eq (w.Id node) target -> Some []
            | MoveNode(t1, _), MoveNode(t2, _) when eq t1 t2 -> Some [ x ]
            | ReorderChildren(p1, _), ReorderChildren(p2, _) when eq p1 p2 -> Some [ x ]
            | UpdateNode a, UpdateNode a' when eq (w.Id a) (w.Id a') -> Some [ x ]
            | InsertChild(p, n), UpdateNode n' when eq (w.Id n) (w.Id n') ->
                Some [ InsertChild(p, w.ReplaceChildren n' (w.Children n)) ]
            | UpdateNode n, RemoveNode target when eq (w.Id n) target -> Some [ x ]
            | _ -> None

        // Push `x` onto the stack, collapsing against the top for as long as it collapses. The loop
        // is bounded by the stack's depth and pops at every turn, so the whole fold is O(n).
        let push (stack: SkeletonOp<'Node, 'Id> list) (x: SkeletonOp<'Node, 'Id>) =
            let mutable stack = stack
            let mutable pending = Some x

            while Option.isSome pending do
                let x = Option.get pending

                match stack with
                | top :: rest ->
                    match collapse top x with
                    | None ->
                        stack <- x :: stack
                        pending <- None
                    | Some [] ->
                        stack <- rest
                        pending <- None
                    | Some(y :: _) ->
                        stack <- rest
                        pending <- Some y
                | [] ->
                    stack <- [ x ]
                    pending <- None

            stack

        // Batches are normalised inside first and dropped when empty; everything else is pushed.
        (([], ops)
         ||> List.fold (fun stack op ->
             match op with
             | Batch inner ->
                 match normalize w idw inner with
                 | [] -> stack
                 | xs -> push stack (Batch xs)
             | _ -> push stack op))
        |> List.rev

    // ---- footprint + independence (Phase 78) ----
    // The multi-agent coordination invariant, computed structurally from the op-script (never
    // separately declared — the `paramsOf` precedent, Phase 77). `footprint` is a pure, total union-fold
    // over the skeleton ops through the witnesses; `independent` is pairwise footprint disjointness.
    // The structural basis for dispatch-time conflict refusal (a downstream dispatcher's computed
    // leases), lease derivation (Phase 84), and proposal arbitration (Phase 85).
    //
    // Conservativity is the contract (STABILITY.md "Op-script footprint + independence"): where an
    // exact read/write set is a tree fact the script cannot name, over-approximate — report *dependent*.
    // `independent = true` is a promise; `independent = false` is always safe. The pinned
    // over-approximations, enumerated in `Footprint`'s doc-comment and STABILITY.md:
    //   (1) a RemoveNode/MoveNode's SOURCE parent (and any ancestor relationship) is unknown from the
    //       script, so it lands in `UnknownParentWrites` and conflicts with every structural write in a
    //       concurrent script — disjoint-subtree independence is NOT proven when either side removes or
    //       moves (that needs the tree);
    //   (2) a RemoveNode's `ContentWrites` records only the target id, not its (tree-unknown) subtree.
    //       Sound for the skeleton ops because every one of them — `UpdateNode` included, which is why
    //       its footprint carries an unknown-parent write (Phase 250) — is a structural write, so (1)
    //       already serialises a remove/move against any concurrent op; a domain that layers its OWN
    //       in-place content op on top, with no unknown-parent write, must fold the removed subtree in
    //       itself (it has the tree).
    //
    // Phase 143 asked whether (1) could now be TIGHTENED, with Phase 138's preservation theorem in
    // hand: a relocation ought to commute with a structural write under an unrelated parent. It
    // cannot be, over THIS record, and that is now a theorem rather than a suspicion
    // (`proofs/TreeOps.fst` section 18, `relocation_clause_is_necessary`). The witness is one
    // well-formed tree and three ops: a `MoveNode` and a structural write under a parent inside the
    // moved subtree DO commute (`relocation_disjoint_diamond`); the same shape with a `RemoveNode`
    // in it does NOT, because the insert's parent is destroyed with the subtree
    // (`relocation_diamond_fails_for_a_remove`); and the two ops carry the SAME address sets — the
    // four node sets then, and the six since Phase 340, whose two slot sets are empty for every
    // skeleton op (`relocation_footprints_coincide`). A predicate over footprints alone gives one verdict to
    // both, so freeing the safe pair frees the fatal one. That is (2) being load-bearing for (1)
    // and (1) being load-bearing for (2), each proved rather than asserted.
    //
    // So the last two clauses of `independent` below are NECESSARY for a footprint of the script
    // alone. The tightening needs the tree — the destroyed subtree, the source parent, a move's
    // destination ancestors — and is `footprintIn` (Phase 500, D166), below, where the base is known.

    // The one fold behind `footprint` over a plain and a keyed scope (Phase 247). `carried` reads the ids an
    // inserted subtree carries; `introduced` the ids an `UpdateNode` payload brings in beyond its own
    // target. The unkeyed form passes `Tree.ids w` and nothing, which is exactly the fold `footprint`
    // was before the keyed form existed.
    let private footprintOver
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (carried: 'Node -> 'Id list)
        (introduced: 'Node -> 'Id list)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        let key (i: 'Id) = idw.ToString i

        let subtreeKeys (node: 'Node) =
            carried node |> List.map key |> Set.ofList

        let rec ofOp (op: SkeletonOp<'Node, 'Id>) : Footprint =
            match op with
            | InsertChild(parent, node) ->
                let inserted = subtreeKeys node
                // parent existence + the inserted ids' dup-check are reads; the parent's child-list is a
                // (known) structure-write; the inserted subtree is authored into being — a content-write.
                //
                // Phase 137: the dup-check named here is now the one `validateInsert` actually performs
                // — `firstDuplicateId` reads exactly this `Tree.ids w node` set against the whole tree
                // (the keyed walk of the graft under a keyed scope, as the keyed engine reads it),
                // so `Reads` describes a read that happens rather than one the footprint assumed. The
                // set is unchanged: the validator's other half (is the subtree unique WITHIN ITSELF?) is
                // internal to the op and reads no tree state, so it adds nothing to the footprint and
                // creates no new collision between concurrent scripts.
                { Footprint.empty with
                    Reads = Set.add (key parent) inserted
                    StructureWrites = Set.singleton (key parent)
                    ContentWrites = inserted }
            // the node destroyed, or relocated under a known destination; the source parent's child
            // list is the pinned unknown-parent write. The public builders ARE these clauses (Phase 512).
            | RemoveNode target -> Footprint.removeNode (key target)
            | MoveNode(target, newParent) -> Footprint.moveTo (key target) (key newParent)
            | ReorderChildren(parent, order) ->
                // the parent's child-list order is rewritten (known structure-write); the named children
                // are read (their positions are permuted, their content is not touched).
                let named = order |> List.map key |> Set.ofList

                { Footprint.empty with
                    Reads = Set.add (key parent) named
                    StructureWrites = Set.singleton (key parent) }
            | Batch inner -> List.fold (fun acc o -> Footprint.union acc (ofOp o)) Footprint.empty inner
            | UpdateNode node ->
                // Phase 250 — an in-place rewrite. The node is read (it must exist) and its content
                // is written. It is ALSO an unknown-parent write, and that is required rather than
                // cautious: the node is rewritten under a parent — and a chain of ancestors — the
                // script cannot name, so an update of `x` and a concurrent `RemoveNode` of an
                // ancestor of `x` carry disjoint reads and content-writes yet do not commute (the
                // update lands in one order and is refused in the other). Only the unknown-parent
                // clause of `independent` can see that pair, exactly as it sees a remove against a
                // write inside the removed subtree. The cost is the same pinned over-approximation
                // the remove/move pay: an update is independent only of a structure-free script,
                // so two updates of different nodes are reported dependent.
                let target = key (w.Id node)
                // Phase 247 — under the keyed engine the payload's keyed subtrees are new content
                // (`validateUpdate` checks their ids for duplicates), so they are read and written as
                // an insert's subtree is. Empty for the unkeyed form.
                let incoming = introduced node |> List.map key |> Set.ofList

                // Phase 340 — and it is a WHOLE-node write, never a slot write: the payload is the
                // node entire, and a pure script cannot say which part of it changed. A domain that
                // rewrites a field by name lowers its own op with `Footprint.slotEdit`.
                { Footprint.empty with
                    Reads = Set.add target incoming
                    ContentWrites = Set.add target incoming
                    UnknownParentWrites = Set.singleton target }

        List.fold (fun acc op -> Footprint.union acc (ofOp op)) Footprint.empty ops

    /// The pure fold over a scope: the ids an insert authors are the graft's walk in the scope (the
    /// keyed walk under a keyed scope, Phase 247), and an `UpdateNode` payload's keyed subtrees are
    /// new content under a keyed scope.
    let private footprintOfScope (scope: OpScope<'Node, 'Id>) (ops: SkeletonOp<'Node, 'Id> list) : Footprint =
        let w = scope.Nodes

        match scope.Keyed with
        | None -> footprintOver w scope.Ids (Tree.ids w) (fun _ -> []) ops
        | Some keyw ->
            footprintOver
                w
                scope.Ids
                (Tree.idsKeyed w keyw)
                (fun n -> keyw.KeyedChildren n |> List.collect (Tree.idsKeyed w keyw))
                ops

    /// The reads a policy's references add (Phase 313): every id the script writes a reference to —
    /// the references every node of an inserted subtree carries (over the scope's walk), and those of
    /// an `UpdateNode` payload. `None` without a `RefWitness`.
    let private referenceReads
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint option =
        policy.Refs
        |> Option.map (fun refw ->
            let rec written (op: SkeletonOp<'Node, 'Id>) : 'Id list =
                match op with
                | InsertChild(_, node) -> Tree.preorder (OpScope.locate scope) node |> List.collect refw.RefsOf
                | UpdateNode node -> refw.RefsOf node
                | Batch inner -> inner |> List.collect written
                | RemoveNode _
                | MoveNode _
                | ReorderChildren _ -> []

            Footprint.reading (ops |> List.collect written |> List.map scope.Ids.ToString))

    let private withReferenceReads policy scope ops (fp: Footprint) : Footprint =
        match referenceReads policy scope ops with
        | Some reads -> Footprint.union fp reads
        | None -> fp

    /// The read/write footprint of an op-script (Phase 78) under a policy and a scope (Phase 517) — a
    /// pure, total derivation over the skeleton ops through the scope's witnesses (the `IdWitness`
    /// keys every address by its string form). `Batch` folds its inner ops. Total: no tree, no failure
    /// case — it never throws (GP4) and mints no ids. Over-approximating by design — see `Footprint`
    /// and STABILITY.md for the pinned conservative cases.
    ///
    /// **The scope (Phase 247).** Under a plain or contained scope an inserted subtree's ids are read
    /// over `Children` alone. Under a keyed scope the id set an `InsertChild` authors is the graft's
    /// keyed walk (`Tree.idsKeyed`), and an `UpdateNode` payload's keyed subtrees, which the keyed
    /// engine checks as new content, are read and content-written too — so two scripts that each
    /// bring in the same keyed id fail `independent` with `Interference.SameTarget` on it. Every other
    /// address is the same in every scope: the engine edits through `Children` alone. `CanHold` does
    /// not move a footprint.
    ///
    /// **The policy (Phase 313).** A `Refs` witness READS every id the script writes a reference to
    /// — so a script that writes a reference to `x` fails `independent` against a script that destroys
    /// `x`, by `Interference.RightWritesLeftReads` / `LeftWritesRightReads` naming `x`. It only ever
    /// ADDS collisions: as sound as the footprint without it, and `independent` over it is never more
    /// permissive. A `Grammar` does not move a footprint: every check it adds reads a parent the op
    /// already reads or rewrites a node in place, which the unknown-parent clause serialises.
    let footprint
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        footprintOfScope scope ops |> withReferenceReads policy scope ops

    // ---- the base-aware footprint (Phase 500) ----
    // The pure footprint cannot name a relocated or rewritten node's parent, so it records an
    // unknown-parent write and `independent` refuses that against every structural write — the
    // pinned over-approximation, NECESSARY over the record for a footprint computed from the script
    // alone (section 18 of `proofs/TreeOps.fst`). Where the tree is known — arbitration takes a base,
    // the fold applies every delta to one — the parent IS computable, and so is the subtree a removal
    // destroys, which is the set Phase 163 named as the precondition of any tightening. `footprintIn`
    // computes both: every address it records is one the op actually reads or writes at the tree it
    // applies to, and nothing it records is an address standing in for one it could not name.
    //
    // THE SCRIPT IS THREADED. The second op of a script applies to the tree the first op leaves, not
    // to the base: a removal after a move destroys what the move brought under it. So each op is
    // footprinted at the state the ops before it reach (`apply`, the engine the script lands with;
    // the keyed engine with an admitting `canHold` for a keyed scope, which reaches the same
    // tree as the domain's engine for every script that engine accepts), and the script's footprint
    // is the union. Where an op does not apply, the state is lost and every op from there on takes
    // the PURE arm: conservative, and honest — the promise `independent` makes is for scripts that
    // apply, which is also the premise of the theorem (`FootprintIn.footprint_in_sound`).
    //
    // What each arm resolves against the state it applies to, and what it keeps from the pure form:
    //   - `InsertChild` and `ReorderChildren`: nothing to resolve — the parent is named by the op,
    //     and every id the pure arm records is one the op reads or writes. The pure arm, verbatim.
    //   - `RemoveNode x`, present: the source parent `p` is a KNOWN structure-write and a read (its
    //     child list is read to be rewritten, as an insert reads its parent), and the content-write
    //     is the whole subtree `x` roots — the destroyed set — not `x` alone. No unknown-parent write.
    //   - `MoveNode (x, np)`, both present: the source parent as for a removal; the destination `np`
    //     a known structure-write and a read as before; `x` content-written; and the ANCESTORS of
    //     `np` read — the cycle check (`WouldNestUnderSelf`) reads them, so a concurrent move that
    //     puts an ancestor of `np` under `x` is what makes this op refuse, and reading them is what
    //     catches it (`relocation_move_pair_also_fails` is the pair; two moves nesting into each
    //     other's subtrees now fail `RightWritesLeftReads`, where the pure form failed the
    //     unknown-parent clause). No unknown-parent write.
    //   - `UpdateNode n`, present: the target read and content-written, with the payload's keyed
    //     subtrees as before. No unknown-parent write, and no ancestor read either: the removal of an
    //     ancestor is caught because a removal now content-writes its whole subtree, and the move of
    //     an ancestor commutes with an in-place rewrite — the theorem is what says the read is not
    //     needed.
    //   - a target absent from the state (inserted later in the same script, named by an op that does
    //     not apply, or held in a keyed position the structural walk does not reach): the pure arm.
    //
    // Two in-place edits of different nodes, anywhere in the tree, are therefore independent; an
    // in-place edit beside the removal of its ancestor is not (its target is in the removed set);
    // and no pair the pure footprint frees is refused here (`FootprintIn.footprint_in_refines`).

    /// The base-aware arm of one non-batch op, read from its view of the state (Phase 512): `None`
    /// where the pure arm stands (see above).
    let private resolvedFootprint
        (s: Walks<'Node, 'Id>)
        (ids: 'Node -> 'Id list)
        (introduced: 'Node -> 'Id list)
        (v: OpView<'Node, 'Id>)
        (t: 'Node)
        : Footprint option =
        let key (i: 'Id) = s.Ids.ToString i
        let keys (xs: 'Id list) = xs |> List.map key |> Set.ofList

        // the proper ancestors of `target`, root first, as keys — empty when absent or the root
        let ancestorKeys (target: 'Id) =
            match Tree.path s.Locate s.Ids target t with
            | Some path -> path |> List.take (List.length path - 1) |> keys
            | None -> Set.empty

        match v.Op, v.Located, v.SourceParent, v.Destination with
        | RemoveNode target, Some _, Some parent, _ ->
            let p = key (s.Edit.Id parent)

            Some
                { Footprint.empty with
                    Reads = Set.ofList [ key target; p ]
                    StructureWrites = Set.singleton p
                    ContentWrites = keys (destroyedBy ids v) }
        | MoveNode(target, newParent), Some _, Some parent, Some _ ->
            let p = key (s.Edit.Id parent)
            let np = key newParent

            Some
                { Footprint.empty with
                    Reads = Set.unionMany [ Set.ofList [ key target; np; p ]; ancestorKeys newParent ]
                    StructureWrites = Set.ofList [ p; np ]
                    ContentWrites = Set.singleton (key target) }
        | UpdateNode node, Some _, _, _ ->
            let target = key (s.Edit.Id node)
            let incoming = keys (introduced node)

            Some
                { Footprint.empty with
                    Reads = Set.add target incoming
                    ContentWrites = Set.add target incoming }
        | _ -> None

    // The resolution LOCATES through the structural walk in both forms (a node held in, or below, a
    // keyed position is absent to it and takes the pure arm), and the script is threaded through
    // `engine`, which the unkeyed form hands the very view the resolution read.
    let private footprintInOver
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (engine: OpView<'Node, 'Id> -> 'Node -> Result<'Node, Rejection<'Id>>)
        (ids: 'Node -> 'Id list)
        (introduced: 'Node -> 'Id list)
        (baseTree: 'Node)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        let s = plainScope nodew idw
        let purely = footprintOver nodew idw ids introduced

        let rec ofOp (state: 'Node option) (op: SkeletonOp<'Node, 'Id>) : Footprint * 'Node option =
            match state, op with
            | None, _ -> purely [ op ], None
            | Some t, Batch inner ->
                inner
                |> List.fold
                    (fun (acc, st) o ->
                        let f, st' = ofOp st o
                        Footprint.union acc f, st')
                    (Footprint.empty, Some t)
            | Some t, _ ->
                let v = view s op t

                resolvedFootprint s ids introduced v t |> Option.defaultValue (purely [ op ]),
                engine v t |> Result.toOption

        // the script is the batch of its ops, threaded the same way
        ofOp (Some baseTree) (Batch ops) |> fst

    /// The footprint of an op-script at the tree it applies to (Phase 500) — `footprint`'s address
    /// kinds with every parent the pure form could not name resolved against `baseTree`, arm by arm
    /// as the section head states; an absent target, and every op after one that does not apply,
    /// takes the pure arm. Total, no throws (GP4), mints no ids, never mutates `base`. Sound where
    /// the pure footprint is sound and strictly more precise (`FootprintIn.footprint_in_sound`,
    /// `footprint_in_refines`, `Conformance.footprintInLawsAt`). `Arbitration.arbitrateIn` and
    /// `Dag.conflictsIn` / `reconcileIn` are the entries that hold the base and compose it.
    ///
    /// Under a keyed scope (as the keyed `footprint`) the ids an insert authors and a removal destroys
    /// are the graft's and the subtree's keyed walks, an `UpdateNode` payload's keyed subtrees are read
    /// and content-written, and the script is threaded through the keyed engine with an admitting
    /// container capability — the tree every script the keyed engine accepts reaches; the resolution
    /// still locates structurally (D166). A policy's `Refs` adds `footprint`'s reference reads.
    let footprintIn
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (baseTree: 'Node)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        let w = scope.Nodes
        let idw = scope.Ids

        match scope.Keyed with
        | None ->
            footprintInOver w idw (applyView (fun _ -> true) (plainScope w idw)) (Tree.ids w) (fun _ -> []) baseTree ops
        | Some keyw ->
            footprintInOver
                w
                idw
                (fun v t -> applyWith (fun _ -> true) (keyedScope keyw w idw) v.Op t)
                (Tree.idsKeyed w keyw)
                (fun n -> keyw.KeyedChildren n |> List.collect (Tree.idsKeyed w keyw))
                baseTree
                ops
        |> withReferenceReads policy scope ops

    /// Every clause of `independent` two footprints fail, with the addresses each fails on (Phase
    /// 248) — the explanation of a `false` verdict, so a refused party can see WHAT it collides on
    /// (the parent it shares, the id the other script reads, the relocation that serialises it)
    /// without re-deriving the clauses itself. Empty exactly when `independent a b`. The clauses are
    /// listed in `Interference`'s declaration order, each at most once; `a` is the left side and `b`
    /// the right, so `interference b a` reports the same clauses with the directional cases mirrored.
    /// Total, no throws (GP4).
    let interference (a: Footprint) (b: Footprint) : Interference list =
        let structural (f: Footprint) =
            Set.union f.StructureWrites f.UnknownParentWrites

        let sameTarget = Set.intersect a.ContentWrites b.ContentWrites
        let leftWrites = Set.intersect a.ContentWrites b.Reads
        let rightWrites = Set.intersect a.Reads b.ContentWrites
        let sameParent = Set.intersect a.StructureWrites b.StructureWrites
        let structuralA = structural a
        let structuralB = structural b
        // Phase 340 — the slot clauses. A slot access is compared at the slot against another slot
        // access, and as an access of the NODE against the other side's whole-node sets. The three
        // sets are `Footprint`'s helpers, which `Dag.conflicts` reads too.
        let slotClash = Footprint.slotClash a b
        let leftSlots = Footprint.slotsAgainstNode a b
        let rightSlots = Footprint.slotsAgainstNode b a

        [ if not (Set.isEmpty sameTarget) then
              Interference.SameTarget sameTarget
          if not (Set.isEmpty leftWrites) then
              Interference.LeftWritesRightReads leftWrites
          if not (Set.isEmpty rightWrites) then
              Interference.RightWritesLeftReads rightWrites
          if not (Set.isEmpty sameParent) then
              Interference.SameParent sameParent
          if not (Set.isEmpty a.UnknownParentWrites) && not (Set.isEmpty structuralB) then
              Interference.LeftUnknownParent(a.UnknownParentWrites, structuralB)
          if not (Set.isEmpty b.UnknownParentWrites) && not (Set.isEmpty structuralA) then
              Interference.RightUnknownParent(structuralA, b.UnknownParentWrites)
          if not (Set.isEmpty slotClash) then
              Interference.SlotClash slotClash
          if not (Set.isEmpty leftSlots) then
              Interference.LeftSlotsRightNode leftSlots
          if not (Set.isEmpty rightSlots) then
              Interference.RightSlotsLeftNode rightSlots ]

    /// Are two footprints **independent** (Phase 78) — do their scripts provably commute under `apply`?
    /// Pairwise disjointness across the write kinds, with the conservative rules pinned:
    ///   - no content write/write overlap, and no content-write vs read overlap either way (a node one
    ///     script authors/destroys must not be read or written by the other);
    ///   - no shared **named** structural parent — two positional inserts (or an insert + a reorder, …)
    ///     under one parent are NOT independent (they shift the same siblings — THE pinned same-parent
    ///     rule);
    ///   - an `UnknownParentWrites` op (a remove/move, whose source parent is a tree fact) conflicts with
    ///     *any* structural write — known or unknown — in the other script (the pinned unknown-parent
    ///     over-approximation): a remove/move is only independent of a structure-free script;
    ///   - (Phase 340) no slot is written by one script and accessed by the other (`SlotClash`), and no
    ///     node one script accesses through a slot is touched whole by the other (`LeftSlotsRightNode`
    ///     / `RightSlotsLeftNode`): two writes to DIFFERENT slots of one node are independent; a
    ///     whole-node write is a write of every slot and serialises against each.
    /// `true` is a promise (they commute); `false` is always a safe answer. Total, no throws (GP4).
    ///
    /// **The unknown-parent clauses are NECESSARY over this record, not merely conservative (Phase
    /// 143).** A move and a batch that removes and reorders carry the SAME address sets (the slot
    /// sets Phase 340 added are empty for every skeleton op, so they tell them apart no better), and one of them
    /// commutes with a structural write under a parent inside the relocated subtree while the other
    /// destroys that parent — so no predicate over footprints alone can free the first without
    /// freeing the second. Proved, with the witness, in `proofs/TreeOps.fst` section 18
    /// (`relocation_clause_is_necessary`); pinned in the test tree by the `Proofs.Oracle` case
    /// "the pinned unknown-parent clause is necessary" and by the `Conformance.concurrencyLaws`
    /// teeth-check that erases `UnknownParentWrites`. Tightening it is a change to the `Footprint`
    /// record and to every consumer that reads it, not a change to this function.
    ///
    /// Since Phase 248 it is DEFINED as `interference a b = []`, so the verdict and its explanation
    /// have one source and cannot drift; the clauses above are `interference`'s cases.
    let independent (a: Footprint) (b: Footprint) : bool = List.isEmpty (interference a b)

    // ---- lowering a tree to a skeleton root plus an insert script (Phase 312) ----
    // A tree that arrives whole — decoded, generated, streamed — reaches the op stream as ops: a
    // skeleton root to start from, and the inserts that rebuild everything below it. The UI host
    // shipped this as its streaming lowering, generic over the witness already; it lives here now.

    /// `node` with its STRUCTURAL children emptied — the shape an insert script grows back. A node
    /// that holds no children is returned as it is, so `ReplaceChildren` is never asked to rebuild
    /// a leaf (a witness may leave it partial on nodes that cannot hold children). Only
    /// `Children` is emptied: anything a domain holds in keyed positions travels WITH the shell,
    /// exactly as an `UpdateNode` payload's keyed positions do.
    let shellOf (w: NodeWitness<'Node, 'Id>) (node: 'Node) : 'Node =
        if List.isEmpty (w.Children node) then
            node
        else
            w.ReplaceChildren node []

    /// The genesis tree `lower`'s script is applied to: the root's shell.
    let skeletonRoot (w: NodeWitness<'Node, 'Id>) (root: 'Node) : 'Node = shellOf w root

    /// Lower `root` to the insert script that rebuilds it from `skeletonRoot w root`: in preorder,
    /// one `InsertChild(parent, shellOf child)` per node below the root, each child inserted as its
    /// own shell and then filled. `InsertChild` appends, and preorder inserts a parent's children
    /// left to right, so sibling order is rebuilt with no `ReorderChildren`. Iterative — a deep tree
    /// cannot overflow.
    ///
    /// **The law** (`Conformance.loweringLaws`): for every `Tree.wellFormed` tree,
    /// `applyAll Policy.none (OpScope.plain w idw) (lower w t) (skeletonRoot w t) = Ok t`. **The contained
    /// form is the same script under `OpScope.contained canHold`**: every node that holds children is an insert's parent,
    /// so for a tree in the containment invariant (every node with children satisfies `canHold`)
    /// the script is accepted and rebuilds the tree; for a tree outside it, the refusal is
    /// `NotAContainer` naming the first offender in preorder — the shell is accepted childless and
    /// its first child is refused. A well-formed tree's script never meets `DuplicateId`; an
    /// ill-formed one's meets it at the second occurrence.
    let lower (w: NodeWitness<'Node, 'Id>) (root: 'Node) : SkeletonOp<'Node, 'Id> list =
        let rec go (acc: SkeletonOp<'Node, 'Id> list) (stack: ('Id * 'Node) list) =
            match stack with
            | [] -> List.rev acc
            | (parent, node) :: rest ->
                let below = w.Children node |> List.map (fun c -> w.Id node, c)
                go (InsertChild(parent, shellOf w node) :: acc) (below @ rest)

        go [] (w.Children root |> List.map (fun c -> w.Id root, c))

    // ---- the containment grammar and the reference witness (Phase 313) ----
    // `canHold` is unary and child-blind by design (Phase 161): it answers whether a node can hold
    // children AT ALL. A grammar answers which: for a parent's kind tag, the kind tags it may hold
    // (`None` = any). The grammar is DATA the domain declares, never a kind Core knows (DECISIONS,
    // Phase 313), and every surface that reads it — the engine under a grammar `Policy`, `Diff.toOps`,
    // `Arbitration.arbitrate`, `Validator.containment`, `Conformance.containmentLaws` — reads
    // it through `isLegalChild`, so they share ONE definition of a legal child.
    //
    // A policy's grammar and reference clauses WRAP the engine rather than widening it: each
    // non-batch step is first decided exactly as the engine decides it, and the new clauses run
    // only on a step it accepted, against the tree before and after it. So no operation the engine
    // refuses changes class (the D38 ordering, at the level of a whole engine), and a `Batch` stays
    // all-or-nothing because it is threaded step by step through the same wrapper.

    /// Is a node of kind `childKind` a legal child of a node of kind `parentKind` under
    /// `allowedChildren` (Phase 313)? `None` for the parent's kind admits every child; `Some legal`
    /// admits exactly the kinds `legal` lists (ordinal comparison of the kind tags). The one
    /// definition every grammar-reading surface uses.
    let isLegalChild (allowedChildren: string -> string list option) (parentKind: string) (childKind: string) : bool =
        match allowedChildren parentKind with
        | None -> true
        | Some legal -> List.contains childKind legal

    /// Every parent→child pair of `node`'s subtree whose child the grammar does not let its parent
    /// hold (Phase 313), parents in preorder and each parent's children in order. Empty exactly when
    /// the subtree keeps the grammar. Walks `Children` — the structural surface the engine edits.
    let illegalChildren
        (allowedChildren: string -> string list option)
        (w: NodeWitness<'Node, 'Id>)
        (node: 'Node)
        : ('Node * 'Node) list =
        Tree.preorder w node
        |> List.collect (fun p ->
            let pk = w.KindTag p

            w.Children p
            |> List.filter (fun c -> not (isLegalChild allowedChildren pk (w.KindTag c)))
            |> List.map (fun c -> p, c))

    /// The `IllegalChild` envelope for `child` under `parent`, enumerating what the grammar lets the
    /// parent's kind hold.
    let private illegalChild
        (allowedChildren: string -> string list option)
        (w: NodeWitness<'Node, 'Id>)
        (parent: 'Node)
        (child: 'Node)
        : Rejection<'Id> =
        let pk = w.KindTag parent
        IllegalChild(w.Id child, w.KindTag child, w.Id parent, pk, allowedChildren pk |> Option.defaultValue [])

    /// The parent→child pairs one non-batch step CREATES, read from its view of the tree before it
    /// (Phase 512), in a fixed order: an insert's node under its parent, then every pair inside the
    /// graft (preorder); a move's node under its new parent (the pairs inside the moved subtree
    /// already stood — D38's reasoning for `NotAContainer`); an in-place rewrite under its parent,
    /// then each kept child under it, since a rewrite can change the kind. Removes and reorders none.
    let private createdPairs (s: Walks<'Node, 'Id>) (v: OpView<'Node, 'Id>) (before: 'Node) : ('Node * 'Node) list =
        let w = s.Edit

        let under (parent: 'Node option) (child: 'Node) =
            parent |> Option.map (fun p -> p, child) |> Option.toList

        match v.Op with
        | InsertChild(_, node) ->
            under v.Located node
            @ (grafted w v.Op
               |> List.collect (fun p -> w.Children p |> List.map (fun c -> p, c)))
        | MoveNode _ ->
            match v.Destination, v.Located with
            | Some np, Some moved -> [ np, moved ]
            | _ -> []
        | UpdateNode node ->
            match v.Located with
            | None -> []
            | Some existing ->
                let rewritten = updated w existing node

                under (structuralParentOf s (w.Id node) before) rewritten
                @ (w.Children rewritten |> List.map (fun c -> rewritten, c))
        | RemoveNode _
        | ReorderChildren _
        | Batch _ -> []

    /// The grammar clause of one accepted non-batch step: the first pair it creates whose child the
    /// grammar does not let its parent hold.
    let private grammarRefusal
        (allowedChildren: string -> string list option)
        (s: Walks<'Node, 'Id>)
        (v: OpView<'Node, 'Id>)
        (before: 'Node)
        : Rejection<'Id> option =
        let w = s.Edit

        createdPairs s v before
        |> List.tryFind (fun (p, c) -> not (isLegalChild allowedChildren (w.KindTag p) (w.KindTag c)))
        |> Option.map (fun (p, c) -> illegalChild allowedChildren w p c)

    /// The reference clause of one accepted non-batch step: the ids the tree declared before the step
    /// and no longer declares after it are ORPHANED, and a node of the resulting tree that refers to
    /// one is a referrer — `StillReferenced`, naming the step's target (a remove's target, a
    /// rewrite's node) and every referrer in preorder. Only a `RemoveNode` (its subtree's
    /// declarations leave) and an `UpdateNode` (the rewritten node may declare less) can orphan an id;
    /// an insert, a move or a reorder keeps every declaration. A reference that already dangled before
    /// the step is not the step's doing and is not reported.
    let private referenceRefusal
        (refw: RefWitness<'Node, 'Id>)
        (s: Walks<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Rejection<'Id> option =
        // the walk the engine locates through: under a keyed scope a declaration or a reference held
        // in a keyed position is one the tree carries (Phase 517)
        let w = s.Locate
        let key (i: 'Id) = s.Ids.ToString i

        let declared (t: 'Node) =
            Tree.preorder w t |> List.collect refw.DeclsOf |> List.map key |> Set.ofList

        let refuse (target: 'Id) =
            let orphaned = Set.difference (declared before) (declared after)

            if Set.isEmpty orphaned then
                None
            else
                match
                    Tree.preorder w after
                    |> List.filter (fun n -> refw.RefsOf n |> List.exists (fun r -> orphaned.Contains(key r)))
                with
                | [] -> None
                | referrers -> Some(StillReferenced(target, referrers |> List.map w.Id))

        match op with
        | RemoveNode target -> refuse target
        | UpdateNode node -> refuse (w.Id node)
        | InsertChild _
        | MoveNode _
        | ReorderChildren _
        | Batch _ -> None

    // ---- the one engine entry: a policy and a scope (Phase 517) ----
    // The entry matrix (plain x contained x keyed scopes, grammar and reference policies, single
    // and script, do and dry run) was 39 named functions with eight cells nobody had drawn. It is
    // four verbs over two records: `OpScope` says how the engine sees the tree, `Policy` which
    // checks run beyond the engine's own, and every combination exists by construction.

    /// One step under a policy: the engine's checks (D38's order) on the view located once, then the
    /// grammar over the pairs the step creates (read from the tree BEFORE it), then the edit, then the
    /// references over the trees before and after it. A `Batch` is the runner's all-or-nothing form.
    let rec private stepWith
        (policy: Policy<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (s: Walks<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        match op with
        | Batch ops -> batch (fun node o -> stepWith policy canHold s o node) ops root
        | _ ->
            let v = view s op root

            match check canHold s v root with
            | Error e -> Error e
            | Ok() ->
                match policy.Grammar |> Option.bind (fun g -> grammarRefusal g s v root) with
                | Some r -> Error r
                | None ->
                    edit s v root
                    |> Result.bind (fun after ->
                        match policy.Refs |> Option.bind (fun refw -> referenceRefusal refw s v.Op root after) with
                        | Some r -> Error r
                        | None -> Ok after)

    /// The dry run of one step: the engine's checks and the grammar read the view and the tree
    /// before it, so no tree is built (Phase 246, Phase 298) unless the policy carries references,
    /// whose clause reads the tree after the step, or the op is a `Batch`, which is order-dependent.
    let private checkWith
        (policy: Policy<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (s: Walks<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        match op, policy.Refs with
        | Batch _, _
        | _, Some _ -> stepWith policy canHold s op root |> Result.map ignore
        | _, None ->
            let v = view s op root

            check canHold s v root
            |> Result.bind (fun () ->
                match policy.Grammar |> Option.bind (fun g -> grammarRefusal g s v root) with
                | Some r -> Error r
                | None -> Ok())

    /// The base's own verdict, taken once at each public entry: the first id `root` carries twice
    /// over the walk the scope locates through, or `None` for an id-unique base. `Tree.wellFormed`
    /// over `s.Locate` is `Tree.wellFormedKeyed` under a keyed scope, as `Arbitration.arbitrate` and
    /// `Diff.toOps` read it; the engine's steps assume what this establishes.
    let private baseRefusal (s: Walks<'Node, 'Id>) (root: 'Node) : 'Id option =
        match Tree.wellFormed s.Locate s.Ids root with
        | Tree.RepeatedId d -> Some d
        | Tree.Structural -> None

    /// Apply one skeleton op under `policy` and `scope` (Phase 517). Every failure is a typed
    /// `Rejection`, never an exception (GP4).
    ///
    /// **The scope.** `OpScope.plain` treats every node as able to hold children. Under
    /// `OpScope.contained canHold` (Phase 251) a step that would leave children under a node
    /// `canHold` refuses is `NotAContainer` — an insert's parent, a move's destination, a reorder's
    /// parent (Phase 517), an update's rewritten node, and, since Phase 161 (D38), every interior
    /// node of an inserted graft; containment LEGALITY (which kinds may parent which) is the policy's
    /// grammar. The one premise no check can discharge is `child_blind`: a `canHold` that reads the
    /// child list — the domain's obligation, certified by `Conformance.containerLaws`. Under
    /// `OpScope.keyed` (Phase 286) the engine LOCATES through `Tree.traversal`, so a node held in or
    /// below a keyed position can be an insert's or a reorder's parent, a move's destination, an
    /// update's target, and a remove's or move's target when it has a structural parent; it EDITS
    /// through `Nodes`, so a remove or move of a node held directly in a keyed position is
    /// `KeyedPosition`, naming its holder and slot; `DuplicateId` sees every id the keyed walk
    /// reaches, on both sides of a graft and in an update payload's keyed subtrees. For a domain whose
    /// `KeyedChildren` is `fun _ -> []` the keyed scope answers exactly what the contained one does
    /// (`Conformance.keyedApplyLaws`).
    ///
    /// **The policy.** After every check the engine makes (so no op it refuses changes class), a
    /// `Grammar` refuses a step that would leave a child under a parent whose kind may not hold it
    /// (`IllegalChild`, Phase 313; `grammar_preserves` in `proofs/Preservation.fst`), and `Refs`
    /// refuses a `RemoveNode` or `UpdateNode` that would leave a reference of the tree dangling
    /// (`StillReferenced`). A reference an insert or a rewrite brings in is not refused: whether it
    /// resolves is `Validator.referenceIntegrity`'s report. With `fun _ -> None` as its grammar a
    /// policy answers exactly what `Policy.none` answers.
    ///
    /// **The base.** A `root` carrying one id twice, over the walk the scope locates through, is
    /// refused before any step is read: `DuplicateId d`, `d` the first id the preorder carries twice
    /// (`Tree.wellFormed`'s verdict, `Tree.wellFormedKeyed`'s under a keyed scope). It is the check
    /// every other entry a tree takes into the algebra already makes — `Tree.Index.build`,
    /// `Arbitration.arbitrate` / `arbitrateIn` (`Inapplicable(0, DuplicateId d)`), `Diff.toOps` —
    /// and with it here the ladder's input hypothesis (`tree-algebra-well-formed-states`) is
    /// discharged at every entry: a tree the engine accepts was id-unique, and by
    /// `apply_preserves_wf` (`proofs/Preservation.fst`) the tree it returns is. On an id-unique base
    /// the check finds nothing and nothing else changes; it is made once per public entry, never per
    /// step of a `Batch` or a script, because preservation carries the invariant across the steps.
    let apply
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        let s = walksOf scope

        match baseRefusal s root with
        | Some d -> Error(DuplicateId d)
        | None -> stepWith policy (canHoldOf scope) s op root

    /// Dry-run validation (Phase 246): would `op` be accepted against `root` under `policy` and
    /// `scope`? The exact `Rejection` `apply` would return, and no new tree built for a non-batch op
    /// unless the policy carries references (their clause reads the tree after the step). A `Batch`
    /// is order-dependent, so its check simulates through `apply` and discards the result. The AI
    /// pre-flight surface — "is this op legal?" — without committing the rebuild.
    let canApply
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        let s = walksOf scope

        match baseRefusal s root with
        | Some d -> Error(DuplicateId d)
        | None -> checkWith policy (canHoldOf scope) s op root

    /// Apply a sequence non-atomically (Phase 160) under `policy` and `scope`, threading the tree.
    ///
    /// FIRST REFUSAL WINS, and a script is **not** a `Batch`. `Batch` is all-or-nothing inside one
    /// op: it aborts and the original tree survives. A script stops at the first refusal and returns
    /// the tree built so far, so the accepted prefix is kept. On failure the payload is a
    /// `ScriptRejection` (Phase 391): `Applied`, the number of ops applied before the refusal and so
    /// the 0-based position of the refused op in `ops`; `Rejection`, the envelope; and `Tree`, the
    /// tree the accepted prefix reached — the state the refused step was offered against.
    let applyAll
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, ScriptRejection<'Node, 'Id>> =
        let canHold = canHoldOf scope
        let s = walksOf scope

        match baseRefusal s root with
        | Some d ->
            // The base's refusal, not an op's: nothing applied, and the tree offered is the one
            // returned — the shape `Arbitration.arbitrate` gives the same base.
            Error
                { Applied = 0
                  Rejection = DuplicateId d
                  Tree = root }
        | None -> script (fun node o -> stepWith policy canHold s o node) ops root

    /// Dry-run a sequence (Phase 246): the 0-based index of the first refused op and its envelope,
    /// exactly as `applyAll` under the same policy and scope would report them, and no tree returned.
    /// Each step is checked against the tree the earlier steps leave. It is the `canApply` a domain
    /// hands `Arbitration.arbitrateWith`; `Arbitration.arbitrate` composes it with `footprint` under
    /// one policy and scope.
    let canApplyAll
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        let canHold = canHoldOf scope
        let s = walksOf scope

        match baseRefusal s root with
        | Some d -> Error(0, DuplicateId d)
        | None -> dryRun (stepWith policy canHold s) ops root
