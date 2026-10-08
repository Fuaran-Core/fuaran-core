#Requires -Version 7.0
# THE PROOF LEG, parameterised — the reusable half of `proofs/check.ps1` (Phase 155).
#
# This file is the KIT's engine. It knows how to run a proof leg; it knows nothing about which
# models a repository has, where its oracle host lives, or what its project is called. A
# repository's own `proofs/check.ps1` is the thin caller that supplies those (see
# `templates/check.ps1` beside this file), and it is the caller — not this script — that holds the
# module list, so the list stays where a reader and a sibling session expect to find it.
#
# EVERY MODULE in -Modules goes through the same three steps, and each step can fail on its own.
#   1. CHECK  — <ProofsDir>/<Module>.fst is verified by the PINNED F*/Z3 (-PinFile) with every SMT
#               query proved -Quake times over varying seeds and every escape hatch (assume /
#               admit) reported as an error. -Runs N repeats the whole check N times from a cold
#               cache — CI asks for 3, which is exit criterion 1 made literal. A run checks EVERY
#               module before the next run starts, so -Runs still means "N cold-cache
#               verifications of everything", as it did when there was one model.
#               Each module's wall clock is MEASURED AGAINST A DECLARED BUDGET (Phase 148):
#               -BudgetFile says what each module is expected to cost, every green line prints
#               the measured seconds beside that budget, and an overshoot is a named COST warning
#               rather than a failure — prover time varies by machine and by load, so the budget
#               is a smoke detector and not a gate. Since Phase 399 that holds under -Strict too:
#               an overshoot is a slow COLD check, the opposite of the failure a gate exists for,
#               so it is a recorded finding (printed, written to -SummaryFile with its percentage)
#               and never a red leg. -Strict promotes only the COVERAGE and SHAPE findings — a
#               module with no budget, a budget for no module — which are defects in a declaration
#               rather than measurements. A fixed CI job timeout is deliberately NOT what this is:
#               a timeout says a run died and nothing about which module.
#               The clock IS a gate in the other direction, and only there (Phase 399): a module
#               that verifies in less than the budget file's CACHED-READ THRESHOLD
#               (`cachedRead.thresholdSeconds`) FAILS the leg on the spot as a probable cached
#               read. Measured on the pinned prover, reading a checked module back from a populated
#               cache costs F* start-up and deserialisation and nothing else — 0.18-0.51s across
#               ten modules whose cold checks took 0.4s to 184s — so one absolute number separates a
#               read from a check for every module whose genuine cold check is well above it. Which
#               modules that is, is read off each entry's recorded `fastestSeconds` (the threshold
#               applies at `cachedRead.appliesFromFastestSeconds` and above); a module that can
#               genuinely check in under a second is guarded by the provenance check alone. This
#               replaces the per-module FLOORS of Phases 164 and 402, which answered the same
#               question with one number per module per OS and broke on the first faster machine.
#               The second writer the threshold backstops is caught DIRECTLY, on every OS and
#               whatever the clock says, by the CACHE
#               PROVENANCE check (Phase 402): between two of this run's prover invocations nothing
#               in the cache may appear, change or vanish, and a module's own `.checked` file may
#               not be in the cache before its cold check starts. Either is a refusal naming the
#               files — the 2026-09-14 incident (another run's `.checked` files found by this one)
#               is exactly both. A file's state is its length and the hash of its bytes, never its
#               timestamp.
#               The cache the cold runs use is PER INVOCATION (<WorkDir>/cache-<pid>, or
#               -CacheDir), created and removed by this script, so that "cold cache" cannot be
#               quietly falsified by another run in the same worktree. See "Running it" in the
#               adopting repository's proofs README for the 2026-09-14 incident that bought both.
#   2. EXTRACT — each checked model is extracted to F# and DIFFED against its committed oracle
#               (<OracleDir>/<Module>.fs). A difference fails: the oracle the suite runs must be
#               the model the theorem is about, byte for byte. -Extract overwrites the committed
#               files with the fresh extractions instead (then commit them). A model named in
#               -ProofOnly is EXEMPT and says so on its own line: an oracle exists so a
#               differential can run the extracted model beside the production code, so a model
#               with no production code to run beside earns no oracle, and committing one would
#               commit generated F# that nothing compiles, calls or compares. The exemption is
#               the caller's to declare and to justify — what step 2 buys for the other models
#               ("the artefact is the model, byte for byte") an exempt one must get some other
#               way, one level further up, and the caller says where.
#   2c. TWIN  — optional, and switched on by -Twins (Phase 309). Step 2 makes "the oracle is the
#               model" a checked claim at the TEXT: the committed F# is what the extractor emits. It
#               says nothing about whether that F# COMPUTES what the model means — a mis-extraction
#               that compiles passes every other step. Twin evaluation closes that on sampled
#               inputs. Each extracted model ends with a `twins` list — records `{ tname; tholds }`
#               whose `tholds` closure applies the model's own functions to a fixture and compares
#               the result with an expected value — and asserts by NORMALISATION that every closure
#               is `true` (`assert_norm (twins_hold twins == true)`), so the prover itself evaluated
#               each fixture under the model's semantics at step 1. The list is extracted with the
#               rest of the model, and the caller's host step runs the extracted closures against the
#               extracted oracle: a closure that comes back `false` there is the F# disagreeing with
#               the normaliser on that input. The twins live in the model they sample, not in one
#               module beside all of them, so a cone run still checks only what a change reaches.
#               What THIS step adds is COVERAGE, read off each source before the prover runs: every
#               model the leg extracts must declare `let twins` and assert it by normalisation, so a
#               model added to the roster without fixtures fails here rather than going quietly
#               unsampled. A -ProofOnly model is exempt — it has no extraction to check.
#   2d. GUARD — the ORACLE-INDEPENDENCE guard (Phase 399, section 4b), after extraction and before
#               the host: the oracle project, as MSBuild evaluates it, may reference FSharp.Core and
#               compile its own directory and nothing else, and its sources may name only `Prims`,
#               `FStar` modules and their own modules. A differential over an oracle that reaches
#               production compares production with itself; the leg refuses one, naming each line.
#   3. HOST   — the -HostFilters the caller declared, each its own invocation of the host test
#               project (-HostProject / -HostProjectFile) with its own failure message. Separate
#               invocations rather than one prefix filter, so two failures read as what they are
#               rather than as one red suite. -SkipOracleHost leaves them all to the repository's
#               own gate, which already runs the whole suite.
#
# THE THREE VERDICT CLASSES (Phase 166). A leg that reports every lost pass as "did NOT verify" is
# not an evidence instrument in either direction: on 2026-09-14/15 three different things all read
# as a refutation, and each cost a session twenty minutes of reading a whole log to establish that
# nothing had been refuted. So a non-zero prover exit is now CLASSIFIED before it is reported, and
# the class decides the words, the exit code and whether anything is retried.
#
#   REFUTATION — the prover exited non-zero AND printed a diagnostic: `(Error NNN)`, `Failed to
#               prove`, `Unexpected`, or a quake line reporting a failure. Something was refuted,
#               or an escape hatch was reported by --report_assumes error. Reported as
#               `<module>.fst did NOT verify`, exactly as before, with the PROVER's exit code.
#               NEVER retried: a refutation is a result, and re-running it to see whether it goes
#               away is the habit this leg exists to make impossible.
#   ABORT     — the prover exited non-zero and printed NO such diagnostic. Nothing was refuted;
#               the prover died. (Phase 162's worker watched WireDecode.fst and JsonParse.fst —
#               modules it never touched — fail with no error, warning or exception anywhere in
#               the log, and both retried green. Phase 149's saw fstar.exe killed mid-Preservation
#               at a different lemma each time, with free memory under 3 GB and six provers on the
#               machine.) Reported as `<module>.fst ABORTED (no diagnostic)` and RETRIED ONCE in
#               the same run — once per module per run, bounded, and logged AS a retry so its
#               timing is never read as a cold measurement. A second abort of the same module in
#               the same run fails the leg with exit $ExitAbort, which is not a refutation's code.
#   APPARATUS — the leg's own machinery failed, not the model: at EXTRACT, a dependency's
#               `.checked` file missing from the cache (F* error 317 — Phase 155's worker watched
#               the per-invocation cache empty mid-run), or the cache directory gone. Reported as
#               an APPARATUS fault NAMING the file, with exit $ExitApparatus. Not retried: the
#               thing it needs is gone, so a second attempt asks the same broken apparatus the
#               same question.
#
# The discriminator between the first two is the LOG, not the exit code, because a killed process
# and a refuted lemma are both "non-zero" and nothing about the number tells them apart. It is
# stated where it is computed — see `Test-ProverDiagnostic` in section 1b — and its whole content
# is: did the prover SAY anything about an undischarged query. F* prints no per-query line for a
# query it discharged, so a log with no diagnostic in it is one in which every query the module
# printed was discharged; that is the same statement, read off the only evidence there is.
#
# THE CONTENTION FACTOR (Phase 171). A budget overshoot has two entirely different causes — this
# module got more expensive, or this machine was busy — and until now the log said nothing about
# which one a reader was looking at. So at the end of every RUN the leg reports one number: the
# median, over the modules this working tree did NOT change, of what each cost divided by the
# `measuredSeconds` its budget entry records. An untouched module's cost is a fact about the
# machine, so a run in which all of them came in at 1.7x their recorded measurements is a run in
# which the machine was 1.7x slower, whatever any single line says. Above the threshold declared in
# the budget file's `contentionSeeding` block, every cost finding from that run is LABELLED a
# contended pass; a labelled finding still prints, still warns, and is explicitly NOT a re-seed
# obligation, while -Strict promotes only the UNLABELLED ones. Nothing is multiplied into a
# measurement: the seconds a green line prints stay the wall clock, and the factor sits beside
# them. Section 2b carries the argument and the limits; section 3c computes it.
#
# WHAT THE NUMBER'S SCALE ACTUALLY IS, because it is not the obvious one and the threshold depends
# on it (measured, Phase 171, on the pinned prover). `measuredSeconds` is not a typical cost — by
# the budget file's own seeding rule it is the SLOWEST cold run ever observed for that module, and
# most of this repository's were seeded under several concurrent sessions. So the ratio's neutral
# point sits well BELOW one: a quiet pass of this leg measured x0.29, not x1. A threshold picked as
# though 1.0 meant "normal" would therefore sit above any contention this leg can experience and
# would never fire — the "detector that cannot fire" the budget file's own TreeOps note warns
# about. The threshold is seeded from a measured quiet pass and a measured contended one, in that
# block, and re-seeding it is the same recorded act as bumping a budget.
#
# Every prover invocation's whole output is also TEED to <WorkDir>/logs/, so a post-mortem reads
# the classification's own evidence rather than a scrollback. And every run prints a PRE-FLIGHT
# line — concurrent fstar process count, free physical memory — so a contended machine can be told
# from a broken model without asking anyone who was there.
#
# The prover is resolved from $env:FSTAR_HOME (a release directory holding bin/fstar.exe), else
# from <ProofsDir>/.fstar/ (a previous install by this script), else DOWNLOADED from the pinned
# GitHub release — or, when that source cannot serve it, from the entry's `mirror` (Phase 399,
# section 1a) — hash-verified, and unpacked there. Both directories, and <WorkDir>, are expected
# to be gitignored by the adopting repository.
# The pin file carries ONE ENTRY PER OPERATING SYSTEM (Phase 393: `windows` and `linux`), every
# entry the same release. The download path resolves the entry by `$IsWindows` / `$IsLinux` /
# `$IsMacOS`, REFUSES an OS with no entry by name (exit 2), and refuses an entry that is incomplete
# or names a different release than the pin's `fstar` — an entry left behind by a pin bump would
# otherwise fetch, hash-verify and run the OLD prover on that OS only. The archive is unpacked by its
# own extension (`.zip` or `.tar.gz`); every release lays out `fstar/bin/fstar.exe` with the bundled
# Z3 under `fstar/lib/fstar/z3-<v>/`, on every OS. FSTAR_HOME still overrides the download on any
# OS, and the pin's version check still applies to whatever it names. `-ResolveOnly` prints the
# entry this host would fetch and stops before any download, so the resolution is testable without
# a prover; `-Platform` names the OS to resolve for instead of the host's.
#
# Public behaviour is the contract. Every line this script prints, and every exit code it returns,
# is what the pre-kit `check.ps1` printed and returned — a repository's tests may parse them.
[CmdletBinding()]
param(
    # The models, in the order they are to be checked. The CALLER owns this list.
    [Parameter(Mandatory)][string[]] $Modules,
    # Models that are CHECKED but not EXTRACTED — see step 2 in the header. A narrow, declared
    # exemption, never a default.
    [string[]] $ProofOnly = @(),
    # The directory holding the .fst models. Everything else defaults relative to it.
    [Parameter(Mandatory)][string] $ProofsDir,
    # The repository root the host step runs from. Defaults to the parent of -ProofsDir.
    [string] $RepoRoot,
    # The pinned prover declaration. Defaults to <ProofsDir>/fstar-pin.json.
    [string] $PinFile,
    # The per-module cost budgets and the cached-read threshold. Defaults to <ProofsDir>/modules.json.
    [string] $BudgetFile,
    # The committed extractions the fresh ones are diffed against. Defaults to <ProofsDir>/oracle.
    [string] $OracleDir,
    # Where the checked-module cache and the fresh extractions go. Defaults to <ProofsDir>/obj.
    [string] $WorkDir,
    # The host test project, as a path relative to -RepoRoot: the directory `dotnet run --project`
    # takes, and the .fsproj `dotnet build` takes.
    [string] $HostProject,
    [string] $HostProjectFile,
    # One entry per host invocation: @{ Filter = '<Expecto filter>'; Failure = '<message on red>' }.
    # An empty list means there is no host step, which is a legitimate shape for a repository whose
    # models have no differential yet.
    [hashtable[]] $HostFilters = @(),
    # TWIN EVALUATION (Phase 309; header step 2c): every extracted model declares `twins` and
    # asserts them by normalisation. Off means the caller runs no twin evaluation, which the leg
    # says out loud.
    [switch] $Twins,
    # The prover flags the leg is defined by. Defaults are the values the leg was cut with.
    [int] $Quake = 3,
    [int] $ZRlimit = 40,
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [string] $CacheDir,
    [int]    $Runs = 1,
    # Phase 402 — THE KIT'S OWN TEST SEAM, and nothing else: a script block run after each prover
    # invocation, once its cache state is recorded and before the next one is checked against it —
    # exactly where a second writer would act. It is called with the module, the run and the cache
    # directory. `check-proof-leg.tests.ps1` plants its second writer here, synchronously, so the
    # refusal it asserts cannot depend on scheduling. A caller that names nothing is unaffected.
    [scriptblock] $AfterInvocation,
    # Phase 399 — the seam's other half, for the same tests and nothing else: a script block run
    # after the cache provenance check has passed for a module and before the prover is started on
    # it — the one point a writer DURING an invocation can act unseen by that check. The kit tests
    # put a genuine checked file there to show the cached-read threshold refuses the read-back.
    [scriptblock] $BeforeInvocation,
    # Phase 393 — the OS whose pin entry is resolved; defaults to the host's. Naming another OS is
    # only meaningful with -ResolveOnly: a prover built for one OS does not run on another.
    [ValidateSet('windows', 'linux', 'macos')][string] $Platform,
    # Print the pin entry the download path would fetch for -Platform, then exit 0 — or refuse it,
    # exactly as the download path would. Downloads nothing, runs nothing.
    [switch] $ResolveOnly,
    # Phase 399 — fetch the pinned archive for this host's OS into <ProofsDir>/.fstar/ exactly as the
    # download path does (its sources in order, the hash checked), then exit 0 — or refuse, exactly
    # as the download path would. Unpacks nothing and runs no prover, so the mirror is testable
    # without one.
    [switch] $FetchOnly,
    # Phase 399 — run the ORACLE-INDEPENDENCE GUARD (section 4b) over -OracleDir and exit: 0 when it
    # holds, 1 naming every offending line when it does not. Checks no model, so the guard is
    # testable without a prover.
    [switch] $GuardOnly,
    # Phase 399 — where a GREEN leg writes its run facts as JSON: the machine it ran on and each
    # run's contention factor. Nothing is written on a red leg. The caller's strict baseline reads
    # it, so the record says what kind of machine stands behind it.
    [string] $SummaryFile
)

