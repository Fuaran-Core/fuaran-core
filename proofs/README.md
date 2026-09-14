# proofs/ — the F\* model of the DAG fold

**Status: GO** (Phase 131, 2026-09-12). All three exit criteria met — the confluence proof is
reproducible on the pinned prover, the extracted model agrees with production over every lane set
the differential host draws, and the model reads beside the F# in one sitting. The decoder-totality
theorem is the next phase, in F\*.

This directory is the mechanised half of the correctness story whose differential half already
existed: Phase 80 certified two-script confluence, Phase 83 the two-head `Dag.reconcile`, Phase 100
the N-lane fold-confluence pack. Those are property tests over sampled orders; this is the same law
as a theorem, and the theorem's model run as a sixth host through the same differential test.

| File | What it is |
|---|---|
| `DagFold.fst` | The model: `Ops.independent`, `Dag.conflicts`, `Dag.reconcileMany`, the replay and `FoldConfluence.foldOnce`, over an abstract `op`/`state`/`rej`, with `fold_confluence` proved; and, since Phase 134, the DAG beneath them — `DagNode` / `Dag.T` / `Dag.ancestorsOf` / `Dag.between` / `Dag.betweenOps` for the base-plus-N-chains shape, with `between_chain` and `fold_confluence_dag` proved. Every definition names its F# counterpart. |
| `oracle/DagFold.fs` | **Generated** — the model extracted to F# by F\*'s own code generator. The suite runs it beside production. |
| `WireDecode.fst` | The second model (Phase 135): `Decode`'s combinators, a reference vocabulary with its encoder and kind-dispatch node decoder, and the Phase 102 read policy, with `decode_total`, `decode_node_wf`, `decode_encode_roundtrip` and `lenient_agrees_off_policy` proved. Shares nothing with `DagFold.fst` but `oracle/Prims.fs`. |
| `oracle/WireDecode.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `TreeOps.fst` | The third model (Phase 133): the skeleton-op tree algebra — `Ops.apply` with its `Rejection` envelope and `Ops.footprint`, over the tree as the `NodeWitness` shows it — with `tree_independence_diamond` proved, which is the fold theorem's one domain hypothesis. Unlike the two above it does NOT share only `Prims.fs`: it `open`s `DagFold`, which is what makes the composite an instantiation rather than a second model. |
| `Skeleton.fst` | The composite (Phase 133): `DagFold`'s fold theorem instantiated at `TreeOps`, so `skeleton_fold_confluence` holds with no domain hypothesis left. Thin on purpose — the argument is in the two halves it joins. |
| `oracle/TreeOps.fs`, `oracle/Skeleton.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `oracle/Prims.fs` | The `Prims` names the F# backend emits and the release does not ship. |
| `oracle/Fuaran.Core.Proofs.Oracle.fsproj` | The oracle assembly. Never packed; nothing extracted enters the shipped kernel. |
| `fstar-pin.json` | The pinned F\* release (which bundles Z3) and its hash. |
| `check.ps1` | The proof leg: check with the pin, re-extract and diff against the committed oracle, run the host. |
| `../tests/Fuaran.Core.Tests/ProofOracleTests.fs` | The differential host (`Proofs.Oracle`), and the family that measures the theorem's hypothesis on the reference witness. |

## The theorem

`fold_confluence` (the last definition in `DagFold.fst`): for every lane set `ls1` and every
re-arrangement `ls2` of it — any two lists related by the inductive permutation relation `perm`,
Coq's `Permutation` — and every base state, `fold_once ls1` and `fold_once ls2` are **equivalent
outcomes**:

- both fold, to the **same state** (propositional equality, not a hash); or
- both halt, with the **same canonical report** — the same set of (shape, address, unordered op
  pair), which is exactly what `FoldConfluence.canonicalConflictReport` renders.

No lane set folds under one order and halts under another. The proof is by induction on the
permutation derivation, in two halves: the halt report is a permutation invariant of the pairwise
sweep (`all_conflicts_perm`), and a conflict-free set replays identically under any order
(`replay_perm`), with the lemma that `Dag.conflicts` is empty **iff** every cross-lane pair is
`Ops.independent` (`conflicts_nil_independent`, both directions — the Phase 64/83 cross-validation
promise, mechanised) joining the two.

**The one domain hypothesis** is `independence_diamond`: for this domain, two ops whose footprints
`independent` declares disjoint and which **both apply** at a state each apply after the other and
reach the same state. That is the domain's promise, and it is exactly the promise a shipped witness
certifies — Phase 78/80 for the tree algebra by sampling, Phase 100 for a domain's own witness by
sampling. Nothing proves it; everything in the fold half rests on it; and the `Proofs.Oracle`
family now **measures it on the reference tree witness**, so the hypothesis is not merely stated
about the domain it names.

The fold half asks one thing more, and it is a statement about the lane set in hand rather than a
promise about the domain: `lanes_apply` — every lane applies cleanly from the base state. That is
the lane set `foldOnce`'s generators produce, and it is the set the fold half was always about. A
lane set with a **rejecting** lane is outside the fold claim, exactly as it is outside Phase 80's;
`outcome_equiv` still has a both-rejected arm, and the theorem simply says nothing about when it is
taken. **The halt half asks for neither** — `fold_confluence_halt` is stated and proved with no
hypothesis about `apply` at all, because whether a lane set halts and what it halts with are
properties of the footprints alone.

### The DAG beneath it (Phase 134)

