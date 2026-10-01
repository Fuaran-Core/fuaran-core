namespace Fuaran.Core

/// The tree families (Phase 297 split): the witness laws, the op algebra, the structural diff, the container and keyed-children laws.
module internal TreeLaws =

    /// The witness laws (Phase 253) — check the `NodeWitness`/`IdWitness` is well-formed
    /// *before* the algebra laws run, so a defect localises to the accessor that's wrong
    /// instead of surfacing as a downstream `apply ∘ invert` failure (the F1 lesson):
    /// `Children (ReplaceChildren n cs) = cs`, `Id`/`KindTag` preserved under rebuild, the
    /// `IdWitness` round-trip + reflexivity, and (Phase 290) that the witness's two identities
    /// are one relation — `Equals a b ⇔ ToString a = ToString b` — over every drawn pair AND
    /// over BUILT pairs (a drawn id against its case-flipped and whitespace-padded string read
    /// back through `OfString`), because the reference generator never draws the pair that
    /// exhibits a case-insensitive `Equals`. The `ReplaceChildren` laws are checked only
    /// on nodes that `CanHold` children (a leaf is not required to round-trip a child list).
    ///
    /// The rebuild identity — `ReplaceChildren n (Children n) = n` on every holder, which the engine
    /// relies on (`UpdateNode` rebuilds a node over its own children) — is certified by `opAlgebra`
    /// since Phase 297, whose `apply ∘ invert = identity` law runs over the identity update the kit
    /// now draws; it is not a sixth law HERE only because the family's result count is pinned by
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

        let identitiesAgree =
            LawKit.LawCell "IdWitness identities agree (Equals a b ⇔ ToString a = ToString b), drawn and built"

        // Phase 290 — the two identities are ONE relation: `Equals a b ⇔ ToString a = ToString b`.
        // The spine reads ids both ways (`tryFind` / `parentOf` / `updateNode` through `Equals`;
        // `wellFormed` / `graftWellFormed` / `Index` / `Diff` through the string key), so a witness
        // whose `Equals` is coarser than its `ToString` — case-insensitive over a case-preserving
        // string — passes every other law here and then lets `InsertChild(p, node "A")` beside an
        // existing `"a"` through the duplicate check while `updateNode "a"` rewrites both. Refused
        // HERE, by name, rather than downstream by the first duplicate id.
        let askBoth (where: string) (a: 'Id) (b: 'Id) =
            let byEquals = idw.Equals a b
            let byString = idw.ToString a = idw.ToString b

            identitiesAgree.Check(
                (byEquals = byString),
                fun () ->
                    sprintf
                        "%s: Equals says %b but ToString says %b for %s and %s — the witness carries two identities"
                        where
                        byEquals
                        byString
                        (idw.ToString a)
                        (idw.ToString b)
            )

        LawKit.run iterations seed (fun rng i at ->
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
                    )

            // ---- Phase 290: Equals a b ⇔ ToString a = ToString b ----
            // DRAWN: every ordered pair of ids the tree carries. With the reference generator this
            // arm is green for a case-insensitive witness in every family today — a generator that
            // never draws two ids differing only in case cannot exhibit the defect — which is why
            // the BUILT arm below exists and is the one that bites.
            let ids = Tree.ids nodew tree

            for a in ids do
                for b in ids do
                    askBoth (sprintf "seed=%d iter=%d (drawn)" seed i) a b

            // BUILT: perturb each drawn id's string — the string itself (the ⇐ direction: an id
            // `Equals` keeps apart that `ToString` renders alike), its upper- and lower-case forms
            // and a whitespace-padded form (the ⇒ direction: an `Equals` blind to a difference
            // `ToString` shows) — read each back through `OfString`, and ask both identities about
            // the pair. A witness whose `OfString` REFUSES a perturbation (an int id handed
            // `"12 "`) cannot represent that pair at all and so cannot carry the defect for it;
            // the arm reports nothing for that perturbation rather than failing, so the law stays
            // true over what the witness can construct, just narrower.
            for id in ids do
                let s = idw.ToString id

                for s' in List.distinct [ s; s.ToUpperInvariant(); s.ToLowerInvariant(); s + " "; " " + s ] do
                    match
                        (try
                            Some(idw.OfString s')
                         with _ ->
                             None)
                    with
                    | Some p -> askBoth (sprintf "seed=%d iter=%d (built from %s)" seed i s) id p
                    | None -> ())

        LawKit.results [ rcRoundTrip; idPreserved; kindStable; idRoundTrip; identitiesAgree ]

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
    /// **What it does NOT do is widen the witness surface.** `Children` is still what the engine
    /// rebuilds through, and the reasons `README.md` gives for that stand. What is certified here
    /// is the DOMAIN'S check, at the domain's own witness, over the domain's own generator. Since
    /// Phase 286 the same declaration also reaches the engine — `Ops.applyContainedKeyed` refuses a
    /// keyed collision itself — and `keyedApplyLaws` certifies that the engine's refusal and this
    /// check agree; the keyed ids read here are `Tree.keyedIds`, derived from `KeyedChildren`.
    ///
    /// Three laws, and the pair after the first is where the content is:
    ///
    /// - **The check ACCEPTS a walk with no repeat.** The anti-vacuity arm, and it is first
    ///   because without it `fun _ -> false` passes everything below. Measured only where the
    ///   kit's own walk — `Tree.idsKeyed`, every id `Children` reaches plus every id held in or
    ///   below a keyed position — repeats nothing, because a tree the kit can see a collision in is one the check is
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
    /// `KeyedChildren` empty everywhere and `PlaceKeyedChild` answering `None` everywhere is a
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
            Tree.keyedIds nodew keyw n |> List.map idw.ToString

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
            // The keyed walk (Phase 286): every id `Children` reaches, every id a node holds in a
            // keyed position, and every id below one — a keyed node's own subtree included.
            let walk = Tree.idsKeyed nodew keyw tree |> List.map idw.ToString

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
                                "%s ACCEPTED a tree holding %s both in the witness surface and in a keyed position of %s (kind %s) — the unkeyed engine cannot see the keyed one, so nothing on that path will refuse it, and every theorem about this tree is then about a different tree from the one the domain holds"
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
                [ "accepted op", accepted; "IllegalChild", refused ] ]

    /// The reference-integrity laws (Phase 313) — the teeth on `Validator.referenceDefects` /
    /// `forwardReferences` and their families, the reference-aware engine (`Ops.applyReferenced`) and
    /// `Ops.footprintReferenced`, at the domain's own `RefWitness`. The defects are checked against
    /// a naive specification held here (a reference with no declarer; a declaration with no
    /// referrer; the nodes that reach themselves through "refers to a declaration of"; a declaration
    /// later in preorder that is not the referrer's descendant), never against the validator's own
    /// walk. Drawn ops are threaded through the engine:
    ///
    ///   - **agreement** — the engine refuses with `applyContained`'s envelope wherever it refuses,
    ///     and refuses an accepted `RemoveNode` with `StillReferenced` exactly when the tree it would
    ///     build leaves a surviving node's reference dangling, naming every such node;
    ///   - **preservation** — an accepted remove leaves no reference dangling that resolved before;
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
                "the reference engine is applyContained then StillReferenced, exactly where an accepted remove leaves a surviving reference dangling"

        let preservation =
            LawKit.LawCell "an accepted remove leaves no reference dangling that resolved before it"

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

        // The naive specification of one op, step by step as a batch is threaded: the container-aware
        // engine decides, and an accepted remove that leaves a surviving node's resolved reference
        // dangling is `StillReferenced`, naming those nodes in preorder.
        let rec expectedOf (op: SkeletonOp<'Node, 'Id>) (t: 'Node) : Result<'Node, Rejection<'Id>> =
            match op with
            | Batch ops -> ops |> List.fold (fun acc o -> acc |> Result.bind (expectedOf o)) (Ok t)
            | _ ->
                match Ops.applyContained canHold nodew idw op t with
                | Error e -> Error e
                | Ok t1 ->
                    match op with
                    | RemoveNode target ->
                        let was = Set.ofList (dangling t)

                        let fresh =
                            dangling t1 |> List.filter (fun pair -> not (was.Contains pair)) |> List.map fst

                        let referrers =
                            Tree.preorder nodew t
                            |> List.filter (fun n -> List.contains (nodeKey n) fresh)
                            |> List.map nodew.Id

                        if List.isEmpty referrers then
                            Ok t1
                        else
                            Error(StillReferenced(target, referrers))
                    | _ -> Ok t1

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

                let before = Set.ofList (dangling cur)

                let fp = Ops.footprint nodew idw [ op ]
                let fr = Ops.footprintReferenced refw nodew idw [ op ]

                footprintReads.Check(
                    Set.isSubset fp.Reads fr.Reads
                    && fp.StructureWrites = fr.StructureWrites
                    && fp.ContentWrites = fr.ContentWrites
                    && fp.UnknownParentWrites = fr.UnknownParentWrites
                    && (written op |> List.forall (fun r -> fr.Reads.Contains(key r))),
                    fun () -> at (sprintf "footprintReferenced of %A is %A against footprint %A" op fr fp)
                )

                let expected = expectedOf op cur

                agreement.Check(
                    (g = expected),
                    fun () -> at (sprintf "op %A: expected %A, applyReferenced answered %A" op expected g)
                )

                match op, g with
                | RemoveNode _, Ok t' ->
                    removes <- removes + 1

                    preservation.Check(
                        Set.isSubset (Set.ofList (dangling t')) before,
                        fun () -> at (sprintf "the accepted %A left a resolved reference dangling" op)
                    )
                | _, Error(StillReferenced _) -> stillReferenced <- stillReferenced + 1
                | _ -> ()

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
