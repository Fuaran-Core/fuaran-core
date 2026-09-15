# proofs/ — the F\* models of Fuaran.Core's kernel algebras

**Status: GO** (Phase 131, 2026-09-12; the header last brought level with the body 2026-09-14).
Phase 131's three exit criteria were met and remain met — the confluence proof is reproducible on
the pinned prover, the extracted model agrees with production over every lane set the differential
host draws, and the model reads beside the F# in one sitting — and six further theorems have
shipped beside it since. **Shipped, seven in all: fold confluence (131, with its hypothesis
corrected by 132, the DAG beneath it proved by 134 and its topological order by 142), decoder
totality (135), independence soundness for the tree algebra (133), chain integrity (136, its
content-id premise decomposed by 145),
`Json.parse` totality, bounded (146), apply-engine preservation (138, which also lifts 133's
model to the validator 137 fixed), and the diff's refusal characterisation and emission order
(141).** Each carries its own claims ladder in its own section
below; the "Next" section at the foot is the live list.

This directory is the mechanised half of the correctness story whose differential half already
existed: Phase 80 certified two-script confluence, Phase 83 the two-head `Dag.reconcile`, Phase 100
the N-lane fold-confluence pack. Those are property tests over sampled orders; this is the same law
as a theorem, and the theorem's model run as a sixth host through the same differential test.

| File | What it is |
|---|---|
| `DagFold.fst` | The model: `Ops.independent`, `Dag.conflicts`, `Dag.reconcileMany`, the replay and `FoldConfluence.foldOnce`, over an abstract `op`/`state`/`rej`, with `fold_confluence` proved; and, since Phase 134, the DAG beneath them — `DagNode` / `Dag.T` / `Dag.ancestorsOf` / `Dag.between` / `Dag.betweenOps` for the base-plus-N-chains shape, with `between_chain` and `fold_confluence_dag` proved; and, since Phase 142, `topoOrder`'s own frontier drain, with the uniqueness of a spine's topological order proved (`spine_order_forced`, `kahn_drain_is_such_an_enumeration`); and, since Phase 156, that drain over an ABSTRACT node set at an abstract total order on ids, with `drain_deterministic`, `drain_linear_extension` and `drain_total_on_acyclic` proved and the dangling-parent policy carried as a parameter. Every definition names its F# counterpart. |
| `oracle/DagFold.fs` | **Generated** — the model extracted to F# by F\*'s own code generator. The suite runs it beside production. |
| `WireDecode.fst` | The second model (Phase 135): `Decode`'s combinators, a reference vocabulary with its encoder and kind-dispatch node decoder, and the Phase 102 read policy, with `decode_total`, `decode_node_wf`, `decode_encode_roundtrip` and `lenient_agrees_off_policy` proved. Shares nothing with `DagFold.fst` but `oracle/Prims.fs`. |
| `oracle/WireDecode.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `TreeOps.fst` | The third model (Phase 133): the skeleton-op tree algebra — `Ops.apply` with its `Rejection` envelope and `Ops.footprint`, over the tree as the `NodeWitness` shows it — with `tree_independence_diamond` proved, which is the fold theorem's one domain hypothesis. Unlike the two above it does NOT share only `Prims.fs`: it `open`s `DagFold`, which is what makes the composite an instantiation rather than a second model. |
| `Skeleton.fst` | The composite (Phase 133): `DagFold`'s fold theorem instantiated at `TreeOps`, so `skeleton_fold_confluence` holds with no domain hypothesis left. Thin on purpose — the argument is in the two halves it joins. |
| `oracle/TreeOps.fs`, `oracle/Skeleton.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `Chain.fst` | The fourth model (Phase 136): the two INTEGRITY WALKERS — `Dag.firstBreak` / `verifyDag` over the content-addressed DAG and `OpStream.firstChainBreak` / `verifyChain` over the linear chain — clause for clause, with both characterised and `intact_verifies` / `tamper_detected` proved for each under a named injective-hash premise. Phase 145 decomposed the DAG's: the two SPLICES in `nodeHash`'s pre-image are proved unambiguous, the op codec's injectivity moves to a conformance law, and what is assumed is the hash itself. Shares nothing with the models above but `oracle/Prims.fs`. |
| `oracle/Chain.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `JsonParse.fst` | The fifth model (Phase 146): the recursive-descent JSON PARSER — `Json.parseDetailedWithPolicy`'s `skipWs` / `expect` / `parseString` / `parseNumber` / `parseValue` / `parseObject` / `parseArray`, the depth counter, both numeric guards and the `EraseMemberNull` fork — with `parse_total`, `depth_bound_exact`, `int53_guard_exact` and `error_kind_exhaustive` proved. This is the boundary theorem 1 named. Shares nothing with the models above but `oracle/Prims.fs`. |
| `oracle/JsonParse.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `Preservation.fst` | The sixth model (Phase 138): the APPLY ENGINE — `Ops.apply`'s totality with its per-clause rejection characterisation, all-or-nothing rejection, id uniqueness preserved by every accepted operation, `Ops.canApply` agreeing with `apply`, and `Ops.invert`'s round trip. It `open`s `TreeOps` (and through it `DagFold`) rather than remodelling the tree: the theorem is about the algebra that model already describes. |
| `oracle/Preservation.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `TreeDiff.fst` | The seventh model (Phase 141): the DIFF — `Diff.toOps`'s two refusals characterised exactly, its four passes clause for clause, what each pass guarantees about the block it emits, and `Diff.toOpsContained`'s pre-emptive container refusal. Named `TreeDiff` and not `Diff` for the reason `TreeOps.fst` is not called `Ops`: the extracted oracle is a top-level F# module and the differential host opens `Fuaran.Core`. It `open`s `TreeOps` (and through it `DagFold`); it is independent of `Preservation.fst`. |
| `oracle/TreeDiff.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `oracle/Prims.fs` | The `Prims` names the F# backend emits and the release does not ship. |
| `oracle/Fuaran.Core.Proofs.Oracle.fsproj` | The oracle assembly. Never packed; nothing extracted enters the shipped kernel. |
| `fstar-pin.json` | The pinned F\* release (which bundles Z3) and its hash. |
| `check.ps1` | The proof leg: check with the pin, re-extract and diff against the committed oracle, run the host. |
| `modules.json` | What the leg COSTS: one entry per checked module — its `budgetSeconds`, the measurement that budget was seeded from, and the phase that set it. `check.ps1` reads it and prints measured-against-budget on every green line. See "What the leg costs" below. |
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

**One step Phase 134 left ARGUED is now proved too (Phase 142).** Production chooses its
topological order with Kahn's algorithm, draining a ready frontier smallest-id-first; the model
takes the reverse of the parent walk. That "on a spine there is only one topological order anyway,
so the two coincide" was a sentence in a comment and a row at level 3 of the ladder. Section 12 is
that sentence, mechanised, in two lemmas over the same model:

- **`spine_order_forced`** — a DISTINCT enumeration of a spine's closure in which every node
  follows its own parent is that spine in append order, base first. A list argument: it names no
  DAG, no hash and no production function.
- **`kahn_drain_is_such_an_enumeration`** — the frontier drain, modelled as `topoCore`'s loop over
  `parentsIn` / `indeg` / `ready`, produces exactly such an enumeration over a spine's closure.

From the two, **`between_chain_any_order`** is `between_chain` restated with the order UNIVERSALLY
QUANTIFIED where section 11 fixed it to the parent-walk reversal by definition, and
`reconcile_many_dag_ordered_eq` / `fold_once_dag_ordered_eq` carry that up to the fold:
`Dag.reconcileMany` from the DAG is the deltas-first fold whatever order each head's recovery
walked. **`topo_of_is_the_kahn_drain`** joins the halves and is the row itself — production's drain
and the model's reversal are the same list.

**The smallest-id tie-break is not modelled, and the theorems say why it does not need to be.** The
selector is a PARAMETER, constrained only to return a member of the frontier it is handed
(`picks_from_frontier`, with `pick_head` exhibited so the hypothesis is not vacuous), and every
result is stated for every such selector. `kahn_frontier_singleton` proves the frontier is a
ONE-element list at every step of a spine's drain, so all selectors agree and nothing anywhere says
a word about how ids compare. A frontier wider than one is exactly where the tie-break would begin
to matter, and that is the MERGE-DAG case, which stays unclaimed.

**Neither universally quantified statement is vacuous, and both witnesses are in the module rather
than in this paragraph.** A theorem quantified over every enumeration says nothing if no
enumeration meets its hypotheses, and one quantified over every selector says nothing if none does:
`pick_head` is exhibited as a selector that does, and `kahn_drain_is_such_an_enumeration` concludes
`distinct ord /\ follows_parents ns ord` of an enumeration it produces — which
`topo_of_is_the_kahn_drain` then identifies with the model's own order. So the enumeration
`between_chain_any_order` is quantified over exists, is production's, and is `topo_of`; that they
are one list is the point of the phase.

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

### The drain over an abstract DAG (Phase 156)

Section 12 proves the order on a SPINE. The shape a clone actually folds is the UNION of N lanes,
and there the ready frontier is N wide once the base is drained — so the tie-break section 12 could
leave as an unexercised parameter is what decides the sequence, and the determinism claim every
convergent consumer rests on ("the same node set gives the same order on every machine") rests on
it. Section 13 is that case, over an **abstract** node set: no `mint`, no lanes, no base, just
nodes naming parents.

It is section 12's own `kahn`, at the selector `pick_min lt`, and not a second drain. Three
theorems, no admits:

- **`drain_linear_extension`** — every node the drain places stands after every one of its in-set
  parents. Stated over the nodes it PLACED rather than over all of them, because on a cyclic set
  it places only a prefix, and a statement quantified over all of them would be false there rather
  than silent.
- **`drain_total_on_acyclic`** — on an acyclic set it places every node exactly once, and what it
  produces is itself a topological enumeration.
