namespace Fuaran.Core

/// The tree families (Phase 297 split): the witness laws, the op algebra, the structural diff, the container and keyed-children laws.
module internal TreeLaws =

    /// The witness laws (Phase 253) — check the `NodeWitness`/`IdWitness` is well-formed
    /// *before* the algebra laws run, so a defect localises to the accessor that's wrong
    /// instead of surfacing as a downstream `apply ∘ invert` failure (the F1 lesson):
    /// `Children (ReplaceChildren n cs) = cs`, `Id`/`KindTag` preserved under rebuild, and
    /// the `IdWitness` round-trip + reflexivity. The `ReplaceChildren` laws are checked only
    /// on nodes that `CanHold` children (a leaf is not required to round-trip a child list).
    ///
    /// The rebuild identity — `ReplaceChildren n (Children n) = n` on every holder, which the engine
    /// relies on (`UpdateNode` rebuilds a node over its own children) — is certified by `opAlgebra`
    /// since Phase 297, whose `apply ∘ invert = identity` law runs over the identity update the kit
    /// now draws; it is not a fifth law HERE only because the family's result count is pinned by
    /// readers this draft cannot move, and it is the next law this family gains.
    let witnessLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)

        let rcRoundTrip =
            LawKit.LawCell "ReplaceChildren round-trip (Children(ReplaceChildren n cs) = cs)"

        let idPreserved = LawKit.LawCell "ReplaceChildren preserves Id"
        let kindStable = LawKit.LawCell "ReplaceChildren preserves KindTag"
        let idRoundTrip = LawKit.LawCell "IdWitness round-trip + reflexivity"

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree

            match Tree.preorder nodew tree |> List.filter canHold with
            | [] -> ()
            | holders ->
                let n = rng.Choose holders
                let k = rng.IntBelow 3
                // a candidate child list of fresh nodes (ids disjoint from the tree)
                let mutable cs = []
                let mutable seen = Tree.ids nodew tree |> List.map idw.ToString |> Set.ofList

                for _ in 1..k do
                    let fresh = rng.Draw(gen.FreshNode seen)
                    seen <- Set.add (idw.ToString(nodew.Id fresh)) seen
                    cs <- cs @ [ fresh ]

                let rebuilt = nodew.ReplaceChildren n cs

                rcRoundTrip.Check(
                    nodew.Children rebuilt = cs,
                    fun () ->
                        at (
                            sprintf
                                "Children(ReplaceChildren n cs) ≠ cs for node %s (kind %s) — ReplaceChildren is not total"
                                (idw.ToString(nodew.Id n))
                                (nodew.KindTag n)
                        )
                )

                idPreserved.Check(
                    idw.Equals (nodew.Id rebuilt) (nodew.Id n),
                    fun () -> at "Id changed under ReplaceChildren"
                )

                kindStable.Check(
                    nodew.KindTag rebuilt = nodew.KindTag n,
                    fun () -> at "KindTag changed under ReplaceChildren"
                )

            for id in Tree.ids nodew tree do
                if not (idw.Equals id id) then
                    idRoundTrip.Check(false, fun () -> at "Equals is not reflexive")
                else
                    idRoundTrip.Check(
                        idw.Equals (idw.OfString(idw.ToString id)) id,
                        fun () -> at (sprintf "OfString∘ToString ≠ id for %s" (idw.ToString id))
                    ))

        LawKit.results [ rcRoundTrip; idPreserved; kindStable; idRoundTrip ]

    /// The op-algebra laws: apply totality (never throws), `canApply` ≡ `apply` (same
    /// accept/reject + envelope), apply∘invert = identity on every applyable op, — Phase 137 —
    /// an accepted insert introduces no id already present, and — Phase 139 — apply's accept path
    /// preserves `Tree.WellFormed`, the sampled twin of the apply-engine preservation theorem.
    ///
    /// **The insert-uniqueness law needs a BUILT arm, and that is the whole reason it reads the way
    /// it does.** `genOp`'s insert branch calls `gen.FreshNode idKeys`, whose contract is a node
    /// whose id is *not* in the tree, so a DRAWN insert never collides — a law quantified over the
    /// drawn sample alone would certify a validator that checked nothing, which is exactly the
    /// vacuity `SampleAdequacy` was cut for. Each iteration therefore BUILDS two subtrees whose root
    /// id is fresh but whose contents repeat an id — one taken from the tree, one repeated within
    /// itself — and feeds them to this law and to the `canApply ≡ apply` law beside it.
    ///
    /// A built candidate is re-read through `Tree.ids` before it counts as evidence: the kit
    /// deliberately admits a witness whose `ReplaceChildren` is partial on leaves, and the shell
    /// comes from `FreshNode`, which may be one. A witness that cannot carry a multi-node subtree at
    /// all cannot exhibit the defect this law is about, so the arm reports nothing rather than
    /// failing — the law over the drawn sample stays true, just narrower.
    let opAlgebra
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let totality = LawKit.LawCell "apply totality (never throws)"
        let equivalence = LawKit.LawCell "canApply ≡ apply (accept/reject + envelope)"
        // The three accept-side laws are asserted only over an ACCEPTED op, which the guard below
        // counts; the two both-sides laws hold their own zero.
        let inversion =
            LawKit.LawCell("apply ∘ invert = identity", Some "accepted op and op kind")

        let uniqueness =
            LawKit.LawCell("an accepted insert introduces no id already present", Some "accepted op and op kind")

        let preservation =
            LawKit.LawCell("apply's accept path preserves Tree.WellFormed", Some "accepted op and op kind")
        // Phase 220 — the apply-outcome populations every law above branches on. `canApply ≡
        // apply` and totality are claims about BOTH sides; inversion, uniqueness and preservation
        // read the accepted side alone. Counted over the drawn and the built arms together, because
        // the refusal population is partly each: `genOp` draws refusals (a remove of the root, a
        // move under a descendant) and the collision arm BUILDS them where the witness can carry a
        // multi-node subtree.
        let mutable accepted = 0
        let mutable refused = 0
        // Phase 297 — the kind of every DRAWN op, for the op-kind guard.
        let kinds = LawKit.OpKindTally()

        /// The first id `t` carries twice (by key), if any — the post-condition an accepted insert
        /// must not create.
        let repeatedId (t: 'Node) : string option =
            let rec scan (seen: Set<string>) ids =
                match ids with
                | [] -> None
                | i :: rest ->
                    let k = idw.ToString i

                    if Set.contains k seen then
                        Some k
                    else
                        scan (Set.add k seen) rest

            scan Set.empty (Tree.ids nodew t)

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let op = rng.Draw(LawKit.genOp nodew idw gen tree)
            kinds.Note op

            /// Phase 139 — the sampled twin of the apply-engine preservation theorem: if the
            /// pre-state is `Tree.WellFormed` and `apply` ACCEPTS, the post-state is too. Quantified
            /// over every op the sample reaches, where the Phase 137 law beside it is about inserts
            /// alone — a `MoveNode` or a `Batch` that broke uniqueness would be invisible to that
            /// one and is exactly what the theorem claims cannot happen.
            ///
            /// The pre-state guard is load-bearing rather than defensive: the law says apply
            /// PRESERVES well-formedness, and an op applied to an already-malformed tree can leave
            /// it malformed without any of that being apply's doing. A generator that happened to
            /// draw a malformed tree would otherwise turn a true theorem into a red law.
            let notePreserves (origin: string) (op: SkeletonOp<'Node, 'Id>) (pre: 'Node) (post: 'Node) =
                if Tree.isWellFormed nodew idw pre then
                    match Tree.wellFormed nodew idw post with
                    | Tree.RepeatedId d ->
                        preservation.Check(
                            false,
                            fun () ->
                                at (
                                    sprintf
                                        "an ACCEPTED %s %A left a WELL-FORMED tree carrying id %s twice"
                                        origin
                                        op
                                        (idw.ToString d)
                                )
                        )
                    | Tree.Structural -> preservation.Saw()

            /// Record an accepted insert that left a repeated id behind. Shared by the drawn and the
            /// built arms, because the property is the same one either way.
            let noteIfRepeats (origin: string) (inserted: 'Node) (post: 'Node) =
                match repeatedId post with
                | Some d ->
                    uniqueness.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "an ACCEPTED %s insert left id %s in the tree twice — the inserted subtree carried ids %A"
                                    origin
                                    d
                                    (Tree.ids nodew inserted |> List.map idw.ToString)
                            )
                    )
                | None -> uniqueness.Saw()

            let applied =
                try
                    Some(Ops.applyContained canHold nodew idw op tree)
                with _ ->
                    None

            totality.Check(applied.IsSome, fun () -> at (sprintf "apply threw on %A" op))

            match applied with
            | None -> ()
            | Some res ->
                let chk = Ops.canApplyContained canHold nodew idw op tree

                let equiv =
                    match res, chk with
                    | Ok _, Ok() -> true
                    | Error e1, Error e2 -> e1 = e2
                    | _ -> false

                equivalence.Check(
                    equiv,
                    fun () -> at (sprintf "canApply≠apply on %A (apply=%A canApply=%A)" op res chk)
                )

                (match res with
                 | Ok _ -> accepted <- accepted + 1
                 | Error _ -> refused <- refused + 1)

                match res with
                | Ok post ->
                    // Phase 137 — the drawn arm. `FreshNode` should make this unreachable as a
                    // failure; it is checked anyway, because a generator that breaks its own
                    // contract is precisely the case nobody would otherwise notice.
                    (match op with
                     | InsertChild(_, inserted) -> noteIfRepeats "drawn" inserted post
                     | _ -> ())

                    // Phase 139 — over EVERY drawn op, not just inserts.
                    notePreserves "drawn" op tree post

                    match Ops.invert nodew idw op tree with
                    | Error e ->
                        inversion.Check(false, fun () -> at (sprintf "invert failed (%A) on an applyable %A" e op))
                    | Ok inv ->
                        let restored = Ops.applyContained canHold nodew idw inv post

                        inversion.Check(
                            (match restored with
                             | Ok r when r = tree -> true
                             | _ -> false),
                            fun () -> at (sprintf "apply∘invert≠identity on %A (got %A)" op restored)
                        )
                | Error _ -> ()

            // ---- Phase 137: the deliberate-collision arm (BUILT, not drawn) ----
            // Drawing the parent from the holders is what gets past the parent's own capability
            // check under a container-aware witness whose fresh nodes are leaves.
            //
            // Phase 161 widened `validateInsert` to walk the GRAFT's interior as well, and this arm
            // builds a shell that HOLDS children — so the note that used to stand here ("`canHold`
            // is never applied to the incoming subtree") is no longer true. The arm is unaffected,
            // and by construction rather than by luck: the duplicate-id scan runs FIRST (D38's
            // precedence decision), and `carries` below is exactly the condition that makes it
            // fire, so a built candidate always earns `DuplicateId` before the interior walk is
            // reached. `containerLaws` is where the interior walk is certified.
            match Tree.preorder nodew tree |> List.filter canHold with
            | [] -> ()
            | holders ->
                let parent = rng.Choose holders
                let victim = rng.Choose(Tree.preorder nodew tree)
                let treeKeys = Tree.ids nodew tree |> List.map idw.ToString |> Set.ofList
                let shell = rng.Draw(gen.FreshNode treeKeys)
                let inner = rng.Draw(gen.FreshNode(Set.add (idw.ToString(nodew.Id shell)) treeKeys))

                let candidates =
                    [ "descendant-collision", nodew.ReplaceChildren shell [ victim ]
                      "internal-duplication", nodew.ReplaceChildren shell [ inner; inner ] ]

                for origin, candidate in candidates do
                    // Evidence only if the witness actually honoured `ReplaceChildren` here, so the
                    // subtree genuinely repeats an id against the tree or against itself.
                    let carries =
                        let rec scan (seen: Set<string>) ids =
                            match ids with
                            | [] -> false
                            | i :: rest ->
                                let k = idw.ToString i
                                Set.contains k seen || scan (Set.add k seen) rest

                        scan treeKeys (Tree.ids nodew candidate)

                    if carries then
                        let colliding = InsertChild(nodew.Id parent, candidate)

                        let applied =
                            try
                                Some(Ops.applyContained canHold nodew idw colliding tree)
                            with _ ->
                                None

                        totality.Check(
                            applied.IsSome,
                            fun () -> at (sprintf "apply threw on the built %s insert" origin)
                        )

                        match applied with
                        | None -> ()
                        | Some res ->
                            // the `canApply ≡ apply` family, re-run over the widened op population
                            let chk = Ops.canApplyContained canHold nodew idw colliding tree

                            let equiv =
                                match res, chk with
                                | Ok _, Ok() -> true
                                | Error e1, Error e2 -> e1 = e2
                                | _ -> false

                            equivalence.Check(
                                equiv,
                                fun () ->
                                    at (
                                        sprintf
                                            "canApply≠apply on the built %s insert (apply=%A canApply=%A)"
                                            origin
                                            res
                                            chk
                                    )
                            )

                            (match res with
                             | Ok _ -> accepted <- accepted + 1
                             | Error _ -> refused <- refused + 1)

                            match res with
                            | Ok post ->
                                noteIfRepeats origin candidate post
                                notePreserves origin colliding tree post
                            | Error _ -> ())

        LawKit.results [ totality; equivalence; inversion; uniqueness; preservation ]
        @ [
            // Phase 220 — `Guarded ["accepted"; "refused"]`. A generator that never draws a refused
            // op (and a witness that cannot carry the built collision) leaves totality and
            // `canApply ≡ apply` certified on the accept path alone; one that never draws an
            // accepted op leaves three of the five laws asserting nothing. Either is a green run
            // that tested nothing on the side a law is about, so it reports the guard, not a pass.
            // Phase 297 — the accepted-op guard also demands every op kind `genOp` draws (folded into
            // it rather than appended, so a reader that pins this family's result count sees no new
            // result): a run that missed a kind certified the laws over a narrower algebra than the
            // one the engine ships.
            SampleAdequacy.reached
                "Conformance.opAlgebra"
                "accepted op and op kind"
                seed
                ([ "accepted", accepted ] @ kinds.Demands)
            SampleAdequacy.reached "Conformance.opAlgebra" "refused op" seed [ "refused", refused ] ]

    /// The structural-diff laws (Phase 03) — certify `Diff.toOps` against a domain's own
    /// witness. Build a random `before`, derive `after` by applying a random valid op sequence,
    /// then check: **reconstruction** (`applyAll (toOps before after) before = after`),
    /// **applyability** (`canApplyAll` accepts the emitted script — no step rejects), and
    /// **survivor preservation** (no `RemoveNode` targets an id present in both `before` and
    /// `after` — a relocated survivor diffs to `MoveNode`, never remove+insert). `'Node` needs
    /// equality. Homogeneous-tree domains fold this into `certify`; stream-only domains skip it.
    let diffLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)

        let reconstruction =
            LawKit.LawCell "diff reconstruction (applyAll(toOps before after) before = after)"

        let applyability =
            LawKit.LawCell "diff applyability (canApplyAll accepts the emitted script)"

        let survivor =
            LawKit.LawCell "diff survivor preservation (no RemoveNode on a survived id)"

        LawKit.run iterations seed (fun rng _ at ->
            let before = rng.Draw gen.Tree

            // derive `after` by applying a few random valid ops (rejected ops are skipped)
            let mutable after = before

            for _ in 1..4 do
                let op = rng.Draw(LawKit.genOp nodew idw gen after)

                match Ops.applyContained canHold nodew idw op after with
                | Ok t' -> after <- t'
                | Error _ -> ()

            match Diff.toOps nodew idw before after with
            | Error e ->
                reconstruction.Check(false, fun () -> at (sprintf "toOps errored on a valid before/after: %A" e))
            | Ok ops ->
                let rebuilt = Ops.applyAll nodew idw ops before

                reconstruction.Check(
                    (match rebuilt with
                     | Ok r when r = after -> true
                     | _ -> false),
                    fun () -> at (sprintf "applyAll(toOps) ≠ after (got %A)" rebuilt)
                )

                match Ops.canApplyAll nodew idw ops before with
                | Ok() -> applyability.Saw()
                | Error(j, e) -> applyability.Check(false, fun () -> at (sprintf "emitted op %d rejects: %A" j e))

                let survivors =
                    Set.intersect
                        (Tree.ids nodew before |> List.map idw.ToString |> Set.ofList)
                        (Tree.ids nodew after |> List.map idw.ToString |> Set.ofList)

                let badRemove =
                    ops
                    |> List.tryPick (function
                        | RemoveNode t when survivors.Contains(idw.ToString t) -> Some t
                        | _ -> None)

                match badRemove with
                | Some t ->
                    survivor.Check(false, fun () -> at (sprintf "RemoveNode targets a survivor %s" (idw.ToString t)))
                | None -> survivor.Saw())

        LawKit.results [ reconstruction; applyability; survivor ]

    /// The CONTAINER-AWARE structural-diff laws (Phase 141) — `Diff.toOpsContained` certified
    /// through the container-aware sequence surface, with the witness's own `canHold`.
    ///
    /// **Why a twin rather than a widening of `diffLaws`.** `diffLaws` above certifies
    /// `Diff.toOps`' scripts with the PLAIN `Ops.canApplyAll` and `Ops.applyAll`, which are the
    /// `fun _ -> true` instances of the container-aware pair and therefore blind to containment by
    /// construction (Phase 160's `all_with_at_total_is_plain`). So a script emitted for a
    /// container-aware witness was never shown applyable under the engine that witness actually
    /// runs: the check and the executor disagreed about what a refusal is. This family asks the
    /// same questions of the pair that belong together — `toOpsContained` emitted it, so
    /// `applyAllWith` / `canApplyAllWith` under the same predicate are what must accept it.
    ///
    /// Three laws, mirroring `diffLaws`' three: **reconstruction**
    /// (`applyAllWith canHold (toOpsContained before after) before = after`), **applyability**
    /// (`canApplyAllWith canHold` accepts every emitted step — `Diff.fst`'s
    /// `diff_applicable_contained` asked of the shipped engine), and **refusal exactness**
    /// (`toOpsContained` refuses with `TargetNotAContainer` exactly when `after` nests children
    /// under a node the predicate rejects, and the plain `toOps` accepts the same pair — so the
    /// refusal is the container check's contribution and nothing else's).
    ///
    /// A fourth law, **refusal correspondence** (Phase 228), holds the diff-side refusal to its
    /// apply-side sibling: wherever `toOpsContained` refuses an `after` with
    /// `TargetNotAContainer(t, k)`, the offending nesting is BUILT as a graft — the subtree `after`
    /// carries at `t`, cut out of `after` and re-inserted under its own parent through
    /// `Ops.applyContained` — and that insert must refuse with `NotAContainer(t, k)`: the same
    /// offender, the same kind tag. Both refusals are defined by `Ops.firstUncontained`, and this is
    /// the law that says a host may read them as one class. Two cases are not askable and are
    /// skipped rather than counted: an offender that is `after`'s ROOT (there is no parent to graft
    /// under), and a host parent the predicate refuses once the graft is cut out of it (a `canHold`
    /// may read a node's child list — Phase 140's `child_blind` — and the insert would then be
    /// refused at the PARENT site, which is a different clause).
    ///
    /// The third law mints a probe as well as checking the generated pair: where the witness's
    /// predicate refuses some node of `after`, a fresh child is grafted under the first such node
    /// and the refusal is demanded by name. A witness that supplies no `CanHold` exercises only the
    /// trivial direction of that iff — which is what a witness with no container capability HAS to
    /// exercise, and is why the census row for this family reads as it does. `'Node` needs
    /// equality. Opt-in, like the snapshot and DAG families: a domain with a container capability
    /// runs it beside `certify`.
    let diffContainedLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)

        let reconstruction =
            LawKit.LawCell "contained diff reconstruction (applyAllWith (toOpsContained before after) before = after)"

        let applyability =
            LawKit.LawCell "contained diff applyability (canApplyAllWith accepts the emitted script)"

        let refusal =
            LawKit.LawCell "contained diff refuses exactly a non-container after-parent"

        let correspondence =
            LawKit.LawCell(
                "contained diff refusal corresponds (TargetNotAContainer(t, k) ⇒ the same graft through applyContained refuses NotAContainer(t, k))",
                Some "refused pair"
            )
        // Phase 223 — the refusal IFF's two directions, counted over every pair it is asked of
        // (the derived pair and the minted probe). The demanding direction is reached only where
        // `after` carries a node the witness's `canHold` rejects, which is DRAWN.
        let mutable accepted = 0
        let mutable refused = 0

        /// the first node of `t` the predicate refuses, if any — the offender `toOpsContained`
        /// names once it also carries children
        let firstRefused (t: 'Node) =
            Tree.preorder nodew t |> List.tryFind (fun n -> not (canHold n))

        /// "`after` nests children under a node `canHold` rejects" — the condition the diff-side
        /// check is exactly about
        let violates (t: 'Node) =
            Tree.preorder nodew t
            |> List.exists (fun n -> not (List.isEmpty (nodew.Children n)) && not (canHold n))

        /// Phase 228 — the offending nesting `after` carries at `t`, built as a graft and applied
        /// through `Ops.applyContained`: it must refuse with `NotAContainer(t, k)`
        let checkCorrespondence (at: string -> string) (after: 'Node) (t: 'Id) (k: string) =
            match Tree.tryFind nodew idw t after, Tree.parentOf nodew idw t after with
            | Some graft, Some host ->
                match Ops.apply nodew idw (RemoveNode t) after with
                | Ok cut when Tree.tryFind nodew idw (nodew.Id host) cut |> Option.exists canHold ->
                    let answer =
                        Ops.applyContained canHold nodew idw (InsertChild(nodew.Id host, graft)) cut

                    correspondence.Check(
                        (match answer with
                         | Error(NotAContainer(t', k')) when idw.Equals t t' && k = k' -> true
                         | _ -> false),
                        fun () ->
                            at (
                                sprintf
                                    "toOpsContained refused with TargetNotAContainer(%s, %s), but grafting the same subtree through applyContained answered %A"
                                    (idw.ToString t)
                                    k
                                    answer
                            )
                    )
                | _ -> ()
            | _ -> ()

        let checkRefusal (at: string -> string) (before: 'Node) (after: 'Node) =
            let expected = violates after

            if expected then
                refused <- refused + 1
            else
                accepted <- accepted + 1

            match Diff.toOpsContained canHold nodew idw before after with
            | Error(Diff.TargetNotAContainer(p, k)) when expected ->
                checkCorrespondence at after p k

                // the node it names must be one `after` really carries, really has children, and
                // the predicate really rejects
                let named =
                    Tree.tryFind nodew idw p after
                    |> Option.map (fun n -> not (List.isEmpty (nodew.Children n)) && not (canHold n))

                refusal.Check(
                    (named = Some true),
                    fun () ->
                        at (
                            sprintf
                                "TargetNotAContainer named %s, which is not a childful non-container of `after`"
                                (idw.ToString p)
                        )
                )
            | Error(Diff.TargetNotAContainer(p, _)) ->
                refusal.Check(
                    false,
                    fun () ->
                        at (
                            sprintf
                                "TargetNotAContainer(%s) on an `after` every parent of which can hold children"
                                (idw.ToString p)
                        )
                )
            | _ when expected ->
                refusal.Check(false, fun () -> at "`after` nests under a non-container and the diff did not refuse")
            | _ -> refusal.Saw()

        LawKit.run iterations seed (fun rng _ at ->
            let before = rng.Draw gen.Tree

            // derive `after` with the container-aware engine, so the pair is one the witness's own
            // predicate admits — the reconstruction and applyability laws are about THAT pair
            let mutable after = before

            for _ in 1..4 do
                let op = rng.Draw(LawKit.genOp nodew idw gen after)

                match Ops.applyContained canHold nodew idw op after with
                | Ok t' -> after <- t'
                | Error _ -> ()

            checkRefusal at before after

            match Diff.toOpsContained canHold nodew idw before after with
            | Error e ->
                if not (violates after) then
                    reconstruction.Check(
                        false,
                        fun () -> at (sprintf "toOpsContained errored on a container-valid pair: %A" e)
                    )
            | Ok ops ->
                let rebuilt = Ops.applyAllWith canHold nodew idw ops before

                reconstruction.Check(
                    (match rebuilt with
                     | Ok r when r = after -> true
                     | _ -> false),
                    fun () -> at (sprintf "applyAllWith(toOpsContained) ≠ after (got %A)" rebuilt)
                )

                match Ops.canApplyAllWith canHold nodew idw ops before with
                | Ok() -> applyability.Saw()
                | Error(j, e) ->
                    applyability.Check(false, fun () -> at (sprintf "emitted op %d rejects under canHold: %A" j e))

            // and the minted probe: graft a child under a node the predicate refuses, so the
            // refusal direction of the third law is exercised rather than merely stated
            match firstRefused after with
            | Some offender when not (violates after) ->
                let fresh =
                    rng.Draw(gen.FreshNode(Tree.ids nodew after |> List.map idw.ToString |> Set.ofList))

                match
                    Tree.updateNode nodew idw (nodew.Id offender) (fun n -> nodew.ReplaceChildren n [ fresh ]) after
                with
                | Some violating ->
                    checkRefusal at before violating

                    // and the plain diff must ACCEPT the same pair: the refusal is the container
                    // check's contribution and nothing else's
                    match Diff.toOps nodew idw before violating with
                    | Ok _ -> refusal.Saw()
                    | Error e ->
                        refusal.Check(
                            false,
                            fun () ->
                                at (
                                    sprintf
                                        "the plain toOps also refused the probe (%A), so the refusal is not the container check's"
                                        e
                                )
                        )
                | None -> ()
            | _ -> ())

        LawKit.results [ reconstruction; applyability; refusal; correspondence ]
        @ [
            // Phase 223 — `Guarded ["accepted"; "refused"]`, after the subject laws. A witness whose
            // `canHold` refuses nothing (or supplies none) exercises only the trivial direction of the
            // refusal IFF; that is now reported as the guard, not as a pass.
            SampleAdequacy.reached "Conformance.diffContainedLaws" "container-valid pair" seed [ "accepted", accepted ]
            SampleAdequacy.reached "Conformance.diffContainedLaws" "refused pair" seed [ "refused", refused ] ]

    /// The op-script normalisation laws (Phase 23) — the teeth on `Ops.normalize`. Build a random
    /// *applyable* script (apply random ops, keep the accepted ones), then check: **preservation**
    /// (`applyAll (normalize ops) = applyAll ops` — the result is unchanged), **idempotence**
    /// (`normalize (normalize ops) = normalize ops`), and **non-growth** (normalisation never
    /// lengthens a script). `'Node` needs equality (it compares result trees + op scripts).
    let normalizeLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let preservation =
            LawKit.LawCell "normalize preservation (applyAll(normalize ops) = applyAll ops)"

        let idempotence =
            LawKit.LawCell "normalize idempotence (normalize ∘ normalize = normalize)"

        let nonGrowth = LawKit.LawCell "normalize never lengthens a script"

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree

            // collect an applyable script: thread random ops, keep the accepted ones
            let accepted = rng.Draw(LawKit.collectScript None nodew idw gen 6 tree)

            let normd = Ops.normalize nodew idw accepted

            preservation.Check(
                Ops.applyAll nodew idw normd tree = Ops.applyAll nodew idw accepted tree,
                fun () -> at "applyAll(normalize ops) ≠ applyAll ops"
            )

            idempotence.Check(Ops.normalize nodew idw normd = normd, fun () -> at "normalize is not idempotent")

            nonGrowth.Check(List.length normd <= List.length accepted, fun () -> at "normalize lengthened the script"))

        LawKit.results [ preservation; idempotence; nonGrowth ]

    /// The body of `containerLaws`, under a predicate the domain actually declared. Private so the
    /// census's reflection over public `…Laws` entry points sees one family rather than two.
    let private containerLawsOver
        (canHold: 'Node -> bool)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let blind =
            LawKit.LawCell(
                "canHold is child-blind (perturbing a node's children leaves it unchanged)",
                Some "built arm"
            )

        let preservation =
            LawKit.LawCell("applyContained preserves the container invariant over this witness", Some "built arm")

        let graftRefusal =
            LawKit.LawCell("a graft with an interior non-container is refused, naming that node", Some "built arm")
        // the three built arms, counted so an arm nothing reached is REPORTED rather than assumed
        let mutable perturbations = 0
        let mutable probes = 0
        let mutable grafts = 0

        /// The invariant, over the domain's own witness: the first node that holds children while
        /// `canHold` refuses it, or `None`. Written out here rather than shared with the engine's
        /// own `validateInsert` helper — a law that read the engine's helper would agree with the
        /// engine by construction, which is the one thing a conformance law must not do.
        let firstUncontained (t: 'Node) : 'Node option =
            Tree.preorder nodew t
            |> List.tryFind (fun n -> not (List.isEmpty (nodew.Children n)) && not (canHold n))

        /// Did the witness honour a rebuild, and does the node still have the identity the
        /// predicate was asked about? A rebuild the witness declined is not evidence; one that
        /// moved the id or the kind is a WITNESS defect (`witnessLaws`' territory), and reporting
        /// it here would localise it to the wrong place — the F1 lesson.
        let sameIdentity (original: 'Node) (rebuilt: 'Node) =
            idw.Equals (nodew.Id rebuilt) (nodew.Id original)
            && nodew.KindTag rebuilt = nodew.KindTag original

        let keysOf (n: 'Node) =
            Tree.ids nodew n |> List.map idw.ToString

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let treeKeys = keysOf tree |> Set.ofList

            // ---- arm 1: child-blindness, by PERTURBATION (built, never drawn) ----
            let subject = rng.Choose(Tree.preorder nodew tree)
            let filler = rng.Draw(gen.FreshNode treeKeys)

            let kids = nodew.Children subject
            let emptied = nodew.ReplaceChildren subject []
            let extended = nodew.ReplaceChildren subject (kids @ [ filler ])

            let notePerturbation (label: string) (rebuilt: 'Node) =
                perturbations <- perturbations + 1

                blind.Check(
                    canHold rebuilt = canHold subject,
                    fun () ->
                        at (
                            sprintf
                                "canHold READS THE CHILD LIST — node %s (kind %s) answers %b holding %d child(ren) and %b %s. A predicate that changes under an edit can be satisfied at the instant applyContained checks it and violated by the very insert that check licensed, so applyContained does not preserve the container invariant for this domain (proofs/Preservation.fst, contained_needs_child_blind). Write canHold over the node's own kind, or its own fields, and never over its children."
                                (idw.ToString(nodew.Id subject))
                                (nodew.KindTag subject)
                                (canHold subject)
                                (List.length kids)
                                (canHold rebuilt)
                                label
                        )
                )

            if
                not (List.isEmpty kids)
                && sameIdentity subject emptied
                && List.isEmpty (nodew.Children emptied)
            then
                notePerturbation "with none" emptied

            if
                sameIdentity subject extended
                && List.length (nodew.Children extended) = List.length kids + 1
            then
                notePerturbation "with one more" extended

            // ---- arm 2: the engine keeps the invariant, over the domain's own witness ----
            // Phase 161 retired the `contained_op` hypothesis, so the only premise left is that the
            // INPUT tree satisfies the invariant. A generator that draws a tree already in violation
            // is not tested by this law, and is not failed by it either.
            let op = rng.Draw(LawKit.genOp nodew idw gen tree)

            if (firstUncontained tree).IsNone then
                match Ops.applyContained canHold nodew idw op tree with
                | Ok post ->
                    probes <- probes + 1

                    match firstUncontained post with
                    | Some offender ->
                        preservation.Check(
                            false,
                            fun () ->
                                at (
                                    sprintf
                                        "the container invariant BROKE across an ACCEPTED %A — node %s (kind %s) holds %d child(ren) in the result and canHold refuses it, though every node with children satisfied canHold in the input"
                                        op
                                        (idw.ToString(nodew.Id offender))
                                        (nodew.KindTag offender)
                                        (List.length (nodew.Children offender))
                                )
                        )
                    | None -> preservation.Saw()
                | Error _ -> ()

            // ---- arm 3: a graft carrying an interior offender is refused, NAMING it (built) ----
            // The offender is the graft's own root — a fresh node given a child, where the domain's
            // own predicate refuses it. That is Phase 140's `cx_nested_graft` shape expressed over
            // the domain's own nodes.
            let shell = rng.Draw(gen.FreshNode treeKeys)
            let shellKey = idw.ToString(nodew.Id shell)
            let inner = rng.Draw(gen.FreshNode(Set.add shellKey treeKeys))
            let graft = nodew.ReplaceChildren shell [ inner ]
            let graftKeys = keysOf graft

            // Evidence only where the witness carried the shape AND the graft is clean by every
            // OTHER clause of `validateInsert` — a candidate that also breaks id uniqueness earns
            // `DuplicateId` first (D38's precedence), and would measure that instead.
            let usable =
                sameIdentity shell graft
                && nodew.Children graft |> List.map (fun c -> idw.ToString(nodew.Id c)) = [ idw.ToString(
                                                                                                nodew.Id inner
                                                                                            ) ]
                && not (canHold graft)
                && List.length (List.distinct graftKeys) = List.length graftKeys
                && graftKeys |> List.forall (fun k -> not (treeKeys.Contains k))

            match
                (if usable then
                     Tree.preorder nodew tree |> List.filter canHold
                 else
                     [])
            with
            | [] -> ()
            | holders ->
                let parent = rng.Choose holders
                grafts <- grafts + 1

                match Ops.applyContained canHold nodew idw (InsertChild(nodew.Id parent, graft)) tree with
                | Error(NotAContainer(named, kindTag)) ->
                    graftRefusal.Check(
                        idw.Equals named (nodew.Id shell) && kindTag = nodew.KindTag shell,
                        fun () ->
                            at (
                                sprintf
                                    "the interior refusal named %s (kind %s), but the offending node is %s (kind %s) — a caller repairs the node the envelope names, so naming another one sends them to the wrong place"
                                    (idw.ToString named)
                                    kindTag
                                    shellKey
                                    (nodew.KindTag shell)
                            )
                    )
                | other ->
                    graftRefusal.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "a graft whose root %s (kind %s) holds a child while canHold refuses it was answered with %A — it must be NotAContainer naming that node (DECISIONS D38; proofs/Preservation.fst, nested_graft_refused)"
                                    shellKey
                                    (nodew.KindTag shell)
                                    other
                            )
                    ))

        LawKit.results [ blind; preservation; graftRefusal ]
        @ [ SampleAdequacy.reached
                "Conformance.containerLaws"
                "built arm"
                seed
                [ "child perturbation", perturbations
                  "invariant probe", probes
                  "interior graft", grafts ] ]

    /// **The container capability's two obligations, certified rather than assumed** (Phase 161).
    ///
    /// Phase 140 proved `Ops.applyContained` preserves the invariant it exists to keep — *every node
    /// with children satisfies `canHold`* — and named the two hypotheses the theorem must carry to
    /// be true of the function that ships. Phase 161 discharged one of them in code (DECISIONS D38:
    /// `validateInsert` walks the graft). The other cannot be discharged by any engine check, and
    /// this family is where it lands instead. Three laws and an adequacy guard:
    ///
    /// - **`canHold` is CHILD-BLIND.** Its type is `'Node -> bool`, so it may read the node's child
    ///   list — and a predicate that does can admit a node at the instant it is checked and refuse
    ///   it the instant it gains a child. No check placed anywhere in the engine repairs that,
    ///   because the predicate's answer changes under the very edit the check licensed. So it is the
    ///   DOMAIN's obligation, sampled here by PERTURBING a drawn node's children — emptied, and
    ///   extended by one — and requiring `canHold` to be unchanged. A domain whose predicate reads
    ///   the child list learns it from its own conformance run rather than from a broken tree.
    /// - **The engine keeps the invariant over the domain's own witness.** Phase 140's theorem is
    ///   about a model of the engine; this is the same sentence sampled against the shipped engine
    ///   with the domain's own nodes, operations and predicate — available to an adopter who has
    ///   neither a prover nor the model.
    /// - **A graft's interior is refused, naming the offender.** Phase 161's widening, sampled the
    ///   same way: a subtree that places children under a node `canHold` refuses is rejected with
    ///   `NotAContainer` carrying THAT node's id and kind tag, not the parent's. A caller repairs
    ///   the node the envelope names, so which node is named is the law, not an implementation
    ///   detail.
    ///
    /// **Opt-in, not folded into `certify`** — the `chainBreakReasonLaws` / `dagBreakReasonLaws`
    /// shape (Phase 147). `certify` runs over any `OpGen`, and `OpGen.CanHold` is an OPTION: folding
    /// this family in would add laws that cannot fail for every domain that leaves it `None`, which
    /// is the vacuity this kit exists to refuse, and would make `certify`'s law count depend on its
    /// input. A domain with a container notion calls this alongside its base run, and
    /// `Conformance.certify`'s results do not depend on whether it does.
    ///
    /// **A domain that declares NO predicate is reported by name rather than skipped** — the
    /// `constructThenEncodeLaws` precedent. Calling this family is a claim to have a container
    /// notion; `CanHold = None` contradicts the claim, and a green report over no predicate would
    /// look exactly like certification of something.
    ///
    /// **The built arms are GUARDED, because whether the witness honours them is drawn.** Each arm
    /// rebuilds a node through `ReplaceChildren` and counts as evidence only where the rebuild was
    /// honoured and the id and kind survived it: the kit deliberately admits a witness whose
    /// `ReplaceChildren` is partial on leaves (`witnessLaws`), and `opAlgebra`'s built collision arm
    /// takes the same care for the same reason. What differs here is that an arm nothing reached is
    /// REPORTED — the family emits a `SampleAdequacy.reached` law over the three arms, so a run that
    /// measured nothing says so with the counts instead of passing quietly.
    let containerLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        match gen.CanHold with
        | Some canHold -> containerLawsOver canHold nodew idw gen seed iterations
        | None ->
            let declared =
                LawKit.LawCell "the domain declares a container predicate (OpGen.CanHold)"

            declared.Check(
                false,
                fun () ->
                    "seed="
                    + string seed
                    + ": OpGen.CanHold is None, so there is no container capability to certify — a domain without one has nothing for this family to say, and answering with a green report would look like certification of a claim nobody made"
            )

            LawKit.results [ declared ]

    /// **The witness surface's own boundary, certified rather than assumed** (Phase 189).
    ///
    /// `Tree.ids` walks `NodeWitness.Children`, so a node a domain holds in a keyed,
    /// non-structural position is invisible to the uniqueness scan, to every theorem in `proofs/`
    /// and to the differential. Uniqueness over those positions is the domain's obligation — the
    /// claims ladder carried it as `witness-surface-scope`, a PERMANENT premise, on the reasoning
    /// that no kit law could discharge it. That reasoning was about the surface the kit could
    /// SEE, and it stopped holding the moment a domain could declare those positions itself. This
    /// family is that declaration and the three laws it buys.
    ///
    /// **What it does NOT do is widen the witness surface.** The engine still cannot reach a
    /// keyed position, `Ops.apply` still refuses only what `Children` reports, and the reasons
    /// `README.md` gives for that stand. What is certified here is the DOMAIN'S check, at the
    /// domain's own witness, over the domain's own generator — which is what the obligation was
    /// always about.
    ///
    /// Three laws, and the pair after the first is where the content is:
    ///
    /// - **The check ACCEPTS a walk with no repeat.** The anti-vacuity arm, and it is first
    ///   because without it `fun _ -> false` passes everything below. Measured only where the
    ///   kit's own walk — every id `Children` reports, plus every id a node declares keyed —
    ///   repeats nothing, because a tree the kit can see a collision in is one the check is
    ///   RIGHT to refuse.
    /// - **The check REFUSES an id held in a keyed position that the witness surface also
    ///   holds.** BUILT, never drawn: `gen.FreshNode`'s contract is an id the tree does not
    ///   carry, so a drawn sample cannot exhibit this and a law quantified over it would certify
    ///   a check that checked nothing. The kit builds the collision through
    ///   `KeyedWitness.PlaceKeyedChild` and splices the rebuilt node back with `Tree.updateNode`.
    /// - **The check REFUSES one id held in two keyed positions.** The other half, and a
    ///   different defect: a check that walks the keyed positions but compares them only against
    ///   the surface passes the law above and fails this one.
    ///
    /// **A domain that declares NO keyed position passes VACUOUSLY and the report says so** —
    /// `HasKeyedChildren` empty everywhere and `PlaceKeyedChild` answering `None` everywhere is a
    /// DECLARATION that there is nothing here to certify, and the adequacy line says that in
    /// those words rather than reporting three laws nothing reached. A domain that declares keyed
    /// positions and gives the kit no way to BUILD one is the other case entirely, and fails the
    /// adequacy guard naming the arms: a claim to have keyed positions is a claim this family can
    /// measure, and an unmeasurable one is not evidence.
    ///
    /// **Opt-in, not folded into `certify`** — the `containerLaws` shape. `certify` runs over any
    /// `OpGen`, and a domain with no keyed positions has nothing for this family to say; folding
    /// it in would add laws that cannot fail for most domains, and would make `certify`'s law
    /// count depend on a witness it does not take.
    let keyedChildrenLaws
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let accepted =
            LawKit.LawCell("the domain's id check accepts a tree whose full walk repeats no id", Some "built arm")

        let surfaceClash =
            LawKit.LawCell(
                "the domain's id check refuses an id held in a keyed position and in the witness surface",
                Some "built arm"
            )

        let twiceKeyed =
            LawKit.LawCell("the domain's id check refuses an id held in two keyed positions", Some "built arm")
        // the three arms, counted so an arm nothing reached is REPORTED rather than assumed
        let mutable cleanWalks = 0
        let mutable surfaceBuilds = 0
        let mutable keyedBuilds = 0
        // and the two counts that tell "declares none" apart from "declares some and hid them"
        let mutable declaredKeyed = 0
        let mutable placementsTaken = 0

        let keyOf (n: 'Node) = idw.ToString(nodew.Id n)

        let keyedKeysOf (n: 'Node) =
            keyw.HasKeyedChildren n |> List.map idw.ToString

        /// The kit's own full walk: every id the witness surface reports, plus every id a node
        /// declares in a keyed position. It is the kit's model of the domain's walk and never the
        /// domain's own — a law that read the domain's check for its own expectation would agree
        /// with it by construction, which is the one thing a conformance law must not do.
        let surfaceKeys (t: 'Node) = Tree.preorder nodew t |> List.map keyOf

        let keyedKeys (t: 'Node) =
            Tree.preorder nodew t |> List.collect keyedKeysOf

        let occurrences (k: string) (ks: string list) =
            ks |> List.filter (fun x -> x = k) |> List.length

        /// Splice a rebuilt node back where it came from. `None` where the witness declined the
        /// rebuild or moved the node's identity under it: a placement that changed the id or the
        /// kind tag is a DECLARATION defect, and counting it as evidence here would localise it to
        /// the wrong place — the F1 lesson `containerLaws` states at its own rebuild guard.
        let spliceIn (original: 'Node) (rebuilt: 'Node) (t: 'Node) : 'Node option =
            if
                idw.Equals (nodew.Id rebuilt) (nodew.Id original)
                && nodew.KindTag rebuilt = nodew.KindTag original
            then
                Tree.updateNode nodew idw (nodew.Id original) (fun _ -> rebuilt) t
            else
                None

        /// Place `id` in a keyed position of `n`, and answer only where the placement TOOK — the
        /// id is in the rebuilt node's own declaration. A witness that answered `Some` and placed
        /// nothing would otherwise be measured as having exhibited a collision it does not carry.
        let place (n: 'Node) (id: 'Id) : 'Node option =
            match keyw.PlaceKeyedChild n id with
            | Some rebuilt when List.contains (idw.ToString id) (keyedKeysOf rebuilt) ->
                placementsTaken <- placementsTaken + 1
                Some rebuilt
            | _ -> None

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let nodes = Tree.preorder nodew tree
            declaredKeyed <- declaredKeyed + List.length (keyedKeys tree)

            // ---- arm 1: a full walk that repeats no id is ACCEPTED (the anti-vacuity arm) ----
            let walk = surfaceKeys tree @ keyedKeys tree

            if List.length (List.distinct walk) = List.length walk then
                cleanWalks <- cleanWalks + 1

                accepted.Check(
                    keyw.IdsUnique tree,
                    fun () ->
                        at (
                            sprintf
                                "%s REFUSED a tree whose full walk (%d id(s), %d of them keyed) repeats nothing — a check that refuses a lawful tree certifies nothing by refusing an unlawful one, and the two laws below would pass for a check that refuses everything"
                                keyw.Surface
                                (List.length walk)
                                (List.length (keyedKeys tree))
                        )
                )

            // ---- arm 2: an id held keyed AND in the witness surface is REFUSED (built) ----
            let holder = rng.Choose nodes
            let victim = rng.Choose nodes
            let victimKey = keyOf victim

            match
                place holder (nodew.Id victim)
                |> Option.bind (fun rb -> spliceIn holder rb tree)
            with
            | Some clashed when
                List.contains victimKey (surfaceKeys clashed)
                && List.contains victimKey (keyedKeys clashed)
                ->
                surfaceBuilds <- surfaceBuilds + 1

                surfaceClash.Check(
                    not (keyw.IdsUnique clashed),
                    fun () ->
                        at (
                            sprintf
                                "%s ACCEPTED a tree holding %s both in the witness surface and in a keyed position of %s (kind %s) — the engine cannot see the keyed one, so nothing else will refuse it, and every theorem about this tree is then about a different tree from the one the domain holds"
                                keyw.Surface
                                victimKey
                                (keyOf holder)
                                (nodew.KindTag holder)
                        )
                )
            | _ -> ()

            // ---- arm 3: one id held in TWO keyed positions is REFUSED (built) ----
            let fresh = rng.Draw(gen.FreshNode(Set.ofList walk))
            let freshId = nodew.Id fresh
            let freshKey = idw.ToString freshId
            let a = rng.Choose nodes
            let b = rng.Choose nodes

            if not (idw.Equals (nodew.Id a) (nodew.Id b)) then
                let built =
                    place a freshId
                    |> Option.bind (fun ra -> spliceIn a ra tree)
                    |> Option.bind (fun t1 ->
                        match Tree.tryFind nodew idw (nodew.Id b) t1 with
                        | Some b1 -> place b1 freshId |> Option.bind (fun rb -> spliceIn b1 rb t1)
                        | None -> None)

                match built with
                | Some doubled when
                    occurrences freshKey (keyedKeys doubled) = 2
                    && not (List.contains freshKey (surfaceKeys doubled))
                    ->
                    keyedBuilds <- keyedBuilds + 1

                    twiceKeyed.Check(
                        not (keyw.IdsUnique doubled),
                        fun () ->
                            at (
                                sprintf
                                    "%s ACCEPTED a tree holding %s in a keyed position of BOTH %s and %s — the id occurs nowhere in the witness surface, so a check that compares the keyed positions against the surface alone passes the law above and lets this one through"
                                    keyw.Surface
                                    freshKey
                                    (keyOf a)
                                    (keyOf b)
                            )
                    )
                | _ -> ())

        if declaredKeyed = 0 && placementsTaken = 0 then
            // Vacuous BY DECLARATION, which is a different thing from an arm nothing reached:
            // the witness was asked for a keyed position on every iteration and answered that
            // it has none. Passing is the honest verdict, and saying which verdict it is — in
            // the adequacy line every consumer's census reads — is what keeps it from looking
            // like three laws certified. The three cells are covered by the built-arm guard, so with
            // no evidence they read through this line rather than reporting "never reached".
            LawKit.results [ accepted; surfaceClash; twiceKeyed ]
            @ [ { Law =
                    SampleAdequacy.lawPrefix "Conformance.keyedChildrenLaws"
                    + "the witness declares NO keyed position, so the collision laws are vacuous BY DECLARATION"
                  Passed = true
                  Counterexample = None } ]
        else
            LawKit.results [ accepted; surfaceClash; twiceKeyed ]
            @ [ SampleAdequacy.reached
                    "Conformance.keyedChildrenLaws"
                    "built arm"
                    seed
                    [ "clean full walk", cleanWalks
                      "keyed id in the witness surface", surfaceBuilds
                      "one id in two keyed positions", keyedBuilds ] ]
