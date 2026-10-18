#Requires -Version 7.0
# fuaran-core — the proof leg (Phases 131, 135, 136, 148, 149, 150, 151, 164, 155, 173, 176, 177, 187 and 157).
#
# THE ENGINE IS `kit/check-proof-leg.ps1` (Phase 155) and this file is the caller: it hands the kit
# what THIS repository has — the models and which of them are checked but not extracted (read from
# `modules.json`, the roster, since Phase 504), where the oracle host lives, and which Expecto
# families the host step runs — and the kit runs the leg.
# Read the kit script's header for what the three steps are and why each is shaped the way it is;
# read `kit/README.md` for what the kit is and how another repository adopts it. Nothing about the
# mechanism lives here any more, and nothing about this repository lives in the kit.
#
# Step 2b (Phase 150; re-sourced by Phase 173) is this repository's own and sits in the host step
# below rather than in the engine: the GENERATED models and their proof scripts are held to a fresh
# generation from the certification vocabularies in `tests/Fuaran.Core.Tests` by the
# Proofs.Vocabulary family — the same discipline as the engine's extraction diff, one level further
# up. Step 2 says the oracle is the model; this says the model is the vocabulary the engine is
# certified on. Nothing in this leg reads the shared corpus for the generated files any more.
#
# The flags are unchanged and are forwarded verbatim:
#   -Runs N          N cold-cache verifications of every model (CI asks for 3)
#   -Extract         rewrite the committed oracle/*.fs from a fresh extraction, then commit them
#   -SkipOracleHost  leave the Expecto families to ./verify.ps1, which runs the whole suite
#   -Strict          the recorded run: a cost overrun stays a finding (never red, Phase 399); an
#                    incomplete budget declaration is red
#   -CacheDir <dir>  put the checked-module cache somewhere you name
#
# Phase 328 adds the MODULE-CONE SELECTOR — three flags, and with none of them the leg is exactly
# the leg it was (the kit is handed the same arguments, so its verdict cannot move):
#   -Since <tree>    verify, cold, only the models whose inputs changed between <tree> and the
#                    working tree — the cone — and print every registered module IN, DEP or OUT
#                    with the reason. An EMPTY cone is green only over a recorded strict run.
#   -Modules <list>  name a cone by hand (`-Modules TreeOps,Skeleton`): those modules, and (since
#                    Phase 522) what they reference, checked ahead of them as -Since checks it.
#   -PlanOnly        print what the kit would be handed, and stop before the prover runs.
#
# EVERY RUN of the leg leaves ONE record (Phase 522): the kit's run verdict (its header, "THE RUN
# VERDICT"), with this script's `cone` added, at proofs/obj/last-run.json — green or red. A green
# -Strict FULL run's record, with the tree it verified, is proofs/last-strict.json (commit the file):
# the baseline an empty cone leans on, the record .github/workflows/proofs-strict.yml hands over
# weekly, and the same shape as every other run's. The selector section below says how the cone is
# computed and why each input puts a module in it.
[CmdletBinding()]
param(
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [string] $CacheDir,
    [int]    $Runs = 1,
    [string] $Since,
    # Bound as -Modules; named $Cone because PowerShell variables ignore case, and `$modules` is the
    # roster read below, which would overwrite a parameter of that name.
    [Alias('Modules')][string[]] $Cone,
    [switch] $PlanOnly
)

$ErrorActionPreference = 'Stop'

