# proofs/ — the F\* models of Fuaran.Core's kernel algebras

**Status: GO** (Phase 131, 2026-09-12; the header last brought level with the body 2026-09-14).
Phase 131's three exit criteria were met and remain met — the confluence proof is reproducible on
the pinned prover, the extracted model agrees with production over every lane set the differential
host draws, and the model reads beside the F# in one sitting — and four theorems have shipped
beside it since. **Shipped: fold confluence (131, with its hypothesis corrected by 132 and the DAG
beneath it proved by 134), decoder totality (135), independence soundness for the tree algebra
(133), and chain integrity (136).** Each carries its own claims ladder in its own section below;
the "Next" section at the foot is the live list.

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
| `Chain.fst` | The fourth model (Phase 136): the two INTEGRITY WALKERS — `Dag.firstBreak` / `verifyDag` over the content-addressed DAG and `OpStream.firstChainBreak` / `verifyChain` over the linear chain — clause for clause, with both characterised and `intact_verifies` / `tamper_detected` proved for each under a named injective-hash premise. Phase 145 decomposed the DAG's: the two SPLICES in `nodeHash`'s pre-image are proved unambiguous, the op codec's injectivity moves to a conformance law, and what is assumed is the hash itself. Shares nothing with the models above but `oracle/Prims.fs`. |
| `oracle/Chain.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
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
what is said there is said about `SkeletonOp` and about no other domain's witness. Theorem 3 —
chain integrity — carries a fourth, and what is said there is said about the two integrity walkers
and about nothing else; in particular it says nothing about signatures.

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

**The host step runs two families, and the second is about this document.** `../proofs.json`
declares the claims ladders below as data — what is proved, what is only tested, what is assumed —
and since Phase 144 the leg CHECKS it rather than trusting it: the `Proofs.Ladder` family
(`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`) holds every row to this tree. A `proved` row's
`evidence.theorem` must be a top-level `val` / `let` / `let rec` of that name in the model it
names; that model must be one `$modules` actually checks, and must be there; a `tested` row must
name `Proofs.Oracle` cases that exist; every module in `$modules` must have at least one `proved`
row; and every row's `phase` and `level` must be the closed forms. So renaming a theorem without
touching the file, or adding a model with no claim written down, fails the leg with the row named
— the same posture as oracle drift, applied to the ladder. Each clause has a go-red fixture beside
it (`../tests/Fuaran.Core.Tests/fixtures/proofs-ladder/`), so a green ladder is known to be one
that could have failed.

What it does **not** check is this prose. Row-to-README agreement stays a human act; what is
mechanical is row-to-tree agreement, which is the half a check can settle. Editing a ladder still
means editing both.

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
   under no hypothesis at all — unlike the fold theorem, which rests on `independence_diamond`,
   this one assumes nothing about a domain. F\* 2026.09.06, Z3 4.13.3, every query 3/3 under
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

**Two boundaries come with that sentence and are stated here rather than in a footnote**, because a
reader who takes it at face value would be over-reading it: the theorem is about ID-UNIQUE trees,
and the composite's op alphabet is the four NON-`Batch` ops. Both are argued below — the first
because the diamond is *false* without it and not merely unproved, the second because it is three
of the fifteen pairs and they are counted rather than estimated.

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

## Theorem 3 — chain integrity (Phase 136)

_(The numbering in these headings is this directory's own running count. The attested-stack
programme numbers its theorems separately, and its theorem 3 — interpreter budget monotonicity — is
`fuaran-program`'s, not this one. Only "theorem 1" means the same thing in both.)_

Every ledger in the estate rests on two functions: `Dag.firstBreak` / `verifyDag` over the
content-addressed DAG, and `OpStream.firstChainBreak` / `verifyChain` over the linear chain. The
claim they carry — that any tampered node is found — is what the attestation story is built on, and
until this phase it was certified by go-red tests alone. `Chain.fst` models both walkers clause for
clause and proves it, and in doing so states in the open the one assumption it rests on.

Four things are proved, two per shape, each a corollary of a **characterisation** of the walker it
is about (`break_none_iff`, `chain_break_none_iff`: the walk finds nothing exactly when every
entry's key is its content hash and every named parent is present, and exactly when every record's
sequence, prev-link and recomputed hash agree). Stating the characterisation first is what keeps the
theorems statements about the walker rather than about a re-description of it:

- **`intact_verifies`.** A DAG grown from nothing by `append` and `merge` alone has no break. The id
  half is free — `append` mints the id from the content it is storing — but the PARENT half is not,
  and that is half the value of the theorem: `Dag.append` does not check that `parentId` names a
  node the DAG holds, it takes the string and stores it, so "every parent is present" is a
  precondition on how a DAG was GROWN rather than a property of the constructor. It appears in the
  theorem as `steps_well_parented`, checked against the DAG as it stood when each step ran.
- **`intact_chain_verifies`.** A chain grown from genesis by `append` alone verifies.
- **`tamper_detected`** (with `tamper_op_detected` / `tamper_actor_detected` /
  `tamper_parents_detected` beside it). Change one node's op, actor or parent multiset while
  leaving its ADDRESS as it was, in a DAG whose node under that id was intact, and the walk reports
  a break. `dangling_parent_detected` covers the other break class — a node another node names,
  deleted — **under no hypothesis at all**, because the parent check never consults the hash.
- **`chain_tamper_detected`** (and `chain_tamper_detected_verify`, the same statement at the entry
  point production's callers use). Change one record's sequence, actor or op and leave its two hash
  fields alone, in a chain that was intact, and the walk reports a break.

### The assumptions, and the three things worth knowing about them

The premise is that **the content id determines the content**: two contents that hash to one id are
the same content. That is the collision-resistance assumption every content-addressed store makes;
what is unusual is only that it is written down beside the code that needs it. It is an explicit
lemma-valued **parameter** of the theorems that use it (`node_injective_on`, `rec_injective`), never
an `assume` — `--report_assumes error` is on and the module carries no `assume`, no `admit`, no
`assume val`.

**1. It is stated MODULO THE PARENT SORT, and that is the strongest form that is true.** Production
sorts a node's parents (Ordinal) before hashing, so `merge(A,B)` and `merge(B,A)` converge to one
content id — the Phase 64.1 property without which two hosts reconciling the same pair mint
different ids. The premise therefore concludes "the same actor, the same op, and the same parents UP
TO ORDER". The consequence is stated as a theorem rather than left to prose:
`parent_reorder_undetected` — **re-ordering a node's parents is not a detectable tamper.** It is not
meant to be. `merge_id_parent_order_independent` proves what the sort buys, and needs only that the
comparison is a total order (both halves hold of `String.CompareOrdinal(a,b) <= 0`: any two strings
compare, and two that compare both ways are equal). Production has at most two parents — `append`
gives none or one, `merge` exactly two — so the two-parent case is the general one.

**2. It bundled more than the hash, and Phase 145 unbundled it.** `nodeHash` is
`hashFn (String.concat "," sortedParents) (Actor.encode actor + "|" + encode op)`, so "the id
determines the content" needs four things at once: the hash injective on the pairs it is handed, the
two SPLICES unambiguous, and the op codec injective. Phase 136 declined to decompose the premise,
because string concatenation is opaque to F\* and an argument the model cannot check is better
stated than half-mechanised. That was the right call for a phase whose subject was the walkers, and
it left three claims resting on a sentence. Phase 145 separated them, and none of the three landed
where the sentence said it would:

- **The parent splice is PROVED** (`parent_splice_unambiguous`): `String.concat ","` is injective on
  lists of parent ids given that an id carries no comma **and is not empty**. Both conditions are
  load-bearing and only the first was named — without the second, `[]` and `[""]` join to the same
  string. Production supplies both: a content id under the default `HashFn` is eight hex characters,
  and a parent that is neither is not a key of the map, so the walk's second clause reports it.
- **The actor/op splice is PROVED** (`actor_op_splice_unambiguous`) — but not for the reason this
  document gave. It said the splice is unambiguous because "`Actor.encode` emits a JSON object that
  never contains one". **It can contain one.** `Actor.encode`'s escaper handles `"`, `\` and the C0
  controls; `|` is `0x7C` and is none of those, so `Human "a|b"` encodes to
  `{"kind":"human","id":"a|b"}`. Separator freedom is FALSE of production, and the splice condition
  is not academic — the differential's own work-plan codec encodes an op as
  `"A|" + id + "|" + title`, so the composite pre-image really does carry several `|` characters.
  What saves it is stronger than what was claimed: a JSON object whose string literals are
  self-delimiting is a **prefix-free code**, so no encoding is a proper prefix of another and the
  split point is forced wherever the separators fall. That is the condition the lemma takes, and
  both halves — the refutation and the property — are measured over an adversarial actor population
  in the differential rather than argued here.
