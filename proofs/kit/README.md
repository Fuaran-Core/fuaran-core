# `proofs/kit/` — the proof leg, shipped once

A repository that wants an F\* proof leg needs the same nine things every time: a pinned prover, a
script that locates or downloads it, checks each model from a cold cache, extracts each model to F\#
and diffs the result against a committed oracle, runs the host families, and measures each module
against a declared budget and a declared cached-read threshold; two hand-written runtime shims the extractor's
output compiles against; a never-packed oracle project with the two settings that generated F\#
needs; a CI job; a claims ladder; and a cost declaration. Written out by hand each time, that is
four copies of one design and — the part that actually bites — **four prover pins, three of which
will be behind the day the fourth moves**, because F\* releases weekly.

This directory is that design, written once. This repository consumes it **in place**: its
`proofs/check.ps1` is a thin caller that declares what this repository has and hands it to
`check-proof-leg.ps1`. Another repository adopts it as **one versioned release asset**,
`proofs-kit-<version>.tar`, attached to this repository's `v<version>` release and pinned by sha256
in the adopter's own `proofs/proofs-kit.json` — the shape `fstar-pin.json` already gives the prover.
The adopter's `check.ps1` verifies the asset before anything runs and runs the engine from it.

**What it deliberately is: scripts and templates by versioned asset, models never copied** (Phase
536, `DECISIONS.md` D206). This line used to read "a package, a module, or a runtime dependency …
files copied by declaration and checked for drift". That was true of the engine and false of what
travelled with it: the runtime floor grows as models reach further into F\*'s library, a copied model
is a fork of a theorem, and two adopters' per-file copies were mostly stale within a month. So the
kit is not a package — nothing is built, referenced or restored — but it IS a release: an adopter runs
the kit this repository tagged, byte for byte, and moves to a newer one as a deliberate act whose
release notes say what moved. `asset.json` beside this file is the asset's contents.

**The kit also ships the theorems** (Phase 175), and since Phase 536 an adopter opens them
**read-only from the asset** and never copies one: the shared modules every model opens (`Twin`,
`ListAlgebra`), the generic theorems (`DagFold`, `DagClosure`, `Chain`, `WireCanon`, `WireDecode`,
`JsonParse`, `Limits`), the reference instance on the skeleton tree (`TreeOps`, `Skeleton`,
`Preservation`, `TreeDiff`), and `templates/Instance.fst.template`, which makes fold confluence at a
domain's own algebra a fill-in-the-holes act with exactly one hole that is an obligation. See
"Importing a theorem" below.

## What is here