# THE ROSTER IS `modules.json` (Phase 504). Each entry names a module, its `kind` and its `opens`
# (the registered modules its code references), in CHECK ORDER: every module after the modules it
# opens. The four kinds:
#   extracted — checked, extracted to oracle/<name>.fs, twinned, and run beside production by the
#               Proofs.Oracle family.
#   proofOnly — checked and NOT extracted: production cannot be run beside an extraction. The
#               Phase 306, 308 and 362 paragraphs below say why for each.
#   generated — emitted by `Fuaran.Core.Idl.Codegen`'s F* target; checked and not extracted, and
#               held to a fresh generation by the Proofs.Vocabulary family (the Phase 150 and 173
#               paragraphs below).
#   shared    — not a model: `Twin` (the twin record) and `ListAlgebra` (the Prims-only list algebra),
#               opened by the models and referencing none. Checked FIRST and extracted, because
#               every extraction after them needs their checked files; they carry no ladder row.
# What each model is ABOUT is its .fst header and its section of README.md; what it costs is its
# entry's budget. Adding a model is adding ONE entry to modules.json: nothing else enumerates the
# models, here, in the tests or in the kit. The Proofs.Ladder family holds each entry's `opens` to
# the model's code, the twin roster and the oracle project to the extracted set, and every model to
# a `proved` row; the Proofs.Cone family reads the same file for the roster it selects from, and its
# -PlanOnly cases hold this script's reading of it to the tests' reading.
$rosterPath = Join-Path $PSScriptRoot 'modules.json'
$roster = @((Get-Content $rosterPath -Raw | ConvertFrom-Json).modules)
$kinds = @('extracted', 'proofOnly', 'generated', 'shared')
$misdeclared = @($roster | Where-Object { $kinds -notcontains $_.kind -or $null -eq $_.opens } | ForEach-Object { $_.module })
if ($roster.Count -eq 0 -or $misdeclared.Count -gt 0) {
    Write-Host ("==== proofs: $rosterPath is not a roster: " +
        $(if ($roster.Count -eq 0) { 'it registers no module' } else { "$($misdeclared -join ', ') declare(s) no ``kind`` among $($kinds -join ', ') or no ``opens`` list" })) -ForegroundColor Red
    exit 2
}
$modules = @($roster | ForEach-Object { $_.module })

# Phase 173 — the generated files are about the CERTIFICATION SET, and that is why the theorems
# are committed now when Phase 150 could not commit them.
#
# `Fuaran.Core.Idl.Codegen`'s F* target emits both: `FStarTarget.vocabularyModuleFrom` for a model
# and `FStarTarget.proofsModuleFrom` for the round trip over it. Phase 150 generated the one model
# from the shared corpus's `idl.json` — the UI vocabulary, twenty selected kinds over a node
# envelope with five optional members — and measured the emitted proof script NOT discharging at
# that scale (one 65-goal node query failing a `--quake` seed; a 2^k blow-up in the widest kind's
# arm), so it committed the model alone, at 322–398s a check. That was a domain's proof running in
# the substrate's CI, which D14 had already ruled out for `tests/` (Phase 114). Phase 173 applies
# the same rule to `proofs/`: the generation source is the set the engine is CERTIFIED on — the
# reference vocabulary and the two vendored non-UI samples, the same set the F# and TypeScript
# backends are certified over in `IdlCertificationTests` — so what this leg proves is the F*
# BACKEND. Over that set the round trip discharges under the leg's own flags in seconds (the six
# budgets in modules.json), so the proof scripts are committed and checked like every other module.
# The UI vocabulary's model, proofs, cost and exhaustive-coverage decision are the adopter's —
# `fuaran#1754`, the kit's first adopter — where the cost is charged to the commits that change UI
# kinds. The per-kind lemma shape that reaches that scale SHIPPED as Phase 168 — one lemma per
# constructor, one per presence pattern — and Phase 182 made the presence split LINEAR in the
# conditional members after the adopter measured 168's own 2^k, in the lemma COUNT this time, at
# 71,722 lemmas in a 114 MB script. The measurements that motivated both, and the model-side
# exponential that neither fixes, are kept in `proofs/README.md`'s theorem 1 section under its own
# heading.