`fold_confluence` is stated from the lane deltas. Production does not have them: it has a
content-addressed DAG and rebuilds each lane's delta from it. Section 11 of `DagFold.fst` models
that step for the shape `foldOnce` builds and every local-first deployment has — **one shared base
node and N linear chains, one per writer** — as `DagNode` / `Dag.T` / `Map.tryFind` /
`Dag.ancestorsOf` / `Dag.between` / `Dag.betweenOps`, clause for clause.

Three results, all with no admits:

- **`between_chain`** — `Dag.between` over a linear lane off the base returns that lane's nodes,
  in order (`between_ops_chain` is its op projection). The whole layer computed: the head's
  ancestor closure, the base's, the topological order, the difference and the lookup.
- **`reconcile_many_dag_eq`** — `Dag.reconcileMany` FROM the DAG equals the deltas-first
  `reconcile_many` on the lanes that were appended.
- **`fold_confluence_dag`** — the confluence theorem restated from the DAG: the same heads over
  the same base fold to the same state, or halt with the same canonical report, however the heads
  arrive.

**Hashing is abstracted, and what replaces it is a named premise.** Node ids come from an opaque
`mint` standing for `Dag.nodeHash`. What content addressing buys the recovery is that the ids come
out DISTINCT, so that is what the model assumes — `resolves`, with `resolves_of_distinct` the
bridge from plain id distinctness to the form the recovery consumes. **The hash-collision
assumption is exactly that assumption and no other**: two nodes sharing a content id is the only
way `lookup` returns a node other than the one a chain named, and it is the only way this proof
says nothing.

**And one step is deliberately NOT mechanised, which is where the honest reading of "proved" stops
here.** Production chooses its topological order with Kahn's algorithm, draining a ready frontier
smallest-id-first; the model takes the reverse of the parent walk. On a spine every pair of the
closure is comparable under the ancestor relation, so exactly one topological order exists and the
two must coincide — but that sentence is argued, not proved. Mechanising it means showing that a
distinct enumeration of a spine respecting each node's single parent is forced, and then that
Kahn's drain produces such an enumeration; that is the honest successor to this phase. Everything
downstream of the order is proved, and the differential below measures the order itself against
the real `Dag.betweenOps`.

Also outside the model, and named so it is not assumed in: **`Dag.mergeBase`**. It is not on this
path at all — `foldOnce` hands `reconcileMany` the base node's id directly — so nothing here says
anything about locating a divergence point, and the general topological order over an arbitrary
MERGE DAG stays where Phase 134's own statement put it, out of scope.

