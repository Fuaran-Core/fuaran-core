// Phase 611: what rendering a date or an instant to its canonical text allocates — `TemporalText.dateText` and
// `TemporalText.instantText` called directly, the per-row cell read of a date and a timestamp column that calls them,
// and (optionally) a compute-layer group-by over a date key.
//
// Run from the repository root, after a Release build of the column package:
//
//   dotnet build src/Fuaran.Core.Column -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/temporal-text-phase-611.fsx
//
// With no arguments the script runs every case, EACH IN ITS OWN PROCESS (it re-invokes itself with
// `-- <case> <size>`), and prints the results table. One case is
// `dotnet fsi --optimize+ benchmarks/results/temporal-text-phase-611.fsx -- date-text 100000`.
//
// The group-by case needs a built `Fuaran.Compute.DataFrame.dll`; name it in the environment variable
// `FUARAN_COMPUTE_DATAFRAME_DLL`. Without it the case is skipped. The compute assembly is loaded by path and driven
// by reflection, so this script compiles without it, and it runs against the column package built in this tree.
//
// The method is `tree-lookups.fsx`'s: the input is built OUTSIDE the timed closure; the closure is warmed up for a
// fixed WALL-CLOCK time (three seconds), not a count of calls; then sampled until a ten-second budget is spent (at
// least five samples, at most 2,000). Each sample records its wall time and the bytes this thread allocated
// (`GC.GetAllocatedBytesForCurrentThread`); gen2 is the count of generation-2 collections over the sampled phase,
// divided by the number of samples. One call renders or reads every row of the input once.
//
// The falsifier: every case checks its answer before it is timed (the rendered texts against an independent
// `DateTime` formatting, the cell read against the render, the group-by's row count), and fails the run.

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

let epoch = DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)

/// `n` day counts spread over 0001-01-01..9999-12-31 (DateTime's range), deterministic.
let days (n: int) =
    let lo = int (DateTime(1, 1, 1) - epoch).TotalDays
    let hi = int (DateTime(9999, 12, 31) - epoch).TotalDays
    [| for i in 0 .. n - 1 -> lo + int ((int64 i * 7919L) % int64 (hi - lo + 1)) |]

let expectDate (d: int) =
    epoch.AddDays(float d).ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)

/// `n` instants in `unit`: a whole epoch second and a fraction in `[0, scale)`, every tenth fraction zero.
let instants (unit: TimeUnit) (n: int) =
    let ds = days n
    let scale = TimeUnit.scale unit

    [| for i in 0 .. n - 1 ->
           let second = float ds.[i] * 86400.0 + float ((i * 7907) % 86400)
           let fraction = if i % 10 = 0 then 0 else int ((int64 i * 104729L) % int64 scale)
           struct (second, fraction) |]

let expectInstant (unit: TimeUnit) (struct (second: float, fraction: int)) =
    let t = epoch.AddSeconds second

    let frac =
        if fraction = 0 then
            ""
        else
            "." + fraction.ToString().PadLeft(TimeUnit.digits unit, '0').TrimEnd('0')

    t.ToString("yyyy-MM-ddTHH:mm:ss", Globalization.CultureInfo.InvariantCulture) + frac + "Z"

let dateTextCase (size: int) =
    let ds = days size

    for d in ds do
        if TemporalText.dateText d <> expectDate d then
            fail (sprintf "dateText %d = %s, expected %s" d (TemporalText.dateText d) (expectDate d))

    measure "dateText" size (fun () ->
        for d in ds do
            TemporalText.dateText d |> ignore)

let instantTextCase (label: string) (unit: TimeUnit) (size: int) =
    let xs = instants unit size

    for struct (s, f) as x in xs do
        let got = TemporalText.instantText unit s f

        if got <> expectInstant unit x then
            fail (sprintf "instantText %A = %s, expected %s" x got (expectInstant unit x))

    measure (sprintf "instantText (%s)" label) size (fun () ->
        for struct (s, f) in xs do
            TemporalText.instantText unit s f |> ignore)

let cellDateCase (size: int) =
    let ds = days size
    let column = Column.ofDates "d" (Vector.ofArray ds) AllValid

    for i in 0 .. size - 1 do
        if Column.cell i column <> Date(expectDate ds.[i]) then
            fail (sprintf "cell %d of the date column" i)

    measure "Column.cell (date)" size (fun () ->
        for i in 0 .. size - 1 do
            Column.cell i column |> ignore)

