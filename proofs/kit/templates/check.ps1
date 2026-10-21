#Requires -Version 7.0
# <REPO> — the proof leg. TEMPLATE: copy to <repo>/proofs/check.ps1 and edit the two declarations
# below. Nothing else in this file needs changing, and nothing in the kit does.
#
# THE KIT IS A PINNED RELEASE ASSET (Phase 536). `proofs/proofs-kit.json` names it — `kit`, `asset`,
# `url`, `sha256`, the `fstar-pin.json` shape — and the lower half of this file downloads it into
# proofs/.kit/ (or takes the local file -KitAsset / FUARAN_PROOFS_KIT_ASSET names: the offline path),
# REFUSES it unless its sha256 is the pin's, unpacks it fresh, and runs the engine from it with the
# asset's prover pin, its read-only models and its runtime floor. Nothing of the kit is copied here.
#
# THE ENGINE IS `kit/check-proof-leg.ps1` in the asset and this file is the caller: it hands the kit what
# THIS repository has — the models (read from modules.json), where the oracle host lives, which
# test families the host step runs — and the kit runs the leg. Read the kit script's header for what the three steps are and why
# each is shaped the way it is; read `kit/README.md` for how a repository adopts the kit.
#
# The flags this file forwards are the leg's public surface:
#   -Runs N          N cold-cache verifications of every model (CI asks for 3)
#   -Extract         rewrite the committed oracle/*.fs from a fresh extraction, then commit them
#   -SkipOracleHost  leave the host families to the repository's own gate, which runs the suite
#   -Strict          the recorded run: a cost overrun stays a finding (never red, Phase 399); an
#                    incomplete budget declaration is red
#   -CacheDir <dir>  put the checked-module cache somewhere you name
#   -KitAsset <file> the kit asset as a local file instead of the pin's url (verified all the same)
[CmdletBinding()]
param(
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [string] $CacheDir,
    [int]    $Runs = 1,
    [string] $KitAsset
)

$ErrorActionPreference = 'Stop'

# ---- the roster: modules.json ---------------------------------------------------------------------
# NOT a declaration of this file (since the reference repository's Phase 504): the models are the
# entries of modules.json, each with a `kind` and an `opens` list, in check order — a model after
# what it opens. `extracted` models are checked, extracted to proofs/oracle/<name>.fs and diffed;
# `proofOnly` and `generated` ones are checked only; `shared` ones (`Twin`, `ListAlgebra`, read-only
# from the kit asset) are checked first and extracted, and are no model's subject. A kit model you
# open (`DagFold`, `Limits`, ...) is an entry like any other, resolved from the asset. Adding a
# model is adding ONE entry to modules.json. Nothing here needs editing for it.
$roster = @((Get-Content (Join-Path $PSScriptRoot 'modules.json') -Raw | ConvertFrom-Json).modules)
$kinds = @('extracted', 'proofOnly', 'generated', 'shared')
$misdeclared = @($roster | Where-Object { $kinds -notcontains $_.kind -or $null -eq $_.opens } | ForEach-Object { $_.module })
if ($roster.Count -eq 0 -or $misdeclared.Count -gt 0) {
    Write-Host "==== proofs: modules.json is not a roster: no module, or $($misdeclared -join ', ') declare(s) no kind among $($kinds -join ', ') or no opens list" -ForegroundColor Red
    exit 2
}
$modules = @($roster | ForEach-Object { $_.module })
$proofOnly = @($roster | Where-Object { $_.kind -in 'proofOnly', 'generated' } | ForEach-Object { $_.module })

# ---- DECLARATION 1: the host families --------------------------------------------------------------
# One entry per test-runner invocation, each with its own failure message. Separate invocations
# rather than one prefix filter, so two failures read as what they are rather than as one red suite.
# An EMPTY list is a legitimate shape for a repository whose models have no differential yet: the
# leg then checks, extracts and diffs, and says so.
$hostFilters = @(
    @{
        Filter  = 'Proofs.Oracle'
        Failure = 'the oracle host (Proofs.Oracle) is RED — an extracted model and production disagree'
    }
)

# ---- DECLARATION 2: where the host lives ---------------------------------------------------------
# Paths relative to the repository root (the parent of proofs/, unless you pass -RepoRoot).
$hostProject = 'tests/<Your>.Tests'
$hostProjectFile = 'tests/<Your>.Tests/<Your>.Tests.fsproj'

# ---- nothing below here is per-repository ----------------------------------------------------------

