module Fuaran.Core.Tests.ReachTests

// Phase 289 — the reachability index on the lane DAG: `Dag.Reach`, the indexed extension, the
// index-taking overloads, `Conformance.reachLaws`, and the opt-in measurement leg its cost claim
// stands on.

open System
open Expecto
open Fuaran.Core
open Fuaran.Core.Tests.Reference.Counter

let private sw = witness
let private h = OpStream.defaultHash

let private nodeIds (ns: DagNode<'Op> list) = ns |> List.map (fun n -> n.Id)

/// genesis g; a and b fork off g; c extends a; m merges c and b.
let private forkMerge () =
    let g, d1 = Dag.append h sw (Human "x") (Inc 1) "" Dag.empty |> Reference.built
    let a, d2 = Dag.append h sw (Human "x") (Inc 2) g d1 |> Reference.built
    let b, d3 = Dag.append h sw (Human "x") (Inc 3) g d2 |> Reference.built
    let c, d4 = Dag.append h sw (Human "x") (Inc 4) a d3 |> Reference.built
    let m, d5 = Dag.merge h sw (Human "x") (Inc 5) c b d4 |> Reference.built
    g, a, b, c, m, d5

/// Every answer of `reach` against the unindexed functions on `dag`, over `ids` and their pairs.
let private agrees (dag: Dag.T<CounterOp>) (reach: Dag.Reach<CounterOp>) (ids: string list) =
    for x in ids do
        Expect.equal (Dag.Reach.ancestors reach x) (Dag.ancestorsOf dag x) ("ancestors " + x)
        Expect.equal (Dag.Reach.tryTopoOrder reach x) (Dag.tryTopoOrder dag x) ("order " + x)
        Expect.equal (Dag.tryReplayToWith sw 0 reach x) (Dag.tryReplayTo sw 0 dag x) ("replay " + x)

        for y in ids do
            Expect.equal
                (Dag.Reach.reaches reach x y)
                (Set.contains x (Dag.ancestorsOf dag y))
                (sprintf "reaches %s %s" x y)

            Expect.equal (Dag.Reach.mergeBase reach x y) (Dag.mergeBase dag x y) (sprintf "mergeBase %s %s" x y)

            Expect.equal
                (Dag.Reach.between reach x y |> nodeIds)
                (Dag.between dag x y |> nodeIds)
                (sprintf "between %s %s" x y)

// ---- the measurement leg (opt-in, not a gate) ----

/// The variable that asks for the measurement leg. Unset, the leg is skipped by name.
let measureVariable = "FUARAN_CORE_MEASURE_REACH"

module Measure =

    /// A lane store's shape at the size Phase 289 names: one genesis, 25 lanes forked off it, 200
    /// merge points (a lane's head merged with another lane's head, the result the first lane's new
    /// head), the rest appends onto a drawn lane — 5,000 nodes in all, built through
    /// `appendIndexed` / `mergeIndexed` under SHA-256, so no 32-bit collision skips a step.
    let build (nodes: int) (lanes: int) (merges: int) (seed: int) =
        let hash = OpStream.sha256Hash
        let rng = Random(seed)
        let actor (i: int) = Human("lane-" + string (i % lanes))
        let g, d0 = Dag.append hash sw (actor 0) (Inc 1) "" Dag.empty |> Reference.built
        let mutable reach = Dag.Reach.ofDag d0
        let heads = Array.create lanes g
        let mutable count = 1
        let mergeEvery = max 1 ((nodes - lanes) / (merges + 1))
        let mutable mergesDone = 0

        let take (r: Result<string * Dag.T<CounterOp> * Dag.Reach<CounterOp>, DagAppendFault>) =
            match r with
            | Ok(id, _, r') -> id, r'
            | Error f -> failwith ("measurement DAG refused a node: " + DagAppendFault.toString f)

        while count < nodes do
            let lane = if count <= lanes then count - 1 else rng.Next lanes

            if count > lanes && count % mergeEvery = 0 && mergesDone < merges then
                let other = (lane + 1 + rng.Next(lanes - 1)) % lanes

                let id, r' =
                    take (Dag.mergeIndexed hash sw (actor lane) (Inc count) heads.[lane] heads.[other] reach)

                heads.[lane] <- id
                reach <- r'
                mergesDone <- mergesDone + 1
            else
                let id, r' =
                    take (Dag.appendIndexed hash sw (actor lane) (Inc(1 + count % 7)) heads.[lane] reach)

                heads.[lane] <- id
                reach <- r'

            count <- count + 1

        reach, List.ofArray heads, mergesDone

    /// Time `n` calls of `f` over drawn arguments, stopping early when `budget` runs out; returns
    /// (calls made, mean microseconds per call).
    let time (budget: TimeSpan) (n: int) (args: 'a array) (f: 'a -> 'b) : int * float =
        let sw = Diagnostics.Stopwatch.StartNew()
        let mutable made = 0

        while made < n && sw.Elapsed < budget do
            f args.[made] |> ignore
            made <- made + 1

        made, sw.Elapsed.TotalMilliseconds * 1000.0 / float (max 1 made)

    /// The leg: every query kind asked 5,000 times through the index and up to 5,000 times through
    /// the unindexed function (each unindexed loop stops at its time budget, and the report says how
    /// many it made), plus the build, the extension loop and the replay / reconcile overloads.
    /// Each report line is handed to `report` as soon as it is measured. Every argument is drawn
    /// before any clock starts, so a loop times the question and nothing else.
    let run (budget: TimeSpan) (report: string -> unit) : unit =
        let queries = 5000
        let t0 = Diagnostics.Stopwatch.StartNew()
        let reachExt, laneHeads, mergesDone = build 5000 25 200 289
        let buildMs = t0.Elapsed.TotalMilliseconds
        let dag = Dag.Reach.dag reachExt
        let allocBefore = GC.GetAllocatedBytesForCurrentThread()
        let t1 = Diagnostics.Stopwatch.StartNew()
        let reach = Dag.Reach.ofDag dag
        let ofDagMs = t1.Elapsed.TotalMilliseconds
        let ofDagBytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore
        let ids = dag.Nodes |> Map.toArray |> Array.map fst
        let rng = Random(2890)

        let pairs =
            Array.init queries (fun _ -> ids.[rng.Next ids.Length], ids.[rng.Next ids.Length])

        let heads = List.toArray laneHeads

        let headPairs =
            Array.init queries (fun _ -> heads.[rng.Next heads.Length], heads.[rng.Next heads.Length])

        let row (name: string) (args: 'a array) (indexed: 'a -> 'b) (unindexed: 'a -> 'b) =
            // agreement on the first calls, so the numbers compare the same answers
            for i in 0..2 do
                if indexed args.[i] <> unindexed args.[i] then
                    failwithf "%s: the index and the unindexed function disagree at call %d" name i

            let ni, ti = time budget queries args indexed
            let nu, tu = time budget queries args unindexed

            sprintf "| %s | %d | %.1f | %d | %.1f | %.4f |" name ni ti nu tu (if tu > 0.0 then ti / tu else nan)

        // no op writes anything the other lane reads, so the fold reaches the lane replays and the
        // row times the region partition the index replaces, not the conflict enumeration
        let fp (_: CounterOp) : Footprint =
            { Reads = Set.empty
              StructureWrites = Set.empty
              ContentWrites = Set.empty
              UnknownParentWrites = Set.empty }

        // the base of each drawn head pair, taken from the index (the same answer, pinned by the laws)
        let reconcileArgs =
            headPairs
            |> Array.map (fun (a, b) -> (Dag.Reach.mergeBase reach a b |> Option.defaultValue a), [ a; b ])

        // the extension loop against a rebuild per append, over the last 100 appends' worth of DAG
        let extensionRow () =
            let k = 100
            let tail = laneHeads.Head

            let tExt =
                let s = Diagnostics.Stopwatch.StartNew()
                let mutable r = reach
                let mutable parent = tail

                for i in 1..k do
                    match Dag.appendIndexed h sw (Human "ext") (Inc i) parent r with
                    | Ok(id, _, r') ->
                        r <- r'
                        parent <- id
                    | Error _ -> ()

                s.Elapsed.TotalMilliseconds * 1000.0 / float k

            let tRebuild =
                let s = Diagnostics.Stopwatch.StartNew()
                let mutable d = dag
                let mutable parent = tail

                for i in 1..k do
                    match Dag.append h sw (Human "ext") (Inc i) parent d with
                    | Ok(id, d') ->
                        Dag.Reach.ofDag d' |> ignore
                        d <- d'
                        parent <- id
                    | Error _ -> ()

                s.Elapsed.TotalMilliseconds * 1000.0 / float k

            sprintf
                "| appendIndexed vs append + Reach.ofDag | %d | %.1f | %d | %.1f | %.4f |"
                k
                tExt
                k
                tRebuild
                (tExt / tRebuild)

        let n = dag.Nodes.Count
        let words = Seq.sum (seq { for s in 0 .. n - 1 -> s / 32 + 1 })

        report (
            sprintf
                "Reach measurement (Phase 289): %d nodes, %d lanes, %d merge points; %d queries per kind; unindexed loops budgeted %.0f s each"
                n
                (List.length laneHeads)
                mergesDone
                queries
                budget.TotalSeconds
        )

        report (
            sprintf
                "build through appendIndexed: %.0f ms; Reach.ofDag over the result: %.0f ms, %d bytes allocated; bitset words held: %d (%d bytes)"
                buildMs
                ofDagMs
                ofDagBytes
                words
                (4 * words)
        )

        report "| query | indexed calls | indexed us/call | unindexed calls | unindexed us/call | fraction |"
        report "|---|---|---|---|---|---|"

        report (
            row "reaches" pairs (fun (a, d) -> Dag.Reach.reaches reach a d) (fun (a, d) ->
                Set.contains a (Dag.ancestorsOf dag d))
        )

        report (row "ancestors" (Array.map fst pairs) (Dag.Reach.ancestors reach) (Dag.ancestorsOf dag))
        report (row "tryTopoOrder" (Array.map fst pairs) (Dag.Reach.tryTopoOrder reach) (Dag.tryTopoOrder dag))

        report (
            row "mergeBase (lane heads)" headPairs (fun (a, b) -> Dag.Reach.mergeBase reach a b) (fun (a, b) ->
                Dag.mergeBase dag a b)
        )

        report (
            row
                "between (base, lane head)"
                (Array.init queries (fun i -> fst pairs.[i], snd headPairs.[i]))
                (fun (b, x) -> Dag.Reach.between reach b x |> nodeIds)
                (fun (b, x) -> Dag.between dag b x |> nodeIds)
        )

        report (
            row
                "tryReplayToWith vs tryReplayTo (lane head)"
                (Array.map snd headPairs)
                (Dag.tryReplayToWith sw 0 reach)
                (Dag.tryReplayTo sw 0 dag)
        )

        report (
            row
                "reconcileManyWith vs reconcileMany (two lane heads over their merge base)"
                reconcileArgs
                (fun (b, hs) ->
                    Dag.reconcileManyWith sw fp reach b 0 hs
                    |> Result.map (List.map sw.Encode)
                    |> Result.mapError (sprintf "%A"))
                (fun (b, hs) ->
                    Dag.reconcileMany sw fp dag b 0 hs
                    |> Result.map (List.map sw.Encode)
                    |> Result.mapError (sprintf "%A"))
        )

        report (extensionRow ())

[<Tests>]
let tests =
    testList
        "Dag.Reach (Phase 289)"
        [ testCase "on a fork and a merge every indexed answer equals the unindexed one"
          <| fun _ ->
              let g, a, b, c, m, dag = forkMerge ()
              let reach = Dag.Reach.ofDag dag
              agrees dag reach [ g; a; b; c; m; "absent" ]
              Expect.equal (Dag.Reach.mergeBase reach c b) (Some g) "the fork point is the merge base"
              Expect.equal (Dag.Reach.between reach g m |> nodeIds |> Set.ofList) (set [ a; b; c; m ]) "the delta"
              Expect.isTrue (Dag.Reach.reaches reach m m) "a node reaches itself"
              Expect.isFalse (Dag.Reach.reaches reach "absent" m) "nothing absent reaches"
              Expect.isTrue (Dag.Reach.dag reach = dag) "the index carries the DAG it was built for"

          testCase "the empty DAG indexes and answers as the unindexed functions"
          <| fun _ ->
              let reach = Dag.Reach.ofDag Dag.empty
              agrees Dag.empty reach [ "x"; "" ]

              Expect.equal
                  (Dag.tryReplayToWith sw 0 reach "x")
                  (Error(Dag.ReplayFault.UnknownHead "x"))
                  "an unknown head is refused"

          testCase "appendIndexed and mergeIndexed extend to the index Reach.ofDag builds"
          <| fun _ ->
              let reach = ref (Dag.Reach.ofDag Dag.empty)
              let ids = ref []

              let step
                  (f: Dag.Reach<CounterOp> -> Result<string * Dag.T<CounterOp> * Dag.Reach<CounterOp>, DagAppendFault>)
                  =
                  match f reach.Value with
                  | Ok(id, d, r') ->
                      Expect.isTrue (Dag.Reach.dag r' = d) "the extended index carries the new DAG"
                      reach.Value <- r'
                      ids.Value <- ids.Value @ [ id ]
                      id
                  | Error f -> failwith (DagAppendFault.toString f)

              // ids drawn from FNV over these ops land in no particular order, so a new leaf enters
              // the drain both before and after older lanes
              let g = step (Dag.appendIndexed h sw (Human "x") (Inc 1) "")
              let a = step (Dag.appendIndexed h sw (Human "x") (Inc 2) g)
              let b = step (Dag.appendIndexed h sw (Human "x") (Inc 3) g)
              let c = step (Dag.appendIndexed h sw (Human "y") (Inc 4) g)
              let r2 = step (Dag.appendIndexed h sw (Human "z") (Inc 9) "")
              let m = step (Dag.mergeIndexed h sw (Human "x") (Inc 5) a b)
              let n = step (Dag.mergeIndexed h sw (Human "x") (Inc 6) m c)
              let _ = step (Dag.mergeIndexed h sw (Human "x") (Inc 7) n r2)
              let _ = step (Dag.appendIndexed h sw (Human "x") (Inc 8) a)
              let final = reach.Value
              let dag = Dag.Reach.dag final
              let ids' = ids.Value @ [ "absent" ]
              agrees dag final ids'
              agrees dag (Dag.Reach.ofDag dag) ids'

              for x in ids' do
                  Expect.equal
                      (Dag.Reach.tryTopoOrder final x)
                      (Dag.Reach.tryTopoOrder (Dag.Reach.ofDag dag) x)
                      "extended order = rebuilt order"

          testCase "an append that deduplicates leaves the index answering as before"
          <| fun _ ->
              let g, a, _, _, _, dag = forkMerge ()
              let reach = Dag.Reach.ofDag dag

              match Dag.appendIndexed h sw (Human "x") (Inc 2) g reach with
              | Ok(id, dag', reach') ->
                  Expect.equal id a "the same node"
                  Expect.isTrue (dag' = dag) "the DAG is unchanged"
                  agrees dag reach' (Map.toList dag.Nodes |> List.map fst)
              | Error f -> failwith (DagAppendFault.toString f)

          testCase "appendIndexed refuses exactly what append refuses"
          <| fun _ ->
              let _, _, _, _, m, dag = forkMerge ()
              let reach = Dag.Reach.ofDag dag

              let refused =
                  match Dag.appendIndexed h sw (Human "x") (Inc 1) "nope" reach with
                  | Error f -> Some f
                  | Ok _ -> None

              Expect.equal refused (Some(DagAppendFault.UnknownParent "nope")) "an unknown parent"

              let refusedMerge =
                  match Dag.mergeIndexed h sw (Human "x") (Inc 1) "" m reach with
                  | Error f -> Some f
                  | Ok _ -> None

              Expect.equal refusedMerge (Some DagAppendFault.EmptyParentId) "an empty merge parent"

          testCase "a cyclic load and a dangling parent are answered as the unindexed functions answer"
          <| fun _ ->
              let g, a, _, _, m, dag = forkMerge ()
              let template = dag.Nodes.[g]

              let forged: Dag.T<CounterOp> =
                  { Nodes =
                      [ "cx", [ "cy"; a ]
                        "cy", [ "cx" ]
                        "cz", [ "cx" ]
                        "cw", [ m ]
                        "cd", [ "missing" ] ]
                      |> List.fold
                          (fun nodes (id, ps) -> Map.add id { template with Id = id; Parents = ps } nodes)
                          dag.Nodes }

              let reach = Dag.Reach.ofDag forged
              let ids = (Map.toList forged.Nodes |> List.map fst) @ [ "missing"; "absent" ]
              agrees forged reach ids

              Expect.isError (Dag.Reach.tryTopoOrder reach "cz") "a cyclic history is still an error"

              Expect.equal
                  (Dag.tryReplayToWith sw 0 reach "cz")
                  (Error(Dag.ReplayFault.CyclicHistory "cz"))
                  "and still refused by the replay"

              // a node minted under the dangling id makes "cd" a non-root: the index rebuilds
              let minted: HashFn = fun _ _ -> "missing"

              match Dag.appendIndexed minted sw (Human "x") (Inc 3) m reach with
              | Ok(_, dag', reach') -> agrees dag' reach' (ids @ [ "cd" ])
              | Error f -> failwith (DagAppendFault.toString f)

              match Dag.appendIndexed h sw (Human "x") (Inc 3) "cz" reach with
              | Ok(id, dag', reach') -> agrees dag' reach' (ids @ [ id ])
              | Error f -> failwith (DagAppendFault.toString f)

          testCase "reconcileManyWith answers as reconcileMany over every base and head set of a lane DAG"
          <| fun _ ->
              let g, a, b, c, m, dag = forkMerge ()
              let reach = Dag.Reach.ofDag dag
              let ids = [ g; a; b; c; m; "absent" ]

              let fp (op: CounterOp) : Footprint =
                  { Reads = Set.empty
                    StructureWrites = Set.empty
                    ContentWrites = Set.singleton (sw.Encode op)
                    UnknownParentWrites = Set.empty }

              for baseId in ids do
                  for x in ids do
                      for y in ids do
                          for hs in [ []; [ x ]; [ x; y ]; [ x; y; x ] ] do
                              Expect.equal
                                  (Dag.reconcileManyWith sw fp reach baseId 0 hs)
                                  (Dag.reconcileMany sw fp dag baseId 0 hs)
                                  (sprintf "base %s heads %A" baseId hs)

          testCase "reachLaws certify the reference stream witness green"
          <| fun _ ->
              let results =
                  Conformance.reachLaws ConformanceTests.sw ConformanceTests.streamGen h 289 100

              Expect.equal (List.length results) 10 "nine laws and the DAG-shape guard"

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "reachLaws under SHA-256 certify green too"
          <| fun _ ->
              let results =
                  Conformance.reachLaws ConformanceTests.sw ConformanceTests.streamGen OpStream.sha256Hash 2890 60

              for r in results do
                  Expect.isTrue r.Passed (sprintf "%s: %A" r.Law r.Counterexample)

          testCase "a sample without merges reads the merge-base and delta laws as never tested"
          <| fun _ ->
              // The Phase 245 guard. A hash that answers one id for everything refuses every step
              // after the first as a content-id collision, so each sample is one node and no two lanes
              // ever merge. The merge-base and delta laws hold there trivially; the guard reds instead
              // of letting that read as a pass.
              let oneId: HashFn = fun _ _ -> "one"

              let results =
                  Conformance.reachLaws ConformanceTests.sw ConformanceTests.streamGen oneId 4242 50

              for r in results do
                  if not (r.Law.StartsWith(SampleAdequacy.lawPrefix "Conformance.reachLaws")) then
                      Expect.isTrue r.Passed (sprintf "no law is refuted, only untested: %s %A" r.Law r.Counterexample)

              let guard =
                  results
                  |> List.find (fun r -> r.Law.StartsWith(SampleAdequacy.lawPrefix "Conformance.reachLaws"))

              Expect.isFalse guard.Passed "the DAG-shape guard is red"

              Expect.stringContains
                  (defaultArg guard.Counterexample "")
                  "never reached lane merge"
                  "naming the shape the sample never held"

              let klass =
                  Families.census
                  |> List.find (fun (id, _) -> id = "Conformance.reachLaws")
                  |> snd

              let measured = SampleAdequacy.cases "Conformance.reachLaws" klass 50 results
              Expect.isTrue (SampleAdequacy.isVacuous measured) "the census cell reads vacuous"

          testCase "the reach measurement (opt-in: FUARAN_CORE_MEASURE_REACH; not a gate)"
          <| fun _ ->
              match Environment.GetEnvironmentVariable measureVariable with
              | null
              | "" -> skiptest (measureVariable + " is not set; the measurement leg runs only when asked for")
              | v ->
                  let seconds =
                      match Int32.TryParse v with
                      | true, s when s > 1 -> s
                      | _ -> 30

                  Measure.run (TimeSpan.FromSeconds(float seconds)) (fun line ->
                      printfn "%s" line
                      Console.Out.Flush()) ]