| File | What it is | An adopter |
|---|---|---|
| `check-proof-leg.ps1` | **The engine.** Knows how to run a proof leg; knows nothing about which models a repository has. Takes the module list, the oracle host and the paths as parameters. Classifies every lost pass into one of the three verdicts below. | runs it from the asset |
| `templates/check.ps1` | The thin caller. Two declarations to edit at the top; nothing below them is per-repository — including the lower half that resolves, verifies and unpacks the pinned asset. | copies once, edits |
| `templates/oracle.fsproj.template` | The never-packed oracle project: `--strict-indentation-`, the FS0058/FS0064/FS1182 `NoWarn`, and the compile order the shims and models need. Named `.template` so no build or glob in a host repository can pick it up. | copies once, renames, edits |
| `templates/modules.json` | The cost declaration: the budget rule, the cached-read threshold, the contention threshold, and one worked entry. | copies once, edits |
| `LADDER.md` | **The ladder's schema**: every field of `proofs.json`, which are required, what each level's evidence is, and how names are matched. The page the template is written against, and the one any tool reading a ladder reads it by. | read |
| `templates/proofs.json` | The claims ladder: the closed level set, what each level means, and the host family that holds the rows to the tree. Goes at the **repository root**, not in `proofs/`. Written against `LADDER.md`. | copies once, edits |
| `templates/ci-proofs-job.yml` | The CI job, with the cache key that hashes the kit pin — which is the whole mechanism by which a kit or prover bump reaches CI with no second edit. | copies once, edits |
| `templates/Instance.fst.template` | The instantiation template (Phase 175): `../Skeleton.fst` with fourteen named holes. Drop the preamble, fill the holes, and the result is a domain's fold-confluence composite; the one obligation is `{{DIAMOND}}`, a proof of `independence_diamond` at the domain's own footprint and apply. Held to `../Skeleton.fst` byte for byte by the `Proofs.Kit` family, so the template and its first instance cannot drift apart. | instantiates |
| `extraction-post-pass.ps1` | **The extraction post-pass** (Phase 169): two functions, dot-sourced by the engine's EXTRACT stage, that re-indent a mutual type group's `and` to the column F\# expects and touch nothing else. See "The extraction post-pass" below. | runs it from the asset |
| `extraction-post-pass.tests.ps1` | Its go-red proof: four arms, two of which need the pinned prover and `dotnet` and are reported NOT RUN where either is absent. Also the machinery that answers the retirement condition. | runs it from the asset |
| `check-proof-leg.tests.ps1` | **The engine's refusals, held to their exit codes** (Phase 221). Runs the engine the way a caller does (`&`, in process) over a scratch proofs directory: a green control, then a missing host project, a host filter that cannot run and a refuted model, each of which must exit non-zero and never print `proofs: green`. Needs only the pin and the prover, never your models; `templates/check.ps1` calls it after a green leg. Its pin-resolution, mirror and oracle-guard arms (`R`, `M`, `I`) need no prover and run first; the rest report NOT RUN (exit 2) where there is no prover. | runs it from the asset |
| `templates/MutualTypes.fst` | The post-pass's fixture — the smallest model that makes the backend emit a mutual type group. **Not a template to instantiate**, and not to be registered in `$modules`: it earns no committed oracle. | runs it from the asset |
| `asset.json` | **The asset's contents** (Phase 536): every file the release asset carries, where it sits in the archive, and its role. The builder reads it and nothing else. | reads it |
| `build-kit-asset.ps1` | **The builder** (Phase 536): the archive, reproducibly — fixed order, epoch timestamps, mode 0644, owner 0:0, LF, uncompressed ustar — so its sha256 is a function of the kit's bytes alone. `-Check` holds the build to `copies.json`'s `proofsKit.sha256`; `-Update` rewrites it. The suite, the publish workflow and an adopter's offline path all call it. | builds an offline asset with it |

The asset also carries what used to be copied beside the kit, from `../`:

| In the asset | From | What an adopter does with it |
|---|---|---|
| `fstar-pin.json` | `proofs/fstar-pin.json` | Nothing: its `check.ps1` hands it to the engine. The kit version determines the prover, so a host's leg runs the prover this repository's leg ran. |
| `oracle/Prims.fs`, `oracle/FStar_Pervasives_Native.fs` | `proofs/oracle/` | **Commits** them in its own oracle directory (an oracle project compiles nothing outside it, Phase 399) — and the engine holds the committed files to the asset's bytes (`-FloorDir`), red on any difference, rewritten under `-Extract`. A floor is never a fork. |
| `models/<Model>.fst` | `proofs/<Model>.fst` | **Opens** them: the engine checks them read-only from the asset (`-ModelDirs`), extracts the ones its roster extracts into its own oracle like any model, and refuses a kit model's name found in the adopter's own tree. |

## The three verdict classes (Phase 166)

A leg that reports every lost pass as "did NOT verify" is not an evidence instrument in either
direction. Over 2026-09-14/15 three different things all read that way — a prover that died with
nothing printed, a prover killed under memory pressure, and a dependency's checked-module file
vanishing mid-run — and each cost a session twenty minutes of reading a whole log to establish that
nothing had been refuted. So a non-zero prover exit is **classified before it is reported**, and the
class decides the words, the exit code and whether anything is retried.

| Verdict | What happened | Exit | Retried? | What it obliges |
|---|---|---|---|---|
| **REFUTATION** — `<module>.fst did NOT verify` | The prover exited non-zero **and printed a diagnostic**: an error line, an error summary, `Failed to prove`, `Unexpected`, or a failing-quake line. | the **prover's** code (1 in practice) | **Never.** | Read the model. Something was refuted, or `--report_assumes error` caught an escape hatch. Re-running it to see whether it goes away is the habit this leg exists to make impossible. |
| **ABORT** — `<module>.fst ABORTED (no diagnostic)` | The prover exited non-zero and printed **nothing** about an undischarged query. Nothing was refuted; the prover died. | **3**, on the second abort | **Once**, in the same run, bounded at one retry per module per run and logged as a retry. | Read the machine, not the model. The pre-flight line above the abort says how many provers were running and how much memory was free. A module that aborts across legs is a finding about this machine, or about that model's memory appetite, and is worth a phase. |
| **APPARATUS** — `extraction of <module> hit an APPARATUS fault` | The leg's own machinery failed: at extract, a dependency's `.checked` file missing from the cache, or the cache directory gone. **Named**, so the reader starts from the file. | **4** | **Never** — the thing it needs is gone, so a second attempt asks the same broken apparatus the same question. | Find the second writer. The cache is per invocation and the script owns it, so something removed it underneath the run. |