# ---- the kit: the pinned release asset, verified before anything runs ------------------------------
$kitPinFile = Join-Path $PSScriptRoot 'proofs-kit.json'
$kitPin = if (Test-Path $kitPinFile) { Get-Content $kitPinFile -Raw | ConvertFrom-Json } else { $null }
if ($null -eq $kitPin -or $kitPin.kind -ne 'proofsKitPin' -or -not $kitPin.asset -or -not $kitPin.url -or $kitPin.sha256 -notmatch '^[0-9a-f]{64}$') {
    Write-Host "==== proofs: $kitPinFile is not a kit pin (kind proofsKitPin, kit, asset, url, a 64-hex sha256)" -ForegroundColor Red
    exit 2
}
$kitRoot = Join-Path $PSScriptRoot '.kit'
New-Item -ItemType Directory -Force $kitRoot | Out-Null
$archive = Join-Path $kitRoot $kitPin.asset
if (-not $KitAsset) { $KitAsset = $env:FUARAN_PROOFS_KIT_ASSET }
if ($KitAsset) {
    $kitSource = [System.IO.Path]::GetFullPath($KitAsset)
    Copy-Item $kitSource $archive -Force
}
else {
    $kitSource = $kitPin.url
    $held = (Test-Path $archive) -and (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -eq $kitPin.sha256
    if (-not $held) { Invoke-WebRequest -Uri $kitPin.url -OutFile $archive -UseBasicParsing }
}
$kitSha = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($kitSha -ne $kitPin.sha256) {
    Remove-Item $archive -Force
    Write-Host "==== proofs: REFUSED the kit asset from $kitSource — its sha256 is $kitSha, the pin ($kitPinFile) is $($kitPin.sha256). Nothing ran." -ForegroundColor Red
    exit 2
}
# Unpacked fresh per invocation, so this run reads exactly the verified bytes; dead runs' trees are swept.
foreach ($stale in @(Get-ChildItem $kitRoot -Directory -Filter 'run-*' -ErrorAction SilentlyContinue)) {
    $stalePid = 0
    if ([int]::TryParse($stale.Name.Substring(4), [ref] $stalePid) -and $stalePid -ne $PID -and -not (Get-Process -Id $stalePid -ErrorAction SilentlyContinue)) {
        Remove-Item $stale.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}
$kit = Join-Path $kitRoot "run-$PID"
if (Test-Path $kit) { Remove-Item $kit -Recurse -Force }
New-Item -ItemType Directory -Force $kit | Out-Null
[System.Formats.Tar.TarFile]::ExtractToDirectory($archive, $kit, $false)
Write-Host "==== proofs: kit $($kitPin.kit) ($($kitPin.asset), sha256 $kitSha) from $kitSource" -ForegroundColor Cyan
# The host step inherits this: a ladder family holding a row about a kit model reads that model's text
# from the verified asset here, since the model is not in this tree (kit/README.md, "Importing a theorem").
$env:FUARAN_PROOFS_KIT_DIR = $kit

$legArgs = @{
    Modules         = $modules
    ProofOnly       = $proofOnly
    ProofsDir       = $PSScriptRoot
    HostProject     = $hostProject
    HostProjectFile = $hostProjectFile
    HostFilters     = $hostFilters
    Runs            = $Runs
    PinFile         = Join-Path $kit 'fstar-pin.json'
    ModelDirs       = @(Join-Path $kit 'models')
    FloorDir        = Join-Path $kit 'oracle'
}
if ($Extract) { $legArgs.Extract = $true }
if ($SkipOracleHost) { $legArgs.SkipOracleHost = $true }
if ($Strict) { $legArgs.Strict = $true }
if ($CacheDir) { $legArgs.CacheDir = $CacheDir }
# The run verdict: one JSON record of every run, green or red (the kit engine's header, "THE RUN
# VERDICT"). Under the gitignored obj/, so a reader — a CI summary, a strict record you commit — reads
# the record rather than the transcript.
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'obj') | Out-Null
$legArgs.SummaryFile = Join-Path $PSScriptRoot 'obj/last-run.json'

# `&` and not `.`: a dot-sourced script's `exit` does NOT propagate to its caller, so a dot-source
# here would print the kit's red line and then return 0 — a green leg over a failed proof. Measured
# both ways before this template was written; do not "simplify" it to a dot-source.
& (Join-Path $kit 'kit/check-proof-leg.ps1') @legArgs
if ($LASTEXITCODE -ne 0) { Remove-Item $kit -Recurse -Force -ErrorAction SilentlyContinue; exit $LASTEXITCODE }

# Then the engine's own refusals, run the same way (Phase 221): a scratch leg over a failed host
# build, a host filter that cannot run and a refuted model must each exit non-zero, beside a green
# control. A few seconds, and after the leg so the prover is already installed. Needs nothing from
# this file's declarations — keep it as it is.
& (Join-Path $kit 'kit/check-proof-leg.tests.ps1') -ProofsDir $PSScriptRoot -PinFile (Join-Path $kit 'fstar-pin.json')
$testsExit = $LASTEXITCODE
Remove-Item $kit -Recurse -Force -ErrorAction SilentlyContinue
exit $testsExit
