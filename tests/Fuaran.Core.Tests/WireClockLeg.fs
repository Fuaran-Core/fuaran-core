/// Phase 364 — the wire layer's clock leg, and the freeze of the corpus the benchmark tables time.
///
/// THE CLOCK LEG asserts a SHAPE, never a speed: each case times one wire operation over the
/// benchmark's state-shaped corpus (benchmarks/Fuaran.Core.Wire.Benchmarks/Corpus.fs) at two sizes
/// eight times apart, in one window on one host, and bounds the ratio. A linear operation reads about
/// 8; the accidental quadratic this exists to catch (a list append in a loop, a string concatenated
/// per member, a re-scan per character) reads 50 to 64. An absolute time would make the gate a statement
/// about the machine; a ratio of two cases on the same host, in the same window, is a statement about
/// the code. A constant-factor slowdown moves both sizes alike and is NOT this leg's to catch: the
/// two-host tables under benchmarks/results/ are (docs/wire-performance.md, "How to read a ratio").
///
/// IT RUNS IN THE ORDINARY SUITE, IN THE GATE'S BUILD. `verify.ps1` runs this suite as the Debug build
/// it compiles, so the bounds below are calibrated on Debug readings, with the Release readings beside
/// them in the doc. A Debug ratio guards the shape; it is not the speed figure — that is the Release
/// and node table. The list is `testSequenced`, so Expecto runs it after every parallel list has
/// finished and nothing else of the suite shares its window.
///
/// The leg measures the code rather than the machine the way the compute repository's leg does
/// (its Phase 282 and 285): every attempt is bracketed by readings of a fixed calibration workload,
/// a window whose readings show load (above `k` times the leg's quiet baseline, or two consecutive
/// readings more than `k` apart) is DISCARDED rather than counted, and a case is red only when all
/// three counted attempts are red. A case that cannot find an unsaturated window within its budget
/// fails with "machine saturated, no verdict" — never a green it did not measure. One thing differs:
/// every sample runs inside a no-GC region (`sampler`), because over this corpus the collector, not
/// the code, decided the ratio — see `sampler` for the measurement.
module Fuaran.Core.Tests.WireClockLeg

open System.Diagnostics
open Expecto
open Fuaran.Core
open Fuaran.Core.WireBench

/// The two sizes of every clock case: `small` and eight times it.
let private small = 20_000
let private sizeRatio = 8

/// The bound on each case's large/small ratio: 20, PROPOSED from the Phase 364 readings and the
/// operator's to set (docs/wire-performance.md, "The clock leg's bound", carries every reading).
/// Linear reads 8. On the reference machine, with the collector held off, 36 quiet readings across
/// Debug and Release read 7.99 to 10.35 (render the highest: its buffer grows by doubling), and 37
/// readings under an all-core burner read up to 16.24, a burner-starved large size outgrowing the
/// cache. A deliberately quadratic escape read 50.6. So 20 is about twice the worst quiet shape (the
/// compute leg's rule), 1.2x the worst busy one, and 2.5x below the quadratic it exists to catch.
let ratioBound = 20.0

/// How many cases the leg holds: a literal, held against the list below by a main-suite case, so a
/// case dropped from the list without this moving is red.
let clockInventory = 3

/// The calibration workload (the compute repository's Phase 285 shape): an integer multiply-xor
/// chain, each step dependent on the one before, allocating and touching no memory, so it measures
/// how fast the core it is scheduled on retires work and cannot move the collector the cases are
/// timed against. A reading is the best of nine runs of 2^22 steps.
module private Calibration =
    let private steps = 1 <<< 22
    let private runsPerReading = 9
    let mutable private sink = 0L

    let private runOnce () : float =
        let t0 = Stopwatch.GetTimestamp()
        let mutable acc = 0x5851F42DL

        for s in 1..steps do
            acc <- (acc ^^^ int64 s) * 0x100000001B3L

        let t1 = Stopwatch.GetTimestamp()
        sink <- sink ^^^ acc
        float (t1 - t0) * 1000.0 / float Stopwatch.Frequency

    let reading () : float =
        let mutable best = runOnce ()

        for _ in 2..runsPerReading do
            best <- min best (runOnce ())

        best

    /// The saturation factor, the compute leg's measured value.
    let k = 1.25

    /// Saturated windows a case may discard before it gives up with no verdict.
    let maxDiscarded = 5

    /// The back-off before a retry: 2 s, doubling, at most 16 s.
    let backoffMs (discarded: int) : int =
        min 16_000 (2_000 * (1 <<< (discarded - 1)))

    let mutable private baseline = nan

    /// The leg's quiet baseline: the best of fifteen readings after a warm-up, fixed on first use.
    let baselineMs () : float =
        if System.Double.IsNaN baseline then
            reading () |> ignore
            baseline <- List.min [ for _ in 1..15 -> reading () ]

        baseline

    let saturated (b: float) (readings: float list) : bool =
        List.exists (fun r -> r > k * b) readings
        || readings |> List.pairwise |> List.exists (fun (x, y) -> max x y > k * min x y)

