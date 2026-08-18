# Fuaran.Core — decisions (newest first)

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
