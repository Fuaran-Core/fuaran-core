// Phase 615: the validator's per-family walks and an op's change set, timed and metered.
//
// Run from the repository root, after a Release build of the validator package (which builds the tree, op and
// propagation packages it references):
//
//   dotnet build src/Fuaran.Core.Validator -c Release --nologo
//   dotnet fsi --optimize+ benchmarks/results/walks-phase-615.fsx
//
// With no arguments the script runs every case at every size, EACH IN ITS OWN PROCESS (it re-invokes itself
// with `-- <case> <size>`), and prints the results table. One case at one size is
// `dotnet fsi --optimize+ benchmarks/results/walks-phase-615.fsx -- runAll 5000`.
//
// The tree is `tree-lookups.fsx`'s roadmap shape (a root, epochs of 50 phases, four tasks a phase), each task
// reading the task before it in its phase, so the dependency graph has edges and a closure to compute.
//
// The cases, in two groups.
//
//   The validator, 30 `perNode` families over the tree:
//     runAll       `Validator.runAll` as shipped: each family runs its own preorder.
//     oneWalk      the same 30 rule bodies over ONE `Tree.preorder`, in family order then node order: the
//                  bound a shared walk can reach (the per-node rule calls remain; only the walks go).
//     preorder     one `Tree.preorder` alone, the unit the 29 extra walks are made of.
//
//   An op's change set, over an `UpdateNode` of one task (the commonest live edit) and an `InsertChild` of a task:
//     changedUpdate / changedInsert   `Propagation.changedForOp` as shipped.
//     depMap       `Propagation.dependencyMap` over the pre-edit tree (a walk a held index does not remove).
//     index2       the two id -> node maps `seedsForOp` builds (pre and post), plus `Tree.ids` of post: the
//                  walks a held `Tree.NodeIndex` would replace.
//     touched      `Propagation.touchedBy` over the update (its `Tree.subtree` / `parentOf` lookups are walks too).
//
// The method is Phase 607's (`tree-lookups.fsx` beside this file): the input is built OUTSIDE the timed closure;
// the closure is warmed up for a fixed WALL-CLOCK time, not a count of calls; then sampled until a time budget is
// spent (at least five samples). Each sample records its wall time and the bytes this thread allocated
// (`GC.GetAllocatedBytesForCurrentThread`); gen2 is the count of generation-2 collections over the sampled phase,
// divided by the number of samples.
//
// The falsifier: before anything is timed, `oneWalk` must return exactly `runAll`'s defects (a non-empty list:
// every fifth family flags a fifth of the nodes), and each change set must be non-empty and contain the edited
// task's reader, so a case that did nothing cannot be reported.

#r "../../src/Fuaran.Core.Validator/bin/Release/net10.0/Fuaran.Core.Text.dll"
#r "../../src/Fuaran.Core.Validator/bin/Release/net10.0/Fuaran.Core.Tree.dll"
#r "../../src/Fuaran.Core.Validator/bin/Release/net10.0/Fuaran.Core.Ops.dll"
#r "../../src/Fuaran.Core.Validator/bin/Release/net10.0/Fuaran.Core.Relation.dll"
#r "../../src/Fuaran.Core.Validator/bin/Release/net10.0/Fuaran.Core.Propagation.dll"
#r "../../src/Fuaran.Core.Validator/bin/Release/net10.0/Fuaran.Core.Column.dll"
#r "../../src/Fuaran.Core.Validator/bin/Release/net10.0/Fuaran.Core.Wire.dll"
#r "../../src/Fuaran.Core.Validator/bin/Release/net10.0/Fuaran.Core.Validator.dll"

open System
open System.Diagnostics
open Fuaran.Core