- **`drain_deterministic`** — the sequence is a function of the node SET: permute the work list
  (F#: receive the lanes in any arrival order) and the same list comes back, under either policy.

**This is where the tie-break stops being decoration.** `frontier` answers in the work list's
order, so a permuted node set hands the selector a permuted frontier; `pick_min` is invariant under
that and section 12's `pick_head` is not. A drain taking the head of an unsorted frontier satisfies
`picks_from_frontier` and FAILS `drain_deterministic` — which is the precise sense in which
smallest-id-first is load-bearing. Section 12's two spine results are re-derived here as corollaries
by instantiating its selector at `pick_min lt` (`spine_drain_is_the_parent_walk`,
`spine_drain_is_append_order`), so the spine case is this section's special case rather than a
parallel claim to keep in step.

**The ID ORDER is a parameter, and that is the stronger statement rather than a weaker one.** The
model takes any `lt` satisfying `total_order` — irreflexive, transitive, trichotomous — and every
result holds for all of them. What determinism needs is that all clones use the SAME order, never
that the order is any particular one, and quantifying over total orders says exactly that without
the module acquiring the character arithmetic a concrete string comparison would need (finding 2).
Production's `List.sort` on a string list is F#'s structural comparison and so
`String.CompareOrdinal`; tying the abstract order to that one is the differential's job rather than
the model's, and the host instantiates `lt` there.

**The DANGLING-PARENT POLICY is a parameter, because the two production call sites differ on it**
and a theorem about "the drain" that did not say which would be a theorem about neither:

- `IgnoreDangling` — `Dag.topoCore`'s closure walk (`match Map.tryFind id dag.Nodes with | Some n
  -> … | None -> collect acc rest`) with `Dag.ancestorsOf`'s `ContainsKey` guard beside it and the
  `parentsIn` filter that follows. A parent the set does not hold never enters the closure and is
  filtered out of the in-degree, so it constrains nothing and the drain proceeds. This is the
  policy on the FOLD path — `topoOrder` -> `between` -> `betweenOps` -> `reconcileMany` ->
  `foldOnce` — and on `replayTo`.
- `RefuseDangling` — `Dag.firstBreak` (`n.Parents |> List.tryFind (fun p -> not
  (dag.Nodes.ContainsKey p))` -> `MissingParent`), hence `verifyDag` and `fromJsonlVerified`,
  which refuse the whole set before any drain runs.

`drain_policies_agree` proves the two are the same function on a set with no dangling parent — so
the fold path pays nothing for the refusing one's existence — and `drain_refusal_characterised`
proves the refusal fires exactly when a parent lies outside. The refusal names the SMALLEST such id
rather than the first in the work list, which is not a liberty: `firstBreak` scans `Map.toList`, in
id order, and its docstring says so, so the refusal is order-invariant on both sides and
`drain_deterministic` covers it. What the model does NOT carry is the refusal's diagnostic payload
— `firstBreak` reports the missing parent beside the node, and the node determines the parent, so
the model names the node and stops there.

**How a cycle is surfaced, stated exactly, because it is a claim about production.** `topoCore`
does not raise: a node inside a cycle never reaches in-degree zero, so the emitted list is strictly
SHORTER than the closure — `isAcyclic` and `tryTopoOrder` read that length comparison and surface
it, while `replayTo` and `between` fold the truncated prefix. The model says the same thing without
lengths. `drain_total_on_acyclic` gives completeness from acyclicity; `drain_complete_is_acyclic`
reads the converse off the linear-extension theorem, because a complete drain IS a topological
enumeration and so witnesses acyclicity. The two are an iff, and that iff is exactly what
`isAcyclic`'s comparison claims.

**Acyclicity is "a topological enumeration exists", supplied as a witness LIST.** It is the standard
characterisation of a finite acyclic digraph; it is not circular, because the witness is any such
list and never the drain's own output; and taking it as a parameter rather than as an existential
or as the absence of a self-reachable node keeps every statement in the first-order fragment this
module stays inside. `drain_complete_is_acyclic` is what keeps the hypothesis non-vacuous in the
direction that matters — the drain's own output is such a witness whenever it is complete.

**What is still NOT claimed here.** That any particular id ordering is production's — `lt` is a
parameter and the differential is the tie. The DELTA RECOVERY over a merge DAG: `between_chain` is
still stated for the base-plus-N-chains shape, and what Phase 156 adds is the ORDER over an
arbitrary set, not the recovery over one. `Dag.mergeBase`, still. And any consumer's own
instantiation of this theorem for its own total order, which is that consumer's work.

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

**And since Phase 156 the ORDER itself is measured over a MERGE DAG, which is the case the
delta-recovery cases above structurally cannot reach.** The lane heads are folded into one
convergent head with `Dag.merge` — the node a real reconciliation writes — so the union head's
closure is every node and the ready frontier is N wide once the base is drained. The extracted
`drain`, instantiated at `String.CompareOrdinal`, is compared against `Dag.tryTopoOrder` over the
same nodes: per lane head (the spine case, which no tie-break can get wrong) and per union (the
case that needs one). Its adequacy guard is the frontier WIDTH — a run whose frontier never passed
one node has re-measured the spine under a different name, and the case says so by number. Its
**go-red** is a model draining LARGEST-id-first, as legitimate a selector as the model's own since
it still returns a member of the frontier it is handed, so what it breaks is agreement with
production and nothing else; it is run over the unions and not the lane heads, deliberately,
because a one-element frontier has one minimum and one maximum and no tie-break can lose there.
Three further cases ride with it: every permutation of the node set drained and compared against
the drain of the set, which is `drain_deterministic` measured; the two policies, agreeing on a
closure and diverging the moment the base node is dropped from it, which is `drain_policies_agree`
and `drain_refusal_characterised` measured against the two callers they model; and a cyclic node
set built by hand — unreachable through `Dag.append`, whose parent id is minted before its child's,
and exactly the hand-crafted or tampered JSONL load `Dag.fromJsonl`'s docstring warns about — where
production's `isAcyclic` must see the cycle and the model's drain must stop at the same short
prefix, with an acyclic control beside it so the comparison is a discrimination rather than a
refusal of everything.

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
   `fold_confluence_dag`, under the id-distinctness premise named at level 3. F\* 2026.09.06,
   Z3 4.13.3, every query 3/3 under
   `--quake 3`, cold-cache checks on three seeds and at a quarter of the rlimit the leg runs with.

   **And, since Phase 142, the CHOICE of topological order — which was level 3 until this phase.**
   A spine's closure admits exactly one topological order (`spine_order_forced`), production's
   frontier drain produces it (`kahn_drain_is_such_an_enumeration`), and the two are therefore the
   same list (`topo_of_is_the_kahn_drain`). The recovery is restated with the order universally
   quantified (`between_chain_any_order`), and the fold above it with it
   (`reconcile_many_dag_ordered_eq`, `fold_once_dag_ordered_eq`), so no result here depends on
   which topological order was walked. The tie-break is a parameter constrained only to select from
   the frontier, and the frontier on a spine is one element wide at every step
   (`kahn_frontier_singleton`) — so that phase says nothing about how ids compare.

   **And, since Phase 156, the ORDER OVER AN ARBITRARY ACYCLIC SET, which is where the tie-break
   does become observable.** The drain is deterministic — a function of the node set alone,
   invariant under the order the lanes arrived (`drain_deterministic`) — a linear extension of the
   parent relation over every node it places (`drain_linear_extension`), and TOTAL on an acyclic
   set, placing each node exactly once and producing a topological enumeration
   (`drain_total_on_acyclic`). The id order is a parameter constrained to be a total order, so the
   claim is that all clones using the SAME order agree, and the differential is what ties that
   parameter to `String.CompareOrdinal`. The dangling-parent policy is likewise a parameter, and
   `drain_policies_agree` / `drain_refusal_characterised` say which of the two production call
   sites gets which. `drain_complete_is_acyclic` gives the converse of totality, so a complete
   drain and an acyclic set are an iff — which is exactly the length comparison `Dag.isAcyclic` and
   `Dag.tryTopoOrder` make. Section 12's spine results are corollaries of this one at
   `pick_min lt`. What is NOT here is the DELTA RECOVERY over a merge DAG, which stays where Phase
   134 put it, below.

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
   - _(**How the topological order is CHOSEN** left this level at Phase 142 and is now at level 1
     above. Nothing about the order is assumed at any shape since Phase 156: the drain over an
     arbitrary acyclic set is proved deterministic, a linear extension and total, and the only
     parameters left — which total order on ids, and which dangling-parent policy — are universally
     quantified rather than assumed, with the differential tying the first to production's.)_
   - **`Dag.mergeBase` is outside the model**, because it is outside this path: `foldOnce` hands
     `reconcileMany` the base node's id directly and never locates a divergence point. Level 2 is
     the only evidence about it. _(The general topological ORDER over an arbitrary acyclic set —
     merge DAGs included — is at level 1 since Phase 156; what is still unclaimed over a merge DAG
     is the delta RECOVERY, below.)_
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
     whose heads have already been merged has nodes with two parents and `mergeBase` on its path,
     and neither is modelled, so a consumer folding over already-merged heads is outside every
     level here. **What this entry no longer covers is the ORDER**: Phase 156 proves the drain
     deterministic, a linear extension and total over an arbitrary acyclic node set, so the third
     thing this entry used to name — "a topological order that is genuinely a choice rather than a
     forced one" — is at level 1 above, and is measured over a real `Dag.merge` union. The
     RECOVERY over such a DAG is what remains.

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
and about nothing else; in particular it says nothing about signatures. Theorem 4 — `Json.parse`
totality, bounded — carries a fifth, and what is said there is said about the recursive-descent
parser's two guards and its error classification; in particular it is not a grammar-conformance
claim, and its section says exactly which grammar paths remain sampled. Theorem 5 — apply-engine
preservation — carries a sixth, and what is said there is said about `Ops.apply`, `Ops.canApply`
and `Ops.invert` over the tree as the `NodeWitness` shows it; in particular the witness itself is
assumed lawful there rather than proved so, and the invariant preserved is id uniqueness and not
any domain's rule family.

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
pwsh ./proofs/check.ps1 -Strict    # turn a cost finding (below) from a warning into a red leg
pwsh ./proofs/check.ps1 -NoFloor   # do not enforce the per-module time floors (below)
pwsh ./proofs/check.ps1 -CacheDir <dir>   # put the checked-module cache somewhere you name
pwsh ./verify.ps1 -Proofs          # the whole repo gate plus the proof leg
```

Every model in the script's `$modules` list goes through all three steps, and `-Runs N` means N
cold-cache verifications of all of them; adding a model is adding its name to that list and a
budget entry to `modules.json` beside it. The first
run downloads the pinned release (~200 MB, hash-verified) into `proofs/.fstar/`; `FSTAR_HOME`
pointing at a matching release skips that. Editing a `.fst` without re-extracting fails the leg
with "oracle drift" — that is the point, not an inconvenience.

### "Cold cache" means it, and "verified" has a floor (Phase 164)

On 2026-09-14 a background `check.ps1` was orphaned at a turn boundary and went on writing the
then-shared `proofs/obj/cache` while a replacement run started in the same worktree; the
replacement found the orphan's `.checked` files, reported `TreeOps 0s`, `Skeleton 0s`, `Chain 0s`
and printed `==== proofs: green`, and it was caught only because a human read the timings rather
than the verdict. Two mechanisms come from that, one closing the cause and one closing the class.

**The cache is per invocation.** Each run uses `proofs/obj/cache-<pid>`, created at its head and
removed when it exits, so two invocations in one worktree cannot share checked files and the
`-Runs` loop's cache-clear means what it says. Before Phase 164 the directory was a constant, and
clearing it only made a run cold if nothing else was writing there — which the script cannot see,
because a second writer arrives *after* the clear. A run that is killed outright cannot remove its
own directory, so the next run sweeps any `cache-<pid>` whose process is gone; a live invocation's
cache is never touched, which is the point of naming it after the process. `-CacheDir <dir>` puts
the cache somewhere you name instead: it is still cleared before every run — otherwise the flag
would quietly mean "warm" — so the script refuses a directory holding anything but `*.checked`
files, and leaves it in place at exit, because you named it.

**Each module declares a floor as well as a budget**, `floorSeconds` beside `budgetSeconds` in
`modules.json`, and a module that verifies in **less** than its floor **fails** the leg on the
spot, naming the module and the time. The asymmetry with the ceiling is deliberate. An overshoot
is a true measurement of a true cost, so it warns and the leg stays green; an undershoot says the
apparatus is broken rather than the module fast, and every module measured after it shares that
apparatus — implausibly fast is the one direction in which a lie looks like good news. The seeding
rule is the budget's mirror, in the file's own `floorSeeding` block: **the fastest genuine cold run
ever observed, halved**, rounded down, and **0** for a module whose fastest cold run is under 5s,
where halving lands inside process-start noise. `Skeleton` is that case at about one second, so its
floor is 0 and the file says so — which makes it the one module a shared cache could still speed up
unnoticed, and the per-invocation directory rather than the floor is what covers it. Halving on top
of the fastest-ever run leaves an enormous margin, and it costs nothing in detection because the
failure this catches lands at or near zero seconds. A module with **no** `floorSeconds` is a cost
finding, not a failure — a sibling adding a model should no more go red for a floor nobody has
measured than for a budget nobody has measured — and re-seeding a floor downwards is the same
recorded act as bumping a budget upwards: the new number, the `fastestSeconds` it came from, and
your phase. `-NoFloor` is the deliberate opt-out for a machine genuinely that fast, and the log
says when it was passed.

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

### What the leg costs — the budget, and the two cliffs (Phase 148)

Prover time is this leg's real cost, and it moves for reasons the author of a model does not see, so
it is **declared**: `modules.json` beside this file carries one entry per checked module — a
`budgetSeconds`, the `measuredSeconds` that budget was seeded from, the `phase` that set it, and a
note. Every green CHECK line prints the pair, so the trend is legible in the log with no arithmetic:

```
==== proofs: TreeOps.fst verified — run 1 of 3, 70s/160s, every query 3/3 under --quake
```

A run over budget prints a named finding — this one is from the phase's own halved-budget probe —
and the leg is **still green**:

```
==== proofs: COST — TreeOps.fst took 64s against its 25s budget on run 1 of 1 — 39s over, 256% of budget
```

That is deliberate, and it is the whole posture. Prover time varies by machine and by load: on the
machine these budgets were measured on, concurrent work inflates a cold check by around 1.8× with
nothing about the tree changed (`TreeOps` 45s → 77s, `JsonParse` 52s → 99s, inside one three-run
pass). So a single overshoot is noise and only a persistent one is a regression, and the budgets are
seeded from the SLOWEST observed cold run rather than a median for exactly that reason — a budget
that fires on a busy afternoon teaches a reader to ignore it. **A budget is a smoke detector, not a
gate.** `check.ps1 -Strict` promotes every cost finding to a red leg for a session that wants one;
CI deliberately does not pass it. It is also not a job timeout: a timeout says a run died and
nothing about which module, where an overshoot is attributable.

Coverage is checked both ways, and both are findings rather than failures: a module in `$modules`
with no entry is named — so adding a model tells you to budget it, without reddening the leg of
whoever added it — and an entry for a module the leg does not check is named too, so a removed model
cannot leave a budget behind pretending to measure something. The entry's SHAPE is the one thing
here that IS a failure: a missing or duplicate module name, or a `budgetSeconds` that is not a
positive number, fails the leg with the entry named, because a file in that state cannot be read as
a budget at all.

**Bumping a budget is a deliberate, recorded act.** Time the module on a cold, otherwise-quiet run,
then edit that module's entry: the new `budgetSeconds`, the `measuredSeconds` it was seeded from,
**your phase**, and a note saying what grew and why the cost is worth paying. The seeding rule lives
in the file's own `seeding` block — `max(2 × the slowest observed cold run, 20s)`, rounded up to the
next 10s, the floor being for the sub-5s modules where 2× is inside process-start noise. Editing the
number alone is how a budget stops meaning anything: the point of the entry is that a later reader
can tell a cost someone decided to pay from a drift nobody noticed.

**Why `modules.json` and not `../proofs.json`.** The ladder is a claim about CORRECTNESS, at a closed
set of four levels, checked row by row against the tree by `Proofs.Ladder`; a wall-clock budget is a
claim at none of those levels, and it is per MODULE where a ladder row is per CLAIM. One home, and
this is it — the ladder's `proof-leg` policy row points here for the cost half rather than carrying
numbers the family beside it would not be checking.

**The two cost cliffs, to be read before the next model is written.** Both have been paid for once:

- **Context pruning is the difference between minutes and tens of minutes.** `TreeOps.fst` checks in
  300s under the default SMT context and 56s under `--ext context_pruning`; `JsonParse.fst`, 128s
  against 72s. Both set it with `#set-options` INSIDE the module rather than in `check.ps1`'s flags,
  and that placement is load-bearing rather than tidy: pruning changes which facts a query can see,
  so a module that has not been checked under it must not be switched to it as a side effect of
  another module's cost. If either budget ever needs bumping by a large factor, check first that the
  option is still in force — losing it reads in the log exactly like ordinary growth.
- **An SMT pattern on a rewriting lemma is a cliff, not a slope** (Phase 134's lesson, at
  `DagFold.fst` section 11). `rev`, `app`, `ids_of` and `ops_of` rewrite into one another, and their
  lemmas deliberately carry NO `SMTPat`: left to fire on their own they turn the delta-recovery
  query into one Z3 does not return from, where the module checks in seconds without them. Each is
  called by name at the point it is needed, which is also what makes the proof read as the
  calculation it is. A pattern added to a lemma of that shape does not make a module slower — it
  makes it not finish, and no budget catches that, because there is no measurement to compare.

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

### The vocabulary is GENERATED now, and the round trip is a theorem about `idl.json` (Phase 150)

Everything above is about a **reference** vocabulary: four cases, written by hand beside the
combinators, chosen to exercise one combinator each. That was the honest scope of Phase 135 and it
said so — but it leaves theorem 1 saying nothing about the kind that landed last week, because the
real vocabulary is the wire-format specification's `idl.json`, it carries 43 kinds, 28 records, 22
unions and 46 enums, and it grows.

`Vocabulary.fst` and `VocabularyProofs.fst` close that gap for most of it. Both are **generated**,
from `idl.json`, by a fourth backend of the IDL's own code generator (`FStarTarget`, beside the F#
structural layer, the TypeScript encoder and the JSON schema) — the model AND its proof script,
from the same walk:

- **`Vocabulary.fst`** — the vocabulary's types, its `$type`-discriminated encoder and its
  tag-dispatch decoder, over the `jval` value model and the combinators above. Generic unions are
  monomorphised at the arguments the vocabulary reaches (`Binding<string>`, `Binding<bool>`,
  `Binding<SelectOption list>`, …), which is what keeps every definition and every lemma
  first-order: a parametric `Binding` would have to take its element codec as a value, and the
  round-trip lemma would then need a higher-order hypothesis relating an encoder to a decoder it
  cannot see.
- **`VocabularyProofs.fst`** — `rt_node` and the mutual family beside it: **`dec_node (enc_node x)
  == Ok x`, for every value of every modelled type**, at every depth, through every list, map,
  optional member and omit-default. Plus `dec_node_total`, the outcome's exclusivity. It verifies
  on the pinned prover and is **checked by `check.ps1 -Theorems` rather than by a default run** —
  see the cost note below, which is a measurement and not a preference.

**Why the proof script is generated and not written.** A hand-written proof over the real
vocabulary would be a theorem about the vocabulary as it stood on the day it was written — the same
defect as the reference vocabulary, one release later and harder to see. Generated, a kind added to
the IDL enters the model at the next regeneration and **re-proves itself**, and a kind whose
encoder and decoder disagree fails the leg.

**Two diffs hold it, and they are one level apart.** Step 2 of `check.ps1` holds each committed
oracle to a fresh EXTRACTION, so the artefact the suite runs is the model. The
`Proofs.Vocabulary` family holds these two committed `.fst` files to a fresh GENERATION from the
pinned corpus, so the model is the vocabulary the specification declares — an IDL that moves
without a regeneration is **vocabulary drift** and the leg names it, with
`dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar` as the remedy. These two models are
CHECKED but not EXTRACTED (`$proofOnly` in `check.ps1`): nothing here runs the model beside
production, because the decoder it models is one a generator emits into a consuming host and not
one this repository ships, so an oracle for it would be several hundred kilobytes of generated F#
that nothing compiles, calls or compares.

**What the model covers, and the two different reasons it does not cover the rest.** The emitted
header carries the live list; the shapes are:

- **A construct with no wire-level meaning in the model is a BOUNDARY**, and a typed refusal
  (`CodegenError.UnmodellableInFStar`) rather than a dropped member — a silently-dropped member
  would make the round trip a theorem about a document nobody sends. One kind of this corpus is
  refused outright: `Tabs.activeIndex` declares the default `Binding.Static 0`, and the model's
  numeric carriers are opaque type parameters, so there is no F* literal for it.
- **A kind held out of the proof vocabulary is a COST CONTROL**, which a measurement could lift.
  The rule is `FStarTarget.proofKinds`: every expressible kind that introduces no declared type
  beyond the node envelope's own closure. It selects 20 of this corpus's 43 kinds, and the reason
  it is drawn there rather than anywhere else is measured below.

Three things are deliberately NOT claimed here. The `wf` characterisation — `Ok? (dec el) == wf
el`, which the reference vocabulary carries above — is not restated over the generated vocabulary:
it needs a second generated predicate mirroring the decoder's accept set, a model-sized artefact of
its own, so the round trip covers everything the encoder can produce and the characterisation of
what ELSE is accepted is open. A **host-only** member is absent from the model entirely, because it
is never on the wire. And a **wire-visible closure** is modelled as `unit` encoding to the fixed
`"<closure>"` sentinel — not a weakening but the precise statement that the member carries no
information, and the difference between a model of 67% of the vocabulary and one of 16%, since the
node envelope reaches `Binding.Computed` and every kind reaches the envelope.

**The cost, measured — and the reason the theorems are OPT-IN.** On the pinned prover, cold, with
the leg's own flags (`--z3rlimit 40 --quake 3`; `--ext context_pruning` emitted inside both
generated modules). `WireDecode` measured 37s here against the 33s `modules.json` records, which is
what says the measurement is of this leg rather than of something adjacent to it.

- **`Vocabulary` — 322s and 398s across two runs.** Budgeted in `modules.json` like every other
  module, and checked on every run.
