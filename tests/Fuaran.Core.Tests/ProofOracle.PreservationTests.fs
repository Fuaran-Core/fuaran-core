module Fuaran.Core.Tests.ProofOracle.PreservationTests

// The `Proofs.Oracle` differential for `proofs/Preservation.fst`: the extracted model
// (`proofs/oracle/Preservation.fs`) run beside production.
// The bridges more than one model reads are in `ProofOracle.Bridges.fs`.

open System.IO
open Expecto

open Fuaran.Core
open Fuaran.Core.Tests
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.FoldFixtures
open Fuaran.Core.Tests.ProofOracle.Bridges

/// A refusal's render pair: production's rejection and the model's, each as its class.
let private rejectionClass: Differential<Rejection<string>, TreeOps.rejection> =
    Differential.pair prodRejClass modelRejClass

/// A single op's render pair — the two renderers `renderProdOp` / `renderModelOp`.
let private skeletonOp: Differential<SkeletonOp<RNode, string>, TreeOps.op> =
    Differential.pair renderProdOp renderModelOp

/// A dry run's render pair: "ok", or the class of the refusal.
let private canVerdict: Differential<Result<unit, Rejection<string>>, DagFold.outcome<unit, TreeOps.rejection>> =
    Differential.pair
        (function
        | Ok() -> "ok"
        | Error e -> prodRejClass e)
        (function
         | DagFold.Ok() -> "ok"
         | DagFold.Error e -> modelRejClass e)

/// The accepted arm of an apply comparison. Phase 302 — compared STRUCTURALLY; the digests stay in
/// the message only.
let acceptedAgreement (where: string) (pt: RNode) (mt: TreeOps.tree) (t: Tally) : Tally =
    Differential.check
        (prodShape pt = modelShape mt)
        (fun () ->
            sprintf
                "accepted result differs — %s\n  production: %s\n  oracle:     %s"
                where
                (prodTreeHash pt)
                (modelTreeHash mt))
        t

/// The refused arm of an apply comparison: the two refusals agree on their class.
let rejectionAgreement (where: string) (pe: Rejection<string>) (me: TreeOps.rejection) (t: Tally) : Tally =
    Differential.compareWith
        rejectionClass
        (fun pc mc -> sprintf "rejection class differs — %s\n  production: %s\n  oracle:     %s" where pc mc)
        pe
        me
        t

