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
#               is a smoke detector and not a gate. -Strict promotes every cost finding to a red
#               leg, for a session that wants one. A fixed CI job timeout is deliberately NOT what
#               this is: a timeout says a run died and nothing about which module.
#               The clock is also measured against a declared FLOOR (Phase 164), and that one
#               IS a gate: a module that verifies in less than its floorSeconds FAILS the leg on
#               the spot, naming the module and the time. The two directions are not symmetric.
#               An overshoot is a real measurement of a real cost; an undershoot means the
#               measuring apparatus is broken — almost always a second writer in the cache — so
#               everything after it would be measured with the same broken apparatus. -NoFloor
#               is the deliberate opt-out for a machine genuinely that fast.
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
# Every prover invocation's whole output is also TEED to <WorkDir>/logs/, so a post-mortem reads
# the classification's own evidence rather than a scrollback. And every run prints a PRE-FLIGHT
# line — concurrent fstar process count, free physical memory — so a contended machine can be told
# from a broken model without asking anyone who was there.
#
# The prover is resolved from $env:FSTAR_HOME (a release directory holding bin/fstar.exe), else
# from <ProofsDir>/.fstar/ (a previous install by this script), else DOWNLOADED from the pinned
# GitHub release, hash-verified, and unpacked there. Both directories, and <WorkDir>, are expected
# to be gitignored by the adopting repository.
# Only the Windows release is pinned by the pin file's shape — the leg runs on the Windows CI
# runner and the Windows dev machines; on another OS set FSTAR_HOME to a matching release and the
# pin's version check still applies.
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
    # The per-module cost budgets and time floors. Defaults to <ProofsDir>/modules.json.
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
    # The prover flags the leg is defined by. Defaults are the values the leg was cut with.
    [int] $Quake = 3,
    [int] $ZRlimit = 40,
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [switch] $NoFloor,
    [string] $CacheDir,
    [int]    $Runs = 1
)

$ErrorActionPreference = 'Stop'

# The leg's OWN exit codes, for the two verdicts that are not a refutation (Phase 166). A
# refutation returns the PROVER's code, which is 1 in practice; these two are the leg's, so a
# caller — a CI job, a wrapper, a post-mortem grep — can tell "the model is wrong" from "the
# prover died" and from "the leg's machinery broke" without reading a line of output. 2 is already
# taken (no FSTAR_HOME on a non-Windows machine), so these start at 3.
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
$LASTEXITCODE = 0

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

function Resolve-FStar {
    if ($env:FSTAR_HOME) {
        $exe = Join-Path $env:FSTAR_HOME 'bin/fstar.exe'
        if (-not (Test-Path $exe)) { Fail "FSTAR_HOME is set to '$env:FSTAR_HOME' but bin/fstar.exe is not there" }
        return $exe
    }

    $local = Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe'
    if (Test-Path $local) { return $local }

    if (-not $IsWindows) {
        Fail "no FSTAR_HOME and this is not Windows — only the Windows release is pinned ($(Split-Path $PinFile -Leaf)); set FSTAR_HOME to an F* $($pin.fstar) release" 2
    }

    $asset = $pin.windows.asset
    $dir = Join-Path $ProofsDir '.fstar'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $zip = Join-Path $dir $asset

    if (-not (Test-Path $zip)) {
        Write-Host "==== proofs: downloading the pinned prover $($pin.fstar) ($asset)" -ForegroundColor Cyan
        Invoke-WebRequest -Uri $pin.windows.url -OutFile $zip
    }

    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $pin.windows.sha256) {
        Remove-Item $zip -Force
        Fail "the downloaded $asset does not match the pinned sha256 (got $hash, pinned $($pin.windows.sha256)); it was deleted — re-run to fetch again"
    }

    Write-Host "==== proofs: unpacking $asset" -ForegroundColor Cyan
    Expand-Archive -Path $zip -DestinationPath $dir -Force
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
    '|Quake[^\n]*(fail|Fail|FAIL)')

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
    try { $provers = @(Get-Process -Name 'fstar' -ErrorAction SilentlyContinue).Count } catch { $provers = -1 }
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
$costFindings = [System.Collections.Generic.List[string]]::new()

function Add-CostFinding([string] $message) {
    $script:costFindings.Add($message)
    Write-Host "==== proofs: COST — $message" -ForegroundColor Yellow
}

if (-not (Test-Path $BudgetFile)) {
    Fail "$budgetName is missing — it declares each module's cost budget (see the README's 'Running it')"
}