_(Phase 131 stated the hypothesis as `independence_sound`: independent ops commute at every state,
**rejections included**. That premise is false of the reference witness — `Rejection.UnknownNode`
carries `addressable`, the whole id set of the tree it was raised against — so the theorem was
sound about a domain nobody has. Phase 132 replaced it with what the algebra keeps. See the claims
ladder's "not claimed" entry, and the `Proofs.Oracle` case that holds the refuting witness.)_

`--report_assumes error` is on: the module carries no `assume`, no `admit`, no `assume val`.

## What the corpus covers

The differential host draws lane sets from three pools and compares, per lane set and per sampled
arrival order, the outcome (folded hash / canonical halt report / rejection) and the merge script
(the composed op sequence) of production — `Dag.reconcileMany` through a real DAG, rendered as
`FoldConfluence.foldOnce` renders it — against the extracted model fed the lane deltas directly:

| Pool | Lanes | Trials | Both classes exercised |
|---|---|---|---|
| the reference tree witness (Phase 100's generator) | 3 and 4 | 120 + 60, all orders | yes (asserted) |
| the work-plan domain (Map state, its own footprint) | 3 and 5 | 150 + 40 (5 lanes: 24 sampled orders) | yes (asserted) |
| the wire corpus's `ops/` fixtures, projected to footprints | every pair and triple, plus two-op lanes | ~1.8k lane sets × all orders | yes (asserted) |

On the two domain pools the host also asserts the theorem's instance on the extracted code: the
oracle's own outcomes across all sampled orders are one outcome. The go-red case hands the oracle
a blind footprint and requires the comparison to fail and shrink to two lanes of one op, so a green
report is known to be a comparison that can lose.

**Since Phase 134 the DELTA RECOVERY is measured too, over the same three pools.** Production's own
`Dag.ancestorsOf` / `topoOrder` / `Dag.between` / `Dag.betweenOps`, on the DAG `foldOnce` actually
builds, against the model's recovery over the **same nodes** — the ids the model walks are the
content hashes production minted, so a disagreement can only be about the recovery and never about
the hash. A sixth case runs `fold_once_dag`, the model's DAG-shaped entry point, end to end against
production and against the deltas-first oracle beside it, so a divergence says which half moved.

| Pool | Lanes | What is compared |
|---|---|---|
| the reference tree witness | 3 and 4 | per-lane `betweenOps` against `between_ops`, 120 + 60 trials |
| the work-plan domain | 3 and 5 | the same, 150 + 40 trials; and `fold_once_dag` end to end, 120 trials |
| the wire corpus's `ops/` fixtures | every pair and triple, plus two-op lanes | the same |

Its **go-red** re-parents each lane's first node onto the previous lane's head. Ids are untouched,
so every lookup still succeeds and the only thing that has moved is the shape the recovery walks —
which is the thing under test — and the comparison is required to fail. Two vacuity guards ride
with it, in the pack's posture: a run that recovered no ops compared two empty lists, and a run
whose every lane was one op walked no chain at all. Both are asserted.

**And the theorem's HYPOTHESIS is measured, not only stated.** Over the same generator, for every
op pair the model's own `independent` declares disjoint and every state where both ops apply, the
host checks the diamond directly on the tree `apply`: each applies after the other, and the two
orders reach the same tree. A sample-adequacy guard reports how many (pair, state) triples actually
met the premise, because a run that never met it measures nothing; a blind footprint must break the
same sample, so this measurement too is known to be one that can lose. A third case holds the
witness that the **stronger** clause is unavailable here: two footprint-independent inserts, one
under an absent parent, reject in both orders with *different* `UnknownNode` envelopes — which is
the claims-ladder entry below, as an assertion that goes red if the algebra ever changes back.

## The gap between model and production — the claims ladder

What may be said, and at what strength, per the attested-stack programme's §6:

1. **Proved (machine-checked, no admits).** On the model, for every N and every permutation:
   the fold-confluence law above — the fold half under `independence_diamond` for a lane set whose
   lanes all apply from the base state, the halt half under nothing at all. **And, for the
   base-plus-N-chains shape, the DELTA RECOVERY beneath it**: `between_chain` (the delta of a
   linear lane off the base is that lane's ops, in order), `reconcile_many_dag_eq` and
   `fold_confluence_dag`, under the id-distinctness premise named at level 3 and with the CHOICE
   of topological order named there too. F\* 2026.09.06, Z3 4.13.3, every query 3/3 under
   `--quake 3`, cold-cache checks on three seeds and at a quarter of the rlimit the leg runs with.

   **And, since Phase 133, the tree algebra's own diamond** — `TreeOps.tree_independence_diamond`,
   which discharges that hypothesis for `SkeletonOp` rather than sampling it, and
   `Skeleton.skeleton_fold_confluence`, the composite. See "Theorem 2" below for what the composite
   covers and the two boundaries it names; this level's sentence about the fold theorem itself is
   unchanged, because the theorem is still generic over a domain and still carries the hypothesis
   for any other one.
2. **Differentially tested.** Production's `betweenOps` recovery of each lane's delta from a
   content-addressed DAG — now against the model's own recovery on the same nodes, not only
   against a model that was handed the answer — its pairwise `conflicts` sweep, `reconcileMany`'s
   composition and `foldOnce`'s replay and rendering, together and separately, over the pools
   above. Agreement is over sampled lane sets and the `arrivalOrders` sample (exhaustive to 4
   lanes, 24 orders above), never over all inputs. Since Phase 133 the same level also carries
   the tree algebra's own differential — `Ops.apply` and `Ops.footprint` against the extracted
   `TreeOps` over the generated op pool and every state a prefix of it reaches — with its own
   go-red and its own adequacy guard; see "Theorem 2" below.
3. **Assumed, and stated as such.**
   - `independence_diamond` — the domain's promise, and the only domain hypothesis the theorem
     carries. Independent ops that **both apply** at a state each apply after the other and reach
     the same state. This is Phase 80's law and no more than it: interleaving totality (accepted
     scripts stay accepted) plus confluence, at op granularity. For **a domain's own witness** it
     is still **sampled**, never proved — Phase 100, and the `Proofs.Oracle` diamond family for
     the reference witness directly against the extracted model's own `independent`. Sampling is
     what this level means, and the theorem is generic over the domain, so this entry does not
     go away.

     **For `SkeletonOp` it has left this level.** Phase 133 proves it — `TreeOps.fst` models the
     tree algebra and `tree_independence_diamond` discharges the hypothesis for it, so what Phase
     78/80 certified by sampling is now a theorem at level 1. Two boundaries come with that and
     are stated as their own entries below rather than folded into this one, because they are
     about the tree algebra and not about the fold.
   - **The lane set is assumed to apply.** The fold half is stated for a lane set whose every lane
     applies cleanly from the base state (`lanes_apply`), which is what `foldOnce`'s generators
     produce and what the differential host draws. Nothing is proved about a lane set one of whose
     lanes rejects; the halt half still covers it whenever it halts.
   - **Node ids are distinct** — the hash-collision assumption, and since Phase 134 the ONLY thing
     the delta recovery assumes about content addressing. `Dag.nodeHash` is injective on
     (sorted parents, actor, encoded op) unless the hash collides; two nodes sharing a content id
     is the only way `lookup` returns a node other than the one a chain named. The model states it
     as `distinct_ids` and consumes it as `resolves`, with `resolves_of_distinct` between them.
   - **How the topological order is CHOSEN.** Production runs Kahn's algorithm, draining a ready
     frontier smallest-id-first; the model reverses the parent walk. A spine admits exactly one
     topological order, so on this shape they coincide — argued here, measured by the differential
     against the real `Dag.betweenOps`, and not mechanised. Everything downstream of the order is
     proved.
   - **`Dag.mergeBase` is outside the model**, because it is outside this path: `foldOnce` hands
     `reconcileMany` the base node's id directly and never locates a divergence point. Level 2 is
     the only evidence about it, and the general topological order over an arbitrary MERGE DAG is
     not claimed at any level.
   - **The extractor and the F# compiler are trusted.** The proof leg holds the committed oracle
     to a fresh extraction byte for byte, which makes "the oracle is the model" a checked claim;
     it does not make the F# backend correct. The backend is second-class upstream (findings
     below), and this is the least-examined link in the chain.
   - **Sets are lists.** The model reads F#'s `Set<string>` as lists under membership; the
     host compares canonical (deduplicated, sorted) reports, never raw conflict lists, so
     multiplicity and order in the model's reports are unobserved by construction.
   - **The tree the skeleton algebra runs over is id-unique** (Phase 133). Not an oversight: the
     diamond is FALSE without it, because `Tree.tryFind` returns the first match in document order
     and a reorder moves document order. Nothing in the estate produces such a tree, but no
     shipped type carries the invariant. "Theorem 2" below has the argument.
   - **The composite's op alphabet excludes a nested `Batch`** (Phase 133). Three of the fifteen
     pairs, all the same shape, and `Ops.apply` threads a `Batch` exactly as the fold threads a
     lane, so it removes no behaviour — it declines to nest one lane inside another. Counted in
     "Theorem 2" below.
4. **Not claimed.**
   - **Rejection identity.** That two independent ops, one of which rejects, reject *identically*
     whichever ran first. The reference algebra cannot keep it and the theorem no longer asks for
     it: `Rejection.UnknownNode` carries `addressable` — the whole id set of the tree it was raised
     against — and `ReorderMismatch` carries the parent's current children, so an op that rejects
     at `s` rejects with a **different envelope** after an independent op has inserted a node. Both
     orders reject; they do not reject identically. Phase 131's `independence_sound` demanded this
     clause, which made the theorem sound about a domain no shipped witness is; Phase 132 dropped
     it, and a `Proofs.Oracle` case holds the refuting witness so the sentence goes red if the
     algebra ever changes back.

   - **Delta recovery over a MERGE DAG.** `between_chain` is stated for the base-plus-N-chains
     shape, which is what `foldOnce` builds and what the fold-confluence pack certifies. A DAG
     whose heads have already been merged has nodes with two parents, `mergeBase` on its path, and
     a topological order that is genuinely a choice rather than a forced one; none of that is
     modelled, and a consumer folding over already-merged heads is outside every level here.

   Also not claimed: anything about the linear `OpStream`, about `Dag.replayTo`'s order, about the
   engine's Lamport projection order (the roadmap engine's own certification of its fold over
   the real `RoadmapOp` union is roadmap-engine#343; this theorem is what makes its choice of
   total order canonical-form-only), or about any domain's reconciliation policy — the model,
   like production, decides nothing and applies nothing on a halt.

