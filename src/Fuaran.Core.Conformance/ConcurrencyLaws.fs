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
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        let mutable rng = ConfRng.ofSeed seed
        let mutable soundness = None
        let mutable monotonicity = None
        let mutable determinism = None
        // Phase 121 — the soundness law only runs on an INDEPENDENT pair, so a generator that never
        // produced one would certify it green having never applied it once.
        let mutable independentPairs = 0

        // Thread up to `n` random ops through `apply`, keeping the accepted ones — an applyable script.
        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = LawKit.genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        let subsetFp (s: Footprint) (f: Footprint) =
            Set.isSubset s.Reads f.Reads
            && Set.isSubset s.StructureWrites f.StructureWrites
            && Set.isSubset s.ContentWrites f.ContentWrites
            && Set.isSubset s.UnknownParentWrites f.UnknownParentWrites

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let a, r2 = collectScript 4 tree r1
            let b, r3 = collectScript 4 tree r2
            rng <- r3

            let fa = Ops.footprint nodew idw a
            let fb = Ops.footprint nodew idw b

            // determinism: footprint is a pure function of the script.
            if Ops.footprint nodew idw a <> fa && determinism.IsNone then
                determinism <- Some(sprintf "seed=%d iter=%d: footprint is not a pure function of the script" seed i)

            // monotonicity: a prefix's footprint ⊆ the full script's.
            let k, r4 = ConfRng.intBelow (List.length a + 1) rng
            rng <- r4
            let prefix = List.truncate k a

            if not (subsetFp (Ops.footprint nodew idw prefix) fa) && monotonicity.IsNone then
                monotonicity <- Some(sprintf "seed=%d iter=%d: a %d-op prefix footprint ⊄ the full footprint" seed i k)

            // soundness: an independent pair must commute under apply (content-hash equality).
            if Ops.independent fa fb then
                independentPairs <- independentPairs + 1
                let applyAll ops t = Ops.applyAll nodew idw ops t

                let ab =
                    applyAll a tree |> Result.bind (fun ta -> applyAll b ta |> Result.map hashOf)

                let ba =
                    applyAll b tree |> Result.bind (fun tb -> applyAll a tb |> Result.map hashOf)

                match ab, ba with
                | Ok ha, Ok hb when ha = hb -> ()
                | _ ->
                    if soundness.IsNone then
                        soundness <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: an INDEPENDENT pair did not commute under apply (a=%A b=%A ab=%A ba=%A)"
                                    seed
                                    i
                                    a
                                    b
                                    ab
                                    ba
                            )

        [ { Law = "footprint soundness (an independent pair commutes under apply — content-hash equal)"
            Passed = soundness.IsNone
            Counterexample = soundness }
          { Law = "footprint monotonicity (a sub-script's footprint ⊆ its script's)"
            Passed = monotonicity.IsNone
            Counterexample = monotonicity }
          { Law = "footprint determinism (a pure function of the script)"
            Passed = determinism.IsNone
            Counterexample = determinism }
          SampleAdequacy.reached
              "footprintLaws"
              "script-pair independence"
              seed
              [ "independent pair", independentPairs ] ]

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
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let fp (op: SkeletonOp<'Node, 'Id>) = Ops.footprint nodew idw [ op ]
        let mutable rng = ConfRng.ofSeed seed
        let mutable symmetryCx = None
        let mutable determinismCx = None
        let mutable agreementCx = None
        // Phase 121 — the agreement law is an `iff` over generated op pairs, so it is satisfied
        // trivially by a sample in which no pair is ever reported (or in which every pair is).
        let mutable reportedPairs = 0
        let mutable unreportedPairs = 0

        // Thread up to `n` random ops through `apply`, keeping the accepted ones — an applyable script.
        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = LawKit.genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let a, r2 = collectScript 4 tree r1
            let b, r3 = collectScript 4 tree r2
            rng <- r3

            let ab = Dag.conflicts fp a b

            // determinism: a pure function of the inputs.
            if Dag.conflicts fp a b <> ab && determinismCx.IsNone then
                determinismCx <- Some(sprintf "seed=%d iter=%d: conflicts is not a pure function of its inputs" seed i)

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

            if not (sameMultiset fwd bwd) && symmetryCx.IsNone then
                symmetryCx <- Some(sprintf "seed=%d iter=%d: conflicts a b ≠ conflicts b a (up to pair swap)" seed i)

            // agreement with #78: a pair is reported iff its footprints are not independent.
            for oa in a do
                for ob in b do
                    let reported = Dag.conflicts fp [ oa ] [ ob ] |> List.isEmpty |> not
                    let dependent = not (Ops.independent (fp oa) (fp ob))

                    if reported then
                        reportedPairs <- reportedPairs + 1
                    else
                        unreportedPairs <- unreportedPairs + 1

                    if reported <> dependent && agreementCx.IsNone then
                        agreementCx <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: reported=%b but not-independent=%b for (%A, %A)"
                                    seed
                                    i
                                    reported
                                    dependent
                                    oa
                                    ob
                            )

        [ { Law = "conflicts is symmetric (conflicts a b ≡ conflicts b a up to Left/Right swap)"
            Passed = symmetryCx.IsNone
            Counterexample = symmetryCx }
          { Law = "conflicts is deterministic (a pure function of its inputs)"
            Passed = determinismCx.IsNone
            Counterexample = determinismCx }
          { Law = "conflicts agrees with #78 (a pair is reported iff its footprints are not independent)"
            Passed = agreementCx.IsNone
            Counterexample = agreementCx }
          SampleAdequacy.reached
              "mergeConflictLaws"
              "op-pair interference"
              seed
              [ "reported pair", reportedPairs; "unreported pair", unreportedPairs ] ]

    /// The branch-reconciliation laws (Phase 83) — the teeth on `Dag.reconcile` and its "fold what
    /// commutes, hand conflicts back untouched" contract (GP6). Over a seed-replayable sample it builds
    /// a fork DAG (a common base + two independent branch deltas of accepted ops) and certifies:
    ///
    ///  - **clean-merge replay** — when `reconcile = Ok script`, the script applied to the base replays
    ///    to the SAME tree (Phase-06 content hash) as delta A then delta B AND as delta B then delta A:
    ///    a conflict-free merge folds order-independently (the pin is canonical form, not semantics);
    ///  - **footprint cross-validation (#78)** — footprint-independent deltas are always conflict-free
    ///    (`independent ⇒ reconcile = Ok`); the converse is not claimed (footprints over-approximate);
    ///  - **conflicted path is inert** — when the deltas conflict, `reconcile = Error` carrying exactly
    ///    `Dag.conflicts`' report, and nothing is applied (GP6 — no winner, no partial merge);
    ///  - **determinism / order pinning** — `reconcile` is a pure function of `(base, headA, headB)`,
    ///    and the clean script is `betweenOps base headA ++ betweenOps base headB`.
    ///
    /// `'Node` needs equality. `encode` is the per-node content encoder (as `footprintLaws`). Mirrors
    /// `footprintLaws` — a domain that reconciles branches runs it.
    let reconcileLaws
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        let fp (op: SkeletonOp<'Node, 'Id>) = Ops.footprint nodew idw [ op ]
        let hashFn = OpStream.defaultHash

        // A minimal StreamWitness so the branch deltas live in a REAL DAG (append needs Encode for the
        // content id; reconcile/betweenOps never call Apply or Decode). Encode is a structural
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

        let mutable rng = ConfRng.ofSeed seed
        let mutable cleanCx = None
        let mutable crossCx = None
        let mutable conflictedCx = None
        let mutable determinismCx = None
        // Phase 121 — three of the four laws below only run on a sample that reached their branch:
        // the clean-fold law needs an `Ok`, the conflicted-path law needs an `Error`, and the
        // cross-validation law needs a footprint-independent delta pair. A run that reached only
        // one of them certifies the other two green having never applied them.
        let mutable cleanFolds = 0
        let mutable conflictedFolds = 0
        let mutable independentDeltas = 0

        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = LawKit.genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        // Chain a script onto `parent`, returning the new head (parent itself when the script is empty).
        let chain (ops: SkeletonOp<'Node, 'Id> list) (parent: string) (d0: Dag.T<SkeletonOp<'Node, 'Id>>) =
            let mutable head = parent
            let mutable d = d0

            for op in ops do
                let id, d' = Dag.append hashFn sw (Human "conf") op head d
                head <- id
                d <- d'

            head, d

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let a, r2 = collectScript 4 tree r1
            let b, r3 = collectScript 4 tree r2
            rng <- r3

            // a fork DAG: a genesis base node (its op never participates — it is in the base closure,
            // which betweenOps excludes), then branch A and branch B forked off the base.
            let baseId, d1 =
                Dag.append hashFn sw (Human "conf") (RemoveNode(nodew.Id tree)) "" Dag.empty

            let headA, d2 = chain a baseId d1
            let headB, dag = chain b baseId d2

            let deltaA = Dag.betweenOps dag baseId headA
            let deltaB = Dag.betweenOps dag baseId headB
            let result = Dag.reconcile fp dag baseId headA headB

            // determinism + order pinning: a pure function of (base, headA, headB); clean script pinned.
            if Dag.reconcile fp dag baseId headA headB <> result && determinismCx.IsNone then
                determinismCx <- Some(sprintf "seed=%d iter=%d: reconcile is not a pure function of its inputs" seed i)

            match result with
            | Ok script ->
                cleanFolds <- cleanFolds + 1

                if script <> deltaA @ deltaB && determinismCx.IsNone then
                    determinismCx <-
                        Some(sprintf "seed=%d iter=%d: clean script ≠ betweenOps A ++ betweenOps B (order pin)" seed i)

                // clean-merge replay: script ≡ A-then-B ≡ B-then-A on the base tree (content hash).
                let viaScript = applyAll script tree |> Result.map hashOf
                let ab = applyAll deltaA tree |> Result.bind (applyAll deltaB) |> Result.map hashOf
                let ba = applyAll deltaB tree |> Result.bind (applyAll deltaA) |> Result.map hashOf

                match viaScript, ab, ba with
                | Ok hs, Ok hab, Ok hba when hs = hab && hab = hba -> ()
                | _ ->
                    if cleanCx.IsNone then
                        cleanCx <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: a conflict-free merge did not fold order-independently (script=%A ab=%A ba=%A)"
                                    seed
                                    i
                                    viaScript
                                    ab
                                    ba
                            )
            | Error cs ->
                conflictedFolds <- conflictedFolds + 1

                // the conflicted path returns exactly Dag.conflicts' report, nothing applied.
                if cs <> Dag.conflicts fp deltaA deltaB && conflictedCx.IsNone then
                    conflictedCx <- Some(sprintf "seed=%d iter=%d: Error payload ≠ Dag.conflicts report" seed i)

            // footprint cross-validation: footprint-independent deltas ⇒ conflict-free (Ok).
            if Ops.independent (Ops.footprint nodew idw deltaA) (Ops.footprint nodew idw deltaB) then
                independentDeltas <- independentDeltas + 1

                match result with
                | Ok _ -> ()
                | Error _ ->
                    if crossCx.IsNone then
                        crossCx <-
                            Some(
                                sprintf "seed=%d iter=%d: footprint-independent deltas were NOT reconciled clean" seed i
                            )

        [ { Law = "reconcile clean fold replays order-independently (content-hash equal)"
            Passed = cleanCx.IsNone
            Counterexample = cleanCx }
          { Law = "reconcile is conflict-free when the deltas are footprint-independent (#78 cross-validation)"
            Passed = crossCx.IsNone
            Counterexample = crossCx }
          { Law = "reconcile hands back Dag.conflicts' report on conflict (nothing applied)"
            Passed = conflictedCx.IsNone
            Counterexample = conflictedCx }
          { Law = "reconcile is deterministic + order-pinned (pure fn of (base, headA, headB))"
            Passed = determinismCx.IsNone
            Counterexample = determinismCx }
          SampleAdequacy.reached
              "reconcileLaws"
              "reconcile outcome"
              seed
              [ "clean fold", cleanFolds; "conflicted fold", conflictedFolds ]
          SampleAdequacy.reached
              "reconcileLaws"
              "delta-pair independence"
              seed
              [ "independent delta pair", independentDeltas ] ]

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
    ///  - **coverage** — the sample exercised at least one non-empty independent pair (a vacuity
    ///    guard: a run whose generator never yields an independent pair certifies nothing, and
    ///    says so instead of reporting a hollow green).
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
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        // The documented bound: 8 sampled riffles + the two sequential extremes per pair.
        let riffleSamples = 8
        let mutable rng = ConfRng.ofSeed seed
        let mutable totality = None
        let mutable confluence = None
        let mutable checkedPairs = 0

        // Thread up to `n` random ops through `apply`, keeping the accepted ones — an applyable script.
        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = LawKit.genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            let a, r2 = collectScript 4 tree r1
            let b, r3 = collectScript 4 tree r2
            rng <- r3

            if
                not (List.isEmpty a)
                && not (List.isEmpty b)
                && Ops.independent (footprintOf a) (footprintOf b)
            then
                checkedPairs <- checkedPairs + 1

                let mutable interleavings = [ a @ b; b @ a ]

                for _ in 1..riffleSamples do
                    let ops, r' = riffle a b rng
                    rng <- r'
                    interleavings <- ops :: interleavings

                match Ops.applyAll nodew idw (a @ b) tree |> Result.map hashOf with
                | Error rej ->
                    if totality.IsNone then
                        totality <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: the sequential a@b order of an INDEPENDENT pair failed to apply (%A; a=%A b=%A)"
                                    seed
                                    i
                                    rej
                                    a
                                    b
                            )
                | Ok refHash ->
                    for ops in interleavings do
                        match Ops.applyAll nodew idw ops tree with
                        | Error rej ->
                            if totality.IsNone then
                                totality <-
                                    Some(
                                        sprintf
                                            "seed=%d iter=%d: an interleaving of an INDEPENDENT pair failed to apply (%A; a=%A b=%A ops=%A)"
                                            seed
                                            i
                                            rej
                                            a
                                            b
                                            ops
                                    )
                        | Ok t ->
                            if hashOf t <> refHash && confluence.IsNone then
                                confluence <-
                                    Some(
                                        sprintf
                                            "seed=%d iter=%d: an interleaving of an INDEPENDENT pair replayed to a different tree (a=%A b=%A ops=%A)"
                                            seed
                                            i
                                            a
                                            b
                                            ops
                                    )

        [ { Law = "interleaving totality (every sampled interleaving of an independent pair applies cleanly)"
            Passed = totality.IsNone
            Counterexample = totality }
          { Law = "confluence (every sampled interleaving of an independent pair replays content-hash-equal)"
            Passed = confluence.IsNone
            Counterexample = confluence }
          { Law = "coverage (the sample exercised at least one independent pair — vacuity guard)"
            Passed = checkedPairs > 0
            Counterexample =
              if checkedPairs > 0 then
                  None
              else
                  Some(sprintf "seed=%d: %d iterations produced no non-empty independent pair" seed iterations) } ]

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
        let canHold = gen.CanHold |> Option.defaultValue (fun _ -> true)
        let hashOf = Tree.encodeHash nodew encode
        let mutable rng = ConfRng.ofSeed seed
        let mutable permutation = None
        let mutable partition = None
        let mutable independence = None
        let mutable actionability = None
        let mutable confluence = None
        // Phase 157 — the id-uniqueness hypothesis. `twinsSeen` is its own vacuity guard: the
        // observability half only runs on a set holding an applicable, self-interfering proposal.
        let mutable uniqueness = None
        let mutable twinsSeen = 0
        // Phase 121 — pairwise independence and any-order confluence are trivially true of an EMPTY
        // accepted set, and the actionability law quantifies over rejections. A sample that never
        // accepted, or never rejected, certifies those green having never applied them.
        let mutable acceptedSeen = 0
        let mutable rejectedSeen = 0

        // An applyable script: up to `n` random ops threaded from `tree`, keeping the accepted.
        let collectScript n (tree: 'Node) (r0: ConfRng.T) =
            let mutable cur = tree
            let mutable accepted = []
            let mutable r = r0

            for _ in 1..n do
                let op, r' = LawKit.genOp nodew idw gen cur r
                r <- r'

                match Ops.applyContained canHold nodew idw op cur with
                | Ok t' ->
                    cur <- t'
                    accepted <- accepted @ [ op ]
                | Error _ -> ()

            accepted, r

        let mkProposal id ops : OpScriptProposal<'Node, 'Id> =
            { Id = id
              Holder = sprintf "agent-%d" id
              Ops = ops }

        for i in 0 .. iterations - 1 do
            let tree, r1 = gen.Tree rng
            rng <- r1
            let extra, r2 = ConfRng.intBelow 4 rng
            rng <- r2
            let count = extra + 2 // 2..5 proposals

            // Proposals off one base: applyable scripts (which frequently share parents, so
            // conflicts arise naturally) + ~1-in-4 corrupted into a provably-inapplicable
            // script (an op addressing an id the base tree does not carry).
            let mutable proposals = []

            for k in 1..count do
                let script, r3 = collectScript 3 tree rng
                rng <- r3
                let corrupt, r4 = ConfRng.intBelow 4 rng
                rng <- r4

                let ops =
                    if corrupt = 0 then
                        let baseIds = Tree.ids nodew tree |> List.map idw.ToString |> Set.ofList
                        let ghost, r5 = gen.FreshNode baseIds rng
                        rng <- r5
                        script @ [ RemoveNode(nodew.Id ghost) ]
                    else
                        script

                proposals <- proposals @ [ mkProposal k ops ]

            let result = Arbitration.arbitrate nodew idw tree proposals

            // determinism + permutation invariance: shuffled input ⇒ identical Arbitration.
            let shuffled, rS = ConfRng.shuffle proposals rng
            rng <- rS

            if
                (Arbitration.arbitrate nodew idw tree shuffled <> result
                 || Arbitration.arbitrate nodew idw tree proposals <> result)
                && permutation.IsNone
            then
                permutation <-
                    Some(sprintf "seed=%d iter=%d: arbitrate is not deterministic / permutation-invariant" seed i)

            // Phase 157 — id uniqueness IS the permutation law's hypothesis. The check is held to
            // an independent recount; it is empty on the set the law above just ran over; and a
            // twin — the same id and script under another holder — makes arrival order observable.
            let recount (ps: OpScriptProposal<'Node, 'Id> list) =
                let ids = ps |> List.map (fun p -> p.Id)

                ids
                |> List.filter (fun x -> (ids |> List.filter (fun y -> y = x) |> List.length) > 1)
                |> List.distinct
                |> List.sort

            if Arbitration.duplicateIds proposals <> [] && uniqueness.IsNone then
                uniqueness <-
                    Some(sprintf "seed=%d iter=%d: duplicateIds is non-empty on an id-unique proposal set" seed i)

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

                if
                    (Arbitration.duplicateIds twinLast <> [ p.Id ]
                     || Arbitration.duplicateIds twinLast <> recount twinLast
                     || Arbitration.duplicateIds twinFirst <> recount twinFirst)
                    && uniqueness.IsNone
                then
                    uniqueness <-
                        Some(
                            sprintf "seed=%d iter=%d: duplicateIds did not name exactly the repeated id %d" seed i p.Id
                        )

                if
                    Arbitration.arbitrate nodew idw tree twinLast = Arbitration.arbitrate nodew idw tree twinFirst
                    && uniqueness.IsNone
                then
                    uniqueness <-
                        Some(
                            sprintf
                                "seed=%d iter=%d: a repeated id (%d) left arrival order unobservable — the hypothesis would be decoration"
                                seed
                                i
                                p.Id
                        )

            // total partition: accepted + rejected = input, each exactly once.
            let acceptedIds = result.Accepted |> List.map (fun p -> p.Id)
            let rejectedIds = result.Rejected |> List.map (fun (p, _) -> p.Id)
            acceptedSeen <- acceptedSeen + List.length acceptedIds
            rejectedSeen <- rejectedSeen + List.length rejectedIds
            let inputIds = proposals |> List.map (fun p -> p.Id) |> List.sort

            if List.sort (acceptedIds @ rejectedIds) <> inputIds && partition.IsNone then
                partition <- Some(sprintf "seed=%d iter=%d: accepted+rejected ≠ input (dropped or duplicated)" seed i)

            // pairwise independence of the accepted set.
            let acceptedFps =
                result.Accepted |> List.map (fun p -> p.Id, Ops.footprint nodew idw p.Ops)

            let pairwise =
                acceptedFps
                |> List.forall (fun (ida, fa) ->
                    acceptedFps |> List.forall (fun (idb, fb) -> ida = idb || Ops.independent fa fb))

            if not pairwise && independence.IsNone then
                independence <- Some(sprintf "seed=%d iter=%d: the accepted set is not pairwise independent" seed i)

            // rejection actionability (GP5).
            for p, reason in result.Rejected do
                match reason with
                | Inapplicable(ix, rej) ->
                    if Ops.canApplyAll nodew idw p.Ops tree <> Error(ix, rej) && actionability.IsNone then
                        actionability <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: Inapplicable ≠ the canApplyAll envelope (proposal %d)"
                                    seed
                                    i
                                    p.Id
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

                    if not citesInterferingAccepted && actionability.IsNone then
                        actionability <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: Conflicts cites a non-accepted or non-interfering id (proposal %d)"
                                    seed
                                    i
                                    p.Id
                            )

            // any-order confluence: pinned, reversed, and shuffled application orders all
            // succeed and agree (content hash) — and MergedScript reproduces the same tree.
            let scripts = result.Accepted |> List.map (fun p -> p.Ops)

            let applyIn (order: SkeletonOp<'Node, 'Id> list list) =
                order
                |> List.fold (fun acc s -> acc |> Result.bind (Ops.applyAll nodew idw s)) (Ok tree)
                |> Result.map hashOf

            let shuffledScripts, rO = ConfRng.shuffle scripts rng
            rng <- rO

            let viaMerged = Ops.applyAll nodew idw result.MergedScript tree |> Result.map hashOf

            match applyIn scripts, applyIn (List.rev scripts), applyIn shuffledScripts, viaMerged with
            | Ok a, Ok b, Ok c, Ok d when a = b && b = c && c = d -> ()
            | _ ->
                if confluence.IsNone then
                    confluence <- Some(sprintf "seed=%d iter=%d: the accepted scripts did not apply confluently" seed i)

        [ { Law = "arbitrate determinism + input-permutation invariance (the pinned order decides)"
            Passed = permutation.IsNone
            Counterexample = permutation }
          { Law = "arbitrate is a total partition (every proposal lands in exactly one bucket)"
            Passed = partition.IsNone
            Counterexample = partition }
          { Law = "the accepted set is pairwise independent (Ops.independent)"
            Passed = independence.IsNone
            Counterexample = independence }
          { Law =
              "every rejection is actionable (Inapplicable = the canApplyAll envelope; Conflicts cites interfering accepted ids)"
            Passed = actionability.IsNone
            Counterexample = actionability }
          { Law = "the accepted scripts apply confluently in any order (the whole-script any-order claim)"
            Passed = confluence.IsNone
            Counterexample = confluence }
          { Law =
              "id uniqueness is the invariance hypothesis (duplicateIds is exact and empty on the certified sets; a repeated id makes arrival order observable)"
            Passed = uniqueness.IsNone && twinsSeen > 0
            Counterexample =
              match uniqueness with
              | Some _ -> uniqueness
              | None when twinsSeen = 0 ->
                  Some(
                      sprintf
                          "seed=%d: %d iterations produced no applicable self-interfering proposal to twin — the observability half never ran"
                          seed
                          iterations
                  )
              | None -> None }
          SampleAdequacy.reached
              "arbitrationLaws"
              "arbitration bucket"
              seed
              [ "accepted proposal", acceptedSeen; "rejected proposal", rejectedSeen ] ]
