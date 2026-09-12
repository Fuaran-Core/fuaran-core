# fuaran-core — repo verify gate: format-check + build + test.
# Non-zero exit on the first failing stage. The "is the repo green" command.
[CmdletBinding()]
param(
    [switch] $SkipFormatCheck,
    # Phase 131 — also run the proof leg (proofs/check.ps1): verify proofs/DagFold.fst with the
    # pinned F*/Z3 and hold the committed oracle to a fresh extraction. Opt-in here because it
    # installs a prover; CI's proofs job runs it on every push. The oracle HOST (the Proofs.Oracle
    # differential family) needs no prover and runs in the ordinary suite below, always.
    [switch] $Proofs
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipFormatCheck) {
    dotnet fantomas --check src tests
    if ($LASTEXITCODE -ne 0) {
        Write-Host '==== verify: fantomas format-check FAILED (run ./run.ps1 to format)' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

dotnet build Fuaran.Core.slnx --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Fable-compile gate (Phase 54): every PUBLIC Fuaran.Core package must compile clean under Fable, so the
# GP3 / STABILITY.md "Fable-clean on encode AND decode" claim is enforced in-repo rather than discovered
# downstream. The smoke project references the public packages + touches their encode/decode surface; the
# emitted JS is throwaway (the compile is the gate). No npm/npx is invoked — `dotnet fable` transpiles
# without installing the JS runtime library — so the workspace Invoke-Npm/Invoke-Npx convention does not
# apply here. Errors fail the gate; pre-existing benign warnings (e.g. Double.TryParse provider ignored —
# JS parses invariantly) do not.
dotnet fable tests/fable-smoke/FableSmoke.fsproj -o tests/fable-smoke/out --noCache
if ($LASTEXITCODE -ne 0) {
    Write-Host '==== verify: Fable-compile gate FAILED (a public package is not Fable-clean)' -ForegroundColor Red
    exit $LASTEXITCODE
}

# Value-parity leg (Phase 118): the emitted JS is not throwaway any more — the smoke prints the committed
# parity vectors under node and they must be byte-identical to the .NET table. A missing node FAILS.
pwsh ./tests/fable-smoke/parity.ps1 -UseFreshlyEmitted tests/fable-smoke/out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run --project tests/Fuaran.Core.Tests --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The reference adoption sample (docs/ADOPTION.md) must certify GREEN — it exercises the
# whole adoption path (witness laws + op-algebra + reducer + op-stream) end to end.
dotnet run --project samples/adoption --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host '==== verify: adoption sample FAILED its conformance report' -ForegroundColor Red
    exit $LASTEXITCODE
}

# The C# facade's conformance report (Phase 128) — a C# consumer constructs and reads `ColExpr`,
# `Transform`, the artifact-function hole family and `JVal` through `Fuaran.Core.CSharp` alone, the
# read-then-rebuild round trip is the identity over a generated sample, that sample is shown to reach
# every case of every union it covers (read off the F# type, so a NEW case reddens this), and no public
# facade member mentions an F# type outside the two declared bridge names. Run from here rather than
# from the Expecto suite because the claim is about what a C# CONSUMER can express, and only C# consumer
# code can make it.
dotnet run --project tests/Fuaran.Core.CSharp.Proof --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host '==== verify: C# facade proof FAILED its conformance report' -ForegroundColor Red
    exit $LASTEXITCODE
}

if ($Proofs) {
    pwsh ./proofs/check.ps1 -Runs 3 -SkipOracleHost
    if ($LASTEXITCODE -ne 0) {
        Write-Host '==== verify: the proof leg FAILED (see proofs/README.md)' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host '==== verify: fuaran-core green' -ForegroundColor Green
exit 0
