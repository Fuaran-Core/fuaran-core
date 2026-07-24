namespace Fuaran.Core

/// The recoverable error-envelope contract — the single most valuable thing to
/// standardise across domains (it is the AI-feedback protocol). Every rejection
/// *names the failure and enumerates the valid alternatives*, so an orchestrator
/// reads one rejection shape regardless of tier. Domain defect codes that don't fit
/// the skeleton plug in through `Rejected (code, message)`. Total — failures are
/// data, never exceptions.
type Rejection<'Id> =
    /// `target` is not in the tree; `addressable` enumerates the ids that are.
    | UnknownNode of target: 'Id * addressable: 'Id list
    /// An index op addressed a parent whose child count is `count`.
    | IndexOutOfRange of parent: 'Id * index: int * count: int
    /// Inserting / moving a node whose id already exists in the tree.
    | DuplicateId of 'Id
    /// The root cannot be removed or moved.
    | CannotRemoveRoot
    /// A move whose new parent is the target itself or one of its descendants.
    | WouldNestUnderSelf of 'Id
    /// The (new) parent of an insert/move is a leaf that cannot hold children (Phase 251).
    /// Only the container-aware `applyContained` / `canApplyContained` raise this; the
    /// plain `apply` / `canApply` treat every node as able to hold children.
    | NotAContainer of target: 'Id * kindTag: string
    /// A reorder whose proposed order is not a permutation of the parent's children.
    | ReorderMismatch of parent: 'Id * expected: 'Id list * got: 'Id list
    /// Domain-side extension point: a per-kind property-edit rejection.
    | Rejected of code: string * message: string

/// The skeleton-five structural-edit ops shared by every domain. Per-kind property
/// edits (`SetInput`, `SetParameter`, `SetVariable`, `UpdateProp`, …) stay domain-side
/// and compose with these. `Batch` is all-or-nothing.
type SkeletonOp<'Node, 'Id> =
    | InsertChild of parent: 'Id * index: int * node: 'Node
    | RemoveNode of target: 'Id
    | MoveNode of target: 'Id * newParent: 'Id * index: int
    | ReorderChildren of parent: 'Id * order: 'Id list
    | Batch of SkeletonOp<'Node, 'Id> list

/// The structural read/write **footprint** of an op-script (Phase 78) — the multi-agent coordination
/// invariant computed *from the script*, never separately declared (the `paramsOf` precedent). The
/// addresses are id-keys (`IdWitness.ToString` form — no `comparison` demanded of `'Id`), split by the
/// role the analysis needs to decide disjointness:
///
///   - `Reads` — node ids whose existence / identity an op depends on (a parent lookup, a move target,
///     a reorder's named children, the dup-id check on an inserted subtree). Every structural anchor is
///     also a read (the parent must exist), so a script that *creates or destroys* a node the other
///     script uses as an anchor collides through a content-vs-read overlap.
///   - `StructureWrites` — parent ids whose child-list membership / order an op changes (shifting sibling
///     positions): `InsertChild.parent`, `ReorderChildren.parent`, `MoveNode.newParent`. A **known**
///     structural position.
///   - `ContentWrites` — node ids whose own node is authored / destroyed / relocated (the identity-level
///     write): an `InsertChild`'s whole inserted subtree, a `RemoveNode`/`MoveNode` target. Distinct from
///     a structure-write: it is *which node*, not *whose child-list*. (A domain that layers an in-place
///     property-edit op on top populates this the same way — two content-writes to one node collide.)
///   - `UnknownParentWrites` — the targets of `RemoveNode` / `MoveNode`. Removing or moving a node also
///     rewrites its *source* parent's child-list, but that parent (and every ancestor relationship) is a
///     **tree fact the pure script cannot name** — so such an op is the conservative case: it collides
///     with *every* structural write in a concurrent script. THE pinned over-approximation (STABILITY.md
///     "Op-script footprint + independence").
///
/// **Conservativity is the contract:** `footprint` over-approximates — it records more collisions than a
/// tree-aware analysis would. `Ops.independent = true` is therefore a *promise* (the scripts provably
/// commute); `independent = false` is always a safe answer, never a defect report.
type Footprint =
    { Reads: Set<string>
      StructureWrites: Set<string>
      ContentWrites: Set<string>
      UnknownParentWrites: Set<string> }

