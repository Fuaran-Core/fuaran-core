module Fuaran.Core.Tests.LaneTests

// Phase 311 — lanes and a whole-DAG total order, with the multi-parent primitives: `Dag.appendOn` /
// `mergeAll` / `mergeWith` / `nodeId`, `commonBase`, `conflictsOfNodes` / `conflictsOfHeads`, the
// topological `firstBreak`, `totalOrderBy` / `ranks` / `replayAllBy`, the lane store (`loadLanes`,
// `verifyLanes`, `laneCollisions`, `disjointRoots`, `appendOnLane`, `laneKey`), `rehashWith`, the
// party attestation, `prunable`, `appendIf`, and `Conformance.laneLaws`. Three downstream consumers'
// orders are reproduced over one fixture by `totalOrderBy` with each consumer's own key.

open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference.Counter

let private sw = witness
let private h = OpStream.defaultHash
let private x = Human "x"

let private ok (r: Result<'a, 'e>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwithf "unexpected refusal: %A" e

/// genesis g; a and b fork off g; c extends a; m merges c and b.
let private forkMerge () =
    let g, d1 = Dag.append h sw x (Inc 1) "" Dag.empty |> Reference.built
    let a, d2 = Dag.append h sw x (Inc 2) g d1 |> Reference.built
    let b, d3 = Dag.append h sw x (Inc 3) g d2 |> Reference.built
    let c, d4 = Dag.append h sw x (Inc 4) a d3 |> Reference.built
    let m, d5 = Dag.merge h sw x (Inc 5) c b d4 |> Reference.built
    g, a, b, c, m, d5

/// A three-headed DAG: genesis g, and three lanes forked off it.
let private threeHeads () =
    let g, d1 = Dag.append h sw x (Inc 1) "" Dag.empty |> Reference.built
    let p, d2 = Dag.append h sw (Human "p") (Inc 2) g d1 |> Reference.built
    let q, d3 = Dag.append h sw (Human "q") (Inc 3) g d2 |> Reference.built
    let r, d4 = Dag.append h sw (Human "r") (Inc 4) g d3 |> Reference.built
    g, [ p; q; r ], d4

let private ordinal (xs: string list) =
    xs |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

// ---- the three consumers' orders, as they compute them (the reference the test reproduces) ----

/// A downstream engine's order: at every step, the smallest-ordinal-id node whose in-union parents
/// have all been emitted — the ready set recomputed from the whole remainder each step.
let private smallestIdDrain (dag: Dag.T<'Op>) : string list =
    let mutable pending =
        dag.Nodes
        |> Map.map (fun _ n -> n.Parents |> List.filter dag.Nodes.ContainsKey |> Set.ofList)

    let out = ResizeArray<string>()

    while not pending.IsEmpty do
        let ready =
            pending
            |> Map.toList
            |> List.filter (fun (_, ps) -> Set.isEmpty ps)
            |> List.map fst
            |> ordinal

        match ready with
        | [] -> failwith "cycle"
        | id :: _ ->
            out.Add id
            pending <- pending |> Map.remove id |> Map.map (fun _ ps -> Set.remove id ps)

    List.ofSeq out

/// A downstream app's order: every node sorted by (longest-path rank, lane id, node id).
let private rankLaneSort (loaded: Dag.Loaded<'Op>) : string list =
    let rec rankOf (memo: Map<string, int>) (id: string) : int * Map<string, int> =
        match Map.tryFind id memo with
        | Some r -> r, memo
        | None ->
            match Map.tryFind id loaded.Dag.Nodes with
            | None -> -1, memo
            | Some n when List.isEmpty n.Parents -> 0, Map.add id 0 memo
            | Some n ->
                let r, memo' =
                    n.Parents
                    |> List.fold
                        (fun (best, m) p ->
                            let rp, m' = rankOf m p
                            max best rp, m')
                        (-1, memo)

                let r = max (r + 1) 0
                r, Map.add id r memo'

    let ranks = loaded.Dag.Nodes |> Map.fold (fun m id _ -> snd (rankOf m id)) Map.empty

    loaded.Dag.Nodes
    |> Map.toList
    |> List.map fst
    |> List.sortBy (fun id -> ranks[id], loaded.LaneOf[id], id)

/// A downstream session's order by a domain rank: a Kahn drain taking the smallest (domain rank, id) each
/// step, the ready list re-sorted after every push.
let private domainRankDrain (rankOf: 'Op -> int) (dag: Dag.T<'Op>) : string list =
    let parentsIn (n: DagNode<'Op>) =
        n.Parents |> List.filter dag.Nodes.ContainsKey

    let mutable indeg = dag.Nodes |> Map.map (fun _ n -> List.length (parentsIn n))

    let children =
        dag.Nodes
        |> Map.toList
        |> List.collect (fun (id, n) -> parentsIn n |> List.map (fun p -> p, id))
        |> List.groupBy fst
        |> List.map (fun (p, cs) -> p, cs |> List.map snd)
        |> Map.ofList

    let rank id = rankOf dag.Nodes[id].Op, id

    let mutable ready =
        indeg
        |> Map.toList
        |> List.filter (fun (_, d) -> d = 0)
        |> List.map fst
        |> List.sortBy rank

    let out = ResizeArray<string>()

    while not ready.IsEmpty do
        let id = List.head ready
        ready <- List.tail ready
        out.Add id

        for k in Map.tryFind id children |> Option.defaultValue [] do
            indeg <- Map.add k (indeg[k] - 1) indeg

            if indeg[k] = 0 then
                ready <- (k :: ready) |> List.sortBy rank

    List.ofSeq out

/// The shared fixture: three lanes off one genesis, each writer appending onto its own last node and
/// now and then onto every head of the union (the shape a writer that folds before it writes takes).
let private fixtureStore () : Dag.Loaded<CounterOp> =
    let mutable store: Dag.Loaded<CounterOp> = { Dag = Dag.empty; LaneOf = Map.empty }
    let mutable tips: Map<string, string> = Map.empty

    let write lane op (parents: string list) =
        let id, s = Dag.appendOnLane h sw (Human lane) op parents lane store |> ok
        store <- s
        tips <- Map.add lane id tips

    let tip lane = [ tips[lane] ]
    let heads () = Dag.heads store.Dag

    write "alpha" (Inc 1) []
    let g = tips["alpha"]
    write "beta" (Inc 2) [ g ]
    write "gamma" (Inc 3) [ g ]
    write "alpha" (Inc 4) (tip "alpha")
    write "beta" (Dec 1) (tip "beta")
    write "gamma" (Inc 5) (heads ())
    write "alpha" (Inc 6) (tip "alpha")
    write "beta" (Inc 7) (tip "beta")
    write "gamma" (Dec 2) (tip "gamma")
    write "alpha" (Inc 8) (heads ())
    write "beta" (Inc 9) (tip "beta")
    store

// ---- a fork-and-kill domain for the replay-order case (the "kill that folds first") ----

type ModelOp =
    | Seed
    | Fork of string
    | Kill of string

let private modelEncode (op: ModelOp) =
    match op with
    | Seed -> "\"seed\""
    | Fork s -> "\"fork:" + s + "\""
    | Kill s -> "\"kill:" + s + "\""

let private modelWitness: StreamWitness<ModelOp, Set<string> * int, string> =
    { Apply =
        fun op (killed, forks) ->
            match op with
            | Seed -> Ok(killed, forks)
            | Fork s when Set.contains s killed -> Error("KilledState " + s)
            | Fork _ -> Ok(killed, forks + 1)
            | Kill s -> Ok(Set.add s killed, forks)
      Encode = modelEncode
      Decode =
        fun s ->
            match s.Trim('"').Split(':') with
            | [| "seed" |] -> Ok Seed
            | [| "fork"; v |] -> Ok(Fork v)
            | [| "kill"; v |] -> Ok(Kill v)
            | _ -> Error("unknown op " + s) }

/// The domain's own fold rank: a kill folds after every fork that is ready beside it.
let private foldRank (op: ModelOp) =
    match op with
    | Seed
    | Fork _ -> 0
    | Kill _ -> 1

/// A fork and a kill of the same state, siblings under one seed — no edge between them, so only the
/// order decides whether the fork sees the state alive. `salt` varies the seed's actor, and so every id.
let private killAndFork (salt: int) =
    let s, d1 =
        Dag.append h modelWitness (Human("seed-" + string salt)) Seed "" Dag.empty
        |> Reference.built

    let f, d2 = Dag.append h modelWitness (Human "w") (Fork "S") s d1 |> Reference.built
    let k, d3 = Dag.append h modelWitness (Human "w") (Kill "S") s d2 |> Reference.built
    s, f, k, d3

/// A recording sink: signs `sig:<subject>` and accepts exactly that.
let private recordingSink: IAttestationSink =
    { new IAttestationSink with
        member _.Sign head =
            Some
                { Head = head
                  KeyId = "k1"
                  Signature = "sig:" + head }

        member _.Verify attestation head = attestation.Signature = "sig:" + head }

let private asOps (cs: MergeConflict<DagNode<'Op>> list) : MergeConflict<'Op> list =
    cs
    |> List.map (fun cf ->
        { Left = cf.Left.Op
          Right = cf.Right.Op
          Address = cf.Address
          Shape = cf.Shape })

[<Tests>]
let tests =
    testList
        "Phase 311 lanes and the whole-DAG order"
        [ testList
              "the multi-parent primitives"
              [ testCase "appendOn is append at one parent, merge at two, genesis at none"
                <| fun _ ->
                    let g, a, b, _, _, dag = forkMerge ()

                    Expect.equal (Dag.appendOn h sw x (Inc 9) [] dag) (Dag.append h sw x (Inc 9) "" dag) "genesis"
                    Expect.equal (Dag.appendOn h sw x (Inc 9) [ a ] dag) (Dag.append h sw x (Inc 9) a dag) "one parent"

                    Expect.equal
                        (Dag.appendOn h sw x (Inc 9) [ a; b ] dag)
                        (Dag.merge h sw x (Inc 9) a b dag)
                        "two parents"

                    let id, d = Dag.appendOn h sw x (Inc 9) [ g; a; b ] dag |> ok
                    Expect.equal d.Nodes[id].Parents [ g; a; b ] "the parents are stored as given"
                    Expect.equal id (Dag.nodeId h sw.Encode [ b; g; a ] x (Inc 9)) "nodeId is the minted id, order-free"
                    Expect.isTrue (Dag.verifyDag h sw d) "an N-parent node verifies"

                testCase "appendOn judges the parents in order and refuses as append and merge do"
                <| fun _ ->
                    let _, a, _, _, _, dag = forkMerge ()

                    Expect.equal
                        (Dag.appendOn h sw x (Inc 9) [ a; "" ] dag)
                        (Error DagAppendFault.EmptyParentId)
                        "an empty parent id"

                    Expect.equal
                        (Dag.appendOn h sw x (Inc 9) [ a; "p,q"; "zz" ] dag)
                        (Error(DagAppendFault.CommaInParentId "p,q"))
                        "the first refusal in order"

                    Expect.equal
                        (Dag.appendOn h sw x (Inc 9) [ "zz"; a ] dag)
                        (Error(DagAppendFault.UnknownParent "zz"))
                        "an unknown parent"

                testCase "mergeAll is a function of the head set and is the node appendOn mints over it"
                <| fun _ ->
                    let _, hs, dag = threeHeads ()
                    let m, d = Dag.mergeAll h sw x (Inc 7) hs dag |> ok

                    for perm in [ hs; List.rev hs; [ hs[1]; hs[2]; hs[0]; hs[1] ] ] do
                        let m', d' = Dag.mergeAll h sw x (Inc 7) perm dag |> ok
                        Expect.equal m' m "the same id"
                        Expect.equal d'.Nodes[m'].Parents (ordinal hs) "the same stored parents"

                    for perm in [ hs; List.rev hs ] do
                        Expect.equal (Dag.appendOn h sw x (Inc 7) perm dag |> ok |> fst) m "appendOn's id"

                    Expect.equal (Dag.heads d) [ m ] "one head"
                    Expect.equal (Dag.tryReplayTo sw 0 d m) (Ok 17) "every lane once, then the merge op"
                    let one, _ = Dag.mergeAll h sw x (Inc 7) [ hs[0] ] dag |> ok
                    Expect.equal one (Dag.append h sw x (Inc 7) hs[0] dag |> ok |> fst) "one head is an append"

                testCase "mergeWith records the script as the merge node and a chain after it"
                <| fun _ ->
                    let _, a, b, _, _, dag = forkMerge ()
                    let ids, d = Dag.mergeWith h sw x [ Inc 10; Dec 3; Inc 20 ] a b dag |> ok
                    Expect.equal (List.length ids) 3 "three nodes"
                    Expect.equal d.Nodes[ids[0]].Parents [ a; b ] "the first is the merge"
                    Expect.equal d.Nodes[ids[2]].Parents [ ids[1] ] "the rest a chain"

                    Expect.equal
                        (Dag.tryReplayTo sw 0 d (List.last ids))
                        (Ok(1 + 2 + 3 + 10 - 3 + 20))
                        "union, then the script"

                    Expect.equal (Dag.mergeWith h sw x [] a b dag) (Ok([], dag)) "an empty script records nothing"

                    Expect.equal
                        (Dag.mergeWith h sw x [] a "zz" dag)
                        (Error(DagAppendFault.UnknownParent "zz"))
                        "the parents are judged whatever the script"

                testCase "commonBase is mergeBase at two heads, the head at one, and order-free on a criss-cross"
                <| fun _ ->
                    let _, a, b, c, _, dag = forkMerge ()
                    Expect.equal (Dag.commonBase dag [ c; b ]) (Dag.mergeBase dag c b) "two heads"
                    Expect.equal (Dag.commonBase dag [ c ]) (Some c) "one head"
                    Expect.equal (Dag.commonBase dag []) None "no heads"
                    Expect.equal (Dag.commonBase dag [ c; "zz" ]) None "an absent head"
                    // a criss-cross: two merges of the same two lanes, a lane off each, and a third lane
                    let m1, d1 = Dag.merge h sw x (Inc 11) a b dag |> ok
                    let m2, d2 = Dag.merge h sw x (Inc 12) a b d1 |> ok
                    let l1, d3 = Dag.append h sw x (Inc 13) m1 d2 |> ok
                    let l2, d4 = Dag.append h sw x (Inc 14) m2 d3 |> ok
                    let hs = [ l1; l2; c ]
                    let cb = Dag.commonBase d4 hs

                    for perm in [ hs; List.rev hs; [ c; l1; l2 ] ] do
                        Expect.equal (Dag.commonBase d4 perm) cb "order-free"
                        Expect.equal (Dag.Reach.commonBase (Dag.Reach.ofDag d4) perm) cb "the index agrees"

                    Expect.equal cb (Some a) "a is the deepest node common to all three"

                testCase "conflictsOfNodes is conflicts with nodes, and conflictsOfHeads is the LanesInterfere report"
                <| fun _ ->
                    let g, a, b, c, _, dag = forkMerge ()

                    let fp (_: CounterOp) =
                        { Footprint.empty with
                            ContentWrites = Set.singleton "counter" }

                    let nodesA = Dag.between dag g c
                    let nodesB = Dag.between dag g b
                    let byNode = Dag.conflictsOfNodes fp nodesA nodesB

                    let byOp = Dag.conflicts fp (nodesA |> List.map _.Op) (nodesB |> List.map _.Op)

                    Expect.equal (asOps byNode) byOp "the same report"
                    Expect.isNonEmpty byNode "the lanes interfere"

                    let viaHeads = Dag.conflictsOfHeads fp dag g [ c; b ]

                    match Dag.reconcileMany sw fp dag g 1 [ c; b ] with
                    | Error(ReconcileFault.LanesInterfere cs) ->
                        Expect.equal (asOps viaHeads) cs "the reconcile's own report"
                        Expect.equal (viaHeads |> List.map _.Left.Id) [ a; c ] "named by node"
                    | other -> failtestf "expected LanesInterfere, got %A" other

                testCase "firstBreak names the earliest break in the history, not the smallest id"
                <| fun _ ->
                    // the tampered ancestor's id and the tampered descendant's sort both ways round
                    // across the salts; the ancestor is named every time
                    let mutable descendantFirst = 0

                    for salt in 0..30 do
                        let g, d1 = Dag.append h sw (Human("t" + string salt)) (Inc 1) "" Dag.empty |> ok
                        let a, d2 = Dag.append h sw x (Inc 2) g d1 |> ok
                        let b, d3 = Dag.append h sw x (Inc 3) a d2 |> ok

                        if System.String.CompareOrdinal(b, a) < 0 then
                            descendantFirst <- descendantFirst + 1

                        let tampered: Dag.T<CounterOp> =
                            DagOf.nodes (
                                d3.Nodes
                                |> Map.add a { d3.Nodes[a] with Op = Inc 99 }
                                |> Map.add b { d3.Nodes[b] with Op = Inc 98 }
                            )

                        match Dag.firstBreak h sw tampered with
                        | Some br -> Expect.equal br.NodeId a "the ancestor"
                        | None -> failtest "a tampered DAG verified"

                    Expect.isGreaterThan descendantFirst 0 "the probe drew a descendant whose id sorts first" ]

          testList
              "the whole-DAG order"
              [ testCase "totalOrderBy at a constant key is the smallest-id drain, and each head's tryTopoOrder"
                <| fun _ ->
                    let loaded = fixtureStore ()
                    let dag = loaded.Dag
                    let order = Dag.totalOrderBy (fun _ -> 0) dag |> ok
                    Expect.equal order (smallestIdDrain dag) "the downstream engine's order"

                    for hd in Dag.heads dag do
                        let c = Dag.ancestorsOf dag hd
                        Expect.equal (Ok(order |> List.filter c.Contains)) (Dag.tryTopoOrder dag hd) "restricted"

                testCase "three consumers' orders are each totalOrderBy with the consumer's own key"
                <| fun _ ->
                    let loaded = fixtureStore ()
                    let dag = loaded.Dag
                    let ranks = Dag.ranks dag |> ok

                    // the engine: the smallest id
                    Expect.equal (Dag.totalOrderBy (fun _ -> 0) dag |> ok) (smallestIdDrain dag) "engine"

                    // the app: (longest-path rank, lane, id), a sort
                    Expect.equal
                        (Dag.totalOrderBy (fun (n: DagNode<CounterOp>) -> ranks[n.Id], loaded.LaneOf[n.Id]) dag
                         |> ok)
                        (rankLaneSort loaded)
                        "app"

                    // the session with a domain rank: (domain rank, id), a drain
                    let opRank (op: CounterOp) =
                        match op with
                        | Inc _ -> 0
                        | Dec _ -> 1

                    Expect.equal
                        (Dag.totalOrderBy (fun (n: DagNode<CounterOp>) -> opRank n.Op) dag |> ok)
                        (domainRankDrain opRank dag)
                        "domain rank"

                testCase "ranks are the Lamport depth"
                <| fun _ ->
                    let g, a, b, c, m, dag = forkMerge ()
                    let r = Dag.ranks dag |> ok
                    Expect.equal [ r[g]; r[a]; r[b]; r[c]; r[m] ] [ 0; 1; 1; 2; 3 ] "depths"

                testCase "a cycle is a typed refusal naming every unplaced node"
                <| fun _ ->
                    let g, _, _, _, _, dag = forkMerge ()
                    let n = dag.Nodes[g]

                    let cyclic: Dag.T<CounterOp> =
                        DagOf.nodes (
                            dag.Nodes
                            |> Map.add
                                "cy-1"
                                { n with
                                    Id = "cy-1"
                                    Parents = [ "cy-2" ] }
                            |> Map.add
                                "cy-2"
                                { n with
                                    Id = "cy-2"
                                    Parents = [ "cy-1" ] }
                            |> Map.add
                                "cy-3"
                                { n with
                                    Id = "cy-3"
                                    Parents = [ "cy-2" ] }
                        )

                    let expected = [ "cy-1"; "cy-2"; "cy-3" ]

                    Expect.equal
                        (Dag.totalOrderBy (fun _ -> 0) cyclic)
                        (Error(Dag.TotalOrderFault.Cyclic expected))
                        "order"

                    Expect.equal (Dag.ranks cyclic) (Error(Dag.TotalOrderFault.Cyclic expected)) "ranks"

                    Expect.equal
                        (Dag.replayAllBy (fun _ -> 0) sw 0 cyclic)
                        (Error(Dag.ReplayAllFault.Cyclic expected))
                        "replay"

                testCase "replayAllBy folds every node once and names the first rejection"
                <| fun _ ->
                    let _, _, _, _, _, dag = forkMerge ()
                    Expect.equal (Dag.replayAllBy (fun _ -> 0) sw 0 dag) (Ok 15) "every op once"
                    let m, d = Dag.append h sw x (Dec 100) (Dag.heads dag |> List.head) dag |> ok

                    Expect.equal
                        (Dag.replayAllBy (fun _ -> 0) sw 0 d)
                        (Error(Dag.ReplayAllFault.Rejected(m, "would go negative")))
                        "the rejecting node"

                testCase "a domain key replays a kill-and-fork history deterministically where the id order does not"
                <| fun _ ->
                    let mutable idOrderFailed = 0

                    for salt in 0..40 do
                        let _, f, k, dag = killAndFork salt

                        let byRank =
                            Dag.replayAllBy (fun (n: DagNode<ModelOp>) -> foldRank n.Op) modelWitness (Set.empty, 0) dag

                        Expect.equal byRank (Ok(Set.ofList [ "S" ], 1)) "the fork folds before the kill, every time"

                        Expect.equal
                            (Dag.totalOrderBy (fun (n: DagNode<ModelOp>) -> foldRank n.Op) dag |> ok)
                            (domainRankDrain foldRank dag)
                            "the session's own drain"

                        match Dag.replayAllBy (fun _ -> 0) modelWitness (Set.empty, 0) dag with
                        | Error(Dag.ReplayAllFault.Rejected(id, _)) ->
                            Expect.equal id f "only the fork can be refused"
                            Expect.isLessThan (System.String.CompareOrdinal(k, f)) 0 "when the kill's id sorts first"
                            idOrderFailed <- idOrderFailed + 1
                        | _ -> ()

                    Expect.isGreaterThan idOrderFailed 0 "the id order folded the kill first for some salt" ]

          testList
              "the lane store"
              [ testCase "loadLanes is order-free, round-trips, and is the union of the lane texts"
                <| fun _ ->
                    let loaded = fixtureStore ()
                    let texts = Dag.lanesToJsonl sw.Encode loaded
                    Expect.equal (texts |> List.map fst) [ "alpha"; "beta"; "gamma" ] "one file per lane"

                    for perm in [ texts; List.rev texts; [ texts[1]; texts[2]; texts[0] ] ] do
                        let l = Dag.loadLanes sw perm |> ok
                        Expect.equal (Dag.toJsonl sw.Encode l.Dag) (Dag.toJsonl sw.Encode loaded.Dag) "the union"
                        Expect.equal l.LaneOf loaded.LaneOf "the attribution"
                        Expect.equal (Dag.totalOrder l) (Dag.totalOrder loaded) "the order"
                        Expect.equal (Dag.replayAll sw 0 l) (Dag.replayAll sw 0 loaded) "the replay"

                    let concatenated =
                        Dag.fromJsonl sw (texts |> List.map snd |> String.concat "\n") |> ok

                    Expect.equal (Dag.toJsonl sw.Encode concatenated) (Dag.toJsonl sw.Encode loaded.Dag) "concatenated"
                    Expect.equal (Dag.tryLanesToJsonl sw.Encode loaded) (Ok texts) "the checked writer agrees"

                testCase "loadLanes refuses a duplicate lane, an unreadable lane and a cross-lane collision"
                <| fun _ ->
                    let loaded = fixtureStore ()
                    let texts = Dag.lanesToJsonl sw.Encode loaded

                    Expect.equal
                        (Dag.loadLanes sw (texts @ [ texts[0] ]))
                        (Error(StreamLoadFault.DuplicateLane "alpha"))
                        "duplicate"

                    match Dag.loadLanes sw (texts @ [ "zeta", "{not json" ]) with
                    | Error(StreamLoadFault.Unreadable(Some "zeta", f)) ->
                        Expect.equal f.Line 1 "the typed fault names the lane's line"
                    | other -> failtestf "expected Unreadable zeta, got %A" other

                    let some =
                        loaded.Dag.Nodes
                        |> Map.toList
                        |> List.find (fun (id, _) -> loaded.LaneOf[id] = "beta")
                        |> snd

                    let copy =
                        "zeta", Dag.toJsonl sw.Encode (DagOf.nodes (Map.ofList [ some.Id, some ]))

                    let l = Dag.loadLanes sw (copy :: texts) |> ok
                    Expect.equal l.LaneOf[some.Id] "beta" "an identical node keeps the smaller lane"

                    let forged =
                        "aardvark",
                        Dag.toJsonl sw.Encode (DagOf.nodes (Map.ofList [ some.Id, { some with Op = Inc 1000 } ]))

                    Expect.equal
                        (Dag.loadLanes sw (texts @ [ forged ]))
                        (Error(StreamLoadFault.Collision(some.Id, [ "aardvark"; "beta" ])))
                        "a different node under one id"

                testCase "verifyLanes names the lane of the earliest faulty node"
                <| fun _ ->
                    let loaded = fixtureStore ()
                    Expect.equal (Dag.verifyLanes h sw loaded) (Ok()) "intact"

                    let victim =
                        loaded.Dag.Nodes
                        |> Map.toList
                        |> List.find (fun (id, _) -> loaded.LaneOf[id] = "gamma")
                        |> fst

                    let tampered =
                        { loaded with
                            Dag =
                                DagOf.nodes (
                                    loaded.Dag.Nodes
                                    |> Map.add
                                        victim
                                        { loaded.Dag.Nodes[victim] with
                                            Op = Inc 77 }
                                ) }

                    match Dag.verifyLanes h sw tampered with
                    | Error b ->
                        Expect.equal b.Lane "gamma" "the lane"
                        Expect.equal b.Break.NodeId victim "the node"
                    | Ok() -> failtest "a tampered store verified"

                testCase "laneCollisions names a lane with two heads and not a writer that appends through a merge"
                <| fun _ ->
                    let loaded = fixtureStore ()
                    Expect.equal (Dag.laneCollisions loaded) [] "every writer appends onto its own history"

                    let gammaTip =
                        loaded.Dag.Nodes
                        |> Map.toList
                        |> List.filter (fun (id, _) -> loaded.LaneOf[id] = "gamma")
                        |> List.map fst
                        |> List.find (fun id ->
                            loaded.Dag.Nodes
                            |> Map.forall (fun _ n ->
                                not (List.contains id n.Parents) || loaded.LaneOf[n.Id] <> "gamma"))

                    let parents = loaded.Dag.Nodes[gammaTip].Parents

                    let sib, forked =
                        Dag.appendOnLane h sw (Human "gamma") (Inc 555) parents "gamma" loaded |> ok

                    Expect.equal (Dag.laneCollisions forked) [ "gamma", ordinal [ gammaTip; sib ] ] "two heads in gamma"

                testCase "disjointRoots names every head and its root when the heads share nothing"
                <| fun _ ->
                    let r1, d1 = Dag.append h sw (Human "one") (Inc 1) "" Dag.empty |> ok
                    let a1, d2 = Dag.append h sw (Human "one") (Inc 2) r1 d1 |> ok
                    let r2, d3 = Dag.append h sw (Human "two") (Inc 3) "" d2 |> ok
                    let a2, d4 = Dag.append h sw (Human "two") (Inc 4) r2 d3 |> ok

                    let expected =
                        [ a1, r1; a2, r2 ]
                        |> List.sortWith (fun (p, _) (q, _) -> System.String.CompareOrdinal(p, q))

                    Expect.equal (Dag.disjointRoots d4) expected "disjoint"
                    let _, _, _, _, _, dag = forkMerge ()
                    Expect.equal (Dag.disjointRoots dag) [] "one history"
                    let m, d5 = Dag.merge h sw x (Inc 5) a1 a2 d4 |> ok
                    ignore m
                    Expect.equal (Dag.disjointRoots d5) [] "merged"

                testCase "laneKey is a LaneKey (lane, seq, id), seq counting the lane's own history"
                <| fun _ ->
                    let loaded = fixtureStore ()
                    let key = Dag.laneKey loaded

                    for lane in [ "alpha"; "beta"; "gamma" ] do
                        let ns =
                            loaded.Dag.Nodes
                            |> Map.toList
                            |> List.filter (fun (id, _) -> loaded.LaneOf[id] = lane)

                        for id, n in ns do
                            Expect.equal (key n).Lane lane "the node's lane"
                            Expect.equal (key n).Id id "the node's id"

                        let seqs = ns |> List.map (fun (_, n) -> (key n).Seq) |> List.sort
                        Expect.equal seqs [ 0 .. List.length ns - 1 ] ("one position per node in " + lane)

                    // Phase 410: the record compares as the `(lane, seq, id)` triple it replaced did
                    let nodes = loaded.Dag.Nodes |> Map.toList |> List.map snd
                    let triple (k: Dag.LaneKey) = k.Lane, k.Seq, k.Id

                    for p in nodes do
                        for q in nodes do
                            Expect.equal
                                (sign (compare (key p) (key q)))
                                (sign (compare (triple (key p)) (triple (key q))))
                                "field order is the triple's order"

                    Expect.equal (Dag.totalOrder loaded) (Dag.totalOrderBy key loaded.Dag) "the default order"

                testCase "rehashWith re-mints under SHA-256 and back, and refuses an unverified source"
                <| fun _ ->
                    let loaded = fixtureStore ()
                    let there, ids = Dag.rehashWith h OpStream.sha256Hash sw loaded.Dag |> ok
                    Expect.isTrue (Dag.verifyDag OpStream.sha256Hash sw there) "verifies under the new hash"
                    Expect.isFalse (Dag.verifyDag h sw there) "and not under the old"
                    let back, ids' = Dag.rehashWith OpStream.sha256Hash h sw there |> ok
                    Expect.equal (Dag.toJsonl sw.Encode back) (Dag.toJsonl sw.Encode loaded.Dag) "round trip"

                    for KeyValue(o, n) in ids do
                        Expect.equal ids'[n] o "the id map inverts"

                    let relaned, _ = Dag.rehashLanes h OpStream.sha256Hash sw loaded |> ok
                    Expect.equal (Dag.verifyLanes OpStream.sha256Hash sw relaned) (Ok()) "the lanes carried"

                    Expect.equal
                        (Dag.lanesToJsonl sw.Encode relaned |> List.map fst)
                        [ "alpha"; "beta"; "gamma" ]
                        "lanes"

                    let victim = loaded.Dag.Nodes |> Map.toList |> List.head |> fst

                    let tampered: Dag.T<CounterOp> =
                        DagOf.nodes (
                            loaded.Dag.Nodes
                            |> Map.add
                                victim
                                { loaded.Dag.Nodes[victim] with
                                    Op = Inc 31 }
                        )

                    match Dag.rehashWith h OpStream.sha256Hash sw tampered with
                    | Error(Dag.RehashFault.Unverified b) -> Expect.equal b.NodeId victim "the break"
                    | other -> failtestf "expected Unverified, got %A" other

                testCase "a party attests a node and nobody else's attestation verifies for it"
                <| fun _ ->
                    let _, _, b, _, m, dag = forkMerge ()
                    let alice = Human "alice"

                    let att =
                        match Dag.attestHead recordingSink h alice dag m with
                        | Ok(Some a) -> a
                        | other -> failtestf "expected an attestation, got %A" other

                    Expect.equal att.Head (OpStream.attestationSubject h alice m) "the party-bound subject"
                    Expect.isTrue (Dag.verifyAttestation recordingSink h alice att dag m) "verifies"
                    Expect.isFalse (Dag.verifyAttestation recordingSink h (Human "bob") att dag m) "not as bob"
                    Expect.isFalse (Dag.verifyAttestation recordingSink h alice att dag b) "not for another node"

                    Expect.equal
                        (Dag.attestHead recordingSink h alice dag "zz")
                        (Error(Dag.DagAttestFault.UnknownNode "zz"))
                        "an unknown node"

                    Expect.equal (Dag.attestHead OpStream.noAttestation h alice dag m) (Ok None) "the no-op sink"

                    let records =
                        [ Inc 1; Inc 2 ]
                        |> List.fold (fun st op -> OpStream.append h sw x op 0 st |> ok |> snd) []

                    let linear = OpStream.attestHeadAs recordingSink h alice records |> Option.get
                    Expect.isTrue (OpStream.verifyAttestationAs recordingSink h alice linear records) "linear"

                    Expect.isFalse
                        (OpStream.verifyAttestationAs recordingSink h (Human "bob") linear records)
                        "linear, bob"

                testCase "prunable names the branches no root needs, and the pruned DAG keeps every root"
                <| fun _ ->
                    let g, a, b, c, _, dag0 = forkMerge ()
                    // an abandoned branch off b
                    let z, dag = Dag.append h sw (Human "abandoned") (Inc 40) b dag0 |> ok
                    let heads = Dag.heads dag
                    let keepRoots = heads |> List.filter (fun hd -> hd <> z)
                    let reach = Dag.Reach.ofDag dag
                    let dropped = Dag.prunable reach keepRoots |> ok
                    Expect.equal dropped [ z ] "only the abandoned node"

                    let pruned: Dag.T<CounterOp> =
                        DagOf.nodes (dag.Nodes |> Map.filter (fun id _ -> id <> z))

                    Expect.isTrue (Dag.verifyDag h sw pruned) "verifies"

                    for r in keepRoots do
                        Expect.equal (Dag.tryReplayTo sw 0 pruned r) (Dag.tryReplayTo sw 0 dag r) "replays as before"

                    Expect.equal (Dag.prunable reach [ "zz" ]) (Error "zz") "an unknown root"

                    Expect.equal
                        (Dag.prunable reach [ c ] |> ok |> List.sort)
                        (List.sort [ b; z; Dag.heads dag0 |> List.head ])
                        "keep only c"

                    ignore (g, a)

                testCase "appendIf appends onto exactly the current heads and refuses a moved head set"
                <| fun _ ->
                    let _, hs, dag = threeHeads ()
                    let state = Dag.replayAllBy (fun _ -> 0) sw 0 dag |> ok

                    let written = Dag.appendIf h sw (List.rev hs) x (Inc 5) state dag |> ok
                    let st', id, d = written.State, written.Id, written.Dag
                    Expect.equal st' (state + 5) "applied at the caller's state"
                    Expect.equal id (Dag.mergeAll h sw x (Inc 5) hs dag |> ok |> fst) "mergeAll's node"
                    Expect.equal (Dag.heads d) [ id ] "converged"

                    Expect.equal
                        (Dag.appendIf h sw hs x (Inc 6) st' d)
                        (Error(Dag.DagAppendIfRejection.StaleHeads(ordinal hs, [ id ])))
                        "stale"

                    Expect.equal
                        (Dag.appendIf h sw [ id ] x (Dec 1000) st' d)
                        (Error(Dag.DagAppendIfRejection.Domain "would go negative"))
                        "the domain's rejection"

                    let genesis = Dag.appendIf h sw [] x (Inc 1) 0 Dag.empty |> ok

                    Expect.equal genesis.State 1 "genesis on an empty DAG"
                    Expect.equal (Dag.heads genesis.Dag) [ genesis.Id ] "one node" ] ]

/// The `Conformance.laneLaws` cases — the suite `proofs.json`'s Phase 311 tested row cites.
[<Tests>]
let laneLawTests =
    testList
        "Conformance.laneLaws"
        [ testCase "laneLaws certify the reference stream witness green"
          <| fun _ ->
              let results =
                  Conformance.laneLaws ConformanceTests.sw ConformanceTests.streamGen h 311 100

              Expect.equal (List.length results) 10 "nine laws and the lane-shape guard"

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "laneLaws under SHA-256 certify green too"
          <| fun _ ->
              let results =
                  Conformance.laneLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.sha256Hash 3110 60

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "a hash that answers one id reds the lane-shape guard rather than reading green"
          <| fun _ ->
              let oneId: HashFn = fun _ _ -> "one"

              let results =
                  Conformance.laneLaws ConformanceTests.sw ConformanceTests.streamGen oneId 4311 40

              let guard =
                  results
                  |> List.find (fun r -> r.Law.StartsWith(SampleAdequacy.lawPrefix "Conformance.laneLaws"))

              Expect.isFalse guard.Passed "the lane-shape guard is red" ]

// ---- Phase 416: a lane store written by a newer host ----

/// Two lanes written by the newer host (`OpStreamTests.Newer`): alpha holds counter ops only; beta
/// holds one `reset` this build's witness does not know, with a counter op on top of it. Answers the
/// store and the reset's node id.
let private newerStore () =
    let nw = OpStreamTests.Newer.witness

    let mutable store: Dag.Loaded<OpStreamTests.Newer.NewerOp> =
        { Dag = Dag.empty; LaneOf = Map.empty }

    let mutable tips: Map<string, string> = Map.empty

    let write lane op (parents: string list) =
        let id, s = Dag.appendOnLane h nw (Human lane) op parents lane store |> ok
        store <- s
        tips <- Map.add lane id tips
        id

    let g = write "alpha" (OpStreamTests.Newer.Known(Inc 1)) []
    write "beta" (OpStreamTests.Newer.Known(Inc 2)) [ g ] |> ignore
    let reset = write "beta" OpStreamTests.Newer.Reset [ tips["beta"] ]
    write "beta" (OpStreamTests.Newer.Known(Inc 3)) [ reset ] |> ignore
    write "alpha" (OpStreamTests.Newer.Known(Inc 4)) [ tips["alpha"] ] |> ignore
    store, reset

/// The 1-based line of `text` holding node `id`.
let private lineOfNode (text: string) (id: string) =
    1
    + (text.Split('\n')
       |> Array.findIndex (fun l -> l.Contains("\"id\":\"" + id + "\"")))

[<Tests>]
let newerStoreTests =
    testList
        "a lane store written by a newer host (Phase 416)"
        [ testCase "one op kind this witness does not know loads to Undecodable, naming that site and only it"
          <| fun _ ->
              let store, reset = newerStore ()
              let texts = Dag.lanesToJsonl OpStreamTests.Newer.witness.Encode store
              let beta = texts |> List.find (fun (l, _) -> l = "beta") |> snd

              let site =
                  { Lane = Some "beta"
                    Line = lineOfNode beta reset
                    NodeId = reset
                    Reason = OpStreamTests.Newer.refusal }

              Expect.equal (Dag.loadLanesVerified h sw texts) (Error(StreamLoadFault.Undecodable [ site ])) "verified"
              Expect.equal (Dag.loadLanes sw texts) (Error(StreamLoadFault.Undecodable [ site ])) "structural"

              Expect.equal
                  (Dag.loadLanesVerified h sw texts |> Result.mapError Dag.laneLoadFaultToString)
                  (Error(sprintf "lane beta: line %d: %s (position 0)" site.Line OpStreamTests.Newer.refusal))
                  "the site renders with its lane"

              // the newer host reads its own store, verified and attributed
              let back = Dag.loadLanesVerified h OpStreamTests.Newer.witness texts |> ok
              Expect.equal back.LaneOf store.LaneOf "the newer witness reads it"

          testCase "the same store with one byte flipped in a content id answers the chain break, not Undecodable"
          <| fun _ ->
              let store, reset = newerStore ()
              let texts = Dag.lanesToJsonl OpStreamTests.Newer.witness.Encode store

              let flipped =
                  reset.Substring(0, reset.Length - 1) + (if reset.EndsWith "0" then "1" else "0")

              let tampered =
                  texts
                  |> List.map (fun (lane, text) ->
                      lane, text.Replace("\"id\":\"" + reset + "\"", "\"id\":\"" + flipped + "\""))

              Expect.notEqual tampered texts "the probe flipped a byte"

              match Dag.loadLanesVerified h sw tampered with
              | Error(StreamLoadFault.Broken lb) -> Expect.equal lb.Lane "beta" "the break is in the lane holding it"
              | other -> failtestf "expected the chain break, got %A" other

              // a structural load cannot see the break, and still names the op it could not decode
              match Dag.loadLanes sw tampered with
              | Error(StreamLoadFault.Undecodable [ s ]) -> Expect.equal s.NodeId flipped "the stored id"
              | other -> failtestf "expected Undecodable from the structural load, got %A" other

          testCase "loadLanesVerified answers what loadLanes then verifyLanes answer on a decodable store"
          <| fun _ ->
              let loaded = fixtureStore ()
              let texts = Dag.lanesToJsonl sw.Encode loaded
              let verified = Dag.loadLanesVerified h sw texts |> ok
              Expect.equal verified.LaneOf loaded.LaneOf "the attribution"
              Expect.equal verified.Dag.Nodes loaded.Dag.Nodes "the nodes"

              let victim =
                  loaded.Dag.Nodes
                  |> Map.toList
                  |> List.find (fun (id, _) -> loaded.LaneOf[id] = "gamma")
                  |> fst

              let tampered =
                  texts
                  |> List.map (fun (lane, text) ->
                      lane, text.Replace("\"id\":\"" + victim + "\"", "\"id\":\"" + victim + "x\""))

              let viaVerify = Dag.loadLanes sw tampered |> ok |> Dag.verifyLanes h sw

              match Dag.loadLanesVerified h sw tampered, viaVerify with
              | Error(StreamLoadFault.Broken lb), Error b -> Expect.equal lb b "the same break"
              | other -> failtestf "expected both to break, got %A" other ]
