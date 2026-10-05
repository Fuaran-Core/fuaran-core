#Requires -Version 7.0
<#
.SYNOPSIS
  The wire layer's two-host timing run (Phase 364): escape, render, canonical render and parse over
  four fixed corpora, on .NET and under node, with the node / .NET ratio per case.

.DESCRIPTION
  One harness (Fuaran.Core.Wire.Benchmarks/) is timed on both hosts. The script:

    1. builds it in Release and runs it on .NET;
    2. reads both hosts' output fingerprints and REFUSES to time anything unless they agree line for
       line (each host also checks its own fingerprints against the pins in Corpus.fs);
    3. times every case on .NET and, given -NodeEntry, under node;
    4. prints both median tables, the node / .NET ratio table, and the machine, .NET, node and Fable
       versions - the header a results file under results/ carries.

  THE NODE LEG RUNS JAVASCRIPT THIS REPOSITORY DOES NOT COMPILE. The Fable compiler does not run in
  fuaran-core (DECISIONS.md D55; the suite's FableSmokeCompleteness test holds it), so the harness is
  compiled by a checkout that owns a Fable toolchain and the script is handed the emitted entry point.
  docs/wire-performance.md gives the compile command. Without -NodeEntry the node leg is reported as
  NOT RUN, never as passed.

    pwsh ./benchmarks/run.ps1 -NodeEntry <fable output>/Program.js
    pwsh ./benchmarks/run.ps1 -NodeEntry <fable output>/Program.js -Runs 15
    pwsh ./benchmarks/run.ps1                     # the .NET leg alone

  Exit 0 = every agreement held and every requested table printed.
#>
[CmdletBinding()]
param(
    # Measured samples per case, after two warm-up calls.
    [int] $Runs = 10,
    # The Fable-emitted Program.js of the harness. Omitted: the node leg does not run, and says so.
    [string] $NodeEntry = ''
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$global:LASTEXITCODE = 0

$project = Join-Path $PSScriptRoot 'Fuaran.Core.Wire.Benchmarks' 'Fuaran.Core.Wire.Benchmarks.fsproj'
$dll = Join-Path $PSScriptRoot 'Fuaran.Core.Wire.Benchmarks' 'bin' 'Release' 'net10.0' 'Fuaran.Core.Wire.Benchmarks.dll'

# Lines of a native command's stdout, with the exit code checked - never a pipe, whose status is the
# LAST command's.
function Invoke-Harness([string] $what, [string] $exe, [string[]] $arguments) {
    $global:LASTEXITCODE = 0
    $out = & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "the $what run failed (exit $LASTEXITCODE)" }
    @($out | ForEach-Object { ([string] $_).TrimEnd() })
}

$global:LASTEXITCODE = 0
dotnet build $project -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "the Release build of the harness failed (exit $LASTEXITCODE)" }
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

$node = $null
if ($NodeEntry) {
    if (-not (Test-Path -LiteralPath $NodeEntry -PathType Leaf)) { throw "-NodeEntry '$NodeEntry' is not a file" }
    $node = (Get-Command node -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1)
    if (-not $node) { throw 'node is not on PATH: -NodeEntry was given and the node leg cannot run without it' }
    $node = $node.Source
}

# ---- versions: the header of a results file ----
$cpu = try { (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim() } catch { [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture }
Write-Host "machine: $cpu, $([Environment]::ProcessorCount) logical processors, $([Runtime.InteropServices.RuntimeInformation]::OSDescription)"
Write-Host ".NET: $((& $dotnet --version).Trim()) (Release build)"
if ($node) {
    $library = Get-ChildItem -LiteralPath (Join-Path (Split-Path -Parent $NodeEntry) 'fable_modules') -Directory -Filter 'fable-library-js.*' -ErrorAction SilentlyContinue | Select-Object -First 1
    $fable = if ($library) { $library.Name.Substring('fable-library-js.'.Length) } else { 'unknown (no fable_modules/fable-library-js.* beside the entry)' }
    Write-Host "node: $((& $node --version).Trim()); Fable: $fable"
}
else {
    Write-Host 'node: NOT RUN (no -NodeEntry)' -ForegroundColor Yellow
}
Write-Host "runs: $Runs measured samples per case, median, ms per call"

# ---- the agreement, before anything is timed ----
$fpNet = Invoke-Harness '.NET fingerprint' $dotnet @($dll, '--fingerprints')
if ($fpNet.Count -eq 0) { throw 'the .NET fingerprint listing is empty: nothing would be compared' }
if ($node) {
    $fpNode = Invoke-Harness 'node fingerprint' $node @($NodeEntry, '--fingerprints')
    $diff = Compare-Object -ReferenceObject $fpNet -DifferenceObject $fpNode -SyncWindow 0
    if ($diff) {
        $diff | ForEach-Object { Write-Host "  $($_.SideIndicator) $($_.InputObject)" }
        throw 'the hosts DISAGREE on a case''s output bytes; nothing was timed'
    }
    Write-Host "agreement: $($fpNet.Count) case fingerprints identical on .NET and node"
}
else {
    Write-Host "agreement: $($fpNet.Count) case fingerprints checked on .NET against the pins (node not run)"
}

# ---- the tables ----
function Read-Rows([string[]] $lines) {
    $rows = @{}
    foreach ($l in $lines) {
        if ($l -match '^ROW (\S+) (\S+) (\S+)$') { $rows["$($Matches[1]) $($Matches[2])"] = [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture) }
    }
    $rows
}

$netOut = Invoke-Harness '.NET timing' $dotnet @($dll, "$Runs")
Write-Host ''
Write-Host '.NET (Release), median ms per call:'
$netOut | Where-Object { $_ -notlike 'ROW *' } | ForEach-Object { Write-Host $_ }
$netRows = Read-Rows $netOut

if ($node) {
    $nodeOut = Invoke-Harness 'node timing' $node @($NodeEntry, "$Runs")
    Write-Host ''
    Write-Host 'node (Fable), median ms per call:'
    $nodeOut | Where-Object { $_ -notlike 'ROW *' } | ForEach-Object { Write-Host $_ }
    $nodeRows = Read-Rows $nodeOut

    $corpora = @($netRows.Keys | ForEach-Object { ($_ -split ' ')[0] } | Select-Object -Unique)
    $cases = @('escape', 'render', 'canon', 'parse')
    $order = @('escape-free', 'escape-heavy', 'op-stream', 'state', 'floats') | Where-Object { $corpora -contains $_ }
    Write-Host ''
    Write-Host 'node / .NET, per case:'
    Write-Host "| corpus | $($cases -join ' | ') |"
    Write-Host "|---|$(($cases | ForEach-Object { '---:' }) -join '|')|"
    foreach ($c in $order) {
        $cells = foreach ($k in $cases) {
            $key = "$c $k"
            if ($netRows.ContainsKey($key) -and $nodeRows.ContainsKey($key) -and $netRows[$key] -gt 0) {
                ($nodeRows[$key] / $netRows[$key]).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture)
            }
            else { '-' }
        }
        Write-Host "| $c | $($cells -join ' | ') |"
    }
}
exit 0
