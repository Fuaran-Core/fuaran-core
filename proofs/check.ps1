#Requires -Version 7.0
# fuaran-core — the proof leg (Phases 131, 135, 136, 148, 164 and 155).
#
# THE ENGINE IS `kit/check-proof-leg.ps1` (Phase 155) and this file is the caller: it declares
# what THIS repository has — the models, where the oracle host lives, which Expecto families the
# host step runs — and the kit runs the leg. Read the kit script's header for what the three steps
# are and why each is shaped the way it is; read `kit/README.md` for what the kit is and how
# another repository adopts it. Nothing about the mechanism lives here any more, and nothing about
# this repository lives in the kit.
#
# The flags are unchanged and are forwarded verbatim:
#   -Runs N          N cold-cache verifications of every model (CI asks for 3)
#   -Extract         rewrite the committed oracle/*.fs from a fresh extraction, then commit them
#   -SkipOracleHost  leave the two Expecto families to ./verify.ps1, which runs the whole suite
#   -Strict          promote every cost finding to a red leg
#   -NoFloor         do not enforce the per-module time floors declared in modules.json
#   -CacheDir <dir>  put the checked-module cache somewhere you name
[CmdletBinding()]
param(
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [switch] $NoFloor,
    [string] $CacheDir,
    [int]    $Runs = 1
)

$ErrorActionPreference = 'Stop'

# The models, in the order they were cut. Each is proofs/<name>.fst with its committed extraction
# at proofs/oracle/<name>.fs; they share nothing but oracle/Prims.fs.
#   DagFold    — Phase 131, the N-lane DAG fold, with fold confluence proved.
#   WireDecode — Phase 135, the wire decode combinators, with decoder totality proved.
#   TreeOps    — Phase 133, the skeleton-op tree algebra, with the fold theorem's domain
#                hypothesis proved for it. Opens DagFold, so it follows it here.
#   Skeleton   — Phase 133, the composite: DagFold's fold theorem instantiated at TreeOps, with
#                no domain hypothesis left. Opens both, so it follows both.
#   Chain      — Phase 136, the two integrity walkers, with tamper detection proved under a
#                named injective-hash premise. Opens nothing, so its position is free.
#   JsonParse  — Phase 146, the recursive-descent JSON parser, with the depth bound, the int53
#                token guard and the exhaustiveness of the error classification proved. Opens
#                nothing, so its position is free; it is the boundary WireDecode named.
#   Preservation — Phase 138, the apply engine: totality with its rejection characterisation,
#                all-or-nothing rejection, id uniqueness preserved by every accepted operation,
#                canApply/apply agreement, and invert's round trip. Opens TreeOps, so it follows it.
#   TreeDiff   — Phase 141, the apply engine's companion in the other direction: `Diff.toOps`'s
#                two refusals characterised exactly, the four-pass emission order, what each pass
#                guarantees about the block it emits, and the container-aware mirror's pre-emptive
#                refusal. Opens TreeOps (and through it DagFold), so it follows both.
#
# Adding a model is adding its name to this list AND a budget entry to modules.json: nothing else
# is per-module, here or in the kit. The line below is also READ AS TEXT by the `Proofs.Ladder`
# family (`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`, `parseModules`), which matches
# `^\$modules\s*=\s*@\(...\)` against this file — so it stays one literal line in this file, which
# is where a reader looks for it anyway.
$modules = @('DagFold', 'WireDecode', 'TreeOps', 'Skeleton', 'Chain', 'JsonParse', 'Preservation', 'TreeDiff')

# The host step. Two invocations rather than one prefix filter, so the two failures read as what
# they are: a model and production disagreeing, versus the ladder and the tree disagreeing.
$hostFilters = @(
    @{
        Filter  = 'Proofs.Oracle'
        Failure = 'the oracle host (Proofs.Oracle) is RED — an extracted model and production disagree'
    }
    @{
        Filter  = 'Proofs.Ladder'
        Failure = 'the claims ladder (Proofs.Ladder) is RED — ../proofs.json and this tree disagree; the failing row and clause are named above'
    }
)

$legArgs = @{
    Modules         = $modules
    ProofsDir       = $PSScriptRoot
    HostProject     = 'tests/Fuaran.Core.Tests'
    HostProjectFile = 'tests/Fuaran.Core.Tests/Fuaran.Core.Tests.fsproj'
    HostFilters     = $hostFilters
    Runs            = $Runs
}
if ($Extract) { $legArgs.Extract = $true }
if ($SkipOracleHost) { $legArgs.SkipOracleHost = $true }
if ($Strict) { $legArgs.Strict = $true }
if ($NoFloor) { $legArgs.NoFloor = $true }
if ($CacheDir) { $legArgs.CacheDir = $CacheDir }

# `&` and not `.`: a dot-sourced script's `exit` does NOT propagate to its caller, so a dot-source
# here would print the kit's red line and then return 0 — a green leg over a failed proof, which
# is the very class Phase 164 was about. Measured both ways before choosing.
& (Join-Path $PSScriptRoot 'kit/check-proof-leg.ps1') @legArgs
exit $LASTEXITCODE
