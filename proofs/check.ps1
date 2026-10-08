#Requires -Version 7.0
# fuaran-core — the proof leg (Phases 131, 135, 136, 148, 149, 150, 151, 164, 155, 173, 176, 177, 187 and 157).
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
#
# Phase 328 adds the MODULE-CONE SELECTOR — three flags, and with none of them the leg is exactly
# the leg it was (the kit is handed the same arguments, so its verdict cannot move):
#   -Since <tree>    verify, cold, only the models whose inputs changed between <tree> and the
#                    working tree — the cone — and print every registered module IN, DEP or OUT
#                    with the reason. An EMPTY cone is green only over a recorded strict run.
#   -Modules <list>  name a cone by hand (`-Modules TreeOps,Skeleton`): exactly those modules.
#   -PlanOnly        print what the kit would be handed, and stop before the prover runs.
# And a green -Strict FULL run now records itself in proofs/last-strict.json (commit the file),
# which is the baseline an empty cone leans on. Since Phase 399 the record also carries the machine
# the run was on and each run's contention factor, and .github/workflows/proofs-strict.yml makes
# that run weekly and hands the refreshed record over as an artefact. The selector section below says how the cone is
# computed and why each input puts a module in it.
[CmdletBinding()]
param(
    [switch] $Extract,
    [switch] $SkipOracleHost,
    [switch] $Strict,
    [switch] $NoFloor,
    [string] $CacheDir,
    [int]    $Runs = 1,
    [string] $Since,
    # Bound as -Modules; named $Cone because PowerShell variables ignore case, and `$modules` is the
    # roster literal below, which would overwrite a parameter of that name.
    [Alias('Modules')][string[]] $Cone,
    [switch] $PlanOnly
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
#   Utf8       — Phase 306, the UTF-8 ENCODER the digests hash through: `Hash.utf8Bytes` clause for
#                clause, with `Hash.tryUtf8Bytes` beside it, and the encoding proved INJECTIVE on
#                well-formed UTF-16 — the step that carries `WireCanon`'s canonical form from
#                characters to the bytes a hash is taken over. Opens nothing; it precedes
#                `WireCanon`, which reads it for section 16. CHECKED AND NOT EXTRACTED (see
#                `$proofOnly`): its units are integers, which do not survive this extraction.
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
#   WireColumn — Phase 306, the COLUMNAR CODEC: `Table.validate`, `ColumnCodec.encodeJson` /
#                `decodeJson` / `tryEncode` and the decimal canonicaliser, clause for clause at the
#                `JVal`, with the codec's image proved to lie inside what `validate` accepts and
#                the round trip proved up to a NORMAL FORM (columns in schema order, an `Int`
#                widened into its column's type) — the literal round trip refuted three ways
#                beside it. `Fuaran.Core.Column`'s first model: its coverage exclusion is gone.
#                Opens `WireCanon` for the value type the renderer is proved about, so it follows
#                it; named with the prefix for the reason `WireVersioning` is.
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
#   Capability — Phase 177, the FUNCTION SEAM every AI edit crosses: `Fuaran.Core.Function`'s
#                effect lattice, value spaces, `signature` / `apply` / `curry` / `compose` /
#                `auditEffect` over an abstract witness, and `Capability.validateArgs` / `invoke`
#                with `CapabilityRegistry.register` / `enumerate` / `dispatch`, clause for clause — with
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
#   Query      — Phase 187, the DATA-ACQUISITION SEAM beside the function seam:
#                `Fuaran.Core.Query`'s `validateParams` / `invoke` / `invocationKey` with
#                `QueryRegistry.register` / `enumerate` / `dispatch`, clause for clause over an
#                abstract resolver — with default-deny dispatch, validation before resolution,
#                enumeration equal to the registry and the capture key's determinism proved, and
#                two findings read off the model. Self-contained: it opens nothing, so its
#                position is free.
#   Arbitrate  — Phase 157, PROPOSAL ARBITRATION: `Arbitration.arbitrate` clause for clause — the
#                stable pinned sort, the `canApplyAll` dry run, the greedy independence pass and
#                the re-citation — with the accepted set proved pairwise independent, every
#                rejection proved justified (maximal, NOT maximum), and the whole result proved
#                invariant under arrival order for id-distinct input, with the witness that the
#                hypothesis is needed. Opens DagFold and TreeOps, so it follows both.
#   DecimalText — Phase 279, the EXACT DECIMAL's text arithmetic: `DecimalText.parts` / `render` /
#                `tryCanonical` / `aligned` / `addMagnitudes` / `subMagnitudes` / `compare` / `add`
#                clause for clause over a text read as its symbols, with one number proved to have
#                one canonical text (D72 K3), the order proved total and equal to the numeric order,
#                the sum proved to denote the sum and to be commutative, associative and canonical
#                with zero its identity, and the accepted set proved to be exactly the grammar (D72
#                K4). Self-contained: it opens nothing, so its position is free. Its integers extract
#                to `Prims.int` / `Prims.nat` (`bigint`), which the oracle host bridges at the edge.
#
#   Normalize  — Phase 305, `Ops.normalize` clause for clause — the six-row collapse table, the
#                push loop and the left fold with its output stack — with the defining law proved
#                (an applyable script at a well-formed tree reaches the same tree normalised),
#                idempotence proved through a stability invariant on the output, and the script
#                proved never to lengthen. Opens DagFold, TreeOps and Preservation and cites
#                TreeDiff's `tree_ext`, so it follows all four.
#   VocabularyVectors — Phase 303, the interpreter's vectors as NORMALISER facts over the three
#                generated models: for each vector the three-way differential draws (drawn and
#                adversarial), `assert_norm` that the model's `enc_node` maps the interpreter's
#                decoded value to the jval its bytes parse to. Emitted by the same target from the
#                test project (`--emit-fstar`). Opens Vocabulary, DocVocabulary and ScoreVocabulary,
#                so it follows them; checked but not extracted, like them.
#   PropagationOps — Phase 308, `Propagation.touchedBy` and the diff-based `changedForOp` clause
#                for clause over TreeOps' tree and `apply`, with the change set proved COMPLETE
#                (`changed_for_op_complete`: every survivor whose content, child ids or reads moved,
#                and every reader of a removed id, Batch included at any depth). Opens DagFold and
#                TreeOps and cites Propagation's closure, so it follows all three. Checked but not
#                extracted — see the Phase 308 paragraph below the exemption list.
#   TreeFrame  — Phase 362, the frame of the tree algebra: an applied op writes only where its
#                footprint names, or at the source parent or destroyed subtree of one of its
#                unknown-parent writes (`apply_frame`, `batch_frame`), and `WriteGate.targetsOf`,
#                `decide` and `applyGated` clause for clause, with the targets proved to cover the
#                writes and a gated apply proved to leave every locked subtree as it was. Opens
#                DagFold, TreeOps and Preservation and cites TreeDiff's `tree_ext` and
#                `apply_all_app`, so it follows all four. Checked but not extracted — see the Phase
#                362 paragraph below the exemption list.
#
#   ColumnOps and Pipeline — the compute strand's two models (Phases 176 and 154/234) — left this
#                repository with `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` in Phase 258
#                (DECISIONS.md D66); they are checked by the compute repository's own proof leg.
#
# Adding a model is adding its name to this list AND a budget entry to modules.json: nothing else
# is per-module, here or in the kit. The line below is also READ AS TEXT by the `Proofs.Ladder`
# family (`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`, `parseModules`), which matches
# `^\$modules\s*=\s*@\(...\)` against this file — so it stays one literal line in this file, which
# is where a reader looks for it anyway.
$modules = @('DagFold', 'WireDecode', 'TreeOps', 'Skeleton', 'Chain', 'JsonParse', 'Preservation', 'TreeDiff', 'Limits', 'Utf8', 'WireCanon', 'WireVersioning', 'WireColumn', 'Vocabulary', 'VocabularyProofs', 'DocVocabulary', 'DocVocabularyProofs', 'ScoreVocabulary', 'ScoreVocabularyProofs', 'Capability', 'Propagation', 'Query', 'Arbitrate', 'DecimalText', 'Normalize', 'VocabularyVectors', 'PropagationOps', 'TreeFrame')

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
# Phase 306 adds `Utf8` to the list, for a different reason and it is said separately so the two
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
$proofOnly = @('Vocabulary', 'VocabularyProofs', 'DocVocabulary', 'DocVocabularyProofs', 'ScoreVocabulary', 'ScoreVocabularyProofs', 'Utf8', 'VocabularyVectors', 'PropagationOps', 'TreeFrame')

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
if ($NoFloor) { $legArgs.NoFloor = $true }
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
# cone: the registered modules whose model, committed oracle, `modules.json` entry, registration or
# covered production sources (`modules.json`'s `packages`) changed between <tree> and the working
# tree, plus every module that references a changed model, however transitively — and EVERY module
# when a shared input of the leg moved (the prover pin, the kit's engine, this script's code). It
# runs there, not here, so that the roster is read by `parseModules` — the very function the
# `Proofs.Ladder` family reads it with — and the two can never disagree about which models exist.
# The kit is then handed the cone (IN) and the modules the cone references (DEP, checked ahead of
# the modules that need them, so every clock is the module's own) in the roster's order, with a
# copy of `modules.json` that drops only the entries of registered modules left OUT — so the kit's
# both-ways budget coverage still fires for everything it checks and for any orphan entry.
#
# THE EMPTY CONE is green only when `proofs/last-strict.json` records a green -Strict FULL run on
# an ancestor of HEAD and the cone against THAT tree is empty too; otherwise the leg says it has
# no strict baseline and exits 1. A green -Strict full run writes that record — unless the working
# tree differs from HEAD in anything a module reads, because then it verified bytes no commit
# holds. Commit the file it writes.
#
# WITH NEITHER SWITCH nothing below changes `$legArgs`: the kit is handed exactly the arguments it
# was handed before this phase, so the full leg's verdict cannot move. `-PlanOnly` prints what the
# kit would be handed and stops, which is how the Proofs.Cone family holds that claim.
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

$width = ($modules | Measure-Object -Property Length -Maximum).Maximum

if ($Cone) {
    # Split as well as accept an array: through `pwsh -File`, `-Modules A,B` arrives as ONE string.
    $named = @($Cone | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })
    if ($named.Count -eq 0) { Stop-Leg '-Modules names no module' }
    $unknown = @($named | Where-Object { $modules -notcontains $_ })
    if ($unknown.Count -gt 0) {
        Stop-Leg "-Modules names $($unknown -join ', '), which `$modules does not register; the registered modules are $($modules -join ', ')"
    }
    $selected = @($modules | Where-Object { $named -contains $_ })

    Write-Host ("==== proofs: cone named by hand (-Modules) -- $($selected.Count) of $($modules.Count) registered module(s) IN, " +
        "0 checked as a dependency, $($modules.Count - $selected.Count) OUT") -ForegroundColor Cyan
    foreach ($module in $modules) {
        if ($selected -contains $module) { Write-Host ('     IN   {0}  named by -Modules' -f $module.PadRight($width)) -ForegroundColor Green }
        else { Write-Host ('     OUT  {0}  not named' -f $module.PadRight($width)) -ForegroundColor DarkGray }
    }
    Write-Host ('     A hand cone is exactly what it names. A module a named one references is still checked by the prover, ' +
        'inside the named module''s clock, so a cost line here is not a cold measurement of that module alone; -Since lists them.') -ForegroundColor DarkGray
}
elseif ($Since) {
    $answer = Invoke-ConeTool @('--since', $Since)
    foreach ($line in @($answer.lines)) {
        $colour = if ($line.StartsWith('====')) { 'Cyan' }
        elseif ($line.Length -ge 8 -and $line.Substring(5, 3) -eq 'IN ') { 'Green' }
        elseif ($line.Length -ge 8 -and $line.Substring(5, 3) -eq 'DEP') { 'Cyan' }
        else { 'DarkGray' }
        Write-Host $line -ForegroundColor $colour
    }

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
    $coneBudget.modules = @($coneBudget.modules | Where-Object { $selected -contains $_.module -or $modules -notcontains $_.module })
    $legArgs.Modules = $selected
    $legArgs.BudgetFile = Join-Path $coneWork 'modules.json'
}