- **`VocabularyProofs` — tens of minutes, and not registered by default.** Its node lemma is a
  single query of 65 goals, because the node's five OPTIONAL envelope members put 32 object shapes
  into it; one of those goals is hard. At the leg's default rlimit it proved 64 of 65 and FAILED
  the third `--quake` seed — green standalone, red under `check.ps1`, the shape `Preservation`'s
  `invert_applicable` entry warns about. At `--z3rlimit 200` (the remedy that precedent took, and
  what the generated module now carries) the goal stops failing and starts grinding: no run of it
  under `--quake 3` has been observed to finish inside half an hour on this machine. Registering it
  unconditionally would take the CI proofs job from minutes to hours on every push, so it is
  behind `check.ps1 -Theorems`, and `proofs.json` carries no `proved` row for the round trip —
  a claim the leg does not reproduce is not a claim the ladder will carry. **It is not unguarded
  in the meantime**: the generation diff holds it to a fresh generation on every run, so it cannot
  drift from the vocabulary it is about; what a default run does not do is re-prove it.

**Where the cost is, which is the finding worth carrying forward: the node ENVELOPE's closure, not
the kinds.** One kind and eight kinds measured the same, because `Accessibility`, `SemanticStyle`,
`StateBehaviour`, `TextSource` and nine `Binding<…>` instantiations are paid by any kind at all.
The curve then turns superlinear in the size of the mutual family: the whole expressible
vocabulary — 42 kinds, ~30 more declared types — did not finish a single check in twenty-five
minutes. So narrowing the selection further buys almost nothing, and widening it is not a matter of
patience.

**What would make the theorems affordable, for whoever takes it.** The hard goal is hard for a
structural reason, and the fix is structural. `--split_queries always` would isolate it and leave
the other 64 as the trivialities they are, but it is not settable as a `#set-options` pragma on the
pinned prover — so the equivalent is to emit the node's proof as SEVERAL smaller lemmas rather than
one, which is generator work rather than a flag. A second lever was tried and measured WORSE, which
is worth recording so it is not tried again blind: routing conditional members through an
`opt_cons` helper with an SMT-patterned lookup law (to stop the prover splitting 2^k ways past k
optional members) made `Vocabulary` itself several times slower, because the helper's pattern then
fires throughout the encoder as well.

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

**Since Phase 146 the parser has its own theorem, and this boundary has moved rather than
vanished.** "Theorem 4" below proves the parser TOTAL, proves its two guards, and proves its
failure classification exhaustive — so the sentence above is now the boundary of *this* theorem
rather than of the directory. What is still not claimed anywhere is grammar conformance: that the
parser accepts exactly RFC 8259. Read the two together as "no input can make the parser fail to
answer, and every answer is a value or a named refusal", with which inputs get which answer settled
by the corpus at level 2.

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
4. **Not claimed.** Anything about `Json.parse`; and anything about encode — `Canon.render`'s key
   ordering and float layout are certified by the wire-format corpus, not by this theorem.

**Phase 150 amends clause 4's middle third, and it is worth saying which third.** It used to read
"anything about a domain's own decoder beyond the reference vocabulary modelled here". That is no
longer the boundary: `Vocabulary.fst` is the wire-format specification's OWN vocabulary, generated
from `idl.json`, and `rt_node` proves the round trip over 20 of its 43 kinds — so what carries to a
domain is now the combinator layer **and** a machine-checked round trip over a substantial slice of
the vocabulary the specification declares. (That theorem is checked by `check.ps1 -Theorems` rather
than on every run, for the measured reason in the cost note above, and `proofs.json` carries no
`proved` row for it while that is true.) What the amendment does NOT buy, and must not be read as buying: this
is a theorem about the vocabulary, not about any HOST's decoder. The six conformant hosts keep
their own hand-written decoders and are certified against the fixture corpus; no host's build
generates this model, and nothing here says a host implements it. The per-kind coverage, the two
reasons a kind is outside it, and the cost that decided them are in the Phase 150 section above.

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

#### The precision ceiling — the clause is NECESSARY, and that is Phase 143's result

Phase 143 was chartered to tighten it. Phase 78 pinned the clause as conservative-not-tight because
nothing could then say what a tighter clause would be sound against; Phase 138's preservation
theorem removed that obstacle, so the phase set out to prove that a relocation commutes with a
structural write under an unrelated parent and then to narrow `Ops.independent` to match.

**It proved the opposite, and section 18 of the model carries the proof.** Three facts at one
well-formed tree, `assert_norm`-checked with no admits:

- `relocation_disjoint_diamond` — a `MoveNode` and an `InsertChild` under a parent **inside the
  moved subtree** DO commute. The subtree travels intact. Every clause of `independent` except the
  pinned relocation pair already holds of them (`but_for_relocation`), so that clause is the only
  thing refusing them. The tightening's premise is real.
- `relocation_diamond_fails_for_a_remove` — swap the move for a `RemoveNode` and the diamond breaks.
  Both operations apply at the tree; remove-then-insert is `UnknownNode`, because the insert's
  parent was destroyed with the subtree, while insert-then-remove succeeds.
- `relocation_footprints_coincide` — and **the two operations carry the same footprint.** A move,
  and a batch that removes and then reorders, fold to byte-identical records across all four address
  sets, because `union_fp` is a union and neither the operation's shape nor the direction of its
  structural write survives the fold.

`relocation_clause_is_necessary` is the three together: a predicate over the four sets gives both
operations one verdict, so freeing the safe pair frees the fatal one. A single witness refutes a
universal, and that is what this is. The paragraph above therefore reads more sharply than it did: a
tighter **clause** over this record is not merely unproved, it does not exist; a tighter **footprint
record** — a fifth address kind naming the relocation's kind, or a destroyed-subtree set the pure
script cannot compute — is what a future tightening needs, and that is a breaking change to every
consumer that reads a `Footprint`.

This is also the second pinned over-approximation being load-bearing for the first. STABILITY.md has
said since Phase 78 that a `RemoveNode`'s content-write records the target id and not its
tree-unknown subtree, and that this is sound *because* the unknown-parent rule already serialises
the pair. Fact 2 is that sentence's counterexample.

**And part of the refused set could not be freed by any record.**
`relocation_move_pair_also_fails` is two `MoveNode`s whose destinations sit inside each other's
subtrees: each applies alone, and after either the other is `WouldNestUnderSelf`. The obstruction
there is the cycle check rather than the addresses. So the nine refused pairs are not one
homogeneous class waiting on a better footprint — `still_refused` enumerates them with, for each,
whether a member is known to commute (move × insert, move × reorder), known not to (remove × insert,
move × move), or unexamined by this phase.

`relocating_forces_inert` is unchanged and stays live: it is what refuses the pairs, and section 18
is why it must.

**What Phase 143 deliberately did NOT prove.** The general move-versus-structural-write theorem, of
which fact 1 is an instance. It is true; `Ops.independent` cannot consume it, so proving it would
add prover budget to every run in exchange for nothing. The phase that gains the record's
discriminator is the phase that should prove it, against the clause it will license.

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
does not currently give.

**That invariant arrived in Phase 138 and the three pairs are still open** — the sentence here used
to read "it closes when Phase 137 lands", which was true of the BLOCKER and not of the work. Phase
137 fixed the validator, Phase 138 lifted this model to it and proved `apply_preserves_wf`
unconditionally, so the hypothesis the lift was waiting on now holds; what remains is the induction,
which nobody has done. The distinction is worth keeping visible: a boundary waiting on a theorem and
a boundary waiting on labour are not the same kind of open.

The composite theorem in `Skeleton.fst` takes the same boundary as its op alphabet. That costs less
than it looks: `Ops.apply` threads a `Batch` exactly as the fold threads a lane, so the alphabet
removes no behaviour from the fold — it declines to nest one lane inside another.

