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

The relational/columnar strand (`Fuaran.Core.Column`, `Fuaran.Core.DataFrame`, `Fuaran.Core.Query`) is
a **first-class, stability-critical member of the substrate**, not an example or a reference sketch —
its public surface carries the same 1.0 commitment as the witness spine, and its cross-host semantics
are **conformance-certified**, not asserted. Stable surfaces:

- **The columnar model + wire codec** — the fixed Arrow scalar set (`int` / `float` / `bool` / `string`
  / `date` / `timestamp`), the validity-mask null model, and `ColumnCodec`'s six-code decode envelope
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
`office`/Calc convergence work), the host supplies the injected `evalNode` (GP6 — no evaluator in Core). The first two are keyed on the same content-addressing discipline (the Phase-49 `applyMemo` /
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

### The Lease strand — `Fuaran.Core.Lease` (generic claims over a resource axis, Phase 84)

A self-contained coordination strand: **claims over an abstract resource axis**, with grant / release /
expiry as a closed `LeaseOp<'Res>` algebra (`Claim` / `Release` / `Expire`). It generalises the
coordination a multi-agent dispatcher hand-rolls — a **holder** claims a **resource set** for a
host-supplied duration, and an overlapping claim by a different holder is refused. Stable surfaces:

- **`LeaseOp<'Res>` + `LeaseState<'Res>` + `LeaseRejection<'Res>` + the wire codec** — the camelCase
  kind-tag envelope (`claim` / `release` / `expire`) is the cross-host contract; logical time
  (`grantedAt` / `ttl` / `now`) is carried as an **int64 encoded as a JSON string** (the `JVal` number
  model caps `JInt` at int32), so it round-trips with full precision, Fable-clean on encode AND decode
  (GP3). Changing the envelope keys, the kind tags, or the time encoding is a **major** bump; an
  additive op case is a **minor** bump.
- **`Lease.apply` totality + the conflict contract (GP4/GP5)** — `apply` is total (a typed
  `LeaseRejection`, never a throw), and a `Claim` overlapping a *different* holder's live lease is
  always the enumerated `Conflict of holder * overlap` — it **names the current holder and the exact
  overlapping resources**. A same-holder re-`Claim` renews in place; `Release` of an absent holder is
  `NoSuchLease`. Removing the enumeration or weakening the conflict guarantee is breaking.
- **The resource axis reuses `IdWitness<'Res>` (GP2 — no new witness shape)** — a resource is whatever
  the host's ids name (file paths for the dispatcher, `Fuaran.Core.Ops` footprint tree-addresses for a
  tree host); the witness is a per-call parameter, so one module serves both. No new witness record was
  introduced.
- **Expiry is time-as-data, no clock in Core (GP6)** — `Expire now` drops every lease whose
  `grantedAt + ttl <= now`, where `now` is supplied by the host as data. Core reads no clock and runs no
  scheduler/timer (analysis + state only), so **replay is deterministic**: the lease stream is a
  `Fuaran.Core.OpStream` instance (`Lease.streamWitnessFor idw`), hash-chained and replayable exactly
  like the tree/columnar streams.

Certified by `Conformance.leaseLaws` (apply totality, `canApply` ≡ `apply`, conflict completeness,
`verifyChain`, replay determinism, expiry-as-data) — a host in another language re-implements the codec
+ these laws against this section, exactly as it does the witness surface.

## Witness-record field freeze (the 1.0 contract)

The six public witness records (`IdWitness`, `NodeWitness`, `StreamWitness`, `ArtifactWitness`,
`AiSurfaceWitness`, `ProjectionWitness`) are
**plain records**: adding a field is a compile-break for *every* adopter's construction site, with no
gradual-migration path. At `1.0` their field sets **freeze**. (The freeze originally named only the
four base records; `AiSurfaceWitness` and `ProjectionWitness` are equally public, equally
adopter-constructed, and carry the same blast radius — an unpinned public witness is exactly the gap
the freeze exists to close, so they are frozen on the same terms.) This is a safe commitment, not a
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

