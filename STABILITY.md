# Fuaran.Core — API stability

**Status:** pre-1.0. The released version is single-sourced from `<Version>` in
`Directory.Build.props` — this document deliberately does not restate the number (restated
versions drift; the props file cannot). The surface may change as the first domain adopters
(UI, Calc, Documents, CAD, Office) re-express their machinery over these packages and
surface friction. Once adopted, the witness contracts harden.

## Versioning policy

Per-release semver: `0.0.1-alpha` → `0.0.1-alpha.2` → … → `1.0.0`. Published to the
`fuaran-ui` GitHub Packages NuGet feed. The publish workflow uses `--skip-duplicate`;
bump `<Version>` in `Directory.Build.props` before tagging.

**Every version cut cites a green run of the Core Fable gate against the candidate (Phase 217,
DECISIONS.md D55).** This repository runs no Fable compiler, so before the release gesture the
candidate packages are packed to a folder and `fuaran-dotnet`'s
`pwsh ./tests/core-fable/core-fable.ps1 -CoreVersion <candidate> -CoreFeed <folder>` is run against
them, and the release record names that run. It is the compile leg and the value leg at the
candidate — the value leg REQUIRED — so a Fable divergence is caught at the cut, before any consumer
can pin it, rather than when `fuaran-dotnet` next raises its pin. A cut whose run is red is not
released.

**Every released slot from `0.25.0` up has an entry header naming it, and the gate refuses a tag
that has none** — the `Package roster` family reads `git tag` and holds this document to the set of
releases the repository actually made, rather than to the standing `<Version>` alone. **`0.25.0` is
the floor because it is the oldest slot for which the record can still be written from evidence**:
every earlier release predates this document's per-slot classes, so entries for them would be
invention rather than record, and they are deliberately not retro-fitted.

## Public-surface baselines — the class of a move is a gate output, not an argument (Phase 183)

Every packable package carries a committed baseline of its public contract at
[`api/<package>.txt`](api/), rendered from the built assembly's IL metadata: one ordinal-sorted
line per externally-visible type, member, record field and union case. The `Public surface` test
family renders each package afresh on every gate run and diffs it against its baseline.

**Field names are part of the surface (Phase 237).** A record field has always rendered by name.
A union case renders each field as `name: Type` in declaration order. A consumer that constructs or
matches a case by field name (`TargetNotAContainer(target = …)`) stops compiling when that name
changes. So a union field rename is a `retype`, which is breaking, and the report names the case
and both names. Before Phase 237 a case rendered its field types only, and a rename moved nothing.

**The gate refuses an UNCLASSIFIED move, never a breaking one.** Additive or breaking, a move whose
baseline moved with it passes; what fails is a surface that moved while its baseline stood still.
The record-widening dispensation stands — widening is permitted, widening *in silence* is
not — and the class below is what a reviewer applies it to.

Regenerate with:

```
CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Tests
```

**The hazard is the one the same switch carries elsewhere: it rewrites EVERY drifted
baseline, not the one you were looking at.** An unrelated drift sitting in the tree lands in your
commit silently. Stage the baselines you meant to move BY NAME and read the rest back out.

### The classes, and what each costs a pinned consumer

The six are what the diff reports per move; the headline for a whole package is the most
informative of them present (a required record field widens the primary constructor too, so it
shows as `record-widening` beside a `retype`, and the headline names the cause rather than its side
effect).

| class | what moved | what it costs a consumer | ride or advance |
|---|---|---|---|
| `removal` | a baseline token with no counterpart — removed or renamed | call sites stop resolving | **advance `<Version>`** |
| `retype` | the same member rendered differently: a parameter or return type changed, or a record field's POSITION moved | call sites stop type-checking; positional construction binds the wrong slot | **advance** |
| `record-widening` | a field added to a record the baseline published | every full-literal construction fails, `FS0764` | **advance** |
| `union-widening` | a case added to a union the baseline published | every exhaustive `match` becomes incomplete — and against a stale same-version pack, an `InvalidCastException` at run time with no compile signal at all | **advance** |
| `interface-widening` | a member added to an interface the baseline published | every implementer stops compiling | **advance** |
| `additive` | a new type, a new module function, a new member on a class — and a field or case on a type the baseline never published | nothing: a pinned consumer compiles either way | may **ride** the standing draft slot |

The three middle rows are the reason this is not a plain token diff. A union case addition REMOVES
NOTHING — it emits a new factory, a new `IsCase` property and a new `Tags` literal — so a
removal-only differ reports it as ordinary growth. They are read off the
`CompilationMappingAttribute` the F# compiler already emits, which is what makes them computable
rather than arguable.

### What the gate prints, and what it does not claim

The family prints the class of every baseline that has moved **since the newest `vX.Y.Z` tag** —
the "ride or advance" line that entries in this document wrote by hand until now. A package whose
baseline the newest tag does not carry is reported as a first snapshot rather than as unchanged.

**A tag cut before Phase 237 is read without field names, and the report says so.** Its baselines
carry nameless union cases. Compared with today's named ones, they would report every carrying
case as a `retype` that no consumer ever saw. So when the tagged baseline predates the names, the
report strips them from the current surface before it classifies. It prints a line for that
package: a field rename since that tag is not visible to the comparison, just as it was invisible
to the gate then. **This normalisation expires by itself.** The first tag cut after Phase 237
carries named baselines, and nothing is stripped against it. **The live gate never normalises.** A
committed baseline without names reads as moved against today's render. So a stale baseline cannot
hide a rename.

Three boundaries, named rather than assumed:

- **Not semantics.** A function whose signature is unchanged and whose behaviour reversed is
  invisible here and always will be.
- **Not the Fable source half.** Every packable project also ships its `.fs` sources under `fable/`
  for a Fable consumer to compile; what is rendered is the managed assembly, which is the .NET
  consumer's contract.
- **Not internals.** An internal member cannot break a consumer in another assembly and is not in
  the surface to begin with.

## The wire surface — a canonical-encode change states its class in the same commit (Phase 214)

The managed baseline's first boundary is "not semantics", and the canonical BYTES a package emits sit
on the far side of it. Phase 213 moved them: a `project` step's `cols` became `columns`. That is
breaking for every consumer that pins canonical bytes, and the managed gate correctly reported
nothing. So the wire surface has a baseline of its own:
[`api/wire/<package>.txt`](api/wire/).

**What each file holds.** One file per wire-bearing package. It holds the canonical bytes of a
document set that reaches every member name and every discriminator the package emits. Each line
carries the sha256 of the emitted bytes and the document rendered canonically. The set is DERIVED by
reflection from each package's root document types, so a case added to a union is in the set without
anyone listing it (DECISIONS D53).

**What the gate does.** The `Wire surface` test family re-encodes the set on every gate run. A changed
byte fails it, naming the document and the member, until the baseline is regenerated:

```
CORE_APPROVE_WIRE=1 dotnet run --project tests/Fuaran.Core.Tests
```

**THE RULE: a wire-baseline change states its class in the same commit.** The regeneration writes the
class into the baseline's own header (`# class since <tag>: <class>`). A second test recomputes that
class against the tag the header names and fails if the statement is missing, stale or wrong. The
commit that moves a baseline therefore carries its class, and the class is checked rather than
asserted. Put the same word in the commit message and, for a published package, in the draft slot's
entry here.

The classes:

| class | what moved | ride or advance |
|---|---|---|
| `additive` | a new member, or a new case (a document added) | may **ride** the standing draft slot |
| `breaking` | a member renamed or removed; a discriminator changed; a document removed; the same structure emitted as different bytes | **advance `<Version>`** |

An F# case renamed with its wire bytes untouched is not a wire move; that one is the managed gate's.
A member reordering cannot occur in `Canon.render` output, which sorts keys. The two emitters that are
not `Canon.render` (the IDL artifact's indented form, and the hand-assembled stream envelopes) are
pinned by the hash, and a layout move there is `breaking`.

**What it does not pin, named rather than assumed:**
- **Decode.** A consumer pins what a package EMITS. A decode alias is outside the baseline, and
  adding one moves nothing; a test holds that for the alias Phase 213 kept.
- **A domain's vocabulary.** A domain's values under its own IDL vocabulary, and the op payload
  embedded in a stream envelope, are the domain's to pin.
- **Semantics.** The same bytes meaning something new is invisible here, as it is to the managed
  gate.

**The hazard is the managed switch's.** The switch rewrites EVERY drifted wire baseline. Stage the
ones you meant to move BY NAME.

## The load-bearing invariant

`Fuaran.Core.*` is **a library of generic functions over domain-witness records, never a
base node type.** This is not a style choice — closed exhaustive `NodeKind` DUs with
total matching are load-bearing in every domain (the compiler-checked totality guarantee
no open base type can give). A shared base node type
in the core would destroy that property. Any change that introduces a concrete node/kind
type into a core package is a breaking architectural regression, not a feature.

## Stability-critical surfaces

These thread through multiple packages; changing their shape is a breaking change
requiring a major-version bump and coordinated domain-adopter updates (adopting a domain?
see [`docs/ADOPTION.md`](docs/ADOPTION.md)):

- **`IdWitness<'Id>`** (`{ ToString; OfString; Equals }`) — resolves the string-vs-Guid
  identity axis. Threads through `Core.Tree`, `Core.Ops`, `Core.Wire`, `Core.Function`.
  Deliberately carries **no `fresh`**: the generic functions mint no ids (hygiene derives
  ids deterministically); id minting stays domain-side.
- **`NodeWitness<'Node, 'Id>`** (`{ Id; KindTag; Children; ReplaceChildren }`) — the four
  accessors the whole tree/ops/validator surface operates through.
- **`StreamWitness<'Op, 'State, 'Rej>`** (`{ Apply; Encode; Decode }`) — the two-seam
  op-stream witness. `Decode : string -> Result<'Op, string>` as of `0.0.1-alpha.3` —
  the recoverable-envelope discipline; `fromJsonl`/`snapshotFromJsonl` return
  `Result` (migration notes shipped with that release).
- **`Actor`** (`Human of id | Agent of model * version * id`) + the `OpRecord` / `DagNode` /
  `StreamConfig` / `append` / `merge` actor field+parameter (typed as of `0.0.1-alpha.13`).
  The typed actor is **folded into the chain hash** — a breaking hash-format bump.
  See "Typed, attested provenance" below (migration notes shipped with that release).
- **`Hash.fnv1a` / `Hash.sha256Hex` / `Hash.sha256Bytes`** (`Core.Tree`) — the two hashing
  regimes. Their **values** are the contract, not merely their signatures: `fnv1a` is folded
  into every stored content hash and `sha256Hex` is pinned to the FIPS 180-4 vectors, so a
  change to what either returns invalidates persisted data rather than breaking a compile.
  Which regime a call site is in is part of its meaning — a cache fingerprint quietly
  substituted for the crypto digest would still typecheck.
- **`ArtifactWitness<'Node, 'Id>`** (`{ Tree; IdW; Holes; Effect; Bind }`) — the
  artifact-function witness.
- **`RowIdentity<'Id>`** (`{ Scheme; KeyOf; KeyString }`, `0.9.0`) — the columnar strand's
  row-identity witness, the seam every `TableDelta` producer is parameterised over. A per-call
  argument, never a field on `Table` / `Column` / `Transform`: the columnar strand stays
  witness-free, and a delta is told how to key a row without Core learning what a key means.
- **`SkeletonOp` carries no ordinal** (`0.2.0`, 2026-07-26). `InsertChild` and `MoveNode` are
  `(parent, node)` and `(target, newParent)`; both **append**, and `ReorderChildren` states order by
  naming ids. Placing a node anywhere but last is `Batch [InsertChild …; ReorderChildren …]`.
  **Breaking twice over**: the two constructors lost an argument, and `Rejection.IndexOutOfRange` was
  removed because nothing can construct it any more — an unreachable rejection case is dead
  vocabulary, and the envelope discipline above is about naming real failures.
  **The rule this establishes: where a collection's members have identity, they are addressed by it.**
  An ordinal names a projection over a list rather than anything the tree stores — children are a list,
  so order is structural and no index exists in the state. It is therefore derivable, snapshot-bound,
  and silently wrong after any preceding or concurrent edit, where a wrong id fails loudly.
  Reintroducing a positional argument to these ops is a regression, not a convenience. Contained data
  with no identity of its own (a column list, a chart's series) is a different case and may still be
  addressed positionally.
  *Consequence worth knowing:* a remove and an insert on the same parent now **commute**, where the
  index-bearing forms did not. `Ops.footprint` still reports them dependent — it is a deliberate
  over-approximation keyed on the parent id and does not consult the tree — and that conservatism is
  pinned by a test so it reads as a choice rather than a defect.
- **The recoverable envelope discipline** — `Rejection<'Id>` (Ops) and `ApplyError`
  (Function) must always *name the failure and enumerate the valid alternatives*. New
  cases are additive; removing the enumeration from a case is breaking.
- **`EffectClass`** + the `Effect.join` law — the two-axis effect signature is mandatory
  and total; `compose` must always join componentwise. Making the effect optional is
  breaking (it is exactly how impurity leaks in).
- **`Wire.Versioning`** — the wire version/profile + forward-compatibility contract
  (`Profile` `<name>@<major>.<minor>`, `negotiate` → `Current` / `Behind` / `Foreign`, the
  `$profile` / `$payload` `Envelope`, the transport-only `Unknown { Kind; Payload; RequiredProfile }`
  carrier + `decodeTolerant` / `reencode` must-ignore-but-preserve, `classify` / `bump`). This is a
  **cross-host wire contract**: the envelope shape, the negotiation table, the name+major equality
  rule, and the byte-for-byte preservation of an unknown kind are re-implemented byte-identically by
  each language host (F#/TS/Python) and conformance-certified (the `VersioningTests` in-repo, plus the
  `envelope-*` families in the shared UI `wire-format-fixtures/` corpus). The base profile is `core@1.0`;
  the bare (un-enveloped) form is read as the implicit `core@1.0`, so envelopes are opt-in carriage — no
  existing wire changes. Changing the envelope keys, an `Unknown` carrier field, a `negotiate` outcome,
  or dropping preservation is a **major** bump; an additive kind/case is a **minor** bump an older
  consumer tolerates via `decodeTolerant`.

### The Compute strand — `Column` / `DataFrame` / `Query` (first-class)

> **Since `0.33.0` (Phase 258, DECISIONS.md D66) `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops`
> are produced by [`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute),
> and every change to them from `0.33.0` on is recorded in that repository's `STABILITY.md`.** The
> paragraphs below about those two packages are the record of what this repository promised while
> it produced them (up to `0.32.0`), which that document points at rather than restates; the
> promises about `Column` and `Query` are still this repository's.

The relational/columnar strand (`Fuaran.Core.Column`, `Fuaran.Core.DataFrame`, `Fuaran.Core.Query`) is
a **first-class, stability-critical member of the substrate**, not an example or a reference sketch —
its public surface carries the same 1.0 commitment as the witness spine, and its cross-host semantics
are **conformance-certified**, not asserted. Stable surfaces:

- **The columnar model + wire codec** — the fixed Arrow scalar set (`int` / `float` / `bool` / `string`
  / `date` / `timestamp`, and from `0.33.0` the exact `decimal`), the validity-mask null model, and `ColumnCodec`'s six-code decode envelope
  are the cross-host contract: two hosts encode a column to byte-identical wire (same null / coercion /
  ordering / **canonical-float** semantics — floats route through `Wire.Canon.canonicalFloat`, certified
  by `Conformance.canonicalFloatLaws`). Changing the scalar set, the null model, the codec envelope, or
  a coercion/widening rule is a **major** bump.
- **`Column.aggregate`** — the single source every `DataFrame.GroupBy` aggregate calls; its per-fn
  semantics (Count / Sum / … including the empty/all-null cases) are pinned by
  `Conformance.aggregateParityLaws` (single-source parity: `aggregate fn col` == the one-group GroupBy
  cell). A change to an aggregate's result is breaking.
- **The `Transform` pipeline algebra** — the transform-step vocabulary + its evaluator are certified by
  `Conformance.transformLaws`: a reference and a host evaluator agree by **byte-identical wire output**
  (or both reject), the cross-host determinism contract. Removing a step kind or changing a step's
  semantics is a major bump; adding one is minor.
- **The vocabulary's closed sets + the recorded omissions (Phase 101)** — `Transform` /
  `JoinKind` / `AggFn` / `WindowFn` / `ScalarFn` are closed sets, and Phase 101 closed their
  remaining asymmetries **additively** (minor): `Intersect` / `Except`; `Semi` / `Anti`;
  `CountDistinct`; `DenseRank` / `CompetitionRank` / `NTile` / `CumulMax` / `CumulMin` /
  `RollingSum`; `Sqrt` / `Least` / `Greatest` / `IndexOf`. Every pre-existing pipeline encodes to
  byte-identical wire (the one new field, a window step's `"n"`, is emitted only for `NTile`), so a
  consumer repins without source changes. Two semantics are **pinned and load-bearing**: the set
  operations and `CountDistinct` key on the canonical `Distinct` token (`Null` matches `Null`,
  `NaN` is one value, `Int 1` ≠ `Float 1.0`), and `Rank` remains the **dense** rank it has always
  computed — `DenseRank` is its explicit spelling, `CompetitionRank` is SQL `RANK()`. Re-pointing
  `Rank` at the gapped semantics would be a **major** bump, not a fix. What is deliberately ABSENT
  is a decision, not a gap — no clock, no regex, no `Pow`/`Log`, no explode/`Split`, no `PadLeft`;
  the reasons (determinism, cross-host portability, IEEE-754 rounding, the flat `Cell` set) are
  recorded in `DECISIONS.md` D13 and beside the `ScalarFn` declaration.
- **`ColExpr.Param` + the evaluation environment (Phase 77)** — `ColExpr` gains a `Param of name`
  case and the evaluator gains env-aware entry points (`DataFrame.evalPipelineInEnv` /
  `evalPipelineWithInEnv`, an `env: Map<string, Cell>`), certified by `Conformance.paramLaws`. Both
  are **additive** (minor): the env-less entry points (`evalPipeline` / `evalPipelineWith`) delegate
  with `Map.empty`, so **param-free pipelines evaluate and encode byte-identically** to before — no
  signature change, corpus byte-stable. Core semantics are **strict**: an unbound `Param` is the
  enumerated `EvalError.UnboundParam(name, bound)` (also additive), never a throw and never a guessed
  default. The lenient "unset filter ⇒ no constraint" idiom is **host-side policy**, not Core's — a
  host prunes steps whose params are unbound *before* evaluating; `Transform.paramsOf` (pure, total,
  deduplicated) is the contract that makes that derivable, and is also what a host reads to derive
  dependency edges + reactivity subscriptions. Removing `Param`/`UnboundParam` or changing the strict
  unbound semantics is a major bump.
- **`Query`** — the data-acquisition seam + its invocation key (also `canonicalFloat`-routed) are
  certified by `Conformance.queryLaws`.

A domain that ships a `Transform` evaluator / `Query` provider runs `transformLaws` / `queryLaws`
alongside its base witness certification; a host in another language re-implements the codec + these
laws against this section, exactly as it does the witness surface.

**Three incremental-eval surfaces (Phase 34 columnar ∥ Phase 62 capability ∥ Phase 68 tree-level).** The
compute strand carries three dirty-aware re-evaluation paths sharing one discipline — the incremental
result is **byte-identical to a full eval over the same inputs** (certified, not asserted):
`DataFrame.evalFrom` (columnar, change-relevance reuse, `Conformance.incrementalLaws`, Phase 34);
`CapabilityPipeline.evalFrom` (the capability-DAG, re-invoke only the downstream-of-change nodes,
`Conformance.capabilityPipelineIncrementalLaws`, Phase 62); and `Fuaran.Core.Propagation.evalFrom` (a
typed domain tree with declared id-addressed bindings — the minimal dirty set from a value change or a
structural `SkeletonOp` (Phase 68) drives a dirty-subgraph recompute in dependency order, reusing clean
nodes, `Conformance.propagationEvalLaws`, Phase 69). `Propagation.eval` is the reference full evaluator
the tree-level `evalFrom` is certified against — Core walks the acyclic nodes in dependency order and
returns cyclic SCCs as data (`EvalOutcome.Cyclic`, the `#CALC!` posture; the iterative upgrade is the
`office`/Calc convergence work), the host supplies the injected `evalNode` (GP6 — no evaluator in Core).
**On the tree-level path the declared reads are ENFORCED, not assumed (Phase 209):** the resolver
`evalNode` is handed answers for `deps[id]` and for nothing else, and a read outside that set is the
typed `EvalUndeclaredRead` naming the node and the read — so the byte-identity above is a property of
the driver rather than a clause the host has to keep. A host re-implementing this path implements the
restriction too. The first two are keyed on the same content-addressing discipline (the Phase-49 `applyMemo` /
Phase-27 capture keys). `CapabilityPipeline.eval` is the reference evaluator the capability path is
certified against — Core supplies the DAG plumbing (topological walk + `FromNode` edge resolution), the
host supplies the node `body`. A dirty non-deterministic node re-invokes (or replays from its Phase-27
capture); a clean node reuses its prior value, so incrementality never serves a stale effect result (the
Phase-53 effect-honesty gate on the dirty path).

**`Fuaran.Core.Propagation` owns no evaluator (GP6).** The new package (added Phase 68) computes *what is
stale* — the dependency structure, the cycle enumeration, and the dirty `Set` — and returns it as data;
the actual re-evaluation stays domain-side. The dependency relation is a per-call `readsOf` function, not
a witness field (GP2); staleness is a returned `Set`, not a stored flag. Its public surface (`dependencyMap`
/ `sort` / `cycleThrough` / `dirtyFromChangedIds` / `touchedBy` / `dirtyFromOp` / `staleSet`) is
FSharp.Core-only + Fable-clean and carries the same pre-1.0 additive-growth commitment as the rest of the
substrate.

### Public because a sibling Core package calls it (`0.19.0`)

`Fuaran.Core.*` is nineteen assemblies and declares **no `InternalsVisibleTo` anywhere**, so a
function one Core package needs from another has to be public — `internal` cannot express it. Four
members are exactly that and nothing else: no caller outside these packages, no test, and until
`0.19.0` no document saying why they were public. A reading that classifies surface by caller count
therefore takes each for dead and reaches for a narrowing that would not compile. They are contracts:

- **`Fuaran.Core.ColumnOps.changeOf`** — `Fuaran.Core.Conformance` drives the edit-stream incremental
  re-evaluation law through it.
- **`Fuaran.Core.FunctionRegistryModule.partiallyApply`** — the content-pack currying
  `Fuaran.Core.Conformance` builds its samples with.
- **`Fuaran.Core.Memo.isMemoisable`** — the memo-soundness law asks it the same question the memo
  gate asks, which is the whole point of that law.
- **`Fuaran.Core.Idl.ProposalModule.touchedKinds`** — `Fuaran.Core.Idl.Codegen` reads it when
  reporting which kinds a proposal delta reaches.

Public here is a consequence of the package split, not an invitation. They carry the ordinary
breaking-change terms above; nothing in this entry promotes them to a documented extension point.

### Members the other language hosts mirror name for name (`0.19.0`)

Six members are re-implemented under the same name by the TypeScript and Go reference
implementations. Those hosts do not call the F#; the F# is the reference they must agree with. So a
rename, or a change to what one of these returns, is a cross-host divergence to be landed in every
host in the same change-set — whatever the caller count in this repository says.

- **`Fuaran.Core.CellModule.defaultFor`** — the type default a `Null` cell encodes as on the wire.
- **`Fuaran.Core.DataFrame.cellString`** — the cell → string projection group keys and sorts read.
- **`Fuaran.Core.DataFrameCodec.decodeExpr`** — the column-expression decoder.
- **`Fuaran.Core.CapabilityPipelineModule.nodeInvocationKey`** — a pipeline node's invocation key.
- **`Fuaran.Core.Idl.Sanitize.sanitizeUrlOrBlank`** — the URL sanitiser. Its RESULT is the contract:
  a divergence here is a security divergence, not a cosmetic one.
- **`Fuaran.Core.Conformance.capabilityLaws`** — the capability law family a host certifies against.

## Witness-record field freeze (the 1.0 contract)

The thirteen public witness records (`IdWitness`, `NodeWitness`, `StreamWitness`, `ArtifactWitness`,
`AiSurfaceWitness`, `ProjectionWitness`, since Phase 330 the six conformance-kit inputs
`CapabilitySeamWitness`, `QuerySeamWitness`, `CapabilityPipelineWitness`, `ConstructWitness`,
`KeyedWitness`, `EvaluatorWitness`, and since Phase 313 `RefWitness`, frozen at birth) are
**plain records**: adding a field is a compile-break for *every* adopter's construction site, with no
gradual-migration path. At `1.0` their field sets **freeze**. (The freeze originally named only the
four base records; `AiSurfaceWitness` and `ProjectionWitness` are equally public, equally
adopter-constructed, and carry the same blast radius — an unpinned public witness is exactly the gap
the freeze exists to close, so they are frozen on the same terms. The six conformance-kit inputs
joined at Phase 330 on the same argument: a domain builds each one by name, exactly as it builds a
core witness, so a field added to one breaks every adopter that runs its family — and `KeyedWitness`
is read by the apply path besides, since Phase 286.) This is a safe commitment, not a
gamble: the adoption cycle exercised the surface across eight structurally-distinct domains — a
prose/section tree, a heterogeneous spreadsheet model, a CAD feature tree, a belief-bearing world
graph, the UI tree, a derived legal-drafting domain, a slide deck, and a music score — spanning both
id shapes (string and Guid), every state shape (homogeneous tree / heterogeneous layers / graph),
every op posture (own op-stream / base-domain op-stream via projection / no op layer yet), and the
non-equality-state case. Each of the last five adopters needed **zero** additive witness surface. The
surface has saturated; the freeze records that.

**How a genuinely-new post-1.0 capability evolves — compose, never grow.** The evolution path is
already in the design: `ArtifactWitness` does **not** add fields to `NodeWitness`/`IdWitness`; it
*embeds* them (`{ Tree: NodeWitness<…>; IdW: IdWitness<…>; Holes; Effect; Bind }`) and carries the
extra accessors itself. Any future generic layer that needs more domain accessors follows that
precedent: it introduces a **new composing witness record** that embeds the frozen ones plus its own
fields. Existing adopters that don't use the new layer construct nothing new and never break; adopters
that opt in construct the new record once. This keeps Core's evolution *additive at the type level*
without a flag-day.

**`WitnessV2` + bridge is the last resort, not the default.** A versioned record
(`NodeWitnessV2` + a `NodeWitness -> NodeWitnessV2` bridge, both maintained for a deprecation window)
is reserved for the case where a *frozen* witness is found fundamentally insufficient — not merely
"a new layer wants more," which composition already covers. That is a major-version event and must be
justified against why composition could not express it.

**Enforcement.** New `Fuaran.Core.*` code that adds a field to any of the twelve frozen records is a
review-blocking regression (the same class as introducing a concrete node type, per *The load-bearing
invariant* above). The mechanical backstop is the law family **`Conformance.witnessSurfaceLaws`**
(Phase 232), which this repository's suite runs on every gate: one law per frozen record, holding
its field set — read by reflection from the compiled record — equal to the pinned list in
`Conformance.frozenWitnessFields` **by name and in declaration order**, and one more law holding
that every public record named `…Witness` in the Fuaran.Core assemblies the kit references is
either frozen there or declared outside the freeze, with its reason, in
`Conformance.unfrozenWitnesses` (empty since Phase 330, which froze the six conformance-kit inputs
Phase 232 had declared there; the list and the law that reads it stay, so a witness added before
`1.0` is still classified by the commit that adds it). A field added,
removed, renamed or reordered on a frozen record turns the gate red **by the record's name**, and
regenerating the `api/` baseline does not turn it green. A domain can run the same family at its
own pin bump to certify that the Core it compiled against carries the frozen shape.

**The opt-out for a deliberate widening before `1.0`.** Until the `1.0` cut, a frozen record may
grow only as a deliberate, named act, never as a regenerated baseline: (1) edit the record's entry
in `Conformance.frozenWitnessFields` to the new field list — the law stays red until you do, and
that edit is the act a reviewer sees in the diff; (2) in the SAME commit, add an entry under the
open draft heading of this file naming the record, the field, the class the surface gate reports
(`record-widening`), and why composition — a new witness record that EMBEDS the frozen one — could
not express it; (3) regenerate the `api/` baselines. Extending the freeze to a record now declared
outside it is the same act in reverse: move it from `unfrozenWitnesses` to `frozenWitnessFields`
with an entry here. From `1.0` there is no widening route: a frozen witness does not grow, and the
`WitnessV2` path above is the only one.

## Digest changes

Content-hash digests are in-memory fingerprints (FNV-1a; not cryptographic), not a wire format —
but a domain that *persists* a digest as a content-address should note one-time changes here.

- **`Tree.encodeHash` (Phase 11, `0.0.1-alpha.7`):** the fold now separates adjacent per-node
  encodings with the `U+0001` byte `contentHash` already used, instead of `""`. This fixes a
  boundary collision (`["ab";"c"]` and `["a";"bc"]` previously hashed equal) and aligns the code
  with its docstring. **`encodeHash` digests therefore change once**; `contentHash` digests are
  unchanged (same separator byte, now via the shared `hashSep` constant).
- **`Validator.canonicalCodes` (Phase 25, `0.0.1-alpha.8`):** the cross-host parity projection now
  joins sorted codes with the `U+0001` byte instead of `,`, so a code containing the separator can
  no longer alias. **The projected string changes once**; defect codes are stable identifiers, so a
  host that persisted the old `,`-joined parity string should re-derive it.
- **FNV-1a everywhere in the spine (`0.6.0`) — a ONE-SIDED change, and the asymmetry is the whole
  of it.** The multiply now goes through a split-half 32-bit form so the function computes true
  32-bit FNV-1a on both pipelines. **On .NET nothing changes**: every digest, content-address,
  chain hash and staleness stamp minted by a .NET process is byte-for-byte what it was, which is
  the constraint the fix was built to satisfy, and the pinned vectors enforce it. **Under Fable
  every value changes**, because the pre-`0.6.0` transpiled multiply overflowed 2^53 and was not
  FNV-1a at all. A host that persisted a JavaScript-minted FNV digest as a content-address must
  re-derive it from its source data. This is stated as a one-time digest change rather than a bug
  fix precisely because a caller cannot tell the two apart from the outside.

  **The surface is wider than `Hash.fnv1a`, and that is the part worth reading.** The spine shipped
  *six* copies of FNV-1a, inlined at various times so a package need not take a `Tree` dependency.
  Fixing only the canonical one would have left **`OpStream.defaultHash` — the op-stream chain
  hash — still divergent**, which is the exact harm the fix existed to prevent: two hosts replaying
  one log computing two different chains. All six are fixed in `0.6.0`. Three are now gone
  entirely (`Capability.invocationKey`, the pipeline node key, and `Query.invocationKey` call
  `Hash.fnv1a` directly — their packages already depended on `Tree`). Three remain by necessity —
  `Hash.fnv1a`, `OpStream`'s copy (standalone by DECISIONS D2) and `Column`'s (references only
  `Wire`) — and the two copies are now **checked against the canonical one by test and by probe**
  rather than kept in step by discipline. Affected values: `Tree.contentHash`, `Tree.encodeHash`,
  `Tree.Index.fingerprintOf`, `Projection.digestOf`, `ContentPack.signatureFingerprint`,
  `Function.Memo` keys, `OpStream.defaultHash`, `Schema.fingerprint`, and both `invocationKey`s.

## Typed, attested provenance (Phase 320, `0.0.1-alpha.13`)

The actor is a typed `Actor` (`Human of id | Agent of model * version * id`) **folded into the chain
hash** — a one-shot breaking hash-format bump (the canonical payload now encodes the actor as a JSON
object, not a bare string). Attribution is therefore part of the integrity chain: re-attributing an
op breaks `verifyChain` / `verifyDag` exactly as op-tampering does. The `Human` / `Agent` distinction
is the load-bearing accountability fact; an `Agent`'s `model` / `version` is a neutral attribution axis
a consumer may use for its own analytics or provenance reporting. A pre-320 bare-string stream migrates via `fromJsonlLegacyActor` + `rehash` under
`legacyActorConfig` (the Phase-255 migration seam) — see the migration doc. **Cross-host break:** the
hash pre-image is a wire contract; the TS / Python / Fuaran.UI hosts must adopt the same typed-actor
object + field ordering in the same release or chains diverge (the Fuaran.UI op-record fold is
coordinated with the Phase-319 wire-versioning work).

An optional **`IAttestationSink`** signs chain checkpoints — the head at a commit / publish boundary
(O(commits), not O(ops); the hash-chain attests the whole prefix). The default `noAttestation` is a
no-op, so signing is opt-in and the un-attested path is unchanged; an enterprise host plugs in
KMS / HSM signing behind the seam (Core stays FSharp.Core-only + Fable-clean — the crypto lives
host-side). **Replay-as-provenance:** a state is provably the deterministic replay of its attested
op log / op-DAG, and a signed head + replay is independently re-verifiable (integrity → attestation →
deterministic replay), falsified by any op- or actor-tamper.

## Attributed-stream lift — "who did what" without a witness seam (Phase 81)

`OpStream.Attributed.liftWitness : StreamWitness<'Op,…> -> StreamWitness<Attributed<'Op>,…>` gives every
appended op actor / session / turn / timestamp provenance for agent-fleet accountability — as a **derived
lift over the existing three-seam `StreamWitness`, not a new witness field.** This is deliberate: the
per-op witness-metadata seam (adoption fork **F8**) was rejected and **stays rejected** — the lift
*wraps*, it does not seam. `Attributed<'Op>` is a plain envelope record (`Actor` / `Session` opaque host
strings, optional `Turn`, `At` timestamp, wrapped `Op`); the lift's `Apply` delegates to the inner reducer
on `.Op` (attribution is provenance, never state), and `Encode`/`Decode` wrap the inner op codec in a
camelCase `{"actor":…,"session":…,"turn":…,"at":…,"op":<inner>}` envelope.

- **The chain covers attribution for free.** The envelope rides *inside* the chained op's wire encoding,
  which the hash chain already folds over — so re-attributing a chained op breaks `verifyChain` exactly as
  op-tampering does, with **no change to the `OpStream` surface** and `verifyChain` unchanged.
- **Timestamp-as-data (Phase 27 discipline).** `At` is a host-supplied string carried as data — **Core
  never reads a clock**; `""` means unstamped.
- **Identity is host-side vocabulary.** `Actor` / `Session` are opaque strings — Core owns no identity
  model and stores exactly what the host supplies. This is a **distinct axis** from the chain-level typed
  `Actor` DU folded into `OpRecord` (Phase 320): that names the *appender* in the chain payload; the
  `Attributed` envelope carries per-op session/turn provenance *inside the op* via the lift. Both end up
  hash-covered.
- **Additive + Fable-clean (GP2/GP3).** No new witness field; unattributed streams and their codecs are
  byte-unchanged (the inner op is embedded verbatim). Encode is hand-rolled canonical JSON, decode reuses
  the self-contained JSONL scanner, so encode AND decode are Fable-clean. `Conformance.attributedLaws`
  certifies replay-preservation, chain-covers-attribution (tamper), and envelope round-trip; `byActor` /
  `bySession` are pure projection folds.

**Attested provenance is now a conformance-certified claim (Phase 60).** `Conformance.attestationLaws`
is a seed-replayable law kit a domain (or host) runs against its `StreamWitness` + sink to prove the
three-stage guarantee end-to-end: **checkpoint round-trip** (a signed head verifies against its chain),
**prefix attestation** (one signature is bound to the whole prefix its head folds — O(commits)),
**replay-equivalence** (the attested log replays to exactly the state the head was taken over), and
**falsification** — an op-tamper AND an actor-re-attribution, *each rehashed so `verifyChain`
re-accepts the forged chain*, are still caught by `verifyAttestation` (the head moved; the signature
covers only the original). That last property is precisely what attestation adds over a bare hash
chain, and it holds under a cryptographic `HashFn` too (a re-hashed forgery cannot be re-signed without
the host key). The `noAttestation` default satisfies the kit **vacuously**
(`Conformance.noAttestationVacuityLaws`): `Sign ⇒ None`, `Verify ⇒ false`, and the chain is unchanged —
so adopting the seam never forces a sink on a host. The crypto stays host-side (GP3); the kit
self-proves green in-repo against the reference witness with a keyed FNV/HMAC-style stand-in sink.

## Hash-chain integrity posture

The op-stream (`OpRecord`) and op-DAG (`DagNode`) chains are **tamper-evident against accidental
corruption**, not against a motivated adversary, *with the default hash*. Since Phase 320 the typed
actor is inside the hash, so attribution tampering is detected on the same footing as op tampering. `OpStream.defaultHash` is
FNV-1a — fast, portable, Fable-clean, and **not cryptographic**: anyone who edits a record can
recompute the whole chain. `verifyChain` / `verifyDag` detect reordering, a dropped link, or a bit
flip; they do **not** detect a re-hashed forgery under FNV-1a. For adversarial tamper-evidence,
supply a cryptographic `HashFn` (e.g. SHA-256) at the host boundary — the `HashFn` seam is pluggable
for exactly this, and cross-host parity holds as long as both hosts use the same one.

**The supply-your-own-crypto contract is conformance-certified (Phase 65).** `Conformance.hashFnLaws`
certifies, over a seed-replayable sample under the SUPPLIED `HashFn`, that a chain hash is a pure
function of the canonical wire pre-image: **determinism** (the same op sequence hashes identically
across builds), **pre-image parity** (an incremental `append` build and a bulk reforge of the same
`(seq, actor, op)` pre-images agree hash-for-hash — the cross-host-parity foundation: two hosts on the
same `HashFn` + same pre-image get byte-identical chains), and **tamper-detection** (reorder / drop /
bit-flip caught by `verifyChain`). Until Phase 302 this read "under *any* supplied `HashFn`", and a
constant `HashFn` passed: those three tampers are caught before the hash is consulted. Since Phase 302
the family also builds the three arms that need the hash itself — an op replaced with its seq, prev and
stored hash kept, the forgery re-minted end to end (the head must move), and two envelopes differing
only in their op under one prev hash (they must hash apart) — so a `HashFn` that cannot tell ops apart
is RED, and the family is not a certificate for any `HashFn` whatever. The crypto posture itself is pinned by
`Conformance.hashFnAdversarialLaws`: a collision-resistant `HashFn` admits **no** pre-image collision
within the search budget (a re-hashed forgery cannot land a chosen head, so it is caught), whereas the
32-bit default FNV-1a **does** — the documented forgery primitive, asserted so a silent widening of the
default would be flagged as a posture regression. The adversarial branch uses a wide, test-side
stand-in that models collision resistance in-budget, so the law certifies the *contract* rather than
any particular digest.

**A cryptographic digest now ships in Core (`0.5.0`), and the default is deliberately unchanged.**
`Hash.sha256Hex` / `Hash.sha256Bytes` are a pinned pure FIPS 180-4 SHA-256 — FSharp.Core-only and
Fable-clean, so a digest taken by a server verifies in a browser. What moved is availability, not
posture: `OpStream.defaultHash` is still FNV-1a, because changing it would silently invalidate every
persisted chain in every domain, and a host that wants adversarial tamper-evidence still supplies
`Hash.sha256Hex` through the `HashFn` seam as an explicit act. The two are separately named for that
reason — a **cache fingerprint** (`fnv1a`: staleness stamps, bounded-escape content hashes, where
nobody gains by forging) and a **crypto digest** (`sha256*`: anything that becomes a signed head or a
record a dispute is read from), never interchanged.

**BOTH hashes are now certified .NET/Fable value-identical, and both were measured rather than
assumed (`sha256*` at `0.5.0`, `fnv1a` at `0.6.0`).** Compiling the two pipelines against the same
corpus — including the one-million-`a` vector — gives byte-identical SHA-256 on both, which is what
the masked add in its inner loop exists for. **`fnv1a` did not agree until `0.6.0`**, and the way
that was found is the part worth keeping: the `0.5.0` probe measured it as a by-product and reported
`fnv1a "a"` as `e40c292c` on .NET but `e40c2930` under Fable, because `h * 16777619u` transpiled to a
plain JS multiply whose product passes 2^53 — precision lost inside the operation, where a trailing
mask cannot reach it. `0.6.0` routes the multiply through a split-half 32-bit form that never builds
a product above 2^32, and a re-run of the same probe over a 124-entry corpus is byte-identical on
both pipelines.

**The .NET values did not move, and that was the constraint the fix was designed around** — the .NET
side is canonical, and `fnv1a` is folded into every stored content hash, so a change here would
invalidate persisted data rather than break a compile. The pinned vectors hold it to that
byte-for-byte. What moved is the transpiled side, which now agrees with .NET instead of disagreeing
with it. **A `fnv1a` value minted by a JavaScript host before `0.6.0` will not re-verify** — see the
migration note below.

**The Fable-compile gate cannot catch this class, and that has not changed** — it proves a construct
transpiles, not that it computes the same number, so nothing in the repo had reason to report the
divergence for as long as it existed. Two guards replace it, and neither is a compile: the `fnv1a`
vectors and an independent 64-bit reference comparison in `HashTests` pin the .NET half, and the
cross-pipeline half is the value leg ("The value leg" under Fable cleanliness), which runs
`ParityVectors` on both pipelines and byte-compares. Since Phase 217 that table carries the
124-entry corpus of the retired by-hand probe as its `hashSweep/*` rows — one column each for the
canonical `fnv1a`, `sha256Hex`, and the two deliberate copies in `OpStream` and `Column` — so one run
says both that a divergence exists and how far it reaches. Both guards were taken go-red before
being trusted: reintroducing the naive multiply was measured to leave the suite fully green while
120 of the 124 entries diverged. **Anyone touching either multiply-safe helper (`mul32`, `.+.`) runs
the value leg against a candidate pack** — `fuaran-dotnet`'s
`tests/core-fable/core-fable.ps1 -CoreVersion <v> -CoreFeed <folder>` — because a green .NET suite
is not evidence about the other pipeline, and nothing in THIS repository runs the transpiled side.

**Migration (`0.6.0`).** No action is needed for a value minted on .NET: those are unchanged, so
every persisted chain, content hash and staleness stamp written by a .NET process re-verifies
exactly as before. A value minted by a **JavaScript/Fable host** before `0.6.0` was never the true
FNV-1a of its input and will not re-verify under `0.6.0`; such a value must be re-minted from its
source data rather than carried across. Note that the pre-`0.6.0` guidance in this file was that
such a value must not be compared across pipelines at all, so it was never usable as a portable
identity — which is what makes re-minting a correction rather than a loss.

This reverses the earlier "no cryptographic hash ships in Core (GP3)" line, and the reason is worth
stating: GP3 asks that public surfaces stay FSharp.Core-only and Fable-clean, which this
implementation is — it is pure `uint32` arithmetic and compiles under the Fable gate like everything
else. What GP3 rules out is a *host-side* crypto dependency (`System.Security.Cryptography`, a keyed
MAC, a signer), and none of that is here. The previous line conflated "no crypto dependency" with "no
cryptographic algorithm", and the cost of that conflation was two domains each hand-porting the same
primitive, which is a divergence waiting for a patch that reaches one of them. Keys, signing and
attestation remain host-side behind the `IAttestationSink` seam, exactly as before.

Integrity is also **opt-in on load**: `OpStream.fromJsonl` / `Dag.fromJsonl` decode *structurally*
and do not verify — a tampered, dangling-parent, or cyclic input decodes to a clean `Ok`. Use
`fromJsonlVerified` (Phase 13) to gate the load on `verifyChain` / `verifyDag`, or call the verifier
explicitly before trusting a decoded stream/DAG.

**Compaction reads the boundary, it does not check it — verify, then compact (Phase 227).**
`OpStream.compact` / `compactChainOnly` (and their `...With` forms) read the boundary record's hash
and TRUST it. The compacted stream verifies exactly when the original does only over a prefix that
was verified BEFORE it was discarded (`compact_preserves_verify` / `compact_verifies_iff_original`,
`proofs/Chain.fst`). A tamper in the prefix of an unverified stream survives compaction, verifies
across, and cannot be found once the prefix is gone. A host that compacts an unverified stream has
compacted whatever it was handed, so run `verifyChain` / `verifyChainWith cfg` first.

**A snapshot at sequence zero carries the configured genesis (Phase 227).** The boundary hash at zero
is the genesis every chain walker starts from. `snapshotAtOptWith cfg` / `compactWith cfg` /
`compactChainOnlyWith cfg` write `cfg.Genesis` there. The canonical entry points (`snapshotAt`,
`snapshotAtOpt`, `snapshotAtChainOnly`, `compact`, `compactChainOnly`) write `""`, which is
`canonicalConfig.Genesis` and `legacyActorConfig.Genesis`. So under both shipped configs every
snapshot byte is unchanged. A stream appended under a config with any other genesis must compact
through the `...With` form under that config. The canonical form's snapshot at zero does not verify
across such a stream.

### Chain pre-image portability

The chain pre-image is pluggable via `StreamConfig.Payload` (`int -> Actor -> string -> string`,
Phase 255) so a domain on a legacy chain format can verify its persisted streams and `rehash` to the
canonical form without a flag-day. That flexibility is a **migration seam, not an interchange
format**: a stream hash-chained under a bespoke `Payload` verifies only for a reader who knows that
config, so it is **not** portable across independently built hosts. **Cross-host conformance is
therefore pinned to `OpStream.canonicalConfig`** (the `{seq, actor, op}` envelope + `""` genesis): a
stream claiming the canonical `core@1.0` chain profile MUST verify under `canonicalConfig`, and a
bespoke `StreamConfig.Payload` is a **host-private profile, non-portable by declaration** — sanctioned
only as the transient input side of the `verifyChainWith <legacy>` → `rehash <legacy> canonicalConfig`
migration. This is a **definitional** boundary: `Conformance.streamLaws` already certifies
append / verify / replay over the default `canonicalConfig` pre-image, so no separate portability law
is added — "the canonical profile is verifiable under `canonicalConfig`" is what `streamLaws` proves,
and the non-portability of a bespoke pre-image is a declared contract, not a testable property. (The
decision is recorded in [`DECISIONS.md`](DECISIONS.md) D11.)

## Deterministic replay posture (Phase 27, `0.0.1-alpha.8`)

The hash chain proves a stream's *shape* was not altered, but plain `replay` re-derives state by
re-applying ops — so a non-deterministic effect evaluated **outside** the op sequence (a clock read,
an RNG draw, a network / tool response) is *not* reproducible from the file: re-running it re-reads
the live source. The **determinism-capture seam** closes that gap. `OpStream.captureEffect` records
the realized value at the boundary (keyed on the `Fuaran.Core.Function` determinism tag —
`Effect.determinismTag`); `replayEffect` feeds the recorded value back instead of re-evaluating; the
capture is hash-chained (`verifyCaptures` / `firstCaptureBreak`), so a tampered captured value is
detected exactly like a tampered op. A `Deterministic` effect emits no capture and replays unchanged.
With captures present, a recorded clock/random/network session **replays byte-identically**, upgrading
the op-stream from *structural* tamper-evidence to *full deterministic behavioral replay*.

**Opaque determinism-label rule.** `EffectCapture.Determinism` is an **open label space**. The core
interprets exactly one value — `"deterministic"`, the label of an effect that emits no capture. Every
**other** label (`"clock"` / `"random"` / `"network"`, the composite `"clock+random"` spellings of a factor set, and any host- or domain-minted label) is
**opaque and reserved to the effect supplier**: the core carries it verbatim through the hash-chained
journal and keys replay ordering on the `Eff` identity, never on interpreting the label. A conformant
host MUST carry a non-`"deterministic"` label without interpretation — inventing or re-meaning one is
a host/domain concern, not a wire change. This is the open-label counterpart of the `IdWitness`
posture: identity/label vocabulary is supplied, not fixed, and the substrate stays FSharp.Core-only by
never depending on the `Fuaran.Core.Function` `Determinism` set.

**The guarantee is *outcome*-faithful, not *trajectory*-faithful.** Replay reproduces the values the
boundary *returned*; it does not reproduce the internal path a stateful effect took to produce them.
An effect that reads non-determinism internally (a seeded RNG whose individual draws are not each
captured) replays its *outcome* from the journal, but its internal trajectory only matches if the
consumer reseeds from the captured seed — `OpStream.capturedSeed` exposes the recorded seed precisely
so that trajectory replay is automatic rather than left to discipline. Capture is also **opt-in**: a
legacy stream with no journal (or an exhausted journal) falls back to live evaluation, so replay is
only as reproducible as the effects a host chose to capture. The capture chain shares `OpStream`'s
non-cryptographic-by-default posture — supply a cryptographic `HashFn` for adversarial tamper-evidence.

**Replay consumes captures in record order (Phase 40, `0.0.1-alpha.13`).** `replayEffect` takes the
requesting effect identity and asserts the head capture's `Eff` matches it; a cross-identity misorder
is a *named error*, not a silently-mispaired value. A driver must therefore replay effects in the same
identity order it recorded them. This is a signature change (`replayEffect decode eff det effect captures`).

## verifyFunction guarantee scope (Phase 52)

`Conformance.verifyFunction` (Phase 48) certifies that an artifact-function emits a
**validator-conformant tree for every binding in the sampled / symbolic param space** — *structural
validity over the param space*, plus the Fork-3 effect cross-check (the result observes no effect the
declaration doesn't cover). It is **scoped deliberately and must not be read more widely:**

- It does **NOT** certify that the output is *semantically good* — only that it is structurally valid
  (validator-clean) for each binding.
- For a function whose effect class is **non-deterministic** (`Clock` / `Random` / `Network` — a
  stochastic spec, e.g. an MMM model or a future `Fuaran.Model` dialect), the verdict asserts
  **structural validity only**, never output determinism or quality. A structurally-valid but
  value-varying output still verifies.
- The verdict is **effect-class-agnostic** — the determinism axis does not change it; verify keys on
  structure, not on the effect class. The `Conformance.verifyHonestyLaws` guard law proves this.

Advertising "verified" as a quality or determinism guarantee is an **over-claim** the statistical
domains must not make. This is a contract/scope clarification, not a behaviour change: the shipped
`verifyFunction` is unchanged.

## Memo soundness preconditions (Phase 53 / 56)

`Function.applyMemo` is sound only under two preconditions, now both enforced or certifiable:

- **Effect honesty (Phase 53, enforced).** Memoisation gates on the **observed** effect
  (`Function.observedEffect` — the widest effect walked over the whole subtree), not the declared root.
  A function whose root declares `Pure`/`Deterministic` while a descendant leaks `Clock`/`Random`/
  `ReadsHost` is bypassed (never cached/served), so the cache cannot serve a stale result for an
  actually-impure function even when the root under-declares. `Conformance.memoSoundnessLaws` proves it.
- **Encoder injectivity (Phase 56, caller precondition + certifiable).** The cache key is
  `Tree.encodeHash w.Tree encode node`; the caller-supplied `encode` MUST be injective over the node
  space, or two distinct trees collide and the cache serves the wrong one. Core cannot enforce this for
  an arbitrary host encoder, so it is a documented precondition (`applyMemo` / `Tree.encodeHash`
  doc-comments) that a domain certifies with `Conformance.encoderInjectivityLaws` (the
  "certify-your-codec" posture, like `Corpus.codecLaws`).

## Op-script footprint + independence (Phase 78)

`Ops.footprint : NodeWitness -> IdWitness -> SkeletonOp list -> Footprint` computes an op-script's
read/write **footprint** — the node ids / structural positions it reads and writes — as a pure, total
derivation *from the script* (the `paramsOf` precedent, Phase 77), and `Ops.independent : Footprint ->
Footprint -> bool` is pairwise footprint disjointness. This is the structural basis for dispatch-time
conflict refusal, computed leases (Phase 84), and proposal arbitration (Phase 85) — the coordination
edge is *computed from the script*, never separately declared. Additive over `Ops.fs`: **no new witness
field** (GP2), FSharp.Core-only + Fable-clean (GP3), no failure case (GP4 — a footprint is always a
value). `Conformance.footprintLaws` certifies soundness (an independent pair commutes under `apply`,
content-hash equal), monotonicity (a sub-script's footprint ⊆ its script's), and determinism.

**Conservativity is the contract — it is over-approximating by design.** `footprint` records *more*
potential collisions than a tree-aware analysis would, so `Ops.independent = true` is a **promise** (the
two scripts provably commute), and `independent = false` is **always a safe answer, never a defect
report**. A host may treat `false` as "serialise these two edits" without it implying either is wrong.

The **pinned over-approximations** (enumerated here and in the `Footprint` / `footprint` / `independent`
doc-comments), each a place where an exact set is a *tree fact the pure script cannot name*:

- **A `RemoveNode` / `MoveNode` has an unknown source parent.** Removing or moving a node also rewrites
  its *source* parent's child-list, but that parent — and every ancestor relationship — is a tree fact
  the op does not carry. So such a target lands in `Footprint.UnknownParentWrites`, and an op that
  removes/moves conservatively conflicts with **every** structural write in a concurrent script.
  Disjoint-subtree independence is therefore **not proven** when either side removes or moves (proving
  it needs the tree); a remove/move is independent only of a structure-free script. This is why two
  agents each editing a *different* subtree are still reported dependent if either deletes or relocates a
  node — safe, deliberately coarse.
- **A `RemoveNode`'s content-write records only the target id, not its (tree-unknown) subtree.** Sound
  for the skeleton five because *every* skeleton op is a structural write, so the unknown-parent rule
  above already serialises a remove/move against any concurrent structural op. A domain that layers a
  **pure in-place content-edit op** (an "update a node's payload" op, which the skeleton five have none
  of) on top must fold the removed subtree into the footprint itself — it has the tree.
- **The same-named-parent rule is pinned:** two ops that write the *same named* parent's child-list
  (two positional inserts, an insert + a reorder, …) are **not** independent — they shift the same
  siblings. Two structural writes on *distinct named* parents (with disjoint content) are independent.

The `Footprint` record's four address sets (`Reads`, `StructureWrites`, `ContentWrites`,
`UnknownParentWrites`) are keyed by the `IdWitness.ToString` string form (no `comparison` demanded of
`'Id`) and grow additively like the rest of the pre-1.0 surface.

## Branch merge: conflict enumeration + reconciliation (Phases 64, 83)

The op-DAG (`Fuaran.Core.OpStream.Dag`) exists for branch/merge: [Phase 08](.) gives the merge-base +
branch-delta of two heads, [Phase 26](.) projects a delta as an applyable `'Op` sequence. Two pure,
generic functions close the merge — and the **GP6 line runs straight through them**: Core *detects and
folds what provably commutes*; the domain *resolves* everything else. Neither picks a winner or applies
a policy.

- **`Dag.conflicts : ('Op -> Footprint) -> 'Op list -> 'Op list -> MergeConflict<'Op> list`** — the
  DETECTION half. Given two branch deltas from a common base and a caller-supplied address projection
  (`'Op -> Footprint`; the domain feeds `Ops.footprint` over its own witnesses, since the DAG is
  generic over the opaque `'Op`), it enumerates every op pair across the two deltas that targets the
  same address and would interfere — a typed `MergeConflict` per collision (`Left`, `Right`, the shared
  `Address`, and the `Shape`). It is the **`canApply` of merging**: it decides nothing, applies nothing,
  picks no winner. `MergeConflictShape` is a **closed DU** (GP5) — `ConcurrentUpdate` (both branches
  write one node's content, or one writes what the other reads), `InsertPositionClash` (a shared *named*
  structural parent), `MoveVsRemove` (a remove/move whose unknown source parent races the other side's
  structural write). No witness field (GP2); FSharp.Core-only, Fable-clean (GP3).

- **`Dag.reconcile : ('Op -> Footprint) -> Dag.T<'Op> -> string -> string -> string -> Result<'Op list, MergeConflict<'Op> list>`**
  — the mechanical FOLD half. Non-conflicting deltas ⇒ `Ok` the merge script
  (`betweenOps base headA ++ betweenOps base headB`, one applyable sequence); any conflict ⇒ `Error`
  `Dag.conflicts`' report verbatim, **nothing applied** — no partial merge. It is to merging what
  `apply` is to a `canApply`-clean op. The pinned composition order is **canonical form, not semantics**:
  a conflict-free pair commutes, so `A ++ B` and `B ++ A` replay to content-hash-equal state; pinning
  delta-A-then-delta-B makes the output a pure function of `(base, headA, headB)`.

**Conflict detection inherits #78's conservativity exactly.** `conflicts` fires on a pair *iff*
`Ops.independent` would reject it — the three shapes partition the negation of the independence
predicate over the four `Footprint` address kinds. So `conflicts = []` carries the same **promise**
`Ops.independent = true` does: the two deltas provably commute — never a false "clean merge". A
remove/move is therefore reported against *any* concurrent structural write (its source parent is a
tree fact the pure script cannot name — the pinned `UnknownParentWrites` over-approximation), tagged
`MoveVsRemove` and keyed by the removed/moved id. This makes `Dag.reconcile`'s **footprint
cross-validation** hold by construction: `Ops.independent (footprint deltaA) (footprint deltaB) ⇒
reconcile = Ok` (`Conformance.reconcileLaws`). The converse is *not* claimed — footprints
over-approximate, so `conflicts = []` means footprint-independent (and, via `footprintLaws`' certified
soundness, genuinely commuting), not tree-aware-minimal.

Certified by `Conformance.mergeConflictLaws` (symmetry up to `Left`/`Right` swap; determinism;
agreement with #78 — a pair is reported iff its footprints are not independent) and
`Conformance.reconcileLaws` (clean-fold order-independent replay by content hash; #78 cross-validation;
the conflicted path returns `conflicts`' report with nothing applied; determinism + order pinning).
Both are append-only additions to the conformance kit; a domain that reconciles branches runs them
alongside its base certification. `Dag.conflicts` / `Dag.reconcile` add a build-time dependency from
`Fuaran.Core.OpStream.Dag` onto `Fuaran.Core.Ops` (for the `Footprint` type); additive, no cycle.

## Confluence / interleaving law (Phase 80)

`Conformance.concurrencyLaws` certifies the coordination claim the agent-fleet substrate rests on:
**op-scripts `Ops.independent` declares disjoint replay to the same tree under every interleaving of
their individual ops** — confluence. `footprintLaws` (Phase 78) proves the two whole-script sequential
orders commute; concurrent appenders produce arbitrary op-level interleavings, and this law samples
them: interleavings grow as C(m+n, m), so per independent pair it checks a **bounded, deterministic,
seed-replayable sample** (the two sequential extremes + 8 uniform riffles) for **interleaving
totality** (every sampled interleaving applies cleanly) and **content-hash-equal replay** (the
Phase-06 encoder hash). A **coverage vacuity guard** fails a run whose generator never produced an
independent pair, so a green report always means "certified over real pairs", never "nothing was
checked".

**What it deliberately does not certify: anything about dependent scripts.** Independence is
*sufficient* for confluence, not *necessary* — a pair `Ops.independent` does not declare independent
is **skipped, not asserted** (it may or may not commute; the law makes no claim about it). This is
the same posture as Phase 78's conservativity contract: `independent = false` stays "serialise
these two", never "one of these is wrong". The footprint function is injectable
(`concurrencyLawsWith`) purely as the teeth seam — the in-repo suite proves the law bites under a
falsely-independent footprint; domains run `concurrencyLaws` (pinned to the real `Ops.footprint`).

### Mechanised: the N-lane fold law is a theorem (Phase 131)

The Phase 100 fold-confluence law — the same lanes fold to the same state however they arrive,
a lane set that cannot fold halts with the same canonical report however it arrives, and no set
folds under one order and halts under another — is **proved** in `proofs/DagFold.fst` over an F\*
model of `Ops.independent` / `Dag.conflicts` / `Dag.reconcileMany` / the replay, for every lane
count and every permutation, under the domain's commutation promise for independent ops. The
model is extracted to F# and run by the suite beside production over the Phase 100 generators and
the wire corpus (`Proofs.Oracle`), and the proof leg holds the committed oracle to a fresh
extraction byte for byte. **No shipped surface changes**: the claim is about what the existing
`reconcileMany` does, stated as a theorem on a model and tied to production by a differential
test. The claims ladder — what is proved, what is only tested, what is assumed — is in
`proofs/README.md`, and "formally verified" is spent on the theorem alone.

## The IDL engine — two packages, two promises (Phase 97, `0.8.0`)

The IDL engine ships as **two** packages from `0.8.0`, and the split is by what each one
commits to rather than by size.

**`Fuaran.Core.Idl` — the model half. What it promises:**

- **The model** — `IdlType`, `IdlValue`, `Idl`, `IdlKind`, `IdlField`, `IdlUnion`, `IdlEnum`,
  `IdlRecord`, `IdlDefault`, `Optionality`, `Annotations`, `Deprecation` — plus the `Declare`
  helpers.
- **The codec** — `Encode.encode` / `encodeOp` and `Decode.decode` / `decodeOp`, schema-driven
  from an `Idl`. Both return `Result<_, string>`: a vocabulary the codec cannot honour is a
  named failure, never an exception. The bytes are canonical because the codec builds a `JVal`
  and renders it through `Canon` — the canonical number / key / escape rules are **inherited**
  from `Fuaran.Core.Wire`, not re-implemented, so a change to them is a `Canon` event and is
  governed by "Canonical float layout" and "Digest changes" below, not here.
- **The sampler** — `Sample.sampleNodes`, deterministic from `(seed, index)` alone. Its LCG is
  explicit rather than `System.Random` precisely so a vector that fails on another host
  reproduces here from its seed. **The sampled sequence for a given `(idl, tags, seed, count)`
  is part of the contract**: a change to the draw order is a wire-visible change for anyone
  storing vectors, and is treated as breaking even though no signature moves.
- **`Sanitize`** — the host-neutral URL / attribute / markdown floor. Its **behaviour** is the
  contract, not its signatures: it is a security floor, and a change that admits something it
  previously rejected is breaking regardless of what compiles. Phase 96 is the standing lesson
  — a lift that dropped two behaviours, both failing open, survived because the claim was
  written in a comment rather than pinned by a test.
- **`Artifact.render` / `Artifact.parse`** — the canonical `idl.json` projection and its inverse,
  with the ordering contract stated at the module and available as `Artifact.canonicalise`.
  `Artifact.version` pins the ENCODING; a consumer pins that, not the contents. The law is
  `parse (render idl) = canonicalise idl`, pinned over every vocabulary the suite declares —
  see "The artifact reads back" below.
- **Fable-cleanliness**, gated rather than asserted: the Core Fable gate (`fuaran-dotnet`'s
  `tests/core-fable/`, since Phase 217) compiles the whole of this package, so every one of the
  above reaches a browser. That is the reason the split exists — see "Fable cleanliness" below. No
  Fable consumer compiles this package today (measured 2026-09-24); the gate references it for no
  reason but to keep the claim checked, and that was decided rather than drifted into (DECISIONS.md
  D55).

**`Fuaran.Core.Idl.Codegen` — the generation half.** `Gen` (the F# structural-layer emitter and
its declared-support channel, the TypeScript encoder backend, the JSON-schema emitter, the
scaffold writer), `CodegenError`, `SupportArtifact` (the declared-support record as a canonical
data document), `Trust` (the codegen trust boundary) and `Diff` (the stability classifier over
two `idl.json` revisions). **.NET-only and build-time only**: it ships no Fable
source, because `StringBuilder` and `CultureInfo.InvariantCulture` serve the TypeScript backend
and a portability it cannot keep should not be offered.

**Its real contract is the shape of what it EMITS, and that is deliberately weaker than an API
promise.** A consumer compiles and ships generated source, so a change to the emitted prelude is
a downstream source change for everyone — which is a harder thing to version than a signature.
The posture, pre-1.0: **the emitted shape may move on a minor**, and a phase that moves it says
so in its outcome and in the migration note for the generated layer. A consumer who cannot
absorb that pins the package rather than tracking it. `Fuaran.Core.Idl.Spike` is the standing
proof the emitters produce valid F#: it compiles the generated module against the **model half
alone**, so generated source that needed the generator present would fail the build.

**The open-DU consequence, accepted knowingly rather than designed around.** `IdlType` is an
open DU that has gained `TClosure`, `TOpaque`, `TJson`, `TRecord`, `TMap`, `TFn` and `THosted`,
several of them recently, and `IdlValue` tracks it. **Every future case is a breaking change for
a consumer matching exhaustively**, and more cases are expected: the engine grows a case each
time a domain declares a slot shape it cannot yet express. Hiding the DU behind constructors
would buy source-compatibility for a consumer set that is currently two, at the cost of the
exhaustiveness that makes a vocabulary total — the same trade "The load-bearing invariant"
refuses at the top of this document. So the DU stays open in both senses, the pre-1.0 status at
the head of this document applies with full force here, and a consumer that matches `IdlType`
exhaustively should expect to revisit that match on a minor bump.

**A vocabulary is not distributed from either package.** See DECISIONS.md D14: the `Idl` value
describing a domain's kinds is data the domain owns in its own repo. There will be no
`Fuaran.Core.Idl.Vocabularies.*`.

**One known wart, stated rather than hidden — now closed; kept because the reason it was
public still holds.** `TransparentUnion.tag` used to key bare-value encoding on a hard-coded
vocabulary name (`TextSource`) inside an otherwise domain-generic engine. Phase 97 made the
accessor public because the split made the dependency real — an independent emitter must agree
with this codec about which cases are bare, or it generates a host that disagrees on the wire —
and that is still why it is public. What changed is where the answer comes from.

**The wart above is CLOSED (Phase 116, `0.18.0`) — the hardening vocabulary is a seam a
domain supplies.** `Idl` carries a `Harden: HardenPolicy`: the kind the codegen trust
boundary GATES, the placeholder kind (and its field) a gated-out node becomes, the literal
TEXT case and field the markdown scrub matches, the literal VALUE case and field the URL
sanitiser matches, and — the transparency rule this section named as the wart — which unions
have a bare-encoded case, as `(unionName, caseTag)` pairs. `TransparentUnion.tag` now takes
that policy instead of testing a hard-coded name, and `Trust.harden` takes the `Idl` beside
the caller's trust decisions.

**`HardenPolicy.Default` is exactly the set the engine hard-coded**, so a vocabulary that
declares nothing behaves byte-for-byte as it did in both directions, and the artifact omits
the block at the default — every `idl.json` written before this release is byte-identical and
reads back as the same vocabulary. That is deliberate rather than incidental: the hard-coding
became a DEFAULT rather than disappearing, which is what lets the seam land without a
migration for anyone.

**What did NOT move onto the `Idl`, and why the split is where it is.** `Trust.Policy`
(renamed from `Trust.HardenPolicy`, whose name this record took) still carries the caller's
side: the `Custom`-gate allowlist, and which `(kind, field)` pairs carry a URL or markdown.
Two different reasons. The allowlist is deployment trust state — module ids and content
hashes — and the `Idl` is projected into `idl.json`, so carrying it there would publish it as
though it were vocabulary. And the two field sets are a security floor whose empty value is
silent: a vocabulary migrating by writing the default would stop sanitising, and nothing would
say so. Those sets were never hard-coded, so moving them would have closed no leak while
opening that one.

**One member is wire-visible and the rest are not**, which is the distinction a reader of
`idl.json` needs. `transparentUnions` moves the bytes of every document using a transparent
case, and the artifact keeps surfacing the derived `transparentCase` per union for exactly
that reason — the stability classifier still reports the effect as `UnionTransparencyChanged`
(`BreakingWire`), unchanged. The remaining members are codegen-boundary spec: they change what
`Trust.scaffoldFSharp` EMITS and nothing a decoder reads, and a move is reported as
`HardenPolicyChanged` (`HostSurfaceOnly`), whose remedy is to re-scaffold.

**Source-breaking, on the pre-1.0 posture at the head of this document.** `Idl` gains a
required field, so every record-literal construction adds `Harden = HardenPolicy.Default`;
`Trust.HardenPolicy` is `Trust.Policy`; `Trust.harden` takes the `Idl` first; and
`TransparentUnion.tag` takes a policy. `Trust.scaffoldFSharp`'s signature is unchanged — it
already had the `Idl`, and now reads the tokens from it.

### Declared defaults: what the generator renders, and what it refuses (Phase 124, `0.21.0`)

An `OmitDefault d` says the encoder emits a field only when it differs from `d` and the decoder
restores `d` on absence. Rendering `d` as source in each target language is what makes that true
of the GENERATED layer, and until this release the generator could render only a scalar, an enum
case, the empty list and a **nullary** union case. The newly expressible class is the
**value-carrying union case** — `Slot.Fixed(0.0)`, `Binding.Static(Some 0)` — with its payload
nested to any depth through further unions, records and scalars.

**What decides the admissible set is a constraint that is easy to miss.** The emitted string is
used in an EXPRESSION position (the decoder's `dDef` restore, a smart constructor's filled field)
*and* in a PATTERN position: the encoder's omit test for a union field is
`match s.X with | <lit> -> None | _ -> …`, never `=`, because Phase 691 established that a union
whose fields reach a closure supports no equality at all. Constants, a union-case application over
constants, `Some`/`None`, a record literal and `[]` are legal in both. A **non-empty** list is
therefore still refused even though it renders: the encoder's omit test for a list field is
`List.isEmpty`, which has no non-empty analogue, and equality may not compile at the element type.
A non-finite float is refused for the same class of reason — `nan` and `infinity` are F#
identifiers, not literals, so they have no pattern spelling. (The §5 wire sentinel Phase 1063 added
is a WIRE spelling and is unaffected; this is a host-source position.)

**`defaultExpr` and the omit-test literal are now ONE renderer.** They were two match expressions
over overlapping-but-different sets, so a smart constructor could fill a `TStr` default the encoder
could not test for. One contract, one renderer: the constructor, the encoder's omit test and the
decoder's restore cannot come apart.

**The half that is not additive: a declaration the generator cannot render REFUSES.** Before this
release the literal emitters answered `None`, and four of the six paths that consult them fell back
to **always-emit** (encoder) or **`dReq`** (decoder) — consistent with each other and with nothing
else, least of all the IDL that declared the slot omitted at its default. The artefact contradicted
its own declaration, and did so with a green build. Every such path is now a typed
`CodegenError.UnsupportedDefault` reported at generation time:

- a **kind-spec** field (already refused, via the smart-constructor leg);
- a **projected** kind's field — the projection suppresses the generated constructor, and that
  skip took the only default check with it while the encoder still emitted the field;
- a **union-case** field and a **record** field, neither of which passes through that leg;
- the **node envelope**, whose refusal was an untyped `failwithf` inside a function that already
  returned `Result`;
- and the **whole TypeScript backend**, which had no error case anywhere.

`Gen.fsharpValue`'s scaffold leg publishes a plain-string channel and so cannot carry the case, but
it renders that same case through `CodegenError.describe` rather than wording a refusal of its own.

**BREAKING, at compile time only: `Gen.typescriptModule` returns `Result<string, CodegenError>`.**
That is the refusal channel; a backend that emits source for a declaration it cannot honour is worse
than one that refuses. The fix at a call site is one `match`. Nothing else on either package's
surface moved.

**Every pre-existing emission is byte-identical**, which is what makes the widening safe to adopt:
no vocabulary in the certification set declared a value-carrying default before this phase, the
nullary-union, enum, boolean and empty-list renderings are character-for-character what they were,
and the TypeScript literal is now produced by the value emitter that already spelled a tagged object
exactly the same way. The reference vocabulary, both vendored foreign vocabularies and the committed
generated module pin it.

**One narrowing worth naming.** `VUnion(tag, [])` used to match ANY union, so a default authored
without its payload emitted the bare tag — which, for a case that takes arguments, is a FUNCTION
where a value belongs. The declared field list decides the arity now, and a payload-carrying case
spelled nullary is refused. A vocabulary relying on the old spelling was relying on a defect.

### Declared annotations on cases and fields (Phase 113, `0.18.0`)

`IdlUnionCase` and `IdlField` each carry an `Annotations` record — a **bounded** set of three
slots (`Deprecated` with an optional replacement and message, `InProcessOnly`, `Since`) saying
what is true ABOUT a member rather than about its shape. `Annotations.Empty` is the default and
means what every declaration written before this release means.

**The wire is untouched, and that is the load-bearing claim.** `Encode` and `Decode` never read
the record, so an annotated vocabulary's bytes are byte-for-byte its unannotated bytes in both
directions. The artifact omits an empty set entirely, so every pre-`0.18.0` `idl.json` is
byte-identical — the posture `ops` / `hostCases` / `wire` already take, and the reason
`Artifact.version` does **not** move.

**What DOES move is the generated declaration, and a consumer of the generator should know the
shape.** The F# backend emits a `///` block plus at most **one** warning-grade
`[<System.Obsolete(msg, false)>]` — one because `ObsoleteAttribute` is not `AllowMultiple`, and
`isError = false` because the generated layer must not decide for its host that touching a marked
member fails the build. FS0044 is a warning the host escalates (`--warnaserror:44`) or silences
(`--nowarn:44`) on its own schedule; an unconditional error would make the two-release retirement
this set exists to enable impossible to ship. A vocabulary that marks anything also gets
`#nowarn "44"` in the generated module, because that layer constructs and matches every declared
member including the marked ones — the warning is for CONSUMERS of the layer, never for the layer
itself. The TypeScript backend emits `//` line comments naming the member, at the case arm for a
case and on the owning function for a field: the emitted module is plain JS, where a field is an
inline entry in a one-line object literal and has no declaration to hang a JSDoc on, and a
`@deprecated` block above `encFooSpec` would tell tooling the encoder is deprecated, which is
false.

**In the stability classifier**, MARKING a member is `Additive` — nothing valid stops being valid
and no conformant emitter stops conforming — while moving or withdrawing a marking is
`HostSurfaceOnly`. Neither is ever a wire event. That split is what lets a vocabulary retire a
case across two releases without the marking itself costing a breaking bump.

**Source-breaking for a consumer that builds `IdlField` / `IdlUnionCase` by record literal**
(FS0764), which is the pre-1.0 posture at the head of this document applied as written; a `0.18.0`
minor carries it. `{ existing with Annotations = … }` and `Annotations.Empty` are the two shapes a
caller needs. On the codegen side the same release moves `Diff.Change` (two new cases —
`FieldAnnotationsChanged` and `UnionCaseAnnotationsChanged`) and `Diff.UnionSnap.Cases` (now
`Map<string, CaseSnap>`, so a case's own annotations have somewhere to live), both breaking for a
consumer that matches or destructures them exhaustively — the open-DU paragraph above, applied to
the classifier. It is deliberately NOT an `Idl`-level side table addressed by owner and member: that
shape can name a member the vocabulary no longer has, which is a defect class this one cannot
represent, and every emitter would have to thread the lookup rather than reading the member it is
already holding.

### Kind and enum-case annotations, and the declared retirement path (Phase 119, `0.18.0`)

`IdlKind` and `IdlEnum` now carry annotations too, on the same bounded set and the same terms as
Phase 113's cases and fields. `IdlKind` gains `Annotations`; because `Idl.Ops` is an `IdlKind list`,
a tree-op is annotatable by the same slot. `IdlEnum` gains `CaseAnnotations` — `(hostCase,
Annotations) list`, **sparse and keyed rather than positional** like `Wires`, because annotating one
case of a ten-case set must not cost nine empty entries, and because the parallel-arity invariant is
exactly what `Declare.enumWith` exists to make unstatable. Build it with `Declare.enumAnnotate`,
which refuses a case the enum does not declare; `Declare.enumWireErrors` is the backstop for a
record built by literal, and `IdlEnum.AnnotationsOf` is how the set is read.

**The wire is untouched, as before.** `Encode` and `Decode` read neither; the artifact omits an
empty set and omits `caseAnnotations` entirely when no case says anything, so every pre-`0.18.0`
`idl.json` is byte-identical and `Artifact.version` does not move. `caseAnnotations` is keyed on the
WIRE string, not the host case name: `cases` is the one key every revision carries, so a
third-party reader resolves an entry without consulting the conditional `hostCases`. `Artifact.parse`
reads both back, and **refuses** a `caseAnnotations` entry naming a case the enum does not declare
rather than dropping it silently.

**What the generated declaration gains.** F# puts a kind's doc block above its generated spec record
and the single `[<System.Obsolete(msg, false)>]` on the declaration — on its own line above `type`
for the group's first member, inline after the `and` for every other, which is the position
`[<RequireQualifiedAccess>]` already occupies there. An enum case takes the union-case placement:
doc block above the bar, attribute inline after it. TypeScript names the kind at its spec encoder
AND its spec decoder, and names an enum case as `<Enum>."<wire>"` above the enum's decoder — line
comments, for the reason Phase 113 records.

#### The retirement clause of the vocabulary-growth charter

The charter admits kinds; this is how one leaves, and it takes **two releases**:

1. **Mark.** The retiring kind (or enum case) gains `Deprecated`, naming a replacement where there
   is one. Nothing on the wire moves, every document still decodes, every conformant emitter stays
   conformant, and the stability classifier grades it `Additive`. A consumer of the generated F#
   sees FS0044 — a warning it escalates with `--warnaserror:44` or silences with `--nowarn:44` on
   its own schedule. **The marking release is never a breaking bump**, which is the whole point: if
   it were, no vocabulary could afford to announce a retirement before performing it.
2. **Remove.** The next release deletes the kind. THAT is the breaking event — `KindRemoved`,
   graded as the wire event it is — and it is priced where the cost actually falls.

Withdrawing a marking between the two (an un-retirement) is `HostSurfaceOnly`, not breaking: the
generated declaration moved and the wire did not. Skipping step 1 is not forbidden by anything
mechanical, and is what the clause exists to make unnecessary: a removal with no marking release
before it gives consumers no compiler-visible warning at any point.

**Source-breaking for a consumer that builds `IdlKind` or `IdlEnum` by record literal** (FS0764) —
the same pre-1.0 posture, and the same two shapes a caller needs (`Annotations.Empty`,
`CaseAnnotations = []`, or the `Declare` helpers, which is why they exist). On the codegen side
`Diff.Change` gains two cases (`KindAnnotationsChanged`, `EnumCaseAnnotationsChanged`),
`Diff.Snapshot` gains `KindAnnotations` / `OpAnnotations`, and `Diff.EnumSnap` gains
`CaseAnnotations` — breaking for a consumer that matches or destructures them exhaustively.

### The artifact reads back; the declaration triple (Phase 114, `0.18.0`)

`Artifact.parse` is the total inverse of `Artifact.render`, and `SupportArtifact.render` /
`.parse` do the same for the generator's declared-support record. Together with the host prelude
those three files are everything a regeneration needs, so a domain holding its own vocabulary
emits its structural layer against the packaged engine with no checkout of this repo present.
That was the missing half of DECISIONS.md D14: the engine has shipped since `0.4.0`, but a
vocabulary could only ever be an F# compile input, which is why the one domain using it reached
across a sibling checkout for a byte copy.

**Three additions to the promised surface, all additive.** `Artifact.parse` / `Artifact.ofJson`
(bytes or a parsed root to an `Idl`); `Artifact.canonicalise` (the ordering contract as a function
over the model, and now the single definition of it — `Artifact.json` applies it and no longer
sorts inline); `Artifact.renderJson` (the indented canonical layout over any `JVal`, so a sibling
document of a vocabulary lays out identically without a second stringifier appearing). On the
codegen side, `SupportArtifact` with `SupportDocument` and `HostPreludeRef`. **No emitted bytes
move**: every `idl.json` this engine writes is what it wrote before — pinned here by the
round-trip and canonicalisation laws over the neutral vocabularies, and by each consuming
domain's own regenerate-and-byte-compare guard over its committed artifact.

**The encoding version is now REFUSED rather than ignored.** An `idl.json` (or `support.json`)
declaring a version this engine does not read is an error naming both numbers. A newer encoder may
spell a member this reader would silently drop, and a vocabulary that loses a field quietly emits
a host that compiles and is wrong.

**Two things the artifact deliberately does not carry back.** A `closure` / `opaque` type's `wire`
key restates a sentinel the engine already knows, and a union's `transparentCase` is DERIVED from
the engine's hard-coded transparent set (the wart recorded above). Both are published for a
third-party reader and both are ignored on read, so a hand-edited artifact cannot redefine what
`<closure>` means or claim a transparency the engine does not implement.

**The host prelude is NAMED, not inlined.** `HostPreludeRef` carries a module name and a path
relative to the document. The prelude is F# source the domain already compiles and the generator
never reads it; copying its text into a JSON document would mint a second copy of a compiled
artefact with nothing keeping the two equal.

**One consequence for a domain taking its vocabulary home.** The artifact's ordering contract
Ordinal-sorts the top-level collections, so a module regenerated from bytes declares its kinds in
that order rather than in whatever order the vocabulary was authored in. The emission is otherwise
identical. It is a one-time reordering of a generated file, absorbed once — and it has been: the
first domain to take its vocabulary home absorbed it, and this repo's fixture was deleted
afterwards (Phase 123, and see the D14 amendment). The proofs that remain here are stated over
the neutral vocabularies; the full-scale instance of each is now the domain's own gate, which is
what "a vocabulary lives in its domain's repo" has to mean to be worth anything.

### The emitted artefact's line endings are the generator's (Phase 129, `0.22.0`)

**Promise: every emitter in `Fuaran.Core.Idl.Codegen` emits LF, whatever the generator was built
from and whatever its declared support carries.** `Gen.fsharpTypes`, `Gen.fsharpModuleWith` /
`Gen.fsharpModule` and `Gen.typescriptModule` normalise at their own boundary, so a regeneration is
reproducible across machines rather than across machines-that-share-a-checkout-style.

Two inputs could previously put a carriage return into an emission, and a consumer had no way to
see either. The generator's own multi-line `"""…"""` templates bake whatever line ending
`Codegen.fs` had on disk in the checkout that compiled it — the repository pins LF in the index, but
a formatter run used to rewrite the working copy to the platform's ending, so the same version of
the same package emitted CRLF when built on one machine and LF when built on another. And declared
support (`GenSupport` doc blocks and verbatim splices, annotation prose in the IDL) is authored data
that travels as a document written on any machine.

**Measured, in both directions.** Building `0.21.0`'s generator from an all-LF source tree and from
the same tree with `Codegen.fs` forced to CRLF produced two different emissions of the same
vocabulary (differing by exactly the carriage returns). With the normalisation in place both builds
emit the same bytes — and those bytes are byte-identical to `0.21.0`'s LF emission, so **no
committed generated artefact moves**: a consumer's regeneration guard sees nothing change.

**What the repository's own guards could not see, recorded because it is the reason this reached a
consumer first.** Both committed-artefact drift guards in the suite compare through a
whitespace-stripping normaliser, so an emission that differs only in line endings is EQUAL to them.
They are right to be insensitive — they exist to catch a real generator change, not reformatting —
but it means nothing in this repository was measuring the property. The new family asserts it
directly, and asserts the authored prose is PRESENT in its LF form, so a generator that dropped or
escaped the prose instead of normalising it fails rather than passing by emitting nothing.

**Two producer-side changes travel with it, and neither is on the API surface.** `.editorconfig`
pins `end_of_line = lf`, which is what Fantomas reads — `.gitattributes` governs the index and the
checkout, and demonstrably did not keep the working copy LF. And a test in the suite fails a working copy
that has drifted to CRLF, naming the files and the remedy, so the drift is caught where it happens
rather than in a package.

**What this does NOT claim.** A locally packed assembly is *not* byte-identical to the published one
for the same version, and no line-ending fix could make it so: the compiling SDK rolls forward to
the newest feature band available on the machine, the released packages are built on a different
operating system, and this repository sets none of the deterministic-build properties that would
pin source paths. The promise here is about the generator's OUTPUT, which is what a consumer
regenerates and compares; assembly-level reproducibility is a separate question and is not promised.

## Surface narrowing: the uncalled internals (`0.19.0`) — BREAKING

A sweep of all nineteen packable `Fuaran.Core.*` packages measured 613 public functions against
every F# source file in this repository and in every known consumer of these packages, and found 233
with no caller outside the package that defines them. `0.19.0` acts on the part of that set where
acting is safe and leaves the rest alone: **31 members become `internal`, 4 are deleted.**

**Narrowing `public` to `internal` is a BREAKING surface change**, labelled as one here rather than
filed as tidying: it removes a token from the published surface, and a consumer that was using it
stops compiling. **No caller outside the defining assembly was found for any of them** — qualified
and bare-name searches over every `.fs` / `.fsi` / `.fsx` file available, comment lines excluded,
including `tests/` and `tests/fable-smoke/`, plus a name scan of the other language hosts. That is a
measurement, not a guarantee: if you are the caller it could not see, say so and the member returns.

**Narrowed to `internal` (31):**

| Package | Members |
|---|---|
| `AiSurface` | `PatternBank.tryMatch` |
| `Conformance` | `FoldConfluence.renderLanes`, `IncrementalDelta.rowsTheLawsNeed` |
| `DataFrame` | `ColExprModule.paramNames`, `TransformModule.stepParamNames`, `SchemaWalk.noSources` (**public again at `0.22.0`** — see below), `Incremental.verbName`, `Incremental.classifyStep`, `Incremental.evalDelta` |
| `Function` | `Space.isBounded`, `Function.memoKey`, `CapabilityCodec.signatureJson`, `CapabilityCodec.encodeInvocationJson`, `CapabilityPipelineModule.nodeOutputType` |
| `Idl` | `Idl.Encode.encodeNodeEnv` |
| `Idl.Codegen` | `Idl.Diff.stabilityImpact`, `Idl.Diff.profileBump`, `Idl.Diff.rosterFrom`, `Idl.Gen.msgCarrying`, `Idl.Gen.typescriptValueWith`, `Idl.Trust.gateCustom` |
| `OpStream` | `OpStream.verifyAcrossWithOpt`, `OpStream.verifyAcrossChainOnlyWith` (**public again at `0.31.0`**, Phase 236 — see there) |
| `Query` | `QueryModule.cellType`, and the codec quartet `QueryCodec.queryJson` / `queryOf` / `resultJson` / `resultOf` |
| `Wire` | `Versioning.profileKey`, `Versioning.requiredProfileKey`, `Corpus.runCase` |

**The `QueryCodec` quartet moved as ONE decision, not four.** Narrowing half a symmetric
encode/decode surface is worse than narrowing neither — it leaves a consumer able to write a query
document it cannot read back. `Fuaran.Core.QueryCodec` has no consumer at all, so the whole codec
narrows together and would come back together.

**Deleted (4):** `Idl.Trust.uiPolicy`, `Incremental.evalDeltaOn`, `CellModule.shapeName`,
`ContentPack.baseVersions`. Each had **zero references of any kind** — not a call, not a test, not a
doc comment naming it. An `internal` binding nothing calls is still code to read, compile and
believe; deletion is the honest disposal, and the history is where it lives now.

**Members the sweep proposed and this release did NOT narrow, for three different reasons:**

- **`Conformance.snapshotLawsWith`** and **`FoldConfluence.laneFoldLawsWith`** — **enrolled by NAME
  in `SampleAdequacy.census`**, which is a shipped public declaration of every law family the kit
  ships. A string enrolment is not a call, so a caller-count reading cannot see it; the suite can,
  and did — `SampleAdequacy.census`'s completeness test resolves each row by reflection over the
  kit's PUBLIC law entry points, so narrowing these two turned two census rows into rows naming
  nothing. That is the guard working. The general rule it establishes: **a `*Laws` / `*LawsWith`
  entry point enrolled in the census is public surface**, and the census row is the promise.
- **`Incremental.windowFnName`** — promised, and recently: see "The row-set-preserving window
  family" below, which retains it alongside the `FallBackReason` case it renders.
- **`Idl.Gen.usesHosted`** — a **declared boundary**, not a helper. It answers "does this authored
  value populate a hosted slot", which is the question a generative cross-host comparison must
  answer about its own vectors before it can claim to have compared them: a hosted slot's content
  belongs to the host codec's own specification, so a value-generic sampler cannot draw content that
  codec is obliged to accept. It has no caller in this repository today — the leg it was written for
  is not wired here — and it stays public precisely because the alternative is a consumer swallowing
  those vectors silently instead of stating the boundary.

**Not narrowed, and not an API question at all:** `Fuaran.Core.Conformance` ships fifty law families
that no adopting domain runs. They are correctly public — the gap is adoption, and a visibility
change would answer the wrong question by deleting the kit's reason to exist.

The other members the sweep found uncalled but load-bearing are now documented as such, in the same
release, under "Public because a sibling Core package calls it", "Members the other language hosts
mirror name for name", and "The portability set" — so the next reading classifies them as promised
rather than proposing them again.

### The narrowing had a caller: `SchemaWalk.noSources` is public again (Phase 129, `0.22.0`)

`SchemaWalk.noSources` is **public** from `0.22.0`. The sentence above — "if you are the caller it
could not see, say so and the member returns" — was taken up, and this is the member returning.

The caller was a downstream consumer's schema-walk helper: the UI host `fuaran-dotnet`, whose
grounding helper for two validator codes called it to walk a pipeline under no declared source. The
sweep searched every `.fs` / `.fsi` / `.fsx` file available to it and could not see the call, because
that consumer was sitting on a deliberate version hold at `0.18.0` when the sweep ran — so the file
that held the call was in the workspace and the call itself was against a Core the consumer had not
yet adopted. It surfaced when the consumer was raised to `0.21.0` and stopped compiling, and it was
repaired there with a file-private `SchemaWalk.ofMap Map.empty`, which is the public spelling of the
same thing and remains a correct way to write it.

**Restoring it is additive** — a token returns to the surface, nothing changes shape, and no
consumer that adopted the narrowing has anything to do. The `ofMap Map.empty` spelling is not
deprecated by this; `noSources` is back because it NAMES the case ("no named source declared"),
which a caller reading the call site should not have to reconstruct from an empty map.

**What it says about the method, which is the part worth keeping.** A caller-count sweep measures the
sources it can read at the revision they are at, and a consumer on a version hold is invisible to it
in precisely the way that matters: its code is present, its call is real, and the call is against a
surface it has not adopted yet. A future narrowing sweep should read each consumer at the version it
will adopt, not at the version it currently pins — or treat a consumer on a hold as unmeasured and
say so, rather than as measured and silent.

## Open-core posture

Apache-2.0, abstractions-tier. The contract (protocol + witness records + signature/
effect types + envelopes) lives here; each domain's **evaluator / reduction** (render,
recompute, regenerate geometry, reflow) stays domain-side. No domain evaluator may leak
into a `Fuaran.Core.*` package (FGP 6).

## Fable cleanliness

Public surfaces are FSharp.Core-only and Fable-clean on **both** the encode and the decode
path. Decode is portable as of Phase 241: `Fuaran.Core.Wire.Json.parse` is a hand-rolled
recursive-descent JSON parser (no System.Text.Json), `Fuaran.Core.Wire.Decode`'s combinators
operate over the resulting `JVal`, and `Fuaran.Core.OpStream.fromJsonl` ships its own
self-contained line scanner — so a Fable-compiled host can decode, `verifyChain`, and replay
in-browser without a host-side boundary. `Json.render` and `Json.parse` are inverses over
canonical wire JSON. The Fuaran wire `JVal` model has no `null`; a bare `null` token is
rejected by name on decode (and see "Null-tolerant read" below for the opt-in, read-side-only
tolerance that lets a foreign document spell an absent member `null` without the model gaining one).

**Enforced, not asserted — in `fuaran-dotnet`, since Phase 217.** This repository runs no Fable
compiler: the compiler belongs where a Fable toolchain already lives, so the gate that enforces this
claim is **`fuaran-dotnet`'s `tests/core-fable/`** — run by that repository's Fable stage
(`tests/fable-laws/fable-check.ps1`) and by its CI's `fable-portability` job. It has two legs. The
**compile leg** references every public package, touches each one's encode/decode surface, and
checks that every reference was actually transpiled; a construct that is not Fable-clean fails
there. The **value leg** is "The value leg" below. Until Phase 217 both ran here, in `./verify.ps1`
(Phase 54 and Phase 118); DECISIONS.md D55 records why they moved and why neither was retired.

**The cost of the move, and the rule that pays it.** That gate sees this repository at the version
`fuaran-dotnet` pins, so on its own it fires when the pin is raised — a release after a divergence
was introduced. So **every version cut of this repository cites a green run of that gate against
the candidate packages** before the release gesture (see "Versioning policy"). The cut-time run
restores every `Fuaran.Core.*` package from the candidate folder alone, into an isolated package
cache so a same-version repack is never served stale, derives the surface from what the candidate
ships, and REQUIRES the value leg.

`Fuaran.Core.Idl` joined the surface at `0.8.0` — it was the one `src/` package absent from it, which
made its portability an unprovable claim rather than a certified one. What had blocked it was not the
model but the emitters sharing its project; splitting them into `Fuaran.Core.Idl.Codegen` (Phase 97)
removed the obstacle rather than working around it.

**Since Phase 185 the surface is CHECKED for every packable package, not asserted over them — and
since Phase 217 that half stays here, because it needs no Fable.**
`Fuaran.Core.Tests.FableSmokeCompletenessTests` derives the packable set from the tree — every
project under `src/` whose own `IsPackable` is not `false`, honouring `Directory.Build.props` — and
fails naming any that neither ships the `fable/` source distribution (the `Directory.Build.props`
convention, which is what a Fable consumer compiles and so is the act of making the claim) nor is
listed in [`fable-exclusions.json`](fable-exclusions.json). The exclusions are
`{ "package", "reason", "phase" }` — what is off the surface, why, and which phase decided — and the
check runs in both directions, so an entry whose package ships the `fable/` sources anyway fails as
loudly as a package covered by neither. The receiving gate holds its own completeness check against
the same entries, over its pins and over a candidate's packages. The same test family checks the
rest of Phase 217 rather than asserting it: no script or workflow here invokes `dotnet fable`, the
tool manifest does not carry the compiler, and the only authored `Fable.Core` reference is
`Fuaran.Core.Wire`'s — which powers Wire's own `#if FABLE_COMPILER` float layout, the fix for the
`Json.render` defect below, and is the one sanctioned library reference (D55). Off the surface today: `Idl.Codegen` and `Idl.Cli` (build-time and
.NET-only — they emit source or are a console tool, and neither ships the `fable/` source
distribution), and `Fuaran.Core.CSharp` (a C# assembly is not Fable-compiled; listed rather than
filtered out by project type, so the excusal is on the record). `Idl.Spike` needs no entry — it is
`IsPackable=false`, so it makes no claim to keep. (`Double.ToString`
with the round-trip specifier is *not* Fable-supported — float→string must route through
`Wire.Canon.canonicalFloat`; `Double.TryParse`'s style/provider arguments are ignored under Fable but
parse invariantly, which is a benign warning, not a gate failure.)

**A compile gate is not a VALUE gate, and the difference has cost real defects here.** It proves a
construct transpiles; it cannot notice that the transpiled code computes a different number. That is
exactly how `fnv1a` sat divergent behind a green gate until `0.6.0` (see "Hash-chain integrity
posture"). Where a value must agree across pipelines, the claim is bought by the value leg below and
by an independent in-suite reference implementation on the .NET side. Anything new making a
cross-pipeline value claim adds its vector to `ParityVectors` — see "The value leg".

### The value leg (Phase 118, `0.18.0`)

**The compile gate has a VALUE leg beside it.** A committed vector table — `Hash.fnv1a`,
`Hash.sha256Hex` / `sha256HexOfBytes` / `utf8Bytes`, `Wire.Canon.canonicalFloat`,
`Wire.Json.render`'s own float layout, encode + decode of a reference witness document through both
renderers, `OpStream.defaultHash` over a two-op chain, `ConfRng`'s draw stream, and (since Phase 217)
the 124-row `hashSweep/*` corpus — runs on **both** pipelines and the output is byte-compared.
**The table is PUBLIC since Phase 217: `Fuaran.Core.ParityVectors`, in `Fuaran.Core.Conformance`**
(additive, `0.31.0`). It ships as code rather than data because the transpiled side has to COMPUTE
it, and a consumer sees this repository only as packages. `fuaran-dotnet`'s `tests/core-fable/`
compiles it from the package's `fable/` sources with the rest of the surface and diffs
`ParityVectors.lines ()` between .NET and node. The .NET half is pinned here against committed
expected bytes by `ParityVectorTests` (the sweep by row count and digest). Those are two claims and
neither implies the other: a change that moves both pipelines identically fails the pinned table and
passes the diff, and one that moves only the transpiled side does the reverse.

**It FAILS without a JS runtime; it never skips a leg it can run.** A check that reports success on
a machine where it did not run asserts exactly what was not checked. The one case it cannot run is
STATED rather than skipped: at a `fuaran-dotnet` pin below `0.31.0` the restored Conformance package
has no table, so that gate says so on every run and names the cut-time rule that covers the gap —
and it FAILS if a pin at or above `0.31.0` restores a package without the table. The by-hand probe
that used to sit beside the leg (`tests/hash-parity-probe/`, retired by Phase 217) is absorbed: its
corpus is the `hashSweep/*` rows.

**What the leg found on its first run, and what changed as a result.** `Wire.Json.render` — a public
encode surface — **threw under Fable for any `JFloat`**. Its float case was
`System.String.Format(InvariantCulture, "{0:R}", f)`, and Fable's `String.format` refuses the
round-trip specifier at RUNTIME ("The round-trip format is not supported by Fable"). It compiled
cleanly, so the Fable gate had been green over it since the gate existed, and the smoke it gates had
never been executed. From `0.18.0` the layout is one shared internal helper: the finite re-lay that
`Canon.canonicalFloat` already carried is now `FloatLayout`, used by both, and `Json.render` renders
through it. **The .NET bytes are unchanged and pinned** — `String.Format(inv, "{0:R}", f)` and
`f.ToString("R", inv)` agree across the finite range, on `-0` (`"-0"`, which `Json.render` keeps and
`canonicalFloat` deliberately collapses to `0`), and on the non-finite tokens (`NaN` / `Infinity` /
`-Infinity`, which are not valid JSON and are exactly what `Json.tryRender` exists to refuse). What
moved is the transpiled side, from throwing to agreeing. A Fable host that had been avoiding
`Json.render` for floats can stop.

**The go-red measurement, re-taken.** Removing the masked add `Hash.(.+.)` leaves `HashTests` 12/12
green **and** the committed .NET vector table green — the mask is a no-op on .NET, so neither can see
it — while the parity leg reddens on exactly the two-block SHA-256 vectors (the 56-byte FIPS message
and the byte-form vector over it) and leaves every single-block vector untouched. That is the class
this leg exists for, and it is now measured on the gate's own vectors rather than in a scratch probe.
**Re-taken in the receiving gate (Phase 217.E):** the same perturbation, packed as a `0.31.0`
candidate and run through `fuaran-dotnet`'s cut-time mode, reported 40 of 164 vectors divergent —
`sha256/two-block` first, then the multi-block `hashSweep/*` rows — and the clean candidate 164/164.

### The portability set — what the Core Fable smoke reaches is PROMISED (`0.19.0`)

The gate above compiles its smoke program against every public package, and what it touches it
touches deliberately. (The smoke lived in this repository as `tests/fable-smoke/` until Phase 217;
it is now `fuaran-dotnet`'s `tests/core-fable/Program.fs`, carried over unchanged, touches included.) A member reached from there therefore carries a **portability guarantee to Fable
consumers**, not merely a proof that it compiles — and seven such members have no other caller in
this repository, which is exactly the shape a caller-count reading takes for dead code. Narrowing
one to `internal` would withdraw a guarantee **without turning the gate red**: the smoke project
would simply stop seeing it, and stop asserting anything about it. They are promised, and named here
so the next such reading knows it:

- **`Fuaran.Core.Delta.ofRows`** and **`Fuaran.Core.RowIdentity.byColumn`** — building a row-set
  delta and keying its rows, the first two calls a client-side incremental host makes.
- **`Fuaran.Core.Incremental.isIncremental`**, **`Incremental.primeOn`**, **`Incremental.refreshOn`**
  — the incremental column-layer entry points, so a Fable host primes and refreshes in the browser
  rather than round-tripping for every edit.
- **`Fuaran.Core.Idl.Sanitize.sanitizeAttributes`** and **`Idl.Sanitize.scrubMarkdown`** — the
  sanitisers, which a client host needs precisely because it is the side rendering untrusted content.

**The rule generalises: the Core Fable smoke is a promise surface, not a scratch project.** Adding a
member to it makes a portability promise; removing one withdraws it. Neither is a tidying edit —
and since Phase 217 the edit is made in `fuaran-dotnet`, so a change here that should widen the
promise names the smoke line it needs in its own record.

## Canonical float layout (Phase 55)

`Fuaran.Core.Wire.Canon.canonicalFloat : float -> string` is the **single cross-host float → string
encoder**, and every float→wire / float→key path in the substrate routes through it (the tree/columnar
wire codecs via `Canon.render`, `DataFrame`'s cell-string + group-key, `Query`'s invocation key). The
pinned layout: non-finite floats → the fixed JSON-string tokens `"NaN"` / `"Infinity"` / `"-Infinity"`;
`-0.0` collapses to `0`; a finite float uses `Double.ToString("R", InvariantCulture)` on .NET and the
byte-identical JS shortest-round-trip re-layout under Fable (WIRE_FORMAT §2 rule 5). A conformant TS /
Python host MUST replicate exactly this. `Conformance.canonicalFloatLaws` certifies determinism, finite
round-trip, and the stable non-finite tokens. `Fuaran.Core.Validator` references `Fuaran.Core.Wire` and
routes its uniqueness tokens through `Canon.canonicalFloat` directly — one canonical float layout,
one implementation, no inlined copy to drift (a host comparing Validator tokens cross-host uses the
same canonical form).

## Typed row-source codec (fuaran#665, `0.2.1`)

`Fuaran.Core.Row` (= `Map<string, obj>`) + `Fuaran.Core.RowCodec` are the canonical codec for the
UI wire format's grid/chart row-source payload (WIRE_FORMAT §5 — rows leave the residual-`"<opaque>"`
boundary). Pinned behaviour a conformant host MUST replicate: rows encode as a JSON array of row
objects (empty feed → `[]`, never `null`); cells are best-effort scalars per WIRE_FORMAT §2 rule 11
(string / bool / int / int64 / float / float32 / DateTimeOffset / DateTime → Unix seconds; `null`
cells omit their key per rule 4; anything else the `"<opaque>"` sentinel — the residual boundary,
narrowed to the cell seam). The `float` type-test runs before `int` deliberately: under Fable every
number satisfies every numeric test, so float-first routes all JS numbers through `canonicalFloat`,
byte-identical to .NET. Decode accepts the typed array **and** the legacy `"<opaque>"` sentinel
indefinitely (read-compat → the empty feed); decoded numbers surface as `float` (one number
population, per the `JVal` numeric-normalization note).

## Value-level compare-and-append (Phase 79)

`Fuaran.Core.OpStream.appendIf : HashFn -> StreamWitness -> expectedHead -> Actor -> op -> state ->
records -> Result<state * records, AppendRejection<'Rej>>` is a **compare-and-append** primitive:
it chains the op only when the stream's current head (`OpStream.head`) equals `expectedHead`,
otherwise it returns `AppendRejection.StaleHead (expected, actual)` naming both heads. On a match it
is behaviourally identical to `append` — a domain-reducer rejection is forwarded as
`AppendRejection.Domain rej`. It is **additive** over `OpStream`: `append` is unchanged and there is
no new `StreamWitness` field.

**The CAS is value-level only, by design.** The guard is over the *logical* chain head — a `string`
value. Core owns no filesystem (GP3) and no process model (GP6), so **file-level atomicity for a
persisted JSONL stream is the host's job**: the host serialises the read-check-append (an advisory
lock, a single-writer actor, or rename-into-place) and calls `appendIf` inside that critical section.
`appendIf` is what makes the losing side of a race a *typed outcome* (`StaleHead`) rather than a lost
write — it does not by itself provide the OS-level mutual exclusion. The intended host shape is the
viewer/CLI **single-mutation surface**: one serialised writer per stream. `AppendRejection<'Rej>` is a
`Rejection`-class envelope — a new case is additive; removing a case is breaking.

## Idempotent append (Phase 82)

`Fuaran.Core.OpStream.appendIdempotent : HashFn -> StreamWitness -> key -> Actor -> op -> state ->
KeyIndex -> records -> Result<AppendOutcome, 'Rej>` is the **at-least-once retry** primitive: an
orchestrated session that times out mid-append re-sends its op under the same invocation key (the
Phase 27 `Function.invocationKey` shape), and the re-send *converges* instead of double-applying. A
fresh key appends **chain-identically to `append`** (the idempotency guard adds nothing to the chain
— no new record field, no new witness field, GP2) and returns the incrementally-updated `KeyIndex`; a
seen key returns `AppendOutcome.Duplicate` naming the entry the key already produced (an `EntryRef` —
`Seq` + `Hash`, GP5) with the caller's stream and index untouched. A domain rejection is forwarded
verbatim and indexes nothing — the key stays fresh for a corrected retry. Total, never a throw (GP4).

**The index is caller-threaded pure state — Core holds no seen-key registry (GP6).** `KeyIndex` is a
plain value: `KeyIndex.ofStream keyOf records` rebuilds it from any stream (a total fold, first-wins
on a duplicate-keyed plain-`append` stream), and the `KeyIndex` returned by each `Appended` maintains
it incrementally — the two agree (the rebuild-parity law; `ofStream` is literally a fold of
`KeyIndex.add`, which is itself first-wins). `keyOf : 'Op -> string` is a per-call parameter, not a
witness seam (GP2). **Key uniqueness scope is per-stream**: an index is only meaningful against the
stream it was built from; cross-stream dedup, storage, and locking stay host-side (GP3/GP6), exactly
as for the Phase 79 CAS.

**The full agent retry loop composes idempotency with the CAS — key check first, deliberately.**
`appendIdempotentIf` (Phase 82 ∘ Phase 79) checks the key *before* the head: when a retry's earlier
attempt actually landed (the ack was lost, not the write), the head has advanced and a bare
`appendIf` would return `StaleHead` forever — the key check first lets the retry converge on
`Duplicate` under any head. Only a fresh key reaches the CAS (stale head ⇒
`AppendRejection.StaleHead`: re-read the stream, rebuild the index via `KeyIndex.ofStream` — the
re-read picks up any own-earlier append — and retry; matched head ≡ `append`).
`Conformance.idempotencyLaws` certifies fresh≡append, duplicate convergence, rebuild parity, and the
idempotency-before-CAS ordering — seed-replayable.

## Proposal arbitration (Phase 85)

`Arbitration.arbitrate : NodeWitness -> IdWitness -> 'Node -> OpScriptProposal list -> Arbitration`
decides which subset of N op-script proposals can land together against one base tree: batch
`Ops.canApplyAll` against the base filters the inapplicable (each rejection carries the op-algebra's
own envelope + the failing op index), then a greedy pass in the **pinned order** (ascending proposal
id) accepts each proposal whose footprint (Phase 78) is `Ops.independent` of everything already
accepted. The result is a **deterministic, total partition** (GP4 — analysis only, the base is never
mutated): the accepted set is mutually independent — by footprint soundness its scripts apply
confluently in any order (`MergedScript` is the pinned-order composition) — and every rejection is
typed + actionable (GP5): `Inapplicable (opIndex, rejection)`, or `Conflicts interfering` naming the
accepted ids (computed against the **full** accepted set) the agent must rebase against. Proposal ids
are expected unique — a queue assigns them, and `AiSurface.Proposals` (Phase 59) is one such assigner,
projecting to `OpScriptProposal` with `Proposals.toOpScript`; `arbitrate` is total on duplicate-id
input, but the permutation-invariance guarantee assumes unique ids (only then is the pinned order a
total order). Since Phase 157 that assumption is a total check —
`Arbitration.duplicateIds proposals = []` — and [`proofs/README.md`](proofs/README.md)'s theorem 13
proves the guarantee under it and that it cannot be dropped.

**It lives in `Fuaran.Core.Ops` since Phase 192** (`AiSurface.arbitrate` until then), beside
`Ops.footprint` and `Ops.independent` — the two it is the other end of. `OpScriptProposal<'Node,'Id>`
is the minimal record the partition needs: `Id` (what the pinned order sorts by), `Holder` (whom a
rejection is reported to) and `Ops`. The lifecycle fields a queue carries — author, timestamp,
intent, approval status — stay in `AiSurface.Proposals`, because arbitration reads none of them.

**Coexistence, not quality (GP6).** Arbitration says which proposals *can coexist*, never which is
*better*: no ranking policy, no quality judgement, no evaluator lives in Core. A host that wants
priority or scoring exercises it upstream — the pinned order is proposal id, and id assignment is the
host's lever — or downstream, by choosing what to re-propose; the partition itself is mechanical.

**Greedy-maximal, not maximum (the Phase 52 honesty discipline).** Greedy-in-pinned-order yields *a
maximal* mutually-independent set — nothing rejected could be added without a conflict — not *the
maximum* one; a different order could accept a larger set (and finding the maximum is NP-hard, and
would smuggle a ranking policy into Core besides). The order is pinned, documented, deterministic.
And independence is conservative (Phase 78): `Conflicts` means "not **provably** coexistent", never
"wrong".

**The consumption shape (dispatcher / orchestration).** `arbitrate` → the accepted set lands (the
merged script, or per-proposal in any order); every `Conflicts` reject is re-proposed after rebasing
onto the tree the accepted set produced; every `Inapplicable` reject repairs against its op-algebra
envelope. `Conformance.arbitrationLaws` certifies determinism + input-permutation invariance, the
total partition, pairwise independence, rejection actionability, and any-order confluence
(`concurrencyLaws`, Phase 80, is the stronger op-level-interleaving form of the same claim — a domain
that arbitrates runs both). `ArbitrationRejection<'Id>` is a `Rejection`-class envelope — a new case
is additive; removing a case is breaking.

## Null-tolerant read (Phase 102, `0.7.0`)

`Fuaran.Core.Wire.NullPolicy` is the **read-side** policy for the JSON `null` token, and it changes
nothing about the wire model: `JVal` gains no constructor, `Json.render` / `Canon.render` never emit
`null` whichever policy a read ran under, and the encode side is untouched. What it adds is an opt-in
way to *read* a foreign, spec-conformant document that spells an absent member `null` — which a great
many JSON producers do, and which no amount of consumer-side work can route around.

- **`RejectNull` is the pinned default.** `parse` / `parseWith` / `parseDetailed` /
  `parseDetailedWith` are byte-identical under it — same errors, same `Kind`s, same positions, same
  messages, including the `NullNotRepresentable` refusal consumers branch on. The core parser now
  takes the policy as a parameter (`parseDetailedWithPolicy`); the pre-existing entry points pass
  `RejectNull` and are wrappers over it.
- **`EraseMemberNull` erases a `null` in object-member value position to member absence**, so
  `{"a":null}` reads exactly as `{}` — the same "absence is structural" rule the encode side already
  applies to a null cell (`RowCodec.encodeCell` rule 4, WIRE_FORMAT §2 rule 4). Entry points:
  `Json.parseTolerantOfNull` / `parseTolerantOfNullWith` / `parseDetailedTolerantOfNull` /
  `parseWithPolicy`, and `Decode.parseTolerantOfNull` for the combinator path. A consumer swaps one
  call; every combinator downstream behaves as it does against the `null`-free spelling
  (`getProp` on an erased member returns `missing property: <name>`, not a null).
- **The position rules are part of the contract, not an implementation accident.** A bare top-level
  `null` (the whole document would vanish) and a `null` **array element** (erasing it would silently
  renumber every later index) have no absence to erase to and stay `NullNotRepresentable` under the
  tolerant policy too — with a **different message** from the strict refusal, because the two have
  different remedies and a consumer must not read one as the other. Array-position tolerance is a
  deliberate future extension if a real need surfaces, never a thing this policy quietly already does.
- **Tolerance is a read normalisation, never a new emission.** `render` of a tolerantly-parsed
  document is exactly the canonical `null`-free form, and that form re-parses under the strict policy —
  so nothing downstream (a hash chain, a byte-comparing conformance corpus, another host) can tell
  a tolerantly-read document from one written without the token. This is the leg that keeps the
  tolerance from leaking into the format.

`Fuaran.Core.Conformance.WireNullTolerance` is the executable form of all four bullets — the vectors
any host claiming the tolerant read, in any language, satisfies or does not have it: erasure ≡
omission (including nested, and inside array elements), the strict path's refusal unchanged, the
non-member positions rejected by name with a distinguishable message, near-misses of the token not
absorbed, `null`-free controls unaffected by the policy, and the render-and-re-parse round trip.

## Column-layer deltas (Phase 98, `0.9.0`)

`Fuaran.Core.DataFrame` carries `TableDelta` — a typed description of what changed in one columnar
table — with the `Delta` algebra over it and `DeltaCodec` for its canonical wire. **Purely
additive**: nothing that shipped before moved, and every pre-existing wire byte is unchanged.

**The shape.** `TableDelta` is `FullRefresh | RowSet of RowSetDelta`. `RowSetDelta` carries the
identity `Scheme` its keys were minted under, canonically-ordered `Rows` (`RowRef * RowChange`), and
`InvalidatedColumns` — columns whose values can no longer be trusted **without** naming rows, which
is the honest shape for a change whose row extent is unknown or is all of them. `RowChange` is
`RowAdded | RowChanged | RowRemoved | RowTransient`.

**`FullRefresh` is the top element, not an error value.** A structural schema change, a wholesale
replacement, or a change that cannot be located IS "everything may have changed", and the type says
so precisely rather than emitting a `RowSet` that under-reports.

**The algebra is a monoid and it is pinned as one.** `Delta.compose` is **total and associative for
every pair of inputs** — not merely for consistent ones — `FullRefresh` absorbs on both sides, and
`Delta.empty scheme` is a two-sided identity within that scheme. `RowTransient` is load-bearing to
that claim: a row change is a `(existed-before, exists-after)` pair, composition is relational
composition of those pairs, and `Added ∘ Removed` is `(absent, absent)` — a state the three obvious
cases cannot represent. The laws are exhaustive over the four-element change space rather than
sampled. Composing across **different identity schemes** yields `FullRefresh` (the truthful coarse
answer); `Delta.composeChecked` refuses instead, with `SchemeMismatch`.

**Addressing.** A row is named by identity (`ByKey`). `ByOrdinal` is reserved for a source with no
identity at all, under the reserved scheme name `RowIdentity.ordinalScheme` (`"ordinal"`), and the
two addressing modes may **not** be mixed inside one delta — `Delta.validate` and the wire decoder
both enforce it in both directions. This is the `SkeletonOp` rule of `0.2.0` applied to the columnar
strand: where a collection's members have identity, they are addressed by it.

**Totality.** Nothing throws. `Delta.defects` enumerates every fault; `Delta.validate` refuses the
delta **whole** with the first, never partially applying it. `DeltaDefect` is a `Rejection`-class
envelope — a new case is additive, removing one is breaking. `DeltaCodec.decode` refuses a
structurally-decodable but *inconsistent* delta as `ColumnError.Malformed`, naming the defect: the
wire carries no delta the in-memory algebra would reject.

**Wire.** `"$type"`-tagged and rendered through `Canon`, so keys are Ordinal-sorted and the bytes are
identical on every host. `encode` normalises first, so two spellings of one delta are the same bytes.
Decode returns the columnar strand's six-code `ColumnError` envelope. The pinned canonical forms are
`{"$type":"fullRefresh"}` and
`{"$type":"rowSet","columns":[…],"rows":[{"$type":"added","key":"…"},…],"scheme":"…"}`.

**Relationship to `Change` (Phase 34).** `Change` is now a **projection** of the delta rather than a
rival vocabulary: `Delta.ofChange` lifts, `Delta.toChange` projects back for a consumer still on it
(`DataFrame.evalFrom`). The projection is deliberately **conservative** — anything the four `Change`
cases cannot express precisely becomes `FullChange`, so a consumer acting on it recomputes too much
and never too little — and it returns an **option**, because "nothing changed" is a statement
`Change` has no case for.

**`DataFrame.cellToken` / `rowToken` / `rowTokenString`** are public as of `0.9.0` — the pinned
canonical row token `GroupBy` / `Distinct` / `Intersect` already partition by. They are exposed, not
duplicated, so "did this row's content change" has exactly one answer across the strand.

## Incremental column-layer evaluation (Phase 99, `0.11.0`)

`Fuaran.Core.DataFrame` carries `Incremental` — a `Transform` pipeline evaluated against a
`TableDelta` rather than from scratch — with the `IncrementalDelta` equivalence family in
`Fuaran.Core.Conformance` certifying it. **Purely additive**: nothing that shipped before moved, no
wire byte changed, and no evaluation result changed.

**The contract is an equality, and it is the whole contract.** For every pipeline, every state and
every delta, `Incremental.refresh … |> Result.map Incremental.result` equals
`DataFrame.evalPipelineWithInEnv resolve env pipeline source` over the same source — the same table,
or the same `EvalError`. The reference evaluator remains the single cross-host semantics; the
incremental path is a restriction of it that recomputes less, and it computes what it does recompute
through the reference evaluator's own primitives. A consumer may switch a pipeline between the two at
any time and observe nothing but the cost.

**The caller's obligation.** The delta must truthfully describe the change from the source the state
was last evaluated against to the source now passed in; `Delta.diff` produces exactly that. A delta
that under-reports is a false statement about the data, and no evaluator can detect one without
recomputing the answer it was asked to avoid recomputing.

**The declared boundary.** `Incremental.plan` classifies every step before any evaluation:
`PropagateRows` (`Filter` / `Project` / `Derive`), `MaintainGroups` (a `GroupBy` as the pipeline's
**last** step), or `FallBack` with a typed `FallBackReason`. `IncrementalStrategy` is the induced
verdict. Adding a `FallBackReason` case, or reclassifying a step from `FallBack` to a restricted
class, is **additive** — the answers do not move, only the cost. Reclassifying a step the other way
(from restricted to `FallBack`) is likewise answer-preserving but is a **performance** regression a
consumer may be asserting on through the footprint, so it is announced in the release note.

**The footprint is part of the surface, not diagnostics.** `RecomputeFootprint`
(`{ SourceRows; ResultRows; Recompute }`) and `Recompute` (`Primed` / `ReusedPrior` /
`RowsRecomputed` / `GroupsRecomputed` / `FullRecompute`) carry **counts only, no clock**, so they are
deterministic and identical on every host and a consumer may assert on them. `Recompute` is a
closed-set envelope — a new case is additive, removing one is breaking. Because a consumer may assert
on them, changing what a case's count MEANS is breaking too: see "One scale for `rowsEvaluated`"
below, where `FullRecompute`'s reading was corrected in `0.18.0`.

**The type is `RecomputeFootprint`, deliberately.** `Fuaran.Core.Ops` publishes
`Fuaran.Core.Footprint` (the op-script address set). Two same-named types in one namespace across two
packages collide for a consumer that opens both, so the columnar one carries the longer name.

**`IncrementalEval`'s fields are public but engine-owned.** Build one with `Incremental.prime` and
advance it with `Incremental.refresh`. The record is transparent because the columnar strand keeps
its data transparent, not because a hand-built state is supported: one whose caches disagree with its
source is a claim the evaluator cannot check. Its shape is **not** a stability promise the way a
witness record is — treat `Incremental.result` / `Incremental.footprint` as the surface.

**Ordinal-addressed deltas are declined.** The reserved `ordinal` scheme names positions, and a cache
keyed by position is invalidated wholesale by any insert, so a `RowSet` under it takes the full-
evaluation path with `OrdinalAddressing` recorded. An identity witness that cannot key the source
degrades the same way rather than failing the call: identity is what the seam needs, not what the
answer needs.

**Four new `DataFrame` entry points**, each a one-line wrapper over a primitive the reference
evaluator already used privately: `evalExprInRow`, `aggregateCells`, `aggregateType`,
`inferCellType`. They exist so the incremental path computes through the reference implementation
rather than a copy, and they are stable in the ordinary way.

### The merged order — `Sort` admitted (Phase 115, `0.18.0`)

`Incremental` no longer declines a `Sort`. `plan` classifies one as
`StepIncrementality.MergeOrder by`, and a pipeline whose only non-row-local step is a sort is
`RowLocal` rather than `ReferenceOnly (StepNotRowLocal "sort")`. **This is the additive
reclassification the paragraph above names**: the answers do not move, only the cost. Every other
order-dependent verb (`Limit`, `Window`, `Distinct`, the joins and the whole-relation set ops) is
still declined, still by type, still naming the verb. _(Phase 120 admitted a bounded-frame `Window`
and the filtering `Join` kinds, and moved their declines onto reasons that name the frame and the
kind; `0.19.0` then admitted the REST of the window family on row-set preservation, so `Window` is no
longer a declining verb at all — see both sections below.)_

**A sort is admitted at ANY position in the pipeline, not only as the last step.** It carries no
condition of the kind a `GroupBy` does, because it emits the rows it was handed rather than a
different relation: every step admitted after it — `Filter`, `Project`, `Derive`, and a final
maintained `GroupBy` — reads the order it produced exactly as it would have read the reference's.

**The saving is NOT in the sorting.** A sort evaluates no expression, so it contributes nothing to
`rowsEvaluated` — the same accounting a `GroupBy` gets, and for the same reason. What a widened sort
buys is that the steps *before* it stop re-evaluating every row: on the shared recompute fixture
family, a filter-then-sort pipeline over six rows with one cell edited falls from six row-evaluations
to one. A sort-bearing row-local pipeline therefore reports `RowsRecomputed`, and the footprint
vocabulary gains no case.

**`IncrementalEval` gains one engine-owned field, `SortOrders`** — per sort step, the token order its
rows arrived in and the token order it produced. Both halves are load-bearing: the produced order is
what a merge reuses, and the arrival order is the only thing that says the reuse is still valid,
because a stable sort breaks ties by arrival position. This is the ordered-member condition the
maintained groups already carry, one verb along, and it is live for the same reason: `Delta.diff`
reports a pure row reordering as *quiet*, so a merge that trusted its cached order without checking
arrival order would answer a delta that named nothing with a table in the wrong order.

**One new `DataFrame` entry point**, on the same terms as the four above: `rowCompareBy`, the
reference `Sort`'s own row comparator (multi-key, nulls last regardless of direction, unknown columns
skipped). The merge sorts through it rather than through a copy — a second comparator would agree on
every corpus anyone thought to write and disagree on the first null, the first tie and the first
misspelled key.

**`Window` remains declined, deliberately and by type.** The shared fixture family records no
footprint for it, and this phase's own gate is that a class is not widened before it is measured — so
a bounded-frame window stays `StepNotRowLocal "window"` until a vector exists to measure it against.
_(SUPERSEDED by Phase 120, under an operator decision of 2026-09-02 that waived the corpus-side gate
for `Window` and `Join`: this repository vendors its own before/after vectors for both, and the
corpus records the family's own afterwards. The rule itself — measure, then widen — is unchanged; see
DECISIONS D24. `0.19.0` then widened the remaining window functions, with its own vendored vector,
under the same rule; see DECISIONS D25.)_

### One scale for `rowsEvaluated`, and a declined prime is `Primed` (Phase 117, `0.18.0`) — BREAKING

**This is a BREAKING change for any consumer asserting on a footprint, and the reason it is breaking
is that the recorded VALUES move**, not merely the type. Two corrections, both to the instrument the
paragraphs above call part of the surface. `0.18.0` has not been tagged, so this ships under it
alongside Phases 112–116 and 119; a consumer moving from `0.17.0` should re-read every footprint
assertion it holds.

**`Incremental.rowsEvaluated` now counts row evaluations AT STEPS in every case, a full evaluation
included.** `Recompute.FullRecompute` gains that count as its first field —
`FullRecompute of rowsEvaluated: int * reason: FallBackReason` — supplied by the reference evaluator
itself through the new `DataFrame.evalPipelineWithInEnvCounted`. It was previously projected onto
`RecomputeFootprint.SourceRows`, which is a different unit: a pipeline with three evaluating steps
over six rows costs eighteen row evaluations and was charged six. So a decline compared against its
own full baseline read as having done LESS work than the thing it fell back to, and the conformance
family's work law could only be stated over the restricted classes, because the declined ones were
not on a scale it could compare. `SourceRows` is unchanged and stays its own field.
`Incremental.footprintString` renders the count for this case too, so that string moves as well.

**A pipeline the plan DECLINES now primes to `Primed n`.** Priming avoids nothing whatever the plan
says and has no prior state to fall back from, so reporting `FullRecompute` there described a
fall-back that did not happen. The decline and its typed reason attach to a **refresh**, where the
fall-back is real; `Incremental.plan` remains what a consumer asks beforehand, and it is unchanged.

**The scope of that second correction is the PLAN-LEVEL decline only, deliberately.** A prime whose
identity witness cannot key the source still reports `FullRecompute (n, RowIdentityUnusable …)`,
because that defect depends on the source data and is invisible to `plan` — the footprint is its only
channel, where a plan-level decline is askable before any evaluation. The unreachable
`plan`/`split` disagreement likewise still reports `FullRecompute`, being a defect that should be
visible wherever it surfaces.

**`DataFrame.evalPipelineWithInEnvCounted` is a new public entry point** and is purely additive: it
returns `Result<Table * int, EvalError>` and `evalPipelineWithInEnv` now delegates to it, so the
existing four entry points evaluate byte-identically to before. The unit it counts is one evaluation
of one step's expression against one row — a `Filter` and a `Derive` are charged the frame's row count
where they stand, and every other verb, evaluating no per-row expression, is charged none.

**The shared `incremental-recompute` vectors vendored under
`tests/Fuaran.Core.Tests/fixtures/incremental-recompute/` were re-pinned to the corrected readings**,
with the before/after in that directory's `README.md`. Only the sort vector moved; the control
vector's footprints were already on this scale, which is the control working. Re-recording them on
the corpus side is that specification's act, not this repository's.

### The bounded frame and the filtering join — `Window` and `Join` partly admitted (Phase 120, `0.18.0`)

`Incremental` no longer declines every `Window` or every `Join`. **The additive reclassification the
Phase 99 paragraph names, applied twice more**: the answers do not move, only the cost.

**A `Window` whose frame is BOUNDED is admitted** — `Lag`, `Lead`, `RollingMean`, `RollingSum`, the
four whose output for a row is a function of the rows within a fixed offset of it in its partition's
order. `plan` classifies one as `StepIncrementality.RecomputeFrame (partitionBy, orderBy)`, at any
position, on the same argument a `Sort` is admitted at any position: it emits the rows it was
handed, in the order it was handed them, so every step after it reads what the reference would have
handed it. The other eight — the ranking family, `NTile` and the three cumulative aggregates — read
the WHOLE partition and stay declined, now with `FallBackReason.WindowFrameUnbounded fn` naming the
function rather than `StepNotRowLocal "window"` naming the verb, because "window" alone no longer
says which frame declined.

**The appended column is RECOMPUTED over the walked frame, not read from a cache, and that is the
honest accounting rather than a shortcut.** A window evaluates no expression, so it costs nothing on
this seam's instrument in either path; and a row's frame moves when its NEIGHBOUR moves, which a
delta naming one row does not say — so knowing which rows were displaced would mean recomputing the
partitions and their orders anyway. What the admission buys is what a widened `Sort` buys: the steps
*before* it stop re-evaluating every row. On the vendored fixture family, a filter-then-lag pipeline
over six rows with one cell edited falls from six row-evaluations to one.

**A FILTERING `Join` is admitted** — `Semi` and `Anti`, which keep or drop each left row once and
emit it unchanged with the left schema only. `plan` classifies one as
`StepIncrementality.FilterByRelation (kind, on)`: a `Filter` whose predicate reads a relation. The
combining kinds — `Inner` and `Left`, which fan one left row out across every right row it matches
and append the right schema, and `Right` / `Outer`, which additionally emit rows no left row
produced — stay declined, now with `FallBackReason.JoinNotRowPreserving kind` naming the kind.

**`IncrementalEval` gains one engine-owned field, `JoinKeys`** — per admitted join step, the joined
relation's key cells in its own row order. It is what says a cached verdict is still valid: the delta
describes the SOURCE, so a row it did not name can still have a different verdict when the relation
gained or lost the key that row matched on. A relation whose key index has not moved keeps the
reuse; one whose keys moved has every verdict recomputed at that step, with the prefix's reuse
untouched.

**One behaviour outside the two new classes did change, and it is a correctness fix.** The wholesale
reuse of a prior result (`ReusedPrior`, for a quiet delta over a byte-identical source) now
additionally requires that no step names a **`Ref`** relation. That reuse asks whether anything the
answer depends on has moved, and it can only ask about the pipeline, the env and the source; a `Ref`
relation is whatever `resolve` returns at the moment it is called, so handing back the prior result
answered the previous relation's question with the previous relation's answer. An `Embedded`
relation is part of the pipeline and is already pinned by `PipelineChanged`, and the convenience
entry points resolve through `noResolve`, which refuses a `Ref` — so no pipeline that could
previously reach this path is affected. A `Ref`-bearing pipeline now takes the ordinary path, where
the relation is resolved and compared; an unmoved one still costs nothing, because a join evaluates
no expression.

**Four new `DataFrame` entry points**, on the terms of the six above — the incremental path computes
through the reference implementation rather than a copy of it: `windowFrameBounded` (is this window
function's frame bounded), `windowStep` (the reference `Window` step over a frame given as its
schema and rows), `joinKeyIndices` (the join's key resolution, refusing the FIRST unresolvable name
in the order the reference reports it) and `joinKeysMatch` (the join's `cellEq` key predicate, in
which a `Null` matches nothing — not even another `Null`, unlike the canonical row token the set ops
dedup on). `evalJoin` was refactored onto the private definitions these wrap, so there is one
implementation of each.

**Two more vectors are vendored** under `tests/Fuaran.Core.Tests/fixtures/incremental-recompute/` —
`window-declines-in-full` and `join-declines-in-full` — each recording the declined triple its class
produced before this phase as the measured "before", exactly as the sort vector does. See that
directory's `README.md`, including the one spelling this repository had to invent (`{"null": true}`
for a cell, which a `lag` column cannot avoid).

_(The window half of this section is SUPERSEDED by "The row-set-preserving window family" below:
`0.19.0` admits every window function, so the eight declined here are declined no longer and
`WindowFrameUnbounded` is no longer produced. The join half stands unchanged.)_

### The row-set-preserving window family — every `Window` admitted (`0.19.0`)

`Incremental` no longer declines any `Window`. `plan` classifies **every** window function as
`StepIncrementality.RecomputeFrame (partitionBy, orderBy)`, at any position — the ranking family,
`NTile` and the three cumulative aggregates alongside `Lag`, `Lead`, `RollingMean` and `RollingSum`.
**The additive reclassification the Phase 99 paragraph names, once more**: the answers do not move,
only the cost. See DECISIONS D25.

**What admits a `Window` is that it PRESERVES THE ROW SET, not that its frame is bounded.** One row
in, one row out, in input order, plus an appended column — which every member has. The appended
column is recomputed wholesale over the walked frame through `DataFrame.windowStep` whatever the
function is, so nothing in the walk ever consulted the frame's width; frame boundedness was a proxy,
and the section above drew the line one class too narrow. `DataFrame.windowFrameBounded` is
**unchanged and still public**: the distinction it draws is real and is the line a later phase
restricting the recompute to the rows a delta *displaces* would draw. It is simply not the admission
predicate, and `plan` no longer calls it.

**`FallBackReason.WindowFrameUnbounded` is RETAINED and is no longer produced by `plan`.** Removing a
case from a published DU is breaking for every consumer that matches on it, so the case stays and
`Incremental.reasonString` still renders it — a reason recorded under `0.18.0` still reads. It is not
repurposed. `Incremental.windowFnName` stays with it, on the same terms and for the same reason. A
consumer that switched on `WindowFrameUnbounded` to route a pipeline away from the seam will now
never see it, which is the point: that pipeline is restricted instead.

**The saving is identical to the bounded frames', and so is the accounting.** On the vendored fixture
family, a filter-then-`rank` pipeline over six rows with one cell edited falls from six
row-evaluations to one — the same numbers `window-declines-in-full` records for a `lag`, asserted
against each other directly. `rank-declines-in-full` is vendored beside it, recording as its "before"
the decline Phase 120 itself produced (`windowFrameUnbounded` / `rank`) rather than the older
`stepNotRowLocal` / `window`, because that is the evaluator this widening improves on.

**What `rowsEvaluated` counts, stated so the saving is not misread.** It counts **expression
evaluations at steps** — one evaluation of one step's expression against one row — and **ordering and
windowing work is not on that scale**. `DataFrame.evalPipelineWithInEnvCounted` charges `Filter` and
`Derive` and nothing else, so a `rank` refresh reports the six-to-one saving above while still sorting
each partition on every refresh. That is the same accounting a `Sort` gets (Phase 115) and the bounded
frames got (Phase 120), and it is what "one scale" means in the Phase 117 section: the scale measures
expression evaluations in every case, so a refresh and the full evaluation it replaced are comparable.
**The steps before the window stop re-evaluating every row; the window itself did not get cheaper.**
The unit is deliberately unchanged — redefining it would move every recorded value in every law,
fixture and consumer assertion, which is the breaking change Phase 117 was, and a consumer needing to
compare ordering strategies needs a second instrument rather than a redefinition of this one.

**The conformance family's sample-adequacy demand is extended, and this is what makes the widening
measured rather than asserted.** `IncrementalDelta.demands` now requires a **partition-global** window
to have been restricted, not merely "a window" — the older verdict is satisfied by the `lag` alone and
would have gone on passing had this relaxation been reverted. The generated corpus grew to meet it: a
bare `cumulSum` (which was the family's window decline) and a `rank` behind a filter.

## Static output-schema derivation (Phase 112, `0.18.0`)

`Fuaran.Core.DataFrame` carries `SchemaWalk` — a pipeline's OUTPUT columns derived from its input
schema **without evaluating anything** — with `Conformance.schemaWalkLaws` certifying it against the
reference evaluator. **Purely additive**: two new types (`ColumnKnowledge`, `SchemaKnowledge`) and one
new module; nothing that shipped before moved, no wire byte changed, and no evaluation result changed.

**Two verdicts, and only one of them supports a refusal.** `SchemaKnowledge.Closed cols` means *these
columns, in this order, and no others* — it is the only case from which "that column is absent" may be
concluded. `SchemaKnowledge.AtLeast(cols, reason)` means *these columns are present and the walk
cannot name the rest*: a reader can be CONFIRMED against it and can never be REFUTED, and the reason
names what cost the walk its certainty. A consumer that refutes on an `AtLeast` has written a check
that refuses working pipelines; `isClosed` is the guard, and it is part of the contract rather than a
convenience.

**Three shapes open the set, and each is a fact about data rather than a gap in the walk.** A
`Derive`'s column name is declared but its type is inferred from the cells its expression produced, so
`ColumnKnowledge.Type` is `None` however simple the expression looks — a guess that disagreed with the
evaluator would be worse than no answer. A `Pivot`'s value columns are named by the data, one per
distinct present value in the `on` column, so not even their number is derivable. A `Ref` source's
schema is whatever the caller declares (`ofPipelineFrom` with `ofMap`), and an undeclared name
degrades to `AtLeast` with the name in the reason — never to a guess, and never to a refusal.

**The contract is an agreement with the evaluator, and it is certified.** For every pipeline the
reference evaluator accepts: where the walk says `Closed`, the derived names equal the evaluated
schema's names **in order and with duplicates**; where it says `AtLeast`, every derived name is one
the evaluated schema carries; and wherever the walk states a type, it is the type the evaluator gave
that column. `Conformance.schemaWalkLaws` reports those three plus a fourth — that the generated
sample reached BOTH verdicts, so a green is never half a claim.

**Two mirrors of the evaluator that read as quirks and are not.** `Window` **appends** its output
column unconditionally, so a window whose `As` collides with an existing column leaves the schema
carrying that name twice and the walk says so; `Derive` **upserts** (retype in place, position kept).
Both match what the evaluator does. A walk that tidied either away would be wrong about the shape the
consumer actually receives.

**Forward-coupled with no catch-all.** `ofTransform` matches `Transform`, `JoinKind`, `WindowFn` and
`AggFn` exhaustively, so a new verb or kind is a **compile error here** rather than a silent drift in a
downstream copy. That is why the walk lives beside the evaluator: the same growth that is additive for
the algebra is a correctness event for anything deriving schemas from it.

**Named `SchemaWalk`, not `Schema`.** `Fuaran.Core` already publishes the `Schema` type abbreviation
and a `Schema` module of schema-level operations (`diff` / `classify` / `fingerprint`) beside it, and a
second module of that name in one namespace does not compile.

## Fold-confluence pack: N-lane arrival-order invariance (Phase 100)

Phase 80 certifies confluence for a domain's TREE ops (interleavings of two independence-declared
skeleton-op scripts replay to the same tree); Phase 83 certifies that a two-head `Dag.reconcile`
folds order-independently. Both are pinned to the skeleton-op algebra and both stop at two branches.
A local-first deployment converges neither: it converges **N lanes** — one op-stream per writer,
off one shared base, arriving in whatever order the network delivered them — over its own `'Op` and
`'State`. Phase 100 is that claim, made runnable per domain.

Two surfaces, one in each half of the boundary.

- **`Dag.reconcileMany : ('Op -> Footprint) -> Dag.T<'Op> -> string -> string list -> Result<'Op list, MergeConflict<'Op> list>`**
  — the N-lane generalisation of `reconcile`: every UNORDERED pair of lane deltas is checked with
  `conflicts`, then the whole set is composed at once. `reconcileMany fp dag b [x; y]` is
  `reconcile fp dag b x y`, so #78's conservativity contract is inherited verbatim — `Ok` still
  carries the promise that the deltas provably commute, never a false "clean merge". Pairwise is
  sufficient because set disjointness IS pairwise: there is no N-way interference the sweep can
  miss. The `heads` order is **canonical form, not semantics**, exactly as `reconcile`'s pin is.
  Folding by repeated pairwise `reconcile` is deliberately NOT the same operation: it would mint
  intermediate merge nodes and let the pairing order leak into the result.

- **`FoldConfluence.laneFoldLaws`** (in `Fuaran.Core.Conformance`) — the teeth. Given a domain's
  `StreamWitness`, its footprint projection, a state hash and a lane generator, it folds each
  generated lane set under every sampled arrival order and certifies five laws: **lane-fold
  determinism** (a folding set folds to one state hash under every order — a reducer that rejects
  under one order and not another fails here too), **lane-halt determinism** (a halting set halts
  with the same canonical report under every order), **outcome classification invariance** (no lane
  set folds under one order and halts under another), and — since `0.18.0`, where the pack's two
  hand-written coverage guards became one **sample-adequacy** law (see below) — a guard demanding
  that the sample exercised both a folding and a halting set, reported with the counts it did reach.
  A run that never collided has not tested the halt law at all. Seed-replayable; every divergence is **shrunk** by greedy
  delta-debugging before it is reported, so the counterexample is a minimal reproducer in the
  domain's own op encoding rather than the generated trial that happened to expose it.

**Three outcomes, not two — and the third is the point.** A lane set that folds under one arrival
order and halts under another is named as its own defect rather than folded into "not confluent",
because it is the worse failure: the deployment that received the lanes the other way round
proceeds, and two replicas then disagree about whether they diverged at all. Distinguishing "folds
identically" from "halts identically" is what makes that observable.

**The halt report is canonicalised, and it has to be.** `Dag.conflicts` is symmetric only up to a
`Left`/`Right` swap (`mergeConflictLaws` pins exactly that), so handing it the same two deltas the
other way round yields a report that differs in presentation. `FoldConfluence.canonicalConflictReport`
renders each interference as shape + address + its UNORDERED op pair, deduplicated and sorted — so
"halts identically" is a claim about the interference, not about the printing.

**Sampling bound.** N lanes admit N! arrival orders. `FoldConfluence.arrivalOrders` enumerates all of
them up to `permutationBound` (24 = 4!) and above that samples deterministically — the identity, the
reverse, and shuffles from a fixed seed. The order set is a pure function of the lane count, which is
what lets a shrunk counterexample be re-measured against the same orders. A green verdict therefore
means "certified over the sampled orders", never "proved for all N!".

**What it deliberately does not certify.** Not **resolution** — the pack says a halt is
order-invariant, never that it was correct, and picks no winner (GP6). Not **necessity** — as with
Phase 80, footprint independence is sufficient for a clean fold and never necessary, so a lane set
the footprints declare interfering is required to halt *consistently*, not required to be genuinely
unmergeable. Both surfaces are additive; `laneFoldLaws` is opt-in like `footprintLaws` — a domain
that converges concurrent lanes runs it.

**How a domain runs it.** Supply a `StreamWitness<'Op, 'State, 'Rej>`, an address projection
`'Op -> Footprint` over the domain's own vocabulary (a tree domain feeds `Ops.footprint`; a non-tree
domain writes its own), a canonical `'State -> string` hash, and a `LaneGen` naming the base state, a
genesis op and a lane source; then call
`FoldConfluence.laneFoldLaws witness footprintOf hashState gen laneCount seed iterations` (or
`certifyFold` for the aggregate verdict). The in-repo suite certifies the reference witness and a
second, non-tree domain whose state is a `Map` and whose footprint is its own, and proves the pack
can FAIL: an openly order-sensitive reducer whose footprint declares everything independent produces
a shrunk two-lane, one-op-per-lane counterexample.

**The clause that is easiest to get wrong is the address projection, and the classification law is
what catches it.** An op that requires an address to EXIST must READ it, not merely write its own:
an op creating an entity and an op depending on that entity are not independent, and a projection
that omits the read declares them so. The two then fold under one arrival order and reject under the
other — which is exactly the classification law, and exactly the reason it is stated separately.

**A one-time behaviour change: the kit's sampler moved to the high-order bits (0.12.0).**
`ConfRng.intBelow` — the small-integer draw every generator in `Fuaran.Core.Conformance` is built
from, and therefore `choose` and `shuffle` with it — reduced by `v % n` until 0.12.0. The state is a
linear congruential generator taken mod 2^32, in which bit `k` has period 2^(k+1), so a modulo by a
small `n` read the weakest bits in the word, and read them *in phase*: generators drawn consecutively
off one stream chose in lockstep rather than independently. The coverage guards above are what
surfaced it — a three-lane generator halted on 150 of 150 trials and the fold law never executed
once. `intBelow` now takes the top `bitWidth (n - 1)` bits and redraws an out-of-range candidate:
full-period bits only, and exactly uniform rather than modulo-biased, from shifts and comparisons
alone so the kit stays value-identical under Fable.

The signatures did not move; **every value did**. A consumer pinning a seed will see different
generated data, so a law that certified green over one sample is now certifying over another — which
is the point, since a weak generator in a conformance kit does not produce failures, it produces
false assurance. Treat a repin as a **read**: check that a coverage or vacuity guard which passes
still passes for a reason, rather than raising the iteration count until it does. In this repo's own
suite the change moved exactly one expectation — a four-lane reference-witness certification whose
lanes were a fixed two ops each, a shape that essentially never has mutually-independent footprints
over a six-node tree; its lane length is now drawn, which restores a mixture of both outcome classes
at every lane count.

## Sample adequacy: what a family's sample must contain (Phase 121, `0.18.0`)

A `LawResult` says a law HELD. It cannot say how many samples the law was reached BY — so a law
gated on a generated condition ("an independent pair", "a halting lane set", "a declined refresh")
reports the same green whether the condition arose two hundred times or never. `SampleAdequacy` is
the missing half: a family declares what its sample must contain, and a sample that does not contain
it fails the family with the counts rather than certifying it quietly.

**New public surface.** `AdequacyDemand<'Sample>` — `ReachesEvery (dimension, verdicts, classify)`
and `Spans (measure, atLeast, measureOf)`; `AdequacyClass` — `Guarded` / `Unconditional`;
`SampleAdequacy.check` (demands over a materialised sample), `SampleAdequacy.reached` /
`SampleAdequacy.spanned` (over counts a family already keeps), and `SampleAdequacy.census` — every
law family the kit ships and how each answers the adequacy question. A domain gets the same guard
for its own families: declare the verdicts your laws branch on and the width they read.

**The remedy for a red guard is to widen the generator.** Not to raise the iteration count, and not
to hunt a seed until the count turns positive: a coverage guard is satisfied by one folding trial in
three hundred, so both of those leave the law certified by that one trial. The counterexamples say
so in those words.

**What moved in the reports, and what a consumer must do.** No law changed its meaning and no
generated value moved. Nine families gained an adequacy law, so **their reported law LISTS grew** —
a consumer asserting a law count, or indexing into `ConformanceReport.Results` positionally, has to
adjust; a consumer reading `AllPassed` or matching on `Law` does not:

| Family | Was | Now |
|---|---|---|
| `IncrementalDelta.laws` | 9 laws, two of them hand-written coverage laws | 7 laws + 2 adequacy demands |
| `FoldConfluence.laneFoldLaws` | 5 (3 invariance + 2 coverage) | 4 (3 invariance + 1 adequacy) |
| `Conformance.footprintLaws` | 3 | 4 |
| `Conformance.mergeConflictLaws` | 3 | 4 |
| `Conformance.reconcileLaws` | 4 | 6 |
| `Conformance.arbitrationLaws` | 5 | 6 |
| `Conformance.capabilityPipelineIncrementalLaws` | 3 | 4 |
| `Conformance.dirtyPropagationLaws` | 4 | 5 |
| `Conformance.propagationEvalLaws` | 3 | 4 |

Those seven `Conformance` families are the ones whose laws only run on a sample that reached their
branch — an independent pair, a clean fold beside a conflicted one, an accepted proposal beside a
rejected one, a dirty node beside a clean one. None of them was vacuous on its pinned seeds; each
could have become so silently, which is what a guard is for.

**Two `*With` entry points, added so the guards have go-red proofs in the suite.**
`IncrementalDelta.samplesWith` / `lawsWith` take the generated table's row bound, which is the exact
lever Phase 115 had to move (from five rows to nine, having measured that most tables held one).
Narrowing it back turns that family's span demand red, in the suite, rather than in a session's
memory. `samples` / `laws` pin the shipped bound and are unchanged in behaviour.

**`LawResult` moved file** — from `Conformance.fs` to `SampleAdequacy.fs`, which compiles ahead of
it. Same namespace, same shape, same name: nothing a consumer can observe. It moved because the
guard produces `LawResult`s and every family declares its demands through the guard, so the guard
has to precede them, and it deliberately depends on no family it guards.

## The conformance kit's draw stream: xorshift32, Fable-identical (`0.20.0`) — BREAKING

`ConfRng` — the seeded generator every `Fuaran.Core.Conformance` law family draws from — was a
32-bit LCG (`state * 1664525u + 1013904223u`, off a multiplied seed mix). It is xorshift32 from
`0.20.0`: three shift/XOR rounds, no multiply anywhere in the path.

**Why it had to change.** A `uint32` multiply is the one arithmetic shape that does not survive
Fable — the product is formed on a double and its low bits are lost inside the operation, before any
mask could recover them (`Hash.mul32` documents the same defect on the FNV multiply, and
`Hash.(.+.)` on the SHA-256 add). The consequence here was worse than a divergence: at seed 1488 the
.NET stream `1547650046, 642982293, 2003517255, …` came out under Fable as `1547650048, 0, 0, …` —
the first draw very nearly right, and every draw after it the generator's fixed point. So a domain
certifying **in a browser** ran every self-contained `(seed, iterations)` family against a
degenerate sample: a guarded family reddened on its adequacy demand, and an unguarded one passed
**vacuously green**. `intBelow` had documented that no-32-bit-multiply constraint since it was
written and honoured it; `next`, one function above it, did not.

**What a consumer must do.** Every seeded sample changes — a family run at seed `s` draws a
different sample than it did at `0.19.0`, on both pipelines. Nothing about a law's meaning moved and
no report shape moved. The thing to re-read is any recorded seed: **a recorded seed means "a
sample", not "that sample"** — it reproduces a run against the generator of its release, which is
exactly what makes a counterexample replayable and exactly what makes it not a stored vector. A
consumer that pinned a drawn VALUE (rather than a seed) was pinning the generator, and must repin.

**It is guarded by measurement, not by review.** The `confRng/*` vectors in
`tests/fable-smoke/ParityVectors.fs` print the first eight draws for four seeds on both pipelines
and byte-compare them, so the multiply cannot come back quietly. Eight draws rather than one
deliberately: a single-draw vector would have read `1547650048` against `1547650046` as a rounding
difference rather than as the collapse it precedes. Measured by reverting the step: the whole .NET
suite stays green — it always could — while the parity leg reddens on all four vectors.

## The C# facade: an F#-free authoring surface over the closed unions (Phase 128, `0.22.0`)

`Fuaran.Core.CSharp` is a new package (the count is not restated here — `dotnet pack Fuaran.Core.slnx`
is the enumeration, and a written-down number drifts) carrying a C#-shaped facade over the closed
unions a non-F# authoring surface has to construct and read: the dataframe algebra (`ColExpr`,
`Transform`, and `Cell` / `ColumnType` / `BinOp` / `ScalarFn` / `AggFn` / `JoinKind` / `SortDir` /
`WindowFn` / `DataSource` / `Agg` / `WindowSpec` / `PivotSpec` under them), the artifact-function
declaration family (`ValueSpace`, `HoleKind`, `HoleDecl`, `EffectClass`, `SigEntry`, `Signature`), and
the wire JSON model (`JVal`). Construction is factory methods, reading is a total `Match` / `Switch`
per union, and a pipeline is a `Pipeline` value rather than an `FSharpList`.

**Purely additive: no F# surface moved.** Every existing package is byte-unchanged and carries the new
stamp only, so an F# consumer repins without reading anything. MINOR rather than patch on the `0.4.0`
precedent — adding a package to the published set is a contract change even when no existing
package's surface moves, because `dotnet pack` now emits one more nupkg and a consumer pinning
`0.22.0` gets an id that did not exist at `0.21.0`.

**What the promise IS, stated precisely, because the loose version is not checkable.** No public member
of the facade mentions a type from `FSharp.Core`, a type from a `Fuaran.Core.*` F# assembly, or a
positional `Tuple` / `ValueTuple`, at any depth of a generic argument — **except** on a member named
exactly `ToCore` or `FromCore`. That pair is the declared bridge, and it has to exist: the facade's
whole job is to hand Core its own values. The gate asserts the rule and PRINTS the bridge census, so
how wide the exemption actually is stays visible rather than merely permitted; when the facade
shipped it was 36 members, two per facade type.

**Three things this package deliberately is not.**

- **It is not Fable-clean, and it is not in the Fable-compile gate.** A C# assembly is not
  Fable-compiled, so the smoke project does not and must not reference it. The "public surfaces are
  FSharp.Core-only and Fable-clean" claim above is about the F# packages and is unaffected; this one is
  .NET-only by construction, and a consumer needing the same vocabulary in a browser uses the F# tier
  or a host implementation.
- **It holds no logic.** Every member either constructs a wrapped value, reads one, or forwards to a
  function the F# side already publishes (`Effect.join` / `covers` / `determinismTag`,
  `Space.validate`, `Json.tryRender` / `parse`, `DataFrameCodec.encodePipeline` / `decodePipeline`).
  Nothing is re-implemented, so there is no second semantics to keep in step.
- **It is not a general C# surface over Core.** It covers the unions a veneer AUTHORS. `Arg<'Node>`,
  the witness records, the op-stream and the evaluators are absent on purpose — the witness records
  are generic over a domain's own types, and a facade over them belongs to the host that supplies
  those types.

**The forward-coupling rule this creates, and it is the reason the coverage guard exists.** Adding a
case to `ColExpr`, `Transform`, `Cell`, `JVal`, `ValueSpace` or `HoleKind` now also adds a factory and
a `Match` arm to this package, **in the same change-set**. That is not a discipline to remember: the
proof's coverage leg reads the expected case list off the F# type through
`FSharpType.GetUnionCases`, so a case with no facade spelling cannot be produced by the generator, and
the gate reddens naming it. The widened `Match` signature is a compile break for a caller that passed
its arms positionally, which is the honest cost of a closed union growing and is the same cost the F#
side's exhaustive matches pay.

**Its own certification is a C# console proof, not an Expecto suite.** `./verify.ps1` and `./run.ps1`
run `tests/Fuaran.Core.CSharp.Proof`, on the reference-adoption-sample pattern. The claim is about what
a C# CONSUMER can express, and only C# consumer code can make it — a law about the facade written in F#
would be certifying the wrapped values, which is what the F# suite already does. Its four legs and
their go-red proofs are in DECISIONS.md D28, with the D9 exception this package rides and the
criterion for deleting it.

## Construct-then-encode: the authoring surface certified (Phase 126, `0.22.0`)

`Fuaran.Core.Conformance` gains one public type and one law family:

- **`ConstructWitness<'T>`** (`{ Surface: string; Construct: 'T -> Result<'T, string> }`) — how a
  domain rebuilds a decoded value through the smart constructors / builders an author writes
  against, rather than through the decoded record itself. `Surface` names that surface in the
  report, because a counterexample is read by a human.
- **`Conformance.constructThenEncodeLaws domain codec witness corpus`** — over a domain's existing
  `Corpus.Case` list: the corpus is non-vacuous, the authoring surface accepts every decoded
  document, and `encode (construct (decode b)) = encode (decode b)` for every one of them.

**Purely additive.** No existing type, function, signature or emitted byte moved, and no existing
family's verdict changes, so consumers repin without source changes. MINOR rather than patch on the
standing grounds: a new public type and a new public function in a shipped package are a contract a
consumer can pin against.

**What it certifies that nothing else did.** Every codec law in this kit certifies `decode` and
`encode` against each other. The authoring surface is a *different function into the same type*, and
a round-trip law never calls it — it starts at bytes and ends at bytes. So a field that widens in
memory to a richer carrier keeps a decoder-encoder suite green over thousands of vectors while
breaking every program that BUILDS a value. That is not a constructed example: it is the
`@fuaran-ui/ui` 0.26.0 release of 2026-09-11 (fuaran#1661), where the only author-direction consumer
broke on the pin bump against a fully green corpus.

**A domain opts in, and one that does not is named.** `witness` is an option. `None` reports a
single result — `construct-then-encode (<domain>): NOT ADOPTED — no ConstructWitness supplied` —
whose `Passed` is **false**. `LawResult` has two states and no third, and widening it would be a
compile-breaking change for every consumer that constructs one, so not-adopted is reported as not
passed: a family asked to certify a surface it was never given has certified nothing. Running the
family with `None` is therefore not a way to stay green; a domain whose subject this is not records
a reasoned non-use in its census row, as it does for every other family it does not use.

**The right-hand side is `encode (decode b)`, not the literal bytes `b`.** The law reads naturally
as `encode (construct (decode b)) = b`, and on a corpus written in its codec's canonical form that
is what it computes. A `Corpus.Case`'s JSON is not *required* to be canonical — `Corpus.roundTrip`
compares values, so a legal corpus may spell a document with a different key order — and comparing
against literal bytes would redden on the corpus's formatting rather than on the authoring surface.

**A refusal and a divergence are separate laws**, so a red is never misattributed: a constructor
that rejects a value the domain's own codec just decoded fails the acceptance law, and the
construct-then-encode law stays green because nothing was built to compare. When the law itself
reddens, the counterexample states whether the plain codec round-trip passes over that same document
— it usually does, and a reader meeting the red for the first time should not have to establish it.

**Go-red proof.** `tests/Fuaran.Core.Tests/ConstructThenEncodeTests.fs` plants the fuaran#1661 shape
over the reference string-id witness: an authoring constructor that widens a leaf's text into a
child source node. Every resulting tree encodes and decodes perfectly, `Corpus.runCorpus` is green
over the same corpus in the same test, and the construct-then-encode law reddens naming the
document. The not-adopted report, the vacuous corpus, the reject-cases-are-not-documents rule and
the refusing surface each have their own case.

Adoption guide: [`docs/construct-then-encode.md`](docs/construct-then-encode.md).

## One classifier for the wire-profile bump, and the F# consequence table (Phase 127, `0.22.0`)

Two things ship, and they are the same thing at two altitudes.

**`Fuaran.Core.Idl.Codegen` gains, in `Diff`,** the entry point the classification never had over
values plus the axis it never reported:

- **`Diff.classifyDiff : Idl -> Idl -> Result<Verdict, string>`** and
  **`Diff.classifyArtifacts : string -> string -> Result<Verdict, string>`** — one classifier, two
  doors. The value door renders through `Artifact.render` deliberately, because the artifact is the
  published contract and the render is lossy by design, so classifying the values directly would
  classify more than the contract does.
- **`Diff.Verdict`** — the rows, the wire `Versioning.Evolution`, the undecided rows, whether
  emitters break, the F# consequence set, and the two prose drafts `report` already emitted.
- **`Diff.FSharpConsequence`** + `consequences` / `consequenceLabel` / `consequenceWhy` /
  `allConsequences` / `consequenceTable` — what a change does to a consumer's F# source compiled
  against the generated structural layer, which is a **different question** from what it does to a
  document.
- **`Diff.VerdictClass`** + `verdictClass` / `classLabel` / `classOfLabel` / `allClasses` /
  `exitCode` — the one word a gate branches on, and the codes it branches with.
- **`Diff.bumpProfile : Versioning.Profile -> Verdict -> Bump`**, `Diff.verdictBlock`,
  `Diff.runVerdict`.

**A twelfth package: `Fuaran.Core.Idl.Cli`,** packed as a dotnet tool whose command is
`fuaran-core-idl` (`classify <before.json> <after.json>` / `table`). Adding a package id is a
contract change even though no existing package's surface moves — the `0.4.0` precedent — so it is
recorded here rather than passed over as additive. `dotnet pack Fuaran.Core.slnx` now emits twelve
nupkgs where `0.21.0` emitted eleven. .NET-only and build-time, with no Fable source distribution,
for the reason `Fuaran.Core.Idl.Codegen` states.

**Purely additive for the eleven existing packages.** `Versioning.classify` and `Versioning.bump`
keep their signatures and their values; `Diff.changes`, `Diff.classify`, `Diff.report` and
`Diff.run` are untouched, and the report's existing bytes are unchanged — the verdict block is
appended by a separate renderer. Consumers repin without source changes. MINOR rather than patch on
the standing grounds, twice over: new public types and functions in a shipped package, and a new
package id.

**Why the second axis exists, and why it is not derived from the first.** The wire severity answers
"what happens to a document". It does not answer "what happens to a consumer's source", and the two
diverge routinely in both directions. An **optional** field added is `Additive` on the wire — every
existing document is byte-unchanged and stays valid — and it stops every full record literal
compiling, because an F# record literal must name every field whether or not its type is an
`option` (`FS0764`). A **`hostSurface`** edit is the same divergence reversed: invisible on the
wire, and it moves a generated field's type. So a caller reading only the wire verdict is told a
consumer repins without source changes in exactly the cases where it does not. The three classes —
construction sites (`FS0764` / `FS1129` / `FS0001`), match sites (`FS0025`, a warning by default and
a `MatchFailureException` at run time; `FS0039` on a removal) and the stale package slot (no compile
event at all, an `InvalidCastException` from code that type-checked) — are stated **once**, in the
code that decides them, so external surface guards and corpus gates cite one table instead of each
re-deriving the mapping from the compiler's behaviour.

**The bump routes through `Versioning.bump` rather than restating its rule.** `Diff.evolution`
builds the two subject sets `Versioning.classify` compares — the members a revision retires, the
members it introduces — and hands them over, so the additive-vs-breaking rule has one definition and
this answer moves when it moves. `profileBump`'s prose is kept and is unchanged; what it could never
do is be branched on, which is why nothing downstream called `Versioning.bump` at all before this.

**An undecided verdict yields no profile.** `Bump` has a second case, and `bumpProfile` returns it
whenever any row crosses an erased slot the artifact does not describe. Returning the base profile
unchanged, or a minor, would hand a caller a number to publish for a revision whose class nobody has
established — which is how a `/v2/` event gets published as a minor. For the same reason
`VerdictClass.Undecided` takes precedence over every other class, and the command's exit codes keep
"I cannot tell" (`4`) separate from "this breaks" (`3`) and from "I reached no verdict at all" (`2`).

**Still advisory.** Nothing here writes a file, bumps a version or gates a build on its own account;
a gate that wants to stop is the one that reads the exit code. The reason is the one `Diff` has
carried since Phase 700 — a classifier that applied itself would make the hand-declared
classification unfalsifiable, and the retroactive validation depends on the two being independently
produced.

**How the compile claims are held.** Compiled, not asserted:
`tests/Fuaran.Core.Tests/IdlStabilityClassTests.fs` spawns the real F# compiler over the real
generator output for `FS0764` and `FS0025`, each in **both** directions — the perturbed consumer
must fail and the adapted consumer must pass — so a leg that has stopped measuring anything is
visible rather than green. Beside them a property over every declared perturbation asserts that the
consequence reported tracks what the generator's **output** structurally exhibits, read off the
emitted text rather than re-derived from the IDL, with its own non-vacuity guard; restating the
mapping in the assertion would have certified the suite against itself. The command is exercised as
a command over three committed fixture artifacts — both calling shapes, both directions of the
`--expect` assertion, and every refusal — and the fixtures are generated from the same declarations,
with a guard that fails naming the regeneration command when a committed file and its declaration
disagree.

Reference: [`docs/idl-stability-classes.md`](docs/idl-stability-classes.md).

## The hardening policy's UNDECLARED half (Phase 178) — ADDITIVE, and it ADVANCES the slot

**The class.** Additive on every published surface. `Fuaran.Core.Idl` gains a
`HardenPolicy.Undeclared` static member; `Fuaran.Core.Idl.Codegen` gains
`Trust.checkHardenPolicy`, `Trust.hardenOrRefuse` and one `CodegenError` case
(`UndeclaredHardenToken`). Nothing existing changes shape: `HardenPolicy`'s members keep their
types, `HardenPolicy.Default` keeps its tokens, `Trust.harden` keeps its signature and its
behaviour, and no artifact's bytes move. A vocabulary on the default cannot reach the new refusal —
the default declares every member — so adoption is an opt-in in the strict sense: you declare
`Undeclared` (or leave a member empty) and call the checked entry point, or nothing about your build
changes.

The `CodegenError` case follows this document's own precedent: Phase 150 (`0.24.0`) classed a new
case on the same union ADDITIVE for the same reason it is additive here — the union is matched
nowhere outside `Fuaran.Core.Idl.Codegen`, measured, and the new case is reachable only from a leg
that did not previously exist. A consumer that DOES match it exhaustively would gain a warning, not
a silent wrong answer; against a same-version repack of the slot it would be the runtime skew the
workspace's pack guard classifies, which is why the slot advances rather than being repacked.

**It ADVANCES rather than riding the draft.** The standing `<Version>` is `0.24.0`, and `v0.24.0` is
tagged and published (2026-09-15). That slot is a released contract, not a draft, so under the
draft-slot rule this cannot ride it: the commit that carries this work advances `<Version>` to the
next slot. The number itself is the release gesture's to move, not this phase's.

**What this phase deliberately did NOT change, and why the class would have been different.** Phase
178 was written to FLIP the default — `Undeclared` replacing `Default`, `Trust.harden` itself
refusing — which would have been **breaking for a vocabulary that declared nothing**, and that is
the class this document would be recording had the flip landed. It did not, because the flip's
licensing premise was measured and refuted before the work: thirteen declaration sites across the consuming repositories
take their tokens from the default (the UI tier's own `Vocabulary.fs` among them), and both
published `idl.json` artifacts — including the shared cross-host corpus — carry no `harden` block at
all, which `Artifact.readHarden` resolves through `HardenPolicy.Default` by a promise stated in its
own doc comment. Emptying the default would have changed what already-published bytes MEAN, for
every host that reads them, with a green build. [`DECISIONS.md`](DECISIONS.md) D40 carries the full
measurement, the compat promise, and the migration route if the flip is ever wanted.

## 0.34.0 — DRAFT

**Slot class: breaking (source).** Opened as an additive slot over the tagged `0.33.0` and reclassed
breaking before any tag, when Phase 252 widened `HostedCodec` (two fields) and retyped `Gen.fsharpValue`
and `Gen.typescriptValue`, and Phase 248 retyped `ArbitrationRejection.Conflicts`. Each entry below names
its own class and the edit a consumer makes; the wire classes are recorded per entry. Pre-1.0 a breaking
change is a minor bump, which this slot already is over `0.33.0`, so the number does not move.

**Phase 331 — `tests/Fuaran.Core.Tests/ConformanceTests.fs` is split along the conformance kit's topic
files. Class: `additive`, and the whole of it is tests: no package's public surface moves (the seventeen
surface baselines read, none moved).** Phase 297 split the kit's source into topic files and deferred the
matching split of its test file, because sibling phases were appending cases to it. That file held
the cases of every law family in one 2,400-line module, so a change to one family edited the file every
other family's phase was editing.

Seven test files now mirror the kit's topic files — `TreeLawsTests`, `StreamLawsTests`,
`IntegrityLawsTests`, `SeamLawsTests`, `FunctionLawsTests`, `PropagationLawsTests` and
`SurfaceLawsTests` — registered in the test project in the kit's compile order. Each case moved verbatim
(names, bodies, order within a topic), and the list a case is registered under kept its name, so the
Expecto test list is identical before and after, by name and by count (1,531 listed lines each).
Three lists keep their old names over cases that now live in other files — the `casLaws` cases still read
`Conformance.functionVerify/…` — because the name is the identity a filter or a recorded result keys on.

`ConformanceTests.fs` keeps what several files compile against: the reference domains and generators
(the counter stream witness, the keyed and formula-sheet domains, the validity oracle), the guard helpers
the topic files share, and the cases that exercise the facade's `certify` rather than one family. Moving
the domains into topic files would have rewritten every file that names them, which is a different change
from this one. No assertion changed.

### The F\* target encodes the wire shape it declares, the classifier's rules are one table with `support.json` as an input, and `Codegen.fs` splits behind the `Gen` facade with one type emitter (Phase 293) — BREAKING (source) in `Diff`, inside the slot's standing class; `Gen`'s baseline moves by zero lines; `Gen.fsharpTypes`' OUTPUT changes

**`Fuaran.Core.Idl.Codegen`. Class: `additive` on the baseline — `Diff.mappingTable : string` is
the one public addition (the seventeen surface baselines read, one moved by that line). Three
classifier VERDICTS consumers read through the `classify` command move, and they are the point.**

*The F\* proof model.* `FStarTarget` used to emit one shape whatever the vocabulary declared: the
nested `("id", …) :: ("kind", …) :: envelope` object, every member list Ordinal-sorted, with the
fixed keys prepended — so the committed `DocVocabulary.fst` announced a flat-kind envelope in
declaration key order and modelled a document nobody sends. Every object literal is now laid out
in the declared key order with the keys the shape places (the discriminator's tag, the node's `id`,
the nested kind object) merged among the members under `Sorted` and leading them under `Declared`;
under the flat envelope the node IS the kind union — one constructor per kind over the one object
the wire has, `id` and the kind's and envelope's members together, and no `vkind` type at all. A
member whose key the shape already carries (a flat kind field named `id` or the discriminator, or
sharing an envelope member's key; an envelope member named `kind` under the nested shape; a union
case field named the discriminator) is a typed `UnmodellableInFStar` naming the member and the
object, never a literal with two entries under one key. The six committed models and proof scripts
are regenerated and every query discharges on the pinned prover under `--quake 3`; the lookup count
the proof shape emits now includes a fixed key that sorts after a conditional member (the reference
vocabulary's `id` and `kind` after `hidden`), which is two more lemmas on `VocabularyProofs.fst`. A
test holds each model's header to its encoder's text, and the encoder's key order to the
INTERPRETER's (`Encode.encode`) over a sampled node per plain arm — the oracle is never the model.

*The classifier.* `Diff`'s rules lived in seven parallel matches over the `Change` union plus a
hand-copied table in `docs/idl-stability-classes.md`. They are now ONE descriptor table —
rank, §11 family, wire verdict, F\# consequence, summary, documented rows — of which `classify`,
`consequences`, the report's sort order, its one-line summaries, the three family predicates and
`Diff.mappingTable` are projections; `stabilityImpact`, `profileBump` and `verdictClass` read one
precedence (undecided, wire break, emitter break, addition, host-surface). The document's mapping
table is a generated section held byte-equal to `mappingTable` by a test, with a row for every
`Change` case by name. Three documented rows read differently and the code was right in each (a
union's type parameters moving is `host-surface-only`; `omitDefault` to `required` is
`breaking-for-emitters`); the third was a code defect: the erased-slot rule tested the top-level
tag alone, so a type change across a NESTED `hosted` / `json` / `opaque` slot classified
`breaking-wire` where the document said undecided — it is `undecided` (exit 4) at any depth now.

*The two F\# consequence verdicts that change.* `defaultsDecl` makes a `mk<Kind>` parameter of every
required field with no authoring default, so (1) an authoring default added to or removed from a
required field, and (2) a kind field moving into or out of `required` with no default to stand in,
each move the constructor's parameter list — `full-literal-construction` (every call site) where the
axis read `no-generated-shape-change`. The consequence property test now reads the emitter that
emits the constructors (`Gen.fsharpModule`), which is what found the second; the context-free
`Diff.consequences` reads a default as a required field's, and the verdict over two artifacts asks
the snapshots.

*The generator.* `GenSupport.Docs` paths, `CaseRefines` keys and `KindProjections` keys are held to
the vocabulary by `Gen.fsharpModuleWith`: a doc path the emission never consults, a refine on no
referenced case, a projection on no selected kind — each a typed `UnsupportedConstruct` naming the
key where it used to be a silent drop. The emitted header names the package version
(`Fuaran.Core.Idl.Gen 0.34.0`) in place of a phase label frozen at first emission; a consumer's next
regeneration moves that one comment line. The bare alias `defaultExpr` and the unused
`reachesDeclared` / `bindNode` are gone; the F\* suffix-chain fold is linear.

*The split (the phase's second task, landed after the first close-out).* `Codegen.fs` (4,761
lines) is gone; the emitters are `internal` modules under `src/Fuaran.Core.Idl.Codegen/Emit/` —
`Core` (the error channel's helpers, the one `Result` sequencer, naming and mangling, the
msg-carrying analysis, the F# type spelling, the emitters' mirror of the support records, and the
one name-to-declaration index with `findKind` / `findUnion` / `findEnum` / `findRecord`), `Reach`
(the one reachability walk), `Annotations` (the single landing site for declared annotations, with
the stated precedence: a declaration's comment is its category, then the SUPPORT doc block, then the
AUTHORED annotation lines — `memberDocLines`), `FSharpTypes` (THE ONE TYPE EMITTER: `typeGroup`),
`FSharpDefaults`, `FSharpCodec`, `JsonSchema`, `TypeScript`, `Scaffold` — and `Gen.fs` is a facade:
the published `KindProjection` / `GenSupport` declared there, every entry point a one-line forward.
`FStar.fs` consumes `Emit/Core.fs` (its three private finders are the shared ones). **The baseline
`api/Fuaran.Core.Idl.Codegen.txt` moves by zero lines for the split** — every `Gen.*` line and both
record types are byte-identical; the lines that do move are the classifier widening below.

*`Gen.fsharpTypes`' OUTPUT changes — class `breaking` for a consumer that pins the emitted text, and
it is the change one type emitter implies.* The old `fsharpTypes` was a second, older emitter: every
enum, union and kind as its own `type` declaration, no `RequireQualifiedAccess`, no records, no
`NodeKind` / `Node`, no wire-equality members. It is now the ONE emitter's projection with no
declared support: the enums as `[<RequireQualifiedAccess>]` types, then unions, records, kind specs,
`NodeKind` and `Node` as one `type … and …` recursion group (unions and `NodeKind`
`RequireQualifiedAccess`, attributes inline after `and`), category comments on specs, and the Phase 252
wire-equality members where a host-only field earns them — exactly what `fsharpModuleWith` renders
inside the module, which is the point: there is no longer a declaration the two could render
differently. A consumer compiling against the text gains declarations (`Node`, `NodeKind`, the
records) and loses nothing; a golden that pinned the old text re-pins. The generated MODULES are
byte-identical across the split (`MiniGenerated.fs`, `DocGenerated.fs`, `DocAnnotatedGenerated.fs`
regenerate unchanged), and the repository's own type-leg pin now reads a member whichever keyword
joins it.

*`support.json` joins the classifier's inputs — the three widenings the operator accepted, each
classed honestly.* (1) `Diff.Change` gains `SupportChanged of key * before: string option * after:
string option` — **breaking for an exhaustive matcher** over the union (`FS0025`, an error under
`TreatWarningsAsErrors`): the one case the published shape could not express, keyed `doc:<path>` /
`splice:<slot>` / `refine:<Union.Tag>` / `projection:<Kind>` / `prelude`. (2) `Diff.Snapshot` gains
`Support: Map<string, string>` — **breaking for a full record literal** (`FS0764`); a consumer that
reads snapshots through `Diff.snapshot` / `parse` is unaffected and sees an empty map. (3) The
`classify` command of `fuaran-core-idl` (the CLI package, outside this phase's declared files) takes
`--support-before <support.json> --support-after <support.json>`, both or neither (one side alone is
refused: a support document on one side is a diff against nothing) — additive. The new library doors
are `snapshotWith`, `parseWith`, `classifyArtifactsWith`, `classifyDiffWith`, `runWith` and
`runVerdictWith` (additive); the old doors delegate with no support and classify exactly as before.
The verdict: every support move is `host-surface-only` on the wire (host-language source the generator
splices, never a wire fact); a kind projection or the type splice is `full-literal-construction` on the
F# axis (generated declarations move), everything else `no-generated-shape-change`. A projection or
refine edit beside an unchanged vocabulary therefore classifies `host-surface` (exit 0) where it read
`unchanged`, which is the row the phase was filed for. The slot's class is already `breaking (source)`,
so the number does not move.

*Consumer edit.* A full `Snapshot` literal names `Support`; an exhaustive `match` over `Change` gains a
`SupportChanged` arm; a golden over `Gen.fsharpTypes` re-pins. A gate that pins a classifier verdict
re-reads the three rows above; a regenerated proof model is a regenerated file.

### The `OpStream` module becomes a forwarding facade over the concern files (Phase 332) — ADDITIVE; no public surface moves

**What changed.** Phase 296 split `OpStream.fs` by concern at the type level and left the `OpStream`
module's own members in one file, so that file still held every concern's code. Each member's body
now lives in the file that owns its concern, in an internal module there — `Chain.fs` (the chain
hashes, the payload configs, append, verification, rehash, replay, the head, compare-and-append and
idempotent append), `Jsonl.fs` (the one scanner and the record reader and writer), `Snapshot.fs` (the
snapshot family and its pre-Phase-296 forwards), `Capture.fs` (capture / replay and attestation) and
`Attributed.fs` — and `OpStream` (`OpStream.fs`, compiled last) keeps every public name, the nested
`OpStream.Jsonl`, `OpStream.Snapshots` and `OpStream.Attributed` included, as a forward with the same
signature, attributes and documentation. `Jsonl.fs` now compiles before `Snapshot.fs`, ahead of the
readers built on its scanner; the `fable/` source distribution ships the project file that says so.
Two documentation edits ride it: `OpStream.appendIf` now carries the compare-and-append contract,
which was written above the private core it calls and so never reached a consumer's tooltip, and two
doc comments that pointed "above" / "below" at code that moved name its file instead.

**What adopting it costs.** Nothing. `api/Fuaran.Core.OpStream.txt` is byte-identical, every error
string is byte-identical (the bodies moved verbatim, the `OpStream.snapshotAt: …` strings the
compaction proof model pins among them), and the proof cone over `proofs/Chain.fst` verifies with its extracted oracle
byte-identical to a fresh extraction. A forward of a hash function keeps the lambda form
(`fun prev payload -> …`): written as a bare value it would compile to a property rather than a
two-argument method, which the baseline refuses as a `removal`.

**Class: additive.** No public surface moves; a change to one op-stream concern now edits one file.

### One sanitisation floor, total on hostile input, with the URL floor at parity with the UI tier's (Phase 291) — ADDITIVE; no public surface moves

**What changed.** `Fuaran.Core.Idl.Sanitize` is the floor `Trust.harden` sends every declared URL and
markdown field through, and it had two defects. Neither changes a signature; `api/Fuaran.Core.Idl.txt` is
byte-identical.

**1. `scrubMarkdown` took indices from a lowered copy of the string and applied them to the original.** The
event-handler scan and the `javascript:` / `vbscript:` loop both did (`s.ToLowerInvariant()` then
`IndexOf`, then `Remove` / `Substring` on `s`). On .NET `ToLowerInvariant` keeps the length. Under Fable a
string lowercase is `toLowerCase()`, which turns U+0130 into two units, so each `İ` shifts every later
index by one: the wrong characters are replaced, and with enough of them and a tail the scheme loop
replaces past the real match forever. Every scan now compares per unit on the string it indexes —
`foldAscii` (`A`-`Z` to `a`-`z`, nothing else) inside `indexOfFolded`, and the same fold inside the handler
scan — so no index is ever taken from a string of another length, on either pipeline. The element sweep
moved onto the same helper: `IndexOf(…, OrdinalIgnoreCase)` is a comparison whose Fable lowering this
repository cannot see, and the one function whose contract is "total on hostile input" should not lean on
it. The scheme loop is bounded by a `searchFrom` cursor that advances past every replacement, as the UI
tier's copy is; resuming at the cursor loses no match, because `about:blank` holds no `j` or `v` and its one
`:` follows `about`, which is the tail of neither scheme (the Phase 96 splice case is still in the corpus
and still passes). On .NET the output is byte-identical to before for every input: the fold is the one
`OrdinalIgnoreCase` already applied to these ASCII needles, and the one character whose invariant
lowercase is ASCII under .NET (U+212A) occurs in no token this module scans for.

**2. The URL floor was not the UI floor.** Compared clause by clause (`Fuaran.UI.EmissionGrammar`'s
`normalizeUrlForFloor`, `isProtocolRelative`, `sanitizeUrl`):

| Clause | UI floor | Core before | Core now |
|---|---|---|---|
| Edge removal | every unit at or below U+0020, both ends | `String.Trim()`: Unicode white space only | as the UI floor |
| Interior TAB / LF / CR | removed | kept | as the UI floor |
| Interior VT / FF | kept (the parser keeps them) | kept | as the UI floor |
| Protocol-relative pair | any two units drawn from `/` and `\` | `//` and `/\` only | as the UI floor |
| A single leading `\` | allowed (reads as `/`) | allowed | as the UI floor |
| Value returned | the normalised string | the trimmed string | as the UI floor |
| Scheme extraction, allow and reject sets, default-deny, empty string | — | identical | unchanged |

The clauses the UI floor held that Core lacked are therefore four: the C0 half of the edge removal (NUL,
U+0001-U+0008, U+000E-U+001B), the interior TAB / LF / CR removal, the `\\` and `\/` halves of the pair, and the
normalised return. So Core accepted `\\evil.example`, `\/evil.example`, `/<TAB>/evil.example` and
`<U+0001>//evil.example`, each of which a browser normalises to an off-origin `//evil.example`; all four are
refused now. The clause table is a test (`urlFloorClauses`), so a divergence is a red case naming its
clause.

**What adopting it costs.** Nothing at the call site. One consequence is worth reading: the edge removal no
longer takes non-ASCII white space (U+0085, U+00A0, U+2028, …), because the parser keeps it, so
`<U+00A0>//host` is now returned as an ordinary relative path where Core used to refuse it. That is the
UI floor's choice and the reason for the exact normalisation; a consumer that trims the value again itself
before use re-opens the hole the floor exists to close, and should not. Both floors still classify a scheme
candidate through `Trim()` and `ToLowerInvariant` (the candidate has already lost everything at or below
U+0020); that residue is shared, default-deny covers it, and it is unchanged here.

**The corpus.** `IdlCertificationTests` gains, with the expected output stated as a literal so both
pipelines can share the oracle: `İ` runs of 1, 3, 11 and 12 before each scheme with and without a tail,
bare and inside an `href`; a lone surrogate (high, low, and a doubled form) before a scheme; the handler
and element scans after an `İ` run; and the URL clause table. Each case runs under a 20 s bound.

**Class: additive.** The floor refuses more (the four pair spellings, and a scheme after any number of
`İ` under Fable, which the old scan could miss or loop on) and nothing it accepted that was safe is
refused. The narrow reverse edge above, strings it used to refuse and now passes through unchanged, is the
parity the phase exists to establish.

### Rejections that explain themselves: `Ops.interference` names the clause, `Conflicts` carries it, and a stale proposal has a bounded report (Phase 248) — BREAKING-SOURCE: `ArbitrationRejection.Conflicts` gains a field (`retype`); the rest `additive`

**What changed.** A `Conflicts` rejection named WHO interfered and not HOW, so a party that wanted
to know what to rebase against — the parent it shares, the id the other script reads, the
relocation that serialises it — re-derived `Ops.independent`'s clauses itself, in glue that copied
private logic and could drift from it. And a stale proposal's rejection reused the op-algebra's
`UnknownNode` envelope, whose `addressable` is every id in the base: at a 465-node document, 465 ids
per stale proposal, sent again to every party a scheduler reports to.

- **`Interference`** (new, `Fuaran.Core.Ops`, qualified access) — one case per clause of
  `independent`, in its order: `SameTarget`, `LeftWritesRightReads`, `RightWritesLeftReads`,
  `SameParent`, `LeftUnknownParent`, `RightUnknownParent`, each carrying the addresses it fails on
  (an overlap's one shared set; the unknown-parent clauses the relocated ids on one side and the
  structural writes on the other).
- **`Ops.interference : Footprint -> Footprint -> Interference list`** (new) — every clause two
  footprints fail. **`Ops.independent` is now DEFINED as `interference a b = []`**, so the verdict and
  its explanation have one source. Its verdict did not move: the suite holds the redefinition to the
  previous clause-by-clause conjunction on every ordered pair of the 256 footprints over a two-address
  universe, in which every clause fires.
- **`ArbitrationRejection.Conflicts` gains a second field, `interference: (int * Interference list) list`**
  — BREAKING-SOURCE, `retype`. Every cited id, in the same order, paired with the clauses its footprint
  and the rejected proposal's fail (the proposal on the left), each list non-empty; `interfering` is
  unchanged and is always `List.map fst interference`. **The consumer edit:** a match on `Conflicts`
  gains a field — `| Conflicts ids ->` becomes `| Conflicts(ids, _) ->` (or `Conflicts(ids, clauses)` to
  read the explanation); a match written `| Conflicts _ ->` still compiles; a constructed `Conflicts ids`
  becomes `Conflicts(ids, clauses)`, and an expected value written as a `Conflicts` literal now has to
  state the clauses or compare the ids alone. The rejection's wire is unaffected: no Core package
  encodes `ArbitrationRejection`.
- **`Arbitration.interference nodew idw accepted proposal`** (new) — the same per-pair list as a query,
  for a party that holds a proposal and an accepted set but no rejection. `arbitrate` computes both
  `Conflicts` fields through the one helper this function calls, so handed an arbitration's `Accepted`
  and a `Conflicts`-rejected proposal it returns exactly that case's `interference` member.
- **`StaleProposal<'Id>`** (new record: `OpIndex`, `Missing`, `AddressableCount`, `Sample`),
  **`Arbitration.stale`** (new: `Some` for an `Inapplicable` whose envelope is `UnknownNode`, `None`
  otherwise) and **`Arbitration.staleSampleSize`** (new, `8`). **The bound:** a stale proposal's
  report is one id, two integers and at most eight sampled ids (the base's first eight in pre-order),
  whatever the document's size.
- **Documentation only:** `Rejection.UnknownNode` now states what `addressable` is for — a repair
  aid on the single-op `canApply` path, where a model repairs one op and needs every id it could
  have meant — and that a party reporting a stale script to others reports `Arbitration.stale`
  instead.

**What did NOT change, deliberately.** `Inapplicable` still carries the op-algebra's own envelope,
`addressable` in full: the bounded report ships beside it rather than in it, because the full list is
the right payload for the single-op repair it was built for and a scheduler can choose the bounded one.
`arbitrate`'s partition, its `interfering` citations and its envelopes are what they were; the formal
model (`proofs/Arbitrate.fst`) states its theorems over the citation, which did not move, and the oracle
differential compares that citation.

**Class: breaking-source**, for the one `retype` on `Conflicts`; everything else — one new union, one
new record, four new module values — is `additive`. Pre-1.0, an untagged draft's minor carries a
breaking change without a new number. `api/Fuaran.Core.Ops.txt` records the case's new field and the
new members.

### `Json.parse` reads every float `Canon.render` writes; the canonical-float family samples the whole double range (Phase 253) — ADDITIVE: the read side widens, no emitted byte moves

**What changed.** The canonical float layout (WIRE_FORMAT §2 rule 5, `FloatLayout.finite`) writes a
finite double whose base-10 exponent is 15 or 16 in fixed point, so `1e16` renders as
`10000000000000000` and `9007199254740994.0` as `9007199254740994` — integer tokens past 2^53, which the
parser's int53 guard refused. A document Core wrote could not be read by Core. The parser now reads an
integer token past 2^53 **exactly when it is the canonical float layout of the double it reads as**, as
that `JFloat`; it re-renders to the same token. Every other integer token past 2^53 is refused as
before, with the same message: `9007199254740993` (2^53 + 1), a 19-digit identifier, and the exact
value of a double whose layout spells it differently (`18205257897171752`, whose double's layout is
`18205257897171750`). DECISIONS D90 records why the read side moved rather than the layout.

`Conformance.canonicalFloatLaws` builds one float per iteration in each of four strata — the old
small-magnitude spread, the whole normal range, the subnormals, and the integral range from 2^53 to
2^57 — and asserts the edges every run (the largest finite and smallest subnormal of each sign, the
largest subnormal and smallest normal, 2^53 and its neighbour, both ends of the fixed-point window, the
Int32 edges, both zeroes). Its round trip now runs through the parser over that whole range, and a
fourth law asserts the fixed point: the parsed value re-renders to the very bytes it was read from. With
the parser change reverted, the family goes red at its first iteration, in the integral stratum.

**What adopting it costs.** Nothing to compile against: no public signature moves and every
`api/` baseline, managed and wire, is unchanged — no `Canon.render` byte moves. Three observable
differences, each a read that used to be refused:

- `Json.parse` (and every reader over it) returns `Ok (JFloat …)` for a canonical integer-shaped float
  layout past 2^53 where it returned a `MalformedNumber`. A caller that relied on that refusal to keep
  identifiers past 2^53 out keeps it for every token that is not such a layout; an identifier in that
  range travels as a string, as before.
- The columnar decoder's vector `decode-refuses-integer-token-past-2-53` (`conformance/laws/
  decimal-laws.json`) is still refused, now by the decimal column (`TypeMismatch`, a whole float past
  2^53) rather than by the parser (`NotJson`). The shared corpus's copy of that file is re-emitted with `--emit-laws <corpus dir>`.
- `canonicalFloatLaws` reports four laws, not three; a census over it counts 2000 cases at 500
  iterations where it counted 1500.

**The proofs move with it.** `proofs/JsonParse.fst`'s float reader gains a verdict, `FCanonical`;
`int53_guard_exact` now reads "int53-safe, or the canonical layout of its double",
`int53_guard_refuses` excludes that layout, `canonical_layout_past_int53_is_read` is new, and the
oracle is re-extracted. `proofs/WireCanon.fst` gains a second numeral premise,
`integral_float_reads_back`, and `every_finite_float_reads_back` on it; the oracle host evaluates that
premise against production's parser and against a reader with the old guard, which must lose past 2^53
and only there.

**Class: additive** — a decode widening; nothing a consumer pins as emitted moves.


### The IDL serves a vocabulary that is not the UI's (Phase 252) — BREAKING (source): one record widened, two emitters retyped

**What changed.** A second vocabulary taken through every IDL leg found the legs disagreeing with each
other and assuming the UI vocabulary. Seven corrections, one change each.

- **A hosted slot declares its wire form.** `HostedCodec` gains `Wire: IdlType option` (what the codec
  writes, as an IDL type) and `Format: string option` (a closed string format on a string wire:
  `date`, `date-time`, `uuid` — `HostedFormat`). A declared form is what every leg but the F# host
  reads: the sampler draws from it, `Gen.jsonSchema` states it (with `format`), the TypeScript module
  encodes through it and its decoder checks it, `Decode` (the interpreter) refuses a value outside it,
  and the generated F# decoder checks it before the host codec runs. Without one (`Wire = None`)
  every leg carries the JSON verbatim as before. The artifact writes a declared form as the hosted
  type's `wire` (a type object) and `format`; an undeclared slot still writes `"wire": "json"`,
  byte-for-byte as before. `Declare.hostedWireErrors` reports a malformed declaration, and the
  generators refuse one as `CodegenError.UnsupportedConstruct`.
- **`Gen.fsharpValue` returns the typed refusal** (`Result<string, CodegenError>`, was
  `Result<string, string>`), resolves an enum literal through `IdlEnum.CaseOf` to the host case,
  emits a `TRecord` value as a record expression, writes a union case's `Optional` field as
  `Some(…)` / `None` as its declaration says, and scaffolds a hosted slot that declares its wire form
  through its own `Decode` over an escaped `JVal` literal. What remains (an undeclared hosted slot, a
  closure, JSON, a map, a sentinel, an op slot) is refused as `UnsupportedConstruct`.
- **`Gen.typescriptValue` fills declared defaults.** It took the value alone and so could not know a
  default; it now takes the vocabulary and the slot's type (`typescriptValue idl t v :
  Result<string, CodegenError>`) and emits the generated decoder's shape — every omit-at-default
  member present — so the generated encoder no longer writes `undefined` or a default the wire
  omits. **`Gen.typescriptDeclarations idl kindTags`** (new) emits the `.d.ts` beside the module.
- **The checked scaffold no longer requires a gated kind.** `Trust.checkHardenPolicy` asks for the
  gate's members only when a gated kind is declared; it still refuses, by name, a vocabulary that
  declares a kind carrying `moduleId` and `componentId` (the fields the gate reads) and no gate, and a
  declared markdown field now needs the text-literal members on its own account.
- **The artifact carries the vocabulary's identity.** `Artifact.renderWith identity idl` writes the
  vocabulary's own `description` and `name`; `Artifact.identityOf` reads them back, so an authored
  `idl.json` re-renders to itself. `Artifact.render` is `renderWith Artifact.defaultIdentity`, its
  bytes unchanged.
- **The classifier's host roster is the manifest's `hosts` and nothing else.** Without a manifest, or
  with one declaring no `hosts`, no host is obliged by name and the report says so; the UI tier's own
  rows follow its reference host `fuaran` when a roster declares it. `Diff.declaredRoster` stays, for
  a caller that passes it explicitly.
- **Two classifier rules corrected** (`docs/idl-stability-classes.md`): `int` to `float` (anywhere in
  a type) is an `additive` widening, exit 0, not `breaking-wire`; a move in a **hosted** slot's
  `hostSurface` block (its host type, `encode` or `decode`) is `undecided`, exit 4, not
  `host-surface-only`. On the F# axis a `hostSurface` move is now decided from the block: a
  construction break when `fsharp` moved, `no-generated-shape-change` otherwise.
- **Generated wire equality** (DECISIONS D91). A generated record, kind spec or node holding a
  host-only CLOSURE, not generic in `'Msg`, whose wire fields all compare, takes
  `[<CustomEquality; NoComparison>]` with `Equals` / `GetHashCode` over its wire fields, so the
  tree-algebra families accept the generated layer. A vocabulary without such a declaration emits
  byte-for-byte what it did.

**What adopting it costs — the consumer edits.**

- Every `THosted { FSharp = …; Encode = …; Decode = … }` literal adds `Wire = None; Format = None`
  (FS0764 until it does), or declares the slot's form: `Wire = Some TStr; Format = Some "date"`.
- A caller of `Gen.fsharpValue` that wants the sentence adapts with
  `|> Result.mapError CodegenError.describe`.
- A caller of `Gen.typescriptValue v` writes `Gen.typescriptValue idl TNode v` and handles the
  `CodegenError` (a value that does not fit its declaration is refused now, not emitted).
- A caller of `Diff.run` (or `fuaran-core-idl classify`) that relied on the UI roster without a
  manifest supplies a manifest declaring `hosts`, or calls `Diff.report` with `Diff.declaredRoster`.
- A generated module regenerates: a vocabulary that declares a hosted wire form, or holds a qualifying
  host-only closure, gets new decoder or equality code.

**Class: breaking (source).** `api/Fuaran.Core.Idl.txt` reports `record-widening` (`HostedCodec`)
plus additive members; `api/Fuaran.Core.Idl.Codegen.txt` reports two retypes (`fsharpValue`,
`typescriptValue`) and one addition. The wire baseline for `Fuaran.Core.Idl` reads `breaking`: its
document set is derived by reflection and now draws a hosted slot WITH a declared form, so its
`"wire": "json"` document is gone from the set — no existing vocabulary's artifact bytes move, since an
undeclared slot renders exactly as before.

### The sanitisation floor joins the cross-pipeline table (Phase 291's Fable half) — ADDITIVE

**What changed.** Phase 291's sanitiser cases (U+0130 runs before each scheme, bare and inside an
`href`; case folding on the original string; lone surrogates; the handler and element scans after a
run; the URL floor's clause table) ran on .NET alone, while the defect they guard against only shows
under Fable. They now ride the one cross-pipeline table (DECISIONS.md D55): `ParityVectors.sanitiseSweep`,
86 rows, each carrying its expected output and printing `ok` when the floor produced exactly it (else
`diverges:` and the UTF-8 hex of what it did produce), appended to `ParityVectors.lines ()` after the
hash sweep. So the printed lines stay ASCII, this repository asserts every row reads `ok` on .NET, and
the downstream Fable runner byte-compares the transpiled pipeline against them with no change of its
own. `Fuaran.Core.Conformance` gains a reference to `Fuaran.Core.Idl` for it — `Idl` depends on `Wire`
alone, already in the kit's closure, so the kit's package graph gains one package.

**What adopting it costs.** Nothing. A consumer of the conformance kit restores one more package; the
runner's comparison unit grows by 86 lines at the consumer's next Core raise.

**Class: additive.** `api/Fuaran.Core.Conformance.txt` gains one member (`ParityVectors.sanitiseSweep`).

### A reachability index on the lane DAG: `Dag.Reach`, the indexed append and merge, and two index-taking overloads (Phase 289) — ADDITIVE

**What changed.** Every graph query on the lane DAG recomputed from the node map on each call:
`ancestorsOf` walked the parents, `tryTopoOrder` / `tryReplayTo` / `between` re-drained a head's closure,
`mergeBase` took two closures and one more per common ancestor, and `reconcileMany` one per head. A
loop of them — a keyring walk asking reachability per act, a fold asking a merge base per lane pair —
cost the history's size times the number of questions. `Fuaran.Core.OpStream.Dag` now ships an index
that answers the same questions from what it built once:

- `Dag.Reach<'Op>` (no equality; immutable; carries the DAG it indexes) and the `Dag.Reach` module:
  `ofDag` (one drain of the whole node set), `dag`, `ancestors`, `reaches ancestor descendant`,
  `tryTopoOrder`, `mergeBase` and `between`, each answering exactly as the unindexed function of the
  same name (`reaches` as membership in `ancestorsOf`).
- `Dag.appendIndexed` / `Dag.mergeIndexed`: `append` / `merge` that also return the index extended for
  the new node, at O(N) rather than a rebuild. Their refusals are `append`'s and `merge`'s.
- `Dag.tryReplayToWith` and `Dag.reconcileManyWith`: `tryReplayTo` and `reconcileMany` taking the index
  where they take the DAG, so a consumer holding an index does not pay the closure walks inside the call.
- `Conformance.reachLaws` (opt-in, `StrongerPromise`, `Guarded [ "DAG shape" ]`): every indexed answer
  equals the unindexed one on generated DAGs and on a built cyclic load with a dangling parent, the
  index grown by `appendIndexed` / `mergeIndexed` answers as `Reach.ofDag` of the result, and a sample
  without a merge of two incomparable lanes reds the guard rather than passing the merge-base, delta
  and reconcile laws on chains.

The representation and its measured cost are DECISIONS.md's Phase 289 entry: a slot per orderable
node, the whole DAG's drain order (a head's order, a union's and a delta's are filters of it), and a
per-slot ancestor bitset over slots — N²/64 32-bit words, about 1.6 MB at 5,000 nodes.

**What did NOT change, deliberately.** No existing function's signature or answer moves:
`reconcileMany` is re-expressed over a shared private fold (`reconcileRegion`) with the partition it
always computed, and the laws pin every indexed answer to the unindexed one. The shard named
`replayToWith`; the overload is `tryReplayToWith`, because `replayTo` is obsolete and leaves after this
draft, and an overload of it would leave with it. The shard named `Reach.topoOrder`; it is
`Reach.tryTopoOrder`, because the unindexed function it equals is `tryTopoOrder` (`topoOrder` is
private) and answers with a `Result`. `mergeIndexed` is beyond the shard's list: a session that appends
and merges in a loop would otherwise rebuild at its first merge.

**What adopting it costs.** Nothing for a consumer that does not build an index. A consumer that does
builds it once per load (`Dag.Reach.ofDag`), keeps it current with `appendIndexed` / `mergeIndexed`
where it appended with `append` / `merge`, and asks through `Dag.Reach.*` or passes it to the `…With`
overloads. The index holds its DAG, so there is no DAG argument to mismatch.

**Class: additive.** `api/Fuaran.Core.OpStream.Dag.txt` gains the `Reach` type and module and four
functions; `api/Fuaran.Core.Conformance.txt` gains `Conformance.reachLaws`. No wire bytes move.


### Off-walk identity in the footprint and arbitration: a keyed footprint, a domain's own arbitration pair, and the container rule at the partition (Phase 247) — ADDITIVE

**What changed.** `Ops.footprint` reads an inserted subtree's ids over `Children` alone, so two scripts
that each graft a subtree carrying the same id in a keyed position were declared independent and
collided only at replay; and `Arbitration.arbitrate` checks applicability with `Ops.canApplyAll`, under
which every node can hold children, so it admitted an insert under a leaf that the domain's own engine
then refused (or, under plain `apply` with a witness that ignores a leaf's new children, dropped). Five
members close both, beside what was there:

- `Ops.footprintKeyed keyw nodew idw ops` — the footprint of the script `Ops.applyContainedKeyed` runs.
  An `InsertChild`'s authored id set is the graft's keyed walk (`Tree.idsKeyed`), and an `UpdateNode`
  payload's keyed subtrees, which the keyed engine checks as new content, are read and content-written
  too. Two scripts bringing in one keyed id now fail `Ops.independent` with `Interference.SameTarget`
  on it, and `Dag.conflicts` over it reports `ConcurrentUpdate` at that id. For a witness whose
  `KeyedChildren` is `fun _ -> []` it returns exactly `Ops.footprint`.
- `Ops.canApplyAllKeyed keyw canHold nodew idw ops root` — the sequence dry run over the keyed engine
  (the `canApplyAllWith` mirror of `applyContainedKeyed`), first-refusal index and envelope.
- `Arbitration.arbitrateWith footprint canApply baseTree proposals` — the partition with the domain's own
  independence and applicability (the `concurrencyLawsWith` precedent). `arbitrate nodew idw` is now
  defined as `arbitrateWith (Ops.footprint nodew idw) (Ops.canApplyAll nodew idw)`; its output is
  unchanged on every input.
- `Arbitration.arbitrateContained canHold nodew idw` — the common case: `arbitrateWith` at
  `Ops.footprint` and `Ops.canApplyAllWith canHold`, so an insert under a node `canHold` refuses is
  `Inapplicable(_, NotAContainer _)`. A keyed domain composes `arbitrateWith (Ops.footprintKeyed …)
  (Ops.canApplyAllKeyed …)`.
- `Conformance.keyedArbitrationLaws` / `keyedArbitrationLawsWith` — a new opt-in family
  (`NeedsWitnessCapability`; `KeyedWitness`, `NodeWitness`, `IdWitness`, `OpGen`). It runs the
  partition through `arbitrateWith` and BUILDS the two admissions this phase closes: an insert under a
  node `OpGen.CanHold` refuses (must be `Inapplicable(NotAContainer)`), and two grafts carrying one id in
  a keyed position through `PlaceKeyedChild` (never both admitted; the later is `Conflicts` citing the
  earlier with `SameTarget` on the id). It also holds the accepted scripts to LAND under
  `applyContainedKeyed` in any order, to one tree whose keyed walk repeats no id. Handed `arbitrate`'s
  footprint or `arbitrate`'s applicability through the `With` form, the clash arm or the container arm
  goes red. A generator with no `CanHold`, or a witness with no keyed position, makes the matching arm
  vacuous BY DECLARATION, and the guard line says so.

**What adopting it costs.** Nothing for a consumer who keeps calling `arbitrate` / `footprint`: neither's
output moves. A domain with a container capability switches to `arbitrateContained canHold`; one with
keyed positions to the keyed `arbitrateWith` pair and `footprintKeyed` wherever it feeds a footprint to
`Dag.conflicts` / `Dag.reconcile`.

**What it does not do.** A keyed-slot write still has no address kind of its own: the shard's fifth
`Footprint` address set (`KeyedWrites`) and a `KeyedSlotClash` conflict shape are NOT in this entry. A
fifth field on the published `Footprint` record breaks every full-record construction, and a sound
landing needs `Dag.conflicts` and `MergeConflictShape` (and the `DagFold` model of them) to read the new
set in the same change — otherwise `Dag.conflicts` would report nothing for a pair `Ops.independent`
rejects on a slot, and `Dag.reconcile` would fold two lanes writing one slot as clean. It is left for a
change that can move those together and class itself breaking (source).

**Class: additive.** `api/Fuaran.Core.Ops.txt` gains four members (`Ops.footprintKeyed`,
`Ops.canApplyAllKeyed`, `Arbitration.arbitrateWith`, `Arbitration.arbitrateContained`) and
`api/Fuaran.Core.Conformance.txt` two (`Conformance.keyedArbitrationLaws`,
`Conformance.keyedArbitrationLawsWith`); no existing member moves, and no wire byte.

### An authored `doc` annotation, emitted as the member's `///` summary (Phase 255) — BREAKING (source): `Annotations` widened; additive on the artifact wire and the generated emission

**What changed.** `Annotations` gains a fourth slot, `Doc: string option` — what the member IS, as
authored prose — beside `Deprecated`, `InProcessOnly` and `Since`. It rides every annotatable
declaration the set already reaches: kinds and tree-ops, their fields, the node envelope's fields,
record fields, union cases, and enum cases through `Declare.enumAnnotate` (which takes the whole set,
so it needed no change). `Annotations.Empty` is unchanged in meaning (`Doc = None`), and `IsEmpty`
counts the new slot.

- **The artifact** writes it as a `doc` key inside the existing `annotations` object (and inside an
  enum's `caseAnnotations` entries), verbatim, and reads it back; absent means none. An artifact that
  declares no doc renders byte-for-byte what it did, so only one that declares a doc gains the key and a
  new content hash. A reader older than this slot ignores the key. The stability classifier needs no
  change: it compares the canonical annotation object, so a doc on a member that declared nothing grades
  `additive`, and a doc added to an annotated member, moved or withdrawn grades `host-surface-only`, as
  for the other three slots.
- **The F# generator** emits the doc FIRST in the member's `///` block, ahead of the deprecation,
  in-process and since notes, one `///` line per authored line. Every line break an author can type ends
  a line, so no authored text can fall out of a comment into the generated source; trailing whitespace
  and blank lines at either end are dropped; a whitespace-only doc emits nothing. A character XML 1.0
  cannot carry (a C0 control other than tab, an unpaired surrogate, U+FFFE / U+FFFF) becomes U+FFFD,
  because the compiler checks every doc block as XML (FS3390) and nothing else can spell it.
- **The block follows the compiler's mode, and that is the correction this entry records.** The F#
  compiler reads a `///` block whose first line does not begin with `<` as TEXT: it wraps it in
  `<summary>` and XML-encodes it itself. Encoding `<` and `&` in such a block as well — the first
  design — makes the documentation file carry `&amp;lt;` and a reader see `&lt;'T>` where the author
  wrote `<'T>`, which was measured, not inferred. So a text block is emitted verbatim. A block whose
  first line DOES begin with `<` is read as XML and authored text is not valid XML in general, so a doc
  that opens with `<` is emitted as an explicit `<summary>` holding every line of the block, the notes
  included, with `<` and `&` encoded. The test project compiles a generated module covering both modes
  with FS3390 as an error, and a drift guard holds that module to the generator.
- **The TypeScript backend** does not render the doc: its emitted JavaScript has no per-member
  declaration to carry a summary, and its annotation comments are `//` lines naming their subject.

**What adopting it costs — the consumer edits.**

- Every `Annotations` value written as a FULL record literal (`{ Deprecated = …; InProcessOnly = …;
  Since = … }`) adds `Doc = None` (FS0764 until it does). A `{ Annotations.Empty with … }` expression
  compiles unchanged (one full literal in this repository's own test fixtures took the edit).
- A vocabulary that declares no doc regenerates byte-identical artifacts and generated modules. One that
  declares a doc regenerates with exactly the added `///` lines — plus the `<summary>` pair, and the
  encoded notes, for a doc that opens with `<`.
- A consumer re-pins to adopt; the documentation reaches its own users when it ships an XML
  documentation file.

**Class: breaking (source).** `api/Fuaran.Core.Idl.txt` reports `record-widening` (`Annotations` gains
`Doc`; the constructor gains a parameter). The wire baseline for `Fuaran.Core.Idl` moves by the members
added under every `annotations` object (`additive` moves; its stated class since the newest tag was
already `breaking`). `api/Fuaran.Core.Idl.Codegen.txt` does not move: the emitter's signatures are
unchanged and only what it writes for a documented member differs.

### The capability and query seams speak to a model (Phase 251) — ADDITIVE

**What changed.** The two seams refused in typed unions and stopped there: nothing rendered a
refusal for a model, neither codec encoded one, a body or resolver was not handed the arguments
validation had accepted, validation stopped at the first failure, `Query` had no JSON Schema, and an
unknown member was always read past. Each now has a Core answer, beside the existing forms:

- **Refusals rendered.** `InvokeError.describe` and `QueryError.describe` (with `describeAll`) turn a
  case into one sentence that names the failure and, wherever a closed set is refused against, its
  members. `Space.describe` phrases a value space. A `ParamTypeMismatch` whose expected type is
  `decimal`, `date` or `timestamp` also says how to write one (a decimal as a JSON string of decimal
  text, such as `"12.50"`, never a JSON number).
- **Refusals encoded.** `CapabilityCodec.invokeErrorJson` / `encodeInvokeError` / `invokeErrorOf` /
  `decodeInvokeError`, and the `QueryCodec.queryErrorJson` family: one `"$type"` per case (the case
  name in camelCase), a value space in its codec form, a column type by its tag. Both are wire roots
  now (`invokeError`, `queryError`).
- **Validated arguments handed on.** `Capability.invokeWithArgs` / `Registry.dispatchWithArgs` hand a
  body the validated arguments TYPED by their spaces, as the new `ArgValue` union (`IntValue`,
  `FloatValue`, `TextValue`, `TreeValue` — the parsed document of a slot argument);
  `Capability.typeArgs` is that list on its own. `Query.invokeWithArgs` /
  `QueryRegistry.dispatchWithArgs` hand a resolver the validated `(string * Cell) list`.
- **Every violation at once.** `Capability.validateArgsAll` and `Query.validateParamsAll` answer every
  refusal — per argument in argument order, then the unbound required ones, then (query) the required
  ones bound only to `Null`. The first-failure forms are unchanged, and the head of each list is
  exactly their answer; a suite enumerates every combination of a fixture's arguments to hold it.
- **A query schema.** `Query.toJsonSchema` is `Function.toJsonSchema`'s twin: untagged JSON Schema,
  parameters keyed by name, `x-effect`, and `x-result` (one result row, keyed by column) outside
  `properties`. A `decimal` parameter or result column is a STRING with the pattern
  `^-?[0-9]+(\.[0-9]+)?$` — exactly the text the codec reads into a `Decimal` cell — because a model
  told "number" emits a fractional token the codec refuses. `QueryCodec.decodeArgs` /
  `decodeArgsJson` read the argument object that schema describes, through the column codec's one
  cell decoder, answering every refusal (an undeclared member is always `UnknownParam`).
- **A strict read policy.** `ReadPolicy` (`Lenient`, the default, or `Strict`) and
  `CapabilityCodec.decodeWith` / `decodeJsonWith` / `decodeInvocationWith` / `deferredOfWith` /
  `decodeDeferredWith`, `QueryCodec.decodeWith` / `decodeResultWith` / `decodeDeferredResultWith`.
  `Strict` refuses an unknown member of any object the codec reads, naming it and the members read; it
  runs as a check before the unchanged decoder, so `Lenient` is the old behaviour byte for byte. The
  embedded `source` and `rows` documents are the column codec's and a `ready` payload is the caller's
  decoder's, so the check stops at them.
- **The tag convention, written down** beside `Function.toSchema`: a wire document is `"$type"`, a
  descriptor read beside the tree wire is `"kind"`, a JSON Schema for a model is untagged with `x-`
  extensions outside `properties`.

**What adopting it costs.** Nothing: every existing function, type and emitted byte is unchanged. A
consumer that opens `Fuaran.Core` beside a namespace of its own declaring `ArgValue`, `ReadPolicy`, or
a case named `IntValue` / `FloatValue` / `TextValue` / `TreeValue`, resolves the later-opened one.

**Class: additive.** `api/Fuaran.Core.Function.txt` and `api/Fuaran.Core.Query.txt` gain members and
types only; `api/wire/Fuaran.Core.Function.txt` and `api/wire/Fuaran.Core.Query.txt` gain the two
refusal roots' documents, every existing document unchanged.


### A checkpoint on the lane DAG: a sealed state at a node, bounded replay from it, and a DAG that may begin at one (Phase 288, DECISIONS.md D93) — ADDITIVE

**What changed.** The linear stream has had a checkpoint since Phase 244; the lane DAG had none, so every
replay folded a head's whole ancestor closure and a DAG file was its complete history or did not verify.
`Fuaran.Core.OpStream.Dag` now ships the linear `Snapshot`'s counterpart:

- `Dag.Checkpoint<'State>` — `{ Node; State; Hash }`: the fold of `Node`'s ancestor closure, sealed with
  the linear snapshot's strict pre-image at sequence zero chained from the node id (computed through
  `OpStream.Snapshots`, not copied). `Dag.checkpointAt` folds to a node and seals; `Dag.sealAt` seals a
  state the domain vouches for (the genesis-import shape); `Dag.checkpointFrom` takes the next checkpoint
  from an earlier one. `Dag.verifyCheckpoint` recomputes the seal and refuses by name
  (`CheckpointBreak.UnknownNode` / `Seal`).
- `Dag.replayFrom w cp dag head` folds only the history above the checkpoint over its state, and answers
  as `tryReplayTo` from the initial state for every head the checkpoint covers; `Dag.replayFromWith`
  takes Phase 289's index. Refusals are the typed `Dag.CheckpointFault` — `UnknownNode`, `Replay` (the
  replay's own fault), `Unreached`, and `Uncovered`: a node above the checkpoint that does not descend
  from its node, which the full replay folds before part of the checkpoint's closure (DECISIONS.md D93).
- `Dag.compactAt` / `Dag.compactFrom` truncate the history behind a checkpoint's node and keep the node,
  so the compacted DAG lives on — append onto the node, checkpoint and compact again. `Dag.firstBreakFrom`
  / `Dag.verifyDagFrom` verify a DAG that begins at a checkpoint: a changed byte in a kept node or in the
  checkpoint fails them.
- `Dag.toJsonlWithCheckpoints` / `Dag.fromJsonlWithCheckpoints`: checkpoints ride a SIDECAR beside the
  lane file, one line each, the line being the linear snapshot line of the snapshot the checkpoint is
  sealed as. The lane is `toJsonl`'s bytes; a DAG file with no sidecar reads exactly as `fromJsonl` reads
  it.
- `Conformance.checkpointLaws` (opt-in, `StrongerPromise`, `Guarded [ "DAG shape" ]`): taking, the seal,
  replay equivalence and its refusals, truncation, tamper, the compacted DAG living on, and the lane
  bytes; a sample with no covered replay over a lane merge, no uncovered refusal or no compaction keeping
  history above its checkpoint reds the guard rather than reading green. `proofs.json` gains two
  `tested` rows evidenced by it; whether `proofs/DagFold.fst` extends to a checkpointed origin is recorded
  as open, not claimed.

**What did NOT change, deliberately.** `Dag.T<'Op>` gains no field: the shard's optional origin field
"with a default" does not exist in F# (a record field has no default, so it would have been a
`record-widening` breaking every `{ Nodes = … }` literal), so the checkpoint travels beside the history
as a snapshot travels beside its tail. No existing function's signature, answer or emitted byte moves,
and no linear-stream file changed. The shard's `checkpointAt w stateEncode hashFn …` takes this module's
argument order instead (`hashFn stateEncode w …`, as `OpStream.Snapshots.take` does). Beyond the shard's
list: `sealAt`, `checkpointFrom` and `compactFrom` (a compacted DAG checkpointing and compacting again),
`replayFromWith`, and `firstBreakFrom` beside `verifyDagFrom`.

**What adopting it costs.** Nothing for a consumer that takes no checkpoint. One that does keeps the
checkpoint beside its DAG, reads and writes it through the sidecar, replays with `replayFrom`, and treats
an `Uncovered` refusal as "take the checkpoint later, or replay from the origin". Verify the DAG before
compacting it: the discarded closure is folded and dropped, and nothing can find a tamper in it again.

**Class: additive.** `api/Fuaran.Core.OpStream.Dag.txt` gains the `Checkpoint` record, the
`CheckpointFault` and `CheckpointBreak` unions and twelve functions; `api/Fuaran.Core.Conformance.txt`
gains `Conformance.checkpointLaws`. No wire bytes move.


### A placement algebra, tree lowering and fresh ids: `TreePlacement`, `Ops.lower`, `FreshIds` (Phase 312) — ADDITIVE

**What changed.** `InsertChild` and `MoveNode` append and order is stated by `ReorderChildren` naming
ids (Phase 95), so every consumer that wanted a node anywhere but last derived the sibling
permutation itself, and every consumer that cloned, pasted or repaired a tree hand-rolled a
derive-and-probe id loop. Core now ships both, as helpers over the existing ops and witnesses:

- `Fuaran.Core.Ops`: `Anchor<'Id>` (`First | Last | Before of 'Id | After of 'Id | Index of int`,
  positions counted among the destination's children OTHER than the node placed), `PlaceError<'Id>`
  (`Refused of Rejection` carrying the engine's own envelope, `UnknownAnchor` enumerating the
  siblings, `IndexOutOfRange` naming the count), and the `TreePlacement` module — `place` / `move` /
  `clone` and their `…Contained` forms — each LOWERED to a skeleton script: `[InsertChild]` or
  `[Batch [InsertChild; ReorderChildren]]`, `[MoveNode]` or `[Batch [MoveNode; ReorderChildren]]`
  across parents, `[ReorderChildren]` or `[]` within one, and a clone's copy renamed by
  `FreshIds.repairDuplicates` and then placed. No new op.
- `Ops.shellOf` / `Ops.skeletonRoot` / `Ops.lower`: a tree as its root's shell plus the preorder
  `InsertChild` script that rebuilds it, `applyAll (lower t) (skeletonRoot t) = Ok t` for every
  well-formed tree, and the same script under `applyAllWith canHold` for a tree in the containment
  invariant.
- `Fuaran.Core.Tree`: the `FreshIds` module — `derived` (`<id>-copy`, `-copy-2`, …), `sequential`
  (`<prefix>-1`, `-2`, … with no hidden counter) and `repairDuplicates` (rename every later
  occurrence and every id a caller-supplied taken set holds, return the mapping), each over
  `IdWitness` and a taken set of `ToString` keys.
- `Conformance.placementLaws`, `Conformance.loweringLaws` (both over the base witness) and
  `Conformance.freshIdLaws` (also a `setId` and the minting strategy it certifies) — all opt-in,
  `StrongerPromise`, guarded on the arms they draw.

**What did NOT change, deliberately.** `IdWitness` gains no `fresh` (D5): minting is a helper over
`ToString` / `OfString` and a taken set, so the same inputs mint the same id on every host and every
replay. No existing function's signature or answer moves, and no wire bytes move. The shard named the
module `Placement`; `Fuaran.Core.Placement` is already a public type (where a capability's body runs),
so the module is `TreePlacement` (DECISIONS.md, Phase 312).

**What adopting it costs.** Nothing for a consumer that does not call it. A consumer retiring its own
placement verbs gets the same script for an anchor-relative insert and a cross-parent move, and a
`ReorderChildren` (or nothing) where it emitted `Batch [MoveNode; ReorderChildren]` for a move within
one parent — the same tree, a smaller footprint. A domain whose ids are not strings with a suffix
(a `Guid`, an integer) passes its own strategy of the shape `'Id -> Set<string> -> 'Id` and certifies
it with `freshIdLaws`. A consumer that opens `Fuaran.Core` beside a namespace of its own declaring
`Anchor`, `PlaceError`, `TreePlacement` or `FreshIds` resolves the later-opened one.

**Class: additive.** `api/Fuaran.Core.Ops.txt`, `api/Fuaran.Core.Tree.txt` and
`api/Fuaran.Core.Conformance.txt` gain members and types only.

**Phase 292 — a vocabulary is validated data, and the generator escapes everything it splices:
`Declare.errors` at every loading path, one source-literal escaper, a total codec and sampler. Class:
`additive` — refusals where silence was, new members only; one reference-interpreter canonical-form
correction, stated below.**

- **`Declare.errors : Idl -> string list`** — every well-formedness rule in one call: the three existing
  checks plus references, duplicates, defaults of their slot's type, `HostOnly` is `TFn`, a transparent
  case that cannot encode to an object (at its declaration and at every instantiation), `id` / `kind`
  reserved beside the nested kind body, identifier grammar for every emitted name, single-line
  annotation slots and categories, well-formed UTF-16 (DECISIONS D95). `Artifact.ofJson` /
  `Artifact.parse` and `Proposal.applyDelta` refuse a vocabulary it reports, naming every error; the
  `classify` command reports them before classifying (exit 2, refused). A field-less kind or record is
  well-formed.
- **`SourceLit`** — the one escaper: `fsString`, `fsAttribute`, `fsDocLines` / `fsDocXml`, `tsString`,
  `tsStringSingle`, `tsKey`, `tsIsIdentifier`, `tsCommentLines`, `fstarString`, `isWellFormed`. The
  generator's five private escapers are gone, and every splice of IDL-authored text — discriminator,
  enum wire strings, category, annotation prose, object keys — goes through it. A deprecation message,
  replacement or version, or a category, carrying a line break used to end its comment and put the rest
  into the generated module as code; it is now one comment line per authored line. A declared string
  default carrying CR or LF is now escaped (`fsDefaultStr` escaped neither).
- **`TypeParams.substitute` / `TypeParams.bind`** — one type-parameter substitution, keyed by name and
  bound only at matching arity, shared by `Encode`, `Decode`, `Sample` and the generator. `Decode`'s
  bare transparent arm refuses an arity mismatch (it threw).
- **`Encode`** refuses a non-finite float inside a verbatim `json` / hosted value, naming its token and
  path (there it aliases the string; at a `float` slot the quoted token stays WIRE_FORMAT §7's
  spelling), and an ill-formed UTF-16 string, as `Canon.tryRender` does. **The omit-at-default test
  compares in the slot's value space**: a value equal to the default after encoding is omitted, so
  `VInt 2` and `VFloat 2.0` at a float slot defaulting to `2` now encode identically (absent) — the
  reference interpreter's one canonical-form change, bringing it to what the generated F# and
  TypeScript encoders already wrote (they compare host values). Decoding is unchanged: a present field
  equal to its default still reads.
- **`Sample.trySampleNodes` / `Sample.SampleRefusal`** — the total sampler: no tag to cycle over (it
  divided by zero), a tag naming no kind, an empty enum or union, a dangling name, an unbound type
  variable or a type with no finite value is a typed refusal; every drawn value is one the encoder
  accepts (the `VStr "?"` / `VUnion("?", [])` placeholders are gone; a bare kind and an op draw their
  fields under the presence rules; an op is drawn at the depth floor). `sampleNodes` keeps its
  signature and its vectors, and raises the refusal as `InvalidOperationException`. Seeded streams are
  unchanged for a vocabulary with no `TKind` / `TOp` slot.
- **Scaffold and defaults.** `Gen.fsharpValue` writes a scalar or enum through the declared-default
  literal: a whole float at a float slot is `2.0` (it wrote `2`, FS0001), a non-finite float is refused.
  A `VEnum` default or value is resolved through `IdlEnum.CaseOf` and a wire string the enum does not
  admit is refused by both backends (the F# one fell through to an undeclared identifier).

**What adopting it costs.** A vocabulary that loaded and breaks a rule no longer loads — fix what the
refusal names. Generated modules regenerate byte-identically for every vocabulary whose text needs no
escaping (every committed generated fixture here does); one whose text carries a line break, quote or
control character emits it escaped or split, which is the point. A consumer that opens
`Fuaran.Core.Idl` beside its own `SourceLit` or `TypeParams` resolves the later-opened one.

**Class: additive.** `api/Fuaran.Core.Idl.txt` gains members and types only;
`api/Fuaran.Core.Idl.Codegen.txt` does not move.

### Footprint soundness at a domain's own ops, and the lane DAG `foldOnce` folds (Phase 249) — ADDITIVE

**What changed.** Every soundness law over a footprint — `footprintLaws`, `mergeConflictLaws`,
`reconcileLaws`, `concurrencyLawsWith`, `keyedArbitrationLawsWith` — is typed over `SkeletonOp`, so a
domain whose ops are its own, each with a hand-written footprint, had no law that certified that
footprint. `FoldConfluence.laneFoldLaws` takes the projection but certifies arrival-order invariance,
and a footprint that misses an id is symmetric: two grafts bringing in one keyed id are refused the
same way under every order, and the pack passes it. And `FoldConfluence.foldOnce` returned only a
canonicalised outcome, so a domain that wanted a halt's typed `MergeConflict` list rebuilt the lane
DAG beside it, free to drift. Two members, beside what was there:

- `Conformance.footprintLawsAt sw footprintOf hashState gen seed iterations` — a new opt-in family
  (`StrongerPromise`; `StreamWitness`, `StreamGen`): Phase 78's soundness law at the domain's own
  ops. It reaches a state by threading drawn ops from `gen.State0`, draws two scripts from it (each
  applies on its own), and holds every pair `Dag.conflicts footprintOf` reports nothing for to
  commute under the domain's `Apply` — both orders apply and land on states `hashState` cannot tell
  apart — with footprint determinism beside it. The guard demands an independent pair AND an
  interfering one (a sample that never puts two ops on one address cannot tell a sound footprint from
  an empty one) and counts refused draws beside them. Named by the kit's rule: `…At` is the
  domain-witness form; it carries no `hashFn`, because it chains nothing.
- `FoldConfluence.laneDag hashFn w baseOp lanes : Dag.T<'Op> * string * string list` — the DAG one
  trial folds: `baseOp` under `Human "base"`, each lane chained onto it under `Human "lane-<i>"`,
  answered as the DAG, the base id and the heads in lane order (an empty lane's head is the base) —
  what `Dag.reconcileMany w footprintOf dag baseId state0 heads` takes. `foldOnce` is now defined over
  it, and `laneFoldLawsWith`'s shaped draws build their base through the same private helper, so the
  DAG a domain reads conflicts off and the DAG the pack folds cannot diverge. `foldOnce`'s output is
  unchanged on every input.

**What adopting it costs.** Nothing: no existing member, type or emitted byte moves. A domain that
folds lanes over its own ops adds `footprintLawsAt` beside `laneFoldLaws`, handing it the same witness,
footprint and state hash; one that built its own lane DAG to read typed conflicts replaces it with
`laneDag`.

**What it does not do.** The claims ladder's `independence-diamond` row — the fold theorem's one
domain hypothesis, stated about a domain's OWN ops — still names `footprintLaws` as what discharges
it, which samples the skeleton algebra the `tree-independence-diamond` row already proves. Moving the
discharge to `footprintLawsAt` changes that row and is left to a change that owns the ladder.

**Class: additive.** `api/Fuaran.Core.Conformance.txt` gains two members
(`Conformance.footprintLawsAt`, `FoldConfluence.laneDag`); no existing member moves, and no wire byte.
The roster gains one family, and `docs/conformance-families.md` / `.json` are regenerated.

### The compacted stream lives on, checked JSONL writers, strict effect replay (Phase 301, DECISIONS.md D97) — ADDITIVE

**What changed.** Compaction was terminal. `append` numbers a record from the length of the list it is
handed and links it to that list's last hash, falling back to the genesis, so an append onto a
compacted tail continued at the wrong sequence (onto an empty tail, at sequence zero linked to the
genesis) and no verifier accepted the result; compacting the tail again renumbered it from zero; a key
index rebuilt from the tail forgot every key the discarded prefix produced, so an idempotent retry of
a pre-compaction key applied a second time. The JSONL writers embedded each `Encode` output unchecked,
and an encoding with a line break or whitespace around its value read back changed and failed
`verifyChain` / `verifyCaptures`. `replayEffect` answered an exhausted journal from the live source,
so a journal cut short at its tail replayed with live values and no signal. Beside what was there:

- `Compacted<'Op,'State>` (`Snapshot`, `Tail`, `Keys` — the discarded history's key index) and
  `CompactedOutcome<'Op,'State>`, with `OpStream.Compacted.compactAt` / `compactFrom` / `head` /
  `appendTo` / `appendIfTo` / `appendIdempotentTo` / `keyIndex` / `verify` / `replayFrom`. Every
  member reads the snapshot's BOUNDARY: the next sequence is `Snapshot.Seq` plus the tail's length,
  the head of an empty tail is `Snapshot.PrevHash`, the key index is `Keys` plus the tail's.
  `compactFrom` takes its boundary in the origin's numbering and refuses a prefix op at its origin
  index. Proved in `proofs/Chain.fst` §7b: `append_after_compact`, `compact_compose`,
  `key_index_rebuild_parity`.
- Checked writers beside the unchanged ones, each `Result<_, JsonlWriteFault>` and byte-identical to
  its unchecked twin on `Ok`: `OpStream.tryToJsonl`, `OpStream.tryCaptureToJsonl`,
  `OpStream.Snapshots.tryToJsonl`, `Dag.tryToJsonl`, `Dag.tryToJsonlWithCheckpoints`, all through the
  one check `OpStream.Jsonl.checkRaw` (`JsonlWriteFaultReason`: `MultiLine`, `NotTrimStable`,
  `Unreadable`). A line or paragraph separator inside a string is NOT refused: the canonical escaper
  emits it raw and the reader splits on `\n` alone, so it round-trips.
- Strict replay: `OpStream.replayEffectStrict` (`CaptureReplayFault`: `Exhausted`,
  `IdentityMismatch`, `LabelMismatch`, `LabelNotCanonical`, `Undecodable`), `OpStream.isDeterminismLabel`
  (case-exact, held equal to `Effect.tryDeterminismOfTag` by a test), and the journal anchor
  `captureHead` / `captureHeadWith` / `verifyCapturesAt` / `verifyCapturesAtWith`. Proved in §7c:
  `intact_captures_verify`, `replay_record`, `truncated_journal_exhausted`,
  `capture_value_tamper_detected`.
- `Conformance.streamLaws` and `Conformance.captureReplayLaws` each gain one law cell, the JSONL round
  trip through the checked writer, after their existing cells.

**What adopting it costs.** Nothing to compile: no existing member, type or emitted byte moves, and
the snapshot and record lines a compacted stream writes are the lines it wrote. **One run can go red
that was green:** the base run (`certify`, `certifyStream`, `streamLaws`) now certifies LINEAR
PERSISTENCE, so a domain whose `Encode` is not one single-line JSON value — `i5` rather than `"i5"`,
or a value with a trailing space — fails the new cell, naming the record and member. That domain's
streams never round-tripped through `toJsonl` / `fromJsonl`; the cell reports what was already true.
This repository's own reducer fixture was such a domain and now encodes a JSON string. A host that
compacts and keeps writing moves to `Compacted` and persists `Keys` beside the compacted file
(DECISIONS.md D97); a host that writes JSONL moves to the `try…` writers.

**What it does not do.** The pre-Phase-296 `OpStream.replayFrom` forward still does not check the
seam (it is `Obsolete` and leaves after this draft); `Snapshots.replayFrom` and `Compacted.replayFrom`
do. Nothing binds `Keys` into a hash, and no line format for it is minted here (D97). The capture
envelope's injectivity premise is stated bundled, not decomposed as the record envelope's was.

**Class: additive.** `api/Fuaran.Core.OpStream.txt` and `api/Fuaran.Core.OpStream.Dag.txt` gain
members only; the surface guard classes both moves additive. The roster's two families each gain a
cell (counts pinned by the suite move with them), and `docs/conformance-families.md` / `.json` are
regenerated. `proofs.json` gains five proved rows and two tested ones; Chain's prover budget is
re-seeded (`proofs/modules.json`).

### A structural-integrity strand: a containment grammar and a reference witness (Phase 313, DECISIONS.md D98) — BREAKING (source): `union-widening` of `Rejection` and `Diff.DiffError`; the rest `additive`

**What changed.** Core's containment was unary (`canHold`, child-blind by design) and its references
were nobody's. Two strands, beside what was there:

- **The grammar.** `allowedChildren : string -> string list option` — a parent's kind tag to the kind
  tags it may hold, `None` meaning any — is handed beside `canHold` to `Ops.applyGrammar`,
  `canApplyGrammar`, `applyAllGrammar`, `canApplyAllGrammar`, `Diff.toOpsGrammar` and
  `Arbitration.arbitrateGrammar`. Each is the container-aware form it sits beside, then the grammar:
  a step `applyContained` refuses is refused with the same envelope, and a step it accepts is refused
  with **`Rejection.IllegalChild(child, childKind, parent, parentKind, legal)`** when it would leave a
  child under a parent whose kind may not hold it — `legal` enumerating what that kind may hold.
  `Ops.isLegalChild` is the one definition and `Ops.illegalChildren` the whole-tree reading;
  `Validator.containment` reports the same pairs over a tree that arrived whole (`TREE-ILLEGALCHILD`).
  The diff refuses an `after` holding an illegal pair with **`Diff.IllegalChildInTree`**, after every
  `toOpsContained` refusal.
- **References.** `RefWitness<'Node, 'Id> = { RefsOf; DeclsOf }` (frozen at birth, beside the twelve).
  `Validator.referenceDefects` answers `Validator.ReferenceDefect` — `DanglingReference`,
  `UnusedDeclaration`, `ReferenceCycle` (through `Propagation.sort`) — and `forwardReferences` the
  opt-in `ForwardReference`; `referenceIntegrity` (`REF-DANGLING`, `REF-UNUSED` warning, `REF-CYCLE`)
  and `referenceOrder` (`REF-FORWARD`) are their rule families. `Ops.applyReferenced` (with its dry run
  and sequence forms) is `applyGrammar` that also refuses a `RemoveNode`, or an `UpdateNode` that stops
  declaring an id, leaving a resolved reference dangling — **`Rejection.StillReferenced(target, referrers)`**. `Ops.footprintReferenced` reads every
  id a script writes a reference to, `Footprint.reading` is the builder for a domain op that does, and
  `Arbitration.arbitrateReferenced` composes the pair.
- **`Graph`** (in `Fuaran.Core.Propagation`): `sort`, `cycleThrough`, `dependents` and `TopoResult`,
  `Propagation`'s own, under the name a consumer with no evaluator looks for.
- **Kit:** `Conformance.containmentLaws` (opt-in, `SeamNotEveryDomainHas`) and
  `Conformance.referenceLaws` (opt-in, `NeedsWitnessCapability`), both guarded, both checked against a
  naive specification rather than the code under test.

**What breaks.** An exhaustive `match` over `Rejection` adds `IllegalChild` and `StillReferenced`; one
over `Diff.DiffError` adds `IllegalChildInTree`. All three are declared last, so every existing tag
keeps its number, and only the new forms raise them. `RejectionCodec` encodes them as `illegalChild`
and `stillReferenced`; `Rejection.code` answers the same words. `Fuaran.Core.Validator` now references
`Fuaran.Core.Propagation` (and through it `Fuaran.Core.Ops`). No existing member changes behaviour.

**What a consumer does.** A domain with a grammar declares it once and executes through
`applyGrammar` / `applyAllGrammar`, deletes its own parent-kind table check and refusal, and runs
`containmentLaws` at its grammar. A domain with references supplies a `RefWitness`, registers
`referenceIntegrity` (and `referenceOrder` if it defines before it uses), executes through
`applyReferenced` where a dangling reference must be refused at the remove, footprints its own
reference-writing ops with `Footprint.reading`, and runs `referenceLaws`. A cycle checker with no
visited set becomes one `Graph.sort`.

**The ladder.** `proofs/Preservation.fst` section 13 models the grammar engine over its result and
proves `grammar_preserves` (an accepted op keeps every parent's children legal, batches included),
`grammar_refines` (it accepts only what the container engine accepts, with the same tree, so section
8.5's invariant holds of it too — `grammar_preserves_contained`) and `grammar_trivial` (no grammar is
the container engine). The shipped engine reads only the pairs a step creates; that it agrees with the
model from a grammatical tree is `containmentLaws`' agreement law, tested, not proved. Nothing new is
extracted.

**Class: breaking (source)** — the three union widenings; every other member is additive. The slot was
already breaking (source). `api/Fuaran.Core.Ops.txt`, `.Validator.txt`, `.Propagation.txt` and
`.Conformance.txt` are regenerated; the roster gains two families and `docs/conformance-families.md` /
`.json` are regenerated. No wire byte moves.

### A decode layer with typed, path-carrying refusals (Phase 310, DECISIONS.md D99) — ADDITIVE; a malformed optional member is now refused where it was read as absent

**What changed.** `Decode`'s combinators answered `Result<_, string>`: no optional or defaulted member,
no reader for half the `JVal` kinds, no path, no tag dispatch, no accumulation. Beside them, in
`Fuaran.Core.Wire`:

- **The refusal.** `DecodeError { Code; Path; Expected; Message }` over the closed `DecodeCode` set
  (`InvalidJson`, `MissingField`, `WrongKind`, `UnknownTag`, `OutOfRange`, `UndeclaredMember`,
  `LimitExceeded`, `NotAdmitted`, `SchemaFault`), a path of `PathSegment.Key` / `Index` steps from the
  root, and the `DecodeError` module (`codes`, `codeName` / `tryCodeOfName`, `make`, `under`,
  `within`, `reword`, `describe`, `render`, `toJson`, `resolvesIn`, `ofJsonError`). `DecodePath`
  renders (`$["a"][0]`), resolves, and carries a path as a JSON array.
- **The decoders.** `Decoder<'T> = JVal -> Result<'T, DecodeError>` and the `Decoder` module: `str`,
  `int`, `float`, `bool`, `items`, `obj`, `json`; `field`, `optField` (absent → `Ok None`, present and
  refused → the refusal), `fieldOr` (any kind, through its decoder), `at`; `list`, `mapListIndexed`,
  `boundedList`, `listAll`; `sequence` and the accumulating `all`; `oneOf`, `tagDispatch`,
  `kindDispatch` (a miss names every known tag); `intRange`; the strict policy `members` / `closed` /
  `undeclared` (Phase 251's `ReadPolicy.Strict`, generalised); `parse`, `parseWith`, `ofString`,
  `describing`, `succeed`, `fail`, `map`, `bind`, `andThen`, `wrongKind`, `missing`, `tryMember`.
- **The refusal law and reject vectors.** `Corpus.RejectVector`, `runRejects`, `mutations` and
  `refusalLaws`: every refusal a decoder raises over a structurally mutated encoding carries a path that
  resolves in that document.
- **Typed entry points beside the string ones.** `Idl.Decode.valueDetailed` / `decodeDetailed` /
  `decodeOpDetailed` (the interpreter walks natively typed; the path runs through the vocabulary),
  `Artifact.ofJsonDetailed` / `parseDetailed`, `Proposal.ofJsonDetailed` / `parseDetailed`,
  `CapabilityCodec.decodeJsonDetailedWith` / `decodeDetailedWith` / `decodeInvocationDetailedWith`,
  `CapabilityPipeline.decodeDetailed`. Each string form is the typed one's `DecodeError.describe`, and
  answers the sentence it always answered.
- **The codecs read through the layer, and their private readers are gone:** Proposal's
  `field`/`str`/`intOf`/`arr`, Artifact's `atKey`/`strAt`/`arrAt`/`arrOrEmpty`, the IDL interpreter's
  member reader, Query's `asBool` and `optIntOf`, the two strict-member copies in the capability and
  query codecs, and the pipeline codec's second copy of the value-space codec. `Diff`'s snapshot reads
  through the layer and stays tolerant by design. The string combinators in `Decode` (`getProp`,
  `asString`, `asInt`, `asBool`, `asFloat`, `kindOf`, `strField`, `intField`, `tryProp`) are forwards
  onto `Decoder`, kept for this draft and removed at the next breaking one.
- **`conformance/decode/`**: the decode reject family (`decode-rejects.json`, its own `manifest.json`,
  every code at the path it names over a shape grammar a host reads with its own combinators), emitted
  by `dotnet run --project tests/Fuaran.Core.Tests -- --emit-decode`, adoption `proposed` for every host.

**What breaks.** No signature. A decode BEHAVIOUR tightens on malformed input only: an optional member
that is present and of the wrong kind is refused where the reader took it for absent and read its
default — `QueryCodec`'s `nextPageToken`, the capability codec's `slotKind` (on a hole and on a
`slotTree` space), every optional member and omitted-when-empty array of a proposal document, and an
artifact's `deprecated` / `annotations` blocks when they are not objects. A document a writer produces
never carries such a member. Every sentence a well-formed or previously refused document produced is
unchanged.

**What a consumer does.** Nothing to keep compiling. To read refusals as data, call the `…Detailed`
form and branch on `Code`; to report where, render `Path`. A hand-written decoder moves onto `Decoder`
and deletes its own field readers; a reader that kept a lenient optional on purpose discards the
refusal at the call, by name.

**Class: additive** (surface). `api/Fuaran.Core.Wire.txt`, `.Idl.txt` and `.Function.txt` gain
members and types only. The malformed-optional tightening above is a behaviour change on input no
writer emits, recorded here rather than classed.

### The kit's non-degeneracy floor: every gated arm counts what it built and reds at zero (Phase 302, DECISIONS.md D100) — ADDITIVE: no public member moves; families go red on witnesses they passed

**What moved.** An audit ran every law family against deliberately broken witnesses and found one
recurring blind spot: an arm whose evidence is DRAWN and then gated on a difference the defective
witness cannot produce, so the law was skipped and nothing counted the skip. The floor closes it.

- **Replacements are redrawn, bounded, and counted where they are built.** `streamLaws`,
  `attestationLaws`, `dagLaws`, `checkpointLaws`, `captureReplayLaws` and both fresh-key arms of
  `idempotencyLaws` redraw a replacement op (or value, or key) until it genuinely differs, at most
  sixteen times; the count each guard reads is taken inside the gate. `checkpointLaws` demands a kept
  node with a forged op (its tamper cell was otherwise met by the seal arms alone), and
  `attestationLaws` demands the different-length-prefix half of its prefix law.
- **`hashFnLaws` gains three laws** — op-tamper detection, re-minting moves the head, and content
  addressing (two envelopes differing only in their op hash apart under one prev hash) — so a
  constant `HashFn` is red. Its result count moves from 4 to 7; the guard stays last.
- **`certify` folds `reducer`'s accepted/refused guards**, so a refusal-free `StreamGen` is red there
  as it already was in `certifyStream`. Its result count moves by two. Its header no longer claims a
  codec round trip beyond the JSONL cell it runs.
- **The confluence families are given non-empty independent pairs and told when encode is blind.**
  `footprintLaws` counts an independent pair only over two NON-EMPTY scripts — measured at this
  repository's reference witness, every pair it had counted held an empty script, so its soundness
  law had never commuted anything — and, like `concurrencyLawsWith` and `reconcileLawsWith`, draws a
  short independent pair (bounded) where the drawn one interferes. `reconcileLawsWith` counts a
  shape where its law is asserted (on a clean fold), counts the clean-fold law on the disjoint arm
  only, and counts an independent delta pair only when both deltas are non-empty. The four families
  (`footprintLaws`, `concurrencyLawsWith`, `reconcileLawsWith`, `arbitrationLaws`) demand an
  `encode-distinguished node`: a node `encode` that ignores its input is red. `arbitrationLaws` and
  `keyedArbitrationLawsWith` demand a `Conflicts` rejection beside the rejected proposals.
- **`diffLaws` and `normalizeLaws` leave `Unconditional`.** Each carries a guard — a non-identity
  pair, a script that moves the tree — and is `Guarded` in the census. Result counts move from 3 to 4.
- **`idempotencyLaws`' thresholds are fractions**: the fresh-key arms must be built on at least half
  their attempts and each outcome must be one in twenty of the arms built; a `keyOf` that is not a
  function is a counterexample, not a `KeyNotFoundException`.
- **`queryLawsWith` / `queryLawsAt` gain a result law**: a settled result's `Rows.Schema` equals the
  query's `ResultSchema` and `Table.validate` accepts the rows. The law carries it; `Query.invoke` is
  unchanged (no `QueryError` case is added — D100).
- **`propagationEvaluatorLaws`' honesty law** checks every id a node asks for is one it declares and
  that every reader of a removed node is named; its agreement law also replays an edit that moves the
  map from a survivor-restricted prior (counted beside the guard, not demanded).
- Smaller counts folded into existing guards: `keyedApplyLaws` (a keyed holder that can hold
  children), `containmentLaws` (an illegal drawn tree), `diffContainedLaws` (built grafts, counted
  beside), `FoldConfluence.laneFoldLawsWith` (a fold over two or more non-empty lanes).
- **The proof bridge**: the tree differentials in the suite compare accepted results structurally
  rather than by digest.

**The ladder.** `independence-diamond`'s `dischargedBy` moves from `Conformance.footprintLaws` to
`Conformance.footprintLawsAt` (the row is about a domain's own ops; the skeleton algebra's form is
proved at `tree-independence-diamond`), with the roster's `Discharges` field beside it.
`content-id-determines-content` is restated: it holds for the host's collision-resistant `HashFn` and
is false for the default at scale; `hashFnLaws` carries it, sampled; a premise is discharged by
nothing, so the row names no `dischargedBy`.

**What a consumer does.** Nothing compiles differently. Re-run your families at your next pin: a red
that was not there before is information your witness or generator did not give the kit until now —
widen the generator (the counterexamples name the starved arm); do not raise the iteration count. A
reader that pins a family's result count re-pins `hashFnLaws`, `diffLaws`, `normalizeLaws`, `certify`
and `queryLawsWith`.

**Class: additive** — no public type or member moves (`api/Fuaran.Core.Conformance.txt` unchanged);
families go red on witnesses they passed. `docs/conformance-families.md` / `.json` are regenerated. No
wire byte moves.

### The ladder tells the truth about production (Phase 309, DECISIONS.md D101) — ADDITIVE: no public surface moves

**What changed.** Proof rows, a proof-leg step and a regenerated document; no shipped package changes.

- `Dag.tryReplayTo` is a theorem over the proved drain (`proofs/DagFold.fst` section 16:
  `replay_to_is_fold_over_drain`, `replay_to_unknown_head_is_refused`, `replay_to_deterministic`), and
  the refusing append is too (`append_refuses_differing_node`, `append_never_replaces`), each with a
  differential that can lose. `node-ids-distinct` narrows to "the hash is injective on the nodes a
  store holds".
- `chain_tamper_evident_iff_hash_distinguishes` (`proofs/Chain.fst` 6c): an op tamper is rejected by
  `verifyChain` exactly when the hash distinguishes the two envelopes — no premise.
- The id-witness equality is named (`id_key_faithful`, carried on `lawful-abstract-witness`);
  `witness-surface-scope` is marked `"discharge": "domain-declared"` — a new optional member of an
  assumed `domain-obligation` row in `proofs.json`, and a `domain-declared` count in the
  `Proofs.Coverage` line, which now reads `… N domain-discharged, N domain-declared, …`.
- Twin evaluation: every extracted model declares `twins` asserted by `assert_norm`, extracted into
  the oracle and run by the `Proofs.Oracle` family; the kit gains step 2c, `-Twins`, which refuses an
  extracted model that declares none. A repository that adopted the kit by copy is unaffected until
  it passes `-Twins`.
- The tree differentials' generated pool draws `UpdateNode` and `Batch`; the two hand-written update
  cases are retired. Stale counts and the diff's `kinds_agree` reason are corrected.
- `proofs/README.md`'s header count, contract table and contract counts are generated from
  `proofs.json` (`CORE_APPROVE_LADDER=1`).

**What a consumer does.** Nothing. A reader that parses the `Proofs.Coverage` line adds the
`domain-declared` field. An adopter of the kit that wants twin evaluation adds a `twins` list to each
extracted model and passes `-Twins`.

**Class: additive** — no `api/*.txt` baseline moves and no wire byte moves.

## 0.33.0 — released 2026-10-01 as `v0.33.0`

**Release record — the receiving gate (Phase 276): GREEN, both legs, against the candidate.**
On 2026-10-01 the candidate was packed from commit `095d6f9` (every one of the 17 packable projects,
version `0.33.0`, into a folder) and the downstream host's Fable gate was run against it in its
cut-time mode (`tests/core-fable/core-fable.ps1 -CoreVersion 0.33.0 -CoreFeed <folder>`). The first
run FAILED at the host's membership check: its own exclusion list still named `Fuaran.Core.CSharp`,
which this slot removes (Phase 231); this repository's `fable-exclusions.json` had already dropped it.
The host then dropped the entry, and changed its gate in two ways a split producer needs: a Core-only
cut no longer compiles the compute packages (they pin an older Core until this release is published,
so their own producer's cut gates them, and the run prints that it skipped them), and its smoke
program compiles against both Core lines (`EffectClass.Determinism` is a set from this slot, Phase
319). The re-run passed: compile leg green over 15 Fuaran.Core packages, value leg 307/307. **Phase 330 then
landed on this slot** (the kit's last three `…With` entries reordered, six witness records frozen), which
moves the Conformance package the gate compiles, so the candidate was re-packed from commit `727f2e5`
and the gate re-run. That run is the one this slot cites: **compile leg green — 15 Fuaran.Core packages
transpiled at 0.33.0, compute packages skipped — and value leg green, 307/307 vectors byte-identical
on both pipelines at 0.33.0.** No run named a defect in Core's code. Only this ledger has changed
since `727f2e5`, so the packages a release builds are the ones the gate measured.

**It is a MINOR release because the change that opened it is BREAKING.** `0.32.0` is tagged, so it is a
consumer's contract and nothing rides it. Phase 258 removes four packages from this repository's
roster — `removal`, breaking for a consumer that takes them from here — and a breaking change opens a
minor slot rather than a patch one. `<Version>` and the laws corpus here (`conformance/laws/capability-laws.json`) were
re-stamped in the same commit as the version move, per `docs/conformance-corpus.md`; the byte copy in
the shared wire-format corpus is re-synced separately.

### The compute strand leaves: moved, not removed (Phase 258) — BREAKING, `removal`

**What changed.** DECISIONS.md D66 ruled that the compute layer is produced by a repository of its own,
because its programme (performance, and breaking changes to reach it) is not this spine's (stability,
laws, proofs). Phase 257 prepared the line inside this tree; this phase cuts it. Four package ids are
no longer produced here:

| Package id | Last emitted here | Continues from |
|---|---|---|
| `Fuaran.Core.DataFrame` | `0.32.0` | [`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute) at `0.33.0` |
| `Fuaran.Core.Column.Ops` | `0.32.0` | the same repository, at `0.33.0` |
| `Fuaran.Core.DataFrame.Conformance` | `0.32.0` | the same repository, at `0.33.0` |
| `Fuaran.Core.DataFrame.CSharp` | `0.32.0` | the same repository, at `0.33.0` |

They are **moved, not removed** (D7's rule for a package that changes producer): the ids, the CLR
namespaces and the public surfaces continue unchanged from the new producer, which opened at `0.33.0`
so that no version number names two contracts. With them went their tests, the two proof models
(`proofs/ColumnOps.fst`, `proofs/Pipeline.fst`) and their claims-ladder rows, their public-surface and
wire-surface baselines, the transform law vectors (`conformance/laws/transform-laws.json`, now the
compute repository's derived file, stamped with its version), the Phase 257 forwarding module (it
lived in `Fuaran.Core.DataFrame.Conformance`, so it goes with that package; the compute repository
retires it in its own change-set) and `docs/incremental-evaluation.md`. The entries below for versions up
to `0.32.0` stay here as the record of those packages' history — the compute repository's
`STABILITY.md` points at them rather than restating them, so they are not edited or removed — and
every change to the four packages from `0.33.0` on is recorded there.

**What stays, and why.** `Fuaran.Core.Column` stays: `Table`, `Schema` and `DataSource` are the types
the `Query` seam produces and declares, the `Validator` column rules check and the kit's columnar
families read, so the seams need it, and the compute packages are built over it. `Propagation`,
`Query`, `Conformance` (with its `aggregateNullSkipLaws` and `columnarValidatorLaws` families) and the
C# facade's column half stay for the same reason. No surviving package's public surface moved.

**What adopting it costs.**

- **A consumer that pins this repository for any of the four ids** keeps restoring what it pinned —
  every version this repository published (up to `0.32.0`) stays on nuget.org, which is immutable. To
  move past `0.32.0` it raises those ids to the compute repository's release with a **second**
  `PackageVersion` property, one per producing repository (for example `FuaranCoreVersion` for the
  spine and `FuaranCoreComputeVersion` for the four), because the two repositories version
  independently and one property spanning both can never be correct.
- **A consumer of the spine only** raises `FuaranCoreVersion` to `0.33.0` and, for THIS move, changes
  nothing else. The decimal entry below is a second move in the same slot and has a cost of its own.
- **A consumer that references both** sees no compile change: the compute repository's `0.33.0` is
  built over this repository's `0.32.0` substrate, whose surface this slot does not move.

**What the surface gate printed, and what it did not.** Measured on this change: `18 baseline(s)
read, 0 moved` and `no baseline has moved since v0.32.0` — every surviving package's public surface
renders exactly as tagged. The gate reads the baselines of the packages the tree still ships, so a
package that leaves takes its baseline with it (`api/` loses four files, `api/wire/` the two for
`Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops`) and no class is printed for it. The class of
this slot is therefore this entry's statement rather than a gate output: four whole surfaces are
removed from the roster, which is `removal` — breaking — for any consumer that takes them from this
repository, and nothing for one that does not. The boundary test (`ComputeBoundaryTests`) is now an
assertion of ABSENCE: no project, directory, package reference or built spine assembly in this tree
names one of the four ids.

- **`Column.aggregate` reads its input once per function branch; results unchanged — BEHAVIOUR-IDENTICAL,
  no public surface moves.** The present-cell list is built only on the branches that read it
  (`Count`, `CountDistinct`, `Min`, `Max`) instead of for every function; the numeric aggregates build
  their numbers alone and `First`/`Last` read neither. Every pinned semantic is kept: nulls skipped,
  `Null` for an empty numeric input, int64 accumulation with the int32 range check, `Median` by sort,
  `CountDistinct` by the distinct token, `Min`/`Max` keeping the first of an incomparable pair, and
  `First`/`Last` including a `Null`. It costs a consumer nothing.

### An exact decimal in the column model (DECISIONS.md D72) — BREAKING, `union-widening`

**What changed.** The scalar set gains `decimal`: `ColumnType.DecimalType` and `Cell.Decimal of
string`, the cell carrying canonical decimal text (`0`, `12`, `-3.5`, `0.05`). Both are declared last
in their unions, `Decimal` after `Null`, so every case already published keeps its tag. Beside them:

- **`DecimalText`** — `tryCanonical`, `isCanonical`, `compare`, `add`, `tryToFloat`, `zero`. Exact,
  arbitrary precision, FSharp.Core only.
- **`Cell.decimal`** — the constructor, which canonicalises and answers `None` for text that is not
  decimal.
- **`ColumnType.widens`** — `int → decimal` is a safe widening. `float` and `decimal` are not
  interchangeable in either direction.
- **`Column.aggregate`** — a decimal column is numeric. `Sum` is exact and is a `Decimal`; `Min` and
  `Max` compare exactly; `Mean`, `Median` and `StdDev` are `float`, as `aggType` declares. The
  `expected` list an `IncompatibleAggType` carries is now `int`, `float`, `decimal`.
- **`ColumnCodec`** — a decimal is a JSON **string** on the wire. Decode canonicalises the text
  (`12.50` reads as `12.5`), reads an integer token, and refuses a fractional number token as a
  `TypeMismatch` and text that is not decimal as a `MalformedShape`. `tryEncode` refuses a `Decimal`
  cell whose text is not canonical. Schema inference never infers `decimal`.
- **`Query`** — a parameter or a result column may be declared `decimal`, and the capture key tags a
  decimal binding `m`.
- **`ColumnValidator.inRange`** — reads a decimal at its nearest float, against its float bounds.
- **The C# facade** — `ColumnKind.Decimal`, `CellValue.Decimal`, `CellValue.TryDecimal`, and an
  `onDecimal` argument on `CellValue.Match` and `CellValue.Switch`, before `onNull`.

**What adopting it costs.**

- **Every exhaustive `match` over `Cell` or `ColumnType` stops compiling** until it names the new
  case. That is the intent of a closed union, and it is the whole of the cost for an F# consumer that
  does not use the type.
- **Every C# call of `CellValue.Match` or `CellValue.Switch`** gains one argument. Named arguments
  stop compiling with a message naming `onDecimal`; positional ones stop compiling on the arity.
- **A host in another language** mirrors the type when it raises: the tag `decimal`, the canonical
  form, the string on the wire, the refusal of a fractional number token, and the capture-key tag
  `m`. Until it does, a document carrying a decimal column is an `UnknownType` to it, which is the
  refusal that host already makes for any tag it does not know.
- **A test that used `"decimal"` as its example of an unknown type tag** needs another example.
  This repository's own suite did.
- **Nothing on the wire changes for a document that carries no decimal.** Every existing document
  encodes to the bytes it encoded to before.

**What the surface gate printed.** Measured on this change, before the baselines were regenerated:
`Fuaran.Core.Column — union-widening (13 move(s))` and `Fuaran.Core.CSharp — retype (5 move(s))` on the
managed surface; `Fuaran.Core.Column additive, 2 move(s)` and `Fuaran.Core.Query additive, 6 move(s)` on
the wire surface. Four baselines moved with the change: `api/Fuaran.Core.Column.txt`,
`api/Fuaran.Core.CSharp.txt`, `api/wire/Fuaran.Core.Column.txt` and `api/wire/Fuaran.Core.Query.txt`.
The wire class is `additive` and the managed class is breaking, so the move rides this slot, which is
untagged and already breaking, and advances nothing.

**What was checked, and what was not.** `proofs/Query.fst` carries the new case. It was verified with
the pinned prover on three cold runs, every query 3/3 under `--quake`, and a fresh extraction of
`proofs/oracle/Query.fs` is byte-identical to the committed one; the differential families in the
ordinary suite run green against it. That is the one model this change touches: the full leg over
all twenty-one was not run on the machine the change was made on, and is the continuous-integration
job's. **The Fable gate has not been run over this change.** The new code is written to the
Fable-clean subset the rest of the package keeps, and that is a claim until the gate every cut
cites says so; a release of this slot waits on it.

### The proposal-pricing harness leaves `Fuaran.Core.Idl.Codegen` for the CLI (Phase 230) — BREAKING, `removal`

**What changed.** `ProposalSpike` (`run`, `render`) and the four records it is driven and reported
through — `SpikeInput`, `SpikeLeg`, `SpikeReport`, `ExternalLeg` — are no longer public members of
`Fuaran.Core.Idl.Codegen`. The harness prices a vocabulary-change proposal against a vocabulary
without cutting a branch; it is an operator tool, not part of the generation library, and it now
lives behind the command that runs it, as the `spike-proposal` verb of `fuaran-core-idl`
(`Fuaran.Core.Idl.Cli`), internal to that assembly. Its flags (`--idl`, `--corpus`, `--out`, `--seed`,
`--vectors`), report and exit codes (0 every leg passed, 1 a leg failed, 2 the document did not read)
are the ones the repository's own test runner carried as `--spike-proposal`, which no longer exists
there. The `Fuaran.Core.Idl.Spike` project, which was never packable, is gone from `src/`; the mini
vocabulary and the F# emitted from it moved into the test project as fixtures. No other package's
surface moves, and `api/Fuaran.Core.Idl.Cli.txt` does not move either (the harness is internal).

**Class: `removal` — breaking, and it RIDES this slot rather than advancing it.** The surface gate
printed `Fuaran.Core.Idl.Codegen removal 27 move(s)`, every one a `-` line over the harness and its
four records (constructors, record fields, the two functions, the type entries), and nothing added.
A consumer compiled against any of them would stop compiling, so this is a removal of members a
consumer could have pinned, and it is classed as one. The `0.33.0` slot was already opened by a
breaking change (`removal`, Phase 258) and is an untagged, publicly unpinned draft, so a change of
the same class rides it: `<Version>` does not move and the baseline `api/Fuaran.Core.Idl.Codegen.txt`
is regenerated in the same commit.

**What adopting it costs.** A consumer of `Fuaran.Core.Idl.Codegen` that called `ProposalSpike.run` /
`render` replaces the call with the command: `fuaran-core-idl spike-proposal <proposal.json> --idl
<idl.json> [--corpus <dir>]`, which takes the vocabulary as an artifact file rather than an `Idl`
value and writes the report to standard output. The in-process `ExternalLeg` hook (a generated
TypeScript module run under a JavaScript runtime) was never reachable from the command and is not
reachable now; a consumer that used it has the harness source in this repository's history. A
consumer that never referenced those names changes nothing.

### The witness-record field freeze is held by a law family now, not at the `1.0` release candidate (Phase 232) — BREAKING, `union-widening`

**What changed.** The freeze section above promised its mechanical backstop "alongside the first `1.0`
release-candidate", and until now nothing mechanical held the six frozen records: the surface gate
classes a field add as `record-widening` and refuses only an UNCLASSIFIED move, so a field add that
landed with its baseline regenerated passed. `Conformance.witnessSurfaceLaws ()` is that backstop,
and this repository's suite runs it on every gate. It takes no witness and no seed. On .NET it
answers seven laws: one per frozen record, holding the record's field set — read by reflection from
the compiled record — equal to its pin in `Conformance.frozenWitnessFields` by name and in declaration
order; and a coverage law holding that every public record named `…Witness` in the Fuaran.Core
assemblies the kit references is frozen there or declared outside the freeze, with why, in
`Conformance.unfrozenWitnesses`. Under Fable the coverage law is absent rather than reported green —
a transpiled program has no assembly to enumerate — and the six field laws run as on .NET. The freeze
section now names the family and states the opt-out for a deliberate widening before `1.0`.

**The coverage law's classification is a finding, not a restatement.** Twelve public records end in
`Witness`, not six: the conformance kit's own inputs (`CapabilitySeamWitness`, `QuerySeamWitness`,
`CapabilityPipelineWitness`, `ConstructWitness`, `KeyedWitness`, `EvaluatorWitness`) were never named
by the freeze. They are declared OUTSIDE it, each with its reason, rather than silently frozen:
extending a `1.0` promise to six more records is a decision this entry does not take by default, and
declaring them is what lets the law refuse a thirteenth witness that nobody classified. Moving one
into the freeze is a move from `unfrozenWitnesses` to `frozenWitnessFields` with an entry here.

**The roster gains the family and a reason for it.** `Families.OptInReason` gains
`NoWitnessToCertify` (wire token `no-witness-to-certify`): the family certifies the Core a domain
compiled against rather than the domain, so no aggregate runs it, and the three existing reasons all
describe a family relevant to only some domains, which this one is not. The census class is
`Unconditional` — nothing is drawn; the one run is the whole sample — and the refusal audit reads
`none`. `docs/conformance-families.{md,json}` are regenerated (60 families).

**Class: `union-widening`**, printed by the surface gate as
`Fuaran.Core.Conformance — union-widening (7 move(s))`: one union case, three module functions
(`witnessSurfaceLaws`, `witnessFieldsLaw`, `witnessCoverageLaw`) and two values
(`frozenWitnessFields`, `unfrozenWitnesses`). The shard called the change additive; the new
`OptInReason` case is what makes it breaking-source — an exhaustive `match` over the union stops
compiling — and the gate decides (D45). This slot is untagged and already breaking, so the move rides
it and advances nothing. One baseline moved: `api/Fuaran.Core.Conformance.txt`. No wire surface moved.

**What adopting it costs.** A consumer that matches exhaustively on `Families.OptInReason` adds the
`NoWitnessToCertify` arm. A consumer that reads the roster export and dispatches on the `reason`
token meets one new token. Nothing else: no witness record moved, and a domain that never calls the
new family is unaffected. A domain that wants the pin-bump check calls
`Conformance.witnessSurfaceLaws ()` and reads the seven results.

**Shown failing first.** With `IdWitness`'s first two fields swapped in the tree (a move that compiles
at every recompiled construction site, since records are built by name), the suite failed
`witness surface (IdWitness)` naming the reorder — and it still failed after the `api/` baselines were
regenerated over the perturbed tree, which turned the surface gate green. The suite also carries decoy
go-reds: a record with one extra field fails naming the record and the field, a reordered one and a
non-record fail, and the coverage law refuses an unclassified `…Witness` record and a pin with no
record behind it. **The Fable gate has not been run over this change**; the field laws use only
`FSharpType.IsRecord` / `GetRecordFields` and the assembly walk sits behind `#if !FABLE_COMPILER`,
which is a claim until that gate says so.

### An unknown node-actor kind refuses instead of reading as `Human` (Phase 260) — BREAKING for a store that carries one; no public surface moves

**What changed.** The two JSONL readers that decode a node's actor — `OpStream.fromJsonl` (with
`fromJsonlWithSnapshots` and `fromJsonlVerified`, which are built on it) and `Dag.fromJsonl` (with
`Dag.fromJsonlVerified`) — matched `"agent"` and sent every other `kind` to `Human`, carrying whatever
`id` the object had. A store written by a build that knows a third kind of actor was therefore read by
an older reader as a person, which is the misattribution the actor field exists to prevent. The match is
now closed: `"human"` decodes to `Human`, `"agent"` to `Agent`, and any other `kind` — or an actor object
with no `kind` at all — is a decode `Error` through the readers' existing `line N: <reason>` channel,
naming the kind:

```
line 0: OpStream.fromJsonl: unknown actor kind "service"
line 0: Dag.fromJsonl: the actor carries no kind
```

(`Dag.fromJsonl:` for the DAG reader.) Nothing else about the readers changes: a record with `"human"` or
`"agent"` decodes to the same value as before, and the canonical encoder, the chain hash and every
emitted byte are unchanged.

**The class, stated plainly.** This is a change of BEHAVIOUR on read, not of surface. The managed and wire
baselines do not move and the gate prints no class for it — the gate cannot see it, which is why it is
recorded here. For a consumer it is BREAKING in exactly one case: a store that holds an actor with a
`kind` other than `human` / `agent`, or with no `kind`, which an older build read as `Human` and this
build refuses. Core's own encoder has never written such a record, so a store written only by Core
reads exactly as before; one that was hand-edited, or written by a newer producer of the format, does
not. It is not `additive`: a record that decoded before stops decoding.

**What a consumer sees, and what a host should do with it.** A store that carries an unknown kind now
fails to load with the `Error` above rather than loading with that node attributed to a person. The text
names the kind, so a host can surface it as **version skew** — the store was written by a newer or
different writer than this reader understands — and tell the operator to raise the reader, rather than
report corruption. The refusal is whole-store, as every other decode fault in these readers is: no
record is returned, so no node is silently mis-attributed. A host that wants to tolerate a newer actor
kind has to decide that explicitly; this change removes the option of doing it by accident. The
pre-320 reader, `OpStream.fromJsonlLegacyActor`, is unaffected: it reads the bare-string actor and lifts
it to `Human` by design.

**What adopting it costs.** Nothing for a store Core wrote. For a host that reads a store it did not
write with these readers, read the `Error` case of `fromJsonl` where it may have ignored it; it was
previously unreachable for an actor-kind fault.

### The C# facade is removed (Phase 231, DECISIONS.md D28) — BREAKING, `removal`

**What changed.** `Fuaran.Core.CSharp` is no longer produced by this repository. It shipped at
`0.22.0` (Phase 128) under D9's single-consumer exception, as a C#-shaped facade over the column
layer (`CellValue`, `TableValue`, `SourceValue`, `ColumnKind`, `AggregateFunction` and the
`Vocabulary` bridge), the artifact-function hole-declaration family and the wire JSON model
(`JsonValue`). Phase 231 re-measured D28's premise: the consumer it was shipped for never adopted it
and keeps its rule another way, and the one package that reads it is the compute repository's
`Fuaran.Core.DataFrame.CSharp` — the dataframe half of the same facade, which that repository removes
in the release that raises its pin to `0.33.0`. So both halves go, and D28's other criterion, a C#
veneer generated from the IDL, is the route by which one returns. With the package went its proof
project (`tests/Fuaran.Core.CSharp.Proof`, and the gate stage that ran it), its baseline
(`api/Fuaran.Core.CSharp.txt`), its entries in `fable-exclusions.json` and
`proofs/coverage-exclusions.json` (seven exclusions remain), and its row in the README roster.

| Package id | Last emitted here | Continues from |
|---|---|---|
| `Fuaran.Core.CSharp` | `0.32.0` | nowhere — removed, not moved |

**Class: `removal` — breaking, and it RIDES this slot.** A package id a consumer can pin stops being
produced. The `0.33.0` slot was opened by a `removal` (Phase 258) and is an untagged, publicly
unpinned draft, so a change of the same class rides it and `<Version>` does not move. The surface
gate reads the baselines of the packages the tree still ships, so a package that leaves takes its
baseline with it and no class is printed for it: measured on this change, `17 baseline(s) read, 3
moved`, the three being the other entries in this slot, and the class of this entry is its
statement rather than a gate output. No surviving package's public surface moves.

**The decimal entry above names facade members that never ship.** `ColumnKind.Decimal`,
`CellValue.Decimal`, `CellValue.TryDecimal` and the `onDecimal` argument were added to this package
in this same untagged slot; with the package removed they are not released, and the `retype` the gate
printed for `Fuaran.Core.CSharp` there costs no consumer anything.

**What adopting it costs.**

- **A consumer that pins `Fuaran.Core.CSharp`** keeps restoring what it pinned — `0.22.0` to `0.32.0`
  stay on nuget.org — and cannot raise it past `0.32.0`. To move its other `Fuaran.Core.*` pins to
  `0.33.0` it drops the reference and constructs Core's values through the F# surface, wrapping only
  what it authors.
- **The compute repository's `Fuaran.Core.DataFrame.CSharp`** is the one known reader. It is removed
  in the same change-set that raises that repository's pin to `0.33.0`, because its reference to this
  package cannot resolve after it.
- **A consumer that never referenced the package** changes nothing.

### `streamLaws` guards its sample, `laneFoldLaws` counts its rejected lanes, and an aggregate's pass reads its counts (Phase 245) — BREAKING for a stream generator that never reaches an accepted op or a tampered chain; no public surface moves

**What changed.** `Conformance.streamLaws` was censused `Unconditional` ("each iteration applies,
replays and tampers the same chain"), which is true only of a generator whose ops the domain
accepts. The chain is built from ops the caller's `StreamGen` DRAWS, and a refused op does not
extend it. A downstream consumer measured this at `0.30.0`: with a generator whose every op the
domain refused, every chain was empty and all three stream laws — an intact chain verifies, replay
re-derives the live state, a tampered op is detected — held green over nothing, inside `certify`
and `certifyStream` alike. The family now counts the ops it accepted and the chains it actually
tampered, and reports two `sample adequacy (Conformance.streamLaws)` laws, `accepted op` and
`tampered chain`, on the Phase 220 pattern. Its census row is `Guarded [ "accepted"; "tampered chain" ]`
and its refusal-audit row is `drawn` (the tampered chain is built only over a chain the generator
fills).

**The second side is the tampered chain, not a non-empty one.** The shard asked for "at least one
accepted op and at least one non-empty chain". In this family's loop those are the same event — a
chain is non-empty exactly when an op was accepted into it — so a guard over both would demand one
thing twice. The evidence the third law reads is a chain it TAMPERED, and a generator that draws
one op, always the same one, fills every chain and never tampers any of them (every substitute
encodes like the op it replaces). That is the second side.

**`FoldConfluence.laneFoldLaws` / `laneFoldLawsWith` count `LaneRejected`.** A lane set the reducer
rejects under every arrival order is now counted beside the folded and halted sets the guard
demands, and named in the guard's counterexample
(`lane-fold outcome reached folded=0 halted=0 (counted beside them, not demanded: rejected=20)`).
It is counted and not DEMANDED: the fold-determinism law holds over it, but it tests neither a clean
fold nor a halt, and a domain whose reducer accepts everything its footprint lets through must not
be starved for never producing one. A sample made only of rejected lanes was already starved (both
demanded outcomes read zero); the line now says why. The law text is unchanged.

**An aggregate's pass reads its counts through `SampleAdequacy.cases`.** `certify` and
`certifyStream` return every law and every guard of the families they run, so
`SampleAdequacy.cases "<aggregate>" (Guarded …) iterations report.Results` is the aggregate's
subject laws times its iterations, with every starved side named — `1000` for `certifyStream` at
200 iterations over the reference domain. The suite holds that reading, and the generated
`docs/conformance-families.md` says it in its `Cases` paragraph. Every family's own count was
already carried on the pass path by Phase 196's `cases` column; this entry adds nothing to
`CaseCount` or to `LawResult`.

**Class.** The surface gate prints no move: the rejected count is rendered through an `internal`
helper beside `SampleAdequacy.reached`, whose own signature and law text are unchanged. By the
reading `0.31.0` applied to `certify` (Phase 220), a verdict change on a certifying family is
breaking whatever its member's surface class, and this is one: `streamLaws`, `certify` and
`certifyStream` go RED for a generator that never reaches an accepted op or a tampered chain. The
shard called the change additive; that holds for the `laneFoldLaws` half and not for the
`streamLaws` half. This slot is untagged and already breaking, so the change rides it. No baseline
under `api/` moved. No wire surface moved. `docs/conformance-families.{md,json}` are regenerated:
`Conformance.streamLaws` reads `guarded-reached` and `drawn` where it read `unconditional` and
`built`.

**What adopting it costs.**

- **A domain whose stream generator reaches an accepted op and a tampered chain** — any generator
  that draws two ops that encode differently and that the domain accepts — sees two extra green
  `LawResult`s from `streamLaws` and from each aggregate. A law-count assertion pinned at the old
  count moves by two (`certifyStream` reports nine, `certify` nineteen at a witness whose `opAlgebra`
  and `diffLaws` report as the kit's reference does).
- **A domain whose generator is refused every time, or draws one op only,** turns red, deliberately:
  its stream laws were certifying nothing. Widen the generator — draw an op the domain accepts, and
  a second that encodes differently — rather than raising the iteration count.
- **A `laneFoldLaws` caller** sees a longer counterexample on a red guard and nothing else.

**Shown failing first.** The new tests were run before the guards existed: the `streamLaws` guard
tests failed or errored (no guard law to find, and `certify` / `certifyStream` green over a
generator that refuses every op), and the rejected-lane test failed on a counterexample reading
`folded=0 halted=0` with no mention of the twenty rejected lane sets. Each passes now; the reference
stream generator reaches both guarded sides on twenty further seeds at the census's size.

**Not done here: the reached counts of a PASSING guard.** A green guard still carries
`Counterexample = None`, so a guarded family that reached a side six times in two hundred reports
the same pass as one that reached it every time; the census's `Cases` is the run's size, not its
reach. Carrying the per-side counts on a pass needs a place on `LawResult` to put them, which is a
record-shape change to the kit's most-constructed type, and is left for a decision rather than
taken inside this entry.

### The gate closes its blind spots, and the packages gain metadata (Phase 294) — ADDITIVE; no public surface moves

**What changed.** Two halves, neither of which touches a type, a function or a wire byte.

*The build.* `FS0025` (an incomplete match) is an error in every project of this repository, by
number in `Directory.Build.props` rather than through `TreatWarningsAsErrors`. The one exception is
the extracted proof oracle, which carries its own override with the reason beside it. Turning it on
revealed no incomplete match anywhere in the spine, the suites or the sample, so nothing had to be
rewritten to land it. It is a property of how THIS repository compiles: the `fable/` source
distribution a consumer compiles is built under the consumer's own settings, so no consumer meets it.

*The packages.* Every packable id now builds `Deterministic`, with `ContinuousIntegrationBuild` set
on a CI runner (and only there: locally it would hide real source paths from a debugger), ships the
inputs SourceLink reads (`RepositoryUrl`, `PublishRepositoryUrl`, `EmbedUntrackedSources`), carries its
symbols as a `snupkg` beside the `nupkg`, and gains `PackageReadmeFile` (the repository README) and
`PackageTags`. A packed id is therefore two files rather than one, and the nuspec gains `readme`,
`tags` and the `repository` commit.

*The release path.* The publish workflow refuses a ref that is not the tag `v<Version>` for the
`<Version>` in `Directory.Build.props`, runs the repository gate (`verify.ps1`) before it packs, and
pushes the symbol packages beside the packages.

**What adopting it costs.** Nothing. A consumer restores the same assemblies with more metadata
beside them; `api/` and the wire baselines did not move.

**Class: additive.** The packages gain metadata and the gate gains arms; no public surface changes.
See DECISIONS.md D75 for the `FS0025` override, the publication sweep (a standing arm of the suite)
and the re-run of D29's reproducibility measurement: with the CI property set, two checkouts at
paths of equal length produce byte-identical assemblies, and paths of different length differ in
67 bytes of PE debug-directory padding and nothing else; byte-identity of a local pack against the
published package is still not claimed.

### One string escaping on the spine: every control character is `\u00xx` (Phase 287, DECISIONS.md "the spine owns the string-escaping rule") — BREAKING, wire bytes; the managed surface moves `additive`

**What changed.** `Wire.Json.escape` — and with it `Json.render`, `Json.encode`, every `Codec`
built on them, and `Canon.render`, which now escapes through the same function — spells `\n`, `\r`
and `\t` as `\u000a`, `\u000d` and `\u0009`. Before, those three had short forms and only the other
twenty-nine control characters were `\u00xx`. The rule is now exactly three classes: `"` → `\"`,
`\` → `\\`, `U+0000`–`U+001F` → lower-case `\u00xx`, nothing else. `Actor.encode` in
`Fuaran.Core.OpStream` and `Dag.toJsonl` in `Fuaran.Core.OpStream.Dag` carry the same rule — they
are deliberate copies of it, because DECISIONS.md D2 keeps both packages free of a `Wire` reference,
and `StringEscapeVectors` (new, in `Fuaran.Core.Conformance`) pins every byte the three emit for
every character the rule escapes against one table, so a copy cannot drift quietly. `Canon.render`'s
bytes do not move: it already wrote `\u00xx` for every control character (WIRE_FORMAT §2 rule 6),
and its private copy of the rule is deleted in favour of `Json.escape`.

**Why.** The UI host's canonical encoder and the TypeScript twin already wrote every control
character as `\u00xx`, and their comments claimed byte-identity with this package's `Actor.encode`.
The claim was false, and the place it mattered was the chain hash: the UI's linear chain folds
through `OpStream.canonicalConfig.Payload`, so for an actor id, model or version carrying CR, LF or
TAB the .NET linear hash disagreed with the TypeScript linear hash AND with the .NET DAG hash of the
same record. Nothing in any shared corpus carried such a record, which is why every host was green.
The spine owns the rule now, and the rule is the one the hosts already held.

**The class, stated plainly.** BREAKING on the wire and in the chain: any string containing `\n`,
`\r` or `\t` renders to different bytes, and any chain record whose actor carries one of those three
hashes differently. NOT breaking for a string or a record that carries none — which is every record
this package's tests, its samples and the shared chain corpus have ever written — and the gate's
managed-surface classes are `additive` (`OpStream.legacyEscapeConfig`; the `StringEscapeVectors`
module). Reading is unchanged: `Json.parse`, `OpStream.fromJsonl` and `Dag.fromJsonl` accepted
`\u000a` before and still accept `\n`, so a document written by either version reads under both.

**What adopting it costs.**

- **A consumer that renders strings through `Json.render` / a `Codec`** and pins their bytes — a
  golden file, a hash over the encoding — sees the golden move exactly where a string carried one of
  the three characters. This package's own such pins (`ParityVectors`' `witness/render` vectors, one
  `Wire` test) moved that way in this change-set.
- **A linear op-stream store written between Phase 320 and this change whose actors carry a
  control character** no longer verifies under `canonicalConfig`. The Phase-255 shape covers it:
  `verifyChainWith legacyEscapeConfig` confirms it intact, `rehash legacyEscapeConfig canonicalConfig`
  cuts it over — `legacyEscapeConfig` is the outgoing payload, kept beside `legacyActorConfig` (which
  is unchanged and still reproduces the pre-320 bytes, short escapes included, because that is what a
  pre-320 writer wrote). A store whose actors carry NO control character has byte-identical payloads
  under both configs: it verifies under `canonicalConfig` unmigrated, and the rehash is a no-op that
  reproduces every hash — the suite proves that rather than assumes it.
- **What no config reaches.** The chain config governs the ACTOR's spelling in the payload. A stored
  OP whose own domain encoding carried a short escape (a `Codec` over `Json.render` with a newline
  in a string) re-encodes to different bytes under the new rule, and `rehash` — which re-encodes
  through the one witness it is handed — cannot verify such a chain under any config. Migrating it
  needs the domain's OLD encoder for the verify leg and its new one for the rehash leg; no such
  two-witness rehash ships here, because no known store needs it. That is a stated boundary of this
  entry, recorded in the decision beside it, not an oversight.
- **A DAG node whose actor carries a control character** has a different content id now, and a
  content-addressed DAG has no rehash: its ids are its parent links. No known store holds one; a
  host that does re-appends the history under the new ids.
- **The shared chain corpus** (`chain/chain-corpus.json`, certified by the UI host and the
  TypeScript twin) gains a record with a control-character actor when its resident emitter — which
  lives in the UI host and folds THIS package's `canonicalConfig.Payload` at the version it pins —
  is raised to a release carrying this change. It cannot be emitted from here; it lands with the
  consumer's pin raise.

**Measured on this change:** two baselines moved against their committed copy, both `additive`
(`Fuaran.Core.OpStream`, 1 move; `Fuaran.Core.Conformance`, 14 moves, all the new module) — the
gate's since-tag line reads `17 baseline(s) read, 4 moved`, the other two being the slot's earlier
entries above (`Column` D72, `Idl.Codegen` Phase 230); the whole suite green
with the two `ParityVectors` `Json.render` vectors and one `Wire` test re-pinned to the `\u00xx`
bytes; the new `StringEscape` suite runs the vector family (over 350 checks, every control character
through every escaper, both chain configs and the DAG line) and shows it going red on a row spelling
`U+000A` as `\n`.

### Every hash pre-image is injective and every tree digest sees the tree's shape (Phase 290) — BREAKING, `digest-move`: memo keys, projection digests, `contentHash` / `encodeHash` digests, `Index` fingerprints and `canonicalCodes` all change value; `additive` on the public surface (`Tree.encodePreimage`)

**What changed.** Three hashing pre-images on the spine were not what their documentation claimed,
and each was a soundness hole in a function a consumer keys on.

1. **The tree digests see the shape.** `Tree.contentHash` and `Tree.encodeHash` folded the
   PREORDER alone. A preorder does not determine a tree: `root(a(a1,a2), b(b1))` and
   `root(a(a1,a2,b(b1)))` visit the same nodes in the same order, are one accepted `MoveNode(b, a)`
   apart, and hashed equal under every encoder — including one that carries ids. Both folds now
   write every preorder node as TWO fields, its label and its ARITY (`List.length (w.Children n)`),
   through `Hash.canonicalFields`; a preorder with arities is injective over ordered trees
   (`preorder_arity_injective`, `proofs/TreeOps.fst`, section 21). `Tree.encodePreimage` is the new
   public function exposing that unhashed string — the only public-surface move, classed `additive`
   by the gate — and `encodeHash` is its FNV-1a. `Tree.Index.fingerprintOf` (private; the
   `NodeIndex.Fingerprint` stamp) builds `id`, `kind`, child count, child ids through the same
   encoding where it joined on `>` and `,`, which an id can spell.
2. **The memo key is the pre-image, not a hash of it.** `Function.memoKey` was two 32-bit FNV-1a
   halves joined on a bare `U+0001`, and `applyMemo` served the cached tree on a key hit alone — so
   a value spelling the separator (`{a = "X\u0001b=vY"}` keyed as `{a = "X"; b = "Y"}`), two
   argument sets colliding under FNV-1a, or the two functions above each served the WRONG tree. The
   key is now `Hash.canonicalFields` over the function's `encodePreimage` and its address-sorted
   bindings (three fields each: address, case tag, payload — a slot payload is the slot subtree's
   `encodePreimage`), so a `Map` hit IS an equality of `(function, param-set)` under the caller's
   `encode`, with nothing to compare after it and no collision bound to record. The cost is the
   key's length: a function's whole encoding beside the result tree the entry already holds.
3. **Every key builds through one encoding, and a roster holds it.** `Projection.digestOf` and
   `Validator.canonicalCodes` joined on the bare separator too; both now go through
   `Hash.canonicalFields`. The doc comment above `canonicalFields` lists every key the spine mints
   that is not a chain hash, and the `Hash.Roster` family reads that list and holds it to `src/`
   both ways — every call site of `canonicalFields` is a listed definition, every listed definition
   calls it, and no definition joins on a bare `foldSep`, a `+ foldSep +` splice or the `U+0001`
   literal — so a new key cannot be minted without joining the list. `Hash.foldSep` itself is
   written `"\u0001"` rather than the raw byte, and the raw-byte hazard is noted beside `fieldEsc`.
4. **`Hash.utf8Bytes` is byte-for-byte the platform's answer on ill-formed input.** A high surrogate
   is a pair only when the next unit is in `DC00..DFFF`; a lone or ill-ordered surrogate encodes as
   `EF BF BD`, which is what `System.Text.Encoding.UTF8` emits. Until now the low half was consumed
   unchecked (`"\uD801\uD800"` produced U+10000's four bytes, a trivial second pre-image for
   `sha256Hex`) and a lone surrogate was written CESU-style; the platform-parity claim was tested
   over a well-formed corpus only. Seven ill-formed rows join `HashTests`' parity corpus (asserted
   against `Encoding.UTF8`) and `ParityVectors` (`utf8Bytes/ill-formed-*`, `sha256/ill-formed-lone-high`,
   pinned on both pipelines). What replacement does NOT buy is injectivity — the platform maps
   `"\uD800"`, `"\uDFFF"` and `"\uFFFD"` to one byte string — and `utf8Bytes` says so: it is the
   UNGUARDED, platform-parity path; the guarded form that REFUSES an ill-formed unit wherever a
   digest must name one string is Phase 306's, and nothing here pre-empts it.
5. **The witness's two identities are one relation.** `Conformance.witnessLaws` gains the law
   `Equals a b ⇔ ToString a = ToString b`, over every drawn pair AND over BUILT pairs — a drawn id
   against its own string, its case-flipped and its whitespace-padded forms read back through
   `OfString` — because the reference generator never draws two ids differing only in case, so a
   case-insensitive `Equals` over a case-preserving `ToString` was green in every family. It is
   refused by the kit now, by name, inside `certify`'s short-circuit, rather than downstream by the
   first duplicate id `InsertChild(p, node "A")` slips past beside an existing `"a"`. A witness whose
   `OfString` refuses a perturbation reports nothing for it (the law stays true over what the
   witness can construct). `witnessLaws` reports FIVE laws; `certify` twenty.
6. **`memoLaws` gains the separator-in-value case**, BUILT from the drawn param-set: the two-binding
   set is cached, and the one-binding set whose value spells the separator and the next binding
   must answer exactly what `apply` answers (a refusal — the hole is unbound) and must not be a hit.
   `memoLaws` reports FIVE laws. Both new laws' census rows are in `docs/conformance-families.*`.

**The proof leg.** `proofs/TreeOps.fst` section 21 (`preorder_arity_injective`, unconditional;
`preorder_alone_aliases`, the evaluated premise; `digest_fields_injective`, conditional on the label
and numeral renderers being injective, and named so in the ladder), `proofs/oracle/TreeOps.fs`
re-extracted, three ladder rows (`tree-digest-preimage-injective`, `tree-digest-fields-recover-shape`,
`tree-digest-differential`), and two `Proofs.Oracle` cases — the pre-image production folds equals
the extracted model's rendered shape at every generated state, and a MIS-NESTING op bridge (every
insert re-parented under the parent's deepest last descendant, which keeps the preorder) now loses
on the result hash and on no verdict; under the arity-free fold it agreed (measured). Dropping the
arity from the model refutes `shape_prefix` (measured).

**What adopting it costs.**

- **Every stored digest of these five kinds is stale.** A `MemoCache` built before this version
  never hits again (its keys are eight-hex halves; the new keys are pre-images) — rebuild it; a
  `NodeIndex` built before it reads as stale to `Index.isFreshFor` — rebuild it; a
  `ProjectionSnapshot` taken before it reports every line changed on the first diff — retake it;
  a persisted `contentHash` / `encodeHash` value (a bounded-escape region stamp, a domain's
  structural-equality record) no longer matches — re-derive it. A host twin that mirrors any of
  these folds re-implements the (label, arity) field encoding and re-certifies against the new
  vectors at its next pin raise.
- **`Validator.canonicalCodes` is a different string** — each code escaped and `U+0001`-terminated
  rather than `U+0001`-joined — so a host that persisted the projection re-derives it and a twin
  re-certifies. Two hosts still produce the same bytes for the same defect set.
- **A `MemoCache` holds longer keys.** Per entry, the function's whole encoding and its arguments'
  rather than sixteen hex characters. A consumer that sized a memo by entry count sizes it by
  key length too.
- **`Hash.utf8Bytes` / `sha256Hex` over an ILL-FORMED string give different bytes** — the platform's.
  Over a well-formed string nothing moves (the `hashSweep/*` digest is unchanged). A consumer that
  digested a lone surrogate before now gets the platform's digest for it, which is the one its
  server-side `SHA256` already computed.
- **A witness with two identities is refused.** A domain whose `Equals` is case-insensitive over a
  case-preserving `ToString`, or whose `ToString` is lossy, now fails `witnessLaws` and `certify`
  at the identities law. That domain was already unsound under `tryFind` / `wellFormed`; the kit
  now says so before the algebra does.
- **A `witnessLaws` / `memoLaws` / `certify` caller that counts laws** reads 5 / 5 / 20 where it
  read 4 / 4 / 19.

**Not done here, and named.** `Schema.fingerprint` (`Fuaran.Core.Column`) still joins `name:type`
cells on the raw `U+0001` byte: `Column` references only `Wire` and its own comment declines the
package edge to `Tree` that `canonicalFields` would need, and its bytes are pinned by the
`hashSweep/*` rows. The `Hash.Roster` family names it as the ONE known bare join, by file and count,
so a second one there still fails; the residue is the compute strand's to resolve. The guarded,
refusing `utf8Bytes` and the parser-side refusal are Phase 306's. The wire encoder's string escaping
is untouched (Phase 287 owns it).

### The kit gets a runner, splits by topic behind a facade, declares each family once, and gives `…With` one meaning (Phase 297) — BREAKING: `record-widening` of `Families.LawFamily`, six report types move to namespace level, and verdict changes; obsolete forwards for this draft

**What changed.** `Conformance.fs` — laid out by the order its parts arrived, not by topic — is a
public facade now, compiled last: one-line forwards with full signatures, plus `certify` and
`certifyStream`. The families live in eight internal topic modules ahead of it (`TreeLaws`,
`StreamLaws`, `IntegrityLaws`, `ConcurrencyLaws`, `SeamLaws`, `FunctionLaws`, `PropagationLaws`,
`SurfaceLaws`), with `ConfRng` and the witness records in files of their own. Every family, the
fold-confluence pack and the null-tolerance vectors among them, runs over one internal runner
(`LawKit`): one loop, one cursor, one first-counterexample cell, one result tail, where there were
sixty copies of each. Every seed replays to the same sample it drew before, and every law and
counterexample text is unchanged; the suite's pinned counterexamples are the evidence.

- **A law the run never asserted is not green.** Each law is a cell that counts its evidence. A law
  asserted only inside an arm the family's own adequacy guard counts reads through that guard (the
  guard is red, and says why, once); a law whose arm no guard counts is STRICT and reds itself as
  `never reached`, naming the remedy — widen the generator. `SampleAdequacy.cases` reads a
  never-reached law as starvation under either census class, so the census cell says `vacuous`
  rather than a number. `SampleAdequacy.neverReached` is the counterexample's opening.
- **Guards the census can see.** Every guard is labelled with its family's roster id
  (`sample adequacy (Conformance.<entry>): …`); seven were labelled with a bare name and one with
  its module name. `concurrencyLawsWith`'s coverage law is a guard now. `hashFnLaws` (its tamper
  arms), `attributedLaws` (the re-attribution), `captureReplayLaws` (the tampered capture) and
  `encoderInjectivityLaws` (distinct trees seen and compared — its old census reason was false)
  count their gated arms and are censused `Guarded`; `casLaws` counts its race arm and
  `attestationLaws` its op tamper. `snapshotLawsWith`'s snapshot arm, `dagLaws`' tamper and
  `idempotencyLaws`' gated arm are held by strict cells: several readers pin those families' result
  counts, and a strict cell is red at zero without a new result. The suite holds every census class
  to what the family emits at the reference witness — a `Guarded` family emits a roster-labelled
  guard, an `Unconditional` one emits none.
- **`genOp` draws every op kind.** A `Batch` of one to three structural ops, and an `UpdateNode`:
  the identity update of a drawn node, or a domain content edit through the new opt-in
  `UpdateGen<'Node>` record (compose it; `OpGen` does not grow). `opAlgebra`, `footprintLaws`,
  `mergeConflictLaws`, `reconcileLaws`, `concurrencyLaws` / `concurrencyLawsWith` and
  `arbitrationLaws` demand all six kinds — folded into each family's existing guard (its dimension
  reads "… and op kind"), not added as a result.
- **One record per family.** `Families.LawFamily` gains `Adequacy : AdequacyClass` and
  `Refusal : RefusalVerdict` (new: `Population` and `Why`). Each family row declares both;
  `SampleAdequacy.census` forwards to `Families.census`, and `Families.refusalAudit` is a
  projection (`censusOf` / `refusalAuditOf` project any list). The renderings read the record. The
  roster now compiles ahead of the guard module, so `LawResult`, `AdequacyDemand`, `CaseCount` and
  `AdequacyClass` moved, unchanged and under the same names, into `LawResult.fs`. The JSON export
  escapes through `Json.escape`.
- **`WireNullTolerance.laws ()`** answers the null-tolerance vectors as `LawResult`s, one per
  vector, so the family is rostered, censused (`Unconditional`: a fixed corpus) and measured.
  `check` and `run` are unchanged.
- **`verifyFunctionSymbolic` sizes in `int64`.** The space-size product saturates at
  `maxCases + 1` (four holes of 1,000 wrapped negative as an `int`, passed `<= maxCases` and were
  enumerated whole), `domainOf` sizes a range in `int64` (`IntRange(0, Int32.MaxValue)` read as
  empty and reported a false `DidNotApply`), the cartesian product is a lazy sequence, and a
  `Sampled` size too large for its `int` is reported `None`.
- **Smaller corrections.** The two `Option.get` sites in `capabilityLaws` and `queryLaws` fail a
  law instead of throwing. `memoSoundnessLaws` honours its `iterations`. An empty AI-surface pattern
  bank is checked whole rather than reported never reached. Stale comments (a fixed `certify` result
  count, a green no-attestation run, an LCG) are corrected, and so is the package description.

**The naming rule.** A bare entry name is the family at its default — the kit's own fixtures, or a
pinned parameter at its default value. `…With` means exactly one thing: **the same laws with a
pinned parameter injected, last before `seed`**. `…At` is **the domain-witness form** — the laws run
at a domain's own seam, witness or policy instead of the kit's fixtures. A family that chains the
DOMAIN'S ops under the domain's hash posture takes `hashFn`; a family whose chain is built over the
kit's own fixtures defaults it to `OpStream.defaultHash`. Brought under it on this draft:

| Was | Now | For this draft |
|---|---|---|
| `capabilityLawsWith` | `capabilityLawsAt` | obsolete forward, own roster id and guard label |
| `queryLawsWith` | `queryLawsAt` | obsolete forward, own roster id and guard label |
| `capabilityPipelineLawsWith` | `capabilityPipelineLawsAt` | obsolete forward, own roster id and guard label |
| `aiSurfaceLaws` (the domain's policy — the inverse of every other bare name) | `aiSurfaceLawsAt` | obsolete forward; the bare name is retired, not reassigned |
| `reconcileLaws` (hash hard-coded) | `reconcileLawsWith … hashFn seed iterations` | `reconcileLaws` pins `OpStream.defaultHash` and stays |

`propagationEvaluatorLawsWith` already conforms (its pinned `evalNodeWith` sits last before
`seed`), and `aiSurfaceLawsUnderKitPolicy` keeps its explicit name. **Not yet conforming:**
`snapshotLawsWith` (its `StreamConfig` is first), `concurrencyLawsWith` (its footprint projection is
first) and `FoldConfluence.laneFoldLawsWith` (its `hashFn` sits mid-signature). An obsolete forward
cannot carry an old parameter order under the name the rule assigns, so each is a reorder in place,
breaking without a forward; they move together in one later change rather than one at a time (Phase
330 made that change on this same draft — "The three remaining `…With` entries take the rule's
order" below). A
phase that adds a `…With` or `…At` family (Phase 249's `footprintLawsWith` is the next) follows the
rule from its first commit.

**Class.** The surface gate reads this as breaking on three counts, and the phase's own shard, which
called it additive, is wrong on all three:

- **`record-widening`** of `Families.LawFamily` (two fields). Every consumer that builds a
  `LawFamily` as a full record literal fails to compile (FS0764) until it supplies `Adequacy` and
  `Refusal` — the dataframe share of the roster, `DataFrameFamilies.roster` in the compute
  repository, is one. A consumer that only reads the roster is unaffected; `Families.Roster` keeps
  its `RefusalAudit` and `Census` fields, and this package fills them by projection.
- **Six report types move to namespace level** — `CompositionSample`, `VerifyDefect`,
  `VerifyCounterexample`, `VerifyCoverage`, `FunctionVerifyReport`, `MemoSample`, nested in
  `module Conformance` until now. Abbreviations in the facade keep every `Conformance.<Type>`
  annotation compiling, but an abbreviation does not carry union CASES: `Conformance.Exhaustive`,
  `Conformance.Sampled`, `Conformance.ValidatorRejected` and their siblings are spelled without the
  prefix now. The IL names move, so the change is binary-breaking too. The API baseline therefore
  moves for the split — the shard's "byte-identical across the split" was not available together
  with its own instruction to promote the types.
- **Verdict changes.** A family that certified nothing now says so: a strict cell reds a law no run
  reached, the newly counted families red a starved arm, the op-drawing families red a sample that
  missed an op kind, and `witnessLaws`' rebuild laws red a witness under which no node holds
  children (so `certify` stops at the witness laws there). The kit's reference witnesses reach every
  side. Law counts move for `hashFnLaws`, `attributedLaws`, `captureReplayLaws`,
  `encoderInjectivityLaws` and `casLaws` (one guard each); `concurrencyLawsWith` reports one subject
  law fewer and one guard more.
- **Additive beside them.** `capabilityLawsAt`, `queryLawsAt`, `capabilityPipelineLawsAt`,
  `aiSurfaceLawsAt`, `reconcileLawsWith`, `WireNullTolerance.laws`, `UpdateGen`,
  `Families.RefusalVerdict` / `census` / `censusOf` / `refusalAuditOf`, `SampleAdequacy.neverReached`.

`api/Fuaran.Core.Conformance.txt` and `docs/conformance-families.{md,json}` are regenerated (66
families: the five renamed-or-added entries and `WireNullTolerance.laws` join the sixty). No wire
surface moved; the law vectors under `conformance/laws/` draw exactly as before.

**What adopting it costs.**

- **A domain calling `capabilityLawsWith`, `queryLawsWith`, `capabilityPipelineLawsWith` or
  `aiSurfaceLaws`** gets a deprecation warning naming the new entry and nothing else on this draft;
  the forward runs the same laws under the old roster id. Rename the call before the forward goes.
- **A domain building a `LawFamily` literal** adds the two fields.
- **A domain matching `Conformance.Exhaustive` (or any case of the six moved unions)** drops the
  `Conformance.` prefix.
- **A domain whose generator never reached a side a law is about** turns red, deliberately. Widen
  the generator — draw an op the domain accepts and one it refuses, every op kind, two values that
  encode differently — rather than raising the iteration count or hunting a seed.

**Not done here.** The `LawResult` widening that would carry a passing guard's reached counts is a
recorded decision still open, and this phase does not take it. `ConformanceTests.fs` is not split
along the new seams in this change: sibling phases in the same batch append cases to it, and a split
would turn each of their appends into a cross-file conflict. A hash that answers one digest for
everything still leaves `hashFnLaws` green — measured, and its missing op-tamper arm is Phase 302's.

### Column trusts nothing it is handed, and the parser holds to the JSON grammar (Phase 299, DECISIONS.md "the parser holds to the JSON grammar, NaN sorts last, the column codec carries only a table it can decode, and `RowCodec` is obsoleted") — BREAKING: `union-widening` (`ColumnError`, `AggregateError`), a changed case payload (`ColumnError.NotJson`), a `removal` (`Cell.defaultFor`), refusals of input that was accepted, and a value move (`Schema.fingerprint`); `additive` beside them

**What changed.** The parser refuses a number token outside the JSON grammar (`01`, `1.`, `-.5`,
`1.e5` — `Json.isJsonNumber` states it) and a string holding a lone or ill-ordered surrogate
(`BadEscape`, raw or escaped), and reads integers through one invariant-culture reader
(`Json.readInt32`). The column codec and `Table.validate` refuse a cell outside its column's type, a
repeated name, non-canonical decimal, date and timestamp text and a non-finite float, and name a
ragged table; `decode` ends in `validate`; `aggregate` admits every cell first and orders NaN last.
`RowCodec` is `[<Obsolete>]`. No wire byte an encoder EMITS moves (`api/wire/` is unchanged); what
moves is what the readers accept, the `Schema.fingerprint` values, and the managed surface below.

**The break a Column consumer meets, one line each** (the compute repository's raise checklist; it
adopts `ColumnType.widens` in its `Typing.join` so it never builds a column `validate` refuses):

- `ColumnError.NotJson` carries the parser's `JsonError` (kind, message, position), not a string —
  a site that builds `NotJson m` from `Json.parse`'s string reads `Json.parseDetailed` instead.
- `ColumnError.RaggedColumns` is a new case (FS0025 at every exhaustive match); `Table.validate`
  returns it for ragged columns where it returned `LengthMismatch`, which now means one column's
  wire `values` and `validity` disagreeing, and nothing else.
- `AggregateError.CellOutsideType` is a new case (FS0025 at every exhaustive match — an evaluator's
  lift of `AggregateError` into its own envelope adds an arm).
- `Cell.defaultFor` is removed: it built `Date ""`, a cell no date column accepts. Nothing replaces it
  on the surface; the codec writes the same absent-slot bytes itself.
- `Table.validate` refuses a present cell whose type does not widen into its column's (`Bool` in an
  int column, `Float` in an int or decimal column, `Decimal` in a float column), a duplicate schema or
  column name, non-canonical decimal, date or timestamp text, and a non-finite `Float`.
- `ColumnCodec.tryEncode` is `validate` then `encode`: every refusal is `validate`'s, with its error.
- `ColumnCodec.decode` / `decodeJson` end in `validate`, refuse a repeated key in `columns`, validate
  date and timestamp text (`TemporalText`), and refuse an epoch outside the years `0000`–`9999`.
- A `DecimalType` column reads a whole-valued number token within 2^53 (`3000000000`, `3e9`), and
  still refuses a fractional one or one past 2^53.
- `Column.aggregate` refuses a cell outside its column's type (`CellOutsideType`) instead of
  truncating or dropping it, canonicalises decimal text (`Min`/`Max`/`First`/`Last` answer canonical
  text), orders NaN last and `-0` equal to `0` in `Min` / `Max` / `Median`, and names a decimal past the
  float range in `Mean` / `Median` / `StdDev` as `AggregateOverflow`.
- `DecimalText.tryToFloat` returns `None` past the float range instead of `∞`.
- `Schema.fingerprint` changes value for every schema: its pre-image is the canonical field encoding
  (`Hash.canonicalFields`'s) rather than a bare `U+0001` join. A recorded fingerprint mismatches once.
- A `ref` source decodes with or without a `schema`; a present one must still be well-formed.
- `ColumnCodec.errorString (NotJson _)` spells `not valid JSON:` once (it was prefixed twice).

**The break a `Wire` consumer meets:**

- `Json.parse` and every entry point over it refuse the tokens and strings above; a producer that
  emitted them is refused where it was read.
- Under a culture whose negative sign is not U+002D (fa-IR, he-IL), `-5` parses as `JInt -5`; it was
  `JFloat -5.0` there.
- `Versioning.Profile.tryParse` refuses white space around the version integers (the invariant reader
  takes a sign and digits only); what it still accepts that `render` never emits is Phase 306's.
- `RowCodec` is obsolete: FS0044 at every use, a BUILD ERROR in a consumer that treats warnings as
  errors. Suppress FS0044 at the call sites or move to a row codec of the consumer's own; it is removed
  at the first breaking draft after the UI tier hosts one (DECISIONS.md, same entry).
- `Corpus.fuzzRoundTrip` draws a wider alphabet (every control character, every surrogate class) and
  checks `Canon.render` too: a caller's seed-pinned expectation of its result can move.

**Additive:** `Decode.Fault`, `Decode.describe`, `Decode.tryProp`, `Decode.propWith`,
`Decode.stringWith`, `Decode.arrayWith` (the combinators over a caller's error type); `JVal.kindName`,
`JVal.nonFiniteToken`, `Json.firstNonFinite`, `Json.isJsonNumber`, `Json.readInt32`; the
`TemporalText` module (`isCanonicalDate`, `isCanonicalTimestamp`). `api/Fuaran.Core.Column.txt` and
`api/Fuaran.Core.Wire.txt` are regenerated; `api/wire/` does not move.

**Vectors.** `conformance/refusals/` is a new self-enumerated family (`--emit-refusals`): the grammar,
surrogate, cell-type, canonical-text, duplicate-name and ragged-table refusals beside the acceptances
they bound, every host `proposed`; the shared corpus takes its copy at the hosts' next pin raise.
`ParityVectors` gains `aggregate/nan-order`, and every `hashSweep/*` row's fourth value moves with the
fingerprint — a Fable consumer re-runs its parity leg against this draft.

**Not done here.** The extracted parser model still reads the pre-299 grammar; the parser
differential carves out exactly these refusals until Phase 306 restates `proofs/JsonParse.fst`.

### Totality against the machine in Wire and Column (Phase 306, DECISIONS.md D84) — BREAKING: refusals of input that was accepted (`Versioning.Profile.tryParse`, `Json.tryRender`, `Canon.tryRender`, `Json.readInt32`), a refusal where an infinity was answered (float `Sum`), and value moves at the edge of the float range (`Mean`, `Median`, `StdDev`); `additive` beside them

**What changed.** The four renderers are iterative and render a value of any nesting depth (they
killed the process at a depth of about 1,400 on a 1 MB stack); their bytes are unchanged. A string
that is not well-formed UTF-16 is refused by the guarded renderers and by a new guarded encoder and
digest. The float aggregates no longer answer an infinity over finite input, and `aggregate` folds
in one pass. The profile grammar is a bijection on its canonical strings, and a profile counter no
longer wraps negative. No wire byte an encoder EMITS moves (`api/wire/` is unchanged).

**The break a `Wire` consumer meets, one line each:**

- `Versioning.Profile.tryParse` refuses what `Profile.render` never writes: a leading zero
  (`core@01.0`), a sign (`core@+1.0`, `core@-0.0`), anything after the digits (a NUL, a space), and a
  name that is not an ASCII letter followed by ASCII letters, digits, `.`, `_` or `-`. A
  `requiredProfile` that is not canonical now reads as absent.
- `Canon.tryRender` and `Json.tryRender` refuse a value holding a string or a member key with an
  unpaired surrogate, naming the first by path (a member's key before its value). A non-finite float
  is still looked for first and its message is unchanged. Over a value with neither they are exactly
  `Ok (render v)`.
- `Json.readInt32` refuses a token that is not an optional sign and digits — `"7\u0000"` read as `7`.
- `Versioning.bump` SATURATES at `Int32.MaxValue` (the profile comes back unchanged) where the
  counter wrapped negative; `Versioning.tryBump` is the refusing form.
- `Corpus.fuzzRoundTrip` is unchanged in what it checks; its well-formedness test is now
  `Json.firstIllFormedString`.

**The break a `Column` consumer meets (the compute repository's raise checklist):**

- A float `Sum` whose running total leaves the float range over FINITE input is
  `AggregateOverflow` (`<column>: sum overflowed the float range`); it answered `±∞`. A column that
  itself holds a NaN or an infinity still answers what IEEE arithmetic gives.
- `Mean`, `Median` and `StdDev` over finite input answer a finite value where an intermediate
  overflowed — `Median [1e308; 1e308]` is `1e308`, not `∞`. Every answer that was finite before is
  the same value to the bit; the suite compares the two folds over a generated pool.
- `StdDev` is documented as the POPULATION form (it divides by `n`). Its values did not move.
- `Column.aggregate`'s refusals keep their precedence (a cell outside its column's type, then a
  non-numeric column, then the first decimal past the float range); it no longer builds three
  intermediate lists.
- `ColumnCodec.decodeJson` documents surplus members as must-ignore. Nothing it accepted is refused.

**Additive:** `Json.firstIllFormedUnit`, `Json.isWellFormedUtf16`, `Json.firstIllFormedString`;
`Versioning.Profile.isValidName`, `isValid`, `tryRender`; `Versioning.tryBump`, `Versioning.tryRender`;
in `Fuaran.Core.Tree`, the `IllFormedUtf16` record and `Hash.firstIllFormedUnit`,
`Hash.tryUtf8Bytes`, `Hash.trySha256Hex`. `api/Fuaran.Core.Wire.txt` and `api/Fuaran.Core.Tree.txt`
are regenerated; `api/Fuaran.Core.Column.txt` and `api/wire/` do not move.

**Not changed, and said so:** `Hash.utf8Bytes` and `Hash.sha256Hex` still answer the platform's
replacement bytes over an ill-formed string — the unguarded, platform-parity path, which has second
pre-images there by construction. `Json.render (JFloat -0.0)` is still `-0`, the one divergence from
`Canon.render` that a parse collapses. `Hash.fnv1a`'s unit is, as it always was, the UTF-16 code unit.

**Vectors.** `ParityVectors` gains the Phase 306 rows (`fnv1a/astral-code-units`, `tryUtf8Bytes/*`,
`canonTryRender/*`, `jsonTryRender/*`, `render/depth-10000-*`, five `aggregate/*` rows and six
`profile/*` rows), appended; its `utf8Bytes/ill-formed-*` rows are built from code units now, with the
same expected bytes (an F# string literal's unpaired `\u` surrogate is compiled as U+FFFD, so on .NET
those rows had not been measuring an ill-formed unit). `conformance/refusals/` gains a `profile` codec
— the grammar's refusals beside its acceptances — and one `column` acceptance for surplus members;
every host is still `proposed`. A Fable consumer re-runs its parity leg against this draft.

**Proofs.** `proofs/Utf8.fst` (new, checked and not extracted) proves the UTF-8 encoding injective on
well-formed UTF-16; `proofs/WireCanon.fst` carries canonical injectivity to the bytes a digest is
taken over, models `Canon.renderOrdered` and proves it injective outright, and restates the guard
over both refusals; `proofs/JsonParse.fst` is restated to Phase 299's grammar and surrogate refusal —
the differential's carve-out is deleted — and proves every string the parser returns well-formed and
the int53 guard EXACT on the grammar; `proofs/WireVersioning.fst` restates the bump over the
refusal; `proofs/WireColumn.fst` (new) is the columnar codec's first model, and
`Fuaran.Core.Column`'s coverage exclusion is gone. The ladder rows are in `proofs.json`.

### The lane DAG's reconcile applies shared history once and refuses a rejecting lane set the same way under every arrival order; `append` / `merge` refuse a splice-bearing parent id (Phase 300, DECISIONS.md "the reconcile partitions the region above its base") — BREAKING: `Dag.reconcileMany`'s signature and error type change, scripts change value where they were wrong, halts become folds, and `append` / `merge` raise where they accepted

**What changed.**

- **The delta rule.** `Dag.reconcile` and `Dag.reconcileMany` took each lane's delta as
  `between base head` and concatenated them, so history two heads SHARE above the base was applied
  twice: a fast-forward (`headB` descending from `headA`) returned `[Inc 7; Inc 7; Inc 1]`, the same
  head named twice replayed its delta twice, and a criss-cross reconciled over `mergeBase`'s
  tie-break (one of two maximal common ancestors) replayed the other branch twice. Under a real
  footprint the same shapes HALTED, the shared ops "conflicting" with themselves. The region above
  the base is now partitioned by node id: the SHARED region (nodes two or more heads' closures hold)
  is applied once, first, in the drain order of the union; each head's EXCLUSIVE delta is its
  closure minus the base's minus every other head's; heads are deduplicated (first occurrence kept).
  Conflicts are checked between the exclusive deltas. Where the heads share nothing above the base —
  two lanes forked off it, which is what `FoldConfluence.foldOnce` builds — the script is exactly
  the one the old rule produced.
- **`Dag.reconcileMany` tests that every lane applies before it folds.** It now takes the witness
  and the base state — `reconcileMany w footprintOf dag baseId baseState heads` — and returns
  `Result<'Op list, ReconcileFault<'Op, 'Rej>>`: `LanesInterfere` (the old `MergeConflict list`),
  `SharedHistoryRejected` (the shared region does not replay from the base), or `LanesRejected`
  (every lane that does not apply ON ITS OWN from the state the shared region reaches, sorted by
  head id, each a `LaneRejection` carrying the head, its delta, the rejecting node and the
  rejection). A lane set with a rejecting lane used to fold under the arrival order that happened
  to run another lane first and reject under the other; it is refused identically under every order
  now. `Dag.reconcile` keeps its signature.
- **`FoldConfluence`.** `foldOnce` renders `LanesRejected` through the new public
  `canonicalRejectionReport` (one line per distinct rejecting lane, in the domain's encoding, sorted);
  the pack's header, which claimed a non-folding lane set refused identically when it did not, is
  corrected. `laneFoldLaws` judges every drawn lane set in three shapes — disjoint (as before), the
  first head named twice, and the second lane chained onto the first — and its adequacy line counts
  the two shared-history shapes beside the outcomes (`duplicate-head=`, `fast-forward=`); the
  `rejected=` count covers all three shapes, so it is three times what one draw per iteration gave.
- **`Conformance.reconcileLaws`** builds four shapes (disjoint, fast-forward, duplicate head,
  criss-cross over `mergeBase`) with each lane under its own actor, and gains a law — "reconcile
  applies shared history once": a clean shared-history script replays to `Dag.replayTo` of a merge
  node over the two heads — and a guard on the shape. Its result list grows from 6 to 8, and the
  sample it draws moves (the criss-cross draws a lane off each merge).
- **`Dag.append` / `Dag.merge` refuse a splice-bearing parent id.** A comma-bearing parent id
  spliced into the content-hash pre-image — an `append` naming the parent `"x,y"` minted the id of
  `merge(x, y)` and replaced it — and `merge("", x)` built a node with a phantom parent. The typed
  refusal is `DagAppendFault` (`EmptyParentId`, `CommaInParentId`) through the new `tryAppend` /
  `tryMerge`; the plain forms raise `ArgumentException` carrying `DagAppendFault.toString`. `""`
  stays `append`'s genesis marker. No DAG production could build and still accepts changes a node id.
- **`Dag.mergeBase`** is documented as "a maximal common ancestor" — a policy, not "the" base. No
  behaviour change; the reconcile no longer leans on the choice.

**Who is affected, and the migration.**

- A caller of `reconcileMany`: pass the witness and the state at the base, and match
  `ReconcileFault.LanesInterfere cs` where it matched `Error cs`. A caller that wants the partition
  and the interference sweep without the lanes-apply test passes a witness whose `Apply` accepts
  every op (the proof differential in `ProofOracleTests.fs` does exactly that).
- A caller of either reconciler whose heads share history above the base: the script is shorter,
  and correct; a lane set that halted on shared history now folds.
- A caller that builds parent ids carrying commas, or merges with `""`: catch the
  `ArgumentException`, or call `tryAppend` / `tryMerge`. Phase 296, later on this draft, turns
  `append` / `merge` themselves into `Result`-returning functions and adds its own refusals.
- A domain running `laneFoldLaws` or `reconcileLaws`: the adequacy lines and counts move as above.

**Class.** `breaking-source` (the `reconcileMany` signature and error type; the refusals on
`append` / `merge`) and behaviour (script values, halts into folds, rejections now order-free).
Wire: none — the JSONL bytes and every node id production could build before are unchanged.
Public surface: `ReconcileFault`, `LaneRejection`, `DagAppendFault`, `Dag.tryAppend`,
`Dag.tryMerge`, `FoldConfluence.canonicalRejectionReport` added; `Dag.reconcileMany` retyped. The
`api/` baselines are regenerated.

**Proofs.** `DagFold.fold_confluence_total` — fold confluence with the diamond as the only
hypothesis, because `fold_once` now tests every lane before folding — retires the `lanes-apply`
assumed row; `fold_confluence` is its corollary. Section 15 models the partition clause for clause:
`reconcile_sound` (each node at most once, exactly the region above the base, for any closures) and
`reconcile_fold_order_free` (permuting the heads leaves the shared region identical and the checked
fold outcome-equivalent). The oracle is re-extracted.

### The keyed witness reaches the apply path (Phase 286, DECISIONS.md "uniqueness over keyed positions is Core's refusal") — BREAKING: `record-widening` of `KeyedWitness` and a field removed, `union-widening` of `Rejection`; the rest `additive`

**What changed.** A domain that holds nodes in keyed, non-structural positions declares them once, and
Core's walks and refusals see them.

- **`KeyedWitness` moves from `Fuaran.Core.Conformance` to `Fuaran.Core.Tree`**, same namespace
  (`Fuaran.Core`), so source that references both assemblies — every kit consumer — compiles unchanged at
  the type name. It gains `KeyedChildren: 'Node -> 'Node list` (the nodes, not only their ids) and
  `ReplaceKeyedChildren: 'Node -> 'Node list -> 'Node` (arity-preserving), and **loses
  `HasKeyedChildren`**, which is derived now (`Tree.keyedIds nodew keyw n`). Every construction breaks:
  replace `HasKeyedChildren = f` with `KeyedChildren` returning the nodes, and add `ReplaceKeyedChildren`
  (`fun n _ -> n` for a domain with no keyed position).
- **`Rejection` gains `KeyedPosition of target * holder`**, declared last (every existing tag kept). Only
  the keyed engine raises it; an exhaustive `match` over `Rejection` must add the case.
- **Additive:** `Tree.traversal`, `Tree.preorderKeyed`, `Tree.idsKeyed`, `Tree.foldKeyed`,
  `Tree.wellFormedKeyed`, `Tree.graftWellFormedKeyed`, `Tree.keyedIds`, `Tree.firstRepeatedId`;
  `Ops.applyContainedKeyed` and `Ops.canApplyContainedKeyed`; `Conformance.keyedApplyLaws`.

**What `applyContainedKeyed` does.** It locates through `Tree.traversal` (a node's keyed children, then
its structural ones), so a node held in or below a keyed position can be an insert's or reorder's parent,
a move's destination and an update's target; `UnknownNode` enumerates the keyed walk. Its `DuplicateId`
refusal is `Tree.graftWellFormedKeyed`, seeing keyed positions on both sides of an insert, and an
`UpdateNode` payload's keyed subtrees are checked against the tree its target keeps. It edits through
`Children` alone: a remove or move of a node held directly in a keyed position is `KeyedPosition`. Graft
containment (`NotAContainer`) walks keyed subtrees too. For a domain whose `KeyedChildren` is
`fun _ -> []` every answer is `applyContained`'s — `keyedApplyLaws` runs both over every op kind the kit
draws. `applyContained`, `apply` and the sequence forms are unchanged; there is no keyed sequence form —
a caller threads `applyContainedKeyed`.

**What a consumer does.** A domain with keyed positions supplies its `KeyedWitness` to
`applyContainedKeyed` in production (not only to the kit), deletes any apply-path pre-check that re-walks
keyed positions, and reaches keyed nodes through `Tree.traversal nodew keyw` instead of its own walkers.
Run `Conformance.keyedApplyLaws` at your witness: its agreement law fails when `KeyedChildren` misses a
position your own id check walks.

**The ladder.** `keyed-apply-preserves-wf` is proved (`proofs/Preservation.fst` section 12, the insert
clause; the keyed `UpdateNode` check is tested, not modelled). `witness-surface-scope` stays a
domain obligation and is discharged by `Conformance.keyedApplyLaws` now — what the domain still owes is
the declaration's accuracy — and `Conformance.keyedChildrenLaws` discharges nothing; the generated family
census and the `api/` baselines for `Fuaran.Core.Tree`, `Fuaran.Core.Ops` and `Fuaran.Core.Conformance`
are regenerated.

### The determinism axis is a SET of factors, joined by union (Phase 319, DECISIONS.md D82) — BREAKING, `retype` + `removal`; the `determinism` wire vocabulary WIDENS

**What changed.** `DeterminismSource` was a four-case chain — `Deterministic < Clock < Random <
Network` — joined by maximum, so `join Clock Random` was `Random` (the clock factor dropped) and a
`Network` declaration `covers` a clock read without naming it. It is now an ALIAS for
`Set<DeterminismFactor>`, where `DeterminismFactor` is the closed union `ClockFactor | RandomFactor |
NetworkFactor`: `Deterministic` is the empty set, `Effect.join` is union on the determinism axis,
`Effect.covers` is superset, and a capture journal can say what a body read.

| Was | Is |
|---|---|
| `Deterministic` | `Effect.deterministic` (`Set.empty`) |
| `Clock` / `Random` / `Network` | `Effect.clock` / `Effect.random` / `Effect.network` (one-member sets) |
| `d = Deterministic` | `Set.isEmpty d` |
| a `match` on the four cases | set membership, `Set.contains ClockFactor d` |
| `max` of two classes | `Set.union` |
| `declared >= actual` | `Set.isSubset actual declared` |

The chain is the projection of the set by maximum rank (empty → `Deterministic`, otherwise the member
of highest rank); the reverse loses information, which is the argument for the set and the reading
for anyone holding the old shape.

**The label.** `Effect.determinismTag` renders the set canonically: `"deterministic"` for the empty
set, otherwise the member factors in the fixed order clock, random, network joined by `+` —
`"clock"`, `"random"`, `"network"` (unchanged), `"clock+random"`, `"clock+network"`,
`"random+network"`, `"clock+random+network"`. `Effect.tryDeterminismOfTag` (new) inverts it on
exactly the canonical labels; a reordered, repeated, empty or unknown member is `None`, so a set has
one wire spelling. The capability and query codecs decode through it and refuse anything else with
the message they always used (`unknown determinism: …` / `unknown determinism source: …`).

**Wire.** The four single-token labels (`deterministic`, `clock`, `random`, `network`) are byte-identical, so a document emitted for a single-factor
class does not move. The vocabulary WIDENS: a composed or multi-factor class now emits a `+` label
where the chain emitted its maximum, and a strict decoder that accepts only the four old tokens
refuses them. `api/wire/Fuaran.Core.Function.txt` and `api/wire/Fuaran.Core.Query.txt` are
regenerated and classed `breaking`. The wire-surface exemplar builder draws a set of union cases
three ways — one member per case, empty, and two members — so those baselines pin the three
singleton labels, the empty set's `deterministic` (`… / Set<DeterminismFactor>.empty`) and the
two-factor `clock+random` (`… / Set<DeterminismFactor>.two`); every multi-factor label is pinned by
`conformance/laws/capability-laws.json` (below) and by `FunctionTests`.

**Journals.** No capture journal is invalidated. `replayEffect` consumes by effect identity and does
not compare the label, and `verifyCaptures` hashes the label as recorded. A journal recorded under a
composed class that the chain had joined to its maximum keeps its maximum label and replays; new
captures of such a class carry the richer one.

**Laws and corpus.** `Conformance.capabilityLaws` gains two laws, BUILT every iteration: the effect a
capture records covers every factor the body exercised (the journaled label decodes back to exactly
the declared set), and its converse — a body that reads a factor outside the recorded class is not
covered and the difference names that factor. Each iteration now declares a non-empty set drawn by
index, so a run reaches all seven labels with no further draw from the cursor.
`verifyHonestyLaws` ranges over the eight sets rather than the four chain values. The capability law
vectors (`conformance/laws/capability-laws.json`) are RE-EMITTED: the twelve iterations carry all
seven non-empty labels in their declarations and `determinismTag` rows, where every row was `random`;
the file's description states the new sample.

**Proofs.** `Capability.fst` and `Query.fst` move with the type: the set is modelled as its
characteristic vector over the closed three-factor alphabet, `join` is the pointwise union and
`covers` the pointwise subset, and the canonical label is proved a bijection with the eight sets
(`det_tag_roundtrip`, `det_tag_canonical`, `det_tag_injective`). The effect law and the audit law
(`effect_law`, `audit_effect_join`) hold unchanged over the new lattice, and two new theorems state
the capture seam: `capture_records_declared` and `capture_covers_exercised`. The committed oracle
extractions are regenerated and the differential host compares the label, its inverse, `join` and
`covers` over every set and pair.

**What adopting it costs.**

- **A consumer that names `Deterministic` / `Clock` / `Random` / `Network`** as values or patterns
  stops compiling (`removal`); the table above is the whole migration. A match over the old union
  is an `FS0025`-class failure by construction; there is no silent miscompile.
- **A consumer that constructs `EffectClass` or `Capability`** re-types the `Determinism` field
  (`retype`).
- **A host that mirrors the vocabulary as a closed enum** — a generated wire chain in a UI tier, a
  name-mapping function in an orchestration layer — moves at its next raise: it must accept the `+`
  label or refuse composed classes by name. Nothing on the Core side reaches those hosts before
  they raise their pin.
- **A consumer that wrote `covers Network …` to mean "any non-deterministic read"** must name the
  factors it accepts; the declaration is now exactly the factors it lists.

**Rollback.** None: the chain is the set's projection and the set cannot be reconstructed from it.

### One JSONL scanner that refuses; the DAG's typed refusals; the snapshot family with its mode on the snapshot (Phase 296, DECISIONS.md "one JSONL scanner, shared rather than copied") — BREAKING: `Dag.append` / `Dag.merge` change return type, `Snapshot` gains a field (`record-widening`), four unions become qualified-access, and input that was accepted is refused; the rest `additive`, with obsolete forwards for this draft

The shard classed this change additive; it is not, and the class above is the honest one. Each
breaking item is listed with what a consumer does about it.

**What changed — the scanner.**

- **One scanner.** `OpStream.Jsonl` is the one JSONL line scanner in the repository, public. The DAG
  package carried a verbatim copy (and a copy of the actor decoder) under a comment citing D2; it
  reads through `OpStream.Jsonl` now. The five per-reader record loops collapse onto
  `Jsonl.scanRecords`. Public: `topFields` / `rawSpan` (raw member spans, byte-for-byte — the opaque
  canonical payload a consumer embeds keeps its bytes), `unquote` (total), `parseLine`, the typed
  accessors `rawField` / `tryRawField` / `stringField` / `intField` / `stringsField` / `actorField`,
  `refuse`, `lineNumber` / `lineText`, and `scanRecords`. New types `JsonlFault`,
  `JsonlFaultReason`, `JsonlLine` (`additive`).
- **It refuses what it used to misread** — BREAKING for a stream that carried such a line. Every
  reader (`fromJsonl`, `fromJsonlWithSnapshots`, `fromJsonlLegacyActor`, `fromJsonlVerified`,
  `captureFromJsonl`, `Attributed.decodeEnvelope`, `snapshotFromJsonlResult`, `Dag.fromJsonl`) now
  refuses: an unquoted value where a string is required (`"prevHash":null` read as `"ul"`, `"id":12`
  as `""`); a `\u` escape without four hex digits, an unknown escape letter, an unpaired surrogate; a
  line that ends inside its object (after `:` it used to index past the end, with a message that
  differed by host); a line that is not an object; trailing content after the closing `}`; a bare
  value that is not `true` / `false` / `null` / a JSON number; an integer member outside the JSON
  grammar or the 32-bit range (`"seq":0x2`, `"seq":"1"`, `"seq":1.0`, `"turn":01`); a DAG `parents`
  member that is not an array of strings (`["a" "b"]` used to read as two parents); an actor object
  missing a member its kind requires. **A store that reads `Ok` is one the scanner parsed.** Sweep a
  persisted store by reading it once: every line the old scanner misread is now named.
- **The error text.** Each refusal renders `line N: <reason> (position P)` — `N` is **1-based over
  every line of the text, blank lines counted** (it was 0-based over the non-blank lines), `P` the
  scanner's own 0-based position in that line. A consumer matching `line 0` matches `line 1`. A
  single-object reader (`snapshotFromJsonlResult`, `decodeEnvelope`) renders `<reason> (position P)`.
- **Snapshot lines.** One snapshot line is admitted, and only as the first line of the stream; one
  after a record, or a second one, is refused (`JsonlFaultReason.SnapshotNotAtHead`) — they were
  accepted anywhere and in any number. A record line carrying a `snapshot` member is read as the
  record it is (it was dropped as a snapshot). `compact` writes exactly the admitted shape.

**What changed — the DAG.**

- **`Dag.append` / `Dag.merge` return `Result<string * Dag.T<'Op>, DagAppendFault>`** — BREAKING,
  `retype`. Phase 300's `tryAppend` / `tryMerge` were the same refusals typed beside raising plain
  forms; they are folded back into the plain names (removed: they were added on this draft and never
  released). A call site that built a DAG it knows to be sound unwraps the `Ok`.
  `DagAppendFault` gains `UnknownParent` (a non-empty parent the DAG does not hold — it used to
  build a node `verifyDag` then rejected) and `ContentIdCollision` (an id the DAG holds for a node
  whose parents modulo order, actor or encoded op differ — it used to REPLACE the held node
  silently, and under the 32-bit FNV-1a default a collision is about 0.3% likely at 5,000 nodes and
  even odds near 77,000). The same node appended twice deduplicates, by design. `Dag.fromJsonl`
  applies the same rule to a repeated id. `union-widening` on a union this draft added.
- **`Dag.appendChecked` / `Dag.mergeChecked`** apply the op at a state the caller holds and refuse
  an op the domain rejects before it enters the DAG (`DagAppendRejection<'Rej>`: `Fault` | `Domain`)
  — `additive`. The plain forms do not apply the op; see the DECISIONS entry for why.
- **`Dag.ReplayFault.UnknownHead`**; `tryReplayTo` refuses a head the DAG does not hold (it replayed
  to the initial state). `Dag.replayTo` is `[<Obsolete>]` for this draft and delegates: a domain
  rejection is returned as before, an unknown head or a cyclic history RAISES
  (`ArgumentException`) where it answered `Ok` with the initial state or a silent partial fold.
- **`[<RequireQualifiedAccess>]` on `ChainBreakReason`, `DagBreakReason`, `MergeConflictShape` and
  `Dag.ReplayFault`** (D1's rule) — BREAKING, source only: write `ChainBreakReason.HashMismatch`,
  `Dag.ReplayFault.CyclicHistory`. `ChainBreakReason.Unrecognised` and `DagBreakReason.Unrecognised`
  no longer shadow one another in `namespace Fuaran.Core`. The wire strings are unchanged.

**What changed — the linear stream.**

- **`Snapshot<'State>` gains `Mode: SnapshotMode`** (`Strict` | `ChainOnly`) — BREAKING,
  `record-widening`: a full-literal construction must name the mode. **`OpStream.Snapshots`** is
  the one family — `take`, `compact`, `firstBreak` (localising: `SnapshotBreak.SnapshotHash` or
  `SnapshotBreak.Tail` of a `ChainBreak`), `verify`, `replayFrom`, `toJsonl`, `ofJsonl` — taking the
  mode and the `StreamConfig`, reading the mode from the snapshot after `take`, and returning the
  typed `SnapshotFault<'Rej>` (`SeqOutOfRange`, `PrefixRejected` with the domain's rejection,
  `TailSeqMismatch`, `TailRejected`) the string forms discarded. `Snapshots.replayFrom` refuses a tail
  whose first `Seq` is not the snapshot's boundary. The pre-image bytes, the line bytes and every
  hash are unchanged. The seventeen-member matrix (`snapshotAt*`, `compact*`, `verifyAcross*`,
  `replayFrom`, `snapshotToJsonl*`, `snapshotStateHashedFromJsonl`, `snapshotFromJsonl*`) stays as
  `[<Obsolete>]` forwards for this draft, each pinning the mode its name says and answering exactly
  as before — `verifyAcrossWithOpt` is public now (it was internal). Their `Error` text keeps the
  `OpStream.snapshotAt:` prefix whichever member reports, because the proof model of compaction
  pins those strings; the typed fault is the corrected surface.
- **One chain walker** serves `firstChainBreakWith`, `tryRehash`, `Snapshots.firstBreak` and
  `firstCaptureBreakWith`, where four were written. **`tryRehash`** returns the `ChainBreak` `rehash`
  discarded; `rehash`'s message now names the record and the reason.
- **The configured genesis reaches every seed**: `headWith`, `appendIfWith`, `captureEffectWith`,
  `firstCaptureBreakWith` read `cfg.Genesis` (the canonical forms keep `""`) — `additive`.
- **`appendMany` / `appendManyWith`** chain a batch of ops in one walk and one copy, all or nothing
  (`Error(index, rejection)`) — `additive`. `append` and `appendIf` walk the stream once where they
  walked three and four times; `appendIdempotent*` index the new record without walking again;
  `Attributed.groupBy` (behind `byActor` / `bySession`) is linear where it copied each group per
  record.

**Measured (a 5,000-record loop, Release, median of five, the same chain bytes in every arm).** The
pre-296 append body re-run verbatim: 185–280 ms. The shipped `append` in the same loop: 296 ms when
run first, ~600 ms when run after the other arms — the per-call cost is the copy `@` makes and the
garbage it leaves, and the walks saved are inside the noise. `appendMany` over the same 5,000 ops:
12.3 ms, **about 5% of the loop**. The shard asked for `append` to be O(1) per call behind the
unchanged `OpRecord list`; that is not reachable — an immutable list reaches its end only by walking
it, and a copy is the price of putting a record there — so the batch form is the remedy, and a caller
appending in a loop should collect and call `appendMany`.

**Rollback.** Pin `0.32.0`. Nothing persisted moves: every hash, pre-image and line byte is the
same, and a store `0.33.0` refuses is one `0.32.0` was misreading.

### The light set: the small exports consumers copied (Phase 315, DECISIONS.md "the light set") — `additive`, with three BREAKING items beside it: a case payload (`Rejection.WouldNestUnderSelf`, breaking-source), a type that moves package (`RejectionGuidance`, AiSurface → Ops, source-compatible), and refusals of input that was accepted (an empty actor id on read; `ColumnValidator.unique` reading decimals by value)

**Additive.** `OpStream.sha256Hash` (the named SHA-256 `HashFn`, byte-for-byte `Hash.sha256Hex (prev +
"|" + payload)`); `Hash.fnv1a32` (the raw value `fnv1a` renders); `OpStream.chainHashOf` and
`OpStream.appendChainOnly` (the record hash, and an append with no state and no apply); the
`Footprint` builders `empty` / `union` / `contentEdit` / `insertUnder` / `removeNode` / `moveTo`;
`Rejection.code` / `Rejection.explain` with `RejectionNouns`, and `RejectionCodec.encode` / `render` in
AiSurface; `NestRelation`; `FloatLayout` public (`finite`, `roundTrip`); `Cell.token` / `Cell.compare`;
`Validator.Pack` / `PackCheck` / `PackFinding` / `citation` / `runPack`; `ActorInvalid` with
`Actor.validate` / `Actor.human` / `Actor.agent`. The guard classes `Column`, `Tree`, `Validator` and
`Wire` as `additive`. No emitted byte moves: the chain hash, the apply vectors, the law vectors and
every committed parity row are unchanged, and the parity table gains `sha256Hash/*`, `fnv1a32/*`,
`floatLayout/*`, `cellToken/*` and `cellCompare/*` rows, appended.

**BREAKING — `Rejection.WouldNestUnderSelf of target * relation: NestRelation`** (the guard's `retype`
on `Ops`). The case carried the target alone, so a move under itself and a move into its own subtree
were one refusal; the relation is now a field. A pattern `WouldNestUnderSelf id` no longer compiles:
write `WouldNestUnderSelf(id, _)`, or match the relation. `WouldNestUnderSelf _` compiles unchanged, and
the class every host and vector names is unchanged.

**BREAKING (binary only) — `RejectionGuidance` is declared in `Fuaran.Core.Ops`** (the guard's
`removal` on `AiSurface`). Same namespace, same fields: a source that names it compiles unchanged; an
assembly compiled against the old location rebuilds.

**BREAKING (behaviour) — an empty actor id is refused on read.** `OpStream.fromJsonl` (and the readers
built on it, `Dag.fromJsonl` included) refuse an actor whose `id` is empty with
`JsonlFaultReason.ActorInvalid(member, ActorInvalid.EmptyId)` — the guard's `union-widening` on
`OpStream`, the new reason being declared last. A store holding such a record is no longer read; the
legacy bare-string reader (`fromJsonlLegacyActor`) is unchanged. The `Human` / `Agent` cases still
construct anything — validate at the boundary with `Actor.validate` / `Actor.human` / `Actor.agent`.

**BREAKING (behaviour) — `ColumnValidator.unique` keys on `Cell.token`.** Its private token wrote a
`Decimal` as its raw text; `Cell.token` writes the canonical text, so two cells holding `1.5` and `1.50`
are now one key value and the second is a `COL-UNIQUE` defect. No other cell's key moves. A table that
passed `Table.validate` holds only canonical decimals and sees no difference. `CountDistinct` now reads
the same token; its counts do not move (its ±∞ spelling changed, which no count can see).

**Not moved.** `Query.invocationKey` keeps its own cell encoding — it is injective on cells, which
`Cell.token` deliberately is not (DECISIONS.md "the light set"). No `appendAll` is added: Phase 296's
`appendMany` is that function.

### The remaining algebra symmetries: `Schema.patch`, propagation pull, an index carried through an edit (Phase 317, DECISIONS.md "the algebra's remaining symmetries are built") — BREAKING: `record-widening` of `SchemaDelta`, and a value move (`Tree.Index` stamps); the rest `additive`

The shard classed this change additive; one record gained a field and one stamp changed its
definition, so the class above is the honest one. Each item says what a consumer does about it.

- **`SchemaDelta` gains `Order : string list`** — BREAKING, `record-widening`: a full-literal
  construction of the record no longer compiles (FS0764); add `Order = []`, or start from
  `Schema.identityDelta` with `{ … with … }`. `Order` is the target's column order, EMPTY exactly when
  the target's order is the one `Schema.patch` derives without it (the surviving columns in their old
  order, then the added ones in listed order), so an append-only change and `diff a a` record nothing.
  `Reordered` keeps its meaning and is implied by `Order`. A delta `diff` returned before this draft
  has no `Order`; one persisted by a consumer and read back through `SchemaDeltaCodec` must carry the
  member.
- **`Schema.patch : Schema -> SchemaDelta -> Result<Schema, SchemaError>`** — `additive`. The transform
  `diff` reports: `patch old (diff old target) = Ok target` for every pair of schemas naming no column
  twice, reorders and additions placed anywhere included; `diff a a = Schema.identityDelta`. It refuses
  by name (`SchemaError`: `DuplicateColumn`, `AbsentColumn`, `TypeDisagrees`, `AlreadyPresent`,
  `OrderMismatch` — a new union, `additive`) and never repairs. **The compute repository adopts it at
  its raise**: a `SchemaChanged` delta replays through `patch` instead of forcing a full refresh.
- **`SchemaDeltaCodec`** (`encodeJson` / `encode` / `decodeJson` / `decode` / `codec`) — `additive`.
  One object, five required members (`added`, `removed` as `{name, type}`; `retyped` as
  `{name, from, to}`; `reordered`; `order`), rendered under `Canon`; decode reports in the columnar
  envelope `ColumnError`. A new wire shape, not a change to one.
- **`Propagation.neededFor` / `evalFor` / `evalForWith`** — `additive`. The pull dual of
  `dirtyFromChangedIds`: the targets and everything upstream of them, and demand-driven evaluation of
  that set and nothing else, agreeing with `eval` / `evalWith` on it wherever those succeed
  (`eval_for_agrees`, `eval_for_with_agrees` and `needed_for_least`, `proofs/Propagation.fst`). A pull
  can succeed where the full evaluation fails, at a node outside the needed set — that is the point of
  it. `dirtyFromChangedIds`' answers are unchanged; it now shares its frontier loop with `neededFor`.
- **`Ops.Index.afterOp` and `Tree.Index.rebind`** — `additive`. `afterOp w idw op post ix` is the index
  of `post` carried through `op` instead of rebuilt, for every op kind, a `Batch` included; it equals
  `Tree.Index.build w idw post` (the law over the op generator, and at every step of a carried edit
  session), and falls back to that rebuild whenever `post` disagrees with what the op predicts. Its
  cost follows the op, not the tree. `rebind` is the primitive beneath it.
- **Every `Tree.Index` stamp (`NodeIndex.Fingerprint`) changes value** — BREAKING for a consumer that
  persisted one: the stamp is now the sum, modulo 2^32, of one FNV-1a term per node rather than one
  digest over the preorder, so an edit can re-stamp in the nodes it touched. A stamp persisted before
  this draft reads stale once (`isFreshFor` is false) and the index is rebuilt, which is the safe
  direction; nothing reads it as a content id. What it detects is unchanged: every skeleton edit, a
  kind change and an id-remap, not an opaque payload edit.
- **Five omissions are now recorded as deliberate** (DECISIONS.md): no `uncurry` on `Function`, no
  seventh skeleton op, `normalize` is not a canonical form, no ordinal on `InsertChild` / `MoveNode`,
  no `fresh` on the id witness. Nothing moves.

**Public surface:** the baselines `api/Fuaran.Core.Column.txt`, `api/Fuaran.Core.Propagation.txt`,
`api/Fuaran.Core.Ops.txt` and `api/Fuaran.Core.Tree.txt` move, regenerated through the approval mode;
`Column` reads `record-widening`, the other three `additive`.

### The DAG's checked append gains a verifying variant (Phase 329, DECISIONS.md "the verified append is the call-site check D83 left to the caller") — ADDITIVE on the public surface; `dagLaws` gains two laws, and a stream generator whose DAG nodes never differ in state now reds the family as never reached

**What changed.**

- **`Dag.appendVerified` / `Dag.mergeVerified`** (`'State : equality`) and **`Dag.appendVerifiedWith`
  / `Dag.mergeVerifiedWith`** (the caller's comparison, first) — `additive`. Each takes the checked
  form's arguments with the initial state `state0` after the witness, replays the named parent's
  ancestor closure from it (the genesis parent `""` replays to `state0`; a merge replays the union of
  both parents' closures, without the merge op, in the order `tryReplayTo` drains the merge node's
  closure), and refuses a handed-in state that differs. Refusals in order: the graph refusals, then a
  parent whose replay fails, then the mismatch; otherwise the result is exactly `appendChecked`'s /
  `mergeChecked`'s. One replay per call, linear in the parent's closure — a choice for tests and debug
  builds, never the production path.
- **`Dag.VerifiedAppendRejection<'State, 'Rej>`** — a NEW union (`Checked of DagAppendRejection<'Rej>`
  | `ParentReplay of Dag.ReplayFault<'Rej>` | `StateMismatch of handed * replayed`), `additive`. No
  existing union gains a case.
- **`Dag.tryReplayTo`** is now the shared union replay at one root. Same fold, same order, same faults;
  its tests are unchanged and green.
- **`Conformance.dagLaws`** gains "appendVerified / mergeVerified answer as the checked forms at the
  parent's replayed state" and "appendVerified / mergeVerified refuse another node's state with
  StateMismatch": nine results where there were seven. The new draws come after every earlier arm's,
  so a recorded seed reproduces the sample the seven laws saw. The family's signature is unchanged and
  `api/Fuaran.Core.Conformance.txt` did not move; the census row reads `900` cases where it read `700`
  (`docs/conformance-families.{md,json}` regenerated).

**Class.** One baseline moved, `api/Fuaran.Core.OpStream.Dag.txt`, by additions only: the four
functions and the union's members. No wire surface moved. The slot is untagged and already breaking,
so this rides it.

**What adopting it costs.** Nothing for a caller of the checked forms. A domain certifying `dagLaws`
reads two more results; a generator whose drawn ops never make two of the family's nodes differ in
state never reaches the refusal law, and that strict cell then reports "never reached" — the same
verdict a constant generator already gets from the tamper law. A caller that wants the check swaps
`appendChecked hashFn w actor op state parentId dag` for `appendVerified hashFn w state0 actor op state
parentId dag` and matches `VerifiedAppendRejection.Checked` where it matched the checked refusal.

**Shown failing first.** With the comparison in the verifiers' shared step skipped (a perturbation of
the tree, reverted), `dagLaws` at the reference counter went red on exactly the refusal law, at seed 99
iteration 0: an append onto a fork head holding its sibling's state was admitted. The seven earlier
laws and the agreement law stayed green. `DagTests.fs` pins the three mis-pairings, the genesis
parent, a parent whose replay is refused (a rejecting node, a cyclic history), the graph refusals'
precedence, and that a `…With` comparison which always agrees verifies nothing.

**Rollback.** Pin `0.32.0`, or stop calling the verified forms. Nothing persisted moves.

### The conflict-shape renderer distinguishes the three shapes again (`Fuaran.Core.Conformance`, DECISIONS.md D88) — a behaviour correction on the draft

**A behaviour correction in the conformance kit.** `FoldConfluence.canonicalConflictReport` rendered
every conflict on this draft as `concurrent-update`: its shape renderer matched the three
`MergeConflictShape` cases unqualified, and once Phase 296 made the union `[<RequireQualifiedAccess>]`
the first arm became a variable pattern that caught every shape. Two reports differing only in shape
compared equal in the canonical rendering, and conflicts at one address over one op pair collapsed to
one line. Each shape again renders as its own name (`concurrent-update`, `insert-position-clash`,
`move-vs-remove`). The defect never reached a tag — `v0.32.0` renders the three shapes apart — so a
consumer moving from `0.32.0` sees no change; only a rendering taken from an untagged draft build
moves. Nothing on the wire or in a chain moves, and no public surface moves. FS0049 and FS0026 are now
build errors repository-wide (`Directory.Build.props`), so a pattern of this kind cannot ship again.

**Rollback.** None needed: the corrected rendering is `0.32.0`'s.

### Decimal vectors and the cut gate: the parity table carries `DecimalText`, the law set carries decimal documents, and the cell-ranging families reach the decimal (Phase 276) — ADDITIVE on every surface; BREAKING as a verdict change for two kit families (`aggregateNullSkipLaws` is now `Guarded`, `columnarValidatorLaws` gains a guard); no public surface moves

**What changed.**

- **`ParityVectors.vectors` gains 94 rows**, appended after every earlier row so each keeps its place
  in the comparison: `decimal/canonical/*` (every refused form K4 names, one row each, and every
  normalisation K3 performs), `decimal/compare/*` (sign, place, equal spellings, and two pairs one
  double cannot tell apart), `decimal/add/*` (carries through the point, a widening carry, a
  narrowing borrow, cancellation to an unsigned zero, mixed signs in both orders, a scale and a
  magnitude past any host decimal), `decimal/toFloat/*`, `decimalCodec/*` (the canonical encode, the
  refusal of a non-canonical cell, a canonicalising decode, integer and whole-exponent tokens, the
  refusals of a fractional token, of a whole token past 2^53 and of text) and `decimalAggregate/*`
  (`Sum`, `Min`, `Max`, `Mean`, `Median`, `StdDev`, `CountDistinct`, the past-float refusal, text that
  is not decimal). `additive`: a consumer's runner compares more lines. Their .NET bytes are committed
  in `ParityVectorTests`, and the in-range sums and orders are held to `System.Decimal`.
- **`conformance/laws/decimal-laws.json`** — a second family in the emitted law set, `decimal`:
  authored inputs over every behaviour D72 pins, each `expected` computed by the kit, refusals among
  them, stamped with `kitVersion`. Declared in `version-derives.json` and `copies.json` beside
  `capability-laws.json`; its copy in the shared corpus is written by the same `--emit-laws` command.
  `docs/conformance-corpus.md` names the family and what a host must reproduce. `additive`.
- **`Conformance.aggregateNullSkipLaws`** draws its column type from int, float AND decimal (it drew
  int and float), builds decimal cells through `Cell.decimal`, and emits a guard over the present cell
  type it reached. Its census class moves from `Unconditional` to `Guarded ["int cell"; "float
  cell"; "decimal cell"]` (`docs/conformance-families.{md,json}` regenerated).
- **`Conformance.columnarValidatorLaws`** carries its ranged column as a decimal column on odd
  iterations (each drawn int `n` read as `n.25`, no extra draw, so an even iteration's table is the one
  it always was), injects `100.01` as the decimal stratum's out-of-range value, counts out-of-range
  decimals EXACTLY by their digits, and emits a guard over `int cell` and `decimal cell`. Its census
  class gains those two dimensions.

**Class.** No `api/*.txt` baseline moved and no wire surface moved: the new rows, the new file and the
new guards are values and law results, not members. The two family changes are **verdict changes for
a certifying family**, and that is the breaking part. `aggregateNullSkipLaws` emitted one result and
now emits two; a run that never draws a decimal column — any run of one iteration, and a seed whose
draws all fall on int or float — is now RED on its guard where it was green, and the census renders a
`Guarded` row where it rendered an `Unconditional` one. `columnarValidatorLaws` emits five results
where it emitted four, and a run with no present decimal cell (one iteration, at least) is red on the
new guard. Both families draw a different sample from the same seed. The slot is untagged and already
breaking, so this rides it.

**What adopting it costs.** A consumer that runs either family at the kit's reference size (200
iterations) sees two more passing results and nothing else. A consumer that runs either at a handful
of iterations, or asserts a result count, re-reads the guard and the count. A consumer running the
cross-pipeline table compares 94 more lines; a host that mirrors the decimal certifies against
`laws/decimal-laws.json`.

**Shown failing first.** With the two guards added and the generators not yet widened, the suite went
red on both at the reference seed: `column type reached int cell=238 float cell=282 decimal cell=0 —
never reached decimal cell` (`aggregateNullSkipLaws`, seed 4242, 200 iterations) and `column type
reached int cell=635 decimal cell=0 — never reached decimal cell` (`columnarValidatorLaws`). The suite
keeps the go-red: a one-iteration run of each is red on its column-type guard alone, naming the cell
type it missed.

**The cut gate.** The run is in this slot's release record above: packed and run, and blocked at the
host's membership check before its compile and value legs — the record names the host's list entry
that blocks it. Nothing in it named a Core defect.

**Rollback.** Pin `0.32.0`. Nothing persisted moves.

### The three remaining `…With` entries take the rule's order, and the six conformance-kit witness records join the freeze (Phase 330, DECISIONS.md "the kit's last three `…With` entries are reordered with no forward, and the six kit witness records are frozen") — BREAKING (`removal` of the old parameter order); the freeze is a promise, no surface moves for it

**What changed.**

- **The pinned parameter sits last before `seed` in the three entries Phase 297 left out of the
  naming rule.** Each is reordered in place, in its topic module and in its `Conformance` facade
  forward. No forward keeps the old order — none can, under the same name — so a call in the old
  order no longer compiles. Each pinned parameter's type differs from every parameter it changed
  places with, so the compiler names every such call; none can compile in the wrong order.

  | Entry | Was | Now |
  |---|---|---|
  | `Conformance.snapshotLawsWith` | `cfg sw gen stateEncode hashFn seed iterations` | `sw gen stateEncode hashFn cfg seed iterations` |
  | `Conformance.concurrencyLawsWith` | `footprintOf nodew idw gen encode seed iterations` | `nodew idw gen encode footprintOf seed iterations` |
  | `FoldConfluence.laneFoldLawsWith` | `w footprintOf hashFn hashState gen laneCount seed iterations` | `w footprintOf hashState gen laneCount hashFn seed iterations` |

  The law text, the draw order, the roster ids and the guard labels are unchanged, so every
  recorded seed reproduces its sample and the census rows of the three families do not move. The
  bare forms (`snapshotLaws`, `concurrencyLaws`, `laneFoldLaws`) were never out of order and do not
  move.
- **`CapabilitySeamWitness`, `QuerySeamWitness`, `CapabilityPipelineWitness`, `ConstructWitness`,
  `KeyedWitness` and `EvaluatorWitness` are frozen** with their fields as they stand, in declaration
  order, appended to `Conformance.frozenWitnessFields` after the six core records.
  `Conformance.unfrozenWitnesses` is now EMPTY; it stays, and the coverage law still reads it, so a
  witness added before `1.0` is still classified by the commit that adds it. `witnessSurfaceLaws ()`
  returns thirteen results where it returned seven: twelve field laws and the coverage law. The
  freeze section above lists the twelve records.

**Class.** The surface gate classes the three moved signatures `retype` (`api/Fuaran.Core.Conformance.txt`
regenerated): the old parameter order is REMOVED, with nothing in its place, so it is read here as a
removal. The draft is untagged and already breaking, so it rides it. The freeze moves no member: the
six records keep every field, and `unfrozenWitnesses` keeps its type. What it changes is a promise —
a field added to any of the six is now a red gate naming the record, and from `1.0` it is not
available at all — and one verdict count: a consumer that asserts `witnessSurfaceLaws ()` returns
seven results, or that reads a record out of `unfrozenWitnesses`, moves.
`docs/conformance-families.{md,json}` are regenerated for that count.

**Raise checklist — the test call sites a consumer meets at this raise.** Every one is a test or a
census, not product code: the three entries are conformance families, run by a suite.

- A dataframe consumer's conformance tests: any hand-written call of the three entries moves its
  pinned argument to the last place before the seed.
- A consumer's op-stream DAG law tests: `laneFoldLawsWith` with a host hash takes `hashFn` after
  `laneCount`.
- A consumer's persistence law tests: `snapshotLawsWith` with a legacy chain format takes its
  `StreamConfig` after `hashFn`.
- A consumer's own Core law tests that inject a footprint (the confluence teeth checks):
  `concurrencyLawsWith` takes `footprintOf` after `encode`.
- A consumer's conformance census that runs each roster entry by a hand-written call moves the same
  lines; one that reads `Families` rows only is unaffected. A census that runs `witnessSurfaceLaws`
  counts thirteen results.

**Shown failing first.** With the six records added to the pin list and two of `KeyedWitness`'s
fields swapped in its pin (`KeyedChildren` and `ReplaceKeyedChildren`), the suite went red by the
record's name: `KeyedWitness declares [Surface; KeyedChildren; ReplaceKeyedChildren;
PlaceKeyedChild; IdsUnique] where the freeze pins [Surface; ReplaceKeyedChildren; KeyedChildren;
PlaceKeyedChild; IdsUnique] — added: none; removed: none (the same fields, reordered)`. The suite
keeps the go-red: a decoy `KeyedWitness` with those two fields swapped fails its pin, and the real
record passes it.

**Rollback.** Pin `0.32.0`. Nothing persisted moves.

## 0.32.0 — released 2026-09-26 as `v0.32.0`

**It is a MINOR release because the change that opened it is BREAKING.** `0.31.0` is tagged, so it is a
consumer's contract and nothing rides it. Phase 250 adds a case to the closed `SkeletonOp` union,
which the surface gate classes `union-widening`, and a breaking change opens a minor slot rather
than a patch one. Every other member this phase adds is classed `additive` by the gate and rides
the slot beside it. Phase 246 adds two more breaking moves, a `retype` of `columnarOpLawsWith` and
a verdict change on `aiSurfaceLaws`, and both ride the slot for the same reason. Phase 257 moves the dataframe families and the dataframe half of the C# facade into two new
packages, which the gate classes `removal` for `Fuaran.Core.Conformance` and `Fuaran.Core.CSharp`;
both ride the slot too. `<Version>` and the laws corpus here (`conformance/laws/*.json`) were re-stamped
in the same commit as the version move, per `docs/conformance-corpus.md`; the byte copy in the
shared wire-format corpus is re-synced separately.

**Release record.** Phases 250, 246, 256 and 257 ship in it. The cut-time Fable gate ran green against the
candidate on 2026-09-26 — `fuaran-dotnet`'s `tests/core-fable/core-fable.ps1 -CoreVersion 0.32.0` from the
local candidate feed: 22 packages on the surface (18 referenced, 4 excused), the compile leg and 166/166
parity vectors byte-identical on both pipelines. Two package ids are new in this release,
`Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp` (Phase 257).

The phase's source is a downstream spreadsheet-shaped consumer's measurement (Phase 250). It built
a sheet over `Column.Ops`, `DataFrame.Incremental` and `Propagation`, and found five places where
the three strands met only through glue it kept by hand. Each entry below closes one of them.

### `UpdateNode` — the in-place skeleton op (Phase 250) — BREAKING, `union-widening`

**What changed.** `SkeletonOp<'Node, 'Id>` gains a sixth case, declared last so every existing tag
keeps its number:

```fsharp
| UpdateNode of node: 'Node
```

It rewrites one node in place. The node whose id is `w.Id node` takes the payload's content and
KEEPS the children it already has; the payload's own children are not read. The target is the
payload's id rather than a second field. A two-field `UpdateNode(id, node)` could carry an id that
disagrees with its payload, and Core has no honest refusal for that. The single field makes the
mismatch unrepresentable. This is the argument `Ops.fs` already records for removing the ordinal:
an id stated twice can disagree, and an id stated once cannot.

- `Ops.apply` / `canApply`: an absent target is `UnknownNode`, enumerating the tree. The root may
  be rewritten. No id set moves, so there is no duplicate-id clause.
- `Ops.applyContained` / `canApplyContained`: when the node holds children and the REWRITTEN node
  cannot hold them, the refusal is `NotAContainer(target, newKindTag)`. It names the new kind,
  because that is the kind the predicate refused.
- `Ops.invert`: `UpdateNode` of the pre-state node. The undo restores the content and keeps the
  children the tree holds when it runs.
- `Ops.footprint`: `Reads {id}`, `ContentWrites {id}`, `UnknownParentWrites {id}`. The unknown-parent
  write is REQUIRED, not cautious. An update of `x` and a concurrent `RemoveNode` of an ancestor of
  `x` share no address the script can name, and they do not commute: the update lands in one order
  and is refused in the other. It costs the same pinned over-approximation remove and move pay: an
  update is independent only of a structure-free script, so two updates of different nodes are
  reported dependent. See "Op-script footprint + independence".
- `Propagation.touchedBy`: the node alone. A redefinition used to be
  `Batch [RemoveNode id; InsertChild(parent, node')]`. That moved the node to the end of its parent
  and dirtied the parent too; the consumer measured 2.82 nodes dirtied per redefinition where 1.00
  moved. Now a redefinition dirties the node and its readers.
- The apply-vector family (`conformance/apply/skeleton-apply.json`) gains four `updateNode` vectors:
  three accepts and an unknown-target refusal. The family is `proposed` for every other host, and
  none of them carries an in-place update op yet, so the refusal vector names no host code.
- The proof model carries the case. `TreeOps.op` gained `UpdateNode` with its apply and footprint
  clauses, so `Skeleton.skeleton_fold_confluence` is about the shipped alphabet and not a
  sub-alphabet of it. The diamond closes by `relocating_forces_inert`, the same elimination as a
  remove or a move (`update_is_relocating`). `Preservation` proves the rest: the rejection
  characterisation, `apply_preserves_wf`, `invert_applicable` and `contained_preserves` all gained
  an update clause.

**What adopting it costs a consumer with an exhaustive `match` on `SkeletonOp`.** Every such match
gains one arm, or it becomes incomplete: FS0025, a warning, or an error under warnings-as-errors.
Against a stale same-version pack there is no compile signal at all, only an
`InvalidCastException` at run time, which is why this advances the slot. The arm is usually one line:

- an op encoder or fingerprint: encode the payload node;
- a domain `touchedBy`-shaped walk: the node alone;
- a footprint of the domain's own: follow `Ops.footprint` — the unknown-parent write is what makes
  it sound.

A consumer that only CONSTRUCTS ops and hands them to `Ops.*` needs nothing.

### `Propagation.evalWith` / `evalFromWith` — the prior value, inside the contract (Phase 250) — additive

**What changed.** Two drivers whose evaluator is `resolve -> prior -> id -> Result`. A recomputed
node is handed `Map.tryFind id prior`, its own value from the evaluation that produced `prior`.
`evalWith` hands every node `None`. The refusals are `evalFrom`'s, unchanged: an unknown changed
id is `EvalUnknownChange`, and an undeclared read is `EvalUndeclaredRead`. `eval` and `evalFrom`
are now the shared walk over an evaluator that ignores its prior. That is a proved identity
(`eval_is_walk_with`), and neither moved.

**The agreement theorem, restated** (`evalfromwith_agrees`, `proofs/Propagation.fst`). It holds
under `evalFrom`'s premises for the evaluator's prior-blind reading, plus one more: **the
evaluator's answer does not depend on the prior it is handed**, at every node the walk recomputes,
under the resolver the walk hands it there. The prior is a hint for reusing work, never an input to
the answer. It is the `propagation-prior-blind` row of `proofs.json`, a domain obligation.

**`Conformance.propagationEvaluatorLawsWith`** — additive — samples that obligation at a domain's
own evaluator. It runs `propagationEvaluatorLaws` over the domain's reference evaluator, then three
laws about the prior-aware one: its prior-blind reading is the reference, the prior discipline, and
agreement with the prior. Its adequacy guard counts a recomputed node handed a prior, and a clean
node reused from one.

**What a consumer does.** Nothing, unless it keeps per-node reuse state beside the driver. That is
the consumer's case: one `IncrementalEval` per table node, kept in step by hand. Such state moves
into the node's value. Give the value an equality over what it means, not over the cache, and
certify the evaluator with the new family.

### `Propagation.changedForOp` — the post-edit change set for a structural op (Phase 250) — additive

`dirtyFromOp` over the PRE-edit tree, restricted to the ids the post-edit tree holds. The pre-edit
graph is what still reaches a removed node's dependents; dropping the removed ids is what
`evalFrom` over the post-edit map accepts. The one-line glue the consumer wrote for it took a
failing test to find. Restricting `touchedBy` to the survivors instead is accepted and leaves the
dependents stale.

**Declined, with the reason:** "`evalFrom` reports rather than refuses an id absent from the graph."
`evalFrom` still refuses. That refusal is a proved clause (`evalfrom_unknown_refused`) and a
certified law arm (`propagationEvalLaws`' unknown-change arm, which `SampleAdequacy` guards). It is
what catches a domain's mistyped change set. `changedForOp` never names an absent id, so the report
half has no remaining case, and a report field would be a second closed-record break. DECISIONS.md
has the ruling.

**A law verdict moves with it, in the permissive direction.** `propagationEvaluatorLaws`'
change-set-honesty law no longer holds a node the edit REMOVED from the dependency map to being
named. Nothing evaluates it after the edit, and `changedForOp` leaves it out. Its readers are still
held. A domain that was RED only because its change set omitted a removed id is now GREEN; no green
verdict turns red.

### Column-granular reads — `Propagation.PartRead`, `partDependencyMap`, `nodeDependencies`, `dirtyFromChangedParts`; `ColumnOps.changedColumns` (Phase 250) — additive

A read may name the parts of the read node's value it depends on (`Parts = None` is the whole
value). `dirtyFromChangedParts` narrows the FIRST hop: a reader of a changed node is dirty only
when its declared parts meet the parts that moved. From the second hop on every reader is dirty,
because which parts of a recomputed value move is not known until it is recomputed. The
changed-parts function is a PARAMETER, and Propagation names no column vocabulary.
`ColumnOps.changedColumns`, read off `changeOf`, is the columnar adapter. It is sound under
part-faithful reads, which is the domain's promise. The proved driver still recomputes
node-granularly; this set is what an edit can reach at part granularity, for scheduling, reporting
and measurement.

### `ColumnOps.deltaOf` — a cell edit reaches `Incremental` as one row (Phase 250) — additive

`deltaOf : RowIdentity<'Id> -> Table -> ColumnOp -> TableDelta` builds the row delta from the op
and the BEFORE table. It does not diff before against after. A cell edit is its one row
(`RowChanged`), or the old key removed and the new one added when it wrote the key. An append is
its new rows, a column edit is the rows whose cell moved, and a schema or whole-table op is
`FullRefresh`. `FullRefresh` is also the answer wherever identity is missing or the op does not
apply. On the consumer's sheet, a one-cell edit of a 1,000-row source re-evaluated one row of the
row-local node. `changeOf`'s column invalidation re-evaluated every row of it. The docs'
"What it costs on the clock" section carries the measured numbers and where full evaluation wins.

### Witness-taking law families — the seam, AI-surface and columnar families certify the domain (Phase 246)

The source is downstream consumers' measurements (Phase 246). The four seam families took a seed
and certified this repository's own fixtures. A host that ran the body before the registry refused
passed every one of them while a domain-side law went red. `aiSurfaceLaws` took the domain's witness
and swapped in the kit's `Decide`, so a policy that allowed every write passed it. The columnar pair
ran on the kit's fixtures with an empty witness list. Each fixture-bound family stays as it is, for
the kit's own census. Each gains a form that takes the domain. The suite reproduces both planted
defects (`WitnessTakingFamiliesTests`, `AiSurfaceTests`). Each witness-taking family goes RED on its
defect, and the fixture-bound family beside it stays green on the same domain.

#### `capabilityLawsWith`, `queryLawsWith`, `capabilityPipelineLawsWith` and their witnesses — additive

Three composing witness records, per "compose, never grow" above:

- `CapabilitySeamWitness<'v>`: `Registry`, `Body` (handed the call's arguments, then the
  capability), `Dispatch` (the host's path; `Registry.dispatch registry` for a host that delegates
  to Core) and `GenCall`.
- `QuerySeamWitness`: `Queries`, `Resolver`, `Dispatch` (`QueryRegistry.dispatch queries`) and
  `GenQuery`.
- `CapabilityPipelineWitness`: `PipelineRegistry` and `GenPipeline`.

`capabilityLawsWith` and `queryLawsWith` send every drawn call through the witness's `Dispatch`
with its body counted. Three laws hold there. **Three outcomes**: `Ok(Failed _)` never escapes, and
`BodyFailed` / `ExecutionFailed` carries the body's own failure. **A refusal precedes the body**: a
typed refusal ran no body, and a dispatched call ran it exactly once. **The host is the registry's**:
a call reaches the body iff it is registered and its arguments validate, and a refused call carries
the registry's own error. Each is `Guarded [ "settled"; "pending"; "refused" ]`, so a generator that
misses one of the three outcomes is starved.

`capabilityPipelineLawsWith` checks every drawn pipeline. It must type-check against the domain's
registry, round-trip the wire, and give each node its own invocation key. Its default-deny arms are
BUILT per `Invoke` node: that node naming an unregistered capability, and that node binding an
undeclared argument, must each be refused by name. It is `Guarded [ "invoke node" ]`. `deferredLaws`
has no witness-taking form, because it is over the envelope alone.

#### `aiSurfaceLaws` runs the domain's `Decide` — a VERDICT change, BREAKING whatever the surface gate says

**What changed.** Each drawn op is submitted as the actor `"author"` through the witness's own
`Decide`. The proposal arm that runs is the one that policy chose. The guard gains a dimension,
`Guarded [ "accepted"; "refused"; "allowed"; "parked"; "denied" ]`. A policy that never parks or
never denies anything the generator draws is starved, and RED. An allow-all policy is exactly that.
The family's signature did not move, and the surface gate prints nothing for it. By the reading
`0.31.0` applied to `certify` (Phase 220), a verdict change on a certifying family is breaking whatever
its member's surface class. It rides this draft because the draft is already breaking (`UpdateNode`
above).

**The escape hatch: `aiSurfaceLawsUnderKitPolicy`** (additive). This is `aiSurfaceLaws` as it stood
at `0.31.0`: the kit rolls `Allow`, `NeedsApproval` or `Deny` per draw. It certifies the proposal
plumbing for any policy and says nothing about the domain's. It is named for what it does, and it is
never the default.

**What adopting it costs.** A domain whose reference policy allows everything, or never parks, turns
red. Give the reference run a policy that reaches all three decisions over the ops its generator
draws. Where only the plumbing is in question, run `aiSurfaceLawsUnderKitPolicy` beside it; one line
changes. `proofs/coverage-exclusions.json`'s entry for the family says the same.

#### `columnarOpLawsWith` takes the domain's generator — BREAKING for that member, `retype`

**What changed.** The Phase 181 teeth seam gains the domain's `StreamGen<ColumnOp, Table>`. That is
`concurrencyLawsWith`'s shape: the injectable seam and the domain's witness in one entry point
(DECISIONS D67).

```fsharp
// 0.31.0
Conformance.columnarOpLawsWith (invertUnderTest: ColumnOp -> Table -> Result<ColumnOp, ColumnRejection>) (seed: int) (iterations: int)
// 0.32.0
Conformance.columnarOpLawsWith (invertUnderTest: ColumnOp -> Table -> Result<ColumnOp, ColumnRejection>) (gen: StreamGen<ColumnOp, Table>) (seed: int) (iterations: int)
```

The surface gate classes it `retype`. It rides this draft because the draft is already breaking.
`columnarOpLaws` keeps its signature and its sample exactly.

**Migration, one line.** A caller that ran the kit's sample with its own `invert` now passes the
kit's reference generator:
`Conformance.columnarOpLawsWith invert Conformance.columnarOpStreamGen seed iterations`.
`columnarOpStreamGen` (additive) is a `StreamGen` over `columnarOpLaws`' fixture table and reaches
every population the laws read. It is not that family's own sample, which reads the evolving table
and so cannot be a `StreamGen`. A domain passes `ColumnOps.invert` and its own generator.

#### `incrementalLawsWith` — additive

`incrementalLawsWith (pipelines: Transform list list) (gen: StreamGen<ColumnOp, Table>)` evolves
the domain's table by the domain's ops. For each op the table accepts, it certifies that
`DataFrame.evalFrom` over `ColumnOps.changeOf op` equals a full `evalPipeline` at one of the domain's
pipelines. `evalFrom` answers every change but a value edit by evaluating in full, so the guard is
`Guarded [ "value edit" ]`.

#### The roster and the counts

Every new family has a roster record naming its witness (`CapabilitySeamWitness`,
`QuerySeamWitness`, `CapabilityPipelineWitness`, or `StreamGen` for the columnar pair). Each also
has a refusal-audit row and a census class. None discharges a `proofs.json` obligation; each
certifies rather than answering for an open assumption. The generated
`docs/conformance-families.md` shows each family's cases at this repository's reference witness,
through the Phase 196 `cases` column. The pass-path counts Phase 245 proposes are not part of this
entry.

### The compute boundary, prepared inside Core — two new packages, `Fuaran.Core.DataFrame.Conformance` and `Fuaran.Core.DataFrame.CSharp` (Phase 257)

**What changed.** No spine package references `Fuaran.Core.DataFrame` or `Fuaran.Core.Column.Ops`
any more (DECISIONS.md D66, D68). Two packages crossed that line, and each is now split in two:

- **`Fuaran.Core.DataFrame.Conformance`** (new, Fable-clean) carries every law family that reads
  the dataframe layer. They are `transformLaws`, `aggregateParityLaws` (its `GroupBy` parity
  half), `columnarOpLaws`, `columnarOpLawsWith`, `columnarOpStreamGen`, `incrementalLaws`,
  `incrementalLawsWith`, `paramLaws`, `schemaWalkLaws`, `nowLaws`, `slotParamLaws`, and the whole
  `IncrementalDelta` module. Their home is the module `DataFrameConformance`. A same-named
  forwarding module, `Fuaran.Core.Conformance`, in the same package keeps every `Conformance.<family>`
  spelling compiling (D68 §2). **The forwards are marked for removal in Phase 258.**
- **`Fuaran.Core.DataFrame.CSharp`** (new, .NET-only like its parent) carries the dataframe half of
  the C# facade: `Expr`, `CaseArm`, `Step`, `Pipeline`, the step specs, the slot types, and
  `BinaryOperator`, `ScalarFunction`, `JoinMode`, `SortOrder` and `ClockGrain`. They stay in the
  `Fuaran.Core.CSharp` namespace.
- **`Fuaran.Core.Conformance`** keeps every other family, `columnarValidatorLaws` and the
  `Column.aggregate` half of aggregate parity. That half is now its own family,
  **`aggregateNullSkipLaws`** (additive).
- **`Fuaran.Core.CSharp`** keeps the column layer, the hole family and the JSON model. It gains a
  public **`Vocabulary`** bridge (`ToCore` / `FromCore` for `ColumnKind` and `AggregateFunction`),
  which is additive.

**Two of these ids are the ones that leave.** `Fuaran.Core.DataFrame.Conformance` and
`Fuaran.Core.DataFrame.CSharp` go with `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` when
the compute repository takes them over (D66). They are cut here so the move is a copy of whole
assemblies.

**The class the gate prints.** `Fuaran.Core.Conformance` and `Fuaran.Core.CSharp` are both
`removal`, because a binary compiled against 0.31.0 that calls a moved member will not find it.
The two new packages have no earlier baseline to move from. Both removals ride this draft, which is
already breaking (`UpdateNode`, and Phase 246's `retype` and verdict change). **For a consumer that
recompiles, it is additive.** Add one PackageReference and no source changes: every family name and
every facade type resolves as it did. This has been measured on .NET and under Fable 5 for `open`,
a module abbreviation and fully-qualified access (D68).

**What a consumer does.**

- An F# consumer that runs any of the moved families adds `Fuaran.Core.DataFrame.Conformance`.
- A C# consumer that authors pipelines adds `Fuaran.Core.DataFrame.CSharp`.
- A census that reflects over the kit's assembly alone widens to both assemblies. The roster keys
  are unchanged, so no row is renamed. Composed from both packages, the roster has 71 families
  where 0.31.0's had 70. None is removed or renamed, and the one addition is
  `Conformance.aggregateNullSkipLaws`.
- A consumer calling `aggregateParityLaws` now runs one law where it ran two. It adopts
  `aggregateNullSkipLaws` to keep the null-skip law.

**The renames Phase 258 will ask for.** When the forwards go, a consumer spells the moved families
`DataFrameConformance.<family>`. At 0.31.0, fuaran-dotnet's census adopts ten of the families this
touches. They are `aggregateParityLaws`, `columnarOpLaws`, `columnarOpLawsWith`, `incrementalLaws`,
`paramLaws`, `schemaWalkLaws`, `nowLaws`, `slotParamLaws`, `IncrementalDelta.laws` and
`IncrementalDelta.lawsWith`. Of those, `IncrementalDelta` keeps its name.

**The roster export's shape moves, and `schema` reads 5.** `docs/conformance-families.json` is
rendered from both packages' shares (`Families.toJsonOf [ Families.roster; DataFrameFamilies.roster ]`).
The top-level `package` became `packages`, the package ids composed, in order. Each family object
gains `package`, after `entry`, naming the package it ships from. The markdown gains a `Package`
column. `Families.toJsonWith` / `toMarkdownWith` still render one package's share: the kit's own,
at the same schema.

**Held by the suite.** `ComputeBoundaryTests` refuses any of the seventeen spine assemblies that
reaches the compute side. It reads the `ProjectReference` closure of every project and each built
dll's assembly-reference table, so an `open` against a transitively available assembly is caught
where no project file names it. It was shown red by a perturbation: `Query` given a `DataFrame`
reference and one use of it, which reddened both readings and named `Conformance` as reaching
`DataFrame` through `Query`. The same perturbation, reverted, left the tests green. The roster,
census, refusal-audit and reference-run checks now quantify over both assemblies. A forwards test
holds `Conformance` in the new package to `DataFrameConformance` member for member. Each facade
proof scans its own assembly (`tests/Fuaran.Core.CSharp.Proof`,
`tests/Fuaran.Core.DataFrame.CSharp.Proof`).

### The F* target's omit-at-default wrapper terminates for a list, map, record or union member (Phase 256) — `none` on the API, a fix in what it emits

**The defect.** Phase 222 (`0.31.0`) gave each suffixed constructor's omit-at-default member a
per-slot wrapper in the encoder's mutual family, `enc_dflt_<slot> (d) v`, which tested the default
and encoded `v` itself under `(decreases v)`. For a leaf slot the encoding calls nothing, so that
was sound. For a slot whose encoder is itself in the family — a list, a map, a record or a union —
it is a recursive call on the very value the wrapper decreases on, and the pinned prover refuses
the whole model: `Error 19 ... Could not prove termination of this recursive call ... Failed to
prove: v << v`. Core's own certification vocabularies reach no such member, so Core's proof leg
stayed green. `fuaran#1860` met it at `Embed.permissions`, a list defaulting to `[]`, and held
`Fuaran.Core.Idl.Codegen` at `0.30.0` with a recorded cohort lag.

**What changed.** For a non-leaf slot the wrapper now RECEIVES the member's encoding as a third
argument, built at the call site on the member binder:
`enc_dflt_l_x #num #flt ([]) f1 (JArr (enc_items_l_x f1))`, and the wrapper is
`if v = d then None else Some e`. The recursion is on `f1`, a strict subterm of the value the
calling encoder decreases on, and the wrapper calls nothing. The argument is still an application,
so Phase 222's reason for the wrapper holds. A leaf slot's wrapper (`enc_dflt_str`, `enc_dflt_bool`,
an enum's) keeps its two-argument shape byte for byte.

**The class.** The surface gate reads `none` on `api/Fuaran.Core.Idl.Codegen.txt`: no signature
moves, only the text `vocabularyModule` / `proofsModule` return. The change is confined to
vocabularies with a defaulted list, map, record or union member in a constructor with at least
`presenceSplitAt` conditional members. Every such model emitted by `0.31.0` failed the prover, so
there is no verified model, and no hand-written lemma over one, whose meaning this moves. It rides
the `0.32.0` draft.

**Which consumer vocabularies it affects.** Core's three committed models regenerate byte-identical;
the generation diff is that assertion, and `IdlFStarTargetTests` pins that the certification set
reaches no non-leaf wrapper. `fuaran-dotnet`'s UI vocabulary is the known consumer. Regenerated with
this emitter, its model changes on three lines, the `Embed.permissions` wrapper and its one call
site, and it checks. `fuaran#1874` raises that pin and regenerates once this version is released.

**Evidence.** Pinned prover (F* `v2026.09.06`), `--z3rlimit 40 --report_assumes error`, one slot
per run. Against the `0.31.0` emitter a suffixed fixture with a defaulted list, map, record or union
member is red at the wrapper's recursive call, each run on its own, and so is `fuaran-dotnet`'s
regenerated model at the same site. Against this emitter the fixture carrying all four, a leaf
default and an optional member checks, model and proof script, at `--quake 3`. The list-only and
map-only fixtures check too, and so does `fuaran-dotnet`'s model. The fixture is
`defaultedFamilyIdl` in `IdlFStarTargetTests`, and its text-level pin fails against the `0.31.0`
emitter.

## 0.31.0 — released 2026-09-26 as `v0.31.0`

**It is a MINOR release because the change that opened it is BREAKING.**
Phase 220 changes `Conformance.certify`'s and `Conformance.certifyStream`'s VERDICT for some
domains. That is a behaviour change on the kit's most central aggregates, whatever the surface gate
says about the members that carry it (the gate classes those members as additive, and that is
right about the members and says nothing about the verdict). A breaking change cannot ride a patch
draft, so it advances the untagged `0.30.1` draft below to `0.31.0`. The entries under `0.30.1`
were never tagged, so they ship in `0.31.0`. `<Version>` and both copies of the laws corpus
(`conformance/laws/*.json` here, and its byte copy in the shared wire-format corpus) are re-stamped
together in one sitting when the slot moves, per `docs/conformance-corpus.md`.

### The Fable gate leaves this repository; the parity table is public — `Fuaran.Core.ParityVectors` (Phase 217) — ADDITIVE

**What changed.** `Fuaran.Core.Conformance` gains a module, `ParityVectors`: `vectors` (the named
cross-pipeline table), `hashSweep` (the 124-row corpus of the retired hash probe, four digests a
row) and `lines ()` (both, as the `VEC <label> <value>` lines a runner compares). The surface gate
classes the move additive, so it rides this draft. Nothing else moves: no member changes, no value
changes, no published guarantee changes.

**Why it is public.** This repository no longer runs the Fable compiler; its compile leg and value
leg run in `fuaran-dotnet`'s `tests/core-fable/` (see "Fable cleanliness"). That gate sees this
repository as packages, and the transpiled side of a value comparison has to COMPUTE the vectors, so
the table ships as code in a package the gate already Fable-compiles.

**What a consumer does.** Nothing, unless it runs its own cross-pipeline check, in which case
`ParityVectors.lines ()` is the table to diff. **What a maintainer of this repository does:** every
version cut, `0.31.0` first, cites a green run of that gate against the candidate packages
("Versioning policy"). DECISIONS.md D55 has the ruling, the consumer census it rests on, and the
217.E red/green evidence.

### The refusable-family audit — `opAlgebra` and `reducer` are `Guarded` over accepted / refused, and `certify`'s verdict moves with them (Phase 220) — BREAKING

**What changed.** `Conformance.opAlgebra` and `Conformance.reducer` are the two families `certify`
and `certifyStream` are built from. Their laws read the apply OUTCOME: totality (a refusal is
typed, never thrown), `canApply ≡ apply`, the envelope law, and the replay, inversion, uniqueness
and preservation laws, which all read the accepted side. Whether a run reached an accepted op AND a
refused one is decided by the draw, not built. Until now both families were censused
`Unconditional`, so a run that never reached one side reported every law green. Each family now
appends two adequacy laws. They come after its subject laws, so the subject laws' positions do not
move:

- `sample adequacy (Conformance.opAlgebra): the sample reached every accepted op the laws distinguish`
- `sample adequacy (Conformance.opAlgebra): the sample reached every refused op the laws distinguish`
- the same two, for `Conformance.reducer`

and `SampleAdequacy.census` reads `Guarded [ "accepted"; "refused" ]` for both.

**Which verdicts move, and which domains go RED.**

- **`certifyStream`, which runs `reducer`, is where a real domain moves.** Every op the reducer sees
  comes from the domain's own `StreamGen`. A generator that never draws an op the reducer refuses
  was certified green on a totality law that never probed the refusal path. It is now RED on
  `…reducer): the sample reached every refused op…`. The concrete instance in this repository is
  the counter reducer driven by an increment-only generator (`ReducerTests`, "a generator that
  draws only Inc"): the verdict was green before this phase and is red after it, and the refused
  guard is the ONLY red line. The mirror case also goes red: a generator whose every op is refused
  starves `accepted`, and replay determinism then asserted nothing.
- **`certify`, which runs `opAlgebra`: no consumer-visible break at a realistic iteration count.**
  This is a premise finding, and it corrects the phase's own framing. The phase assumed that a
  domain's generator might never draw a refused op for `opAlgebra`. For this family that is false.
  The ops come from the KIT's own `genOp`, not from the domain, and over ANY tree it draws both a
  reorder (always accepted) and a remove of the root (always refused). The built collision arm adds
  more refusals wherever the witness can carry a multi-node subtree. We measured it: a lone leaf
  that holds nothing, a lone leaf that holds everything, and a one-child section all reach both
  sides over 200 iterations. So at the default and customary counts, `certify` gives every domain
  the same verdict it gave before. Its guard fires only on runs too short to have reached both
  sides, such as `iterations = 1`. It is guarded because the class has to say what the code does,
  not because we expect consumers to see it red. **The consumer-visible break is `reducer`'s,
  through `certifyStream`.**
- **Nothing else moves.** Every other family's verdict, and every subject law's verdict, is
  unchanged.

**What a consumer does when their build goes red.** Read the red line. It names the side the run
never reached: `refused op` or `accepted op`.

- **`refused op` never reached: WIDEN THE GENERATOR** so that it can draw an op your reducer
  rejects. That can be an op against a missing id, an out-of-range value, or whatever your domain
  refuses. Do NOT raise the iteration count and do NOT hunt for a seed. Either leaves the refusal
  path certified by one trial, and the guard's own counterexample says so.
- **`accepted op` never reached**: your generator only produces ops your State0 refuses. Draw some
  that apply.
- **opAlgebra's guard (seen only at a tiny iteration count)**: run the base run at a realistic
  count. The kit's own suites use 200.
- There is no opt-out. A domain whose reducer genuinely has no refusal (it accepts every op) is
  the one case the guard cannot be satisfied for. Such a reducer has no refusal path for totality
  to probe, so call `streamLaws` directly, and record in your conformance census that you did not
  use `reducer`, with that reason. Do not run a generator that cannot reach the path.

**`certifyStream`'s short-circuit now reads the reducer's SUBJECT laws only.** It still refuses to
run `streamLaws` over a non-total reducer, because `append` would crash. A starved guard is no such
hazard, so the stream laws still run beside it and report their own verdict: a starved domain sees
one red line, not four. The report therefore carries 7 results where it used to carry 5 (3 stream
laws, 2 reducer laws, 2 guards). `certify` carries 17 where it used to carry 15. A consumer that
asserts on those counts moves with them.

**The audit is DATA: `Families.refusalAudit`.** It has one row per family: the family, a
`RefusalPopulation` (`NoRefusal` | `Built` | `DrawnMissIsRed` | `Drawn`) and the evidence for the
verdict. `Families.tryRefusal` looks up one family. The suite holds the audit equal to the roster
in both directions, so a family added later is audited in the commit that ships it. `Drawn` means
refusals a run can miss while every law stays green. That is the vacuity class, and a `Guarded`
census class is what reports it. `DrawnMissIsRed` means a drawn refusal whose absence turns a law
red (`functionVerifyLaws`, `verifyHonestyLaws`), which is loud and needs no guard.

**The roster export's shape moves, and `schema` reads 4.** Each family object gains two members,
written after `cases`:

- `adequacy`: `unconditional` | `guarded-reached` | `guarded-starved` | `guarded-unmeasured`
  (`Families.adequacyToken`). With it, whether a family passed, was guarded and reached, or was
  guarded and starved is a fact the generated data carries, not something a reader reconstructs
  from the census and the `cases` cell.
- `refusal`: `none` | `built` | `drawn-miss-is-red` | `drawn` (`Families.refusalToken`).

The markdown table gains two columns, `Adequacy` and `Refusal`, after `Cases`. Every member and
column before them is unchanged. A reader keyed on `schema: 3` should expect 4. Nothing it already
reads has moved. This is a shape change and it takes the stamp that says so.

### The six drawn-refusal families are `Guarded` too, and the kit's reference generators reach the refused branch (Phase 223) — BREAKING

**What changed.** Phase 220's audit found six more families in the same class as `opAlgebra` and
`reducer`. Each has a law that compares a REFUSED outcome, an agreement or an iff that holds
trivially when nothing was refused, over a population the run DRAWS. Each was censused
`Unconditional`, so a run that drew no refusal reported every law green. Each now appends adequacy
laws after its subject laws, so the subject laws' positions do not move, and `SampleAdequacy.census`
reads `Guarded` for all six. These are the six verdict moves:

- **`casLaws`**: `Guarded [ "accepted"; "refused" ]`. `appendIf with the true head ≡ append`
  compares a domain refusal with a CAS `Domain` rejection only when your `StreamGen` draws a
  refused op. New lines: `…casLaws): the sample reached every accepted op …` and `… every refused
  op …`. 3 results become 5.
- **`idempotencyLaws`**: `Guarded [ "accepted"; "refused" ]`. `fresh ≡ append` and the true-head
  CAS arm forward a domain refusal verbatim only when the drawn fresh op is refused. New lines: `…
  every accepted fresh op …` and `… every refused fresh op …`. 4 results become 6.
- **`aiSurfaceLaws`**: `Guarded [ "accepted"; "refused" ]`. `explainRejection` and the rejected arms
  of the allow / approve parity read a reducer rejection only when your op generator draws one. New
  lines: `… every accepted op …` and `… every rejected op …`. 4 results become 6.
- **`transformLaws`**: `Guarded [ "accepted"; "refused" ]`. The Error/Error arm of the parity law
  is reached only when your generator yields a pipeline the reference refuses. New lines: `… every
  evaluated pipeline …` and `… every refused pipeline …`. 2 results become 4.
- **`columnarValidatorLaws`**: `Guarded [ "null cell"; "out-of-range cell" ]`. The kit draws this
  sample itself, and a fault-free draw satisfied the soundness law as 0 = 0. The roll is now
  stratified by iteration index: iteration `i mod 3 = 0` is a clean table, `1` adds a null, and
  `2` adds an out-of-range value. So every run of three or more iterations reaches both faults.
  New lines: `… every injected null …` and `… every injected out-of-range value …`. 2 results
  become 4. The sample the soundness law sees changes with the stratification. Its verdict does not.
- **`diffContainedLaws`**: `Guarded [ "accepted"; "refused" ]`. The refusal iff reads the drawn
  pair and the minted probe under your witness's `canHold`. A `canHold` that refuses nothing, or an
  `OpGen` that supplies none, exercises only the trivial direction. The census row used to excuse
  this as "rather than missing a branch", and the guard now reports it. New lines: `… every
  container-valid pair …` and `… every refused pair …`. 3 results become 5.

**Which domains go RED.** A consumer whose own generator never draws a refusal on one of these
families now sees the guard instead of a pass. The subject laws are unchanged, so the guard line is
the ONLY red line. Each family's suite in this repository holds that as a must-fail case: an
`Inc`-only `StreamGen` (`casLaws`, `idempotencyLaws`), a generator of only applicable notes
(`aiSurfaceLaws`), the five well-formed pipeline shapes (`transformLaws`), one iteration
(`columnarValidatorLaws`), and an `OpGen` with no `CanHold` (`diffContainedLaws`). Two cases break
without any generator at fault:

- **`diffContainedLaws` over a witness with no container capability is now RED by design.** Such a
  witness has no refusal for the iff to demand, so it has nothing this family can certify. Run
  `diffLaws` instead, and record why in your census.
- **`columnarValidatorLaws` at fewer than three iterations** reports the guard. Run it at a realistic
  count; the kit's own suites use 200.

**What a consumer does.** It is the same remedy as `reducer` above. Read the red line, WIDEN THE
GENERATOR so it can draw the side it names, and do not raise the iteration count or hunt for a
seed. The kit's reference generators show the shape. Give the refusal its own STRATUM: a class of
draws that is refused in every reachable state, drawn at a fixed rate. Do not rely on a refusal
that depends on where the state happens to sit. `ConformanceTests.stratifiedStreamGen` adds an
overdraw no counter state absorbs. `AiSurfaceTests.genNoteOp` removes a note that is never there.
`LawVectorExport.lawGen` cycles eight shapes, one of which names an unknown column.
`ConformanceTests.containedGen` puts a non-container leaf under every root.

**Nothing else moves.** The published transform-laws corpus (`conformance/laws/transform-laws.json`
and its byte copy in the shared corpus) is byte-identical. Its sample already carried an
always-refused shape, so hosts reading it see no change. No member was added, removed or retyped,
and the roster export stays `schema: 4`. Its `adequacy` cell reads `guarded-reached` for all six
where it read `unconditional`. The ratchet Phase 220 left in the suite, six permitted violators
asserted exactly, is now the plain property. Every family `Families.refusalAudit` classes `Drawn`
is `Guarded`, with no exceptions.

### A snapshot at sequence zero carries the configured genesis — `snapshotAtOptWith`, `compactWith`, `compactChainOnlyWith` (Phase 227) — ADDITIVE

**What changed.** `Fuaran.Core.OpStream` gains three members. Each takes a `StreamConfig` first and
seeds the boundary hash at sequence zero with `cfg.Genesis`:

- `snapshotAtOptWith`
- `compactWith`
- `compactChainOnlyWith`

Until now `snapshotAtOpt` wrote the literal `""` there. Every chain walker starts from `cfg.Genesis`,
so under a non-empty genesis the compaction at zero of an intact stream failed `verifyAcross`
(Phase 191's `compact_at_zero_needs_the_empty_genesis`). The existing entry points keep their
signatures and are the canonical config's instantiation. The surface gate classes the move additive
(3 additions, nothing retyped), so it rides this draft.

**What it costs a pinned consumer: nothing.** Both shipped configs carry the empty genesis, so every
byte the existing entry points emit is unchanged. A digest vector and a value-for-value equality at
every boundary pin that (`Proofs.Oracle`). A domain that appends under its own `StreamConfig` with a
non-empty genesis should compact through `compactWith cfg`. The canonical `compact` still writes
`""` at zero.

**The obligation it does not remove: verify, then compact.** See "Hash-chain integrity posture". The
boundary hash is read and trusted, so the compacted stream's verdict equals the original's only over
a prefix verified first. The proof side moved in the same commit. The model's `compact` takes the
genesis, `compact_preserves_verify` drops its genesis condition, and the finding is restated as
`compact_at_zero_verifies_under_any_genesis`. DECISIONS.md D58 has the ruling and the declined option.
### Every capture key's pre-image is injective, through one canonicaliser — `Hash.canonicalFields` (Phase 225) — BREAKING (a key's VALUE), with two additive members

**What changed.** Three functions compute the Phase 27 capture key a host journals a
non-deterministic result under, and all three now build its pre-image through ONE new canonicaliser,
`Hash.canonicalFields`:

- `Query.invocationKey`: three fields per binding (name, a one-letter cell tag, the cell's
  rendering). A `Null` is now tag `n` with an empty payload, where it used to be the literal `∅`.
- `Capability.invocationKey`: two fields per binding (address, value).
- `CapabilityPipeline.nodeInvocationKey`: its hashed pre-image now covers the node's ids as well as
  its arg references, three fields per binding. A `Source` node's key is `source#<id>#<hash>` rather
  than `source#<id>#<dataRef>`.

Every field is escaped (`U+0010` before each `U+0010` and `U+0001` it carries) and terminated by
`U+0001` (`Hash.foldSep`). So the first unescaped terminator always ends a field, whatever a string
value contains. The old pre-images spliced `name=value` pairs together: on the empty string in
`Query` and the pipeline, and on `U+0001` in `Capability`. A value could therefore spell the next
binding, and two DIFFERENT argument sets shared a key. Injectivity is now proved of both seams
(`invocation_key_injective` in `proofs/Query.fst` and `proofs/Capability.fst`) and pinned on the
shipped seam.

`Hash` gains `fieldEsc` and `canonicalField` beside `canonicalFields`. These are ADDITIVE members,
in `api/Fuaran.Core.Tree.txt`.

**Why this is BREAKING and cannot ride a patch slot.** The key's type, name and arguments did not
change, but its VALUE did, for every argument set. A key's value is a contract: the Phase 27 replay
posture published "same args, same key", and a host that journals effects under a key reads them
back by that key.

**What a host with a PERSISTED journal does at the pin bump.**
- A journal written under a pre-0.31.0 key MISSES on every lookup after the bump. It is a silent
  replay-miss, never a wrong-value replay: the old key names no entry the new code will ask for.
- A host that needs those captures replays them from its own records, or re-captures them live, and
  treats the pre-bump journal as read-only history.
- No compatibility window ships (no versioned `#2#` prefix, no read-both period). This was the
  operator's ruling on the census: no host journals these keys today, so a window would protect
  nothing and would add permanent surface. See `DECISIONS.md` D60.
- A host whose journal lives only for one process (an in-memory sink) is unaffected.

**What else moves with it.** The cross-host law corpus pins literal capability keys, and the other
language ports reimplement the key. Both follow this release at their consumer's Core pin raise, in
that consumer's own change-set. Nothing in this repository's published corpus
(`conformance/laws/transform-laws.json`) moves.

### One payload for the graft-containment refusal — `DiffError.TargetNotAContainer`'s field is `target` (Phase 228) — BREAKING (one named field renamed; by-name use only)

**What changed.** `Diff.DiffError.TargetNotAContainer of parent: 'Id * kindTag: string` is now
`TargetNotAContainer of target: 'Id * kindTag: string`. That is the payload its apply-side sibling
`Rejection.NotAContainer` has always had. Both are raised from one definition,
`Ops.firstUncontained`, which `Diff.toOpsContained` now calls instead of re-stating it. The
function is `internal`, so no new public surface comes with it. A named field is source surface,
so the change is breaking and rides this breaking draft. **`api/Fuaran.Core.Ops.txt` does not move,
and that is not an oversight.** The baseline records a union case by its field TYPES
(`NewTargetNotAContainer #2(!0, System.String)`), not by its field names, so a field rename is
invisible to it and the gate prints no `retype`. The class is stated here instead.

**Migration, one line.** Construction or matching BY NAME changes:
`TargetNotAContainer(parent = …)` becomes `TargetNotAContainer(target = …)`. Positional
construction and matching (`TargetNotAContainer(p, k)`) are unaffected, and so is the runtime
value: the case, its tag and its field order do not move.

**One law list grows.** `Conformance.diffContainedLaws` reports a fourth subject law, **refusal
correspondence**, "contained diff refusal corresponds (TargetNotAContainer(t, k) ⇒ the same graft
through applyContained refuses NotAContainer(t, k))". Wherever the diff refuses, the law BUILDS the
offending nesting: the subtree `after` carries at `t` is cut out and re-inserted under its own
parent through `Ops.applyContained`. That insert must refuse with the same offender and the same
kind tag. The family now returns six results where it returned five, and its roster `cases` cell
reads 800 where it read 600. A consumer asserting the family's result COUNT updates that number. A
consumer that reads the laws by name, or checks that all passed, sees one more green law at any
coherent witness. At the reference witness the correspondence is asked on 180 of 200 iterations.
No public member of `Fuaran.Core.Conformance` moves. `docs/conformance-corpus.md` records that the
two Core classes map to one host class. The laws corpus is byte-identical. DECISIONS.md D59 has the
ruling.

### `Required` means non-null — a required query param bound only to `Null` is refused as the new `QueryError.RequiredParamsNull` (Phase 226) — BREAKING (behaviour, and a case added to a closed union)

**What changed.** `Query.validateParams`, and so `Query.invoke` and `QueryRegistry.dispatch`, gains
a third step. A `Required` parameter that is PRESENT but bound only to `Null` (every binding of its
name is `Null`) is refused before any resolver runs, with a new case,
`QueryError.RequiredParamsNull of names: string list`, naming every such parameter in declaration
order. A required parameter that is left OUT is still `RequiredParamsUnbound`, so a caller can tell
"present but null" from "missing". The steps run in order and the first refusal is the answer: an
unknown or mistyped binding first, then a missing required parameter, then a null-bound one. Before
this change `Required` checked only that the NAME was present. `[ "a", Null ]` was accepted for a
required `a`, and the resolver ran with its required parameter absent.

**Two breaks, both in this draft.**
- **Behaviour.** The argument sets that newly refuse are exactly those that bind some `Required`
  parameter, and bind it only to `Null`. That includes the all-`Null` set of any declaration that
  requires something. An optional parameter bound to `Null` is accepted as before. A required name
  with at least one non-`Null` binding is bound, whatever else binds it.
- **Source.** `QueryError` is a closed union, and it gains a case (tag 8, appended after `Timeout`
  so no existing tag moves). **Every exhaustive `match` on `QueryError` becomes incomplete.** F# warns
  FS0025, which fails a build that treats warnings as errors, and an unhandled `RequiredParamsNull`
  throws `MatchFailureException` at run time. `api/Fuaran.Core.Query.txt` records the case.

**What a consumer does.** If a caller relied on passing `Null` for a required parameter so that the
resolver could default it, **declare that parameter optional** (`Required = false`). The resolver
then sees the `Null` exactly as it did before. Add a `RequiredParamsNull names` arm to every
exhaustive match on `QueryError`. Where the caller's remedy is the same as for a missing parameter,
handle it beside `RequiredParamsUnbound`.

**The capability seam does not change.** `Capability.validateArgs` takes string arguments checked
against a `ValueSpace`, and no value space has an absent marker. A bound required hole therefore
always carries an in-space value, and "bound" and "bound to a value" are the same thing there.
`InvokeError` gains nothing. Its doc comments, and `SigEntry`'s, now say this.

**Where it is certified.** `required_is_non_null`, `all_null_refusal_exact` and
`null_required_truthful` in `proofs/Query.fst`. The ladder row `query-required-is-non-null` replaces
`query-all-null-accepted`. The oracle is re-extracted, and the query differential compares the new
refusal by class and payload and reaches it. `Conformance.queryLaws`' param-validation law now
BUILDS the all-`Null` set from its declaration and requires `RequiredParamsNull` naming exactly the
required params, and it also requires that an optional `Null` is still accepted. The law count (7)
and the family's `cases` cell (1400) are unchanged; only the law's name gained a clause. The laws
corpus is byte-identical. DECISIONS.md D61 has the ruling.

### A tree-typed slot has a value space — `ValueSpace.SlotTree`, so a capability over a slotted artifact is invocable (Phase 229) — BREAKING (a case added to a closed union, and behaviour)

**What changed.** `ValueSpace` gains a sixth case, `SlotTree of kindConstraint: string option`. It
is the value space of a tree-typed slot at the scalar invocation seam: a wire document (a
`"kind"`-tagged JSON object, passed as the argument string) whose kind matches the constraint when
one is declared, and any kind otherwise. `Function.signature` now enters every `SlotHole c` with
`Space = Some(SlotTree c)`. Before this change it entered it with `Space = None`. It is still
required, and `Slot` still carries `c`. `Capability.validateArgs`, and so `invoke` and
`Registry.dispatch`, now ACCEPT a slot argument that is a tree of the right kind. A tree of the
wrong kind is refused as `ArgOutOfSpace(addr, SlotTree c, got)`, which names the address and the
constraint. An argument that is no tree at all (a scalar, a malformed document, an object with no
string `"kind"`) is `UninvocableArg addr`, as every slot argument was before. `Space.validate`
checks the new space. `Space.slotKindOf : string -> string option` is the public reader it uses
(ADDITIVE). A capability declared over an artifact with a slot is now dispatchable. Before, it
registered, enumerated and refused every argument list (Phase 177's finding).

**Two breaks, both in this draft.**
- **Source.** `ValueSpace` is a closed union and gains a case (tag 5, appended after `AnyString`,
  so no existing tag moves). **Every exhaustive `match` on `ValueSpace` needs a `SlotTree` arm.**
  Without one, F# warns FS0025, which fails a build that treats warnings as errors, and a slot
  entry's space throws `MatchFailureException` at run time. That applies to any code that walks a
  derived signature's `Space`, because slot entries now carry one. `api/Fuaran.Core.Function.txt`
  records the case and the reader.
- **Behaviour.** A derived slot entry's `Space` is `Some(SlotTree c)` where it was `None`. Code
  that read `Space = None` as "this is a slot" should test `Kind = "slot"` instead. An argument set
  that binds a slot to a conforming tree is now accepted where it was refused. A capability
  `Pipeline` literal for a slot hole is checked against the space the same way, where it was
  `PipelineUnknownArg`.
- **The C# facade, the same break.** `Fuaran.Core.CSharp.HoleSpace` gains
  `HoleSpace.SlotTree(string? kindConstraint)`, and `HoleSpace.Match` / `Switch` gain a REQUIRED
  sixth arm, `onSlotTree` (`Func<string?, T>` / `Action<string?>`). A C# consumer's existing
  five-arm `Match` stops compiling, which is the facade's form of FS0025. There is deliberately no
  five-arm overload kept for compatibility: it would have to throw on a derived slot entry's
  `SignatureHoleView.Space`, and throwing there is exactly the defect this change removes.
  `api/Fuaran.Core.CSharp.txt` records the change.

**What does NOT change: the wire.** A slot entry's space is derived from its `Slot` constraint, so
`Function.toSchema`, `Function.toJsonSchema` and `CapabilityCodec` omit it. The bytes of every
slotted signature are the bytes they were before this change, and so is
`ContentPack.signatureFingerprint`, which a packed function records as its
`BaseSignatureVersion`. A pack authored against a slotted base still loads. A document written
before 229 decodes to the post-229 signature: the decoder restores the space from `slotKind`. Only a
slot entry whose space says something its constraint does not is written with an explicit
`"slotTree"` space, and it round-trips. **Wire-surface class: ADDITIVE** (`api/wire/Fuaran.Core.Function.txt`,
Phase 214's rule). The `capability` and `capabilityPipeline` documents gain the `"slotTree"`
space encoding, `{"$type":"slotTree"}` with an optional `"slotKind"`. It is emitted only when a
slot's space disagrees with its constraint, and every slotted signature is byte-identical. A port
that decodes capability documents (the TS and Go capability ports) learns the encoding under
fuaran#1860. The laws corpus is byte-identical. `InvokeError`,
`ApplyError` and `Deferred` are unchanged. `FunctionRegistry`'s `Exact` match compares a slot's
derived space, so a hand-built spaceless slot entry still matches a derived one.

**What a consumer does.** Add a `SlotTree` arm to every exhaustive match on `ValueSpace`. A schema
renderer projects it as an object-typed parameter carrying its kind, as `toJsonSchema` does. A host
that dispatches a slotted capability passes the slot's tree as its wire JSON string and decodes it
into its own node inside the body. Core checks the document's shape and kind and decodes nothing
below the tag. Nothing changes for a consumer that declares no slot.

**Where it is certified.** In `proofs/Capability.fst`, `slot_hole_invocable_in_space` replaces
Phase 177's `slot_hole_uninvocable`. It is proved over the completeness lemma
`validate_args_complete`, with `slot_entry_shape` (a slot is entered with its tree space),
`slot_space_exact`, `slot_wrong_kind_refused` and `slot_scalar_uninvocable`. The ladder row
`capability-slot-hole-invocable-in-space` replaces `capability-slot-hole-uninvocable`. The oracle is
re-extracted with the `kind_of` reader, the capability differential draws conforming,
wrong-kind and non-tree slot arguments, and the shipped-seam test moves the finding to closed.
`Conformance.capabilityLaws` gains an eighth law, BUILT each iteration. A capability over a slotted
artifact is derived through `Function.signature`, registered, enumerated and dispatched with a
conforming argument, and refused with both non-conforming ones. The family's `cases` cell moves from
1400 to 1600. DECISIONS.md D63 has the ruling.

### A chain-only compaction under any `StreamConfig` is verifiable through the public surface — `verifyAcrossChainOnlyWith` (Phase 236) — ADDITIVE

**What changed.** `OpStream.verifyAcrossChainOnlyWith` is public again. It takes a `StreamConfig`
first, like the rest of the `...With` family. `0.19.0` narrowed it to `internal` because no caller
outside the package was found. Phase 227 then shipped `compactChainOnlyWith cfg`, whose boundary
only this function can verify under a non-canonical config. The canonical `verifyAcrossChainOnly`
walks the tail under the canonical payload, so it refuses an intact compaction taken under any other
format. The doc comments on `compactChainOnly` and `compactChainOnlyWith` now name their verifier.

**What it costs a pinned consumer: nothing.** One member is added and nothing is retyped. The
surface gate classes the OpStream move additive, so it rides this draft. A test in `SnapshotTests`
pins the pair from outside the package. It uses a config with its own payload format and a
non-empty genesis, and checks every boundary, zero included. A one-byte change to any tail record's
hash or back-link fails the verification.

### Core emits every law set it is the reference for — `capability-laws.json` moves out of the UI tier (Phase 235) — ADDITIVE (tests and corpus only)

**What changed.** `--emit-laws` now writes TWO files under `conformance/laws/`: `transform-laws.json`,
as before, and `capability-laws.json`, the `capabilityLaws` vectors the UI tier's test project used
to export against its own Core pin. The renderer moved unchanged, member for member, and the suite
certifies the committed file the way it certifies the transform one: every vector is read back
through the public codecs and recomputed, and the file is held to a fresh render. `copies.json`
declares the corpus copy. No package's public surface moves; this is test-project code and data.

**The corpus copy LAGS, by ruling, and its owner is fuaran#1860.** Phase 225 changed every capture
key's value, and this file pins literal keys. So the committed file here carries the `0.31.0` keys,
and the corpus copy still carries the `0.30.0` ones, which is what the TS and Go ports and the UI
tier's pinned kit certify against today. Re-syncing the copy now would redden those hosts before
their ports move. fuaran#1860 re-syncs it with both ports at the UI tier's Core pin raise. Until
then the workspace copy registry names the copy stale. The in-suite leg reads it as the recorded lag and
never fails for it: every line must be byte-identical to this renderer's output, except the stamp
and the `key` value of the twelve `invocationKey` vectors. Any other difference is still a
divergence and is reported as one, fatal where the leg is asked for. The transform leg is unchanged
and strict.

### The public-surface gate sees a union field rename, and classes it `retype` (Phase 237) — NO PACKAGE MOVES (the gate's own baseline format)

**What changed.** The `api/<package>.txt` baselines now render each union case's field NAMES beside
their types: `union-case Fuaran.Core.Diff+DiffError`1.NewTargetNotAContainer #2(target: !0,
kindTag: System.String)` where the token read `#2(!0, System.String)`. The Phase 228 entry above
could only STATE its rename's class, because the gate printed nothing for it. A rename now keeps
the token's identity and changes its text, so the classifier pairs it into one `retype`, which is
breaking. The report names the case and both field names:
`field rename on …NewTargetNotAContainer: parent -> target`. Record fields already rendered by
name (`record-field X.Alpha #0 : System.Int32`), so they did not change.

**What it costs a pinned consumer: nothing.** No package source changed. The one-time regeneration
moved 16 of the 20 baselines. The other four publish no union case with fields. Every moved line
is a `union-case` token, and each file with its names stripped is byte-identical to its
predecessor. So the regeneration added names and moved nothing else. It rides this draft.

**Where the names come from, measured.** The case factory's parameter names are a lossy spelling
of the field names (`target` is emitted as `_target`), so the renderer does not read them. It reads
the field properties' `CompilationMappingAttribute(Field, variant, seq)` instead. For a multi-case
union those properties are on the case class, and for a single-case or struct union they are on the
union type. A test fails any carrying case whose fields the renderer could not name, so a fallback
to types alone cannot quietly bring the blindness back.

## 0.30.1 — never released; its entries ship in `0.31.0`

**This slot was a draft that was never tagged.** Phase 220's breaking change advanced it to
`0.31.0` before any release, so no `v0.30.1` exists. Every entry below ships in `0.31.0`, and they
are kept here, under the slot they were written for, as the record of their class.

**It is a PATCH slot because the work opening it is additive**, which is the rule the `0.30.0` entry
states rather than a choice made here: a patch number says adopting costs nothing — no source
changes, no contract moves — and that is true of an added law family or an added census row. A
member of a HIGHER class advances the slot to `0.31.0` rather than riding it, and the surface gate's
classification is what decides that, not an argument here.

**The slot was cut before the work landed, and its derived copies were re-stamped in the same
sitting.** `conformance/laws/transform-laws.json` carries a `kitVersion` derived from `<Version>`,
and the shared corpus holds a declared byte copy of it, so a version move restales both — one a
failing test in this repository, the other a failing gate in a repository this one cannot write to.
Moving the number without re-emitting both is what reddened `main` on 2026-09-22 and on three
occasions before it. The sequence is in `docs/conformance-corpus.md` and it is one sitting, not a
handoff.

### Vacuity is MEASURED — every law family reports a case count, and the roster export carries it as a `cases` column (Phase 196) — additive, with one behaviour change worth reading if you run `attestationLaws` without a signing sink

**What it is.** A `LawResult` records that a law HELD. It cannot record how many cases reached it,
so a family whose evidence is DRAWN rather than built reports the same green whether the condition
arose two hundred times or never. `SampleAdequacy` has asserted the difference *inside* a run since
Phase 121 and then discarded what it measured, which left the outside of a run unchanged: a
consumer's generated conformance census renders the same cell for a family that exercised twelve
hundred cases and one that exercised none. This ships the measurement.

**The managed additions**, all new members — nothing existing moves:

- `CaseCount` (`Family` / `Cases` / `Starved`) — what one family's run exercised. `Cases` is
  subject-law assertions made (laws reported that are not the guard's own, times iterations);
  `Starved` names the guarded dimensions whose own adequacy law went red.
- `SampleAdequacy.cases family class iterations results` — the derivation, read through the
  family's own `census` class rather than through a second registry. `SampleAdequacy.isVacuous`
  and `SampleAdequacy.renderCases` answer and render it; `SampleAdequacy.vacuousToken` and
  `SampleAdequacy.guardOpening` are the two spellings a reader keys off, exported so a host and
  the kit cannot invent different ones.
- `Families.toMarkdownWith` / `Families.toJsonWith`, taking `(string * CaseCount) list`.
  `toMarkdown ()` and `toJson ()` remain and now render `Families.unmeasuredToken` in every
  `cases` cell — which is the honest state for a caller that ran nothing, and deliberately a
  different word from `vacuous`.

**The export's shape moves, and `schema` reads 3.** Each family object gains a `cases` member (a
string: a decimal count, `vacuous`, or `unmeasured`), written last, after `discharges`. Every
member before it is byte-identical, and the markdown table gains a sixth column with the five
before it unchanged. A reader keyed on `schema: 2` should expect 3; nothing else it reads has
moved.

**The one behaviour change: `Conformance.attestationLaws` is re-classed `Guarded` and emits a
sixth law.** Its census row claimed `Unconditional` — "each iteration signs a head and forges both
an op and an attribution" — which is true of a SIGNING sink and false of `OpStream.noAttestation`,
under which four of its five laws assert nothing and all five still report green. The sink is a
parameter, so whether the evidence is built is a property of the run. The family now counts signed
heads and falsification arms and reports a `sample adequacy (Conformance.attestationLaws)` law over
both.

**What that costs you.** If you run `attestationLaws` with a real signing sink: one extra green
`LawResult`, and a law-count assertion pinned at five needs to read six. If you run it with
`OpStream.noAttestation`: the family is now RED, deliberately. That configuration was never
certifying anything — `noAttestationVacuityLaws` is the family that certifies the unsigned path on
purpose, and it is what a host with no sink should be running.

**And the kit now proves the claim about itself.** `ConformanceVacuityTests` runs every family in
the roster once at this repository's own reference witness, holds the run set equal to
`Fuaran.Core.Families` in both directions, and asserts that every family reaches a non-zero,
non-starved count — which is what lets you read a `vacuous` cell in your own census as a fact
about your witness rather than about the kit. The committed
`docs/conformance-families.{md,json}` carry that run's counts.

### One model-bridge row LEAVES the proof contract a domain inherits (Phase 200) — additive; nothing to compile

**What it is.** `evolution-table-coverage` is `tested` rather than `assumed` / `model-bridge` /
`closes: permanent`. It recorded that one of WIRE_FORMAT §15.4's four evolution rows — an added
optional field — was satisfied VACUOUSLY by `Versioning.classify`, and that nothing could close it
because widening `classify` to see field sets would model a function this repository does not ship.
The second half was wrong: `classify` takes two SUBJECT SETS rather than kind tags, and
`Diff.evolution` is the shipped caller that builds them from an IDL diff by partitioning each row on
the severity `Diff.classifyFieldAdd` reads off the field's optionality class, with
`Diff.bumpProfile` carrying the result to a published profile. `proofs/WireVersioning.fst` models
that composition and proves the row's content — an optional field moves the MINOR and leaves an old
consumer `Behind`; a required one moves the same minor while the emitter obligation is carried
separately; a host-only one moves nothing.

**What it costs a pinned consumer: nothing to compile.** No public member, type or signature moves;
this is a proof-leg and ladder change. `api/` is byte-identical.

**What it changes if you read the ladder.** The `## The Core-to-domain proof contract` table in
`proofs/README.md` loses a row: the model bridges a domain inherits are 15 rather than 16, and the
assumed rows 24 rather than 25. Nothing a domain does closed it and nothing a domain must now do —
the row moved because the model grew, which is the only way a `model-bridge` ever leaves that table.

### Defaults-fill on decode is a LAW, and the full node envelope is declared (Phase 201) — additive, and `api/` is byte-identical

**What it is.** `docs/idl-inversion-spike-findings.md` carried two open items from the Phase 316
inversion spike: defaults-fill ("feasible, not yet built") and the full node envelope ("the spike
emits `id` + `kind` only"). Both were written about `Fuaran.Core.Idl.Spike`, and both had since been
built in the production engine — `Optionality.OmitDefault` (Phase 124), `Idl.NodeFields` +
`Optionality.HostOnly` (Phases 690/691/698). What was missing was not machinery but the CLAIM:
nothing held the engine to the law the two items describe, and no vocabulary declared an envelope
carrying all five `WIRE_FORMAT.md` §3.1 slots, so "the declaration can express the whole envelope"
was an assertion about code rather than a property of something that exists.

`tests/Fuaran.Core.Tests/IdlEnvelopeTests.fs` is that claim, stated so it can fail:

- **Defaults-fill is a law, not a code path.** A member absent from a document decodes to its
  declared default and **re-encodes ABSENT**, so the bytes are stable across the round trip; a
  present non-default value is carried unchanged. Both halves are load-bearing and each alone is
  trivially satisfiable — with the interpreter's restore removed, the byte-stability case stays
  green and only the fill case goes red, which is why the pair is stated rather than either.
- **In EVERY decoder leg the generator emits** — the kind spec, a record, a union case and the node
  envelope, in one vocabulary, on both the F# and TypeScript backends.
- **The five-slot envelope**: three wire-visible members and two host-only ones, generated on both
  backends and in the JSON Schema, with a host-only member held to being on *neither* side of the
  wire. `motion` and `extraAttributes` are declarable as `HostOnly` over a `TFn` slot — `TFn` is
  named for its commonest use rather than its meaning (a slot whose HOST type is declared and whose
  WIRE form is fixed, here at absence), so a map-valued host-only member needed no type-model change.
- **The regeneration proof** extended from the reference vocabulary on one backend to all three
  neutral vocabularies on both: each regenerates its module byte-identically from its own artifact
  bytes. The comparison is against `Artifact.canonicalise idl`, not the authored form — an artifact
  is canonical by construction and declaration order is emission order, so the two vendored
  vocabularies (which are not authored canonically) would otherwise have measured their own
  authoring order rather than the engine.

**The boundary this records, because the two are one word apart.** `Idl.IdlDefault` — the root
`Defaults` list — is an **authoring** default: what a generated smart constructor fills so a caller
need not pass it. It deliberately does **not** fill on decode. The wire-level default is
`Optionality.OmitDefault`, which already does, in every leg. Collapsing the two was the obvious move
and it is not byte-stable: a `Required` member is always emitted, so a decoder that filled one from a
declared default would re-encode it PRESENT, and two distinct byte-streams would decode to one tree.
`decode >> encode` would stop being the identity on the wire — the property the conformance corpus
compares, that a content digest over a tree depends on, and that a cross-host attestation rests on.
The leniency bought is toward documents the vocabulary's own encoder cannot produce. The argument is
carried on `IdlDefault`'s own doc comment, and a test holds the generated decoder to `dReq` rather
than `dDef` for such a member, so the "improvement" cannot be made without meeting it.

**What it costs a pinned consumer: nothing.** No public member, type or signature moves; no emitted
byte moves. The change is one test module, one registration line, and prose on an existing type.
`api/` is byte-identical.

### The propagation contract at YOUR evaluator — `propagationEvaluatorLaws` and `EvaluatorWitness` (Phase 211) — additive

**What it is.** `Fuaran.Core.Conformance` gains `propagationEvaluatorLaws` and the
`EvaluatorWitness<'Model, 'V>` record it takes: a domain's model generator, the dependency map it
hands `Propagation.eval` / `evalFrom` for a model, its per-node evaluator, and its edits, each with
the change set the domain would name for it. The family runs that evaluator and certifies three
things of it. **Purity and determinism:** repeated and reordered evaluation agree, in values and in
the reads asked for. **Change-set honesty:** off the named ids, an edit moves neither the declared
reads nor the evaluator's results or asked reads, probed at four resolvers. **Agreement:** where the
edit keeps the map, `evalFrom` over `eval`'s own prior, whole and with holes drawn in it, equals
`eval`. One adequacy guard counts three arms: an edit that reached a reader, a clean node reused
from `prior`, and an edited evaluator that failed. The family reports four `LawResult`s.

**Why.** It is what the agreement theorem still assumed after Phase 209. The claims-ladder row
`propagation-change-set-and-prior` was a `premise` because no kit law ran a domain's evaluator. It is
now a `domain-obligation` with `dischargedBy: Conformance.propagationEvaluatorLaws`, following the
precedent Phase 189 set for `witness-surface-scope`. The proof contract in `proofs/README.md` is now
6 obligations, 15 bridges and 3 premises. The `prior` clause is carried by construction, and its
unobservable half stays with the domain, stated on the family: **a `prior` kept across an edit that
moves the dependency map is re-primed with `eval`, never replayed.** The family checks such an edit
for honesty and deliberately does not replay it.

**What it costs a pinned consumer: nothing.** The Phase 183 surface gate classifies the
`Fuaran.Core.Conformance` move `additive`: one new record type, one new module function, and no
existing member touched. `propagationEvalLaws`, which certifies the DRIVER over a toy evaluator, is
byte-identical and still reports six results. The `Families` roster gains the family (opt-in,
`needs-witness-capability`), so `docs/conformance-families.{md,json}` carry 64 families. A consumer
whose census quantifies over the roster has one new row to answer at its next pin raise. A domain
that evaluates incrementally should run the family from its first build.

**The adequacy witness is in-repo, and the go-reds are one perturbation each.** No adopter
evaluator exists yet (measured 2026-09-24: no caller of `Propagation.eval` / `evalFrom` outside
this repository, and every caller inside it is a test's toy). So the family ships certified against
a formula sheet in the suite: cells that divide, branch and read cells nobody holds. It reaches all
three arms (at seed 2110, 200 iterations: 110 readers, 154 clean reuses, 35 failures). Four
witnesses, each a single perturbation in its own run, show the laws can fail:
- an impure evaluator that reads a mutable cell loses purity;
- a change set naming the wrong cell loses honesty and agreement, while purity holds;
- an edit that moves only the reads an unnamed cell ASKS for loses honesty alone, which shows the
  reads half has teeth of its own;
- an evaluator that never fails starves the guard, which names that arm alone.

**Also here, comment only:** `CapabilityPipeline.eval`'s doc comment now records that the pipeline
fold stays synchronous (Phase 210's routed-out question, operator decision 2026-09-19). The
`Fuaran.Core.Function` baseline did not move.

## 0.30.0 — released 2026-09-23 as `v0.30.0`

**This slot is RELEASED.** `<Version>` reads `0.30.0` and the repository holds the `v0.30.0` tag, so
this is a released contract rather than a draft and nothing further can ride it: the next
public-contract change opens a new slot and advances `<Version>` with it — a `0.30.1` draft for an
additive or behaviour-identical change, `0.31.0` for a breaking one. The entries below are what
shipped in it. Entries written while the slot was open refer to it as the `0.30.0` draft, and to what
may ride it; they are left as written.

> **If you packed `0.30.0` as a DRAFT before 2026-09-23, repack and re-run — a pin bump is not
> enough.** This slot's contract CHANGED after it had been packed: an early draft pack still carried
> `Fuaran.Core.Lease` and `Conformance.leaseLaws`, and Phase 188 then removed that package entirely
> before the slot was released under the same number. Two different contracts wore one version
> string, and which one a consumer saw was decided by its NuGet cache rather than by its pin.
>
> Nothing a consumer normally checks can see this. The pin is correct, the source is correct, the
> suite is green, and a pin/feed audit reports nothing — because the version you name IS the version
> that was emitted. Only the content moved. A green run made against the draft bytes is therefore
> **unproven rather than wrong**, which is the worse of the two: one consumer recorded 5,126 passing
> tests while still carrying the `leaseLaws` row that the released kit's roster test refuses.
>
> The remedy is a repack with a cleared cache and a re-run of every consumer, plus deleting any
> `Fuaran.Core.Lease.0.30.0` package a draft pack left in a local feed — resolvable on that one
> machine and nowhere else. This note exists because the draft-slot model has no way to say
> "same number, new contract"; until it does, the release note is where a consumer is told.

**It was released after four phases rather than the eleven it was cut for**, which is a deliberate
decision and not an abandonment. The slot was opened for a programme of work across the hardening, IDL and
compute strands; 180, 189, 188 and 190 landed in it, and the remaining seven phases were released
from it so that consumers blocked on an unpublished version could move. Those phases open the next
slot when the first of them changes the contract. A reader comparing this entry to the cut's stated
scope is reading the difference correctly.

**This slot was OPENED rather than ridden because `0.29.0` is tagged**, which is the rule the
`0.29.0` entry states rather than a choice made here: a tagged slot is a released contract and a
contract-moving commit cannot ride it. It is a MINOR rather than a patch because the work opening it
is breaking. Phases 180 and 188 are both classed `breaking` on the roadmap — 180 retires the
hardening default, taking the last UI vocabulary tokens out of Core's source, and 188 moves the
lease algebra out to the Dispatch edge — and a patch slot states that adopting costs nothing, which
would be false of either.

**The slot was cut before any of that work landed, deliberately.** Eleven phases are in flight
against it across the hardening, IDL and compute strands; cutting once, up front, means every branch
already declares `0.30.0` and no individual change has to carry a version decision. Additive members
ride this draft without moving the number, and a second breaking change rides it too — breaking over
breaking is the same class, and the number states what adopting costs, which does not change by
being breaking twice. A change of a HIGHER class than this slot carries would advance it again, and
the surface gate's classification is what would decide that, not an argument here.

### `witness-surface-scope` becomes a domain obligation with a law (Phase 189) — additive; adds one row to a consumer's conformance census

**What it is.** `Fuaran.Core.Conformance` gains `keyedChildrenLaws` and the `KeyedWitness<'Node,
'Id>` record it takes. A domain declares which ids a node holds in keyed, non-structural positions —
the ones `NodeWitness.Children` does not report — a way to place one there, and its own full-walk id
check; the family then BUILDS the two trees that check exists to refuse (one id held both keyed and
in the witness surface, and one id held in two keyed positions) and requires the check to refuse
them. `api/Fuaran.Core.Conformance.txt` moves by those additions and nothing else.

**What it costs a pinned consumer: nothing to compile.** No existing member, type, record field or
union case moves — `OpGen` is deliberately untouched, because a field on it would stop every
full-literal construction from compiling (FS0764) and this obligation is one most domains do not
have. The witness surface itself is unwidened: `Ops` still sees exactly what `Children` reports.

**What it does change for a consumer, and it is worth reading if you publish a conformance census.**
The `Families` roster gains `Conformance.keyedChildrenLaws`, so a census that quantifies over the
roster gains a row it has not answered. Answering it is cheap in both directions: a domain that holds
nothing outside `Children` declares `HasKeyedChildren = fun _ -> []`, runs the family and is told, in
the adequacy line, that its report is **vacuous by declaration** — which is a different verdict from a
green run, and the report says which one it is. And the claims ladder's `witness-surface-scope` row is
re-classed from `premise` to `domain-obligation` with this family as its `dischargedBy`: if you have
been reading that row as one nothing could discharge, it is now one you can.

### The hardening default is RETIRED — `HardenPolicy.Default` is deleted and an absent `harden` block means "declared nothing" (Phase 180) — BREAKING on the API and on what a pre-179 artifact MEANS

**Three managed changes, and the second is the one that is not a compile error.**

1. **`HardenPolicy.Default` is DELETED.** The static member carried the five vocabulary tokens the
   engine hard-coded before Phase 116 made them declarable — one domain's spelling, reachable by any
   vocabulary that said nothing. A vocabulary that declared nothing now gets
   `HardenPolicy.Undeclared`, and the hardener refuses it by name. Every construction of
   `HardenPolicy.Default` stops compiling; the remedy is to declare the tokens the vocabulary
   actually uses, or `HardenPolicy.Undeclared` where it has no gated kind.
2. **`Artifact.readHarden` resolves an absent block as `Undeclared`, where it resolved it through
   `Default`.** This is the half that is not a compile error: an `idl.json` written before Phase 179
   and never re-rendered now decodes to a vocabulary that REFUSES to harden, where it used to decode
   to one that hardened as a domain it never named. No source changes; the meaning of already-written
   bytes does.
3. **`Trust.harden` is the CHECKED path and its return widened** from `IdlValue` to
   `Result<IdlValue, CodegenError>` — it runs `checkHardenPolicy` and refuses an undeclared member
   rather than gating through it. `Trust.hardenOrRefuse`, the name Phase 178 shipped that behaviour
   under, is kept as an ALIAS of it, so code written between 178 and 180 still compiles unchanged.
   `Trust.checkHardenPolicy` is unchanged and stays public. `Trust.scaffoldFSharp`'s signature does
   not move — it threads the refusal into the prose error channel it already had.

**Why the wire half is safe NOW and was not in Phase 178.** The default was a WIRE fact: an absent
block MEANT four tokens, so emptying it in place would have changed what published bytes mean,
silently and with a green build. `DECISIONS.md` D40 measured that and refused the flip, naming the
migration instead. Both steps of that migration are taken. Phase 179 made `Artifact.render` emit the
block for every policy, so nothing this renderer writes relies on the absent-block answer;
`fuaran#1755` re-rendered the two published artifacts — the UI tier's `idl.json` and the shared
cross-host corpus — and both carry the block, byte-identical to each other, with the workspace copy registry
reporting the corpus and both bundled host snapshots in step. The set of artifacts whose meaning
this flip could change is **empty across the consuming repositories**, which is the condition D40 named and the only
thing that ever gated it.

**And the direction of the change is the safe one, which is why it is the direction taken.** An
artifact that WOULD still hit this path decodes to a vocabulary that refuses to harden — a typed
refusal naming the member it needed — rather than one that hardens through a tag no node in it
carries. The Phase 96 lesson applies in its purest form: the failure that costs is the silent one.

**Class and slot.** BREAKING on both axes, shipping as a MINOR: this repository is pre-`1.0`, where a
minor is what carries a breaking change, and `0.30.0` was cut for exactly this work before any of it
landed. Adopting costs a consumer the compile fixes in (1) and (3), and — for a consumer holding
`idl.json` bytes written before Phase 179 — a re-render, which is the same act `fuaran#1755`
performed and is what makes (2) a non-event.

**The measured consequence, rather than the asserted one.** After this phase a search of `src/` for
`Custom`, `Markdown`, `Static` and `TextSource` as string literals returns doc comments and the
Spike's own declared vocabulary, and nothing else — 178's original acceptance criterion, met on the
schedule that makes it safe. The Spike names those tokens because they are ITS kinds and union cases,
which is precisely what Phase 116 asks a vocabulary to do; what left the engine is the set it
supplied to vocabularies that had said nothing. One further engine copy of the retired default went
with it: `Diff`'s artifact snapshot carried its own literal for an absent block, and now walks the
same members over an empty object, because a classifier that disagrees with the reader about what an
artifact means is worse than either answer alone.

### The lease strand LEAVES — `Fuaran.Core.Lease` and `Conformance.leaseLaws` are removed (Phase 188) — BREAKING: a removal, and the package is no longer emitted at all

**What is gone.** The whole `Fuaran.Core.Lease` package: `LeaseOp<'Res>` / `LeaseState<'Res>` /
`LeaseRejection<'Res>`, the `Lease` module (`emptyState`, `apply`, `canApply`, `isHeld`,
`streamWitnessFor`), its canonical wire codec and its `OpStream` `StreamWitness`. With it goes
`Conformance.leaseLaws`, the law family that certified it, and the `Stability-critical surfaces`
section above that described the strand as one of this substrate's promises. **No package is emitted
on this id at `0.30.0` or after**, and no forwarding shim is left behind — a type-forwarder would
make a consumer's restore succeed while binding it to a strand this repository no longer certifies,
which is worse than the compile error.

**Migration.** The algebra moved VERBATIM — same closed op algebra, same camelCase kind-tag envelope,
same int64-as-JSON-string time encoding, same conflict contract, same law kit — into the coordination
library that was its one consumer, which lives outside this repository and pins this substrate rather
than the reverse. A consumer that held `Fuaran.Core.Lease` takes the algebra from there: a package and
namespace change at the `open`, and nothing else. Nothing in the algebra's behaviour or its wire bytes
changed in the move, which is why a stream written under `Fuaran.Core.Lease` replays unchanged on the
other side. A consumer whose lease coordination never came from this substrate is unaffected.

**The removal CLOSES a hazard rather than only removing a surface, and this is the part worth reading
if you take `Fuaran.Core.Conformance`.** Between the receiving library's own cut and this removal, a
consumer of this kit restored `Fuaran.Core.Lease` transitively — the kit depended on it to host
`leaseLaws` — so a project that had already adopted the moved algebra held TWO `Lease` modules with
the same type names in one closure. F# binds a name to the last `open` that provides it, so the
`open` order decided which algebra was in scope, silently and with a clean build either way. After
this removal the kit's closure carries no lease package at all and the ambiguity is not expressible.

**The roster consequence, which is the mirror of Phase 189's in this same slot.** `Families` loses
`Conformance.leaseLaws`, so a conformance census that quantifies over the roster loses a row rather
than gaining one — an opt-out row, declared `seam-not-every-domain-has`, so a domain that answered it
answered "not mine". `docs/conformance-families.md` and `docs/conformance-families.json` are
regenerated accordingly. `SampleAdequacy.census` loses its entry in the same commit, because a census
naming a family the kit does not ship is a claim about nothing.

**Why it left, and the reason is NOT the consumer count.** Recorded as D51 (2026-09-23), the
operator's 2026-09-16 membership ruling: membership in this substrate is genericity over the witness
and plausible cross-domain use, never present consumer count. `LeaseOp<'Res>` is generic in its type
parameter, and its only instantiation is a claim over a **resource** axis — not the tree, and not a
witness this substrate defines. It shipped in 2026-07 under D9's single-unblocking-consumer exception
and the corroborating adopters D9 called "corroboration, not prerequisites" never arrived; the census
that showed so is evidence in D51 and is not its argument. The strand would leave on this rule with
ten consumers, and a tree-generic function stays here with one.

**What deliberately did NOT go with it.** `Ops`' `footprint` and independence, and `Arbitration` —
tree-generic by shape, and `Arbitration.arbitrate` is D51's live counter-case: one caller, and it
stays. Lease AUTHORITY — who may claim, and the fold that decides — was never in this repository to
move.

**Baselines.** `api/Fuaran.Core.Lease.txt` is deleted with its package.
`api/Fuaran.Core.Conformance.txt` moves by exactly one token, the `leaseLaws` method — a `removal`
under the Phase 183 classifier, which is what makes this entry's BREAKING class a gate output rather
than an argument.

**Riding the draft rather than advancing it.** `0.30.0` is already BREAKING — Phase 180 opened it
retiring the hardening default — and breaking over breaking is the same class: the number states what
adopting costs, and that does not change by being breaking twice. This slot's own preamble named this
phase before the work landed.

## 0.29.0 — released 2026-09-21 as `v0.29.0`

**This slot is RELEASED.** `<Version>` reads `0.29.0` and the repository holds the `v0.29.0` tag, so
this is a released contract rather than a draft and nothing further can ride it: the next
public-contract change opens a new slot and advances `<Version>` with it — a `0.29.1` draft for an
additive or behaviour-identical change, `0.30.0` for a breaking one. The entries below are what
shipped in it. Entries written while the slot was open refer to it as the `0.29.0` draft, and to what
may ride it; they are left as written.

**This slot was `0.28.1` until Phase 194, and the number advanced under the rule in the paragraph
above rather than by anyone's choice.** The entries below were written for a patch slot — an
additive guarded renderer, a corrective fix to a merged order — and Phase 194 then widened a
PUBLISHED record (`Families.LawFamily` gained `Reason`), which the surface gate classified
`record-widening`: every full-literal construction of that record stops compiling (FS0764). That is
a higher class than a patch slot can carry, so the slot became `0.29.0` and the entries that had
ridden `0.28.1` ride it. No `v0.28.1` tag was ever cut, so no consumer can have pinned the old
number.

**A corrective change rides a PATCH slot, and that is the rule rather than a convenience.** The
number states what ADOPTING costs, and adopting a correction costs nothing: no source changes, no
contract moves, and the behaviour that changes is behaviour that was wrong. What such a change owes
the reader instead is the opposite direction — which *published* versions answer wrongly, so a
consumer can tell whether it is on one. The entry for Phase 215 below names them, measured against
the released packages.

**This slot was OPENED rather than ridden because `0.28.0` is tagged**, and it is a PATCH rather
than a minor because the Phase 183 surface gate reported **no baseline moved since `v0.28.0`** when
this entry was written — the classes are `additive` and `corrective`, and the rule the `0.28.0` entry
states is a patch draft for an additive or behaviour-identical change. _(The slot has since become
`0.29.0`: Phase 194 widened a published record, which is a higher class than a patch slot carries.
This entry's own class is unchanged — what moved is the number it ships under.)_

**What adopting this slot (`0.29.0`) costs, and the one consumer it can cost something.** For THIS
entry no managed member,
type, record field or union case moves, so no .NET or Fable consumer's source changes and nothing
needs migrating. Two things change. **A wrong answer stops**: a merged order whose sort key reads a
window's output column returned the wrong rows on every release from `0.18.0` to `0.28.0`, and does
not now — if you run such a pipeline through the incremental seam, this pin is the fix and the entry
below is the one to read first. And DELIVERED CONTENT changes: the `IncrementalDelta` conformance
family's generated corpus grows by ten pipeline shapes. **A host that runs that family against its
own incremental evaluator can go RED at this pin — and a red here is a WRONG ANSWER in your
incremental path, not a regression in this kit.** The predicate to check is in the entries below.

**One managed member HAS been added since the two paragraphs above were written, and it costs a
pinned consumer nothing.** `Fuaran.Core.Wire` gains `Canon.tryRender` (Phase 165, below) — class
`additive`, with `api/Fuaran.Core.Wire.txt` moved by that one line. An addition is a class this slot
was opened to carry, so the number stands.

### The incremental corpus reaches a row-local step reading a cross-row column (Phase 212) — additive; may turn an ADOPTER's conformance run red

**What it is.** `IncrementalDelta`'s enumerated corpus grows from 38 pipeline shapes to 48. The ten
new ones are the class Phase 208 found the hard way and `v0.26.0` published: **a row-local step
(`Filter`, `Derive`, `Project`) reading a column whose value for row *r* depends on rows other than
*r*.** The corpus carried ten window-bearing shapes and not one of them — in eight the window was
the last step, and in the two that continued the next step was a `GroupBy`, which re-aggregates from
its member rows and never consults the per-row cache. So the family that exists to catch that defect
could not, and three phases read the code without seeing it.

**Why an adopter can go red, and what to do about it.** If your evaluator keys its per-row cache on
*the delta did not name this row* rather than on *this row's cells have not moved*, these ten shapes
are exactly the ones that expose it, and the family will now say so. That is the intended effect.
Check that predicate first: a `Window` recomputes its column over the whole frame it is handed, so a
row the delta never named legitimately comes out of it with a different cell, and any later step
that reuses a cached answer computed from that row's old cells is answering a question that has
moved. This kit's own fix was to replace the condition with *byte-identical to the cells the prior
evaluation held for this row*, cleared for every row at a `Window` (`0.27.0`, Phase 208).

**What it does not change.** No public surface moves — the Phase 183 gate reports zero baselines
moved since `v0.28.0`. No existing shape moves in verdict; `IncrementalDelta.laws` is green over the
whole corpus at every pinned seed. The sample-adequacy census gains a third guarded dimension,
`cross-row column read`, with one verdict per cross-row producer class, and every pre-existing
refresh class still clears the suite's 7% reach floor. The measured shares, the full
(producer × consumer) census and the two producers that are absent by construction are in
[`docs/incremental-evaluation.md`](docs/incremental-evaluation.md).

**A SECOND live defect, found here and fixed in this same draft slot — see the entry below.** Adding
these shapes surfaced a neighbouring wrong answer in the merged order. Phase 212 reported it and
deliberately left it standing, because the fix belongs to the seam rather than to a conformance-corpus
phase and a corpus phase that quietly patches the seam is how a finding stops being a finding. Phase
215 fixed it; the corrective entry immediately below is what it means for you.

### A merged order no longer reuses the position of a row a window moved (Phase 215) — CORRECTIVE; wrong answers in `0.18.0`–`0.28.0`

**What was wrong.** **A merged order whose sort key read a column a `Window` appended returned the
wrong rows.** The seam's per-row cache condition is *this row's cells are byte-identical to the ones
the prior evaluation held* (`Stable`), and a `Window` clears it for every row because it recomputes
its column over the whole frame it is handed. Phase 208 (`0.27.0`) substituted that condition for the
older *the delta did not name this row* at the two sites it found — the per-row cell cache, and a
filtering join's cached verdict — and left a THIRD standing: the `Sort` step built its reusable set
the old way, so a merge reused the cached POSITION of a row whose sort key a window had moved. This
release reads the sort's reusable set on the same condition as the other two.

**Which releases answer wrongly, measured rather than inferred.** A probe pinned to one published
`Fuaran.Core.DataFrame` at a time ran `window(rank) > sort(rk)` through that package's own
incremental seam and compared the answer with that same package's reference evaluator.
**`0.19.0` through `0.28.0` — every released version in that span — disagree**, and so does
`window(lag) > sort(prev)`, while the same pipeline sorting on a source column agrees. `0.16.0` and
`0.17.0` agree: they predate the merged order (`0.18.0`, Phase 115) entirely, and they are the
probe's falsifier. `0.18.0` itself carries the same reuse condition and admits bounded-frame windows
only, so it is reached by the `lag` shape rather than the `rank` one; no package was published for it
to measure. **Neither the condition nor the span is `0.26.0`-onward**, which is where the two earlier
records placed it by analogy with the sibling defect `0.27.0` fixed.

**What to do if you are on any of those releases.** Adopt `0.29.0`. Until you do: re-prime rather
than refresh, or put the `Sort` ahead of the `Window`, for any pipeline whose sort key reads a
window's output column — directly, through a `Derive` that reads it, or through a `Project` that
renames it. A pipeline whose sort key reads source columns only was never affected, with or without a
window in front of it.

**What it does not change.** Nothing public moves: no managed member, type, record field or union
case, and the Phase 183 surface gate reports no baseline moved. The seam refuses nothing new and its
declined set is unchanged — it stops answering wrongly. A footprint can legitimately RISE for an
affected pipeline: the reusable set is now the smaller, correct one, so a refresh behind a window
re-sorts rows it used to merge, which is the cost of the right answer. `IncrementalDelta` shape `44`
sorts on the window's own column now rather than withholding the key, and the pending reproducer in
`IncrementalRefreshCostTests` — which asserted the defect's PRESENCE — is the regression test that
asserts its absence. The per-site census of which cache condition each reuse in the seam needs, so
that a fourth site cannot quietly be written, is in
[`docs/incremental-evaluation.md`](docs/incremental-evaluation.md).

### `Arbitration.duplicateIds`, and the id-uniqueness hypothesis as a law (Phase 157) — additive; one law list grows

**What it is.** `Arbitration.duplicateIds : OpScriptProposal<'Node,'Id> list -> int list` in
**`Fuaran.Core.Ops`**: the proposal ids carried by more than one proposal, ascending, each once;
empty exactly when the ids are unique. A TOTAL check and never an assigner — it reads `Id` and
nothing else. `arbitrate` does not call it and did not change: same partition, same order, same
rejections, byte for byte.

**Why it exists.** `arbitrate`'s doc comment has always said its permutation invariance "assumes
unique ids". Phase 157 proved the three promises that comment makes
([`proofs/README.md`](proofs/README.md), theorem 13) and, as chartered, found out what the prover
needs of that assumption: **it is needed, and its absence is a counterexample rather than a gap.**
Two proposals sharing an id that interfere with each other are accepted in ARRIVAL order — the
stable sort breaks the tie by input position — so the same proposal set in two orders arbitrates
differently. `arbitrate` stays total on such input, and its accepted set stays pairwise independent
and maximal; what a repeated id costs is invariance and nothing else. Since Phase 192 the function
that assumes uniqueness and the callers that mint ids sit in different packages, so the assumption
is now checkable by whoever holds the list: `Arbitration.duplicateIds proposals = []` IS the
theorem's hypothesis, and a non-empty answer names the ids to repair.

**What adopting it costs.** The Phase 183 gate classes the `Fuaran.Core.Ops` baseline move
`additive` (1 move: the new module function), so it **rides this draft slot** — a baseline that
has moved since `v0.28.0`, which the slot's opening paragraphs, written before any had, say had not
happened yet. A pinned consumer compiles either way. **One thing a consumer asserting
law COUNTS must adjust:** `Conformance.arbitrationLaws` reports **7** results where it reported 6 —
the new one, *id uniqueness is the invariance hypothesis*, sits before the Phase 121 adequacy row,
which is still last. It holds `duplicateIds` to an independent recount, asserts it empty on every
set the permutation law is certified over, and asserts that a twin — the same id and script under
another holder — makes arrival order observable, with its own vacuity guard. No existing law moved
in meaning, order or verdict; a consumer reading `AllPassed` or matching on `Law` changes nothing,
exactly as when Phase 121 grew the same list from 5 to 6. A domain whose generator can never
produce an applicable self-interfering proposal would see the new law's vacuity guard fire; every
script holding one real skeleton op interferes with itself, so that is a generator producing only
empty scripts, which the Phase 121 bucket guard already refuses.

**What was deliberately not shipped.** An id ASSIGNER. Declined by operator decision (2026-09-19):
there is one in-repo assigner already (`AiSurface.Proposals`), and the coordination-layer callers
hold ids of their own, so a second way to mint them would be a second source of truth about what a
proposal's id is. And `arbitrate` does not refuse duplicate-id input: refusing would change what a
public function returns on input it has always accepted, and totality on that input is documented
behaviour that the proof now states exactly.

### A guarded `Canon.tryRender` beside the canonical renderer (Phase 165) — additive; rides this draft

**What it is.** One new function in `Fuaran.Core.Wire`:

```
Canon.tryRender : JVal -> Result<string, string>
```

Over a value holding no non-finite float it is exactly `Ok (Canon.render v)`. Otherwise it is an
`Error` naming the FIRST non-finite `JFloat` in document order — arrays by index, object members in
AUTHORED order — by its token and its path: `$` for the root, `[i]` for an array item, `["key"]` for
a member, the key under the canonical escape so the path is unambiguous for any key. For example
`non-finite float has no canonical rendering of its own: Infinity at $["a"][1]`. It is the shape
`Json.tryRender` has given `Json.render` since Phase 12.

**Why it exists.** `Canon.render` spells a non-finite float as the QUOTED token `"NaN"` /
`"Infinity"` / `"-Infinity"`, which is byte for byte what the STRING of those characters renders as.
The wire is valid, and wrong: a digest over `JFloat nan` equals the digest over `JStr "NaN"`, the
value a reader decodes those bytes back to. Phase 149 proved that as `render_aliases_nan` and its
two siblings and recorded that nothing refused it. This is the refusal.

**What it does NOT change — `Canon.render` is untouched.** Its bytes are pinned by the conformance
corpus and by every other host; no conformance vector moves, and a value that aliases under `render`
keeps aliasing under `render`. A caller that wants the refusal reaches for the new entry point; a
caller that does not is exactly where it was. `Canon.renderOrdered` is untouched and has no guarded
companion.

**What the guard deliberately does not refuse.** Its predicate is FINITENESS and nothing else. The
format's documented normalisations pass through unchanged — an integer-shaped finite float
(`JFloat 2.0` renders `2`), the `-0` collapse, and the key sort — because each is what the format
says rather than a value silently becoming another. `proofs/WireCanon.fst` section 13 proves both
halves: the guard is the renderer wherever every float is finite
(`tryrender_is_render_on_finite`), it refuses exactly where one is not
(`tryrender_refuses_exactly_aliasing`), and the three documented normalisations are accepted
(`tryrender_keeps_the_documented_normalisations`).

**There is no guarded DIGEST in this package, and that is the design rather than an omission.**
`Fuaran.Core.Wire` references nothing that hashes, and every digest in the repository takes a
string a caller has already rendered. The guarded digest is `Canon.tryRender v |> Result.map digest`
at the caller, with the caller's own hash.

**The error message is for a human and is not a parsing contract.** The token and the path are
stated above so a reader knows what to expect; a consumer that needs to BRANCH on a refusal branches
on `Error`, not on the text.

**Class: `additive`** — a new module function, per the Phase 183 surface gate
(`api/Fuaran.Core.Wire.txt` moves with it, by one line). A pinned consumer compiles either way, so
it rides this draft and moves no number.

### `Families.LawFamily` gains `Reason`, and the roster says WHY a family is opt-in (Phase 194) — RECORD-WIDENING

**What it is.** `Fuaran.Core.Families.LawFamily` gains `Reason: OptInReason option`, and
`OptInReason` is a new closed union — `NeedsWitnessCapability`, `SeamNotEveryDomainHas`,
`StrongerPromise`. Every one of the roster's 58 opt-in families now states which it is; the five
families `certify` and `certifyStream` are built from carry `None`. `Families.reasonToken` renders
a case to its wire spelling.

**The class, and why it is not additive.** The Phase 183 surface gate classified this
`record-widening`: a field added to a record the baseline published, so every FULL-LITERAL
construction of `LawFamily` stops compiling with FS0764. In this repository exactly one such
construction existed — a test helper — and it was updated in the same commit. A consumer that only
READS the roster is unaffected; a consumer that constructs a `LawFamily` adds `Reason = None`.
The shard called the change additive; the gate disagreed, and the gate decides (D45).

**`OptIn` did not move, and is now derived.** It remains a published `bool`, and every construction
site in `Families` computes it as `Reason.IsSome` — so a family cannot be declared opt-in without
saying why, and a base-run family cannot carry a reason. That invariant is structural rather than
test-enforced; the suite additionally fails loudly if the two are ever re-introduced as independent
fields, and holds `NeedsWitnessCapability` to the roster's OWN data (a family claiming it must take
a witness the base run does not).

**The generated export moved with it.** `docs/conformance-families.json` carries a `reason` member
and its `schema` reads **2**. The member is **present only for an opt-in family** — this wire model
has no null, so absence is how the format spells "not applicable"; rendering `null` produced a
document the kit's own parser refuses, which is `no_null_ever` (Phase 153) doing its job.
`docs/conformance-families.md` gains a `Why opt-in` column. a downstream tooling phase (#482), which will replace
that projection's roster with this file, is the coupled surface and is unstarted.

**What adopting costs.** Nothing for a reader. One field for a constructor. The census a consumer
regenerates is unaffected — no cell vocabulary moved.

## 0.28.0 — released 2026-09-20 as `v0.28.0`

**This slot is RELEASED.** `<Version>` read `0.28.0` and the repository holds the `v0.28.0` tag, so
this is a released contract rather than a draft and nothing further can ride it: the next
public-contract change opens a new slot and advances `<Version>` with it — a `0.28.1` draft for an
additive or behaviour-identical change, `0.29.0` for a breaking one. The entries below are what
shipped in it, and the paragraph after next is the one to plan an adoption from. Entries written
while the slot was open refer to it as the `0.28.0` draft; they are left as written.

**This slot was OPENED rather than ridden, and the reason is the rule rather than the size of the
change.** `0.27.0` is tagged, so nothing can ride it; and the one change below is BREAKING on the
canonical encode, which under the draft-slot rule takes a MINOR bump pre-1.0 — the precedent the
`0.19.0`, `0.20.0` and `0.27.0` entries in this document set.

**What adopting `0.28.0` costs.** One entry, one package (`Fuaran.Core.DataFrame`), and the cost
falls on exactly one kind of consumer: a host that compares the canonical encode's BYTES. Every
decoder is unaffected — every document written before this slot still decodes, to the same tree —
and no .NET or Fable consumer's SOURCE changes, because no managed member, type, record field or
union case moved. A host with a round-trip corpus of pipeline wire strings re-records it; a host
that only decodes does nothing.

### The dataframe wire spells out a column-naming member (Phase 213) — BREAKING on canonical encode, additive on decode

**The rule, in one sentence** (`DECISIONS.md` D48): a wire member of the dataframe algebra whose only
honest name is "the column" or "the columns" is spelled out in full — `column` for one, `columns` for
a list — and never abbreviated; every other member is named for the ROLE its columns play in the step,
and an abbreviation survives only as a decode alias.

Two members move under it, and they move in opposite directions from where they stood:

| object | was | is | alias kept |
|---|---|---|---|
| `project` step | `cols` | **`columns`** | `cols` |
| a `sort` key, and a `window`'s frame-ordering entry | `col` (canonical), `column` (alias) | **`column`** | `col` |

Nothing else moves. `groupBy.keys`, `sort.by`, `window.partitionBy` / `of` / `as`, an aggregate's
`of` / `name`, `derive.name`, `join.on`, `pivot.index` / `on` / `values`, `unpivot.idVars` /
`valueVars` and a pair's positional `a` / `b` all name a ROLE rather than "the column", and keep the
names and aliases they had. The `col` EXPRESSION's `$type` tag is untouched: a `$type` names a KIND,
not a column, and it is not a member.

**What a document does.** Decode accepts either spelling through the existing `fieldAliased`
mechanism, so nothing written before this slot stops reading; a document carrying the alias
re-encodes with the canonical spelling; a document carrying BOTH is refused as ambiguous, by the same
error every other aliased member raises. The alias is kept until a major version says otherwise and
is never emitted. This is the shape of the earlier window-function tag rename, which likewise kept
the old tags as decode-only aliases.

**The class, and the honest limit of the gate that assigns it.** The Phase 183 public-surface family
prints, for this tree, `(no baseline has moved since v0.27.0)` over `21 baseline(s) read, 0 moved` —
so by the instrument this document normally quotes, the move is invisible. That is not the gate
failing; it is the second of its three declared boundaries doing exactly what it says. The gate
renders the managed assembly's IL metadata, and a wire member name is a STRING inside a function
body: no type, member, record field or union case moved, so there is nothing for it to see. The
breaking class here is a property of the canonical BYTES, which is the boundary the gate names as
"not semantics" — so the version advances on the byte change, with the gate's output quoted as the
evidence that the managed surface is NOT what moved rather than as the classification itself.

**The law corpus moved, and not for the reason the change is about.** `conformance/laws/transform-laws.json`
(and its declared copy in the shared corpus) is re-emitted: **no law's bytes carry a `project` step at
all**, so the rename that names this entry changes none of them — what moves is the two `sort` laws'
key members, plus the `kitVersion` stamp a version cut re-stamps. A host certifying against that file
takes the new bytes with the new pin.

## 0.27.0 — released 2026-09-19 as `v0.27.0`

**This slot is RELEASED.** `<Version>` reads `0.27.0` and the repository holds the `v0.27.0` tag, so
this is a released contract rather than a draft and nothing further can ride it: the next
public-contract change opens a new slot and advances `<Version>` with it — a `0.27.1` draft for an
additive or behaviour-identical change, `0.28.0` for a breaking one. The entries below are what
shipped in it, and the paragraph after next is the one to plan an adoption from. Entries written
while the slot was open refer to it as the `0.27.0` draft; they are left as written.

**This slot was ADVANCED from a `0.26.1` draft, and the two entries below it come forward with it.**
`0.26.1` was opened by Phase 206 (because `0.26.0` is tagged) and ridden by Phase 186, both
BEHAVIOUR-IDENTICAL or ADDITIVE with no public surface moved. No `v0.26.1` tag was ever cut, so
nothing was published under that number and no consumer can be pinned to it — the slot was a draft
in the strict sense. Phase 198 then made a BREAKING change to a published seam, which the draft-slot
rule says advances rather than rides: a number that reads "additive" over a retyped public member
tells a consumer the wrong thing about what adopting it costs. So `0.26.1` is not a slot this
repository will ever release, and the work that was riding it ships in `0.27.0`. Pre-1.0 breaking is
a MINOR bump, per the precedent the `0.19.0` and `0.20.0` entries in this document set.

**What adopting `0.27.0` costs — the NET of the entries below, measured against `v0.26.0`.** Seven of
the ten entries in this slot are breaking, across six packages, and three of them touch the same
type — so a consumer pays the net, not the sum, and this paragraph is the one to plan an adoption
from. Each class is the Phase 183 gate's own output for that move, quoted in its entry; the gate's
since-the-newest-tag report cannot restate them for THIS release, because the `api/` baselines were
first committed after `v0.26.0` was tagged and it reads every package as a first snapshot. They were
committed with no change under `src/` since that tag, so they ARE the tagged surface, and from
`v0.27.0` onward the report answers this question itself.

| package | class (gate output) | what a pinned consumer changes | entry |
|---|---|---|---|
| `Fuaran.Core.Query` | `retype` (4 moves) | a resolver returns `Ready r` / `Failed m` where it returned `Ok r` / `Error m`; a caller of `Query.invoke` or `QueryRegistry.dispatch` handles `Ok (Ready r)` and `Ok Pending`. Every `QueryError` arrives as before. No use for `Pending`: one line, `Deferred.toResult` | Phase 198 |
| `Fuaran.Core.Function` | `retype` (3 moves) | every capability body returns `Deferred` (`Ready v` / `Failed m`); a caller of `Capability.invoke`, `Registry.dispatch` or `FunctionRegistry.dispatch` — THREE members — handles settled / pending / refused. Every `InvokeError` arrives as before. `CapabilityPipeline.eval`'s per-node body is deliberately unchanged | Phase 210 |
| `Fuaran.Core.AiSurface`, `Fuaran.Core.Ops` | `removal` (15 moves); `additive` (20 moves) | `AiSurface.arbitrate` becomes `Arbitration.arbitrate` over an `OpScriptProposal` list; `Proposals.toOpScript` is the projection. A consumer with no arbitration call site changes nothing | Phase 192 |
| `Fuaran.Core.Propagation` | `union-widening` (3 moves) | an exhaustive `match` over `PropagationError` gains `EvalUndeclaredRead` (declared last); an evaluator that reads a node it did not declare is now REFUSED by `eval` and `evalFrom` alike — declare the read | Phase 209 |
| `Fuaran.Core.DataFrame` | `union-widening`; `record-widening`; `removal` (19 moves) | NET of three entries: an exhaustive `match` over `StepIncrementality` gains `TruncateOrder`, and one over `FallBackReason` gains `AggregateStepRepeated` (both declared last, no existing tag moved); `IncrementalEval` is OPAQUE — a field read becomes an accessor call (the substitution table is in Phase 208's entry) and a state can no longer be constructed or copied with `{ … with … }`. Phase 202's record-widening cost never reaches a consumer: the record it widened is private by the end of the same slot | Phases 207, 202, 208 |

**This release is CORRECTIVE as well as breaking, and that is the reason to adopt it promptly.**
`v0.26.0` carries a wrong answer in the incremental seam: a step that reads a `Window`'s output column
(`Filter > Window(cumulSum) > Filter` on the window column, and the same with a `Derive`) can reuse a
cell the window has since recomputed, and the restricted refresh then DISAGREES with the reference
evaluation — the one thing the seam promises never to do. Phase 208 found it, measured it on the
`0.26.0` code and fixed it. A consumer of a window-bearing pipeline will see a LARGER recompute
footprint after adopting: the smaller one came with the wrong answer. No law caught it because the
conformance corpus generates no step that reads a window's column; closing that class is follow-on
work and is not in this slot.

**Three statements further down this slot are superseded by later entries in it, and are left as
written because each was true when its phase landed.** Phase 206's and Phase 207's measurements that
the restricted refresh LOSES to the full evaluation for a cheap row expression are superseded by
Phase 208's (it now wins on all three measured pipelines, at 20,000 rows with one row edited). Phase
198's paragraph recording that `Capability` does NOT carry the envelope is superseded by Phase 210,
which says so itself. Phases 206, 186 and 163 move no public surface and cost a consumer nothing.

**The stale-pack hazard, once, for the whole slot.** Three of these moves are union-widenings. Against
a package rebuilt from this source an exhaustive `match` fails to compile, which is the safe failure;
against a STALE pack of the same version number it is an `InvalidCastException` at run time with no
compile signal at all. Adopt by version — pin `0.27.0` as released — never by repacking a number a
consumer has already restored.

### Linear-time row access (Phase 206) — BEHAVIOUR-IDENTICAL, no public surface moves

The columnar evaluator was **quadratic in the row count**, and the delta and the incremental seam
inherited it. Every consumer that wanted a table's rows asked for them one index at a time through
`Column.cell i c`, which is `List.item` over a linked list: each cell read walked its column from
the head, so reading an n-row table cost O(n² × columns). Four grouping folds compounded it by
appending to an accumulator per row (`rows @ [ row ]`), which is quadratic in the group size and —
where the grouping key has high cardinality — quadratic in the row count again.

Everything is now a single pass. The frame is a transpose, the delta's row tokens are computed once
per table, the reference identity witnesses build a row-indexable view on their first application,
and the folds prepend and turn once at the end.

**This is a PATCH and the class is a gate output, not a claim.** `Column.Cells` is still a
`Cell list` — deliberately, because making it an array would break every consumer's construction
sites and is not needed to remove the quadratic — no public signature moves, and the Phase 183
public-surface family renders each package afresh and reports no difference against the committed
`api/` baselines. Results are byte-identical: the transform-parity family, the incremental
equivalence family (`IncrementalDelta.laws`), `incrementalLaws`, `footprintLaws` and
`dirtyPropagationLaws` are all unchanged in verdict, which is the point — this is an access-pattern
change and not a semantic one. The only file under `conformance/` that moved is
`laws/transform-laws.json`, and only because it stamps the kit version.

Measured on one machine, 1,000 → 20,000 rows, a `Filter > GroupBy` pipeline with one row of the
source edited. Median of three timings on both sides:

| at 20,000 rows | before | after | |
|---|---|---|---|
| reference evaluation | 3,153.39 ms | **27.49 ms** | 115× |
| `Delta.diff` | 7,689.30 ms | **107.20 ms** | 72× |
| prime + diff + refresh | 16,015.97 ms | **246.03 ms** | 65× |

The scaling ratios over 1,000 → 20,000 rows went 163.67 → 20.26, 412.85 → 26.91 and 440.48 → 38.43
respectively: four hundred is the quadratic signature, twenty the linear one.

A new `Scaling` family holds that SHAPE rather than the times: twenty times the rows may cost at
most five times the linear expectation. A ratio between two sizes in one process, never an absolute
threshold — Core owns no clock (GP6), and an absolute bound is a test that eventually fails on a
slow runner for a reason nobody can act on. The bound is five rather than two because none of the
three paths is exactly linear even now: they key rows through persistent maps over string and
string-list keys, so they are n log n, and the family's own best-of-five estimator reports them at
34, 52 and 44.

**What the fix did NOT buy, stated because the phase set out to buy it.** With the quadratic gone,
a restricted refresh is still **slower** than the full evaluation it replaces when the row
expression is trivial: 88 ms against 20 ms at 20,000 rows for a single `Ge` comparison. The seam's
per-row bookkeeping — an identity token, its uniqueness check, two lookups into the prior row-cell
map, a group-membership entry, all string-keyed persistent-map operations — does not shrink when
the expression does, and it now outweighs the one row expression the refresh avoids evaluating
twenty thousand times. Put real work in the expression and the seam wins as designed: at 129
expression nodes it is 99 ms against 224 ms. The refresh's own cost barely moves between the two
(88 → 99 ms) while the full evaluation's goes up eleven-fold, which is the model stated as a
measurement. The seam's proposition is therefore about the SIZE of the row expression, not about
the size of the table, and `docs/incremental-evaluation.md` now says so with the figures.

### Incremental evaluation agrees with full evaluation, as a theorem (Phase 186) — ADDITIVE, no public surface moves

`Propagation.evalFrom`'s doc comment says its result is "byte-identical to a full `eval`", and until
this phase that sentence was sampled by two law families and proved nowhere. `proofs/Propagation.fst`
models the dirty set and the driver clause for clause and proves it: the dirty set is sound and is
the LEAST set closed under "reads" (`dirty_sound`, `dirty_least`), `evalFrom` equals `eval` as a
whole `Result` (`evalfrom_agrees`), reuse is minimal (`evalfrom_minimal`), and an unknown change is
refused with nothing evaluated (`evalfrom_unknown_refused`). `proofs/README.md`, theorem 11.

**This is ADDITIVE and the class is a gate output, not a claim.** What ships is a proof model, its
extracted oracle, three `Proofs.Oracle` cases, ladder rows and prose; the one source file touched is
`Propagation.fs`, and only its doc comment. The Phase 183 public-surface family is green with no
baseline moved, and every conformance family's verdict is unchanged.

**What a consumer should read, because the theorem has premises the driver cannot enforce.**
`evalFrom` is `eval` for an evaluator that reads other nodes ONLY through the reads its dependency
map declares, with a change set naming every node whose evaluation changed, and a `prior` that came
from `eval` over the same map. The resolver `evalNode` is handed answers for every id computed so
far, so an evaluator that reads an undeclared node runs without complaint and keeps a stale value
under `evalFrom` — `Proofs.Oracle` exhibits it on the shipped driver. That contract is now stated on
`evalFrom` itself and recorded as the ladder row `propagation-evaluator-contract`.

**What this did NOT buy, stated because the phase set out to buy it.** `sort` is not modelled: the
one fact the agreement theorem needs of it — `Order` holds no id twice — is a hypothesis, checked on
every generated graph and proved nowhere (`propagation-order-distinct`). And no behaviour changed: a
resolver restricted to declared reads, which would make the evaluator contract hold by construction,
is its own phase.

### The Query seam carries the `Deferred` envelope (Phase 198) — BREAKING: a public return type moves

**The class.** `retype`, on two members of `Fuaran.Core.Query`, and it is a Phase 183 gate output
rather than a claim. The family reported it verbatim as `Fuaran.Core.Query — retype (4 move(s))`:
two `retype` and two `additive`, one package, with the headline naming the most informative of them.
The two retyped members:

| member | was | is |
|---|---|---|
| `Query.invoke` | `resolve: Query -> Result<QueryResult, string>` → `Result<QueryResult, QueryError>` | `resolve: Query -> Deferred<QueryResult>` → `Result<Deferred<QueryResult>, QueryError>` |
| `QueryRegistry.dispatch` | the same resolver and the same return | the same move |

Additive beside them: `QueryCodec.encodeDeferredResult` / `decodeDeferredResult`, the wire codec for
the new result type, which reuses the shipped envelope encoding (`"$type"`-tagged `pending` /
`ready` / `failed`) rather than minting a second spelling of the same three cases. Nothing else
moves: `Query`, `QueryParam`, `QueryResult`, `QueryRegistry` and `QueryError` keep every field, case
and position, `validateParams` and `invocationKey` keep their signatures, and no encoded byte of a
declaration or a result changes.

**What a consumer pays.** Every caller of `Query.invoke` or `QueryRegistry.dispatch` adapts twice:
its resolver returns `Ready r` where it returned `Ok r` and `Failed m` where it returned `Error m`,
and its own match on the result handles `Ok(Ready r)` and `Ok Pending` where it handled `Ok r`.
Refusals are unchanged — every `QueryError` a caller matches today arrives exactly as it did,
including a resolver failure, which is still `ExecutionFailed(m, [])`. A caller with no use for
`Pending` writes one line: `Deferred.toResult` projects the envelope back to a `Result`, which is
what the envelope has shipped for since Phase 32.

**Why the seam gains an axis rather than a wrapper.** The boundary previously had no way to say *not
yet*. A fetch in flight had to be reported as a failure, or the host had to invent its own
three-state shape above the seam and wrap every call in it — which is the drift a seam exists to
prevent, and this repository's own comment on the seam recorded it as the expected practice. The
envelope for exactly this has been in `Fuaran.Core.Function` since Phase 32 and the seam did not use
it. So the async axis now rides `Deferred` and the error axis stays typed, which is the split that
lets both be true at once.

**The fourth case is unreachable, and that is a certified law rather than a comment.** `Deferred`'s
failure is a rendered `string` by deliberate design — one type parameter, serialisable — so letting
a resolver's `Failed` ride out of the seam would have traded the enumerated `QueryError` for a
message. It does not: `invoke` projects `Failed m` into `ExecutionFailed(m, [])`, so a dispatch has
exactly three outcomes — SETTLED `Ok(Ready r)`, PENDING `Ok Pending`, REFUSED `Error e` — and
`Ok(Failed _)` cannot occur. `Conformance.queryLaws` gained three laws for it: the
`Deferred<QueryResult>` wire round-trip over all three cases, the three-outcome property (with the
refusal shown to precede the resolver), and the typed-failure invariant. Its four existing laws are
unchanged in verdict, and every other family is untouched.

**What this deliberately did NOT do, stated because the phase's own brief asked for it.** It did not
change `Capability`. `Capability.invoke` returns a plain `Result<'v, InvokeError>` and still does, so
the two seams are NOT symmetric after this change — `Query` is the first to carry the envelope, not
the second. The phase was framed as reconciling a disagreement between the seams, and the
disagreement measured the other way: both returned a plain `Result`, and neither carried the
envelope. Giving `Capability` the same shape retypes a surface Phase 177 models and proves, so the
model would move in the same change-set; that is its own phase. It also did not put a handle on
`Pending`: that is a union-widening on a type this repository's codec, `deferredLaws` and every
adopting host already read, and a pending fetch is correlated by `invocationKey`, which is a function
of the declaration and the validated arguments alone.

### Arbitration leaves `AiSurface` for the op layer (Phase 192) — BREAKING: a public function and its types change package

`arbitrate` — which subset of N op-script proposals can land together against one base tree — is now
`Arbitration.arbitrate` in **`Fuaran.Core.Ops`**. It was `AiSurface.arbitrate` in
`Fuaran.Core.AiSurface`. `ArbitrationRejection<'Id>` and `Arbitration<'Node,'Id>` move with it, and
the proposal the partition takes is a new minimal record, `OpScriptProposal<'Node,'Id>` =
`{ Id: int; Holder: string; Ops: SkeletonOp list }`.

**Why it moved.** `Fuaran.Core.AiSurface` is the seam a model is driven through: read tools, the op
catalogue, the pattern bank, the proposal gate. Arbitration is not one of those — it is the other end
of `Ops.footprint` and `Ops.independent`, the concurrency half of the tree algebra, and its callers
are schedulers deciding what may land together rather than orchestrators driving a model. It was
declared in `AiSurface` because the proposal type was, which is a reason about where a record sat and
not about what the function is. A reader looking for concurrency semantics now finds them in `Ops`.

**What adopting it costs, exactly.** Callers of `AiSurface.arbitrate` move to
`Arbitration.arbitrate` over an `OpScriptProposal` list. Under `open Fuaran.Core` — which every
consumer already has, since both packages declare into that one namespace — the two type names
resolve unchanged, so a caller that only pattern-matches `Inapplicable` / `Conflicts` or reads
`Accepted` / `MergedScript` / `Rejected` needs no edit there. Two changes are real: the module
qualifier on the call, and the proposal value. A caller holding an `AiSurface.Proposals` queue gets
the second for free — `Proposals.toOpScript` is the projection, one call per proposal — and a caller
that was constructing the Phase-59 record only to satisfy `arbitrate` now constructs three fields
instead of six. The `Fuaran.Core.Ops` package reference is new only for a consumer that had
`AiSurface` without it, which is none: `AiSurface` has always depended on `Ops`.

There are **two** consumers of `arbitrate` outside this repository — both coordination-layer
components — and each repins for this release. A caller with no arbitration call site is
unaffected: nothing else in either package moved.

**Why it rides this draft rather than advancing `<Version>`.** The draft already carries a BREAKING
entry (Phase 198's retyped `Query` return). The Phase 183 classifier reports this change as

```
Fuaran.Core.AiSurface — removal (15 move(s))
Fuaran.Core.Ops — additive (20 move(s))
```

and `removal` is not a HIGHER class than `retype`: the classifier's own ranking exists to say which
word tells a reader most about what the author did, not to rank severity — the five non-additive
classes all stop a pinned consumer compiling. So the number already says "breaking", which is the
true thing to tell a consumer about adopting it, and `0.27.0` admits this entry. Pre-1.0 breaking is
a MINOR bump, per the `0.19.0` / `0.20.0` precedent above.

**The partition is unchanged, and that was measured rather than asserted.** The body moved verbatim.
Before the old function was deleted, both implementations ran over the same 300 generated proposal
sets from the law kit's own generators — `arbitrationLaws`' seed, generator and corruption rate —
comparing the accepted ids with their holders, the merged script and every rejection reason: 300 sets,
339 accepted and 729 rejected proposals, **zero disagreements**. The comparison was shown able to go
red before the green was believed: the same differential against an inverted pinned order disagreed
on 282 of the 300 sets.

**`Conformance.arbitrationLaws` did not move** — same name, same signature, same six laws, same
verdicts. Its baseline line in `api/Fuaran.Core.Conformance.txt` is byte-identical: the family builds
its proposals inside its own body, so only that body was re-pointed. A domain certifying arbitration
re-pins and runs exactly what it ran before, with no source change.

**What did NOT change:** `AiSurface` keeps its name, the `*.AiTools` vocabulary it is named for, and
the frozen `AiSurfaceWitness`; `Proposals` keeps its queue, its approval flow and its guidance
rendering; nothing about what `arbitrate` decides, in what order, or with what honesty boundary
moved. This is a change of address.

### The relocation-kind footprint widening was MEASURED AND DECLINED (Phase 163) — NO surface moves

`Footprint`, `Ops.footprint` and `Ops.independent` are **byte-for-byte unchanged**, and this entry
exists so that nobody reads their stillness as an oversight. Phase 143 proved a precision ceiling
(`proofs/TreeOps.fst` section 18, `relocation_clause_is_necessary`): the pinned unknown-parent clause
cannot be tightened over the record as it stands, because a `MoveNode` and a remove-shaped `Batch`
present byte-identical footprints while only the move commutes with a structural write inside the
relocated subtree. Its named remedy was a wider record — one carrying the relocation's KIND and, for
a move, its target. Phase 163 measured what that would buy before building it, and the answer is
nothing: **an upper bound of 0 freeable pairs over 20,649,689 concurrent op pairs** in the recorded
op-stream ledgers available to measure. The instrument, its falsifier and its go-red self-test are
`proofs/kit/measure-relocation-halts.ps1` (+ `.tests.ps1`); the reasoning is
[`DECISIONS.md`](DECISIONS.md) D47.

**So no consumer has anything to adopt, and that is the whole of the consumer-facing news.** The
widening would have been breaking — the record is mirrored by hand in F\* (`proofs/DagFold.fst`
declares its own `footprint`, with `independent` re-implemented clause-for-clause) and its four
fields are pinned WITH THEIR ORDINALS in `api/Fuaran.Core.Ops.txt`, so a field added anywhere but
last moves the baseline. Declining it leaves every projection over this record (`'Op -> Footprint`,
the shape a scheduling or fold consumer supplies) valid exactly as written.
### An evaluator reads only what it declared (Phase 209) — BREAKING: a union case is added, and the driver refuses a non-conforming evaluator

**The class.** `union-widening`, on `Fuaran.Core.Propagation`, and it is a Phase 183 gate output
rather than a claim. The family reported it verbatim as `Fuaran.Core.Propagation — union-widening
(3 move(s))`:

```
additive           + field Fuaran.Core.Propagation+PropagationError+Tags.EvalUndeclaredRead : System.Int32 (literal)
additive           + type Fuaran.Core.Propagation+PropagationError+EvalUndeclaredRead (type)
union-widening     + union-case Fuaran.Core.Propagation+PropagationError.NewEvalUndeclaredRead #2(System.String, System.String)
```

No signature moves. `eval`, `evalFrom`, `dependencyMap`, `sort`, `dirtyFromChangedIds`, `staleSet`,
`touchedBy`, `dirtyFromOp`, `cycleThrough`, `dependents`, `TopoResult` and `EvalOutcome` keep every
parameter, field and position; `PropagationError` keeps both of its existing cases at their existing
tags, and the new case is declared LAST because a case's declaration order IS its tag number — the
finding Phases 207 and 202 each recorded. It rides the `0.27.0` draft rather than advancing it: the
slot already carries a breaking `retype` from Phase 198, and this is not a higher class than that.

**And the BEHAVIOUR changes, which is the part a consumer must read rather than the surface.** The
resolver `walk` hands `evalNode` used to answer for EVERY id computed so far. It now answers for
`deps[id]` and for nothing else, and a read outside that set ends the evaluation with
`EvalUndeclaredRead(node, read)`.

So an evaluator that read a node it never declared in the dependency map used to run without
complaint and is now refused as data. **Say plainly what such an evaluator already was:** wrong under
`evalFrom` and order-dependent under `eval`. Under `evalFrom` its node was not in the dirty cone of
the undeclared read, so it was not recomputed when that node changed and kept a STALE value where
`eval` computed a fresh one — silently, with both calls returning `Ok`. Under `eval` it saw the
undeclared node's value or `None` depending only on where the topological order happened to place
it. This change does not take a working program away; it replaces two silent wrong answers with one
named one, at the read, before the day an upstream edit lands outside the cone.

**What a consumer pays.** Three things, and the first is the only one most callers meet:

| | |
|---|---|
| a `match` over `PropagationError` | gains a case. Exhaustive matches stop compiling (`FS0025` as a warning, an error under warnings-as-errors); a consumer holding a STALE same-version pack gets an `InvalidCastException` at run time with no compile signal at all, which is why the slot is a draft and not a repack |
| an evaluator that reads outside its declaration | is refused. Declare the read in the dependency map — the domain still declares its reads, `dependencyMap` is unchanged — or stop reading it |
| nothing else | a declared read behaves exactly as before, including one that resolves to `None`: a dangling reference, a cyclic upstream and a not-yet-reached read are all still answered, and still `None` |

**Why the refusal is not `None`.** `None` already means "declared, and absent or failed upstream",
which a domain propagates as a value of its own — a Calc model renders it `#CALC!`. Answering an
undeclared read with `None` would report a contract violation in the vocabulary of a legitimate
missing value, so it is a typed refusal instead. The one-set-lookup cost per read is the whole of
the runtime price.

**Where the refusal is observable, stated because the enforcement has a boundary.** `evalFrom`
invokes `evalNode` only on the nodes it recomputes — the dirty set, plus any node absent from
`prior` — so a violating node that is clean AND present in `prior` is reused without being
re-invoked and its violation is not seen there. That is not a hole: `evalFrom`'s contract says
`prior` came from `eval` over the same `deps`, and `eval` recomputes everything, so such a `prior`
cannot exist. Prime with `eval` and the violation is found before there is a `prior` to reuse.
`PropagationContractTests` asserts both halves rather than leaving them to be composed.

**What this bought on the proof side.** Phase 186 proved `evalFrom` equal to `eval` under four
premises, the first of which — the evaluator reads other nodes only through its declared reads — was
recorded as the ladder row `propagation-evaluator-contract`, a `premise` that production stated in
prose and enforced nowhere. That row is now a **proved** row: the model's resolver is restricted
identically, `evalfrom_agrees` no longer carries the hypothesis, `local` is deleted from
`proofs/Propagation.fst` rather than left as a hypothesis nobody supplies, and the refusal itself is
proved (`undeclared_refused`, with `eval_refuses_undeclared` / `evalfrom_refuses_undeclared` and
`ok_implies_declared`). `Conformance.propagationEvalLaws` gained a fourth law — both drivers refuse
an undeclared read identically, naming the same node and read — and a second adequacy dimension, so
its reported law list grew from 4 entries to 6; a consumer asserting a law count or indexing
positionally into `ConformanceReport.Results` adjusts, one reading `AllPassed` or matching on `Law`
does not. Every other law family, and the propagation differential's whole generated pool, is
unchanged in verdict, which is the point: a conforming evaluator cannot tell this change happened.

**What this did NOT do.** It did not infer the dependency map from the evaluator — the domain still
declares its reads. It did not touch `dirtyFromChangedIds` or `sort`. And it did not close the rest
of the evaluator contract: a complete change set and a `prior` that came from `eval` over the same
map remain the domain's, now recorded as `propagation-change-set-and-prior`, and closing them wants
the other candidate Phase 186 named — a law family generic over a DOMAIN'S evaluator — which is
still not taken. The model also gained one bridge in exchange, `propagation-read-witness`: production
detects the violation by instrumenting its resolver, a pure model cannot observe a call, so the
observed read set is a parameter the differential supplies.

### Top-N is maintainable (Phase 207) — BREAKING: a case added to `StepIncrementality`

`Incremental.plan` declined every pipeline carrying a `Limit`, as `FallBack (StepNotRowLocal
"limit")`. It no longer does. A `Limit` whose count and offset are literals is classified
**`StepIncrementality.TruncateOrder (n, offset)`**, at any position, over any order the restricted
walk produced — so `Filter > Sort > Limit`, `Filter > Project > Limit`, a bare `Limit`, and a
`Limit` feeding a maintained `GroupBy` are all restricted rather than declined.

**The class is the gate's output, quoted rather than argued:**

```
Fuaran.Core.DataFrame — union-widening (3 move(s))
  additive           + field Fuaran.Core.StepIncrementality+Tags.TruncateOrder : System.Int32 (literal)
  additive           + type Fuaran.Core.StepIncrementality+TruncateOrder (type)
  union-widening     + union-case Fuaran.Core.StepIncrementality.NewTruncateOrder #6(System.Int32, System.Int32)
```

It read `union-widening (4 move(s))` on the first cut, the fourth being a `retype` of
`StepIncrementality.NewFallBack` from tag `#5` to `#6`: the new case had been declared where it
belongs thematically, among the admitted classes, and a union case's declaration order **is** its
tag. That renumbering cost a consumer a second breakage on a case that had not changed, so the case
is declared last instead — after `FallBack`, appended in arrival order exactly as `FallBackReason`'s
own cases are. The reason is recorded on the case itself, because the next author to tidy the
declaration order will otherwise put it back.

**What a pinned consumer pays, exactly.** `StepIncrementality` is a published union, so **every
exhaustive `match` over it gains an arm** — `FS0025` at compile time against a rebuilt package, and,
against a stale same-version pack, an `InvalidCastException` at run time with no compile signal at
all. A `match` with a wildcard arm is unaffected. Nothing was removed, no signature moved, and
`IncrementalStrategy` did **not** gain a case: a top-N pipeline reports `RowLocal` (or
`RowLocalThenGroups` behind a maintained group) exactly as a sort-bearing one does, so
`isIncremental`, the strategy dispatch and every law that keys off `ReferenceOnly`-versus-not are
untouched. In this repository the cost of the widening measured **zero matches extended** — every
in-repo reader of the type already carried a wildcard — which is evidence about the shape of the
type's use rather than a promise about any consumer's.

**One decline moved reason.** A `Limit` whose count or offset is still a `Slot.Param` continues to
decline, and now does so as `UnresolvedSlotParam ("limit", <param>)` rather than `StepNotRowLocal
"limit"` — the `0.23.0` rule a `Sort` on a param key already follows. A consumer matching on the old
pairing sees the new one. The reason is the accurate one: the window is not known without an env,
and substituting the params (`Transform.substitute`) makes the plan computable again, which the old
reason denied.

**There is no second decline class, and that is a finding rather than an omission.** The shape this
was designed to have — admit a `Limit` only where the order it reads is one the seam maintains,
decline the rest by type — describes a distinction the walk cannot draw. Every step the walk admits
(`PropagateRows`, `MergeOrder`, `RecomputeFrame`, `FilterByRelation`) preserves the reference's row
set *and* its order, and a step that does not declines the whole pipeline before the limit is
reached; so a `Limit` the walk reaches is over a maintained order by construction, and a predicate
saying so would be a branch that cannot be taken. This is the call `0.19.0` made for `Window` when
frame boundedness turned out not to be the admission criterion, and it is recorded the same way:
in the type's own doc comment, beside the code.

**What it buys, measured rather than claimed.** A `Limit` evaluates no expression, so it costs
nothing on the footprint's scale in either path; what the admission buys is that the steps *before*
it stop re-evaluating every row. On one machine at 20,000 rows with one row edited, over
`Filter > Sort > Limit 10`: with a 129-node row expression the refresh is **87 ms against the full
evaluation's 245 ms**; with a single `Ge` comparison it is **89 ms against 56 ms** and the seam
still loses. That is Phase 206's finding holding for this shape — the seam's proposition is about
the size of the ROW EXPRESSION, not the size of the table — with one difference worth knowing: a
top-N full evaluation sorts the whole frame every tick while the refresh merges into an order it
already holds, so the trivial-predicate gap narrows from 3.4× against the seam to 1.6×. It does not
close. `docs/incremental-evaluation.md` carries both tables.

**The maintenance adds no third quadratic class**, and that is asserted rather than asserted-about:
the `Scaling` family counts the step's element visits at both sizes and pins the shipped shape at
**exactly linear** (20.00 over a twentyfold span) beside the obvious index-lookup shape at **398.86**
— a go-red proof that is exact, clock-free and identical on every host, which the timed form of the
same comparison was not. The whole top-N refresh is also timed, at a ratio of 49 against the
family's bound of 100.

### Steps after a group-by (Phase 202) — BREAKING: a case added to `FallBackReason`, a field added to `IncrementalEval`

`Incremental.plan` declined every pipeline whose `GroupBy` was not its last step, as `FallBack
(AggregateStepNotLast "groupBy")`. It no longer does. A `GroupBy` is maintained at **any** position
and the steps after it are restricted too, so a `Having` — a `Filter` after a `GroupBy`, there being
no `Having` verb — and a sort, a derive, a projection or a top-N over the GROUP table are all
refreshed rather than declined.

**The class is the gate's output, quoted rather than argued:**

```
Fuaran.Core.DataFrame — record-widening (5 move(s))
  retype             ctor Fuaran.Core.IncrementalEval..ctor(… , Fuaran.Core.RecomputeFootprint)
                  -> ctor Fuaran.Core.IncrementalEval..ctor(… , Fuaran.Core.RecomputeFootprint,
                                                            FSharpMap`2<String, FSharpList`1<Cell>>)
  additive           + field Fuaran.Core.FallBackReason+Tags.AggregateStepRepeated : System.Int32 (literal)
  record-widening    + record-field Fuaran.Core.IncrementalEval.GroupCells #13 : FSharpMap`2<String, FSharpList`1<Cell>>
  additive           + type Fuaran.Core.FallBackReason+AggregateStepRepeated (type)
  union-widening     + union-case Fuaran.Core.FallBackReason.NewAggregateStepRepeated #11(System.String)
```

It read `record-widening (8 move(s))` on the first cut, the three extra being `retype`s of
`SortOrders`, `JoinKeys` and `Footprint` — the new field had been declared where it belongs
thematically, beside `GroupAggs`, and a record field's **position** is part of the published surface
exactly as a union case's is. That shift cost a consumer three reported breakages on fields that had
not changed, and it would have moved the structural-comparison precedence of a type whose ordering
nothing here intends to move. **This is Phase 207's union lesson with a record twin**, found the same
way — by reading the classifier rather than by arguing about it — so the field is appended last and
the reason is recorded on the field itself.

**What a pinned consumer pays, exactly.** Two things, and they bite differently. `FallBackReason` is
a published union, so **every exhaustive `match` over it gains an arm** — `FS0025` against a rebuilt
package, and against a stale same-version pack an `InvalidCastException` at run time with no compile
signal. `IncrementalEval` is a published record, so **every full-literal construction of one stops
compiling** (`FS0764`) and the primary constructor widens by one parameter. Copy-and-update
(`{ state with … }`), field reads and pattern matches on other types are unaffected. Nothing was
removed and no existing signature moved. `StepIncrementality` and `IncrementalStrategy` gained
**nothing**: a tail step classifies as the same `PropagateRows` / `MergeOrder` / `RecomputeFrame` /
`TruncateOrder` / `FilterByRelation` it would before the group-by, and a tail-bearing pipeline
reports `RowLocalThenGroups` exactly as a group-by-last one does — so `isIncremental`, the strategy
dispatch and every law keyed off `ReferenceOnly`-versus-not are untouched.

**`AggregateStepNotLast` is RETAINED and no longer produced by any plan.** Removing a case from a
published union breaks every consumer that matches on it, and a footprint stored under `0.26.1` still
has to read, so `reasonString` still renders it. It joins `WindowFrameUnbounded` (`0.19.0`) as the
second reason kept for that purpose; the type's doc comment says so on the case.

**The new decline is a SECOND aggregating step**, `AggregateStepRepeated ("groupBy")`: grouping the
group table needs a second level of row-to-group, ordered-membership and per-group aggregate state.
It refuses as data, never by a silent full re-evaluation — a declined pipeline still answers through
the reference evaluator with the reason in its footprint. The case is declared **last** in
`FallBackReason`, after `JoinNotRowPreserving`, for the tag-order reason Phase 207 records above.

**The decomposable-aggregate decline class does not exist, and that is a finding rather than an
omission.** The shape this was designed to have — maintain the decomposable aggregates (`sum`,
`count`, `mean` via sum+count, `min`/`max` with a tombstone re-scan), decline the rest — describes a
*running-accumulator* maintenance model the seam does not use. `groupStep` recomputes an affected
group **from that group's own member rows**, through the reference evaluator's own aggregator, so
there is no accumulator to repair, no tombstone, and no decomposability requirement: **all ten
`AggFn` cases are maintained**, `Median` and `StdDev` included. A deletion from a `min`/`max` group
is not a special case, because the group is simply re-aggregated over the members it has left. What
that trades is stated in the doc rather than hidden: a group with a thousand members costs a
thousand-row aggregation when one of them moves, where a running `sum` would cost an addition. This
is the same class of finding as the absent second decline above, and it is recorded the same way —
in `docs/incremental-evaluation.md` beside the aggregate list, and as an enumerating assertion in
`IncrementalGroupByTests` that goes red if it stops being true.

**What it buys, measured rather than claimed.** The tail's own work is charged by the GROUP count,
which is small and bounded, so the admission does not move the row-expression threshold at all. On
one machine at 20,000 rows with one row edited, over `Filter > GroupBy > Filter`: with a 129-node row
expression the refresh is **61 ms against the full evaluation's 211 ms**; with a single `Ge`
comparison it is **61 ms against 20 ms** and the seam loses by 3.1×. The refresh's cost is the same
figure in both rows — it does not move with the expression at all — which is Phase 206's diagnosis
confirmed a third time: what the refresh pays is per-source-row string-keyed bookkeeping, and what it
saves is `n−1` evaluations of the row expression. Three admissions have now been measured on this
axis and all three answer the same way. `docs/incremental-evaluation.md` carries all three tables.

**The maintenance adds no third quadratic class.** The `Scaling` family times the group-tail refresh
at a ratio of **49 over a twentyfold span**, against the family's bound of 100 and a linear
expectation of 20 — the same band as Phase 207's top-N (46) and the plain refresh (28), and four
times clear of the quadratic (400) the family exists to refuse.

### The capability seam carries the `Deferred` envelope (Phase 210) — BREAKING: a host body's type and three public return types move

**The class.** `retype`, and it is a Phase 183 gate output rather than a claim. The family reported it
verbatim as `Fuaran.Core.Function — retype (3 move(s))`. **Three** members, not the two the phase was
framed around, and the third is the part to read before planning an adoption:

| member | the body was | the body is | the return was | the return is |
|---|---|---|---|---|
| `Capability.invoke` | `unit -> Result<'v, string>` | `unit -> Deferred<'v>` | `Result<'v, InvokeError>` | `Result<Deferred<'v>, InvokeError>` |
| `Registry.dispatch` | `Capability -> unit -> Result<'v, string>` | `Capability -> unit -> Deferred<'v>` | the same move | the same move |
| `FunctionRegistry.dispatch` | `FunctionEntry -> unit -> Result<'v, string>` | `FunctionEntry -> unit -> Deferred<'v>` | the same move | the same move |

`FunctionRegistry.dispatch` moved because it IS `Capability.invoke` — it resolves an id and delegates,
deliberately holding no parallel dispatch path — and once `invoke` answers in the envelope there is no
TOTAL way to project back: `Pending` has no `InvokeError` case, and minting one would widen a
published union precisely to avoid carrying the envelope. A consumer of the signature-typed registry
therefore pays this change too, whether or not it uses the capability registry.

**Nothing else moves, and one thing that looks additive is not.** `BodyFailed of reason: string` was
already `InvokeError`'s seventh and last case, so the projection reuses it and **no union case, record
field or declaration position changes anywhere** — `Capability`, `InvokeError`, `Deferred`,
`CapabilityRegistry`, `FunctionRegistry` and `FunctionEntry` keep every field, case and position.
`validateArgs`, `invocationKey`, `determinismTag`, `register`, `tryFind`, `enumerate`,
`findBySignature`, `partiallyApply` and the whole `CapabilityCodec` keep their signatures, and no
encoded byte of a declaration or an invocation record changes.

**What a consumer pays.** Every capability body returns `Deferred` — a synchronous body wraps its
value in `Ready v` where it returned `Ok v`, and answers `Failed m` where it returned `Error m` — and
every caller of `invoke` / `dispatch` handles the three outcomes: SETTLED `Ok(Ready v)`, PENDING
`Ok Pending`, REFUSED `Error e`. Refusals are unchanged: every `InvokeError` a caller matches today
arrives exactly as it did, a body failure included, which is still `BodyFailed m`. A caller with no
use for `Pending` writes one line — `Result.map Deferred.toResult`, or `Deferred.toResult` on the
accepted value — which is what the envelope has shipped for since Phase 32. The in-repo adoption cost
is 13 call sites and 8 bodies across the law kit and the tests; an adopter's is its own count of the
same two kinds.

**Why the envelope rather than a wrapper, and why now.** `Deferred`'s own declaration in this package
calls it "a domain-general async-result envelope **for a capability invocation**", put in the
substrate so every host gets the async-invocation envelope from Core — and until this change the
capability seam was the one seam in the repository that did not use it, while `Placement` routed a
body to a `Server` or a `ClientIsland`, `DeterminismSource` admitted `Network`, and the seam's own
header offered it to model inference. A body that cannot settle synchronously had to be reported as a
failure, or wrapped in a three-state shape the host invented above the seam — the drift a seam exists
to prevent. So the ASYNC axis rides `Deferred` and the ERROR axis stays typed on the outer `Result`,
which is the split that lets both be true at once, and it is now the SAME split on both seams a host
adopts (Phase 198 made it on `Query`). **That supersedes Phase 198's "What this deliberately did NOT
do" paragraph above**, which recorded the asymmetry as the state this repository was leaving behind:
the two seams are symmetric from this entry onward.

**The fourth case is unreachable, and it is proved rather than asserted.** Letting a body's `Failed`
ride out inside an `Ok` would have traded the enumerated `InvokeError` for a rendered message, so
`invoke` projects `Failed m` into `BodyFailed m`: `Ok(Failed _)` cannot occur.
`proofs/Capability.fst` carries that as a theorem over every body and every argument set
(`invoke_never_ok_failed`, and `dispatch_never_ok_failed` through the registry, ladder row
`capability-envelope-three-outcomes`), and Phase 177's four capability theorems were re-DISCHARGED
over the new clause — each holds verbatim, because every one of them is about what happens before the
body is consulted or about body-independence, and the envelope sits on the body's answer.
`Conformance.capabilityLaws` gained three laws for the shipped seam: the envelope riding out of
`invoke` and `dispatch` unchanged on `Ready` / `Pending`, the three-outcome property with the refusal
shown to precede the body, and the typed-failure invariant. Its reported law list grows from 4 entries
to 7, so a consumer asserting a law count or indexing positionally into the results adjusts; one
reading `AllPassed` or matching on `Law` does not. Its four existing laws, and every other family, are
unchanged in verdict.

**What this deliberately did NOT do.** It did not put a handle on `Pending` or a typed error on
`Failed`: `Deferred<'T> = Pending | Ready of 'T | Failed of message: string` is untouched, because
widening it would move a type this repository's codec, `deferredLaws` and every adopting host already
read, and a pending invocation is correlated by `invocationKey`, a function of the declaration and the
validated arguments alone. It did not touch `CapabilityPipeline.eval`, whose per-node body is a fourth
plain-`Result` body on this package: it does not go through `Capability.invoke`, and threading a
`Pending` through a fold needs a resumption model rather than a retype — a design question, recorded
rather than answered. And it moved no version: this rides the `0.27.0` draft, whose entries already
carry a breaking class.

### The restricted refresh pays for the delta (Phase 208) — BREAKING: `IncrementalEval`'s representation becomes private

**What a consumer pays, in one sentence: every read of a field on an `IncrementalEval` becomes a call
to an accessor, and a state value cannot be constructed or copied with `{ … with … }` any more.** The
substitutions are mechanical and total:

| was | is |
|---|---|
| `state.Output` | `Incremental.result state` |
| `state.Footprint` | `Incremental.footprint state` |
| `state.Plan` | `Incremental.plan' state` |
| `state.Plan.Strategy` | `Incremental.strategy state` |
| `state.Source` | `Incremental.source state` |
| `state.Pipeline` | `Incremental.pipelineOf state` |
| `state.RowCells` / `.RowGroup` / `.GroupMembers` / `.GroupAggs` / `.GroupCells` / `.SortOrders` / `.JoinKeys` / `.Env` / `.Scheme` | no replacement — engine-owned caches, see below |

**There is no wire form and nothing to migrate.** An `IncrementalEval` is in-memory state a consumer
holds between refreshes; it has never had an encoder, a decoder or a persisted shape. A state value
carried across a version boundary in the same process does not arise either, because the type's
identity is the assembly's. What a consumer holding a state from an earlier version has is a value it
can no longer read the fields of — a compile error at every such read, which is the whole of the
adoption cost — and the remedy where a state genuinely cannot be re-primed does not exist and does not
need to: losing a state costs one `prime`, never a wrong answer.

**The nine cache fields have no accessor, deliberately.** They were public because the columnar strand
keeps its data transparent, and the doc comment always said they were engine-owned — a hand-built
state whose caches disagree with its source is a lie the evaluator cannot detect. What publishing them
actually bought was that every change to HOW they are keyed was a breaking change, and three phases in
a row paid for it: 206, 207 and 202 each measured the refresh losing to the full evaluation for a cheap
row expression, each diagnosed the same cause — per-source-row string-keyed persistent-map bookkeeping
— and each left the keying as it found it, because moving it was not theirs to spend. 202 measured
that even a field's POSITION in the record is published surface. The representation is private now, so
the next re-keying is a patch. If a consumer turns out to need something a cache field answered, the
remedy is an accessor for the QUESTION rather than the field, and an entry in this document; the
adoption measured in this repository found no such reader — every in-repo consumer read `Output` or
`Footprint` and nothing else.

**What the re-keying bought, measured on one machine at 20,000 rows with one row edited** — same
estimator as the three entries above, and the "before" column is the code those entries shipped:

| pipeline, one `Ge` comparison per row | refresh before | refresh after | full evaluation | |
|---|---|---|---|---|
| `Filter > GroupBy` | 72.3 ms | **13.0 ms** | 26.3 ms | wins by 2.0× |
| `Filter > Sort > Limit 10` | 102.5 ms | **25.8 ms** | 55.7 ms | wins by 2.2× |
| `Filter > GroupBy > Filter` | 69.8 ms | **10.4 ms** | 16.7 ms | wins by 1.6× |

The `Scaling` family now ASSERTS the trivial-predicate case on all three pipelines — the case 206, 207
and 202 each printed and deliberately did not assert — so the claim is a gate rather than a table. The
profile that preceded the change, and the one that follows it, are both in
`docs/incremental-evaluation.md`; the short version is that the caches are positional arrays indexed
by the row's slot now, a group identity is carried rather than re-minted for a row whose cells have not
moved, and the partition is accumulated in mutable locals that never leave the function.

**A correctness defect was found and fixed in the same change, and it is the more important half.** The
per-row cache was reused for any row "the delta did not name". That is not the same statement as "this
row's cells have not moved", and a `Window` is where the two part: a window recomputes its column over
the whole frame it is handed, so a row the delta never named comes out of it with a different cell
whenever another row in its partition moved. A step after the window then reused an answer to a question
that had changed. Measured on the shipped `0.26.0` code, on ten rows with one edited:
`Filter > Window(cumulSum) > Filter(on the window column)` and the same with a `Derive` both
**disagreed with the reference evaluator** — the one thing this seam promises never to do. Both are
admitted pipelines (`RowLocal`), so this was reachable, not hypothetical; the conformance corpus did not
generate a step reading a window's output column, which is why no law caught it. The cache condition is
now "this row's cells are byte-identical to the ones the prior evaluation held for it", which a window
clears for every row, and `IncrementalRefreshCostTests` holds all three shapes as cases. **Consequence
for a consumer: a pipeline with a `Window` followed by a `Filter`, `Derive` or maintained `GroupBy` will
report a LARGER footprint after this version than before it** — more rows re-evaluated and more groups
recomputed — because it was previously reporting a smaller one and a wrong answer. Every other pipeline's
footprint is unchanged.

**No conformance verdict moves.** `IncrementalDelta.laws` is unchanged in verdict over the whole
corpus, top-N and group-tail pipelines included, and so are `incrementalLaws`, `footprintLaws` and
`dirtyPropagationLaws`; no law family's count moves and no law vector file changes content. The whole
suite is green at 1,325 cases.

**Why this rides `0.27.0` rather than advancing it.** The draft already carries breaking entries —
Phase 198 retyped a public return, 192 moved a public function between packages, 209 and 207 each added
a union case — so the number already tells a consumer that adopting this slot costs source changes. A
removal is the same class of cost and not a higher one.

## 0.26.0 — released 2026-09-17 as `v0.26.0`

**This slot is RELEASED.** `<Version>` reads `0.26.0` and the repository holds the `v0.26.0` tag, so
this is a released contract rather than a draft and nothing further can ride it: the next
public-contract change opens a `0.27.0` slot and advances `<Version>` with it. The entries below are
what shipped in it. _(The draft-slot preamble this replaces still denied the tag after it was cut —
found by Phase 199's own check, which fails a sentence that outlives the tag it denies, and retired
here because Phase 183's gate cannot run on a red suite.)_

### The artifact ALWAYS carries its `harden` block (Phase 179) — ADDITIVE on the wire and on the API

`Artifact.render` emits the `harden` block for every policy value, `HardenPolicy.Default` included.
Until now it omitted the block exactly at the default, so a freshly rendered artifact declared its
hardening vocabulary only when that vocabulary was unusual.

**Why this is a step of its own rather than part of the retirement it begins.** The default was a
WIRE fact, not only a source one: the block's ABSENCE MEANT one domain's four tokens, by a promise
`Artifact.readHarden` makes in its own doc comment to every artifact written before those tokens
were declarable — and both published `idl.json` artifacts, the UI tier's own and the
shared cross-host corpus, carry no block at all. Phase 178 measured that before flipping and
refused ([`DECISIONS.md`](DECISIONS.md) D40); this is step one of the two-step migration that
finding left in its place. Step two — an absent block meaning "declared nothing", and `Default`
retired — is Phase 180, gated on those two artifacts having been re-rendered under this one.

**The class, on both axes.** On the WIRE it is additive: an extra member on an object a reader
looks up by name, which a conformant reader tolerates (`WIRE_FORMAT.md` §2.1 rule 2), and its
presence at the default says exactly what its absence did. That last clause is MEASURED rather than
asserted — `IdlArtifactTests` runs the diff classifier over the pre-179 bytes and the post-179
bytes in both directions and requires no `HardenPolicyChanged` row, beside a falsifier that
requires one for a policy that genuinely moved, so the quiet answer cannot be a classifier that
reports nothing. On the API it is additive too: no signature moved, and `readHarden` is
deliberately unchanged, so a consumer that never re-renders sees nothing at all.

**What a consumer does about it: nothing is required.** Re-rendering a vocabulary's artifact adds
the block and changes no other byte — the three committed classify fixtures in this repo are the
worked instance, each a pure insertion. A consumer that pins artifact bytes in a test regenerates
them; one that merely reads artifacts is untouched, in either direction, because the reader's
answer for an absent block has not moved.

### `Fuaran.Core.Families` — the law-family roster, exported by the kit (Phase 184) — ADDITIVE

**New public surface: the module `Fuaran.Core.Families`**, in `Fuaran.Core.Conformance`. It declares
one `LawFamily` record per law family the kit ships — `Id` (`"<Module>.<Entry>"`), `Module`, `Entry`,
`Witness` (the witness and generator types the entry point takes, in parameter order), `OptIn` (it is
not folded into `certify` / `certifyStream`) and `Discharges` (the `proofs.json` obligation rows a
green run of it discharges) — plus `families`, `ids`, `modules`, `tryFind`, `obligations`, `toJson`
and `toMarkdown`. Purely additive: no existing type, signature or law moved, and a consumer that
never opens the module is unaffected.

**What it is for.** The kit shipped its families and enumerated them nowhere, so three readers each
kept a list derived by a rule that could miss one, and a missing row in any of them is silent by
construction: every check quantifies over the list, so the one thing a list cannot notice is a family
nobody added to it. Downstream tooling phase #476's join of the proofs registry to the laws census surfaced the
instance — `Conformance.opAlgebra`, the law the `tree-algebra-well-formed-states` obligation names, was
absent from the roster every consumer's conformance census quantifies over, so every consumer reported
it unrostered and none could ever mark it adopted.

**The roster is held to reflection over the shipped assembly BY RETURN TYPE** — a law family is a
public static entry point answering with `LawResult list`, which is what a family IS — rather than by
the naming convention that preceded it. That correction is the substance: the old completeness check
reflected over method names ending in `Laws` / `LawsWith`, and **three** families are not spelled that
way. `opAlgebra` and `reducer` are two of the five families `Conformance.certify` and
`Conformance.certifyStream` are built from, and `compositionPilot` is the third.

**Consequently `SampleAdequacy.census` gains three rows** — `Conformance.opAlgebra`,
`Conformance.reducer` and `Conformance.compositionPilot`, each `Unconditional` with its reason — and
is now held equal to the roster. `census` is public and this widens its value; nothing about its type
or its existing rows changed.

**Two GENERATED artefacts, written by one command and compared by the suite:**

```
dotnet run --project tests/Fuaran.Core.Tests -- --emit-families
```

- [`docs/conformance-families.md`](docs/conformance-families.md) — the human-readable table.
- [`docs/conformance-families.json`](docs/conformance-families.json) — **the machine export, and the
  one an offline reader consumes without building or running anything.** Its shape is a contract, for
  downstream tooling phase #482, which replaces that projection's own roster with this file. **`schema` reads
  2 since Phase 194 added the `reason` member** (a string from a closed vocabulary, PRESENT ONLY for
  an opt-in family — this wire model has no null, so absence is how it spells "not applicable") — an
  addition, not a break, and the number was moved
  anyway because this repository's own suite pins it, so a shape that changed under an unmoved stamp
  would be the drift the pin exists to catch. downstream tooling phase #482 is unstarted and is the coupled
  surface: it reads whatever ships, and a reader that keys on the number reads 2.

  ```json
  {
    "kind": "fuaran.core.conformance.families",
    "schema": 2,
    "package": "Fuaran.Core.Conformance",
    "families": [
      {
        "id": "Conformance.opAlgebra",
        "module": "Conformance",
        "entry": "opAlgebra",
        "witness": ["NodeWitness", "IdWitness", "OpGen"],
        "optIn": false,
        "discharges": ["tree-algebra-well-formed-states"]
      }
    ]
  }
  ```

  `kind` and `schema` are the reader's version handle — a shape change bumps `schema`, so a reader
  can refuse rather than misread. `families` is sorted by `id` and each object writes its members in
  the order above, so the rendering is byte-stable across runs and a diff shows only what moved;
  `witness` and `discharges` are arrays and may be empty; `optIn` is a JSON boolean. Two spaces of
  indent, `\n` line endings, a trailing newline. A reader that only wants the roster reads
  `families[].id` and needs nothing else.

**One test-side narrowing worth recording, because it changes what a claim means.** The claims
ladder's `dischargedBy` vocabulary was every public static method of `Conformance`; it is now the
roster's entry points. That is the phase's title clause — an obligation cannot name a law no
conformance census enumerates, because the vocabulary IS the enumeration. It constrains
`proofs.json`, not any published API.

### A refused columnar op has no inverse (Phase 181) — CORRECTIVE, and it rides this slot

`ColumnOps.invert` is guarded by `canApply` on the pre-state. An operation the table would refuse
now yields that refusal — `Error (DuplicateColumn "a")` for a duplicate insert — where until now the
`InsertColumn` clause answered `Ok (RemoveColumn col.Name)` unconditionally, having read nothing from
the pre-state at all.

**What was wrong with the old answer.** That remove SUCCEEDS at the pre-state, and takes the column
that was already there. So an undo stack of the natural shape — record `invert op pre` beside every
op you attempt, replay the inverses to undo — lost a column the refused operation never touched, on
the one path where the forward step had done nothing. Phase 176's model found it and reported it
without fixing it (`invert_insert_reads_nothing`, `refused_insert_inverse_is_live`); this is its
phase.

**The refusal is the REFUSING rejection, not `NotInvertible`.** `invert` now answers exactly what
`apply` would have answered on a refusal, which is the tree engine's shape (`Ops.invert` returns
`canApply`'s `Rejection`). `NotInvertible` keeps one meaning — *this operation has no inverse at any
table* — and stays `AppendRows`' and `ApplyTransform`'s alone. Those two still answer
unconditionally, and they answer BEFORE the guard: `canApply (ApplyTransform p)` runs the pipeline,
and `invert` must not evaluate one to report what it already knows.

**It strengthens all four invertible clauses, not just the insert.** `SetCell` and `SetColumn` read
the pre-state for the column and the row but never for the VALUE, so a wrong-typed cell or a
wrong-length column — both of which `apply` refuses — had an inverse too. Those were harmless rather
than destructive, which is why the finding named only the insert; they are gone with it.

**The class, and what a consumer does about it.** No signature moves, no wire byte moves, no record
gains a field and no union gains a case: what changes is that a function refuses where it wrongly
answered. `apply (invert op t) (apply op t) = t` — the doc comment's defining law, and the only
promise `invert` ever made — is unchanged and still proved, because it was always about an ACCEPTED
op. A caller that checks acceptance before inverting sees nothing at all. A caller that does not now
gets a rejection instead of a live operation, which is the point. The conformance kit carries the
new clause as a law (`columnarOpLaws`' "an inverse exists only for an applicable op", with an
injectable `invert` seam so the pre-181 clause can be handed to the kit and watched to lose), and
the model proves it over all six operations, any table and any evaluator
(`invert_only_for_applicable`).
### The generated proof script's presence split, now LINEAR (Phase 182) — ADDITIVE on the API, and it ADVANCES the slot

**The class.** Additive on every published type and signature, and a BEHAVIOUR change in what one
of them emits. `Fuaran.Core.Idl.Codegen` gains nothing and loses nothing: `FStarTarget.proofsModule`
and `proofsModuleFrom` keep their signatures, `FStarTarget.presenceSplitAt` keeps its type and its
value (2). What changed is the text those functions RETURN. A constructor with `presenceSplitAt` or
more conditional members was emitted as one lemma per presence PATTERN — `rt_<T>__<Ctor>__p<bits>`,
2^k of them — and is now emitted as one LOOKUP per member: `lk_<T>__<Ctor>__<member>`, with
`__present` / `__absent` for a conditional one, each pinning only its own member, cited a member at
a time by the constructor's round-trip lemma. `2k + r'` lemmas where there were `2^k`.

**Why it advances rather than riding.** The standing `<Version>` is `0.25.0` and `v0.25.0` is
tagged, so that slot is a released contract and the draft-slot rule forbids riding it. And a
generator whose OUTPUT changes shape is a consumer-visible change even when no signature moves: a
consumer that regenerates gets a structurally different `.fst`, `presenceSplitAt` means a different
thing to a reader who sets it, and a same-version repack of the slot would hand two consumers two
emitters depending only on when their NuGet cache was last populated.

**What a consumer has to do.** Regenerate, and re-run the prover. Nothing else: the MODEL emitter is
untouched, so `Vocabulary.fst` and its siblings are byte-identical across this change, and the
theorem proved is the same theorem. A consumer that PINS emitted proof-script text — rather than
regenerating it — has a diff the size of its whole script, which is the point of the change.

**What it is measured to buy, and the limit it does not lift.** At `fuaran#1754`'s scale the emitted
script goes from 78,192,374 characters and 655,507 lines to 25,847 and 333 on the same synthetic
vocabulary. What it does NOT lift is the MODEL emitter's own exponential — the encoder's member list
is written into both arms of every conditional member's match, so at sixteen conditional members the
model is a 5.3 MB expression the prover dies loading, with no proof script involved. Exhaustive
coverage at that width therefore stays refuted, now for a reason that names the artefact responsible.
[`DECISIONS.md`](DECISIONS.md) D42 and `proofs/README.md`'s theorem 1 section carry the measurements.

### The Fable smoke's membership is DERIVED (Phase 185) — ADDITIVE, and two promises now kept

**No public surface moves.** What moves is which packages the Fable-compile gate actually covers, and
therefore which of them the "Fable-clean on encode AND decode" claim at the head of
["Fable cleanliness"](#fable-cleanliness) is entitled to be made about. `Fuaran.Core.Column.Ops` and
`Fuaran.Core.Observer` join `tests/fable-smoke`; the exclusions become
[`tests/fable-smoke/exclusions.json`](tests/fable-smoke/exclusions.json) — a JSON array of
`{ "package": "<Fuaran.Core.X>", "reason": "…", "phase": "<NN>" }`, one entry per packable project
deliberately off the surface; and `Fuaran.Core.Tests.FableSmokeCompletenessTests` holds the derived
packable set to the union of the two.

**What was actually wrong, which is not quite what the phase was filed for.** Membership was a
hand-maintained `<ProjectReference>` list beside a hand-written exclusion COMMENT, and the one thing
such a pair cannot notice is a package nobody added to either. Measured on the tree rather than taken
from the shard: **two** packable projects were uncovered, not one. `Column.Ops` is the one the phase
named — the package whose own `Description` ends "FSharp.Core only, Fable-clean", making the claim
with nothing behind it. `Idl.Cli` is the second, uncovered since Phase 127 added it and named in no
list; it is genuinely .NET-only and is now an exclusions entry. Both compile-or-excuse answers are on
the record now, and neither was before.

**And one recorded reason was FALSE, which is the other half of why this is data now.** The comment
grouped `Observer` with `Idl.Codegen` and `Idl.Spike` as "build-time tools". `Fuaran.Core.Observer` is
the RUNTIME verification seam, and its own header has said "FSharp.Core only + Fable-clean … the same
engine drives the in-memory .NET test substrate and a Fable-compiled host" since it was written. So
the exclusion was not a decision anyone had taken; it was a sentence that had stopped being true and
had no check standing over it. Gating it corrects the claim rather than recording it — the compile is
clean on the first attempt, as `Column.Ops`'s is. The completeness check runs in both directions
precisely so this class fails next time: an entry whose project has since joined the smoke reddens.

**What this means for a consumer of those two packages: a stronger promise, and nothing to do.**
`tests/fable-smoke/` is a promise surface (see "The portability set" above) — a member reached from
there carries a portability guarantee to Fable consumers. `Column.Ops`'s whole round trip
(`canApply` / `apply` / the partial `invert` / `encode` / `decode` / `streamWitness`) and `Observer`'s
engine (register / update / derive / observe / subscribe) are now reached deliberately, so a construct
that stops transpiling in either is a red gate here rather than a downstream discovery. No signature,
value or emitted byte moved in either package; both were already Fable-clean, which is the finding.

**Riding the draft rather than advancing it.** This adds no public member and breaks nothing, so it is
strictly below the change class `0.26.0` already carries.

### The package roster is DERIVED, and this document's header is held to `<Version>` (Phase 199) — DOCS + GATE, no surface moves

**The class: neither additive nor breaking — no published type, signature, byte or emitted artefact
moves.** What changes is what the two documents beside the code are permitted to say. It rides this
draft rather than advancing it for exactly that reason: a consumer has nothing to adopt.

**What was wrong, measured 2026-09-17 rather than assumed.** `README.md`'s package table listed
twelve packages while `src/` held twenty-one packable projects, so nine shipped packages —
`AiSurface`, `CSharp`, `Idl`, `Idl.Cli`, `Idl.Codegen`, `Lease`, `Observer`, `Projection`,
`Propagation` — were documented nowhere a consumer reads. Five of them (`Projection`, `AiSurface`,
`Propagation`, `Lease`, and `Observer`'s sibling position) are referenced by
`Fuaran.Core.Conformance`, so a domain adopting the kit already depended on packages the table did
not admit existed. And this document carried two DRAFT-slot preambles — on `0.24.0` and on
`0.23.0` — each saying "no `vX.Y.Z` tag exists yet" beside its own header saying it was released,
because the sentence that was true when the slot was cut was never retired when the tag was made.

**What now holds it.** `PackageRosterTests` in the suite `./verify.ps1` runs in every lane:

1. the README table's rows equal the packable set, derived from the project files themselves
   (`src/*/*.fsproj` and `src/*/*.csproj` whose `IsPackable` is not `false`, falling back to
   `Directory.Build.props`), naming every row that is missing and every row that is surplus;
2. no packable project sits outside `src/` — the scope the derivation assumes is checked rather
   than trusted, so a package added elsewhere cannot slip past the roster by being out of frame;
3. some entry header in this document names the standing `<Version>`;
4. no "no `vX.Y.Z` tag exists" sentence survives the tag it denies, read off `git tag` — so a
   released slot cannot go on describing itself as a draft.

Each refusal has its own go-red case over synthetic input beside the live one, because a check
whose only exercised case is the passing one has not been shown to detect anything.

**The purpose column is still hand-written.** Only the ROSTER is asserted: what a package is *for*
is prose a person writes, and a gate that generated it would be describing the file layout rather
than the design.

**One premise of the phase's own shard was refuted in passing.** It named *ten* missing rows,
counting `Fuaran.Core.Idl.Spike`. That project declares `<IsPackable>false</IsPackable>` — it is
the throwaway second-vocabulary spike — so under the check's own definition it must be ABSENT from
the table, and adding its row would have made the gate red on the commit that wrote it. Nine rows,
not ten.

### Codegen refusals are DATA — every remaining throw in `Fuaran.Core.Idl.Codegen` is now a `CodegenError` (Phase 195) — BREAKING

**The class is BREAKING, on two axes.** `CodegenError` gains two cases —
`RequiredEnvelopeField of field * ty * alternative` and
`UnsupportedConstruct of construct * principle * alternative` — so an exhaustive `match` over the
reason family stops compiling. And two published emitters change their return type from `string` to
`Result<string, CodegenError>`: `Gen.fsharpTypes` and `Gen.jsonSchema`.

**What changed, and why the signature change is entailed rather than opportunistic.** Guiding
principle 3 says every rejection is a typed envelope naming the failure and the valid alternatives,
never an exception. The generator held thirteen throws that broke it: a required node-envelope
member, an op-vocabulary slot (`TKind` / `TOp`) in each of the five recursive emitters, a `HostOnly`
field that declared no placeholder, a declared transparent union case of the wrong arity in three
backends, an unbounded generic-instantiation walk in the schema leg, and one "unreachable" arm in
the proposal spike. Every other refusal in the same file was already a value. The five recursive
emitters feed `fsharpTypes` and `jsonSchema`, whose channels were plain strings — so those two legs
were the only place the generator's refusal COULD be an exception, and leaving either as a string
would have left a throw behind it. [`DECISIONS.md`](DECISIONS.md) D44 records the alternative that
was rejected and why.

**One refusal NARROWED rather than moved: a Required node-envelope member.** A smart constructor
fills the envelope with an identity value so the common call stays `mkHeading "h" 2 text`, and a
member that is neither optional, nor omit-at-default, nor host-only used to crash the generator. It
is now EMITTED when a default is declared for it — the shape the full node envelope needs — and
refused as data only when none is. A declared envelope default is addressed by the EMPTY
`IdlDefault.Kind`: the envelope has no kind tag, and a kind's tag is its wire discriminator, so the
empty address can name nothing else. No published record widened to express it.

**What a consumer has to do.** An exhaustive `match` over `CodegenError` gains two arms;
`CodegenError.describe` already renders both, so a consumer that only reports the refusal needs
nothing. A caller of `Gen.fsharpTypes` / `Gen.jsonSchema` unwraps the `Result` — and a caller that
would rather keep failing loudly writes
`|> Result.defaultWith (CodegenError.describe >> failwith)`, which is the behaviour it had before,
with a typed value behind it instead of a sentence.

**What is NOT changed: the emitted bytes.** Every generated artefact this repository commits — the
spike vocabulary's `Generated.fs`, the second domain's `DocGenerated.fs`, the three F\* vocabulary
/ proof pairs, and the classify fixtures — regenerates byte-identically across this change. The
refusals are on paths no declared vocabulary reaches; what moved is what happens when one does.

## 0.25.0 — the proof programme's certification leg, and the hardening policy's undeclared half — released 2026-09-15 as `v0.25.0`

**This slot is RELEASED.** The repository holds the `v0.25.0` tag, made 2026-09-15 on `cf7a303`, so
this is a released contract a consumer can pin today and nothing further may ride it. `<Version>`
has since moved on through `0.26.0`.

**This entry was written after the fact, by Phase 205, and that is itself the finding it records.**
The slot was cut, filled, tagged and released while no entry header was ever opened under it, and
the next slot's draft preamble was opened over the top of it. Phase 199's check asserts an entry
header for the STANDING `<Version>` and nothing else, so a released slot that was skipped is
invisible to it by construction — which is why 199 reported the gap rather than closing it, and why
the property that now guards it is per-TAG rather than per-`<Version>`.

**The floor, stated here because this slot is the first held to it.** From `0.25.0` upward, every
`vX.Y.Z` tag the repository holds has an entry header naming it, and the `Package roster` family
refuses a tag that has none. Slots below the floor are deliberately not retro-fitted: `0.1.11`,
`0.2.0`, `0.3.0`, `0.12.0`–`0.15.0` and `0.17.0` were released before this document recorded
per-slot classes at all, and writing entries for them now would be invention rather than record.

### What shipped in it, and the class of each

`git log v0.24.0..v0.25.0` carries seven phases. The classes are the ones the **Public-surface
baselines** section above defines, and they were read off the range's `src/` diff rather than
argued: exactly four source files moved across the whole range —
`Fuaran.Core.Idl.Codegen/FStar.fs`, `Codegen.fs`, `Trust.fs` and `Fuaran.Core.Idl/Idl.fs` — and
they belong to the three phases the table marks `additive`. The other four touched no `src/` at
all, so they moved no published surface; that is a statement about the packaged contract, not a
claim that nothing a consumer can observe changed, and where something did the row says so.

| phase | what shipped | class |
|---|---|---|
| 151 | evolution-policy soundness — §15.4's compatibility table carried as an F\* theorem (`proofs/WireVersioning.fst`, its F# oracle, and the oracle tests that hold the two in step) | none — no published surface moved |
| 174 | the assumed rows are classified, and every domain obligation names the law that discharges it (`proofs.json`, the ladder tests) | none — no published surface moved |
| 172 | Core owns its conformance vectors, and the shared corpus carries a DECLARED copy of them rather than the authority | none — no published surface moved; see the note below on what a certifying host sees |
| 178 | the hardening policy's undeclared half — `HardenPolicy.Undeclared`, `Trust.checkHardenPolicy` / `hardenOrRefuse`, one `CodegenError` case | **additive** — and it is the change that advanced `<Version>` onto this slot; its own entry sits above under *The hardening policy's UNDECLARED half* |
| 175 | the proof kit ships the generic models, so a domain instantiates a theorem instead of re-modelling it (`proofs/kit/templates/`) | none — no published surface moved |
| 173 | the proof leg certifies the F\* backend on the certification set (D14 applied to `proofs/`) | **additive** — see below |
| 168 | generated proofs: one lemma per constructor and one per presence pattern | **additive** — see below |

**The ordering is worth reading, because it is the draft-slot rule working rather than an
accident.** `0.25.0` was cut as a draft at `ca07895` once Phase 178 had landed — 178 could not ride
`0.24.0`, which was already tagged and released. Phases 173 and 168 landed *after* that cut and
rode the open draft, because both are additive and the draft already carried an additive class; the
release gesture was then made on 168's own commit. No phase in the range outranked what the draft
held, so the number moved once.

### The two additive moves on `Fuaran.Core.Idl.Codegen` (Phases 173, 168)

`FStarTarget` gains, and loses nothing:

- **Phase 173** — a `Provenance` record (`Origin`, `Proves`) with a `Provenance.supplied` value, and
  the two `…From` entry points that take one: `vocabularyModuleFrom` and `proofsModuleFrom`.
- **Phase 168** — `presenceSplitAt`, the constant at which a constructor's optional members stop
  being enumerated as presence patterns and become a linear split.

`vocabularyModule` and `proofsModule` keep their exact signatures and delegate to the new entry
points at `Provenance.supplied`, so no call site moves and no baseline token is removed or retyped.
A pinned consumer compiles either way.

**What is NOT additive-by-default, and is where a consumer should look:** the GENERATED F\* text
changed shape in both phases — 168 emits a lemma family per constructor and per presence pattern
where it previously emitted one, and 173 splits the vocabulary module and re-emits it with a
provenance header. That output is read by a PROVER and by nothing at runtime (the boundary Phase
150's entry draws for this backend), so it is not a package contract; but a consumer that pins
generated proof text in a fixture regenerates it, exactly as the committed `proofs/*.fst` pairs in
this repository did.

### Two artefact facts that are not API classes (Phases 172, and the slot's own re-emit)

**Phase 172 moved where the conformance vectors LIVE, not what they say.** Core's `conformance/`
directory is now the authority for the vectors Core owns, and the shared cross-host corpus carries
a declared copy of them (`copies.json`), checked on the workspace sweep. A host certifying against
the corpus reads the same bytes it read before; what changed is which repository is entitled to
change them, and where a staleness report will point when they drift.

**The laws vectors were re-emitted at `kitVersion` `0.25.0` after the cut.** The `0.25.0` cut left
`conformance/laws/transform-laws.json` stamped `0.24.0`; `45cbb9d` and `647af2f` corrected it
in-slot. The stamp names the kit that produced the vectors, so a consumer comparing stamps across
this boundary sees `0.24.0` → `0.25.0` with no vector content change beneath it.

## 0.24.0 — the apply-engine correctness programme and the proof programme's contract changes — released 2026-09-15 as `v0.24.0`

**This section describes a RELEASED slot** — `v0.24.0` is tagged (released 2026-09-15), so the
entries below are a contract a consumer can pin today and nothing further may ride them. It was cut
as a DRAFT on 2026-09-14 by the driving session so the phases below can ride one slot rather than each
minting a number: Phase 137 (a previously accepted `InsertChild` whose subtree carries an
already-present or internally duplicated id is now refused with `DuplicateId` — a parity
correction with the other hosts), Phase 147 (`Dag.DagBreak.Reason` becomes a closed DU, the
sibling of `ChainBreak.Reason`), Phase 143 (which set out to widen `Ops.independent`'s promise
under a proved clause and instead proved the promise cannot widen over this record — **no contract
change; see its entry below**), Phase 145 (a `codecInjectivityLaws` family in the conformance
kit), Phase 161 (a previously accepted `InsertChild` whose subtree places children under a node
`canHold` refuses is now refused with `NotAContainer` naming that node — the container-capability
sibling of 137's widening, plus the `containerLaws` family that makes the one premise no engine
check can discharge a domain obligation the domain certifies), and the additive riders — Phase 139
(`Tree.WellFormed`, the `apply/` corpus family), Phase 160 (`applyAllWith` / `canApplyAllWith`) and
Phase 141 (`diffContainedLaws`). Each phase appends
its own entry beneath this header as it lands; the version moves only if a later class outranks
what the draft already carries (the draft-slot rule).

### An F\* PROOF-MODEL target for the IDL generator (`0.24.0`, Phase 150) — ADDITIVE

`Fuaran.Core.Idl.Codegen` gains a fourth backend and one `CodegenError` case. Nothing existing
changes shape, no runtime package gains code, and no host's build gains a generation step: what the
target emits is read by a PROVER and by nobody at runtime.

```fsharp
FStarTarget.vocabularyModule : string -> Idl -> string list -> Result<string, CodegenError>
FStarTarget.proofsModule     : string -> string -> Idl -> string list -> Result<string, CodegenError>
FStarTarget.partition        : Idl -> FStarTarget.Verdict list
FStarTarget.proofKinds       : Idl -> string list
FStarTarget.beyondEnvelope   : Idl -> string -> string list
```

`vocabularyModule` emits a vocabulary's types, its discriminated encoder and its tag-dispatch
decoder as an F\* module over the wire decode model; `proofsModule` emits the round-trip and
totality THEOREMS over exactly those definitions, from the same walk. Generating the proof script
as well as the model is the point rather than a convenience: a hand-written proof over a vocabulary
is a theorem about the day it was written, and a generated one re-proves itself when a kind lands.
The emitted proof discharges for a small vocabulary and not yet for this corpus's own — measured,
with the reason and the structural fix in `proofs/README.md` — so this release ships the emitter
and the generated MODEL, and commits no proof script.

**The `CodegenError` addition is a DU case on a published closed union** —
`UnmodellableInFStar of construct: string * where: string` — so a consumer matching
`CodegenError` exhaustively gains a warning, and one matching it with `| _ ->` gains nothing. It is
filed as additive on the draft-slot rule's class test: the slot already carries breaking changes
(137, 161), so this cannot outrank what the draft carries, and it moves no number.

The refusal is a refusal rather than a dropped member, deliberately: a model that silently omitted
the construct it could not express would prove a round trip for a document nobody sends, and the
theorem would read exactly as it reads now. The kinds a vocabulary's model covers, the two
different reasons it may not cover one, and the measured cost that decided the second are in
`proofs/README.md`, theorem 1.

### A container-aware SEQUENCE surface — `Ops.applyAllWith` / `Ops.canApplyAllWith` (`0.24.0`, Phase 160) — ADDITIVE

Two new public functions in `Fuaran.Core.Ops`, and nothing existing changes shape:

```fsharp
Ops.applyAllWith    : ('Node -> bool) -> NodeWitness<'Node,'Id> -> IdWitness<'Id>
                          -> SkeletonOp<'Node,'Id> list -> 'Node
                          -> Result<'Node, int * Rejection<'Id> * 'Node>
Ops.canApplyAllWith : ('Node -> bool) -> NodeWitness<'Node,'Id> -> IdWitness<'Id>
                          -> SkeletonOp<'Node,'Id> list -> 'Node
                          -> Result<unit, int * Rejection<'Id>>
```

They are `applyAll` / `canApplyAll` with the container capability threaded — the same relation
`applyContained` / `canApplyContained` have had to `apply` / `canApply` since Phase 251, now at the
SEQUENCE level. Same result shapes, same 0-based step index, same first-refusal-wins fold.

**What this closes.** Until now there was no container-aware sequence surface at all: `applyAll` and
`canApplyAll` both threaded the plain `apply`, so a script `canApplyAll` certified could be refused
by `applyContained` at its first step — one move of a leaf under a leaf is the counterexample, and
Phase 140 proved it (`can_apply_all_ignores_containment`). A domain with leaves that wanted to apply
a script had to write its own loop over `applyContained` and lose the index contract, which is what
every container-aware consumer does today. It can now call `applyAllWith` and get the
same `(index, envelope, partial tree)` triple back, `NotAContainer` included.

**`applyAll` and `canApplyAll` are now defined AS the `fun _ -> true` instances, and their behaviour
is unchanged.** Nothing to adopt, nothing to re-test: the restatement is proved in
`proofs/Preservation.fst` section 9 (`all_with_at_total_is_plain`, discharged against the folds as
they stood before this phase) and measured on the shipped functions over a generated script pool
(`OpsTests`, "applyAll and canApplyAll ARE the `fun _ -> true` instances"). A consequence worth
stating plainly, because it reads like an omission: the plain pair remains BLIND to containment, and
always will be — that is what being the total instance means. A container-aware caller pre-flights
with `canApplyAllWith` and executes with `applyAllWith`; a caller that pre-flights with `canApplyAll`
and executes with `applyContained` still has the Phase 140 gap, and now has a surface that closes it.

**A script is not a `Batch`, and the doc comments say so.** `Batch` is all-or-nothing inside one
operation: it aborts and the caller's original tree survives. A script stops at the first refusal and
hands back the tree the accepted prefix reached, so the accepted prefix is KEPT. That tree satisfies
the container invariant — `contained_preserves_all_with` covers the refusal arm as well as the
accepted one, which is the statement that makes a non-atomic container-aware surface safe to use.

**Proved, not merely tested.** Section 9 of `proofs/Preservation.fst` models both functions clause
for clause and proves four things with no admits, for any predicate and any tree: the invariant
survives a script including the partial tree a refusal returns; the dry run reaches the same verdict,
the same index and the same envelope as the mutating call; the plain pair is the total instance; and
the Phase 140 counterexample is answered — the same script, at the same tree, refused at index 0 by
the new pair. The extracted model runs beside the shipped pair in the `Proofs.Oracle` family, with a
go-red (the capability-free sequence check in the dry-run slot) required to lose.

### The unknown-parent over-approximation is NECESSARY, not merely conservative (`0.24.0`, Phase 143) — NO CONTRACT CHANGE

**Nothing in the promise moves, and that is the result.** `Ops.footprint` and `Ops.independent` are
byte-for-byte what they were; no type, signature, address set or verdict changes; a host reading this
entry has nothing to adopt. What changes is what is *known* about the entry above
("Op-script footprint + independence", Phase 78), which recorded the first two pinned
over-approximations as deliberate coarseness that a tree-aware analysis could in principle sharpen.

Phase 143 asked whether the first one could be sharpened now, with Phase 138's preservation theorem
in hand: a relocation ought to commute with a structural write under an unrelated parent, so
`independent` ought to be able to stop serialising every remove/move against every structural write.
**It cannot, over this `Footprint` record, and that is now a theorem** — `proofs/TreeOps.fst`
section 18, `relocation_clause_is_necessary`, verified with no admits under the pinned prover.

The witness is one well-formed tree and three operations:

- a `MoveNode` and an `InsertChild` under a parent **inside the moved subtree** DO commute
  (`relocation_disjoint_diamond`). Every clause of `independent` except the pinned relocation pair
  already holds of them, so that clause is the only thing refusing them — exactly the tightening
  this phase was after, and it is real;
- the same shape with a `RemoveNode` in it does NOT
  (`relocation_diamond_fails_for_a_remove`): both operations apply at the tree, but
  remove-then-insert is `UnknownNode`, because the insert's parent was destroyed with the subtree,
  while insert-then-remove succeeds;
- and the two operations carry **the same four address sets**
  (`relocation_footprints_coincide`) — a move, and a batch that removes and then reorders, fold to
  byte-identical footprints.

A predicate over footprints alone therefore gives both operations one verdict. Freeing the safe pair
frees the fatal one. This is the *second* pinned over-approximation above — a `RemoveNode`'s
content-write records the target id and not its tree-unknown subtree — being load-bearing for the
first, which that entry already said in prose and which is now machine-checked.

**What a future tightening would cost, stated so it is not re-attempted cheaply.** It needs a
footprint that can NAME the difference between a relocation that preserves its subtree and one that
destroys it: a fifth address kind carrying the relocation's kind, or a destroyed-subtree set the
pure script cannot compute without the tree. Either is a change to the `Footprint` record and to
every consumer that reads it, not a change to a clause — a breaking change with its own version,
weighed against how much fold availability it actually buys.

**And part of the refused set could not be freed by any record.** Two `MoveNode`s whose destinations
sit inside each other's subtrees each apply alone and reject each other with `WouldNestUnderSelf`
(`relocation_move_pair_also_fails`). The obstruction there is the cycle check — a fact about the
tree's shape at the moment the second operation runs — so the refused set is not one homogeneous
class waiting on a better footprint.

**Pinned in the tree, both ways.** The `Proofs.Oracle` case "the pinned unknown-parent clause is
necessary" runs the witness on the extracted model and on production `Ops.footprint` /
`Ops.independent` side by side, and the `Conformance.concurrencyLaws` teeth-check
"dropping ONLY the unknown-parent clause makes the law bite" erases `UnknownParentWrites` and
nothing else — which is precisely `independent` with its last two clauses removed — and requires
the confluence or totality law to go red over a generated pool. A session that tightens the clause
meets both.

### `InsertChild` refuses a subtree that breaks id uniqueness (`0.24.0`, Phase 137) — a REFUSAL-CLASS WIDENING

`Ops.apply` / `canApply` / `applyContained` / `canApplyContained` now reject `InsertChild(parent, node)`
with `DuplicateId d` when **any** id in `node`'s subtree is already carried by the tree, or occurs twice
within `node` itself. `d` is the FIRST offender in `Tree.ids` (preorder) order.

**Read this as a widened refusal surface, not as a bug fix that moved bytes.** Nothing about the accept
path changes: a script whose inserts were legal before produces the identical tree, byte for byte. What
changes is that a script a host previously ACCEPTED can now be refused — which is wire-visible to
anything replaying a stored op-stream, so it is recorded here as a contract change and rides the `0.24.0`
draft as a minor.

**The check used to read the inserted node's own id alone.** So a subtree whose DESCENDANT id was already
present, or which repeated an id within itself, was accepted and the tree then carried one id twice.
Nothing downstream survives that state: `Tree.updateNode` rewrites *every* node matching the repeated id,
and `Tree.Index.build`'s `Map.ofList` silently keeps the last — so the tree does not fail, it quietly
answers wrongly. The diff path already refused the same tree outright
(`Diff.toOps` → `DiffError.DuplicateIdInTree`), and `Ops.footprint` already computed the very id set the
validator needed; the accept path was the one place the invariant was not enforced.

**Which half is PARITY and which is stricter — stated separately, because they are different claims.**

- *A descendant id already present:* **parity.** The TypeScript, Go and Rust reference implementations of
  the tree-op engine all refuse this, under the code `DuplicateNodeId`; the UI tier runs its own
  pre-check ahead of this engine for the same reason. Core was the outlier, and this closes it.
- *An id repeated WITHIN the inserted subtree, with none of them in the tree:* **stricter than those three
  reference implementations**, each of which seeds its comparison set from the destination tree alone and
  therefore accepts this shape. Core refuses it, because the invariant is a property of the tree that
  RESULTS, and that tree carries the id twice however it got there. Stated plainly rather than folded into
  the parity sentence: a reader porting between engines needs to know which of the two claims they are
  relying on.

**The class stays `DuplicateId`.** It is the same failure the envelope already named, reached through more
of the subtree — a rename would break every consumer matching on it to buy nothing.

**Precedence is unchanged and is now pinned by a test.** The pre-0.24.0 validator checked the inserted
node's own id BEFORE parent existence, so a duplicate outranked `UnknownNode`. `Tree.ids` is preorder, so
the widened scan reaches that same id first and the ordering is preserved rather than quietly reordered.
A consumer that already handled `DuplicateId` ahead of `UnknownNode` sees no change; one that inserts
under an unknown parent with a colliding subtree still gets `DuplicateId` first. (Some sibling
implementations report the missing parent first. That divergence predates this change and is not widened
in kind by it, only in extent.)

**Scope: unique over the WITNESS surface, and that is a deliberate limit.** `Tree.ids`, `Tree.exists` and
`Tree.updateNode` all walk `NodeWitness.Children`. A node a domain holds in a keyed, non-structural
position is invisible to them, so the invariant this enforces — and the one the conformance law below
certifies — reads *each id occurs at most once over the witness's `Children` traversal*. Uniqueness over
keyed positions is the DOMAIN's obligation, and a domain that has them keeps its own pre-check. Widening
the witness is not the answer: `Children` is also what the engine rebuilds through, so a widened witness
would oblige a domain to restructure keyed cases as an ordered list.

**`Conformance.opAlgebra` reports a fourth law**, `"an accepted insert introduces no id already present"`,
so an adopting domain certifies the property over its own witness rather than trusting this engine.
`Conformance.certify` therefore returns **14** law results where it returned 13; a consumer asserting the
count updates it. `witnessLaws` is unchanged.

### `Dag.DagBreak.Reason` is a closed DU (`0.24.0`, Phase 147) — BREAKING

`DagBreak.Reason` is `DagBreakReason`, not `string`:

```fsharp
type DagBreakReason =
    | ContentIdMismatch
    | MissingParent
    | Unrecognised of reason: string
```

`Dag.firstBreak` mints the two named cases and never the third — which is what
`Conformance.dagBreakReasonLaws` certifies, over every kind of break the walker can produce. Two
named cases for two spellings, unlike the chain's three-for-four: the DAG walker makes exactly two
checks, so there is no collapse to justify.

**Why `Unrecognised` exists on a type this library alone mints** — the same argument the chain's
carries. It is the honest arm for a reason that arrives from outside the walker: a DAG verified by a
host's own verifier, a reason carried across a wire or a process boundary, a `DagBreak` a consumer
constructs itself. `DagBreakReason.ofString` is total and lands there; `toString` renders a named
case back to the exact string the walker emitted before this release. The pair round-trips on the
named cases, and that is a law too.

**The rendered bytes are UNCHANGED, and that is the load-bearing half of this entry.**
`Dag.fromJsonlVerified`'s error still reads `Dag.fromJsonlVerified: <reason> at node <id>` with
`<reason>` one of the two pre-release spellings, because it renders `DagBreakReason.toString`.
Downstream consumers matching that error text exist. A sweep for the two spellings across every
repository available when this was written found **two**, both asserting by substring on the string a
verified load returns — one on a DAG store's own load wrapper, one on a session history's — and
neither reads the `Reason` field, so both are unaffected without doing anything. `DagTests` pins both
spellings by exact comparison rather than by substring, so a later reword goes red here instead of
downstream.

**The consumer that deletes its projection is this repo's own proof differential.** Phase 136's
`Chain.fst` model carries the walker's two break classes as a closed set, and its differential
compared production against the model *by class* — through those two string spellings, which is
exactly the re-typing this DU removes. That differential now compares typed verdicts, on both
walkers, and a model whose reason string ever drifted lands in `Unrecognised` against a named case
instead of failing as an unexplained textual mismatch. Outside that, the sweep found no consumer of
the `Reason` field at all: the remaining matches are this library's own walker, the model, the
extracted oracle, and their tests.

**This is deliberately NOT a merge with `ChainBreakReason`.** The chain and the DAG fail
differently, and a shared type would have to carry cases each walker never mints. `0.23.0`'s entry
below said this sibling would take the same shape when it was asked for; Phase 136 is the ask, and
this is that shape.

**`Conformance.certify` is unchanged.** `dagBreakReasonLaws` is a standalone opt-in family beside
`chainBreakReasonLaws`, not folded into the aggregate, so no consumer's law count moves for it.

### `Conformance.codecInjectivityLaws` — the op codec's own contract, certifiable (`0.24.0`, Phase 145) — ADDITIVE

A new opt-in law family:

```fsharp
val codecInjectivityLaws:
    StreamWitness<'Op, 'State, 'Rej> -> StreamGen<'Op, 'State> -> int -> int -> LawResult list
```

Three laws over one seed-replayable draw of the domain's own ops: **no collision was drawn** (two
distinct ops never share an encoding), **the codec has a left inverse** (`Decode (Encode op) = Ok op`,
which makes the codec injective rather than merely un-collided — one drawn op exercises it, where a
collision search needs the pair to come up), and **the draw compared more than one encoding**.

**Why a domain should run it.** `Dag.nodeHash` hashes `Actor.encode actor + "|" + w.Encode op`, so a
node's content id determines its op only if the codec is injective. Two distinct ops that encode alike
mint ONE content id, and a tamper between them is invisible to `Dag.firstBreak` and to
`OpStream.verifyChain` alike — the walker is not weak there, the pre-image simply does not distinguish
them. That is the fourth premise of the chain-integrity theorem (`proofs/Chain.fst`'s
`op_codec_injective`), and it is the domain's rather than this library's, because `Encode` belongs to
whoever brings the op type. Same division of labour as the fold theorem's `independence_diamond` and
`FoldConfluence.laneFoldLaws`.

**Deliberately NOT folded into `certify` / `certifyStream`, and the reason is a contract one.** The
left-inverse law demands a working `Decode`. A witness that legitimately stubs it — a domain that never
reads a stream back — would go red inside an aggregate it passes today, for something that is not about
its op algebra. So this joins the snapshot and DAG surfaces as a family a domain calls beside its base
certification, and it does not enter the `certify` aggregate at all (that count moved to 15 in Phase 139,
which added a law to `opAlgebra` rather than a family beside it). A domain whose `Decode` is real should
run it: nothing else in the kit certifies this premise. `'Op` needs equality.

### `Tree.WellFormed` — structural validity, named once (`0.24.0`, Phase 139) — ADDITIVE

`Fuaran.Core.Tree` gains a named predicate and Core's other id-uniqueness call sites become projections
of it:

- **`Tree.WellFormed<'Id>`** — `Structural | RepeatedId of 'Id`, the verdict.
- **`Tree.wellFormed w idw root`** — is this tree structurally valid, and if not, which id breaks it.
- **`Tree.isWellFormed w idw root`** — the boolean form.
- **`Tree.graftWellFormed w idw node root`** — the verdict for the tree that grafting `node` into `root`
  WOULD produce, computed without building it. This is the question every insert validator asks.

**Its scope is the WITNESS SURFACE** — `NodeWitness.Children` and nothing else — stated in the type's
own doc comment and in the README's "What the witness surface covers" section. A domain holding nodes in
keyed, non-structural positions owes its own check over its own traversal; the predicate says so rather
than implying a guarantee Core cannot make. That is Phase 137's scoping, now attached to the name.

**One clause, not two.** The obvious pairing is "unique ids and a single root", but a `'Node` value IS
its tree here: the walk starts at exactly one node by construction of the type, so single-rootedness is a
type-level guarantee rather than a checkable clause, and the predicate carries the one clause that can
actually be violated and that everything downstream depends on.

`Ops`'s insert validator and `Diff.toOps`'s tree check now both read it, so the accept path and the diff
path cannot drift into two notions of the same defect.

**One OBSERVABLE change, and it is which id is NAMED — never whether a tree is refused.**
`Diff.DiffError.DuplicateIdInTree d` previously reported the first id whose duplicate GROUP appeared
earliest; it now reports the first id at its SECOND occurrence in preorder. For `[a; b; b; a]` the old
answer was `a` and the new is `b`. The new answer is the one `Rejection.DuplicateId` already gave on the
accept path, so the two paths now name the same offender for the same tree. Additive rather than
breaking: the error CASE and the refusal are unchanged, and nothing in the kit ever pinned which of
several duplicates was named.

### `Conformance.opAlgebra` gains a preservation law (`0.24.0`, Phase 139) — ADDITIVE

`opAlgebra` reports a fifth law, **"apply's accept path preserves `Tree.WellFormed`"**: over every op the
sample reaches, if the pre-state is well-formed and `apply` ACCEPTS, the post-state is well-formed too.
It is the sampled twin of Phase 138's machine-checked `apply_preserves_wf`, and it is wider than the
Phase 137 law beside it, which is about inserts alone — a `MoveNode` or a `Batch` that broke uniqueness
would be invisible to that one.

**`Conformance.certify` therefore reports 15 law results, not 14** (witness 4 + algebra 5 + diff 3 +
stream 3). A domain that pins the count updates it; the other four laws are unchanged.

### The `apply/` conformance family (`0.24.0`, Phase 139) — ADDITIVE, and it is a CORPUS artefact

The shared wire-format conformance corpus gains a self-enumerated `apply/` family: one vector per
validator clause per skeleton op, `Batch` all-or-nothing, and the Phase 137 collision cases, each an
authored `(tree, op)` pair with the outcome computed by calling `Ops.apply`. See
[`docs/conformance-corpus.md`](docs/conformance-corpus.md) for the shape, the per-host rejection-code
mapping and what the family deliberately does not pin. Emitted by
`dotnet run --project tests/Fuaran.Core.Tests -- --emit-apply <corpus dir>`; nothing in this repository's
public package surface changes.

### `Conformance.diffContainedLaws` — a contained diff certified by the engine that runs it (`0.24.0`, Phase 141) — ADDITIVE

One new public function in `Fuaran.Core.Conformance`, and nothing existing changes shape:

```fsharp
Conformance.diffContainedLaws : NodeWitness<'Node,'Id> -> IdWitness<'Id> -> OpGen<'Node,'Id>
                                    -> int -> int -> LawResult list
```

The twin of `diffLaws` for `Diff.toOpsContained`, certified through `Ops.applyAllWith` /
`Ops.canApplyAllWith` — the container-aware sequence pair Phase 160 added two entries above — under
the witness's own `CanHold`.

**What this closes.** `diffLaws` certifies a diff script with the PLAIN sequence pair, which Phase
160 proved is blind to containment by construction (`all_with_at_total_is_plain`). So a script
emitted for a container-aware witness was never shown applyable under the engine that witness
actually runs: the pre-flight and the executor disagreed about what a refusal is. Three laws, the
shape of `diffLaws`' three — reconstruction, applyability, and refusal exactness (`toOpsContained`
refuses with `TargetNotAContainer` exactly when `after` nests children under a node the predicate
rejects, and the plain `toOps` accepts the same pair, so the refusal is the container check's
contribution and nothing else's).

**Opt-in, like the snapshot and DAG families, and deliberately not folded into `Conformance.certify`.**
`certify`'s report is a pinned length and its witness may carry no container capability at all; a
domain with one runs this beside it. Nothing to adopt: a domain that does not call it is unaffected,
and `diffLaws` is unchanged.

**And a new model in `proofs/`, which changes no package surface at all.** `proofs/TreeDiff.fst`
proves `Diff.toOps`' two refusals exactly (so a refused pair is one no skeleton script could
express), its four-pass emission order, what each pass guarantees about the block it emits —
including survivor preservation, which was a sampled law and is now a theorem — and that a contained
script cannot be refused for containment at any step. Reconstruction itself stays differentially
tested; `proofs/README.md`'s theorem 6 says why, and says it plainly rather than leaving a reader to
infer that "the diff is proved" covers it.

### `applyContained` refuses a graft whose INTERIOR is not a container (`0.24.0`, Phase 161) — a REFUSAL-CLASS WIDENING

`Ops.applyContained` / `canApplyContained` now reject `InsertChild(parent, node)` with
`NotAContainer(id, kindTag)` when **any** node of `node`'s subtree holds children while `canHold`
refuses it. The named node is the FIRST such node in preorder — the same first-offender discipline
`DuplicateId` follows. The plain `apply` / `canApply` are unaffected in every respect: they pass
`canHold = fun _ -> true`, under which no node is ever an offender, so the clause is inert there.
This is the sibling of Phase 137's entry above and rides the same `0.24.0` draft as a minor.

**Read this as a widened refusal surface, not as a bug fix that moved bytes.** Nothing about the
accept path changes: a script whose inserts were legal before produces the identical tree, byte for
byte, and that is asserted rather than asserted-about — `proofs/Preservation.fst`'s
`apply_contained_is_apply` proves the two engines are the same function at `fun _ -> true`, and the
`Ops.applyContained` suite compares the whole result tree on a graft the real predicate refuses.
What changes is that an operation a host previously ACCEPTED can now be refused, which is
wire-visible to anything replaying a stored op-stream, so it is recorded here as a contract change.

**Which operations, exactly.** Only ones that were already producing a tree the domain's own
predicate calls invalid. `canHold` used to be applied to the parent of an insert and to nothing
inside the subtree, so a graft that placed children under its own non-container node was accepted
whole and the invariant `applyContained` exists to keep — *every node with children satisfies
`canHold`* — broke across an accepted operation, silently. That was machine-checked as a
counterexample by Phase 140 (`contained_needs_op_hypothesis`, now `nested_graft_refused`) before it
was a refusal.

**The class stays `NotAContainer`, and `target` now names a node in one of two places.** A parent
refusal names a node of the TREE; a graft refusal names a node of the caller's own INSERTED SUBTREE,
which the duplicate-id scan guarantees is not in the tree. A consumer that resolves `target` against
the tree and reports "unknown node" will be wrong about the second site; resolve it against the
graft you supplied. A new rejection case was considered and declined for Phase 137's reason, in
Phase 137's words: it is the same failure the envelope already names, reached through more of the
subtree, and a new case breaks every consumer matching on it to buy a distinction they can already
make — the node is in their own graft, and they have it in hand. (`DECISIONS.md` D38 records the
choice and the option declined.)

**Precedence is unchanged, deliberately.** The new check is the LAST clause of `validateInsert` —
after the duplicate-id scan, after the parent's existence, after the parent's own capability — so
**no operation that was refused before Phase 161 changes its class.** Only operations that were
accepted can now be refused. A graft that breaks both invariants still earns `DuplicateId` first,
and that is pinned by a test.

**`MoveNode` is NOT walked, and the omission is argued rather than inherited.** A move relocates a
subtree that is already in the tree, so it introduces no interior structure the tree did not already
hold: a violation found inside it was carried in by some earlier insert. Refusing the move for it
would be an invariant-REPAIR gate, a different feature. The machine-checked form of that argument is
`contained_preserves`' move clause, which derives the moved subtree's containment from the tree's
own and needs no hypothesis about the operation at all.

**Scope: the WITNESS surface, exactly as Phase 137's entry scopes id uniqueness.** The walk is
`Tree.preorder` over `NodeWitness.Children`, so a node a domain holds in a keyed, non-structural
position is invisible to it, and containment over those positions is the domain's own obligation.

**No other language host mirrors this surface.** `canHold`, `applyContained` and `NotAContainer`
have no counterpart in the TypeScript, Go, Rust or Python hosts, which model no container capability
at all, and no committed conformance-corpus `apply/` vector exercises one. So unlike Phase 137's
widening — half of which was a parity correction — this one is Core's alone and nothing outside this
repository moves.

**What it buys, in the proof ladder's terms.** `contained_preserves` carried TWO hypotheses and was
refuted without either. This change discharges one of them **in code**: `contained_op` is gone from
the theorem's premises, and the counterexample is kept evaluable against a model of the pre-161
engine so the reason it was needed survives the premise (`proofs.json` row `graft-containment`). The
other, `child_blind`, stays and always will — a `canHold` that reads the child list can admit a node
at the instant it is checked and refuse it the instant the licensed insert gives it a child, and no
check placed anywhere in the engine repairs that.

**So `child_blind` becomes the DOMAIN's obligation, certified rather than assumed.**
`Conformance.containerLaws` is new: three laws and an adequacy guard, certifying that the domain's
`canHold` is child-blind (sampled by perturbing a drawn node's children, emptied and extended by
one), that the engine preserves the container invariant over the domain's own witness, and that a
graft with an interior non-container is refused naming that node. It is **opt-in** — the
`chainBreakReasonLaws` / `dagBreakReasonLaws` shape — because `OpGen.CanHold` is an option and
folding it into `certify` would add laws that cannot fail for every domain without a container
notion. **`Conformance.certify` still returns 14 law results**; a consumer asserting that count does
not move it. A domain with a container notion calls `containerLaws` alongside its base run; one that
calls it with `CanHold = None` is reported by name rather than skipped.

## 0.23.0 — the Core API asks routed here from the UI tier (Phase 125) — released 2026-09-13 as `v0.23.0`

**This section describes a RELEASED slot** — `v0.23.0` is tagged (released 2026-09-13), so the
entries below are the contract a consumer can pin today, not the draft they were written as. They
are grouped as one section because they are cut as ONE minor
deliberately: each is a separate ask, and raising a pin four times to adopt four asks costs every
consumer three raises it gains nothing from.

Three of the four are BREAKING in shape and one is additive; a fifth entry is a removal, and a sixth
is a codegen NARROWING the fourth ask's own property found and the operator ruled on the same day.
Each names the consumer that deletes a workaround on adoption, because that is the only reliable way
to tell afterwards whether the ask was answered or merely implemented.

### `ChainBreak.Reason` is a closed DU (`0.23.0`) — BREAKING

`ChainBreak.Reason` is `ChainBreakReason`, not `string`:

```fsharp
type ChainBreakReason =
    | SequenceMismatch
    | PrevHashLinkBroken
    | HashMismatch
    | Unrecognised of reason: string
```

`firstChainBreakWith` and `firstCaptureBreak` mint the three named cases and never the fourth —
which is what `Conformance.chainBreakReasonLaws` certifies, over both walkers, on every kind of
break each can produce.

**Why `Unrecognised` exists on a type this library alone mints.** It is the honest arm for a reason
that arrives from outside the walkers: a stream verified by a host's own walker, a reason carried
across a wire or a process boundary, a `ChainBreak` a consumer constructs itself.
`ChainBreakReason.ofString` is total and lands there; `toString` renders a named case back to the
exact string the walkers emitted before this release, so a consumer that logged those strings keeps
logging the same bytes. The pair round-trips on the named cases, and that is a law too.

**The consumer that deletes its workaround:** the UI tier's hash-chain verifier
(`fuaran-dotnet`, `Fuaran.UI.OpStream.Abstractions.Verify.classify`). It declares this exact
four-case type and string-matches Core's four spellings to reach it, and its own comment says so —
"the Core ask above is what removes the projection entirely". On adoption the domain's
`ChainBreakReason` and its `classify` both delete, and its `ofChainBreak` reads `b.Reason` directly.

**Note what deliberately did NOT happen.** The two hash spellings the walkers used —
`"hash mismatch (tampered op/actor/seq)"` and `"hash mismatch (tampered capture)"` — collapse to
ONE `HashMismatch` case rather than becoming two. `Reason` answers *which check failed*, and both
are the digest check; *which walker ran* is the caller's own choice and needs no case. The single
measured consumer collapses them already, and a distinction every consumer immediately discards is a
worse contract than no distinction. `toString` therefore renders the op-walk spelling for both, and
`ofString` accepts either.

**The sibling `Dag.DagBreak.Reason` is UNCHANGED and still a string** *as of this release*. It is a
different type with its own two spellings and no measured consumer, so widening it here would be an
unrequested breaking change made on the strength of a symmetry argument. When it is asked for, it
takes the same shape. (It was asked for, by Phase 136, and took that shape in `0.24.0` — the entry
above.)

### `ColExpr.Now` — a `now` literal at a declared grain (`0.23.0`) — BREAKING

```fsharp
[<RequireQualifiedAccess>]
type NowGrain =
    | Date
    | Timestamp

type ColExpr =
    | (…)
    | Now of grain: NowGrain
```

A pipeline can name `now` without a host threading a param by hand for it. The grain set is the two
`Cell` cases that can hold a clock reading (`Cell.Date` / `Cell.Timestamp`, both canonical ISO-8601
strings); no `hour` / `minute` / `quarter` ladder is invented, because no cell could carry one and
nothing asked for one.

**The clock is a seam, not a platform call.** Core is FSharp.Core-only and Fable-clean, so it holds
no clock:

```fsharp
type ClockWitness = NowGrain -> Cell
```

`ColExpr.substituteNow` / `Transform.substituteNow` replace every `Now g` with `Lit (clock g)` — the
same resolve-by-substitution seam `InParam` already uses, reused deliberately rather than joined by
a new one. `DataFrame.evalPipelineAt` / `evalPipelineInEnvAt` / `evalPipelineWithInEnvAt` are the
entry points that pin a clock and then evaluate.

**A `Now` that reaches evaluation unpinned is a strict `EvalError.UnpinnedClock`**, never a silent
reading of the host's real clock. This mirrors `UnboundParam` exactly, and it is what makes the
determinism law statable at all: `Conformance.nowLaws` certifies that evaluating the same pipeline
twice under one `ClockWitness` yields identical tables, that a pinned clock's answer is the literal
the witness returned, and that an unpinned `Now` is refused by name.

**`EvalError` gains a case**, so an exhaustive match over it warns (`FS0025`).

**The consumer that deletes its workaround:** the wire format's cross-side clause for a current
time (`WIRE_FORMAT.md` §3.3.1), which documents a transform param whose binding is the host's `now`
as the canonical route. That route stays valid for a host-fed binding; what it stops being is the
ONLY way to say `now`, and the clause can point at `ColExpr.Now` for the pipeline-internal case.

### `Limit` and `Sort` accept a param in their scalar slots (`0.23.0`) — BREAKING

```fsharp
[<RequireQualifiedAccess>]
type Slot<'T> =
    | Lit of 'T
    | Param of name: string

type Transform =
    | (…)
    | Sort of (Slot<string> * SortDir) list
    | Limit of n: Slot<int> * offset: Slot<int>
```

`Slot<'T>` is the smallest shape that says "a literal, or a named parameter" and nothing else. It is
deliberately NOT `ColExpr`: a `Limit` count has no row to read a column from, so admitting `Col` at
that slot would admit an expression with no meaning there, and the closed two-case DU is what
default-deny by shape means at a scalar slot.

**Resolution reuses the param seam rather than adding one.** `Transform.substitute` resolves slot
params from the same `Map<string, Cell>` env that `ColExpr.Param` reads, and `Transform.paramsOf`
reports slot params alongside expression params — so a host's dependency edges, reactivity
subscriptions and unbound-param pruning all pick them up with no new call. An unbound slot param
reaching evaluation is `EvalError.UnboundParam(name, bound)`: the same case, the same shape, the
strict `UnboundParam` the UI tier asked for. A bound param of the wrong cell shape (a `Str` at a
count slot) is a `TypeError` naming the slot.

`Conformance.slotParamLaws` certifies all three: a bound slot param evaluates identically to the
literal it stands for, substitution and env-resolution agree, and an unbound one is refused by name.

**`Sort` was decided in the same cut**, as the ask required, because it is the same question about
the same DU. The ask calls its subject a sort key's column; Core has no `SortKey` type — the key is
a bare `(column, direction)` tuple — and the column half is what became a `Slot<string>`. The
DIRECTION stays a literal `SortDir`: no demand named it, and a param resolving to a `Cell` would
have to carry a sort direction as a string, which is the stringly-typed shape this type exists to
avoid.

**Every construction and match site moves.** `Limit(10, 0)` becomes `Limit(Slot.Lit 10, Slot.Lit 0)`
and `Sort [ "total", Desc ]` becomes `Sort [ Slot.Lit "total", Desc ]`. `Transform.limit` and
`Transform.sortBy` are literal-taking constructors for the common case, so a call site that never
uses a param reads as it did.

### Declared defaults: the generative property, and one named backend divergence (`0.23.0`)

**No public surface moved for this entry.** The generative property lives in this repository's own
suite (`tests/Fuaran.Core.Tests/IdlCertificationTests.fs`, beside Phase 124's case-based
certification), NOT as a `Fuaran.Core.Conformance` law family. Two reasons, both structural: a
conformance family certifies a HOST against a contract, and nothing outside this repository
implements this generator, so the family would be one no adopter could ever run; and
`Fuaran.Core.Conformance` is Fable-clean while `Fuaran.Core.Idl.Codegen` is .NET-only and build-time
by declaration, so the reference would break the Fable gate the kit's own portability claim rests on.

**What the property says.** For a drawn declared default over a drawn field of the neutral
vocabularies, the F# backend and the TypeScript backend EITHER both render a literal OR both refuse
with `UnsupportedDefault` — never one each. `tsDefaultLit` takes its admissibility from
`tsIsDefault`, and `fsDefaultLit` decides its own, so the rule is written twice and can drift in
either direction: one drift emits an F# encoder whose omit test has no TypeScript counterpart, the
other refuses a module in one language and ships it in the other. Both are green builds.

**The ask's own phrasing — "every representable default has a literal" — is FALSE, and is
deliberately not what is asserted.** Several representable defaults are refused on purpose and
correctly: a `HostOnly` slot's placeholder is a host expression with no pattern spelling, a non-empty
list has no `List.isEmpty` analogue, a non-finite float has no F# literal. The honest property is
AGREEMENT plus a NAMED refusal.

**The half of the ask that was already shipped.** The ask was for payload-carrying
`OmitDefault (VUnion (tag, payload))` in all three emitters. **Phase 124 (`0.21.0`) had already
delivered exactly that** — `fsDefaultLit` renders every declared field of a value-carrying case,
`defaultExpr` is a one-line alias of it, `tsIsDefault` conjoins a test per declared field nested to
any depth, and the `dReq` / always-emit fallbacks are gone. What 124 certified was CASE-BASED. A case
proves a case; this is the property that holds over the shapes nobody wrote a case for.

**ONE backend divergence was found on the property's first run. It is CLOSED in this same draft, and
the close is a narrowing — see the entry below.**

### A declared TRANSPARENT case as a default is REFUSED by the F# backend too (`0.23.0`) — BREAKING

`Gen.fsharpModule` / `Gen.fsharpModuleWith` / `Gen.fsharpValue` now return
`CodegenError.UnsupportedDefault` for a field whose `OmitDefault` value is a union case that the
vocabulary declares TRANSPARENT (`Idl.Harden.TransparentUnions`). They rendered it before. The
TypeScript backend has refused it since `0.21.0` (Phase 124) and is unchanged.

**A vocabulary that declares such a default stops generating an F# module and starts reporting a
typed refusal naming the offending type and value.** Nothing else moves: a default whose case is
NOT the declared transparent one renders exactly as it did, payload and all, and a vocabulary
declaring no transparent unions at all is bit-for-bit unaffected. `HardenPolicy.Default` declares
`TextSource.Literal`, so a vocabulary carrying the default policy and declaring a `TextSource.Literal`
default is the shape that meets this.

**Why the narrower side wins.** A transparent case is on the wire BARE — no `$type` — so the omit
predicate the TypeScript encoder would need is about a value it never writes, and is not expressible.
The F# omit test is a pattern match on the HOST value, where the case is not transparent at all, so
the F# backend could render it and did. Both were locally correct and the PAIR was wrong: one
generator emitted two hosts that disagreed about what the vocabulary meant, and a default only one
host honours is not a default. The ruling (operator, 2026-09-13; `DECISIONS.md` D33) is that the
backends must agree.

**The rule is now ONE predicate both backends call** (`isDeclaredTransparentCase`), not two matching
checks. The rest of each backend's admissibility differs for real reasons and stays separate; this
clause is about the wire, and a wire rule written once per backend is a rule that drifts — which is
exactly how this one arose.

**This rides the untagged `0.23.0` draft**, so no further version move: the slot is already BREAKING
and already unreleased. A consumer adopting `0.23.0` meets the narrowing with everything else in this
section.

**The property is tightened with it.** The named-class admission and the class-must-stay-non-empty
guard are both gone; it demands FULL agreement now. A case-based test beside it plants a
transparent-case default and asserts both backends refuse it with `UnsupportedDefault`, then plants
the same value at the same field with the case no longer declared transparent and asserts both
render — so the refusal is certified to be about transparency, not about the payload-carrying shape.

### `Idl.Gen.usesHosted` is REMOVED (`0.23.0`) — BREAKING

`Idl.Gen.usesHosted` is gone. The `0.19.0` surface-narrowing section above kept it, reasoning that
it was "a declared boundary, not a helper" for a generative cross-host comparison, and recorded
plainly that it "has no caller in this repository today — the leg it was written for is not wired
here".

That leg is still not wired here, and the ruling (operator, 2026-09-10) is that it will not be: the
cross-host fuzz leg the boundary was written for is not Core's to run, and the vocabulary-scale
sweep is owned by the UI tier now. The sibling `encodeNodeEnv` was narrowed to internal on exactly
the same evidence, and leaving one of a pair standing on an argument the other was already judged to
have lost is how a surface accumulates members nobody can retire.

`FS0039` for a caller. This repository has none — `src`, `tests`, `samples` and `docs` were swept —
which is precisely why the decision had to be taken rather than discovered.
