#Requires -Version 7.0
# THE PROOF LEG'S REFUSALS, HELD TO THEIR EXIT CODES — Phase 221.
#
#     pwsh ./proofs/kit/check-proof-leg.tests.ps1
#
# `check-proof-leg.ps1` printed `==== proofs: green` and exited 0 over a host build that did not
# exist (MSB1009), and over a model with a type error. Both failures were DIAGNOSED — MSBuild and F*
# each said so in the log — and neither was REFUSED. The cause was one line (`$LASTEXITCODE = 0` at
# script scope, which shadows the automatic variable once the leg is invoked with `&`); the header
# of `check-proof-leg.ps1` says where it was and why it must not come back.
#
# So this script runs the leg THE WAY A CALLER DOES — `& check-proof-leg.ps1 @args`, in this
# process, exactly as every `proofs/check.ps1` does — against a scratch proofs directory holding
# models small enough to check in a second, and holds every step to its EXIT CODE. The message is
# read too, but only to assert that `proofs: green` is ABSENT: the finding was that the message and
# the exit code both disagreed with reality, so pinning the text alone would let the pair drift
# apart again.
#
# FOUR ARMS (and, since Phase 309, three TWIN arms, F-H, since Phase 393 the PIN-RESOLUTION arms,
# R, which need no prover and run first, and since Phase 402 the CACHE PROVENANCE arms, P (its
# FLOOR-OS arms, O, retired with the floors in Phase 399, for the CACHED-READ and COST arms, C); since Phase 399 the MIRROR arms, M, and the ORACLE-INDEPENDENCE arms, I, which
# need no prover either, the GUARD-IN-THE-LEG arms, Q, and the RUN-FACTS arms, S). The first prover arm is the control that
# makes the other three mean something:
#
#   A. GREEN CONTROL — a true model, no host step: exit 0 AND `proofs: green`. If this is red, the
#                      scratch apparatus is broken and a red B–D would prove nothing.
#   B. HOST BUILD    — a -HostProjectFile that does not exist: exit non-zero, no green.
#   C. HOST RUN      — a host project that builds and cannot run the filter: exit non-zero, no green.
#   D. CHECK         — a model with a type error: exit non-zero, no green.
#
# The R, M and I arms run anywhere. The rest need the pinned prover; where there is none it says NOT RUN
# and exits 2 — never 0, because "nothing was checked" must not read as "everything held". `proofs/check.ps1` runs it after a
# green leg, when the prover is by construction present.
[CmdletBinding()]
param(
    # The adopter's proofs directory: where the pin, and the prover `check.ps1` installed, live.
    [string] $ProofsDir,
    # The leg under test. Defaults to the engine beside this file; pointing it at an older copy is
    # how the go-red half of the acceptance is demonstrated.
    [string] $Kit,
    # Scratch. Created fresh and removed at exit.
    [string] $WorkDir
)

$ErrorActionPreference = 'Stop'

if (-not $ProofsDir) { $ProofsDir = Split-Path $PSScriptRoot -Parent }
$ProofsDir = [System.IO.Path]::GetFullPath($ProofsDir)
if (-not $Kit) { $Kit = Join-Path $PSScriptRoot 'check-proof-leg.ps1' }
$Kit = [System.IO.Path]::GetFullPath($Kit)
if (-not $WorkDir) { $WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) "check-proof-leg-tests-$PID" }

$pinFile = Join-Path $ProofsDir 'fstar-pin.json'
$pinnedVersion = (Get-Content $pinFile -Raw | ConvertFrom-Json).fstar.TrimStart('v')

$script:failures = [System.Collections.Generic.List[string]]::new()
$script:cases = 0

function Assert-That([string] $what, [bool] $holds, [string] $detail = '') {
    $script:cases++
    if ($holds) { Write-Host "  PASS  $what$(if ($detail) { " — $detail" })" -ForegroundColor Green }
    else {
        $script:failures.Add($what)
        Write-Host "  FAIL  $what$(if ($detail) { " — $detail" })" -ForegroundColor Red
    }
}

# ---- R. PIN RESOLUTION, PER OPERATING SYSTEM (Phase 393) -------------------------------------------

# These arms need NO prover: `-ResolveOnly` resolves the pin entry the download path would fetch and
# stops, so they run first and run everywhere. The committed pin must resolve for both OSes it
# declares — and for the host's OS — and a pin that cannot serve an OS must be REFUSED NAMING it,
# exit 2, rather than fetching the wrong release or reading as green.
$resolveDir = Join-Path $WorkDir 'resolve'
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force $resolveDir | Out-Null
$committedPin = Get-Content $pinFile -Raw | ConvertFrom-Json

function Invoke-Resolve([string] $pinPath, [string] $platform) {
    $resolveArgs = @{ Modules = @('Resolve'); ProofsDir = $resolveDir; PinFile = $pinPath; ResolveOnly = $true }
    if ($platform) { $resolveArgs.Platform = $platform }
    Push-Location $WorkDir
    try {
        $global:LASTEXITCODE = 0
        $lines = @(& $Kit @resolveArgs *>&1 | ForEach-Object { [string]$_ })
        $code = $global:LASTEXITCODE
    }
    catch { $lines = @($_.ToString()); $code = 1 }
    finally { Pop-Location }
    [pscustomobject]@{ Exit = $code; Text = ($lines -join ' ') }
}

# A copy of the committed pin with one edit applied, written to the scratch directory.
function New-ScratchPin([string] $name, [scriptblock] $edit) {
    $copy = Get-Content $pinFile -Raw | ConvertFrom-Json
    & $edit $copy
    $path = Join-Path $resolveDir $name
    $copy | ConvertTo-Json -Depth 4 | Set-Content $path
    $path
}

foreach ($os in 'windows', 'linux') {
    $r = Invoke-Resolve $pinFile $os
    $asset = $committedPin.$os.asset
    Assert-That "R. RESOLVE — the committed pin resolves '$os' to its own entry" ($r.Exit -eq 0 -and $asset -and $r.Text.Contains($asset)) "exit $($r.Exit): $($r.Text)"
}
$hostOs = if ($IsWindows) { 'windows' } elseif ($IsLinux) { 'linux' } elseif ($IsMacOS) { 'macos' } else { '' }
$r = Invoke-Resolve $pinFile ''
if ($hostOs -and $committedPin.$hostOs) {
    Assert-That "R. RESOLVE — with no -Platform the HOST's OS ($hostOs) is resolved" ($r.Exit -eq 0 -and $r.Text.Contains($committedPin.$hostOs.asset)) "exit $($r.Exit): $($r.Text)"
}
else {
    Assert-That "R. REFUSE — a host OS the pin does not declare is refused by name" ($r.Exit -eq 2) "exit $($r.Exit): $($r.Text)"
}

