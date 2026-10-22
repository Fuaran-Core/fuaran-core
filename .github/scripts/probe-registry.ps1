#Requires -Version 7.0
# THE POST-PUSH REGISTRY PROBE — Phase 393.
#
#     pwsh ./.github/scripts/probe-registry.ps1                      # probe nuget.org for <Version>
#     pwsh ./.github/scripts/probe-registry.ps1 -Version 0.35.2      # probe a named version
#     pwsh ./.github/scripts/probe-registry.ps1 -PackagesDir <dir>   # and hold the packed set to the roster
#     pwsh ./.github/scripts/probe-registry.ps1 -PushLog <file>      # read what the push step accepted (Phase 561)
#     pwsh ./.github/scripts/probe-registry.ps1 -ListRoster          # print the roster and stop
#
# `publish-packages.yml` pushes with `--skip-duplicate`, which is right for a re-run and means a
# re-run after a PARTIAL push prints success without saying which ids were new — and nothing asked
# the registry afterwards what it serves. This script asks. It derives the PACKABLE ROSTER from the
# project files, exactly as `Roster.derive` (tests/Fuaran.Core.Tests.Fixtures/Roster.fs) does (`src/*/*.fsproj` and
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
#
# -PushLog names the push step's own output (Phase 561; publish-packages.yml tees it). NOT SERVED
# above is two different situations, and only the push step can tell them apart: a push that did
# not land needs a re-run, while a push the registry ACCEPTED (`Your package was pushed.`) or found
# ALREADY PRESENT (`--skip-duplicate`'s `already exists`) only needs nuget.org to finish indexing.
# On `v0.40.1` every push landed and the fifteen-minute budget ran out first, so a red job reported
# a release that had shipped. With -PushLog:
#   NOT PUSHED     a roster id with no push line that accepted it or found it present fails at once,
#                  before the registry is asked anything — waiting cannot fix it;
#   PUSHED, INDEXING PENDING
#                  every id was pushed, so the probe waits on a BACKED-OFF budget instead (-IntervalSeconds,
#                  doubling to -MaxIntervalSeconds, for at most -IndexBudgetMinutes: about an hour by
#                  default), prints that budget before the first pass, says on every pass that the pushes
#                  landed, and fails only when that budget is spent too, naming each id still pending and
#                  the push lines that accepted it.
# COULD NOT ASK stays its own answer in both modes. Without -PushLog (a hand run from a clone), the
# schedule is -Attempts passes -IntervalSeconds apart, as before. An -IntervalSeconds of 0 asks with
# no wait: -Attempts passes back to back by hand, and a single pass with -PushLog (no backed-off
# schedule can be built from a zero wait).
#
# Exit 0: every id is served. Exit 1: at least one id is not pushed, not served, pending indexing
# or could not be asked about when the budget ran out, or the packed set (-PackagesDir) differs from
# the roster, or a -PushLog file is missing. Exit 2: the roster itself could not be derived.
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
    # The push step's output, one file or several; when named, every roster id must have been pushed
    # and the backed-off budget below governs the wait.
    [string[]] $PushLog,
    # The hand-run budget: passes over the pending set, and the wait between passes. The wait is also
    # the first wait of the backed-off budget.
    [ValidateRange(1, 1000)][int] $Attempts = 30,
    [ValidateRange(0, 3600)][int] $IntervalSeconds = 30,
    # The backed-off budget for pushed-not-indexed ids (-PushLog only): the longest wait between
    # passes, and the most the waits add up to.
    [ValidateRange(1, 3600)][int] $MaxIntervalSeconds = 300,
    [ValidateRange(1, 240)][int] $IndexBudgetMinutes = 60,
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

# ---- the roster: THE derivation `Roster.derive` uses -------------------------------------------------

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

# ---- the push record, when named: every id was pushed ------------------------------------------------

