#Requires -Version 7.0
# fuaran-core — the proof leg (Phases 131, 135, 136, 148, 149, 150, 164 and 155).
#
# THE ENGINE IS `kit/check-proof-leg.ps1` (Phase 155) and this file is the caller: it declares
# what THIS repository has — the models, which of them are checked but not extracted, where the
# oracle host lives, and which Expecto families the host step runs — and the kit runs the leg.
# Read the kit script's header for what the three steps are and why each is shaped the way it is;
# read `kit/README.md` for what the kit is and how another repository adopts it. Nothing about the
# mechanism lives here any more, and nothing about this repository lives in the kit.
#
# Step 2b (Phase 150) is this repository's own and sits in the host step below rather than in the
# engine: the one GENERATED model is held to a fresh generation from the pinned `idl.json` by the
# Proofs.Vocabulary family — the same discipline as the engine's extraction diff, one level
# further up. Step 2 says the oracle is the model; this says the model is the vocabulary the
# specification declares.
#
# The flags are unchanged and are forwarded verbatim:
#   -Runs N          N cold-cache verifications of every model (CI asks for 3)
#   -Extract         rewrite the committed oracle/*.fs from a fresh extraction, then commit them
#   -SkipOracleHost  leave the Expecto families to ./verify.ps1, which runs the whole suite
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
#   Limits     — Phase 149, the WIRE_FORMAT section-21 resource limits as named premises and
#                nothing else: eight constants with their captions, and the two relations the
#                specification's own argument uses. It models no enforcement and opens nothing;
#                it is here because `WireCanon` imports it, which is why it precedes it.
#   WireCanon  — Phase 149, the CANONICAL ENCODER — `Canon.escape`, `Canon.canonicalFloat` and
#                `Canon.render` clause for clause, with a reader for exactly the grammar they
#                emit, and the canonical form proved in both directions: equal bytes imply equal
#                normal forms, equal normal forms imply equal bytes. It `open`s Limits, so it
#                follows it.
#   Vocabulary — Phase 150, and the only GENERATED model here: the wire-format IDL's own
#                vocabulary — its types, its discriminated encoder and its tag-dispatch decoder —
#                emitted from `idl.json` by `Fuaran.Core.Idl.Codegen`'s F* target. Opens
#                WireDecode, so it follows it. See the note below the list for the theorems that
#                the same target emits and that are NOT committed beside it.
#
# Adding a model is adding its name to this list AND a budget entry to modules.json: nothing else
# is per-module, here or in the kit. The line below is also READ AS TEXT by the `Proofs.Ladder`
# family (`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`, `parseModules`), which matches
# `^\$modules\s*=\s*@\(...\)` against this file — so it stays one literal line in this file, which
# is where a reader looks for it anyway.
$modules = @('DagFold', 'WireDecode', 'TreeOps', 'Skeleton', 'Chain', 'JsonParse', 'Preservation', 'TreeDiff', 'Limits', 'WireCanon', 'Vocabulary')

# Phase 150 — why there is a generated MODEL here and no generated THEOREMS beside it (yet).
#
# `Fuaran.Core.Idl.Codegen`'s F* target emits both: `FStarTarget.vocabularyModule` for the model
# below, and `FStarTarget.proofsModule` for the round trip over it. The emitted proof script
# DISCHARGES on the pinned prover for a small vocabulary — it was checked green at one kind and at
# eight — and at the twenty this corpus's proof vocabulary selects it does not: the widest kind's
# arm (`FileUpload`, eleven members, five of them conditional) is not proved even at
# `--z3rlimit 200`, because a decoder reading eleven members off an object with five conditional
# cells puts thirty-two object shapes into one query. So no theorems module is committed, rather
# than one committed that does not verify.
#
# The cause is understood and named in `proofs/README.md`'s theorem 1 section, along with the three
# remedies already measured, so that whoever takes it does not start from scratch. What IS here is
# the model, checked below like every other module — and the totality it carries is not nothing:
# every generated decoder is `Tot` on an arbitrary `jval`, which F* admits only after proving it.

# Phase 150 — the model that is CHECKED but not EXTRACTED, and why an exemption exists at all.
#
# An oracle is here so the Expecto differential can run the extracted model beside the production
# code over the same inputs. `Vocabulary` has no production code on this side to run beside: it
# models the vocabulary an `idl.json` DECLARES, and the decoder it models is one a GENERATOR emits
# into a consuming host rather than one this repository ships. Extracting it anyway would commit
# ~400 KB of generated F# that nothing compiles, calls or compares — which is what an oracle is
# supposed to be the opposite of. (The extractor also emits its mutual TYPE group with the `and`
# indented one space, which F# 10's parser rejects outright even under the oracle project's
# `--strict-indentation-`; that is a real finding about the F# backend — this is the first model
# here with a mutual type group — and it is NOT the reason for this exemption. An oracle nothing
# runs would not be worth committing even if it compiled.)
#
# The exemption is NARROW and it is not a hole in the discipline: what step 2 buys for the other
# models — "the artefact is the model, byte for byte" — this one gets from the GENERATION diff in
# the host step instead, one level further up, against the `idl.json` it is generated from.
$proofOnly = @('Vocabulary')

# The host step, in this order. Separate invocations rather than one prefix filter, so each failure
# reads as what it is rather than as one red suite.
#   Proofs.Vocabulary — Phase 150's GENERATION diff, beside the engine's extraction diff and for
#                       the same reason one step further up: it holds the committed
#                       `Vocabulary.fst` to a fresh generation from the pinned `idl.json`, so the
#                       vocabulary the model is about is the vocabulary the specification declares.
#                       An IDL that moves without a regeneration is VOCABULARY DRIFT, and this is
#                       where it is named. It runs here rather than ahead of the prover because
#                       the generator is F# and the host step is the first thing with a built test
#                       project to hand — and because the leg fails either way: a stale model still
#                       verifies, and then this step reports what it is stale against.
#   Proofs.Oracle     — the differential: each extracted model beside the production code.
#   Proofs.Ladder     — ../proofs.json against this tree.
$hostFilters = @(
    @{
        Filter  = 'Proofs.Vocabulary'
        Failure = 'the generated vocabulary (Proofs.Vocabulary) is RED — the committed F* model and the pinned idl.json disagree, or the target refused a construct'
    }
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
    ProofOnly       = $proofOnly
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
