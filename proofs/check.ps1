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
#   2. EXTRACT — each checked model is extracted to F# and DIFFED against its committed oracle
#               (proofs/oracle/<Module>.fs). A difference fails: the oracle the suite runs must be
#               the model the theorem is about, byte for byte. -Extract overwrites the committed
#               files with the fresh extractions instead (then commit them).
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
    [int]    $Runs = 1
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$LASTEXITCODE = 0

function Fail([string] $message, [int] $code = 1) {
    Write-Host "==== proofs: $message" -ForegroundColor Red
    exit $code
}

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
#   Limits     — Phase 149, the WIRE_FORMAT section-21 resource limits as named premises and
#                nothing else: eight constants with their captions, and the two relations the
#                specification's own argument uses. It models no enforcement and opens nothing;
#                it is here because `WireCanon` imports it, which is why it precedes it.
#   WireCanon  — Phase 149, the CANONICAL ENCODER — `Canon.escape`, `Canon.canonicalFloat` and
#                `Canon.render` clause for clause, with a reader for exactly the grammar they
#                emit, and the canonical form proved in both directions: equal bytes imply equal
#                normal forms, equal normal forms imply equal bytes. It `open`s Limits, so it
#                follows it.
$modules = @('DagFold', 'WireDecode', 'TreeOps', 'Skeleton', 'Chain', 'JsonParse', 'Preservation', 'TreeDiff', 'Limits', 'WireCanon')

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
$budgets = @{}
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
}

foreach ($module in $modules) {
    if (-not $budgets.ContainsKey($module)) {
        Add-CostFinding "$module is checked by the leg and modules.json declares no budget for it — time a cold run, budget it per the file's seeding rule, and cite your phase"
    }
}
foreach ($declared in $budgets.Keys) {
    if ($modules -notcontains $declared) {
        Add-CostFinding "modules.json budgets '$declared', which the leg does not check — drop the entry, or add the model to check.ps1's module list"
    }
}

# ---- 3. check, -Runs times from a cold cache -------------------------------------------------------

$cache = Join-Path $PSScriptRoot 'obj/cache'
$out = Join-Path $PSScriptRoot 'obj/out'

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

        if ($null -ne $budget -and $seconds -gt $budget) {
            Add-CostFinding "$module.fst took ${seconds}s against its ${budget}s budget on run $run of $Runs — $($seconds - $budget)s over, $([int](100 * $seconds / $budget))% of budget"
        }
    }
}

# ---- 4. extract, and hold the committed oracle to the model ---------------------------------------

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($module in $modules) {
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

Write-Host '==== proofs: green' -ForegroundColor Green
exit 0