"Formally verified" is spent on level 1 alone.

Theorem 1 — decoder totality — carries its own ladder of the same shape, in its own section below;
what is said at each level there is said about the decode combinators and about nothing else.
Theorem 2 — independence soundness for the tree algebra — carries a third, in the section after it;
what is said there is said about `SkeletonOp` and about no other domain's witness.

## Exit criteria, with evidence

1. **Reproducible — met.** `check.ps1 -Runs 3` verifies the module three times from a cold
   cache with `--quake 3` (each query 3/3 over varying Z3 seeds); CI's `proofs` job runs exactly
   that on `windows-latest` on every push. Locally the module also checks at `--z3rlimit 10` (the
   leg uses 40) and under seeds 1, 2 and 3. There are no proof hints to commit, because the
   pinned release removed them (finding 1) — reproducibility rests on the pin, the seed sweep and
   the margin, not on a replay file.
2. **Agrees — met.** The `Proofs.Oracle` cases are green: no divergence over any pool, all
   three outcome classes exercised (folded and halted asserted non-vacuous; rejected covered by
   `LaneFoldOutcome` equality), and the merge script equal, not merely its hash. Since Phase 132
   the same family also measures the theorem's hypothesis on the reference witness, with its own
   adequacy guard and its own go-red; since Phase 134 it also measures the delta recovery against
   production's own DAG, with its own go-red and two vacuity guards.
3. **Readable — met.** `DagFold.fst` is ~1,300 lines with its commentary, of which sections 0–4
   and 11 — everything the oracle is extracted from — are under 500, and every definition is
   captioned with its F# counterpart. The structural differences a reviewer meets are stated
   where they are made: lists-for-sets at the top, and in section 11 a LIST as the walk's fuel
   (one step per node) and a locally-spelled `found` where F# has `option`, both so that the
   extracted oracle needs nothing of `Prims` beyond what section 0 already needed.

## Findings — the F\* bet, measured

The phase called the choice of F\* an untested bet and asked for the backend's quality as a finding
either way. What was found, in the order it was hit:

1. **Proof hints no longer exist.** F\* 2026.09.06 (and the releases since the change) has
   removed `--record_hints` / `--use_hints` / `--hint_dir` outright — `.hints` files are neither
   read nor written, and Z3 is no longer asked for unsat cores. The phase's "check from committed
   hints" cannot be done on any current release. `--quake N` (repeat each query N times over
   varying seeds) is the robustness tool that remains, and `--ext context_pruning` is what
   upstream names as the successor for hint-style pruning. The pin is therefore load-bearing:
   F\* releases **weekly**, and a floating prover is a gate that changes under you.
2. **The F# backend ships no runtime.** `--codegen FSharp` emits code referencing `Prims.list`,
   `Prims.string`, `Prims.bool` and `Prims.op_Equals`, and the release carries an OCaml `ulib`
   only. Sixteen lines of aliases (`oracle/Prims.fs`) close it for a model that keeps its list
   helpers self-contained; a model leaning on `FStar.List.Tot` would need each used function
   ported by hand.
