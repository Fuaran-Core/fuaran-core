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
- **`ArtifactWitness<'Node, 'Id>`** (`{ Tree; IdW; Holes; Effect; Bind }`) — the
  artifact-function witness.
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
default would be flagged as a posture regression. No cryptographic hash ships in Core (GP3): the
adversarial branch uses a wide, test-side stand-in that models collision resistance in-budget.

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
rejected by name on decode.

**Enforced, not asserted (Phase 54).** `./verify.ps1` includes a **Fable-compile gate**: the
`tests/fable-smoke/` project references every public package and is compiled with `dotnet fable`, so a
construct that is not Fable-clean fails the green gate in-repo rather than surfacing downstream. (`Double.ToString`
with the round-trip specifier is *not* Fable-supported — float→string must route through
`Wire.Canon.canonicalFloat`; `Double.TryParse`'s style/provider arguments are ignored under Fable but
parse invariantly, which is a benign warning, not a gate failure.)

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