$r = Invoke-Resolve $pinFile 'macos'
Assert-That "R. REFUSE — an OS the pin has no entry for exits 2, naming it" ($r.Exit -eq 2 -and $r.Text.Contains("pins no 'macos' release")) "exit $($r.Exit): $($r.Text)"

$noLinux = New-ScratchPin 'no-linux.json' { param($p) $p.PSObject.Properties.Remove('linux') }
$r = Invoke-Resolve $noLinux 'linux'
Assert-That "R. REFUSE — a pin without a linux entry refuses linux by name" ($r.Exit -eq 2 -and $r.Text.Contains("pins no 'linux' release")) "exit $($r.Exit): $($r.Text)"
$r = Invoke-Resolve $noLinux 'windows'
Assert-That "R. RESOLVE — and the same pin still resolves windows" ($r.Exit -eq 0) "exit $($r.Exit): $($r.Text)"

$noHash = New-ScratchPin 'no-hash.json' { param($p) $p.linux.PSObject.Properties.Remove('sha256') }
$r = Invoke-Resolve $noHash 'linux'
Assert-That "R. REFUSE — an entry with no sha256 is refused, naming the field" ($r.Exit -eq 2 -and $r.Text.Contains('incomplete') -and $r.Text.Contains('sha256')) "exit $($r.Exit): $($r.Text)"

$stale = New-ScratchPin 'stale.json' { param($p) $p.linux.asset = $p.linux.asset.Replace($p.fstar, 'v2000.01.01') }
$r = Invoke-Resolve $stale 'linux'
Assert-That "R. REFUSE — an entry naming a different release than the pin is refused" ($r.Exit -eq 2 -and $r.Text.Contains('different release')) "exit $($r.Exit): $($r.Text)"

# The committed pin carries a MIRROR per OS (Phase 399), and -ResolveOnly names both sources in the
# order they are tried.
foreach ($os in 'windows', 'linux') {
    $r = Invoke-Resolve $pinFile $os
    $mirror = $committedPin.$os.mirror
    Assert-That "R. MIRROR — the committed '$os' entry names a mirror, tried after its url" ($r.Exit -eq 0 -and $mirror -and $r.Text.Contains("2 source(s) in order: $($committedPin.$os.url) then $mirror")) "exit $($r.Exit): $($r.Text)"
}
$staleMirror = New-ScratchPin 'stale-mirror.json' { param($p) $p.linux.mirror = $p.linux.mirror.Replace($p.fstar, 'v2000.01.01') }
$r = Invoke-Resolve $staleMirror 'linux'
Assert-That 'R. REFUSE — a mirror naming a different release than the pin is refused' ($r.Exit -eq 2 -and $r.Text.Contains('its mirror is')) "exit $($r.Exit): $($r.Text)"
$otherAsset = New-ScratchPin 'other-asset.json' { param($p) $p.linux.mirror = $p.linux.mirror -replace '[^/]+$', "other-$($p.fstar).zip" }
$r = Invoke-Resolve $otherAsset 'linux'
Assert-That 'R. REFUSE — a mirror serving a different asset than the entry is refused' ($r.Exit -eq 2 -and $r.Text.Contains('does not serve the pinned asset')) "exit $($r.Exit): $($r.Text)"

# ---- M. THE MIRROR, FETCHED (Phase 399) -------------------------------------------------------------

# -FetchOnly fetches the archive the way the download path does and stops. The archive here is a few
# scratch bytes served from a file:// source; the unreachable source is a closed local port, so it
# fails fast and for an availability reason. No prover and no network are needed.
$fetchHome = Join-Path $WorkDir 'fetch'
$served = Join-Path $WorkDir "served/$($committedPin.fstar)"
New-Item -ItemType Directory -Force $fetchHome, $served | Out-Null
$fetchOs = if ($hostOs -and $committedPin.$hostOs) { $hostOs } else { 'linux' }
$fetchAsset = $committedPin.$fetchOs.asset
$goodBytes = Join-Path $served $fetchAsset
Set-Content -LiteralPath $goodBytes 'the pinned bytes' -NoNewline
$goodHash = (Get-FileHash -Algorithm SHA256 $goodBytes).Hash.ToLowerInvariant()
$badDir = Join-Path $WorkDir "replaced/$($committedPin.fstar)"
New-Item -ItemType Directory -Force $badDir | Out-Null
Set-Content -LiteralPath (Join-Path $badDir $fetchAsset) 'replaced bytes' -NoNewline
$closed = "http://127.0.0.1:9/$($committedPin.fstar)/$fetchAsset"
$closedMirror = "http://127.0.0.1:9/mirror/$($committedPin.fstar)/$fetchAsset"
$goodUri = [Uri]::new($goodBytes).AbsoluteUri
$badUri = [Uri]::new((Join-Path $badDir $fetchAsset)).AbsoluteUri

function Invoke-Fetch([string] $url, [string] $mirror) {
    $pinPath = New-ScratchPin 'fetch.json' { param($p) $p.$fetchOs.url = $url; $p.$fetchOs.mirror = $mirror; $p.$fetchOs.sha256 = $goodHash }
    if (Test-Path (Join-Path $fetchHome '.fstar')) { Remove-Item (Join-Path $fetchHome '.fstar') -Recurse -Force }
    Push-Location $WorkDir
    try {
        $global:LASTEXITCODE = 0
        $lines = @(& $Kit -Modules Fetch -ProofsDir $fetchHome -PinFile $pinPath -Platform $fetchOs -FetchOnly *>&1 | ForEach-Object { [string]$_ })
        $code = $global:LASTEXITCODE
    }
    catch { $lines = @($_.ToString()); $code = 1 }
    finally { Pop-Location }
    [pscustomobject]@{ Exit = $code; Text = ($lines -join ' '); Fetched = (Test-Path (Join-Path $fetchHome ".fstar/$fetchAsset")) }
}

$m = Invoke-Fetch $closed $goodUri
Assert-That 'M. MIRROR — a url that cannot serve falls through to the mirror, which is fetched and verified' ($m.Exit -eq 0 -and $m.Fetched -and $m.Text.Contains("from $goodUri matches the pinned sha256")) "exit $($m.Exit): $($m.Text)"
Assert-That 'M. MIRROR — and the url''s failure is reported, not swallowed' ($m.Text.Contains("$closed could not serve")) $m.Text

$m = Invoke-Fetch $goodUri $closedMirror
Assert-That 'M. ORDER — a url that serves is used, and the mirror is never asked' ($m.Exit -eq 0 -and $m.Fetched -and -not $m.Text.Contains($closedMirror)) "exit $($m.Exit): $($m.Text)"