$ErrorActionPreference = 'Stop'

# The leg's OWN exit codes, for the two verdicts that are not a refutation (Phase 166). A
# refutation returns the PROVER's code, which is 1 in practice; these two are the leg's, so a
# caller — a CI job, a wrapper, a post-mortem grep — can tell "the model is wrong" from "the
# prover died" and from "the leg's machinery broke" without reading a line of output. 2 is already
# taken (no prover can be resolved for this OS: the pin has no usable entry for it and FSTAR_HOME is
# unset), so these start at 3.
$ExitAbort = 3
$ExitApparatus = 4

$ProofsDir = [System.IO.Path]::GetFullPath($ProofsDir)
if (-not (Test-Path $ProofsDir)) { throw "the proofs directory '$ProofsDir' does not exist" }
if (-not $RepoRoot) { $RepoRoot = Split-Path $ProofsDir -Parent }
if (-not $PinFile) { $PinFile = Join-Path $ProofsDir 'fstar-pin.json' }
if (-not $BudgetFile) { $BudgetFile = Join-Path $ProofsDir 'modules.json' }
if (-not $OracleDir) { $OracleDir = Join-Path $ProofsDir 'oracle' }
if (-not $WorkDir) { $WorkDir = Join-Path $ProofsDir 'obj' }

# The budget file's own name, so that every message about it names the file the caller declared
# rather than a name baked in here.
$budgetName = Split-Path $BudgetFile -Leaf

Set-Location $ProofsDir

# NEVER ASSIGN $LASTEXITCODE IN THIS SCRIPT (Phase 221). It is an automatic variable that a native
# command sets in the GLOBAL scope. An assignment here — this line used to read `$LASTEXITCODE = 0`,
# carried over from the pre-kit `check.ps1` — creates a SCRIPT-scope variable of the same name, and
# every later read in this script and in its functions finds that one first. Run as `pwsh -File`,
# the script's scope happens to be the one a native command writes, so the seed was harmless in
# the pre-kit script. Run as `& check-proof-leg.ps1`, which is how every caller's `check.ps1`
# invokes it (Phase 155), the seed SHADOWS the real code: every `$LASTEXITCODE` read below saw 0
# whatever the command did. Measured 2026-09-24 against the pinned prover: a host build of a
# project that does not exist (MSB1009) printed `==== proofs: green` and exited 0, and so did a
# model with a type error — the check step's exit code read 0 too, so a refutation was reported
# as a verification. The failure was DIAGNOSED (MSBuild and F* both said so) and not REFUSED.
# `check-proof-leg.tests.ps1` beside this file runs the leg the way a caller does and holds every
# step to a non-zero exit; it goes red if this line ever comes back.

# The per-invocation cache this run owns, once section 3 has resolved one. Named here, above
# Fail, so that EVERY exit path removes it: PowerShell resolves a function body at call time, so
# Fail can call Remove-InvocationCache from before its definition. A run killed outright (a turn
# boundary, Ctrl-C) still leaves its directory behind — nothing inside a process can promise
# otherwise, which is why section 3 sweeps dead runs' directories at startup instead of trusting
# this. Leaving one behind is harmless in any case: it belongs to a pid, so it poisons nobody.
$script:invocationCache = $null
$script:invocationCacheIsOurs = $false