Three details of that table are load-bearing rather than decorative, and each was measured against
the pinned prover rather than assumed:

- **The discriminator is the LOG, not the exit code.** A killed process and a refuted lemma are both
  "non-zero", and nothing about the number tells them apart. What the leg asks is whether the prover
  *said* anything about an undischarged query — and since F\* prints no per-query line for a query it
  discharged, a log with no diagnostic in it is a log in which every query the module printed was
  discharged. That is the same statement, read off the only evidence there is.
- **Both spellings of an error line are matched, because the prover uses one and the message formats
  use the other.** On the pinned release a diagnostic reads `* Error 19 at Foo.fst(8,39-8,41):`, not
  `(Error 19)`. A leg that knew only the parenthesised form classified a plain error as an ABORT and
  *retried* it — the exact inversion this verdict exists to prevent. The end-of-run
  `N errors were reported` summary is matched as a third, independent witness.
- **A failed extraction is not always a non-zero exit.** Removing a dependency's `.checked` file
  underneath an extraction makes F\* print `* Error 317: Cross-module inlining expects all modules to
  be checked first` and then **exit 0**; the fault surfaces two lines later as "extraction produced
  no `<module>.fs`", which names the symptom and nothing about the cause. So the engine decides
  failure as *non-zero exit **or** no file produced*, and only then classifies.

**A retry's clock is a WARM measurement and feeds neither gate.** The aborted attempt has already
half-filled the cache, so the retry is not a cold run; its line says so, it is compared to neither
the budget nor the cached-read threshold, and the leg's closing verdict names every abort that was retried and
passed — a green run that lost a prover and got it back is not the same evidence as one that did
not, and it should not take scrolling to find that out.

**Every prover invocation's whole output is teed** to `<WorkDir>/logs/<module>[.retry].<step>.log`,
and each verdict names the transcript it was read from, so a post-mortem reads the classification's
own evidence rather than a scrollback that is gone.

## Twin evaluation (Phase 309)

The extraction diff makes "the oracle is the model" a checked claim about TEXT. It says nothing about
whether the F\# the backend emits COMPUTES what the model means: a mis-extraction that compiles passes
every other step. Twin evaluation discharges that premise on sampled inputs, and it is opt-in —
`-Twins` on the engine.

- **In each extracted model**, a final section declaring a `let twins : list twin = [...]` of fixtures
  over the shared `Twin` module's record (`Twin.fst`, opened by every model since the reference
  repository's Phase 504, which also carries `twins_hold`) — each closure applies
  the model's own functions to a concrete input and compares the result with the value the model
  means — and `let _ = assert_norm (twins_hold twins == true)`. The prover's NORMALISER evaluates
  every fixture at the check step; a wrong expected value is a refutation like any other.