$m = Invoke-Fetch $closed $badUri
Assert-That 'M. REFUSE — a mirror serving the wrong bytes is refused with the pin''s sha256, and nothing is left behind' ($m.Exit -ne 0 -and -not $m.Fetched -and $m.Text.Contains("pinned $goodHash") -and $m.Text.Contains($badUri)) "exit $($m.Exit): $($m.Text)"

$m = Invoke-Fetch $badUri $goodUri
Assert-That 'M. REFUSE — a url serving the wrong bytes is refused on the spot: the mirror is NOT tried' ($m.Exit -ne 0 -and -not $m.Fetched -and $m.Text.Contains("pinned $goodHash") -and -not $m.Text.Contains("from $goodUri")) "exit $($m.Exit): $($m.Text)"

$m = Invoke-Fetch $closed $closedMirror
Assert-That 'M. REFUSE — when no source can serve, the leg fails naming every source it tried' ($m.Exit -ne 0 -and -not $m.Fetched -and $m.Text.Contains('no source could serve') -and $m.Text.Contains($closedMirror)) "exit $($m.Exit): $($m.Text)"

# ---- I. THE ORACLE IS INDEPENDENT OF PRODUCTION (Phase 399) ------------------------------------------

# -GuardOnly runs the guard over a scratch oracle and stops. The CONTROL is an oracle shaped like a
# real one — a runtime floor naming System, an extracted model naming Prims and its sibling module,
# and production names in a comment, a string and beside a character literal — and it must hold.
# Each plant below then breaks it in exactly one way, and must be refused naming the line.
$guardRoot = Join-Path $WorkDir 'guard'
$guardOracle = Join-Path $guardRoot 'oracle'

function Reset-GuardOracle {
    if (Test-Path $guardRoot) { Remove-Item $guardRoot -Recurse -Force }
    New-Item -ItemType Directory -Force $guardOracle | Out-Null
    Set-Content (Join-Path $guardOracle 'Prims.fs') "module Prims`n`ntype string = System.String`ntype list<'a> = Microsoft.FSharp.Collections.List<'a>`nlet strcat (a: string) (b: string) : string = a + b`n"
    Set-Content (Join-Path $guardOracle 'LegModel.fs') @'
module LegModel

(* The production Fuaran.Core.Canon.render is what this models (* nested *) — named in prose only. *)
// Fuaran.Core.Ops.apply, likewise.
let label = Prims.strcat "Fuaran.Core.Canon." "render"
let quote = '"'
let x' = 1
let quoted = @"Fuaran.Core.Verbatim ""still a string"""
let twice (s: Prims.string) = LegSibling.dup s
'@
    Set-Content (Join-Path $guardOracle 'LegSibling.fs') "module LegSibling`n`nlet dup (s: Prims.string) = Prims.strcat s s`n"
    Set-Content (Join-Path $guardOracle 'Oracle.fsproj') @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup>
    <Compile Include="Prims.fs" />
    <Compile Include="LegSibling.fs" />
    <Compile Include="LegModel.fs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="FSharp.Core" />
  </ItemGroup>
</Project>
'@
}

function Invoke-Guard {
    Push-Location $WorkDir
    try {
        $global:LASTEXITCODE = 0
        $lines = @(& $Kit -Modules LegModel, LegSibling -ProofsDir $guardRoot -PinFile $pinFile -GuardOnly *>&1 | ForEach-Object { [string]$_ })
        $code = $global:LASTEXITCODE
    }
    catch { $lines = @($_.ToString()); $code = 1 }
    finally { Pop-Location }
    [pscustomobject]@{ Exit = $code; Lines = $lines; Text = ($lines -join ' ') }
}

function Edit-GuardFile([string] $name, [string] $find, [string] $replace) {
    $path = Join-Path $guardOracle $name
    $text = Get-Content -LiteralPath $path -Raw
    if (-not $text.Contains($find)) { throw "the plant cannot find '$find' in $name" }
    Set-Content -LiteralPath $path $text.Replace($find, $replace) -NoNewline
}

Reset-GuardOracle
$i = Invoke-Guard
Assert-That 'I. GUARD CONTROL — an independent oracle (production named only in prose and strings) holds: exit 0' ($i.Exit -eq 0 -and $i.Text.Contains('oracle independence — the oracle project and its 3 source file(s)')) "exit $($i.Exit): $($i.Text)"

$plants = @(
    @{ What = 'an extracted file that OPENS a production namespace'; File = 'LegModel.fs'; Find = "let x' = 1"; Replace = "open Fuaran.Core.Wire`nlet x' = 1"; Expect = "LegModel\.fs:7: names 'Fuaran'.*open Fuaran\.Core\.Wire" }
    @{ What = 'an extracted file that QUALIFIES a production function'; File = 'LegModel.fs'; Find = 'LegSibling.dup s'; Replace = 'Fuaran.Core.Canon.render s'; Expect = "LegModel\.fs:9: names 'Fuaran'.*Fuaran\.Core\.Canon\.render" }
    @{ What = 'an extracted file that abbreviates a production module'; File = 'LegModel.fs'; Find = "let x' = 1"; Replace = "module C = Fuaran.Core.Canon`nlet x' = 1"; Expect = "LegModel\.fs:7: names 'Fuaran'" }
    @{ What = 'an extracted file reaching past every module with global.'; File = 'LegModel.fs'; Find = 'LegSibling.dup s'; Replace = 'global.Fuaran.Core.Canon.render s'; Expect = 'LegModel\.fs:9: `global\.`' }
    @{ What = 'an EXTRACTED file naming System, which only the runtime floor may'; File = 'LegModel.fs'; Find = "let x' = 1"; Replace = "let x' = System.IO.File.ReadAllText ""p"""; Expect = "LegModel\.fs:7: names 'System'" }
    @{ What = 'a ProjectReference in the oracle project'; File = 'Oracle.fsproj'; Find = '<PackageReference Include="FSharp.Core" />'; Replace = "<PackageReference Include=`"FSharp.Core`" />`n    <ProjectReference Include=`"../../src/Prod/Prod.fsproj`" />"; Expect = 'Oracle\.fsproj:\d+: a ProjectReference.*Prod\.fsproj' }
    @{ What = 'a PackageReference other than FSharp.Core'; File = 'Oracle.fsproj'; Find = '<PackageReference Include="FSharp.Core" />'; Replace = "<PackageReference Include=`"FSharp.Core`" />`n    <PackageReference Include=`"Fuaran.Core.Wire`" />"; Expect = 'Oracle\.fsproj:\d+: a PackageReference other than FSharp\.Core' }
    @{ What = 'production source compiled into the oracle'; File = 'Oracle.fsproj'; Find = '<Compile Include="LegModel.fs" />'; Replace = "<Compile Include=`"LegModel.fs`" />`n    <Compile Include=`"../../src/Canon.fs`" />"; Expect = 'Oracle\.fsproj:\d+: a Compile item outside the oracle directory' }
)
foreach ($plant in $plants) {
    Reset-GuardOracle
    Edit-GuardFile $plant.File $plant.Find $plant.Replace
    $i = Invoke-Guard
    $named = [bool](@($i.Lines -match $plant.Expect).Count)
    Assert-That "I. GUARD — $($plant.What) is refused, naming the line" ($i.Exit -ne 0 -and $named -and $i.Text.Contains('NOT INDEPENDENT')) "exit $($i.Exit): $($i.Text)"
}

