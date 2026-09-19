#Requires -Version 7.0
# fuaran-core — the proof leg (Phases 131, 135, 136, 148, 149, 150, 151, 164, 155, 173, 176 and 177).
#
# THE ENGINE IS `kit/check-proof-leg.ps1` (Phase 155) and this file is the caller: it declares
# what THIS repository has — the models, which of them are checked but not extracted, where the
# oracle host lives, and which Expecto families the host step runs — and the kit runs the leg.
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
#   WireVersioning — Phase 151, WIRE_FORMAT §15.4's evolution-policy table as a theorem about the
#                IDL diff: `Versioning.classify` / `bump` / `negotiate` / `decodeTolerant` /
#                `reencode` clause for clause, with the classifier proved SOUND (an `Additive`
#                verdict IS the subset claim), an additive step proved unobservable to a document
#                that predates it, must-ignore-but-preserve proved AT THE BYTES, and the
#                transport-only `Unknown` proved un-constructible from an encoder. It `open`s
#                `WireCanon` — the byte claim is stated against Phase 149's renderer rather than
#                a second one — so it follows it.
#   Vocabulary — Phase 150, re-sourced by Phase 173: the GENERATED model of the engine's own
#                REFERENCE vocabulary (`tests/Fuaran.Core.Tests/ReferenceIdl.fs`) — its types, its
#                discriminated encoder and its tag-dispatch decoder — emitted by
#                `Fuaran.Core.Idl.Codegen`'s F* target. Opens WireDecode, so it follows it.
#   VocabularyProofs — Phase 173: the ROUND TRIP over `Vocabulary`, emitted by the same target from
#                the same walk (`dec_node (enc_node x) == Ok x`, plus totality's exclusivity). Opens
#                `Vocabulary`, so it follows it. See the note below the list for why this was not
#                committed by Phase 150 and is now. Since Phase 182 the script is one lemma per
#                constructor over a presence split LINEAR in the conditional members, checked at
#                the leg's own rlimit.
#   DocVocabulary / DocVocabularyProofs — Phase 173: the same pair over the vendored second-domain
#                sample (`SecondDomainSpike.fs`), which is on the DECLARED non-default wire shape —
#                bare-string discriminator, flat node envelope, declaration key order — that the
#                reference vocabulary does not reach. Each opens its predecessor.
#   ScoreVocabulary / ScoreVocabularyProofs — Phase 173: the same pair over the vendored
#                third-domain sample (`ScoreDomainSpike.fs`): records and omit-at-default at scale,
#                on the same non-default shape. Each opens its predecessor.
#   ColumnOps  — Phase 176, the COMPUTE strand's op algebra: `ColumnOps.apply` / `canApply` /
#                `invert` / `Diff.toOps` over a table with a validity mask, clause for clause,
#                with the five Preservation clauses proved for columns — totality with its
#                rejection characterisation, all-or-nothing rejection, the dry run's agreement,
#                well-formedness preserved, `invert`'s round trip with its partial cases
#                characterised — and `Diff.toOps`'s script proved to reconstruct its target.
#                Self-contained: it opens nothing, so its position is free.
#   Capability — Phase 177, the FUNCTION SEAM every AI edit crosses: `Fuaran.Core.Function`'s
#                effect lattice, value spaces, `signature` / `apply` / `curry` / `compose` /
#                `auditEffect` over an abstract witness, and `Capability.validateArgs` / `invoke`
#                with `Registry.register` / `enumerate` / `dispatch`, clause for clause — with
#                default-deny dispatch, validation before invocation, enumeration equal to the
#                registry and the three function laws proved. Self-contained: it opens nothing,
#                so its position is free.
#   Propagation — Phase 186, the INCREMENTAL PROMISE of the compute strand:
#                `Fuaran.Core.Propagation`'s `dependents`, `dirtyFromChangedIds` (with its
#                frontier loop, total here by a checked measure), `staleSet`, and the driver —
#                `walk` / `eval` / `evalFrom` over an abstract node evaluator, clause for clause,
#                with `sort`'s result a parameter — and the dirty set proved sound and least,
#                `evalFrom` proved equal to `eval` under the stated evaluator contract, reuse
#                proved minimal and an unknown change proved refused. Self-contained: it opens
#                nothing, so its position is free.
#
# Adding a model is adding its name to this list AND a budget entry to modules.json: nothing else
# is per-module, here or in the kit. The line below is also READ AS TEXT by the `Proofs.Ladder`
# family (`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`, `parseModules`), which matches
# `^\$modules\s*=\s*@\(...\)` against this file — so it stays one literal line in this file, which
# is where a reader looks for it anyway.
$modules = @('DagFold', 'WireDecode', 'TreeOps', 'Skeleton', 'Chain', 'JsonParse', 'Preservation', 'TreeDiff', 'Limits', 'WireCanon', 'WireVersioning', 'Vocabulary', 'VocabularyProofs', 'DocVocabulary', 'DocVocabularyProofs', 'ScoreVocabulary', 'ScoreVocabularyProofs', 'ColumnOps', 'Capability', 'Propagation')

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
$proofOnly = @('Vocabulary', 'VocabularyProofs', 'DocVocabulary', 'DocVocabularyProofs', 'ScoreVocabulary', 'ScoreVocabularyProofs')

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
