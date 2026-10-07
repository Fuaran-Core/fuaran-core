#Requires -Version 7.0
# THE POST-PUSH REGISTRY PROBE — Phase 393.
#
#     pwsh ./.github/scripts/probe-registry.ps1                      # probe nuget.org for <Version>
#     pwsh ./.github/scripts/probe-registry.ps1 -Version 0.35.2      # probe a named version
#     pwsh ./.github/scripts/probe-registry.ps1 -PackagesDir <dir>   # and hold the packed set to the roster
#     pwsh ./.github/scripts/probe-registry.ps1 -ListRoster          # print the roster and stop
#
# `publish-packages.yml` pushes with `--skip-duplicate`, which is right for a re-run and means a
# re-run after a PARTIAL push prints success without saying which ids were new — and nothing asked
# the registry afterwards what it serves. This script asks. It derives the PACKABLE ROSTER from the
# project files, exactly as `PackageRosterTests.packableProjects` does (`src/*/*.fsproj` and
# `src/*/*.csproj`; `<IsPackable>` from the project, else `Directory.Build.props`, else true — only
# a literal `false` excludes; `<PackageId>` else the file's base name), and the suite holds the two
# derivations to one set (`PackageRosterTests`, "the registry probe derives the same roster"), so
# the probe cannot quietly ask about a different list than the one the README table is gated on.
#
# Then, for every id, it reads `<Registry>/<id lowercased>/index.json` — the v3 flat container,
# which lists every version the registry SERVES — until the version is listed. nuget.org indexes a
# push asynchronously, so an id not served yet is retried on a bounded schedule (-Attempts passes,
# -IntervalSeconds apart; the defaults wait fifteen minutes) and the job fails only when the budget
# is spent, NAMING every id still missing. Two answers are kept apart all the way to the verdict:
#   NOT SERVED     the registry answered (a 404 for an id it has never seen, or a version list
#                  without this version) — the push did not land, or has not been indexed yet;
#   COULD NOT ASK  the request itself failed (unreachable, a 5xx, an unreadable body) — the probe
#                  learned nothing about that id, which is not the same claim and is not reported
#                  as one.
# Exit 0: every id is served. Exit 1: at least one id is not served or could not be asked about
# when the budget ran out, or the packed set (-PackagesDir) differs from the roster. Exit 2: the
# roster itself could not be derived.
#
# -PackagesDir names the directory `dotnet pack` wrote. The `.nupkg` files there must be exactly
# the roster at -Version: a roster id with no package means the push could not have published it,
# and a package outside the roster is one the README table and this probe do not know about. Both
# fail BEFORE the registry is asked anything.
[CmdletBinding()]
param(
    # The version to probe for. Defaults to <Version> in Directory.Build.props.
    [string] $Version,
    # The directory `dotnet pack` wrote; when named, its .nupkg set must equal the roster.
    [string] $PackagesDir,
    # The v3 flat-container base address.
    [string] $Registry = 'https://api.nuget.org/v3-flatcontainer',
    # The probe budget: passes over the pending set, and the wait between passes.
    [ValidateRange(1, 1000)][int] $Attempts = 30,
    [ValidateRange(0, 3600)][int] $IntervalSeconds = 30,
    # Print the derived roster, one id per line, and exit 0. What the suite compares.
    [switch] $ListRoster,
    # The repository root the roster is derived from. Defaults to this script's repository; the
    # suite names a synthetic tree to hold the derivation's edge cases.
    [string] $Root
)

