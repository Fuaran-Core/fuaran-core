# Fuaran.Core — decisions (newest first)

## 2026-08-21 — D17: the IDL splits into a model half and a codegen half, and both ship

**Decided.** From `0.7.0` the IDL engine is two packages. `Fuaran.Core.Idl` holds the model, the
codec, the sampler, the `idl.json` artifact projection and the sanitisation floor; a new
`Fuaran.Core.Idl.Codegen` holds the source emitters, `CodegenError`, the codegen trust boundary and
the stability diff classifier. Both are packable. The namespace does not split — `Gen`, `Trust` and
`Diff` stay `Fuaran.Core.Idl`, so an existing call site changes its package reference and nothing
else.

**The reason is portability, and it was measurable rather than aesthetic.** `Fuaran.Core.Idl` was the
one project under `src/` absent from `tests/fable-smoke`, so "Core is Fable-clean on encode and
decode" had an exception nobody had proven either way — and the estate's browser hosts are Fable. The
obstruction was entirely emitter-side: one `CultureInfo.InvariantCulture` and two `StringBuilder`s,
all three serving the TypeScript source backend. The model, the codec, the sampler and `Sanitize`
touch none of it. Splitting therefore turned an unprovable claim into a gated one by moving the
obstruction rather than by working around it, and `tests/fable-smoke` now compiles the model half like
every other public package.

**The split boundary was already there; it did not have to be invented.** `Encode.encode` returns
`Result<string, string>` and never mentions `CodegenError`, so the error type genuinely belongs with
the emitters that raise it. The sampler's `Rng`, `nextInt`, `pick` and three pools are private to it
and used by no emitter — it sat inside `Gen` by where it was written, not by what it depends on, and
extracting it into `Sample` was a lift.

