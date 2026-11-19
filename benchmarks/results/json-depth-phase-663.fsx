// Phase 663: how deep `Json.read` nests on a small thread stack, and what one nesting level costs it.
//
// Run from the repository root, after a build of the wire benchmark in the configuration you probe (it builds
// the wire layer):
//
//   dotnet build benchmarks/Fuaran.Core.Wire.Benchmarks -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/json-depth-phase-663.fsx
//
//   dotnet build benchmarks/Fuaran.Core.Wire.Benchmarks -c Debug --nologo
//   dotnet fsi --optimize+ --define:LIBDEBUG benchmarks/results/json-depth-phase-663.fsx
//
// A stack overflow cannot be caught on .NET: it ends the process. So every probe runs IN ITS OWN PROCESS (the
// script re-invokes itself with `-- probe <shape> <stackKB> <depth> <cap>`), and the parent reads the child's
// exit code: 0 with the line `ok`, `refused` or `other` printed is an answer; anything else is the process lost.
//
// A probe parses one document nested `depth` deep on a thread created with `stackKB` KB of stack, under
// `JsonLimits.depthOnly cap` (the null policy `ReadOptions.strict`'s). Two shapes: `arrays` is `[[[…]]]`, and
// `objects` is `{"a":{"a":…{}}}`.
//
// The parent answers two questions for each shape:
//
// 1. At the default cap (`Json.defaultMaxDepth`, 512) on a 256 KB thread: does the document AT the cap decode,
//    and is the one PAST it refused with `MaxDepthExceeded`, or is the process lost first?
// 2. With the cap lifted out of the way (1,000,000), the deepest document that still decodes on a 256 KB and on
//    a 1 MB thread, by bisection over processes. The stack per nesting level is the difference of the two stacks
//    over the difference of the two depths, which cancels what the thread spends before the first level. A
//    parser whose depth costs no stack decodes every depth the bisection tries (`>= 100000`), and no frame cost
//    is printed.
//
// The falsifier: a probe that answered `ok` parsed a value whose nesting was counted back to `depth` before it
// printed, so a probe that returned early cannot read as a success.

#if LIBDEBUG
#r "../Fuaran.Core.Wire.Benchmarks/bin/Debug/net10.0/Fuaran.Core.Text.dll"
#r "../Fuaran.Core.Wire.Benchmarks/bin/Debug/net10.0/Fuaran.Core.Tree.dll"
#r "../Fuaran.Core.Wire.Benchmarks/bin/Debug/net10.0/Fuaran.Core.Wire.dll"
#else
#r "../Fuaran.Core.Wire.Benchmarks/bin/Release/net10.0/Fuaran.Core.Text.dll"
#r "../Fuaran.Core.Wire.Benchmarks/bin/Release/net10.0/Fuaran.Core.Tree.dll"
#r "../Fuaran.Core.Wire.Benchmarks/bin/Release/net10.0/Fuaran.Core.Wire.dll"
#endif

open System
open System.Diagnostics
open System.Text
open System.Threading
open Fuaran.Core

let doc (shape: string) (depth: int) : string =
    let sb = StringBuilder()

    match shape with
    | "arrays" ->
        sb.Append('[', depth).Append(']', depth) |> ignore
    | "objects" ->
        for _ in 1 .. depth - 1 do
            sb.Append("{\"a\":") |> ignore

        sb.Append("{}").Append('}', depth - 1) |> ignore
    | s -> failwithf "unknown shape %s" s

    sb.ToString()

// The nesting of a parsed value, counted iteratively so the count itself spends no stack.
let nesting (v: JVal) : int =
    let mutable d = 0
    let mutable cur = Some v

    while cur.IsSome do
        match cur.Value with
        | JArr [ x ]
        | JObj [ (_, x) ] ->
            d <- d + 1
            cur <- Some x
        | JArr []
        | JObj [] ->
            d <- d + 1
            cur <- None
        | _ -> failwith "unexpected shape"

    d

let probe (shape: string) (stackKB: int) (depth: int) (cap: int) : int =
    let text = doc shape depth

    let options =
        { ReadOptions.strict with
            Limits = JsonLimits.depthOnly cap }

    let mutable answer = "unset"

    let work () =
        answer <-
            match Json.read options text with
            | Ok v when nesting v = depth -> "ok"
            | Ok _ -> "wrong"
            | Error(JsonRefusal.LimitExceeded b) when b.Limit = JsonLimit.Depth -> "refused@" + string b.Position
            | Error r -> "other:" + string r

    let t = Thread(ThreadStart work, stackKB * 1024)
    t.Start()
    t.Join()
    printfn "%s" answer
    0

let self = __SOURCE_DIRECTORY__ + "/" + __SOURCE_FILE__

let defineArgs =
#if LIBDEBUG
    "--define:LIBDEBUG "
#else
    ""
#endif

let run (shape: string) (stackKB: int) (depth: int) (cap: int) : string =
    let psi =
        ProcessStartInfo(
            "dotnet",
            sprintf "fsi --optimize+ %s\"%s\" -- probe %s %d %d %d" defineArgs self shape stackKB depth cap
        )

    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    use p = Process.Start psi
    // Both streams drained at once: a lost process writes its whole stack to stderr, which fills the pipe and
    // blocks the child while the parent waits on stdout.
    let err = p.StandardError.ReadToEndAsync()
    let out = p.StandardOutput.ReadToEnd()
    err.Wait()
    p.WaitForExit()

    if p.ExitCode = 0 then
        out.Trim()
    else
        sprintf "LOST (exit %d)" p.ExitCode

// The deepest depth in [lo, hi] that answers `ok`, assuming `lo` does and monotonicity above it.
let deepest (shape: string) (stackKB: int) : int =
    let lift = 1000000
    let mutable lo = 1
    let mutable hi = 100000

    if run shape stackKB hi lift = "ok" then
        hi
    else
        while hi - lo > 1 do
            let mid = lo + (hi - lo) / 2

            if run shape stackKB mid lift = "ok" then lo <- mid else hi <- mid

        lo

match fsi.CommandLineArgs |> Array.toList |> List.tail with
| [ "probe"; shape; stackKB; depth; cap ] -> exit (probe shape (int stackKB) (int depth) (int cap))
| [] ->
    let config =
#if LIBDEBUG
        "Debug"
#else
        "Release"
#endif

    printfn "library build: %s" config

    for shape in [ "arrays"; "objects" ] do
        let cap = Json.defaultMaxDepth
        printfn "%s, 256 KB, cap %d: depth %d -> %s" shape cap cap (run shape 256 cap cap)
        printfn "%s, 256 KB, cap %d: depth %d -> %s" shape cap (cap + 1) (run shape 256 (cap + 1) cap)
        let small = deepest shape 256
        let large = deepest shape 1024
        printfn "%s: deepest decoded on 256 KB = %d, on 1 MB = %d" shape small large

        if large > small && large < 100000 then
            printfn "%s: stack per nesting level = %d bytes" shape ((1024 - 256) * 1024 / (large - small))
| args -> failwithf "unexpected arguments %A" args