function Remove-InvocationCache {
    if ($script:invocationCacheIsOurs -and $script:invocationCache -and (Test-Path $script:invocationCache)) {
        Remove-Item $script:invocationCache -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Fail([string] $message, [int] $code = 1) {
    Remove-InvocationCache
    Write-Host "==== proofs: $message" -ForegroundColor Red
    exit $code
}

# A terminating error under $ErrorActionPreference = 'Stop' bypasses Fail; this does not.
trap { Remove-InvocationCache; break }

if (-not (Test-Path $PinFile)) { Fail "the pin file '$PinFile' is missing — it declares the prover this leg is defined by" }
$pin = Get-Content $PinFile -Raw | ConvertFrom-Json
$pinnedVersion = $pin.fstar.TrimStart('v')

# ---- 1. resolve the prover ---------------------------------------------------------------------

# The OS this run resolves a pin entry for (Phase 393): -Platform when named, else the host's.
# An OS PowerShell does not name (none today) resolves to '' and is refused below like any other
# OS without an entry.
$pinPlatform = if ($Platform) { $Platform }
elseif ($IsWindows) { 'windows' }
elseif ($IsLinux) { 'linux' }
elseif ($IsMacOS) { 'macos' }
else { '' }

# The pin entry for $pinPlatform, or a refusal NAMING the OS (exit 2, the "no prover for this OS"
# code). An entry must be complete and must name the pin's own release in both its asset and its
# url: the hash alone cannot catch an entry left behind by a pin bump, because it is the right hash
# for the wrong release.
function Resolve-PinEntry {
    $pinName = Split-Path $PinFile -Leaf
    $declared = @($pin.PSObject.Properties.Name | Where-Object { $pin.$_ -is [pscustomobject] -and $null -ne $pin.$_.asset })
    $osName = if ($pinPlatform) { $pinPlatform } else { 'this operating system' }
    $entry = if ($pinPlatform) { $pin.$pinPlatform } else { $null }
    if ($null -eq $entry) {
        Fail "$pinName pins no '$osName' release of F* $($pin.fstar) (it pins: $($declared -join ', ')); add a '$osName' entry (asset, url, sha256) for the same release, or set FSTAR_HOME to an F* $($pin.fstar) release" 2
    }
    $missing = @('asset', 'url', 'sha256' | Where-Object { -not $entry.$_ })
    if ($missing.Count -gt 0) {
        Fail "$pinName's '$osName' entry is incomplete: it has no $($missing -join ', ')" 2
    }
    foreach ($field in 'asset', 'url') {
        if (-not ([string]$entry.$field).Contains($pin.fstar)) {
            Fail "$pinName's '$osName' entry names a different release than the pin ($($pin.fstar)): its $field is '$($entry.$field)'" 2
        }
    }
    if ($entry.sha256 -notmatch '^[0-9a-f]{64}$') {
        Fail "$pinName's '$osName' sha256 is not 64 lowercase hex digits: '$($entry.sha256)'" 2
    }
    # Phase 399 — the MIRROR is optional for an adopter and held to the same rules as `url` when it
    # is there: it names the pin's release, and it serves the SAME asset — the one sha256 above is
    # the only thing either source is trusted for.
    if ($null -ne $entry.PSObject.Properties['mirror']) {
        $mirror = [string]$entry.mirror
        if (-not $mirror) { Fail "$pinName's '$osName' mirror is empty; remove the key or name a source" 2 }
        if (-not $mirror.Contains($pin.fstar)) {
            Fail "$pinName's '$osName' entry names a different release than the pin ($($pin.fstar)): its mirror is '$mirror'" 2
        }
        if (-not $mirror.EndsWith("/$($entry.asset)")) {
            Fail "$pinName's '$osName' mirror does not serve the pinned asset $($entry.asset): '$mirror'" 2
        }
    }
    $entry
}

# The sources an entry is fetched from, in the order they are tried: `url` (upstream), then `mirror`.
function Get-PinSources($entry) {
    $sources = @([string]$entry.url)
    if ($null -ne $entry.PSObject.Properties['mirror'] -and $entry.mirror) { $sources += [string]$entry.mirror }
    , $sources
}

if ($ResolveOnly) {
    $entry = Resolve-PinEntry
    $sources = Get-PinSources $entry
    Write-Host "==== proofs: the pinned prover for $pinPlatform is $($entry.asset) (sha256 $($entry.sha256)), from $($sources.Count) source(s) in order: $($sources -join ' then ')" -ForegroundColor Green
    exit 0
}

# ---- 1a. fetching the pinned archive (Phase 399) ----------------------------------------------------
#
# THE PROVER IS MIRRORED. Until Phase 399 the archive had one source, an upstream release asset, and
# CI cached it by the pin's hash — so a deleted upstream asset broke the leg only when the cache
# evicted, on a quiet week, with a message about a download rather than about the pin. An entry may
# now carry a `mirror` beside its `url`, and the sources are tried IN ORDER: a source that cannot
# SERVE the archive (unreachable, 404, refused) is reported and the next is tried; the leg fails
# only when every source has failed, naming each one and why.
#
# A source that serves the WRONG BYTES is not an availability failure and is NOT fallen past: the
# leg refuses on the spot, naming the source, the digest it served and the pin's sha256, and deletes
# what it fetched. A mismatch means the asset was replaced — at upstream or at the mirror — and
# quietly trying the other source would turn a tamper signal into a green leg over whichever copy
# happened to agree. The pin's sha256 is the only thing either source is trusted for.
#
# A `file://` source is copied rather than downloaded (Invoke-WebRequest has no file scheme), so a
# mirror can be an organisation's file share as well as a release asset, and the kit's own tests
# can serve one from a scratch directory.
function Get-PinnedArchive($entry, [string] $archive) {
    $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($source in (Get-PinSources $entry)) {
        if (Test-Path $archive) { Remove-Item $archive -Force }
        Write-Host "==== proofs: fetching the pinned prover $($pin.fstar) ($($entry.asset)) from $source" -ForegroundColor Cyan
        try {
            $uri = [Uri]::new($source)
            if ($uri.IsFile) { Copy-Item -LiteralPath $uri.LocalPath -Destination $archive -ErrorAction Stop }
            else { Invoke-WebRequest -Uri $source -OutFile $archive -ErrorAction Stop }
        }
        catch {
            if (Test-Path $archive) { Remove-Item $archive -Force }
            $failures.Add("$source — $($_.Exception.Message)")
            Write-Host "==== proofs: $source could not serve $($entry.asset): $($_.Exception.Message)" -ForegroundColor Yellow
            continue
        }
        $hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
        if ($hash -ne $entry.sha256) {
            Remove-Item $archive -Force
            Fail ("$source served a $($entry.asset) that does not match the pin: sha256 $hash, pinned $($entry.sha256). It was deleted, and the next source " +
                'was NOT tried — wrong bytes mean the asset was replaced, which is a finding about that source, not an outage to route around.')
        }
        Write-Host "==== proofs: $($entry.asset) from $source matches the pinned sha256 $($entry.sha256)" -ForegroundColor Cyan
        return
    }
    Fail ("no source could serve the pinned $($entry.asset) (sha256 $($entry.sha256)); tried, in order: " + ($failures -join '; ') +
        '. Set FSTAR_HOME to an F* ' + $pin.fstar + ' release to run without fetching.')
}

if ($FetchOnly) {
    $entry = Resolve-PinEntry
    $dir = Join-Path $ProofsDir '.fstar'
    New-Item -ItemType Directory -Force $dir | Out-Null
    Get-PinnedArchive $entry (Join-Path $dir $entry.asset)
    Write-Host "==== proofs: fetched $($entry.asset) into $dir" -ForegroundColor Green
    exit 0
}

# ---- 4b. the oracle-independence guard (Phase 399) -------------------------------------------------
#
# Defined here, ahead of the prover, so -GuardOnly can run it without one; the leg runs it at 4b,
# after extraction and before the host step.
#
# WHY. A differential test is worth something only if the two sides are independent. The extracted
# oracle is compiled beside production, and nothing stopped `oracle/*.fs` or the oracle's project
# from reaching a production assembly or namespace. An oracle that shortcut to the production
# function would pass every differential case by comparing production with itself, and the ladder
# would still say "differentially tested" — the false-positive class Chakraborty et al. (ICSE 2025)
# name for proof harnesses, where a definition closes only because the original it was meant to
# reproduce is still in scope. Production is opened by the TEST HOST alone.
#
# WHAT IT REFUSES, naming the file, the line and the text each time:
#   1. THE PROJECT, as MSBuild EVALUATES it (`dotnet msbuild -getItem`), so a reference inherited
#      from a Directory.Build.props counts as much as one written in the project: any
#      ProjectReference; any PackageReference other than FSharp.Core; any Reference (an assembly by
#      path); and any Compile item outside the oracle directory (production source compiled in).
#   2. THE SOURCES, read lexically with comments and string and character literals blanked: an
#      `open`, a module abbreviation or a dotted name whose FIRST segment is not `Prims`, an `FStar`
#      module, or a module declared by a file in the oracle directory; and `global.` outright. The
#      hand-written runtime floor — every oracle file that is not an extraction of a module this
#      leg extracts, `Prims.fs` and the option shim here — may also name `System` and
#      `Microsoft.FSharp`, because that is what it defines the primitives AS. A lower-case first
#      segment is a value's member access and is not judged: the project half is what makes
#      production unreachable, and the source half names the line that tried.
# An oracle directory with no project is checked on its sources alone, and the leg says so.
function Get-FSharpCode([string] $text) {
    # Comments and non-interpolated string / character literals become spaces; newlines are kept, so
    # a line number in the result is a line number in the file. Block comments nest, as F#'s do, and
    # `(*)` is the multiplication operator, not a comment. An INTERPOLATED string is kept as code: its
    # holes are code, and reading its text as code too can only refuse more, never less.
    $sb = [System.Text.StringBuilder]::new($text.Length)
    $n = $text.Length
    $i = 0
    $depth = 0
    while ($i -lt $n) {
        $c = $text[$i]
        $next = if ($i + 1 -lt $n) { $text[$i + 1] } else { [char]0 }
        if ($depth -gt 0) {
            if ($c -eq '(' -and $next -eq '*') { $depth++; $i += 2; $null = $sb.Append('  '); continue }
            if ($c -eq '*' -and $next -eq ')') { $depth--; $i += 2; $null = $sb.Append('  '); continue }
            $null = $sb.Append($(if ($c -eq "`n") { "`n" } else { ' ' })); $i++; continue
        }
        if ($c -eq '(' -and $next -eq '*' -and -not ($i + 2 -lt $n -and $text[$i + 2] -eq ')')) { $depth = 1; $i += 2; $null = $sb.Append('  '); continue }
        if ($c -eq '/' -and $next -eq '/') {
            while ($i -lt $n -and $text[$i] -ne "`n") { $null = $sb.Append(' '); $i++ }
            continue
        }
        $interpolated = ($c -eq '$') -or ($c -eq '@' -and $next -eq '$')
        if ($c -eq '"' -or (($c -eq '@' -or $c -eq '$') -and ($next -eq '"' -or $next -eq '@' -or $next -eq '$'))) {
            # The prefix ($, @, $@, @$), then the delimiter.
            $start = $i
            $verbatim = $false
            while ($i -lt $n -and ($text[$i] -eq '$' -or $text[$i] -eq '@')) { if ($text[$i] -eq '@') { $verbatim = $true }; $i++ }
            if ($i -ge $n -or $text[$i] -ne '"') { $null = $sb.Append($text.Substring($start, $i - $start)); continue }
            $triple = ($i + 2 -lt $n -and $text[$i + 1] -eq '"' -and $text[$i + 2] -eq '"')
            $open = if ($triple) { 3 } else { 1 }
            $j = $i + $open
            while ($j -lt $n) {
                if ($triple) { if ($j + 2 -lt $n -and $text[$j] -eq '"' -and $text[$j + 1] -eq '"' -and $text[$j + 2] -eq '"') { $j += 3; break } }
                elseif ($verbatim) {
                    if ($text[$j] -eq '"') { if ($j + 1 -lt $n -and $text[$j + 1] -eq '"') { $j += 2; continue } else { $j++; break } }
                }
                else {
                    if ($text[$j] -eq '\') { $j += 2; continue }
                    if ($text[$j] -eq '"') { $j++; break }
                }
                $j++
            }
            if ($j -gt $n) { $j = $n }
            $literal = $text.Substring($start, $j - $start)
            if ($interpolated) { $null = $sb.Append($literal) }
            else { $null = $sb.Append(($literal -replace '[^\n]', ' ')) }
            $i = $j
            continue
        }
        if ($c -eq "'") {
            $prev = if ($i -gt 0) { $text[$i - 1] } else { ' ' }
            if (-not ([char]::IsLetterOrDigit($prev) -or $prev -eq '_' -or $prev -eq "'")) {
                $m = [regex]::Match($text.Substring($i, [Math]::Min(12, $n - $i)), "^'(\\[^']{1,8}|[^\\'\n])'")
                if ($m.Success) { $null = $sb.Append(' ' * $m.Length); $i += $m.Length; continue }
            }
        }
        $null = $sb.Append($c)
        $i++
    }
    $sb.ToString()
}

function Test-OracleIndependence([string] $oracleDir, [string[]] $extractedModules) {
    $findings = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path $oracleDir)) {
        Write-Host "==== proofs: oracle independence — there is no oracle directory ($oracleDir), so there is nothing to guard" -ForegroundColor Cyan
        return $findings
    }
    $oracleFull = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($oracleDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

    # 1. the project, evaluated.
    $projects = @(Get-ChildItem $oracleDir -Filter '*.fsproj' -File)
    if ($projects.Count -eq 0) {
        Write-Host '==== proofs: oracle independence — the oracle directory has no project, so the guard reads its sources alone' -ForegroundColor Cyan
    }
    foreach ($project in $projects) {
        $global:LASTEXITCODE = 0
        $json = & dotnet msbuild $project.FullName -nologo '-getItem:ProjectReference' '-getItem:PackageReference' '-getItem:Reference' '-getItem:Compile' 2>&1
        if ($LASTEXITCODE -ne 0) {
            $findings.Add("$($project.Name): MSBuild could not evaluate the project (exit $LASTEXITCODE), so its references cannot be shown to be independent: $(($json | Select-Object -Last 3) -join ' ')")
            continue
        }
        $items = ($json -join "`n" | ConvertFrom-Json).Items
        $offending = [System.Collections.Generic.List[object]]::new()
        foreach ($item in @($items.ProjectReference)) { if ($item) { $offending.Add(@($item, 'a ProjectReference — the oracle may reference no project')) } }
        foreach ($item in @($items.Reference)) { if ($item) { $offending.Add(@($item, 'a Reference — the oracle may reference no assembly by path')) } }
        foreach ($item in @($items.PackageReference)) {
            if ($item -and $item.Identity -ne 'FSharp.Core') { $offending.Add(@($item, 'a PackageReference other than FSharp.Core')) }
        }
        foreach ($item in @($items.Compile)) {
            if (-not $item) { continue }
            $itemDir = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($item.FullPath))
            if ($itemDir -ne $oracleFull) { $offending.Add(@($item, 'a Compile item outside the oracle directory — source the oracle did not extract')) }
        }
        foreach ($pair in $offending) {
            $item, $why = $pair
            $definedIn = if ($item.DefiningProjectFullPath) { $item.DefiningProjectFullPath } else { $project.FullName }
            $where = Split-Path $definedIn -Leaf
            $lineText = ''
            if (Test-Path -LiteralPath $definedIn) {
                $lines = Get-Content -LiteralPath $definedIn
                for ($k = 0; $k -lt $lines.Count; $k++) {
                    if ($lines[$k].Contains($item.Identity)) { $where = "$where`:$($k + 1)"; $lineText = $lines[$k].Trim(); break }
                }
            }
            $findings.Add("$where`: $why ('$($item.Identity)')$(if ($lineText) { ": $lineText" })")
        }
    }

    # 2. the sources.
    $sources = @(Get-ChildItem $oracleDir -Filter '*.fs' -File)
    $own = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($source in $sources) {
        $null = $own.Add($source.BaseName)
        $declared = [regex]::Match((Get-Content -LiteralPath $source.FullName -Raw), '(?m)^\s*module\s+(?:rec\s+)?([A-Za-z_][\w.]*)\s*$')
        if ($declared.Success) { $null = $own.Add(($declared.Groups[1].Value -split '\.')[0]) }
    }
    $headPattern = [regex]::new('(?<![\w''.`])(``[^`]+``|[A-Za-z_][\w'']*)(?=\s*\.\s*[A-Za-z_`])')
    $openPattern = [regex]::new('^\s*open\s+(?:type\s+)?(``[^`]+``|[A-Za-z_][\w'']*)((?:\s*\.\s*[\w`'']+)*)')
    $aliasPattern = [regex]::new('^\s*module\s+[\w'']+\s*=\s*(``[^`]+``|[A-Za-z_][\w'']*)((?:\s*\.\s*[\w`'']+)*)')
    foreach ($source in $sources) {
        $floor = $extractedModules -notcontains $source.BaseName
        $raw = (Get-Content -LiteralPath $source.FullName -Raw).Replace("`r`n", "`n")
        $code = (Get-FSharpCode $raw) -split "`n"
        $rawLines = $raw -split "`n"
        for ($k = 0; $k -lt $code.Count; $k++) {
            $line = $code[$k]
            if ($line.Trim() -eq '') { continue }
            $heads = [System.Collections.Generic.List[object]]::new()
            foreach ($p in @($openPattern, $aliasPattern)) {
                $m = $p.Match($line)
                if ($m.Success) { $heads.Add(@($m.Groups[1].Value, ($m.Groups[1].Value + $m.Groups[2].Value) -replace '\s', '')) }
            }
            foreach ($m in $headPattern.Matches($line)) {
                $rest = $line.Substring($m.Index) -replace '^(``[^`]+``|[\w'']+)((\s*\.\s*(``[^`]+``|[\w'']+))*).*$', '$1$2'
                $heads.Add(@($m.Groups[1].Value, ($rest -replace '\s', '')))
            }
            $judged = [System.Collections.Generic.HashSet[string]]::new()
            foreach ($pair in $heads) {
                $head, $path = $pair
                $bare = $head.Trim('`')
                if (-not $judged.Add($bare)) { continue }   # an `open X.Y` is matched by both patterns
                $why = $null
                if ($bare -eq 'global') { $why = '`global.` reaches past every module the oracle declares' }
                elseif ($bare -cmatch '^[a-z_]') { continue }
                elseif ($bare -eq 'Prims' -or $bare -cmatch '^FStar($|_)' -or $own.Contains($bare)) { continue }
                elseif ($floor -and ($bare -eq 'System' -or $path -eq 'Microsoft.FSharp' -or $path.StartsWith('Microsoft.FSharp.'))) { continue }
                else { $why = "names '$bare', which is not Prims, an FStar module or a module of the oracle's own$(if ($floor) { ', nor the runtime floor''s System / Microsoft.FSharp' })" }
                $findings.Add("$($source.Name):$($k + 1): $why`: $($rawLines[$k].Trim())")
            }
        }
    }
    return $findings
}

function Assert-OracleIndependence {
    $extracted = @($Modules | Where-Object { $ProofOnly -notcontains $_ })
    $findings = Test-OracleIndependence $OracleDir $extracted
    if ($findings.Count -gt 0) {
        Write-Host "==== proofs: the ORACLE is NOT INDEPENDENT of production — $($findings.Count) offending line(s):" -ForegroundColor Red
        foreach ($f in $findings) { Write-Host "     $f" -ForegroundColor Red }
        Write-Host '     A differential between an oracle that reaches production and production compares production with itself.' -ForegroundColor Red
        Write-Host '     The oracle may reference FSharp.Core alone and name only Prims, FStar modules and its own modules (see kit/README.md).' -ForegroundColor Red
        Fail 'oracle independence'
    }
    $count = @(Get-ChildItem $OracleDir -Filter '*.fs' -File -ErrorAction SilentlyContinue).Count
    Write-Host "==== proofs: oracle independence — the oracle project and its $count source file(s) reach nothing but FSharp.Core, Prims, FStar and their own modules" -ForegroundColor Green
}

if ($GuardOnly) {
    Assert-OracleIndependence
    exit 0
}

function Resolve-FStar {
    if ($env:FSTAR_HOME) {
        $exe = Join-Path $env:FSTAR_HOME 'bin/fstar.exe'
        if (-not (Test-Path $exe)) { Fail "FSTAR_HOME is set to '$env:FSTAR_HOME' but bin/fstar.exe is not there" }
        return $exe
    }

    # Every release, on every OS, lays the prover out as fstar/bin/fstar.exe (the release's own
    # packaging adds that top-level directory), so one local path serves both entries.
    $local = Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe'
    if (Test-Path $local) { return $local }

    $entry = Resolve-PinEntry
    $asset = $entry.asset
    $dir = Join-Path $ProofsDir '.fstar'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $archive = Join-Path $dir $asset

    if (Test-Path $archive) {
        # An archive a previous run left behind is held to the pin like a fresh one.
        $hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
        if ($hash -ne $entry.sha256) {
            Remove-Item $archive -Force
            Fail "the cached $asset does not match the pinned sha256 (got $hash, pinned $($entry.sha256)); it was deleted — re-run to fetch again"
        }
    }
    else { Get-PinnedArchive $entry $archive }

    Write-Host "==== proofs: unpacking $asset" -ForegroundColor Cyan
    if ($asset.EndsWith('.zip')) {
        Expand-Archive -Path $archive -DestinationPath $dir -Force
    }
    elseif ($asset.EndsWith('.tar.gz')) {
        # tar keeps the executable bits a .zip cannot carry; it is on every runner image and on
        # Windows 10+ alike.
        & tar -xzf $archive -C $dir
        if ($LASTEXITCODE -ne 0) { Fail "tar could not unpack $asset into $dir (exit $LASTEXITCODE)" }
    }
    else { Fail "$asset is neither a .zip nor a .tar.gz; the leg does not know how to unpack it" }
    if (-not (Test-Path $local)) { Fail "unpacked $asset but found no fstar/bin/fstar.exe under $dir" }
    return $local
}

$fstar = Resolve-FStar
$versionLine = (& $fstar --version 2>&1 | Select-Object -First 1)
if ($versionLine -ne "F* $pinnedVersion") {
    Fail "the resolved prover reports '$versionLine' but the pin is 'F* $pinnedVersion' ($fstar)"
}

$fstarHome = Split-Path (Split-Path $fstar -Parent) -Parent
$z3dir = Get-ChildItem (Join-Path $fstarHome 'lib/fstar') -Directory -Filter 'z3-*' | Select-Object -First 1
if ($null -eq $z3dir) { Fail "no bundled Z3 under $fstarHome/lib/fstar" }
if ($z3dir.Name -ne "z3-$($pin.z3)") { Fail "the bundled Z3 is $($z3dir.Name); the pin is z3-$($pin.z3)" }
Write-Host "==== proofs: $versionLine, bundled $($z3dir.Name), at $fstarHome" -ForegroundColor Cyan

# ---- 1b. the three verdicts (Phase 166) ----------------------------------------------------------
#
# See the header for what each class means and what it obliges. What lives here is HOW the class is
# decided, and it is deliberately one small readable function per question.

# THE DISCRIMINATOR between a refutation and an abort. A log carrying any of these is one in which
# the prover SAID something was wrong; a log carrying none of them is one in which it said nothing,
# so whatever ended the process, it was not a refutation. Line by line, and each alternative is
# here for a reason:
#
#   `* Error NNN`        — F*'s own diagnostic header, and the FORM THE PINNED PROVER ACTUALLY
#                          PRINTS: `* Error 19 at Foo.fst(8,39-8,41):`, `* Error 325 …`,
#                          `* Error 129:` for a file it cannot open. Measured, not assumed — see
#                          the note below, which is the whole reason this line is first.
#   `(Error NNN)`        — the parenthesised spelling. Kept beside the one above rather than
#                          instead of it: it is what F*'s other message formats emit, and a leg
#                          whose discriminator only knows one of two spellings is a leg that reads
#                          a refutation as an abort the day the format is switched.
#   `N error(s) … reported` — F*'s end-of-run summary. The most unambiguous line in the log, and
#                          the one that survives any change to how an individual diagnostic reads.
#   `Failed to prove`    — the SMT solver's own sentence, which appears inside a numbered
#                          diagnostic and also on its own.
#   `Unexpected`         — `Unexpected error` / `Unexpected exception`, which can reach the log
#                          without a number at all.
#   a failing quake line — a query that survived some seeds and not others; `--quake` reports it,
#                          and it is a refutation even though the module part-verified.
#
# WHAT WAS MEASURED, AND WHAT THE SHARD ASSUMED (Phase 166). This phase was specified against
# `(Error NNN)` as "the F* error line". On the pinned prover it is not: the probe that staged a
# missing model file printed `* Error 129:` and the leg — with only the parenthesised spelling —
# classified a plain diagnostic as an ABORT and RETRIED it, which is the exact inversion this
# verdict exists to prevent. The deliberately-false-lemma probe had passed a moment earlier, but
# only through `Failed to prove`, so the green probe was agreeing for the wrong reason. Both
# spellings are matched now and the summary line is matched as a third, independent witness.
#
# What is deliberately NOT here: warnings. A warning is not an undischarged query, and the one
# warning class that matters to this leg (an assume) is promoted to an error by the flags above, so
# matching warnings would only make a green-but-chatty module read as refuted.
$diagnosticPattern = [regex]::new(
    '^\s*\*\s*Error\b' +
    '|\(Error\s+\d+\)' +
    '|\berrors?\s+(were|was)\s+reported\b' +
    '|Failed to prove' +
    '|Unexpected' +
    # A quake line is a failure unless it reads `proved N/N goals` with equal counts. Matching
    # `fail` anywhere refused TreeOps.fst over a query NAMED `..._fails_...` (2026-09-26). The
    # whitespace sits INSIDE the lookahead: outside it, `\s+` backtracks to a shorter match and
    # the lookahead then sees `\tproved`, never `proved`.
    '|^\s*Quake:\s*query\s*\([^)]*\)(?!\s*proved\s+(\d+)/\2\s+goals\b)')

function Test-ProverDiagnostic([System.Collections.Generic.List[string]] $lines) {
    foreach ($line in $lines) {
        if ($diagnosticPattern.IsMatch($line)) { return $true }
    }
    return $false
}

# The APPARATUS discriminator, at the extraction step: a dependency's checked-module file missing
# from the cache. Returns the file's name when the log carries one, so the fault is reported
# NAMING it rather than as a bare error number nobody can act on.
#
# Asked of the WHOLE log rather than line by line, because the two sentences that say this happened
# are wrapped across several lines and the path sits on one of its own:
#
#     * Warning 241 at PDep.fst(0,0-0,0):
#       - Unable to load
#         …\cache-16348\PDep.fst.checked
#         since checked file
#         …\cache-16348\PDep.fst.checked
#         does not exist; will recheck PDep.fst
#     * Error 317:
#       - Cross-module inlining expects all modules to be checked first.
#
# It is only ever CONSULTED about an extraction that already failed (see section 4), and that
# ordering is load-bearing: Warning 241 on its own means F* re-checked the module and carried on,
# which is a working leg, so treating the warning as the fault would redden a run that succeeded.
function Get-MissingCheckedDependency([System.Collections.Generic.List[string]] $lines) {
    $text = $lines -join "`n"
    $fault = ($text -match 'Error\s+317\b') -or ($text -match 'checked file' -and $text -match 'does not exist')
    if ($fault) {
        $named = [regex]::Match($text, '[^\s"'']+\.checked')
        if ($named.Success) { return $named.Value }
        return 'a checked-module file the log does not name — read the extraction log'
    }
    # The cache directory going away entirely is the same fault one level up, and it prints nothing
    # recognisable, so it is asked about rather than parsed for.
    if ($script:invocationCache -and -not (Test-Path $script:invocationCache)) {
        return "the cache directory $script:invocationCache itself, which is gone"
    }
    return $null
}

# A refutation returns the prover's own code — that is the pre-kit behaviour and a repository's
# tests may depend on it. The two lines below are what make "distinct from a refutation's" a fact
# rather than an expectation: a prover that ever exited 3 or 4 would otherwise make the leg's own
# codes ambiguous, so those two are remapped to the generic 1 and the remap says so.
function Get-RefutationExitCode([int] $code) {
    if ($code -eq $ExitAbort -or $code -eq $ExitApparatus -or $code -eq 0) {
        Write-Host "==== proofs: the prover exited $code, which is one of this leg's own verdict codes; reporting the refutation as exit 1 so the codes stay distinct" -ForegroundColor Yellow
        return 1
    }
    return $code
}

# The PRE-FLIGHT snapshot. A post-mortem asking "was the machine contended?" has, today, no way to
# find out — the run is over and the processes are gone. Two numbers at the head of every run
# answer it: how many provers were running (this one included), and how much physical memory was
# free. Phase 149's aborts happened under six concurrent provers with free memory below 3 GB.
# It must never be able to fail the leg, so every reading is guarded and an unavailable one reads
# as `unknown` rather than throwing. Note where each reading is taken from: at the HEAD of a run
# this run's own prover has not started yet, so the count is of the OTHER provers on the machine,
# which is the number a contention question is actually about; at an ABORT it includes whatever is
# running at that instant.
function Get-ResourceSnapshot {
    $provers = 0
    # `fstar` is the Windows process name of fstar.exe; on Linux the name keeps its `.exe`.
    try { $provers = @(Get-Process -Name 'fstar', 'fstar.exe' -ErrorAction SilentlyContinue).Count } catch { $provers = -1 }
    $free = 'unknown'
    try {
        if ($IsWindows) {
            $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
            $free = '{0:N1} GB' -f ($os.FreePhysicalMemory / 1MB)   # FreePhysicalMemory is in KB
        }
        elseif (Test-Path '/proc/meminfo') {
            $kb = [regex]::Match((Get-Content '/proc/meminfo' -Raw), 'MemAvailable:\s+(\d+)')
            if ($kb.Success) { $free = '{0:N1} GB' -f ([double]$kb.Groups[1].Value / 1MB) }
        }
    }
    catch { $free = 'unknown' }
    $proverText = if ($provers -lt 0) { 'an unreadable number of' } else { "$provers" }
    "$proverText fstar process(es) on this machine, $free free physical memory"
}

# Every prover invocation goes through here, so that (a) its whole output is available to the
# classifiers above rather than only to a human reading scrollback, and (b) the same bytes are teed
# to a file a post-mortem can open. The output is re-emitted line by line as it arrives, so what a
# watcher sees is what it saw before.
#
# $ErrorActionPreference is lowered for the invocation and restored after: with `2>&1` on a native
# command, PowerShell surfaces stderr as ErrorRecords, and under 'Stop' the prover's first stderr
# line would terminate the leg before anything could be classified — which is precisely the failure
# mode this section exists to remove.
function Invoke-Prover([string[]] $arguments, [string] $logPath) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $previous = $ErrorActionPreference
    $code = 0
    try {
        $ErrorActionPreference = 'Continue'
        & $fstar @arguments 2>&1 | ForEach-Object {
            $line = if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.ToString() } else { [string]$_ }
            $lines.Add($line)
            Write-Host $line
        }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($logPath) {
        try { [System.IO.File]::WriteAllLines($logPath, $lines) } catch { }
    }
    [pscustomobject]@{ ExitCode = $code; Lines = $lines; LogPath = $logPath }
}

