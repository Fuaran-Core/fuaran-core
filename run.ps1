# fuaran-core — "drop into the repo, run one command, the thing works".
# Standard switches per the workspace conventions.
[CmdletBinding()]
param(
    [switch] $SkipFormat,
    [switch] $SkipBuild,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $SkipFormat) {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet fantomas src tests
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipBuild) {
    dotnet build Fuaran.Core.slnx --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipTests) {
    dotnet run --project tests/Fuaran.Core.Tests --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