- **The op codec's injectivity is the DOMAIN'S** (`op_codec_injective`), so it stays a parameter and
  gains a conformance law: `Conformance.codecInjectivityLaws`, which a witness certifies by
  sampling. Same division of labour as the fold theorem's `independence_diamond`.

`node_injective_derived` composes the four back into the premise the tamper theorems take, so the
composite is a theorem now rather than an assumption. Two things about the decomposition are worth
knowing. It costs a **hypothesis about `string` itself** — `symbols_faithful reveal`: concatenation
is symbol-list append, and two strings with the same symbols are the same string. Both are true of
`System.String` by construction; F\*'s own `FStar.String` states them as `val`s under the comment
"admitted for now as we don't have a model", so naming them in the module says the same thing with
the assumption visible in a signature rather than inherited from a library `--report_assumes` does
not quantify over. And it costs a **restriction**: `node_injective_on` concludes only for pre-images
whose parents are ids and whose actor string is one the actor code emits, which is what the splice
lemmas need and what production supplies. The LINEAR side is deliberately untouched — `rec_injective`
splices a four-way JSON envelope rather than `nodeHash`'s two, and decomposing it is separate work,
named in the ladder rather than quietly implied by this one.

**3. The chain's SEQUENCE and PREV-LINK breaks need none of it.**
`chain_tamper_seq_detected` and `chain_tamper_prev_detected` are proved **under no hypothesis at
all**: those two checks compare stored data against the walk's own running values and never consult
the hash. Only the third check — the record's recomputed hash — spends the premise. That split is
the sharpest thing the mechanisation says about the linear walker, and it is why a re-ordered or
spliced chain is detectable independently of the hash's strength.