**Enforcement.** New `Fuaran.Core.*` code that adds a field to any of the six frozen records is a
review-blocking regression (the same class as introducing a concrete node type, per *The load-bearing
invariant* above). A conformance/API-surface test that pins each witness's field set is the mechanical
backstop (`Conformance.witnessSurfaceLaws` — to be added alongside the first `1.0` release-candidate).

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
certifies, over a seed-replayable sample under *any* supplied `HashFn`, that a chain hash is a pure
function of the canonical wire pre-image: **determinism** (the same op sequence hashes identically
across builds), **pre-image parity** (an incremental `append` build and a bulk reforge of the same
`(seq, actor, op)` pre-images agree hash-for-hash — the cross-host-parity foundation: two hosts on the
same `HashFn` + same pre-image get byte-identical chains), and **tamper-detection** (reorder / drop /
bit-flip caught by `verifyChain`). The crypto posture itself is pinned by
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
cross-pipeline half is `tests/hash-parity-probe/run-parity-probe.ps1`, which compiles a 124-entry
corpus both ways and byte-compares. Both were taken go-red before being trusted. **Anyone touching
either multiply-safe helper (`mul32`, `.+.`) must re-run that probe**; a green .NET suite is not
evidence about the other pipeline, and reintroducing the naive multiply was measured to leave the
suite fully green while 120 of the 124 entries diverged. The probe is deliberately **not** in
`./verify.ps1` — it needs a Node runtime and the default gate stays dependency-free — which means
nothing runs it for you.

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
**other** label (`"clock"` / `"random"` / `"network"` and any host- or domain-minted label) is
**opaque and reserved to the effect supplier**: the core carries it verbatim through the hash-chained
journal and keys replay ordering on the `Eff` identity, never on interpreting the label. A conformant
host MUST carry a non-`"deterministic"` label without interpretation — inventing or re-meaning one is
a host/domain concern, not a wire change. This is the open-label counterpart of the `IdWitness`
posture: identity/label vocabulary is supplied, not fixed, and the substrate stays FSharp.Core-only by
never depending on the `Fuaran.Core.Function` `Determinism` DU.

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
- **Fable-cleanliness**, gated rather than asserted: `tests/fable-smoke` compiles the whole of
  this package, so every one of the above reaches a browser. That is the reason the split exists
  — see "Fable cleanliness" below.

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
move**: every `idl.json` this engine writes is what it wrote before, which the corpus byte-guard
enforces.

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
identical — pinned at full scale, over the ~40-kind vocabulary, by the round-trip and triple
proofs in the test suite. It is a one-time reordering of a generated file, absorbed once.

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

**Enforced, not asserted (Phase 54).** `./verify.ps1` includes a **Fable-compile gate**: the
`tests/fable-smoke/` project references every public package and is compiled with `dotnet fable`, so a
construct that is not Fable-clean fails the green gate in-repo rather than surfacing downstream.
`Fuaran.Core.Idl` joined that set at `0.8.0` — it was the one `src/` package absent from it, which
made its portability an unprovable claim rather than a certified one. What had blocked it was not the
model but the emitters sharing its project; splitting them into `Fuaran.Core.Idl.Codegen` (Phase 97)
removed the obstacle rather than working around it. `Idl.Codegen`, `Idl.Spike` and `Observer` stay off
the surface deliberately: they are build-time tools, not a Fable-targeted surface. (`Double.ToString`
with the round-trip specifier is *not* Fable-supported — float→string must route through
`Wire.Canon.canonicalFloat`; `Double.TryParse`'s style/provider arguments are ignored under Fable but
parse invariantly, which is a benign warning, not a gate failure.)

**A compile gate is not a VALUE gate, and the difference has cost real defects here.** It proves a
construct transpiles; it cannot notice that the transpiled code computes a different number. That is
exactly how `fnv1a` sat divergent behind a green gate until `0.6.0` (see "Hash-chain integrity
posture"). Where a value must agree across pipelines, the claim is bought by a **probe** —
`tests/hash-parity-probe/run-parity-probe.ps1`, which compiles a corpus both ways and byte-compares —
and by an independent in-suite reference implementation on the .NET side. Both hashes are certified
that way; anything new making a cross-pipeline value claim should be too.

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

`AiSurface.arbitrate : NodeWitness -> IdWitness -> 'Node -> Proposal list -> Arbitration` decides
which subset of N op-script proposals (Phase 59) can land together against one base tree: batch
`Ops.canApplyAll` against the base filters the inapplicable (each rejection carries the op-algebra's
own envelope + the failing op index), then a greedy pass in the **pinned order** (ascending proposal
id) accepts each proposal whose footprint (Phase 78) is `Ops.independent` of everything already
accepted. The result is a **deterministic, total partition** (GP4 — analysis only, the base is never
mutated): the accepted set is mutually independent — by footprint soundness its scripts apply
confluently in any order (`MergedScript` is the pinned-order composition) — and every rejection is
typed + actionable (GP5): `Inapplicable (opIndex, rejection)`, or `Conflicts interfering` naming the
accepted ids (computed against the **full** accepted set) the agent must rebase against. Proposal ids
are queue-assigned and unique (Phase 59); `arbitrate` is total on duplicate-id input, but the
permutation-invariance guarantee assumes unique ids (only then is the pinned order a total order).

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
closed-set envelope — a new case is additive, removing one is breaking.

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
still declined, still by type, still naming the verb.

**A sort is admitted at ANY position in the pipeline, not only as the last step.** It carries no
condition of the kind a `GroupBy` does, because it emits the rows it was handed rather than a
different relation: every step admitted after it — `Filter`, `Project`, `Derive`, and a final
maintained `GroupBy` — reads the order it produced exactly as it would have read the reference's.

**The saving is NOT in the sorting.** A sort evaluates no expression, so it contributes nothing to
`rowsEvaluated` — the same accounting a `GroupBy` gets, and for the same reason. What a widened sort
buys is that the steps *before* it stop re-evaluating every row: on the estate's recompute fixture
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

**`Window` remains declined, deliberately and by type.** The estate's fixture family records no
footprint for it, and this phase's own gate is that a class is not widened before it is measured — so
a bounded-frame window stays `StepNotRowLocal "window"` until a vector exists to measure it against.

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
  set folds under one order and halts under another), and two **coverage vacuity guards** (the
  sample must have exercised both a folding and a halting set — a run that never collided has not
  tested the halt law at all). Seed-replayable; every divergence is **shrunk** by greedy
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
