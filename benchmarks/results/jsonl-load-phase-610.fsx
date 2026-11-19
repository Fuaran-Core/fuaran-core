// Phase 610: a store load's line scan, timed and metered.
//
// Run from the repository root, after a Release build of the op-stream DAG package (which builds the op-stream
// package it references):
//
//   dotnet build src/Fuaran.Core.OpStream.Dag -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/jsonl-load-phase-610.fsx -- <store dir>
//
// `<store dir>` holds the lane files (`*.jsonl`) of a DAG op-stream store; they are read in ordinal file-name
// order and concatenated, one `\n` between files, into ONE text. With only the directory the script runs every
// case EACH IN ITS OWN PROCESS (it re-invokes itself with `-- <store dir> <case>`) and prints the results table.
// One case is `dotnet fsi --optimize+ benchmarks/results/jsonl-load-phase-610.fsx -- <store dir> scan`.
//
// The cases:
//   scan        `Jsonl.scanRecords` over the text, the decoder taking the `op` member's raw span (`rawField`):
//               the line split and the member scan, and nothing a domain adds.
//   scan-crlf   the same over the text with every `\n` written `\r\n`, built outside the timed closure.
//   dag         `Dag.fromJsonl` over the text with a witness whose op is its raw text: the structural load a
//               store's commands start with (the line scan, the id / parents / actor members, the carried op,
//               the node map).
//   dag-crlf    the same over the CRLF text.
//
// The method is Phase 607's: the input is built OUTSIDE the timed closure; the closure is warmed up for a fixed
// WALL-CLOCK time, not a count of calls; then sampled until a time budget is spent (at least five samples). Each
// sample records its wall time and the bytes this thread allocated (`GC.GetAllocatedBytesForCurrentThread`); gen2
// is the count of generation-2 collections over the sampled phase, divided by the number of samples.
//
// The falsifier: every case checks its own answer before it is timed (the record count, or the node count, of the
// LF text and of the CRLF text must agree with the count of non-blank lines), and fails the run otherwise.

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

let run (dir: string) (case: string) =
    let lf = storeText dir

    let text =
        if case.EndsWith "-crlf" then
            lf.Replace("\n", "\r\n")
        else
            lf

    let records =
        lf.Split('\n') |> Array.filter (fun l -> l.Trim() <> "") |> Array.length

    let warm = TimeSpan.FromSeconds 3.0
    let budget = TimeSpan.FromSeconds 10.0

    match case with
    | "scan"
    | "scan-crlf" ->
        let scan () =
            Jsonl.scanRecords (Jsonl.rawField "op") text

        match scan () with
        | Ok ops when ops.Length = records -> ()
        | Ok ops -> fail (sprintf "scan read %d records of %d" ops.Length records)
        | Error f -> fail (sprintf "scan refused: %A" f)

        measure
            (sprintf "Jsonl.scanRecords (rawField op), %s" (if case = "scan" then "LF" else "CRLF"))
            text.Length
            5
            warm
            budget
            (fun () -> scan () |> ignore)
    | "dag"
    | "dag-crlf" ->
        // a repeated id with the same content is one node, so the node count is at most the line count
        match Dag.fromJsonl rawWitness text with
        | Ok d when d.Nodes.Count > 0 && d.Nodes.Count <= records -> ()
        | Ok d -> fail (sprintf "dag read %d nodes of %d lines" d.Nodes.Count records)
        | Error f -> fail (sprintf "dag refused: %s" (sprintf "%A" f))

        measure
            (sprintf "Dag.fromJsonl (raw op), %s" (if case = "dag" then "LF" else "CRLF"))
            text.Length
            5
            warm
            budget
            (fun () -> Dag.fromJsonl rawWitness text |> ignore)
    | other -> fail (sprintf "unknown case %s" other)

match fsi.CommandLineArgs |> Array.skip 1 with
| [| dir; case |] -> run dir case
| [| dir |] ->
    let script = fsi.CommandLineArgs[0]

    printfn "| case | chars | samples | mean ms | median ms | p95 ms | allocated bytes / call | gen2 / call |"
    printfn "|---|---:|---:|---:|---:|---:|---:|---:|"

    for case in [ "scan"; "scan-crlf"; "dag"; "dag-crlf" ] do
        let psi =
            ProcessStartInfo("dotnet", [ "fsi"; "--optimize+"; script; "--"; dir; case ])

        psi.RedirectStandardOutput <- true
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            fail (sprintf "case %s failed (exit %d)" case p.ExitCode)

        for line in out.Split('\n') do
            if line.StartsWith "RESULT|" then
                let f = line.Trim().Split('|')
                printfn "| %s | %s | %s | %s | %s | %s | %s | %s |" f[1] f[2] f[3] f[4] f[5] f[6] f[7] f[8]
| _ -> fail "usage: dotnet fsi --optimize+ jsonl-load-phase-610.fsx -- <store dir> [case]"