### What the corpus covers

The differential host runs the extracted model beside BOTH production walkers, rendering each
verdict into one string so the outcome class, the node or index it is reported at, WHICH check
failed, and the expected and got values are compared at once — a model agreeing on
broken-vs-intact alone would not notice a walker naming the wrong check.

| Pool | What is asked | Both classes exercised |
|---|---|---|
| the reference tree witness | the DAG walker on 60 generated 3-lane DAGs and every single-node tamper of each (op, actor, re-parent, dropped parent) | yes (asserted) |
| the work-plan domain | the same, 60 DAGs | yes (asserted) |
| the work-plan domain | the LINEAR walker on 80 generated chains and every single-record tamper (op, actor, seq, prevHash, dropped record) | yes (asserted) |
| the reference tree witness | the same, 60 chains | yes (asserted) |
| the wire corpus's `dag/` fixtures | their SHAPES — see below — rebuilt through production's own `append`/`merge`, intact and under every tamper | yes (asserted) |
| an adversarial ACTOR population (Phase 145) | the two alphabet conditions the splice lemmas ask of production: that `Actor.encode`'s image is prefix-free, and that the splice recovers both halves over every (actor, op-encoding) pair | yes — a bare-id encoder is prefix-comparable and collides on the same population |
| the work-plan witness (Phase 145) | its op codec, through `Conformance.codecInjectivityLaws` — the model's fourth premise certified for the domain whose ids the cases above compare | yes — a codec that erases its op fails the law, and the tamper it hides is then invisible to production's own walker |

`Dag.nodeHash` is private, so the only way to reach it is through `Dag.append`: the ids the model
recomputes are the ones production minted, and a model whose `isort` and `join_comma` were not
`List.sortWith CompareOrdinal` and `String.concat ","` would report a break on an INTACT DAG. That
is what makes the intact runs evidence rather than a formality, and a further case asserts the same
thing directly, node by node, so a divergence says which half moved. Every run carries a vacuity
guard that DETECTION actually happened: two walkers that both say "intact" agree perfectly and
certify nothing.

**The `dag/` corpus family is a source of SHAPES, not of addresses, and the reason is recorded as an
assertion rather than a sentence.** That family is the UI host's DAG-RECORD wire format
(`kind: "dag-record-round-trip"`): its `hash` members are 64 characters, minted by that host's own
pre-image under SHA-256 over an envelope carrying members Core's `DagNode` does not have, where
Core's default `HashFn` is FNV-1a and emits 8. Handing those four records to `Dag.firstBreak` would
report four content-id mismatches — a true answer to the wrong question, and a "differential" that
agreed with it would certify nothing. What the family DOES supply, and what the generated pools
cannot, are the shapes: a genesis node, a linear step, both actor kinds, and a **two-parent MERGE**.
Those are rebuilt through production's own `Dag.append` / `Dag.merge`, so the ids under test are
Core's; the size fact is asserted, so the boundary goes red if it moves.

