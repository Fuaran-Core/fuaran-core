#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes `fuaran-core-oracle` as one self-contained single-file binary for one runtime identifier.

.DESCRIPTION
    The one way the release asset is built (DECISIONS.md D208, RELEASING.md "The oracle binaries"):
    the publish workflow's `oracle` jobs call it, and so does an operator reproducing an asset by hand.
    It writes `fuaran-core-oracle-<Version>-<Rid>` (`.exe` on Windows) into -OutDir, where <Version> is
    Directory.Build.props's `<Version>`, and prints the file's path and sha256. Self-contained, so the
    host running it needs no .NET; invariant globalization (the project file), so it needs no ICU.

.PARAMETER Rid
    The runtime identifier: one of the identifiers RELEASING.md names (linux-x64, linux-arm64,
    osx-arm64, win-x64). Any other is refused, so an asset set cannot grow by a typo.

.PARAMETER OutDir
    Where the binary is written. Created when absent.
#>
param(
    [Parameter(Mandatory)] [string] $Rid,
    [Parameter(Mandatory)] [string] $OutDir
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..' '..')

$rids = @('linux-x64', 'linux-arm64', 'osx-arm64', 'win-x64')
if ($rids -notcontains $Rid) {
    throw "unknown runtime identifier '$Rid': the oracle is released for $($rids -join ', ') (RELEASING.md)"
}

$node = Select-Xml -Path Directory.Build.props -XPath '//PropertyGroup/Version' | Select-Object -First 1
if ($null -eq $node) { throw 'Directory.Build.props declares no <Version>.' }
$version = $node.Node.InnerText.Trim()

$stage = Join-Path ([System.IO.Path]::GetTempPath()) "fuaran-core-oracle-$Rid-$([guid]::NewGuid().ToString('N'))"
$global:LASTEXITCODE = 0
dotnet publish tools/Fuaran.Core.Oracle.Cli/Fuaran.Core.Oracle.Cli.fsproj `
    -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o $stage --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Rid (exit $LASTEXITCODE)" }

$suffix = if ($Rid.StartsWith('win-')) { '.exe' } else { '' }
$built = Join-Path $stage "fuaran-core-oracle$suffix"
if (-not (Test-Path $built)) { throw "the publish wrote no fuaran-core-oracle$suffix into $stage" }

New-Item -ItemType Directory -Force $OutDir | Out-Null
$asset = Join-Path $OutDir "fuaran-core-oracle-$version-$Rid$suffix"
Copy-Item $built $asset -Force
Remove-Item -Recurse -Force $stage

$sha = (Get-FileHash $asset -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "$asset"
Write-Host "sha256 $sha"