# What the push step said about each roster id at -Version: `accepted` (the registry took the package
# in this run) or `already present` (`--skip-duplicate` met a package the registry already holds), with
# the lines that said so. Read from `dotnet nuget push`'s own output: a `Pushing <file> to '<feed>'...`
# line, then `Your package was pushed.` for an accepted package, or a `Package '<path>' already exists
# at feed` line naming the package. Only `.nupkg` files count (the symbol packages travel beside them
# and are not what the flat container serves), and an id is recorded at its first outcome. An id with
# no such line was not pushed, whatever else the log says: a change in the tool's wording reads as NOT
# PUSHED (red at once), never as pushed.
function Read-PushRecord([string[]] $paths, [string] $version) {
    $suffix = ".$version.nupkg"
    $record = @{}   # id -> [pscustomobject]@{ Outcome; Lines }
    foreach ($path in $paths) {
        $pushing = $null   # the file the most recent `Pushing` line named, and that line
        $pushingLine = $null
        foreach ($line in Get-Content -LiteralPath $path) {
            $m = [regex]::Match($line, "Pushing (\S+) to '")
            if ($m.Success) {
                $pushing = $m.Groups[1].Value
                $pushingLine = $line.Trim()
                continue
            }
            $outcome = $null
            $file = $null
            $lines = @()
            if ($line -match '^\s*Your package was pushed\.\s*$' -and $pushing) {
                $outcome = 'accepted'
                $file = $pushing
                $lines = @($pushingLine, $line.Trim())
            }
            else {
                $m = [regex]::Match($line, "Package '([^']+)' already exists at feed")
                if ($m.Success) {
                    $outcome = 'already present'
                    $file = [System.IO.Path]::GetFileName($m.Groups[1].Value)
                    $lines = @($line.Trim())
                }
            }
            if ($null -eq $outcome) { continue }
            $pushing = $null
            $pushingLine = $null
            if (-not $file.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $id = $file.Substring(0, $file.Length - $suffix.Length)
            if (-not $record.ContainsKey($id)) { $record[$id] = [pscustomobject]@{ Outcome = $outcome; Lines = $lines } }
        }
    }
    return $record
}

$pushRecord = $null
if ($PushLog) {
    foreach ($path in $PushLog) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Fail "-PushLog '$path' does not exist; there is no push record to read" 1 }
    }
    $pushRecord = Read-PushRecord $PushLog $Version
    $notPushed = @($roster | Where-Object { -not $pushRecord.ContainsKey($_) })
    $accepted = @($roster | Where-Object { $pushRecord.ContainsKey($_) -and $pushRecord[$_].Outcome -eq 'accepted' })
    $present = @($roster | Where-Object { $pushRecord.ContainsKey($_) -and $pushRecord[$_].Outcome -eq 'already present' })
    Write-Host "==== registry: the push record ($($PushLog -join ', ')): $($accepted.Count) accepted, $($present.Count) already present, $($notPushed.Count) not pushed" -ForegroundColor Cyan
    if ($notPushed.Count -gt 0) {
        Fail "NOT PUSHED at ${Version}: $($notPushed -join ', '). No push line accepted these or found them present, so no wait can make the registry serve them. Re-run the job: the pushes skip what is already there" 1
    }
}

# ---- the budget --------------------------------------------------------------------------------------

# The waits between passes; there is one more pass than there are waits. Without a push record:
# -Attempts passes, -IntervalSeconds apart. With one, the backed-off budget: the first wait is
# -IntervalSeconds (capped at -MaxIntervalSeconds), each next wait doubles up to -MaxIntervalSeconds, and
# waits are added while their sum stays within -IndexBudgetMinutes. A zero first wait builds no
# backed-off schedule, so that is one pass.
function Get-ProbeWaits([bool] $backedOff) {
    if (-not $backedOff) {
        if ($Attempts -le 1) { return @() }
        return @(1..($Attempts - 1) | ForEach-Object { $IntervalSeconds })
    }
    $waits = @()
    $budget = $IndexBudgetMinutes * 60
    $total = 0
    $wait = [Math]::Min($IntervalSeconds, $MaxIntervalSeconds)
    while ($wait -gt 0 -and $total + $wait -le $budget) {
        $waits += $wait
        $total += $wait
        $wait = [Math]::Min($wait * 2, $MaxIntervalSeconds)
    }
    return $waits
}

function Format-Seconds([int] $seconds) {
    if ($seconds -lt 120) { return "${seconds}s" }
    return '{0:0.#} min' -f ($seconds / 60)
}

$waits = @(Get-ProbeWaits ($null -ne $pushRecord))
$passes = $waits.Count + 1
$waited = 0
foreach ($wait in $waits) { $waited += $wait }
if ($pushRecord) {
    $steps = if ($waits.Count -gt 0) { "waits $(($waits | ForEach-Object { "${_}s" }) -join ', ')" } else { 'no waits' }
    Write-Host "==== registry: every id was pushed, so a slow index is waited out: up to $passes pass(es) over $(Format-Seconds $waited) ($steps; budget $IndexBudgetMinutes min)" -ForegroundColor Cyan
}
else {
    Write-Host "==== registry: up to $passes pass(es), ${IntervalSeconds}s apart ($(Format-Seconds $waited) of waits)" -ForegroundColor Cyan
}