# A reference INHERITED from a Directory.Build.props above the oracle counts as much as one written in
# the project: the guard reads the project as MSBuild evaluates it.
Reset-GuardOracle
Set-Content (Join-Path $guardRoot 'Directory.Build.props') "<Project>`n  <ItemGroup>`n    <PackageReference Include=`"Inherited.Production`" />`n  </ItemGroup>`n</Project>`n"
$i = Invoke-Guard
Assert-That 'I. GUARD — a PackageReference inherited from a Directory.Build.props is refused, naming that file' ($i.Exit -ne 0 -and [bool](@($i.Lines -match 'Directory\.Build\.props:3: a PackageReference other than FSharp\.Core').Count)) "exit $($i.Exit): $($i.Text)"
Remove-Item $guardRoot -Recurse -Force -ErrorAction SilentlyContinue

# The prover, resolved the way the leg resolves it, WITHOUT the leg's download: a test that fetched
# a 100 MB release as a side effect would be a surprise, and `check.ps1` has already fetched it.
$fstarHome = $null
if ($env:FSTAR_HOME) { $fstarHome = $env:FSTAR_HOME }
elseif (Test-Path (Join-Path $ProofsDir '.fstar/fstar/bin/fstar.exe')) { $fstarHome = Join-Path $ProofsDir '.fstar/fstar' }
if (-not $fstarHome -or -not (Test-Path (Join-Path $fstarHome 'bin/fstar.exe'))) {
    Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    if ($script:failures.Count -gt 0) {
        Write-Host "==== leg-tests: RED — $($script:failures.Count) of $script:cases prover-free assertion(s) failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "==== leg-tests: NOT RUN — no pinned prover ($script:cases prover-free assertions held: pin resolution, the mirror and the oracle guard; the prover arms need it). Set FSTAR_HOME to an F* $pinnedVersion release, or run ``pwsh ./proofs/check.ps1`` once to install it under proofs/.fstar/." -ForegroundColor Yellow
    exit 2
}

# ---- the scratch proofs directory ----------------------------------------------------------------

if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
$scratch = Join-Path $WorkDir 'proofs'
New-Item -ItemType Directory -Force $scratch | Out-Null
Copy-Item $pinFile (Join-Path $scratch 'fstar-pin.json')

# Two models: one true, one refuted by a plain type error (F* Error 19).
Set-Content (Join-Path $scratch 'LegGood.fst') "module LegGood`n`nlet one : nat = 1`n"
Set-Content (Join-Path $scratch 'LegBad.fst') "module LegBad`n`nlet minus_one : nat = -1`n"
# A TRUE model whose query name contains "fails": a success line naming it must not read as a failure.
Set-Content (Join-Path $scratch 'LegFailsName.fst') "module LegFailsName`n`nlet this_never_fails (x: nat) : nat = x + 1`n"

# Budgets for each. No cachedRead threshold is declared here, so none applies: the C arms declare one.
@{
    kind    = 'proofModules'
    modules = @(
        @{ module = 'LegGood'; budgetSeconds = 60; fastestSeconds = 0 }
        @{ module = 'LegBad'; budgetSeconds = 60; fastestSeconds = 0 }
        @{ module = 'LegFailsName'; budgetSeconds = 60; fastestSeconds = 0 }
    )
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.json')

# A host project that BUILDS (two empty targets, no SDK, no restore) and cannot RUN — `dotnet run`
# refuses a project with no runnable output type, exit 1. Arm C needs exactly that pair.
$hostDir = Join-Path $WorkDir 'host'
New-Item -ItemType Directory -Force $hostDir | Out-Null
Set-Content (Join-Path $hostDir 'Host.proj') '<Project><Target Name="Restore" /><Target Name="Build" /></Project>'

$hostFilters = @(@{ Filter = 'Leg.Host'; Failure = 'the scratch host is RED' })

# ---- running the leg the way a caller does -------------------------------------------------------

# In THIS process, with `&`, which is the shape that shadowed the exit code. Every stream is
# captured so the transcript can be searched; the exit code is read from the GLOBAL automatic
# variable, which is the only one this script never assigns.
function Invoke-Leg([hashtable] $legArgs) {
    $env:FSTAR_HOME = $fstarHome
    Push-Location $WorkDir
    $threw = $null
    try {
        $global:LASTEXITCODE = 0
        $lines = @(& $Kit @legArgs *>&1 | ForEach-Object { [string]$_ })
        $code = $global:LASTEXITCODE
    }
    catch {
        # A terminating error is a refusal too — as `pwsh -File` it is exit 1 — but it is named,
        # so a leg that has started THROWING where it used to exit is visible here.
        $threw = $_.ToString()
        $lines = @($threw)
        $code = 1
    }
    finally { Pop-Location }
    [pscustomobject]@{ Exit = $code; Green = [bool]($lines -match '==== proofs: green'); Lines = $lines; Threw = $threw }
}

function Show-Tail($result) {
    $verdict = @($result.Lines -match '^==== proofs: ') | Select-Object -Last 1
    if ($result.Threw) { "threw: $($result.Threw)" } elseif ($verdict) { $verdict } else { '(no verdict line)' }
}

$base = @{ ProofsDir = $scratch; RepoRoot = $WorkDir; Runs = 1 }

# ---- A. GREEN CONTROL ----------------------------------------------------------------------------

$a = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood') })
Assert-That 'A. GREEN CONTROL — a true model with no host step exits 0' ($a.Exit -eq 0) "exit $($a.Exit): $(Show-Tail $a)"
Assert-That 'A. GREEN CONTROL — and prints proofs: green' $a.Green (Show-Tail $a)
if ($a.Exit -ne 0 -or -not $a.Green) {
    Write-Host '==== leg-tests: the GREEN CONTROL is red, so the scratch apparatus is broken and arms B–D would prove nothing. Stopping.' -ForegroundColor Red
    Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    exit 1
}

# ---- B. HOST BUILD -------------------------------------------------------------------------------

$b = Invoke-Leg ($base + @{
        Modules = @('LegGood'); ProofOnly = @('LegGood')
        HostProject = 'nosuch'; HostProjectFile = 'nosuch/NoSuchProject.fsproj'; HostFilters = $hostFilters
    })
Assert-That 'B. HOST BUILD — a host project that does not exist exits NON-ZERO' ($b.Exit -ne 0) "exit $($b.Exit): $(Show-Tail $b)"
Assert-That 'B. HOST BUILD — and does not print proofs: green' (-not $b.Green) (Show-Tail $b)

# ---- C. HOST RUN ---------------------------------------------------------------------------------

$c = Invoke-Leg ($base + @{
        Modules = @('LegGood'); ProofOnly = @('LegGood')
        HostProject = 'host'; HostProjectFile = 'host/Host.proj'; HostFilters = $hostFilters
    })
Assert-That 'C. HOST RUN — a host filter that cannot run exits NON-ZERO' ($c.Exit -ne 0) "exit $($c.Exit): $(Show-Tail $c)"
Assert-That 'C. HOST RUN — and does not print proofs: green' (-not $c.Green) (Show-Tail $c)

# ---- D. CHECK ------------------------------------------------------------------------------------

$d = Invoke-Leg ($base + @{ Modules = @('LegBad'); ProofOnly = @('LegBad') })
Assert-That 'D. CHECK — a model with a type error exits NON-ZERO' ($d.Exit -ne 0) "exit $($d.Exit): $(Show-Tail $d)"
Assert-That 'D. CHECK — and does not print proofs: green' (-not $d.Green) (Show-Tail $d)
# Recorded 2026-09-25: a sibling copy of this kit printed `<module>.fst verified` over a refused
# model and went red only later. The per-module line is held too, not only the closing verdict.
Assert-That 'D. CHECK — and prints NO LegBad.fst verified line' (-not [bool](@($d.Lines -match 'LegBad\.fst verified').Count)) (Show-Tail $d)
Assert-That 'D. CHECK — and fails at the CHECK step, naming the module' ([bool](@($d.Lines -match '==== proofs: LegBad\.fst did NOT verify').Count)) (Show-Tail $d)

# ---- E. A SUCCESS LINE THAT NAMES A FAILURE ------------------------------------------------------

# Recorded 2026-09-26: the zero-exit diagnostic check matched `Quake[^\n]*fail` and refused TreeOps.fst,
# whose query `relocation_diamond_fails_for_a_remove` had `proved 8/8 goals`. A quake line is a failure
# unless it reads `proved N/N goals`; a query's NAME is not a verdict.
$e = Invoke-Leg ($base + @{ Modules = @('LegFailsName'); ProofOnly = @('LegFailsName') })
Assert-That 'E. NAMES — a true model whose query name contains "fails" exits 0' ($e.Exit -eq 0) "exit $($e.Exit): $(Show-Tail $e)"
Assert-That 'E. NAMES — and prints proofs: green' $e.Green (Show-Tail $e)

# ---- F. TWINS (Phase 309) --------------------------------------------------------------------------

# With -Twins, an EXTRACTED model must carry a normalised `twins` list. LegTwinned does and is green
# (extracted under -Extract into the scratch oracle); LegGood, extracted and twinless, is refused at
# the TWIN step before the prover runs; a -ProofOnly model needs none.
Set-Content (Join-Path $scratch 'LegTwinned.fst') @"
module LegTwinned

let double (x: nat) : nat = x + x

noeq type twin = { tname : string; tholds : unit -> bool }

let rec twins_hold (l: list twin) : Tot bool =
  match l with
  | [] -> true
  | t :: r -> t.tholds () && twins_hold r

let twins : list twin = [ { tname = "double-two"; tholds = (fun () -> double 2 = 4) } ]

let _ = assert_norm (twins_hold twins == true)
"@
@{
    kind    = 'proofModules'
    modules = @(
        @{ module = 'LegGood'; budgetSeconds = 60; fastestSeconds = 0 }
        @{ module = 'LegBad'; budgetSeconds = 60; fastestSeconds = 0 }
        @{ module = 'LegFailsName'; budgetSeconds = 60; fastestSeconds = 0 }
        @{ module = 'LegTwinned'; budgetSeconds = 60; fastestSeconds = 0 }
    )
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.json')

New-Item -ItemType Directory -Force (Join-Path $scratch 'oracle') | Out-Null
$f = Invoke-Leg ($base + @{ Modules = @('LegTwinned'); Twins = $true; Extract = $true })
Assert-That 'F. TWINS — an extracted model with normalised twins exits 0' ($f.Exit -eq 0) "exit $($f.Exit): $(Show-Tail $f)"
Assert-That 'F. TWINS — and says every extracted model is covered' ([bool](@($f.Lines -match 'twin evaluation covers all 1 extracted model').Count)) (Show-Tail $f)

$g = Invoke-Leg ($base + @{ Modules = @('LegGood'); Twins = $true; Extract = $true })
Assert-That 'G. TWINS — an extracted model with no twins exits NON-ZERO' ($g.Exit -ne 0) "exit $($g.Exit): $(Show-Tail $g)"
Assert-That 'G. TWINS — and names it at the TWIN step' ([bool](@($g.Lines -match 'twin evaluation does not cover every extracted model: LegGood').Count)) (Show-Tail $g)
Assert-That 'G. TWINS — and does not print proofs: green' (-not $g.Green) (Show-Tail $g)

$h = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); Twins = $true })
Assert-That 'H. TWINS — a -ProofOnly model needs no twins' ($h.Exit -eq 0) "exit $($h.Exit): $(Show-Tail $h)"