if ($PlanOnly) {
    Write-Host "==== proofs: plan -- the kit checks $(@($legArgs.Modules).Count) module(s): $($legArgs.Modules -join ', ')"
    if ($null -ne $coneBudget) {
        Write-Host "==== proofs: plan -- budget file: proofs/modules.json without the entries of registered modules left OUT ($(@($coneBudget.modules).Count) entries)"
    }
    else {
        Write-Host '==== proofs: plan -- budget file: the declared proofs/modules.json'
    }
    Remove-ConeWork
    exit 0
}

# Phase 399 — a -Strict FULL run asks the kit for its run facts (the machine, each run's contention
# factor), so the baseline it records says what kind of machine and what kind of afternoon stand
# behind it. Outside the cone's work directory, which is removed before the record is written.
$runFactsPath = $null
if ($Strict -and $null -eq $selected) {
    $runFactsPath = Join-Path $PSScriptRoot "obj/run-facts-$PID.json"
    New-Item -ItemType Directory -Force (Split-Path $runFactsPath -Parent) | Out-Null
    if (Test-Path $runFactsPath) { Remove-Item $runFactsPath -Force }
    $legArgs.SummaryFile = $runFactsPath
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
if ($legExit -ne 0) { exit $legExit }

# Then the leg's own refusals, run the same way (Phase 221): a scratch leg over a failed host build,
# a host filter that cannot run and a refuted model must each exit non-zero, beside a green control.
# A few seconds, and after the leg so the prover is already installed. This is what lets the green
# above be cited as "every step was able to fail" rather than only as "no step said it failed".
& (Join-Path $PSScriptRoot 'kit/check-proof-leg.tests.ps1') -ProofsDir $PSScriptRoot
$refusalsExit = $LASTEXITCODE
if ($refusalsExit -ne 0 -or -not $Strict -or $null -ne $selected) { exit $refusalsExit }

# Phase 328 — a green -Strict FULL run records itself as the baseline an empty cone may lean on.
# It never changes the verdict: the leg is green whatever the record says, and a record that could
# not be written says why in yellow.
$recordArgs = @('--record-strict', "$Runs")
if ($runFactsPath -and (Test-Path $runFactsPath)) { $recordArgs += @('--run-facts', $runFactsPath) }
$record = Invoke-ConeTool $recordArgs -Soft
Remove-ConeWork
if ($runFactsPath -and (Test-Path $runFactsPath)) { Remove-Item $runFactsPath -Force }
if ($null -ne $record) {
    if ($record.recorded) { Write-Host "==== proofs: $($record.message)" -ForegroundColor Green }
    else { Write-Host "==== proofs: strict baseline $($record.message)" -ForegroundColor Yellow }
}
exit 0