# ---- 2. the declared cost budgets ----------------------------------------------------------------

# The budget file says what each module is expected to cost on a cold run. It is a committed
# declared artefact, so its ABSENCE is a defect and fails here; a mismatch between it and -Modules
# is a cost finding rather than a failure, because a sibling adding a model should not have their
# leg go red for a budget nobody could have measured yet — the finding names the module and what
# to do.
# A finding is an OBJECT rather than a string since Phase 171, because a ceiling finding acquires
# one more fact after it is printed: the contention factor of the run it fired on, which is not
# known until that run has measured every module. `Run` is the run it belongs to (0 for the
# coverage and shape findings below, which belong to no run and are never labelled), and `Label` is
# filled in at the end of that run by section 3c. The line printed HERE is byte-identical to the
# one this leg has always printed — the label reaches the reader on the run's own contention line
# and on the closing verdict, which are the two places that can carry it honestly.
$costFindings = [System.Collections.Generic.List[object]]::new()

function Add-CostFinding([string] $message, [int] $run = 0) {
    $script:costFindings.Add([pscustomobject]@{ Text = $message; Run = $run; Label = '' })
    Write-Host "==== proofs: COST — $message" -ForegroundColor Yellow
}

if (-not (Test-Path $BudgetFile)) {
    Fail "$budgetName is missing — it declares each module's cost budget (see the README's 'Running it')"
}