3. **The emitted layout is pre-F#-8.** Match arms sit at column 1 inside a parenthesised lambda,
   which F# 10's strict indentation rejects as an error. The oracle project compiles with
   `--strict-indentation-` and `FS0058` silenced, for that project only.
4. **A ghost inductive extracts unless told not to.** The `perm` relation extracted as a DU with
   `obj` index parameters — legal F#, dead code. `[@@ noextract_to "FSharp"]` on the type keeps
   it out; lemmas are erased on their own.
5. **Extraction needs a checked module first.** `--codegen` on an unchecked file refuses with
   "cross-module inlining expects all modules to be checked"; the leg checks with
   `--cache_checked_modules` and extracts from the cache.
6. **The proof itself was cheap.** The model verifies in under three seconds; the two proof
   iterations needed were an implicit type parameter on the indexed `perm` type (made explicit)
   and a `forall_intro_2` whose predicate F\* could not infer inline (a named helper lemma). No
   rlimit tuning, no tactics, no SMT patterns beyond the membership algebra.

On the operator's familiarity argument — an ML-family model of an ML-family kernel — the finding is
that it held: the model is the F# with `Set` spelled as `list` and `Result` spelled as `outcome`,
and the extracted F# is legible enough to diff against the source by eye. The cost landed on the
backend's edges (findings 2–4), each closed in a few lines, not on the modelling.

## Running it

```powershell
pwsh ./proofs/check.ps1            # check (once), re-extract + diff, run the oracle host
pwsh ./proofs/check.ps1 -Runs 3    # what CI runs
pwsh ./proofs/check.ps1 -Extract   # after editing a model: rewrite its oracle/*.fs, then commit it
pwsh ./verify.ps1 -Proofs          # the whole repo gate plus the proof leg
```

Every model in the script's `$modules` list goes through all three steps, and `-Runs N` means N
cold-cache verifications of all of them; adding a model is adding its name to that list. The first
run downloads the pinned release (~200 MB, hash-verified) into `proofs/.fstar/`; `FSTAR_HOME`
pointing at a matching release skips that. Editing a `.fst` without re-extracting fails the leg
with "oracle drift" — that is the point, not an inconvenience.

## Theorem 1 — decoder totality (Phase 135)

`WireDecode.fst` is this directory's second model, and the programme's WS6.1 **theorem 1**. The
Fuaran wire is JSON with a kind-tag discipline, so this is a hand-written model of the decode
combinators over an abstract JSON value rather than an EverParse artefact — EverParse targets
binary formats and would say nothing about the layer where the estate's decoders actually live.

It models `Fuaran.Core.Decode` clause for clause — `getProp`, `asString` / `asInt` / `asBool` /
`asFloat`, `kindOf`, `strField` / `intField`, and `mapList`, the array walker and the only walker
`Decode` has — with the error **messages** reproduced verbatim rather than classified, because
naming what was expected is most of what these combinators are for. On top of them sit a reference
vocabulary, its `Json.kindObj` encoder, and the kind-dispatch node decoder a domain writes from
those combinators.

Four things are proved:

- **`decode_total`.** Every combinator reaches exactly one outcome on every input — `Ok` or a named
  `Error` — and WHICH one is characterised structurally: `as_string` succeeds exactly on a string,
  `as_float` exactly on a float **or an int** (the numeric normalisation `JVal`'s own doc warns a
  reader not to assume away), `get_prop` exactly on an object carrying the member, `map_list`
  exactly on an array whose every element decodes. That the combinators are `Tot` is carried by
  their types, since F\* admits a definition only after showing it is defined on every input and
  terminates; the lemma is what makes the failure classification **exhaustive** rather than merely
  non-empty.
- **`decode_node_total` / `decode_node_wf`.** The recursive node decoder is `Tot` on an arbitrary
  `jval`, and succeeds on exactly the well-formed documents (`wf`). Termination is the content here
  rather than a formality: the decoder descends into a child array it obtained **by name**, so
  nothing structural is visible at the call site, and `get_prop` carries
  `Ok? r ==> jsize (Ok?.v r) < jsize el` in its RETURN TYPE to supply it.
- **`decode_encode_roundtrip`.** `decode_node (encode n) == Ok n`, for every node of the reference
  vocabulary, at every depth.
- **`lenient_agrees_off_policy` / `strict_unchanged_on_null_free`.** The Phase 102 promise — see
  the policy note below, which is also where this model diverges from what the phase's brief
  assumed.

### The boundary — `Json.parse` is excluded, and why

**`Json.parse`, the string-to-`JVal` parser, is outside theorem 1.** Its totality is a property of
a recursive-descent parser over bytes — the depth bound, escape handling, the int53 token guard,
the `MaxDepthExceeded` and `TrailingCharacters` classes — and it is the one place in this stack an
EverParse-shaped approach might apply. It is a separate phase. Everything the theorem says begins
at a `JVal` that already exists; `Decode.parse` and `Decode.parseTolerantOfNull` are one-line
delegates to the parser and are not modelled either.

So **the programme's §6 wording "the decoder is total" is spent on the combinator layer alone.** It
says: given a parsed value, no combinator and no decoder built from them can diverge, throw, or
reach a state that is neither an accept nor a named refusal. It says nothing about what happens to
the bytes before that value exists.

One further exclusion, named rather than silent: `Versioning.decodeTolerant` is the shipped
**generic** instance of the kind-dispatch pattern, and it is not modelled — its `requiredProfile`
read goes through `Versioning.Profile.tryParse`, string-splitting at a different layer. The pattern
itself is modelled where a domain meets it, as the reference vocabulary's `decode_node`.