# The SHAPE is a failure where a mismatch is a finding, and the difference is whether the file can
# still be read as a budget at all. An entry with no `budgetSeconds` would otherwise arrive as 0 and
# every run would be infinitely over it — a flood of findings, and a division by zero rendering the
# percentage. A declared artefact is held to its shape by the code that consumes it.
# The FLOOR beside it (Phase 164) is held to its shape the same way WHEN IT IS THERE, and is a
# cost finding when it is ABSENT — a sibling adding a model should no more go red for a floor
# nobody has measured than for a budget nobody has measured. An absent floor degrades to exactly
# the pre-164 behaviour for that module, which is the safe direction; a floor of 0 is legal and
# means "this module genuinely checks in about a second", which is NOT the same statement as an
# absent one and reads differently in the file.
$budgets = @{}
$floors = @{}
foreach ($entry in (Get-Content $BudgetFile -Raw | ConvertFrom-Json).modules) {
    $name = $entry.module
    if ([string]::IsNullOrWhiteSpace($name)) { Fail "$budgetName carries an entry with no module name" }
    if ($budgets.ContainsKey($name)) { Fail "$budgetName declares '$name' twice" }

    $declaredBudget = $entry.budgetSeconds
    if ($declaredBudget -isnot [int] -and $declaredBudget -isnot [long] -and $declaredBudget -isnot [double]) {
        Fail "$budgetName entry '$name' has no numeric budgetSeconds"
    }
    if ([int]$declaredBudget -lt 1) { Fail "$budgetName entry '$name' has a budgetSeconds of $declaredBudget — a budget is a positive number of seconds" }

    $budgets[$name] = [int]$declaredBudget

    $declaredFloor = $entry.floorSeconds
    if ($null -ne $declaredFloor) {
        if ($declaredFloor -isnot [int] -and $declaredFloor -isnot [long] -and $declaredFloor -isnot [double]) {
            Fail "$budgetName entry '$name' has a non-numeric floorSeconds"
        }
        if ([int]$declaredFloor -lt 0) { Fail "$budgetName entry '$name' has a floorSeconds of $declaredFloor — a floor is a non-negative number of seconds" }
        if ([int]$declaredFloor -ge [int]$declaredBudget) {
            Fail "$budgetName entry '$name' has a floorSeconds of $declaredFloor at or above its budgetSeconds of $([int]$declaredBudget) — no run could satisfy both"
        }
        $floors[$name] = [int]$declaredFloor
    }
}