/// The generic apply engine over the skeleton ops. Total: every failure is a typed
/// `Rejection` envelope. Generic over the `NodeWitness` / `IdWitness` — no domain
/// `NodeKind` is ever in scope.
module Ops =

    let private insertAt (i: int) (x: 'a) (xs: 'a list) =
        let before = xs |> List.truncate i
        let after = xs |> List.skip i
        before @ [ x ] @ after

    // ---- validation (Phase 246) ----
    // The structural checks each op must pass, factored out of `apply` so the dry-run
    // `canApply` and the mutating `apply` share one source of validation truth and can
    // never diverge. The three index/structure ops validate without building a tree;
    // `MoveNode` and `Batch` are order-dependent (a later step sees an earlier step's
    // tree), so their validation simulates through `apply` — the rejection returned is
    // exactly the one `apply` would produce.

    let private validateInsert
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (index: int)
        (node: 'Node)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        if Tree.exists w idw (w.Id node) root then
            Error(DuplicateId(w.Id node))
        elif not (Tree.exists w idw parent root) then
            Error(UnknownNode(parent, Tree.ids w root))
        else
            match Tree.tryFind w idw parent root with
            | None -> Error(UnknownNode(parent, Tree.ids w root))
            | Some p when not (canHold p) -> Error(NotAContainer(parent, w.KindTag p))
            | Some p ->
                let count = w.Children p |> List.length

                if index < 0 || index > count then
                    Error(IndexOutOfRange(parent, index, count))
                else
                    Ok()

    let private validateRemove
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        if idw.Equals (w.Id root) target then
            Error CannotRemoveRoot
        elif not (Tree.exists w idw target root) then
            Error(UnknownNode(target, Tree.ids w root))
        else
            Ok()

    let private validateReorder
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (order: 'Id list)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        match Tree.tryFind w idw parent root with
        | None -> Error(UnknownNode(parent, Tree.ids w root))
        | Some p ->
            let current = w.Children p |> List.map w.Id

            let key xs =
                xs |> List.map idw.ToString |> List.sort

            if key current <> key order then
                Error(ReorderMismatch(parent, current, order))
            else
                Ok()

    /// The shared apply engine, parameterised by a container capability `canHold`
    /// (Phase 251). `apply` passes `(fun _ -> true)` — every node can hold children, so the
    /// behaviour is exactly as before; `applyContained` passes the domain predicate so an
    /// `InsertChild`/`MoveNode` under a leaf is a typed `NotAContainer` instead of a silent
    /// no-op (the F1 adoption finding).
    let rec private applyWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =

        let allIds () = Tree.ids w root
        let eq = idw.Equals

        match op with
        | InsertChild(parent, index, node) ->
            validateInsert canHold w idw parent index node root
            |> Result.bind (fun () ->
                Tree.updateNode w idw parent (fun p -> w.ReplaceChildren p (insertAt index node (w.Children p))) root
                |> Option.map Ok
                |> Option.defaultValue (Error(UnknownNode(parent, allIds ()))))

        | RemoveNode target ->
            validateRemove w idw target root
            |> Result.bind (fun () ->
                match Tree.parentOf w idw target root with
                | None -> Error(UnknownNode(target, allIds ()))
                | Some p ->
                    Tree.updateNode
                        w
                        idw
                        (w.Id p)
                        (fun p ->
                            w.ReplaceChildren p (w.Children p |> List.filter (fun c -> not (eq (w.Id c) target))))
                        root
                    |> Option.map Ok
                    |> Option.defaultValue (Error(UnknownNode(target, allIds ()))))

        | ReorderChildren(parent, order) ->
            validateReorder w idw parent order root
            |> Result.bind (fun () ->
                match Tree.tryFind w idw parent root with
                | None -> Error(UnknownNode(parent, allIds ()))
                | Some p ->
                    let byId = w.Children p |> List.map (fun c -> idw.ToString(w.Id c), c) |> Map.ofList
                    let reordered = order |> List.map (fun i -> byId.[idw.ToString i])

                    Tree.updateNode w idw parent (fun p -> w.ReplaceChildren p reordered) root
                    |> Option.map Ok
                    |> Option.defaultValue (Error(UnknownNode(parent, allIds ()))))

        | MoveNode(target, newParent, index) ->
            if eq (w.Id root) target then
                Error CannotRemoveRoot
            elif not (Tree.exists w idw target root) then
                Error(UnknownNode(target, allIds ()))
            elif not (Tree.exists w idw newParent root) then
                Error(UnknownNode(newParent, allIds ()))
            else
                match Tree.tryFind w idw newParent root with
                | Some np0 when not (canHold np0) -> Error(NotAContainer(newParent, w.KindTag np0))
                | _ ->

                    match Tree.tryFind w idw target root with
                    | None -> Error(UnknownNode(target, allIds ()))
                    | Some sub ->
                        // newParent must not be the target itself nor any of its descendants.
                        let descendantIds = Tree.ids w sub |> Set.ofList |> Set.map idw.ToString

                        if descendantIds.Contains(idw.ToString newParent) then
                            Error(WouldNestUnderSelf target)
                        else
                            // remove then insert: both halves already validated above.
                            match applyWith canHold w idw (RemoveNode target) root with
                            | Error e -> Error e
                            | Ok removed ->
                                // re-target into the removed tree (newParent still present there).
                                match Tree.tryFind w idw newParent removed with
                                | None -> Error(UnknownNode(newParent, Tree.ids w removed))
                                | Some np ->
                                    let count = w.Children np |> List.length

                                    if index < 0 || index > count then
                                        Error(IndexOutOfRange(newParent, index, count))
                                    else
                                        Tree.updateNode
                                            w
                                            idw
                                            newParent
                                            (fun np -> w.ReplaceChildren np (insertAt index sub (w.Children np)))
                                            removed
                                        |> Option.map Ok
                                        |> Option.defaultValue (Error(UnknownNode(newParent, Tree.ids w removed)))

        | Batch ops ->
            // all-or-nothing: thread the tree; abort (leaving the original) on first failure.
            let rec go node =
                function
                | [] -> Ok node
                | o :: rest ->
                    match applyWith canHold w idw o node with
                    | Ok node' -> go node' rest
                    | Error e -> Error e

            go root ops

    /// Apply one skeleton op. Every node is treated as able to hold children.
    let apply
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyWith (fun _ -> true) w idw op root

    /// Container-aware apply (Phase 251): an `InsertChild`/`MoveNode` whose (new) parent
    /// `canHold` rejects is a typed `NotAContainer`, not a silent no-op. Domains with closed
    /// kind-sets that have leaves dispatch through this. Containment *legality* (which kinds
    /// may parent which) stays domain-side — `canHold` answers only "can this node hold
    /// children at all".
    let applyContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<'Node, Rejection<'Id>> =
        applyWith canHold w idw op root

    let private canApplyWith
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        match op with
        | InsertChild(parent, index, node) -> validateInsert canHold w idw parent index node root
        | RemoveNode target -> validateRemove w idw target root
        | ReorderChildren(parent, order) -> validateReorder w idw parent order root
        | MoveNode _
        | Batch _ -> applyWith canHold w idw op root |> Result.map ignore

    /// Dry-run validation (Phase 246): would `op` be accepted against `root`? Returns the
    /// exact `Rejection` `apply` would, but builds **no** new tree for the index/structure
    /// ops. `MoveNode` / `Batch` are order-dependent, so their check simulates through
    /// `apply` (and discards the result). The AI pre-flight surface — "is this op legal?"
    /// — without committing the (potentially large) rebuild.
    let canApply
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        canApplyWith (fun _ -> true) w idw op root

    /// Container-aware dry-run (Phase 251) — the `canApply` mirror of `applyContained`.
    let canApplyContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (root: 'Node)
        : Result<unit, Rejection<'Id>> =
        canApplyWith canHold w idw op root

    /// Apply a sequence non-atomically, threading the tree. On failure returns the
    /// failing index, the envelope, and the partial tree built so far (the shape every
    /// domain `applyAll` returns).
    let applyAll
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<'Node, int * Rejection<'Id> * 'Node> =
        let rec go i node =
            function
            | [] -> Ok node
            | o :: rest ->
                match apply w idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e -> Error(i, e, node)

        go 0 root ops

    /// Dry-run a sequence (Phase 246): report the first failing index + envelope without
    /// returning a tree. The sequence is order-dependent, so this threads through `apply`
    /// (each step's check sees the prior step's tree) and discards the materialised tree.
    let canApplyAll
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (root: 'Node)
        : Result<unit, int * Rejection<'Id>> =
        let rec go i node =
            function
            | [] -> Ok()
            | o :: rest ->
                match apply w idw o node with
                | Ok node' -> go (i + 1) node' rest
                | Error e -> Error(i, e)

        go 0 root ops

    /// Derive the inverse of an op from the **pre-state** tree (the tree the op applied to)
    /// — Phase 242. The skeleton-five are structural, so each inverse is recoverable from
    /// the pre-state: insert↔remove, remove↔insert (capturing the removed subtree + its
    /// parent + index), move↔move-back (prior parent + index), reorder↔reorder (prior
    /// order). `Batch` inverts to its inverses in reverse order (each derived against the
    /// state that op saw). Total: a non-applyable op has no inverse — its `Rejection` is
    /// returned. The defining law: `apply (invert op pre) (apply op pre) = pre`. Undo/redo
    /// becomes a generic capability over the witness, not a per-domain re-implementation.
    let rec invert
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (pre: 'Node)
        : Result<SkeletonOp<'Node, 'Id>, Rejection<'Id>> =

        // The position of `childId` among `parentId`'s children in the pre-state.
        let positionIn (parentNode: 'Node) (childId: 'Id) =
            parentNode
            |> w.Children
            |> List.findIndex (fun c -> idw.Equals (w.Id c) childId)

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
                | InsertChild(_, _, node) -> Ok(RemoveNode(w.Id node))
                | RemoveNode target ->
                    let parent = Tree.parentOf w idw target pre |> Option.get
                    let sub = Tree.tryFind w idw target pre |> Option.get
                    Ok(InsertChild(w.Id parent, positionIn parent target, sub))
                | MoveNode(target, _, _) ->
                    let parent = Tree.parentOf w idw target pre |> Option.get
                    Ok(MoveNode(target, w.Id parent, positionIn parent target))
                | ReorderChildren(parent, _) ->
                    let p = Tree.tryFind w idw parent pre |> Option.get
                    Ok(ReorderChildren(parent, p |> w.Children |> List.map w.Id))
                | Batch _ -> Ok op // unreachable (handled above) — keeps the match total

    /// Normalise an op script (Phase 23): a conservative, structural peephole that collapses the
    /// redundancy classes it can prove safe *without the tree*, leaving everything else untouched.
    /// The defining law (certified by `Conformance.normalizeLaws`): for any script applyable to a
    /// tree, `applyAll (normalize ops) = applyAll ops` — normalisation never changes the result.
    /// Collapses, all on adjacent ops so no intervening op observes the discarded state:
    ///   - `InsertChild(p,i,node)` then `RemoveNode(id node)` — insert-then-remove of the same node
    ///     nets to nothing (the insert+remove cancel `p`'s child-shift, so later ops are unaffected);
    ///   - `MoveNode(t,_,_)` then `MoveNode(t,p,i)` — the first relocation is superseded by the
    ///     second (the intermediate parent is restored, since `t` is not in `p`'s subtree in any
    ///     applyable script), so only the net move survives;
    ///   - `ReorderChildren(p,_)` then `ReorderChildren(p,o)` — a reorder sets the full order, so the
    ///     last one on a parent wins;
    ///   - an empty `Batch []` is dropped, and a `Batch` is normalised recursively.
    /// Left-to-right peephole passes iterated to a fixpoint, so it is genuinely **idempotent**
    /// (`normalize ∘ normalize = normalize`) and never lengthens a script. `'Node` needs no equality
    /// (it compares ids only). **Caveat:** preservation is guaranteed only for a script that is
    /// *applyable* to the tree — collapsing an insert/remove pair can turn an `applyAll` that would
    /// have *failed* at that pair into one that succeeds, so normalise after validating, not before.
    let rec normalize
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        : SkeletonOp<'Node, 'Id> list =
        let eq = idw.Equals

        // 1. normalise inside batches; drop empties
        let stripped =
            ops
            |> List.collect (fun op ->
                match op with
                | Batch inner ->
                    match normalize w idw inner with
                    | [] -> []
                    | xs -> [ Batch xs ]
                | _ -> [ op ])

        // 2. adjacency peephole — one pass commits each non-collapsing head before recursing, so a
        //    collapse that newly adjoins two collapsible ops (e.g. a cancelled insert/remove between
        //    two same-target moves) is not caught within the pass.
        let rec peephole xs =
            match xs with
            | [] -> []
            | InsertChild(_, _, node) :: RemoveNode target :: rest when eq (w.Id node) target -> peephole rest
            | MoveNode(t1, _, _) :: (MoveNode(t2, _, _) as second) :: rest when eq t1 t2 -> peephole (second :: rest)
            | ReorderChildren(p1, _) :: (ReorderChildren(p2, _) as second) :: rest when eq p1 p2 ->
                peephole (second :: rest)
            | x :: rest -> x :: peephole rest

        // …so iterate the pass to a fixpoint. A pass only ever drops ops, so the length is
        // monotonically non-increasing; an unchanged length means no collapse fired ⇒ done. This is
        // what makes `normalize` genuinely idempotent (no 'Node equality needed — length suffices).
        let rec toFixpoint xs =
            let xs' = peephole xs

            if List.length xs' = List.length xs then
                xs'
            else
                toFixpoint xs'

        toFixpoint stripped

    // ---- footprint + independence (Phase 78) ----
    // The multi-agent coordination invariant, computed structurally from the op-script (never
    // separately declared — the `paramsOf` precedent, Phase 77). `footprint` is a pure, total union-fold
    // over the skeleton five through the witnesses; `independent` is pairwise footprint disjointness.
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
    //       Sound for the skeleton five because every skeleton op is a structural write, so (1) already
    //       serialises a remove/move against any concurrent structural op; a domain that layers a *pure
    //       in-place* content op on top must fold the removed subtree in itself (it has the tree).

    let private emptyFootprint =
        { Reads = Set.empty
          StructureWrites = Set.empty
          ContentWrites = Set.empty
          UnknownParentWrites = Set.empty }

    let private unionFootprint (a: Footprint) (b: Footprint) : Footprint =
        { Reads = Set.union a.Reads b.Reads
          StructureWrites = Set.union a.StructureWrites b.StructureWrites
          ContentWrites = Set.union a.ContentWrites b.ContentWrites
          UnknownParentWrites = Set.union a.UnknownParentWrites b.UnknownParentWrites }

    /// The read/write footprint of an op-script (Phase 78) — a pure, total derivation over the skeleton
    /// five through the node/id witnesses (the `NodeWitness` reads the ids out of an inserted `'Node`
    /// subtree; the `IdWitness` keys every address by its string form). `Batch` folds its inner ops.
    /// Total: no tree, no failure case — it never throws (GP4) and mints no ids. Over-approximating by
    /// design — see `Footprint` and STABILITY.md for the pinned conservative cases.
    let footprint (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (ops: SkeletonOp<'Node, 'Id> list) : Footprint =
        let key (i: 'Id) = idw.ToString i

        let subtreeKeys (node: 'Node) =
            Tree.ids w node |> List.map key |> Set.ofList

        let rec ofOp (op: SkeletonOp<'Node, 'Id>) : Footprint =
            match op with
            | InsertChild(parent, _, node) ->
                let inserted = subtreeKeys node
                // parent existence + the inserted ids' dup-check are reads; the parent's child-list is a
                // (known) structure-write; the inserted subtree is authored into being — a content-write.
                { Reads = Set.add (key parent) inserted
                  StructureWrites = Set.singleton (key parent)
                  ContentWrites = inserted
                  UnknownParentWrites = Set.empty }
            | RemoveNode target ->
                // the node is destroyed (content-write on the target) and its unknown source parent's
                // child-list is rewritten (the pinned over-approximation).
                { emptyFootprint with
                    Reads = Set.singleton (key target)
                    ContentWrites = Set.singleton (key target)
                    UnknownParentWrites = Set.singleton (key target) }
            | MoveNode(target, newParent, _) ->
                // relocation: the destination child-list is a known structure-write; the target is
                // content-written (it moves); the source parent's child-list is the unknown over-approx.
                { Reads = Set.ofList [ key target; key newParent ]
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

        List.fold (fun acc op -> unionFootprint acc (ofOp op)) emptyFootprint ops

    /// Are two footprints **independent** (Phase 78) — do their scripts provably commute under `apply`?
    /// Pairwise disjointness across the write kinds, with the conservative rules pinned:
    ///   - no content write/write overlap, and no content-write vs read overlap either way (a node one
    ///     script authors/destroys must not be read or written by the other);
    ///   - no shared **named** structural parent — two positional inserts (or an insert + a reorder, …)
    ///     under one parent are NOT independent (they shift the same siblings — THE pinned same-parent
    ///     rule);
    ///   - an `UnknownParentWrites` op (a remove/move, whose source parent is a tree fact) conflicts with
    ///     *any* structural write — known or unknown — in the other script (the pinned unknown-parent
    ///     over-approximation): a remove/move is only independent of a structure-free script.
    /// `true` is a promise (they commute); `false` is always a safe answer. Total, no throws (GP4).
    let independent (a: Footprint) (b: Footprint) : bool =
        let disjoint x y = Set.isEmpty (Set.intersect x y)

        let hasStructural (f: Footprint) =
            not (Set.isEmpty f.StructureWrites && Set.isEmpty f.UnknownParentWrites)

        disjoint a.ContentWrites b.ContentWrites
        && disjoint a.ContentWrites b.Reads
        && disjoint b.ContentWrites a.Reads
        && disjoint a.StructureWrites b.StructureWrites
        && not (not (Set.isEmpty a.UnknownParentWrites) && hasStructural b)
        && not (not (Set.isEmpty b.UnknownParentWrites) && hasStructural a)

/// Structural tree-diff → op script (Phase 245): the inverse direction of `Ops.apply`.
/// Given two trees over a shared id space, derive a `SkeletonOp` list that transforms one
/// into the other — so a desired tree lowers to the apply/op-stream path every domain has.
module Diff =

    /// Why a diff could not be produced — a typed result, never an unapplyable script.
    type DiffError<'Id> =
        /// Skeleton ops cannot change the root, so two trees with different root ids are
        /// not a shared id space.
        | RootIdMismatch of before: 'Id * after: 'Id
        /// A well-formed tree carries each id at most once; this id appears twice.
        | DuplicateIdInTree of 'Id
        /// (Container-aware diff, Phase 09) the `after` tree places children under a node that
        /// `canHold` rejects — a container-legal script is impossible, so the diff is refused
        /// rather than emitting an `InsertChild`/`MoveNode` under a leaf (the F1 hazard).
        | TargetNotAContainer of parent: 'Id * kindTag: string

    /// Derive a script such that `Ops.applyAll (toOps w idw before after) before`
    /// reproduces `after` structurally. Relocated subtrees diff to `MoveNode` (never
    /// remove+insert), so an unchanged subtree is preserved, not destroyed and rebuilt.
    /// The emitted order is always applyable: added nodes go in as leaf shells (top-down),
    /// every survivor is then reattached/reordered to its `after` position, and removed
    /// regions are deleted **last** (so a surviving child is pulled out before its old
    /// container is removed). Structural only — per-kind property edits are out of scope
    /// (`Core.Ops`' remit); the two roots must share an id.
    let toOps
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =

        let key (i: 'Id) = idw.ToString i

        // First duplicated id in a tree (by key), if any.
        let dupId (root: 'Node) =
            Tree.preorder w root
            |> List.map w.Id
            |> List.groupBy key
            |> List.tryPick (fun (_, ids) -> if List.length ids > 1 then Some(List.head ids) else None)

        // key -> parent id, for every non-root node.
        let parentMap (nodes: 'Node list) =
            [ for p in nodes do
                  for c in w.Children p -> key (w.Id c), w.Id p ]
            |> Map.ofList

        let childKeysOf (n: 'Node) =
            w.Children n |> List.map (fun c -> key (w.Id c))

        if key (w.Id before) <> key (w.Id after) then
            Error(RootIdMismatch(w.Id before, w.Id after))
        else
            match dupId before with
            | Some d -> Error(DuplicateIdInTree d)
            | None ->
                match dupId after with
                | Some d -> Error(DuplicateIdInTree d)
                | None ->
                    let beforeNodes = Tree.preorder w before
                    let afterNodes = Tree.preorder w after
                    let bIds = beforeNodes |> List.map (fun n -> key (w.Id n)) |> Set.ofList
                    let aIds = afterNodes |> List.map (fun n -> key (w.Id n)) |> Set.ofList
                    let aParent = parentMap afterNodes
                    let bParent = parentMap beforeNodes

                    let bChildKeys =
                        beforeNodes |> List.map (fun n -> key (w.Id n), childKeysOf n) |> Map.ofList

                    let ops = ResizeArray<SkeletonOp<'Node, 'Id>>()

                    // 1. Added nodes → leaf shells under their after-parent (top-down via
                    //    preorder, so an added parent exists before an added child). Order
                    //    is fixed in step 2; index 0 is always valid.
                    for n in afterNodes do
                        let k = key (w.Id n)

                        if not (bIds.Contains k) then
                            match Map.tryFind k aParent with
                            | Some pid -> ops.Add(InsertChild(pid, 0, w.ReplaceChildren n []))
                            | None -> () // an added root is impossible (roots match)

                    // 2. Reattach + reorder every survivor to its after-position. A parent
                    //    whose child-id list is unchanged is already correct (its children
                    //    are never disturbed), so skip it; otherwise place each after-child
                    //    at its index in order — the front prefix builds correctly even with
                    //    stale (not-yet-claimed) children trailing.
                    for p in afterNodes do
                        let pk = key (w.Id p)
                        let aKidKeys = childKeysOf p

                        let unchanged =
                            bIds.Contains pk
                            && (match Map.tryFind pk bChildKeys with
                                | Some bk -> bk = aKidKeys
                                | None -> false)

                        if not unchanged then
                            w.Children p |> List.iteri (fun k c -> ops.Add(MoveNode(w.Id c, w.Id p, k)))

                    // 3. Removals last — a removed region's surviving descendants have been
                    //    moved out in step 2, so removing the region's top node (the removed
                    //    node whose before-parent survives) drops only removed nodes.
                    for n in beforeNodes do
                        let k = key (w.Id n)

                        if not (aIds.Contains k) then
                            match Map.tryFind k bParent with
                            | Some pid when aIds.Contains(key pid) -> ops.Add(RemoveNode(w.Id n))
                            | _ -> () // before-parent also removed → covered by removing it

                    Ok(List.ofSeq ops)

    /// Container-aware diff (Phase 09) — the `canHold`-aware mirror of `toOps`, the diff-path
    /// analogue of `Ops.applyContained`. Every parent the emitted script addresses comes from
    /// the `after` tree, so an `after` that nests children under a node `canHold` rejects makes
    /// a container-legal script impossible: that is surfaced as a typed `TargetNotAContainer`
    /// rather than an `InsertChild`/`MoveNode` under a leaf. When every `after`-parent is a
    /// container the result is exactly `toOps` (so `toOps` is the `(fun _ -> true)` wrapper —
    /// the same relationship `apply`/`applyContained` have). Containment *legality* (which kind
    /// may parent which) stays domain-side; `canHold` answers only "can this node hold children
    /// at all".
    let toOpsContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        // Every (new) parent the script targets is an `after` node that has children. If any
        // such node cannot hold children, no container-legal script exists.
        match
            Tree.preorder w after
            |> List.tryFind (fun p -> not (List.isEmpty (w.Children p)) && not (canHold p))
        with
        | Some p -> Error(TargetNotAContainer(w.Id p, w.KindTag p))
        | None -> toOps w idw before after
