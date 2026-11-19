// Phase 614: the DAG reader's walks over a whole store, timed and metered.
//
// Run from the repository root, after a Release build of the op-stream DAG package:
//
//   dotnet build src/Fuaran.Core.OpStream.Dag -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/dag-walks-phase-614.fsx -- <store dir>
//
// `<store dir>` holds the lane files (`*.jsonl`) of a DAG op-stream store; they are read in ordinal file-name
// order and concatenated, one `\n` between files, into ONE text, and loaded by `Dag.fromJsonl` with a witness
// whose op is its raw text. With only the directory the script prints the input's shape and its fingerprints,
// then runs every case EACH IN ITS OWN PROCESS (it re-invokes itself with `-- <store dir> <case>`) and prints the
// results table. One case is `dotnet fsi --optimize+ benchmarks/results/dag-walks-phase-614.fsx -- <store dir> heads`.
//
// The cases (the DAG is loaded OUTSIDE the timed closure; so are the head and base the walks are asked about):
//   heads     `Dag.heads` over the whole DAG.
//   between   `Dag.between dag base head`: `head` is the head with the largest ancestor closure (ties by id), and
//             `base` the merge base of that head and the head with the next-largest closure (or `head`'s first
//             parent where the DAG has one head) - the delta a reconciler replays, which drains `head`'s closure.
//   replay    `Dag.tryReplayTo` to `head` with a witness whose state counts the ops applied: the drain of the
//             head's closure and one `Apply` per node, nothing a domain adds.
//   total     `Dag.totalOrderBy` at a constant key: the drain of the WHOLE DAG, the order a clone folds on load.
//
// The method is Phase 607's: the input is built OUTSIDE the timed closure; the closure is warmed up for a fixed
// WALL-CLOCK time, not a count of calls; then sampled until a time budget is spent (at least five samples). Each
// sample records its wall time and the bytes this thread allocated (`GC.GetAllocatedBytesForCurrentThread`); gen2
// is the count of generation-2 collections over the sampled phase, divided by the number of samples.
//
// The falsifier: `fingerprint` prints a SHA-256 over each walk's whole answer (the heads in order, the delta's ids
// in order, the replayed count, the total order), so two trees are compared on what they return before either is
// timed; a changed order changes a fingerprint. Every case also checks its own answer before it is timed.

#r "../../src/Fuaran.Core.OpStream.Dag/bin/Release/net10.0/Fuaran.Core.Text.dll"
#r "../../src/Fuaran.Core.OpStream.Dag/bin/Release/net10.0/Fuaran.Core.OpStream.dll"
#r "../../src/Fuaran.Core.OpStream.Dag/bin/Release/net10.0/Fuaran.Core.OpStream.Dag.dll"

open System
open System.Diagnostics
open System.IO
open Fuaran.Core

let measure (name: string) (size: int) (minSamples: int) (warm: TimeSpan) (budget: TimeSpan) (f: unit -> unit) =
    let sw = Stopwatch.StartNew()

    while sw.Elapsed < warm do
        f ()

    let times = ResizeArray<float>()
    let bytes = ResizeArray<int64>()
    let gen2Before = GC.CollectionCount 2
    let total = Stopwatch.StartNew()

    while times.Count < minSamples || (total.Elapsed < budget && times.Count < 5000) do
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

    printfn
        "RESULT|%s|%d|%d|%.4f|%.4f|%.4f|%.0f|%.3f"
        name
        size
        times.Count
        (Seq.average times)
        (pick 0.5)
        (pick 0.95)
        (bytes |> Seq.map float |> Seq.average)
        gen2

let fail msg =
    eprintfn "%s" msg
    exit 2

let storeText (dir: string) =
    Directory.GetFiles(dir, "*.jsonl")
    |> Array.sortWith (fun a b -> String.CompareOrdinal(a, b))
    |> Array.map File.ReadAllText
    |> String.concat "\n"

let rawWitness: StreamWitness<string, unit, unit> =
    { Apply = fun _ s -> Ok s
      Encode = id
      Decode = Ok }

let countWitness: StreamWitness<string, int, unit> =
    { Apply = fun _ s -> Ok(s + 1)
      Encode = id
      Decode = Ok }

let sha (parts: string seq) =
    use h = Security.Cryptography.SHA256.Create()

    h.ComputeHash(Text.Encoding.UTF8.GetBytes(String.concat "\n" parts))
    |> Array.map (fun b -> b.ToString "x2")
    |> String.concat ""

