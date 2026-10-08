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
/// `Ops.footprintReferenced` reads every id a script writes a reference to; and the reference-aware
/// engine (`Ops.applyReferenced`) refuses a `RemoveNode` that would leave a reference dangling.
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
/// the prefix — so the refusal names how far it got. `Ops.applyAllWith` and every sequence form
/// beside it (`applyAll`, `applyAllGrammar`, `applyAllReferenced`) answer it on `Error`.
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

/// The generic apply engine over the skeleton ops. Total: every failure is a typed
/// `Rejection` envelope. Generic over the `NodeWitness` / `IdWitness` — no domain
/// `NodeKind` is ever in scope.
module Ops =

    // ---- validation (Phase 246) ----
    // The structural checks each op must pass, factored out of `apply` so the dry-run
    // `canApply` and the mutating `apply` share one source of validation truth and can
    // never diverge. The three index/structure ops validate without building a tree;
    // `MoveNode` and `Batch` are order-dependent (a later step sees an earlier step's
    // tree), so their validation simulates through `apply` — the rejection returned is
    // exactly the one `apply` would produce.

    // ---- the two witnesses every clause below reads (Phase 286) ----
    // `w` is the STRUCTURAL witness: what a node's children are for an edit — what an insert appends
    // to, a remove filters, a reorder permutes, an update keeps. `t` is the witness the engine
    // LOCATES through — which nodes exist, where they are, which ids a graft collides with — and the
    // one it rebuilds ancestors through. The unkeyed forms pass `t = w`, so every clause reads
    // exactly what it read before Phase 286; `applyContainedKeyed` passes `Tree.traversal w keyw`,
    // and `keyed` carries the domain's `KeyedChildren` for the two questions only the keyed engine
    // asks (which node holds a keyed position, and what an `UpdateNode` payload carries into one).

    /// The first id in `node`'s subtree (preorder) that breaks uniqueness — one already carried by
    /// `root`, or one the subtree repeats within itself. `None` when the graft is clean.
    ///
    /// **Phase 137** widened this from the graft's own id to its whole subtree; **Phase 139** moved
    /// the scan itself into `Tree.graftWellFormed`, so this is now a projection of Core's ONE named
    /// definition of structural validity rather than a second, separately-maintained copy of it.
    /// The behaviour is unchanged — same seed, same preorder, same first offender — and the point of
    /// the move is that it can no longer drift from `Tree.wellFormed`, which is what a caller
    /// checks the RESULT with. **Phase 286**: over `t`, so under `applyContainedKeyed` it is
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

    /// `firstUncontained`'s walk, over a witness of the caller's choosing: the first node `t`
    /// reaches that holds STRUCTURAL children while `canHold` refuses it. The keyed engine walks
    /// the graft's keyed subtrees too (Phase 286), since a node the keyed walk reaches is a node of
    /// the tree; "holds children" stays `w.Children`, the list `canHold` is about.
    let private firstUncontainedOver
        (t: NodeWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (node: 'Node)
        : 'Node option =
        Tree.preorder t node
        |> List.tryFind (fun n -> not (List.isEmpty (w.Children n)) && not (canHold n))

    /// The first node of a graft (preorder) that HOLDS children while `canHold` refuses it — the
    /// interior offender an inserted subtree carries in. `None` when the graft's own interior
    /// already satisfies the invariant `applyContained` exists to keep ("every node with children
    /// satisfies `canHold`"). A childless node is unconstrained: the predicate answers "can this
    /// node hold children AT ALL", so a leaf that holds none says nothing.
    ///
    /// **Phase 161 (DECISIONS D38).** `canHold` used to be applied to the PARENT of an insert and
    /// to nothing inside the subtree being inserted, so a graft whose own interior node was a
    /// non-container carried the violation in and the invariant broke across an ACCEPTED operation
    /// — machine-checked as `contained_needs_op_hypothesis` in `proofs/Preservation.fst` before it
    /// was a refusal. The operator's ruling was to inspect the graft.
    ///
    /// The predicate is `Diff.toOpsContained`'s, deliberately: the diff path has walked an `after`
    /// tree for exactly this shape since Phase 09, and the accept path was the one place the check
    /// was missing — word for word Phase 137's situation with `DuplicateIdInTree`.
    ///
    /// **The single definition of the graft-containment shape (Phase 228).** Both refusals of that
    /// shape are raised from THIS function — `Rejection.NotAContainer` on the apply path (through
    /// `validateGraftContainment`) and `Diff.DiffError.TargetNotAContainer` on the diff path (through
    /// `Diff.toOpsContained`, which calls it over the whole `after` tree) — and both name the node it
    /// returns under one payload, `target * kindTag`. It is `internal` rather than `private` only so
    /// the `Diff` module can call it instead of re-stating it; it is not public surface.
    let internal firstUncontained (canHold: 'Node -> bool) (w: NodeWitness<'Node, 'Id>) (node: 'Node) : 'Node option =
        firstUncontainedOver w canHold w node

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
        match firstUncontainedOver t canHold w node with
        | Some offender -> Error(NotAContainer(w.Id offender, w.KindTag offender))
        | None -> Ok()

    let private validateInsert
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (node: 'Node)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        // `Tree.ids` is preorder, so its head is the inserted node's OWN id: the widened scan
        // subsumes the pre-137 root-id check and keeps its precedence over `UnknownNode` rather
        // than quietly reordering the envelope a caller already handles.
        match firstDuplicateId t idw node root with
        | Some d -> Error(DuplicateId d)
        | None ->
            // one walk locates the parent (Phase 298; an `exists` walk then a `tryFind` walk, with
            // a `None` arm after the existence check that could not be reached, before)
            match Tree.tryFind t idw parent root with
            | None -> Error(UnknownNode(parent, Tree.ids t root))
            | Some p when not (canHold p) -> Error(NotAContainer(parent, w.KindTag p))
            // Phase 161 — the graft's own interior, checked LAST. The ordering is D38's: no
            // operation that was REFUSED before this phase changes its class, because every
            // earlier clause still fires first. Only operations that were ACCEPTED can now be
            // refused, which is what makes this a widening rather than a re-shuffling — and it
            // is what keeps Phase 137's built-collision conformance arm reaching `DuplicateId`.
            | Some _ -> validateGraftContainment canHold w t node

    /// The node whose STRUCTURAL child list holds `target`, searched over every node `t` reaches.
    /// With `t = w` this is `Tree.parentOf w`, word for word; over the keyed walk it also finds a
    /// structural parent that is itself held below a keyed position.
    let private structuralParentOf
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (root: 'Node)
        : 'Node option =
        Tree.preorder t root
        |> List.tryFind (fun n -> w.Children n |> List.exists (fun c -> idw.Equals (w.Id c) target))

    /// The node holding `target` directly in one of its KEYED positions, when `target` has no
    /// structural parent (Phase 286). Always `None` for the unkeyed forms (`keyed = None`), which
    /// therefore never pay for the search.
    let private keyedHolderOf
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (root: 'Node)
        : 'Node option =
        match keyed with
        | None -> None
        | Some keyedOf ->
            match structuralParentOf w t idw target root with
            | Some _ -> None
            | None ->
                Tree.preorder t root
                |> List.tryFind (fun n -> keyedOf n |> List.exists (fun c -> idw.Equals (w.Id c) target))

    let private validateRemove
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        if idw.Equals (w.Id root) target then
            Error CannotRemoveRoot
        elif not (Tree.exists t idw target root) then
            Error(UnknownNode(target, Tree.ids t root))
        else
            match keyedHolderOf w t keyed idw target root with
            | Some holder -> Error(KeyedPosition(target, w.Id holder))
            | None -> Ok()

    let private validateReorder
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (order: 'Id list)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        match Tree.tryFind t idw parent root with
        | None -> Error(UnknownNode(parent, Tree.ids t root))
        | Some p ->
            let current = w.Children p |> List.map w.Id

            let key xs =
                xs |> List.map idw.ToString |> List.sort

            if key current <> key order then
                Error(ReorderMismatch(parent, current, order))
            else
                Ok()

    /// `MoveNode`'s checks, without building a tree (Phase 298) — what `canApply (MoveNode _)` now
    /// asks instead of simulating the move through `apply`. In the order the apply path has always
    /// refused in: moving the root (`CannotRemoveRoot`); an absent target, then an absent new
    /// parent (`UnknownNode`, enumerating the ids the walk reaches); a new parent that cannot hold
    /// children (`NotAContainer`); a new parent that is the target or below it
    /// (`WouldNestUnderSelf`); and a target held directly in a keyed position (`KeyedPosition`, the
    /// remove half's refusal). On success it hands back the subtree being moved, which the apply
    /// arm grafts.
    let private validateMove
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (newParent: 'Id)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        if idw.Equals (w.Id root) target then
            Error CannotRemoveRoot
        else
            match Tree.tryFind t idw target root with
            | None -> Error(UnknownNode(target, Tree.ids t root))
            | Some sub ->
                match Tree.tryFind t idw newParent root with
                | None -> Error(UnknownNode(newParent, Tree.ids t root))
                | Some np when not (canHold np) -> Error(NotAContainer(newParent, w.KindTag np))
                | Some _ ->
                    // newParent must not be the target itself nor any of its descendants.
                    let below = Tree.ids t sub |> List.map idw.ToString |> Set.ofList

                    if below.Contains(idw.ToString newParent) then
                        let relation =
                            if idw.ToString newParent = idw.ToString target then
                                NestRelation.Self
                            else
                                NestRelation.Descendant

                        Error(WouldNestUnderSelf(target, relation))
                    else
                        match keyedHolderOf w t keyed idw target root with
                        | Some holder -> Error(KeyedPosition(target, w.Id holder))
                        | None -> Ok sub

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
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (node: 'Node)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        let target = w.Id node

        match Tree.tryFind t idw target root with
        | None -> Error(UnknownNode(target, Tree.ids t root))
        | Some existing ->
            let result = updated w existing node

            if not (List.isEmpty (w.Children result)) && not (canHold result) then
                Error(NotAContainer(target, w.KindTag result))
            else
                match keyed with
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
                                        if idw.Equals (w.Id n) target then
                                            w.Children n
                                        else
                                            t.Children n }

                        match Tree.firstRepeatedId idw (Tree.ids kept root) (incoming |> List.collect (Tree.ids t)) with
                        | Some d -> Error(DuplicateId d)
                        | None ->
                            match incoming |> List.tryPick (firstUncontainedOver t canHold w) with
                            | Some offender -> Error(NotAContainer(w.Id offender, w.KindTag offender))
                            | None -> Ok()

    /// The shared apply engine, parameterised by a container capability `canHold`
    /// (Phase 251). `apply` passes `(fun _ -> true)` — every node can hold children, so the
    /// behaviour is exactly as before; `applyContained` passes the domain predicate so an
    /// `InsertChild`/`MoveNode` under a leaf is a typed `NotAContainer` instead of a silent
    /// no-op (the F1 adoption finding). Since Phase 286 it also takes the witness it LOCATES
    /// through (`t`) and, for the keyed engine, the domain's keyed children (`keyed`); see the
    /// section head above.
    let rec private applyWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =

        let allIds () = Tree.ids t root
        let eq = idw.Equals

        match op with
        | InsertChild(parent, node) ->
            validateInsert canHold w t idw parent node root
            |> Result.bind (fun () ->
                Tree.updateNode t idw parent (fun p -> w.ReplaceChildren p (w.Children p @ [ node ])) root
                |> Option.map Ok
                |> Option.defaultValue (Error(UnknownNode(parent, allIds ()))))

        | RemoveNode target ->
            validateRemove w t keyed idw target root
            |> Result.bind (fun () ->
                match structuralParentOf w t idw target root with
                | None -> Error(UnknownNode(target, allIds ()))
                | Some p ->
                    Tree.updateNode
                        t
                        idw
                        (w.Id p)
                        (fun p ->
                            w.ReplaceChildren p (w.Children p |> List.filter (fun c -> not (eq (w.Id c) target))))
                        root
                    |> Option.map Ok
                    |> Option.defaultValue (Error(UnknownNode(target, allIds ()))))

        | ReorderChildren(parent, order) ->
            validateReorder w t idw parent order root
            |> Result.bind (fun () ->
                // the permutation is computed from the parent `updateNode` hands over, so the parent
                // is located once (Phase 298; a second `tryFind` walk with an unreachable `None` arm,
                // before)
                let permute (p: 'Node) =
                    let byId = w.Children p |> List.map (fun c -> idw.ToString(w.Id c), c) |> Map.ofList
                    w.ReplaceChildren p (order |> List.map (fun i -> byId[idw.ToString i]))

                Tree.updateNode t idw parent permute root
                |> Option.map Ok
                |> Option.defaultValue (Error(UnknownNode(parent, allIds ()))))

        | MoveNode(target, newParent) ->
            // every refusal is `validateMove`'s (Phase 298); after it, remove then graft. newParent
            // is not below the target, so the removal leaves it in the tree for the graft.
            validateMove canHold w t keyed idw target newParent root
            |> Result.bind (fun sub ->
                applyWith canHold w t keyed idw (RemoveNode target) root
                |> Result.bind (fun removed ->
                    Tree.updateNode t idw newParent (fun np -> w.ReplaceChildren np (w.Children np @ [ sub ])) removed
                    |> Option.map Ok
                    |> Option.defaultValue (Error(UnknownNode(newParent, Tree.ids t removed)))))

        | Batch ops ->
            // all-or-nothing: thread the tree; abort (leaving the original) on first failure.
            let rec go node =
                function
                | [] -> Ok node
                | o :: rest ->
                    match applyWith canHold w t keyed idw o node with
                    | Ok node' -> go node' rest
                    | Error e -> Error e

            go root ops

        | UpdateNode node ->
            let target = w.Id node

            validateUpdate canHold w t keyed idw node root
            |> Result.bind (fun () ->
                Tree.updateNode t idw target (fun existing -> updated w existing node) root
                |> Option.map Ok
                |> Option.defaultValue (Error(UnknownNode(target, allIds ()))))

    /// Apply one skeleton op. Every node is treated as able to hold children.
    let apply
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyWith (fun _ -> true) w w None idw op root

    /// Container-aware apply (Phase 251): an `InsertChild`/`MoveNode` whose (new) parent
    /// `canHold` rejects is a typed `NotAContainer`, not a silent no-op. Domains with closed
    /// kind-sets that have leaves dispatch through this. Containment *legality* (which kinds
    /// may parent which) stays domain-side — `canHold` answers only "can this node hold
    /// children at all".
    ///
    /// **Phase 161 (DECISIONS D38): an `InsertChild` also has its GRAFT inspected.** The subtree is
    /// walked and the first interior node that holds children while `canHold` refuses it earns the
    /// same `NotAContainer`, naming that node. So `applyContained` now keeps the invariant
    /// `contained_preserves` is about — every node with children satisfies `canHold` — against a
    /// graft as well as against a parent. The one premise no engine check can discharge is
    /// `child_blind`: a `canHold` that READS the child list can admit a node at the instant it is
    /// checked and refuse it the instant it gains one. That is the domain's obligation, certified
    /// by `Conformance.containerLaws` rather than assumed.
    ///
    /// **The unkeyed form (Phase 286).** It walks `Children` alone, so a node a domain holds in a
    /// keyed position is invisible to it — to its `DuplicateId` refusal as much as to its
    /// addressing. A domain with keyed positions calls `applyContainedKeyed` with its
    /// `KeyedWitness`; for a domain with none the two answer identically.
    let applyContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyWith canHold w w None idw op root

    /// `applyContained` over the keyed walk (Phase 286): the domain declares its keyed positions
    /// once, in `keyw`, and the engine's own refusals see them.
    ///
    /// - **`DuplicateId` sees every id-bearing position.** The insert scan is
    ///   `Tree.graftWellFormedKeyed`, so the refusal names an id the tree holds in a keyed position
    ///   OR one the graft carries into a keyed position, as well as the structural collisions it
    ///   always named; an `UpdateNode` payload's keyed subtrees are checked the same way against the
    ///   tree the target keeps.
    /// - **The engine LOCATES through `Tree.traversal nodew keyw`.** A node held in, or below, a
    ///   keyed position can be an insert's or a reorder's parent, a move's destination, an update's
    ///   target, and a remove's or move's target when it has a structural parent. `UnknownNode`
    ///   enumerates the ids the keyed walk reaches.
    /// - **The engine EDITS through `nodew`.** Structural ops append to, filter and permute
    ///   `Children` and rebuild through `ReplaceChildren`, exactly as `applyContained` does; the
    ///   keyed positions are never added to, vacated or reordered. A `RemoveNode` / `MoveNode` of a
    ///   node held directly in a keyed position is therefore refused as `KeyedPosition`.
    /// - **Containment** (`NotAContainer` over a graft's interior) walks the keyed subtrees too.
    ///
    /// For a domain whose `KeyedChildren` is `fun _ -> []` this returns exactly what
    /// `applyContained` returns, on every op and tree (`Conformance.keyedApplyLaws` runs both).
    let applyContainedKeyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyWith canHold nodew (Tree.traversal nodew keyw) (Some keyw.KeyedChildren) idw op root

    let private canApplyWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (t: NodeWitness<'Node, 'Id>)
        (keyed: ('Node -> 'Node list) option)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        match op with
        | InsertChild(parent, node) -> validateInsert canHold w t idw parent node root
        | RemoveNode target -> validateRemove w t keyed idw target root
        | ReorderChildren(parent, order) -> validateReorder w t idw parent order root
        | UpdateNode node -> validateUpdate canHold w t keyed idw node root
        // Phase 298 — the move's own checks, no tree built (it simulated through `apply` before)
        | MoveNode(target, newParent) -> validateMove canHold w t keyed idw target newParent root |> Result.map ignore
        | Batch _ -> applyWith canHold w t keyed idw op root |> Result.map ignore

    /// Dry-run validation (Phase 246): would `op` be accepted against `root`? Returns the
    /// exact `Rejection` `apply` would, but builds **no** new tree for the index/structure
    /// ops — `MoveNode` included since Phase 298 (`validateMove`). Only `Batch` is
    /// order-dependent, so its check simulates through `apply` (and discards the result). The
    /// AI pre-flight surface — "is this op legal?"
    /// — without committing the (potentially large) rebuild.
    let canApply
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        canApplyWith (fun _ -> true) w w None idw op root

    /// Container-aware dry-run (Phase 251) — the `canApply` mirror of `applyContained`.
    let canApplyContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        canApplyWith canHold w w None idw op root

    /// Keyed dry-run (Phase 286) — the `canApply` mirror of `applyContainedKeyed`: the same
    /// rejection it would return, without building the tree for the index/structure ops.
    let canApplyContainedKeyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        canApplyWith canHold nodew (Tree.traversal nodew keyw) (Some keyw.KeyedChildren) idw op root

    /// Apply a sequence non-atomically under a container capability (Phase 160) — the
    /// sequence-level `applyContained`, threading `applyWith canHold` so every step sees the
    /// capability the per-op surface has consulted since Phase 251.
    ///
    /// FIRST REFUSAL WINS, and a script is **not** a `Batch`. `Batch` is all-or-nothing inside
    /// one op: it aborts and the original tree survives. A script stops at the first refusal and
    /// returns the tree built so far, so the accepted prefix is kept. On failure the payload is a
    /// `ScriptRejection` (Phase 391; a positional triple before `1.0.0`): `Applied`, the number of
    /// ops applied before the refusal and so the 0-based position of the refused op in `ops`;
    /// `Rejection`, the envelope; and `Tree`, the tree the accepted prefix reached — the state the
    /// refused step was offered against.
    let applyAllWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, ScriptRejection<'Node, 'Id>> =
        let rec go i node =
            function
            | [] -> Ok node
            | o :: rest ->
                match applyWith canHold w w None idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e ->
                    Error
                        { Applied = i
                          Rejection = e
                          Tree = node }

        go 0 root ops

    /// Dry-run a sequence under a container capability (Phase 160) — the `canApplyAll` mirror of
    /// `applyAllWith`, and the pre-flight a container-aware executor needs: it reports the same
    /// first-refusal index and the same envelope `applyAllWith` would, `NotAContainer` included.
    ///
    /// The sequence is order-dependent, so this threads through `applyWith canHold` (each step's
    /// check sees the prior step's tree) and discards the materialised tree — the same shape
    /// `canApplyAll` has carried since Phase 246, with the capability now threaded.
    let canApplyAllWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        let rec go i node =
            function
            | [] -> Ok()
            | o :: rest ->
                match applyWith canHold w w None idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e -> Error(i, e)

        go 0 root ops

    /// Apply a sequence non-atomically, threading the tree. Every node is treated as able to hold
    /// children, so this is `applyAllWith (fun _ -> true)` and its behaviour is exactly what it
    /// was before Phase 160 — the instance relation `apply`/`applyContained` have carried since
    /// Phase 251, now at the sequence level. First refusal wins: on failure the `ScriptRejection` —
    /// how many ops applied, the envelope, and the partial tree built so far.
    let applyAll
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, ScriptRejection<'Node, 'Id>> =
        applyAllWith (fun _ -> true) w idw ops root

    /// Dry-run a sequence (Phase 246): report the first failing index + envelope without
    /// returning a tree. `canApplyAllWith (fun _ -> true)` since Phase 160 — the check consults
    /// no capability, so a script it certifies can still be refused by `applyContained` /
    /// `applyAllWith` at a step whose parent cannot hold children. A container-aware caller
    /// pre-flights with `canApplyAllWith` and executes with `applyAllWith`.
    let canApplyAll
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        canApplyAllWith (fun _ -> true) w idw ops root

    /// Dry-run a sequence over the keyed engine (Phase 247) — the `canApplyAllWith` mirror of
    /// `applyContainedKeyed`: each step is checked against the tree the earlier steps leave, under
    /// `canHold` and over the keyed walk, so it reports the first-refusal index and the envelope a
    /// keyed executor would meet — a `DuplicateId` on an id held in a keyed position, a
    /// `KeyedPosition`, a `NotAContainer` — and discards the materialised tree. It is the `canApply` a
    /// keyed domain hands `Arbitration.arbitrateWith`. For a domain whose `KeyedChildren` is
    /// `fun _ -> []` it answers exactly what `canApplyAllWith canHold` answers.
    let canApplyAllKeyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        let t = Tree.traversal nodew keyw

        let rec go i node =
            function
            | [] -> Ok()
            | o :: rest ->
                match applyWith canHold nodew t (Some keyw.KeyedChildren) idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e -> Error(i, e)

        go 0 root ops

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

        /// The index of `post` — the tree `apply w idw op` returned for the tree `ix` indexes —
        /// carried through `op` rather than rebuilt (Phase 317). Every op kind is handled, a
        /// `Batch` folded through its steps without the intermediate trees: the op names the
        /// containers it rewrote and the subtrees it grafted or removed, and only those, their
        /// ancestors (whose node values every edit rebuilds) and the grafted and removed subtrees
        /// are re-indexed, through `Tree.Index.rebind`.
        ///
        /// **The law:** `afterOp w idw op post (Tree.Index.build w idw pre) ≡ Tree.Index.build w
        /// idw post` — the same `Root`, `ParentOf` and `Fingerprint`, the same `ById` keys, and at
        /// each key a node equal to `post`'s (a node outside the edit keeps the value the index
        /// held, which `apply` rebuilt from the same content). So `isFreshFor post` holds of it.
        ///
        /// **The cost** is the op's, not the tree's: per step, the depth times the fanout along each
        /// rewritten container's path, plus the size of each grafted, removed or rewritten subtree's
        /// root record — and never a walk of the tree. **The guard:** what the tracking predicts is
        /// checked against `post` on the nodes it re-indexes (the root's id, every re-indexed node
        /// found where the tracking puts it, with the children the tracking says it has); a `post`
        /// that disagrees — an op `apply` refused, a tree from elsewhere, an index built under
        /// another witness — falls back to `Tree.Index.build w idw post`, so the law holds whatever
        /// the caller hands it, and only the cost depends on the contract.
        let afterOp
            (w: NodeWitness<'Node, 'Id>)
            (idw: IdWitness<'Id>)
            (op: SkeletonOp<'Node, 'Id>)
            (post: 'Node)
            (ix: Tree.NodeIndex<'Node, 'Id>)
            : Tree.NodeIndex<'Node, 'Id> =
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
                        let graft = Tree.preorder w node

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
                    let rec go (s: Track<'Node, 'Id>) =
                        function
                        | [] -> Some s
                        | o :: rest ->
                            match step s o with
                            | Some s' -> go s' rest
                            | None -> None

                    go st ops

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
            | Some ix' -> ix'
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
            // Thread `pre` through the forward ops; invert each against the state it saw.
            // Prepending accumulates the inverses in reverse forward order — exactly the
            // order the undo batch must run.
            let rec go acc state =
                function
                | [] -> Ok(Batch acc)
                | o :: rest ->
                    match invert w idw o state with
                    | Error e -> Error e
                    | Ok inv ->
                        match apply w idw o state with
                        | Ok state' -> go (inv :: acc) state' rest
                        | Error e -> Error e

            go [] pre ops

        | _ ->
            // A single op is invertible iff it would apply to `pre`.
            match canApply w idw op pre with
            | Error e -> Error e
            | Ok() ->
                match op with
                | InsertChild(_, node) -> Ok(RemoveNode(w.Id node))
                | RemoveNode target ->
                    let parent = Tree.parentOf w idw target pre |> Option.get
                    let sub = Tree.tryFind w idw target pre |> Option.get
                    Ok(restoring parent target (InsertChild(w.Id parent, sub)))
                | MoveNode(target, _) ->
                    let parent = Tree.parentOf w idw target pre |> Option.get
                    Ok(restoring parent target (MoveNode(target, w.Id parent)))
                | ReorderChildren(parent, _) ->
                    let p = Tree.tryFind w idw parent pre |> Option.get
                    Ok(ReorderChildren(parent, p |> w.Children |> List.map w.Id))
                // The pre-state node's CONTENT restores the content; the inverse carries it as a
                // shell (`ReplaceChildren old []`, Phase 305), because `UpdateNode` never reads its
                // payload's children — the children the tree holds when the undo runs are kept —
                // and an undo stack that stored the whole pre-state subtree per edit held, and
                // never read, a copy of everything below the node.
                | UpdateNode node ->
                    Ok(UpdateNode(w.ReplaceChildren (Tree.tryFind w idw (w.Id node) pre |> Option.get) []))
                | Batch _ -> Ok op // unreachable (handled above) — keeps the match total

    /// The script-level inverse (Phase 305): the inverses of `ops`, each derived against the state
    /// the forward op saw, in REVERSE order — so `applyAll (invertAll w idw ops pre) (applyAll w idw
    /// ops pre) = pre`, the law `invert` states per op lifted to the sequence. It refuses as
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
        let rec go i acc state =
            function
            | [] -> Ok acc
            | o :: rest ->
                match invert w idw o state with
                | Error e -> Error(i, e)
                | Ok inv ->
                    match apply w idw o state with
                    | Ok state' -> go (i + 1) (inv :: acc) state' rest
                    | Error e -> Error(i, e)

        go 0 [] pre ops

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
    // So the last two clauses of `independent` below are NECESSARY, not a placeholder. Tightening
    // them needs a footprint that can NAME the difference — a fifth address kind carrying the
    // relocation's kind, or a destroyed-subtree set the pure script cannot compute — which is a
    // change to the record, not to a clause. `relocation_move_pair_also_fails` adds the second
    // reason: two moves nesting into each other's subtrees reject each other with
    // `WouldNestUnderSelf`, and no record could free that pair at all.

    let private emptyFootprint = Footprint.empty

    let private unionFootprint (a: Footprint) (b: Footprint) : Footprint = Footprint.union a b

    // The one fold behind `footprint` and `footprintKeyed` (Phase 247). `carried` reads the ids an
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
                // (the keyed walk of the graft under `footprintKeyed`, as the keyed engine reads it),
                // so `Reads` describes a read that happens rather than one the footprint assumed. The
                // set is unchanged: the validator's other half (is the subtree unique WITHIN ITSELF?) is
                // internal to the op and reads no tree state, so it adds nothing to the footprint and
                // creates no new collision between concurrent scripts.
                { emptyFootprint with
                    Reads = Set.add (key parent) inserted
                    StructureWrites = Set.singleton (key parent)
                    ContentWrites = inserted }
            | RemoveNode target ->
                // the node is destroyed (content-write on the target) and its unknown source parent's
                // child-list is rewritten (the pinned over-approximation).
                { emptyFootprint with
                    Reads = Set.singleton (key target)
                    ContentWrites = Set.singleton (key target)
                    UnknownParentWrites = Set.singleton (key target) }
            | MoveNode(target, newParent) ->
                // relocation: the destination child-list is a known structure-write; the target is
                // content-written (it moves); the source parent's child-list is the unknown over-approx.
                { emptyFootprint with
                    Reads = Set.ofList [ key target; key newParent ]
                    StructureWrites = Set.singleton (key newParent)
                    ContentWrites = Set.singleton (key target)
                    UnknownParentWrites = Set.singleton (key target) }
            | ReorderChildren(parent, order) ->
                // the parent's child-list order is rewritten (known structure-write); the named children
                // are read (their positions are permuted, their content is not touched).
                let named = order |> List.map key |> Set.ofList

                { emptyFootprint with
                    Reads = Set.add (key parent) named
                    StructureWrites = Set.singleton (key parent) }
            | Batch inner -> List.fold (fun acc o -> unionFootprint acc (ofOp o)) emptyFootprint inner
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
                { emptyFootprint with
                    Reads = Set.add target incoming
                    ContentWrites = Set.add target incoming
                    UnknownParentWrites = Set.singleton target }

        List.fold (fun acc op -> unionFootprint acc (ofOp op)) emptyFootprint ops

    /// The read/write footprint of an op-script (Phase 78) — a pure, total derivation over the skeleton
    /// five through the node/id witnesses (the `NodeWitness` reads the ids out of an inserted `'Node`
    /// subtree; the `IdWitness` keys every address by its string form). `Batch` folds its inner ops.
    /// Total: no tree, no failure case — it never throws (GP4) and mints no ids. Over-approximating by
    /// design — see `Footprint` and STABILITY.md for the pinned conservative cases.
    ///
    /// **The unkeyed form (Phase 247).** It reads an inserted subtree's ids over `Children` alone, so
    /// an id a domain holds in a keyed position inside the graft is not in the footprint: two scripts
    /// that each insert a subtree carrying the same keyed id are declared independent and collide only
    /// at replay. A domain with keyed positions calls `footprintKeyed` with its `KeyedWitness`; for a
    /// domain with none the two answer identically.
    let footprint (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (ops: SkeletonOp<'Node, 'Id> list) : Footprint =
        footprintOver w idw (Tree.ids w) (fun _ -> []) ops

    /// `footprint` over the keyed walk (Phase 247) — the footprint of the script
    /// `Ops.applyContainedKeyed` runs. The id set an `InsertChild` authors is the graft's keyed walk,
    /// `Tree.idsKeyed nodew keyw node`, so an id held in a keyed position anywhere inside the inserted
    /// subtree is read and content-written as a structural one is; and an `UpdateNode` payload's keyed
    /// subtrees, which the keyed engine checks as new content, are read and content-written too. Two
    /// scripts that each bring in the same keyed id therefore fail `independent` with
    /// `Interference.SameTarget` on it — the collision `footprint` cannot see.
    ///
    /// Every other address is `footprint`'s: the keyed engine edits through `Children` alone, so the
    /// structural writes are unchanged, and an op that locates a node below a keyed position names it by
    /// the same id key. For a domain whose `KeyedChildren` is `fun _ -> []` this returns exactly what
    /// `footprint` returns. Total, no throws (GP4); it mints no ids.
    let footprintKeyed
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        footprintOver
            nodew
            idw
            (Tree.idsKeyed nodew keyw)
            (fun n -> keyw.KeyedChildren n |> List.collect (Tree.idsKeyed nodew keyw))
            ops

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
    /// `applyAll w idw (lower w t) (skeletonRoot w t) = Ok t`. **The contained form is the same
    /// script under `applyAllWith canHold`**: every node that holds children is an insert's parent,
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
    // Phase 313), and every surface that reads it — the engine below, `Diff.toOpsGrammar`,
    // `Arbitration.arbitrateGrammar`, `Validator.containment`, `Conformance.containmentLaws` — reads
    // it through `isLegalChild`, so they share ONE definition of a legal child.
    //
    // The grammar and reference forms WRAP the container-aware engine rather than widening it: each
    // non-batch step is first decided exactly as `applyContained` decides it, and the new clauses run
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

    /// The grammar clause of one accepted non-batch step: the parent→child pairs the step CREATES,
    /// checked in a fixed order. An insert creates its node under the parent, then every pair inside
    /// the graft (preorder); a move creates the moved node under its new parent — the pairs inside
    /// the moved subtree already stood in the tree, and refusing the move for them would be an
    /// invariant-repair gate rather than a check of the move (D38's reasoning for `NotAContainer`);
    /// an in-place rewrite can change the node's kind, so it creates the node under its parent and
    /// each of its kept children under it. Removes and reorders create none.
    let private grammarRefusal
        (allowedChildren: string -> string list option)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Rejection<'Id> option =
        let legal (parent: 'Node) (child: 'Node) =
            isLegalChild allowedChildren (w.KindTag parent) (w.KindTag child)

        match op with
        | InsertChild(parent, node) ->
            match Tree.tryFind w idw parent before with
            | Some p when not (legal p node) -> Some(illegalChild allowedChildren w p node)
            | _ ->
                illegalChildren allowedChildren w node
                |> List.tryHead
                |> Option.map (fun (p, c) -> illegalChild allowedChildren w p c)
        | MoveNode(target, newParent) ->
            match Tree.tryFind w idw newParent before, Tree.tryFind w idw target before with
            | Some np, Some t when not (legal np t) -> Some(illegalChild allowedChildren w np t)
            | _ -> None
        | UpdateNode node ->
            let target = w.Id node

            match Tree.tryFind w idw target after with
            | None -> None
            | Some rewritten ->
                match Tree.parentOf w idw target after with
                | Some p when not (legal p rewritten) -> Some(illegalChild allowedChildren w p rewritten)
                | _ ->
                    w.Children rewritten
                    |> List.tryFind (fun c -> not (legal rewritten c))
                    |> Option.map (illegalChild allowedChildren w rewritten)
        | RemoveNode _
        | ReorderChildren _
        | Batch _ -> None

    /// The reference clause of one accepted non-batch step: the ids the tree declared before the step
    /// and no longer declares after it are ORPHANED, and a node of the resulting tree that refers to
    /// one is a referrer — `StillReferenced`, naming the step's target (a remove's target, a
    /// rewrite's node) and every referrer in preorder. Only a `RemoveNode` (its subtree's
    /// declarations leave) and an `UpdateNode` (the rewritten node may declare less) can orphan an id;
    /// an insert, a move or a reorder keeps every declaration. A reference that already dangled before
    /// the step is not the step's doing and is not reported.
    let private referenceRefusal
        (refw: RefWitness<'Node, 'Id>)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Rejection<'Id> option =
        let key (i: 'Id) = idw.ToString i

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

    /// The wrapped engine: `applyContained` decides each non-batch step, and `check` — handed the
    /// step, the tree before it and the tree after it — runs only on a step it accepted. A `Batch` is
    /// threaded through this same function, all-or-nothing, exactly as the engine threads one.
    let rec private applyChecked
        (check: SkeletonOp<'Node, 'Id> -> 'Node -> 'Node -> Rejection<'Id> option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        match op with
        | Batch ops ->
            let rec go node =
                function
                | [] -> Ok node
                | o :: rest ->
                    match applyChecked check canHold w idw o node with
                    | Ok node' -> go node' rest
                    | Error e -> Error e

            go root ops
        | _ ->
            match applyContained canHold w idw op root with
            | Error e -> Error e
            | Ok after ->
                match check op root after with
                | Some r -> Error r
                | None -> Ok after

    /// The sequence form of `applyChecked`: first refusal wins, `applyAllWith`'s `ScriptRejection`.
    let private applyAllChecked
        (check: SkeletonOp<'Node, 'Id> -> 'Node -> 'Node -> Rejection<'Id> option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, ScriptRejection<'Node, 'Id>> =
        let rec go i node =
            function
            | [] -> Ok node
            | o :: rest ->
                match applyChecked check canHold w idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e ->
                    Error
                        { Applied = i
                          Rejection = e
                          Tree = node }

        go 0 root ops

    let private grammarCheck allowedChildren (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) =
        fun op before after -> grammarRefusal allowedChildren w idw op before after

    let private referenceCheck
        (refw: RefWitness<'Node, 'Id>)
        allowedChildren
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        =
        fun op before after ->
            match grammarRefusal allowedChildren w idw op before after with
            | Some r -> Some r
            | None -> referenceRefusal refw w idw op before after

    /// Grammar-aware apply (Phase 313) — `applyContained` with the domain's containment grammar
    /// beside `canHold`. Every step the container-aware engine refuses is refused with the same
    /// envelope; a step it accepts is then refused with `IllegalChild` when it would leave a child
    /// under a parent whose kind `allowedChildren` does not let hold it (see the section head for
    /// which pairs each op creates). For a tree that keeps the grammar, every tree this returns keeps
    /// it — `Conformance.containmentLaws`, and `grammar_preserves` in `proofs/Preservation.fst`.
    /// With `allowedChildren = fun _ -> None` it answers exactly what `applyContained` answers.
    let applyGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyChecked (grammarCheck allowedChildren w idw) canHold w idw op root

    /// The dry run of `applyGrammar`: its exact envelope, and no tree returned. The grammar clause
    /// reads the step's result, so unlike `canApplyContained` this builds the edited tree.
    let canApplyGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        applyGrammar allowedChildren canHold w idw op root |> Result.map ignore

    /// The sequence form of `applyGrammar` — `applyAllWith`'s contract (first refusal wins; the
    /// `ScriptRejection` naming how many ops applied, the envelope and the tree the accepted prefix
    /// reached) under the grammar.
    let applyAllGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, ScriptRejection<'Node, 'Id>> =
        applyAllChecked (grammarCheck allowedChildren w idw) canHold w idw ops root

    /// The dry run of `applyAllGrammar`, in `canApplyAllWith`'s shape — the `canApply` a domain with
    /// a grammar hands `Arbitration.arbitrateWith` (`Arbitration.arbitrateGrammar` is that
    /// composition).
    let canApplyAllGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        match applyAllGrammar allowedChildren canHold w idw ops root with
        | Ok _ -> Ok()
        | Error r -> Error(r.Applied, r.Rejection)

    /// Reference-aware apply (Phase 313) — `applyGrammar` under a `RefWitness` as well: a
    /// `RemoveNode` or `UpdateNode` it accepts is then refused with `StillReferenced` when the tree
    /// after it no longer declares an id it declared before and a node of that tree still refers to
    /// it — a removed subtree's declarations, or what a rewrite stops declaring. So an accepted op
    /// never leaves dangling a reference that resolved before it. A reference an INSERT or an
    /// in-place rewrite brings in is not refused here: whether it resolves is
    /// `Validator.referenceIntegrity`'s report, because a document under construction legitimately
    /// refers ahead of what it has declared. Pass `fun _ -> None` for a domain with no grammar.
    let applyReferenced
        (refw: RefWitness<'Node, 'Id>)
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyChecked (referenceCheck refw allowedChildren w idw) canHold w idw op root

    /// The dry run of `applyReferenced`.
    let canApplyReferenced
        (refw: RefWitness<'Node, 'Id>)
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        applyReferenced refw allowedChildren canHold w idw op root |> Result.map ignore

    /// The sequence form of `applyReferenced`, `applyAllWith`'s contract.
    let applyAllReferenced
        (refw: RefWitness<'Node, 'Id>)
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, ScriptRejection<'Node, 'Id>> =
        applyAllChecked (referenceCheck refw allowedChildren w idw) canHold w idw ops root

    /// The dry run of `applyAllReferenced`, in `canApplyAllWith`'s shape.
    let canApplyAllReferenced
        (refw: RefWitness<'Node, 'Id>)
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        match applyAllReferenced refw allowedChildren canHold w idw ops root with
        | Ok _ -> Ok()
        | Error r -> Error(r.Applied, r.Rejection)

    /// `footprint` under a `RefWitness` (Phase 313): every id the script writes a reference to is
    /// READ — the references every node of an inserted subtree carries, and those of an `UpdateNode`
    /// payload — so a script that writes a reference to `x` fails `independent` against a script that
    /// destroys `x`, by `Interference.RightWritesLeftReads` / `LeftWritesRightReads` naming `x`.
    /// Every other address is `footprint`'s, so this only ever ADDS collisions: it is as sound as
    /// `footprint` is, and `independent` over it is never more permissive.
    ///
    /// Among the skeleton ops alone the remove-versus-reference race was already serialised, by the
    /// pinned unknown-parent clause (a remove collides with every structural write, an update is one).
    /// What this adds is the collision NAMED for the reference, and the same read for a domain op that
    /// writes a reference and no structure (`Footprint.reading`), where no other clause catches it.
    /// The read meets a removal's content-write when the declared id IS the declaring node's id; a
    /// domain declaring names that are not node ids folds the names a removal destroys into its own
    /// removal footprint.
    let footprintReferenced
        (refw: RefWitness<'Node, 'Id>)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : Footprint =
        let rec written (op: SkeletonOp<'Node, 'Id>) : 'Id list =
            match op with
            | InsertChild(_, node) -> Tree.preorder w node |> List.collect refw.RefsOf
            | UpdateNode node -> refw.RefsOf node
            | Batch inner -> inner |> List.collect written
            | RemoveNode _
            | MoveNode _
            | ReorderChildren _ -> []

        Footprint.union (footprint w idw ops) (Footprint.reading (ops |> List.collect written |> List.map idw.ToString))
