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
| **`Fuaran.Core.Ops`** | the skeleton-five structural ops + the recoverable error-envelope (the AI-feedback protocol), dry-run `canApply`, op `invert` (undo/redo), structural `Diff.toOps` | node/id witness |
| **`Fuaran.Core.OpStream`** | append-only hash-chained stream, `verifyChain`, `replay`, **portable** JSONL encode + decode, snapshot/`compact`/`replayFrom` (bounded replay), determinism capture/replay (`captureEffect`/`replayEffect`/`verifyCaptures` — exact replay of clock/random/network effects) | `(apply, encode, decode)` witness |
| **`Fuaran.Core.OpStream.Dag`** | content-addressed branching/merging op-DAG, `verifyDag`, deterministic `replayTo` a head | the same stream witness |
| **`Fuaran.Core.Wire`** | the `"kind"`-tag/camelCase envelope, Fable-clean encode + **portable** decode combinators, corpus tooling | a domain codec |
| **`Fuaran.Core.Function`** | the artifact-function protocol: `signature`/`apply`/`curry`/`compose` under the three laws, `auditEffect`, `toSchema`; + the invocable `Capability` seam (typed registry + enumerate + default-deny dispatch, arg-validated invocation, Phase-27 replay keying); + the `Deferred<'T>` async-result envelope, the signature-typed `FunctionRegistry` (`findBySignature`), and the serializable `CapabilityPipeline` (typed capability-DAG) | artifact witness |
| **`Fuaran.Core.Column`** | the relational/columnar data strand: a typed, null-aware (validity-mask) columnar model over the fixed Arrow scalar set (`int`/`float`/`bool`/`string`/`date`/`timestamp`) + the canonical column-oriented wire codec (six-code decode envelope, `Wire`-canonical floats); + `Schema.diff`/compatibility/`fingerprint` and the public `Column.aggregate` surface | a self-contained data strand (no witness) |
| **`Fuaran.Core.DataFrame`** | the declarative-compute layer over `Column`: a serializable `Transform`/`ColExpr` algebra (full v1 verb set — filter/project/derive/groupBy/join/window/pivot/unpivot/sort/distinct/limit/union), a pure reference evaluator with pinned null/coercion/order/float semantics, a canonical wire codec, and the incremental `evalFrom` (change-relevance reuse, byte-identical to a full eval) | the columnar strand |
| **`Fuaran.Core.Column.Ops`** | the columnar op-algebra bridging the data + op-stream strands: a `ColumnOp` DU (SetCell / SetColumn / InsertColumn / RemoveColumn / AppendRows / ApplyTransform) with total `apply`/`canApply`, partial `invert`, a structural `Diff`, a wire codec, and a `Fuaran.Core.OpStream` `StreamWitness` — table edits as an append-only, hash-chained, replayable stream | `Column` + `DataFrame` + `OpStream` |
| **`Fuaran.Core.Query`** | the declarative, cross-domain data-acquisition seam — the data-acquisition *sibling* to `Capability`: a serializable, typed `Query` declaration (typed params + result `Schema` + `EffectClass` + `DataSource`) producing a `Table`, with a default-deny registry (enumerate + dispatch), param-type validation, Phase-27 capture keying (`invocationKey`), and a canonical wire codec. The host supplies the resolver (witness pattern) | `Column` + `Function` |
| **`Fuaran.Core.Validator`** | the rule-family framework over a node witness (defect/severity, registration, walker, `PackRule`, byte-parity `canonicalCodes`); + the `ColumnValidator` columnar rule family over a `Table` (`NotNull`/`OfType`/`InRange`/`Unique`, reusing the same defect model) | node witness (+ `Column`) |
| **`Fuaran.Core.Conformance`** | a property-based law kit a domain runs against its witness (apply totality, `canApply`≡`apply`, apply∘invert=identity, `verifyChain`, replay determinism, `captureReplayLaws`, `transformLaws`, `capabilityLaws`, `queryLaws`, `compositionLaws`, `memoLaws`, `registryLaws`, `aggregateParityLaws`, `columnarOpLaws`, `columnarValidatorLaws`, `incrementalLaws`, `deferredLaws`, `capabilityPipelineLaws`, `FoldConfluence.laneFoldLaws`) | any witness + a generator |

Dependency order: `Tree` → `Ops` → (`OpStream` → `OpStream.Dag`; `Wire` standalone); `Column` over `Wire` → `DataFrame` over `Column`; `Validator` over `Tree` + `Column`; `Function` over `Tree`/`Ops`/`Wire`; `Column.Ops` over `Column` + `DataFrame` + `OpStream`; `Query` over `Column` + `Function`; `Conformance` over all of the above.

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