let cellTimestampCase (size: int) =
    let unit = TimeUnit.Milliseconds
    let xs = instants unit size

    let column =
        Column.ofTimestamps
            "t"
            unit
            (Vector.ofArray [| for struct (s, _) in xs -> s |])
            (Some(Vector.ofArray [| for struct (_, f) in xs -> f |]))
            AllValid

    for i in 0 .. size - 1 do
        if Column.cell i column <> Timestamp(expectInstant unit xs.[i]) then
            fail (sprintf "cell %d of the timestamp column" i)

    measure "Column.cell (timestamp_ms)" size (fun () ->
        for i in 0 .. size - 1 do
            Column.cell i column |> ignore)

/// A group-by over a 100,000-row date key of 1,000 distinct days, counting a float column, through the compute
/// layer's `evalPipeline`, loaded by path.
let groupByCase (size: int) =
    match Environment.GetEnvironmentVariable "FUARAN_COMPUTE_DATAFRAME_DLL" with
    | null
    | "" -> printfn "SKIP|group-by needs FUARAN_COMPUTE_DATAFRAME_DLL"
    | path ->
        let asm = Reflection.Assembly.LoadFrom path
        let transformT = asm.GetType "Fuaran.Compute.Transform"
        let aggT = asm.GetType "Fuaran.Compute.Agg"
        let dataFrame = asm.GetType "Fuaran.Compute.DataFrame"
        let evalPipeline = dataFrame.GetMethod "evalPipeline"

        let agg =
            Reflection.FSharpValue.MakeRecord(aggT, [| box "n"; box Count; box "v" |])

        let aggs =
            let listT = typedefof<list<_>>.MakeGenericType aggT
            let cons = listT.GetMethod "Cons"
            let empty = listT.GetProperty("Empty").GetValue null
            cons.Invoke(null, [| agg; empty |])

        let groupBy =
            let case =
                Reflection.FSharpType.GetUnionCases transformT
                |> Array.find (fun c -> c.Name = "GroupBy")

            Reflection.FSharpValue.MakeUnion(case, [| box [ "d" ]; aggs |])

        let pipeline =
            let listT = typedefof<list<_>>.MakeGenericType transformT
            let cons = listT.GetMethod "Cons"
            let empty = listT.GetProperty("Empty").GetValue null
            cons.Invoke(null, [| groupBy; empty |])

        let ds = days 1000

        let table =
            { Schema = [ Field.create "d" DateType; Field.create "v" FloatType ]
              Columns =
                [ Column.ofDates "d" (Vector.ofArray [| for i in 0 .. size - 1 -> ds.[i % 1000] |]) AllValid
                  Column.ofFloats "v" (Vector.ofArray [| for i in 0 .. size - 1 -> float i |]) AllValid ] }

        let run () = evalPipeline.Invoke(null, [| pipeline; box table |])

        let r = run ()

        let ok =
            let case, fields = Reflection.FSharpValue.GetUnionFields(r, r.GetType())

            case.Name = "Ok"
            && (match fields.[0] with
                | :? Table as t -> t.Columns |> List.forall (fun c -> Column.length c = 1000)
                | _ -> false)

        if not ok then
            fail (sprintf "group-by answered %A" r)

        measure "compute group-by on a date key (1,000 days)" size (fun () -> run () |> ignore)

let run (case: string) (size: int) =
    match case with
    | "date-text" -> dateTextCase size
    | "instant-s" -> instantTextCase "seconds" TimeUnit.Seconds size
    | "instant-ms" -> instantTextCase "milliseconds" TimeUnit.Milliseconds size
    | "instant-ns" -> instantTextCase "nanoseconds" TimeUnit.Nanoseconds size
    | "cell-date" -> cellDateCase size
    | "cell-ts" -> cellTimestampCase size
    | "group-by-date" -> groupByCase size
    | other -> fail (sprintf "unknown case %s" other)

let args = fsi.CommandLineArgs |> Array.skip 1

match args with
| [| case; size |] -> run case (int size)
| [||] ->
    let self = fsi.CommandLineArgs.[0]

    let cases =
        [ "date-text"; "instant-s"; "instant-ms"; "instant-ns"; "cell-date"; "cell-ts"; "group-by-date" ]
        |> List.map (fun c -> c, 100000)

    printfn
        "machine: %s, %d logical processors, %s"
        (Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER")
        Environment.ProcessorCount
        Runtime.InteropServices.RuntimeInformation.OSDescription

    printfn ".NET: %s" (Environment.Version.ToString())
    printfn ""
    printfn "| case | rows | samples | mean ms | median ms | p95 ms | allocated bytes / call | bytes / row | gen2 / call |"
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
            elif line.StartsWith "SKIP|" then
                printfn "| %s | skipped |  |  |  |  |  |  |  |" c
| _ -> failwith "usage: temporal-text-phase-611.fsx [-- <case> <size>]"
