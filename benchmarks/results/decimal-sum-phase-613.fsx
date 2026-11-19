// Phase 613: what exact-decimal arithmetic costs — `DecimalText.add` on its own, and `Column.aggregate Sum`, `Mean`
// and `Max` over a 100,000-row decimal column, which admit every row and then add it to the running total (`Sum`),
// read it as a float (`Mean`) or compare it with the greatest so far (`Max`).
//
// Run from the repository root, after a Release build of the column package:
//
//   dotnet build src/Fuaran.Core.Column -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/decimal-sum-phase-613.fsx
//
// With no arguments the script runs every case, EACH IN ITS OWN PROCESS (it re-invokes itself with
// `-- <case> <size>`), and prints the results table. One case is
// `dotnet fsi --optimize+ benchmarks/results/decimal-sum-phase-613.fsx -- sum 100000`.
//
// The method is `tree-lookups.fsx`'s: the input is built OUTSIDE the timed closure; the closure is warmed up for a
// fixed WALL-CLOCK time (three seconds), not a count of calls; then sampled until a ten-second budget is spent (at
// least five samples, at most 2,000). Each sample records its wall time and the bytes this thread allocated
// (`GC.GetAllocatedBytesForCurrentThread`); gen2 is the count of generation-2 collections over the sampled phase,
// divided by the number of samples. One `Sum`, `Mean` or `Max` call aggregates the whole column once; one `add` sample is
// `size` calls over a fixed array of pairs, so its bytes per row are bytes per call.
//
// The falsifier: every case checks its answer before it is timed, against one computed here in exact integer
// arithmetic (thousandths in an `int64`, or a `bigint` for the long texts) and rendered canonically by this script,
// and fails the run on a difference.

#r "../../src/Fuaran.Core.Column/bin/Release/net10.0/Fuaran.Core.Text.dll"
#r "../../src/Fuaran.Core.Column/bin/Release/net10.0/Fuaran.Core.Wire.dll"
#r "../../src/Fuaran.Core.Column/bin/Release/net10.0/Fuaran.Core.Unit.dll"
#r "../../src/Fuaran.Core.Column/bin/Release/net10.0/Fuaran.Core.Column.dll"

open System
open System.Diagnostics
open System.Numerics
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

/// The canonical text of `units / 10^scale`: no leading or trailing zero, no point over a zero fraction, no sign on
/// zero. Written here from `bigint`, apart from the code under measurement.
let canonical (units: bigint) (scale: int) : string =
    let negative = units.Sign < 0
    let digits = BigInteger.Abs(units).ToString().PadLeft(scale + 1, '0')
    let ip = digits.Substring(0, digits.Length - scale).TrimStart '0'
    let fp = digits.Substring(digits.Length - scale).TrimEnd '0'
    let isZero = ip.Length = 0 && fp.Length = 0

    (if negative && not isZero then "-" else "")
    + (if ip.Length = 0 then "0" else ip)
    + (if fp.Length = 0 then "" else "." + fp)

/// `n` thousandths between -1,000,000 and 1,000,000 (so `-1000` to `1000`), deterministic, mixed scales and signs.
let thousandths (n: int) =
    [| for i in 0 .. n - 1 -> ((int64 i * 7919L) % 2000003L) - 1000001L |]

let decimalColumn (units: int64[]) =
    Column.ofDecimals "d" (Vector.ofArray [| for u in units -> canonical (bigint u) 3 |]) AllValid

let sumCase (size: int) =
    let units = thousandths size
    let column = decimalColumn units
    let expected = Decimal(canonical (units |> Array.sumBy bigint) 3)

    match Column.aggregate Sum column with
    | Ok c when c = expected -> ()
    | other -> fail (sprintf "Sum answered %A, expected %A" other expected)

    measure "Sum (decimal)" size (fun () -> Column.aggregate Sum column |> ignore)

let meanCase (size: int) =
    let units = thousandths size
    let column = decimalColumn units
    let expected = float (Array.sum units) / 1000.0 / float size

    match Column.aggregate Mean column with
    | Ok(Float f) when abs (f - expected) <= 1e-9 * max 1.0 (abs expected) -> ()
    | other -> fail (sprintf "Mean answered %A, expected about %g" other expected)

    measure "Mean (decimal)" size (fun () -> Column.aggregate Mean column |> ignore)

let maxCase (size: int) =
    let units = thousandths size
    let column = decimalColumn units
    let expected = Decimal(canonical (bigint (Array.max units)) 3)

    match Column.aggregate Max column with
    | Ok c when c = expected -> ()
    | other -> fail (sprintf "Max answered %A, expected %A" other expected)

    measure "Max (decimal)" size (fun () -> Column.aggregate Max column |> ignore)

/// `size` `add` calls over 64 fixed pairs; `long` draws 40-digit texts at scale 12, else short ones at scale 3.
let addCase (long: bool) (size: int) =
    let scale = if long then 12 else 3

    let draw (i: int) =
        if long then
            (bigint (i + 1) * BigInteger.Pow(10I, 30) + bigint (i * 7919))
            * (if i % 3 = 0 then -1I else 1I)
        else
            bigint (((int64 i * 104729L) % 2000003L) - 1000001L)

    let pairs = [| for i in 0..63 -> draw i, draw (i + 64) |]

    let texts = pairs |> Array.map (fun (a, b) -> canonical a scale, canonical b scale)

    for (a, b), (ta, tb) in Array.zip pairs texts do
        let expected = canonical (a + b) scale

        match DecimalText.add ta tb with
        | Some s when s = expected -> ()
        | other -> fail (sprintf "add %s %s answered %A, expected %s" ta tb other expected)

    let label = if long then "add (40 digits)" else "add (short)"

    measure label size (fun () ->
        for i in 0 .. size - 1 do
            let ta, tb = texts.[i &&& 63]
            DecimalText.add ta tb |> ignore)

let run (case: string) (size: int) =
    match case with
    | "sum" -> sumCase size
    | "mean" -> meanCase size
    | "max" -> maxCase size
    | "add-short" -> addCase false size
    | "add-long" -> addCase true size
    | other -> fail (sprintf "unknown case %s" other)

let args = fsi.CommandLineArgs |> Array.skip 1

match args with
| [| case; size |] -> run case (int size)
| [||] ->
    let self = fsi.CommandLineArgs.[0]

    let cases =
        [ "add-short", 1000
          "add-long", 1000
          "sum", 100000
          "mean", 100000
          "max", 100000 ]

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
| _ -> failwith "usage: decimal-sum-phase-613.fsx [-- <case> <size>]"
