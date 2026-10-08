namespace Fuaran.Core

/// Where a node is to sit among its destination's children (Phase 312) — stated by naming a
/// sibling or an end, or by an index. Placement is OVER the existing ops: `InsertChild` and
/// `MoveNode` append and `ReorderChildren` states order by id (Phase 95), and `TreePlacement`
/// lowers an anchor to those ops. Positions count the destination's children OTHER than the node
/// being placed, so `Index 0` is first and `Index n` (with `n` such siblings) is last, for an insert
/// and a move alike — the index a document domain's index-bearing ops have always meant.
[<RequireQualifiedAccess>]
type Anchor<'Id> =
    /// Before every sibling.
    | First
    /// After every sibling — what `InsertChild` / `MoveNode` do on their own.
    | Last
    /// Immediately before the named sibling.
    | Before of sibling: 'Id
    /// Immediately after the named sibling.
    | After of sibling: 'Id
    /// At this position among the siblings, `0` (first) to their count (last) inclusive.
    | Index of int

/// Why `TreePlacement` could not lower a placement (Phase 312). Every case names the failure and
/// enumerates what was valid, as `Rejection` does.
[<RequireQualifiedAccess>]
type PlaceError<'Id> =
    /// The op the placement lowers to would be refused by the engine — an unknown parent or node,
    /// a duplicate id, a move of the root or under itself, a parent `canHold` refuses. The envelope
    /// is the engine's own, exactly as `canApply` would return it.
    | Refused of Rejection<'Id>
    /// The anchor is not among the destination's children other than the node being placed;
    /// `siblings` are the ids that are, in order.
    | UnknownAnchor of parent: 'Id * anchor: 'Id * siblings: 'Id list
    /// The index is outside `0 .. count`, where `count` is the number of the destination's children
    /// other than the node being placed.
    | IndexOutOfRange of parent: 'Id * index: int * count: int

