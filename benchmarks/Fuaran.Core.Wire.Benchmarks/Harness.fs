/// The wire benchmark's timing and reporting (Phase 364), shared by both hosts: nothing but the wall
/// clock and FSharp.Core, so the program means the same thing compiled by Fable and run under node as
/// it does on .NET. Every agreement in `Corpus.check` is asserted before a corpus is timed.
module Fuaran.Core.WireBench.Harness

open System

/// The wall clock in milliseconds. `DateTime`, not `Stopwatch`: Fable maps no `Stopwatch` member,
/// and a JavaScript timer binding would need a package beyond FSharp.Core. Every sample is batched
/// to `minSampleMs` because node's clock resolves a millisecond.
let nowMs () : float = float DateTime.UtcNow.Ticks / 10_000.0

/// The shortest a timed sample may be, so a millisecond clock contributes at most a few per cent.
let minSampleMs = 50.0

/// Two warm-up calls; then the calls per sample double until one sample spans `minSampleMs`; then
/// `runs` samples. The median sample divided by the calls per sample: milliseconds per call.
let medianMs (runs: int) (f: unit -> obj) : float =
    f () |> ignore
    f () |> ignore

    let sample (calls: int) =
        let start = nowMs ()

        for _ in 1..calls do
            f () |> ignore

        nowMs () - start

    let rec calibrate calls =
        if calls >= 1_000_000 || sample calls >= minSampleMs then
            calls
        else
            calibrate (calls * 2)

    let calls = calibrate 1
    let times = Array.init runs (fun _ -> sample calls / float calls)
    Array.sortInPlace times
    let mid = runs / 2

    if runs % 2 = 1 then
        times[mid]
    else
        (times[mid - 1] + times[mid]) / 2.0

/// One corpus prepared and its laws asserted: a corpus that fails is an error, never a missing row.
let preparedChecked (pins: bool) (name: string) (build: unit -> Fuaran.Core.JVal) : Corpus.Prepared =
    let p = Corpus.prepare (build ())
    let verdict = if pins then Corpus.check name p else Corpus.laws name p

    match verdict with
    | Ok() -> p
    | Error e -> failwith e

/// `FP <corpus> <case> <len>:<fnv>` for every case, after the laws (not the pins: this is the
/// listing a re-pin reads). `run.ps1` compares the two hosts' listings line for line.
let fingerprints () : string list =
    [ for (name, build) in Corpus.corpora do
          let p = preparedChecked false name build

          for (case, _) in Corpus.cases do
              yield "FP " + name + " " + case + " " + Corpus.fingerprint p case ]

/// Every case timed: `(corpus, case, ms per call)`, each corpus checked against its pins first.
let measure (runs: int) : (string * string * float) list =
    [ for (name, build) in Corpus.corpora do
          let p = preparedChecked true name build

          for (case, f) in Corpus.cases do
              yield name, case, medianMs runs (f p) ]

/// The machine-readable rows (`ROW <corpus> <case> <ms>`) and the median table, ms per call.
let report (rows: (string * string * float) list) : string list =
    let cell (c: string) (k: string) =
        rows
        |> List.tryFind (fun (c', k', _) -> c' = c && k' = k)
        |> Option.map (fun (_, _, ms) -> sprintf "%.3f" ms)
        |> Option.defaultValue "-"

    let names = Corpus.cases |> List.map fst

    [ for (c, k, ms) in rows do
          yield sprintf "ROW %s %s %.4f" c k ms
      yield ""
      yield "| corpus | " + String.Join(" | ", names) + " |"
      yield "|---|" + String.Join("|", names |> List.map (fun _ -> "---:")) + "|"
      for (c, _) in Corpus.corpora do
          yield "| " + c + " | " + String.Join(" | ", names |> List.map (cell c)) + " |" ]
