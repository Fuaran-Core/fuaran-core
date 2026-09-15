#Requires -Version 7.0
# fuaran-core — the proof leg (Phases 131, 135, 136 and 148).
#
# EVERY MODULE in $modules below goes through the same three steps, and each step can fail on its
# own. Adding a model is adding its name to that list AND a budget entry to modules.json: nothing
# else here is per-module.
#   1. CHECK  — proofs/<Module>.fst is verified by the PINNED F*/Z3 (proofs/fstar-pin.json) with
#               every SMT query proved three times over varying seeds (--quake 3) and every escape
#               hatch (assume / admit) reported as an error. -Runs N repeats the whole check N
#               times from a cold cache — CI asks for 3, which is exit criterion 1 made literal.
#               A run checks EVERY module before the next run starts, so -Runs still means "N
#               cold-cache verifications of everything", as it did when there was one model.
#               Each module's wall clock is MEASURED AGAINST A DECLARED BUDGET (Phase 148):
#               proofs/modules.json says what each module is expected to cost, every green line
#               prints the measured seconds beside that budget, and an overshoot is a named COST
#               warning rather than a failure — prover time varies by machine and by load, so the
#               budget is a smoke detector and not a gate. -Strict promotes every cost finding to
#               a red leg, for a session that wants one. A fixed CI job timeout is deliberately
#               NOT what this is: a timeout says a run died and nothing about which module.
#               The clock is also measured against a declared FLOOR (Phase 164), and that one
#               IS a gate: a module that verifies in less than its floorSeconds FAILS the leg on
#               the spot, naming the module and the time. The two directions are not symmetric.
#               An overshoot is a real measurement of a real cost; an undershoot means the
#               measuring apparatus is broken — almost always a second writer in the cache — so
#               everything after it would be measured with the same broken apparatus. -NoFloor
#               is the deliberate opt-out for a machine genuinely that fast.
#               The cache the cold runs use is PER INVOCATION (obj/cache-<pid>, or -CacheDir),
#               created and removed by this script, so that "cold cache" cannot be quietly
#               falsified by another run in the same worktree. See "Running it" in the README
#               for the 2026-09-14 incident that bought both of these.
#   2. EXTRACT — each checked model is extracted to F# and DIFFED against its committed oracle
#               (proofs/oracle/<Module>.fs). A difference fails: the oracle the suite runs must be
#               the model the theorem is about, byte for byte. -Extract overwrites the committed
#               files with the fresh extractions instead (then commit them). A model listed in
#               $proofOnly below is EXEMPT and says so on its own line — see that list.
#   2b. GENERATE — the one GENERATED model (Phase 150) is held to a fresh generation from the
#               pinned `idl.json` by the Proofs.Vocabulary family in step 3, which is the same
#               discipline as step 2 one level further up: step 2 says the oracle is the model,
#               and that says the model is the vocabulary the specification declares.
#   3. HOST   — two Expecto families. Proofs.Oracle runs the extracted models beside the production
#               code (the differential tests); Proofs.Ladder holds ../proofs.json — the claims
#               ladder declared as data — to this tree, so a row naming a theorem no model
#               declares, or a module in $modules with no row at all, fails the leg with the row
#               named. Two invocations rather than one prefix filter, so the two failures read as
#               what they are: a model and production disagreeing, versus the ladder and the tree
#               disagreeing. -SkipOracleHost leaves both to ./verify.ps1, which already runs the
#               whole suite.
#
# The prover is resolved from $env:FSTAR_HOME (a release directory holding bin/fstar.exe), else
# from proofs/.fstar/ (a previous install by this script), else DOWNLOADED from the pinned GitHub
# release, hash-verified, and unpacked there. proofs/.fstar/ and proofs/obj/ are gitignored.
# Only the Windows release is pinned — the leg runs on the Windows CI runner and the Windows dev
# machines; on another OS set FSTAR_HOME to a matching release and the pin's version check still
# applies.
[CmdletBinding()]
param(
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [switch] $NoFloor,
    [string] $CacheDir,
    [int]    $Runs = 1
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
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

$pin = Get-Content ./fstar-pin.json -Raw | ConvertFrom-Json
$pinnedVersion = $pin.fstar.TrimStart('v')

# The models, in the order they were cut. Each is proofs/<name>.fst with its committed extraction
# at proofs/oracle/<name>.fs; they share nothing but oracle/Prims.fs.
#   DagFold    — Phase 131, the N-lane DAG fold, with fold confluence proved.
#   WireDecode — Phase 135, the wire decode combinators, with decoder totality proved.
#   TreeOps    — Phase 133, the skeleton-op tree algebra, with the fold theorem's domain
#                hypothesis proved for it. Opens DagFold, so it follows it here.
#   Skeleton   — Phase 133, the composite: DagFold's fold theorem instantiated at TreeOps, with
#                no domain hypothesis left. Opens both, so it follows both.
#   Chain      — Phase 136, the two integrity walkers, with tamper detection proved under a
#                named injective-hash premise. Opens nothing, so its position is free.
#   JsonParse  — Phase 146, the recursive-descent JSON parser, with the depth bound, the int53
#                token guard and the exhaustiveness of the error classification proved. Opens
#                nothing, so its position is free; it is the boundary WireDecode named.
#   Preservation — Phase 138, the apply engine: totality with its rejection characterisation,
#                all-or-nothing rejection, id uniqueness preserved by every accepted operation,
#                canApply/apply agreement, and invert's round trip. Opens TreeOps, so it follows it.
#   TreeDiff   — Phase 141, the apply engine's companion in the other direction: `Diff.toOps`'s
#                two refusals characterised exactly, the four-pass emission order, what each pass
#                guarantees about the block it emits, and the container-aware mirror's pre-emptive
#                refusal. Opens TreeOps (and through it DagFold), so it follows both.
#   Vocabulary — Phase 150, and the only GENERATED model here: the wire-format IDL's own
#                vocabulary — its types, its discriminated encoder and its tag-dispatch decoder —
#                emitted from `idl.json` by `Fuaran.Core.Idl.Codegen`'s F* target. Opens
#                WireDecode, so it follows it.
#   VocabularyProofs — Phase 150, the theorems over that model, emitted by the same walk: the
#                round trip `dec_node (enc_node x) == Ok x` over EVERY value of every modelled
#                type, and the decoder's totality. Generated for the reason the model is: a
#                hand-written proof over a vocabulary is a theorem about the day it was written.
#                Opens Vocabulary, so it follows it.
$modules = @('DagFold', 'WireDecode', 'TreeOps', 'Skeleton', 'Chain', 'JsonParse', 'Preservation', 'TreeDiff', 'Vocabulary', 'VocabularyProofs')

# ---- 1. resolve the prover ---------------------------------------------------------------------

function Resolve-FStar {
    if ($env:FSTAR_HOME) {
        $exe = Join-Path $env:FSTAR_HOME 'bin/fstar.exe'
        if (-not (Test-Path $exe)) { Fail "FSTAR_HOME is set to '$env:FSTAR_HOME' but bin/fstar.exe is not there" }
        return $exe
    }

    $local = Join-Path $PSScriptRoot '.fstar/fstar/bin/fstar.exe'
    if (Test-Path $local) { return $local }

    if (-not $IsWindows) {
        Fail "no FSTAR_HOME and this is not Windows — only the Windows release is pinned (fstar-pin.json); set FSTAR_HOME to an F* $($pin.fstar) release" 2
    }

    $asset = $pin.windows.asset
    $dir = Join-Path $PSScriptRoot '.fstar'
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

# ---- 2. the declared cost budgets ----------------------------------------------------------------

# modules.json says what each module is expected to cost on a cold run. It is a committed declared
# artefact, so its ABSENCE is a defect and fails here; a mismatch between it and $modules is a cost
# finding rather than a failure, because a sibling adding a model should not have their leg go red
# for a budget nobody could have measured yet — the finding names the module and what to do.
$costFindings = [System.Collections.Generic.List[string]]::new()

function Add-CostFinding([string] $message) {
    $script:costFindings.Add($message)
    Write-Host "==== proofs: COST — $message" -ForegroundColor Yellow
}

$budgetFile = Join-Path $PSScriptRoot 'modules.json'
if (-not (Test-Path $budgetFile)) {
    Fail "modules.json is missing — it declares each module's cost budget (see the README's 'Running it')"
}

# The SHAPE is a failure where a mismatch is a finding, and the difference is whether the file can
# still be read as a budget at all. An entry with no `budgetSeconds` would otherwise arrive as 0 and
# every run would be infinitely over it — a flood of findings, and a division by zero rendering the
# percentage. A declared artefact is held to its shape by the code that consumes it.
# The FLOOR beside it (Phase 164) is held to its shape the same way WHEN IT IS THERE, and is a
# cost finding when it is ABSENT — a sibling adding a model should no more go red for a floor
# nobody has measured than for a budget nobody has measured. An absent floor degrades to exactly
# the pre-164 behaviour for that module, which is the safe direction; a floor of 0 is legal and
# means "this module genuinely checks in about a second" (see Skeleton), which is NOT the same
# statement as an absent one and reads differently in the file.
$budgets = @{}
$floors = @{}
foreach ($entry in (Get-Content $budgetFile -Raw | ConvertFrom-Json).modules) {
    $name = $entry.module
    if ([string]::IsNullOrWhiteSpace($name)) { Fail 'modules.json carries an entry with no module name' }
    if ($budgets.ContainsKey($name)) { Fail "modules.json declares '$name' twice" }

    $declaredBudget = $entry.budgetSeconds
    if ($declaredBudget -isnot [int] -and $declaredBudget -isnot [long] -and $declaredBudget -isnot [double]) {
        Fail "modules.json entry '$name' has no numeric budgetSeconds"
    }
    if ([int]$declaredBudget -lt 1) { Fail "modules.json entry '$name' has a budgetSeconds of $declaredBudget — a budget is a positive number of seconds" }

    $budgets[$name] = [int]$declaredBudget

    $declaredFloor = $entry.floorSeconds
    if ($null -ne $declaredFloor) {
        if ($declaredFloor -isnot [int] -and $declaredFloor -isnot [long] -and $declaredFloor -isnot [double]) {
            Fail "modules.json entry '$name' has a non-numeric floorSeconds"
        }
        if ([int]$declaredFloor -lt 0) { Fail "modules.json entry '$name' has a floorSeconds of $declaredFloor — a floor is a non-negative number of seconds" }
        if ([int]$declaredFloor -ge [int]$declaredBudget) {
            Fail "modules.json entry '$name' has a floorSeconds of $declaredFloor at or above its budgetSeconds of $([int]$declaredBudget) — no run could satisfy both"
        }
        $floors[$name] = [int]$declaredFloor
    }
}

foreach ($module in $modules) {
    if (-not $budgets.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and modules.json declares no budget for it — time a cold run, budget it per the file's seeding rule, and cite your phase"
    }
    elseif (-not $floors.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and modules.json declares no floorSeconds for it — nothing can tell an implausibly fast run of it from a real one; seed one per the file's floorSeeding rule and cite your phase"
    }
}
foreach ($declared in $budgets.Keys) {
    if ($modules -notcontains $declared) {
        Add-CostFinding "modules.json budgets '$declared', which the leg does not check — drop the entry, or add the model to check.ps1's module list"
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

$out = Join-Path $PSScriptRoot 'obj/out'
$cacheRoot = Join-Path $PSScriptRoot 'obj'

if ($CacheDir) {
    # A caller-named directory is CLEARED at each run head, exactly as the default one is —
    # otherwise -CacheDir would silently mean "warm", which is the opposite of what this section
    # is for. So refuse one holding anything that is not a checked-module file: the flag is for
    # naming where the cache goes, and pointing it at a directory with other contents in it would
    # delete them. It is not removed at exit; the caller named it, so the caller keeps it.
    $cache = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $CacheDir))
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
    Write-Host '==== proofs: -NoFloor — the per-module time floors in modules.json are NOT enforced on this run' -ForegroundColor Yellow
}

for ($run = 1; $run -le $Runs; $run++) {
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
    New-Item -ItemType Directory -Force $cache | Out-Null

    foreach ($module in $modules) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & $fstar --z3rlimit 40 --quake 3 --report_assumes error --cache_checked_modules --cache_dir $cache "$module.fst"
        if ($LASTEXITCODE -ne 0) { Fail "$module.fst did NOT verify (run $run of $Runs)" $LASTEXITCODE }

        $seconds = [int]$sw.Elapsed.TotalSeconds
        $budget = if ($budgets.ContainsKey($module)) { $budgets[$module] } else { $null }
        $cost = if ($null -eq $budget) { "${seconds}s (no budget)" } else { "${seconds}s/${budget}s" }
        Write-Host "==== proofs: $module.fst verified — run $run of $Runs, $cost, every query 3/3 under --quake" -ForegroundColor Green

        # The floor fails HERE rather than joining the cost findings at the end, and the asymmetry
        # with the ceiling three lines up is deliberate. An overshoot is a true measurement of a
        # true cost, so the run should continue and produce the rest of the evidence. An
        # undershoot says the measurement itself is not to be believed — the cache was not cold —
        # and every module after it is measured by the same apparatus, so carrying on would print
        # more green lines that a reader is entitled to read as evidence and that are not.
        if (-not $NoFloor -and $floors.ContainsKey($module) -and $seconds -lt $floors[$module]) {
            Fail ("$module.fst verified in ${seconds}s on run $run of $Runs, under its $($floors[$module])s floor — that is not a cold verification. " +
                'Almost always a second writer in the cache directory (see section 3). Check for another check.ps1 or fstar process against this worktree; ' +
                'if this machine really is that fast, re-seed the floor per modules.json floorSeeding and cite your phase, or pass -NoFloor for this run.')
        }
    }
}

# ---- 4. extract, and hold the committed oracle to the model ---------------------------------------

# Phase 150 — the models that are CHECKED but not EXTRACTED, and why an exemption exists at all.
#
# An oracle is here so the Expecto differential can run the extracted model beside the production
# code over the same inputs. Two of the models have no production code on this side to run beside:
# `Vocabulary` models the vocabulary an `idl.json` DECLARES, and the decoder it models is one a
# GENERATOR emits into a consuming host, not one this repository ships; `VocabularyProofs` is
# lemmas, which erase. Extracting them anyway would commit ~400 KB of generated F# that nothing
# compiles, calls or compares — which is what an oracle is supposed to be the opposite of. (The
# extractor also emits a mutual TYPE group with the `and` indented one space, which F# 10's parser
# rejects outright; that is a real finding about the F# backend, and it is not the reason for this
# exemption — an oracle nothing runs would not be worth committing even if it compiled.)
#
# The exemption is NARROW and it is not a hole in the discipline: what step 2 buys for the other
# models — "the artefact is the model, byte for byte" — these two get from the GENERATION diff in
# step 3 instead, one level further up, against the `idl.json` they are generated from.
$proofOnly = @('Vocabulary', 'VocabularyProofs')

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($module in $modules) {
    if ($proofOnly -contains $module) {
        Write-Host "==== proofs: $module is checked, not extracted — no oracle runs it (see \$proofOnly)" -ForegroundColor Cyan
        continue
    }

    & $fstar --cache_checked_modules --cache_dir $cache --codegen FSharp --extract $module --odir $out "$module.fst"
    if ($LASTEXITCODE -ne 0) { Fail "extraction of $module to F# failed" $LASTEXITCODE }

    $fresh = Join-Path $out "$module.fs"
    $committed = Join-Path $PSScriptRoot "oracle/$module.fs"
    if (-not (Test-Path $fresh)) { Fail "extraction produced no $module.fs under $out" }

    # Compare LF-normalised: the extractor writes LF and the repository pins LF, but a checkout
    # with autocrlf on would otherwise fail this for a reason that is not the model.
    $freshText = (Get-Content $fresh -Raw).Replace("`r`n", "`n")
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

if (-not $SkipOracleHost) {
    Push-Location (Join-Path $PSScriptRoot '..')
    try {
        dotnet build tests/Fuaran.Core.Tests/Fuaran.Core.Tests.fsproj --nologo
        if ($LASTEXITCODE -ne 0) { Fail "the test project did not build" $LASTEXITCODE }

        # Phase 150 — the GENERATION diff, beside the extraction diff above and for the same
        # reason one step further up. Step 4 holds each committed oracle to a fresh EXTRACTION of
        # its model, so the oracle the suite runs is the model the theorem is about; this holds
        # the committed `Vocabulary.fst` / `VocabularyProofs.fst` to a fresh GENERATION from the
        # pinned `idl.json`, so the vocabulary the theorem is about is the vocabulary the
        # specification declares. An IDL that moves without a regeneration is VOCABULARY DRIFT,
        # and this is where it is named.
        #
        # It runs HERE rather than ahead of the prover because the generator is F# and the check
        # above is the first thing in this script that has a built test project to hand — and
        # because the leg fails either way: a stale model still verifies, and then this step
        # reports what it is stale against. Moving it earlier would restructure the CHECK step.
        dotnet run --project tests/Fuaran.Core.Tests --no-build -- --filter Proofs.Vocabulary
        if ($LASTEXITCODE -ne 0) { Fail "the generated vocabulary (Proofs.Vocabulary) is RED — the committed F* model and the pinned idl.json disagree, or the target refused a construct" $LASTEXITCODE }

        dotnet run --project tests/Fuaran.Core.Tests --no-build -- --filter Proofs.Oracle
        if ($LASTEXITCODE -ne 0) { Fail "the oracle host (Proofs.Oracle) is RED — an extracted model and production disagree" $LASTEXITCODE }

        dotnet run --project tests/Fuaran.Core.Tests --no-build -- --filter Proofs.Ladder
        if ($LASTEXITCODE -ne 0) { Fail "the claims ladder (Proofs.Ladder) is RED — ../proofs.json and this tree disagree; the failing row and clause are named above" $LASTEXITCODE }
    }
    finally { Pop-Location }
}

# ---- 6. the cost verdict ---------------------------------------------------------------------------
#
# Last, so that every finding is in hand and none of them can stop the evidence being produced: a
# leg that went red on the clock before running the differential would hide a real disagreement
# behind a slow afternoon.

if ($costFindings.Count -gt 0) {
    Write-Host "==== proofs: $($costFindings.Count) cost finding(s) against the budgets in modules.json:" -ForegroundColor Yellow
    foreach ($f in $costFindings) { Write-Host "     $f" -ForegroundColor Yellow }
    Write-Host '     A budget is a smoke detector, not a gate: prover time varies by machine and by load,' -ForegroundColor Yellow
    Write-Host '     so one overshoot on a busy machine is noise and a persistent one is a regression.' -ForegroundColor Yellow
    Write-Host '     Bumping a budget is deliberate: new budgetSeconds + measuredSeconds + your phase in' -ForegroundColor Yellow
    Write-Host '     modules.json, and a note saying what grew. See the README, "Running it".' -ForegroundColor Yellow
    if ($Strict) { Fail "the cost budget is exceeded and -Strict is on ($($costFindings.Count) finding(s) above)" }
}

Remove-InvocationCache
Write-Host '==== proofs: green' -ForegroundColor Green
exit 0
