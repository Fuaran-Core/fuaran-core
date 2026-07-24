# fuaran-core — repo verify gate: format-check + build + test.
# Non-zero exit on the first failing stage. The "is the repo green" command.
[CmdletBinding()]
param(
    [switch] $SkipFormatCheck
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

dotnet run --project tests/Fuaran.Core.Tests --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# The reference adoption sample (docs/ADOPTION.md) must certify GREEN — it exercises the
# whole adoption path (witness laws + op-algebra + reducer + op-stream) end to end.
dotnet run --project samples/adoption --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host '==== verify: adoption sample FAILED its conformance report' -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host '==== verify: fuaran-core green' -ForegroundColor Green
exit 0
