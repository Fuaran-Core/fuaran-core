module Fuaran.Core.Tests.FootprintLawsAtTests

// Phase 249 — footprint soundness lifted off `SkeletonOp`, and the lane DAG `foldOnce` builds
// exposed.
//
// `Conformance.footprintLawsAt` is Phase 78's soundness law at a domain's OWN ops: the domain's
// stream witness, its footprint projection and its generator. The acceptance it answers to is the
// keyed-footprint defect a downstream consumer measured: a footprint that reads an inserted
// subtree's SURFACE ids and misses the ids it carries in keyed positions declares two grafts
// bringing in the same keyed id independent, and the two do not commute — the second is refused.
// The defect is symmetric (the same refusal under every arrival order), so the fold-confluence pack
// passes it; this law is red on it, and green on the footprint that reads the keyed walk.
//
// `FoldConfluence.laneDag` is the builder `foldOnce` folds over, so a domain reads a halt's TYPED
// conflicts off the very DAG the pack measured rather than a hand-built twin of it.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference
open Fuaran.Core.Tests.ConformanceTests

// ---------------------------------------------------------------------------
//  The keyed domain — `ConformanceTests`' keyed reference (`KNode`, `keyw`) under the keyed engine
// ---------------------------------------------------------------------------

type KOp = SkeletonOp<KNode, string>

let private k (id: string) (children: KNode list) (cases: (string * KNode) list) : KNode =
    { Id = id
      Kind = (if List.isEmpty children then "para" else "section")
      Children = children
      Cases = cases }

let private leaf (id: string) = k id [] []

let private canHoldAll (_: KNode) = true

/// root [ s1 [ x1 ]; s2 [ x2 ]; s3 [ x3 ] ] — three containers to graft under.
let keyedBase: KNode =
    k "root" [ k "s1" [ leaf "x1" ] []; k "s2" [ leaf "x2" ] []; k "s3" [ leaf "x3" ] [] ] []

let rec private encK (n: KNode) : string =
    n.Id
    + (if List.isEmpty n.Cases then
           ""
       else
           "{"
           + (n.Cases |> List.map (fun (l, c) -> l + ":" + encK c) |> String.concat ",")
           + "}")
    + (if List.isEmpty n.Children then
           ""
       else
           "[" + (n.Children |> List.map encK |> String.concat ",") + "]")

let private encKOp (op: KOp) : string =
    match op with
    | InsertChild(p, n) -> "I|" + p + "|" + encK n
    | RemoveNode t -> "R|" + t
    | other -> sprintf "%A" other

/// The keyed engine as a stream witness: `Ops.applyContainedKeyed`, which refuses an id the keyed
/// walk already holds.
let keyedW: StreamWitness<KOp, KNode, Rejection<string>> =
    { Apply = fun op t -> Ops.applyContainedKeyed keyw canHoldAll knodew idw op t
      Encode = encKOp
      Decode = fun _ -> Error "FootprintLawsAtTests: decode is unused by the family" }

/// The state identity the commuting comparison reads: the content hash over the keyed walk.
let keyedHash: KNode -> string = Tree.encodeHash (Tree.traversal knodew keyw) encK

/// A state-blind draw over deliberately SMALL id pools, so two scripts routinely bring in the same
/// keyed id (the hidden collision), the same surface id, or remove what the other grafts beside.
let private genKeyedOp (rng: ConfRng.T) : KOp * ConfRng.T =
    let parents = [ "root"; "s1"; "s2"; "s3" ]
    let kind, r1 = ConfRng.intBelow 3 rng

    match kind with
    | 0 ->
        // a switch carrying one keyed id in its case table
        let p, r2 = ConfRng.choose parents r1
        let s, r3 = ConfRng.intBelow 4 r2
        let kk, r4 = ConfRng.intBelow 2 r3
        InsertChild(p, k ("sw" + string s) [] [ "on", leaf ("k" + string kk) ]), r4
    | 1 ->
        let p, r2 = ConfRng.choose parents r1
        let n, r3 = ConfRng.intBelow 4 r2
        InsertChild(p, leaf ("n" + string n)), r3
    | _ ->
        let t, r2 =
            ConfRng.choose [ "x1"; "x2"; "x3"; "n0"; "n1"; "n2"; "n3"; "sw0"; "sw1"; "sw2"; "sw3" ] r1

        RemoveNode t, r2

let keyedGen: StreamGen<KOp, KNode> = { State0 = keyedBase; Op = genKeyedOp }