foreach ($module in $Modules) {
    if (-not $budgets.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and $budgetName declares no budget for it — time a cold run, budget it per the file's seeding rule, and cite your phase"
    }
    elseif (-not $floors.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and $budgetName declares no floorSeconds for it — nothing can tell an implausibly fast run of it from a real one; seed one per the file's floorSeeding rule and cite your phase"
    }
}
foreach ($declared in $budgets.Keys) {
    if ($Modules -notcontains $declared) {
        Add-CostFinding "$budgetName budgets '$declared', which the leg does not check — drop the entry, or add the model to check.ps1's module list"
    }
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

if ($NoFloor) {
    Write-Host "==== proofs: -NoFloor — the per-module time floors in $budgetName are NOT enforced on this run" -ForegroundColor Yellow
}

for ($run = 1; $run -le $Runs; $run++) {
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
    New-Item -ItemType Directory -Force $cache | Out-Null

    # The PRE-FLIGHT line (Phase 166), at the head of every run rather than once per invocation:
    # contention is what changes between run 1 and run 3, so a number taken once says nothing about
    # the run that actually went wrong.
    Write-Host "==== proofs: pre-flight — run $run of $Runs, $(Get-ResourceSnapshot)" -ForegroundColor Cyan

    foreach ($module in $Modules) {
        # ONE bounded retry per module per run (Phase 166). `$attempt` is the bound, and it is a
        # number rather than a flag so that the log can say which attempt a line is about.
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            $isRetry = $attempt -gt 1
            $suffix = if ($isRetry) { ".run$run.retry" } else { ".run$run" }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            $checked = Invoke-Prover @(
                '--z3rlimit', $ZRlimit, '--quake', $Quake, '--report_assumes', 'error',
                '--cache_checked_modules', '--cache_dir', $cache, "$module.fst"
            ) (Join-Path $logsDir "$module$suffix.check.log")
            $sw.Stop()
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

                Write-Host "==== proofs: retrying $module.fst once — the retry is BOUNDED (one per module per run) and its timing is a WARM measurement, so it feeds neither the budget nor the floor" -ForegroundColor Yellow
                continue
            }

            $budget = if ($budgets.ContainsKey($module)) { $budgets[$module] } else { $null }

            # A RETRY's clock measures a cache the aborted attempt had already half-filled, so it
            # is not a cold run and must not be read as one — by a person or by either gate. It is
            # printed, marked, and recorded as a finding; it is compared to nothing.
            if ($isRetry) {
                $finding = "$module.fst ABORTED once on run $run of $Runs and verified on the bounded retry in ${seconds}s — a WARM measurement, compared to neither its budget nor its floor"
                $abortFindings.Add($finding)
                Write-Host "==== proofs: $module.fst verified ON RETRY — run $run of $Runs, ${seconds}s (warm cache: NOT a cold measurement), every query $Quake/$Quake under --quake" -ForegroundColor Yellow
                break
            }

            $cost = if ($null -eq $budget) { "${seconds}s (no budget)" } else { "${seconds}s/${budget}s" }
            Write-Host "==== proofs: $module.fst verified — run $run of $Runs, $cost, every query $Quake/$Quake under --quake" -ForegroundColor Green

            # The CEILING. Restored by Phase 155: Phase 164 deleted this block when it added the floor
            # gate below, so from a27afbc until now an overshoot printed nothing, `$costFindings` was
            # never populated from a measured time, and `-Strict` had nothing to promote — while the
            # budget file's comments and the README both went on describing a ceiling that fired. A
            # measured 32s against a 30s budget said nothing at all. It is a WARNING and the run
            # continues, which is the half the floor below is deliberately not.
            if ($null -ne $budget -and $seconds -gt $budget) {
                Add-CostFinding "$module.fst took ${seconds}s against its ${budget}s budget on run $run of $Runs — $($seconds - $budget)s over, $([int](100 * $seconds / $budget))% of budget"
            }

            # The floor fails HERE rather than joining the cost findings at the end, and the asymmetry
            # with the ceiling just above it is deliberate. An overshoot is a true measurement of a
            # true cost, so the run should continue and produce the rest of the evidence. An
            # undershoot says the measurement itself is not to be believed — the cache was not cold —
            # and every module after it is measured by the same apparatus, so carrying on would print
            # more green lines that a reader is entitled to read as evidence and that are not.
            if (-not $NoFloor -and $floors.ContainsKey($module) -and $seconds -lt $floors[$module]) {
                Fail ("$module.fst verified in ${seconds}s on run $run of $Runs, under its $($floors[$module])s floor — that is not a cold verification. " +
                    'Almost always a second writer in the cache directory (see section 3). Check for another check.ps1 or fstar process against this worktree; ' +
                    "if this machine really is that fast, re-seed the floor per $budgetName floorSeeding and cite your phase, or pass -NoFloor for this run.")
            }

            break
        }
    }
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

# ---- 5. the oracle host --------------------------------------------------------------------------

if (-not $SkipOracleHost -and $HostFilters.Count -gt 0) {
    if (-not $HostProject -or -not $HostProjectFile) {
        Fail 'the caller declared host filters but no -HostProject / -HostProjectFile to run them in'
    }
    Push-Location $RepoRoot
    try {
        dotnet build $HostProjectFile --nologo
        if ($LASTEXITCODE -ne 0) { Fail "the test project did not build" $LASTEXITCODE }

        # `$hostStep` and not `$host`: `$Host` is a PowerShell automatic variable and a foreach
        # over it is a hard error, which is the kind of thing that only shows up on the first red
        # run rather than on the first green one.
        foreach ($hostStep in $HostFilters) {
            $filter = $hostStep.Filter
            dotnet run --project $HostProject --no-build -- --filter $filter
            if ($LASTEXITCODE -ne 0) { Fail $hostStep.Failure $LASTEXITCODE }
        }
    }
    finally { Pop-Location }
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
    Write-Host "==== proofs: $($costFindings.Count) cost finding(s) against the budgets in ${budgetName}:" -ForegroundColor Yellow
    foreach ($f in $costFindings) { Write-Host "     $f" -ForegroundColor Yellow }
    Write-Host '     A budget is a smoke detector, not a gate: prover time varies by machine and by load,' -ForegroundColor Yellow
    Write-Host '     so one overshoot on a busy machine is noise and a persistent one is a regression.' -ForegroundColor Yellow
    Write-Host '     Bumping a budget is deliberate: new budgetSeconds + measuredSeconds + your phase in' -ForegroundColor Yellow
    Write-Host "     $budgetName, and a note saying what grew. See the README, ""Running it""." -ForegroundColor Yellow
    if ($Strict) { Fail "the cost budget is exceeded and -Strict is on ($($costFindings.Count) finding(s) above)" }
}

Remove-InvocationCache
Write-Host '==== proofs: green' -ForegroundColor Green
exit 0