# Phase 150 — the generated modules are CHECKED but not EXTRACTED, and why an exemption exists at all.
#
# An oracle is here so the Expecto differential can run the extracted model beside the production
# code over the same inputs. The generated modules have no production code on this side to run
# beside: they model the vocabulary an `Idl` DECLARES, and the decoder they model is one a
# GENERATOR emits into a consuming host rather than one this repository ships. Extracting them
# anyway would commit generated F# that nothing compiles, calls or compares — which is what an
# oracle is supposed to be the opposite of. (At the UI vocabulary's scale that was ~400 KB; the
# extractor also emits a mutual TYPE group with the `and` indented one space, which F# 10's parser
# rejects outright even under the oracle project's `--strict-indentation-` — a real finding about
# the F# backend, and NOT the reason for this exemption. An oracle nothing runs would not be worth
# committing even if it compiled.) A proof script extracts to nothing at all.
#
# The exemption is NARROW and it is not a hole in the discipline: what step 2 buys for the other
# models — "the artefact is the model, byte for byte" — these get from the GENERATION diff in the
# host step instead, one level further up, against the vocabularies they are generated from.
#
# Phase 306 makes `Utf8` proofOnly, for a different reason and it is said separately so the two
# are not confused. The generated modules are exempt because nothing here runs beside them. `Utf8`
# HAS production code beside it — `Hash.utf8Bytes` — but its units and bytes are INTEGERS, and F*'s
# `int` does not survive the extraction this leg uses (README, finding 2): an extracted `Utf8.fs`
# would compute in a type the oracle project cannot run at production's values. Its bridge is the
# independent-oracle differential instead (the Proofs.Oracle family holds `Hash.utf8Bytes` to the
# platform's own encoder over every code unit), and the ladder records the model-to-code step as
# assumed, with that differential as its evidence.
#
# Phase 308 adds `PropagationOps` on Utf8's footing, for a third reason. Its production side reads
# a node through the caller's `readsOf` and the witness, and the model reads a node's content as
# its kind tag with the reads a parameter over the model's tree; an extraction would run beside
# production only through a bridge that re-encodes both, which is the step under test. So the
# Proofs.Oracle family holds production's `changedForOp` to the THEOREM'S STATEMENT directly —
# over generated trees and every op kind, a nested Batch included, computing from the two trees
# the set `changed_for_op_complete` says is named — and the ladder records the model-to-code step
# as assumed (`propagation-ops-model-bridge`), with that differential as its evidence.
#
# Phase 362 adds `TreeFrame` on PropagationOps's footing. Its theorems are about `Ops.apply` and
# `Ops.footprint`, which TreeOps already extracts and the oracle already runs; what is new is the
# model of the write gate, and production's gate reads a tree through the witness where the model
# reads its own. `Conformance.writeGateLaws` already holds production's `targetsOf` to the
# STATEMENT of `targets_cover_written` at a domain's witnesses, so the ladder records the
# model-to-code step as assumed (`write-gate-model-bridge`) with that law as its evidence.
$proofOnly = @($roster | Where-Object { $_.kind -in 'proofOnly', 'generated' } | ForEach-Object { $_.module })

# The host step, in this order. Separate invocations rather than one prefix filter, so each failure
# reads as what it is rather than as one red suite.
#   Proofs.Vocabulary — Phase 150's GENERATION diff, beside the engine's extraction diff and for
#                       the same reason one step further up: it holds each committed model and
#                       proof script to a fresh generation from the certification vocabulary it
#                       is generated from (Phase 173: `ReferenceIdl.refIdl` and the two vendored
#                       samples, in the test project — no corpus read), so the vocabulary the
#                       theorem is about is the vocabulary the engine is certified on. A vocabulary
#                       that moves without a regeneration is VOCABULARY DRIFT, and this is where it
#                       is named. It runs here rather than ahead of the prover because the
#                       generator is F# and the host step is the first thing with a built test
#                       project to hand — and because the leg fails either way: a stale model still
#                       verifies, and then this step reports what it is stale against.
#   Proofs.Oracle     — the differential: each extracted model beside the production code.
#   Proofs.Ladder     — ../proofs.json against this tree.
#   Proofs.Coverage   — Phase 203's stopping rule, and the only family here that is about what is
#                       NOT in this directory: every packable package is named by a model in
#                       modules.json or carries a declared exclusion in coverage-exclusions.json,
#                       every assumed row is accounted for by its class, and every differential is
#                       paired to a theorem. It runs LAST because it is the only one whose subject
#                       is the leg as a whole — the three above each say something about one
#                       artefact, and this one says whether the set of them is the claim the README
#                       makes. It prints the predicate line whatever the verdict.
$hostFilters = @(
    @{
        Filter  = 'Proofs.Vocabulary'
        Failure = 'the generated vocabulary (Proofs.Vocabulary) is RED — a committed F* model or proof script and its certification vocabulary disagree, or the target refused a construct'
    }
    @{
        Filter  = 'Proofs.Oracle'
        Failure = 'the oracle host (Proofs.Oracle) is RED — an extracted model and production disagree'
    }
    @{
        Filter  = 'Proofs.Ladder'
        Failure = 'the claims ladder (Proofs.Ladder) is RED — ../proofs.json and this tree disagree; the failing row and clause are named above'
    }
    @{
        Filter  = 'Proofs.Coverage'
        Failure = 'the coverage predicate (Proofs.Coverage) is RED — "exhaustively proved" is not met on this tree; each finding names the clause and the subject above, and the `proofs:` line says which verdict was computed'
    }
)