/// A sampler for `f` that times the CODE and not the collector (see "Why the leg excludes the
/// collector" in docs/wire-performance.md): each sample runs `f` `calls` times inside a no-GC region
/// sized from `f`'s own measured allocation, after a full collection, and is `None` (not counted)
/// when the region could not be entered or a collection happened anyway. `calls` is what makes a
/// sample span about 5 ms, capped so the region stays under 160 MB.
///
/// Why: measured over this corpus in Release, `Json.parse` read a large/small ratio from 14 to 43
/// for an 8x input (bimodal, run to run), while the same parses timed with no collection read 7.96 —
/// the parser is linear and the excess is the collector promoting the growing result. A bound wide
/// enough for that spread could not see a quadratic; the code's shape, with the collector held off,
/// can be bounded tightly.
let private sampler (f: unit -> obj) : unit -> float option =
    let a0 = System.GC.GetAllocatedBytesForCurrentThread()
    let sw0 = Stopwatch.StartNew()
    f () |> ignore
    let oneMs = max 0.001 sw0.Elapsed.TotalMilliseconds
    let perCall = max 1L (System.GC.GetAllocatedBytesForCurrentThread() - a0)
    let byTime = max 1 (int (ceil (5.0 / oneMs)))
    let byBudget = max 1 (int (128_000_000L / perCall))
    let calls = min byTime byBudget
    let budget = int64 calls * perCall * 5L / 4L + 8_000_000L

    fun () ->
        System.GC.Collect()
        System.GC.WaitForPendingFinalizers()
        System.GC.Collect()

        let entered =
            try
                System.GC.TryStartNoGCRegion budget
            with _ ->
                false

        if not entered then
            None
        else
            // Read AFTER entering: entering the region may itself collect to make room.
            let collections = System.GC.CollectionCount 0
            let sw = Stopwatch.StartNew()

            for _ in 1..calls do
                f () |> ignore

            sw.Stop()

            let clean = System.GC.CollectionCount 0 = collections

            if System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.NoGCRegion then
                System.GC.EndNoGCRegion()

            if clean then
                Some(sw.Elapsed.TotalMilliseconds / float calls)
            else
                None

/// Run `f` for at least `ms` milliseconds: long enough for the runtime's tiered compilation to have
/// promoted it before anything is counted. Without it the FIRST size timed read up to 1.6x slow in
/// some processes and not others, which moved a ratio by as much (docs/wire-performance.md).
let private warm (ms: float) (f: unit -> obj) =
    let sw = Stopwatch.StartNew()

    while sw.Elapsed.TotalMilliseconds < ms do
        f () |> ignore

/// One clock case's inputs at a size: the operation to time, already prepared.
type private Sized = int -> (unit -> obj)

/// The escape case's input at size `n`: `n` x 3.2 characters (64,000 at `small`, the whole of the
/// escape-heavy corpus's strings concatenated) as ONE string, cycling the corpus past its length, so
/// the case times `Json.escape`'s walk over a long input.
let private heavyText =
    lazy (Corpus.strings (Corpus.escapeHeavy ()) |> String.concat "")

let private escapeAt: Sized =
    fun n ->
        let corpus = heavyText.Force()
        let length = n * 64 / 20
        let s = (String.replicate (length / corpus.Length + 1) corpus).Substring(0, length)
        fun () -> box (Json.escape s)

let private renderAt: Sized =
    fun n ->
        let v = Corpus.stateOf n
        fun () -> box (Json.render v)

let private parseAt: Sized =
    fun n ->
        let t = Json.render (Corpus.stateOf n)
        fun () -> box (Json.parse t)