type N =
    { Id: string
      Kind: string
      Value: string
      Reads: string list
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

let readsOf (n: N) : string seq = n.Reads

let leaf id kind =
    { Id = id
      Kind = kind
      Value = ""
      Reads = []
      Children = [] }

/// A roadmap-shaped tree of about `size` nodes; task t of a phase reads task t-1.
let tree (size: int) =
    let phases = max 1 (size / 5)

    let task i t =
        { leaf (sprintf "p%d.t%d" i t) "task" with
            Value = string (t * i)
            Reads = (if t > 1 then [ sprintf "p%d.t%d" i (t - 1) ] else []) }

    let phase i =
        { leaf (sprintf "p%d" i) "phase" with
            Children = [ for t in 1..4 -> task i t ] }

    let epochs =
        [ for e in 0 .. (phases - 1) / 50 ->
              { leaf (sprintf "e%d" e) "epoch" with
                  Children = [ for i in e * 50 .. min (phases - 1) (e * 50 + 49) -> phase i ] } ]

    { leaf "root" "roadmap" with
        Children = epochs }

/// Rule k: every fifth family flags each node whose id hash falls in a fifth of the range; the others check a
/// condition no node meets. Either way the body reads the node, as a real rule does.
let rule (k: int) (_: NodeWitness<N, string>) (n: N) : Defect<string> list =
    if k % 5 = 0 && (n.Id.GetHashCode() &&& 0x7fffffff) % 5 = k % 5 then
        [ { Code = sprintf "R%02d" k
            Severity = Severity.Warning
            Message = "flagged"
            Node = Some n.Id
            Family = ""
            Related = [] } ]
    elif n.Kind = "task" && n.Value.Length > 64 + k then
        [ { Code = sprintf "R%02d" k
            Severity = Severity.Error
            Message = "too long"
            Node = Some n.Id
            Family = ""
            Related = [] } ]
    else
        []

let families = 30

let registry =
    match Validator.ofFamilies [ for k in 0 .. families - 1 -> Validator.perNode (sprintf "fam%02d" k) (rule k) ] with
    | Ok r -> r
    | Error e -> failwithf "registry refused: %A" e

/// The bound: the same rule bodies over one preorder, stamped with their family as `runAll` stamps them.
let oneWalk (root: N) : Defect<string> list =
    let nodes = Tree.preorder nodew root

    [ for k in 0 .. families - 1 do
          let fam = sprintf "fam%02d" k

          for n in nodes do
              for d in rule k nodew n -> { d with Family = fam } ]

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

let warm = TimeSpan.FromSeconds 1.0
let budget = TimeSpan.FromSeconds 3.0
let scope = OpScope.plain nodew idw

let applied (root: N) (op: SkeletonOp<N, string>) =
    match Ops.apply Policy.none scope op root with
    | Ok post -> post
    | Error e -> fail (sprintf "the op was refused: %A" e)

/// The middle phase's second task, rewritten in place; its reader is the third task.
let updateOp (size: int) (root: N) =
    let i = max 0 (size / 5 / 2)

    match Tree.tryFind nodew idw (sprintf "p%d.t2" i) root with
    | Some n -> UpdateNode { n with Value = "edited" }, sprintf "p%d.t3" i
    | None -> fail "the edited task is absent"

let insertOp (size: int) =
    let i = max 0 (size / 5 / 2)

    InsertChild(
        sprintf "p%d" i,
        { leaf (sprintf "p%d.t5" i) "task" with
            Reads = [ sprintf "p%d.t4" i ] }
    ),
    sprintf "p%d.t5" i

let run (case: string) (size: int) =
    let root = tree size
    let n = Tree.preorder nodew root |> List.length

    match case with
    | "runAll" ->
        let d = Validator.runAll nodew registry root

        if List.isEmpty d || d <> oneWalk root then
            fail "runAll found nothing, or disagrees with the one-walk bound"

        measure (sprintf "Validator.runAll, %d families (%d nodes)" families n) size 5 warm budget (fun () ->
            Validator.runAll nodew registry root |> ignore)
    | "oneWalk" ->
        measure (sprintf "one preorder, %d families (%d nodes)" families n) size 5 warm budget (fun () ->
            oneWalk root |> ignore)
    | "preorder" ->
        measure (sprintf "Tree.preorder once (%d nodes)" n) size 5 warm budget (fun () ->
            Tree.preorder nodew root |> ignore)
    | "changedUpdate"
    | "changedInsert" ->
        let op, reader =
            if case = "changedUpdate" then
                updateOp size root
            else
                insertOp size

        let post = applied root op
        let c = Propagation.changedForOp nodew idw readsOf root post op

        if not (Set.contains reader c) then
            fail (sprintf "the change set misses %s: %A" reader c)

        measure (sprintf "Propagation.changedForOp, %s (%d nodes)" case n) size 5 warm budget (fun () ->
            Propagation.changedForOp nodew idw readsOf root post op |> ignore)
    | "depMap" ->
        measure (sprintf "Propagation.dependencyMap (%d nodes)" n) size 5 warm budget (fun () ->
            Propagation.dependencyMap nodew idw readsOf root |> ignore)
    | "index2" ->
        let op, _ = updateOp size root
        let post = applied root op

        let index (r: N) =
            Tree.preorder nodew r |> List.map (fun x -> x.Id, x) |> Map.ofList

        measure (sprintf "two id maps + Tree.ids (%d nodes)" n) size 5 warm budget (fun () ->
            index root |> ignore
            index post |> ignore
            Tree.ids nodew post |> Set.ofList |> ignore)
    | "touched" ->
        let op, _ = updateOp size root

        measure (sprintf "Propagation.touchedBy, update (%d nodes)" n) size 5 warm budget (fun () ->
            Propagation.touchedBy nodew idw root op |> ignore)
    | other -> fail (sprintf "unknown case %s" other)

let args = fsi.CommandLineArgs |> Array.skip 1

match args with
| [| case; size |] -> run case (int size)
| [||] ->
    let self = fsi.CommandLineArgs.[0]

    let cases =
        [ for c in
              [ "runAll"
                "oneWalk"
                "preorder"
                "changedUpdate"
                "changedInsert"
                "depMap"
                "index2"
                "touched" ] do
              for s in [ 1000; 5000 ] -> c, s ]

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
| _ -> failwith "usage: walks-phase-615.fsx [-- <case> <size>]"