$ErrorActionPreference = 'Stop'
# `$Root` and every `$root` below are ONE variable (PowerShell names are case-insensitive): the
# parameter, resolved to a full path.
$Root = if ($Root) { [System.IO.Path]::GetFullPath($Root) } else { [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')) }

function Fail([string] $message, [int] $code) {
    Write-Host "==== registry: $message" -ForegroundColor Red
    exit $code
}

# ---- the roster: THE derivation `PackageRosterTests.packableProjects` uses -------------------------

function Get-FirstGroup([string] $pattern, [string] $text) {
    $m = [regex]::Match($text, $pattern)
    if ($m.Success) {
        $v = $m.Groups[1].Value.Trim()
        if ($v -ne '') { return $v }
    }
    return $null
}

$srcDir = Join-Path $root 'src'
if (-not (Test-Path $srcDir)) { Fail "no src/ directory under $root; the roster cannot be derived" 2 }
$propsPath = Join-Path $root 'Directory.Build.props'
$propsText = if (Test-Path $propsPath) { Get-Content $propsPath -Raw } else { '' }
$fallback = Get-FirstGroup '<IsPackable>([^<]*)</IsPackable>' $propsText

$roster = @(@(
    foreach ($dir in Get-ChildItem $srcDir -Directory) {
        foreach ($proj in @(Get-ChildItem $dir.FullName -File -Filter '*.fsproj') + @(Get-ChildItem $dir.FullName -File -Filter '*.csproj')) {
            $text = Get-Content $proj.FullName -Raw
            $packable = Get-FirstGroup '<IsPackable>([^<]*)</IsPackable>' $text
            if ($null -eq $packable) { $packable = $fallback }
            if ($null -ne $packable -and $packable -ieq 'false') { continue }
            $id = Get-FirstGroup '<PackageId>([^<]*)</PackageId>' $text
            if ($null -eq $id) { $id = [System.IO.Path]::GetFileNameWithoutExtension($proj.Name) }
            $id
        }
    }
) | Sort-Object -CaseSensitive -Unique)

if ($roster.Count -eq 0) { Fail "the roster derived from $srcDir is EMPTY; a probe over no ids would report success having asked nothing" 2 }

if ($ListRoster) {
    $roster | ForEach-Object { Write-Output $_ }
    exit 0
}

if (-not $Version) {
    $Version = Get-FirstGroup '<Version>([^<]*)</Version>' $propsText
    if (-not $Version) { Fail "no -Version given and Directory.Build.props declares no <Version>" 2 }
}
Write-Host "==== registry: $($roster.Count) packable ids at $Version, probed against $Registry" -ForegroundColor Cyan

# ---- the packed set, when named: exactly the roster ---------------------------------------------------

if ($PackagesDir) {
    if (-not (Test-Path $PackagesDir)) { Fail "-PackagesDir '$PackagesDir' does not exist" 1 }
    $suffix = ".$Version.nupkg"
    $packed = @(
        Get-ChildItem $PackagesDir -File -Filter '*.nupkg' |
            Where-Object { $_.Name.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object { $_.Name.Substring(0, $_.Name.Length - $suffix.Length) }
    )
    $unpacked = @($roster | Where-Object { $packed -notcontains $_ })
    $unknown = @($packed | Where-Object { $roster -notcontains $_ })
    if ($unpacked.Count -gt 0 -or $unknown.Count -gt 0) {
        $why = @()
        if ($unpacked.Count -gt 0) { $why += "roster ids with no $Version package in ${PackagesDir}: $($unpacked -join ', ')" }
        if ($unknown.Count -gt 0) { $why += "packages outside the roster: $($unknown -join ', ')" }
        Fail "the packed set is not the roster — $($why -join '; ')" 1
    }
    Write-Host "==== registry: the packed set in $PackagesDir is the roster ($($packed.Count) packages)" -ForegroundColor Cyan
}

# ---- the probe ---------------------------------------------------------------------------------------

$wanted = $Version.ToLowerInvariant()
$pending = @($roster)
$unasked = @{}   # id -> why the last attempt could not ask

foreach ($pass in 1..$Attempts) {
    foreach ($id in @($pending)) {
        $url = "$($Registry.TrimEnd('/'))/$($id.ToLowerInvariant())/index.json"
        try {
            $response = Invoke-WebRequest -Uri $url -SkipHttpErrorCheck -TimeoutSec 60
            $status = [int]$response.StatusCode
            if ($status -eq 404) {
                $unasked.Remove($id)   # answered: never seen
            }
            elseif ($status -eq 200) {
                $versions = @(($response.Content | ConvertFrom-Json).versions)
                $unasked.Remove($id)
                if ($versions -contains $wanted) { $pending = @($pending | Where-Object { $_ -ne $id }) }
            }
            else { $unasked[$id] = "HTTP $status" }
        }
        catch { $unasked[$id] = $_.Exception.Message }
    }
    if ($pending.Count -eq 0) { break }
    if ($pass -lt $Attempts) {
        Write-Host "==== registry: pass $pass of ${Attempts}: $($pending.Count) id(s) not served at $Version yet; retrying in ${IntervalSeconds}s" -ForegroundColor Yellow
        Start-Sleep -Seconds $IntervalSeconds
    }
}

if ($pending.Count -eq 0) {
    Write-Host "==== registry: all $($roster.Count) ids are served at $Version" -ForegroundColor Green
    exit 0
}

$notServed = @($pending | Where-Object { -not $unasked.ContainsKey($_) })
$couldNotAsk = @($pending | Where-Object { $unasked.ContainsKey($_) })
if ($notServed.Count -gt 0) {
    Write-Host "==== registry: NOT SERVED at $Version after $Attempts pass(es): $($notServed -join ', ')" -ForegroundColor Red
}
if ($couldNotAsk.Count -gt 0) {
    Write-Host "==== registry: COULD NOT ASK about $($couldNotAsk.Count) id(s): $(($couldNotAsk | ForEach-Object { "$_ ($($unasked[$_]))" }) -join ', ')" -ForegroundColor Red
}
exit 1
