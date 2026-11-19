// Phase 612: what `Column.aggregate` costs over a bool, a date and a timestamp column — `Max` and `Min`, which
// order the rows, and `CountDistinct`, which keys them — over 100,000 null-free rows.
//
// Run from the repository root, after a Release build of the column package:
//
//   dotnet build src/Fuaran.Core.Column -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/temporal-aggregate-phase-612.fsx
//
// With no arguments the script runs every case, EACH IN ITS OWN PROCESS (it re-invokes itself with
// `-- <case> <size>`), and prints the results table. One case is
// `dotnet fsi --optimize+ benchmarks/results/temporal-aggregate-phase-612.fsx -- max-ts-ms 100000`.
//
// The method is `tree-lookups.fsx`'s: the input is built OUTSIDE the timed closure; the closure is warmed up for a
// fixed WALL-CLOCK time (three seconds), not a count of calls; then sampled until a ten-second budget is spent (at
// least five samples, at most 2,000). Each sample records its wall time and the bytes this thread allocated
// (`GC.GetAllocatedBytesForCurrentThread`); gen2 is the count of generation-2 collections over the sampled phase,
// divided by the number of samples. One call aggregates the whole column once.
//
// The falsifier: every case checks its answer before it is timed, against an answer computed here from the stored
// values with no `Cell` (the greatest or least day, instant or bool rendered once, the count of distinct values), and
// fails the run on a difference.

#r "../../src/Fuaran.Core.Column/bin/Release/net10.0/Fuaran.Core.Text.dll"
#r "../../src/Fuaran.Core.Column/bin/Release/net10.0/Fuaran.Core.Wire.dll"
#r "../../src/Fuaran.Core.Column/bin/Release/net10.0/Fuaran.Core.Unit.dll"
#r "../../src/Fuaran.Core.Column/bin/Release/net10.0/Fuaran.Core.Column.dll"

open System
open System.Diagnostics
open Fuaran.Core

let measure (name: string) (size: int) (f: unit -> unit) =
    let warm = TimeSpan.FromSeconds 3.0
    let budget = TimeSpan.FromSeconds 10.0
    let sw = Stopwatch.StartNew()

    while sw.Elapsed < warm do
        f ()

    let times = ResizeArray<float>()
    let bytes = ResizeArray<int64>()
    let gen2Before = GC.CollectionCount 2
    let total = Stopwatch.StartNew()

    while times.Count < 5 || (total.Elapsed < budget && times.Count < 2000) do
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

    let meanBytes = bytes |> Seq.map float |> Seq.average

    printfn
        "RESULT|%s|%d|%d|%.4f|%.4f|%.4f|%.0f|%.1f|%.3f"
        name
        size
        times.Count
        (Seq.average times)
        (pick 0.5)
        (pick 0.95)
        meanBytes
        (meanBytes / float size)
        gen2

let fail msg =
    eprintfn "%s" msg
    exit 2

/// `n` day counts spread over 0001-01-01..9999-12-31, deterministic.
let days (n: int) =
    let lo = -719162
    let hi = 2932896
    [| for i in 0 .. n - 1 -> lo + int ((int64 i * 7919L) % int64 (hi - lo + 1)) |]

/// `n` millisecond instants: a whole epoch second anywhere in that range and a fraction, every tenth one zero.
let instants (n: int) =
    let ds = days n

    [| for i in 0 .. n - 1 ->
           let second = float ds.[i] * 86400.0 + float ((i * 7907) % 86400)
           let fraction = if i % 10 = 0 then 0 else int ((int64 i * 104729L) % 1000L)
           struct (second, fraction) |]

let timestampColumn (xs: struct (float * int)[]) =
    Column.ofTimestamps
        "t"
        TimeUnit.Milliseconds
        (Vector.ofArray [| for struct (s, _) in xs -> s |])
        (Some(Vector.ofArray [| for struct (_, f) in xs -> f |]))
        AllValid