# ---- the probe ---------------------------------------------------------------------------------------

# One question to the flat container about one id. `served` and `absent` are answers the registry gave
# (a 200 listing the version; a 200 without it, or a 404 for an id it has never seen); anything else is
# `could not ask: <why>`, which is no answer about the id at all.
function Get-IndexAnswer([string] $id, [string] $wanted) {
    $url = "$($Registry.TrimEnd('/'))/$($id.ToLowerInvariant())/index.json"
    try {
        $response = Invoke-WebRequest -Uri $url -SkipHttpErrorCheck -TimeoutSec 60
        $status = [int]$response.StatusCode
        if ($status -eq 404) { return 'absent' }
        if ($status -ne 200) { return "could not ask: HTTP $status" }
        # A body labelled with no text media type arrives as bytes; it is the same JSON either way.
        $body = if ($response.Content -is [byte[]]) { [System.Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
        $index = $body | ConvertFrom-Json
        # A 200 that lists no versions is an unreadable body, not an answer that the version is absent.
        if ($index -isnot [System.Management.Automation.PSCustomObject] -or $index.PSObject.Properties.Name -notcontains 'versions') {
            return 'could not ask: HTTP 200 with no version list'
        }
        $versions = @($index.versions)
        if ($versions -contains $wanted) { return 'served' }
        return 'absent'
    }
    catch { return "could not ask: $($_.Exception.Message)" }
}

$wanted = $Version.ToLowerInvariant()
$pending = @($roster)
$unasked = @{}   # id -> why the last attempt could not ask
$clock = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($pass in 1..$passes) {
    foreach ($id in @($pending)) {
        $answer = Get-IndexAnswer $id $wanted
        if ($answer.StartsWith('could not ask: ')) {
            $unasked[$id] = $answer.Substring('could not ask: '.Length)
            continue
        }
        $unasked.Remove($id)
        if ($answer -eq 'served') { $pending = @($pending | Where-Object { $_ -ne $id }) }
    }
    if ($pending.Count -eq 0) { break }
    if ($pass -lt $passes) {
        $wait = $waits[$pass - 1]
        if ($pushRecord) {
            Write-Host "==== registry: pass $pass of ${passes}: $($pending.Count) id(s) pushed and not indexed at $Version yet; the pushes landed, indexing is pending; next pass in ${wait}s" -ForegroundColor Yellow
        }
        else {
            Write-Host "==== registry: pass $pass of ${passes}: $($pending.Count) id(s) not served at $Version yet; retrying in ${wait}s" -ForegroundColor Yellow
        }
        Start-Sleep -Seconds $wait
    }
}

if ($pending.Count -eq 0) {
    Write-Host "==== registry: all $($roster.Count) ids are served at $Version" -ForegroundColor Green
    exit 0
}

$notServed = @($pending | Where-Object { -not $unasked.ContainsKey($_) })
$couldNotAsk = @($pending | Where-Object { $unasked.ContainsKey($_) })
$elapsed = Format-Seconds ([int]$clock.Elapsed.TotalSeconds)
if ($notServed.Count -gt 0 -and $pushRecord) {
    Write-Host "==== registry: PUSHED, INDEXING PENDING at $Version after $passes pass(es) over ${elapsed}: $($notServed -join ', ')" -ForegroundColor Red
    Write-Host "==== registry: every push landed and nuget.org has not indexed these yet, so nothing needs re-pushing. Wait, then re-run the job (the pushes skip what is already there) or probe by hand with -Version $Version. The push lines:" -ForegroundColor Red
    foreach ($id in $notServed) {
        foreach ($line in $pushRecord[$id].Lines) { Write-Host "    ${id}: $line" -ForegroundColor Red }
    }
}
elseif ($notServed.Count -gt 0) {
    Write-Host "==== registry: NOT SERVED at $Version after $passes pass(es): $($notServed -join ', ')" -ForegroundColor Red
}
if ($couldNotAsk.Count -gt 0) {
    Write-Host "==== registry: COULD NOT ASK about $($couldNotAsk.Count) id(s): $(($couldNotAsk | ForEach-Object { "$_ ($($unasked[$_]))" }) -join ', ')" -ForegroundColor Red
}
exit 1