$legArgs = @{
    Modules         = $modules
    ProofOnly       = $proofOnly
    ProofsDir       = $PSScriptRoot
    HostProject     = 'tests/Fuaran.Core.Tests'
    HostProjectFile = 'tests/Fuaran.Core.Tests/Fuaran.Core.Tests.fsproj'
    HostFilters     = $hostFilters
    Runs            = $Runs
    # Phase 309 — TWIN EVALUATION (the kit's step 2c): every extracted model ends with a `twins`
    # list its own `assert_norm` evaluates, and the kit refuses one that declares none. The host
    # half — the extracted closures run against the extracted oracle — is the "twin evaluation"
    # cases of the Proofs.Oracle family above, and Proofs.Ladder holds that roster to this file's
    # extracted set.
    Twins           = $true
}
if ($Extract) { $legArgs.Extract = $true }
if ($SkipOracleHost) { $legArgs.SkipOracleHost = $true }
if ($Strict) { $legArgs.Strict = $true }
if ($CacheDir) { $legArgs.CacheDir = $CacheDir }

# ---- Phase 328 — the module-cone selector ----------------------------------------------------------
#
# WHY. One cold pass of the whole leg checks every registered model, and a phase that edits one
# model pays for all of them. A SHARED checked-module cache would make that cheap and is declined —
# Phase 164 made the cache per invocation precisely so that evidence nobody produced in this run
# cannot pass (see the README's "Cold cache" section and DECISIONS.md D74). The selector is the
# safe form of the same saving: it recomputes from the tree EVERY time, still verifies cold, and
# the only thing it reuses is the knowledge of what did not change.
#
# WHAT. `-Since <tree>` asks the host test project (`--proof-cone`, `ProofsLadderTests.fs`) for the
# cone: the registered modules whose model, committed oracle, `modules.json` entry (which is also
# its registration and its kind) or covered production sources (`modules.json`'s `packages`) changed
# between <tree> and the working tree, plus every module that references a changed model, however
# transitively — and EVERY module when a shared input of the leg moved (the prover pin, the kit's
# engine, this script's code, a `shared` module). It runs there, not here, so that the roster is
# read by `parseRoster` — the very function the `Proofs.Ladder` family reads it with.
# The kit is then handed the cone (IN) and the modules the cone references (DEP, checked ahead of
# the modules that need them, so every clock is the module's own) in the roster's order, with a
# copy of `modules.json` that drops the entries of the modules left OUT — so the kit's both-ways
# budget coverage still fires for everything it checks.
#
# THE EMPTY CONE is green only when `proofs/last-strict.json` records a green -Strict FULL run on
# an ancestor of HEAD and the cone against THAT tree is empty too; otherwise the leg says it has
# no strict baseline and exits 1. A green -Strict full run writes that record — unless the working
# tree differs from HEAD in anything a module reads, because then it verified bytes no commit
# holds. Commit the file it writes.
#
# WITH NEITHER SWITCH nothing below changes which modules the kit checks or which budget file it
# reads, so the full leg's verdict cannot move. `-PlanOnly` prints what the kit would be handed and
# stops, which is how the Proofs.Cone family holds that claim.
$repoRoot = Split-Path $PSScriptRoot -Parent
$hostProjectDir = Join-Path $repoRoot 'tests/Fuaran.Core.Tests'
$hostProjectPath = Join-Path $hostProjectDir 'Fuaran.Core.Tests.fsproj'
$coneWork = Join-Path $PSScriptRoot "obj/cone-$PID"
$selected = $null   # $null is the full leg
$coneBudget = $null

function Remove-ConeWork {
    if (Test-Path $coneWork) { Remove-Item $coneWork -Recurse -Force -ErrorAction SilentlyContinue }
}

function Stop-Leg([string] $message, [int] $code = 1) {
    Remove-ConeWork
    Write-Host "==== proofs: $message" -ForegroundColor Red
    exit $code
}

# Ask the host project. Its answer comes back as a JSON file, printed lines included, so that no
# console code page sits between the two processes. The build is incremental, and the host step
# builds the same project anyway. With -Soft a failure to answer returns $null instead of ending the
# leg: that is for the strict record, which must never turn a green leg red.
function Invoke-ConeTool([string[]] $ToolArgs, [switch] $Soft) {
    New-Item -ItemType Directory -Force $coneWork | Out-Null
    $answerPath = Join-Path $coneWork 'answer.json'
    if (Test-Path $answerPath) { Remove-Item $answerPath -Force }

    $global:LASTEXITCODE = 0
    & dotnet build $hostProjectPath --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) {
        $why = "the cone selector could not build $hostProjectPath (exit $LASTEXITCODE); it reads the roster with the ladder's own parser, so it runs from the test project"
        if ($Soft) { Write-Host "==== proofs: $why" -ForegroundColor Yellow; return $null }
        Stop-Leg $why $LASTEXITCODE
    }

    $global:LASTEXITCODE = 0
    & dotnet run --project $hostProjectDir --no-build -- --proof-cone @ToolArgs --out $answerPath | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $answerPath)) {
        $why = "the cone selector could not answer (exit $LASTEXITCODE); its reason is printed above"
        if ($Soft) { Write-Host "==== proofs: $why" -ForegroundColor Yellow; return $null }
        Stop-Leg $why 1
    }

    return (Get-Content $answerPath -Raw | ConvertFrom-Json)
}