/// The footprint that reads the keyed walk (`Ops.footprintKeyed`) — the one that is kept.
let keyedFootprint (op: KOp) : Footprint =
    Ops.footprintKeyed keyw knodew idw [ op ]

/// The first, unsound footprint: the surface walk only, so an inserted subtree's keyed ids are
/// missed.
let private surfaceFootprint (op: KOp) : Footprint = Ops.footprint knodew idw [ op ]

// ---------------------------------------------------------------------------
//  The plan domain (`FoldConfluenceTests`) — a non-tree state, its own footprint
// ---------------------------------------------------------------------------

let private genPlanOpBlind (rng: ConfRng.T) : FoldConfluenceTests.PlanOp * ConfRng.T =
    let items = [ for i in 1..8 -> "p" + string i ]
    let kind, r1 = ConfRng.intBelow 4 rng

    match kind with
    | 0 ->
        let v, r2 = ConfRng.intBelow 4 r1
        FoldConfluenceTests.AddItem("n" + string v, "t"), r2
    | 1 ->
        let i, r2 = ConfRng.choose items r1
        let t, r3 = ConfRng.intBelow 3 r2
        FoldConfluenceTests.Retitle(i, "title-" + string t), r3
    | 2 ->
        let i, r2 = ConfRng.choose items r1
        FoldConfluenceTests.SetShipped i, r2
    | _ ->
        let i, r2 = ConfRng.choose items r1
        let d, r3 = ConfRng.choose (items @ [ "n0"; "n1" ]) r2
        FoldConfluenceTests.AddDep(i, d), r3

let private planGen: StreamGen<FoldConfluenceTests.PlanOp, FoldConfluenceTests.Plan> =
    { State0 = FoldConfluenceTests.basePlan
      Op = genPlanOpBlind }

// ---------------------------------------------------------------------------

let private failed (results: LawResult list) =
    results |> List.filter (fun r -> not r.Passed)

let private failedNamed (fragment: string) (results: LawResult list) =
    failed results |> List.filter (fun r -> r.Law.Contains fragment)