The **go-red cases** are three, and the third is the one that earns the premise its place.
Two are the ordinary teeth: a model handed a DIFFERENT hash (the same function with its arguments
swapped) must report a break where production sees none, on the DAG and on the chain, so a green
report is known to be a comparison that can lose. The third runs the SAME tamper under two hashes.
Under the real one it is found, by both walkers. Under a deliberately non-injective one — a hash
that folds the parents and the actor and DROPS the op, so two nodes differing only in their op mint
one id — it is **invisible**, to production and to the model alike. That is the named premise shown
to be load-bearing rather than decorative, and it is the honest reading of what "tamper detection is
proved" means here.

The **proof** was falsified the same way before it was trusted, on scratch copies: dropping the
total-order hypothesis reddens `merge_id_parent_order_independent`; removing the injectivity
appeal reddens `tamper_changes_the_id`; making the DAG walk skip its content-id check reddens
`break_none_iff`; dropping "the stored hash is unchanged" from the tamper premise reddens
`chain_tamper_detected`; and hashing the UNSORTED parents reddens `parent_reorder_undetected`. Each
landed on the lemma that should have caught it.

Phase 145's own lemmas were falsified the same way before they were trusted, and each of those
landed where it should too: dropping the **non-empty** condition on a parent id reddens
`joined_injective`, and so does dropping the **comma-free** one — two conditions, two independent
failures, which is what says both are load-bearing rather than one carrying the other; dropping the
prefix-free hypothesis reddens `splice_split`; dropping the appeal to the op codec reddens
`node_injective_derived`; and hashing the unsorted parents reddens it too, through
`hash_injective`'s own use site.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** On the model: both walkers characterised; a DAG grown
   by well-parented `append`/`merge` and a chain grown by `append` have no break; a single-node
   content tamper that leaves the address alone is found — the DAG's under the injectivity premise,
   the chain's sequence and prev-link arms under nothing at all, and the DAG's missing-parent class
   under nothing at all. Since Phase 145, also: the two SPLICES in `nodeHash`'s pre-image are
   unambiguous for the alphabets production uses (`parent_splice_unambiguous`,
   `actor_op_splice_unambiguous`), and the composite premise the tamper theorems take is DERIVED
   from the hash's injectivity and the op codec's rather than assumed alongside them
   (`node_injective_derived`). F\* 2026.09.06, Z3 4.13.3, every query 3/3 under `--quake 3`,
   `--report_assumes error` on, no `assume`, no `admit`.
2. **Differentially tested.** The extracted model agrees with `Dag.firstBreak` and
   `OpStream.firstChainBreak` over the pools above, on the verdict, the location, the check that
   failed and the expected/got values — and mints production's own content ids node by node.
   Agreement is over those pools, never over all inputs. Since Phase 145 the two alphabet conditions
   the splice lemmas ask of production are measured here too — `Actor.encode`'s image is prefix-free
   over an adversarial population, a content id carries no comma and is not empty, and a parent id
   that would is caught as a missing parent — and the op codec's injectivity is certified for the
   work-plan witness through `Conformance.codecInjectivityLaws`. Sampled, over those populations,
   never over all inputs.
3. **Assumed, and stated as such.**
   - **The hash is injective on the pairs it is handed** — the cryptographic premise, and since
     Phase 145 the only claim about the DAG's content id that is assumed rather than proved or
     certified. It is a PARAMETER of the theorems that need it, so which results depend on it is
     visible in their signatures rather than inferable from prose. Phase 136 stated it in a bundled
     form that also carried the two splices and the op codec; point 2 above is what became of those.
   - **The op codec is injective** — the domain's promise, not this library's, and a parameter for
     that reason. Certified by sampling rather than proved (`Conformance.codecInjectivityLaws`), on
     the same footing as the fold theorem's `independence_diamond`.
   - **A string is its symbols** — `symbols_faithful`: concatenation is symbol-list append and two
     strings with the same symbols are the same string. Both are true of `System.String` by
     construction and are what makes any splice argument possible at all; F\*'s own `FStar.String`
     admits them. A stated hypothesis, in the same style as the total order below, rather than a
     parameter.
   - **The LINEAR side's premise is still bundled.** `rec_injective` splices a four-way JSON
     envelope, and Phase 145 decomposed `nodeHash`'s two splices only. Nothing about the chain
     payload's splices is proved; the row above is the whole of what is claimed there.
   - **The comparison is a total order.** `merge_id_parent_order_independent` asks for it and
     nothing else does; `String.CompareOrdinal(a,b) <= 0` satisfies it.
   - **The walk order is production's own.** The model walks its entry list in the order it is
     given, where production walks `Map.toList` (ascending key). The differential hands the model
     exactly the list `Map.toList` produced, so the order under test is production's — but the model
     does not derive it, and nothing here says anything about F#'s `Map` enumeration. Note the
     *verdict* is order-independent by construction (a break exists or it does not); only WHICH
     break is reported first depends on it.
   - **A sequence number is a Peano numeral.** F#'s `Seq` is an `int`; the extraction carries no
     integers (finding 2), so the model spells the walk index and the stored sequence as zero and
     successor. Every non-negative sequence is representable and a tampered one is representable;
     a NEGATIVE stored sequence is outside what the bridge can carry, so the generated tampers stay
     non-negative. The walker's own check is equality, which is what the numeral supports.
   - **The extractor and the F# compiler are trusted** — the same link, and the same wording, as for
     the other three theorems.