# The SHAPE is a failure where a mismatch is a finding, and the difference is whether the file can
# still be read as a budget at all. An entry with no `budgetSeconds` would otherwise arrive as 0 and
# every run would be infinitely over it — a flood of findings, and a division by zero rendering the
# percentage. A declared artefact is held to its shape by the code that consumes it.
# The `fastestSeconds` beside it (Phase 399: the fastest genuine cold check recorded, which decides
# whether the cached-read threshold applies to the module) is held to its shape the same way WHEN
# IT IS THERE, and is a coverage finding when it is ABSENT — a sibling adding a model should no more
# go red for a measurement nobody has taken than for a budget nobody has measured. An absent one
# leaves that module to the cache provenance check alone, which is the safe direction.
#
# Two more per-entry numbers are READ here since Phase 171, and neither is required. The
# `measuredSeconds` this file has always recorded beside a budget — the observation the budget was
# seeded from — becomes the DENOMINATOR of the contention factor in section 3c, so it is held to
# its shape when it is there and its absence simply takes that module out of the factor. The
# optional `contentionFactor` beside it is PROVENANCE and nothing else: it records what the
# machine was doing when that measurement was taken, so a later reader can tell a number seeded on
# a quiet machine from one seeded on a busy one. Nothing multiplies it into anything — see the
# note on section 3c for why the measurement stays the wall clock.
$budgets = @{}
$fastest = @{}
$measurements = @{}
$budgetDocument = Get-Content $BudgetFile -Raw | ConvertFrom-Json

# Phase 399 RETIRED the per-module time floors. A budget file still carrying one is refused rather
# than read past: a key the leg no longer acts on, left in a declaration, reads to its next editor as
# a gate that is still there.
if ($null -ne $budgetDocument.PSObject.Properties['floorSeeding']) {
    Fail ("$budgetName still carries a floorSeeding block. The per-module floors were retired by Phase 399 for one absolute CACHED-READ threshold: " +
        'replace the block with cachedRead (thresholdSeconds, appliesFromFastestSeconds), keep each entry''s fastestSeconds, delete floorSeconds — see the kit README')
}
foreach ($entry in $budgetDocument.modules) {
    $name = $entry.module
    if ([string]::IsNullOrWhiteSpace($name)) { Fail "$budgetName carries an entry with no module name" }
    if ($budgets.ContainsKey($name)) { Fail "$budgetName declares '$name' twice" }

    $declaredBudget = $entry.budgetSeconds
    if ($declaredBudget -isnot [int] -and $declaredBudget -isnot [long] -and $declaredBudget -isnot [double]) {
        Fail "$budgetName entry '$name' has no numeric budgetSeconds"
    }
    if ([int]$declaredBudget -lt 1) { Fail "$budgetName entry '$name' has a budgetSeconds of $declaredBudget — a budget is a positive number of seconds" }

    $budgets[$name] = [int]$declaredBudget

    if ($null -ne $entry.PSObject.Properties['floorSeconds']) {
        Fail "$budgetName entry '$name' still carries a floorSeconds — the per-module floors were retired by Phase 399 for the cachedRead threshold; delete it (keep fastestSeconds)"
    }
    $declaredFastest = $entry.fastestSeconds
    if ($null -ne $declaredFastest) {
        if ($declaredFastest -isnot [int] -and $declaredFastest -isnot [long] -and $declaredFastest -isnot [double]) {
            Fail "$budgetName entry '$name' has a non-numeric fastestSeconds"
        }
        if ([double]$declaredFastest -lt 0) { Fail "$budgetName entry '$name' has a fastestSeconds of $declaredFastest — a measurement is a non-negative number of seconds" }
        $fastest[$name] = [double]$declaredFastest
    }

    $declaredMeasured = $entry.measuredSeconds
    if ($null -ne $declaredMeasured) {
        if ($declaredMeasured -isnot [int] -and $declaredMeasured -isnot [long] -and $declaredMeasured -isnot [double]) {
            Fail "$budgetName entry '$name' has a non-numeric measuredSeconds"
        }
        if ([double]$declaredMeasured -lt 0) { Fail "$budgetName entry '$name' has a measuredSeconds of $declaredMeasured — a measurement is a non-negative number of seconds" }
        $measurements[$name] = [double]$declaredMeasured
    }

    $declaredContention = $entry.contentionFactor
    if ($null -ne $declaredContention) {
        if ($declaredContention -isnot [int] -and $declaredContention -isnot [long] -and $declaredContention -isnot [double]) {
            Fail "$budgetName entry '$name' has a non-numeric contentionFactor"
        }
        if ([double]$declaredContention -le 0) { Fail "$budgetName entry '$name' has a contentionFactor of $declaredContention — a factor is a positive multiple" }
    }
}

# THE CACHED-READ THRESHOLD (Phase 399). Declared in the budget file, like every other number here,
# because what a cached read costs is a fact about the prover and the machine; see the header.
$cachedReadThreshold = $null
$cachedReadFrom = $null
if ($null -ne $budgetDocument.cachedRead) {
    $block = $budgetDocument.cachedRead
    foreach ($key in 'thresholdSeconds', 'appliesFromFastestSeconds') {
        $v = $block.$key
        if ($v -isnot [int] -and $v -isnot [long] -and $v -isnot [double]) { Fail "$budgetName cachedRead.$key is missing or not numeric" }
        if ([double]$v -le 0) { Fail "$budgetName cachedRead.$key is $v — it must be a positive number of seconds" }
    }
    $cachedReadThreshold = [double]$block.thresholdSeconds
    $cachedReadFrom = [double]$block.appliesFromFastestSeconds
    if ($cachedReadFrom -lt $cachedReadThreshold) {
        Fail "$budgetName cachedRead.appliesFromFastestSeconds ($cachedReadFrom) is under its thresholdSeconds ($cachedReadThreshold) — a module whose genuine cold check can be under the threshold would be refused as a cached read"
    }
}
else {
    Add-CostFinding "$budgetName declares no cachedRead threshold — nothing but the cache provenance check can tell a cached read from a cold check; seed one per the kit README and cite your phase"
}

foreach ($module in $Modules) {
    if (-not $budgets.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and $budgetName declares no budget for it — time a cold run, budget it per the file's seeding rule, and cite your phase"
    }
    elseif ($null -ne $cachedReadThreshold -and -not $fastest.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and $budgetName records no fastestSeconds for it — the cached-read threshold cannot be applied to it without one; record its fastest genuine cold run and cite your phase"
    }
}
foreach ($declared in $budgets.Keys) {
    if ($Modules -notcontains $declared) {
        Add-CostFinding "$budgetName budgets '$declared', which the leg does not check — drop the entry, or add the model to check.ps1's module list"
    }
}

# ---- 2b. the contention factor's declarations (Phase 171) ----------------------------------------
#
# WHY THIS EXISTS. A budget overshoot has two completely different causes and the log said nothing
# about which one it was looking at. On 2026-09-14 `Chain` overshot twice in seven runs and came in
# at 16-25s on the other five, untouched by any phase since 145. Phase 162 measured `TreeOps` at
# 145s in a pass that inflated three untouched modules by the same factor, and had to depart from
# the seeding rule by hand and write a paragraph explaining why. Phase 182 measured `Capability` at
# 75s against a 20s budget on a run contended by three sibling gates. Every one of those ran beside
# other provers, and a reader of any of those logs today cannot tell the afternoon from the module.
#
# THE MEASUREMENT. At the end of each run the leg takes, for every module the run's tree did NOT
# change, the ratio of what that module just cost to the `measuredSeconds` its entry records — and
# reports the MEDIAN of those ratios as the run's contention factor. An untouched module's cost is
# a property of the machine and not of the tree, so a pass in which all of them ran 1.7x their
# recorded measurements is a pass in which the machine was 1.7x slower, whatever any one module's
# line says. The median rather than the mean, because one module aborting-and-retrying or hitting a
# pathological query is exactly the outlier a mean would launder into the number.
#
# WHAT IT IS DELIBERATELY NOT. It is never multiplied into a measurement: the seconds a green line
# prints stay the wall clock the module actually took, and the factor sits BESIDE them. A
# normalised measurement would be a number nobody observed, and the whole value of this leg's cost
# half is that every figure in it is one somebody's machine really produced.
#
# THE FLOORS' OS (Phase 402) is RETIRED with the floors (Phase 399). A floor was the fastest cold
# run ever observed on the machine that seeded it, halved, so it was a fact about that machine: the
# first Linux run verified WireColumn in 16s against a 17s floor seeded on Windows, and the floors
# had to be switched off on every OS but one. The cached-read threshold has no such dependence in
# the direction that matters: a faster machine reads a cache faster still, so it stays under the
# threshold, and `appliesFromFastestSeconds` keeps a fast genuine check above it (see section 2).

# THE THRESHOLD is declared, in this file's own `contentionSeeding` block, for the same reason the
# budget and cached-read rules are: a number the engine baked in would be a number no repository could
# re-seed from its own machine. An ABSENT block is NOT a finding — unlike an absent budget,
# which fire per module when a model is added, this one is per FILE and one-off, and a finding that
# is present on every run of an unseeded repository is one people learn to scroll past. The factor
# is still computed and still printed; nothing is labelled, and the line says so and names the
# block to seed. That is exactly the pre-171 behaviour plus one informative number, which is the
# safe direction for an adopter.
$contentionThreshold = $null
$contentionMinimumSamples = 3
# A module whose recorded measurement is a second or two contributes noise rather than signal: the
# clock is whole seconds, so 0s against a recorded 2s is a ratio of 0 and 1s is a ratio of 0.5, and
# neither says anything about the machine. `contentionSeeding.minimumSeconds` carries this
# repository's answer to "below what is a reading process-start noise" (until Phase 399 it was the
# retired floors' `zeroBelowSeconds`, and the number moved with its argument).
$contentionMinimumSeconds = 5
if ($null -ne $budgetDocument.contentionSeeding -and $null -ne $budgetDocument.contentionSeeding.minimumSeconds) {
    $contentionMinimumSeconds = [double]$budgetDocument.contentionSeeding.minimumSeconds
}
if ($null -ne $budgetDocument.contentionSeeding) {
    $block = $budgetDocument.contentionSeeding
    if ($null -ne $block.threshold) {
        if ($block.threshold -isnot [int] -and $block.threshold -isnot [long] -and $block.threshold -isnot [double]) {
            Fail "$budgetName contentionSeeding.threshold is not numeric"
        }
        if ([double]$block.threshold -le 0) { Fail "$budgetName contentionSeeding.threshold is $($block.threshold) — a threshold is a positive multiple" }
        $contentionThreshold = [double]$block.threshold
    }
    if ($null -ne $block.minimumSamples) {
        if ([int]$block.minimumSamples -lt 1) { Fail "$budgetName contentionSeeding.minimumSamples is $($block.minimumSamples) — a median needs at least one sample" }
        $contentionMinimumSamples = [int]$block.minimumSamples
    }
    if ($null -ne $block.minimumMeasuredSeconds) { $contentionMinimumSeconds = [double]$block.minimumMeasuredSeconds }
}

