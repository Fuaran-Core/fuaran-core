namespace Fuaran.Core

/// The Phase 312 families: placement, lowering and fresh ids — `TreeLaws` until the Phase 388 split
/// along its banners.
module internal PlacementTreeLaws =
    // ---- Phase 312: the placement algebra, tree lowering and fresh ids ----

    /// Every node's child keys, by the node's own key — the "what moved" snapshot the Phase 312
    /// laws compare across an apply.
    let private childKeys (nodew: NodeWitness<'Node, 'Id>) (idw: IdWitness<'Id>) (t: 'Node) : Map<string, string list> =
        Tree.preorder nodew t
        |> List.map (fun n ->
            idw.ToString(nodew.Id n), (nodew.Children n |> List.map (fun c -> idw.ToString(nodew.Id c))))
        |> Map.ofList

    /// Draw an anchor over `others` (the destination's children other than the node placed) and
    /// the position it names. Every anchor shape is drawn; a sibling-named one only where there is
    /// a sibling to name.
    let private drawAnchor (rng: LawKit.Draws) (others: 'Id list) : Anchor<'Id> * int =
        let count = List.length others

        match rng.IntBelow 5 with
        | 0 -> Anchor.First, 0
        | 1 -> Anchor.Last, count
        | 2 when count > 0 ->
            let i = rng.IntBelow count
            Anchor.Before(List.item i others), i
        | 3 when count > 0 ->
            let i = rng.IntBelow count
            Anchor.After(List.item i others), i + 1
        | _ ->
            let i = rng.IntBelow(count + 1)
            Anchor.Index i, i

    /// The placement laws (Phase 312) — certify `TreePlacement.placeContained` / `moveContained`
    /// against a domain's own witness, executed the way that domain executes (`applyAllWith` under
    /// the generator's `CanHold`):
    ///
    ///   - **landing**: an accepted placement's script is accepted by `applyAllWith`, puts the node
    ///     at exactly the position its anchor names, and changes no other node's child list — for a
    ///     placed insert, a move to another parent (whose source parent loses only the node), and a
    ///     move within its own parent;
    ///   - **the bare op**: the reorder leg is present exactly when appending would not already give
    ///     the anchored order (a move within a parent to where it already is lowers to `[]`);
    ///   - **refusals by name** (BUILT every iteration): an anchor naming no other child is
    ///     `UnknownAnchor`, an index outside `0 .. count` is `IndexOutOfRange`, and a placement the
    ///     engine refuses — an absent parent, a duplicate id, a move of the root — carries exactly
    ///     the envelope `canApplyContained` returns.
    ///
    /// The landing arms are drawn, so the family is guarded on each: a placed insert, a move across
    /// parents, a move within a parent, and a landing short of last (the arm that emits a reorder).
    let placementLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = LawKit.canHoldOf gen

        let lands =
            LawKit.LawCell(
                "an accepted placement lands the node exactly at its anchor's position and moves nothing else",
                Some "placement arm"
            )

        let bare =
            LawKit.LawCell(
                "the reorder leg is present exactly when appending would not give the anchored order",
                Some "placement arm"
            )

        let asEngine =
            LawKit.LawCell "a placement the engine would refuse is Refused with the engine's own envelope"

        let byName =
            LawKit.LawCell "a missing anchor and an out-of-range index are refused by name"

        let mutable placed = 0
        let mutable across = 0
        let mutable within = 0
        let mutable nonFinal = 0

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let keyOf (n: 'Node) = idw.ToString(nodew.Id n)
            let treeKeys = Tree.ids nodew tree |> List.map idw.ToString |> Set.ofList
            let before = childKeys nodew idw tree

            let landed
                (what: string)
                (script: SkeletonOp<'Node, 'Id> list)
                (parentKey: string)
                (movedKey: string)
                (others: string list)
                (k: int)
                (sourceParent: string option)
                (added: Set<string>)
                =
                match Ops.applyAllWith canHold nodew idw script tree with
                | Error(j, e, _) ->
                    lands.Check(
                        false,
                        fun () -> at (sprintf "%s: the script %A was refused at step %d: %A" what script j e)
                    )

                    None
                | Ok post ->
                    let after = childKeys nodew idw post
                    let expected = List.insertAt k movedKey others
                    let destOk = Map.tryFind parentKey after = Some expected

                    let sourceOk =
                        match sourceParent with
                        | Some sp -> Map.tryFind sp after = Some(before.[sp] |> List.filter (fun c -> c <> movedKey))
                        | None -> true

                    let restOk =
                        before
                        |> Map.forall (fun key kids ->
                            key = parentKey || Some key = sourceParent || Map.tryFind key after = Some kids)

                    let keysOk =
                        (after |> Map.toList |> List.map fst |> Set.ofList) = Set.union treeKeys added

                    lands.Check(
                        destOk && sourceOk && restOk && keysOk,
                        fun () ->
                            at (
                                sprintf
                                    "%s: %A should land %s at position %d under %s (expected children %A, got %A) and move nothing else (source parent ok %b, other nodes ok %b, id set ok %b)"
                                    what
                                    script
                                    movedKey
                                    k
                                    parentKey
                                    expected
                                    (Map.tryFind parentKey after)
                                    sourceOk
                                    restOk
                                    keysOk
                            )
                    )

                    Some post

            let holders = Tree.preorder nodew tree |> List.filter canHold

            // ---- a placed insert, and the refusals built beside it ----
            match holders with
            | [] -> ()
            | _ ->
                let parent = rng.Choose holders
                let pid = nodew.Id parent
                let node = rng.Draw(gen.FreshNode treeKeys)
                let others = nodew.Children parent |> List.map nodew.Id
                let count = List.length others
                let anchor, k = drawAnchor rng others
                let insert = InsertChild(pid, node)

                match TreePlacement.placeContained canHold nodew idw pid anchor node tree with
                | Error(PlaceError.Refused r) when Ops.canApplyContained canHold nodew idw insert tree = Error r ->
                    // the generator's fresh node is one this witness refuses to graft — the engine's
                    // verdict, carried unchanged, and nothing to land
                    asEngine.Check(true, fun () -> "")
                | Error e ->
                    lands.Check(
                        false,
                        fun () ->
                            at (sprintf "placing a fresh node under %s at %A was refused: %A" (keyOf parent) anchor e)
                    )
                | Ok script ->
                    placed <- placed + 1

                    if k < count then
                        nonFinal <- nonFinal + 1

                    bare.Check(
                        (script = [ insert ]) = (k = count),
                        fun () -> at (sprintf "place at %d of %d lowered to %A" k count script)
                    )

                    let added = Tree.ids nodew node |> List.map idw.ToString |> Set.ofList

                    match
                        landed
                            "place"
                            script
                            (keyOf parent)
                            (keyOf node)
                            (others |> List.map idw.ToString)
                            k
                            None
                            added
                    with
                    | Some post ->
                        lands.Check(
                            Tree.subtree nodew idw (nodew.Id node) post = Some node,
                            fun () -> at "the placed node did not arrive as it was handed over"
                        )
                    | None -> ()

                // BUILT: an anchor naming no other child, and an index either side of the range.
                let stranger = nodew.Id node

                for a, expected in
                    [ Anchor.Before stranger, PlaceError.UnknownAnchor(pid, stranger, others)
                      Anchor.After stranger, PlaceError.UnknownAnchor(pid, stranger, others)
                      Anchor.Index(count + 1), PlaceError.IndexOutOfRange(pid, count + 1, count)
                      Anchor.Index(-1), PlaceError.IndexOutOfRange(pid, -1, count) ] do
                    // a node this witness will not graft is refused by the engine first, which is
                    // the documented precedence; the anchor refusal is asserted where the insert
                    // itself is acceptable
                    if Ops.canApplyContained canHold nodew idw insert tree = Ok() then
                        let got = TreePlacement.placeContained canHold nodew idw pid a node tree

                        byName.Check(
                            (got = Error expected),
                            fun () -> at (sprintf "placing at %A answered %A, expected %A" a got expected)
                        )

                // BUILT: three placements the engine refuses — under an absent parent, a subtree
                // repeating the root's id, a move of the root — each carrying the engine's envelope.
                let absent = nodew.Id(rng.Draw(gen.FreshNode(Set.add (keyOf node) treeKeys)))

                let engineCases =
                    [ "an absent parent",
                      TreePlacement.placeContained canHold nodew idw absent Anchor.Last node tree,
                      InsertChild(absent, node)
                      "a duplicate id",
                      TreePlacement.placeContained canHold nodew idw pid Anchor.First tree tree,
                      InsertChild(pid, tree)
                      "a move of the root",
                      TreePlacement.moveContained canHold nodew idw (nodew.Id tree) pid Anchor.Last tree,
                      MoveNode(nodew.Id tree, pid) ]

                for what, got, op in engineCases do
                    match Ops.canApplyContained canHold nodew idw op tree with
                    | Error r ->
                        asEngine.Check(
                            (got = Error(PlaceError.Refused r)),
                            fun () -> at (sprintf "%s: answered %A, the engine refuses %A" what got r)
                        )
                    | Ok() ->
                        asEngine.Check(
                            false,
                            fun () ->
                                at (sprintf "%s: the engine ACCEPTED %A, which this arm builds to be refused" what op)
                        )

            // ---- moves: to a drawn holder outside the subtree, and within the node's own parent ----
            match Tree.preorder nodew tree with
            | []
            | [ _ ] -> ()
            | _ :: below ->
                let target = rng.Choose below
                let tid = nodew.Id target
                let tk = keyOf target
                let subKeys = Tree.ids nodew target |> List.map idw.ToString |> Set.ofList

                let sourceParent =
                    Tree.parentOf nodew idw tid tree |> Option.map keyOf |> Option.defaultValue ""

                let moveTo (q: 'Node) =
                    let qid = nodew.Id q
                    let siblings = nodew.Children q |> List.map nodew.Id
                    let others = siblings |> List.filter (fun c -> not (idw.Equals c tid))
                    let count = List.length others
                    let anchor, k = drawAnchor rng others
                    let isWithin = keyOf q = sourceParent

                    match TreePlacement.moveContained canHold nodew idw tid qid anchor tree with
                    | Error e ->
                        lands.Check(
                            false,
                            fun () -> at (sprintf "moving %s under %s at %A was refused: %A" tk (keyOf q) anchor e)
                        )
                    | Ok script ->
                        if isWithin then
                            within <- within + 1
                        else
                            across <- across + 1

                        if k < count then
                            nonFinal <- nonFinal + 1

                        let bareOk =
                            if isWithin then
                                let at0 = siblings |> List.tryFindIndex (fun c -> idw.Equals c tid)
                                (List.isEmpty script) = (at0 = Some k)
                            else
                                (script = [ MoveNode(tid, qid) ]) = (k = count)

                        bare.Check(
                            bareOk,
                            fun () -> at (sprintf "move (within=%b) to %d of %d lowered to %A" isWithin k count script)
                        )

                        landed
                            "move"
                            script
                            (keyOf q)
                            tk
                            (others |> List.map idw.ToString)
                            k
                            (if isWithin then None else Some sourceParent)
                            Set.empty
                        |> ignore

                match holders |> List.filter (fun h -> not (Set.contains (keyOf h) subKeys)) with
                | [] -> ()
                | destinations -> moveTo (rng.Choose destinations)

                match holders |> List.tryFind (fun h -> keyOf h = sourceParent) with
                | Some sp -> moveTo sp
                | None -> ())

        LawKit.results [ lands; bare; asEngine; byName ]
        @ [ SampleAdequacy.reached
                "Conformance.placementLaws"
                "placement arm"
                seed
                [ "place", placed
                  "move across parents", across
                  "move within a parent", within
                  "non-final position", nonFinal ] ]

    /// The lowering laws (Phase 312) — certify `Ops.lower` / `Ops.skeletonRoot` against a domain's
    /// own witness:
    ///
    ///   - **round trip**: `applyAll (lower t) (skeletonRoot t) = Ok t` for every well-formed tree
    ///     drawn, and — **the contained form** — `applyAllWith canHold` of the same script gives the
    ///     same tree whenever the tree is in the containment invariant (every node holding children
    ///     satisfies `canHold`);
    ///   - **shape**: the script is one `InsertChild(parent, shellOf node)` per node below the root,
    ///     in preorder, so a streamed and a batched emission are the same ops in the same order;
    ///   - **a repeated id is refused** (BUILT): a tree carrying an id twice lowers to a script
    ///     refused `DuplicateId` naming the id `Tree.wellFormed` names.
    let loweringLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = LawKit.canHoldOf gen

        let roundTrip =
            LawKit.LawCell("applyAll (lower t) (skeletonRoot t) = t", Some "lowered tree")

        let contained =
            LawKit.LawCell(
                "the contained form: applyAllWith canHold (lower t) (skeletonRoot t) = t on a tree in the containment invariant",
                Some "lowered tree"
            )

        let shape =
            LawKit.LawCell(
                "lower emits one InsertChild (parent, shell) per node below the root, in preorder",
                Some "lowered tree"
            )

        let duplicate =
            LawKit.LawCell(
                "a tree carrying an id twice lowers to a script refused DuplicateId at its first repeated id",
                Some "lowered tree"
            )

        let mutable lowered = 0
        let mutable inInvariant = 0
        let mutable filled = 0
        let mutable repeated = 0

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let nodes = Tree.preorder nodew tree

            if Tree.isWellFormed nodew idw tree then
                lowered <- lowered + 1
                let script = Ops.lower nodew tree
                let skeleton = Ops.skeletonRoot nodew tree

                // a shell below the root was filled: some non-root node holds children
                if
                    nodes
                    |> List.tail
                    |> List.exists (fun n -> not (List.isEmpty (nodew.Children n)))
                then
                    filled <- filled + 1

                let plain = Ops.applyAll nodew idw script skeleton

                roundTrip.Check(
                    (plain = Ok tree),
                    fun () -> at (sprintf "applyAll (lower t) (skeletonRoot t) answered %A for %A" plain tree)
                )

                if nodes |> List.forall (fun n -> List.isEmpty (nodew.Children n) || canHold n) then
                    inInvariant <- inInvariant + 1
                    let held = Ops.applyAllWith canHold nodew idw script skeleton

                    contained.Check(
                        (held = Ok tree),
                        fun () -> at (sprintf "applyAllWith canHold (lower t) (skeletonRoot t) answered %A" held)
                    )

                let expected =
                    nodes
                    |> List.tail
                    |> List.map (fun n ->
                        let parent =
                            Tree.parentOf nodew idw (nodew.Id n) tree
                            |> Option.map nodew.Id
                            |> Option.defaultValue (nodew.Id tree)

                        InsertChild(parent, Ops.shellOf nodew n))

                shape.Check(
                    (script = expected),
                    fun () -> at (sprintf "lower emitted %A, expected %A" script expected)
                )

            // BUILT: a copy of a drawn node appended under a holder repeats every id it carries.
            match nodes |> List.filter canHold with
            | [] -> ()
            | holders ->
                let holder = rng.Choose holders
                let victim = rng.Choose nodes

                let bad =
                    Tree.updateNode
                        nodew
                        idw
                        (nodew.Id holder)
                        (fun h -> nodew.ReplaceChildren h (nodew.Children h @ [ victim ]))
                        tree

                match bad |> Option.map (Tree.wellFormed nodew idw) with
                | Some(Tree.RepeatedId d) when Tree.isWellFormed nodew idw tree ->
                    repeated <- repeated + 1
                    let badTree = Option.get bad

                    let got =
                        Ops.applyAll nodew idw (Ops.lower nodew badTree) (Ops.skeletonRoot nodew badTree)

                    duplicate.Check(
                        (match got with
                         | Error(_, DuplicateId d', _) -> idw.Equals d d'
                         | _ -> false),
                        fun () ->
                            at (sprintf "the lowered script of a tree repeating %s answered %A" (idw.ToString d) got)
                    )
                | _ -> ())

        LawKit.results [ roundTrip; contained; shape; duplicate ]
        @ [ SampleAdequacy.reached
                "Conformance.loweringLaws"
                "lowered tree"
                seed
                [ "well-formed tree", lowered
                  "tree in the containment invariant", inInvariant
                  "filled shell below the root", filled
                  "built repeated id", repeated ] ]

    /// The fresh-id laws (Phase 312) — certify a domain's minting strategy `mint` (the shipped
    /// `FreshIds.derived idw` / `FreshIds.sequential idw prefix`, or the domain's own), its `setId`,
    /// and `FreshIds.repairDuplicates` and `TreePlacement.cloneContained` over them:
    ///
    ///   - `setId` sets the id and keeps the kind and the children;
    ///   - a minted id's key is ABSENT from the taken set it was minted against — asserted down a
    ///     chain of three mints, each against the set grown by the one before, so a probing
    ///     strategy is made to probe — and minting is deterministic;
    ///   - `repairDuplicates` is the identity on a well-formed tree holding no taken key, and on a
    ///     BUILT tree that repeats ids (against a drawn taken set) leaves a well-formed tree holding
    ///     no taken key, renames exactly the later occurrences and the taken ids, keeps the shape,
    ///     and maps every rename in preorder;
    ///   - a clone places a same-shape copy whose every id is fresh, and changes no other node's
    ///     child list.
    let freshIdLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (setId: 'Id -> 'Node -> 'Node)
        (mint: 'Id -> Set<string> -> 'Id)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = LawKit.canHoldOf gen

        let setIdLawful =
            LawKit.LawCell "setId sets the id and keeps the kind and the children"

        let absent =
            LawKit.LawCell "a minted id's key is absent from the taken set it was minted against"

        let deterministic =
            LawKit.LawCell "minting is deterministic: one id and one taken set mint one key"

        let identity =
            LawKit.LawCell "repairDuplicates is the identity on a well-formed tree holding no taken key"

        let repairs =
            LawKit.LawCell("repairDuplicates leaves a well-formed tree holding no taken key", Some "built arm")

        let faithful =
            LawKit.LawCell(
                "repairDuplicates renames exactly the later occurrences and the taken ids, keeps the shape, and maps every rename in preorder",
                Some "built arm"
            )

        let clones =
            LawKit.LawCell(
                "a clone places a same-shape copy whose every id is fresh and moves nothing else",
                Some "built arm"
            )

        let mutable repaired = 0
        let mutable cloned = 0

        let keys (t: 'Node) =
            Tree.ids nodew t |> List.map idw.ToString

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let nodes = Tree.preorder nodew tree
            let treeKeys = keys tree |> Set.ofList

            // ---- setId ----
            let n = rng.Choose nodes
            let f = nodew.Id(rng.Draw(gen.FreshNode treeKeys))
            let renamed = setId f n

            setIdLawful.Check(
                idw.Equals (nodew.Id renamed) f
                && nodew.KindTag renamed = nodew.KindTag n
                && nodew.Children renamed = nodew.Children n,
                fun () ->
                    at (sprintf "setId %s on %s moved more than the id" (idw.ToString f) (idw.ToString(nodew.Id n)))
            )

            // ---- minting: a chain of three, each against the set the one before grew ----
            let source = nodew.Id(rng.Choose nodes)

            let rec chain (taken: Set<string>) (left: int) =
                if left > 0 then
                    let m = mint source taken
                    let k = idw.ToString m

                    absent.Check(
                        not (Set.contains k taken),
                        fun () ->
                            at (
                                sprintf "minting for %s answered %s, which the taken set holds" (idw.ToString source) k
                            )
                    )

                    let again = idw.ToString(mint source taken)

                    deterministic.Check(
                        (again = k),
                        fun () -> at (sprintf "minting for %s answered %s, then %s" (idw.ToString source) k again)
                    )

                    chain (Set.add k taken) (left - 1)

            chain treeKeys 3

            // ---- the identity on a clean tree ----
            if Tree.isWellFormed nodew idw tree then
                let same = FreshIds.repairDuplicates nodew idw setId mint Set.empty tree

                identity.Check(
                    (same = (tree, [])),
                    fun () -> at (sprintf "repairDuplicates changed a well-formed tree: mapping %A" (snd same))
                )

            match nodes |> List.filter canHold with
            | [] -> ()
            | holders ->
                // ---- BUILT: a tree repeating every id of a drawn node, against a drawn taken set ----
                let holder = rng.Choose holders
                let victim = rng.Choose nodes

                let taken = keys tree |> List.filter (fun _ -> rng.IntBelow 2 = 0) |> Set.ofList

                match
                    Tree.updateNode
                        nodew
                        idw
                        (nodew.Id holder)
                        (fun h -> nodew.ReplaceChildren h (nodew.Children h @ [ victim ]))
                        tree
                with
                | Some bad when not (Tree.isWellFormed nodew idw bad) ->
                    repaired <- repaired + 1
                    let fixedTree, mapping = FreshIds.repairDuplicates nodew idw setId mint taken bad
                    let pre = keys bad
                    let post = keys fixedTree

                    repairs.Check(
                        Tree.isWellFormed nodew idw fixedTree
                        && post |> List.forall (fun k -> not (Set.contains k taken)),
                        fun () -> at (sprintf "repairDuplicates left %A against taken %A" post taken)
                    )

                    // the renamed positions: a taken key, or a key an earlier position carries
                    let rec expectedRenames (seen: Set<string>) (ks: string list) =
                        match ks with
                        | [] -> []
                        | k :: rest ->
                            if Set.contains k taken || Set.contains k seen then
                                true :: expectedRenames seen rest
                            else
                                false :: expectedRenames (Set.add k seen) rest

                    let renames = expectedRenames Set.empty pre

                    let ok =
                        List.length pre = List.length post
                        && List.forall2 (fun (a, b) r -> (a <> b) = r) (List.zip pre post) renames
                        && (List.zip pre post |> List.zip renames |> List.filter fst |> List.map snd) = (mapping
                                                                                                         |> List.map
                                                                                                             (fun
                                                                                                                 (a, b) ->
                                                                                                                 idw.ToString
                                                                                                                     a,
                                                                                                                 idw.ToString
                                                                                                                     b))
                        && Tree.contentHash nodew bad = Tree.contentHash nodew fixedTree

                    faithful.Check(ok, fun () -> at (sprintf "repairDuplicates mapped %A: %A -> %A" mapping pre post))
                | _ -> ()

                // ---- a clone, placed under a drawn holder at a drawn anchor ----
                let source = rng.Choose nodes
                let parent = rng.Choose holders
                let others = nodew.Children parent |> List.map nodew.Id
                let anchor, k = drawAnchor rng others

                match
                    TreePlacement.cloneContained
                        canHold
                        nodew
                        idw
                        setId
                        mint
                        (nodew.Id source)
                        (nodew.Id parent)
                        anchor
                        tree
                with
                | Error e ->
                    clones.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "cloning %s under %s at %A was refused: %A"
                                    (idw.ToString(nodew.Id source))
                                    (idw.ToString(nodew.Id parent))
                                    anchor
                                    e
                            )
                    )
                | Ok script ->
                    let copy =
                        match script with
                        | [ InsertChild(_, c) ]
                        | [ Batch(InsertChild(_, c) :: _) ] -> Some c
                        | _ -> None

                    match copy, Ops.applyAllWith canHold nodew idw script tree with
                    | Some c, Ok post ->
                        cloned <- cloned + 1
                        let before = childKeys nodew idw tree
                        let after = childKeys nodew idw post
                        let pk = idw.ToString(nodew.Id parent)
                        let ck = idw.ToString(nodew.Id c)

                        let ok =
                            Tree.contentHash nodew c = Tree.contentHash nodew source
                            && keys c |> List.forall (fun x -> not (Set.contains x treeKeys))
                            && Tree.isWellFormed nodew idw post = Tree.isWellFormed nodew idw tree
                            && Map.tryFind pk after = Some(List.insertAt k ck (others |> List.map idw.ToString))
                            && before
                               |> Map.forall (fun key kids -> key = pk || Map.tryFind key after = Some kids)

                        clones.Check(
                            ok,
                            fun () -> at (sprintf "the clone of %A by %A did not land as a fresh copy" source script)
                        )
                    | _ ->
                        clones.Check(
                            false,
                            fun () ->
                                at (
                                    sprintf
                                        "the clone script %A was not one placed insert accepted by the engine"
                                        script
                                )
                        ))

        LawKit.results [ setIdLawful; absent; deterministic; identity; repairs; faithful; clones ]
        @ [ SampleAdequacy.reached
                "Conformance.freshIdLaws"
                "built arm"
                seed
                [ "repeated id built", repaired; "clone placed", cloned ] ]