# ---- Q. THE GUARD IN THE LEG (Phase 399) -------------------------------------------------------------

# The guard runs AFTER extraction and BEFORE the host step. LegTwinned's oracle is the one arm F
# extracted; a planted oracle file beside it that opens a production namespace must fail the leg at
# the guard, naming the line — and the host step, here a project that does not exist, must never be
# reached, or the arm would be red for arm B's reason instead.
$planted = Join-Path $scratch 'oracle/Planted.fs'
Set-Content $planted "module Planted`n`nopen Fuaran.Core.Canon`n`nlet shortcut x = render x`n"
$q = Invoke-Leg ($base + @{
        Modules = @('LegTwinned'); Twins = $true
        HostProject = 'nosuch'; HostProjectFile = 'nosuch/NoSuchProject.fsproj'; HostFilters = $hostFilters
    })
Remove-Item $planted -Force
Assert-That 'Q. GUARD — a planted oracle file that opens production fails the leg' ($q.Exit -ne 0 -and -not $q.Green) "exit $($q.Exit): $(Show-Tail $q)"
Assert-That 'Q. GUARD — naming the file, the line and the text' ([bool](@($q.Lines -match 'Planted\.fs:3: .*open Fuaran\.Core\.Canon').Count)) (Show-Tail $q)
Assert-That 'Q. GUARD — after extraction (the oracle diff ran) and before the host step (no host build was attempted)' (
    [bool](@($q.Lines -match 'oracle/LegTwinned\.fs is byte-identical').Count) -and -not [bool](@($q.Lines -match 'the test project did not build').Count)) (Show-Tail $q)
