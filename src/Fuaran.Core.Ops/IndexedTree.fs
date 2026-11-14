namespace Fuaran.Core

// Phase 659 — an op applied to a tree its caller holds an index of costs the op (DECISIONS.md D257).
// A caller that applies a script one op at a time (a replay, a build frame by frame) paid the whole
// tree on every op: `Ops.apply`'s base check walks it, an insert's duplicate check walks it, and
// every lookup walks to its target. The entries here read an index instead and carry it through the
// op (`Ops.Index.afterOp`), so each op costs the op. They run `Ops`' own engine, located through the
// index (`Lookups`), and answer exactly what `Ops.apply`, `Ops.canApply` and `Ops.applyAll` answer
// under the same policy and scope; `IndexedApplyTests` holds the law over the algebra's op sample.

/// A tree, the scope its ops apply under, and the index that describes it (Phase 659): the value
/// an indexed script threads, so that each op costs the op rather than the tree.
///
/// It is FRESH BY CONSTRUCTION. Only `IndexedTree.ofTree` (which builds the index),
/// `IndexedTree.adopt` (which accepts a caller's index only when `Tree.Index.isFreshFor` says it
/// describes the tree) and the entries `IndexedTree.apply` / `applyAll` (which carry the index
/// through `Ops.Index.afterOp`, whose law is that it equals a fresh build) make one, and the three
/// fields never change. A caller cannot pair a tree with an index that does not describe it, so the
/// entries need no freshness walk of their own: the walk `Ops.apply` makes on every call
/// (`Tree.wellFormed`, the base's uniqueness) is the index's own invariant, established once, when
/// the value was made.
[<Sealed>]
type IndexedTree<'Node, 'Id> internal (scope: OpScope<'Node, 'Id>, tree: 'Node, index: Tree.NodeIndex<'Node, 'Id>) =
    /// The scope every entry applies under, and whose locate walk the index was built over.
    member _.Scope = scope
    /// The tree.
    member _.Tree = tree
    /// The index of `Tree` over the scope's locate walk (`Tree.traversal` under a keyed scope).
    member _.Index = index

/// Why `IndexedTree.ofTree` or `IndexedTree.adopt` made no `IndexedTree` (Phase 659).
[<RequireQualifiedAccess>]
type IndexRefusal<'Id> =
    /// The tree carries this id twice over the scope's locate walk — the first in preorder, the id
    /// `Ops.apply` refuses the same base with (`DuplicateId`). No index describes such a tree.
    | RepeatedId of 'Id
    /// The index handed to `IndexedTree.adopt` does not describe the tree (`Tree.Index.isFreshFor`
    /// is false). The entries would answer from it what the tree does not hold, so the index is
    /// refused rather than read, and nothing falls back to a walk the caller did not ask for.
    | Stale

/// Making an `IndexedTree`, and applying ops to it (Phase 659).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module IndexedTree =

    /// Rewrite the node at the end of `path` (root first, the target last) in `root` through `f`,
    /// descending by id and rebuilding only that path — `Tree.updateNode`'s rebuild, with the search
    /// replaced by the path an index holds. `None` when `root` does not hold the path. Iterative over
    /// the depth (the Phase-19 posture).
    let private atPath
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (path: 'Id list)
        (f: 'Node -> 'Node)
        (root: 'Node)
        : 'Node option =
        let rec down (frames: ('Node * 'Node list * int) list) (node: 'Node) (rest: 'Id list) =
            match rest with
            | [] -> Some(frames, node)
            | next :: more ->
                let kids = w.Children node

                match kids |> List.tryFindIndex (fun c -> idw.Equals (w.Id c) next) with
                | None -> None
                | Some i -> down ((node, kids, i) :: frames) (List.item i kids) more

        match path with
        | top :: below when idw.Equals (w.Id root) top ->
            down [] root below
            |> Option.map (fun (frames, target) ->
                (f target, frames)
                ||> List.fold (fun child (parent, kids, i) ->
                    w.ReplaceChildren parent (kids |> List.mapi (fun j c -> if j = i then child else c))))
        | _ -> None

    /// The lookups that READ `ix`, an index of the tree over `s.Locate`. Each answers what
    /// `Ops.walked` answers over that tree, at the cost of the op: a map read, a path of parent
    /// links, or the graft. `At` follows the path `ix` holds, which every edit of one step leaves in
    /// place (a move's detach removes the moved subtree, and the destination is not below it).
    let private lookups (s: Walks<'Node, 'Id>) (ix: Tree.NodeIndex<'Node, 'Id>) : Lookups<'Node, 'Id> =
        let idw = s.Ids
        let key (i: 'Id) = idw.ToString i

        let holds (p: 'Node) (i: 'Id) =
            s.Edit.Children p |> List.exists (fun c -> idw.Equals (s.Edit.Id c) i)

        { Find = fun i -> Tree.Index.tryFind idw i ix
          StructuralParent = fun i -> Tree.Index.parentOf idw i ix |> Option.filter (fun p -> holds p i)
          KeyedHolder =
            fun target ->
                match s.Keyed with
                | Some keyedOf ->
                    Tree.Index.parentOf idw target ix
                    |> Option.bind (fun p ->
                        keyedOf p
                        |> List.tryFindIndex (fun c -> idw.Equals (s.Edit.Id c) target)
                        |> Option.map (fun slot -> p, slot))
                | None -> None
          GraftRepeat =
            fun node ->
                let incoming = Tree.ids s.Locate node
                Tree.firstRepeatedId idw (incoming |> List.filter (fun i -> ix.ById.ContainsKey(key i))) incoming
          KeptRepeat =
            fun target incoming ->
                // an id the tree holds is kept unless its path runs through one of the target's
                // outgoing keyed positions
                let outgoing =
                    match s.Keyed, Tree.Index.tryFind idw target ix with
                    | Some keyedOf, Some existing -> keyedOf existing |> List.map (s.Edit.Id >> key) |> Set.ofList
                    | _ -> Set.empty

                let replaced (i: 'Id) =
                    match Tree.Index.path idw i ix with
                    | Some p ->
                        p
                        |> List.pairwise
                        |> List.exists (fun (a, b) -> idw.Equals a target && outgoing.Contains(key b))
                    | None -> false

                let held =
                    incoming
                    |> List.filter (fun i -> ix.ById.ContainsKey(key i) && not (replaced i))

                Tree.firstRepeatedId idw held incoming
          Encloses =
            fun sub i ->
                match Tree.Index.path idw i ix with
                | Some p -> p |> List.exists (fun a -> key a = key (s.Edit.Id sub))
                | None -> false
          At = fun i f tree -> Tree.Index.path idw i ix |> Option.bind (fun p -> atPath s.Locate idw p f tree) }

    /// One step: `Ops`' step through the index's lookups, then the index carried through it. The
    /// `Error` arm of `afterOp` is the repeated id it would refuse a rebuild over, which an accepted
    /// step of an id-unique tree cannot leave (`apply_preserves_wf`). A `Batch` is the runner's
    /// all-or-nothing form, each step reading the index the step before it carried.
    let rec private step
        (policy: Policy<'Node, 'Id>)
        (canHold: 'Node -> bool)
        (s: Walks<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (tree: 'Node, ix: Tree.NodeIndex<'Node, 'Id>)
        : Result<'Node * Tree.NodeIndex<'Node, 'Id>, Rejection<'Id>> =
        match op with
        | Batch ops -> Ops.batch (fun st o -> step policy canHold s o st) ops (tree, ix)
        | _ ->
            Ops.stepOnce policy canHold s (lookups s ix) op tree
            |> Result.bind (fun after ->
                match Ops.Index.afterOp s.Locate s.Ids op after ix with
                | Ok carried -> Ok(after, carried)
                | Error d -> Error(DuplicateId d))

    /// Index `root` under `scope`: `Tree.Index.build` over the scope's locate walk. A root carrying
    /// an id twice is refused with that id (`IndexRefusal.RepeatedId d`, the `d` `Ops.apply` refuses
    /// the same base with as `DuplicateId d`), so the uniqueness every entry assumes is established
    /// here, once, by the one walk this value ever costs.
    let ofTree (scope: OpScope<'Node, 'Id>) (root: 'Node) : Result<IndexedTree<'Node, 'Id>, IndexRefusal<'Id>> =
        let s = Ops.walksOf scope

        match Tree.Index.build s.Locate s.Ids root with
        | Ok ix -> Ok(IndexedTree(scope, root, ix))
        | Error d -> Error(IndexRefusal.RepeatedId d)

    /// Adopt an index the caller already holds: `ix` must describe `root` over the scope's locate
    /// walk, and `Tree.Index.isFreshFor` decides. An index that does not is refused as
    /// `IndexRefusal.Stale` — never read, and never silently replaced by a walk — and so is any
    /// index of a root carrying an id twice. `encode` is `isFreshFor`'s content half, under its
    /// contract: the adoption is exact over content when `encode` is injective over a node's own
    /// content (`Conformance.encoderInjectivityLaws`), and the entries read the nodes the index
    /// holds, so a weaker `encode` admits an index whose nodes the tree has since rewritten. One
    /// walk, O(n log n), at adoption; none after it.
    let adopt
        (scope: OpScope<'Node, 'Id>)
        (encode: 'Node -> string)
        (root: 'Node)
        (ix: Tree.NodeIndex<'Node, 'Id>)
        : Result<IndexedTree<'Node, 'Id>, IndexRefusal<'Id>> =
        let s = Ops.walksOf scope

        if Tree.Index.isFreshFor s.Locate s.Ids encode root ix then
            Ok(IndexedTree(scope, root, ix))
        else
            Error IndexRefusal.Stale

    /// `Ops.apply` over an indexed tree: the same verdict `Ops.apply policy t.Scope op t.Tree`
    /// returns — the same tree, or the same `Rejection` — with the index carried to the tree it
    /// returns. No base walk runs (the value's index is its uniqueness); the duplicate check reads
    /// the graft against the index; every target, parent and keyed holder is a map read; and the
    /// edit rebuilds the path the index holds. What remains proportional to the tree is what an
    /// answer itself carries: an `UnknownNode` refusal enumerates the tree's ids, and a policy's
    /// `Refs` clause compares the declarations of the whole tree before and after a remove or an
    /// update.
    let apply
        (policy: Policy<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (t: IndexedTree<'Node, 'Id>)
        : Result<IndexedTree<'Node, 'Id>, Rejection<'Id>> =
        step policy (Ops.canHoldOf t.Scope) (Ops.walksOf t.Scope) op (t.Tree, t.Index)
        |> Result.map (fun (tree, ix) -> IndexedTree(t.Scope, tree, ix))

    /// `Ops.canApply` over an indexed tree: the exact `Rejection` `apply` would return, and no tree
    /// built for a non-batch op unless the policy carries references.
    let canApply
        (policy: Policy<'Node, 'Id>)
        (op: SkeletonOp<'Node, 'Id>)
        (t: IndexedTree<'Node, 'Id>)
        : Result<unit, Rejection<'Id>> =
        let s = Ops.walksOf t.Scope
        let canHold = Ops.canHoldOf t.Scope

        match op, policy.Refs with
        | Batch _, _
        | _, Some _ -> step policy canHold s op (t.Tree, t.Index) |> Result.map ignore
        | _, None -> Ops.checkOnce policy canHold s (lookups s t.Index) op t.Tree

    /// `Ops.applyAll` over an indexed tree: first refusal wins and the prefix is kept, as `applyAll`'s
    /// contract states, with the index carried through every accepted op. The `ScriptRejection`'s
    /// `Tree` is the INDEXED tree the accepted prefix reached, so a caller that resumes after a
    /// refusal keeps paying the op.
    let applyAll
        (policy: Policy<'Node, 'Id>)
        (ops: SkeletonOp<'Node, 'Id> list)
        (t: IndexedTree<'Node, 'Id>)
        : Result<IndexedTree<'Node, 'Id>, ScriptRejection<IndexedTree<'Node, 'Id>, 'Id>> =
        let s = Ops.walksOf t.Scope
        let canHold = Ops.canHoldOf t.Scope

        let r =
            Ops.run (fun st o -> step policy canHold s o st |> Result.map (fun next -> next, ())) ops (t.Tree, t.Index)

        let reached = IndexedTree(t.Scope, fst r.Tree, snd r.Tree)

        match r.Refused with
        | None -> Ok reached
        | Some e ->
            Error
                { Applied = r.Applied
                  Rejection = e
                  Tree = reached }