- **In the oracle**, the list is extracted with the model, so the host can run the very closures the
  normaliser evaluated. The adopting repository's host family evaluates each one against the
  extracted oracle and fails on a `false` — that is the F\# computing something other than the model
  on that input. (A top-level reader rather than a discriminator inside the list: the backend lays a
  `match` nested in a record literal out at column one, which F\# does not parse.)
- **In the engine**, step 2c reads each extracted model's source before the prover runs and refuses
  one that declares no `let twins` list asserted by `assert_norm (twins_hold twins == true)`, so a
  model added to the roster without fixtures fails rather than going quietly unsampled. A
  `-ProofOnly` model is exempt — it has no extraction to check.

The twins live in the model they sample rather than in one module beside all of them: a module
referencing every model would put every model in every cone a selector computes.

## The extraction post-pass (Phase 169)

**F\*'s F\# backend emits a mutual TYPE group that F\# 10 will not parse**, so an extraction carrying
one does not compile in the oracle project and the leg's step 2 holds the oracle to text nothing can
build. The backend breaks the group at the space before each `and`, which leaves the previous
declaration's last line with a trailing space and the `and` line with **one leading space**:

```fsharp
type node =
| Leaf of Prims.string
| Branch of attr          // <- trailing space
 and attr =               // <- one leading space; F# rejects this
| Flag of Prims.bool
| Nested of node
```

F\# reports `error FS0010: Unexpected keyword 'and' in member definition`, and it reports it **even
under the oracle project's `--strict-indentation-`** — which is the whole reason this is a separate
defect from the pre-F\#-8 match-arm layout that flag was relaxed for, and the reason a reader who
knows about the flag will otherwise assume it is covered.

So the engine runs a normalisation step between the extraction and the byte diff: every line whose
first token is the keyword `and` is re-indented to column 0, and **nothing else changes** — not the
trailing space on the line above it, not a line ending, not a byte anywhere else. The committed
oracles are the normalised text, so step 2's contract ("byte-identical to a fresh extraction") is
unchanged in meaning.

| | |
|---|---|
| **Observed on** | F\* **v2026.09.06** / Z3 4.13.3 — the release `../fstar-pin.json` declares |
| **Scope** | mutual type groups only: DU groups and record groups both, at any name length, always one space |
| **Not in scope** | a value `and`. The backend **hoists local mutual recursion to the top level**, so `let rec f … and g …` written inside an F\* function body extracts as two top-level bindings joined by a column-0 `and`, which the pass never sees |
| **Identity today** | no model in `proofs/` has a mutual type group yet — the three `$proofOnly` vocabulary models do, and are never extracted — so the pass moves no byte in any committed oracle. That is checked rather than claimed (arm B below) |
| **When it fires** | the leg prints a named line saying which module and how many lines. A silent rewrite between an extraction and the artefact it is diffed against is exactly the step that must never be invisible |

**The retirement condition, and how you will hear about it.** The pass exists because of an upstream
defect in a particular prover release. Bump the pin, run
`pwsh ./proofs/kit/extraction-post-pass.tests.ps1`, and arm C answers the question: if a fresh
extraction of `templates/MutualTypes.fst` no longer carries the defect, the script **fails by name**
with the retirement instruction rather than going quietly green — a go-red fixture that can no
longer go red is the signal to delete the machinery it guards. Retiring it means deleting the helper,
its call in the engine's EXTRACT stage, the fixture and that script, and re-extracting every oracle.

**Its four arms**, each named for the direction it can fail in:

| Arm | What it proves | Needs |
|---|---|---|
| **A. unit** | the pass repairs the bytes the backend emits, and leaves a column-0 `and`, an `and_then`, an `and` inside a literal, CRLF endings and trailing whitespace alone | nothing |
| **B. identity** | the pass is the identity on **every** committed oracle | nothing |
| **C. go red** | the RAW extraction of the fixture fails to compile, **with FS0010 at the `and` line** — a failure for the wrong reason is not evidence | the pinned prover, `dotnet` |
| **D. go green** | the same extraction, with the pass run over it, compiles | the pinned prover, `dotnet` |

C and D compile against a scratch project **derived from the oracle project itself** — its target
framework, its `NoWarn`, its `--strict-indentation-` and its package references, with only the
compile list rewritten — so what they prove is a statement about your oracle project and not about a
second one that resembles it. Where the prover or `dotnet` is absent they are reported **NOT RUN**
with the remedy, never skipped quietly: "nothing to check" must not read as "everything checked".

## A cost finding that names a contended pass (Phase 171)

A budget overshoot has two entirely different causes — **this module got more expensive**, or **this
machine was busy** — and the leg used to print the same sentence for both. Three recorded instances,
all of them the second kind: `Chain` overshot twice in seven runs and came in at 16–25s on the other
five, untouched by any phase since it was budgeted; a pass measured `TreeOps` at 145s while inflating
three untouched modules by the same factor, and the phase had to depart from the seeding rule by hand
and write a paragraph explaining why; and `Capability` measured 75s against a 20s budget on a run
contended by three sibling gates. A reader of any of those logs cannot tell them from a regression.

So at the end of every **run** the engine reports a **contention factor** — the median, over the
modules the working tree did not change, of what each just cost divided by the `measuredSeconds` its
budget entry records — and labels every cost finding from a run above the budget file's
`contentionSeeding.threshold` a CONTENDED PASS, which is **not a re-seed obligation**:

```
==== proofs: contention — run 1 of 1, x0.29 over 16 untouched module(s), at or under the x0.80 threshold: an ordinary pass. A cost finding on this run is about its module.
==== proofs: contention — run 1 of 1, x0.29 over 4 untouched module(s), ABOVE the x0.20 threshold: this was a CONTENDED pass. 1 cost finding(s) on this run carry the label, and a labelled finding is NOT a re-seed obligation.
     ColumnOps.fst took 12s against its 8s budget on run 1 of 1 — 4s over, 150% of budget — CONTENDED PASS (x0.29 against a x0.20 threshold): the modules this tree did not change ran x0.29 of their recorded measurements on this run, so this figure measures the afternoon and not the module
```

(the first from a quiet cold pass of the reference repository's leg, the second from a probe that
lowered the threshold to 0.20 and one budget to 8s). **The explanation lives once, in the engine's
header** (`check-proof-leg.ps1`: THE CONTENTION FACTOR, WHAT THE NUMBER'S SCALE ACTUALLY IS, and its
sections 2b and 3c): why the neutral point sits well below one and the threshold must be seeded from a
measured quiet pass and a measured contended one, why nothing is multiplied into a measurement, how
the untouched set is derived and where that stops, why a module too cheap to time does not vote
(`minimumSeconds`, `minimumSamples`), why an absent `contentionSeeding` block is not a finding, and
why an entry's optional `contentionFactor` is provenance only. Every run's factor and label are in
its run verdict (`-SummaryFile`).

## Cost is recorded, a cached read is refused (Phase 399)

An operator ruling of 2026-10-08 separated the two questions `-Strict` used to conflate.

- **A budget overrun is never a red leg**, with `-Strict` or without it. It is a slow cold check,
  the opposite of the failure a gate is for. It is printed as a `COST` finding, and the run verdict
  (`-SummaryFile`) records every module's time against its budget, with the percentage, so a strict
  record names the over-budget modules and a scheduled strict run can trend them. Do not raise a budget to
  quiet a finding, and do not delete budgets: they are the trend's reference. Re-seeding is still the
  recorded act it always was.
- **The hard gate is "this was not a real cold verification".** The cache provenance check (Phase
  402) is the primary signal. Its backstop, for a writer active only DURING one invocation, is one
  absolute number in the budget file: `cachedRead.thresholdSeconds`. A check that finishes under it
  fails the leg as a probable cached read, before its green line is printed. It applies to the
  modules whose recorded `fastestSeconds` is at least `cachedRead.appliesFromFastestSeconds`. A
  module that genuinely checks in under a second cannot be told from a cached read by the clock,
  so it rests on the provenance check alone, and the leg's start-up line names those modules.
- **Seed the threshold from a measurement.** Cold-check a few modules of different sizes into one
  cache, re-run each against it with the leg's flags, and set the threshold to about twice the
  slowest read. Set `appliesFromFastestSeconds` to about three times that. The reference repository
  measured 0.18-0.51s for every cached read, against cold checks of 0.4s to 184s; its
  `proofs/README.md` section "Cached read or cold check" has the table.
- **The per-module floors are retired** (`floorSeconds`, `floorSeeding`, `floorSeeding.os`, and
  `-NoFloor`), and a budget file still carrying them is refused by name: delete `floorSeconds` and
  `floorSeeding`, keep each entry's `fastestSeconds`, add the `cachedRead` block, and move
  `zeroBelowSeconds` to `contentionSeeding.minimumSeconds`. Why one threshold replaces them is the
  engine header's (step 1).
- **What `-Strict` still turns red:** a coverage or shape finding, which is a gap in the declaration
  rather than a measurement. Examples are a module with no budget, a budget for a module the leg does
  not check, and a budget file with no `cachedRead` block.

## The oracle is independent of production (Phase 399)

This is the paragraph an adopting repository copies into its own proofs README, unchanged but for
the project name:

> **The oracle cannot see production.** A differential test compares the extracted model with the
> production code over the same inputs, and it is worth something only if the two sides are
> independent. An oracle that referenced a production assembly, compiled a production source, or
> opened a production namespace would pass every differential case by comparing production with
> itself, and the claims ladder would still say "differentially tested". So after extraction and
> before the host step, the leg refuses an oracle project that, as MSBuild evaluates it, carries any
> `ProjectReference`, any `Reference`, a `PackageReference` other than `FSharp.Core`, or a `Compile`
> item outside the oracle directory, and an oracle source that `open`s, abbreviates or qualifies a
> name whose first segment is not `Prims`, an `FStar` module or one of the oracle's own modules (the
> hand-written runtime floor may also name `System` and `Microsoft.FSharp`, which is what it defines
> the primitives as, and a type of a `System` or `Microsoft.FSharp` namespace it opens, unqualified,
> when the name resolves to a type there). Each refusal names the file, the line and the text. Production is opened by
> the test host alone.