$q2 = Invoke-Leg ($base + @{ Modules = @('LegTwinned'); Twins = $true })
Assert-That 'Q. GUARD CONTROL — the same leg without the plant is green, and says the guard held' ($q2.Exit -eq 0 -and $q2.Green -and [bool](@($q2.Lines -match 'oracle independence — the oracle project and its 1 source file').Count)) "exit $($q2.Exit): $(Show-Tail $q2)"

# ---- S. THE RUN FACTS (Phase 399) -------------------------------------------------------------------

# -SummaryFile is what a strict baseline records about its machine: a green leg writes the machine and
# one contention entry per run, and a red leg writes nothing, so no record can describe a failed run.
$factsPath = Join-Path $WorkDir 'run-facts.json'
$twoRuns = $base.Clone(); $twoRuns.Runs = 2
$s1 = Invoke-Leg ($twoRuns + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); SummaryFile = $factsPath })
$facts = if (Test-Path $factsPath) { Get-Content $factsPath -Raw | ConvertFrom-Json } else { $null }
Assert-That 'S. RUN FACTS — a green leg writes the machine and one contention entry per run' (
    $s1.Exit -eq 0 -and $null -ne $facts -and $facts.machine.kind -and $facts.machine.os -and $facts.machine.processors -and @($facts.contention).Count -eq 2 -and @($facts.contention)[1].run -eq 2) "exit $($s1.Exit): $(if ($facts) { $facts | ConvertTo-Json -Compress -Depth 6 } else { 'no file' })"
Remove-Item $factsPath -Force -ErrorAction SilentlyContinue
$s2 = Invoke-Leg ($base + @{ Modules = @('LegBad'); ProofOnly = @('LegBad'); SummaryFile = $factsPath })
Assert-That 'S. RUN FACTS — a red leg writes none' ($s2.Exit -ne 0 -and -not (Test-Path $factsPath)) "exit $($s2.Exit): $(Show-Tail $s2)"

# ---- P. CACHE PROVENANCE (Phase 402) ----------------------------------------------------------------