### Where the Phase 102 policy actually lives

`NullPolicy` is a parameter of `Json.parseDetailedWithPolicy` **and of nothing else**, and `JVal`
has no null constructor — so no `Decode` combinator can see a null, and none takes a policy
parameter. What the policy is, at the layer this theorem is about, is a **document-level read
normalisation** upstream of every combinator: erase object-member nulls, refuse a null that has no
absence to erase it to.

The model puts it there. `read : null_policy -> jvaln -> outcome jval` goes from a document model
that HAS a null into the wire model that cannot carry one, which is the type-level form of
"tolerance is a read normalisation, never a new emission". Two consequences worth stating exactly:

- **The promise is about the VERDICT, not the message.** On every document the policy does not name
  — no null in member position anywhere — the two readers accept the same values and refuse the
  same documents (`lenient_agrees_off_policy`). They do **not** return equal `Error`s: at the two
  positions the tolerant policy declines to erase (a bare root null, an array element) it names a
  different refusal on purpose, "since the remedy is different". On a document with no null at all
  the two readers are literally the same function, message included
  (`strict_unchanged_on_null_free`) — which is the sharpest form of "the policy governs exactly one
  thing".
- **What this ASSUMES.** That the parser's member-null absorption is equivalent to erasing member
  nulls from the document tree the strict grammar would otherwise produce. The near-miss tokens
  that make that an assumption rather than a theorem (`nul`, which falls through to the strict
  arm; `nullish`, which the following `,`/`}` expectation catches) are grammar, and stay with
  `Json.parse`. The differential below is the evidence for the assumption, not a proof of it.

### What the corpus covers

The differential host (`Proofs.Oracle` in `../tests/Fuaran.Core.Tests/ProofOracleTests.fs`) runs
the extracted model beside `Wire.Decode`. Each probe renders both answers into one string, so the
outcome **class**, the decoded **value** and the error **message** are compared at once — a model
agreeing on accept-vs-refuse alone would not notice a decoder that named the wrong expectation.

| Pool | What is asked | Both classes exercised |
|---|---|---|
| the wire corpus's `nodes/` fixtures | every combinator (12 probes) on every value of every fixture — 228 fixtures, 9,374 values | yes (asserted) |
| the wire corpus's `ops/` fixtures | the same 12 probes — 24 fixtures, 245 values | yes (asserted) |
| a generated `JVal` sample | the same 12 probes over 400 seed-replayable documents, key alphabet drawn from the reference vocabulary's own member names | yes (asserted) |
| the reference vocabulary | 150 generated nodes encoded and decoded, model against the same decoder written from the shipped combinators — the round-trip theorem's instance on the extracted code | accept path |
| the same, refused | every corpus `nodes/` value and 400 generated documents through both node decoders | refuse path (asserted) |
| the read policy | 7 hand-written null positions + 400 generated documents carrying the token at every position, rendered to wire text and read by `Json.parseDetailedWithPolicy` under BOTH policies | yes, and the policy is asserted to have FIRED |

The **go-red case** hands the oracle a *blind bridge* that reads every wire integer as a float — the
decode family's counterpart to the fold family's blind footprint — and requires `asInt` to
disagree, on a hand-made value and over the generated sample. A green report is therefore known to
be a comparison that can lose.

The **proof** was falsified the same way before it was trusted, on scratch copies: dropping
`as_float`'s `JInt` clause reddens `decode_total`; renaming the `"text"` kind tag in the encoder
reddens `decode_encode_roundtrip`; making the tolerant reader erase one off-policy member shape
reddens `lenient_agrees_off_policy`. Each landed on the lemma that should have caught it.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** On the model: the four results above, for every input,
   under no hypothesis at all — unlike the fold theorem, which rests on `independence_sound`, this
   one assumes nothing about a domain. F\* 2026.09.06, Z3 4.13.3, every query 3/3 under
   `--quake 3`, `--report_assumes error` on, no `assume`, no `admit`.
2. **Differentially tested.** The extracted model agrees with `Wire.Decode` over the pools above,
   on class, value and message. Agreement is over those pools, never over all inputs.
3. **Assumed, and stated as such.**
   - **The numeric payloads are opaque.** `jval` is parametric in the int and float carriers, and
     `as_float` takes the widening `to_flt` where F# writes `float i`. No combinator in `Decode`
     looks inside a number — it only moves one — so this is the precise statement of the layer
     rather than a weakening; but it does mean nothing here is said about Int32 range or float
     precision. The `JInt`/`JFloat` **distinction** is modelled, because the combinators branch on
     it.
   - **The parser's null handling, at the tree.** As above: the model's `read` is an assumption
     about what the parser's two forks do to the document, evidenced by the differential.
   - **The extractor and the F# compiler are trusted** — the same link, and the same wording, as
     for the fold model. The leg holds the committed oracle to a fresh extraction byte for byte,
     which makes "the oracle is the model" a checked claim and nothing more.
4. **Not claimed.** Anything about `Json.parse`; anything about a domain's own decoder beyond the
   reference vocabulary modelled here (what carries to one is the combinator layer it is built
   from, not its clauses); and anything about encode — `Canon.render`'s key ordering and float
   layout are certified by the wire-format corpus, not by this theorem.

## Theorem 2 — independence soundness for the tree algebra (Phase 133)

