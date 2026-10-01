namespace Fuaran.Core

// The keyed-apply family (Phase 286): the engine's own refusal over a domain's keyed positions,
// held to the domain's own check. Its own topic file because it is a NEW family; the keyed-id
// family it builds on (Phase 189's `keyedChildrenLaws`) stays in `TreeLaws.fs`, which owns it.

module internal KeyedApplyLaws =

    /// **The keyed witness reaches the apply path, certified** (Phase 286).
    ///
    /// Phase 189 let a domain DECLARE its keyed positions and certified the domain's own id check
    /// against that declaration (`keyedChildrenLaws`). Phase 286 hands the same declaration to the
    /// engine: `Ops.applyContainedKeyed` locates through `Tree.traversal` and refuses a
    /// `DuplicateId` over the keyed walk. This family certifies the three things that makes true of
    /// a domain, and none of them is a law the domain could pass by declaring nothing it holds:
    ///
    /// - **The witness laws `Tree.traversal` rebuilds through.** `ReplaceKeyedChildren` puts a
    ///   node's own keyed children back unchanged; it places a same-length list position for
    ///   position and leaves `Children` alone; and `ReplaceChildren` leaves the keyed positions
    ///   alone. A witness that breaks one of these makes every walk below a keyed position rebuild
    ///   a different tree from the one it read.
    /// - **The engine's refusal and the domain's own full walk AGREE.** An insert is refused as
    ///   `DuplicateId` by `applyContainedKeyed` exactly when `KeyedWitness.IdsUnique` refuses the
    ///   tree the unkeyed engine builds from it. The collisions are BUILT through
    ///   `PlaceKeyedChild`, never drawn (a generator's contract is a fresh id, so a drawn sample
    ///   cannot carry one): an id held keyed in the tree against a structural graft, an id held
    ///   structurally against a graft that carries it keyed, and an id held keyed on both sides —
    ///   the three shapes the unkeyed engine is blind to. A clean insert is the arm that keeps the
    ///   law from passing for an engine that refuses everything.
    /// - **Declaring nothing changes nothing.** With `KeyedChildren = fun _ -> []`,
    ///   `applyContainedKeyed` / `canApplyContainedKeyed` answer exactly what `applyContained` /
    ///   `canApplyContained` answer, over every op kind the kit draws.
    /// - **The keyed engine is one engine, and it keeps the keyed walk well formed.** Over ops
    ///   drawn across the keyed walk — parents, targets and updates that sit in or below a keyed
    ///   position — `canApplyContainedKeyed` agrees with `applyContainedKeyed` (accept/reject and
    ///   the envelope), and an op it accepts into a tree whose keyed walk repeats no id leaves one
    ///   that repeats none. The second is the sampled form of `keyed_apply_preserves_wf` over every
    ///   op kind, the `UpdateNode` payload check included, which the model does not reach.
    ///
    /// **A witness that declares NO keyed position passes the keyed arms VACUOUSLY and the report
    /// says so**, in `keyedChildrenLaws`' words — while the identity law, which needs no
    /// declaration, is asserted all the same.
    let keyedApplyLaws
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let family = "Conformance.keyedApplyLaws"
        let canHold = LawKit.canHoldOf gen
        let t = Tree.traversal nodew keyw

        let unkeyed =
            { keyw with
                KeyedChildren = fun _ -> []
                ReplaceKeyedChildren = fun n _ -> n }

        let restores = LawKit.LawCell "ReplaceKeyedChildren n (KeyedChildren n) = n"

        let placesInOrder =
            LawKit.LawCell(
                "ReplaceKeyedChildren places a same-length list position for position and leaves Children where they were",
                Some "built arm and op kind"
            )

        let structuralLeavesKeyed =
            LawKit.LawCell("ReplaceChildren leaves the keyed positions where they were", Some "built arm and op kind")

        let agreement =
            LawKit.LawCell(
                "applyContainedKeyed refuses an insert as DuplicateId exactly when the domain's id check refuses the tree the unkeyed engine builds",
                Some "built arm and op kind"
            )

        let identity =
            LawKit.LawCell
                "with no keyed position declared, applyContainedKeyed and canApplyContainedKeyed answer exactly what applyContained and canApplyContained answer"

        let mutable keyedHolders = 0
        let mutable keyedContainers = 0
        let mutable cleanInserts = 0
        let mutable keyedInTree = 0
        let mutable keyedInGraft = 0
        let mutable keyedBoth = 0
        let mutable acceptedKeyed = 0

        let dryRun =
            LawKit.LawCell
                "canApplyContainedKeyed ≡ applyContainedKeyed (accept/reject + envelope) over ops drawn across the keyed walk"

        let keyedPreservation =
            LawKit.LawCell(
                "an op applyContainedKeyed accepts keeps a tree well formed over its keyed walk",
                Some "built arm and op kind"
            )

        let mutable declaredKeyed = 0
        let mutable placementsTaken = 0
        let kinds = LawKit.OpKindTally()

        let keyOf (n: 'Node) = idw.ToString(nodew.Id n)

        let keyedKeysOf (n: 'Node) =
            Tree.keyedIds nodew keyw n |> List.map idw.ToString

        let structuralKeysOf (n: 'Node) = nodew.Children n |> List.map keyOf

        /// Place `id` in a keyed position of `n` through the domain's own `PlaceKeyedChild`.
        ///
        /// Deliberately NOT filtered to placements the declaration then reports (the filter
        /// `keyedChildrenLaws` applies): a position the domain can place into and `KeyedChildren`
        /// does not report is exactly the defect the agreement law exists to find — the domain's
        /// walk sees the collision, the engine does not. A placement that placed nothing is
        /// harmless here: it builds no collision, so both sides accept and the arm is not counted.
        let place (n: 'Node) (id: 'Id) : 'Node option =
            match keyw.PlaceKeyedChild n id with
            | Some rebuilt ->
                placementsTaken <- placementsTaken + 1
                Some rebuilt
            | None -> None

        /// Splice a rebuilt node back over the structural surface, only where the rebuild kept the
        /// node's identity and kind (a placement that moved either is a declaration defect).
        let spliceIn (original: 'Node) (rebuilt: 'Node) (tree: 'Node) : 'Node option =
            if
                idw.Equals (nodew.Id rebuilt) (nodew.Id original)
                && nodew.KindTag rebuilt = nodew.KindTag original
            then
                Tree.updateNode nodew idw (nodew.Id original) (fun _ -> rebuilt) tree
            else
                None

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let keyedNodes = Tree.preorder t tree
            let surfaceNodes = Tree.preorder nodew tree

            declaredKeyed <-
                declaredKeyed
                + (keyedNodes |> List.sumBy (fun n -> List.length (keyedKeysOf n)))

            // ---- the witness laws the traversal rebuilds through ----
            let n = rng.Choose keyedNodes
            let ks = keyw.KeyedChildren n

            restores.Check(
                keyw.ReplaceKeyedChildren n ks = n,
                fun () -> at (sprintf "rebuilding %s over its own keyed children changed it" (keyOf n))
            )

            if not (List.isEmpty ks) then
                keyedHolders <- keyedHolders + 1
                let reversed = List.rev ks
                let placed = keyw.ReplaceKeyedChildren n reversed

                placesInOrder.Check(
                    keyedKeysOf placed = List.map keyOf reversed
                    && structuralKeysOf placed = structuralKeysOf n,
                    fun () ->
                        at (
                            sprintf
                                "placing %A in the keyed positions of %s gave keyed %A and children %A (children were %A)"
                                (List.map keyOf reversed)
                                (keyOf n)
                                (keyedKeysOf placed)
                                (structuralKeysOf placed)
                                (structuralKeysOf n)
                        )
                )

                if canHold n then
                    // Phase 302 — counted apart from `keyedHolders`: this arm is gated again, on the
                    // holder also holding structural children, and a witness whose keyed holders are
                    // all leaves never asserted it.
                    keyedContainers <- keyedContainers + 1
                    let rebuilt = nodew.ReplaceChildren n (List.rev (nodew.Children n))

                    structuralLeavesKeyed.Check(
                        keyedKeysOf rebuilt = keyedKeysOf n,
                        fun () ->
                            at (
                                sprintf
                                    "rebuilding the children of %s moved its keyed positions from %A to %A"
                                    (keyOf n)
                                    (keyedKeysOf n)
                                    (keyedKeysOf rebuilt)
                            )
                    )

            // ---- the engine's refusal against the domain's own check (built) ----
            let taken = Tree.idsKeyed nodew keyw tree |> List.map idw.ToString |> Set.ofList
            let fresh = rng.Draw(gen.FreshNode taken)
            let freshId = nodew.Id fresh
            let other = rng.Draw(gen.FreshNode(Set.add (idw.ToString freshId) taken))
            let holder = rng.Choose surfaceNodes
            let victim = rng.Choose surfaceNodes

            // An arm is COUNTED only where it exhibited what it is for: a collision the domain's
            // own walk refuses, or, for the clean insert, a tree it accepts.
            let agree (arm: string) (collision: bool) (count: unit -> unit) (pre: 'Node) (graft: 'Node) =
                match pre |> Tree.preorder nodew |> List.filter canHold with
                | [] -> ()
                | parents ->
                    let parent = rng.Choose parents
                    let op = InsertChild(nodew.Id parent, graft)

                    match Ops.applyContained canHold nodew idw op pre with
                    | Error _ -> ()
                    | Ok built ->
                        let domainRefuses = not (keyw.IdsUnique built)

                        if domainRefuses = collision then
                            count ()

                        let keyed = Ops.applyContainedKeyed keyw canHold nodew idw op pre

                        let engineRefuses =
                            match keyed with
                            | Error(DuplicateId _) -> true
                            | _ -> false

                        agreement.Check(
                            (domainRefuses = engineRefuses),
                            fun () ->
                                at (
                                    sprintf
                                        "%s: inserting %s under %s — %s %s the tree the unkeyed engine builds, and applyContainedKeyed answered %A; the engine's refusal and the domain's own walk disagree, so either the declaration misses a position %s walks or the engine walks one it does not"
                                        arm
                                        (keyOf graft)
                                        (keyOf parent)
                                        keyw.Surface
                                        (if domainRefuses then "REFUSES" else "accepts")
                                        keyed
                                        keyw.Surface
                                )
                        )

            // the clean insert: nothing collides, so neither refuses
            agree "clean insert" false (fun () -> cleanInserts <- cleanInserts + 1) tree fresh

            // an id held KEYED in the tree, grafted STRUCTURALLY
            match place holder freshId |> Option.bind (fun rb -> spliceIn holder rb tree) with
            | Some withKeyed ->
                agree
                    "keyed in the tree, structural in the graft"
                    true
                    (fun () -> keyedInTree <- keyedInTree + 1)
                    withKeyed
                    fresh

                // and the same id grafted KEYED as well: keyed on both sides
                match place other freshId with
                | Some carrier ->
                    agree
                        "keyed in the tree and in the graft"
                        true
                        (fun () -> keyedBoth <- keyedBoth + 1)
                        withKeyed
                        carrier
                | None -> ()
            | None -> ()

            // an id held STRUCTURALLY in the tree, grafted KEYED
            match place fresh (nodew.Id victim) with
            | Some carrier ->
                agree
                    "structural in the tree, keyed in the graft"
                    true
                    (fun () -> keyedInGraft <- keyedInGraft + 1)
                    tree
                    carrier
            | None -> ()

            // ---- declaring nothing changes nothing (drawn, every op kind) ----
            let op = rng.Draw(LawKit.genOp nodew idw gen tree)
            kinds.Note op
            let plain = Ops.applyContained canHold nodew idw op tree
            let viaKeyed = Ops.applyContainedKeyed unkeyed canHold nodew idw op tree
            let plainDry = Ops.canApplyContained canHold nodew idw op tree
            let keyedDry = Ops.canApplyContainedKeyed unkeyed canHold nodew idw op tree

            identity.Check(
                (plain = viaKeyed && plainDry = keyedDry),
                fun () ->
                    at (
                        sprintf
                            "%A: applyContained gave %A and applyContainedKeyed %A; canApplyContained gave %A and canApplyContainedKeyed %A"
                            op
                            plain
                            viaKeyed
                            plainDry
                            keyedDry
                    )
            )

            // ---- the keyed engine over ops that address the keyed walk (drawn) ----
            let kop = rng.Draw(LawKit.genOp t idw gen tree)
            let applied = Ops.applyContainedKeyed keyw canHold nodew idw kop tree
            let dry = Ops.canApplyContainedKeyed keyw canHold nodew idw kop tree

            dryRun.Check(
                (Result.map ignore applied = dry),
                fun () -> at (sprintf "%A: applyContainedKeyed gave %A, canApplyContainedKeyed %A" kop applied dry)
            )

            match applied with
            | Ok after when Tree.wellFormedKeyed nodew keyw idw tree = Tree.Structural ->
                acceptedKeyed <- acceptedKeyed + 1

                match Tree.wellFormedKeyed nodew keyw idw after with
                | Tree.Structural -> keyedPreservation.Check(true, fun () -> "")
                | Tree.RepeatedId d ->
                    keyedPreservation.Check(
                        false,
                        fun () ->
                            at (
                                sprintf
                                    "an ACCEPTED %A left a tree whose keyed walk carries %s twice"
                                    kop
                                    (idw.ToString d)
                            )
                    )
            | _ -> ())

        let cells =
            [ restores
              placesInOrder
              structuralLeavesKeyed
              agreement
              identity
              dryRun
              keyedPreservation ]

        if declaredKeyed = 0 && placementsTaken = 0 then
            // Vacuous BY DECLARATION for the keyed arms — the `keyedChildrenLaws` verdict, in its
            // words, carried on the one guard line so the family's result count does not depend on
            // the witness. The identity law needs no declaration and was asserted on every
            // iteration, so the op-kind guard still reads it.
            LawKit.results cells
            @ [ SampleAdequacy.reached
                    family
                    "op kind (the witness declares NO keyed position, so the keyed laws are vacuous BY DECLARATION)"
                    seed
                    kinds.Demands ]
        else
            LawKit.results cells
            @ [ SampleAdequacy.reached
                    family
                    "built arm and op kind"
                    seed
                    ([ "node holding a keyed position", keyedHolders
                       "keyed holder that can hold children", keyedContainers
                       "clean insert", cleanInserts
                       "keyed in the tree, structural in the graft", keyedInTree
                       "structural in the tree, keyed in the graft", keyedInGraft
                       "keyed in the tree and in the graft", keyedBoth
                       "accepted op over the keyed walk", acceptedKeyed ]
                     @ kinds.Demands) ]