# THE UNTOUCHED SET, derived rather than declared. A module this working tree has changed is one
# whose cost may have moved for a reason that IS about the module, so it must not vote on whether
# the machine was slow. `git status --porcelain` answers modified, staged and brand-new in one call
# and needs no branch name, which matters for a kit an adopter drops into a repository whose
# default branch this script cannot know.
#
# The LIMIT is worth stating rather than leaving to be discovered: a session that has already
# COMMITTED its model edits has a clean tree, so its module reads as untouched and votes. That is
# what the median absorbs — one or two skewed ratios out of a dozen move it very little — and it is
# the honest boundary of what a working-tree question can answer. Where git cannot answer at all
# (no repository, no git on PATH) every module counts as untouched, and the line below says so:
# "I could not tell" must never be printed as "nothing is touched".
function Get-TouchedModules {
    try {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try { $porcelain = & git -C $ProofsDir status --porcelain --untracked-files=all -- . 2>&1 }
        finally { $ErrorActionPreference = $previous }
        if ($LASTEXITCODE -ne 0) { return $null }
    }
    catch { return $null }

    $touched = [System.Collections.Generic.List[string]]::new()
    foreach ($module in $Modules) {
        foreach ($line in @($porcelain)) {
            if ([string]$line -match "(^|[/\\""\s])$([regex]::Escape($module))\.fst(""|\s|$)") {
                $touched.Add($module)
                break
            }
        }
    }
    , $touched.ToArray()
}

$touchedModules = Get-TouchedModules
if ($null -eq $touchedModules) {
    Write-Host "==== proofs: contention — the touched set could not be derived from git here, so EVERY module counts as untouched and votes on the contention factor" -ForegroundColor Yellow
}
elseif ($touchedModules.Count -gt 0) {
    Write-Host "==== proofs: contention — this working tree changes $($touchedModules -join ', ') — excluded from the contention factor, since their cost may have moved for a reason that is about the module" -ForegroundColor Cyan
}

# The median of a run's untouched ratios. Separate, small and total: a median of an empty set is
# not a number and the caller is the one that knows what to print instead.
function Get-Median([double[]] $values) {
    $sorted = @($values | Sort-Object)
    $n = $sorted.Count
    if ($n -eq 0) { return $null }
    if ($n % 2 -eq 1) { return [double]$sorted[($n - 1) / 2] }
    return ([double]$sorted[$n / 2 - 1] + [double]$sorted[$n / 2]) / 2
}

# ---- 2c. twin evaluation's coverage (Phase 309) ------------------------------------------------------
#
# Static, and therefore before the prover runs: an extracted model with no fixtures is a refusal that
# needs no proof to establish. Whether each twin HOLDS is the check step's (the model's own
# `assert_norm`) and then the host step's (the extracted closures); this is whether every extracted
# model has twins to hold at all. Two declarations are read, each one regular form in the model's
# own source: the list (`let twins`) and the normalised assertion over it.

if ($Twins) {
    $extractedModels = @($Modules | Where-Object { $ProofOnly -notcontains $_ })
    $untwinned = [System.Collections.Generic.List[string]]::new()
    foreach ($module in $extractedModels) {
        $source = Join-Path $ProofsDir "$module.fst"
        $text = if (Test-Path $source) { Get-Content $source -Raw } else { '' }
        $declares = $text -match '(?m)^let\s+twins\s*:'
        $asserts = $text -match 'assert_norm\s*\(\s*twins_hold\s+twins\s*==\s*true\s*\)'
        if (-not ($declares -and $asserts)) { $untwinned.Add($module) }
    }
    if ($untwinned.Count -gt 0) {
        Fail ("twin evaluation does not cover every extracted model: $($untwinned -join ', ') declare(s) no ``let twins`` list asserted by " +
            '`assert_norm (twins_hold twins == true)`. Add fixtures to each, or the extractor premise goes unsampled for it.')
    }
    Write-Host "==== proofs: twin evaluation covers all $($extractedModels.Count) extracted model(s) — each declares twins the prover normalises" -ForegroundColor Cyan
}
else {
    Write-Host '==== proofs: no -Twins — the extracted F# is held to the model''s TEXT only, never evaluated against its normaliser' -ForegroundColor Yellow
}

# ---- 3. check, -Runs times from a cold cache -------------------------------------------------------
#
# THE CACHE IS PER INVOCATION (Phase 164). Until then it was one constant directory, obj/cache,
# and clearing it at the head of a run only makes that run cold if nothing else is writing there.
# On 2026-09-14 something was: an orphaned background check.ps1 in the same worktree kept writing
# .checked files, and the replacement run reported TreeOps 0s, Skeleton 0s, Chain 0s and printed
# `==== proofs: green`. Nothing in the script could see it — it cleared the directory it was about
# to use, which a second writer defeats a moment later. A directory named for this process cannot
# be written into by another invocation at all, so the property the -Runs loop needs holds by
# construction rather than by nobody else running.

$out = Join-Path $WorkDir 'out'
$cacheRoot = $WorkDir

# The prover transcripts (Phase 166). NOT under the cache directory: a `-CacheDir` the caller named
# is refused at the next invocation if it holds anything that is not a `*.checked` file, so writing
# logs there would turn the flag into a one-shot.
$logsDir = Join-Path $WorkDir 'logs'
if (Test-Path $logsDir) { Remove-Item $logsDir -Recurse -Force }
New-Item -ItemType Directory -Force $logsDir | Out-Null

# Aborts that were retried and then passed. The leg is GREEN when that happens — the model verified
# — but a run that lost a prover and got it back is not the same evidence as one that did not, so
# the verdict at the end says so rather than leaving it to whoever scrolls far enough.
$abortFindings = [System.Collections.Generic.List[string]]::new()