**Rejected: making the codegen half `IsPackable=false`.** The phase that specified this split was
authored when the whole project was unpackable, and proposed keeping the emitters that way on the
grounds that publishing a generator creates a second, implicit contract — consumers depend on its
OUTPUT, which is harder to version than an API. That argument is sound and is now written down (see
STABILITY.md's "two packages, two promises"), but the conclusion no longer follows: **D14 published
the generator one day earlier, deliberately and with reasons, and named `Gen.fsharpModuleWith` as the
call a second domain makes.** Un-publishing it as a side effect of a portability fix would have
withdrawn that the day after it was granted, and would have left the second domain back where D14
found it. What the split changes is that a consumer now **chooses** the generator instead of
inheriting it with the model — which is the part of the original argument that was actually about
coupling.

**Two smaller consequences, both deliberate.** `Fuaran.Core.Idl.Codegen` ships **no** `fable/` source,
unlike every other packable project here: the dual-pack convention promises a Fable consumer can
compile from source, and this is the one package that cannot keep that promise. And
`TransparentUnion` became public: the split made a genuine cross-package dependency out of what
`internal` had been hiding, since an independent emitter must agree with this codec about which union
cases encode bare or it generates a host that disagrees on the wire. It remains a wart — the rule is
keyed on a hard-coded vocabulary name in a domain-generic engine — and publishing the accessor makes
that visible rather than fixing it.

**Not decided here.** Whether the two halves should ever run on separate version lines. They move
together at `0.7.0` because they were one package a commit ago; a divergence needs a reason, and none
exists yet.

## 2026-08-21 — D16: `fnv1a` is made cross-pipeline exact, and the .NET side is the canonical one

**Decided.** From `0.6.0`, `Hash.fnv1a`'s multiply goes through a private split-half 32-bit form
(`mul32`), so the function computes true 32-bit FNV-1a on .NET **and** under Fable. The .NET values
are unchanged; the transpiled values move to meet them. Released as a MINOR bump, not a patch.

**Why there was anything to fix.** Fable emits `uint32` `*` as a plain JavaScript multiply on
doubles. `h * 16777619u` reaches roughly 3.6e16 — past the 2^53 exact-integer ceiling — so precision
is lost *inside* the operation, and a trailing `&&& 0xFFFFFFFFu` cannot recover what is already gone.
Measured: `fnv1a "a"` was `e40c292c` on .NET and `e40c2930` under Fable, and 120 of a 124-entry
corpus diverged. This is the same hazard the `.+.` masked add solves for addition, one order of
magnitude worse, and it went unnoticed for the same reason: nothing in the repo compared the two
pipelines' *numbers*.

**Why .NET is canonical rather than "whichever is cheaper to change".** `fnv1a` is folded into every
stored content hash, and essentially all of them were minted by .NET processes. Moving the .NET
values would invalidate persisted data across every consuming domain; moving the transpiled values
invalidates only digests minted by a JavaScript host, which the pre-`0.6.0` documentation already
declared non-portable and unusable as a cross-pipeline identity. So the direction was not a
preference — one side had data behind it and the other had a warning label. The fix was designed
around keeping .NET fixed, and the pinned vectors hold it to that.

**Why MINOR and not PATCH.** By the .NET-only reading this is invisible: same signature, same values,
a private helper added. But STABILITY.md declares that these functions' *values* are the contract,
not merely their signatures — and on a supported pipeline the values change. A patch bump would tell
a Fable consumer that nothing observable moved, which is false. Pre-1.0, MINOR is the lane that says
"look before you repin", and this is exactly that.

**The fix had to reach six implementations, not one — and that is the substantive part.** The spine
carried six copies of FNV-1a, each inlined at some point so a package need not take a `Tree`
dependency, and each commented as "the same arithmetic class as the rest of the substrate's portable
hashing" — a claim that was false in every copy. Fixing only `Hash.fnv1a` would have left
**`OpStream.defaultHash` divergent**, which is precisely the harm being fixed: it is the op-stream
chain hash, so two hosts replaying one log would still compute two different chains, while the
release notes said the hash was now portable. That is a worse outcome than not fixing it, because it
converts a known limit into a false assurance. Three copies are deleted outright — `Function` and
`Query` already depended on `Tree`, and `Function.fs` was calling `Hash.fnv1a` two lines from its own
duplicate. Three remain because the dependency genuinely forbids consolidation, and they are now
**verified rather than trusted**: `HashTests` compares each against the canonical function over a
corpus, and the parity probe carries a column per implementation.

**Why the probe covers every implementation rather than the canonical one.** This is the general
lesson, not a detail of this change. A probe that samples the function you were thinking about will
report green for the reason you expected while the copy nobody was thinking about stays broken —
which is what the estate's own audit found here, hours after the canonical fix was written and
believed complete. Going red was verified per implementation, not once: perturbing `OpStream`'s copy
alone turns exactly its column red and leaves the other three untouched.

**Why the guard is a probe and not a test.** A compile gate cannot disagree about a number, and
neither can a .NET suite: reintroducing the naive multiply was measured to leave all 720 tests green
while the transpiled side was wrong on 120 of 124 inputs. So the certification is bought twice over —
an independent 64-bit reference implementation inside `HashTests` pins the .NET half against a
mistake in the split multiply itself, and a scratch Fable build of a corpus run under node and
byte-compared pins the cross-pipeline half. Both were taken go-red before being trusted. The standing
obligation, recorded in `Hash.fs` and STABILITY.md: re-run the probe when either multiply-safe helper
is touched.

## 2026-08-21 — D15: the cryptographic digest is homed here; the default chain hash is not changed

**Decided.** `Hash` ships a pinned pure SHA-256 (`sha256Hex`, `sha256Bytes`, `sha256HexOfBytes`, and
the `utf8Bytes` encoder they hash through) from `0.5.0`. It is the spine's one cryptographic digest,
and there will not be a second. `OpStream.defaultHash` stays FNV-1a.

**Why it is here and not in each consumer.** It already existed twice — hand-ported, verbatim, into
two separate tiers, the second port two days before this decision — because both needed a digest that
was FSharp.Core-only and Fable-clean, and the spine offered only a 32-bit checksum. Two copies of one
crypto primitive is not redundancy, it is a divergence waiting for the patch that reaches one of them:
they were identical on the day of the second port and nothing structural kept them so. The FIPS
vectors have to travel with the implementation for the same reason — a copy that is not itself pinned
is a claim rather than a digest, and each porting tier had to re-derive that suite to know what it
had.

**Why the default chain hash does not move with it.** The tempting follow-on — now that a real digest
is available, make `OpStream.defaultHash` use it — is refused. Every persisted chain in every domain
was written under FNV-1a, so changing the default silently invalidates all of them: a store would
verify before the upgrade and fail after, with nothing in the data saying why. A host that wants
adversarial tamper-evidence supplies `Hash.sha256Hex` through the `HashFn` seam, which is what the
seam is for, and a domain that migrates does so as a recorded event rather than a rebuild. Making it
available and making it default are separate decisions, and only the first is taken here.

**Why this reverses "no cryptographic hash ships in Core (GP3)".** That line conflated two things.
GP3 asks that public surfaces be FSharp.Core-only and Fable-clean; this implementation is both — pure
`uint32` arithmetic, no `System.Security.Cryptography`, compiled by the same Fable gate as every
other public package. What GP3 actually rules out is a host-side crypto *dependency*: keys, keyed
MACs, signers, certified modules. None of those are here and none are proposed; they stay behind the
host's attestation seam. The cost of the conflation was paid entirely by consumers, in ports.

**The two regimes are named, and that naming is the contract.** `fnv1a` is a cache fingerprint — a
staleness stamp over data the same process just produced, where forging it gains nobody anything.
`sha256*` is the crypto digest — anything that becomes a signed head or a record a dispute is read
from. A 32-bit second pre-image is seconds of search, so the two must never be interchanged; they are
separately named so that a call site says which regime it is in, and they differ in output length so
that a silent fallback is caught by shape rather than by review.

**`fnv1a` moved file and did not change.** The `Hash` module now lives in its own `Hash.fs` rather
than at the head of `Tree.fs`, because the digest is consumed across the spine and by domains that
never touch a tree. `fnv1a` and `foldSep` are byte-identical through the move — pinned by their own
vectors in the suite, since every stored content hash in the estate folds through them.

## 2026-08-20 — D14: the IDL engine ships; a vocabulary does not

**Decided.** `Fuaran.Core.Idl` is packable from 0.4.0 and is distributed like the rest of the spine.
A **vocabulary** — the `Idl` value describing one domain's kinds, unions, enums and records — is
**not** distributed from here. It lives as data in the repo of the domain whose contract it is,
which takes a `PackageReference` on the engine and declares against it. There will be no
`Fuaran.Core.Idl.Vocabularies.*` package, and no vocabulary-shaped module in any existing one.

**Rejected: a shared vocabularies package.** It reads as the tidier option — one place to look, one
version to pin — and it is wrong on three counts.

1. **A vocabulary is the domain's contract, so it must move at the domain's cadence.** Housed here,
   every kind a domain admits becomes a release of *this* repo, and every domain's wire inherits one
   release cadence and one reviewer. The admission gates that govern whether a kind may exist are
   written per domain and enforced per domain; the artefact they govern should not sit somewhere the
   gate does not run.
2. **A domain-named type in the `Fuaran.Core.*` namespace outlives every later correction.** The
   spine is domain-agnostic by charter — D7 already refuses a domain dependency even in tests,
   holding the reference witness locally instead. A `Fuaran.Core.Idl.Vocabularies.Ui` would put a
   single domain's vocabulary in the substrate's identity permanently, which is the one kind of
   mistake a rename cannot undo cheaply.
3. **It concedes the point the engine exists to prove.** The engine is generic because a vocabulary
   is an ordinary value a caller supplies. Shipping vocabularies *with* it would make the generic
   surface indistinguishable from a plugin registry, and the second domain's declaration would be
   evidence of nothing.

**What this unblocks, and it was genuinely blocked.** Until 0.4.0 the engine was `IsPackable=false`,
so a domain workspace could not consume it at all. The one domain using it — the F# UI tier — got
its generated structural layer by reaching across a sibling checkout and byte-copying the artefact
this repo's tests happen to commit. That is not a distribution mechanism; it is the absence of one,
and it is why no second vocabulary had been declared. With the engine packaged, a domain declares an
`Idl`, calls `Gen.fsharpModuleWith`, and owns both the declaration and the output.

**The one exception, and why it is an exception rather than a counter-example.**
`tests/Fuaran.Core.Tests/UiIdl.fs` is a UI vocabulary living in this repo. Under the rule above its
home is the UI tier's own repo, and it is not moved here. It is worth being exact about what it
currently *is*: not the UI domain's vocabulary home, but **this repo's engine-certification
fixture** — the only full-scale vocabulary the engine has ever been proven against, and the input to
seven suites that between them certify corpus byte-parity, the compiled-codegen drift guard, the
schema leg, the op leg, the diff classifier and the cross-host fuzz.

It cannot follow the rule yet because neither available route is sound:

- **A package dependency the other way closes a cycle.** The tier depends on `Fuaran.Core.Idl`; this
  repo's tests depending on a tier package would make the two unbuildable from cold in either order.
- **A compile-link across a sibling checkout is worse than the byte-copy it replaces.** It would
  make this repo's build fail whenever a checkout it does not control is absent or moved — trading a
  drift hazard for an availability one.

So the migration is **staged, with a stated completion criterion**: the vocabulary moves to the UI
tier's repo, taking its regeneration guard with it, once the engine's certification no longer rests
on it — either because a Core-owned fixture is grown to comparable scale, or because a second domain
certifies the engine in its own repo. Until then the duplication is bounded to one artefact and
pinned rather than trusted: the tier's committed `Generated.fs` is byte-compared against the
emission on every CI run, so a divergence fails a gate instead of accumulating silently.

## 2026-08-18 — D13: the compute vocabulary's closed sets, and what is deliberately absent (Phase 101)

A demand-side census of the transform algebra (enumerate the intents a declarative pipeline must
express, then check each is EXPRESSIBLE — rather than waiting for a failure to harvest) found the
verb set close to complete, with the remaining gaps concentrated in **asymmetries of otherwise-closed
sets**. Those are the cheapest gaps to close and the most expensive to leave: a reader who finds one
member of a familiar pair reasonably assumes the other.

**Closed (all additive — every pre-existing pipeline's wire is byte-unchanged).** `Transform` gains
`Intersect` / `Except` beside `Union`, as **multiset** ops keyed on the full row (`· Distinct`
recovers the SQL set forms, exactly as it does for `Union`); `JoinKind` gains `Semi` / `Anti`;
`AggFn` gains `CountDistinct`; `WindowFn` gains `DenseRank`, `CompetitionRank`, `NTile n`,
`CumulMax`, `CumulMin`, `RollingSum`; `ScalarFn` gains `Sqrt`, `Least`, `Greatest`, `IndexOf`.
Row identity for the set ops and for `CountDistinct` is the **same canonical token `Distinct` dedups
on** — so `NaN` is one value, `-0.0`/`0.0` coincide, `Null` matches `Null`, and an `Int 1` never
matches a `Float 1.0`. That is a different rule from a `Join` key (`cellEq`, where a null matches
nothing), and the difference is intentional: it is what SQL's set operations do and what makes the
result host-identical rather than host-comparison-dependent.

**Two corrections to the census the closure produced, worth more than the additions.**

1. **`Rank` was already DENSE.** It computes `1, 1, 2` — SQL's `DENSE_RANK()`, not `RANK()`. So the
   missing member of the ranking family was never "dense rank"; it was the **gapped** rank, which had
   no spelling at all. `DenseRank` is therefore the explicit (byte-identical) name for what `Rank`
   already does, and `CompetitionRank` is the genuinely new capability. `Rank` is **not** re-pointed
   at the gapped semantics: that would silently change every existing pipeline's output — a major
   bump, not an additive one.
2. **`Semi` is not expressible; `Anti` is.** The prior reading was that both reduce to `Left` + an
   `IsNull` filter. `Anti` does (`cellEq` never matches a null, so a matched row's right key is
   always present, and each unmatched left row yields exactly one output row). `Semi` does **not**: a
   `Left` join has already fanned a left row out once per right match, and no downstream filter undoes
   that — a `Distinct` would also collapse duplicates the input legitimately carried. That is the
   argument for the case, and it is asserted as a test rather than left as prose.

**Declined, with reasons, so the next census does not re-file them.**

- **No clock (`Now` / `Today`).** The evaluator is a pure function of `(table, env, pipeline)`. A
  clock makes the same pipeline over the same data produce different answers — which is precisely
  what `Conformance.transformLaws` byte-identity and deterministic replay exist to forbid. The
  intended route is a host-injected `Param` (D12's binding mechanism): bind `"today"` once at the
  edge and the pipeline stays total; `DateDiffDays` does the arithmetic.
- **No regex.** A pattern is not a cross-host value: .NET, JS, Go and Rust differ on syntax, escapes
  and Unicode classes, and the backtracking engines differ on worst-case *time*, which a total
  algebra cannot absorb. `Contains` / `StartsWith` / `EndsWith` / `IndexOf` / `Substr` / `Replace`
  cover the common intents; anything else is a host-side derived column.
- **No `Pow` / `Log`.** IEEE-754 does not require transcendental functions to be correctly rounded,
  so two conformant hosts may differ in the last ulp — and one ulp is a different byte in the
  canonical float layout, i.e. a parity failure. `Sqrt` **is** IEEE-754-exact, which is why it is
  present and they are not; integer powers compose from `Mul`.
- **No explode/flatten and no `Split`.** Both need a list-valued cell, and D12 rejected exactly that
  (`CellList`) for blast radius and model coherence. `Unpivot` is wide→long **across columns** and
  genuinely does not cover it. So the gap is in the *type model*, not the verb set: closing it is a
  major model change, not an additive verb, and until then a host flattens its JSON before handing
  Core a table — the same materialisation it already performs for every other source.
- **No `PadLeft` / number formatting.** The algebra pins **values**; presentation belongs to the
  render tier, which knows the locale and the column width and this does not.

Naming note: the two-argument extremes are `Least` / `Greatest` (the SQL spelling) rather than
`Min` / `Max`, because `AggFn` already owns those two names in this namespace and a second binding
would shadow it for every unqualified use.

## 2026-07-19 — D12: list-valued params resolve by substitution, not an env change (Phase 91)

The multi-select-chip binding is `ColExpr.InParam of ColExpr * name`, riding the existing `in`
wire tag with `param` in place of `items` (exactly one of the two — the same duality as the flat
filter step's `param`/`value`), resolved by **substitution** (`substituteListParams :
Map<string, Cell list> -> …`, rewriting `InParam(x, n)` to `InList(x, <literals>)`) — mirroring
how scalar `Param`s resolve via `substitute` under the certified `paramLaws`. Rejected: (a) a
`CellList` `Cell` case — the widest blast radius (every codec, aggregate, comparator, all nine
hosts) and list cells are meaningless in the columnar model; (b) an `EvalEnv` shape change —
breaks the public eval API for one feature when substitution already models binding; (c)
host-side expansion — forks the declarative pipeline artefact per host. Consequences: an
`InParam` reaching evaluation unbound is a strict `UnboundParam` (scalar-param parity); "empty
selection ⇒ no constraint" stays host-side pruning policy; `paramsOf` surfaces list params in
the scalar namespace, so reactivity/lease derivation is unchanged. Demand evidence: the
2026-07-19 capability sweep + a downstream consumer's capability-demand log.

## 2026-07-09 — D11: public conformance is pinned to `canonicalConfig`; bespoke chain pre-images are non-portable

`StreamConfig.Payload` (the pluggable chain pre-image binding, Phase 255) has two legitimate uses
that pull in opposite directions: a **migration seam** — a domain whose persisted streams use a
legacy chain format (Documents `"%d|%s|%s|%s"` + `"genesis"`, Calc / Geom their own) verifies the
existing streams under a bespoke `Payload`, then `rehash`es to the canonical form, with no flag-day
re-hash of history — and an **interop hazard**: a stream hash-chained under a bespoke pre-image
verifies only for a reader who already knows that config, so it is not portable across independently
built hosts. Decision: **public / cross-host conformance is pinned to `OpStream.canonicalConfig`**
(the `{seq, actor, op}` envelope + `""` genesis). A stream claiming the canonical `core@1.0`
profile MUST verify under `canonicalConfig`; a bespoke `StreamConfig.Payload` is a **host-private
profile, non-portable by declaration** — legitimate only as the transient input side of the
`verifyChainWith <legacy>` → `rehash <legacy> canonicalConfig` migration, never as a shipped
interchange format. No new conformance law is required: `Conformance.streamLaws` already exercises
append / verify / replay over the default `canonicalConfig` pre-image, so "the canonical profile is
verifiable under `canonicalConfig`" is **definitional**, not a testable property — and the
non-portability of a bespoke pre-image is a **declared boundary**, not something a property test can
assert. Recorded in STABILITY.md ("Chain pre-image portability"). Design decision (2026-07-09).

## 2026-07-09 — D10: content-addressed side-tables are the sanctioned host-private-metadata mechanism

Attaching host-private semantics to the public content-addressed identifiers Core already computes
for integrity reasons — node ids, chain heads, `Tree.contentHash` / `encodeHash`,
`Validator.canonicalCodes` — via **host-side lookup tables keyed by those ids** is the **sanctioned**
mechanism for private per-artifact metadata. It needs no public metadata slot: a content address is
an unforgeable foreign key (tampering changes the key), so a host joins its own off-repo tables
without the public tier ever carrying, hashing, or interpreting the attached data. This closes the
door on future public-metadata-slot pressure and is **consistent with the rejected per-op
witness-metadata seam (adoption fork F8, see STABILITY.md "Attributed-stream lift")**: the same
"metadata / provenance rides *beside* the public record, never *inside* it" posture, applied to
content addresses instead of op records. Recorded so a future request to add a public metadata field
is answered by this convention rather than by growing the surface. Design decision (2026-07-09).

## 2026-07-09 — D9: the rule-of-three bends for a clear downstream unblock

The rule-of-three evidence gate (a new Core strand / surface ships only behind three real consumers)
is a guard against *speculative* surface — **not a hard count.** When a Core feature *clearly unblocks
a desirable feature in a real downstream consumer*, a **single concrete, unblocking consumer is
sufficient evidence to ship**: the feature is not speculative, its consumer already exists, and waiting
for two more hypothetical adopters is gatekeeping that delays clearly-valuable work. Design decision
(2026-07-09). Concretely: the `Fuaran.Core.Lease` strand (Phase 84) shipped on the strength of a single
concrete consumer that hand-rolls the claim/coordination the strand replaces — the further candidate
consumers are corroboration, not prerequisites. The gate still means *evidence of real use* — one real
unblock is real use — so record the driving consumer in the shipping phase's outcome.

## 2026-06-18 — D8: Decode is fully portable — one parser, both pipelines (Phase 241)

The original extraction shipped decode as two `#if !FABLE_COMPILER` System.Text.Json paths
(`Wire.Decode`, `OpStream.fromJsonl`), so a Fable-only host could not `verifyChain`/replay/
round-trip in-browser. Phase 241 retires the asymmetry: `Wire.Json.parse` is a hand-rolled
recursive-descent parser → the `JVal` model (FSharp.Core only), `Wire.Decode`'s combinators
run over `JVal` with no `#if`, and `OpStream.fromJsonl` carries its own self-contained line
scanner (so `OpStream` keeps its no-Wire-dependency posture, D2). The parser is *always*
compiled, so the same code .NET runs is what Fable emits — the existing Wire/OpStream
conformance tests now exercise it as regression coverage. `render`/`parse` are inverses over
canonical wire JSON; the wire model has no `null` (rejected by name on decode).

## 2026-06-17 — D7: Reference witness in tests, no domain dependency

Conformance is proven against an in-repo **reference witness** (`tests/.../Reference.fs`):
a tiny string-id domain (`RNode`) that instantiates every witness. This lets the generic
functions be exercised end-to-end **without depending on any domain repo** — build Core
end-to-end first, rather than substituting it into a domain to prove it. Domain adoption
lives outside this repo.

## 2026-06-17 — D5: `IdWitness` carries no `fresh`

The roadmap phase sketched `toString`/`ofString`/`fresh`/`equals`. We dropped `fresh`:
the generic functions never mint ids — hygiene (Fork 2) derives addresses deterministically
from existing ids via `ToString`/`OfString`, and id minting is a domain concern. Keeping
`fresh` out keeps every generic function pure and deterministic (no hidden id source),
which matters for replay/caching soundness. Domains mint ids their own way.

## 2026-06-17 — D4: Identity is a witness parameter, not a fixed type

The one genuine cross-domain divergence (surfaced during the extraction assessment):
string ids (Doc/Calc) vs Guid ids (UI/Music), 2-2. Resolved as `IdWitness<'Id>` so each
domain keeps its representation over one `Core.Tree`. This is the concrete payoff of the
rule of three — with two data points we'd have guessed; with four/five the axis is visible.

## 2026-06-17 — D3: Extraction order — lowest-risk layers first

Built in the lowest-risk order: `OpStream` (highest genericity, cleanest two-seam witness)
→ `Ops` error-envelope (highest value — the AI-feedback protocol) → `Tree` (where the
identity decision lives) → `Wire` + `Validator` frameworks → `Function` (composes on Tree).

## 2026-06-17 — D2: `OpStream` and `Wire` are standalone; the rest depend on `Tree`

`Core.OpStream` is generic over `'Op`/`'State`/`'Rej` and needs no tree — it is the
cleanest seam, so it depends on nothing. `Core.Wire`'s combinators + corpus tooling are
likewise tree-free. `Core.Ops`/`Core.Validator`/`Core.Function` operate over the tree
witness and reference `Core.Tree`. This keeps the highest-genericity layers maximally
reusable (an op-stream-only or wire-only consumer pays for nothing else).

## 2026-06-17 — D1: `Severity` is `[<RequireQualifiedAccess>]`

`Severity.Error` would otherwise shadow `Result.Error` in any consumer that `open`s
`Fuaran.Core` — a real footgun (it bit the tests immediately). `RequireQualifiedAccess`
forces `Severity.Error` and keeps the bare `Error`/`Ok` as `Result`. Domains adopting the
validator framework inherit the safe spelling.

## Forward-looking notes

- ~~**Decode is .NET-guarded.**~~ _Resolved 2026-06-18 (D8, Phase 241): decode is now fully
  portable — `Wire.Json.parse` + `Wire.Decode` + `OpStream.fromJsonl` are FSharp.Core-only and
  run under both pipelines. No host-side decode boundary remains._
- **DAG op-stream** (Wave 27) is a future `Core.OpStream.Dag.*` follow-on over the linear
  spine extracted here.
- **Domain adoption** (re-expressing UI/Calc/Doc/CAD/Office over `Fuaran.Core.*`) is
  intentionally not in this repo; it lands per-domain.
