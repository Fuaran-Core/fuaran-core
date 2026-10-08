namespace Fuaran.Core

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
        ///
        /// `target` is the first such node of `after` in preorder, as `Ops.firstUncontained` — the
        /// single definition of the shape — finds it, and `kindTag` is that node's own. The payload
        /// is its apply-side sibling's, `Rejection.NotAContainer`: grafting the same subtree through
        /// `Ops.applyContained` refuses with `NotAContainer` naming the same `target` and `kindTag`,
        /// which `Conformance.diffContainedLaws` asserts. (Phase 228 renamed the field from `parent`,
        /// the operator's ruling (B) of 2026-09-20; positional construction and matching are
        /// unaffected.)
        | TargetNotAContainer of target: 'Id * kindTag: string
        /// (Grammar-aware diff, Phase 313) the `after` tree places `child` (of kind `childKind`)
        /// under `parent` (of kind `parentKind`) where the domain's containment grammar does not let
        /// that kind hold it, so no grammar-legal script reaches it. The pair is the first
        /// `Ops.illegalChildren` reports over `after`, and the payload is `Rejection.IllegalChild`'s,
        /// `legal` enumerating what the grammar lets `parentKind` hold. Declared last.
        | IllegalChildInTree of child: 'Id * childKind: string * parent: 'Id * parentKind: string * legal: string list

    /// How one id changed between `before` and `after` (Phase 314) — the per-id reading of the
    /// content-aware diff, which `Diff.changes` derives from the two trees and `Conformance.changeLaws`
    /// holds to the script `toOpsWith` emits (DECISIONS.md D112: a classification is a PROJECTION of
    /// the diff, never a second diff). One id may carry more than one entry — a survivor that moved
    /// AND whose content changed carries `Moved` and `Changed` — because the two facts are
    /// independent and the script carries both ops; a reader that wants one kind per id takes the
    /// first in declaration order, which is what the consumers' single-kind classifiers reported.
    /// `RequireQualifiedAccess`: `ChangeKind.Added`, so the cases shadow nothing a consumer owns.
    [<RequireQualifiedAccess>]
    type ChangeKind<'Id> =
        /// The id is in `after` and not in `before` — the child an `InsertChild` of the script
        /// grafts.
        | Added
        /// The id is in `before` and not in `after` — a `RemoveNode` target, or a node below one
        /// (the script removes a region at its top; every id in it is `Removed` here).
        | Removed
        /// A survivor whose parent differs between the two trees — the script's `MoveNode`. A change
        /// of position under ONE parent is not a move of the child; it is a `Reordered` parent.
        | Moved of fromParent: 'Id * toParent: 'Id
        /// A survivor whose kind tag differs. Reported INSTEAD of `Changed`, which it subsumes: the
        /// script carries one `UpdateNode` for the node, whichever this reads as.
        | KindChanged of fromKind: string * toKind: string
        /// A survivor of unchanged kind whose own content differs under the caller's encoder over
        /// the two shells — exactly the test `toOpsWith` emits an `UpdateNode` on.
        | Changed
        /// A survivor whose KEPT children — the children both trees place under it — stand in a
        /// different relative order in `after`; a child arriving or leaving alone is not a reorder.
        /// Every `ReorderChildren` the script emits names a parent that is `Reordered`, or one that
        /// gained an `Added` or `Moved` child (the structural passes append, so a child placed before
        /// a kept one is restated by a reorder the trees do not otherwise show). Declared last.
        | Reordered

    /// One entry of `Diff.changes`: the id and how it changed.
    type Change<'Id> =
        {
            /// The id the entry is about.
            Id: 'Id
            /// How it changed.
            Kind: ChangeKind<'Id>
        }

    // ---- the readers every entry shares (Phase 388) ----
    // `emitWith`, `changes` and the grammar entries read two trees the same way; these are the one
    // body of each, where every entry used to declare its own.

    /// The tree's index, a repeated id refused as `DuplicateIdInTree` naming it (Phase 139) — Core's
    /// named structural predicate, read through `Tree.Index.tryBuild` since Phase 305 so the index the
    /// passes read is built in the same pass that refuses a malformed tree. This was a `groupBy` of its
    /// own until Phase 139, and the retirement is the point: a diff refusing a malformed tree and an
    /// insert refusing a malformed graft are the same notion of malformed.
    ///
    /// ONE OBSERVABLE CHANGE at Phase 139, and it is which id is NAMED, never whether the tree is
    /// refused. The `groupBy` form reported the first id whose GROUP had more than one member, in
    /// first-appearance order of the keys; `Tree.wellFormed` reports the first id at its SECOND
    /// occurrence in preorder. For `[a; b; b; a]` the old form said `a` and the new says `b`. The new
    /// answer is the one `Rejection.DuplicateId` already gave on the accept path, so the two paths name
    /// the same offender for the same tree. Recorded in STABILITY.md.
    let private indexOf (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (root: 'Node) =
        match Tree.Index.tryBuild w idw root with
        | Ok ix -> Ok ix
        | Error(Tree.RepeatedId d) -> Error(DuplicateIdInTree d)
        | Error Tree.Structural -> Ok(Tree.Index.build w idw root) // unreachable: `tryBuild` refuses only a repeat

    /// A node's children, as their id keys, in order.
    let private childKeysOf (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (n: 'Node) : string list =
        w.Children n |> List.map (fun c -> idw.ToString(w.Id c))

    /// A node's content as the encoder sees it: its SHELL, the node with its children emptied
    /// (Phase 305) — so a survivor whose children alone changed is not a content change.
    let private shellOf (encode: 'Node -> string) (w: NodeWitness<'Node, 'Id>) (n: 'Node) : string =
        encode (w.ReplaceChildren n [])

    /// The grammar entries' second refusal, after the script is found: the first parent/child pair
    /// of `after` the grammar forbids (`Ops.illegalChildren`) as `IllegalChildInTree`, else the script.
    let private grammarChecked
        (allowedChildren: string -> string list option)
        (w: NodeWitness<'Node, 'Id>)
        (after: 'Node)
        (script: Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>>)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        match script with
        | Error e -> Error e
        | Ok ops ->
            match Ops.illegalChildren allowedChildren w after with
            | (p, c) :: _ ->
                let pk = w.KindTag p

                Error(IllegalChildInTree(w.Id c, w.KindTag c, w.Id p, pk, allowedChildren pk |> Option.defaultValue []))
            | [] -> Ok ops

    // ---- the one emitter behind every entry (Phase 305) ----
    // The four structural passes are Phase 245's, and are what `proofs/TreeDiff.fst` models
    // clause for clause; Phase 305 changed one clause of step 4 (the settled-order drop, below).
    // Phase 305 also added the two CONTENT blocks around them — `UpdateNode`
    // for every survivor whose own content differs between the trees, which only a caller's
    // `encode` can see (the witness has no content accessor) — and moved the two maps and the
    // step-4 lookup onto `Tree.Index` (the `parentMap` here was `Tree.Index.build`'s `ParentOf`
    // written a second time, and step 4 walked `after` once per reordered parent).
    //
    // THE PLACEMENT RULE (DECISIONS D103). A content-changing survivor whose NEW node `canHold`
    // accepts is updated FIRST, before any insert or move; every other update goes LAST, after the
    // reorders. First, because a survivor that becomes a container is the parent of the inserts
    // and moves under it, and `validateInsert` / `validateMove` read the kind the tree holds at
    // that step — a leaf that is about to become a section refuses its own new children
    // (`NotAContainer(p, para)`, the shape `TreeDiff.fst` section 12 pins) unless the rewrite
    // lands before them. Last, because a survivor that becomes a LEAF may be rewritten only once
    // its children have left (`validateUpdate` refuses a childful leaf), and they leave in the
    // moves and the removals. An `UpdateNode` keeps the children the tree holds, so it is inert to
    // the four structural blocks wherever it sits; the two sites are where the containment check
    // is satisfied. Measured over 5,444 independent pairs with drawn kinds and a drawn `canHold`:
    // 0 refused under `applyAllWith`, 0 round-trip mismatches, where the structural script had 41%
    // refused and appending every update last still had 41% (`ProofOracleTests`, the content-aware
    // bridge).
    let private emitWith
        (changed: ('Node -> 'Node -> bool) option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =

        let key (i: 'Id) = idw.ToString i

        let index = indexOf w idw
        let childKeysOf = childKeysOf w idw

        if key (w.Id before) <> key (w.Id after) then
            Error(RootIdMismatch(w.Id before, w.Id after))
        else
            match index before with
            | Error e -> Error e
            | Ok bix ->
                match index after with
                | Error e -> Error e
                | Ok aix ->
                    let beforeNodes = Tree.preorder w before
                    let afterNodes = Tree.preorder w after
                    let bIds = bix.ById |> Map.keys |> Set.ofSeq
                    let aIds = aix.ById |> Map.keys |> Set.ofSeq
                    // key -> parent id, for every non-root node — the index's own map.
                    let aParent = aix.ParentOf
                    let bParent = bix.ParentOf

                    let bChildKeys =
                        beforeNodes |> List.map (fun n -> key (w.Id n), childKeysOf n) |> Map.ofList

                    let ops = ResizeArray<SkeletonOp<'Node, 'Id>>()
                    // Parents whose order must be restated once membership is final (step 4).
                    let reorderParents = ResizeArray<'Id>()
                    // Content-changing survivors whose new node cannot hold children (step 5).
                    let trailingUpdates = ResizeArray<SkeletonOp<'Node, 'Id>>()

                    // 0. Content, first and last (Phase 305). A survivor is a node both trees
                    //    carry; `changed` is the caller's encoder over the two SHELLS, so a
                    //    difference in the children alone is never an update. The payload is the
                    //    `after` node (its children are not read by `UpdateNode`, so carrying them
                    //    costs nothing and keeps the op a faithful statement of `after`).
                    match changed with
                    | None -> ()
                    | Some differs ->
                        for n in afterNodes do
                            match Map.tryFind (key (w.Id n)) bix.ById with
                            | Some b when differs b n ->
                                if canHold n then
                                    ops.Add(UpdateNode n)
                                else
                                    trailingUpdates.Add(UpdateNode n)
                            | _ -> ()

                    // 1. Added nodes → leaf shells under their after-parent (top-down via
                    //    preorder, so an added parent exists before an added child). They
                    //    append; step 2 states the order.
                    for n in afterNodes do
                        let k = key (w.Id n)

                        if not (bIds.Contains k) then
                            match Map.tryFind k aParent with
                            | Some pid -> ops.Add(InsertChild(pid, w.ReplaceChildren n []))
                            | None -> () // an added root is impossible (roots match)

                    // 2. Reattach + reorder every survivor to its after-position. A parent
                    //    whose child-id list is unchanged is already correct (its children
                    //    are never disturbed), so skip it.
                    //
                    //    This used to be a positional sweep — one `MoveNode` per child at
                    //    its index, relying on the front prefix building correctly whilst
                    //    stale children trailed. With membership and order separated it is
                    //    a `MoveNode` only for children that actually CHANGED parent, plus
                    //    one `ReorderChildren` naming the after-order. That is strictly
                    //    fewer ops (a pure reorder of n children is now 1 op, not n) and
                    //    carries no ordinals.
                    for p in afterNodes do
                        let pk = key (w.Id p)
                        let aKidKeys = childKeysOf p

                        let unchanged =
                            bIds.Contains pk
                            && (match Map.tryFind pk bChildKeys with
                                | Some bk -> bk = aKidKeys
                                | None -> false)

                        if not unchanged then
                            // Membership: a survivor whose before-parent differs must move.
                            // A newly-added node was already appended here in step 1.
                            for c in w.Children p do
                                let ck = key (w.Id c)

                                let movedParent =
                                    bIds.Contains ck
                                    && (match Map.tryFind ck bParent with
                                        | Some bp -> key bp <> pk
                                        | None -> true) // no before-parent → it was the root's child set

                                if movedParent then
                                    ops.Add(MoveNode(w.Id c, w.Id p))

                            // Order is stated LAST, in step 4 — not here. `ReorderChildren`
                            // demands an exact permutation of the parent's children AT APPLY
                            // TIME, and at this point the parent may still hold children that
                            // are about to leave: a node moving to a parent processed later in
                            // this loop, or a node removed in step 3. The old positional sweep
                            // tolerated that ("the front prefix builds correctly even with
                            // stale children trailing"); naming an order does not, so every
                            // reorder waits until membership is final.
                            reorderParents.Add(w.Id p)

                    // 3. Removals last — a removed region's surviving descendants have been
                    //    moved out in step 2, so removing the region's top node (the removed
                    //    node whose before-parent survives) drops only removed nodes.
                    for n in beforeNodes do
                        let k = key (w.Id n)

                        if not (aIds.Contains k) then
                            match Map.tryFind k bParent with
                            | Some pid when aIds.Contains(key pid) -> ops.Add(RemoveNode(w.Id n))
                            | _ -> () // before-parent also removed → covered by removing it

                    // 4. Order, last of the structure — every parent now holds exactly its
                    //    after-children, so naming the after-order is a legal permutation. One
                    //    op per changed parent, where the old sweep emitted one MoveNode per
                    //    child. The parent is read off the index (Phase 305; a preorder walk of
                    //    `after` per parent before).
                    //
                    //    THE DROP (Phase 305, 305.t1). The order steps 1-3 LEAVE a parent in is
                    //    a function of the two trees alone: its kept survivors in before-order (a
                    //    before-child that is still its child), then the inserted shells, then
                    //    the moved-in survivors, the last two each in after-order — an insert and
                    //    a move both append, step 1 walks `after`'s preorder (a parent's new
                    //    children arrive in its child order), step 2 walks a parent's children in
                    //    order, and a move-out or a removal deletes in place. A parent whose
                    //    after-order IS that order needs no reorder, and the reorder that used to
                    //    trail every append restated an order the tree already held. `settled`
                    //    is `TreeDiff.fst`'s `settled_order` clause for clause; the order-
                    //    prediction lemma behind the drop is section 10's `ord1`/`ord2`/`ord3`,
                    //    and `reorder_settled` is the step that skips. "Moved in" is tested
                    //    against the parent's before-children rather than step 2's `bParent`:
                    //    the two agree on a well-formed `before` (a before-child of `p` has
                    //    before-parent `p`), and this form reads the map step 2 already built.
                    for pid in reorderParents do
                        match Tree.Index.tryFind idw pid aix with
                        | Some p when List.length (w.Children p) > 1 ->
                            let aKidKeys = childKeysOf p
                            let bKidKeys = defaultArg (Map.tryFind (key pid) bChildKeys) []
                            let aKidSet = Set.ofList aKidKeys
                            let bKidSet = Set.ofList bKidKeys

                            let settled =
                                (bKidKeys |> List.filter (fun c -> Set.contains c aKidSet))
                                @ (aKidKeys |> List.filter (fun c -> not (bIds.Contains c)))
                                @ (aKidKeys
                                   |> List.filter (fun c -> bIds.Contains c && not (Set.contains c bKidSet)))

                            if settled <> aKidKeys then
                                ops.Add(ReorderChildren(pid, w.Children p |> List.map w.Id))
                        | _ -> ()

                    // 5. The updates `canHold` refuses, last of all: by now each such node holds
                    //    exactly its `after` children, which `firstUncontained` has already shown
                    //    to be none (a childful node the predicate refuses is refused up front by
                    //    `toOpsContained`, and the plain forms pass a predicate that refuses
                    //    nothing, so this block is empty there).
                    ops.AddRange trailingUpdates

                    Ok(List.ofSeq ops)

    /// Derive a script such that `Ops.applyAll (toOps w idw before after) before`
    /// reproduces `after` structurally. Relocated subtrees diff to `MoveNode` (never
    /// remove+insert), so an unchanged subtree is preserved, not destroyed and rebuilt.
    /// The emitted order is always applyable: added nodes go in as leaf shells (top-down),
    /// every survivor is then reattached/reordered to its `after` position, and removed
    /// regions are deleted **last** (so a surviving child is pulled out before its old
    /// container is removed). Structural only — a survivor keeps `before`'s content, kind
    /// included; `toOpsWith` is the form that diffs content too. The two roots must share an id.
    let toOps
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        emitWith None (fun _ -> true) w idw before after

    /// Container-aware diff (Phase 09) — the `canHold`-aware mirror of `toOps`, the diff-path
    /// analogue of `Ops.applyContained`. Every parent the emitted script addresses comes from
    /// the `after` tree, so an `after` that nests children under a node `canHold` rejects makes
    /// a container-legal script impossible: that is surfaced as a typed `TargetNotAContainer`
    /// rather than an `InsertChild`/`MoveNode` under a leaf. When every `after`-parent is a
    /// container the result is exactly `toOps` (so `toOps` is the `(fun _ -> true)` wrapper —
    /// the same relationship `apply`/`applyContained` have). Containment *legality* (which kind
    /// may parent which) stays domain-side; `canHold` answers only "can this node hold children
    /// at all".
    ///
    /// **What the check buys, exactly (Phase 305).** The script's addresses resolve, in `after`,
    /// to nodes `canHold` accepts (`proofs/TreeDiff.fst`, `diff_applicable_contained`). That is NOT
    /// yet "`applyAllWith canHold` accepts every step": the script runs against `before`'s kinds,
    /// and a structural diff carries no content, so a survivor that is a leaf in `before` and a
    /// container in `after` refuses the inserts under it with `NotAContainer` — over independent
    /// pairs with kinds drawn freely, 41% of the scripts this form returns are refused. The
    /// content-aware `toOpsContainedWith` is the form that applies under the predicate it checked.
    let toOpsContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        // Every (new) parent the script targets is an `after` node that has children. If any
        // such node cannot hold children, no container-legal script exists.
        match Ops.firstUncontained canHold w after with
        | Some p -> Error(TargetNotAContainer(w.Id p, w.KindTag p))
        | None -> emitWith None canHold w idw before after

    /// Content-aware, container-aware diff (Phase 305) — `toOpsContained` that also emits an
    /// `UpdateNode` for every survivor whose own content differs, as the caller's `encode` sees it
    /// over the two nodes' SHELLS (`ReplaceChildren n []`, so a change in the children alone is
    /// never an update). The witness has no content accessor, so the encoder is the one way the
    /// diff can see content — the same per-call parameter `Tree.encodeHash` and `Tree.Index.buildWith`
    /// take, and like theirs it should be injective over a node's own content: a lossy encoder
    /// makes two different nodes read as unchanged and the script lands on a tree that is not
    /// `after`.
    ///
    /// **The guarantee this form adds.** For well-formed `before` and `after` and a child-blind
    /// `canHold`, a script it returns is accepted at every step by `Ops.applyAllWith canHold` and
    /// lands on `after`, content included — the placement rule in `emitWith`'s header (D103) is what
    /// makes it so, and `ProofOracleTests`' content-aware bridge measures it over drawn kinds and a
    /// drawn predicate. The refusals are `toOpsContained`'s, unchanged. With
    /// `canHold = fun _ -> true` it is exactly `toOpsWith`.
    let toOpsContainedWith
        (canHold: 'Node -> bool)
        (encode: 'Node -> string)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        let differs (b: 'Node) (a: 'Node) =
            shellOf encode w b <> shellOf encode w a

        match Ops.firstUncontained canHold w after with
        | Some p -> Error(TargetNotAContainer(w.Id p, w.KindTag p))
        | None -> emitWith (Some differs) canHold w idw before after

    /// Content-aware diff (Phase 305) — `toOps` that also emits an `UpdateNode` for every survivor
    /// whose own content differs under the caller's `encode`; `toOpsContainedWith (fun _ -> true)`,
    /// so every update sits before the structural blocks. `Ops.applyAll (toOpsWith encode w idw
    /// before after) before` reproduces `after` structurally AND in every node's content the
    /// encoder distinguishes.
    let toOpsWith
        (encode: 'Node -> string)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        toOpsContainedWith (fun _ -> true) encode w idw before after

    /// Grammar-aware diff (Phase 313) — `toOpsContained` with the domain's containment grammar
    /// beside `canHold`. Every refusal `toOpsContained` makes is made first and unchanged; then an
    /// `after` that holds a child its parent's kind may not hold is refused with
    /// `IllegalChildInTree`, naming the first such pair (`Ops.illegalChildren`). Otherwise the
    /// script is `toOpsContained`'s, and every parent→child pair it creates is one the final tree
    /// holds — a shell is inserted under its `after` parent and never moved, a survivor is moved
    /// once, to its `after` parent — so for a `before` that keeps the grammar, `Ops.applyAllGrammar`
    /// accepts the script wherever the tree it builds keeps it (`Conformance.containmentLaws`).
    /// Structural only, as `toOps` is: a survivor keeps `before`'s content, kind included;
    /// `toOpsGrammarWith` is the content-aware form.
    let toOpsGrammar
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        toOpsContained canHold w idw before after
        |> grammarChecked allowedChildren w after

    /// `toOpsGrammar` over the content-aware `toOpsContainedWith` (Phase 305): the same two
    /// refusals in the same order, and a script that carries the survivors' content changes.
    let toOpsGrammarWith
        (allowedChildren: string -> string list option)
        (canHold: 'Node -> bool)
        (encode: 'Node -> string)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, DiffError<'Id>> =
        toOpsContainedWith canHold encode w idw before after
        |> grammarChecked allowedChildren w after

    /// The rank of a change kind in the canonical order: declaration order, so an id's entries read
    /// added, removed, moved, kind, content, reordered.
    let private rankOf (k: ChangeKind<'Id>) : int =
        match k with
        | ChangeKind.Added -> 0
        | ChangeKind.Removed -> 1
        | ChangeKind.Moved _ -> 2
        | ChangeKind.KindChanged _ -> 3
        | ChangeKind.Changed -> 4
        | ChangeKind.Reordered -> 5

    /// The per-id change classification between two trees (Phase 314): every id of either tree that
    /// changed, in canonical order — ascending id key, then `ChangeKind` declaration order — each
    /// entry one of `Added | Removed | Moved | KindChanged | Changed | Reordered` as the cases
    /// document. Read off the two trees' indexes directly, not off a script, and held to the script
    /// by `Conformance.changeLaws`: over `Ok ops = toOpsWith encode w idw before after`, the `Added`
    /// ids are exactly the `InsertChild` grafts' ids, the `Moved` ids exactly the `MoveNode` targets,
    /// the `KindChanged` and `Changed` ids together exactly the `UpdateNode` targets (when `encode`
    /// sees the kind, as an injective encoder does), the `RemoveNode` targets are `Removed` and every
    /// other `Removed` id sits below one in `before`, and every `ReorderChildren` parent is
    /// `Reordered` or holds an `Added` or `Moved` child. The encoder is read over each survivor's
    /// SHELL, exactly as `toOpsWith` reads it. The refusals are `toOps`'s and in its order: a root id
    /// mismatch, then a repeated id in `before`, then one in `after`. An identity pair classifies to
    /// `[]`.
    let changes
        (encode: 'Node -> string)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<Change<'Id> list, DiffError<'Id>> =
        let key (i: 'Id) = idw.ToString i
        let shell = shellOf encode w
        let index = indexOf w idw
        let childKeysOf = childKeysOf w idw

        if key (w.Id before) <> key (w.Id after) then
            Error(RootIdMismatch(w.Id before, w.Id after))
        else
            match index before with
            | Error e -> Error e
            | Ok bix ->
                match index after with
                | Error e -> Error e
                | Ok aix ->
                    let found = ResizeArray<Change<'Id>>()

                    for n in Tree.preorder w after do
                        let k = key (w.Id n)

                        match Map.tryFind k bix.ById with
                        | None -> found.Add { Id = w.Id n; Kind = ChangeKind.Added }
                        | Some b ->
                            match Map.tryFind k aix.ParentOf, Map.tryFind k bix.ParentOf with
                            | Some ap, Some bp when key ap <> key bp ->
                                found.Add
                                    { Id = w.Id n
                                      Kind = ChangeKind.Moved(bp, ap) }
                            | _ -> ()

                            let bKind = w.KindTag b
                            let aKind = w.KindTag n

                            if bKind <> aKind then
                                found.Add
                                    { Id = w.Id n
                                      Kind = ChangeKind.KindChanged(bKind, aKind) }
                            elif shell b <> shell n then
                                found.Add
                                    { Id = w.Id n
                                      Kind = ChangeKind.Changed }

                            let bKids = childKeysOf b
                            let aKids = childKeysOf n
                            let aSet = Set.ofList aKids
                            let bSet = Set.ofList bKids

                            if
                                (bKids |> List.filter (fun c -> Set.contains c aSet))
                                <> (aKids |> List.filter (fun c -> Set.contains c bSet))
                            then
                                found.Add
                                    { Id = w.Id n
                                      Kind = ChangeKind.Reordered }

                    for n in Tree.preorder w before do
                        if not (Map.containsKey (key (w.Id n)) aix.ById) then
                            found.Add
                                { Id = w.Id n
                                  Kind = ChangeKind.Removed }

                    found |> List.ofSeq |> List.sortBy (fun c -> key c.Id, rankOf c.Kind) |> Ok
