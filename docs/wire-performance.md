# Wire-layer performance: what is timed, how to run it, how to read it

`Fuaran.Core.Wire` is timed in this repository in two ways (Phase 364):

- **The timing harness** (`benchmarks/`) produces the speed figures: escape, render, canonical render
  and parse over five fixed corpora, on .NET and under node, with the node ÷ .NET ratio per case. It
  is run by hand. Its tables are committed under `benchmarks/results/`.
- **The clock leg** (`tests/Fuaran.Core.Tests/WireClockLeg.fs`) checks the shape of the code, not its
  speed. It runs inside the ordinary suite, so `verify.ps1` runs it on every gate. It fails when escape,
  render or parse stops being linear in its input.

The harness project is in `Fuaran.Core.slnx`, so the gate BUILDS the whole program (a harness that drifts
from the API it times fails the build, not a later run by hand) and never RUNS it; the by-hand run below is
the only place it is timed.

The two answer different questions. A phase that makes the wire layer faster or slower cites the
harness tables before and after. The clock leg only catches the accidental quadratic.

## What is timed

The corpora are in `benchmarks/Fuaran.Core.Wire.Benchmarks/Corpus.fs`. They are built
deterministically, using no clock, no random source and no host setting, so .NET and node build the
same values:

| corpus | value |
|---|---|
| `escape-free` | 1,000 strings of 64 characters: ASCII letters, digits, punctuation and three BMP characters past ASCII, none of them escaped |
| `escape-heavy` | the same count and length, about one character in four a quote, a backslash or a control character |
| `op-stream` | 500 op-stream-shaped records: a kind tag, ids, a Lamport clock, a nested actor, a path array, short text, a non-whole float, a timestamp, a digest |
| `state` | the shape of the compute repository's incremental state: a scheme tag, 20,000 short tokens and a packed text of 20,000 integers |
| `floats` | 10,000 floats (Phase 367): fractions, negatives, small and large numbers in the `E` layout, and one in eight a whole double past 2^53, written as a 16- or 17-digit integer token. Those integer tokens are the only numbers whose parse re-renders the value it read (Phase 253's check) |

Each corpus is timed under four cases:

| case | what it calls |
|---|---|
| `escape` | `Json.escape` on every string and key the value carries |
| `render` | `Json.render` |
| `canon` | `Canon.render` |
| `parse` | `Json.parse` on the `render` text |

**Every agreement is asserted before anything is timed.** Each escaped string must parse back to
itself. The rendered text must parse back to the value. The canonical text must be a fixed point of
parse-then-canon. Each case's output, its length plus its FNV-1a (`Hash.fnv1a`), must equal the
fingerprint pinned in `Corpus.fs`. The suite holds the pins on .NET (`WireBenchCorpus`), so a
corpus edit that changes them is red. Such an edit is a new baseline: re-measure both hosts and say
so in the results file.

## How to run it on each host

**.NET alone:**

```powershell
pwsh ./benchmarks/run.ps1             # Release build, 10 samples per case
pwsh ./benchmarks/run.ps1 -Runs 15
```

**Both hosts.** This repository does not run the Fable compiler (DECISIONS.md D55; the suite's
`FableSmokeCompleteness` checks enforce it), so the node leg takes two commands. First, from a checkout
whose dotnet tool manifest provides `fable` (fuaran-dotnet has one), compile the harness to a scratch
directory outside every repository:

```powershell
# run from the toolchain owner's checkout, so its tool manifest resolves `fable`
dotnet fable <this repo>/benchmarks/Fuaran.Core.Wire.Benchmarks/Fuaran.Core.Wire.Benchmarks.fsproj -o <scratch>/benchmarks/Fuaran.Core.Wire.Benchmarks --noCache
```

Then hand the emitted entry point to the run:

```powershell
pwsh ./benchmarks/run.ps1 -Runs 15 -NodeEntry <scratch>/benchmarks/Fuaran.Core.Wire.Benchmarks/Program.js
```

`run.ps1` prints the machine, .NET, node and Fable versions. It then compares the two hosts'
fingerprints line for line and refuses to time anything if they differ. It prints the .NET table, the
node table and the node ÷ .NET table, which together are the body of a results file. Without
`-NodeEntry` it says the node leg was **not run**. A missing node figure is never reported as a pass.

**The clock leg alone** (in the gate it runs with the rest of the suite):

```powershell
dotnet run --project tests/Fuaran.Core.Tests -- --filter-test-list WireClockLeg              # the gate's Debug build
dotnet run --project tests/Fuaran.Core.Tests -c Release -- --filter-test-list WireClockLeg   # Release
```

Each attempt prints one `[wire-clock]` line with both sizes, the ratio, the bound and the calibration
readings. A run that prints no such lines measured nothing, whatever its summary says.

## How to read a ratio

- **node ÷ .NET** (harness): how many times longer node takes than .NET for the same case on the same
  machine. It compares hosts, not code versions. A speed-up that only one host feels moves this ratio.
- **A before/after figure** (harness): compare the same cell, from the same command, on the same
  machine, taken from two runs. Cells move between back-to-back runs (the Phase 364 results show
  cells moving by more than half), so a change smaller than its cell's spread is not evidence.
- **large ÷ small** (clock leg): how much longer an input eight times larger takes. Linear code reads
  about 8. Quadratic code reads 50 to 64. It is unaffected by a constant-factor change, because a
  slowdown that hits both sizes alike leaves the ratio where it was. That is why the leg cannot replace
  the tables.
- **Debug ratios guard the shape; they are not the speed figure.** The gate runs the suite as a Debug
  build, and the leg's bound is calibrated there. Debug and Release read the same shape: with the
  collector held off, linear reads about 8 in both. Debug times are two to four times Release's, so a
  Debug time is never quoted as a speed. The speed figure is the harness's Release and node table.

## Why the leg excludes the collector

The leg times every sample inside a no-GC region sized from the operation's own measured allocation,
and does not count a sample in which a collection happened anyway. Without this, the collector decided
the result:

- In Release, `Json.parse` at 20,000 → 160,000 tokens read a ratio from 14 to 43, bimodal from run to
  run. The same parses timed with no collection read 7.96. The parser is linear. The excess is the
  collector promoting the growing result: under workstation GC the per-token cost more than doubles
  between 5,000 and 10,000 tokens. Under server GC it stays flat.
- At 2,500 → 20,000, the first sizes tried, Release parse read anywhere from 8 to 32 because the small size sits
  below that knee and the large size above it. The leg's sizes are therefore 20,000 and 160,000,
  both past the knee.
- A bound wide enough for 14 to 43 could not see a quadratic. Once the collector is held off, the code's
  own shape can be bounded tightly.

The collector's real cost belongs in the harness tables, which do not exclude it. The Phase 364
results file records it: most of .NET's `state` parse time is the collector.

## The clock leg's bound

**20** on the large ÷ small ratio, for all three cases. Phase 364 proposed it; the operator SET it on
2026-10-05. The readings it rests on follow. They were taken on an i7-9700, 8 logical processors, on 2026-10-04, six
processes per configuration, each running the leg once.

| reading | escape | render | parse |
|---|---|---|---|
| quiet, Debug (the gate) | 7.99 – 8.17 | 9.33 – 9.57 | 8.40 – 8.70 |
| quiet, Release | 8.05 – 8.16 | 9.19 – 10.35 | 8.22 – 8.62 |
| busy, Debug (8 all-core burners) | 7.72 – 14.84 | 10.47 – 16.24 | 9.11 – 15.05 |
| busy, Release (8 all-core burners) | 8.02 – 8.27 | 8.91 – 15.52 | 8.85 – 15.50 |
| a deliberately quadratic escape, 4,000 → 32,000 characters (separate probe, best of five) | 50.6 | | |

- Render reads a little over 8 even when quiet, because its output buffer grows by doubling.
- Under the burners, the large size, whose working set outgrows the cache, suffers more than the
  small one, and the ratio inflates up to 16.24. That reading was red against a bound of 16. The
  three-attempt rule recovered it (13.63 on the next counted attempt). A sustained burner could hold
  all three attempts above 16.
- The calibration bracket did **not** flag the burners as saturation. The leg takes its baseline under
  the same load, and the load was steady. What the bracket catches is a load that changes during a
  window. The bound itself has to absorb steady load.

So 20 is about twice the worst quiet reading, which is the compute repository's rule for its own leg.
It is 1.2 times the worst busy reading, and 2.5 times below the quadratic the leg exists to catch.
Raising the bound buys tolerance of busier machines and loses sight of growth between n^1.4 and
n^1.6. Lowering it to 16 would make busy machines red on all three attempts.

**What the leg does on a busy machine.** Following the compute repository's leg (its Phases 282 and
285), every attempt is bracketed by readings of a fixed integer-chain workload. A window whose readings
show load is discarded, not counted. That means a reading above 1.25 times the leg's baseline, or two
consecutive readings more than 1.25 apart. A window where the collector could not be held off is
discarded too. Discarded windows are retried after a back-off of 2 s, doubling, at most 16 s. A case
is red only when all three counted attempts are red. After five discarded windows it fails with
**"machine saturated, no verdict"**. That is never a green, and it is not a timing red either: re-run
when the machine is quieter.