if ($CacheDir) {
    # A caller-named directory is CLEARED at each run head, exactly as the default one is —
    # otherwise -CacheDir would silently mean "warm", which is the opposite of what this section
    # is for. So refuse one holding anything that is not a checked-module file: the flag is for
    # naming where the cache goes, and pointing it at a directory with other contents in it would
    # delete them. It is not removed at exit; the caller named it, so the caller keeps it.
    # `[IO.Path]::Combine` and not `Join-Path`: PowerShell's Join-Path CONCATENATES a rooted second
    # argument, so `-CacheDir C:\somewhere` resolved to `<proofs>\C:\somewhere` and the run died a
    # second later with a path nobody would recognise as their own argument. .NET's Combine returns
    # the second path when it is rooted and joins when it is not, so the relative case — which is
    # what the flag's "cleared before every run" contract is written against — is unchanged.
    # (Inherited from Phase 164 and found by using the flag; a kit must not ship it.)
    $cache = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Get-Location).Path, $CacheDir))
    if (Test-Path $cache) {
        $foreign = Get-ChildItem $cache -Force | Where-Object { $_.PSIsContainer -or $_.Name -notlike '*.checked*' }
        if ($foreign) {
            Fail "-CacheDir '$cache' holds $($foreign.Count) entry/entries that are not checked-module files (first: $($foreign[0].Name)) — this script CLEARS its cache directory before every run, so it will only use one that is empty or holds nothing but *.checked files"
        }
    }
    $script:invocationCacheIsOurs = $false
}
else {
    # Sweep the directories left by runs that were killed before they could remove their own. Only
    # ones whose pid is gone, so a concurrent invocation's cache is never touched — which is the
    # whole point of the naming. A pid that has since been reused just leaves a directory behind;
    # that costs nothing, where deleting a live run's cache would cost the exact incident above.
    if (Test-Path $cacheRoot) {
        foreach ($stale in (Get-ChildItem $cacheRoot -Directory -Filter 'cache-*' -ErrorAction SilentlyContinue)) {
            $stalePid = 0
            if (-not [int]::TryParse($stale.Name.Substring('cache-'.Length), [ref] $stalePid)) { continue }
            if ($stalePid -eq $PID) { continue }
            if (Get-Process -Id $stalePid -ErrorAction SilentlyContinue) { continue }
            Write-Host "==== proofs: sweeping $($stale.Name), left by a run that did not finish" -ForegroundColor DarkGray
            Remove-Item $stale.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    $cache = Join-Path $cacheRoot "cache-$PID"
    $script:invocationCacheIsOurs = $true
}
$script:invocationCache = $cache
Write-Host "==== proofs: cache $cache$(if (-not $script:invocationCacheIsOurs) { ' (-CacheDir; left in place at exit)' })" -ForegroundColor Cyan

if ($null -ne $cachedReadThreshold) {
    $guarded = @($Modules | Where-Object { $fastest.ContainsKey($_) -and $fastest[$_] -ge $cachedReadFrom })
    $unguarded = @($Modules | Where-Object { $guarded -notcontains $_ })
    Write-Host ("==== proofs: cached-read threshold ${cachedReadThreshold}s, applied to the $($guarded.Count) module(s) whose recorded fastest cold check is ${cachedReadFrom}s or more" +
        $(if ($unguarded.Count -gt 0) { "; $($unguarded -join ', ') can genuinely check faster and rest on the cache provenance check alone" } else { '' })) -ForegroundColor Cyan
}

# THE CACHE PROVENANCE CHECK (Phase 402) — the second writer, caught directly rather than inferred
# from the clock. Only this run's prover writes the cache, one invocation at a time, so the cache's
# state is RECORDED after each invocation and must be found unchanged before the next; and the pinned
# prover writes a module's `.checked` file only when it checks that module itself — never for a
# dependency it checks on the way, measured 2026-10-07 and held by the kit tests' P arms — so a
# module's own file cannot legitimately be there before its cold check. A file's state is its length
# and the SHA-256 of its bytes, NOT its last write time: a timestamp is as coarse as the filesystem
# keeps it and can be set back by whoever wrote the file, so a same-size rewrite inside one clock tick,
# or one that restored the old time, would read as unchanged. The bytes cannot. The cost is one read
# of the cache per invocation, which is small beside the prover's. What it cannot see is a writer active only DURING one
# invocation and silent after it, whose files the next record takes as this run's own; that is the
# window the cached-read threshold backstops (Phase 399), and a writer whose files include a model
# not yet checked is caught anyway, by that model's own `.checked` file.
function Get-CacheState([string] $dir) {
    $state = @{}
    if (Test-Path $dir) {
        foreach ($f in Get-ChildItem $dir -File -Recurse -Force) {
            $state[[System.IO.Path]::GetRelativePath($dir, $f.FullName)] = "$($f.Length):$((Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash)"
        }
    }
    $state
}

# What moved between two recorded states, one line per file.
function Compare-CacheState([hashtable] $recorded, [hashtable] $now) {
    $moved = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $now.Keys) {
        if (-not $recorded.ContainsKey($name)) { $moved.Add("$name appeared") }
        elseif ($recorded[$name] -ne $now[$name]) { $moved.Add("$name was rewritten") }
    }
    foreach ($name in $recorded.Keys) {
        if (-not $now.ContainsKey($name)) { $moved.Add("$name vanished") }
    }
    , @($moved | Sort-Object)
}

function Assert-CacheProvenance([hashtable] $recorded, [string] $module, [int] $run, [bool] $cold) {
    $moved = Compare-CacheState $recorded (Get-CacheState $cache)
    if ($moved.Count -gt 0) {
        Fail ("a SECOND WRITER in the cache: before $module.fst on run $run of $Runs, $($moved.Count) file(s) in $cache moved since this run's last prover invocation, which nothing in this run did — $($moved -join '; '). " +
            'Every time measured from here on would be read off a cache this run does not own. Check for another check.ps1 or fstar process against this cache (see section 3).')
    }
    if ($cold) {
        $own = @(Get-ChildItem $cache -File -Force -Filter "$module.fst.checked*" -ErrorAction SilentlyContinue)
        if ($own.Count -gt 0) {
            Fail ("$module.fst is NOT about to be checked cold on run $run of ${Runs}: its own $($own[0].Name) is already in $cache. " +
                'This run has not checked it, and the pinned prover writes a module''s .checked file only when it checks that module itself, so a SECOND WRITER put it there (see section 3).')
        }
    }
}

# Phase 399 — each run's contention facts, for -SummaryFile: the pre-flight snapshot and the factor
# section 3c computes (or why it computed none). Collected whatever -SummaryFile says; written only
# on a green leg.
$runFacts = [System.Collections.Generic.List[object]]::new()
$moduleCosts = [System.Collections.Generic.List[object]]::new()

for ($run = 1; $run -le $Runs; $run++) {
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
    New-Item -ItemType Directory -Force $cache | Out-Null
    $cacheState = Get-CacheState $cache

    # The PRE-FLIGHT line (Phase 166), at the head of every run rather than once per invocation:
    # contention is what changes between run 1 and run 3, so a number taken once says nothing about
    # the run that actually went wrong.
    $preflight = Get-ResourceSnapshot
    Write-Host "==== proofs: pre-flight — run $run of $Runs, $preflight" -ForegroundColor Cyan

    # This run's contention sample (Phase 171): one ratio per untouched module with a recorded
    # measurement worth dividing by. Per RUN and not per invocation, for the pre-flight line's own
    # reason — contention is what changes between run 1 and run 3, so a number taken once says
    # nothing about the run that actually went wrong.
    $runRatios = [System.Collections.Generic.List[double]]::new()

    foreach ($module in $Modules) {
        # ONE bounded retry per module per run (Phase 166). `$attempt` is the bound, and it is a
        # number rather than a flag so that the log can say which attempt a line is about.
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            $isRetry = $attempt -gt 1
            $suffix = if ($isRetry) { ".run$run.retry" } else { ".run$run" }
            Assert-CacheProvenance $cacheState $module $run (-not $isRetry)
            if ($BeforeInvocation -and -not $isRetry) { & $BeforeInvocation $module $run $cache }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $checked = Invoke-Prover @(
                '--z3rlimit', $ZRlimit, '--quake', $Quake, '--report_assumes', 'error',
                '--cache_checked_modules', '--cache_dir', $cache, "$module.fst"
            ) (Join-Path $logsDir "$module$suffix.check.log")
            $sw.Stop()
            # Whatever this invocation wrote — verified, refuted or aborted — is this run's own.
            $cacheState = Get-CacheState $cache
            if ($AfterInvocation) { & $AfterInvocation $module $run $cache }
            $seconds = [int]$sw.Elapsed.TotalSeconds

            if ($checked.ExitCode -ne 0) {
                # REFUTATION — the prover said something was wrong. Reported in the words the leg
                # has always used, with the prover's own code, and never retried.
                if (Test-ProverDiagnostic $checked.Lines) {
                    Fail "$module.fst did NOT verify (run $run of $Runs)" (Get-RefutationExitCode $checked.ExitCode)
                }

                # ABORT — the prover exited non-zero having reported nothing. Whatever happened,
                # nothing was refuted.
                Write-Host "==== proofs: $module.fst ABORTED (no diagnostic) — exit $($checked.ExitCode) after ${seconds}s on run $run of $Runs, attempt $attempt of 2. Nothing was refuted: the prover printed no error line (neither the '* Error NNN' nor the '(Error NNN)' spelling), no 'N errors were reported' summary, no 'Failed to prove', no 'Unexpected' and no failing-quake line. Transcript: $($checked.LogPath)" -ForegroundColor Yellow
                Write-Host "==== proofs: at the abort — $(Get-ResourceSnapshot)" -ForegroundColor Yellow

                if ($isRetry) {
                    Fail ("$module.fst ABORTED (no diagnostic) TWICE on run $run of $Runs — the bounded retry is spent. " +
                        'This is NOT a refutation and the model is not implicated: the prover died with nothing to say, twice. ' +
                        "Read $($checked.LogPath) and the attempt before it, and the pre-flight lines above for what else was on the machine.") $ExitAbort
                }

                Write-Host "==== proofs: retrying $module.fst once — the retry is BOUNDED (one per module per run) and its timing is a WARM measurement, so it is compared to neither the budget nor the cached-read threshold" -ForegroundColor Yellow
                continue
            }

            # AN EXIT OF 0 IS NOT A VERDICT ON ITS OWN (recorded 2026-09-25). The green line
            # below is printed only when the prover exited 0 AND said nothing was wrong: the pinned
            # prover is measured to exit 0 over `* Error 317` at extraction (section 4), and a leg
            # that trusted the exit code alone printed `verified` over an Error 12 in a sibling
            # copy of this kit. A diagnostic on a zero exit is a REFUTATION, never retried.
            if (Test-ProverDiagnostic $checked.Lines) {
                Write-Host "==== proofs: $module.fst — the prover exited 0 but reported an error; see $($checked.LogPath)" -ForegroundColor Red
                Fail "$module.fst did NOT verify (run $run of $Runs)" 1
            }

            $budget = if ($budgets.ContainsKey($module)) { $budgets[$module] } else { $null }

            # A RETRY's clock measures a cache the aborted attempt had already half-filled, so it
            # is not a cold run and must not be read as one — by a person or by either gate. It is
            # printed, marked, and recorded as a finding; it is compared to nothing.
            if ($isRetry) {
                $finding = "$module.fst ABORTED once on run $run of $Runs and verified on the bounded retry in ${seconds}s — a WARM measurement, compared to neither its budget nor the cached-read threshold"
                $abortFindings.Add($finding)
                Write-Host "==== proofs: $module.fst verified ON RETRY — run $run of $Runs, ${seconds}s (warm cache: NOT a cold measurement), every query $Quake/$Quake under --quake" -ForegroundColor Yellow
                break
            }

            # THE CACHED-READ GATE (Phase 399) fails HERE, before the green line, rather than joining
            # the cost findings at the end, and the asymmetry with the ceiling below it is deliberate. An overshoot
            # is a true measurement of a true cost. A check faster than a cached read costs is not a
            # measurement of anything: the prover read the module back instead of checking it, so
            # every module after it is measured by the same broken apparatus and carrying on would
            # print green lines a reader is entitled to take as evidence. The wall clock is compared
            # unrounded, since the threshold is a fraction of a second.
            if ($null -ne $cachedReadThreshold -and $fastest.ContainsKey($module) -and $fastest[$module] -ge $cachedReadFrom -and
                $sw.Elapsed.TotalSeconds -lt $cachedReadThreshold) {
                Fail ("$module.fst verified in $([Math]::Round($sw.Elapsed.TotalSeconds, 2))s on run $run of $Runs, under the ${cachedReadThreshold}s cached-read threshold — a PROBABLE CACHED READ, not a cold verification " +
                    "(its fastest genuine cold check is $($fastest[$module])s). The cache provenance check saw no second writer between invocations, so suspect one that wrote DURING this one: " +
                    "check for another check.ps1 or fstar process against this cache. If the module genuinely got that fast, record its new fastestSeconds in $budgetName and cite your phase.")
            }

            $cost = if ($null -eq $budget) { "${seconds}s (no budget)" } else { "${seconds}s/${budget}s" }
            Write-Host "==== proofs: $module.fst verified — run $run of $Runs, $cost, every query $Quake/$Quake under --quake" -ForegroundColor Green

            # The contention sample (Phase 171). A COLD attempt only — the retry path above breaks
            # before it reaches here, which is the same reason it is compared to neither gate.
            if ($measurements.ContainsKey($module) -and
                $measurements[$module] -ge $contentionMinimumSeconds -and
                ($null -eq $touchedModules -or $touchedModules -notcontains $module)) {
                $runRatios.Add($seconds / $measurements[$module])
            }

            # The CEILING. Restored by Phase 155: Phase 164 deleted this block when it added the floor
            # gate below, so from a27afbc until now an overshoot printed nothing, `$costFindings` was
            # never populated from a measured time, and `-Strict` had nothing to promote — while the
            # budget file's comments and the README both went on describing a ceiling that fired. A
            # measured 32s against a 30s budget said nothing at all. It is a WARNING and the run
            # continues, which is the half the cached-read gate above is deliberately not. Since
            # Phase 399 it is never red, under -Strict or otherwise: the run's every module time is
            # recorded beside its budget for -SummaryFile, and the overshoot is a finding there.
            $moduleCosts.Add([ordered]@{
                    module  = $module
                    run     = $run
                    seconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
                    budget  = $budget
                    percent = if ($null -ne $budget) { [int](100 * $seconds / $budget) } else { $null }
                })
            if ($null -ne $budget -and $seconds -gt $budget) {
                Add-CostFinding "$module.fst took ${seconds}s against its ${budget}s budget on run $run of $Runs — $($seconds - $budget)s over, $([int](100 * $seconds / $budget))% of budget" $run
            }

            break
        }
    }

    # ---- 3c. the run's contention factor (Phase 171) ---------------------------------------------
    #
    # Here rather than at the head of the run, because the number is read off the run's own
    # measurements and does not exist until they are all in. That ordering is why a ceiling finding
    # is printed unlabelled at the moment it fires and labelled here and in the closing verdict: at
    # the instant a module goes over budget the leg genuinely does not yet know what kind of
    # afternoon it is having, and printing a label it could not have computed would be a worse lie
    # than printing the measurement alone.
    $factor = Get-Median $runRatios.ToArray()
    $thresholdText = if ($null -eq $contentionThreshold) { '' } else { 'x{0:0.00}' -f $contentionThreshold }
    if ($null -eq $factor -or $runRatios.Count -lt $contentionMinimumSamples) {
        Write-Host ("==== proofs: contention — run $run of $Runs, NOT COMPUTED: $($runRatios.Count) untouched module(s) carried a recorded " +
            "measuredSeconds of ${contentionMinimumSeconds}s or more and the factor needs $contentionMinimumSamples. No cost finding on this run is labelled.") -ForegroundColor Yellow
    }
    else {
        $rendered = 'x{0:0.00}' -f $factor
        if ($null -eq $contentionThreshold) {
            Write-Host ("==== proofs: contention — run $run of $Runs, $rendered over $($runRatios.Count) untouched module(s). $budgetName declares no " +
                "contentionSeeding.threshold, so nothing on this run is labelled — seed one per that block's rule and this leg can tell a contended pass from a regression.") -ForegroundColor Cyan
        }
        elseif ($factor -gt $contentionThreshold) {
            $label = " — CONTENDED PASS ($rendered against a $thresholdText threshold): the modules this tree did not change ran $rendered of their recorded measurements on this run, so this figure measures the afternoon and not the module"
            $labelled = 0
            foreach ($f in $costFindings) { if ($f.Run -eq $run) { $f.Label = $label; $labelled++ } }
            Write-Host ("==== proofs: contention — run $run of $Runs, $rendered over $($runRatios.Count) untouched module(s), ABOVE the $thresholdText threshold: this was a CONTENDED pass. " +
                "$labelled cost finding(s) on this run carry the label, and a labelled finding is NOT a re-seed obligation.") -ForegroundColor Yellow
        }
        else {
            Write-Host ("==== proofs: contention — run $run of $Runs, $rendered over $($runRatios.Count) untouched module(s), at or under the $thresholdText threshold: " +
                'an ordinary pass. A cost finding on this run is about its module.') -ForegroundColor Cyan
        }
    }

    $computed = $null -ne $factor -and $runRatios.Count -ge $contentionMinimumSamples
    $runFacts.Add([ordered]@{
            run       = $run
            preflight = $preflight
            factor    = if ($computed) { [Math]::Round($factor, 2) } else { $null }
            samples   = $runRatios.Count
            threshold = $contentionThreshold
            contended = $computed -and $null -ne $contentionThreshold -and $factor -gt $contentionThreshold
        })
}

# ---- 3b. the extraction post-pass -----------------------------------------------------------------
#
# Phase 169. F*'s F# backend emits a mutual TYPE group with the `and` indented one space, which
# F# 10's parser rejects even under the oracle project's `--strict-indentation-`, so an extraction
# carrying one does not compile. The pass below re-indents exactly those lines and touches nothing
# else; the committed oracles are the NORMALISED text, so step 4's contract ("byte-identical to a
# fresh extraction") is unchanged in meaning and every oracle standing today is unchanged in bytes
# — the pass is the identity on all of them, which `kit/extraction-post-pass.tests.ps1` checks
# rather than asserts. That script also carries the go-red fixture and the retirement condition;
# the defect, the pinned prover it was observed on and the reasoning are in the helper's header.
. (Join-Path $PSScriptRoot 'extraction-post-pass.ps1')