Why it is here. Chakraborty et al., *Towards Neural Synthesis for SMT-Assisted Proof-Oriented
Programming* (ICSE 2025), name two ways a proof harness reports success falsely: an escape hatch
(`admit`, `assume`), which this leg has always refused with `--report_assumes error`, and a definition
that closes only because the original it was meant to reproduce is still in scope. Under F\* the
second cannot happen, because no production F\# is in scope there. The differential half is where
it can: the oracle project is compiled beside production, and before this guard nothing stopped an
agent-written oracle from calling the function it was meant to model. No oracle here did. The guard
makes that a checked claim rather than an observed one.

How it reads. The project half evaluates the project (`dotnet msbuild -getItem`), so a reference
inherited from a `Directory.Build.props` counts as much as one in the project file; it is what
makes production unreachable. The source half is lexical: comments and string and character
literals are blanked first, so a production name in prose or in a message is not a finding, and a
lower-case first segment is a value's member access and is not judged. A name the source half
refuses would not compile anyway once the project half holds; the source half exists to name the
line that tried.

`-GuardOnly` runs the guard alone and needs no prover. The `I` arms of `check-proof-leg.tests.ps1`
plant each kind of breach in a scratch oracle and hold the refusal and the named line; the `Q` arms
plant one inside a full leg and show it fails after extraction and before the host step.