/// The placement algebra (Phase 312): a placed insert, move and clone, each LOWERED to a skeleton
/// script — never a new op. Every consumer that wanted a node anywhere but last derived the full
/// sibling permutation itself; this derives it once, over the witnesses, so it serves every domain.
///
/// **Named for what it is.** `Fuaran.Core.Placement` is already a public type (where a capability's
/// body runs), so this module is `TreePlacement` rather than a second `Placement` whose resolution
/// would depend on which packages a consumer opens (`DECISIONS.md`, Phase 312).
///
/// **The scripts.** A placement is validated by the engine's own dry run first (`PlaceError.Refused`
/// carries its envelope unchanged), then its anchor is resolved, then:
///   - `place`: `[InsertChild]` when the node lands last, else
///     `[Batch [InsertChild; ReorderChildren]]` — one insert plus one reorder, atomic;
///   - `move` to another parent: `[MoveNode]` when it lands last, else
///     `[Batch [MoveNode; ReorderChildren]]`; within its own parent: `[ReorderChildren]`, or `[]`
///     when it is already there — a reorder footprints the parent alone, where a move would also
///     write the node and an unknown parent;
///   - `clone`: the source subtree with every id renamed by `FreshIds.repairDuplicates` against the
///     tree's ids, then `place`d.
/// A reorder leg that would restate the order appending already gives is dropped, so the common
/// case stays one bare op. **The law** (`Conformance.placementLaws`): `applyAll` of the script puts
/// the node exactly at the anchor's position and moves nothing else.
///
/// The `…Contained` forms validate through `canApplyContained canHold`, for a domain that executes
/// through `applyContained` / `applyAllWith canHold`; the plain forms are those with every node able
/// to hold children.
[<RequireQualifiedAccess>]
module TreePlacement =

    /// The position `anchor` names among `others` (the destination's children other than the node
    /// being placed), or the refusal naming why it names none.
    let private positionOf
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (others: 'Id list)
        : Result<int, PlaceError<'Id>> =
        let count = List.length others

        let find a =
            others |> List.tryFindIndex (fun c -> idw.Equals c a)

        match anchor with
        | Anchor.First -> Ok 0
        | Anchor.Last -> Ok count
        | Anchor.Before a ->
            match find a with
            | Some i -> Ok i
            | None -> Error(PlaceError.UnknownAnchor(parent, a, others))
        | Anchor.After a ->
            match find a with
            | Some i -> Ok(i + 1)
            | None -> Error(PlaceError.UnknownAnchor(parent, a, others))
        | Anchor.Index i ->
            if i < 0 || i > count then
                Error(PlaceError.IndexOutOfRange(parent, i, count))
            else
                Ok i

    /// The ids of `parent`'s children, once the engine's dry run has said `parent` exists.
    let private childIds (w: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (parent: 'Id) (root: 'Node) : 'Id list =
        match Tree.tryFind w idw parent root with
        | Some p -> w.Children p |> List.map w.Id
        | None -> []

    /// `place` under a container capability: the dry run is `canApplyContained canHold`.
    let placeContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (node: 'Node)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        let insert = InsertChild(parent, node)

        match Ops.canApplyContained canHold w idw insert root with
        | Error r -> Error(PlaceError.Refused r)
        | Ok() ->
            let others = childIds w idw parent root

            positionOf idw parent anchor others
            |> Result.map (fun k ->
                if k = List.length others then
                    [ insert ]
                else
                    [ Batch [ insert; ReorderChildren(parent, List.insertAt k (w.Id node) others) ] ])

    /// Insert `node` under `parent` at `anchor`, as a script `Ops.applyAll` accepts (see the module
    /// note for its shape). Refused, by name, when the engine would refuse the insert, when the
    /// anchor names no other child of `parent`, or when the index is out of range.
    let place
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (node: 'Node)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        placeContained (fun _ -> true) w idw parent anchor node root

    /// `move` under a container capability: the dry run is `canApplyContained canHold`.
    let moveContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (newParent: 'Id)
        (anchor: Anchor<'Id>)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        let move = MoveNode(target, newParent)

        match Ops.canApplyContained canHold w idw move root with
        | Error r -> Error(PlaceError.Refused r)
        | Ok() ->
            let siblings = childIds w idw newParent root
            let isTarget c = idw.Equals c target
            let held = siblings |> List.filter isTarget |> List.length
            let others = siblings |> List.filter (isTarget >> not)

            // Phase 383 — a tree holding `target` more than once under `newParent` is not
            // `Tree.WellFormed`; the move is refused naming the repeated id, as the engine refuses a
            // repeated id, rather than comparing two lists of different lengths.
            if held > 1 then
                Error(PlaceError.Refused(DuplicateId target))
            else
                positionOf idw newParent anchor others
                |> Result.map (fun k ->
                    let wanted = List.insertAt k target others

                    // Element-wise under the witness, false on a length difference — never a throw.
                    let rec sameIds (a: 'Id list) (b: 'Id list) =
                        match a, b with
                        | [], [] -> true
                        | x :: xs, y :: ys -> idw.Equals x y && sameIds xs ys
                        | _ -> false

                    if held = 1 then
                        if sameIds wanted siblings then
                            []
                        else
                            [ ReorderChildren(newParent, wanted) ]
                    elif k = List.length others then
                        [ move ]
                    else
                        [ Batch [ move; ReorderChildren(newParent, wanted) ] ])

    /// Move `target` under `newParent` at `anchor` — to another parent, or to another position among
    /// its own siblings — as a script `Ops.applyAll` accepts (see the module note for its shape).
    let move
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (target: 'Id)
        (newParent: 'Id)
        (anchor: Anchor<'Id>)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        moveContained (fun _ -> true) w idw target newParent anchor root

    /// `clone` under a container capability.
    let cloneContained
        (canHold: 'Node -> bool)
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (setId: 'Id -> 'Node -> 'Node)
        (mint: 'Id -> Set<string> -> 'Id)
        (source: 'Id)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        match Tree.subtree w idw source root with
        | None -> Error(PlaceError.Refused(UnknownNode(source, Tree.ids w root)))
        | Some sub ->
            let taken = Tree.ids w root |> List.map idw.ToString |> Set.ofList
            let copy, _ = FreshIds.repairDuplicates w idw setId mint taken sub
            placeContained canHold w idw parent anchor copy root

    /// Copy the subtree at `source` and place the copy under `parent` at `anchor`. Every id of the
    /// copy is renamed through `mint` (`FreshIds.derived idw`, `FreshIds.sequential idw prefix`, or a
    /// domain's own strategy) against the tree's ids, by `FreshIds.repairDuplicates`; the script is
    /// `place`'s, so the copy's ids are the ones its `InsertChild` carries. A paste from ANOTHER tree
    /// is the same two steps a caller composes: `repairDuplicates` against this tree's keys, then
    /// `place`.
    let clone
        (w: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (setId: 'Id -> 'Node -> 'Node)
        (mint: 'Id -> Set<string> -> 'Id)
        (source: 'Id)
        (parent: 'Id)
        (anchor: Anchor<'Id>)
        (root: 'Node)
        : Result<SkeletonOp<'Node, 'Id> list, PlaceError<'Id>> =
        cloneContained (fun _ -> true) w idw setId mint source parent anchor root
