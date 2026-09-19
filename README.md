# Fuaran.Core

[![CI](https://github.com/Fuaran-Core/fuaran-core/actions/workflows/ci.yml/badge.svg)](https://github.com/Fuaran-Core/fuaran-core/actions/workflows/ci.yml) [![NuGet](https://img.shields.io/nuget/v/Fuaran.Core.Tree.svg)](https://www.nuget.org/packages/Fuaran.Core.Tree) [![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

The shared cross-domain substrate for the Fuaran family — the genericity-extracted
spine that the UI, Calc, Documents, CAD, Office (and future) domain tiers consume as a
peer dependency, so each domain stops re-implementing the same op-stream / op-algebra /
tree / wire / validator / artifact-function machinery five times over.

**Apache-2.0. FSharp.Core + Fable.Core only** (Fable.Core is a compile-time dependency for
the dual .NET/Fable pipeline — no runtime behaviour rides on it). No domain dependency, no
native dependency, no host dependency. Every package is a library of **generic functions
over domain-witness records** — the core owns **no base node type**. Each domain's closed
`NodeKind` DU stays sovereign and exhaustively matched — closed unions + exhaustive total
matching are the load-bearing F# constraint the whole pattern rests on.

This repo is the realisation of the rule-of-three extraction: the substrate is extracted
only now, against five shipped artifact-witness spines (UI, Calc, Documents, CAD, Office),
with the string-vs-Guid identity axis resolved as a witness parameter rather than guessed.

## Packages

| Package | What it owns | Generic over |
|---|---|---|
| **`Fuaran.Core.Tree`** | addressing, preorder walk, parent/path lookup, structural update, content-hash, the `fold`/`ancestors`/`descendants`/`siblings`/`depth`/`subtree` combinators | `'Node` + `'Id` witnesses |
| **`Fuaran.Core.Ops`** | the skeleton-five structural ops + the recoverable error-envelope (the AI-feedback protocol), dry-run `canApply`, op `invert` (undo/redo), structural `Diff.toOps`, and `Arbitration.arbitrate` — which subset of N op-script proposals can land together against one base tree (batch `canApply` + greedy `footprint` independence, a deterministic total partition with typed rejections) | node/id witness |
| **`Fuaran.Core.OpStream`** | append-only hash-chained stream, `verifyChain`, `replay`, **portable** JSONL encode + decode, snapshot/`compact`/`replayFrom` (bounded replay), determinism capture/replay (`captureEffect`/`replayEffect`/`verifyCaptures` — exact replay of clock/random/network effects) | `(apply, encode, decode)` witness |
| **`Fuaran.Core.OpStream.Dag`** | content-addressed branching/merging op-DAG, `verifyDag`, deterministic `replayTo` a head | the same stream witness |
| **`Fuaran.Core.Wire`** | the `"kind"`-tag/camelCase envelope, Fable-clean encode + **portable** decode combinators, corpus tooling | a domain codec |
| **`Fuaran.Core.Function`** | the artifact-function protocol: `signature`/`apply`/`curry`/`compose` under the three laws, `auditEffect`, `toSchema`; + the invocable `Capability` seam (typed registry + enumerate + default-deny dispatch, arg-validated invocation, Phase-27 replay keying); + the `Deferred<'T>` async-result envelope, the signature-typed `FunctionRegistry` (`findBySignature`), and the serializable `CapabilityPipeline` (typed capability-DAG) | artifact witness |
| **`Fuaran.Core.Column`** | the relational/columnar data strand: a typed, null-aware (validity-mask) columnar model over the fixed Arrow scalar set (`int`/`float`/`bool`/`string`/`date`/`timestamp`) + the canonical column-oriented wire codec (six-code decode envelope, `Wire`-canonical floats); + `Schema.diff`/compatibility/`fingerprint` and the public `Column.aggregate` surface | a self-contained data strand (no witness) |
| **`Fuaran.Core.DataFrame`** | the declarative-compute layer over `Column`: a serializable `Transform`/`ColExpr` algebra (full v1 verb set — filter/project/derive/groupBy/join/window/pivot/unpivot/sort/distinct/limit/union), a pure reference evaluator with pinned null/coercion/order/float semantics, a canonical wire codec, and the incremental `evalFrom` (change-relevance reuse, byte-identical to a full eval) | the columnar strand |
| **`Fuaran.Core.Column.Ops`** | the columnar op-algebra bridging the data + op-stream strands: a `ColumnOp` DU (SetCell / SetColumn / InsertColumn / RemoveColumn / AppendRows / ApplyTransform) with total `apply`/`canApply`, partial `invert`, a structural `Diff`, a wire codec, and a `Fuaran.Core.OpStream` `StreamWitness` — table edits as an append-only, hash-chained, replayable stream | `Column` + `DataFrame` + `OpStream` |
| **`Fuaran.Core.Query`** | the declarative, cross-domain data-acquisition seam — the data-acquisition *sibling* to `Capability`: a serializable, typed `Query` declaration (typed params + result `Schema` + `EffectClass` + `DataSource`) producing a `Table`, with a default-deny registry (enumerate + dispatch), param-type validation, Phase-27 capture keying (`invocationKey`), and a canonical wire codec. The host supplies the resolver (witness pattern), answering in the `Deferred` envelope — so a dispatch has exactly three outcomes: settled, pending, or refused with a typed `QueryError` | `Column` + `Function` |
| **`Fuaran.Core.Validator`** | the rule-family framework over a node witness (defect/severity, registration, walker, `PackRule`, byte-parity `canonicalCodes`); + the `ColumnValidator` columnar rule family over a `Table` (`NotNull`/`OfType`/`InRange`/`Unique`, reusing the same defect model) | node witness (+ `Column`) |
| **`Fuaran.Core.Observer`** | the runtime-verification seam: the `Observation` record, the pure `Input`→`Flag` derivation shape, the register/snapshot/derive/subscribe contract, and an in-memory engine. The runtime analogue of `Validator`; all flag content stays domain-side | nothing (FSharp.Core only) |
| **`Fuaran.Core.Projection`** | the projection seam: a compact, id-keyed, round-trippable textual projection an AI reads instead of the wire JSON, plus scoped/windowed reads (whole / by-id / subtree / changed-since) | a domain `ProjectionWitness` |
| **`Fuaran.Core.Propagation`** | change propagation over a reference DAG + tree-level dirty recomputation: the dependency map from a per-call `readsOf`, the dependency order + reference-cycle enumeration (Tarjan SCC, cycles as data), and the minimal dirty set from a value change or a structural `SkeletonOp`; + the incremental recompute driver (`eval` / `evalFrom` over a domain-supplied node evaluator), whose agreement with full evaluation is a theorem under a stated evaluator contract ([`proofs/`](proofs/README.md), theorem 11). Owns no evaluator — staleness is a returned `Set` | node/id witness |
| **`Fuaran.Core.AiSurface`** | the AI-surface seam: read tools, the mutation-op emission catalogue (JSON schema per op kind), an NL→op pattern bank with a deterministic fast-path resolver, and a proposals/approval flow with agent-readable rejection guidance. Tool logic, pattern content and policy stay domain-side | a domain AI witness |
| **`Fuaran.Core.Lease`** | the lease/claim strand: claims over an abstract resource axis (Claim / Release / Expire) with total `apply`/`canApply` where a conflict is a typed rejection naming the holder and the overlap, a canonical wire codec, and an `OpStream` `StreamWitness` — lease history as an append-only hash-chained stream. Expiry takes time as data, so replay is deterministic | `Tree`'s `IdWitness` |
| **`Fuaran.Core.Conformance`** | a property-based law kit a domain runs against its witness (apply totality, `canApply`≡`apply`, apply∘invert=identity, `verifyChain`, replay determinism, `captureReplayLaws`, `transformLaws`, `capabilityLaws`, `queryLaws`, `compositionLaws`, `memoLaws`, `registryLaws`, `aggregateParityLaws`, `columnarOpLaws`, `columnarValidatorLaws`, `incrementalLaws`, `deferredLaws`, `capabilityPipelineLaws`, `FoldConfluence.laneFoldLaws`) | any witness + a generator |
| **`Fuaran.Core.Idl`** | the interface-definition model: a typed description of a domain's node vocabulary (kinds, unions, enums, records, four-way optionality, a declarable node envelope, hosted slots), the schema-driven canonical encoder/decoder derived from it, a deterministic adversarial sampler, the canonical `idl.json` artifact projection, and the host-neutral sanitisation floor | a declared vocabulary (data) |
| **`Fuaran.Core.Idl.Codegen`** | the generation half of the IDL engine: the F# structural-layer emitter with its declared-support channel, the TypeScript encoder backend, the JSON-schema emitter, the scaffold writer, the codegen trust boundary, and the stability diff classifier over two `idl.json` revisions. Build-time and .NET-only — it emits source, so its contract is the shape of what it emits | the same declared vocabulary |
| **`Fuaran.Core.Idl.Cli`** | the stability classifier as a command (`fuaran-core-idl`): the wire severity per changed member with the reason it applies, the wire-profile evolution, and the F# consequence classes, exiting 0 / 3 / 4 for absorbable / breaking / undecided. Build-time and .NET-only | — |
| **`Fuaran.Core.CSharp`** | a C#-shaped facade over the closed F# unions a non-F# authoring veneer has to construct and read — the dataframe algebra, the artifact-function hole-declaration family, and the wire JSON model — with construction by factory method and reading by `Match`/`Switch`. .NET-only, and deliberately NOT part of the Fable-clean set | — |

Dependency order: `Tree` → `Ops` → (`OpStream` → `OpStream.Dag`; `Wire` standalone); `Column` over `Wire` → `DataFrame` over `Column`; `Validator` over `Tree` + `Column`; `Function` over `Tree`/`Ops`/`Wire`; `Column.Ops` over `Column` + `DataFrame` + `OpStream`; `Query` over `Column` + `Function`; `Conformance` over all of the above.
Beside that spine, and all five read by `Conformance` so they precede it: `Observer` standalone; `Projection` over `Tree`; `Propagation` over `Tree` + `Ops`; `AiSurface` over `Wire` + `Ops`; `Lease` over `Tree` + `Wire` + `OpStream`. Then the build-time and veneer tier, which nothing above depends on: `Idl` over `Wire` → `Idl.Codegen` over `Idl` → `Idl.Cli` over `Idl.Codegen`; `CSharp` over `Column` + `DataFrame` + `Function` + `Wire`.

**This table is a derived roster, not a hand-kept list.** Its rows are held equal to the packable
projects under `src/` by `PackageRosterTests` in the suite `./verify.ps1` runs, so a package that
ships and is not documented here — or a row for a package that no longer ships — is a red gate
rather than a discovery. The *purpose* column stays hand-written; only the roster is asserted.

## The witness pattern

The core never sees a concrete `NodeKind`. A domain supplies a small record of functions
(the *witness*) and the generic functions operate through it:

```fsharp
let nodew : NodeWitness<MyNode, MyId> =
    { Id = fun n -> n.Id
      KindTag = fun n -> n.Kind
      Children = fun n -> n.Children
      ReplaceChildren = fun n cs -> { n with Children = cs } }

// now the whole skeleton op algebra works over MyNode, with no core change:
Ops.apply nodew idw (InsertChild(parentId, child)) tree
```

The one genuine cross-domain divergence — id representation — is a witness too:
`IdWitness<'Id> = { ToString; OfString; Equals }`. Doc/Calc keep human-meaningful string
ids; UI/Music keep Guids; both over one `Core.Tree`.

### What the witness surface covers — and what stays yours

Every traversal in this library — `Tree.ids`, `Tree.exists`, `Tree.updateNode`, and the whole op
algebra built on them — walks `NodeWitness.Children` and nothing else. That is the **witness
surface**, and it is the exact scope of the one structural invariant the engine enforces for you:

> **each id occurs at most once over the witness's `Children` traversal.**

That invariant has a name — **`Tree.WellFormed`** — and one definition. `Tree.wellFormed` answers it
for a tree and names the first id that breaks it; `Tree.graftWellFormed` answers it for the tree a
graft would produce, without building it. `Ops.apply` keeps it by reading the second: an
`InsertChild` whose subtree carries an id the tree already holds — or which repeats an id within
itself — is refused with `DuplicateId`, naming the first offender. `Diff.toOps` reads the first.
Asking whether a tree is valid and refusing an op that would invalidate it are therefore the same
question, asked of the same function.

**"Valid" here means STRUCTURALLY valid and nothing more.** Validity is three layers and only the
first is this library's: structural (the invariant above), vocabulary (your wire boundary decides
whether a kind and its fields are ones you know), and rule families (your own pre-emit lint). A
claim that says only "valid" has not said which, and `Tree.WellFormed` exists so that it can.

**A node your domain holds in a keyed, non-structural position is invisible to that traversal**,
and uniqueness over those positions is **your domain's obligation, not this library's.** If your
node type keeps children anywhere `Children` does not report them — a case table, a fallback slot,
a named alternative, an argument position — then run your own id check over your own full walk
before handing an op to `Ops.apply`; this engine cannot see those nodes and will not pretend to.

That boundary is deliberate rather than a gap waiting to be closed. `Children` is also what the
engine **rebuilds** through, so widening the witness to reach keyed positions would oblige every
domain to re-express them as an ordered list — a large change to what a domain must model, to buy
a check the domain is far better placed to make. `Conformance.opAlgebra` certifies the invariant at
exactly this scope over your own witness, with two laws that say different things: `"an accepted
insert introduces no id already present"` is about the op that can create a duplicate, and
`"apply's accept path preserves Tree.WellFormed"` is about every op, so a `MoveNode` or a `Batch`
that broke it could not hide behind the first.

**Which of this library's assumptions you can discharge, and which you inherit**, is one table:
[the Core-to-domain proof contract](proofs/README.md#the-core-to-domain-proof-contract). Every
`assumed` row of the claims ladder is classed there as a `domain-obligation` (a green
`Conformance` law at your witness is the sampled discharge — the invariant above is one of
them), a `model-bridge` (this repository's own model-to-production gap, which you inherit), or a
`premise` (what nothing discharges — the witness surface boundary above is one of those). The
table is checked against `proofs.json` row for row rather than reviewed.

### The container capability — what `applyContained` enforces, and the one thing it asks of you

`Ops.applyContained canHold` is the variant for a domain with leaves: `canHold` answers *can this
node hold children at all*, and a node that cannot earns a typed `NotAContainer` instead of an
op that silently does nothing. Containment **legality** — which kinds may parent which — stays
yours; this is the coarse question only.

It is enforced at three places, and the third is worth stating because it is the one a caller
authoring a subtree meets: the PARENT of an `InsertChild`, the NEW PARENT of a `MoveNode`, and
**every node of the inserted subtree that holds children**. A graft whose own interior places
children under a node your predicate refuses is rejected, with `NotAContainer` naming that node —
which is a node of *your* graft, not of the tree, so look for it there. A `MoveNode` is deliberately
not walked: the subtree is already in the tree, so it carries in no interior the tree did not
already hold.

**What it asks of you in return: write `canHold` over the node's own kind (or its own fields), never
over its children.** The type is `'Node -> bool`, so a predicate *may* read the child list — and one
that does can admit a node at the instant the engine checks it and refuse it the instant the very
insert that check licensed gives it a child. No check placed anywhere in this library repairs that,
because the answer changes under the edit. `Conformance.containerLaws` perturbs a node's children
and requires the predicate to be unchanged, so you meet this as a red law rather than as a tree your
own predicate calls invalid. Run it alongside `certify`; it is opt-in because a domain with no
container notion has nothing for it to say.

## The artifact-function three laws (`Fuaran.Core.Function`)

A saved typed tree behaves as a function of its declared holes. The contract bakes in:

1. **Totality** — bounded iteration only; an unbounded `RepeatHole` is rejected, never run.
2. **Hygiene** — holes bind by absolute lexical address (id-path), never bare name, so
   composition cannot capture.
3. **Effect signature** — a mandatory two-axis effect/determinism class, joined
   componentwise through `compose` (pure ∘ clock = clock).

## Build

```powershell
./run.ps1            # format + build + test
./verify.ps1         # format-check + build + Fable-compile gate + test (the green gate)
./verify.ps1 -Proofs # … plus the proof leg: the F* model of the DAG fold, checked and re-extracted
dotnet build Fuaran.Core.slnx
dotnet run --project tests/Fuaran.Core.Tests
```

`./verify.ps1` includes a **Fable-compile gate** (Phase 54): `tests/fable-smoke/` references every
public package and is compiled with `dotnet fable`, so the "Fable-clean on encode **and** decode" claim
is enforced in-repo, not discovered downstream — a package that stops compiling under Fable fails the gate.

`proofs/` carries a **machine-checked model of the N-lane DAG fold** (Phase 131): `DagFold.fst` is
an F\* model of `Dag.reconcileMany` and the replay with fold confluence proved as a theorem, and
`proofs/oracle/DagFold.fs` is that model extracted to F# and run by the suite beside the production
fold as a differential oracle. What the theorem covers, what it assumes and how to run the leg are
in [`proofs/README.md`](proofs/README.md); `./verify.ps1 -Proofs` (and CI's `proofs` job) installs
the pinned prover and checks it.

398 conformance tests exercise every layer against an in-repo reference witness (a tiny
string-id domain) — proving the generics work **without depending on any domain
workspace**. Domain adoption (re-expressing each domain's machinery over `Fuaran.Core.*`)
is deliberately out of scope here; it lands on each domain workspace's own roadmap.

## Adopting a domain

Re-expressing a domain spine over `Fuaran.Core.*`? Start at [`docs/ADOPTION.md`](docs/ADOPTION.md) —
the four-witness recipe + the caveats a real adoption surfaced — with the runnable template at
[`samples/adoption`](samples/adoption/Program.fs).

## Status

Pre-1.0 — the released version is single-sourced from `<Version>` in
`Directory.Build.props`. The witness-record contracts (especially `IdWitness` and
`NodeWitness`) are the stability-critical surfaces — see [`STABILITY.md`](STABILITY.md).
Design log in [`DECISIONS.md`](DECISIONS.md).