4. **Not claimed.**
   - **A REWRITE.** A tamper that also re-mints the tampered node's id and then every descendant's
     produces a structure that is intact by construction, and no walker will ever find it. What
     catches it is a signed head, which is a composition this theorem says nothing about — it lives
     with the signing composition, on the coordination plane's own side.
   - **Anything about signatures.** `Attestation` and `IAttestationSink` are outside the model.
   - **Re-ordering a node's parents.** Not merely unproved: proved NOT detected
     (`parent_reorder_undetected`), because the pre-image is sorted. It is the price of the
     convergence `merge_id_parent_order_independent` buys, and it is stated as a theorem so a reader
     meets it rather than inferring it.
   - **Cycles.** A DAG whose ids all recompute cannot carry one — a node's id folds its parents' —
     but that is an argument, not a theorem here, and `Dag.isAcyclic` remains a separate check on a
     structurally-loaded DAG.
   - **The `StreamConfig` migration path.** `legacyActorConfig` and `rehash` are outside the model;
     the payload's shape is a parameter, so a second config is a second instantiation rather than a
     second model, and nothing here is said about the migration between them.
   - **`Json.parse`, `Dag.fromJsonl` and the JSONL scanners.** Loading is not verifying — that is
     `fromJsonlVerified`'s whole point — and the model begins at a structure that already exists.

## Next

**`Json.parse` itself** — the boundary theorem 1 names. Totality of the recursive-descent parser
over bytes: the depth bound that makes deep input a named `Error` rather than an uncatchable stack
overflow, the escape and `\uXXXX` paths, the int53 token guard, and the exhaustiveness of the
`JsonErrorKind` classification. It is the one piece of this stack where an EverParse-shaped
approach is worth pricing rather than assuming away.

**The LINEAR chain payload's splices** — the half Phase 145 deliberately did not take. `rec_hash`
hashes `{"seq":<n>,"actor":<a>,"op":<o>}`, a four-way splice through a JSON envelope rather than
`nodeHash`'s two, so `rec_injective` is still the bundled premise Phase 136 wrote. The symbol-level
machinery it would need is already there (`app_sep_split`, `splice_split`, `symbols_faithful`); what
is new is that the envelope's separators are `":` and `,"` digraphs inside a literal skeleton rather
than single characters, so the argument is a parse rather than a split, and the actor field's
self-delimitation has to be composed with the sequence numeral's. It is the smallest of the three
items here and the one with a worked precedent beside it.

**The topological order's uniqueness on a spine** — the one step Phase 134 argues rather than
proves, and the smaller of the two. It is two lemmas: that a distinct enumeration of a spine's
closure respecting each node's single parent is forced to be that spine, and that Kahn's frontier
drain produces such an enumeration. The first is a list argument and the second is the only place
production's tie-break would have to be modelled at all.

**The signing composition** — the successor Phase 136 names and deliberately does not take.
Chain integrity says a tampered node is found; it says nothing about a REWRITE, which re-mints
every descendant's id and produces a structure no walker can fault. What closes that is a signed
head, and the composition is the coordination plane's own — an attestation over a head, plus this
theorem, is what makes "the history is the one that was signed" a claim rather than a hope. The
seam is here (`Attestation` / `IAttestationSink`); the theorem is not.

Interpreter budget monotonicity — the attested-stack programme's theorem 3, which is not this
directory's numbering — is `fuaran-program`'s and follows the same shape now that the prover is
settled.