Phase 131 proved fold confluence under one domain hypothesis, and Phase 132 established what that
hypothesis has to be — the diamond, not rejection identity. `TreeOps.fst` **proves the diamond for
the skeleton-op tree algebra**, and `Skeleton.fst` composes the two, so for `SkeletonOp` over any
`NodeWitness` the fold-confluence law is a theorem with no domain hypothesis left. Phase 78's
conservativity contract — `Ops.independent = true` is a *promise* that the scripts commute — stops
being a contract and becomes a consequence.

`TreeOps.fst` models the tree as the witness shows it: a node is an id, a kind tag and an ordered
child list, and nothing else is visible to `Ops`. On top of that sit the five skeleton ops,
`Ops.apply`'s validation clause for clause with the `Rejection` envelope it raises, and
`Ops.footprint` clause for clause. It shares `DagFold.fst`'s list-as-set algebra, its `footprint`,
its `independent` and its `diamond` rather than restating them, which is what makes the composition
an instantiation instead of a second model that has to be argued equal to the first.

### The proof is three facts, not fifteen cases

- **The pinned over-approximation retires nine of the fifteen unordered pairs on its own**, and the
  tree is never looked at. A `RemoveNode` or a `MoveNode` writes an UNKNOWN parent — its source
  parent is a tree fact the script cannot name — and `Ops.independent` refuses independence between
  an unknown-parent write and *any* structural write. Every skeleton op except a structure-free
  `Batch` writes structure. So a remove or a move is independent only of an op that does nothing at
  all, and `relocating_forces_inert` says exactly that. This is the phase's most useful finding: the
  conservative clause the shard called a limitation is what makes most of the theorem free.
- **The diamond's conclusion is symmetric in the pair**, so the remaining ordered cases halve
  (`wstep_sym`), and `Ops.independent` is symmetric too (`independent_sym`).
- **Three commutation equalities on the tree** carry the rest: insert/insert, insert/reorder and
  reorder/reorder. Each side condition they need is exactly what independence buys, and one of them
  is worth naming: `Ops.footprint` puts an op's structural PARENT id into `Reads` as well as into
  `StructureWrites`, which is what guarantees that neither op's inserted subtree carries the other's
  anchor. Without that clause two independent inserts would not commute.

### The conservative footprint is the theorem's shape, not its limitation

The theorem is about the pinned over-approximation, and it is **conservative rather than tight**.
`MoveVsRemove` declares a remove or a move to interfere with every concurrent structural write, so
fewer pairs have to commute — which is why nine of them are vacuous above. A *tighter* footprint,
one that used the tree to see that two removes in disjoint subtrees do not interfere, would admit
more pairs as independent and would need its own proof; nothing here carries to it. That is the
right way round: `independent = true` is the promise, `independent = false` is always a safe answer,
and a theorem about the promise is a theorem about the answer the code actually gives.

### Well-formedness, and why it is a hypothesis rather than an oversight

`Tree.tryFind` and `Tree.parentOf` resolve an id to the FIRST node in document order, and
`ReorderChildren` validates against the children of the node they resolve to. A reorder moves
document order. So on a tree carrying one id twice, two footprint-independent reorders of different
parents can validate differently depending on which ran first — the diamond is **false** there, not
merely unproved. The theorem is therefore about id-unique trees, and `TreeOps.wapply` is `Ops.apply`
guarded to say so.

That guard has a second half, and it is a finding about the shipped code rather than about the
model. `Ops.validateInsert` checks the inserted node's **own** id against the tree and not its
descendants, so an inserted subtree carrying an id the tree already holds is accepted and the result
carries that id twice. `insert_breaks_wf` is a concrete accepted insert of exactly that kind,
machine-checked — so "every accepted op preserves id uniqueness" is refuted here rather than
asserted. What is proved beside it is the conditional form, `ins_wf`, and its converse
`ins_wf_conv`: an insert preserves the invariant **exactly when** its subtree is internally
id-unique and disjoint from the tree. That pair is the specification the Phase 137 validation has to meet,
and it is why guarding on the result is an exact stand-in for the check rather than an approximation
of it.

### What is left open, and how big it is — twelve of the fifteen pairs, exactly

Counted rather than estimated. The fifteen unordered pairs over the five ops are the ten pairs of
non-`Batch` ops plus the five involving a `Batch`. **All ten non-`Batch` pairs are proved** — nine
of them by `relocating_forces_inert` and the three commutation equalities covering the rest. **Two
of the five `Batch` pairs are proved**: `RemoveNode`/`Batch` and `MoveNode`/`Batch`, because a
relocating op forces the other side inert whatever it is, so no lift is needed.

**Three remain open**, and all three are the same shape: `InsertChild`/`Batch`,
`ReorderChildren`/`Batch` and `Batch`/`Batch`, where the batch is one that neither does nothing nor
relocates — a batch built only from inserts and reorders. `TreeOps.covered` is that boundary written
as a predicate, and `tree_independence_diamond` is stated over it.

Lifting the leaf diamond along a batch's script is the argument `DagFold.replay_diamond` already
performs at lane granularity — no new idea is needed — and it needs the id-uniqueness invariant at
each intermediate state of the script, which is exactly what the paragraph above says the algebra
does not currently give. It closes when Phase 137 lands.

The composite theorem in `Skeleton.fst` takes the same boundary as its op alphabet. That costs less
than it looks: `Ops.apply` threads a `Batch` exactly as the fold threads a lane, so the alphabet
removes no behaviour from the fold — it declines to nest one lane inside another.