/// How many interleaved sample pairs one measurement takes, and how many clean samples per size it
/// needs to stand; each size's figure is its best clean sample.
let private samplePairs = 7
let private cleanNeeded = 4

/// One measurement: both sizes warmed, then sampled INTERLEAVED (small, large, small, …) so a slow
/// drift in the window touches both alike. `Some(smallMs, largeMs)`, or `None` when either size had
/// fewer than `cleanNeeded` clean samples — the collector could not be held off, and a figure that
/// includes it is not the one the bound is about.
let private measure (at: Sized) : (float * float) option =
    let f1 = at small
    let f2 = at (small * sizeRatio)
    warm 300.0 f1
    warm 300.0 f2
    let s1 = sampler f1
    let s2 = sampler f2
    let pairs = [ for _ in 1..samplePairs -> s1 (), s2 () ]
    let small' = pairs |> List.choose fst
    let large' = pairs |> List.choose snd

    if small'.Length >= cleanNeeded && large'.Length >= cleanNeeded then
        Some(List.min small', List.min large')
    else
        None

/// A clock case: discarded windows retried after a back-off, red only when three counted attempts
/// are red, and "machine saturated, no verdict" (a failure, never a green) past the discard budget.
let private clockCase (name: string) (at: Sized) : Test =
    testCase name
    <| fun _ ->
        let b = Calibration.baselineMs ()

        let rec attempt (counted: int) (discarded: int) =
            let before = Calibration.reading ()
            let measured = measure at
            let after = Calibration.reading ()
            let smallMs, largeMs = defaultArg measured (nan, nan)
            let ratio = largeMs / smallMs

            let line =
                sprintf
                    "  [wire-clock] %s: %.3f ms @ %d -> %.3f ms @ %d, ratio %.2f (bound %.1f); calibration [%.3f, %.3f] ms, baseline %.3f"
                    name
                    smallMs
                    small
                    largeMs
                    (small * sizeRatio)
                    ratio
                    ratioBound
                    before
                    after
                    b

            if measured.IsNone || Calibration.saturated b [ before; after ] then
                printfn
                    "%s - window %s, discarded"
                    line
                    (if measured.IsNone then
                         "UNMEASURED (the collector could not be held off)"
                     else
                         "SATURATED")

                let discarded = discarded + 1

                if discarded >= Calibration.maxDiscarded then
                    failtestf
                        "machine saturated, no verdict: %s - %d window(s) discarded (saturated or unmeasured), %d counted; re-run when the machine is quieter"
                        name
                        discarded
                        counted

                System.Threading.Thread.Sleep(Calibration.backoffMs discarded)
                attempt counted discarded
            else
                let counted = counted + 1
                printfn "%s - counted (attempt %d)" line counted

                if ratio <= ratioBound then
                    ()
                elif counted < 3 then
                    attempt counted discarded
                else
                    failtestf
                        "%s scaled by %.2f for an %dx larger input on all three counted attempts (bound %.1f): the operation is no longer linear in its input"
                        name
                        ratio
                        sizeRatio
                        ratioBound

        attempt 0 0

/// The clock leg: sequenced, so it runs after the suite's parallel lists, alone.
[<Tests>]
let clockLeg =
    testSequenced
    <| testList
        "WireClockLeg"
        [ clockCase "escape scales linearly" escapeAt
          clockCase "render scales linearly" renderAt
          clockCase "parse scales linearly" parseAt ]

/// The benchmark corpus is frozen: every case's output on .NET is the pinned fingerprint, and the
/// clock leg's inventory is the list it runs. A corpus edit that moves a pin is a new baseline for
/// every results file that cites one, and has to say so.
[<Tests>]
let corpusFrozen =
    testList
        "WireBenchCorpus"
        [ for (name, build) in Corpus.corpora do
              yield
                  test (sprintf "the %s corpus holds its laws and its pins" name) {
                      let p = Corpus.prepare (build ())
                      Expect.equal (Corpus.laws name p) (Ok()) "the corpus laws hold on .NET"
                      Expect.equal (Corpus.unpinned name p) [] "every case's fingerprint is the pinned one"
                  }
          yield
              test "the clock leg's inventory is the list it runs" {
                  let cases = clockLeg |> Test.toTestCodeList |> List.length
                  Expect.equal cases clockInventory "the clock leg holds clockInventory cases"
              } ]
