// Phase 607: the tree lookups and the graft's duplicate check, timed and metered.
//
// Run from the repository root, after a Release build of the op-stream DAG package (which builds the tree and
// op packages it references):
//
//   dotnet build src/Fuaran.Core.OpStream.Dag -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/tree-lookups.fsx
//
// With no arguments the script runs every case at every size, EACH IN ITS OWN PROCESS (it re-invokes itself
// with `-- <case> <size>`), and prints the results table. One case at one size is
// `dotnet fsi --optimize+ benchmarks/results/tree-lookups.fsx -- insert 20000`.
//
// The method: the input (the tree, the op, the DAG) is built OUTSIDE the timed closure; the closure is warmed
// up for a fixed WALL-CLOCK time, not a count of calls; then sampled until a time budget is spent (at least
// five samples; three for the 8,800-op replay, whose every call takes minutes). Each sample records its wall
// time and the bytes this thread allocated (`GC.GetAllocatedBytesForCurrentThread`); gen2 is the count of
// generation-2 collections over the sampled phase, divided by the number of samples. The library code timed is the Release build the `#r` lines name.
//
// The falsifier: every case checks its own answer before it is timed (an insert that refused, a lookup that
// missed, a replay that did not reach the head's tree size would be timing nothing), and fails the run.

#r "../../src/Fuaran.Core.OpStream.Dag/bin/Release/net10.0/Fuaran.Core.Text.dll"
#r "../../src/Fuaran.Core.OpStream.Dag/bin/Release/net10.0/Fuaran.Core.Tree.dll"
#r "../../src/Fuaran.Core.OpStream.Dag/bin/Release/net10.0/Fuaran.Core.Ops.dll"
#r "../../src/Fuaran.Core.OpStream.Dag/bin/Release/net10.0/Fuaran.Core.OpStream.dll"
#r "../../src/Fuaran.Core.OpStream.Dag/bin/Release/net10.0/Fuaran.Core.OpStream.Dag.dll"

open System
open System.Diagnostics
open Fuaran.Core

type N =
    { Id: string
      Kind: string
      Value: string
      Children: N list }

let nodew: NodeWitness<N, string> =
    { Id = _.Id
      KindTag = _.Kind
      Children = _.Children
      ReplaceChildren = fun n cs -> { n with Children = cs } }

let idw: IdWitness<string> =
    { ToString = id
      OfString = id
      Equals = fun a b -> a = b }

/// A task holds nothing; every other kind is a container.
let canHold (n: N) = n.Kind <> "task"
let scope = OpScope.contained canHold nodew idw

let leaf id kind =
    { Id = id
      Kind = kind
      Value = ""
      Children = [] }

/// A roadmap-shaped tree of about `size` nodes: a root, epochs of 50 phases, each phase holding four tasks.
let tree (size: int) =
    let phases = max 1 (size / 5)

    let phase i =
        { leaf (sprintf "p%d" i) "phase" with
            Children = [ for t in 1..4 -> leaf (sprintf "p%d.t%d" i t) "task" ] }

    let epochs =
        [ for e in 0 .. (phases - 1) / 50 ->
              { leaf (sprintf "e%d" e) "epoch" with
                  Children = [ for i in e * 50 .. min (phases - 1) (e * 50 + 49) -> phase i ] } ]

    { leaf "root" "roadmap" with
        Children = epochs }

let measure (name: string) (size: int) (minSamples: int) (warm: TimeSpan) (budget: TimeSpan) (f: unit -> unit) =
    let sw = Stopwatch.StartNew()

    while sw.Elapsed < warm do
        f ()

    let times = ResizeArray<float>()
    let bytes = ResizeArray<int64>()
    let gen2Before = GC.CollectionCount 2
    let total = Stopwatch.StartNew()

    while times.Count < minSamples || (total.Elapsed < budget && times.Count < 2000) do
        let a0 = GC.GetAllocatedBytesForCurrentThread()
        let t0 = Stopwatch.GetTimestamp()
        f ()
        let t1 = Stopwatch.GetTimestamp()
        bytes.Add(GC.GetAllocatedBytesForCurrentThread() - a0)
        times.Add(float (t1 - t0) * 1000.0 / float Stopwatch.Frequency)

    let gen2 = float (GC.CollectionCount 2 - gen2Before) / float times.Count
    let sorted = times |> Seq.sort |> Array.ofSeq

    let pick (q: float) =
        sorted.[min (sorted.Length - 1) (int (ceil (q * float sorted.Length)) - 1)]

    let mean = Seq.average times

    printfn
        "RESULT|%s|%d|%d|%.4f|%.4f|%.4f|%.0f|%.3f"
        name
        size
        times.Count
        mean
        (pick 0.5)
        (pick 0.95)
        (bytes |> Seq.map float |> Seq.average)
        gen2

let fail msg =
    eprintfn "%s" msg
    exit 2

