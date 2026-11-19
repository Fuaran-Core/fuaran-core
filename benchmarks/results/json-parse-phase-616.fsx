// Phase 616: `Json.parse` over number and literal tokens, timed and metered.
//
// Run from the repository root, after a Release build of the wire benchmark (which builds the wire layer):
//
//   dotnet build benchmarks/Fuaran.Core.Wire.Benchmarks -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/json-parse-phase-616.fsx
//
// With no arguments the script runs every case EACH IN ITS OWN PROCESS (it re-invokes itself with
// `-- <case>`), and prints the results table. One case is
// `dotnet fsi --optimize+ benchmarks/results/json-parse-phase-616.fsx -- floats`.
//
// The cases: `floats` and `state` are the wire benchmark's corpora of those names (`Corpus.fs`), rendered and
// parsed by `Json.parse`; `floats` is 10,000 float tokens, `state` carries no number or literal token at all and
// is the control. `ints` is 10,000 integer tokens (one in four negative, one in eight past the Int32 range);
// `literals` is 10,000 `true` / `false` tokens; `members` is 2,000 objects of five integer members each, read
// under `ReadOptions.tolerantOfNull`, whose member path looks for an erasable `null` at every member value.
//
// The method is Phase 607's (`tree-lookups.fsx` beside this file): the input is built and rendered OUTSIDE the
// timed closure; the closure is warmed up for a fixed WALL-CLOCK time, not a count of calls; then sampled until a
// time budget is spent (at least five samples). Each sample records its wall time and the bytes this thread
// allocated (`GC.GetAllocatedBytesForCurrentThread`); gen2 is the count of generation-2 collections over the
// sampled phase, divided by the number of samples. The library code timed is the Release build the `#r` lines
// name.
//
// `token bytes` is what one string per token costs on x64 (24 bytes of header and length, two bytes per unit,
// rounded up to 8), summed over the tokens a parse of the case meets: the bytes a parse that copies each token
// out of the input pays for those copies.
//
// The falsifier: every case checks that the parse answers the value the text was rendered from before it is
// timed, and fails the run otherwise, so a fast wrong parse cannot be reported.

#r "../Fuaran.Core.Wire.Benchmarks/bin/Release/net10.0/Fuaran.Core.Text.dll"
#r "../Fuaran.Core.Wire.Benchmarks/bin/Release/net10.0/Fuaran.Core.Tree.dll"
#r "../Fuaran.Core.Wire.Benchmarks/bin/Release/net10.0/Fuaran.Core.Wire.dll"
#r "../Fuaran.Core.Wire.Benchmarks/bin/Release/net10.0/Fuaran.Core.Wire.Benchmarks.dll"

open System
open System.Diagnostics
open Fuaran.Core
open Fuaran.Core.WireBench

let stringBytes (units: int) = (22 + 2 * units + 7) / 8 * 8

let rec tokenBytes (v: JVal) : int64 =
    match v with
    | JInt _
    | JFloat _ -> int64 (stringBytes (Json.render v).Length)
    | JBool b -> int64 (stringBytes (if b then 4 else 5))
    | JStr _ -> 0L
    | JArr xs -> xs |> List.sumBy tokenBytes
    | JObj fields -> fields |> List.sumBy (fun (_, x) -> tokenBytes x)

let ints () =
    let mutable x = 7

    JArr
        [ for i in 1..10_000 do
              x <- (x * 1103 + 12345) % 65521

              match i % 8 with
              | 0 -> yield JFloat(float x * 1e5 + 3e9)
              | 1
              | 5 -> yield JInt(-x)
              | _ -> yield JInt(x * (i % 3 + 1)) ]

let literals () =
    JArr [ for i in 1..10_000 -> JBool(i % 3 <> 0) ]

let members () =
    JArr [ for i in 1..2_000 -> JObj [ for k in 1..5 -> "m" + string k, JInt(i * k) ] ]

let measure (name: string) (minSamples: int) (warm: TimeSpan) (budget: TimeSpan) (extra: int64) (f: unit -> unit) =
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
        "RESULT|%s|%d|%.4f|%.4f|%.4f|%.0f|%d|%.3f"
        name
        times.Count
        (Seq.average times)
        (pick 0.5)
        (pick 0.95)
        (bytes |> Seq.map float |> Seq.average)
        extra
        gen2

let fail msg =
    eprintfn "%s" msg
    exit 2

let warm = TimeSpan.FromSeconds 2.0
let budget = TimeSpan.FromSeconds 5.0

let run (case: string) =
    let value, options, members =
        match case with
        | "floats" -> Corpus.floats (), ReadOptions.strict, 0
        | "state" -> Corpus.state (), ReadOptions.strict, 0
        | "ints" -> ints (), ReadOptions.strict, 0
        | "literals" -> literals (), ReadOptions.strict, 0
        | "members" -> members (), ReadOptions.tolerantOfNull, 10_000
        | other -> fail (sprintf "unknown case %s" other)

    let text = Json.render value

    match Json.read options text with
    | Ok v when v = value -> ()
    | Ok _ -> fail (sprintf "%s: the parse does not answer the rendered value" case)
    | Error e -> fail (sprintf "%s: the parse refused: %A" case e)

    // A member's null look-ahead is one four-unit string per member value.
    let extra = tokenBytes value + int64 members * int64 (stringBytes 4)

    measure case 5 warm budget extra (fun () -> Json.read options text |> ignore)

let args = fsi.CommandLineArgs |> Array.skip 1

match args with
| [| case |] -> run case
| [||] ->
    let self = fsi.CommandLineArgs.[0]

    printfn
        "machine: %s, %d logical processors, %s"
        (Environment.GetEnvironmentVariable "PROCESSOR_IDENTIFIER")
        Environment.ProcessorCount
        Runtime.InteropServices.RuntimeInformation.OSDescription

    printfn ".NET: %s" (Environment.Version.ToString())
    printfn ""
    printfn "| case | samples | mean ms | median ms | p95 ms | allocated bytes / call | token bytes | gen2 / call |"
    printfn "|---|---:|---:|---:|---:|---:|---:|---:|"

    for c in [ "floats"; "state"; "ints"; "literals"; "members" ] do
        let psi = ProcessStartInfo("dotnet", sprintf "fsi --optimize+ \"%s\" -- %s" self c)

        psi.RedirectStandardOutput <- true
        psi.UseShellExecute <- false
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            failwithf "case %s exited %d" c p.ExitCode

        for line in out.Split('\n') do
            if line.StartsWith "RESULT|" then
                let f = line.Trim().Split('|')
                printfn "| %s | %s | %s | %s | %s | %s | %s | %s |" f.[1] f.[2] f.[3] f.[4] f.[5] f.[6] f.[7] f.[8]
| _ -> failwith "usage: json-parse-phase-616.fsx [-- <case>]"
