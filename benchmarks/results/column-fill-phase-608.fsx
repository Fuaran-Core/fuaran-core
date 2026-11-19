// Phase 608: what a column build allocates — the codec's decode of a null-free table, and the cell-list build plus
// aggregate the compute layer's deferred aggregates run once per group (its `aggCells`).
//
// Run from the repository root, after a Release build of the column package:
//
//   dotnet build src/Fuaran.Core.Column -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/column-fill-phase-608.fsx
//
// With no arguments the script runs every case, EACH IN ITS OWN PROCESS (it re-invokes itself with
// `-- <case> <size>`), and prints the results table. One case is
// `dotnet fsi --optimize+ benchmarks/results/column-fill-phase-608.fsx -- decode-float 100000`.
//
// The method is `tree-lookups.fsx`'s: the input (the encoded table, the cell lists) is built OUTSIDE the timed
// closure; the closure is warmed up for a fixed WALL-CLOCK time (three seconds), not a count of calls; then sampled
// until a ten-second budget is spent (at least five samples, at most 2,000). Each sample records its wall time and
// the bytes this thread allocated (`GC.GetAllocatedBytesForCurrentThread`); gen2 is the count of generation-2
// collections over the sampled phase, divided by the number of samples.
//
// The falsifier: every case checks its own answer before it is timed (a decode that refused or answered a column
// holding a mask, an aggregate that answered another sum), and fails the run.
//
// `aggCells` is the compute layer's adapter, not this repository's: it is `Column.ofCells` then `Column.aggregate`
// over one group's cells, so the aggregate cases run exactly that pair once per group.

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

/// A one-column embedded table of `n` rows, null-free, encoded on the wire.
let encoded (column: Column) (ty: ColumnType) =
    ColumnCodec.encode (
        Embedded
            { Schema = [ Field.create column.Name ty ]
              Columns = [ column ] }
    )

let tsUnit = TimeUnit.Milliseconds

/// A null-free millisecond timestamp column of `n` rows, one second and seven milliseconds apart.
let timestamps (n: int) =
    Column.ofTimestamps
        "t"
        tsUnit
        (Vector.ofArray [| for i in 0 .. n - 1 -> 1.7e9 + float i |])
        (Some(Vector.ofArray [| for i in 0 .. n - 1 -> (i * 7) % 1000 |]))
        AllValid

let decodeCase (name: string) (size: int) (column: Column) (ty: ColumnType) =
    let s = encoded column ty

    match ColumnCodec.decode s with
    | Ok(Embedded { Columns = [ c ] }) when Column.length c = size && Column.validity c = AllValid && c = column -> ()
    | other -> fail (sprintf "decode did not round-trip a null-free column: %A" (other |> Result.map ignore))

    measure name size (fun () -> ColumnCodec.decode s |> ignore)

/// `groups` groups of `size / groups` cells each, built and aggregated as the compute layer's `aggCells` does.
let aggCase (name: string) (size: int) (ty: ColumnType) (fn: AggFn) (cell: int -> Cell) (expect: Cell list -> Cell) =
    let groups = 10000
    let per = size / groups

    let lists =
        [| for g in 0 .. groups - 1 -> [ for r in 0 .. per - 1 -> cell (g * per + r) ] |]

    let agg (cells: Cell list) =
        match Column.ofCells "" ty cells with
        | Ok column -> Column.aggregate fn column |> Result.mapError (sprintf "%A")
        | Error e -> Error(sprintf "%A" e)

    for cells in lists do
        match agg cells with
        | Ok c when c = expect cells -> ()
        | other -> fail (sprintf "aggregate answered %A" other)

    measure name size (fun () ->
        for cells in lists do
            agg cells |> ignore)

let run (case: string) (size: int) =
    match case with
    | "decode-float" ->
        let column =
            Column.ofFloats "x" (Vector.ofArray [| for i in 0 .. size - 1 -> float i * 0.5 |]) AllValid

        decodeCase "ColumnCodec.decode (float, null-free)" size column FloatType
    | "decode-ts" ->
        decodeCase "ColumnCodec.decode (timestamp_ms, null-free)" size (timestamps size) (TimestampType tsUnit)
    | "agg-float" ->
        aggCase "aggCells Sum (float), 10,000 groups" size FloatType Sum (fun i -> Float(float i * 0.5)) (fun cells ->
            Float(
                cells
                |> List.fold
                    (fun a c ->
                        match c with
                        | Float x -> a + x
                        | _ -> a)
                    0.0
            ))
    | "agg-ts" ->
        let column = timestamps size

        aggCase
            "aggCells Max (timestamp_ms), 10,000 groups"
            size
            (TimestampType tsUnit)
            Max
            (fun i -> Column.cell i column)
            List.last
    | other -> fail (sprintf "unknown case %s" other)

let args = fsi.CommandLineArgs |> Array.skip 1

match args with
| [| case; size |] -> run case (int size)
| [||] ->
    let self = fsi.CommandLineArgs.[0]

    let cases =
        [ "decode-float", 100000
          "decode-ts", 100000
          "agg-float", 100000
          "agg-ts", 100000 ]

    printfn
        "machine: %s, %d logical processors, %s"
        (Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER")
        Environment.ProcessorCount
        Runtime.InteropServices.RuntimeInformation.OSDescription

    printfn ".NET: %s" (Environment.Version.ToString())
    printfn ""
    printfn "| case | rows | samples | mean ms | median ms | p95 ms | allocated bytes / call | gen2 / call |"
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
| _ -> failwith "usage: column-fill-phase-608.fsx [-- <case> <size>]"
