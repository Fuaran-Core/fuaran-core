namespace Fuaran.Core

// The keyed-arbitration family (Phase 247): `Arbitration.arbitrateWith` at a domain's own footprint
// and applicability, held to the engine the domain lands the accepted scripts with. Its own topic
// file because it is a NEW family; `arbitrationLaws`, over plain `Arbitration.arbitrate`, stays in
// `ConcurrencyLaws.fs`, which owns it.

module internal KeyedArbitrationLaws =

    /// **Arbitration over a domain's containers and keyed positions, certified** (Phase 247).
    ///
    /// `arbitrationLaws` certifies `Arbitration.arbitrate`, and its generator draws every script
    /// through `Ops.applyContained`, so it never proposes one the container capability refuses and
    /// never builds two that carry one id off the `Children` walk. Those are exactly the two things
    /// `arbitrate` admits and a domain's own engine then refuses or loses: an insert under a node that
    /// cannot hold children (plain apply drops it when the witness ignores a leaf's new children), and
    /// two grafts carrying the same id in a keyed position (both admitted, the merged script holds the
    /// id twice). This family draws both, BUILT rather than hoped for, and runs the partition through
    /// `Arbitration.arbitrateWith footprintOf canApplyOf`:
    ///
    /// - the partition laws `arbitrationLaws` states, at the injected pair — determinism and
    ///   input-permutation invariance, a total partition, a pairwise-independent accepted set under
    ///   `footprintOf`, and actionable rejections (`Inapplicable` is `canApplyOf`'s envelope verbatim;
    ///   `Conflicts` cites accepted ids, each with its non-empty `Ops.interference` clauses);
    /// - **the accepted scripts LAND** under the domain's keyed engine, `Ops.applyContainedKeyed` at
    ///   `OpGen.CanHold` — in the pinned order, its reverse, a shuffle, and as `MergedScript`, all to
    ///   one tree (content hash over the keyed walk) whose keyed walk repeats no id and which the
    ///   domain's own `IdsUnique` accepts when it accepted the base;
    /// - **a container-illegal proposal is refused**: an insert under a node `CanHold` refuses is
    ///   `Inapplicable` with `NotAContainer` naming that node — never admitted;
    /// - **a keyed-id clash is refused**: of two proposals that each graft a subtree carrying one id
    ///   in a keyed position (built through `PlaceKeyedChild`; each applies alone and the two do not
    ///   apply together), never both are admitted, and when the first is, the second is `Conflicts`
    ///   citing it with `Interference.SameTarget` on that id.
    ///
    /// `keyedArbitrationLaws` pins the pair to `Ops.footprintKeyed keyw nodew idw` and
    /// `Ops.canApplyAllKeyed keyw canHold nodew idw`. The `With` form takes them as parameters, so a
    /// test can hand it `arbitrate`'s pair and watch both built arms go red, and a domain that records
    /// further identity in its footprint (a label, say) can certify its own composition — its
    /// footprint must still record a keyed id a graft carries in `ContentWrites`, as `footprintKeyed`
    /// does, for the clash arm to name it.
    ///
    /// **Vacuity is declared, not hidden.** A generator whose `CanHold` is `None` has no
    /// container-illegal proposal to draw, and a witness that declares no keyed position has no
    /// keyed clash to build; each such arm is VACUOUS BY DECLARATION and the guard line says so,
    /// while the partition and landing laws are asserted all the same. `'Node` needs equality.
    let keyedArbitrationLawsWith
        (family: string)
        (keyw: KeyedWitness<'Node, 'Id>)
        (nodew: NodeWitness<'Node, 'Id>)
        (idw: IdWitness<'Id>)
        (gen: OpGen<'Node, 'Id>)
        (encode: 'Node -> string)
        (footprintOf: SkeletonOp<'Node, 'Id> list -> Footprint)
        (canApplyOf: SkeletonOp<'Node, 'Id> list -> 'Node -> Result<unit, int * Rejection<'Id>>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let canHold = LawKit.canHoldOf gen
        let t = Tree.traversal nodew keyw
        let hashOf = Tree.encodeHash t encode
        let key (i: 'Id) = idw.ToString i

        let permutation =
            LawKit.LawCell "arbitrateWith determinism + input-permutation invariance (the pinned order decides)"

        let partition =
            LawKit.LawCell "arbitrateWith is a total partition (every proposal lands in exactly one bucket)"

        let independence =
            LawKit.LawCell "the accepted set is pairwise independent under the domain's footprint"

        let actionability =
            LawKit.LawCell(
                "every rejection is actionable (Inapplicable = the domain's canApply envelope; Conflicts cites interfering accepted ids with their clauses)",
                Some "arbitration bucket, built arm and op kind"
            )

        let lands =
            LawKit.LawCell(
                "the accepted scripts land under applyContainedKeyed in any order, to one tree whose keyed walk repeats no id",
                Some "arbitration bucket, built arm and op kind"
            )

        let containerRefused =
            LawKit.LawCell(
                "a proposal inserting under a node the container capability refuses is Inapplicable(NotAContainer), never admitted",
                Some "arbitration bucket, built arm and op kind"
            )

        let clashRefused =
            LawKit.LawCell(
                "two proposals bringing in the same keyed id are never both admitted; the later is Conflicts citing the earlier with SameTarget on that id",
                Some "arbitration bucket, built arm and op kind"
            )

        let mutable acceptedSeen = 0
        let mutable rejectedSeen = 0
        // Phase 302 — the actionability law's `Conflicts` half, counted apart from the rejections
        // the built inapplicable proposals alone can supply.
        let mutable conflictsSeen = 0
        let mutable containerArms = 0
        let mutable clashArms = 0
        let mutable declaredKeyed = 0
        let kinds = LawKit.OpKindTally()

        let mkProposal id ops : OpScriptProposal<'Node, 'Id> =
            { Id = id
              Holder = sprintf "agent-%d" id
              Ops = ops }

        let landFrom (start: 'Node) (ops: SkeletonOp<'Node, 'Id> list) =
            ops
            |> List.fold
                (fun acc op -> acc |> Result.bind (Ops.applyContainedKeyed keyw canHold nodew idw op))
                (Ok start)

        LawKit.run iterations seed (fun rng _ at ->
            let tree = rng.Draw gen.Tree
            let walk = Tree.preorder t tree

            declaredKeyed <-
                declaredKeyed
                + (walk |> List.sumBy (fun n -> List.length (keyw.KeyedChildren n)))

            let taken = walk |> List.map (nodew.Id >> key) |> Set.ofList
            let count = rng.IntBelow 3 + 2 // 2..4 drawn proposals

            // Drawn proposals off one base, ~1-in-4 corrupted into an inapplicable script — the
            // `arbitrationLaws` construction, so the rejected bucket is reached without the arms.
            let drawn =
                [ for k in 1..count do
                      let script = rng.Draw(LawKit.collectScript (Some kinds) nodew idw gen 3 tree)

                      let ops =
                          if rng.IntBelow 4 = 0 then
                              let ghost = rng.Draw(gen.FreshNode taken)
                              script @ [ RemoveNode(nodew.Id ghost) ]
                          else
                              script

                      mkProposal k ops ]

            // ---- the container-illegal proposal (built) ----
            let containerArm =
                match gen.CanHold, walk |> List.filter (fun n -> not (canHold n)) with
                | Some _, (_ :: _ as leaves) ->
                    let leaf = rng.Choose leaves
                    let fresh = rng.Draw(gen.FreshNode taken)
                    Some(nodew.Id leaf, mkProposal (count + 1) [ InsertChild(nodew.Id leaf, fresh) ])
                | _ -> None

            // ---- the keyed-id clash (built through PlaceKeyedChild) ----
            let clashArm =
                let k = nodew.Id(rng.Draw(gen.FreshNode taken))
                let f1 = rng.Draw(gen.FreshNode(Set.add (key k) taken))

                let f2 =
                    rng.Draw(gen.FreshNode(taken |> Set.add (key k) |> Set.add (key (nodew.Id f1))))

                let parents = walk |> List.filter canHold

                match keyw.PlaceKeyedChild f1 k, keyw.PlaceKeyedChild f2 k, parents with
                | Some g1, Some g2, _ :: _ ->
                    let carries g =
                        Tree.idsKeyed nodew keyw g |> List.exists (idw.Equals k)

                    let p1 = nodew.Id(rng.Choose parents)
                    let p2 = nodew.Id(rng.Choose parents)
                    let a = [ InsertChild(p1, g1) ]
                    let b = [ InsertChild(p2, g2) ]
                    let engine = Ops.canApplyAllKeyed keyw canHold nodew idw
                    // counted only where the clash is real: each applies alone, the two do not.
                    if
                        carries g1
                        && carries g2
                        && engine a tree = Ok()
                        && engine b tree = Ok()
                        && Result.isError (engine (a @ b) tree)
                    then
                        Some(k, mkProposal (count + 2) a, mkProposal (count + 3) b)
                    else
                        None
                | _ -> None

            let proposals =
                drawn
                @ (containerArm |> Option.map snd |> Option.toList)
                @ (match clashArm with
                   | Some(_, a, b) -> [ a; b ]
                   | None -> [])

            let arbitrate = Arbitration.arbitrateWith footprintOf canApplyOf tree
            let result = arbitrate proposals

            permutation.Check(
                arbitrate (rng.Shuffle proposals) = result && arbitrate proposals = result,
                fun () -> at "arbitrateWith is not deterministic / permutation-invariant"
            )

            let acceptedIds = result.Accepted |> List.map (fun p -> p.Id)
            let rejectedIds = result.Rejected |> List.map (fun (p, _) -> p.Id)
            acceptedSeen <- acceptedSeen + List.length acceptedIds
            rejectedSeen <- rejectedSeen + List.length rejectedIds

            partition.Check(
                List.sort (acceptedIds @ rejectedIds) = (proposals |> List.map (fun p -> p.Id) |> List.sort),
                fun () -> at "accepted+rejected ≠ input (dropped or duplicated)"
            )

            let acceptedFps = result.Accepted |> List.map (fun p -> p.Id, footprintOf p.Ops)

            independence.Check(
                acceptedFps
                |> List.forall (fun (ida, fa) ->
                    acceptedFps |> List.forall (fun (idb, fb) -> ida = idb || Ops.independent fa fb)),
                fun () -> at "the accepted set is not pairwise independent under the domain's footprint"
            )

            for p, reason in result.Rejected do
                match reason with
                | Inapplicable(ix, rej) ->
                    actionability.Check(
                        canApplyOf p.Ops tree = Error(ix, rej),
                        fun () -> at (sprintf "Inapplicable ≠ the domain's canApply envelope (proposal %d)" p.Id)
                    )
                | Conflicts(ids, explained) ->
                    conflictsSeen <- conflictsSeen + 1
                    let fp = footprintOf p.Ops

                    let cited =
                        not (List.isEmpty ids)
                        && ids = List.map fst explained
                        && explained
                           |> List.forall (fun (cid, clauses) ->
                               match acceptedFps |> List.tryFind (fun (aid, _) -> aid = cid) with
                               | Some(_, afp) -> not (List.isEmpty clauses) && clauses = Ops.interference fp afp
                               | None -> false)

                    actionability.Check(
                        cited,
                        fun () ->
                            at (sprintf "Conflicts cites a non-accepted or non-interfering id (proposal %d)" p.Id)
                    )

            // the accepted scripts land under the keyed engine, in any order, to one tree.
            let scripts = result.Accepted |> List.map (fun p -> p.Ops)

            let orders =
                [ List.concat scripts
                  List.concat (List.rev scripts)
                  List.concat (rng.Shuffle scripts)
                  result.MergedScript ]

            let landed = orders |> List.map (landFrom tree)

            // the keyed walk and the domain's own check are held only where the base satisfied them:
            // landing cannot repair a base, and the laws are about what landing does.
            let baseKeyedWf = Tree.wellFormedKeyed nodew keyw idw tree = Tree.Structural
            let baseUnique = keyw.IdsUnique tree

            lands.Check(
                (match landed with
                 | Ok first :: _ ->
                     landed
                     |> List.forall (function
                         | Ok t' -> hashOf t' = hashOf first
                         | Error _ -> false)
                     && (not baseKeyedWf || Tree.wellFormedKeyed nodew keyw idw first = Tree.Structural)
                     && (not baseUnique || keyw.IdsUnique first)
                 | _ -> false),
                fun () ->
                    at (
                        sprintf
                            "the accepted scripts did not land as one tree under the keyed engine (accepted %A; outcomes %A)"
                            acceptedIds
                            (landed |> List.map (Result.map ignore))
                    )
            )

            match containerArm with
            | Some(leafId, p) ->
                containerArms <- containerArms + 1

                let refused =
                    result.Rejected
                    |> List.exists (fun (q, reason) ->
                        q.Id = p.Id
                        && (match reason with
                            | Inapplicable(0, NotAContainer(target, _)) -> idw.Equals target leafId
                            | _ -> false))

                containerRefused.Check(
                    refused,
                    fun () ->
                        at (
                            sprintf
                                "an insert under %s, which the container capability refuses, was not refused Inapplicable(NotAContainer) (%A)"
                                (key leafId)
                                (result.Rejected |> List.tryFind (fun (q, _) -> q.Id = p.Id) |> Option.map snd)
                        )
                )
            | None -> ()

            match clashArm with
            | Some(k, a, b) ->
                clashArms <- clashArms + 1
                let admitted (q: OpScriptProposal<'Node, 'Id>) = List.contains q.Id acceptedIds

                let namesTheId =
                    not (admitted a)
                    || result.Rejected
                       |> List.exists (fun (q, reason) ->
                           q.Id = b.Id
                           && (match reason with
                               | Conflicts(_, explained) ->
                                   explained
                                   |> List.exists (fun (cid, clauses) ->
                                       cid = a.Id
                                       && clauses
                                          |> List.exists (function
                                              | Interference.SameTarget targets -> Set.contains (key k) targets
                                              | _ -> false))
                               | _ -> false))

                clashRefused.Check(
                    not (admitted a && admitted b) && namesTheId,
                    fun () ->
                        at (
                            sprintf
                                "two grafts carrying the keyed id %s: proposal %d admitted=%b, proposal %d admitted=%b, and the later's rejection %A"
                                (key k)
                                a.Id
                                (admitted a)
                                b.Id
                                (admitted b)
                                (result.Rejected |> List.tryFind (fun (q, _) -> q.Id = b.Id) |> Option.map snd)
                        )
                )
            | None -> ())

        let cells =
            [ permutation
              partition
              independence
              actionability
              lands
              containerRefused
              clashRefused ]

        let noContainer = gen.CanHold.IsNone
        let noKeyed = declaredKeyed = 0 && clashArms = 0

        let builtArms =
            (if noContainer then
                 []
             else
                 [ "container-illegal proposal", containerArms ])
            @ (if noKeyed then [] else [ "keyed-id clash", clashArms ])

        let dimension =
            match noContainer, noKeyed with
            | false, false -> "arbitration bucket, built arm and op kind"
            | true, false ->
                "arbitration bucket, built arm and op kind (OpGen.CanHold is None, so the container arm is vacuous BY DECLARATION)"
            | false, true ->
                "arbitration bucket, built arm and op kind (the witness declares NO keyed position, so the clash arm is vacuous BY DECLARATION)"
            | true, true ->
                "arbitration bucket and op kind (no container capability and no keyed position, so both built arms are vacuous BY DECLARATION)"

        LawKit.results cells
        @ [ SampleAdequacy.reached
                family
                dimension
                seed
                ([ "accepted proposal", acceptedSeen
                   "rejected proposal", rejectedSeen
                   "conflicts rejection", conflictsSeen ]
                 @ builtArms
                 @ kinds.Demands) ]