if ($Since -or $Cone) {
    # Sweep the work directories of cone runs that were killed before they could remove their own,
    # by the kit's rule for its caches: only a directory whose process is gone.
    foreach ($stale in (Get-ChildItem (Join-Path $PSScriptRoot 'obj') -Directory -Filter 'cone-*' -ErrorAction SilentlyContinue)) {
        $stalePid = 0
        if (-not [int]::TryParse($stale.Name.Substring('cone-'.Length), [ref] $stalePid)) { continue }
        if ($stalePid -eq $PID -or (Get-Process -Id $stalePid -ErrorAction SilentlyContinue)) { continue }
        Remove-Item $stale.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Since -and $Cone) { Stop-Leg '-Since and -Modules each name a cone; pass one of them' }

# The cone tool's printed lines, coloured by verdict. -Since and -Modules print the same way.
function Write-ConeLines($lines) {
    foreach ($line in @($lines)) {
        $colour = if ($line.StartsWith('====')) { 'Cyan' }
        elseif ($line.Length -ge 8 -and $line.Substring(5, 3) -eq 'IN ') { 'Green' }
        elseif ($line.Length -ge 8 -and $line.Substring(5, 3) -eq 'DEP') { 'Cyan' }
        else { 'DarkGray' }
        Write-Host $line -ForegroundColor $colour
    }
}

if ($Cone) {
    # A HAND CONE (Phase 522): the named modules IN, and every module they reference handed to the kit
    # as a DEPENDENCY ahead of them, by the SAME closure -Since uses, computed by the same tool. Until
    # Phase 522 a hand cone was exactly what it named, and a named model whose shared modules were not
    # named failed at EXTRACT as an apparatus fault: nothing had checked the modules it opens.
    # Split as well as accept an array: through `pwsh -File`, `-Modules A,B` arrives as ONE string.
    $named = @($Cone | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })
    if ($named.Count -eq 0) { Stop-Leg '-Modules names no module' }
    $answer = Invoke-ConeTool @('--modules', ($named -join ','))
    if ($answer.verdict -ne 'verify') { Stop-Leg $answer.message 1 }
    Write-ConeLines $answer.lines
    $selected = @($answer.verify)
}
elseif ($Since) {
    $answer = Invoke-ConeTool @('--since', $Since)
    Write-ConeLines $answer.lines

    switch ($answer.verdict) {
        'verify' { $selected = @($answer.verify) }
        'empty-green' {
            Remove-ConeWork
            Write-Host "==== proofs: green -- $($answer.message)" -ForegroundColor Green
            exit 0
        }
        default { Stop-Leg $answer.message 1 }
    }
}

if ($null -ne $selected) {
    $coneBudget = Get-Content (Join-Path $PSScriptRoot 'modules.json') -Raw | ConvertFrom-Json
    $coneBudget.modules = @($coneBudget.modules | Where-Object { $selected -contains $_.module })
    $legArgs.Modules = $selected
    $legArgs.BudgetFile = Join-Path $coneWork 'modules.json'
}

if ($PlanOnly) {
    Write-Host "==== proofs: plan -- the kit checks $(@($legArgs.Modules).Count) module(s): $($legArgs.Modules -join ', ')"
    if ($null -ne $coneBudget) {
        Write-Host "==== proofs: plan -- budget file: proofs/modules.json without the entries of the modules left OUT ($(@($coneBudget.modules).Count) entries)"
    }
    else {
        Write-Host '==== proofs: plan -- budget file: the declared proofs/modules.json'
    }
    Remove-ConeWork
    exit 0
}