/// The four-arm apply comparison every per-op differential reads: the accepted results agree
/// structurally, the refusals agree on their class, and a verdict that differs is named by which
/// side accepted. The go-red cases read these messages, so they are fixed here once.
let applyAgreement
    (where: string)
    (prod: Result<RNode, Rejection<string>>)
    (model: DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (t: Tally)
    : Tally =
    match prod, model with
    | Ok pt, DagFold.Ok mt -> acceptedAgreement where pt mt t
    | Error pe, DagFold.Error me -> rejectionAgreement where pe me t
    | Ok _, DagFold.Error me ->
        Tally.disagree (sprintf "production ACCEPTED but the oracle rejected (%s) — %s" (modelRejClass me) where) t
    | Error pe, DagFold.Ok _ ->
        Tally.disagree (sprintf "production REJECTED (%s) but the oracle accepted — %s" (prodRejClass pe) where) t

/// The preservation tally: grafts the tree accepted, refused, and the three disputed classes,
/// counted separately so a run that reached none of them cannot report agreement about them.
let private presTally =
    Tally.empty [ "Accepted"; "Rejected"; "Collided"; "Duplicated"; "Inverted" ]
    |> Tally.sets [ "Classes" ]


/// The disputed grafts, minted from the state's OWN ids so the collision is real rather than
/// hoped for. `existing` is the state's id list; `n` seeds the fresh names.
let private disputedInserts (existing: string list) (parent: string) (n: int) : SkeletonOp<RNode, string> list =
    let tag s = sprintf "p138-%s-%d" s n
    let victim = existing |> List.tryLast |> Option.defaultValue parent
    let root = List.head existing

    [
      // a DESCENDANT the tree already holds — invisible to the pre-137 check, which read the
      // graft's own id alone.
      InsertChild(parent, RNode.node (tag "shell") "section" [ RNode.leaf victim "para" "v" ])
      // the same, one level deeper, and naming the ROOT — the shape TreeOps' own counterexample uses
      InsertChild(
          parent,
          RNode.node (tag "outer") "section" [ RNode.node (tag "inner") "section" [ RNode.leaf root "para" "v" ] ]
      )
      // an id repeated WITHIN the graft, with nothing in common with the tree: the half no host
      // outside Core refuses at all (the TS, Go and Rust engines seed their comparison from the
      // root alone), and the half the pre-137 check could not see either.
      InsertChild(
          parent,
          RNode.node (tag "twins") "section" [ RNode.leaf (tag "twin") "para" "a"; RNode.leaf (tag "twin") "para" "b" ]
      )
      // both at once, so precedence is exercised: the FIRST offender in `Tree.ids` order is named
      InsertChild(
          parent,
          RNode.node (tag "both") "section" [ RNode.leaf victim "para" "a"; RNode.leaf (tag "both") "para" "b" ]
      )
      // and a clean multi-node graft, so the ACCEPT path is exercised with a subtree rather than a
      // leaf — otherwise a validator that refused every non-leaf graft would pass this family
      InsertChild(
          parent,
          RNode.node
              (tag "clean")
              "section"
              [ RNode.leaf (tag "clean-a") "para" "a"; RNode.leaf (tag "clean-b") "para" "b" ]
      ) ]

/// One (op, state) asked of both sides, across all four comparisons. `modelApply` is the model's
/// apply arm — the real one in the green run, `apply_pre137` in the go-red.
let private presProbe
    (modelApply: TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (op: SkeletonOp<RNode, string>)
    (st: RNode)
    (t: Tally)
    : Tally =
    let mop = toModelOpWith toModelTree op
    let mst = toModelTree st
    let where = sprintf "op %s at tree %s" (renderProdOp op) (prodTreeHash st)

    let prod = Ops.apply Policy.none (OpScope.plain nodew idw) op st
    let model = modelApply mop mst
    let prodCan = Ops.canApply Policy.none (OpScope.plain nodew idw) op st
    let modelCan = Preservation.can_apply mop mst

    // 1. apply
    let t =
        let t = applyAgreement where prod model t

        match prod, model with
        | Ok _, DagFold.Ok _ -> Tally.bump "Accepted" t
        | Error pe, DagFold.Error _ -> t |> Tally.bump "Rejected" |> Tally.reach "Classes" (prodRejClass pe)
        | _ -> t

    // 2. canApply, and 3. canApply-vs-apply on each side
    let t =
        let mcOk =
            match modelCan with
            | DagFold.Ok() -> true
            | _ -> false

        let maOk =
            match model with
            | DagFold.Ok _ -> true
            | _ -> false

        t
        |> Differential.compareWith
            canVerdict
            (fun pv mv -> sprintf "canApply differs — %s\n  production: %s\n  oracle:     %s" where pv mv)
            prodCan
            modelCan
        |> Differential.check (Result.isOk prodCan = Result.isOk prod) (fun () ->
            sprintf "production's canApply and apply DISAGREE — %s (canApply %s)" where (canVerdict.Production prodCan))
        |> Differential.check (mcOk = maOk) (fun () ->
            sprintf "the oracle's can_apply and apply DISAGREE — %s (can_apply %s)" where (canVerdict.Model modelCan))

    // 4. invert, on an accepted leaf operation
    let isLeaf =
        match op with
        | Batch _ -> false
        | _ -> true

    let t =
        match prod, model with
        | Ok pt, DagFold.Ok mt when isLeaf ->
            let pInv = Ops.invert nodew idw op st
            let mInv = Preservation.invert_leaf mop mst

            let names (t: Tally) =
                match pInv, mInv with
                | Ok pi, DagFold.Ok mi ->
                    Differential.compareWith
                        skeletonOp
                        (fun pr mr ->
                            sprintf "the derived INVERSE differs — %s\n  production: %s\n  oracle:     %s" where pr mr)
                        pi
                        mi
                        t
                | Error pe, DagFold.Error me ->
                    Differential.compareWith
                        rejectionClass
                        (fun pc mc ->
                            sprintf "invert's rejection class differs — %s\n  production: %s\n  oracle: %s" where pc mc)
                        pe
                        me
                        t
                | Ok _, DagFold.Error _
                | Error _, DagFold.Ok _ -> Tally.disagree (sprintf "invert's verdict differs — %s" where) t

            // the theorem instantiated on the shipped code: undoing an accepted step restores the
            // tree it was applied to, compared through the content hash rather than by equality of
            // a record the model does not model.
            let roundTrip (t: Tally) =
                match pInv with
                | Ok pi ->
                    (match Ops.apply Policy.none (OpScope.plain nodew idw) pi pt with
                     | Ok back ->
                         Differential.check
                             (prodTreeHash back = prodTreeHash st)
                             (fun () ->
                                 sprintf
                                     "the inverse did NOT restore the input — %s\n  before: %s\n  after:  %s"
                                     where
                                     (prodTreeHash st)
                                     (prodTreeHash back))
                             t
                     | Error e ->
                         Tally.disagree
                             (sprintf "the inverse was REJECTED at the result (%s) — %s" (prodRejClass e) where)
                             t)
                | Error _ -> t

            let mRoundTrip (t: Tally) =
                match mInv with
                | DagFold.Ok mi ->
                    (match TreeOps.apply mi mt with
                     | DagFold.Ok back ->
                         Differential.check
                             (modelTreeHash back = modelTreeHash mst)
                             (fun () -> sprintf "the ORACLE's inverse did not restore the input — %s" where)
                             t
                     | DagFold.Error e ->
                         Tally.disagree
                             (sprintf
                                 "the oracle's inverse was rejected at the result (%s) — %s"
                                 (modelRejClass e)
                                 where)
                             t)
                | DagFold.Error _ -> t

            t |> names |> roundTrip |> mRoundTrip |> Tally.bump "Inverted"
        | _ -> t

    // sample adequacy: which DISPUTED shapes this probe actually reached, judged by re-reading the
    // graft through `Tree.ids` rather than by trusting the construction above.
    match op with
    | InsertChild(_, node) ->
        let sub = Tree.ids nodew node
        let treeIds = Tree.ids nodew st |> Set.ofList
        let hits = sub |> List.filter treeIds.Contains |> List.length
        let internalDup = List.length sub <> List.length (List.distinct sub)
        t |> Tally.bumpIf (hits > 0) "Collided" |> Tally.bumpIf internalDup "Duplicated"
    | _ -> t

/// Every op the generator yields, the Phase 133 refusals, AND the disputed grafts minted per state,
/// asked at every state a prefix of the generated pool reaches.
///
/// This is the GENERATED source. Phase 139 landed the second one — the shared corpus's `apply/`
/// family, run by `presCorpusDifferential` below — and the two answer different questions rather
/// than one twice: this pool is wide and nobody chose it, and the corpus pool is narrow and
/// deliberately covers one clause per op, so a shape the generator never draws is still asked.
let private presDifferential
    (modelApply: TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (seed: int)
    (trials: int)
    : Tally =
    // the generator state, and `n` — the per-STATE counter the disputed grafts' names are minted from
    let step _ (r: ConfRng.T, n: int) (t: Tally) =
        let lanes, r' = treeLaneGen.Lanes 3 r
        let generated = List.concat lanes

        let states =
            generated
            |> List.fold
                (fun (acc, cur) op ->
                    match Ops.apply Policy.none (OpScope.plain nodew idw) op cur with
                    | Ok t -> (acc @ [ t ]), t
                    | Error _ -> acc, cur)
                ([ treeBase ], treeBase)
            |> fst

        // Phase 309 — updates and batches by generation, as the tree differential draws them.
        let drawn = drawnUpdatesAndBatches states generated

        let n', t' =
            states
            |> List.fold
                (fun (n, t) st ->
                    let n = n + 1
                    let existing = Tree.ids nodew st
                    let parents = existing |> List.truncate 3

                    let disputed = parents |> List.collect (fun p -> disputedInserts existing p n)

                    n,
                    Differential.over
                        (generated @ drawn @ treeRefusals @ disputed)
                        (fun op -> presProbe modelApply op st)
                        t)
                (n, t)

        (r', n'), t'

    Differential.seeded trials (ConfRng.ofSeed seed, 0) step presTally


/// "every node with children satisfies `canHold`", written directly over the witness — the model's
/// `contained` asked of a production tree. Written out rather than routed through the extracted
/// model, for the reason the two op renderers are written out: one function over both sides could
/// not tell a divergence from its own convention.
let rec private prodContained (canHold: RNode -> bool) (n: RNode) : bool =
    (match nodew.Children n with
     | [] -> true
     | _ -> canHold n)
    && nodew.Children n |> List.forall (prodContained canHold)

/// The invariant's second hypothesis, on the operation's own trees.
let rec private prodContainedOp (canHold: RNode -> bool) (op: SkeletonOp<RNode, string>) : bool =
    match op with
    | InsertChild(_, node) -> prodContained canHold node
    | Batch inner -> inner |> List.forall (prodContainedOp canHold)
    | _ -> true

/// The container tally. `NotAContainer` refusals are counted per SITE — the go-red is about the move
/// half, so a run that reached only the insert half could not have caught it — and (Phase 161)
/// `GraftRefusals` counts refusals raised by the GRAFT walk rather than by either parent check: a
/// fourth capability site, and the one a pool of leaf-only inserts can never reach. `Preempted`
/// counts probes where the capability PRE-EMPTED another refusal — the shape the first run of this
/// differential turned up, and the one an `adds a refusal` reading gets wrong — and `Preserved` the
/// probes where the invariant's hypotheses were MET, and the conclusion therefore asserted.
let private contTally =
    Tally.empty
        [ "Accepted"
          "Refused"
          "InsertRefusals"
          "MoveRefusals"
          "BatchRefusals"
          "GraftRefusals"
          "Preempted"
          "Preserved" ]
    |> Tally.sets [ "Predicates"; "Classes" ]

let private contProbe
    (modelApply:
        (TreeOps.tree -> bool) -> TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (kinds: Set<string>)
    (op: SkeletonOp<RNode, string>)
    (st: RNode)
    (t: Tally)
    : Tally =
    let canHold (n: RNode) = kinds.Contains(nodew.KindTag n)
    let mCanHold (t: TreeOps.tree) = kinds.Contains(mKind t)
    let mop = toModelOpWith toModelTree op
    let mst = toModelTree st

    let where =
        sprintf "op %s at tree %s under canHold={%s}" (renderProdOp op) (prodTreeHash st) (showKinds kinds)

    let prod = Ops.apply Policy.none (OpScope.contained canHold nodew idw) op st
    let model = modelApply mCanHold mop mst
    let prodCan = Ops.canApply Policy.none (OpScope.contained canHold nodew idw) op st
    let modelCan = Preservation.can_apply_contained mCanHold mop mst
    let plain = Ops.apply Policy.none (OpScope.plain nodew idw) op st

    // The refusal is COUNTED even where the two sides disagree about it, unlike the Phase 138
    // family's corresponding arm. That is what lets the go-red's adequacy guard say which
    // capability SITE it reached: in the go-red the model accepts every move, so a move
    // refusal exists only on production's side of a divergence, and a probe that did not
    // count it would report the run as having reached no move at all.
    let cls =
        match prod with
        | Error pe -> Some(prodRejClass pe)
        | Ok _ -> None

    // 1. applyContained
    let t =
        let t = applyAgreement where prod model t

        match prod, model, cls with
        | Ok _, DagFold.Ok _, _ -> Tally.bump "Accepted" t
        | Error _, _, Some c -> t |> Tally.bump "Refused" |> Tally.reach "Classes" c
        | _ -> t

    // 2. canApplyContained, and the dry run against the mutating call on each side
    let t =
        t
        |> Differential.compareWith
            canVerdict
            (fun pv mv -> sprintf "canApplyContained differs — %s\n  production: %s\n  oracle:     %s" where pv mv)
            prodCan
            modelCan
        |> Differential.check (Result.isOk prodCan = Result.isOk prod) (fun () ->
            sprintf
                "production's canApplyContained and applyContained DISAGREE — %s (check %s)"
                where
                (canVerdict.Production prodCan))

    // 3. the DIFFERENCE, on production: the two engines agree EXCEPT where the container-aware one
    //    raises `NotAContainer` — `apply_contained_diff` asked of the shipped code rather than of
    //    the model. This is the half that says `Ops.apply` under `OpScope.contained` is `apply` plus a guard rather
    //    than a second engine.
    //
    //    Note the shape of the exception, which the first run of this differential is what taught:
    //    the capability check can PRE-EMPT another refusal rather than only adding one. The
    //    `MoveNode` arm tests `canHold` on the new parent (Ops.fs:249) BEFORE the descendant test
    //    (Ops.fs:258), so a self-move into a leaf is `NotAContainer` here and `WouldNestUnderSelf`
    //    under `apply` — two engines refusing the same operation with different classes, which is
    //    correct and is why the theorem is stated as "identical, or `NotAContainer`" rather than
    //    as "the same class unless the capability adds one".
    let t =
        match plain, prod with
        | Ok a, Ok b ->
            Differential.check
                (prodTreeHash a = prodTreeHash b)
                (fun () -> sprintf "apply and applyContained both accepted but produced DIFFERENT trees — %s" where)
                t
        | Ok _, Error e ->
            Differential.check
                (prodRejClass e = "NotAContainer")
                (fun () ->
                    sprintf
                        "applyContained refused what apply accepted, and NOT as NotAContainer (%s) — %s"
                        (prodRejClass e)
                        where)
                t
        | Error a, Error b ->
            Differential.check
                (not (prodRejClass b <> "NotAContainer" && prodRejClass a <> prodRejClass b))
                (fun () ->
                    sprintf
                        "apply and applyContained refused DIFFERENTLY, and not by the capability — %s\n  apply:           %s\n  applyContained: %s"
                        where
                        (prodRejClass a)
                        (prodRejClass b))
                t
        | Error a, Ok _ ->
            Tally.disagree (sprintf "applyContained ACCEPTED what apply refused (%s) — %s" (prodRejClass a) where) t

    // 4. where the refusal is a NotAContainer, it names a node that really holds children, reports
    //    that node's own kind tag, and the predicate really refuses it. Since Phase 161 there are
    //    TWO places the node can be, and which one is not a detail: a parent refusal names a node
    //    of the TREE, and a graft refusal names a node of the op's own inserted SUBTREE, which the
    //    duplicate-id scan guarantees is not in the tree at all. Looking only in the tree would
    //    report every graft refusal as a defect; looking in either without saying which would let a
    //    refusal that named nothing pass.
    let t =
        match prod with
        | Error(NotAContainer(target, kindTag)) ->
            let inTree = Tree.tryFind nodew idw target st

            let inGraft =
                match op with
                | InsertChild(_, graft) -> Tree.tryFind nodew idw target graft
                | _ -> None

            match inTree, inGraft with
            | None, None ->
                Tally.disagree
                    (sprintf "NotAContainer named %s, which neither the tree nor the graft holds — %s" target where)
                    t
            | _ ->
                let site, n =
                    match inTree with
                    | Some n -> "the tree", n
                    | None -> "the graft", Option.get inGraft

                t
                |> Differential.check (nodew.KindTag n = kindTag) (fun () ->
                    sprintf
                        "NotAContainer reported kind %s for %s in %s, whose kind is %s — %s"
                        kindTag
                        target
                        site
                        (nodew.KindTag n)
                        where)
                |> Differential.check (not (canHold n)) (fun () ->
                    sprintf "NotAContainer named %s in %s, which canHold ADMITS — %s" target site where)
                |> fun t ->
                    match inTree, inGraft with
                    | None, Some g when List.isEmpty (nodew.Children g) ->
                        // the graft walk refuses a node that HOLDS children; a childless one the
                        // predicate merely dislikes is not a violation of the invariant at all
                        Tally.disagree
                            (sprintf "the graft refusal named %s, which holds no children — %s" target where)
                            t
                    | _ -> t
        | _ -> t

    // 5. the invariant, on production, where its hypothesis is met.
    //
    // Phase 161 RETIRED the second hypothesis: `contained_op` used to gate this arm too, because
    // the engine never inspected a graft and an insert could therefore carry a violation in. The
    // engine refuses such a graft now (`nested_graft_refused`), so the only premise left is that
    // the input tree satisfies the invariant — which is why this arm asserts the conclusion on
    // strictly MORE probes than it did before the phase, including every graft probe below.
    let t =
        if prodContained canHold st then
            let t =
                match prod with
                | Ok result ->
                    Differential.check
                        (prodContained canHold result)
                        (fun () ->
                            sprintf
                                "the container invariant BROKE across an accepted operation — %s\n  a node with children fails canHold in the result"
                                where)
                        t
                | Error _ -> t

            Tally.bump "Preserved" t
        else
            t

    let preempted =
        match plain, prod with
        | Error a, Error b when prodRejClass b = "NotAContainer" && prodRejClass a <> "NotAContainer" -> true
        | _ -> false

    let site =
        match cls, op with
        | Some "NotAContainer", InsertChild _ -> Some "InsertRefusals"
        | Some "NotAContainer", MoveNode _ -> Some "MoveRefusals"
        | Some "NotAContainer", Batch _ -> Some "BatchRefusals"
        | _ -> None

    // Phase 161's site, told from the parent site by WHERE the named node is: a graft refusal names
    // a node the tree does not hold (the duplicate-id scan has already refused a graft sharing an
    // id with it), so the two are distinguishable from the envelope alone.
    let graftRef =
        match prod, op with
        | Error(NotAContainer(target, _)), InsertChild(_, graft) ->
            (match Tree.tryFind nodew idw target st, Tree.tryFind nodew idw target graft with
             | None, Some _ -> true
             | _ -> false)
        | _ -> false

    let t =
        match site with
        | Some s -> Tally.bump s t
        | None -> t

    t
    |> Tally.bumpIf graftRef "GraftRefusals"
    |> Tally.bumpIf preempted "Preempted"
    |> Tally.reach "Predicates" (showKinds kinds)

/// The generated pool, plus probes MINTED PER STATE that address the two capability sites
/// directly: an insert under every id, a move of every leaf under every id, and a batch whose
/// second step is a move into a leaf. The generated pool alone reaches the sites by luck — the
/// probes reach them by construction, which is what the Phase 138 family's own lesson says to do.
let private contDifferential
    (modelApply:
        (TreeOps.tree -> bool) -> TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (seed: int)
    (trials: int)
    : Tally =
    // the generator state, and `n` — the per-STATE counter the minted probes are named from
    let step _ (r: ConfRng.T, n: int) (t: Tally) =
        let lanes, r1 = treeLaneGen.Lanes 3 r
        let kinds, r2 = drawCanHold r1
        let generated = List.concat lanes

        let states =
            generated
            |> List.fold
                (fun (acc, cur) op ->
                    match Ops.apply Policy.none (OpScope.plain nodew idw) op cur with
                    | Ok t -> (acc @ [ t ]), t
                    | Error _ -> acc, cur)
                ([ treeBase ], treeBase)
            |> fst

        let probesAt (n: int) (st: RNode) =
            let ids = Tree.ids nodew st |> List.truncate 4

            let leaves =
                Tree.preorder nodew st
                |> List.filter (fun c -> List.isEmpty (nodew.Children c))
                |> List.map nodew.Id
                |> List.truncate 2

            let inserts =
                ids
                |> List.map (fun p -> InsertChild(p, RNode.leaf (sprintf "p140-%s-%d" p n) "para" "v"))

            // Phase 161's site, reached by CONSTRUCTION for the reason the file's header gives: the
            // pool above mints leaf grafts only, and a leaf graft can never carry an interior
            // offender, so the walk would have been certified by a sample that could not reach it.
            // Each graft's root holds a child, so whether it is an offender is exactly whether the
            // drawn predicate admits its kind — and both kinds are minted, so both verdicts arise
            // within one run rather than across lucky seeds.
            let grafts =
                ids
                |> List.collect (fun p ->
                    [ for kind in [ "para"; "section" ] ->
                          InsertChild(
                              p,
                              RNode.node
                                  (sprintf "p161-%s-%s-%d" kind p n)
                                  kind
                                  [ RNode.leaf (sprintf "p161i-%s-%s-%d" kind p n) "para" "v" ]
                          ) ])

            let moves =
                ids |> List.collect (fun p -> leaves |> List.map (fun l -> MoveNode(l, p)))

            let batches =
                match ids, leaves with
                | root :: _, l :: _ ->
                    [ Batch
                          [ InsertChild(root, RNode.leaf (sprintf "p140-b-%d" n) "para" "v")
                            MoveNode(l, root) ]
                      Batch
                          [ InsertChild(root, RNode.leaf (sprintf "p140-c-%d" n) "section" "")
                            InsertChild(l, RNode.leaf (sprintf "p140-d-%d" n) "para" "v") ] ]
                | _ -> []

            generated @ inserts @ grafts @ moves @ batches

        let n', t' =
            states
            |> List.fold
                (fun (n, t) st ->
                    let n = n + 1
                    n, Differential.over (probesAt n st) (fun op -> contProbe modelApply kinds op st) t)
                (n, t)

        (r2, n'), t'

    Differential.seeded trials (ConfRng.ofSeed seed, 0) step contTally



// ---------------------------------------------------------------------------
//  Phase 160 — the SEQUENCE surface as a third oracle family.
//
//  `Ops.applyAll` under `OpScope.contained` / `Ops.canApplyAll` under `OpScope.contained` are the container-aware script pair that closes the
//  gap Phase 140 recorded, and `proofs/Preservation.fst` section 9 models them clause for clause:
//  `apply_all_with` / `can_apply_all_with`, with the failure payload MODELLED rather than dropped
//  (section 8's `can_apply_all` drops the index because nothing there turned on it; here
//  everything does).
//
//  What runs here is the extracted pair beside the shipped pair, over generated scripts and a
//  drawn `canHold` — drawn for the reason the per-op container family draws one, because the
//  theorems quantify over the predicate. Four things are compared, and the last two are the whole
//  of what the sequence surface adds over the per-op one:
//
//    1. the verdict, and the accepted tree through production's own `Tree.encodeHash`
//    2. the rejection CLASS
//    3. the refusal INDEX — which step was refused, not merely that one was
//    4. the PARTIAL TREE the refusal hands back — the state the accepted prefix reached
//
//  A refusal at index 0 returns the caller's own input, so it says nothing about (4). The
//  adequacy guard therefore counts MID-SCRIPT refusals separately and asserts them, exactly as the
//  Phase 140 family counts capability sites separately: a run that met only step-0 refusals would
//  compare the partial tree against the input and certify nothing.
// ---------------------------------------------------------------------------

/// The extracted sequence dry run, as the differential calls it — a seam so the go-red can be
/// substituted for it. The go-red is the model's own `can_apply_all`: section 8's plain sequence
/// check, which is what the shipped `canApplyAll` threaded before this phase.
type private ScriptCan =
    (TreeOps.tree -> bool)
        -> Prims.nat
        -> Prims.list<TreeOps.op>
        -> TreeOps.tree
        -> DagFold.outcome<unit, Prims.nat * TreeOps.rejection>

/// The go-red: the capability-free sequence dry run, lifted into the container-aware signature by
/// discarding the predicate. `can_apply_all_ignores_containment` proves in F\* that it admits a
/// script the container-aware call refuses, so it is a proved weakening before it is measured.
let private scriptGoRedCan: ScriptCan =
    fun _ i os t ->
        match Preservation.can_apply_all os t with
        | DagFold.Ok() -> DagFold.Ok()
        | DagFold.Error e -> DagFold.Error(i, e)

/// The script tally. `MidScript` counts refusals at an index > 0 — the only probes where the partial
/// tree is something other than the caller's own input — and `ContainerRefusals` refusals whose
/// class is `NotAContainer`, the class the whole phase exists to make reachable from a script.
let private scriptTally =
    Tally.empty [ "Accepted"; "Refused"; "MidScript"; "ContainerRefusals" ]
    |> Tally.sets [ "Predicates" ]

/// A tree's render pair through production's own `Tree.encodeHash` — the partial tree a refusal
/// hands back is compared by digest.
let private treeDigest: Differential<RNode, TreeOps.tree> =
    Differential.pair prodTreeHash modelTreeHash

/// The sequence dry run's render pair: "ok", or the refusal's class at its index.
let private scriptVerdict
    : Differential<Result<unit, int * Rejection<string>>, DagFold.outcome<unit, Prims.nat * TreeOps.rejection>> =
    Differential.pair
        (function
        | Ok() -> "ok"
        | Error(i, e) -> sprintf "%s@%s" (prodRejClass e) (string i))
        (function
         | DagFold.Ok() -> "ok"
         | DagFold.Error(i, e) -> sprintf "%s@%s" (modelRejClass e) (string i))

let private scriptProbe
    (modelCanApply: ScriptCan)
    (kinds: Set<string>)
    (script: SkeletonOp<RNode, string> list)
    (st: RNode)
    (t: Tally)
    : Tally =
    let canHold (n: RNode) = kinds.Contains(nodew.KindTag n)
    let mCanHold (t: TreeOps.tree) = kinds.Contains(mKind t)
    let mscript = script |> List.map (toModelOpWith toModelTree)
    let mst = toModelTree st
    let zero = Prims.parse_int "0"

    let where =
        sprintf
            "script [%s] at tree %s under canHold={%s}"
            (script |> List.map renderProdOp |> String.concat "; ")
            (prodTreeHash st)
            (showKinds kinds)

    let prod = Ops.applyAll Policy.none (OpScope.contained canHold nodew idw) script st
    let model = Preservation.apply_all_with mCanHold zero mscript mst

    let prodCan =
        Ops.canApplyAll Policy.none (OpScope.contained canHold nodew idw) script st

    let modelCan = modelCanApply mCanHold zero mscript mst

    // 1-4. applyAllWith: verdict, tree, class, index, partial tree
    let t =
        match prod, model with
        | Ok pt, DagFold.Ok mt -> t |> acceptedAgreement where pt mt |> Tally.bump "Accepted"
        | Error { Applied = pi
                  Rejection = pe
                  Tree = ppartial },
          DagFold.Error(mi, me, mpartial) ->
            t
            |> rejectionAgreement where pe me
            // The index is the sequence surface's own contribution — which step, not merely that
            // one failed. Compared as a number on both sides; the model's is an unbounded `nat`.
            |> Differential.check (System.Numerics.BigInteger(pi) = mi) (fun () ->
                sprintf "refusal INDEX differs — %s\n  production: %d\n  oracle:     %O" where pi mi)
            // And the partial tree: first-refusal-wins keeps the accepted prefix, so this is the
            // state the caller is left holding and the model has to reproduce it exactly.
            |> Differential.compareWith
                treeDigest
                (fun ph mh -> sprintf "PARTIAL tree differs — %s\n  production: %s\n  oracle:     %s" where ph mh)
                ppartial
                mpartial
            |> Tally.bump "Refused"
            |> Tally.bumpIf (pi > 0) "MidScript"
            |> Tally.bumpIf (prodRejClass pe = "NotAContainer") "ContainerRefusals"
        | Ok _, DagFold.Error(_, me, _) ->
            Tally.disagree
                (sprintf "production ACCEPTED the script but the oracle rejected (%s) — %s" (modelRejClass me) where)
                t
        | Error { Applied = pi; Rejection = pe }, DagFold.Ok _ ->
            t
            |> Tally.disagree (
                sprintf
                    "production REJECTED the script at %d (%s) but the oracle accepted — %s"
                    pi
                    (prodRejClass pe)
                    where
            )
            |> Tally.bump "Refused"
            |> Tally.bumpIf (pi > 0) "MidScript"
            |> Tally.bumpIf (prodRejClass pe = "NotAContainer") "ContainerRefusals"

    // 5. the dry run, against the model's and against production's own mutating call
    let t =
        // production's own two surfaces, which section 9's `can_apply_all_with_agrees` says agree
        // on the index and the envelope as well as on the verdict
        let fromApply =
            match prod with
            | Ok _ -> Ok()
            | Error { Applied = i; Rejection = e } -> Error(i, e)

        t
        |> Differential.compareWith
            scriptVerdict
            (fun pv mv -> sprintf "canApplyAllWith differs — %s\n  production: %s\n  oracle:     %s" where pv mv)
            prodCan
            modelCan
        |> Differential.check (scriptVerdict.Production prodCan = scriptVerdict.Production fromApply) (fun () ->
            sprintf
                "production's canApplyAllWith and applyAllWith DISAGREE — %s\n  dry run: %s\n  apply:   %s"
                where
                (scriptVerdict.Production prodCan)
                (scriptVerdict.Production fromApply))

    // 6. and the total instance: `Ops.applyAll` / `Ops.canApplyAll` ARE the `fun _ -> true`
    //    instances, which is the claim that no existing caller moved. Asserted here as well as in
    //    `OpsTests` because this pool is the generated one.
    let plainApply = Ops.applyAll Policy.none (OpScope.plain nodew idw) script st

    let totalApply =
        Ops.applyAll Policy.none (OpScope.contained (fun _ -> true) nodew idw) script st

    let render r =
        match r with
        | Ok t -> "ok:" + prodTreeHash t
        | Error { Applied = i; Rejection = e; Tree = t } -> sprintf "%d:%s:%s" i (prodRejClass e) (prodTreeHash t)

    let renderCan r =
        match r with
        | Ok() -> "ok"
        | Error(i, e) -> sprintf "%d:%s" i (prodRejClass e)

    t
    |> Differential.check (render plainApply = render totalApply) (fun () ->
        sprintf
            "applyAll is NOT applyAllWith (fun _ -> true) — %s\n  applyAll:     %s\n  applyAllWith: %s"
            where
            (render plainApply)
            (render totalApply))
    |> Differential.check
        (renderCan (Ops.canApplyAll Policy.none (OpScope.plain nodew idw) script st) = renderCan (
            Ops.canApplyAll Policy.none (OpScope.contained (fun _ -> true) nodew idw) script st
        ))
        (fun () -> sprintf "canApplyAll is NOT canApplyAllWith (fun _ -> true) — %s" where)
    |> Tally.reach "Predicates" (showKinds kinds)

/// The generated pool, plus scripts MINTED to place a container refusal at a chosen POSITION.
/// The generated lanes alone reach a mid-script refusal by luck; a script whose second step is a
/// move into a leaf reaches one by construction, which is what the Phase 138 family's lesson says
/// to do and what the partial-tree comparison needs.
let private scriptDifferential' (modelCanApply: ScriptCan) (seed: int) (trials: int) : Tally =
    // `n`, the per-trial counter the minted probes are named from, is the trial's own index
    let step (n: int) (r: ConfRng.T) (t: Tally) =
        let lanes, r1 = treeLaneGen.Lanes 3 r
        let kinds, r2 = drawCanHold r1
        let generated = List.concat lanes

        let leaves =
            Tree.preorder nodew treeBase
            |> List.filter (fun c -> List.isEmpty (nodew.Children c))
            |> List.map nodew.Id
            |> List.truncate 2

        // a step that lands under a leaf — refused by every predicate that does not admit "para",
        // and accepted (as `apply` accepts it) by one that does
        let intoLeaf =
            match leaves with
            | l :: _ ->
                [ MoveNode("b", l)
                  InsertChild(l, RNode.leaf (sprintf "p160-%d" n) "para" "v") ]
            | [] -> []

        let prefix = generated |> List.truncate 2

        let scripts =
            [ generated ]
            @ (intoLeaf |> List.map (fun probe -> [ probe ])) // refusal at index 0
            @ (intoLeaf |> List.map (fun probe -> prefix @ [ probe ])) // refusal MID-script
            @ (intoLeaf |> List.map (fun probe -> prefix @ [ probe ] @ generated)) // and work after it

        r2, Differential.over scripts (fun script -> scriptProbe modelCanApply kinds script treeBase) t

    Differential.seeded trials (ConfRng.ofSeed seed) step scriptTally


/// The differential proper — the extracted `can_apply_all_with` in the dry-run slot.
let private scriptDifferential (seed: int) (trials: int) : Tally =
    scriptDifferential' Preservation.can_apply_all_with seed trials

/// The SECOND source (Phase 139): the shared corpus's `apply/` family, decoded through the
/// envelope's own codec and asked of both sides through the same probe.
///
/// Why a committed corpus is worth running beside a generator that already draws thousands of
/// pairs. The generator's pool is whatever its lane draws reach, so it is wide and unchosen —
/// good for finding a disagreement nobody anticipated, and silent about any clause it happens
/// never to draw. The corpus is the opposite: one vector per validator clause per op, authored,
/// and the same bytes four other hosts are asked to certify against. Running it here means the
/// F* model is held to the SAME artefact the hosts are, so "the model agrees with production"
/// and "production agrees with the published contract" stop being two unconnected claims.
let private presCorpusDifferential
    (modelApply: TreeOps.op -> TreeOps.tree -> DagFold.outcome<TreeOps.tree, TreeOps.rejection>)
    (vectors: ApplyVectorExport.ParsedVector list)
    : Tally =
    presTally
    |> Differential.over vectors (fun (v: ApplyVectorExport.ParsedVector) -> presProbe modelApply v.Op v.Tree)

[<Tests>]
let tests =
    testList
        "Proofs.Oracle"
        [
          // ---- Phase 138 — the APPLY ENGINE: apply, canApply and invert against the model ----

          testCase "the preservation oracle agrees with Ops.apply over colliding and duplicated grafts"
          <| fun _ ->
              let t = presDifferential TreeOps.apply 1380 12

              Differential.expectAgrees "the preservation oracle" t

              // Adequacy, per disputed shape rather than in aggregate: a count of probes cannot
              // say WHICH of the two halves of `firstDuplicateId` was exercised, and this family
              // exists precisely because the Phase 133 pool reaches neither. Measured at 12
              // trials: accepted 276, rejected 1082, collided 507, duplicated 294, inverted 276.
              // Each threshold sits below its measurement with room — they are here to catch a
              // generator that stops reaching a shape, not to pin the numbers.
              Expect.isGreaterThan
                  (Tally.count "Accepted" t)
                  150
                  (sprintf "grafts were accepted (accepted=%d)" (Tally.count "Accepted" t))

              Expect.isGreaterThan
                  (Tally.count "Rejected" t)
                  600
                  (sprintf "grafts were refused (rejected=%d)" (Tally.count "Rejected" t))

              Expect.isGreaterThan
                  (Tally.count "Collided" t)
                  250
                  (sprintf
                      "the sample carried grafts whose ids the tree ALREADY HOLDS (collided=%d) — re-read through Tree.ids, not assumed from the construction"
                      (Tally.count "Collided" t))

              Expect.isGreaterThan
                  (Tally.count "Duplicated" t)
                  150
                  (sprintf
                      "the sample carried grafts that repeat an id WITHIN themselves (duplicated=%d)"
                      (Tally.count "Duplicated" t))

              Expect.isGreaterThan
                  (Tally.count "Inverted" t)
                  150
                  (sprintf "accepted leaf operations were inverted and undone (inverted=%d)" (Tally.count "Inverted" t))

              for cls in
                  [ "UnknownNode"
                    "DuplicateId"
                    "CannotRemoveRoot"
                    "WouldNestUnderSelf"
                    "ReorderMismatch" ] do
                  Expect.isTrue
                      (Set.contains cls (Tally.reached "Classes" t))
                      (sprintf "the sample reached a %s rejection (reached: %A)" cls (Tally.reached "Classes" t))

          // ---- Phase 139 — the same differential, over the committed `apply/` family ----
          //
          // Phase 172: read from THIS repository's `conformance/apply/`, which is where the
          // family is authored; the corpus carries a declared copy of it. This leg is about the
          // model agreeing with production over Core's own vectors, so it needs no corpus and
          // runs in the default suite.

          testCase "the preservation oracle agrees with Ops.apply over the committed `apply/` fixtures"
          <| fun _ ->
              let root = OwnedConformance.root ()
              let path = ApplyVectorExport.vectorsPath root

              if not (System.IO.File.Exists path) then
                  failtestf
                      "this repository carries no %s at '%s' — the differential's second host is not committed; re-run `render apply` (no argument) and commit conformance/"
                      ApplyVectorExport.vectorsFileName
                      path

              match ApplyVectorExport.parseVectors (System.IO.File.ReadAllText path) with
              | Error m -> failtest ("the committed apply vectors did not read: " + m)
              | Ok vectors ->
                  let t = presCorpusDifferential TreeOps.apply vectors

                  Differential.expectAgrees "the preservation oracle, on a corpus fixture," t

                  // Adequacy over the committed sample, so a corpus that quietly stopped
                  // carrying a clause cannot read as agreement about it. The bounds are the
                  // family's own claim about itself, not a count of this run.
                  Expect.isGreaterThan
                      (Tally.count "Accepted" t)
                      0
                      (sprintf "the corpus sample carries accepted ops (accepted=%d)" (Tally.count "Accepted" t))

                  Expect.isGreaterThan
                      (Tally.count "Rejected" t)
                      0
                      (sprintf "the corpus sample carries refused ops (rejected=%d)" (Tally.count "Rejected" t))

                  Expect.isGreaterThan
                      (Tally.count "Collided" t)
                      0
                      (sprintf
                          "the corpus sample carries a graft whose id the tree already holds (collided=%d)"
                          (Tally.count "Collided" t))

                  Expect.isGreaterThan
                      (Tally.count "Duplicated" t)
                      0
                      (sprintf
                          "the corpus sample carries a graft repeating an id WITHIN itself (duplicated=%d)"
                          (Tally.count "Duplicated" t))

                  for cls in
                      [ "UnknownNode"
                        "DuplicateId"
                        "CannotRemoveRoot"
                        "WouldNestUnderSelf"
                        "ReorderMismatch" ] do
                      Expect.isTrue
                          (Set.contains cls (Tally.reached "Classes" t))
                          (sprintf
                              "the corpus sample reached a %s rejection (reached: %A)"
                              cls
                              (Tally.reached "Classes" t))

          testCase "a validator that SKIPS the subtree check loses against production — the measurement can fail"
          <| fun _ ->
              // The teeth, and they cost nothing to build: `TreeOps.apply_pre137` IS the validator
              // that reads the graft's own id alone, kept under its own name when Phase 138 lifted
              // the model. Handing it to the same differential is the go-red — and it is the same
              // comparison that was silently GREEN for the whole of Phase 137, because the Phase 80
              // generator cannot mint a colliding graft. If this ever passes, the disputed inputs
              // have stopped reaching the comparison and the green run above means nothing.
              let t = presDifferential TreeOps.apply_pre137 1380 3

              // Measured at 3 trials: 126 colliding grafts reached. A go-red that met none of the
              // disputed inputs would agree with production perfectly and certify nothing, so this
              // is asserted before the disagreement is.
              Expect.isGreaterThan
                  (Tally.count "Collided" t)
                  60
                  (sprintf
                      "the go-red run reached the disputed inputs at all (collided=%d) — otherwise it proves nothing"
                      (Tally.count "Collided" t))

              Differential.expectLoses
                  "a model whose insert validator reads the graft's own id alone MUST disagree with production"
                  t

              Expect.isTrue
                  (Tally.disagreements t
                   |> List.exists (fun d -> d.Contains "production REJECTED (DuplicateId) but the oracle accepted"))
                  (sprintf
                      "the disagreement is the one this phase is about — production refuses the graft, the old validator admits it. Got:\n%s"
                      (List.head (Tally.disagreements t)))

          testCase
              "the retired counterexample is refused by the shipped engine, and admitted by the model of the old validator"
          <| fun _ ->
              // `TreeOps.insert_breaks_wf_pre137` is machine-checked against `apply_pre137`, and
              // `TreeOps.cx_insert_refused_now` against the live model. This is the third leg: the
              // SHIPPED engine, on the same concrete tree, so the refutation and its fix are pinned
              // on all three surfaces rather than two.
              let cxTree = RNode.node "root" "doc" [ RNode.leaf "a" "section" "" ]

              let cxInsert =
                  InsertChild("a", RNode.node "fresh" "section" [ RNode.leaf "root" "para" "v" ])

              match Ops.apply Policy.none (OpScope.plain nodew idw) cxInsert cxTree with
              | Ok _ -> failtest "the shipped engine ADMITS the Phase 133 counterexample — Phase 137 has regressed"
              | Error(DuplicateId d) ->
                  Expect.equal d "root" "the offender named is the DESCENDANT id, which the pre-137 check could not see"
              | Error e -> failtestf "refused, but not as a duplicate id: %A" e

              // and the old validator's model admits it, which is what made it a counterexample
              match TreeOps.apply_pre137 (toModelOpWith toModelTree cxInsert) (toModelTree cxTree) with
              | DagFold.Ok _ -> ()
              | DagFold.Error e ->
                  failtestf
                      "the model of the PRE-137 validator refused it too — then it was never a counterexample: %A"
                      e

          // ---- Phase 140 — the CONTAINER CAPABILITY: applyContained under a generated canHold ----

          testCase "the container oracle agrees with Ops.applyContained under a generated canHold"
          <| fun _ ->
              let t = contDifferential Preservation.apply_contained 1400 30

              Differential.expectAgrees "the container oracle" t

              // Adequacy, per capability SITE rather than in aggregate. A run that never met a
              // refusal would agree with production perfectly and certify nothing about the
              // guard; a run that met only insert refusals would not have been able to catch
              // the go-red below, which is a move. Measured at 30 trials, seed 1400: accepted
              // 1,245, refused 1,305, insert refusals 649, move refusals 370, batch 112, graft
              // refusals 124, pre-emptions 89, invariant asserted on 1,028 probes, all eight
              // predicates drawn. Each threshold sits below its measurement with room — they
              // are here to catch a pool that stops reaching a shape, not to pin the numbers.
              //
              // Re-measured at Phase 161, and TWO of the numbers moved for reasons worth
              // knowing rather than because the pool grew. The graft probes are new, so the
              // insert count rose with them (185 → 649, of which 124 are the graft site). And
              // the invariant is now asserted on half again as many probes (704 → 1,028)
              // because retiring the `contained_op` premise removed a GATE from arm 5, not
              // because more probes were drawn: probes whose graft broke the invariant used to
              // be skipped, and the engine refuses them now.
              Expect.isGreaterThan
                  (Tally.count "Accepted" t)
                  500
                  (sprintf "operations were accepted (accepted=%d)" (Tally.count "Accepted" t))

              Expect.isGreaterThan
                  (Tally.count "InsertRefusals" t)
                  100
                  (sprintf
                      "the sample refused an INSERT under a non-container (insert=%d)"
                      (Tally.count "InsertRefusals" t))

              Expect.isGreaterThan
                  (Tally.count "MoveRefusals" t)
                  200
                  (sprintf "the sample refused a MOVE into a non-container (move=%d)" (Tally.count "MoveRefusals" t))

              Expect.isGreaterThan
                  (Tally.count "BatchRefusals" t)
                  50
                  (sprintf
                      "a BATCH inherited a member's NotAContainer (batch=%d) — the clause the model's inheritance half is about"
                      (Tally.count "BatchRefusals" t))

              // Phase 161's site. Counted separately from `InsertRefusals` for the reason the
              // move half is counted separately from the insert half: a run that reached only
              // the parent check could not have caught a graft walk that never fired, and
              // before this phase the pool literally could not reach it — every insert it minted
              // was a leaf.
              Expect.isGreaterThan
                  (Tally.count "GraftRefusals" t)
                  50
                  (sprintf
                      "the sample refused a GRAFT for its own interior (graft=%d) — the Phase 161 site"
                      (Tally.count "GraftRefusals" t))

              // The theorem is quantified over `canHold`; a differential that drew one
              // predicate would say nothing about the quantifier. Five of the eight subsets is
              // the measured floor, and the two extremes are named because they are the
              // degenerate readings: nothing can hold children, and the engine is `apply`.
              Expect.isGreaterThan
                  (Set.count (Tally.reached "Predicates" t))
                  5
                  (sprintf "several DIFFERENT canHold predicates were drawn (%A)" (Tally.reached "Predicates" t))

              Expect.isTrue
                  (Set.contains "(nothing)" (Tally.reached "Predicates" t))
                  (sprintf
                      "the empty predicate was drawn — nothing can hold children (%A)"
                      (Tally.reached "Predicates" t))

              Expect.isTrue
                  (Set.contains "doc+para+section" (Tally.reached "Predicates" t))
                  (sprintf
                      "the total predicate was drawn — applyContained IS apply there (%A)"
                      (Tally.reached "Predicates" t))

              // and the invariant was not asserted vacuously
              Expect.isGreaterThan
                  (Tally.count "Preserved" t)
                  400
                  (sprintf
                      "the container invariant's hypotheses were MET on this many probes, and the conclusion therefore asserted (preserved=%d)"
                      (Tally.count "Preserved" t))

              Expect.isTrue
                  (Set.contains "NotAContainer" (Tally.reached "Classes" t))
                  (sprintf "the sample reached a NotAContainer rejection (reached: %A)" (Tally.reached "Classes" t))

              // and the PRE-EMPTION was reached — the capability refusing an operation `apply`
              // also refuses, but under a different class. It is asserted rather than merely
              // permitted because it is the shape that made the first draft of comparison 3
              // wrong, and a sample that stopped reaching it would let that draft back in.
              Expect.isGreaterThan
                  (Tally.count "Preempted" t)
                  40
                  (sprintf
                      "the capability PRE-EMPTED another refusal on this many probes (preempted=%d) — a self-move into a leaf is NotAContainer here and WouldNestUnderSelf under apply"
                      (Tally.count "Preempted" t))

          testCase "an engine that checks canHold on insert but NOT on move loses — the measurement can fail"
          <| fun _ ->
              // The teeth, and the model built them: `apply_contained_insert_only` is the engine a
              // plausible reading of the doc comment describes, and `insert_only_breaks_contained`
              // proves in F* that it admits a move the real one refuses and breaks the invariant
              // doing it. If this ever passes, the move site has stopped reaching the comparison
              // and the green run above means nothing about half the guard.
              let t = contDifferential Preservation.apply_contained_insert_only 1400 3

              // Measured at 3 trials: 42 moves into a non-container reached, and 42 disagreements.
              // A go-red that met none of the disputed inputs would agree with production and
              // certify nothing, so this is asserted before the disagreement is.
              Expect.isGreaterThan
                  (Tally.count "MoveRefusals" t)
                  20
                  (sprintf
                      "the go-red run reached a move into a non-container at all (move=%d) — otherwise it proves nothing"
                      (Tally.count "MoveRefusals" t))

              Differential.expectLoses
                  "an engine that skips the capability check on MoveNode MUST disagree with production"
                  t

              Expect.isTrue
                  (Tally.disagreements t
                   |> List.exists (fun d ->
                       d.Contains "production REJECTED (NotAContainer) but the oracle accepted"
                       && d.Contains "op M|"))
                  (sprintf
                      "the disagreement is the one this phase is about — production refuses the MOVE, the insert-only engine admits it. Got:\n%s"
                      (List.head (Tally.disagreements t)))

          testCase "canHold is consulted on the graft's INTERIOR too — the shipped engine, since Phase 161"
          <| fun _ ->
              // `nested_graft_refused`, on production, and BOTH halves of it — because the half
              // that matters is the difference between them.
              //
              // Until Phase 161 this case asserted the opposite: `validateInsert` applied `canHold`
              // to the parent and to nothing else, so the engine ADMITTED a subtree whose own
              // interior node was a non-container and the invariant broke across an accepted
              // operation. That was not a defect to fix in the test — it was the reason the theorem
              // carried `contained_op` as a hypothesis. The operator's ruling (DECISIONS D38) was to
              // inspect the graft, so the premise is discharged by the code and the case is
              // rewritten rather than deleted: the model still evaluates the old engine
              // (`apply_contained_pre161`), and what is asserted here is that production no longer
              // behaves like it.
              let canHold (n: RNode) = n.Kind = "doc"
              let tree = RNode.node "root" "doc" []

              let graft = InsertChild("root", RNode.node "a" "para" [ RNode.leaf "b" "para" "" ])

              Expect.isTrue (prodContained canHold tree) "the tree satisfies the invariant to begin with"
              Expect.isFalse (prodContainedOp canHold graft) "the GRAFT does not — its own interior node is a leaf kind"

              // (1) the engine as it stood still breaks the invariant, which is what made the
              //     premise necessary. Asked of the MODEL, because production no longer has it.
              match
                  Preservation.apply_contained_pre161
                      (fun t -> TreeOps.kind_of t = "doc")
                      (toModelOpWith toModelTree graft)
                      (toModelTree tree)
              with
              | DagFold.Error e ->
                  failtestf "the PRE-161 engine refused the graft (%A) — then the premise was never necessary" e
              | DagFold.Ok result ->
                  Expect.isFalse
                      (Preservation.contained (fun t -> TreeOps.kind_of t = "doc") result)
                      "the pre-161 engine broke the invariant, which is what `contained_op` was a hypothesis about"

              // (2) and the shipped engine refuses it, naming the offender IN THE GRAFT — "a",
              //     which holds "b" while the predicate refuses it — and not "root", which is the
              //     parent and can hold children perfectly well.
              match Ops.apply Policy.none (OpScope.contained canHold nodew idw) graft tree with
              | Error(NotAContainer("a", "para")) -> ()
              | other -> failtestf "expected NotAContainer naming the interior offender 'a', got %A" other

              Expect.equal
                  (Ops.canApply Policy.none (OpScope.contained canHold nodew idw) graft tree)
                  (Ops.apply Policy.none (OpScope.contained canHold nodew idw) graft tree
                   |> Result.map ignore)
                  "the dry run sees the interior refusal too"

          testCase "the sequence surface sees containment and the plain pair still does not — both halves"
          <| fun _ ->
              // `can_apply_all_ignores_containment` AND `can_apply_all_with_sees_containment`, on
              // production, at the same tree and the same script. This case was Phase 140's, where
              // it recorded a FINDING — `Ops.canApplyAll` and `Ops.applyAll` both threaded the
              // plain `apply`, so there was no container-aware sequence surface at all and a
              // caller pre-flighting with `canApplyAll` and executing with `Ops.apply` under `OpScope.contained` had a
              // check that could not see the refusal its executor would make.
              //
              // Phase 160 closed that by ADDING `Ops.applyAll` under `OpScope.contained` / `Ops.canApplyAll` under `OpScope.contained` beside the pair,
              // and 140's own note — "this case goes RED if that gap is ever closed" — turned out
              // to be a go-red by intention rather than by construction: every assertion it made
              // was about `canApplyAll` and `Ops.apply` under `OpScope.contained`, and 160 deliberately moved neither.
              // The plain pair is the `fun _ -> true` instance BY CONSTRUCTION, so it is blind to
              // containment now and always will be; a phase that changed that would have moved
              // every existing caller.
              //
              // So the case is restated to carry both halves, which is what gives it teeth in both
              // directions: it goes RED if the new pair ever stops consulting the capability, and
              // RED if the plain pair ever gains one.
              let canHold (n: RNode) = n.Kind = "box"

              let tree =
                  RNode.node "root" "box" [ RNode.leaf "leaf" "para" ""; RNode.leaf "x" "para" "" ]

              let script = [ MoveNode("x", "leaf") ]

              Expect.isTrue (prodContained canHold tree) "the tree satisfies the invariant to begin with"

              // ---- half one: the plain pair has not moved, and is still blind ----
              Expect.isOk
                  (Ops.canApplyAll Policy.none (OpScope.plain nodew idw) script tree
                   |> Result.mapError (fun (i, e) -> sprintf "%d:%A" i e))
                  "canApplyAll certifies the script — it consults no capability, and must not start"

              match Ops.apply Policy.none (OpScope.contained canHold nodew idw) (Batch script) tree with
              | Error(NotAContainer("leaf", "para")) -> ()
              | other ->
                  failtestf "applyContained was expected to refuse the very script canApplyAll certified, got %A" other

              // ---- half two: the sequence surface that DOES see it (Phase 160) ----
              match Ops.canApplyAll Policy.none (OpScope.contained canHold nodew idw) script tree with
              | Error(0, NotAContainer("leaf", "para")) -> ()
              | other ->
                  failtestf "canApplyAllWith must refuse at step 0 with the envelope applyAllWith raises, got %A" other

              match Ops.applyAll Policy.none (OpScope.contained canHold nodew idw) script tree with
              | Error { Applied = 0
                        Rejection = NotAContainer("leaf", "para")
                        Tree = partial } ->
                  // the refusal is at the first step, so the accepted prefix is empty and the
                  // partial tree is the caller's own — first-refusal-wins, not all-or-nothing
                  Expect.equal
                      (Tree.encodeHash nodew encNode partial)
                      (Tree.encodeHash nodew encNode tree)
                      "a script refused at step 0 hands back the tree it was given"
              | other -> failtestf "applyAllWith must refuse at step 0 and return the partial tree, got %A" other

          testCase "the script oracle agrees with Ops.applyAllWith / canApplyAllWith under a generated canHold"
          <| fun _ ->
              // The Phase 160 differential: the extracted `apply_all_with` / `can_apply_all_with`
              // beside the shipped pair, over generated scripts and a DRAWN predicate — drawn for
              // the same reason the per-op container family draws one, because the theorems
              // quantify over `canHold` and a run against one hand-picked predicate certifies that
              // instance and nothing about the quantifier.
              //
              // What is compared is the whole failure payload and not merely the verdict: the
              // INDEX and the partial TREE are the sequence surface's entire contribution over the
              // per-op one, so a differential that compared accept-vs-refuse alone would certify
              // nothing this phase added.
              let t = scriptDifferential 1600 24

              Differential.expectAgrees "the script oracle" t

              // adequacy, per the shape this phase is about — measured at 24 trials (seed 1600):
              // 45 scripts accepted, 123 refused, 60 of those refusals MID-script, 103 of them
              // NotAContainer, and all 8 predicates drawn.
              Expect.isGreaterThan
                  (Tally.count "Accepted" t)
                  30
                  (sprintf "the run accepted scripts at all (accepted=%d)" (Tally.count "Accepted" t))

              Expect.isGreaterThan
                  (Tally.count "Refused" t)
                  30
                  (sprintf "the run refused scripts at all (refused=%d)" (Tally.count "Refused" t))

              // The one that matters most: a refusal at index > 0 is the only probe that exercises
              // the partial tree as something other than the caller's own input, and it is the
              // state the non-atomic surface exists to produce.
              Expect.isGreaterThan
                  (Tally.count "MidScript" t)
                  20
                  (sprintf
                      "the run refused scripts MID-WAY (midScript=%d) — otherwise the partial tree is never compared against anything but the input"
                      (Tally.count "MidScript" t))

              Expect.isGreaterThan
                  (Tally.count "ContainerRefusals" t)
                  20
                  (sprintf
                      "the run reached NotAContainer refusals on the SEQUENCE surface (container=%d) — the refusal class this phase exists to make visible to a script"
                      (Tally.count "ContainerRefusals" t))

              Expect.isGreaterThan
                  (Set.count (Tally.reached "Predicates" t))
                  5
                  (sprintf "several DIFFERENT canHold predicates were drawn (%A)" (Tally.reached "Predicates" t))

          testCase "an engine whose sequence dry run drops the capability loses — the measurement can fail"
          <| fun _ ->
              // The teeth. The go-red is the model's own `can_apply_all`, section 8's plain
              // sequence dry run — the very function that was the shipped `canApplyAll` before this
              // phase, and which `can_apply_all_ignores_containment` proves admits a script the
              // container-aware call refuses. Handing it to the same differential in the dry-run
              // slot is the measurement, and it must lose: if this ever passes, the new surface has
              // stopped consulting the capability and the green run above means nothing.
              let t = scriptDifferential' scriptGoRedCan 1600 24

              Expect.isGreaterThan
                  (Tally.count "ContainerRefusals" t)
                  20
                  (sprintf
                      "the go-red run reached a container refusal at all (container=%d) — otherwise it proves nothing"
                      (Tally.count "ContainerRefusals" t))

              Differential.expectLoses
                  "a sequence dry run that drops the capability MUST disagree with Ops.canApplyAllWith"
                  t

              Expect.isTrue
                  (Tally.disagreements t
                   |> List.exists (fun d -> d.Contains "canApplyAllWith differs"))
                  (sprintf
                      "the disagreement is the one this phase is about — production's dry run sees the container refusal, the capability-free one does not. Got:\n%s"
                      (List.head (Tally.disagreements t))) ]