let check (got: Result<Cell, AggregateError>) (expected: Cell) (what: string) =
    match got with
    | Ok c when c = expected -> ()
    | other -> fail (sprintf "%s answered %A, expected %A" what other expected)

let tsCase (fn: AggFn) (label: string) (size: int) =
    let xs = instants size
    let column = timestampColumn xs

    let expected =
        match fn with
        | CountDistinct -> Int(xs |> Array.distinct |> Array.length)
        | _ ->
            // Max keeps the last of equals and Min the first; equal instants render alike, so either is the text.
            let struct (s, f) = if fn = Max then Array.max xs else Array.min xs
            Timestamp(TemporalText.instantText TimeUnit.Milliseconds s f)

    check (Column.aggregate fn column) expected label
    measure label size (fun () -> Column.aggregate fn column |> ignore)

let dateCase (fn: AggFn) (label: string) (size: int) =
    let ds = days size
    let column = Column.ofDates "d" (Vector.ofArray ds) AllValid

    let expected =
        match fn with
        | CountDistinct -> Int(ds |> Array.distinct |> Array.length)
        | Max -> Date(TemporalText.dateText (Array.max ds))
        | _ -> Date(TemporalText.dateText (Array.min ds))

    check (Column.aggregate fn column) expected label
    measure label size (fun () -> Column.aggregate fn column |> ignore)

let boolCase (fn: AggFn) (label: string) (size: int) =
    // Mostly false, so `Max` meets its `true` late and keeps replacing it with the later ones.
    let bs = [| for i in 0 .. size - 1 -> i % 7 = 3 |]
    let column = Column.ofBools "b" (Vector.ofArray bs) AllValid

    let expected =
        match fn with
        | CountDistinct -> Int(bs |> Array.distinct |> Array.length)
        | Max -> Bool(Array.max bs)
        | _ -> Bool(Array.min bs)

    check (Column.aggregate fn column) expected label
    measure label size (fun () -> Column.aggregate fn column |> ignore)

let run (case: string) (size: int) =
    match case with
    | "max-ts-ms" -> tsCase Max "Max (timestamp_ms)" size
    | "min-ts-ms" -> tsCase Min "Min (timestamp_ms)" size
    | "distinct-ts-ms" -> tsCase CountDistinct "CountDistinct (timestamp_ms)" size
    | "max-date" -> dateCase Max "Max (date)" size
    | "distinct-date" -> dateCase CountDistinct "CountDistinct (date)" size
    | "max-bool" -> boolCase Max "Max (bool)" size
    | "distinct-bool" -> boolCase CountDistinct "CountDistinct (bool)" size
    | other -> fail (sprintf "unknown case %s" other)

let args = fsi.CommandLineArgs |> Array.skip 1

match args with
| [| case; size |] -> run case (int size)
| [||] ->
    let self = fsi.CommandLineArgs.[0]

    let cases =
        [ "max-ts-ms"
          "min-ts-ms"
          "distinct-ts-ms"
          "max-date"
          "distinct-date"
          "max-bool"
          "distinct-bool" ]
        |> List.map (fun c -> c, 100000)

    printfn
        "machine: %s, %d logical processors, %s"
        (Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER")
        Environment.ProcessorCount
        Runtime.InteropServices.RuntimeInformation.OSDescription

    printfn ".NET: %s" (Environment.Version.ToString())
    printfn ""

    printfn
        "| case | rows | samples | mean ms | median ms | p95 ms | allocated bytes / call | bytes / row | gen2 / call |"

    printfn "|---|---:|---:|---:|---:|---:|---:|---:|---:|"

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

                printfn
                    "| %s | %s | %s | %s | %s | %s | %s | %s | %s |"
                    f.[1]
                    f.[2]
                    f.[3]
                    f.[4]
                    f.[5]
                    f.[6]
                    f.[7]
                    f.[8]
                    f.[9]
| _ -> failwith "usage: temporal-aggregate-phase-612.fsx [-- <case> <size>]"
