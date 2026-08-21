# Fuaran.Core

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
Ops.apply nodew idw (InsertChild(parentId, 0, child)) tree
```

The one genuine cross-domain divergence — id representation — is a witness too:
`IdWitness<'Id> = { ToString; OfString; Equals }`. Doc/Calc keep human-meaningful string
ids; UI/Music keep Guids; both over one `Core.Tree`.

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
dotnet build Fuaran.Core.slnx
dotnet run --project tests/Fuaran.Core.Tests
```

`./verify.ps1` includes a **Fable-compile gate** (Phase 54): `tests/fable-smoke/` references every
public package and is compiled with `dotnet fable`, so the "Fable-clean on encode **and** decode" claim
is enforced in-repo, not discovered downstream — a package that stops compiling under Fable fails the gate.

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