# The second writer, caught directly. LegUses depends on LegGood; LegThird depends on nothing.
Set-Content (Join-Path $scratch 'LegUses.fst') "module LegUses`n`nopen LegGood`n`nlet two : nat = one + one`n"
Set-Content (Join-Path $scratch 'LegThird.fst') "module LegThird`n`nlet three : nat = 3`n"
@{
    kind    = 'proofModules'
    modules = @(
        @{ module = 'LegGood'; budgetSeconds = 60; fastestSeconds = 0 }
        @{ module = 'LegFailsName'; budgetSeconds = 60; fastestSeconds = 0 }
        @{ module = 'LegUses'; budgetSeconds = 60; fastestSeconds = 0 }
        @{ module = 'LegThird'; budgetSeconds = 60; fastestSeconds = 0 }
    )
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.provenance.json')
$provenance = $base + @{ BudgetFile = (Join-Path $scratch 'modules.provenance.json') }

# The CONTROL: a dependency checked first leaves its own `.checked` file for the dependent to read,
# which is this run's own write and no second writer.
$p0 = Invoke-Leg ($provenance + @{ Modules = @('LegGood', 'LegUses'); ProofOnly = @('LegGood', 'LegUses') })
Assert-That 'P. PROVENANCE CONTROL — a dependency checked before its dependent exits 0 and green' ($p0.Exit -eq 0 -and $p0.Green) "exit $($p0.Exit): $(Show-Tail $p0)"

# The second CONTROL, dependent first. The pinned prover writes a module's `.checked` file only when
# it checks that module itself, never for a dependency it checks on the way (measured 2026-10-07), so
# checking LegUses leaves LegGood's own file absent and LegGood's check after it is still cold. If a
# prover release ever starts caching dependencies, this arm goes red, and the rule's premise with it.
$p1 = Invoke-Leg ($provenance + @{ Modules = @('LegUses', 'LegGood'); ProofOnly = @('LegUses', 'LegGood') })
Assert-That 'P. PROVENANCE CONTROL — a dependent checked before its dependency leaves the dependency cold: exit 0 and green' ($p1.Exit -eq 0 -and $p1.Green) "exit $($p1.Exit): $(Show-Tail $p1)"

# A SECOND WRITER, planted SYNCHRONOUSLY through the leg's -AfterInvocation seam: after LegGood's
# invocation, at the one point between invocations where a foreign writer acts. Until Phase 402's
# rework this was a concurrent runspace polling for LegGood's `.checked` file; on a Linux runner,
# where these models check in well under a second, it could start after the leg had already reached
# LegThird, and the arm read green on some runs. Nothing here depends on scheduling now: the plant
# runs on the leg's own thread, before the leg reads the cache again.

# 1. A file APPEARS — LegThird's own `.checked`, before LegThird's turn.
$p2 = Invoke-Leg ($provenance + @{
        Modules = @('LegGood', 'LegFailsName', 'LegThird'); ProofOnly = @('LegGood', 'LegFailsName', 'LegThird')
        AfterInvocation = {
            param($module, $run, $cacheDir)
            if ($module -eq 'LegGood') { Set-Content (Join-Path $cacheDir 'LegThird.fst.checked') 'forged by a second writer' }
        }
    })
Assert-That 'P. SECOND WRITER — a file another writer put in the cache is refused' ($p2.Exit -ne 0 -and -not $p2.Green) "exit $($p2.Exit): $(Show-Tail $p2)"
Assert-That 'P. SECOND WRITER — before the next module is checked, naming the file' ([bool](@($p2.Lines -match 'SECOND WRITER.*before LegFailsName\.fst.*LegThird\.fst\.checked appeared').Count)) (Show-Tail $p2)
Assert-That 'P. SECOND WRITER — and prints no LegFailsName.fst or LegThird.fst verified line' (-not [bool](@($p2.Lines -match '(LegFailsName|LegThird)\.fst verified').Count)) (Show-Tail $p2)

# 2. A file is REWRITTEN with the same length and its old timestamp restored — invisible to any
# check that reads the clock, which is why the state is the bytes' hash.
$p3 = Invoke-Leg ($provenance + @{
        Modules = @('LegGood', 'LegFailsName'); ProofOnly = @('LegGood', 'LegFailsName')
        AfterInvocation = {
            param($module, $run, $cacheDir)
            if ($module -eq 'LegGood') {
                $path = Join-Path $cacheDir 'LegGood.fst.checked'
                $stamp = (Get-Item -LiteralPath $path).LastWriteTimeUtc
                $bytes = [System.IO.File]::ReadAllBytes($path)
                $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
                [System.IO.File]::WriteAllBytes($path, $bytes)
                (Get-Item -LiteralPath $path).LastWriteTimeUtc = $stamp
            }
        }
    })
Assert-That 'P. SECOND WRITER — a same-length rewrite with its timestamp restored is refused' ($p3.Exit -ne 0 -and -not $p3.Green) "exit $($p3.Exit): $(Show-Tail $p3)"
Assert-That 'P. SECOND WRITER — naming the rewritten file' ([bool](@($p3.Lines -match 'SECOND WRITER.*LegGood\.fst\.checked was rewritten').Count)) (Show-Tail $p3)

# 3. The seam itself changes nothing: a plant that writes nothing leaves the leg green.
$p4 = Invoke-Leg ($provenance + @{
        Modules = @('LegGood', 'LegFailsName'); ProofOnly = @('LegGood', 'LegFailsName')
        AfterInvocation = { param($module, $run, $cacheDir) }
    })
Assert-That 'P. SEAM CONTROL — an -AfterInvocation that writes nothing leaves the leg green' ($p4.Exit -eq 0 -and $p4.Green) "exit $($p4.Exit): $(Show-Tail $p4)"

# ---- C. THE CACHED-READ GATE, AND COST NEVER RED (Phase 399) ------------------------------------------

# A warm re-run is red. The leg empties its cache at the head of every run, so the only way a
# module can be read back rather than checked is a writer putting a REAL checked file in front of
# it. Here that file is genuine: LegFailsName's own `.checked`, produced by a green run into a
# -CacheDir (which the leg leaves in place), and copied into the next run's cache just before
# LegFailsName's turn. The cache provenance check — the PRIMARY signal — refuses it before the
# prover can read it back.
$warmCache = Join-Path $WorkDir 'warm-cache'
$c0 = Invoke-Leg ($base + @{ Modules = @('LegFailsName'); ProofOnly = @('LegFailsName'); CacheDir = $warmCache })
$warmChecked = Join-Path $WorkDir 'LegFailsName.fst.checked'
Copy-Item (Join-Path $warmCache 'LegFailsName.fst.checked') $warmChecked -ErrorAction SilentlyContinue
Assert-That 'C. WARM RE-RUN — a green run leaves a genuine LegFailsName.fst.checked to re-run against' ($c0.Exit -eq 0 -and $c0.Green -and (Test-Path $warmChecked)) "exit $($c0.Exit): $(Show-Tail $c0)"
$c1 = Invoke-Leg ($base + @{
        Modules = @('LegGood', 'LegFailsName'); ProofOnly = @('LegGood', 'LegFailsName')
        AfterInvocation = {
            param($module, $run, $cacheDir)
            if ($module -eq 'LegGood') { Copy-Item $warmChecked (Join-Path $cacheDir 'LegFailsName.fst.checked') }
        }
    })
Assert-That 'C. WARM RE-RUN — a module whose genuine checked file is already in the cache is red, never green' ($c1.Exit -ne 0 -and -not $c1.Green -and [bool](@($c1.Lines -match 'SECOND WRITER.*LegFailsName\.fst\.checked').Count)) "exit $($c1.Exit): $(Show-Tail $c1)"
Assert-That 'C. WARM RE-RUN — and prints no LegFailsName.fst verified line' (-not [bool](@($c1.Lines -match 'LegFailsName\.fst verified').Count)) (Show-Tail $c1)

# The BACKSTOP: a check that finishes under the cached-read threshold is red as a probable cached
# read. LegGood genuinely checks in about a second, so a 30s threshold applied to it stands in for a
# read back from a cache a writer filled DURING the invocation, which no between-invocation check can
# see. Applied only from the module's recorded fastestSeconds: the same threshold over the same
# module, recorded as genuinely that fast, leaves it to the provenance check and stays green.
function Set-CachedReadBudget([double] $fastestSeconds) {
    @{
        kind       = 'proofModules'
        cachedRead = @{ thresholdSeconds = 30; appliesFromFastestSeconds = 90 }
        modules    = @(@{ module = 'LegGood'; budgetSeconds = 60; fastestSeconds = $fastestSeconds })
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.cached.json')
}
Set-CachedReadBudget 120
$c2 = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); BudgetFile = (Join-Path $scratch 'modules.cached.json') })
Assert-That 'C. CACHED READ — a check under the threshold, of a module recorded as slower, is red' ($c2.Exit -ne 0 -and -not $c2.Green) "exit $($c2.Exit): $(Show-Tail $c2)"
Assert-That 'C. CACHED READ — and names it a probable cached read, with the threshold' ([bool](@($c2.Lines -match 'under the 30s cached-read threshold .* PROBABLE CACHED READ').Count)) (Show-Tail $c2)
Assert-That 'C. CACHED READ — and prints no green LegGood.fst verified line before refusing it' (-not [bool](@($c2.Lines -match 'LegGood\.fst verified — run').Count)) (Show-Tail $c2)
Set-CachedReadBudget 2
$c3 = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); BudgetFile = (Join-Path $scratch 'modules.cached.json') })
Assert-That 'C. CACHED READ CONTROL — a module recorded as genuinely that fast is not judged by the threshold: green' ($c3.Exit -eq 0 -and $c3.Green) "exit $($c3.Exit): $(Show-Tail $c3)"
Assert-That 'C. CACHED READ CONTROL — and says it rests on the provenance check alone' ([bool](@($c3.Lines -match 'LegGood can genuinely check faster and rest(s)? on the cache provenance check alone').Count)) (Show-Tail $c3)

# A retired floor is refused, not read past.
@{ kind = 'proofModules'; modules = @(@{ module = 'LegGood'; budgetSeconds = 60; floorSeconds = 0 }) } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.retired.json')
$c4 = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); BudgetFile = (Join-Path $scratch 'modules.retired.json') })
Assert-That 'C. RETIRED — a budget entry still carrying floorSeconds is refused, naming the key' ($c4.Exit -ne 0 -and -not $c4.Green -and [bool](@($c4.Lines -match 'still carries a floorSeconds').Count)) "exit $($c4.Exit): $(Show-Tail $c4)"