[<Tests>]
let tests =
    testList
        "Footprint soundness at the domain's ops, and the lane DAG (Phase 249)"
        [ testList
              "Conformance.footprintLawsAt"
              [ testCase "the footprint that reads the keyed walk passes, every guard reached"
                <| fun _ ->
                    let results =
                        Conformance.footprintLawsAt keyedW keyedFootprint keyedHash keyedGen 2490 300

                    Expect.isEmpty (failed results) (sprintf "%A" (failed results))

                testCase "the footprint that misses an inserted subtree's keyed ids is RED, on soundness"
                <| fun _ ->
                    let results =
                        Conformance.footprintLawsAt keyedW surfaceFootprint keyedHash keyedGen 2490 300

                    let soundness = failedNamed "footprint soundness" results
                    Expect.equal (List.length soundness) 1 (sprintf "the soundness law goes red: %A" results)

                    let cx = soundness |> List.head |> (fun r -> defaultArg r.Counterexample "")
                    Expect.stringContains cx "did not commute" "the counterexample names the break"
                    Expect.stringContains cx "rejected" "and the refusal that one order met"

                testCase "the fold-confluence pack passes the same unsound footprint — the gap this law closes"
                <| fun _ ->
                    // The defect is symmetric: two grafts bringing in one keyed id are refused the
                    // same way under every arrival order, so arrival-order invariance holds.
                    let laneGen: LaneGen<KOp, KNode> =
                        { State0 = keyedBase
                          BaseOp = RemoveNode "root"
                          Lanes =
                            fun n r0 ->
                                let mutable r = r0

                                let lanes =
                                    [ for _ in 1..n do
                                          let mutable cur = keyedBase
                                          let mutable ops = []

                                          for _ in 1..2 do
                                              let op, r' = genKeyedOp r
                                              r <- r'

                                              match keyedW.Apply op cur with
                                              | Ok t ->
                                                  cur <- t
                                                  ops <- ops @ [ op ]
                                              | Error _ -> ()

                                          yield ops ]

                                lanes, r }

                    let pack =
                        FoldConfluence.laneFoldLawsAt keyedW surfaceFootprint keyedHash laneGen 3 2491 60

                    Expect.isEmpty (failed pack) (sprintf "the pack cannot see it: %A" (failed pack))

                    let law =
                        Conformance.footprintLawsAt keyedW surfaceFootprint keyedHash keyedGen 2491 300

                    Expect.isNonEmpty (failedNamed "footprint soundness" law) "this law can"

                testCase "witness-generic: the plan domain's own footprint passes"
                <| fun _ ->
                    let results =
                        Conformance.footprintLawsAt
                            FoldConfluenceTests.planW
                            FoldConfluenceTests.planFootprint
                            FoldConfluenceTests.planHash
                            planGen
                            2492
                            300

                    Expect.isEmpty (failed results) (sprintf "%A" (failed results))

                testCase "a plan footprint that drops Retitle's content write is RED"
                <| fun _ ->
                    let dropped (op: FoldConfluenceTests.PlanOp) =
                        match op with
                        | FoldConfluenceTests.Retitle(id, _) ->
                            { FoldConfluenceTests.planFootprint op with
                                ContentWrites = Set.empty
                                Reads = Set.singleton id }
                        | _ -> FoldConfluenceTests.planFootprint op

                    let results =
                        Conformance.footprintLawsAt
                            FoldConfluenceTests.planW
                            dropped
                            FoldConfluenceTests.planHash
                            planGen
                            2492
                            300

                    Expect.isNonEmpty (failedNamed "footprint soundness" results) "two retitles of one item collide"

                testCase "a footprint that is not a pure function of the op is RED, on determinism"
                <| fun _ ->
                    let mutable calls = 0

                    let drifting (op: KOp) =
                        calls <- calls + 1
                        let fp = keyedFootprint op

                        if calls % 2 = 0 then
                            { fp with
                                Reads = Set.add "drift" fp.Reads }
                        else
                            fp

                    let results = Conformance.footprintLawsAt keyedW drifting keyedHash keyedGen 2493 50
                    Expect.isNonEmpty (failedNamed "footprint determinism" results) "the drift is caught"

                testCase "starved: a footprint that declares every pair interfering never reaches an independent pair"
                <| fun _ ->
                    let everything (_: KOp) : Footprint =
                        { Reads = Set.singleton "root"
                          StructureWrites = Set.empty
                          ContentWrites = Set.singleton "root"
                          UnknownParentWrites = Set.empty
                          SlotReads = Set.empty
                          SlotWrites = Set.empty }

                    let results =
                        Conformance.footprintLawsAt keyedW everything keyedHash keyedGen 2494 100

                    let guard = failedNamed "Conformance.footprintLawsAt" results
                    Expect.equal (List.length guard) 1 (sprintf "the guard goes red: %A" results)

                    let cx = guard |> List.head |> (fun r -> defaultArg r.Counterexample "")
                    Expect.stringContains cx "independent pair" "naming the side it never reached"

                testCase "starved: a generator whose ops never collide never reaches an interfering pair"
                <| fun _ ->
                    // every draw adds a freshly numbered plan item — under the plan's honest
                    // footprint no two scripts ever touch one address
                    let fresh: StreamGen<FoldConfluenceTests.PlanOp, FoldConfluenceTests.Plan> =
                        { State0 = FoldConfluenceTests.basePlan
                          Op =
                            fun r ->
                                let v, r' = ConfRng.next r
                                FoldConfluenceTests.AddItem("u" + string v, "t"), r' }

                    let results =
                        Conformance.footprintLawsAt
                            FoldConfluenceTests.planW
                            FoldConfluenceTests.planFootprint
                            FoldConfluenceTests.planHash
                            fresh
                            2495
                            50

                    let guard = failedNamed "Conformance.footprintLawsAt" results
                    Expect.equal (List.length guard) 1 (sprintf "the guard goes red: %A" results)

                    let cx = guard |> List.head |> (fun r -> defaultArg r.Counterexample "")
                    Expect.stringContains cx "interfering pair" "naming the side it never reached" ]

          testList
              "FoldConfluence.laneDag"
              [ testCase "is the construction foldOnce folds: a hand-built DAG of the same lanes has the same ids"
                <| fun _ ->
                    let w = FoldConfluenceTests.planW
                    let baseOp = FoldConfluenceTests.SetShipped "p1"

                    let lanes =
                        [ [ FoldConfluenceTests.Retitle("p2", "a") ]
                          [ FoldConfluenceTests.SetShipped "p4"; FoldConfluenceTests.Retitle("p5", "b") ]
                          [] ]

                    let trial = FoldConfluence.laneDag OpStream.defaultHash w baseOp lanes
                    let dag, baseId, heads = trial.Dag, trial.BaseId, trial.Heads

                    let built (r: Result<string * Dag.T<_>, DagAppendFault>) =
                        match r with
                        | Ok v -> v
                        | Error e -> failtestf "append refused: %A" e

                    let b, d0 =
                        Dag.append OpStream.defaultHash w (Human "base") baseOp "" Dag.empty |> built

                    let hs, d =
                        lanes
                        |> List.indexed
                        |> List.fold
                            (fun (hs, d) (i, ops) ->
                                let h, d' =
                                    ops
                                    |> List.fold
                                        (fun (h, dd) op ->
                                            Dag.append OpStream.defaultHash w (Human("lane-" + string i)) op h dd
                                            |> built)
                                        (b, d)

                                hs @ [ h ], d')
                            ([], d0)

                    Expect.equal baseId b "the base node"
                    Expect.equal heads hs "the heads, in lane order (an empty lane's head is the base)"
                    Expect.equal (List.item 2 heads) baseId "the empty lane"

                    Expect.equal
                        (dag.Nodes |> Map.toList |> List.map fst)
                        (d.Nodes |> Map.toList |> List.map fst)
                        "the node set"

                testCase "identical lanes do not collapse: each lane has its own head"
                <| fun _ ->
                    let lane = [ FoldConfluenceTests.Retitle("p2", "same") ]

                    let trial =
                        FoldConfluence.laneDag
                            OpStream.defaultHash
                            FoldConfluenceTests.planW
                            (FoldConfluenceTests.SetShipped "p1")
                            [ lane; lane; lane ]

                    Expect.equal (List.length (List.distinct trial.Heads)) 3 "three distinct heads"

                testCase "the typed conflicts read off laneDag are the halt foldOnce reports"
                <| fun _ ->
                    let w = FoldConfluenceTests.planW
                    let baseOp = FoldConfluenceTests.SetShipped "p1"

                    let lanes =
                        [ [ FoldConfluenceTests.AddItem("n0", "left") ]
                          [ FoldConfluenceTests.Retitle("p3", "middle") ]
                          [ FoldConfluenceTests.AddItem("n0", "right") ] ]

                    let trial = FoldConfluence.laneDag OpStream.defaultHash w baseOp lanes
                    let dag, baseId, heads = trial.Dag, trial.BaseId, trial.Heads

                    let cs =
                        match
                            Dag.reconcileMany
                                w
                                FoldConfluenceTests.planFootprint
                                dag
                                baseId
                                FoldConfluenceTests.basePlan
                                heads
                        with
                        | Error(ReconcileFault.LanesInterfere cs) -> cs
                        | other -> failtestf "expected the lanes to interfere, got %A" other

                    Expect.isNonEmpty cs "the typed conflict list"

                    Expect.all cs (fun c -> c.Address = "n0") "every conflict is on the item both lanes add"

                    let outcome =
                        FoldConfluence.foldOnce
                            w
                            FoldConfluenceTests.planFootprint
                            OpStream.defaultHash
                            FoldConfluenceTests.planHash
                            FoldConfluenceTests.basePlan
                            baseOp
                            lanes

                    Expect.equal
                        outcome
                        (LaneHalted(FoldConfluence.canonicalConflictReport w.Encode cs))
                        "foldOnce's halt is the canonical rendering of exactly these conflicts"

                testCase "a folding lane set: the script reconciled off laneDag replays to the state foldOnce hashes"
                <| fun _ ->
                    let w = FoldConfluenceTests.planW
                    let baseOp = FoldConfluenceTests.SetShipped "p1"

                    let lanes =
                        [ [ FoldConfluenceTests.Retitle("p2", "a") ]
                          [ FoldConfluenceTests.SetShipped "p4" ]
                          [ FoldConfluenceTests.AddItem("n1", "c") ] ]

                    let trial = FoldConfluence.laneDag OpStream.defaultHash w baseOp lanes
                    let dag, baseId, heads = trial.Dag, trial.BaseId, trial.Heads

                    let final =
                        match
                            Dag.reconcileMany
                                w
                                FoldConfluenceTests.planFootprint
                                dag
                                baseId
                                FoldConfluenceTests.basePlan
                                heads
                        with
                        | Ok script ->
                            script
                            |> List.fold
                                (fun acc op -> acc |> Result.bind (w.Apply op))
                                (Ok FoldConfluenceTests.basePlan)
                        | Error e -> failtestf "expected a clean fold, got %A" e

                    let outcome =
                        FoldConfluence.foldOnce
                            w
                            FoldConfluenceTests.planFootprint
                            OpStream.defaultHash
                            FoldConfluenceTests.planHash
                            FoldConfluenceTests.basePlan
                            baseOp
                            lanes

                    match final with
                    | Ok p -> Expect.equal outcome (LaneFolded(FoldConfluenceTests.planHash p)) "one state"
                    | Error e -> failtestf "the reconciled script did not replay: %A" e ] ]
