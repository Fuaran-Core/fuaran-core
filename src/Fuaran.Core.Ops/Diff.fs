namespace Fuaran.Core

/// Structural tree-diff → op script (Phase 245): the inverse direction of `Ops.apply`.
/// Given two trees over a shared id space, derive a `SkeletonOp` list that transforms one
/// into the other — so a desired tree lowers to the apply/op-stream path every domain has.
///
/// **One emitter, one answer (Phase 518).** The script (`toOps`, `toOpsWith`) and the per-id
/// classification (`changes`) are two readings of ONE run of the emitter's passes, and every refusal
/// is a `Rejection` — the envelope the apply path refuses through — where the diff had a
/// `DiffError` of its own whose cases mirrored three of it.
///
/// **The keyed diff (Phase 518).** Under a keyed scope (`OpScope.keyed`) the diff LOCATES through
/// `Tree.traversal` — its duplicate check and its containment check see every node a keyed position
/// holds — and EDITS through the structural witness alone, as the engine does, so no shell it builds
/// is shorter than the keyed arity `KeyedWitness.ReplaceKeyedChildren` preserves. A holder's keyed
/// subtrees are its CONTENT (an `UpdateNode` of the holder carries them, and the keyed engine
/// replaces them with the payload's): a change anywhere below a keyed position is seen, whatever the
/// caller's encoder reads, as an `UpdateNode` of the holder on the structural surface, and a node the
/// two trees carry on different sides of a keyed boundary is refused as `KeyedPosition`.
module Diff =

    /// How one id changed between `before` and `after` (Phase 314) — the per-id reading of the
    /// content-aware diff, which `Diff.changes` reads off the same run of the emitter's passes that
    /// `toOpsWith`'s script comes from (DECISIONS.md D112: a classification is a PROJECTION of
    /// the diff, never a second diff; Phase 518 made it literally so). One id may carry more than one
    /// entry — a survivor that moved AND whose content changed carries `Moved` and `Changed` —
    /// because the two facts are independent and the script carries both ops; a reader that wants one
    /// kind per id takes the first in declaration order, which is what the consumers' single-kind
    /// classifiers reported. `RequireQualifiedAccess`: `ChangeKind.Added`, so the cases shadow
    /// nothing a consumer owns.
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

    /// One run of the emitter's passes: the script, and the classification read off the same passes.
    type private Passes<'Node, 'Id> =
        { Script: SkeletonOp<'Node, 'Id> list
          Changes: Change<'Id> list }

    // ---- the readers every pass shares (Phase 388) ----

    /// The structural index of a tree whose ids are unique over the walk the scope LOCATES through
    /// (`Tree.traversal` under a keyed scope), a repeated id refused as `DuplicateId` naming it — the
    /// first id `Tree.wellFormed` finds at its second preorder occurrence (Phase 139), which is the
    /// offender `Rejection.DuplicateId` names on the accept path too. A refusing `Tree.Index.build`
    /// (Phase 518) is the whole check for a scope with no keyed position.
    let private indexOf (scope: OpScope<'Node, 'Id>) (root: 'Node) =
        let byStructure () =
            Tree.Index.build scope.Nodes scope.Ids root |> Result.mapError DuplicateId

        match scope.Keyed with
        | None -> byStructure ()
        | Some _ ->
            match Tree.wellFormed (OpScope.locate scope) scope.Ids root with
            | Tree.RepeatedId d -> Error(DuplicateId d)
            | Tree.Structural -> byStructure ()

    /// A node's children, as their id keys, in order.
    let private childKeysOf (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (n: 'Node) : string list =
        w.Children n |> List.map (fun c -> idw.ToString(w.Id c))

    /// A node's content as the encoder sees it: its SHELL, the node with its STRUCTURAL children
    /// emptied (Phase 305) — so a survivor whose children alone changed is not a content change. Built
    /// through the structural witness, so a keyed position is never vacated (Phase 518).
    let private shellOf (encode: 'Node -> string) (w: NodeWitness<'Node, 'Id>) (n: 'Node) : string =
        encode (w.ReplaceChildren n [])

    /// The keyed subtrees of a holder as content (Phase 518): every node a keyed position holds,
    /// over the keyed walk, by its id, kind, encoded shell and both child lists — so a change
    /// anywhere below a keyed position reads as a change of the holder, whatever `encode` reads.
    /// `[]` under a scope with no keyed position.
    let private keyedContentOf (scope: OpScope<'Node, 'Id>) (encode: 'Node -> string) (n: 'Node) : string list =
        match scope.Keyed with
        | None -> []
        | Some keyw ->
            let w = scope.Nodes
            let t = OpScope.locate scope

            let keys (ns: 'Node list) =
                ns |> List.map (fun c -> scope.Ids.ToString(w.Id c))

            keyw.KeyedChildren n
            |> List.collect (Tree.preorder t)
            |> List.collect (fun m ->
                [ scope.Ids.ToString(w.Id m)
                  w.KindTag m
                  shellOf encode w m
                  string (List.length (keyw.KeyedChildren m)) ]
                @ keys (keyw.KeyedChildren m)
                @ [ string (List.length (w.Children m)) ]
                @ keys (w.Children m))

    /// Which structural-surface holder carries each id a keyed position holds, with the slot whose
    /// subtree holds it — id key → (holder, slot). Ids on the structural surface have no entry.
    let private carriersOf (scope: OpScope<'Node, 'Id>) (root: 'Node) : Map<string, 'Id * int> =
        match scope.Keyed with
        | None -> Map.empty
        | Some keyw ->
            let t = OpScope.locate scope

            [ for s in Tree.preorder scope.Nodes root do
                  for slot, k in List.indexed (keyw.KeyedChildren s) do
                      for i in Tree.ids t k -> scope.Ids.ToString i, (scope.Nodes.Id s, slot) ]
            |> Map.ofList

    /// The keyed boundary (Phase 518): the first node of `before` (keyed preorder) that `after` also
    /// carries, on the other side of a keyed boundary — carried by another holder, or by none — as
    /// `KeyedPosition` naming it, its carrier and the slot. `None` under a scope with no keyed position.
    let private firstCrossing (scope: OpScope<'Node, 'Id>) (before: 'Node) (after: 'Node) : Rejection<'Id> option =
        match scope.Keyed with
        | None -> None
        | Some _ ->
            let key (i: 'Id) = scope.Ids.ToString i
            let bCarrier = carriersOf scope before
            let aCarrier = carriersOf scope after
            let aIds = Tree.ids (OpScope.locate scope) after |> List.map key |> Set.ofList
            let holderKey = Option.map (fst >> key)

            Tree.ids (OpScope.locate scope) before
            |> List.tryPick (fun i ->
                let k = key i
                let b = Map.tryFind k bCarrier
                let a = Map.tryFind k aCarrier

                if aIds.Contains k && holderKey b <> holderKey a then
                    match b, a with
                    | Some(holder, slot), _
                    | None, Some(holder, slot) -> Some(KeyedPosition(i, holder, slot))
                    | None, None -> None
                else
                    None)

    // ---- the one emitter behind every entry (Phase 305; Phase 518 reads `changes` off it) ----
    // The four structural passes are Phase 245's, and are what `proofs/TreeDiff.fst` models
    // clause for clause; Phase 305 changed one clause of step 4 (the settled-order drop, below).
    // Phase 305 also added the two CONTENT blocks around them — `UpdateNode`
    // for every survivor whose own content differs between the trees, which only a caller's
    // `encode` can see (the witness has no content accessor) — and moved the two maps and the
    // step-4 lookup onto `Tree.Index`.
    //
    // Phase 518 records, at the site each pass DECIDES a fact, the `Change` that fact is: an added
    // node where step 1 grafts it, a move where step 2 moves it, a removal where step 3 meets it,
    // a content or kind change where step 0 compares the shells, a reorder of kept children where
    // step 2 finds a parent's child list changed. `changes` is that list; it used to walk both trees
    // a second time and was held equal to the script by a conformance law.
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
    // 0 refused under `Ops.applyAll` with that `canHold` as the scope's, 0 round-trip mismatches,
    // where the structural script had 41%
    // refused and appending every update last still had 41% (`ProofOracleTests`, the content-aware
    // bridge).
    let private passes
        (encode: ('Node -> string) option)
        (scope: OpScope<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<Passes<'Node, 'Id>, Rejection<'Id>> =
        let w = scope.Nodes
        let idw = scope.Ids
        let canHold = Ops.canHoldOf scope
        let key (i: 'Id) = idw.ToString i
        let childKeysOf = childKeysOf w idw

        if key (w.Id before) <> key (w.Id after) then
            Error(RootIdMismatch(w.Id before, w.Id after))
        else
            match indexOf scope before with
            | Error e -> Error e
            | Ok bix ->
                match indexOf scope after with
                | Error e -> Error e
                | Ok aix ->
                    match firstCrossing scope before after with
                    | Some r -> Error r
                    | None ->
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
                        let found = ResizeArray<Change<'Id>>()
                        let note (i: 'Id) (k: ChangeKind<'Id>) = found.Add { Id = i; Kind = k }
                        // Parents whose order must be restated once membership is final (step 4).
                        let reorderParents = ResizeArray<'Id>()
                        // Content-changing survivors whose new node cannot hold children (step 5).
                        let trailingUpdates = ResizeArray<SkeletonOp<'Node, 'Id>>()

                        // 0. Content, first and last (Phase 305). A survivor is a node both trees
                        //    carry; the caller's encoder reads the two SHELLS, so a difference in the
                        //    structural children alone is never an update — and under a keyed scope
                        //    the holder's keyed subtrees are read too (Phase 518). The payload is the
                        //    `after` node (its structural children are not read by `UpdateNode`, so
                        //    carrying them costs nothing and keeps the op a faithful statement of
                        //    `after`; its keyed subtrees are the content the update lands).
                        match encode with
                        | None -> ()
                        | Some enc ->
                            for n in afterNodes do
                                match Map.tryFind (key (w.Id n)) bix.ById with
                                | Some b ->
                                    let differs =
                                        shellOf enc w b <> shellOf enc w n
                                        || keyedContentOf scope enc b <> keyedContentOf scope enc n

                                    let bKind = w.KindTag b
                                    let aKind = w.KindTag n

                                    if bKind <> aKind then
                                        note (w.Id n) (ChangeKind.KindChanged(bKind, aKind))
                                    elif differs then
                                        note (w.Id n) ChangeKind.Changed

                                    if differs then
                                        if canHold n then
                                            ops.Add(UpdateNode n)
                                        else
                                            trailingUpdates.Add(UpdateNode n)
                                | None -> ()

                        // 1. Added nodes → shells under their after-parent (top-down via preorder,
                        //    so an added parent exists before an added child). They append; step 4
                        //    states the order. A shell keeps its keyed positions (Phase 518).
                        for n in afterNodes do
                            let k = key (w.Id n)

                            if not (bIds.Contains k) then
                                note (w.Id n) ChangeKind.Added

                                match Map.tryFind k aParent with
                                | Some pid -> ops.Add(InsertChild(pid, w.ReplaceChildren n []))
                                | None -> () // an added root is impossible (roots match)

                        // 2. Reattach every survivor to its after-parent. A parent whose child-id
                        //    list is unchanged is already correct (its children are never
                        //    disturbed), so skip it. A `MoveNode` only for children that actually
                        //    CHANGED parent; the order is one `ReorderChildren`, named in step 4.
                        for p in afterNodes do
                            let pk = key (w.Id p)
                            let aKidKeys = childKeysOf p
                            let bKidKeys = Map.tryFind pk bChildKeys

                            let unchanged =
                                bIds.Contains pk
                                && (match bKidKeys with
                                    | Some bk -> bk = aKidKeys
                                    | None -> false)

                            if not unchanged then
                                // A survivor whose KEPT children stand in another relative order.
                                match bKidKeys with
                                | Some bk when bIds.Contains pk ->
                                    let aSet = Set.ofList aKidKeys
                                    let bSet = Set.ofList bk

                                    if
                                        (bk |> List.filter (fun c -> Set.contains c aSet))
                                        <> (aKidKeys |> List.filter (fun c -> Set.contains c bSet))
                                    then
                                        note (w.Id p) ChangeKind.Reordered
                                | _ -> ()

                                // Membership: a survivor whose before-parent differs must move.
                                // A newly-added node was already appended here in step 1.
                                for c in w.Children p do
                                    let ck = key (w.Id c)

                                    if bIds.Contains ck then
                                        match Map.tryFind ck bParent with
                                        | Some bp when key bp = pk -> ()
                                        | bp ->
                                            ops.Add(MoveNode(w.Id c, w.Id p))
                                            // no before-parent is the root, which never moves
                                            bp |> Option.iter (fun bp -> note (w.Id c) (ChangeKind.Moved(bp, w.Id p)))

                                // Order is stated LAST, in step 4 — not here. `ReorderChildren`
                                // demands an exact permutation of the parent's children AT APPLY
                                // TIME, and at this point the parent may still hold children that
                                // are about to leave: a node moving to a parent processed later in
                                // this loop, or a node removed in step 3.
                                reorderParents.Add(w.Id p)

                        // 3. Removals last — a removed region's surviving descendants have been
                        //    moved out in step 2, so removing the region's top node (the removed
                        //    node whose before-parent survives) drops only removed nodes. Every id
                        //    of the region is `Removed`.
                        for n in beforeNodes do
                            let k = key (w.Id n)

                            if not (aIds.Contains k) then
                                note (w.Id n) ChangeKind.Removed

                                match Map.tryFind k bParent with
                                | Some pid when aIds.Contains(key pid) -> ops.Add(RemoveNode(w.Id n))
                                | _ -> () // before-parent also removed → covered by removing it

                        // 4. Order, last of the structure — every parent now holds exactly its
                        //    after-children, so naming the after-order is a legal permutation.
                        //
                        //    THE DROP (Phase 305, 305.t1). The order steps 1-3 LEAVE a parent in is
                        //    a function of the two trees alone: its kept survivors in before-order (a
                        //    before-child that is still its child), then the inserted shells, then
                        //    the moved-in survivors, the last two each in after-order — an insert and
                        //    a move both append, step 1 walks `after`'s preorder (a parent's new
                        //    children arrive in its child order), step 2 walks a parent's children in
                        //    order, and a move-out or a removal deletes in place. A parent whose
                        //    after-order IS that order needs no reorder. `settled` is `TreeDiff.fst`'s
                        //    `settled_order` clause for clause; the order-prediction lemma behind the
                        //    drop is section 10's `ord1`/`ord2`/`ord3`, and `reorder_settled` is the
                        //    step that skips.
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
                        //    to be none.
                        ops.AddRange trailingUpdates

                        Ok
                            { Script = List.ofSeq ops
                              Changes = List.ofSeq found }

    /// The one entry behind `toOps`, `toOpsWith` and `changes` (Phase 517; `changes` since Phase
    /// 518): the scope's container refusal, the emitter, then the policy's grammar refusal, in that
    /// order.
    let private diffUnder
        (encode: ('Node -> string) option)
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<Passes<'Node, 'Id>, Rejection<'Id>> =
        let w = scope.Nodes

        // Every (new) parent the script targets is an `after` node that has children — and under a
        // keyed scope every interior node a holder's update lands (Phase 518). If any such node
        // cannot hold children, no container-legal script exists.
        let uncontained =
            scope.CanHold
            |> Option.bind (fun canHold -> Ops.firstUncontained (OpScope.locate scope) canHold w after)

        match uncontained with
        | Some p -> Error(NotAContainer(w.Id p, w.KindTag p))
        | None ->
            match passes encode scope before after, policy.Grammar with
            | Ok run, Some allowedChildren ->
                // the first parent/child pair of `after` the grammar forbids (`Ops.illegalChildren`)
                match Ops.illegalChildren allowedChildren w after with
                | (p, c) :: _ ->
                    let pk = w.KindTag p

                    Error(IllegalChild(w.Id c, w.KindTag c, w.Id p, pk, allowedChildren pk |> Option.defaultValue []))
                | [] -> Ok run
            | run, _ -> run

    /// Derive a script such that `Ops.applyAll policy scope (toOps policy scope before after) before`
    /// reproduces `after` structurally (Phase 245; the policy and scope, Phase 517). Relocated
    /// subtrees diff to `MoveNode` (never remove+insert), so an unchanged subtree is preserved, not
    /// destroyed and rebuilt. The emitted order is always applyable: added nodes go in as shells
    /// (top-down), every survivor is then reattached/reordered to its `after` position, and removed
    /// regions are deleted **last** (so a surviving child is pulled out before its old container is
    /// removed). Structural only — a survivor keeps `before`'s content, kind and keyed subtrees
    /// included; `toOpsWith` is the form that diffs content too. Refusals are `Rejection`s (Phase
    /// 518): `RootIdMismatch` when the roots carry different ids, `DuplicateId` naming a repeated id
    /// of `before`, then of `after`.
    ///
    /// **Under a scope with a `CanHold` (Phase 09).** Every parent the emitted script addresses comes
    /// from the `after` tree, so an `after` that nests children under a node `canHold` rejects makes
    /// a container-legal script impossible: that is `NotAContainer` naming the first such node, made
    /// before every other refusal, not an `InsertChild`/`MoveNode` under a leaf. What the check buys,
    /// exactly (Phase 305): the script's addresses resolve, in `after`, to nodes `canHold` accepts
    /// (`proofs/TreeDiff.fst`, `diff_applicable_contained`). That is NOT yet "`Ops.applyAll` under
    /// the scope accepts every step": the script runs against `before`'s kinds, and a structural diff
    /// carries no content, so a survivor that is a leaf in `before` and a container in `after` refuses
    /// the inserts under it — `toOpsWith` is the form that applies under the predicate it checked.
    ///
    /// **Under a keyed scope (Phase 518)** — see the module note: the repeated-id and containment
    /// checks read the keyed walk, the shells keep their keyed positions, and a node relocated across
    /// a keyed boundary is refused as `KeyedPosition`, after the repeated-id refusals.
    ///
    /// **Under a policy with a `Grammar` (Phase 313).** Every refusal above is made first and
    /// unchanged; then an `after` that holds a child its parent's kind may not hold is refused with
    /// `IllegalChild`, naming the first such pair (`Ops.illegalChildren`). Every parent→child
    /// pair the script creates is one the final tree holds, so for a `before` that keeps the grammar,
    /// `Ops.applyAll` under the same policy accepts the script wherever the tree it builds keeps it
    /// (`Conformance.containmentLaws`). A policy's `Refs` constrains no diff: the script lands on
    /// `after`, and whether `after` keeps its references is `Validator.referenceIntegrity`'s report
    /// and the engine's `StillReferenced` refusal when the script is applied under that policy.
    let toOps
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, Rejection<'Id>> =
        diffUnder None policy scope before after |> Result.map _.Script

    /// Content-aware diff (Phase 305) — `toOps` that also emits an `UpdateNode` for every survivor
    /// whose own content differs, as the caller's `encode` sees it over the two nodes' SHELLS
    /// (`ReplaceChildren n []`, so a change in the children alone is never an update) and, under a
    /// keyed scope, over its keyed subtrees (Phase 518). The witness has no content accessor, so the
    /// encoder is the one way the diff can see content — the same per-call parameter
    /// `Tree.encodeHash` and `Tree.Index.isFreshFor` take, and like theirs it should be injective
    /// over a node's own content: a lossy encoder makes two different nodes read as unchanged and
    /// the script lands on a tree that is not `after`.
    ///
    /// **The guarantee this form adds.** For well-formed `before` and `after` and a child-blind
    /// `canHold`, a script it returns is accepted at every step by `Ops.applyAll` under the same scope
    /// and lands on `after`, content included — the placement rule in `passes`' header (D103) is
    /// what makes it so, and `ProofOracleTests`' content-aware bridge measures it over drawn kinds and
    /// a drawn predicate; under a keyed scope `Conformance.keyedApplyLaws`' keyed diff law holds it
    /// over the keyed reference shapes. The refusals are `toOps`'s, unchanged, under the same policy
    /// and scope.
    let toOpsWith
        (encode: 'Node -> string)
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, Rejection<'Id>> =
        diffUnder (Some encode) policy scope before after |> Result.map _.Script

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

    /// The per-id change classification between two trees (Phase 314): every id that changed, in
    /// canonical order — ascending id key, then `ChangeKind` declaration order — each entry one of
    /// `Added | Removed | Moved | KindChanged | Changed | Reordered` as the cases document.
    ///
    /// **Read off the script's own passes (Phase 518).** `changes encode policy scope` is the run of
    /// the emitter `toOpsWith encode policy scope` takes, so it refuses exactly where that refuses,
    /// with the same `Rejection`, and over `Ok ops = toOpsWith encode policy scope before after`
    /// the `Added` ids are exactly the `InsertChild` grafts' ids, the `Moved` ids exactly the
    /// `MoveNode` targets, the `Changed` ids and the `KindChanged` ids whose shells differ together
    /// exactly the `UpdateNode` targets, the `RemoveNode` targets are `Removed` and every other
    /// `Removed` id sits below one in `before`, and every `ReorderChildren` parent is `Reordered` or
    /// holds an `Added` or `Moved` child — each fact recorded where the pass that emits its op
    /// decides it. `KindChanged` is reported whenever the kind tags differ, whether or not the
    /// encoder reads the kind. Under a keyed scope the ids are the structural surface's: a change
    /// below a keyed position is the holder's `Changed`. An identity pair classifies to `[]`.
    let changes
        (encode: 'Node -> string)
        (policy: Policy<'Node, 'Id>)
        (scope: OpScope<'Node, 'Id>)
        (before: 'Node)
        (after: 'Node)
        : Result<Change<'Id> list, Rejection<'Id>> =
        let key (i: 'Id) = scope.Ids.ToString i

        diffUnder (Some encode) policy scope before after
        |> Result.map (fun run -> run.Changes |> List.sortBy (fun c -> key c.Id, rankOf c.Kind))