## Adopting it

1. **Pin the asset.** Write `<repo>/proofs/proofs-kit.json` — the release you adopt, its asset, its
   url and its sha256 (the shape is under "Pinning the kit" below).
2. **`templates/check.ps1` → `<repo>/proofs/check.ps1`.** Edit the two declarations: the host
   families and where the host project lives. Everything below them — resolving and verifying the
   asset, running the engine from it with the asset's pin, models and floor — is the template's.
3. **`templates/oracle.fsproj.template` → `<repo>/proofs/oracle/<Your>.Proofs.Oracle.fsproj`**, and
   add it to your solution. Keep `IsPackable` false: nothing extracted from a model belongs in a
   shipped assembly. Run the leg once with `-Extract` to write the runtime floor beside it.
4. **`templates/modules.json` → `<repo>/proofs/modules.json`.** One entry per model, the kit models
   you open included. Seed the numbers from real runs, per the rules the file carries; do not invent
   them, and do not seed from a convenient half of the data.
5. **`templates/proofs.json` → `<repo>/proofs.json`** (the repository root). Write the ladder, and
   then write the host family that holds it to your tree — the kit ships the leg that runs that
   family, not the family itself, because what a row must be held to is a property of your tree.
6. **`templates/ci-proofs-job.yml` →** your workflow. Keep the cache key hashing the kit pin.
7. **Gitignore `proofs/.fstar/`, `proofs/.kit/` and `proofs/obj/`.** The prover install, the
   materialised asset and the checked-module cache are not source.
8. **Write your first model**, run `pwsh ./proofs/check.ps1`, and commit the oracle it extracts.
9. **Declare no copy.** Nothing of the kit belongs in your `copies.json`: the templates are yours
   once copied, and the rest is the asset, verified by sha on every run.

## Importing a theorem (Phase 175)

An adopter that wants a theorem rather than only a leg — fold confluence at its own witness, which is
what the three named adopters want — does three things, and the leg above is what checks
the third. Nothing in it is a package, a reference or a copy: the models are opened from the pinned
asset, with a template where the leg had a caller.

1. **Open, do not copy.** The generic model the theorem lives in — for fold confluence that is
   `DagFold`, the one model every instance needs — and the reference instance you will instantiate
   against (`TreeOps`, `Skeleton`) are in the asset under `models/`. Add each to your roster; the
   engine checks it read-only from the asset. A theorem that moves here reaches you as a release,
   named in its notes, when you move your pin — never as a stale copy you did not know you had.