`applyContained`'s container capability is out of scope and is Phase 140's; `Diff` is Phase 141's.
`Ops.invert` was named here as Phase 141's too and is theorem 5's — see
[Theorem 5](#theorem-5--apply-engine-preservation-phase-138). `NotAContainer` and
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
   its halt half under no hypothesis at all; the refutation of unconditional well-formedness
   preservation with the conditional form and its converse beside it; and, since Phase 143, that
   the pinned unknown-parent clause is NECESSARY over this `Footprint` record
   (`relocation_clause_is_necessary`, with `relocation_disjoint_diamond`,
   `relocation_diamond_fails_for_a_remove` and `relocation_footprints_coincide` under it, plus
   `relocation_move_pair_also_fails` and the `still_refused` enumeration). F\* 2026.09.06,
   Z3 4.13.3, every query 3/3 under `--quake 3`, `--report_assumes error` on, no `assume`, no
   `admit`.
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
   Nothing about `applyContained`, `invert`, `normalize` or `Diff`. About a *tighter* footprint than
   the pinned one, exactly one thing is now claimed and no more: that **no tighter clause exists
   over the four address sets this record carries** (Phase 143, above). Nothing is claimed about
   what a WIDER record would admit, about the general move-versus-structural-write theorem, or about
   the remove × remove, remove × move and remove × reorder pairs, which that phase did not examine.

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

## Theorem 4 — `Json.parse` totality, bounded (Phase 146)

`JsonParse.fst` is this directory's fifth model, and the boundary theorem 1 named. It models
`Json.parseDetailedWithPolicy` — the core parser every other entry point is a one-line wrapper over
— clause for clause: `skipWs`, `expect`, `parseString` with its eight short escapes and the
`\uXXXX` path, `parseNumber` with the Int32 and int53 token guards, the two literals, and the mutual
`parseValue` / `parseObject` / `parseArray` with the explicit depth counter, including the
`EraseMemberNull` fork that lives in the member loop and nowhere else.

The claim worth having here is not a byte-level grammar proof. It is that **the parser cannot fail
to answer, and cannot answer with an unclassified failure** — which is what the two guards are for.

Four things are proved:

- **`parse_total`.** The parser reaches exactly one outcome on every input: a value or a classified
  refusal, never both and never neither. That it is `Tot` at all is the content rather than a
  formality — F\* admits the mutual group only after showing it terminates on every input, and a
  recursive-descent parser over a mutable index has no syntactic reason to. Every consuming
  sub-parser carries `llen rest < llen input` in its RETURN TYPE, and that refinement is the whole
  termination argument, exactly as `get_prop`'s `jsize` refinement was theorem 1's.
- **`depth_bound_exact`.** Three statements, because the obvious one-line form is false. The naive
  reading — "input nested past the bound fails with `MaxDepthExceeded` and never otherwise" —
  ignores that the parser is left to right and the first failure wins: `[bad, [[[…]]]]` is an
  `UnexpectedChar` however deep it goes. What is true and proved: the kind is CONFINED to a
  container position (`depth_confined`, carried in every signature so it is re-checked at every call
  site rather than asserted once); on the canonical nesting family the boundary is EXACT, at every
  depth and for every suffix (`nest_accepts` / `nest_refuses`); and the bound touches nothing that
  is not nested (`depth_bound_scalars_unaffected` — a scalar parses at a cap of zero, where every
  container is refused).
- **`int53_guard_exact`.** Every integer token the parser ACCEPTS denotes an int53-safe value,
  whichever branch produced it. See the finding below: "exact" is not available, and the two halves
  that are — soundness and conservatism — are proved separately and both.
- **`error_kind_exhaustive`.** The twelve `JsonErrorKind` cases are confined to the layers that
  raise them, and every one of the twelve is REACHABLE, by a named witness input. A classification
  whose cases are unreachable is closed but not exhaustive, and the difference is exactly what makes
  the differential's coverage mean anything.

### The finding: the int53 guard is SOUND and CONSERVATIVE, not exact

`Wire.fs` justifies comparing the digit string lexically with "JSON forbids leading zeros, so for
equal length that IS the numeric order". **This parser does not enforce that.** `parseNumber`'s
digit loop accepts `007`, and for a token `Int32.TryParse` refuses, leading zeros lengthen the
string without changing the value — so `0009007199254740992`, which is exactly 2^53 and therefore
the largest int53-safe integer there is, is refused as `MalformedNumber` because its *string* is 19
characters.

The error only ever runs that way. A padded reading is numerically smaller than an unpadded one of
the same length, so no amount of padding can make an unsafe integer look safe. So:

- `int53_guard_sound` — the guard as production spells it never admits an integer outside the range.
  This is the half that matters: over-refusing costs a user an error message, under-refusing costs a
  corrupted identifier.
- `int53_guard_conservative` — a witness it refuses and should not, exhibited rather than described,
  so that anyone who "fixes" the guard to match its own comment reddens a lemma and is told the
  parser's behaviour changed.

A second correction to how the guard is usually described, worth stating because the model had to
get it right to prove anything: **the int53 test does not guard `JInt`.** An integer token is
offered to `Int32.TryParse` first, and a success yields `JInt` with no int53 test at all — the Int32
range is a strict subset, so a test there would be vacuous (`int32_within_int53` proves it). The
int53 test governs only the tokens Int32 refused, and decides `JFloat` against a named refusal.

### The boundary — what is modelled at what fidelity

- **The value of a `\uXXXX` escape is not computed in the model.** Its two GUARDS are — a truncated
  escape is `TruncatedUnicodeEscape`, a bad hex digit is `BadHexDigit`, and each reports
  production's own position — but the code point the four digits denote is carried symbolically,
  because computing it needs integers and the extraction deliberately carries none (finding 2).
  The differential resolves it host-side from the digits the model carried, so the decoded character
  is still compared; what is not proved is the transliteration. This is the grammar path the phase's
  time box named, and it is the one that remains at level 2.
- **The float readback is opaque.** `Double.TryParse`'s verdict — parses finitely, parses to a
  non-finite, does not parse — is a PARAMETER, as `float i` was theorem 1's and the hash was theorem
  3's. What the model decides is which of the three verdicts leads where, which is the parser's own
  logic rather than .NET's.
- **The depth cap is a list and the rendered cap a string parameter**, for the same reason
  `Chain.fst` spells a sequence number as a Peano numeral: the extraction carries no integers. Every
  non-negative cap is representable, and the shipped `defaultMaxDepth = 512` is one of them.
- **A character is a constructor.** The alphabet has one constructor per character the parser
  distinguishes plus `COther`, which carries the character verbatim — so two different characters
  are never identified and the bridge loses nothing. What the model reads is a `list ch` where
  production reads a .NET string, and the differential's bridge is what says those are the same
  sequence.
- **A number carries its TOKEN, not its value.** Which constructor the parser chose is the guard
  under test and is modelled exactly; the numeral is .NET's to compute, and the host reads the
  model's token back with the same call production made — so a model that scanned one character more
  or less renders a different value and the differential sees it.

### What the corpus covers

The differential host (`Proofs.Oracle` in `../tests/Fuaran.Core.Tests/ProofOracleTests.fs`) runs the
extracted model beside `Json.parseDetailedWithPolicy` over the same TEXT. Every probe renders both
answers into one string carrying the outcome class, the decoded value and — on a failure — the
KIND, the POSITION and the MESSAGE. The position is the half most likely to drift silently, which is
why it is compared rather than described.

| Pool | What is asked | Both classes exercised |
|---|---|---|
| a hand-written near-miss table | 55 inputs reaching every classified failure, the two `null` near misses (`nul`, `nullish`), every escape, and both numeric boundaries from both sides | yes (asserted) |
| the same, under the tolerant policy | the whole table again under `EraseMemberNull`, with the member-null fork asserted to have FIRED | yes (asserted) |
| the wire corpus's `nodes/` fixtures | every fixture as raw text — the accept path, asserted non-trivial in size | accept path |
| the wire corpus's `ops/` fixtures | the same | accept path |
| generated deep and wide inputs | both nesting families at every depth 0–8 under every cap 0–8, so the BOUNDARY is compared and not merely the interior; arrays and objects of 0, 1, 2, 10 and 64 members | yes (asserted) |
| 4,000 generated inputs | seed-replayable character soup over the parser's own alphabet, under both policies — the only pool that reaches failure positions nobody thought to write down | yes (asserted) |

Two **go-red** cases, because the two things this family could get wrong are different. The first is
the one the phase asks for: a model holding a cap production has already spent accepts a document
production refuses by name, so the depth comparison is known to be one that can lose on exactly the
guard the phase is about. The second is a *blind bridge* that hides the two container characters —
this family's counterpart to the decode family's blind integer bridge — under which every structured
document must disagree while a scalar still agrees, so the instrument is known to be narrow as well
as effective.

The **proof** was falsified the same way before it was trusted, on scratch copies: removing the
depth check from `parse_array` reddens `nest_refuses`; making the int53 guard exact (stripping
leading zeros, as production's own comment claims it may) reddens `int53_guard_conservative`; and
dropping the entry point's trailing-input test reddens `error_kind_exhaustive`. Each landed on the
lemma that should have caught it. The differential's own position comparison was falsified too — an
off-by-one in the recovered index reddens three legs — because a comparison that agrees on the first
run is the least-examined kind of evidence.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** On the model: the four results above, for every input,
   under no hypothesis about a domain. F\* 2026.09.06, Z3 4.13.3, every query 3/3 under `--quake 3`,
   `--report_assumes error` on, no `assume`, no `admit`. The module carries a scoped
   `--ext context_pruning` (72s against 128s) for the reason `TreeOps.fst` gives at the same line.
2. **Differentially tested.** The extracted model agrees with `Json.parseDetailedWithPolicy` over
   the pools above, on class, value, kind, position and message, under both policies. Agreement is
   over those pools, never over all inputs.
3. **Assumed, and stated as such.**
   - **The float readback.** `Double.TryParse`'s three-valued verdict is a parameter, instantiated by
     the host with the call production makes. Nothing here is said about float precision, about
     which tokens .NET accepts, or about the shortest-round-trip layout — that is the wire corpus's.
   - **`Int32.TryParse` is modelled lexically.** The model reads the Int32 range as a comparison
     against the range's own digits, after stripping leading zeros — which is what `Int32.TryParse`
     does to a digit string, argued rather than mechanised, and measured by the differential's
     numeric-boundary cases from both sides.
   - **A character is a constructor, and the bridge is the correspondence.** The model's `list ch`
     is the production string only because the host's `toCh` says so. That mapping is one line per
     character and is not itself proved.
   - **The `\uXXXX` code point.** Sampled, per the boundary above.
   - **The extractor and the F# compiler are trusted** — the same link, and the same wording, as for
     the other four theorems.
4. **Not claimed.**
   - **Grammar conformance.** That the parser accepts exactly RFC 8259. It is not proved and is not
     the point: what the theorem adds is that the parser cannot fail to answer and cannot answer
     with an unclassified failure. Which inputs get which answer stays with the corpus and the
     near-miss fixtures.
   - **Anything about the `\uXXXX` transliteration**, per the boundary above.
   - **That the depth bound prevents a stack overflow ON .NET.** The model proves the bound is
     reached and named; that 512 frames of `parseValue` fit in a .NET thread's stack is an
     engineering judgement about a runtime, and no model here says anything about it.
   - **Theorem 1's policy assumption is not discharged.** `WireDecode.fst` assumes, at level 3, that
     the parser's member-null absorption is equivalent to erasing member nulls from the strict tree.
     This model puts the fork where production puts it and the differential exercises both policies,
     which is better evidence than that assumption had — but relating two models is its own phase and
     was not taken. The assumption stands where it is.
## Theorem 5 — apply-engine preservation (Phase 138)

_(This directory's fifth; the attested-stack programme's SECOND. The heading numbers here are the
running count, per the note under theorem 3 — the programme's numbering is separate, and this is the
one place the two are far apart.)_

The programme states it in one sentence, and it is quoted here as the claim rather than paraphrased:

> **WS6.1(b): a legal op applied to a valid tree yields a valid tree.**

[Phase 133](../proofs/TreeOps.fst) proved one clause of that — id uniqueness preserved by an insert,
and only conditionally, because the validator of the day admitted a graft that broke it. The other
clauses were stated nowhere: not in a test, not in the conformance pack, not in this file.
`Preservation.fst` states and proves them, importing `TreeOps.fst` rather than remodelling, over a
**lawful abstract witness** — `ReplaceChildren` taken as an abstract function satisfying
`Conformance.witnessLaws`, exactly as `DagFold.fst` takes the diamond.

### What the four other clauses say, and why each is worth a theorem

- **`apply_total`.** The engine reaches exactly one outcome on every input — the type of the
  function, discharged by construction — and WHICH rejection each clause can raise is characterised
  rather than listed. That is the content: `NotAContainer` and `Rejected` are now *proved*
  unreachable from `apply`, where three README sections asserted it in prose. A `Batch` inherits
  exactly the union of its members'.
- **`reject_identity`.** A refused step leaves the caller holding the input tree. For one operation
  that is a type-level fact of persistent values that no test and no law names; the theorem names
  it. For a script it has real content, because `applyWith`'s `go` threads the tree and several
  intermediate trees exist by the time a step fails — so the statement is quantified over an
  arbitrary failure position in an arbitrary batch, and says none of them escapes.
- **`apply_preserves_wf`.** Unconditional at last, for all five operations. It is true now because
  Phase 137 changed the CODE, not because the model changed its mind, and the bridge between those
  two facts is its own theorem: `TreeOps.first_dup_none_iff` proves the shipped scan decides exactly
  `ins_wf`'s hypothesis — neither weaker, which would leave the invariant breakable, nor stronger,
  which would refuse safe grafts. Its remove and move clauses needed machinery Phase 133 never had,
  chiefly that a remove takes the whole removed subtree's ids away with it (`rem_kills`), which is
  what makes the move's re-insert provably fresh.
- **`canapply_preserves`.** The dry run accepts exactly what the mutating call accepts. The sampled
  law becomes a corollary — and the proof turned up something better than the law: the two
  `| None -> Error(UnknownNode …)` fallbacks the engine carries after its `Tree.parentOf` lookup are
  UNREACHABLE, because a non-root id the tree holds always has a parent (`parent_exists`). The two
  surfaces cannot diverge by one of them taking a branch the other does not.
- **`invert_applicable`.** The inverse of an accepted operation is accepted at the result AND
  restores the input. `Ops.invert`'s own doc comment states `apply (invert op pre) (apply op pre) =
  pre` as its defining law and the conformance pack samples it; here it is proved, for the four
  non-`Batch` operations.

### Phase 133's refutation is restated, not deleted — and that is deliberate

The model carried `insert_breaks_wf`, a machine-checked counterexample to the unconditional
statement above. Lifting the model to Phase 137's validator makes that lemma FALSE, and there were
two honest things to do with it. Deleting it loses the record of what was wrong, which is the part a
reader a year from now needs most. So it is restated: `validate_insert_pre137` and `apply_pre137`
name the old clause explicitly, `insert_breaks_wf_pre137` is its counterexample unchanged, and
`cx_insert_refused_now` proves the LIVE model refuses that very insert, naming the descendant id as
the offender. The pair reads as one sentence — this was admitted, and it is not any more — and the
second half is a go-red by construction: narrow the validator back and it stops verifying.

### The differential the Phase 133 family structurally could not run

Phase 133's tree differential draws its inserts from Phase 80's generator, whose `FreshNode`
contract is *an id not in the tree*. It therefore cannot mint a colliding graft — so for the whole
of Phase 137 it was green while the extracted model carried the old validator and production carried
the fixed one. **A differential over a pool that cannot reach the disputed inputs is not evidence of
agreement about them**, and this family exists to say so with a measurement rather than an argument.

| Pool | What is asked | Both classes exercised |
|---|---|---|
| the generated op pool × every state a prefix of it reaches | apply verdict, accepted result through `Tree.encodeHash`, rejection by class | yes (asserted) |
| grafts minted per state from the state's OWN ids — a descendant the tree holds, an id repeated within the graft, both at once, and a clean multi-node graft | the same, plus `canApply` against `apply` on each side separately | yes, counted per disputed shape |
| every accepted leaf operation | the derived inverse as an OPERATION, and the round trip run on production | yes (asserted) |

The adequacy guards are per disputed shape rather than in aggregate, and each is re-read through
`Tree.ids` rather than trusted from the construction that made it: measured at 12 trials, 276
accepted, 1,082 refused, 507 colliding grafts, 294 internally-duplicated grafts, 276 inversions.

**The go-red needs no instrument built for it.** `TreeOps.apply_pre137` IS the model of a validator
that skips the subtree check, so handing it to the same differential is exactly the measurement —
and it must lose, naming the specific disagreement (production refuses the graft; the old clause
admits it). A third case pins the concrete counterexample on the SHIPPED engine as well as on both
models, so the refutation and its fix are recorded on all three surfaces rather than two.

**Phase 139's `apply/` fixture family does not exist yet** — it lands after this phase — so the pool
above is the generator plus the constructed grafts, and the differential says so rather than
implying corpus coverage it does not have.

### One finding about proof cost, and it is not the one Phase 133 recorded

Phase 133's finding was that context pruning is the difference between seven minutes of CI and
forty. This one is about the QUERY BOUNDARY, and it was worth more than the flag. The remove round
trip was first written as one lemma: the tree descent and the children-level rearrangement argument
in a single proof. Z3 spent 507 seconds of CPU and 2.7 GB on it and did not terminate inside ten
minutes. Splitting the children-level fact into `parent_node_restore` — its own lemma, its own small
context, the same lemmas in the same order — took the whole module to 21 seconds. Nothing about the
mathematics changed; what changed is how much of it any one query had to see at once.

The second half of the same finding: `invert_applicable` proves 78 goals in one query, and at the
leg's default budget it sat close enough to the ceiling that a different `--quake` seed exhausted
it. It was green run standalone and RED under `check.ps1`. A proof that flakes near the ceiling is a
leg that fails in CI and passes on a desk, which costs more than a slow leg; the budget is raised
locally with `#push-options` and the reason is in the source beside it.

### The container variant — `applyContained`, the premise that remains, and the one the code took over (Phases 140, 161)

Everything above is about `Ops.apply`, which is `applyWith (fun _ -> true)`. `Ops.applyContained
canHold` is the variant every domain with a leaf actually runs, and it is the one where the
`NotAContainer` clause proved unreachable above is live. Section 8 of `Preservation.fst` closes it,
with `canHold` **abstract in the strongest sense available** — a parameter of every definition and
every theorem, so each is universally quantified over it. An `assume val` would be the other shape
and is not available in any case: the leg runs `--report_assumes error`.

**`NotAContainer` is exactly the container violation's refusal**, and that is three statements
rather than one. `apply` never raises the class (the characterisation above). The container-aware
engine differs from `apply` **only** by raising it — never accepting what `apply` refuses, never
producing a different tree, never refusing under some other class of its own. And where it raises
it, the node named really holds children, the kind tag reported is that node's own, and the
predicate refuses it.

**Where that node IS became a disjunction at Phase 161, and the disjunction is content rather than
a weakening.** There are two capability sites and they name nodes in two different places: the (new)
PARENT, which is a node of the tree; and an interior node of the SUBTREE an `InsertChild` carries,
which is a node of the caller's own graft and is deliberately *not* in the tree — the duplicate-id
scan has already refused a graft sharing any id with it. A reader who looked the named id up in the
tree and found nothing would be right to call that a defect, which is why the theorem says which
lookup succeeds rather than asserting one that is false of the second site.

**What that formulation deliberately does not say is the part the differential taught.** The
capability can **pre-empt** another refusal rather than only adding one: the shipped `MoveNode` arm
tests `canHold` on the new parent *before* the descendant test, so a self-move into a leaf is
`NotAContainer` here and `WouldNestUnderSelf` under `apply`. The first draft of the differential
asserted that the two engines refuse the same operation with the same class unless the capability
adds a refusal, and it went red on the first run against a tree the generator had built. The
theorem reads "identical, **or** `NotAContainer`" because the stronger sentence is false.

**The preservation statement needed two hypotheses, and without either it was false of the function
Phase 140 measured.** Both are exhibited as machine-checked counterexamples, in the shape Phase 133's
`insert_breaks_wf_pre137` established, rather than left to be discovered. **They now have different
statuses, and the difference is Phase 161.**

- **`contained_op` — DISCHARGED BY THE CODE (Phase 161).** `canHold` used to be applied to the
  PARENT and to nothing inside the graft, so inserting a subtree whose own interior node was a
  non-container carried the violation in with it: a domain grafting its own validated documents had
  the hypothesis, and one accepting a foreign tree did not. The operator's ruling was to inspect the
  graft (`DECISIONS.md` D38), so `validateInsert` now walks the inserted subtree and refuses, with
  `NotAContainer` naming the first interior offender. The premise is gone from
  `contained_preserves`. The counterexample is **not** gone: `nested_graft_refused` states both
  halves in one lemma — the pre-161 engine (`apply_contained_pre161`, kept for the reason
  `apply_pre137` is kept) still admits the graft and still breaks the invariant, and the shipped one
  refuses the same operation naming the offender. So the reason the premise was needed survives the
  premise, and removing the walk turns the second half red. `contained_graft_still_admitted` is
  beside it, because without it the first half would be satisfied by an engine that refused
  everything.
- **`child_blind` — STILL A HYPOTHESIS, and it always will be.** `canHold` has type
  `'Node -> bool`, so it may read the CHILD LIST, and a predicate that does can admit a node at the
  moment it is checked and refuse it the instant it gains a child. **No check placed anywhere in the
  engine repairs that**, because the predicate's answer changes under the very edit the check
  licensed — which is why it is the one of the pair that could not be discharged by a code change.
  Every domain writes `canHold` as a function of the kind tag, which is also what `NotAContainer`
  reports back, and this hypothesis is that habit made a premise. Since Phase 161 it is a habit the
  domain **certifies**: `Conformance.containerLaws`' first law perturbs a node's children and
  requires the predicate to be unchanged, so an adopting domain meets the premise as a red law
  rather than as a broken tree.

With `child_blind`, `contained_preserves` holds for all five operations including a nested batch,
and `MoveNode` is covered as well as `InsertChild`: the moved subtree inherits the invariant from
the tree it came out of, the removal preserves both the invariant and the destination's capability,
and the graft is then an insert under an admitted parent.

**And that move clause is why the Phase 161 walk is at `InsertChild` ONLY.** It derives the moved
subtree's containment from the tree's own, with no hypothesis about the operation at all — a move
relocates structure that is already there, so it introduces no interior the tree did not already
hold, and a violation found inside a moved subtree was carried in by some earlier insert. Walking
there would refuse an operation for a pre-existing fault elsewhere: an invariant-repair gate, which
is a different feature. The walk belongs where new structure enters.

**The differential draws the predicate**, because the theorems quantify over it and a run against
one hand-picked `canHold` certifies that instance and nothing about the quantifier. Each trial
draws a random subset of the pool's kind vocabulary; all eight are drawn at 30 trials, the empty
predicate (nothing can hold children) and the total one (where the engine is `apply` again)
included. Adequacy is counted per capability SITE — insert refusals, move refusals, batch
inheritance, **graft refusals** (Phase 161) and pre-emptions separately — because a run reaching
only the insert site could not catch the go-red, which is a move. The go-red is the model's own
instrument (`apply_contained_insert_only`), proved in F\* to admit a move the real one refuses and
to break the invariant doing it, before the differential is asked to lose against it.

**The graft site had to be reached by CONSTRUCTION, and that is worth knowing because the pool could
not reach it at all.** Every insert the Phase 140 pool minted was a leaf, and a leaf graft can never
carry an interior offender — so a differential left as it was would have certified the new walk with
a sample structurally incapable of exercising it, and reported green. Phase 161 mints a graft whose
root holds a child, in both a container kind and a leaf kind, so whether it is an offender is exactly
whether the drawn predicate admits its kind and both verdicts arise within one run. Measured at 30
trials, seed 1400: 124 graft refusals. Two of the family's other numbers moved with it, for reasons
that are not "the pool grew": insert refusals rose 185 → 649 because the graft probes are inserts,
and the invariant is now asserted on 1,028 probes where it was 704, because retiring `contained_op`
removed a *gate* — probes whose graft broke the invariant used to be skipped, and are refused now.

**And one finding about the shipped surface, which is not about the model.** When Phase 140 ran
there was **no container-aware sequence surface**: `Ops.canApplyAll` and `Ops.applyAll` both
threaded the plain `apply`, so a script `canApplyAll` certified could be refused by
`applyContained` at its first step — one move of a leaf under a leaf is the counterexample. A
caller that pre-flights with `canApplyAll` and executes with `applyContained`, the combination the
two doc comments invite, had a check that could not see the refusal its executor would make. 140
recorded it rather than fixing it, because closing it is an API change with a version;
`can_apply_all_ignores_containment` and its production-side case are what made the gap visible to
whoever took it. **Phase 160 took it — the next subsection.**

### The sequence surface — `applyAllWith` / `canApplyAllWith`, and what the plain pair is now (Phase 160)

Section 9 of `Preservation.fst` is the surface that closes the finding above, modelled clause for
clause with the same `canHold` parameter section 8 carries. `Ops.applyAllWith` and
`Ops.canApplyAllWith` are `applyAll` / `canApplyAll` with the capability threaded — the relation
`applyContained` has to `apply`, at the sequence level — and the plain pair is RESTATED as their
`fun _ -> true` instances, so no existing caller moved.

**The failure payload is modelled here and was dropped in section 8, and that is the whole
difference.** `can_apply_all` drops the refusal INDEX because nothing in 140 turned on it. Here
everything does: the index says which step was refused, and the partial tree is what the caller is
left holding. A `Batch` is all-or-nothing inside one operation — it aborts and the original tree
survives — while a SCRIPT keeps the accepted prefix. The two folds are structurally the same walk;
they differ in what the failure carries, and that difference is the semantics.

**So the preservation statement is stronger here than per operation.** A per-op theorem may say
nothing about a refusal, because a refused `applyContained` hands back nothing at all. A refused
script hands back a tree the caller keeps, so `contained_preserves_all_with` covers **both** arms —
the accepted tree and the partial tree — under the same hypothesis section 8.5 carries. That is the
statement that makes a non-atomic container-aware surface safe to use rather than merely available.
(It carried section 8.5's *two* hypotheses when Phase 160 cut it, and lost `contained_op_all` with
Phase 161 for exactly the reason 8.5 lost `contained_op`: the sequence lemma only ever used that
premise *through* the per-operation call, and the engine inspects a graft now.)

**`can_apply_all_with_agrees` needs no hypothesis, and 8.5's per-op `can_apply_contained_agrees`
needs `wf t`.** The per-op dry run reaches the three validators directly and has to be shown not to
take a branch the mutating call does not. The sequence pair cannot diverge that way by
construction — both fold the same per-op call — which is the honest reading of why the shipped
`canApplyAll` threads `apply` rather than `canApply`: a sequence is order-dependent, so each step's
check must see the prior step's tree and there is nothing to gain by checking without building.

**The plain pair does not move, and it is proved rather than asserted.**
`all_with_at_total_is_plain` discharges the restatement against `TreeOps.apply_all` and section 8's
`can_apply_all` — the two folds as they stood BEFORE this phase — so what is proved is agreement
with the shipped behaviour rather than with a restatement of itself. A consequence worth stating,
because it reads like an omission: the plain pair remains BLIND to containment and always will be,
that being what the total instance means. `can_apply_all_ignores_containment` is therefore KEPT
rather than retired, and its meaning has changed — it was a finding and is now the pin that the
plain pair's behaviour did not move. It is read beside `can_apply_all_with_sees_containment`: the
same script, at the same tree, certified by the plain dry run and refused at index 0 by the
container-aware one with the envelope `applyAllWith` raises.

**The differential's go-red is the model's own capability-free sequence check.** The extracted
`apply_all_with` / `can_apply_all_with` run beside the shipped pair over generated scripts and a
drawn predicate, comparing the verdict, the accepted tree, the rejection class, the refusal INDEX
and the PARTIAL TREE. Adequacy counts MID-script refusals separately, because a refusal at index 0
returns the caller's own input and so compares the partial tree against nothing. Substituting
section 8's `can_apply_all` into the dry-run slot — a weakening `can_apply_all_ignores_containment`
already proves — must make the run lose, and it does.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** The five lemmas above, over the five skeleton operations
   and any tree, plus the two bridges the lift needed (`first_dup_none_iff`, `wf_iff_no_dups`) and
   the restated counterexample with its closure. **Phase 140 adds six**, over the same operations,
   any tree and ANY predicate: `apply_contained_is_apply` (the container-aware engine at
   `fun _ -> true` IS `apply`, which is what makes the clause-for-clause model checkable rather
   than asserted), `not_a_container_exact`, `contained_preserves`, `can_apply_contained_agrees`, and
   the premise refutation `contained_needs_child_blind` — plus `can_apply_all_ignores_containment`,
   which proves the sequence-surface gap above.
   **Phase 160 adds four**, over the sequence surface that closes that gap, any script, any tree
   and ANY predicate: `contained_preserves_all_with` (the invariant survives a script including the
   partial tree a refusal returns), `can_apply_all_with_agrees` (the dry run reaches the same
   verdict, index and envelope as the mutating call), `all_with_at_total_is_plain` (the plain pair
   IS the `fun _ -> true` instance, discharged against the pre-160 folds) and
   `can_apply_all_with_sees_containment` (140's counterexample, answered at the same tree).
   **Phase 161 adds two and REMOVES a hypothesis from three**: `nested_graft_refused` (the pre-161
   engine breaks the invariant on a graft the shipped one now refuses, naming the offender) and
   `contained_graft_still_admitted` (the refusal is not over-eager — a contained graft still goes
   in), while `contained_preserves`, `contained_preserves_all` and 160's
   `contained_preserves_all_with` all shed `contained_op`. That is the only direction of travel the
   ladder should ever show for a premise: discharged by a code change, with the refutation kept
   evaluable, never quietly dropped.
   F\* 2026.09.06, Z3 4.13.3, every query 3/3 under
   `--quake 3`, `--report_assumes error` on, no `assume`, no `admit`.
2. **Differentially tested.** The extracted model agrees with `Ops.apply`, `Ops.canApply` and
   `Ops.invert` over the pools above, with the go-red required to lose. Agreement is over those
   pools, never over all inputs. The container family is a second pool with its own go-red: the
   extracted `apply_contained` / `can_apply_contained` beside `Ops.applyContained` /
   `Ops.canApplyContained` under a drawn predicate, with the invariant asserted on production
   wherever its hypothesis is met — which since Phase 161 is the input tree alone, so the
   conclusion is asserted on more probes than the phase before it. The SCRIPT family (Phase 160) is
   a third: the extracted `apply_all_with` / `can_apply_all_with` beside `Ops.applyAllWith` /
   `Ops.canApplyAllWith` over generated scripts and a drawn predicate, comparing the refusal index
   and the partial tree as well as the verdict and the class, with the capability-free sequence
   check as its go-red.
3. **Assumed, and stated as such.**
   - **The witness laws themselves.** `ReplaceChildren` is an abstract function satisfying
     `Conformance.witnessLaws`; a domain's own is that domain's promise, sampled by the pack and
     proved by nothing. Where it does not hold, every theorem here is about a different function
     from the one that runs. The pack's own boundary is worth knowing: `witnessLaws` exempts a leaf
     from the round trip, so a witness whose `ReplaceChildren` is partial on leaves is lawful.
   - **The witness surface is the whole tree.** `Tree.ids` walks `NodeWitness.Children`, so a node a
     domain holds in a keyed, non-structural position is invisible to the uniqueness scan and to
     every theorem here. Uniqueness over those positions is the domain's own obligation.
   - **The tree the engine is handed is id-unique.** Unchanged from theorem 2, and NARROWED by this
     one: the algebra can no longer break the invariant on a tree that has it, so what remains
     unenforced is the entry point and only the entry point. No shipped type carries it.
   - **The extractor and the F# compiler are trusted** — the same link, and the same wording, as for
     the other four theorems.
4. **Not claimed.**
   - **A `Batch`'s inverse.** `invert_applicable` is stated over the four non-`Batch` operations. A
     batch's inverse is its members' inverses in reverse order, each derived against the state that
     member saw, so the lift is the one `DagFold.replay_diamond` already performs at lane
     granularity — no new idea, and not done here.
   - **Containment LEGALITY** — which kind may parent which. `canHold` answers only "can this node
     hold children at all", and Phase 140's theorems are about that predicate alone; a domain's
     parenting rules are its own, and no theorem here says a contained tree is a legal document.
     _(The container capability itself was named here as Phase 140's and is DONE — section 8 of
     `Preservation.fst`, above.)_
   - **`Ops.normalize`**, which no theorem in this directory reaches. _(`Diff` was named here as
     Phase 141's and is DONE — theorem 6, below. What that theorem does NOT reach is stated in its
     own ladder rather than here.)_
   - **Vocabulary and schema validity.** Whether a tree is a legal DOCUMENT is the wire boundary's
     question (`decode_node_wf`, theorem 1) and the domain rule families'; nothing here says a
     preserved tree is a meaningful one.
   - **Rejection PAYLOADS.** As for theorem 2: the differential compares a refusal by class, and
     `UnknownNode`'s `addressable` and `ReorderMismatch`'s two orders are outside the comparison.
   - **The three open `Batch` diamond pairs**, which theorem 2 left open pending exactly the
     invariant proved here. They are now **unblocked rather than blocked** — `apply_preserves_wf` is
     the hypothesis the lift was waiting on — and they stay open because nobody has done the
     induction. A boundary waiting on a theorem and a boundary waiting on labour are different
     things, and the distinction is recorded rather than smoothed over.

## Theorem 6 — the diff's refusals and its emission order (Phase 141)

`Diff.toOps` is the apply engine's companion in the other direction: given two trees, derive a
script that turns one into the other. Its law was already sampled — `Conformance.diffLaws` has
asserted reconstruction, applicability and survivor preservation since Phase 03 — and **the sample
was narrower than the claim in a way the law could not see.** `diffLaws` builds `before`, then
derives `after` by APPLYING random operations to it. So every pair it has ever diffed is one the
algebra could already reach, and the interesting half — that a script exists between two trees
nobody built from one another — was never asked.

`TreeDiff.fst` models the function clause for clause and proves what the four passes guarantee.
Read the ladder below before the summary, because the boundary is unusually large for this
directory and it is deliberate.

### What is proved

**The refusals, exactly.** `diff_refusals_exact` says `toOps` refuses precisely when the two roots
carry different ids or either tree repeats one, in that precedence, with the class each raises —
and, the half that makes it a characterisation rather than a list, **on nothing else**. The
contrapositive is the sentence the phase exists for and is stated as its own lemma:
`diff_ok_on_any_wf_pair` — for ANY two well-formed trees sharing a root id, a script is produced.
`diff_never_target_not_a_container` closes the envelope the way `apply_total` closes `apply`'s: the
third error class is `toOpsContained`'s alone and the plain diff cannot reach it.

**The emission order.** `Ops.fs`'s doc comment argues, in prose, that the emitted order is always
applyable: added nodes go in as leaf shells top-down, every survivor is then reattached, and removed
regions are deleted **last**, so a surviving child is pulled out before its old container goes.
`diff_emission_order` is that argument as a property of the emitted list — the script IS
`inserts ++ moves ++ removes ++ reorders`, four homogeneous blocks, each exactly one pass's output.

**What each block guarantees**, which is the argument's four clauses:
`diff_inserts_are_new_leaf_shells` (a graft is CHILDLESS, its id is one `before` does not carry, and
the parent named is that node's after-parent); `diff_moves_are_survivor_reattachments` (a move names
a survivor whose before-parent is not the destination, and the destination HOLDS it in `after`);
`diff_removes_only_dead` — **survivor preservation, which `diffLaws` samples and this proves**, so a
relocated survivor diffs to a `MoveNode` and never to a remove-and-reinsert; and
`diff_reorders_state_the_after_order`.

**The container mirror.** `diff_contained_at_total_is_plain` (at a predicate that refuses nothing,
`toOpsContained` IS `toOps` — the relation `applyContained` has to `apply`, in the diff direction),
`diff_contained_diff` (it is the plain diff unless it is a `TargetNotAContainer`, in the DIFFERENCE
shape Phase 140 found was the honest one), `diff_contained_locates` (the offender it names is a real
node of `after` that really carries children and really is refused), and the phase's fourth task:
`diff_applicable_contained` — **every parent address an emitted operation carries resolves, in
`after`, to a node that holds the addressed child and that `canHold` accepts.** So a script
`toOpsContained` returns cannot be refused for containment at any step, and the typed refusal is the
exact price of that guarantee.

### What is NOT proved, and why the boundary is where it is

`diff_reconstructs` (`applyAll (toOps b a) b = a`) and the OPERATIONAL `diff_applicable` (every
emitted step is ACCEPTED in sequence) are **differentially tested here and not proved**. Both need
the same missing piece: a positional argument relating a preorder walk of `after` to the
intermediate trees the script builds. Informally it is short — a parent precedes its children in
preorder, so by the time `MoveNode(c, p)` is emitted while processing `p`, every after-ancestor of
`p` has already been placed and `p` cannot be inside `c`'s subtree — and mechanising it means
reasoning about `ins` and `rem_at` over intermediate trees, in the cost class `Preservation.fst`
occupies rather than the one this module does. This phase was time-boxed and did not take it. The
boundary is stated rather than smoothed over because a reader who sees "the diff is proved" and
assumes reconstruction is proved would be wrong about the one thing they most likely care about.

### The differential, and the two go-reds

The pool is the widening. Two trees are drawn INDEPENDENTLY over a shared id space and share only
what the draw gave them; nothing derives one from the other.

**One constraint on the pair, and it is a property of the problem rather than of the generator.** A
node's content is a function of its id, so the two trees agree on the content of every id they
share. Skeleton operations relocate and delete nodes; they cannot edit one — per-kind property edits
are out of `Core.Ops`' remit, which `toOps`' own doc comment says — so a pair whose shared id carries
a different kind in each tree is unreconstructible by ANY skeleton script, and asking for one is
asking the wrong question. This was measured before it was believed: a first generator that redrew
each tree's kinds independently reconstructed 1,738 of 4,000 pairs; with content keyed to the id,
20,000 of 20,000, with no applicability refusal and no survivor removed. **The shard's own sentence
("for any two well-formed trees with the same root id") is therefore slightly too strong as written,
and the sharpened form is what the theorem and the sample both use.**

Six comparisons per pair, and the fourth is worth naming: the extracted `script_shape` — the
conjunction of the four block characterisations — is evaluated over PRODUCTION's own emitted script,
so the theorem is held to the engine and not only to the model of it.

**Go-red 1, the order.** `diff_emission_order` proves the script is four homogeneous blocks, so a
stable partition by operation kind recovers those blocks exactly and reassembling them with the
removes FIRST is the same four passes, permuted. It must lose, and it does: 52 of 240 pairs fail to
reconstruct. Not all of them, and that is the honest shape of the claim — the permutation is
harmless on a pair whose removals happen to hold no survivor, which is most of them; what the order
buys is correctness on the rest.

**Go-red 2, the container check.** The model's own plain `to_ops` in the contained slot is exactly
"an oracle that emits an insert under a non-container", because it never looked —
`diff_contained_at_total_is_plain` proves it is the refuses-nothing instance, so handing it a
predicate that refuses something is a proved weakening before it is a measured one. It must
disagree, and it does.

`Conformance.diffContainedLaws` is the kit-side half: `toOpsContained`'s scripts certified through
`applyAllWith` / `canApplyAllWith` under the witness's own predicate, which is the executor such a
script belongs to — `diffLaws` certifies them with the PLAIN pair, which Phase 160 proved is blind
to containment by construction. Its third law is refusal exactness, and its demanding direction was
measured rather than assumed: 100 of 200 iterations mint a violating probe, all 100 are refused with
the offender named by id and kind, and all 100 are ACCEPTED by the plain `toOps` — so the refusal is
the container check's contribution and nothing else's.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** The thirteen lemmas above, over any two trees and any
   predicate: `diff_total`, `diff_refusals_exact`, `diff_ok_on_any_wf_pair`,
   `diff_never_target_not_a_container`, `diff_emission_order`, `diff_script_shape` and the four
   block theorems it discharges, `diff_contained_diff`, `diff_contained_locates`,
   `diff_contained_at_total_is_plain` and `diff_applicable_contained`. F\* 2026.09.06, Z3 4.13.3,
   every query 3/3 under `--quake 3`, `--report_assumes error` on, no `assume`, no `admit`.
2. **Differentially tested.** The extracted `to_ops` / `to_ops_contained` agree with
   `Diff.toOps` / `Diff.toOpsContained` over independently generated pairs and a drawn predicate —
   verdict, error class and the script operation for operation — and over that same pool
   RECONSTRUCTION and APPLICABILITY are checked on production, through `Tree.encodeHash` and
   through `canApplyAll` / `canApplyAllWith`. Agreement is over the pool drawn, never over all
   inputs, and reconstruction is at THIS level rather than level 1 for the reason stated above.
   Two go-reds, each required to lose.
3. **Assumed, and stated as such.**
   - **The witness laws**, **the witness surface is the whole tree**, and **the extractor and the F#
     compiler are trusted** — the same three, in the same words, as theorem 5's.
   - **The two trees agree on the content of every shared id.** Not an assumption the model can
     discharge, because the model's tree carries a kind and the theorem is about structure: it is
     the precondition under which asking for a skeleton script is a well-formed question at all.
4. **Not claimed.**
   - **Reconstruction and operational applicability**, at level 1 — see the boundary above. They are
     the natural successor and they are one positional lemma away.
   - **`Diff.toOpsMoved`** (fuaran-core#63, the move-aware diff) — not shipped, so not modelled and
     not claimed. When it lands it is a second emission strategy over the same two trees, and every
     theorem here is about `toOps`' four passes specifically rather than about diffing in general.
   - **`Ops.normalize`**, which no theorem in this directory reaches.
   - **Containment LEGALITY** — which kind may parent which. As for theorem 5: `canHold` answers
     only "can this node hold children at all", and no theorem here says a contained tree is a legal
     document.
   - **Rejection PAYLOADS.** The differential compares a refusal by class and by the offender a
     `TargetNotAContainer` names; `UnknownNode`'s `addressable` list is outside the comparison.

## Next

**The diff's RECONSTRUCTION, at level 1** — theorem 6's stated boundary, and the item here with the
shortest informal argument and the longest mechanisation. What it needs is one positional lemma: a
parent precedes its children in a preorder walk, so by the time the second pass emits
`MoveNode(c, p)` while processing `p`, every after-ancestor of `p` has already been placed and `p`
cannot be inside `c`'s subtree. Everything else follows from the four block characterisations
theorem 6 already proves. The cost is that the lemma is about `ins` and `rem_at` over the
INTERMEDIATE trees the script builds, which is `Preservation.fst`'s cost class rather than
`TreeDiff.fst`'s — so the module that checks in ten seconds today would not afterwards, and that is
the honest price rather than a reason not to pay it. `Diff.toOpsMoved` (fuaran-core#63) travels
beside it: a second emission strategy over the same two trees, which would want the same lemma.

**Discharging theorem 1's policy assumption** — the smallest of what is left, and now reachable.
`WireDecode.fst` assumes the parser's member-null absorption is equivalent to erasing member nulls
from the tree the strict grammar would produce; both sides of that equation are now modelled, in
`JsonParse.fst` and in `WireDecode.fst`'s section 7, so what remains is a lemma relating the two
rather than a new model. The near-miss tokens that motivated the assumption (`nul`, `nullish`) are
grammar and are now inside a modelled parser rather than outside every model.

**The `\uXXXX` transliteration** — the one grammar path Phase 146's time box left at level 2. It
needs the model to compute a code point from four hex digits, which needs integers in extracted
code, which is finding 2's cost rather than a proof difficulty. Whether it is worth paying is a
question about the extraction's runtime floor, not about the parser.

**The LINEAR chain payload's splices** — the half Phase 145 deliberately did not take. `rec_hash`
hashes `{"seq":<n>,"actor":<a>,"op":<o>}`, a four-way splice through a JSON envelope rather than
`nodeHash`'s two, so `rec_injective` is still the bundled premise Phase 136 wrote. The symbol-level
machinery it would need is already there (`app_sep_split`, `splice_split`, `symbols_faithful`); what
is new is that the envelope's separators are `":` and `,"` digraphs inside a literal skeleton rather
than single characters, so the argument is a parse rather than a split, and the actor field's
self-delimitation has to be composed with the sequence numeral's. It is the item here with a worked
precedent sitting beside it.

_(**The topological order's uniqueness on a spine** was named here and is DONE — Phase 142,
`spine_order_forced` + `kahn_drain_is_such_an_enumeration`, in section 12 of `DagFold.fst`. **And
the successor it named — the general order over a MERGE DAG, where the frontier widens past one and
the tie-break has to be modelled for real — is DONE too**, Phase 156, section 13: the drain over an
abstract acyclic node set at an abstract total order, proved deterministic, a linear extension and
total, with the dangling-parent policy a parameter naming which production caller gets which. What
those two did NOT take, and what still travels with `Dag.mergeBase`, is the delta RECOVERY over a
merge DAG — `between_chain` remains stated for the base-plus-N-chains shape.)_

**A `Footprint` record that can name a relocation's KIND** — the successor Phase 143 priced and
deliberately did not take. Section 18 of `TreeOps.fst` proves that no clause over the four address
sets can widen `Ops.independent`'s promise, because a `MoveNode` and a remove-shaped batch carry
byte-identical footprints while only one of them commutes with a structural write under a parent
inside the relocated subtree. Widening the promise therefore means widening the RECORD — a fifth
address kind carrying the relocation's kind, or a destroyed-subtree set the pure script cannot
compute without the tree — which is a breaking change to every consumer that reads a `Footprint`,
weighed against fold availability nobody has measured. The theorem it would license is the general
move-versus-structural-write diamond, which is true and unproved here for exactly that reason: a
theorem no clause can consume costs prover budget on every run and buys nothing. Two of the nine
refused pairs are known to have commuting members (move × insert, move × reorder); two are known to
have non-commuting ones (remove × insert, move × move); three are unexamined. That is the shape of
what widening would actually recover.

**The signing composition** — the successor Phase 136 names and deliberately does not take.
Chain integrity says a tampered node is found; it says nothing about a REWRITE, which re-mints
every descendant's id and produces a structure no walker can fault. What closes that is a signed
head, and the composition is the coordination plane's own — an attestation over a head, plus this
theorem, is what makes "the history is the one that was signed" a claim rather than a hope. The
seam is here (`Attestation` / `IAttestationSink`); the theorem is not.

Interpreter budget monotonicity — the attested-stack programme's theorem 3, which is not this
directory's numbering — is `fuaran-program`'s and follows the same shape now that the prover is
settled.
