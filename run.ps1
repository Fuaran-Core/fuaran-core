#Requires -Version 7.0
# fuaran-core — "drop into the repo, run one command, the thing works".
# Standard switches per the workspace conventions.
[CmdletBinding()]
param(
    [switch] $SkipFormat,
    [switch] $SkipBuild,
    [switch] $SkipTests,
    # Phase 395 — the configuration built and tested; Debug stays the default so the contributor's
    # command is unchanged. Release is what publish-packages verifies and packs (verify.ps1 takes
    # the same switch).
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# A skipped stage must not read green. `$LASTEXITCODE` is $null until a native command runs and it
# survives from the previous stage, so a guard that reads it after a skipped or native-free stage
# reports whatever ran LAST. Each stage seeds it to 0 first, in the GLOBAL scope: a plain
# `$LASTEXITCODE = 0` creates a script-scope copy that hides the real code when this script is
# invoked with `&`.
if (-not $SkipFormat) {
    $global:LASTEXITCODE = 0
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $global:LASTEXITCODE = 0
    # `samples` is formatted with `src` and `tests`: the adoption sample is code a reader copies.
    dotnet fantomas src tests samples
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipBuild) {
    $global:LASTEXITCODE = 0
    dotnet build Fuaran.Core.slnx --nologo -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipTests) {
    $global:LASTEXITCODE = 0
    dotnet run --project tests/Fuaran.Core.Tests --no-build -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # The reference adoption sample (docs/ADOPTION.md) must certify GREEN — the same stage verify.ps1
    # runs, so the two launchers cannot disagree about whether the repository is green.
    $global:LASTEXITCODE = 0
    dotnet run --project samples/adoption --no-build -c $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