2. **Write the instance.** `templates/Instance.fst.template` → `<repo>/proofs/<Instance>.fst`. The
   contract is the preamble's three rules: drop everything through the
   `(* ==== END OF PREAMBLE ==== *)` line, replace every `{{HOLE}}` — the same hole takes the same
   value everywhere it occurs — and leave no `{{` behind; an unfilled hole is a refusal, not a
   default. Fourteen holes: twelve identifiers, two prose blocks, and exactly **one obligation** —
   `{{DIAMOND}}`, a lemma in your domain module of type
   `unit -> Lemma (independence_diamond #op #state #rej footprint apply)`. That is the fold
   theorem's one hypothesis about the domain, and it is yours to prove: the template fixes
   everything else, and a fixed line you find you must change is a template defect to send back,
   not a fork to keep. The values that make `../Skeleton.fst`, which the `Proofs.Kit` family holds
   the template to:

   | Hole | `Skeleton.fst` | Yours |
   |---|---|---|
   | `MODULE` | `Skeleton` | the instance's module name |
   | `DOMAIN` | `TreeOps` | the module carrying your algebra, opened beside `DagFold` |
   | `OP` / `STATE` / `REJ` | `op` / `tree` / `rejection` | your operation, state and rejection types, as `DOMAIN` names them |
   | `APPLY` | `wapply` | `OP -> STATE -> outcome STATE REJ` — the GUARDED apply where your apply is only lawful on well-formed states; the guard is where an input invariant is stated rather than assumed |
   | `FOOTPRINT` | `op_fp` | `OP -> footprint` |
   | `FOLD` | `skeleton_fold` | the instance's fold; the theorems are `<FOLD>_confluence` and `<FOLD>_confluence_halt` |
   | `DIAMOND` | `op_independence_diamond` | **the obligation** — your proof of `independence_diamond` at `FOOTPRINT` and `APPLY` |
   | `PROD_APPLY` / `PROD_FOOTPRINT` / `PROD_FOLD` | `Ops.apply` / `Ops.footprint` / `FoldConfluence.foldOnce` | the production names your comments cite; prose only |
   | `HEADER` | the opening comment's interior | what your composite says, which boundaries remain and where each is stated, the licence line |
   | `TRAILER` | `batch_lanes_fold` and its comment | your own evidence after the theorem — keep a non-vacuity witness here, or nothing goes red when your alphabet is quietly narrowed |

   **What the template does not give.** The apply-engine preservation theorems (`../Preservation.fst`),
   the diff's (`../TreeDiff.fst`) and the tree algebra's own diamond (`../TreeOps.fst`) are stated
   ABOUT the skeleton-op tree algebra, not generically over a domain. A domain whose state IS that
   tree under those operations inherits all of them by opening the asset's models — it instantiates
   nothing and copies nothing. A domain with its own algebra models its own apply, and `{{DIAMOND}}` is
   the theorem it proves about it before the template applies; `../Preservation.fst` is the shape to
   model preservation against, not a template for it.
3. **Check.** Add the shared `Twin` and `ListAlgebra` (which `DagFold` opens), your domain module and
   the instance to the roster in `<repo>/proofs/modules.json`, in
   dependency order (a model follows what it opens), one entry each with its `kind`, its `opens` and
   its budget, `pwsh ./proofs/check.ps1`, and commit the oracle it extracts. The leg
   checks the instance from a cold cache like any other model and diffs its extraction like any
   other; nothing about it is special, which is the point.

**What your `proofs.json` rows then say.** One `proved` row per theorem the instance yields —
`<FOLD>_confluence` and `<FOLD>_confluence_halt` — with `evidence.theorem` naming it and
`evidence.model` naming your instance file. The obligation you discharged is a `proved` row at YOUR
domain, `evidence.theorem` your `DIAMOND` lemma in your domain module: this repository's ladder
carries `independence-diamond` as `assumed` / `domain-obligation` because the fold theorem is
generic, and at an instance it is what `tree-independence-diamond` is here. No lane-set row: since
Phase 536 the composite has no `requires`, so an instance cut before it, whose ladder carried
`lanes-apply` as an `assumed` / `domain-obligation` row, re-instantiates and retires that row — the
theorem under the same name is now stronger. A guarded `APPLY` carries an
`assumed` / `domain-obligation` row for its input invariant, `dischargedBy: Conformance.opAlgebra`,
as `tree-algebra-well-formed-states` does here. And every `model-bridge` and `premise` row this
ladder carries about a kit model you open is a row you inherit — opening `Chain` inherits
`content-id-determines-content`. A `proved` row about a kit model names it by its archive path,
`evidence.model: "kit:models/<Model>.fst"`: the model is not in your tree, so your ladder family holds
the row to your roster always, and to the model's text when `FUARAN_PROOFS_KIT_DIR` names the verified
asset — which the caller template sets for the leg's host step, so the leg's run of the family is the
one that reads the theorem. The kit ships the leg that runs your ladder family; what each row is
held to is a property of your tree, and the family is yours to write.

## Pinning the kit (Phase 536)

The kit an adopter runs is named by ONE file in the adopter, `proofs/proofs-kit.json`, in the
`fstar-pin.json` shape:

```json
{
  "kind": "proofsKitPin",
  "kit": "0.40.0",
  "asset": "proofs-kit-0.40.0.tar",
  "url": "https://github.com/Fuaran-Core/fuaran-core/releases/download/v0.40.0/proofs-kit-0.40.0.tar",
  "sha256": "<the 64 hex digits this repository's copies.json commits as proofsKit.sha256>"
}
```

`check.ps1` (the template's lower half) reads it before anything else runs: it downloads the asset
from `url` into `proofs/.kit/` — or takes the local file `-KitAsset <path>` (or the
`FUARAN_PROOFS_KIT_ASSET` environment variable) names — computes its sha256, **refuses on the spot**
when it is not the pin's, naming the source and both hashes, unpacks it fresh, and runs the engine from
it with the asset's `fstar-pin.json`, models and floor. The local file is the OFFLINE path and nothing
else: build the asset from a checkout of this repository
(`pwsh proofs/kit/build-kit-asset.ps1 -OutDir <dir>`) and pass the file; the sha is verified all the
same, so an override can only ever be the pinned kit, delivered another way.

Where the sha comes from: this repository commits it, in `copies.json` `proofsKit.sha256`. The
`Proofs.Kit` family builds the asset and fails when the build disagrees (a kit file moved without the
committed sha moving with it — `build-kit-asset.ps1 -Update`, in the same commit), and the publish
workflow refuses to upload, on a `v*` tag, an asset whose sha is not that one. The version names the
file and is not inside it, so a release that moves no kit file carries the same sha as the last.

Moving the pin is the adopter's deliberate act: a new `kit`, `asset`, `url` and `sha256`, a cold run
of the leg, and whatever the release notes say a kit change asks of an adopter (a template moving, a
theorem strengthened). Nothing goes stale in between, because nothing is a copy.

**The pin's mirror (Phase 399).** An entry may carry a `mirror` beside its `url`: the same asset at a
second address. The kit tries `url`, then `mirror`. A source that cannot serve the archive is
reported and the next is tried. A source that serves bytes that do not match the entry's `sha256` is
refused on the spot, naming the source and the pin's hash, and the other source is not tried: a
mismatch is a replaced asset, not an outage. A mirror must name the pin's release and end in the
entry's asset name, and the kit refuses one that does not. `file://` sources work too, so a mirror can
be a file share. `-ResolveOnly` prints both sources in order; `-FetchOnly` fetches and verifies the
archive and stops. In this repository the mirror is a `prover-fstar-<release>` release of the
repository itself, never a `v*` tag, so attaching assets to it cannot fire a publish. A pin bump
therefore has one more step: before the bump lands, download each new asset from upstream, check it
against the sha256 you are about to commit, and attach it to a new mirror release:

```powershell
$release = 'v2026.09.06'                      # the pin's `fstar`
gh release create "prover-fstar-$release" --repo <owner>/<repo> --target main --latest=false `
    --title "Pinned F* $release (proof-leg mirror)" --notes "The proof leg's pinned prover, mirrored. See proofs/fstar-pin.json"
gh release upload "prover-fstar-$release" --repo <owner>/<repo> <each downloaded, hash-checked asset>
```

**The run verdict (Phase 522).** `-SummaryFile <path>` makes the leg write ONE JSON record of the
run on EVERY exit, green or red: `kind: proofRunVerdict`, the `verdict` the exit code encodes, named
(`green`, `refuted`, `aborted`, `apparatus` or `failed`), the `exit` and the red `message`, and what
the run was — strict or not, the runs, the pinned prover, the modules, whether the host step ran, the
machine (never a local host name, because callers commit what they record), each run's contention,
every module's cost against its budget, the cost findings and the retried aborts. The engine's header
("THE RUN VERDICT") is the field list. A caller reads the record, never the transcript: this
repository's `check.ps1` writes every run's to `proofs/obj/last-run.json` and records a green strict
full run's as the empty cone's baseline, and its scheduled strict workflow summarises the run from it.
The refusal tests hold every arm's record to the leg's exit code. Pass it from your own `check.ps1`
(the caller template does).

## What this kit does not solve

A pin is still a pin. The asset tells an adopter exactly which kit it runs; it does not move the
adopter to a newer one, and it cannot say whether a newer one matters to it — that is what the
release notes are for. The trade: an adopter gets a proof leg for the cost of one pinned file, with
no package, no reference and no per-file copies to drift, and pays for it by making the upgrade a
named, visible act instead of an automatic one.
