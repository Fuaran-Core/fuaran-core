namespace Fuaran.Core

/// The concurrency families (Phase 297 split): footprints, merge conflicts, reconciliation, interleavings and arbitration.
module internal ConcurrencyLaws =

    /// The op-script footprint / independence laws (Phase 78) — the teeth on `Ops.footprint` /
    /// `Ops.independent` and the "the edge is computed from the script" claim. Over a seed-replayable
    /// sample it builds two applyable scripts `a`, `b` on a shared tree (each threaded from random
    /// accepted ops) and certifies the conservativity contract:
    ///
    ///  - **soundness** — for every pair `footprint` declares **independent**, the two scripts commute
    ///    under `apply`: `applyAll a` then `applyAll b` equals `applyAll b` then `applyAll a`, compared by
    ///    the Phase-06 content hash (`Tree.encodeHash`), and neither ordering fails. `independent = true`
    ///    is a promise — a single non-commuting independent pair is a soundness break (the over-approx
    ///    leaked to under-approx);
    ///  - **monotonicity** — a sub-script's footprint ⊆ its script's, across all four address sets
    ///    (footprint is a union-fold, so a peephole or reorder that broke it would surface here);
    ///  - **determinism** — `footprint` is a pure function of the script (recomputing it agrees), the
    ///    precondition for seed-replay and for a host to cache a computed lease.
    ///
    /// `'Node` needs equality (it compares result trees). `encode` is the per-node canonical content
    /// encoder for the content-hash equality (the same one a domain feeds `Tree.encodeHash` /
    /// `Function.applyMemo`). Opt-in like `normalizeLaws` — a domain that computes leases runs it.
    let footprintLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let hashOf = Tree.encodeHash nodew encode

        let soundness =
            LawKit.LawCell(
                "footprint soundness (an independent pair commutes under apply — content-hash equal)",
                Some "script-pair independence and op kind"
            )

        let monotonicity =
            LawKit.LawCell "footprint monotonicity (a sub-script's footprint ⊆ its script's)"

        let determinism =
            LawKit.LawCell "footprint determinism (a pure function of the script)"
        // Phase 121 — the soundness law only runs on an INDEPENDENT pair, so a generator that never
        // produced one would certify it green having never applied it once.
        let mutable independentPairs = 0
        // Phase 297 — the kind of every DRAWN op, folded into the guard below.
        let kinds = LawKit.OpKindTally()

        let subsetFp (s: Footprint) (f: Footprint) =
            Set.isSubset s.Reads f.Reads
            && Set.isSubset s.StructureWrites f.StructureWrites
            && Set.isSubset s.ContentWrites f.ContentWrites
            && Set.isSubset s.UnknownParentWrites f.UnknownParentWrites

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let a = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 4 tree)
            let b = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 4 tree)

            let fa = Ops.footprint nodew idw a
            let fb = Ops.footprint nodew idw b

            // determinism: footprint is a pure function of the script.
            determinism.Check(
                Ops.footprint nodew idw a = fa,
                fun () -> at "footprint is not a pure function of the script"
            )

            // monotonicity: a prefix's footprint ⊆ the full script's.
            let k = rng.IntBelow(List.length a + 1)
            let prefix = List.truncate k a

            monotonicity.Check(
                subsetFp (Ops.footprint nodew idw prefix) fa,
                fun () -> at (sprintf "a %d-op prefix footprint ⊄ the full footprint" k)
            )

            // soundness: an independent pair must commute under apply (content-hash equality).
            if Ops.independent fa fb then
                independentPairs <- independentPairs + 1
                let applyAll ops t = Ops.applyAll nodew idw ops t

                let ab =
                    applyAll a tree |> Result.bind (fun ta -> applyAll b ta |> Result.map hashOf)

                let ba =
                    applyAll b tree |> Result.bind (fun tb -> applyAll a tb |> Result.map hashOf)

                soundness.Check(
                    (match ab, ba with
                     | Ok ha, Ok hb when ha = hb -> true
                     | _ -> false),
                    fun () ->
                        at (
                            sprintf "an INDEPENDENT pair did not commute under apply (a=%A b=%A ab=%A ba=%A)" a b ab ba
                        )
                ))

        LawKit.results [ soundness; monotonicity; determinism ]
        @ [ SampleAdequacy.reached
                "Conformance.footprintLaws"
                "script-pair independence and op kind"
                seed
                ([ "independent pair", independentPairs ] @ kinds.Demands) ]

    /// The merge-conflict enumeration laws (Phase 64) — the teeth on `Dag.conflicts` and the
    /// "detection is the negation of #78 independence, decomposed by shape" claim. Over a
    /// seed-replayable sample it builds two applyable scripts `a`, `b` on a shared tree (each threaded
    /// through `apply`, exactly as `footprintLaws` does — so an op is a single-op footprint) and
    /// certifies:
    ///
    ///  - **symmetry** — `conflicts a b` and `conflicts b a` report the same collisions up to swapping
    ///    each pair's `Left`/`Right`: a merge conflict is not directional;
    ///  - **determinism** — `conflicts` is a pure function of its inputs (recomputing agrees), the
    ///    precondition for seed-replay;
    ///  - **agreement with #78 (completeness)** — a pair `(aᵢ, bⱼ)` is reported **iff** its footprints
    ///    are not `Ops.independent`. Grounded in #78's certified soundness this is exactly "two ops that
    ///    would interfere are reported; two that provably commute are not": an independent pair commutes
    ///    (`footprintLaws`) and is never reported, and a reported pair is genuinely dependent.
    ///
    /// Mirrors `footprintLaws`' signature (no `encode` — `conflicts` compares addresses, not trees).
    /// `'Node` needs equality (it compares reported op pairs). A domain that merges branches runs it.
    let mergeConflictLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let fp (op: SkeletonOp<'Node, 'Id>) = Ops.footprint nodew idw [ op ]

        let symmetry =
            LawKit.LawCell "conflicts is symmetric (conflicts a b ≡ conflicts b a up to Left/Right swap)"

        let determinism =
            LawKit.LawCell "conflicts is deterministic (a pure function of its inputs)"

        let agreement =
            LawKit.LawCell "conflicts agrees with #78 (a pair is reported iff its footprints are not independent)"
        // Phase 121 — the agreement law is an `iff` over generated op pairs, so it is satisfied
        // trivially by a sample in which no pair is ever reported (or in which every pair is).
        let mutable reportedPairs = 0
        let mutable unreportedPairs = 0
        // Phase 297 — the kind of every DRAWN op, folded into the guard below.
        let kinds = LawKit.OpKindTally()

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let a = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 4 tree)
            let b = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 4 tree)

            let ab = Dag.conflicts fp a b

            // determinism: a pure function of the inputs.
            determinism.Check(Dag.conflicts fp a b = ab, fun () -> at "conflicts is not a pure function of its inputs")

            // symmetry: conflicts a b ≡ conflicts b a up to each pair's Left/Right swap.
            let ba = Dag.conflicts fp b a

            let norm (swap: bool) (c: MergeConflict<SkeletonOp<'Node, 'Id>>) =
                if swap then
                    (c.Shape, c.Address, c.Right, c.Left)
                else
                    (c.Shape, c.Address, c.Left, c.Right)

            let fwd = ab |> List.map (norm false)
            let bwd = ba |> List.map (norm true)

            // Multiset equality by counting (only equality on `'Node` is demanded, not comparison).
            let sameMultiset (xs: _ list) (ys: _ list) =
                List.length xs = List.length ys
                && xs
                   |> List.forall (fun x ->
                       (xs |> List.filter ((=) x) |> List.length) = (ys |> List.filter ((=) x) |> List.length))

            symmetry.Check(sameMultiset fwd bwd, fun () -> at "conflicts a b ≠ conflicts b a (up to pair swap)")

            // agreement with #78: a pair is reported iff its footprints are not independent.
            for oa in a do
                for ob in b do
                    let reported = Dag.conflicts fp [ oa ] [ ob ] |> List.isEmpty |> not
                    let dependent = not (Ops.independent (fp oa) (fp ob))

                    if reported then
                        reportedPairs <- reportedPairs + 1
                    else
                        unreportedPairs <- unreportedPairs + 1

                    agreement.Check(
                        (reported = dependent),
                        fun () ->
                            at (sprintf "reported=%b but not-independent=%b for (%A, %A)" reported dependent oa ob)
                    ))

        LawKit.results [ symmetry; determinism; agreement ]
        @ [ SampleAdequacy.reached
                "Conformance.mergeConflictLaws"
                "op-pair interference and op kind"
                seed
                ([ "reported pair", reportedPairs; "unreported pair", unreportedPairs ]
                 @ kinds.Demands) ]

    /// The branch-reconciliation laws (Phase 83; the shapes Phase 300) — the teeth on `Dag.reconcile`
    /// and its "fold what commutes, hand conflicts back untouched" contract (GP6). Over a
    /// seed-replayable sample it builds a DAG in one of FOUR shapes, each lane under its OWN actor so a
    /// shared node is shared on purpose and never by an accidental content-id coincidence:
    ///
    ///  - **disjoint** — a common base and two independent branch deltas of accepted ops forked off it;
    ///  - **fast-forward** — branch B chained onto branch A's head, so `headB` descends from `headA`;
    ///  - **duplicate head** — branch A's head named twice;
    ///  - **criss-cross** — two merges of the same two (commuting) branches under two actors, then a
    ///    lane off each, reconciled over `Dag.mergeBase` of the two heads: one of two maximal common
    ///    ancestors, chosen on the tie-break, with the other branch's history shared by both heads.
    ///
    /// and certifies:
    ///
    ///  - **clean-merge replay** — on the disjoint shape, when `reconcile = Ok script`, the script
    ///    applied to the base replays to the SAME tree (Phase-06 content hash) as delta A then delta B
    ///    AND as delta B then delta A: a conflict-free merge folds order-independently;
    ///  - **shared history once** — on the three shared-history shapes, the clean script applied to
    ///    `Dag.replayTo` of the base replays to the same tree as `Dag.replayTo` of a merge node over the
    ///    two heads: history both heads hold is applied exactly once;
    ///  - **footprint cross-validation (#78)** — footprint-independent EXCLUSIVE deltas are always
    ///    conflict-free (`independent ⇒ reconcile = Ok`); the converse is not claimed;
    ///  - **conflicted path is inert** — when the exclusive deltas conflict, `reconcile = Error`
    ///    carrying exactly `Dag.conflicts`' report over them, and nothing is applied (GP6);
    ///  - **determinism / order pinning** — `reconcile` is a pure function of `(base, headA, headB)`,
    ///    and the clean script is the shared region once, then A's exclusive delta, then B's.
    ///
    /// `'Node` needs equality. `encode` is the per-node content encoder (as `footprintLaws`). Mirrors
    /// `footprintLaws` — a domain that reconciles branches runs it.
    ///
    /// `hashFn` is the chain hash the reconciled DAGs are built under — the domain's posture since
    /// Phase 297 (`reconcileLawsWith`); `reconcileLaws` pins `OpStream.defaultHash`, which is what
    /// every run used before.
    let reconcileLawsWith
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (hashFn: HashFn)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        let fp (op: SkeletonOp<'Node, 'Id>) = Ops.footprint nodew idw [ op ]

        // A minimal StreamWitness so the branch deltas live in a REAL DAG (append needs Encode for the
        // content id; the shared-history law replays through Apply). Encode is a structural
        // fingerprint of the op — enough for distinct nodes to get distinct content ids.
        let rec encOp (op: SkeletonOp<'Node, 'Id>) : string =
            match op with
            | InsertChild(p, node) ->
                "I|"
                + idw.ToString p
                + "|"
                + (Tree.preorder nodew node |> List.map encode |> String.concat ",")
            | RemoveNode t -> "R|" + idw.ToString t
            | MoveNode(t, np) -> "M|" + idw.ToString t + "|" + idw.ToString np
            | ReorderChildren(p, order) ->
                "O|"
                + idw.ToString p
                + "|"
                + (order |> List.map idw.ToString |> String.concat ",")
            | Batch inner -> "B|" + (inner |> List.map encOp |> String.concat ";")
            | UpdateNode node -> "U|" + encode node

        let sw: StreamWitness<SkeletonOp<'Node, 'Id>, 'Node, Rejection<'Id>> =
            { Apply = fun op st -> Ops.applyContained canHold nodew idw op st
              Encode = encOp
              Decode = fun _ -> Error "reconcileLaws: decode unused" }

        let applyAll (ops: SkeletonOp<'Node, 'Id> list) (t: 'Node) =
            ops
            |> List.fold (fun acc op -> acc |> Result.bind (fun s -> Ops.applyContained canHold nodew idw op s)) (Ok t)

        let clean =
            LawKit.LawCell(
                "reconcile clean fold replays order-independently (content-hash equal)",
                Some "reconcile outcome"
            )

        let sharedOnce =
            LawKit.LawCell(
                "reconcile applies shared history once (a clean fast-forward, duplicate-head or criss-cross script replays to Dag.replayTo of the merge node)",
                Some "reconcile shape"
            )

        let cross =
            LawKit.LawCell(
                "reconcile is conflict-free when the deltas are footprint-independent (#78 cross-validation)",
                Some "delta-pair independence and op kind"
            )

        let conflicted =
            LawKit.LawCell(
                "reconcile hands back Dag.conflicts' report on conflict (nothing applied)",
                Some "reconcile outcome"
            )

        let determinism =
            LawKit.LawCell "reconcile is deterministic + order-pinned (pure fn of (base, headA, headB))"
        // Phase 121 — three of the four laws below only run on a sample that reached their branch:
        // the clean-fold law needs an `Ok`, the conflicted-path law needs an `Error`, and the
        // cross-validation law needs a footprint-independent delta pair. A run that reached only
        // one of them certifies the other two green having never applied them.
        let mutable cleanFolds = 0
        let mutable conflictedFolds = 0
        let mutable independentDeltas = 0
        // Phase 300 — the shape every trial was built in.
        let mutable disjoint = 0
        let mutable fastForward = 0
        let mutable duplicateHead = 0
        let mutable crissCross = 0
        // Phase 297 — the kind of every DRAWN op, folded into the second guard below.
        let kinds = LawKit.OpKindTally()
        // The no-op the base node and the merge nodes carry, so `Dag.replayTo` of either is the
        // history above it and nothing else.
        let noOp: SkeletonOp<'Node, 'Id> = Batch []

        // Chain a script onto `parent` under `actor`, returning the new head (parent itself when the
        // script is empty).
        let chain
            (actor: string)
            (ops: SkeletonOp<'Node, 'Id> list)
            (parent: string)
            (d0: Dag.T<SkeletonOp<'Node, 'Id>>)
            =
            let mutable head = parent
            let mutable d = d0

            for op in ops do
                let id, d' = Dag.append hashFn sw (Human actor) op head d
                head <- id
                d <- d'

            head, d

        LawKit.run iterations seed (fun rng i at ->
            let tree = rng.Draw gen.Tree
            let a = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 4 tree)
            let b = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 4 tree)

            // a genesis base node carrying the no-op (it is in the base closure, which the
            // partition excludes), then branch A forked off it under its own actor.
            let genesis, d1 = Dag.append hashFn sw (Human "base") noOp "" Dag.empty
            let headA0, d2 = chain "lane-a" a genesis d1

            // The shape, cycled by iteration. A criss-cross is built only over two NON-EMPTY
            // branches that commute, and only when the merged tree admits a lane off each: a merge of
            // two conflicting branches is a history whose own replay order is a tie-break, which no
            // reconcile can make unambiguous (DECISIONS: the subtraction rule). Its second branch is
            // therefore redrawn, a bounded number of times, until it commutes with the first; failing
            // that the trial falls back to the disjoint shape, on the same draw.
            let commutesWithA (s: SkeletonOp<'Node, 'Id> list) =
                not (List.isEmpty s)
                && Ops.independent (Ops.footprint nodew idw a) (Ops.footprint nodew idw s)

            let crissB =
                if i % 4 = 3 && not (List.isEmpty a) then
                    let rec redraw k =
                        if k = 0 then
                            None
                        else
                            let s = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 2 tree)
                            if commutesWithA s then Some s else redraw (k - 1)

                    if commutesWithA b then Some b else redraw 16
                else
                    None

            let crissTree =
                match crissB with
                | Some cb ->
                    match applyAll (a @ cb) tree with
                    | Ok t -> Some(cb, t)
                    | Error _ -> None
                | None -> None

            // (shape, base, headA, headB, dag, expected exclusive deltas, expected shared region)
            let shapeName, baseId, headA, headB, dag, exclA, exclB, shared =
                match i % 4, crissTree with
                | 1, _ ->
                    let headB, dag = chain "lane-b" b headA0 d2
                    "fast-forward", genesis, headA0, headB, dag, [], b, a
                | 2, _ -> "duplicate-head", genesis, headA0, headA0, d2, a, [], []
                | 3, Some(b, merged) ->
                    let headB0, d3 = chain "lane-b" b genesis d2
                    let m1, d4 = Dag.merge hashFn sw (Human "merge-1") noOp headA0 headB0 d3
                    let m2, d5 = Dag.merge hashFn sw (Human "merge-2") noOp headA0 headB0 d4
                    let c = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 3 merged)
                    let d = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 3 merged)
                    let h1, d6 = chain "lane-a" c m1 d5
                    let h2, dag = chain "lane-b" d m2 d6
                    // `mergeBase` picks one of the two maximal common ancestors; the other branch is
                    // history both heads hold above it.
                    let mb = Dag.mergeBase dag h1 h2 |> Option.defaultValue genesis
                    let other = if mb = headA0 then b else a
                    "criss-cross", mb, h1, h2, dag, noOp :: c, noOp :: d, other
                | _ ->
                    let headB, dag = chain "lane-b" b genesis d2
                    "disjoint", genesis, headA0, headB, dag, a, b, []

            match shapeName with
            | "fast-forward" -> fastForward <- fastForward + 1
            | "duplicate-head" -> duplicateHead <- duplicateHead + 1
            | "criss-cross" -> crissCross <- crissCross + 1
            | _ -> disjoint <- disjoint + 1

            let result = Dag.reconcile fp dag baseId headA headB

            // determinism + order pinning: a pure function of (base, headA, headB); clean script pinned.
            determinism.Check(
                Dag.reconcile fp dag baseId headA headB = result,
                fun () -> at "reconcile is not a pure function of its inputs"
            )

            match result with
            | Ok script ->
                cleanFolds <- cleanFolds + 1

                determinism.Check(
                    (script = shared @ exclA @ exclB),
                    fun () ->
                        at (
                            "clean script ≠ shared region ++ exclusive A ++ exclusive B (order pin, "
                            + shapeName
                            + ")"
                        )
                )

                if shapeName = "disjoint" then
                    // clean-merge replay: script ≡ A-then-B ≡ B-then-A on the base tree (content hash).
                    let viaScript = applyAll script tree |> Result.map hashOf
                    let ab = applyAll a tree |> Result.bind (applyAll b) |> Result.map hashOf
                    let ba = applyAll b tree |> Result.bind (applyAll a) |> Result.map hashOf

                    clean.Check(
                        (match viaScript, ab, ba with
                         | Ok hs, Ok hab, Ok hba when hs = hab && hab = hba -> true
                         | _ -> false),
                        fun () ->
                            at (
                                sprintf
                                    "a conflict-free merge did not fold order-independently (script=%A ab=%A ba=%A)"
                                    viaScript
                                    ab
                                    ba
                            )
                    )
                else
                    // shared history once: the script from replayTo(base) ≡ replayTo(merge of the heads).
                    let m, dm = Dag.merge hashFn sw (Human "merge") noOp headA headB dag

                    let viaScript =
                        Dag.replayTo sw tree dm baseId
                        |> Result.mapError snd
                        |> Result.bind (applyAll script)
                        |> Result.map hashOf

                    let viaMerge = Dag.replayTo sw tree dm m |> Result.mapError snd |> Result.map hashOf

                    sharedOnce.Check(
                        (viaScript = viaMerge),
                        fun () ->
                            at (
                                sprintf
                                    "a clean %s script did not replay to the merge node (script=%A merge=%A)"
                                    shapeName
                                    viaScript
                                    viaMerge
                            )
                    )
            | Error cs ->
                conflictedFolds <- conflictedFolds + 1

                // the conflicted path returns exactly Dag.conflicts' report, nothing applied.
                conflicted.Check(
                    (cs = Dag.conflicts fp exclA exclB),
                    fun () -> at "Error payload ≠ Dag.conflicts report"
                )

            // footprint cross-validation: footprint-independent exclusive deltas ⇒ conflict-free (Ok).
            if Ops.independent (Ops.footprint nodew idw exclA) (Ops.footprint nodew idw exclB) then
                independentDeltas <- independentDeltas + 1

                cross.Check(
                    (match result with
                     | Ok _ -> true
                     | Error _ -> false),
                    fun () -> at "footprint-independent deltas were NOT reconciled clean"
                ))

        LawKit.results [ clean; sharedOnce; cross; conflicted; determinism ]
        @ [ SampleAdequacy.reached
                "Conformance.reconcileLawsWith"
                "reconcile outcome"
                seed
                [ "clean fold", cleanFolds; "conflicted fold", conflictedFolds ]
            SampleAdequacy.reached
                "Conformance.reconcileLawsWith"
                "reconcile shape"
                seed
                [ "disjoint", disjoint
                  "fast-forward", fastForward
                  "duplicate head", duplicateHead
                  "criss-cross", crissCross ]
            SampleAdequacy.reached
                "Conformance.reconcileLawsWith"
                "delta-pair independence and op kind"
                seed
                ([ "independent delta pair", independentDeltas ] @ kinds.Demands) ]


    /// `reconcileLawsWith` pinned to `OpStream.defaultHash` — the laws and the guard are its.
    let reconcileLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        reconcileLawsWith nodew idw gen encode OpStream.defaultHash seed iterations

    // ---- confluence / interleaving law (Phase 80) ----
    // The coordination claim the agent-fleet substrate rests on: op-scripts `Ops.independent`
    // declares disjoint replay to the same tree under EVERY interleaving of their individual ops —
    // confluence. `footprintLaws` (Phase 78) proves the two whole-script sequential orders commute;
    // concurrent appenders produce arbitrary op-level interleavings, which is what this law samples.
    // Interleavings grow as C(m+n, m), so the law checks a bounded, deterministic, seed-replayable
    // sample (the two sequential extremes + uniform riffles) rather than enumerating.

    /// A uniform random interleaving of two lists, preserving each list's internal order: at every
    /// step the next element is drawn from either remainder with probability proportional to its
    /// length (the classic riffle — uniform over all C(m+n, m) interleavings). Deterministic in the
    /// rng, like `ConfRng.shuffle`.
    let private riffle (xs: 'a list) (ys: 'a list) (r0: ConfRng.T) : 'a list * ConfRng.T =
        let mutable a = xs
        let mutable b = ys
        let mutable acc = []
        let mutable r = r0

        while not (List.isEmpty a) || not (List.isEmpty b) do
            let na = List.length a
            let pick, r' = ConfRng.intBelow (na + List.length b) r
            r <- r'

            if pick < na then
                acc <- List.head a :: acc
                a <- List.tail a
            else
                acc <- List.head b :: acc
                b <- List.tail b

        List.rev acc, r

    /// The confluence / interleaving laws (Phase 80) with an **injectable footprint** — the teeth
    /// seam: a test injects a defective `footprintOf` (one that falsely declares dependent pairs
    /// independent) and watches the law bite. Domains call `concurrencyLaws`, which pins this to
    /// the real `Ops.footprint`.
    ///
    /// Over a seed-replayable sample it builds two applyable scripts `a`, `b` on a shared tree (the
    /// `footprintLaws` construction) and, for every pair `footprintOf` + `Ops.independent` declares
    /// **independent**, checks each sampled interleaving (the two sequential extremes `a @ b` /
    /// `b @ a` plus 8 uniform riffles — the documented bound):
    ///
    ///  - **interleaving totality** — every sampled interleaving applies cleanly (no rejection):
    ///    independence must survive any op-level schedule, not just whole-script sequencing;
    ///  - **confluence** — every sampled interleaving replays to the content-hash-equal tree
    ///    (the Phase-06 encoder hash) of the sequential `a @ b` reference order;
    ///  - **coverage** — the sample exercised at least one non-empty independent pair, and every
    ///    op kind (a vacuity guard, in `SampleAdequacy`'s words since Phase 297: a run whose
    ///    generator never yields an independent pair certifies nothing, and says so instead of
    ///    reporting a hollow green).
    ///
    /// **Honesty boundary (the Phase 52 discipline): sufficiency, not necessity.** Independence is
    /// *sufficient* for confluence, never *necessary* — a pair NOT declared independent is
    /// **skipped, not asserted** (a dependent pair may or may not commute; the law makes no claim
    /// about it). See STABILITY.md "Confluence / interleaving law".
    let concurrencyLawsWith
        (footprintOf: SkeletonOp<'Node, 'Id> list -> Footprint)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let hashOf = Tree.encodeHash nodew encode
        // The documented bound: 8 sampled riffles + the two sequential extremes per pair.
        let riffleSamples = 8

        let totality =
            LawKit.LawCell(
                "interleaving totality (every sampled interleaving of an independent pair applies cleanly)",
                Some "independent pair (coverage) and op kind"
            )

        let confluence =
            LawKit.LawCell(
                "confluence (every sampled interleaving of an independent pair replays content-hash-equal)",
                Some "independent pair (coverage) and op kind"
            )

        let mutable independentPairs = 0
        // Phase 297 — the kind of every DRAWN op, folded into the coverage guard below.
        let kinds = LawKit.OpKindTally()

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let a = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 4 tree)
            let b = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 4 tree)

            if
                not (List.isEmpty a)
                && not (List.isEmpty b)
                && Ops.independent (footprintOf a) (footprintOf b)
            then
                independentPairs <- independentPairs + 1

                let mutable interleavings = [ a @ b; b @ a ]

                for _ in 1..riffleSamples do
                    let ops, r' = riffle a b rng.State
                    rng.State <- r'
                    interleavings <- ops :: interleavings

                match Ops.applyAll nodew idw (a @ b) tree |> Result.map hashOf with
                | Error rej ->
                    totality.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "the sequential a@b order of an INDEPENDENT pair failed to apply (%A; a=%A b=%A)"
                                    rej
                                    a
                                    b
                            )
                    )
                | Ok refHash ->
                    for ops in interleavings do
                        match Ops.applyAll nodew idw ops tree with
                        | Error rej ->
                            totality.Check(
                                false,
                                fun () ->
                                    at (
                                        sprintf
                                            "an interleaving of an INDEPENDENT pair failed to apply (%A; a=%A b=%A ops=%A)"
                                            rej
                                            a
                                            b
                                            ops
                                    )
                            )
                        | Ok t ->
                            totality.Saw()

                            confluence.Check(
                                hashOf t = refHash,
                                fun () ->
                                    at (
                                        sprintf
                                            "an interleaving of an INDEPENDENT pair replayed to a different tree (a=%A b=%A ops=%A)"
                                            a
                                            b
                                            ops
                                    )
                            ))

        LawKit.results [ totality; confluence ]
        @ [ SampleAdequacy.reached
                "Conformance.concurrencyLawsWith"
                "independent pair (coverage) and op kind"
                seed
                ([ "independent pair", independentPairs ] @ kinds.Demands) ]

    /// The confluence / interleaving laws (Phase 80) pinned to the real `Ops.footprint` — the shape
    /// a domain runs. See `concurrencyLawsWith` for the law text, the sampling bound, and the
    /// sufficiency-not-necessity honesty boundary.
    let concurrencyLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        concurrencyLawsWith (Ops.footprint nodew idw) nodew idw gen encode seed iterations

    // ---- proposal arbitration (Phase 85) ----
    // The teeth on `Arbitration.arbitrate`: a deterministic, total partition of N op-script
    // proposals against one base tree — permutation-invariant, a pairwise-independent accepted
    // set, every rejection typed + actionable (GP5), and the accepted scripts confluent in
    // any order.

    /// The proposal-arbitration laws (Phase 85) — over `Arbitration.arbitrate` (which lived at
    /// `AiSurface.arbitrate` until Phase 192; this family's name and signature did not move).
    /// Certifies:
    /// **determinism + permutation invariance** (arbitrating the same proposals shuffled yields
    /// the identical `Arbitration` — the pinned ascending-id order decides, never input order);
    /// **total partition** (every input proposal lands in exactly one of accepted / rejected —
    /// nothing dropped, nothing duplicated); **pairwise independence** (every accepted pair's
    /// footprints are `Ops.independent`); **rejection actionability** (an `Inapplicable`
    /// carries exactly the `Ops.canApplyAll` envelope against the base; a `Conflicts` cites a
    /// non-empty subset of ACCEPTED ids each of which genuinely interferes); and **any-order
    /// confluence** (the accepted scripts apply green in the pinned order, its reverse, and a
    /// random shuffle — all to the same content-hashed tree, and `MergedScript` reproduces it);
    /// and **id uniqueness is the invariance hypothesis** (Phase 157 — `Arbitration.duplicateIds`
    /// names exactly the ids carried more than once, it is empty on every set the permutation
    /// law above is certified over, and a proposal set carrying one id twice makes arrival order
    /// OBSERVABLE: the same proposals in two orders arbitrate differently. `proofs/Arbitrate.fst`
    /// proves the invariance under that hypothesis and that it cannot be dropped).
    /// The confluence law asserts the whole-script any-order claim directly; `concurrencyLaws`
    /// (Phase 80) is the stronger op-level-interleaving form of the same claim for independent
    /// pairs — a domain that arbitrates runs both. Seed-replayable; `'Node` needs equality.
    /// `encode` is the per-node canonical encoder for the content-hash comparison (as
    /// `footprintLaws`). Opt-in like `footprintLaws` — a domain that arbitrates proposals
    /// runs it.
    let arbitrationLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let hashOf = Tree.encodeHash nodew encode

        let permutation =
            LawKit.LawCell "arbitrate determinism + input-permutation invariance (the pinned order decides)"

        let partition =
            LawKit.LawCell "arbitrate is a total partition (every proposal lands in exactly one bucket)"

        let independence =
            LawKit.LawCell "the accepted set is pairwise independent (Ops.independent)"

        let actionability =
            LawKit.LawCell
                "every rejection is actionable (Inapplicable = the canApplyAll envelope; Conflicts cites interfering accepted ids)"

        let confluence =
            LawKit.LawCell "the accepted scripts apply confluently in any order (the whole-script any-order claim)"
        // Phase 157 — the id-uniqueness hypothesis. `twinsSeen` is its own vacuity guard: the
        // observability half only runs on a set holding an applicable, self-interfering proposal.
        let uniqueness =
            LawKit.LawCell
                "id uniqueness is the invariance hypothesis (duplicateIds is exact and empty on the certified sets; a repeated id makes arrival order observable)"

        let mutable twinsSeen = 0
        // Phase 121 — pairwise independence and any-order confluence are trivially true of an EMPTY
        // accepted set, and the actionability law quantifies over rejections. A sample that never
        // accepted, or never rejected, certifies those green having never applied them.
        let mutable acceptedSeen = 0
        let mutable rejectedSeen = 0
        // Phase 297 — the kind of every DRAWN op, folded into the guard below.
        let kinds = LawKit.OpKindTally()

        let mkProposal id ops : OpScriptProposal<'Node, 'Id> =
            { Id = id
              Holder = sprintf "agent-%d" id
              Ops = ops }

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let extra = rng.IntBelow 4
            let count = extra + 2 // 2..5 proposals

            // Proposals off one base: applyable scripts (which frequently share parents, so
            // conflicts arise naturally) + ~1-in-4 corrupted into a provably-inapplicable
            // script (an op addressing an id the base tree does not carry).
            let mutable proposals = []

            for k in 1..count do
                let script = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 3 tree)
                let corrupt = rng.IntBelow 4

                let ops =
                    if corrupt = 0 then
                        let baseIds = Tree.ids nodew tree |> List.map idw.ToString |> Set.ofList
                        let ghost = rng.Draw(gen.FreshNode baseIds)
                        script @ [ RemoveNode(nodew.Id ghost) ]
                    else
                        script

                proposals <- proposals @ [ mkProposal k ops ]

            let result = Arbitration.arbitrate nodew idw tree proposals

            // determinism + permutation invariance: shuffled input ⇒ identical Arbitration.
            let shuffled = rng.Shuffle proposals

            permutation.Check(
                Arbitration.arbitrate nodew idw tree shuffled = result
                && Arbitration.arbitrate nodew idw tree proposals = result,
                fun () -> at "arbitrate is not deterministic / permutation-invariant"
            )

            // Phase 157 — id uniqueness IS the permutation law's hypothesis. The check is held to
            // an independent recount; it is empty on the set the law above just ran over; and a
            // twin — the same id and script under another holder — makes arrival order observable.
            let recount (ps: OpScriptProposal<'Node, 'Id> list) =
                let ids = ps |> List.map (fun p -> p.Id)

                ids
                |> List.filter (fun x -> (ids |> List.filter (fun y -> y = x) |> List.length) > 1)
                |> List.distinct
                |> List.sort

            uniqueness.Check(
                Arbitration.duplicateIds proposals = [],
                fun () -> at "duplicateIds is non-empty on an id-unique proposal set"
            )

            let selfInterfering (p: OpScriptProposal<'Node, 'Id>) =
                match Ops.canApplyAll nodew idw p.Ops tree with
                | Error _ -> false
                | Ok() ->
                    let fp = Ops.footprint nodew idw p.Ops
                    not (Ops.independent fp fp)

            match proposals |> List.tryFind selfInterfering with
            | None -> ()
            | Some p ->
                twinsSeen <- twinsSeen + 1
                let twin = { p with Holder = p.Holder + "-twin" }
                let twinLast = proposals @ [ twin ]
                let twinFirst = twin :: proposals

                uniqueness.Check(
                    Arbitration.duplicateIds twinLast = [ p.Id ]
                    && Arbitration.duplicateIds twinLast = recount twinLast
                    && Arbitration.duplicateIds twinFirst = recount twinFirst,
                    fun () -> at (sprintf "duplicateIds did not name exactly the repeated id %d" p.Id)
                )

                uniqueness.Check(
                    Arbitration.arbitrate nodew idw tree twinLast
                    <> Arbitration.arbitrate nodew idw tree twinFirst,
                    fun () ->
                        at (
                            sprintf
                                "a repeated id (%d) left arrival order unobservable — the hypothesis would be decoration"
                                p.Id
                        )
                )

            // total partition: accepted + rejected = input, each exactly once.
            let acceptedIds = result.Accepted |> List.map (fun p -> p.Id)
            let rejectedIds = result.Rejected |> List.map (fun (p, _) -> p.Id)
            acceptedSeen <- acceptedSeen + List.length acceptedIds
            rejectedSeen <- rejectedSeen + List.length rejectedIds
            let inputIds = proposals |> List.map (fun p -> p.Id) |> List.sort

            partition.Check(
                List.sort (acceptedIds @ rejectedIds) = inputIds,
                fun () -> at "accepted+rejected ≠ input (dropped or duplicated)"
            )

            // pairwise independence of the accepted set.
            let acceptedFps =
                result.Accepted |> List.map (fun p -> p.Id, Ops.footprint nodew idw p.Ops)

            let pairwise =
                acceptedFps
                |> List.forall (fun (ida, fa) ->
                    acceptedFps |> List.forall (fun (idb, fb) -> ida = idb || Ops.independent fa fb))

            independence.Check(pairwise, fun () -> at "the accepted set is not pairwise independent")

            // rejection actionability (GP5).
            for p, reason in result.Rejected do
                match reason with
                | Inapplicable(ix, rej) ->
                    actionability.Check(
                        Ops.canApplyAll nodew idw p.Ops tree = Error(ix, rej),
                        fun () -> at (sprintf "Inapplicable ≠ the canApplyAll envelope (proposal %d)" p.Id)
                    )
                | Conflicts ids ->
                    let fp = Ops.footprint nodew idw p.Ops

                    let citesInterferingAccepted =
                        not (List.isEmpty ids)
                        && ids
                           |> List.forall (fun cid ->
                               match acceptedFps |> List.tryFind (fun (aid, _) -> aid = cid) with
                               | Some(_, afp) -> not (Ops.independent fp afp)
                               | None -> false)

                    actionability.Check(
                        citesInterferingAccepted,
                        fun () ->
                            at (sprintf "Conflicts cites a non-accepted or non-interfering id (proposal %d)" p.Id)
                    )

            // any-order confluence: pinned, reversed, and shuffled application orders all
            // succeed and agree (content hash) — and MergedScript reproduces the same tree.
            let scripts = result.Accepted |> List.map (fun p -> p.Ops)

            let applyIn (order: SkeletonOp<'Node, 'Id> list list) =
                order
                |> List.fold (fun acc s -> acc |> Result.bind (Ops.applyAll nodew idw s)) (Ok tree)
                |> Result.map hashOf

            let shuffledScripts = rng.Shuffle scripts

            let viaMerged = Ops.applyAll nodew idw result.MergedScript tree |> Result.map hashOf

            confluence.Check(
                (match applyIn scripts, applyIn (List.rev scripts), applyIn shuffledScripts, viaMerged with
                 | Ok a, Ok b, Ok c, Ok d when a = b && b = c && c = d -> true
                 | _ -> false),
                fun () -> at "the accepted scripts did not apply confluently"
            ))

        // Phase 157 — the observability half is the law's own vacuity guard: a sample that never
        // produced a twin certifies the hypothesis by decoration, and says so. An in-loop
        // counterexample, being first, takes precedence.
        if twinsSeen = 0 then
            uniqueness.Fail(
                sprintf
                    "seed=%d: %d iterations produced no applicable self-interfering proposal to twin — the observability half never ran"
                    seed
                    iterations
            )

        LawKit.results [ permutation; partition; independence; actionability; confluence; uniqueness ]
        @ [ SampleAdequacy.reached
                "Conformance.arbitrationLaws"
                "arbitration bucket and op kind"
                seed
                ([ "accepted proposal", acceptedSeen; "rejected proposal", rejectedSeen ]
                 @ kinds.Demands) ]