# ---- 4. extract, and hold the committed oracle to the model ---------------------------------------

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($module in $Modules) {
    if ($ProofOnly -contains $module) {
        Write-Host "==== proofs: $module is checked, not extracted — no oracle runs it (see `$proofOnly)" -ForegroundColor Cyan
        continue
    }

    $extracted = Invoke-Prover @(
        '--cache_checked_modules', '--cache_dir', $cache, '--codegen', 'FSharp',
        '--extract', $module, '--odir', $out, "$module.fst"
    ) (Join-Path $logsDir "$module.extract.log")

    $fresh = Join-Path $out "$module.fs"
    $committed = Join-Path $OracleDir "$module.fs"

    # A FAILED EXTRACTION IS NOT ALWAYS A NON-ZERO EXIT, and finding that out is most of what this
    # block is for (Phase 166, measured against the pinned prover). Staging Phase 155's incident —
    # removing a dependency's `.checked` underneath the extraction — makes F* print `* Error 317:
    # Cross-module inlining expects all modules to be checked first` and `1 error was reported`,
    # and then EXIT 0. The pre-166 code asked only about the exit code, so the fault fell past it
    # and surfaced two lines later as `extraction produced no PMain.fs under <obj/out>` — a true
    # sentence that names the symptom and not one thing about the cause. So the failure is decided
    # by "the prover failed OR the file is not there", and only then classified.
    $extractionFailed = ($extracted.ExitCode -ne 0) -or (-not (Test-Path $fresh))

    if ($extractionFailed) {
        # APPARATUS — the leg's own machinery, not the model. Read as a proof failure, this costs a
        # session the whole log before it can establish that nothing was refuted. Named, so the next
        # reader starts from the file rather than from the model.
        $missing = Get-MissingCheckedDependency $extracted.Lines
        if ($missing) {
            Fail ("extraction of $module hit an APPARATUS fault, not a proof failure: the checked-module file it needs is missing — $missing. " +
                'The model is not implicated and nothing was refuted. The cache is per invocation and this script owns it, so something removed it ' +
                "underneath this run: check for another check.ps1 against this worktree, or a cleaner over $WorkDir. Transcript: $($extracted.LogPath)") $ExitApparatus
        }

        # An extraction that died with nothing to say is the same ABORT class as a check that did.
        # It is NOT retried — the retry is the check step's, where the module is expensive and the
        # cache is still whole; here the thing extraction needs may be exactly what went away, so a
        # second attempt asks the same broken apparatus the same question.
        if (-not (Test-ProverDiagnostic $extracted.Lines)) {
            Fail ("extraction of $module ABORTED (no diagnostic) — exit $($extracted.ExitCode), and the prover printed nothing about an undischarged query. " +
                "Nothing was refuted. Transcript: $($extracted.LogPath)") $ExitAbort
        }

        if ($extracted.ExitCode -ne 0) {
            Fail "extraction of $module to F# failed" (Get-RefutationExitCode $extracted.ExitCode)
        }

        Fail "extraction produced no $module.fs under $out"
    }

    # A file on disk and an exit of 0 are still not a clean extraction when the prover reported an
    # error beside them (recorded 2026-09-25): the diff below would compare a file the prover
    # itself said is wrong.
    if (Test-ProverDiagnostic $extracted.Lines) {
        Fail "extraction of $module to F# failed — the prover exited 0 but reported an error. Transcript: $($extracted.LogPath)"
    }

    # Compare LF-normalised: the extractor writes LF and the repository pins LF, but a checkout
    # with autocrlf on would otherwise fail this for a reason that is not the model.
    $freshText = (Get-Content $fresh -Raw).Replace("`r`n", "`n")

    # THE POST-PASS (Phase 169, section 3b) — between the extraction and the byte diff, so the
    # committed oracle is held to text F# can parse. It fires only on a mutual TYPE group, which no
    # model in this directory has yet, so this line prints nothing and moves nothing today. The next
    # mutually recursive model is what it is here for, and when it fires it SAYS SO: a silent
    # rewrite between an extraction and the artefact it is diffed against is exactly the kind of
    # step that should never be invisible.
    $postPassLines = Test-ExtractionMutualTypeDefect $freshText
    if ($postPassLines.Count -gt 0) {
        $freshText = Repair-ExtractionMutualTypeGroup $freshText
        Write-Host ("==== proofs: the extraction post-pass re-indented $($postPassLines.Count) mutual-type-group " +
            "``and`` line(s) in $module (F* $pinnedVersion emits them one space in, which F# rejects — " +
            'see kit/extraction-post-pass.ps1)') -ForegroundColor Cyan
    }

    $committedText = if (Test-Path $committed) { (Get-Content $committed -Raw).Replace("`r`n", "`n") } else { '' }

    if ($Extract) {
        [System.IO.File]::WriteAllText($committed, $freshText, [System.Text.UTF8Encoding]::new($false))
        Write-Host "==== proofs: wrote the fresh extraction to oracle/$module.fs — commit it" -ForegroundColor Yellow
    }
    elseif ($freshText -ne $committedText) {
        Write-Host "==== proofs: the committed oracle (oracle/$module.fs) is NOT the extraction of $module.fst." -ForegroundColor Red
        Write-Host "     Fresh extraction: $fresh" -ForegroundColor Red
        Write-Host "     Re-extract with: pwsh ./proofs/check.ps1 -Extract   (then commit the result)" -ForegroundColor Red
        Fail "oracle drift"
    }
    else {
        Write-Host "==== proofs: oracle/$module.fs is byte-identical to a fresh extraction" -ForegroundColor Green
    }
}

# ---- 4b. the oracle is independent of production (Phase 399) --------------------------------------
#
# After extraction, so it reads the oracle the diff above has just held to the model (or that
# -Extract has just written), and before the host step, so no differential runs over an oracle that
# could be comparing production with itself. The guard and its argument are defined above, beside
# -GuardOnly.
Assert-OracleIndependence

# ---- 5. the oracle host --------------------------------------------------------------------------

if (-not $SkipOracleHost -and $HostFilters.Count -gt 0) {
    if (-not $HostProject -or -not $HostProjectFile) {
        Fail 'the caller declared host filters but no -HostProject / -HostProjectFile to run them in'
    }
    Push-Location $RepoRoot
    try {
        dotnet build $HostProjectFile --nologo
        if ($LASTEXITCODE -ne 0) { Fail "the test project did not build — HOST step, $HostProjectFile, exit $LASTEXITCODE" $LASTEXITCODE }

        # `$hostStep` and not `$host`: `$Host` is a PowerShell automatic variable and a foreach
        # over it is a hard error, which is the kind of thing that only shows up on the first red
        # run rather than on the first green one.
        foreach ($hostStep in $HostFilters) {
            $filter = $hostStep.Filter
            dotnet run --project $HostProject --no-build -- --filter $filter
            if ($LASTEXITCODE -ne 0) { Fail "$($hostStep.Failure) — HOST step, filter '$filter', exit $LASTEXITCODE" $LASTEXITCODE }
        }
    }
    finally { Pop-Location }
}
else {
    # Said out loud, because the verdict line below is the same word either way (Phase 221): a
    # green with no host step is a statement about the check and extract steps only, and a reader
    # citing it must be able to see that from the log rather than from the invocation.
    $why = if ($SkipOracleHost) { '-SkipOracleHost' } else { 'the caller declared no -HostFilters' }
    Write-Host "==== proofs: the HOST step did NOT run ($why) — this leg's green covers the check and extract steps only" -ForegroundColor Yellow
}

# ---- 6. the cost verdict ---------------------------------------------------------------------------
#
# Last, so that every finding is in hand and none of them can stop the evidence being produced: a
# leg that went red on the clock before running the differential would hide a real disagreement
# behind a slow afternoon.

if ($abortFindings.Count -gt 0) {
    Write-Host "==== proofs: $($abortFindings.Count) ABORT(s) were retried and passed on this leg:" -ForegroundColor Yellow
    foreach ($f in $abortFindings) { Write-Host "     $f" -ForegroundColor Yellow }
    Write-Host '     An abort is the prover dying, not a lemma failing, so the leg is GREEN: every model verified.' -ForegroundColor Yellow
    Write-Host '     What it is NOT is a clean measurement — read the pre-flight lines above for what else was on' -ForegroundColor Yellow
    Write-Host "     the machine, and the transcripts under $logsDir. A module that aborts repeatedly across legs is" -ForegroundColor Yellow
    Write-Host "     a finding about this machine or about that model's memory appetite, and is worth a phase." -ForegroundColor Yellow
}

if ($costFindings.Count -gt 0) {
    $labelledFindings = @($costFindings | Where-Object { $_.Label })
    $unlabelledFindings = @($costFindings | Where-Object { -not $_.Label })

    Write-Host "==== proofs: $($costFindings.Count) cost finding(s) against the budgets in ${budgetName}:" -ForegroundColor Yellow
    foreach ($f in $costFindings) { Write-Host "     $($f.Text)$($f.Label)" -ForegroundColor Yellow }
    Write-Host '     A budget is a smoke detector, not a gate: prover time varies by machine and by load,' -ForegroundColor Yellow
    Write-Host '     so one overshoot on a busy machine is noise and a persistent one is a regression.' -ForegroundColor Yellow
    Write-Host '     Bumping a budget is deliberate: new budgetSeconds + measuredSeconds + your phase in' -ForegroundColor Yellow
    Write-Host "     $budgetName, and a note saying what grew. See the README, ""Running it""." -ForegroundColor Yellow

    # The label's WHOLE consequence, in one place (Phase 171). A labelled finding has been shown,
    # by the modules this tree did not change, to be a measurement of the machine — so it is not a
    # re-seed obligation, and re-seeding a budget from it would raise a ceiling to fit a slow
    # afternoon, which is precisely how a budget stops meaning anything. That is the same judgement
    # Phase 162 had to make by hand, in prose, after departing from the seeding rule; what is new
    # is that the leg makes it and says so.
    if ($labelledFindings.Count -gt 0) {
        Write-Host "     $($labelledFindings.Count) finding(s) above are labelled CONTENDED PASS: the untouched modules on that run were slow too," -ForegroundColor Yellow
        Write-Host '     so those findings measure the machine. They are NOT a re-seed obligation — re-seeding from one' -ForegroundColor Yellow
        Write-Host '     raises a ceiling to fit a slow afternoon. Re-measure on a quiet run before touching a number.' -ForegroundColor Yellow
    }

    # -Strict and a COST finding (Phase 399, an operator ruling). A measured overshoot is a slow
    # COLD check — the opposite of the failure a gate exists for — so it is recorded and never red,
    # under -Strict or not: the strict run's record carries every module's time against its budget,
    # and the scheduled strict run is what trends them. What -Strict still promotes are the findings
    # that belong to no run: a module with no budget or no recorded fastest cold check, a budget for
    # a module the leg does not check, a budget file with no cached-read threshold. Those are
    # defects in a declaration, not measurements of a machine.
    $declarationFindings = @($costFindings | Where-Object { $_.Run -eq 0 })
    if ($Strict -and $declarationFindings.Count -gt 0) {
        Fail "the budget declaration is incomplete and -Strict is on ($($declarationFindings.Count) coverage finding(s) above)"
    }
    if ($Strict) {
        Write-Host '     -Strict is on and the leg stays GREEN: a cost overrun is a slow cold check, recorded as a finding, never a red leg (Phase 399).' -ForegroundColor Yellow
    }
}

Remove-InvocationCache

# Phase 399 — the run facts, for a caller that records the run. The machine is described by what a
# reader comparing two records needs: whether it was a CI runner or a local machine, the OS, the
# processor count and the total memory, and a CI runner's name. Never a local HOST name: the record
# is committed, often to a public repository, and a host name says nothing a reader can compare.
if ($SummaryFile) {
    $memory = $null
    try {
        if ($IsWindows) { $memory = '{0:N0} GB' -f ((Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory / 1GB) }
        elseif (Test-Path '/proc/meminfo') {
            $kb = [regex]::Match((Get-Content '/proc/meminfo' -Raw), 'MemTotal:\s+(\d+)')
            if ($kb.Success) { $memory = '{0:N0} GB' -f ([double]$kb.Groups[1].Value / 1MB) }
        }
    }
    catch { $memory = $null }
    $summary = [ordered]@{
        machine    = [ordered]@{
            kind       = if ($env:GITHUB_ACTIONS -eq 'true') { 'ci' } else { 'local' }
            runner     = if ($env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_NAME) { $env:RUNNER_NAME } else { $null }
            os         = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
            processors = [Environment]::ProcessorCount
            memory     = $memory
        }
        contention = @($runFacts)
        costs      = @($moduleCosts)
        findings   = @($costFindings | ForEach-Object { [ordered]@{ run = $_.Run; text = $_.Text; label = $_.Label } })
    }
    # Resolved through PowerShell, not [IO.Path]::GetFullPath: the .NET call reads the PROCESS's
    # directory, which `Set-Location` above does not move.
    [System.IO.File]::WriteAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SummaryFile),($summary | ConvertTo-Json -Depth 6) + "`n", [System.Text.UTF8Encoding]::new($false))
}

Write-Host '==== proofs: green' -ForegroundColor Green
exit 0