# A COST OVERRUN IS NEVER RED, under -Strict. LegSlow is a true model that takes a couple of seconds
# to normalise; against a 1s budget it is a slow cold check, several times over budget. Under -Strict
# the leg is green, the COST finding is printed, and -SummaryFile records the module's time against
# its budget with the percentage and the finding.
Set-Content (Join-Path $scratch 'LegSlow.fst') "module LegSlow`n`nlet rec fib (n: nat) : nat = if n < 2 then n else fib (n - 1) + fib (n - 2)`n`nlet _ = assert_norm (fib 26 > 0)`n"
@{
    kind       = 'proofModules'
    cachedRead = @{ thresholdSeconds = 0.1; appliesFromFastestSeconds = 0.3 }
    modules    = @(@{ module = 'LegSlow'; budgetSeconds = 1; fastestSeconds = 1 })
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.slow.json')
$slowFacts = Join-Path $WorkDir 'slow-facts.json'
$c5 = Invoke-Leg ($base + @{ Modules = @('LegSlow'); ProofOnly = @('LegSlow'); Strict = $true; SummaryFile = $slowFacts; BudgetFile = (Join-Path $scratch 'modules.slow.json') })
$slow = if (Test-Path $slowFacts) { Get-Content $slowFacts -Raw | ConvertFrom-Json } else { $null }
$slowCost = if ($slow) { @($slow.costs | Where-Object { $_.module -eq 'LegSlow' }) | Select-Object -First 1 } else { $null }
Assert-That 'C. COST — a cold check over its budget under -Strict is GREEN' ($c5.Exit -eq 0 -and $c5.Green) "exit $($c5.Exit): $(Show-Tail $c5)"
Assert-That 'C. COST — and the overrun is printed as a COST finding' ([bool](@($c5.Lines -match 'COST — LegSlow\.fst took \d+s against its 1s budget').Count)) (Show-Tail $c5)
Assert-That 'C. COST — and recorded in the run facts with its percentage and the finding' (
    $null -ne $slowCost -and $slowCost.budget -eq 1 -and $slowCost.percent -gt 100 -and [bool](@($slow.findings | Where-Object { $_.text -match 'LegSlow\.fst took' }).Count)) "$(if ($slow) { $slow | ConvertTo-Json -Compress -Depth 6 } else { 'no facts file' })"

# THE THRESHOLD AT ITS REAL NUMBERS (Phase 399): this repository's own `cachedRead` block, 1.0s from
# 3s. LegGood is a SUB-SECOND module: its genuine cold check takes about 0.2s, under the threshold,
# and its recorded fastestSeconds of 0 exempts it. LegSlow is a module whose cold check takes
# seconds. Both checked cold: green, the sub-second one included.
@{
    kind       = 'proofModules'
    cachedRead = @{ thresholdSeconds = 1.0; appliesFromFastestSeconds = 3 }
    modules    = @(
        @{ module = 'LegGood'; budgetSeconds = 20; fastestSeconds = 0 }
        @{ module = 'LegSlow'; budgetSeconds = 60; fastestSeconds = 3 }
    )
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.real.json')
$real = $base + @{ BudgetFile = (Join-Path $scratch 'modules.real.json') }
$t1 = Invoke-Leg ($real + @{ Modules = @('LegGood', 'LegSlow'); ProofOnly = @('LegGood', 'LegSlow') })
Assert-That 'C. SUB-SECOND — a sub-second module''s genuine cold check stays green under the 1s threshold' ($t1.Exit -eq 0 -and $t1.Green -and [bool](@($t1.Lines -match 'LegGood\.fst verified — run 1 of 1, [01]s').Count)) "exit $($t1.Exit): $(Show-Tail $t1)"

# A WARM re-run of the slow module is red. A green run into a -CacheDir leaves LegSlow's genuine
# checked file; the -BeforeInvocation seam puts it in front of LegSlow's check AFTER the provenance
# check has passed — a writer during the invocation, which only the threshold can see — and the
# prover reads it back instead of checking it.
$slowCache = Join-Path $WorkDir 'slow-cache'
$t2 = Invoke-Leg ($real + @{ Modules = @('LegSlow'); ProofOnly = @('LegSlow'); CacheDir = $slowCache })
$slowChecked = Join-Path $WorkDir 'LegSlow.fst.checked'
Copy-Item (Join-Path $slowCache 'LegSlow.fst.checked') $slowChecked -ErrorAction SilentlyContinue
Assert-That 'C. WARM — a green run of the slow module leaves its genuine checked file' ($t2.Exit -eq 0 -and $t2.Green -and (Test-Path $slowChecked)) "exit $($t2.Exit): $(Show-Tail $t2)"
$t3 = Invoke-Leg ($real + @{
        Modules = @('LegSlow'); ProofOnly = @('LegSlow')
        BeforeInvocation = { param($module, $run, $cacheDir) if ($module -eq 'LegSlow') { Copy-Item $slowChecked (Join-Path $cacheDir 'LegSlow.fst.checked') } }
    })
Assert-That 'C. WARM — a warm re-run of the slow module is red as a probable cached read, under the real 1s threshold' (
    $t3.Exit -ne 0 -and -not $t3.Green -and [bool](@($t3.Lines -match 'LegSlow\.fst verified in 0\.\d+s .* under the 1s cached-read threshold .* PROBABLE CACHED READ').Count)) "exit $($t3.Exit): $(Show-Tail $t3)"

# What -Strict still promotes: a declaration defect. LegGood with no budget entry at all is a
# coverage finding, which is red under -Strict and a warning without it.
@{ kind = 'proofModules'; cachedRead = @{ thresholdSeconds = 1; appliesFromFastestSeconds = 3 }; modules = @() } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $scratch 'modules.empty.json')
$c6 = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); Strict = $true; BudgetFile = (Join-Path $scratch 'modules.empty.json') })
Assert-That 'C. DECLARATION — under -Strict a module with no budget is red' ($c6.Exit -ne 0 -and -not $c6.Green -and [bool](@($c6.Lines -match 'budget declaration is incomplete').Count)) "exit $($c6.Exit): $(Show-Tail $c6)"
$c7 = Invoke-Leg ($base + @{ Modules = @('LegGood'); ProofOnly = @('LegGood'); BudgetFile = (Join-Path $scratch 'modules.empty.json') })
Assert-That 'C. DECLARATION — and without -Strict the same leg is green, with the finding printed' ($c7.Exit -eq 0 -and $c7.Green -and [bool](@($c7.Lines -match 'declares no budget for it').Count)) "exit $($c7.Exit): $(Show-Tail $c7)"

Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue

if ($script:failures.Count -gt 0) {
    Write-Host "==== leg-tests: RED — $($script:failures.Count) of $script:cases assertion(s) failed; the leg reported a step it could not run as green" -ForegroundColor Red
    exit 1
}
Write-Host "==== leg-tests: $script:cases assertions held — every step's failure is REFUSED, not merely printed" -ForegroundColor Green
exit 0
