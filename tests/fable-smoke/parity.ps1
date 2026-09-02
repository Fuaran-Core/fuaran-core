#Requires -Version 7.0
# fuaran-core — the Fable VALUE-parity leg (Phase 118).
#
# WHAT IT ADDS TO THE GATE. `./verify.ps1`'s Fable step is a COMPILE gate: it proves every public
# package transpiles. It cannot notice that the transpiled code computes a DIFFERENT NUMBER, and
# nothing in a .NET test suite can notice it either. Both halves of that gap have cost real defects
# here: `Hash.fnv1a` was value-divergent between the two pipelines behind a fully green suite until
# 0.6.0, and `Wire.Json.render` THREW under Fable for any float — a public encode surface, unusable
# in a browser — from before 0.5.0 until Phase 118, with the compile gate green throughout.
#
# So this leg RUNS the vector table (`ParityVectors.fs`) on both pipelines and byte-compares the
# output. The same table is pinned against committed expected bytes on the .NET side by
# `tests/Fuaran.Core.Tests/ParityVectorTests.fs`; the two claims are different and neither implies
# the other — a defect that moves both pipelines identically passes here and fails there, and one
# that moves only the transpiled side does the reverse.
#
# IT FAILS WITHOUT `node`; IT NEVER SKIPS. The older `tests/hash-parity-probe/run-parity-probe.ps1`
# skips green when no JS runtime is present, which is right for a probe run by hand and wrong for a
# gate leg: a check that reports success on a machine where it did not run teaches everyone to trust
# a green that means nothing.
#
# RUN IT STANDALONE from anywhere:  pwsh ./tests/fable-smoke/parity.ps1
# Exit 0 = every vector is byte-identical on both pipelines.
[CmdletBinding()]
param(
    # Reuse an ALREADY-EMITTED Fable output directory instead of compiling one. Only correct
    # immediately after the emitting step (this is how `./verify.ps1` wires the leg in, on the line
    # after its own `dotnet fable`) — pointed at a stale directory it would compare today's .NET
    # values against yesterday's transpiled ones, which is the one failure mode a value gate must
    # not have. Omit it and the leg compiles its own output and is self-contained.
    [string] $UseFreshlyEmitted,

    # Keep the emitted JS and both captured outputs for inspection instead of comparing quietly.
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'

# Resolved against the CALLER's directory before the working directory moves: the wiring line in
# `./verify.ps1` passes a repo-root-relative path, and this script runs from its own folder so the
# `dotnet` invocations below can name the project by file.
$emittedArg =
    if ($UseFreshlyEmitted) { [IO.Path]::GetFullPath($UseFreshlyEmitted, $PWD.ProviderPath) } else { '' }

Set-Location $PSScriptRoot

# Seeded because every guard below reads it: `$LASTEXITCODE` is `$null` until a native command runs,
# and `$null -ne 0` is true, so an unseeded guard can fail on a stage that never executed.
$global:LASTEXITCODE = 0

# `Get-Command` returns EVERY match on PATH; pin the first so `$node.Source` is a string and not an
# array (the workspace launcher convention, for the same reason it applies to the npm shims — a
# bare `node` is a real executable, so no shim indirection is needed beyond this).
$node = Get-Command node -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1

if (-not $node) {
    Write-Host '==== parity: FAILED — no `node` on PATH.' -ForegroundColor Red
    Write-Host '     This leg RUNS the transpiled code; without a JS runtime the cross-pipeline'
    Write-Host '     value claim cannot be made at all, and reporting it as skipped-green would'
    Write-Host '     assert exactly what was not checked. Install Node, or run ./verify.ps1 with'
    Write-Host '     the leg disabled and know that the Fable side is compile-checked only.'
    exit 1
}

# ---- the .NET side -------------------------------------------------------------------------
# Filtered to the `VEC ` lines so build chatter can never enter the comparison.
$dotnetOut = @(dotnet run --project FableSmoke.fsproj -- --vectors) | Where-Object { $_ -like 'VEC *' }

if ($LASTEXITCODE -ne 0) {
    Write-Host '==== parity: FAILED — the .NET run of the vector table did not succeed' -ForegroundColor Red
    exit 1
}

# ---- the transpiled side -------------------------------------------------------------------
if ($emittedArg) {
    if (-not (Test-Path $emittedArg)) {
        Write-Host "==== parity: FAILED — -UseFreshlyEmitted '$UseFreshlyEmitted' does not exist ($emittedArg)" -ForegroundColor Red
        exit 1
    }

    $outDir = $emittedArg
} else {
    $outDir = Join-Path $PSScriptRoot 'out-parity'
    Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue

    # `dotnet fable` needs no npm/npx, so the workspace Invoke-Npm convention does not apply here.
    dotnet fable FableSmoke.fsproj -o $outDir --noCache | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Host '==== parity: FAILED — the Fable compile of the vector table did not succeed' -ForegroundColor Red
        exit 1
    }
}

$entry = Join-Path $outDir 'Program.js'

if (-not (Test-Path $entry)) {
    Write-Host "==== parity: FAILED — no emitted entry point at $entry" -ForegroundColor Red
    exit 1
}

$fableOut = @(& $node.Source $entry --vectors) | Where-Object { $_ -like 'VEC *' }

if ($LASTEXITCODE -ne 0) {
    Write-Host '==== parity: FAILED — the node run of the vector table did not succeed' -ForegroundColor Red
    exit 1
}

# ---- compare -------------------------------------------------------------------------------
# Both emptiness and a count mismatch are failures in their own right: one side not producing the
# table at all would otherwise be reported as "0 divergences", which is the vacuous green this
# whole leg exists to refuse.
if ($dotnetOut.Count -eq 0) {
    Write-Host '==== parity: FAILED — the .NET run emitted no vector lines at all' -ForegroundColor Red
    exit 1
}

if ($fableOut.Count -ne $dotnetOut.Count) {
    Write-Host "==== parity: FAILED — .NET emitted $($dotnetOut.Count) vectors, the node run emitted $($fableOut.Count)" -ForegroundColor Red
    exit 1
}

# `-cne` — case-SENSITIVE. Every value here is lowercase hex or a canonical numeric/JSON layout, and
# a case difference in a digest is a divergence, not a formatting preference.
$diverged = @(0..($dotnetOut.Count - 1) | Where-Object { $dotnetOut[$_] -cne $fableOut[$_] })

if ($diverged.Count -gt 0) {
    Write-Host "==== parity: FAILED — $($diverged.Count) of $($dotnetOut.Count) vectors differ between the pipelines" -ForegroundColor Red
    Write-Host '     (.NET is the canonical side — see DECISIONS.md D16)'

    foreach ($i in $diverged | Select-Object -First 10) {
        Write-Host "       .NET  $($dotnetOut[$i])"
        Write-Host "       Fable $($fableOut[$i])"
    }

    if ($diverged.Count -gt 10) { Write-Host "       … and $($diverged.Count - 10) more" }

    exit 1
}

if ($KeepOutput) {
    Set-Content -Path (Join-Path $PSScriptRoot 'parity-dotnet.txt') -Value $dotnetOut
    Set-Content -Path (Join-Path $PSScriptRoot 'parity-fable.txt') -Value $fableOut
} elseif (-not $emittedArg) {
    Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue
}

Write-Host "==== parity: green — $($dotnetOut.Count)/$($dotnetOut.Count) vectors byte-identical on both pipelines" -ForegroundColor Green
exit 0