# Phase 522 — EVERY run asks the kit for its run verdict, into a file of this process's own (two runs
# in one worktree must not write one file), outside the cone's work directory. It becomes
# proofs/obj/last-run.json below, once this script has added what only it knows: the cone.
$verdictPath = Join-Path $PSScriptRoot "obj/run-verdict-$PID.json"
$lastRunPath = Join-Path $PSScriptRoot 'obj/last-run.json'
New-Item -ItemType Directory -Force (Split-Path $verdictPath -Parent) | Out-Null
if (Test-Path $verdictPath) { Remove-Item $verdictPath -Force }
$legArgs.SummaryFile = $verdictPath

# Add a member to the run verdict and write it back as proofs/obj/last-run.json. Never changes the
# leg's exit: a record this script cannot complete says so in yellow.
# A previous run's last-run.json is never completed in this run's name: when the kit wrote no record
# (a process killed outright writes none), the file is removed rather than left describing another run.
$script:verdictSaved = $false
function Save-RunVerdict([string] $name, $value) {
    try {
        $source = if (Test-Path $verdictPath) { $verdictPath } elseif ($script:verdictSaved) { $lastRunPath } else { $null }
        if ($null -eq $source) {
            if (Test-Path $lastRunPath) { Remove-Item $lastRunPath -Force }
            Write-Host "==== proofs: the kit wrote no run verdict to $verdictPath, so this run leaves no record" -ForegroundColor Yellow
            return
        }
        $record = Get-Content $source -Raw | ConvertFrom-Json -AsHashtable
        $record[$name] = $value
        [System.IO.File]::WriteAllText($lastRunPath, ($record | ConvertTo-Json -Depth 8) + "`n", [System.Text.UTF8Encoding]::new($false))
        $script:verdictSaved = $true
        if (Test-Path $verdictPath) { Remove-Item $verdictPath -Force }
    }
    catch { Write-Host "==== proofs: the run verdict could not be completed in ${lastRunPath}: $_" -ForegroundColor Yellow }
}

if ($null -ne $coneBudget) {
    New-Item -ItemType Directory -Force $coneWork | Out-Null
    $coneBudget | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $legArgs.BudgetFile -Encoding utf8NoBOM
}

# `&` and not `.`: a dot-sourced script's `exit` does NOT propagate to its caller, so a dot-source
# here would print the kit's red line and then return 0 — a green leg over a failed proof, which
# is the very class Phase 164 was about. Measured both ways before choosing.
# `&` is also the shape under which the kit's own `$LASTEXITCODE = 0` seed shadowed every exit code
# it read, so a failed host build and a refuted model both came back green (Phase 221): the choice
# of `&` was right, and what it exposed was a defect in the kit, now removed.
& (Join-Path $PSScriptRoot 'kit/check-proof-leg.ps1') @legArgs
$legExit = $LASTEXITCODE
Remove-ConeWork
# The cone: `null` is the full leg, otherwise the modules the kit was handed.
$coneValue = $null
if ($null -ne $selected) { $coneValue = [object[]]@($selected) }
Save-RunVerdict 'cone' $coneValue
if ($legExit -ne 0) { exit $legExit }

# Then the leg's own refusals, run the same way (Phase 221): a scratch leg over a failed host build,
# a host filter that cannot run and a refuted model must each exit non-zero, beside a green control.
# A few seconds, and after the leg so the prover is already installed. This is what lets the green
# above be cited as "every step was able to fail" rather than only as "no step said it failed".
& (Join-Path $PSScriptRoot 'kit/check-proof-leg.tests.ps1') -ProofsDir $PSScriptRoot
$refusalsExit = $LASTEXITCODE
# The refusals are part of what a green run stands on, so the record says whether they held.
Save-RunVerdict 'refusals' $(if ($refusalsExit -eq 0) { 'held' } else { "red (exit $refusalsExit)" })
if ($refusalsExit -ne 0 -or -not $Strict -or $null -ne $selected) { exit $refusalsExit }

# Phase 328 — a green -Strict FULL run records itself as the baseline an empty cone may lean on: the
# run's own verdict record (Phase 522), with the tree it verified. It never changes the verdict: the
# leg is green whatever the record says, and a record that could not be written says why in yellow.
$record = Invoke-ConeTool @('--record-strict', '--verdict', $lastRunPath) -Soft
Remove-ConeWork
if ($null -ne $record) {
    if ($record.recorded) { Write-Host "==== proofs: $($record.message)" -ForegroundColor Green }
    else { Write-Host "==== proofs: strict baseline $($record.message)" -ForegroundColor Yellow }
}
exit 0