let load (dir: string) =
    match Dag.fromJsonl rawWitness (storeText dir) with
    | Ok d -> d
    | Error f -> fail (sprintf "load refused: %A" f)

/// The head with the largest closure and the base `between` is asked about (see the header).
let headAndBase (dag: Dag.T<string>) =
    let ranked =
        Dag.heads dag
        |> List.map (fun h -> h, Set.count (Dag.ancestorsOf dag h))
        |> List.sortBy (fun (h, n) -> -n, h)

    match ranked with
    | [] -> fail "the store has no heads"
    | [ (h, _) ] ->
        match dag.Nodes[h].Parents with
        | p :: _ -> h, p
        | [] -> h, h
    | (h, _) :: (other, _) :: _ ->
        match Dag.mergeBase dag h other with
        | Some b -> h, b
        | None -> h, h

let run (dir: string) (case: string) =
    let dag = load dir
    let head, baseId = headAndBase dag
    let n = dag.Nodes.Count
    let warm = TimeSpan.FromSeconds 3.0
    let budget = TimeSpan.FromSeconds 8.0

    match case with
    | "heads" ->
        if List.isEmpty (Dag.heads dag) then
            fail "no heads"

        measure "Dag.heads" n 5 warm budget (fun () -> Dag.heads dag |> ignore)
    | "between" ->
        if List.isEmpty (Dag.between dag baseId head) then
            fail "an empty delta"

        measure "Dag.between (merge base, largest head)" n 5 warm budget (fun () ->
            Dag.between dag baseId head |> ignore)
    | "replay" ->
        match Dag.tryReplayTo countWitness 0 dag head with
        | Ok c when c = Set.count (Dag.ancestorsOf dag head) -> ()
        | other -> fail (sprintf "replay answered %A" other)

        measure "Dag.tryReplayTo (largest head, counting witness)" n 5 warm budget (fun () ->
            Dag.tryReplayTo countWitness 0 dag head |> ignore)
    | "total" ->
        match Dag.totalOrderBy (fun _ -> 0) dag with
        | Ok order when order.Length = n -> ()
        | other -> fail (sprintf "total order answered %A" (Result.map List.length other))

        measure "Dag.totalOrderBy (constant key)" n 5 warm budget (fun () ->
            Dag.totalOrderBy (fun _ -> 0) dag |> ignore)
    | "fingerprint" ->
        let heads = Dag.heads dag

        printfn
            "nodes %d, heads %d, head %s (closure %d), base %s"
            n
            heads.Length
            head
            (Set.count (Dag.ancestorsOf dag head))
            baseId

        printfn "FINGERPRINT|heads|%s" (sha heads)

        printfn
            "FINGERPRINT|between|%d|%s"
            (Dag.between dag baseId head).Length
            (sha (Dag.between dag baseId head |> List.map _.Id))

        printfn "FINGERPRINT|replay|%A" (Dag.tryReplayTo countWitness 0 dag head)

        match Dag.totalOrderBy (fun _ -> 0) dag with
        | Ok order -> printfn "FINGERPRINT|total|%s" (sha order)
        | Error c -> printfn "FINGERPRINT|total|cyclic %d" c.Unplaced.Length
    | other -> fail (sprintf "unknown case %s" other)

match fsi.CommandLineArgs |> Array.skip 1 with
| [| dir; case |] -> run dir case
| [| dir |] ->
    let script = fsi.CommandLineArgs[0]

    let runCase (case: string) =
        let psi =
            ProcessStartInfo("dotnet", [ "fsi"; "--optimize+"; script; "--"; dir; case ])

        psi.RedirectStandardOutput <- true
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            fail (sprintf "case %s failed (exit %d)" case p.ExitCode)

        out.Split('\n') |> Array.map _.TrimEnd()

    for line in runCase "fingerprint" do
        if line <> "" then
            printfn "%s" line

    printfn ""
    printfn "| case | nodes | samples | mean ms | median ms | p95 ms | allocated bytes / call | gen2 / call |"
    printfn "|---|---:|---:|---:|---:|---:|---:|---:|"

    for case in [ "heads"; "between"; "replay"; "total" ] do
        for line in runCase case do
            if line.StartsWith "RESULT|" then
                let f = line.Split('|')
                printfn "| %s | %s | %s | %s | %s | %s | %s | %s |" f[1] f[2] f[3] f[4] f[5] f[6] f[7] f[8]
| _ -> fail "usage: dotnet fsi --optimize+ dag-walks-phase-614.fsx -- <store dir> [case]"