let run (case: string) (size: int) =
    let t = tree size
    let count = Tree.count nodew t
    let lastPhase = sprintf "p%d" (max 1 (size / 5) - 1)
    let midTask = sprintf "p%d.t2" (max 1 (size / 5) / 2)
    let warm = TimeSpan.FromSeconds 3.0
    let budget = TimeSpan.FromSeconds 10.0

    match case with
    | "insert" ->
        // an append of a new task under the LAST phase: the path an edit takes is O(depth)
        let op = InsertChild(lastPhase, leaf "fresh" "task")

        match Ops.apply Policy.none scope op t with
        | Ok after when Tree.count nodew after = count + 1 -> ()
        | other -> fail (sprintf "insert did not apply: %A" (other |> Result.map ignore))

        measure "Ops.apply (InsertChild ...)" count 5 warm budget (fun () -> Ops.apply Policy.none scope op t |> ignore)
    | "graft" ->
        let g = leaf "fresh" "task"

        if Tree.graftWellFormed nodew idw g t <> Tree.Structural then
            fail "graft check refused a clean graft"

        measure "Tree.graftWellFormed" count 5 warm budget (fun () -> Tree.graftWellFormed nodew idw g t |> ignore)
    | "tryFind" ->
        if (Tree.tryFind nodew idw midTask t).IsNone then
            fail "tryFind missed"

        measure "Tree.tryFind (mid-tree)" count 5 warm budget (fun () -> Tree.tryFind nodew idw midTask t |> ignore)
    | "parentOf" ->
        if (Tree.parentOf nodew idw midTask t).IsNone then
            fail "parentOf missed"

        measure "Tree.parentOf (mid-tree)" count 5 warm budget (fun () -> Tree.parentOf nodew idw midTask t |> ignore)
    | "replay" ->
        // a generated store of `size` ops replayed by `Dag.tryReplayTo`: two of every three ops insert a phase
        // (one task) under an epoch, the third rewrites an earlier phase's value. The tree grows to ~2/3 * size
        // phases of two nodes each.
        let encode (op: SkeletonOp<N, string>) =
            match op with
            | InsertChild(p, n) -> sprintf "ins|%s|%s" p n.Id
            | UpdateNode n -> sprintf "upd|%s|%s" n.Id n.Value
            | _ -> "other"

        let sw: StreamWitness<SkeletonOp<N, string>, N, Rejection<string>> =
            { Apply = fun op s -> Ops.apply Policy.none scope op s
              Encode = encode
              Decode = fun _ -> Error "not decoded" }

        let stream = OpStream.canonical OpStream.defaultHash sw

        let genesis =
            { leaf "root" "roadmap" with
                Children = [ for e in 0..59 -> leaf (sprintf "e%d" e) "epoch" ] }

        let ops =
            let acc = ResizeArray<SkeletonOp<N, string>>()
            let mutable inserted = 0

            for i in 0 .. size - 1 do
                if i % 3 = 2 && inserted > 0 then
                    acc.Add(
                        UpdateNode
                            { leaf (sprintf "q%d" (i % inserted)) "phase" with
                                Value = string i }
                    )
                else
                    acc.Add(
                        InsertChild(
                            sprintf "e%d" (inserted % 60),
                            { leaf (sprintf "q%d" inserted) "phase" with
                                Children = [ leaf (sprintf "q%d.t1" inserted) "task" ] }
                        )
                    )

                    inserted <- inserted + 1

            List.ofSeq acc

        let dag, head =
            ops
            |> List.fold
                (fun (d, parent) op ->
                    match Dag.append stream (Human "bench") op parent d with
                    | Ok w -> w.Dag, w.Id
                    | Error e -> fail (sprintf "append refused: %A" e))
                (Dag.empty, "")

        let expected =
            61
            + 2
              * (ops
                 |> List.filter (function
                     | InsertChild _ -> true
                     | _ -> false)
                 |> List.length)

        match Dag.tryReplayTo sw genesis dag head with
        | Ok s when Tree.count nodew s = expected -> ()
        | Ok s -> fail (sprintf "replay reached %d nodes, expected %d" (Tree.count nodew s) expected)
        | Error e -> fail (sprintf "replay refused: %A" e)

        measure
            (sprintf "Dag.tryReplayTo (%d ops)" size)
            expected
            // a replay of the larger store takes minutes: three samples after one warm-up call, and the p95 of
            // three is their maximum
            (if size > 2000 then 3 else 5)
            (TimeSpan.FromSeconds 1.0)
            (TimeSpan.FromSeconds 20.0)
            (fun () -> Dag.tryReplayTo sw genesis dag head |> ignore)
    | other -> fail (sprintf "unknown case %s" other)

let args = fsi.CommandLineArgs |> Array.skip 1

match args with
| [| case; size |] -> run case (int size)
| [||] ->
    let self = fsi.CommandLineArgs.[0]

    let cases =
        [ for s in [ 2000; 20000 ] do
              for c in [ "insert"; "graft"; "tryFind"; "parentOf" ] -> c, s
          yield "replay", 2000
          yield "replay", 8800 ]

    printfn
        "machine: %s, %d logical processors, %s"
        (Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER")
        Environment.ProcessorCount
        Runtime.InteropServices.RuntimeInformation.OSDescription

    printfn ".NET: %s" (Environment.Version.ToString())
    printfn ""
    printfn "| case | nodes | samples | mean ms | median ms | p95 ms | allocated bytes / call | gen2 / call |"
    printfn "|---|---:|---:|---:|---:|---:|---:|---:|"

    for c, s in cases do
        let psi =
            ProcessStartInfo("dotnet", sprintf "fsi --optimize+ \"%s\" -- %s %d" self c s)

        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            failwithf "case %s %d exited %d" c s p.ExitCode

        for line in out.Split('\n') do
            if line.StartsWith "RESULT|" then
                let f = line.Trim().Split('|')
                printfn "| %s | %s | %s | %s | %s | %s | %s | %s |" f.[1] f.[2] f.[3] f.[4] f.[5] f.[6] f.[7] f.[8]
| _ -> failwith "usage: tree-lookups.fsx [-- <case> <size>]"
