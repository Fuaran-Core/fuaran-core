#Requires -Version 7.0
# fuaran-core — the proof leg (Phases 131 and 135).
#
# EVERY MODULE in $modules below goes through the same three steps, and each step can fail on its
# own. Adding a model is adding its name to that list: nothing else here is per-module.
#   1. CHECK  — proofs/<Module>.fst is verified by the PINNED F*/Z3 (proofs/fstar-pin.json) with
#               every SMT query proved three times over varying seeds (--quake 3) and every escape
#               hatch (assume / admit) reported as an error. -Runs N repeats the whole check N
#               times from a cold cache — CI asks for 3, which is exit criterion 1 made literal.
#               A run checks EVERY module before the next run starts, so -Runs still means "N
#               cold-cache verifications of everything", as it did when there was one model.
#   2. EXTRACT — each checked model is extracted to F# and DIFFED against its committed oracle
#               (proofs/oracle/<Module>.fs). A difference fails: the oracle the suite runs must be
#               the model the theorem is about, byte for byte. -Extract overwrites the committed
#               files with the fresh extractions instead (then commit them).
#   3. HOST   — the Expecto Proofs.Oracle family runs the extracted models beside the production
#               code (the differential tests). -SkipOracleHost leaves that to ./verify.ps1, which
#               already runs the whole suite.
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
$modules = @('DagFold', 'WireDecode')

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

# ---- 2. check, -Runs times from a cold cache -------------------------------------------------------

$cache = Join-Path $PSScriptRoot 'obj/cache'
$out = Join-Path $PSScriptRoot 'obj/out'

for ($run = 1; $run -le $Runs; $run++) {
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
    New-Item -ItemType Directory -Force $cache | Out-Null

    foreach ($module in $modules) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & $fstar --z3rlimit 40 --quake 3 --report_assumes error --cache_checked_modules --cache_dir $cache "$module.fst"
        if ($LASTEXITCODE -ne 0) { Fail "$module.fst did NOT verify (run $run of $Runs)" $LASTEXITCODE }
        Write-Host "==== proofs: $module.fst verified — run $run of $Runs, $([int]$sw.Elapsed.TotalSeconds)s, every query 3/3 under --quake" -ForegroundColor Green
    }
}

# ---- 3. extract, and hold the committed oracle to the model ---------------------------------------

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

# ---- 4. the oracle host --------------------------------------------------------------------------

if (-not $SkipOracleHost) {
    Push-Location (Join-Path $PSScriptRoot '..')
    try {
        dotnet build tests/Fuaran.Core.Tests/Fuaran.Core.Tests.fsproj --nologo
        if ($LASTEXITCODE -ne 0) { Fail "the test project did not build" $LASTEXITCODE }

        dotnet run --project tests/Fuaran.Core.Tests --no-build -- --filter Proofs.Oracle
        if ($LASTEXITCODE -ne 0) { Fail "the oracle host (Proofs.Oracle) is RED — an extracted model and production disagree" $LASTEXITCODE }
    }
    finally { Pop-Location }
}

Write-Host '==== proofs: green' -ForegroundColor Green
exit 0