`applyContained`'s container capability is out of scope and is Phase 140's; `Ops.invert` and `Diff` are
Phase 141's. `NotAContainer` and
`Rejected` are carried in the model's envelope vocabulary and are unreachable from `apply` —
`canHold` is `fun _ -> true` there, and `Rejected` is the domain-side extension point Core never
raises.

### What the corpus covers

| Pool | What is asked | Both classes exercised |
|---|---|---|
| the generated op pool × every state a prefix of it reaches | footprint as four address sets, verdict, accepted result through `Tree.encodeHash`, rejection by class | yes (asserted) |
| hand-written refusals | every rejection class the plain `apply` can raise — each asserted reached by name, not counted | refuse path (asserted) |
| the extracted model's own diamond | the theorem's instance on the code that ships, over the same pool | yes, with an adequacy count |

The result hash runs production's own `Tree.encodeHash` on **both** sides, through a `NodeWitness`
for each tree type, over the per-node content the witness exposes — id and kind tag. Hashing the
reference node's value, hole or effect class would compare fields the model does not model, and
agreeing about them would mean nothing. The **go-red case** hands the oracle a bridge that erases
every kind tag: the verdicts still agree, because no clause reads a kind, and the result hash must
lose — so a green report is known to be a comparison that can fail. The diamond family has its own,
a blind footprint over a hand-made pair of same-parent inserts, which must break.

### Two things this model cost that the first two did not

Both are extensions of the Phase 131 findings rather than new classes, and both are worth knowing
before the fourth model is written.

- **The runtime floor grew by one type** (finding 2 again). `Tree.tryFind` and `Tree.parentOf`
  return an `option`, so a faithful model does too — and the F# backend then emits
  `FStar_Pervasives_Native.option` / `.Some` / `.None`, for which the release ships no F#
  implementation any more than it does for `Prims`. `oracle/FStar_Pervasives_Native.fs` is the
  two-line answer, hand-written and un-diffed exactly as `Prims.fs` is. The alternative — restate
  `option` inside the model so the extraction depends on `Prims` alone, which is what `DagFold.fst`
  does for its list helpers — buys a shorter floor at the cost of a model that no longer reads like
  the F# it is about, and readability is exit criterion 3.
- **Context pruning is the difference between seven minutes of CI and forty** (finding 1's
  successor, put to work). This model is an order of magnitude larger than `DagFold.fst` — some
  ninety definitions where that one has thirty — and the SMT context grows with it, because every
  membership lemma's pattern stays live at every later query. Measured on the pinned prover:
  **300s** to check with the default context, **56s** with `--ext context_pruning`. The leg runs
  `--quake 3` three times from a cold cache, so the whole four-model leg is ~2 minutes per run
  rather than ~15. It is set with `#set-options` inside `TreeOps.fst` rather than added to
  `check.ps1`'s flags, deliberately: pruning changes which facts a query can see, and a module that
  has not been checked under it must not be switched to it as a side effect of another module's
  cost.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** The diamond for every operation pair `covered` names,
   at every id-unique tree; the composite fold-confluence law over the non-`Batch` alphabet, with
   its halt half under no hypothesis at all; and the refutation of unconditional well-formedness
   preservation with the conditional form and its converse beside it. F\* 2026.09.06, Z3 4.13.3,
   every query 3/3 under `--quake 3`, `--report_assumes error` on, no `assume`, no `admit`.
2. **Differentially tested.** The extracted model agrees with `Ops.apply` and `Ops.footprint` over
   the pools above. Agreement is over those pools, never over all inputs.
3. **Assumed, and stated as such.**
   - **The tree is id-unique.** As above: the diamond is false without it, and no shipped type
     carries the invariant — `Diff.toOps` refuses an ill-formed tree with `DuplicateIdInTree`, and
     that is the closest the code comes to enforcing it.
   - **The op alphabet excludes a nested `Batch`.** The one open pair shape, and its size is stated
     above rather than left to be guessed.
   - **The extractor and the F# compiler are trusted** — the same link, and the same wording, as for
     the other two theorems.
4. **Not claimed.** Rejection PAYLOADS: the differential compares a refusal by class, and
   `UnknownNode`'s `addressable` and `ReorderMismatch`'s two orders are outside the comparison (the
   Phase 132 entry says why they cannot be claimed to agree across orders in the first place).
   Nothing about `applyContained`, `invert`, `normalize` or `Diff`. Nothing about a *tighter*
   footprint than the pinned one.

## Next

**`Json.parse` itself** — the boundary theorem 1 names. Totality of the recursive-descent parser
over bytes: the depth bound that makes deep input a named `Error` rather than an uncatchable stack
overflow, the escape and `\uXXXX` paths, the int53 token guard, and the exhaustiveness of the
`JsonErrorKind` classification. It is the one piece of this stack where an EverParse-shaped
approach is worth pricing rather than assuming away.

**The topological order's uniqueness on a spine** — the one step Phase 134 argues rather than
proves, and the smaller of the two. It is two lemmas: that a distinct enumeration of a spine's
closure respecting each node's single parent is forced to be that spine, and that Kahn's frontier
drain produces such an enumeration. The first is a list argument and the second is the only place
production's tie-break would have to be modelled at all.

Theorem 3, interpreter budget monotonicity, is `fuaran-program`'s and follows the same shape now
that the prover is settled.
