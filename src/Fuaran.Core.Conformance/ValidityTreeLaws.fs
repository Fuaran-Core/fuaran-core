namespace Fuaran.Core

/// The validity families: containment and references (Phase 313), and the digests, the per-id change
/// classification and the introduced-finding gate (Phase 314) — `TreeLaws` until the Phase 388 split
/// along its banners.
module internal ValidityTreeLaws =
    // ---- Phase 313: the structural-integrity families ----

    /// The containment-grammar laws (Phase 313) — the teeth on the grammar engine
    /// (`Ops.applyGrammar` and its dry runs), `Diff.toOpsGrammar`, `Arbitration.arbitrateGrammar` and
    /// `Validator.containment`, at the domain's own grammar. Each iteration draws a tree, PRUNES it to
    /// the grammar (its lowering, each insert kept only where the grammar engine accepts it — so the
    /// start state keeps the grammar), and threads drawn ops through the engine:
    ///
    ///   - **agreement** — the engine is `applyContained` then the grammar: it refuses with the same
    ///     envelope wherever `applyContained` refuses, and with `IllegalChild` exactly where the tree
    ///     `applyContained` would build breaks the grammar;
    ///   - **preservation** — an accepted op keeps every parent's children legal (`grammar_preserves`
    ///     in `proofs/Preservation.fst`, asked of the shipped engine);
    ///   - **naming** — `IllegalChild` names a pair the step would create, with the parent's kind and
    ///     what the grammar lets that kind hold; the refusal is also BUILT each iteration the drawn
    ///     tree carries an illegal pair, by cutting the child out and grafting it back;
    ///   - the **dry runs** answer the engine's envelope; the **diff** reconstructs a grammar-legal
    ///     pair through `applyAllGrammar` and refuses an `after` holding an illegal pair, naming the
    ///     first; **arbitration** admits only scripts the engine accepts, and its accepted scripts
    ///     land in either order to one grammar-legal tree; and **`Validator.containment`** reports
    ///     exactly the pairs `Ops.illegalChildren` does.
    ///
    /// A grammar the generator never violates exercises only the accepting side, which the
    /// grammar-refusal guard reports rather than passing. `'Node` and `'Id` need equality. Opt-in: a
    /// domain with a grammar runs it beside `certify`.
    let containmentLaws
        (allowedChildren: string -> string list option)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = LawKit.canHoldOf gen

        let engine op t =
            Ops.applyGrammar allowedChildren canHold nodew idw op t

        let legal (t: 'Node) =
            List.isEmpty (Ops.illegalChildren allowedChildren nodew t)

        let agreement =
            LawKit.LawCell
                "the grammar engine is applyContained then the grammar (the same envelope where it refuses, IllegalChild exactly where its result breaks the grammar)"

        let preservation =
            LawKit.LawCell "an accepted op keeps every parent's children legal"

        let named =
            LawKit.LawCell(
                "IllegalChild names a pair the step would create, the parent's kind and what the grammar lets it hold",
                Some "grammar refusal"
            )

        let dryRun =
            LawKit.LawCell "the dry runs answer the engine's envelope (canApplyGrammar, canApplyAllGrammar)"

        let diffBack =
            LawKit.LawCell "grammar diff reconstruction (applyAllGrammar (toOpsGrammar before after) before = after)"

        let diffRefuses =
            LawKit.LawCell(
                "toOpsGrammar refuses an after holding an illegal pair with IllegalChildInTree naming the first, after every toOpsContained refusal",
                Some "grammar refusal"
            )

        let arbitration =
            LawKit.LawCell
                "arbitrateGrammar admits only what the grammar engine accepts, and its accepted scripts land in either order to one grammar-legal tree"

        let validator =
            LawKit.LawCell "Validator.containment reports exactly Ops.illegalChildren, child by child"

        let mutable accepted = 0
        let mutable refused = 0
        let mutable illegalDrawn = 0

        let expectNamed (at: string -> string) (result: 'Node) (e: Rejection<'Id>) =
            match e with
            | IllegalChild(child, childKind, parent, parentKind, legalKinds) ->
                let pair =
                    Ops.illegalChildren allowedChildren nodew result
                    |> List.tryFind (fun (p, c) -> idw.Equals (nodew.Id p) parent && idw.Equals (nodew.Id c) child)

                named.Check(
                    (match pair with
                     | Some(p, c) ->
                         nodew.KindTag p = parentKind
                         && nodew.KindTag c = childKind
                         && legalKinds = (allowedChildren parentKind |> Option.defaultValue [])
                     | None -> false),
                    fun () -> at (sprintf "IllegalChild %A names no illegal pair of the tree the step builds" e)
                )
            | other -> named.Check(false, fun () -> at (sprintf "expected IllegalChild, got %A" other))

        // The naive specification of one op, step by step as a batch is threaded: the container-aware
        // engine decides, and a step it accepts into a tree that breaks the grammar is a grammar
        // refusal (carrying that step's tree, which the refusal must name a pair of). From a
        // grammar-legal tree every illegal pair of an accepted step's result is one it created.
        let rec expectedOf (op: SkeletonOp<'Node, 'Id>) (t: 'Node) : Result<'Node, Choice<Rejection<'Id>, 'Node>> =
            match op with
            | Batch ops -> ops |> List.fold (fun acc o -> acc |> Result.bind (expectedOf o)) (Ok t)
            | _ ->
                match Ops.applyContained canHold nodew idw op t with
                | Error e -> Error(Choice1Of2 e)
                | Ok t1 when legal t1 -> Ok t1
                | Ok t1 -> Error(Choice2Of2 t1)

        let pruned (t: 'Node) =
            Ops.lower nodew t
            |> List.fold
                (fun acc op ->
                    match engine op acc with
                    | Ok t' -> t'
                    | Error _ -> acc)
                (Ops.skeletonRoot nodew t)

        LawKit.run iterations seed (fun rng _ at ->
            let drawn = rng.Draw gen.Tree
            let start = pruned drawn

            validator.Check(
                (let defects = (Validator.containment allowedChildren).Run nodew drawn
                 let pairs = Ops.illegalChildren allowedChildren nodew drawn

                 List.length defects = List.length pairs
                 && List.forall2
                     (fun (d: Defect<'Id>) (_, c) ->
                         d.Code = Validator.IllegalChildCode
                         && (match d.Node with
                             | Some n -> idw.Equals n (nodew.Id c)
                             | None -> false))
                     defects
                     pairs),
                fun () -> at "Validator.containment and Ops.illegalChildren disagree over the drawn tree"
            )

            let mutable cur = start

            for _ in 1..6 do
                let op = rng.Draw(LawKit.genOp nodew idw gen cur)
                let g = engine op cur
                let expected = expectedOf op cur

                agreement.Check(
                    (match expected, g with
                     | Error(Choice1Of2 e), Error e' -> e = e'
                     | Ok t1, Ok t2 -> t1 = t2
                     | Error(Choice2Of2 _), Error(IllegalChild _) -> true
                     | _ -> false),
                    fun () -> at (sprintf "op %A: expected %A, applyGrammar answered %A" op expected g)
                )

                dryRun.Check(
                    Ops.canApplyGrammar allowedChildren canHold nodew idw op cur = Result.map ignore g
                    && Ops.canApplyAllGrammar allowedChildren canHold nodew idw [ op ] cur = (match g with
                                                                                              | Ok _ -> Ok()
                                                                                              | Error e -> Error(0, e)),
                    fun () -> at (sprintf "a dry run disagrees with applyGrammar on %A" op)
                )

                match g, expected with
                | Ok t', _ ->
                    accepted <- accepted + 1
                    preservation.Check(legal t', fun () -> at (sprintf "op %A was accepted into an illegal tree" op))
                    cur <- t'
                | Error(IllegalChild _ as e), Error(Choice2Of2 result) ->
                    refused <- refused + 1
                    expectNamed at result e
                | _ -> ()

            // the built refusal: the drawn tree's first illegal pair, cut out and grafted back
            match Ops.illegalChildren allowedChildren nodew drawn with
            | (p, c) :: _ ->
                match Ops.apply nodew idw (RemoveNode(nodew.Id c)) drawn with
                | Ok cut ->
                    let graft = InsertChild(nodew.Id p, c)

                    match Ops.applyContained canHold nodew idw graft cut with
                    | Ok result ->
                        refused <- refused + 1

                        expectNamed
                            at
                            result
                            (Ops.applyGrammar allowedChildren canHold nodew idw graft cut
                             |> function
                                 | Error e -> e
                                 | Ok _ -> Rejected("accepted", "the illegal graft was accepted"))
                    | Error _ -> ()
                | Error _ -> ()
            | [] -> ()

            // the diff: a grammar-legal pair reconstructs; the drawn tree, where illegal, is refused
            match Diff.toOpsGrammar allowedChildren canHold nodew idw start cur with
            | Ok ops ->
                let back = Ops.applyAllGrammar allowedChildren canHold nodew idw ops start

                diffBack.Check(
                    (match back with
                     | Ok t -> t = cur
                     | Error _ -> false),
                    fun () -> at (sprintf "applyAllGrammar (toOpsGrammar) answered %A" back)
                )
            | Error e ->
                diffBack.Check(false, fun () -> at (sprintf "toOpsGrammar refused a grammar-legal pair: %A" e))

            match Ops.illegalChildren allowedChildren nodew drawn with
            | (p, c) :: _ ->
                // Phase 302 — counted: the engine-loop refusals above meet "IllegalChild" without
                // this arm ever being reached.
                illegalDrawn <- illegalDrawn + 1
                let answer = Diff.toOpsGrammar allowedChildren canHold nodew idw start drawn

                diffRefuses.Check(
                    (match Diff.toOpsContained canHold nodew idw start drawn, answer with
                     | Error e, Error e' -> e = e'
                     | Ok _, Error(Diff.IllegalChildInTree(child, childKind, parent, parentKind, kinds)) ->
                         idw.Equals child (nodew.Id c)
                         && idw.Equals parent (nodew.Id p)
                         && childKind = nodew.KindTag c
                         && parentKind = nodew.KindTag p
                         && kinds = (allowedChildren parentKind |> Option.defaultValue [])
                     | _ -> false),
                    fun () -> at (sprintf "toOpsGrammar over an illegal after answered %A" answer)
                )
            | [] -> ()

            // arbitration: three one-op proposals against the grammar-legal state
            let proposals =
                [ for k in 1..3 ->
                      { Id = k
                        Holder = "p" + string k
                        Ops = [ rng.Draw(LawKit.genOp nodew idw gen cur) ] } ]

            let verdict =
                Arbitration.arbitrateGrammar allowedChildren canHold nodew idw cur proposals

            let landAll (scripts: SkeletonOp<'Node, 'Id> list list) =
                scripts
                |> List.fold
                    (fun acc ops ->
                        acc
                        |> Result.bind (fun t ->
                            Ops.applyAllGrammar allowedChildren canHold nodew idw ops t
                            |> Result.mapError (fun (i, e, _) -> i, e)))
                    (Ok cur)

            let scripts = verdict.Accepted |> List.map (fun p -> p.Ops)
            let forward = landAll scripts
            let backward = landAll (List.rev scripts)

            arbitration.Check(
                (match forward, backward with
                 | Ok a, Ok b -> a = b && legal a
                 | _ -> false)
                && verdict.Rejected
                   |> List.forall (fun (p, why) ->
                       match why with
                       | Inapplicable(i, r) ->
                           Ops.canApplyAllGrammar allowedChildren canHold nodew idw p.Ops cur = Error(i, r)
                       | Conflicts _ -> true),
                fun () -> at (sprintf "arbitrateGrammar: forward %A, backward %A" forward backward)
            ))

        LawKit.results
            [ agreement
              preservation
              named
              dryRun
              diffBack
              diffRefuses
              arbitration
              validator ]
        @ [ SampleAdequacy.reached
                "Conformance.containmentLaws"
                "grammar refusal"
                seed
                [ "accepted op", accepted
                  "IllegalChild", refused
                  "illegal drawn tree", illegalDrawn ] ]

    /// The reference-integrity laws (Phase 313) — the teeth on `Validator.referenceDefects` /
    /// `forwardReferences` and their families, the reference-aware engine (`Ops.applyReferenced`) and
    /// `Ops.footprintReferenced`, at the domain's own `RefWitness`. The defects are checked against
    /// a naive specification held here (a reference with no declarer; a declaration with no
    /// referrer; the nodes that reach themselves through "refers to a declaration of"; a declaration
    /// later in preorder that is not the referrer's descendant), never against the validator's own
    /// walk. Drawn ops are threaded through the engine:
    ///
    ///   - **agreement** — the engine refuses with `applyContained`'s envelope wherever it refuses,
    ///     and refuses an accepted step with `StillReferenced` exactly when, in the tree it would build,
    ///     a node refers to an id the tree declared before the step and no longer does (a remove's
    ///     subtree, a rewrite that declares less), naming every such node;
    ///   - **preservation** — an accepted op leaves no reference dangling that resolved before it;
    ///   - **footprint** — `footprintReferenced` contains `footprint` and reads every reference an
    ///     inserted subtree or an update payload carries; and, BUILT from the drawn tree wherever a
    ///     node refers to a declaration its declarer's id names, the update of the referrer and the
    ///     removal of the declarer interfere on the referenced id — the race `independent` refuses.
    ///
    /// `'Node` and `'Id` need equality. Opt-in: it needs the `RefWitness` a domain with references
    /// declares.
    let referenceLaws
        (refw: RefWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = LawKit.canHoldOf gen
        let key (i: 'Id) = idw.ToString i
        let none: string -> string list option = fun _ -> None
        let nodeKey (n: 'Node) = key (nodew.Id n)

        let exact =
            LawKit.LawCell
                "referenceDefects reports exactly the dangling references, the unused declarations and the nodes on a reference cycle"

        let forwardExact =
            LawKit.LawCell "forwardReferences reports exactly the references declared later in sibling order"

        let families =
            LawKit.LawCell "referenceIntegrity and referenceOrder report those defects, one coded defect each"

        let agreement =
            LawKit.LawCell
                "the reference engine is applyContained then StillReferenced, exactly where an accepted step leaves a resolved reference dangling"

        let preservation =
            LawKit.LawCell "an accepted op leaves no reference dangling that resolved before it"

        let footprintReads =
            LawKit.LawCell "footprintReferenced contains footprint and reads every reference a script writes"

        let race =
            LawKit.LawCell(
                "writing a reference to x and removing x interfere on x (RightWritesLeftReads)",
                Some "reference arm"
            )

        let mutable removes = 0
        let mutable stillReferenced = 0
        let mutable races = 0

        let dangling (t: 'Node) =
            let declared =
                Tree.preorder nodew t |> List.collect refw.DeclsOf |> List.map key |> Set.ofList

            [ for n in Tree.preorder nodew t do
                  for r in refw.RefsOf n do
                      if not (declared.Contains(key r)) then
                          yield nodeKey n, key r ]

        let unused (t: 'Node) =
            let referenced =
                Tree.preorder nodew t |> List.collect refw.RefsOf |> List.map key |> Set.ofList

            [ for n in Tree.preorder nodew t do
                  for d in refw.DeclsOf n do
                      if not (referenced.Contains(key d)) then
                          yield nodeKey n, key d ]

        let declarersOf (t: 'Node) (r: 'Id) =
            Tree.preorder nodew t
            |> List.filter (fun d -> refw.DeclsOf d |> List.exists (fun x -> key x = key r))

        let onCycle (t: 'Node) =
            let nodes = Tree.preorder nodew t

            let succ (k: string) =
                nodes
                |> List.filter (fun n -> nodeKey n = k)
                |> List.collect refw.RefsOf
                |> List.collect (fun r -> declarersOf t r |> List.map nodeKey)

            let reachesSelf (k: string) =
                let rec go (seen: Set<string>) (frontier: string list) =
                    match frontier with
                    | [] -> false
                    | x :: rest ->
                        let next = succ x

                        if List.contains k next then
                            true
                        else
                            let fresh = next |> List.filter (fun y -> not (seen.Contains y)) |> List.distinct
                            go (Set.union seen (Set.ofList fresh)) (rest @ fresh)

                go Set.empty [ k ]

            nodes |> List.map nodeKey |> List.filter reachesSelf |> Set.ofList

        let forwardOf (t: 'Node) =
            let nodes = Tree.preorder nodew t
            let index = nodes |> List.mapi (fun i n -> nodeKey n, i) |> Map.ofList

            [ for n in nodes do
                  let below = Tree.ids nodew n |> List.map key |> Set.ofList

                  for r in refw.RefsOf n do
                      for d in declarersOf t r do
                          if index.[nodeKey d] > index.[nodeKey n] && not (below.Contains(nodeKey d)) then
                              yield nodeKey n, key r, nodeKey d ]

        let checkDefects (at: string -> string) (t: 'Node) =
            let reported = Validator.referenceDefects refw idw nodew t

            let reportedDangling =
                reported
                |> List.choose (function
                    | Validator.ReferenceDefect.DanglingReference(f, r) -> Some(key f, key r)
                    | _ -> None)

            let reportedUnused =
                reported
                |> List.choose (function
                    | Validator.ReferenceDefect.UnusedDeclaration(d, x) -> Some(key d, key x)
                    | _ -> None)

            let cycles =
                reported
                |> List.choose (function
                    | Validator.ReferenceDefect.ReferenceCycle c -> Some(c |> List.map key)
                    | _ -> None)

            exact.Check(
                reportedDangling = dangling t
                && reportedUnused = unused t
                && List.forall (List.isEmpty >> not) cycles
                && Set.ofList (List.concat cycles) = onCycle t,
                fun () -> at (sprintf "referenceDefects answered %A" reported)
            )

            let forward = Validator.forwardReferences refw idw nodew t

            forwardExact.Check(
                (forward
                 |> List.choose (function
                     | Validator.ReferenceDefect.ForwardReference(f, r, d) -> Some(key f, key r, key d)
                     | _ -> None)) = forwardOf t
                && List.length forward = List.length (forwardOf t),
                fun () -> at (sprintf "forwardReferences answered %A" forward)
            )

            let integrity = (Validator.referenceIntegrity refw idw).Run nodew t
            let order = (Validator.referenceOrder refw idw).Run nodew t

            families.Check(
                List.length integrity = List.length reported
                && List.length order = List.length forward
                && order |> List.forall (fun d -> d.Code = Validator.ForwardReferenceCode)
                && List.forall2
                    (fun (d: Defect<'Id>) r ->
                        d.Code = (match r with
                                  | Validator.ReferenceDefect.DanglingReference _ -> Validator.DanglingReferenceCode
                                  | Validator.ReferenceDefect.UnusedDeclaration _ -> Validator.UnusedDeclarationCode
                                  | Validator.ReferenceDefect.ForwardReference _ -> Validator.ForwardReferenceCode
                                  | Validator.ReferenceDefect.ReferenceCycle _ -> Validator.ReferenceCycleCode))
                    integrity
                    reported,
                fun () -> at "the reference families disagree with the defects they report"
            )

        let declaredKeys (t: 'Node) =
            Tree.preorder nodew t |> List.collect refw.DeclsOf |> List.map key |> Set.ofList

        // The naive specification of one op, step by step as a batch is threaded: the container-aware
        // engine decides, and an accepted step after which a node refers to an id the tree declared
        // before the step and no longer declares is `StillReferenced`, naming those nodes in preorder.
        // Only a remove or a rewrite can do that; an insert, a move or a reorder that did would be a
        // defect the spec names rather than a refusal it expects.
        let rec expectedOf (op: SkeletonOp<'Node, 'Id>) (t: 'Node) : Result<'Node, Rejection<'Id>> =
            match op with
            | Batch ops -> ops |> List.fold (fun acc o -> acc |> Result.bind (expectedOf o)) (Ok t)
            | _ ->
                match Ops.applyContained canHold nodew idw op t with
                | Error e -> Error e
                | Ok t1 ->
                    let lost = Set.difference (declaredKeys t) (declaredKeys t1)

                    let referrers =
                        Tree.preorder nodew t1
                        |> List.filter (fun n -> refw.RefsOf n |> List.exists (fun r -> lost.Contains(key r)))
                        |> List.map nodew.Id

                    match referrers, op with
                    | [], _ -> Ok t1
                    | _, RemoveNode target -> Error(StillReferenced(target, referrers))
                    | _, UpdateNode node -> Error(StillReferenced(nodew.Id node, referrers))
                    | _ -> Error(Rejected("spec", "an insert, a move or a reorder orphaned a declaration"))

        let rec written (op: SkeletonOp<'Node, 'Id>) =
            match op with
            | InsertChild(_, node) -> Tree.preorder nodew node |> List.collect refw.RefsOf
            | UpdateNode node -> refw.RefsOf node
            | Batch inner -> inner |> List.collect written
            | _ -> []

        LawKit.run iterations seed (fun rng _ at ->
            let mutable cur = rng.Draw gen.Tree
            checkDefects at cur

            for _ in 1..6 do
                let op = rng.Draw(LawKit.genOp nodew idw gen cur)
                let g = Ops.applyReferenced refw none canHold nodew idw op cur

                let fp = Ops.footprint nodew idw [ op ]
                let fr = Ops.footprintReferenced refw nodew idw [ op ]

                footprintReads.Check(
                    Set.isSubset fp.Reads fr.Reads
                    && fp.StructureWrites = fr.StructureWrites
                    && fp.ContentWrites = fr.ContentWrites
                    && fp.UnknownParentWrites = fr.UnknownParentWrites
                    && fp.SlotReads = fr.SlotReads
                    && fp.SlotWrites = fr.SlotWrites
                    && (written op |> List.forall (fun r -> fr.Reads.Contains(key r))),
                    fun () -> at (sprintf "footprintReferenced of %A is %A against footprint %A" op fr fp)
                )

                let expected = expectedOf op cur

                agreement.Check(
                    (g = expected),
                    fun () -> at (sprintf "op %A: expected %A, applyReferenced answered %A" op expected g)
                )

                match g with
                | Ok t' ->
                    match op with
                    | RemoveNode _ -> removes <- removes + 1
                    | _ -> ()

                    let declaredBefore = declaredKeys cur

                    preservation.Check(
                        dangling t' |> List.forall (fun (_, r) -> not (declaredBefore.Contains r)),
                        fun () -> at (sprintf "the accepted %A left a resolved reference dangling" op)
                    )
                | Error(StillReferenced _) -> stillReferenced <- stillReferenced + 1
                | Error _ -> ()

                match g with
                | Ok t' -> cur <- t'
                | Error _ -> ()

            checkDefects at cur

            // the race, built from the tree: a referrer of a declaration its declarer's id names
            let candidates =
                [ for n in Tree.preorder nodew cur do
                      for r in refw.RefsOf n do
                          for d in declarersOf cur r do
                              if nodeKey d = key r && not (idw.Equals (nodew.Id d) (nodew.Id cur)) then
                                  yield n, r, d ]

            match candidates with
            | (n, r, d) :: _ ->
                races <- races + 1

                let a = Ops.footprintReferenced refw nodew idw [ UpdateNode n ]
                let b = Ops.footprintReferenced refw nodew idw [ RemoveNode(nodew.Id d) ]

                race.Check(
                    not (Ops.independent a b)
                    && Ops.interference a b
                       |> List.exists (function
                           | Interference.RightWritesLeftReads s -> s.Contains(key r)
                           | _ -> false),
                    fun () ->
                        at (
                            sprintf "the update of %s and the removal of %s do not interfere on it" (nodeKey n) (key r)
                        )
                )
            | [] -> ())

        LawKit.results [ exact; forwardExact; families; agreement; preservation; footprintReads; race ]
        @ [ SampleAdequacy.reached
                "Conformance.referenceLaws"
                "reference arm"
                seed
                [ "accepted remove", removes
                  "StillReferenced", stillReferenced
                  "race built", races ] ]

    // ---- Phase 314: digests, the change classification and the defect-set gate ----

    /// The pairs one iteration of the Phase 314 families reads: `before` with an `after` derived by
    /// up to four drawn ops (the `diffLaws` shape — a pair one of which was derived from the other),
    /// and, when the generator's second draw shares `before`'s root key and is well-formed, that
    /// INDEPENDENT draw as well — the pair shape that reaches a changed kind, a removed region and a
    /// moved survivor at once, which a four-op derivation rarely does.
    let private pairsOf
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (rng: LawKit.Draws)
        : ('Node * 'Node) list =
        let canHold = LawKit.canHoldOf gen
        let before = rng.Draw gen.Tree
        let mutable after = before

        for _ in 1..4 do
            let op = rng.Draw(LawKit.genOp nodew idw gen after)

            match Ops.applyContained canHold nodew idw op after with
            | Ok t' -> after <- t'
            | Error _ -> ()

        let other = rng.Draw gen.Tree
        let key (n: 'Node) = idw.ToString(nodew.Id n)

        let independent =
            if key other = key before && Tree.isWellFormed nodew idw other then
                [ before, other ]
            else
                []

        (before, after) :: independent

    /// The canonical rank of a change kind — `ChangeKind`'s declaration order, re-derived here so the
    /// law reads the order off the type rather than off the function under test.
    let private changeRank (k: Diff.ChangeKind<'Id>) : int =
        match k with
        | Diff.ChangeKind.Added -> 0
        | Diff.ChangeKind.Removed -> 1
        | Diff.ChangeKind.Moved _ -> 2
        | Diff.ChangeKind.KindChanged _ -> 3
        | Diff.ChangeKind.Changed -> 4
        | Diff.ChangeKind.Reordered -> 5

    /// True when `xs` ascends strictly under `compare`.
    let private ascending (xs: 'a list) : bool =
        xs |> List.pairwise |> List.forall (fun (x, y) -> compare x y < 0)

    /// The digest-map laws (Phase 314) — certify `Tree.digests` and `Tree.Digests.diff` against a
    /// domain's witness and content encoder. Over each pair `pairsOf` draws: the `Own` and `Frame`
    /// maps agree with the per-node `Tree.ownDigest` / `Tree.frameDigest` at every node; `diff d d`
    /// reads every key `Unchanged`; the four lists of `diff a b` PARTITION the union of the two key
    /// sets (each key exactly once, each list ascending — the sampled form of `digest_partition` in
    /// `proofs/TreeDiff.fst`); a common key is `Changed` exactly when the two nodes' kind or encoded
    /// shell differ; two subtree digests agree exactly when `Diff.changes` over the two subtrees is
    /// empty (the sampled form of `subtree_digest_injective`, both directions); and the `Added`,
    /// `Removed` and `Changed` lists are the ids `Diff.changes` names `Added`, `Removed` and
    /// `KindChanged`-or-`Changed`. `'Node` needs equality. Guarded on the pair shape: a run whose
    /// pairs were all identities, or never reached an added, a removed or an updated id, certified
    /// the partition over nothing but `Unchanged`.
    let digestLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (encode: 'Node -> string)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let key (n: 'Node) = idw.ToString(nodew.Id n)
        let shell (n: 'Node) = encode (nodew.ReplaceChildren n [])

        let keysOf (d: Tree.Digests) =
            d.Own |> Map.toList |> List.map fst |> Set.ofList

        let perNode =
            LawKit.LawCell "digests: the Own and Frame maps agree with ownDigest / frameDigest at every node"

        let identity =
            LawKit.LawCell "digests: diff d d reads every key Unchanged and nothing else"

        let partition =
            LawKit.LawCell
                "digests: added ⊎ removed ⊎ changed ⊎ unchanged is the union of the two key sets, each key once, each list ascending"

        let changedIsOwn =
            LawKit.LawCell "digests: a common key is Changed iff the two nodes' kind or encoded shell differ"

        let merkle =
            LawKit.LawCell "digests: two subtree digests agree iff Diff.changes over the two subtrees is empty"

        let agreesWithChanges =
            LawKit.LawCell
                "digests: Added, Removed and Changed are the ids Diff.changes names Added, Removed and KindChanged-or-Changed"

        let mutable nonIdentity = 0
        let mutable added = 0
        let mutable removed = 0
        let mutable updated = 0

        LawKit.run iterations seed (fun rng _ at ->
            for before, after in pairsOf nodew idw gen rng do
                if after <> before then
                    nonIdentity <- nonIdentity + 1

                let da = Tree.digests nodew idw encode before
                let db = Tree.digests nodew idw encode after

                for n in Tree.preorder nodew before do
                    let k = key n

                    perNode.Check(
                        Map.tryFind k da.Own = Some(Tree.ownDigest nodew idw encode n)
                        && Map.tryFind k da.Frame = Some(Tree.frameDigest nodew idw encode n),
                        fun () -> at (sprintf "the maps disagree with the per-node digests at %s" k)
                    )

                let dd = Tree.Digests.diff da da

                identity.Check(
                    List.isEmpty dd.Added
                    && List.isEmpty dd.Removed
                    && List.isEmpty dd.Changed
                    && Set.ofList dd.Unchanged = keysOf da
                    && List.length dd.Unchanged = Set.count (keysOf da),
                    fun () -> at (sprintf "diff d d is not all Unchanged: %A" dd)
                )

                let delta = Tree.Digests.diff da db
                let all = delta.Added @ delta.Removed @ delta.Changed @ delta.Unchanged
                let union = Set.union (keysOf da) (keysOf db)
                added <- added + List.length delta.Added
                removed <- removed + List.length delta.Removed
                updated <- updated + List.length delta.Changed

                partition.Check(
                    List.length all = Set.count union
                    && Set.ofList all = union
                    && ascending delta.Added
                    && ascending delta.Removed
                    && ascending delta.Changed
                    && ascending delta.Unchanged,
                    fun () -> at (sprintf "the four lists do not partition the key union: %A" delta)
                )

                match Tree.Index.tryBuild nodew idw before, Tree.Index.tryBuild nodew idw after with
                | Ok bix, Ok aix ->
                    for k in Set.intersect (keysOf da) (keysOf db) do
                        match Map.tryFind k bix.ById, Map.tryFind k aix.ById with
                        | Some b, Some a ->
                            let differs = nodew.KindTag b <> nodew.KindTag a || shell b <> shell a

                            changedIsOwn.Check(
                                (List.contains k delta.Changed = differs),
                                fun () -> at (sprintf "Changed disagrees with the nodes' own content at %s" k)
                            )

                            let below =
                                match Diff.changes encode nodew idw b a with
                                | Ok [] -> true
                                | _ -> false

                            merkle.Check(
                                (Tree.Digests.subtreeEqual da db k = below),
                                fun () -> at (sprintf "subtree digests and Diff.changes disagree below %s" k)
                            )
                        | _ -> ()

                    match Diff.changes encode nodew idw before after with
                    | Error e ->
                        agreesWithChanges.Check(
                            false,
                            fun () -> at (sprintf "Diff.changes refused a drawn pair: %A" e)
                        )
                    | Ok cs ->
                        let named (pick: Diff.ChangeKind<'Id> -> bool) =
                            cs
                            |> List.filter (fun c -> pick c.Kind)
                            |> List.map (fun c -> idw.ToString c.Id)
                            |> Set.ofList

                        agreesWithChanges.Check(
                            Set.ofList delta.Added = named ((=) Diff.ChangeKind.Added)
                            && Set.ofList delta.Removed = named ((=) Diff.ChangeKind.Removed)
                            && Set.ofList delta.Changed = named (function
                                | Diff.ChangeKind.KindChanged _
                                | Diff.ChangeKind.Changed -> true
                                | _ -> false),
                            fun () ->
                                at (sprintf "Digests.diff and Diff.changes name different ids: %A vs %A" delta cs)
                        )
                | _ -> ())

        LawKit.results [ perNode; identity; partition; changedIsOwn; merkle; agreesWithChanges ]
        @ [ SampleAdequacy.reached
                "Conformance.digestLaws"
                "pair shape"
                seed
                [ "non-identity pair", nonIdentity
                  "added id", added
                  "removed id", removed
                  "updated survivor", updated ] ]

    /// The change-classification laws (Phase 314) — hold `Diff.changes` to the script
    /// `Diff.toOpsWith` emits, over the pairs `pairsOf` draws. The identity pair classifies to `[]`;
    /// the entries ascend by `(id key, kind rank)` and name only ids of the two trees; the `Added` ids
    /// are exactly the `InsertChild` grafts; the `Moved` ids exactly the `MoveNode` targets, each from
    /// its before-parent to its after-parent; the `KindChanged` and `Changed` ids together exactly
    /// the `UpdateNode` targets (the encoder must see the kind for this to hold, as an injective one
    /// does — a red here over a domain's encoder is the encoder losing the kind); the `Removed` ids
    /// are `before`'s minus `after`'s, every `RemoveNode` target is one and every other sits below one
    /// in `before`; every `Reordered` id is a `ReorderChildren` parent and every such parent is
    /// `Reordered` or holds an `Added` or `Moved` child; and the script's landing classifies as
    /// unchanged against `after`. `'Node` needs equality. Guarded on the pair shape.
    let changeLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (encode: 'Node -> string)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let key (i: 'Id) = idw.ToString i
        let identity = LawKit.LawCell "changes: the identity pair classifies to []"

        let canonical =
            LawKit.LawCell "changes: entries ascend by (id key, kind rank) and name only ids of the two trees"

        let inserts =
            LawKit.LawCell "changes: Added ids are exactly the InsertChild grafts of toOpsWith"

        let moves =
            LawKit.LawCell
                "changes: Moved ids are exactly the MoveNode targets of toOpsWith, each from its before-parent to its after-parent"

        let updates =
            LawKit.LawCell
                "changes: KindChanged and Changed ids together are exactly the UpdateNode targets of toOpsWith"

        let removes =
            LawKit.LawCell
                "changes: Removed ids are before's minus after's; every RemoveNode target is one, and every other sits below one in before"

        let reorders =
            LawKit.LawCell
                "changes: every Reordered id is a ReorderChildren parent, and every such parent is Reordered or holds an Added or Moved child"

        let landing =
            LawKit.LawCell "changes: the script's landing classifies as unchanged against after"

        let mutable nonIdentity = 0
        let mutable added = 0
        let mutable removed = 0
        let mutable moved = 0
        let mutable updated = 0
        let mutable reordered = 0

        LawKit.run iterations seed (fun rng _ at ->
            for before, after in pairsOf nodew idw gen rng do
                if after <> before then
                    nonIdentity <- nonIdentity + 1

                identity.Check(
                    (Diff.changes encode nodew idw before before = Ok []),
                    fun () -> at "the identity pair does not classify to []"
                )

                match
                    Diff.changes encode nodew idw before after,
                    Diff.toOpsWith encode nodew idw before after,
                    Tree.Index.tryBuild nodew idw before,
                    Tree.Index.tryBuild nodew idw after
                with
                | Ok cs, Ok ops, Ok bix, Ok aix ->
                    let bIds = bix.ById |> Map.keys |> Set.ofSeq
                    let aIds = aix.ById |> Map.keys |> Set.ofSeq

                    let named (pick: Diff.ChangeKind<'Id> -> bool) =
                        cs
                        |> List.filter (fun c -> pick c.Kind)
                        |> List.map (fun c -> key c.Id)
                        |> Set.ofList

                    let addedIds = named ((=) Diff.ChangeKind.Added)
                    let removedIds = named ((=) Diff.ChangeKind.Removed)

                    let movedIds =
                        named (function
                            | Diff.ChangeKind.Moved _ -> true
                            | _ -> false)

                    let updatedIds =
                        named (function
                            | Diff.ChangeKind.KindChanged _
                            | Diff.ChangeKind.Changed -> true
                            | _ -> false)

                    let reorderedIds = named ((=) Diff.ChangeKind.Reordered)
                    added <- added + Set.count addedIds
                    removed <- removed + Set.count removedIds
                    moved <- moved + Set.count movedIds
                    updated <- updated + Set.count updatedIds
                    reordered <- reordered + Set.count reorderedIds

                    canonical.Check(
                        ascending (cs |> List.map (fun c -> key c.Id, changeRank c.Kind))
                        && cs |> List.forall (fun c -> Set.contains (key c.Id) (Set.union bIds aIds)),
                        fun () -> at (sprintf "entries are not canonical: %A" cs)
                    )

                    let grafts =
                        ops
                        |> List.choose (function
                            | InsertChild(_, n) -> Some(key (nodew.Id n))
                            | _ -> None)
                        |> Set.ofList

                    inserts.Check((addedIds = grafts), fun () -> at (sprintf "Added %A vs grafts %A" addedIds grafts))

                    let moveTargets =
                        ops
                        |> List.choose (function
                            | MoveNode(t, _) -> Some(key t)
                            | _ -> None)
                        |> Set.ofList

                    let movedWell =
                        cs
                        |> List.forall (fun c ->
                            match c.Kind with
                            | Diff.ChangeKind.Moved(f, t) ->
                                (Map.tryFind (key c.Id) bix.ParentOf |> Option.map key) = Some(key f)
                                && (Map.tryFind (key c.Id) aix.ParentOf |> Option.map key) = Some(key t)
                            | _ -> true)

                    moves.Check(
                        movedIds = moveTargets && movedWell,
                        fun () -> at (sprintf "Moved %A vs MoveNode targets %A" movedIds moveTargets)
                    )

                    let updateTargets =
                        ops
                        |> List.choose (function
                            | UpdateNode n -> Some(key (nodew.Id n))
                            | _ -> None)
                        |> Set.ofList

                    updates.Check(
                        (updatedIds = updateTargets),
                        fun () ->
                            at (sprintf "KindChanged and Changed %A vs UpdateNode targets %A" updatedIds updateTargets)
                    )

                    let removeTargets =
                        ops
                        |> List.choose (function
                            | RemoveNode t -> Some(key t)
                            | _ -> None)
                        |> Set.ofList

                    let belowARemoval (k: string) =
                        match Map.tryFind k bix.ById with
                        | Some n ->
                            Tree.Index.ancestors idw (nodew.Id n) bix
                            |> List.exists (fun p -> Set.contains (key (nodew.Id p)) removeTargets)
                        | None -> false

                    removes.Check(
                        removedIds = Set.difference bIds aIds
                        && Set.isSubset removeTargets removedIds
                        && removedIds
                           |> Set.forall (fun k -> Set.contains k removeTargets || belowARemoval k),
                        fun () -> at (sprintf "Removed %A vs RemoveNode targets %A" removedIds removeTargets)
                    )

                    let reorderParents =
                        ops
                        |> List.choose (function
                            | ReorderChildren(p, _) -> Some(key p)
                            | _ -> None)
                        |> Set.ofList

                    let explained (p: string) =
                        Set.contains p reorderedIds
                        || (match Map.tryFind p aix.ById with
                            | Some n ->
                                nodew.Children n
                                |> List.exists (fun c ->
                                    let ck = key (nodew.Id c)
                                    Set.contains ck addedIds || Set.contains ck movedIds)
                            | None -> false)

                    reorders.Check(
                        Set.isSubset reorderedIds reorderParents
                        && reorderParents |> Set.forall explained,
                        fun () -> at (sprintf "Reordered %A vs ReorderChildren parents %A" reorderedIds reorderParents)
                    )

                    match Ops.applyAll nodew idw ops before with
                    | Ok landed ->
                        landing.Check(
                            (Diff.changes encode nodew idw landed after = Ok []),
                            fun () -> at "the script's landing still classifies as changed against after"
                        )
                    | Error e -> landing.Check(false, fun () -> at (sprintf "the script does not apply: %A" e))
                | cs, ops, _, _ ->
                    canonical.Check(
                        false,
                        fun () -> at (sprintf "a drawn pair was refused: changes %A, toOpsWith %A" cs ops)
                    ))

        LawKit.results [ identity; canonical; inserts; moves; updates; removes; reorders; landing ]
        @ [ SampleAdequacy.reached
                "Conformance.changeLaws"
                "pair shape"
                seed
                [ "non-identity pair", nonIdentity
                  "added id", added
                  "removed id", removed
                  "moved survivor", moved
                  "updated survivor", updated
                  "reordered parent", reordered ] ]

    /// The introduced-defect laws (Phase 314) — certify `Validator.introduced`, `verdict`, `gate` and
    /// `encodeVerdict` against a domain's registry over the pairs `pairsOf` draws, `after` the
    /// candidate and `before` (and the independent draw, when admitted) the baselines. A tree
    /// introduces nothing against itself; the introduced list is exactly the candidate's findings
    /// whose `(code, node)` no baseline reports, in canonical order; `Lenient` reports nothing and
    /// never blocks, `Diagnostic` reports and never blocks, `Gated` blocks exactly on an introduced
    /// error; `gate` is `verdict` over `introduced` under every policy; and `encodeVerdict` is
    /// deterministic, reads the introduced list as a set (a permutation encodes alike) and separates
    /// the three policies. Guarded on the gate arm: a registry the drawn edits never trip certifies
    /// the diff over empty sets.
    let introducedLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (reg: Validator.RuleRegistry<'Node, 'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let identityOf (d: Defect<'Id>) =
            d.Code, d.Node |> Option.map idw.ToString

        let self = LawKit.LawCell "introduced: a tree introduces nothing against itself"

        let exact =
            LawKit.LawCell
                "introduced: exactly the candidate findings whose (code, node) no baseline reports, in canonical order"

        let policies =
            LawKit.LawCell
                "introduced: Lenient reports nothing and never blocks, Diagnostic reports and never blocks, Gated blocks exactly on an introduced error"

        let composed =
            LawKit.LawCell "introduced: gate is verdict over introduced under every policy"

        let encoding =
            LawKit.LawCell
                "introduced: encodeVerdict is deterministic, order-free over the introduced list and separates the policies"

        let mutable introducedN = 0
        let mutable blockedN = 0

        LawKit.run iterations seed (fun rng _ at ->
            let pairs = pairsOf nodew idw gen rng
            let before = fst (List.head pairs)
            let candidate = snd (List.head pairs)
            let baselines = before :: (pairs |> List.tail |> List.map snd)

            self.Check(
                List.isEmpty (Validator.introduced nodew idw reg [ candidate ] candidate),
                fun () -> at "a tree introduces a defect against itself"
            )

            let introduced = Validator.introduced nodew idw reg baselines candidate
            let found = Validator.runAll nodew reg candidate

            let known =
                baselines
                |> List.collect (Validator.runAll nodew reg >> List.map identityOf)
                |> Set.ofList

            let expected =
                found
                |> List.filter (fun d -> not (Set.contains (identityOf d) known))
                |> List.sortBy identityOf

            if not (List.isEmpty introduced) then
                introducedN <- introducedN + 1

            exact.Check(
                (introduced = expected),
                fun () -> at (sprintf "introduced %A, expected %A" introduced expected)
            )

            let lenient = Validator.verdict Validator.GatePolicy.Lenient introduced
            let diagnostic = Validator.verdict Validator.GatePolicy.Diagnostic introduced
            let gated = Validator.verdict Validator.GatePolicy.Gated introduced

            if gated.Blocked then
                blockedN <- blockedN + 1

            policies.Check(
                List.isEmpty lenient.Introduced
                && not lenient.Blocked
                && diagnostic.Introduced = introduced
                && not diagnostic.Blocked
                && gated.Introduced = introduced
                && gated.Blocked = Validator.hasErrors introduced,
                fun () -> at (sprintf "the policies misread %A" introduced)
            )

            composed.Check(
                [ Validator.GatePolicy.Lenient
                  Validator.GatePolicy.Diagnostic
                  Validator.GatePolicy.Gated ]
                |> List.forall (fun p ->
                    Validator.gate p nodew idw reg baselines candidate = Validator.verdict p introduced),
                fun () -> at "gate and verdict over introduced disagree"
            )

            let enc = Validator.encodeVerdict idw

            encoding.Check(
                enc gated = enc gated
                && enc
                    { gated with
                        Introduced = List.rev introduced } = enc gated
                && enc lenient <> enc diagnostic
                && enc diagnostic <> enc gated
                && enc lenient <> enc gated,
                fun () -> at "encodeVerdict is not canonical over the verdicts"
            ))

        LawKit.results [ self; exact; policies; composed; encoding ]
        @ [ SampleAdequacy.reached
                "Conformance.introducedLaws"
                "gate arm"
                seed
                [ "introduced defect", introducedN; "blocked verdict", blockedN ] ]
