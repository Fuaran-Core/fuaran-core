// Phase 609: the SHA-256 digest and its UTF-8 encoder, timed and metered.
//
// Run from the repository root, after a Release build of the text kernel:
//
//   dotnet build src/Fuaran.Core.Text -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/digest-phase-609.fsx
//
// With no arguments the script runs every case at every size, EACH IN ITS OWN PROCESS (it re-invokes itself
// with `-- <case> <size>`), and prints the results table. One case at one size is
// `dotnet fsi --optimize+ benchmarks/results/digest-phase-609.fsx -- hex 65536`.
//
// The cases: `utf8` is `Hash.utf8Bytes` over a string of `size` UTF-16 units; `bytes` is `Hash.sha256HexOfBytes`
// over `size` bytes; `hex` is `Hash.sha256Hex` over a string of `size` units, the composition of the two. The
// string mixes one-, two- and three-byte units and surrogate pairs (see `text`), so every encoder branch is on
// the path; the bytes are a fixed xorshift stream.
//
// The method is Phase 607's (`tree-lookups.fsx` beside this file): the input is built OUTSIDE the timed closure;
// the closure is warmed up for a fixed WALL-CLOCK time, not a count of calls; then sampled until a time budget is
// spent (at least five samples). Each sample records its wall time and the bytes this thread allocated
// (`GC.GetAllocatedBytesForCurrentThread`); gen2 is the count of generation-2 collections over the sampled phase,
// divided by the number of samples. The library code timed is the Release build the `#r` line names.
//
// The falsifier: every case checks its answer against the platform (`System.Text.Encoding.UTF8`,
// `System.Security.Cryptography.SHA256`) before it is timed, and fails the run on a mismatch, so a fast wrong
// digest cannot be reported.

#r "../../src/Fuaran.Core.Text/bin/Release/net10.0/Fuaran.Core.Text.dll"

open System
open System.Diagnostics
open Fuaran.Core

/// `size` UTF-16 units cycling ASCII, a two-byte unit, a three-byte unit and a surrogate pair: mostly ASCII,
/// as a canonical document is, with every encoder branch taken.
let text (size: int) : string =
    let sb = Text.StringBuilder(size)
    let mutable k = 0

    while sb.Length < size do
        match k % 16 with
        | 13 -> sb.Append('é') |> ignore
        | 14 -> sb.Append('中') |> ignore
        | 15 when sb.Length + 2 <= size -> sb.Append("\U0001F600") |> ignore
        | _ -> sb.Append(char (0x61 + k % 26)) |> ignore

        k <- k + 1

    sb.ToString()

let bytesOf (size: int) : byte[] =
    let out = Array.zeroCreate<byte> size
    let mutable x = 2463534242u

    for i in 0 .. size - 1 do
        x <- x ^^^ (x <<< 13)
        x <- x ^^^ (x >>> 17)
        x <- x ^^^ (x <<< 5)
        out[i] <- byte (x &&& 0xFFu)

    out

let platformHex (b: byte[]) =
    Convert.ToHexString(Security.Cryptography.SHA256.HashData b).ToLowerInvariant()

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

let warm = TimeSpan.FromSeconds 2.0
let budget = TimeSpan.FromSeconds 5.0

let run (case: string) (size: int) =
    match case with
    | "utf8" ->
        let s = text size

        if Hash.utf8Bytes s <> Text.Encoding.UTF8.GetBytes s then
            fail "utf8Bytes disagrees with the platform encoder"

        measure "Hash.utf8Bytes (units)" size 5 warm budget (fun () -> Hash.utf8Bytes s |> ignore)
    | "bytes" ->
        let b = bytesOf size

        if Hash.sha256HexOfBytes b <> platformHex b then
            fail "sha256HexOfBytes disagrees with the platform digest"

        measure "Hash.sha256HexOfBytes (bytes)" size 5 warm budget (fun () -> Hash.sha256HexOfBytes b |> ignore)
    | "hex" ->
        let s = text size

        if Hash.sha256Hex s <> platformHex (Text.Encoding.UTF8.GetBytes s) then
            fail "sha256Hex disagrees with the platform digest"

        measure "Hash.sha256Hex (units)" size 5 warm budget (fun () -> Hash.sha256Hex s |> ignore)
    | other -> fail (sprintf "unknown case %s" other)

let args = fsi.CommandLineArgs |> Array.skip 1

match args with
| [| case; size |] -> run case (int size)
| [||] ->
    let self = fsi.CommandLineArgs.[0]

    let cases =
        [ for c in [ "utf8"; "bytes"; "hex" ] do
              for s in [ 64; 1024; 65536; 1048576 ] -> c, s ]

    printfn
        "machine: %s, %d logical processors, %s"
        (Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER")
        Environment.ProcessorCount
        Runtime.InteropServices.RuntimeInformation.OSDescription

    printfn ".NET: %s" (Environment.Version.ToString())
    printfn ""
    printfn "| case | size | samples | mean ms | median ms | p95 ms | allocated bytes / call | gen2 / call |"
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
| _ -> failwith "usage: digest-phase-609.fsx [-- <case> <size>]"
