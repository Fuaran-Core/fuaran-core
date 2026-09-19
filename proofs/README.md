# proofs/ — the F\* models of Fuaran.Core's kernel algebras

**Status: GO** (Phase 131, 2026-09-12; the header last brought level with the body 2026-09-14).
Phase 131's three exit criteria were met and remain met — the confluence proof is reproducible on
the pinned prover, the extracted model agrees with production over every lane set the differential
host draws, and the model reads beside the F# in one sitting — and seven further theorems have
shipped beside it since. **Shipped, eight in all: fold confluence (131, with its hypothesis
corrected by 132, the DAG beneath it proved by 134 and its topological order by 142), decoder
totality (135), independence soundness for the tree algebra (133, completed over the WHOLE
operation alphabet by 162), chain integrity (136, its
content-id premise decomposed by 145),
`Json.parse` totality, bounded (146), apply-engine preservation (138, which also lifts 133's
model to the validator 137 fixed), the diff's refusal characterisation and emission order
(141, with its positional facts about `after` added by 162), and the canonical form's injectivity
(149, which also brings the §21 resource limits into the models as named premises).** Each carries
its own claims ladder in its own section below; the "Next" section at the foot is the live list.

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
| `TreeOps.fst` | The third model (Phase 133): the skeleton-op tree algebra — `Ops.apply` with its `Rejection` envelope and `Ops.footprint`, over the tree as the `NodeWitness` shows it — with `tree_independence_diamond` proved, which is the fold theorem's one domain hypothesis. Unlike the two above it does NOT share only `Prims.fs`: it `open`s `DagFold`, which is what makes the composite an instantiation rather than a second model. Phase 162 added the preorder-position lemma (section 19) and the batch lift (section 20), which retired `covered` and made that diamond unconditional. |
| `Skeleton.fst` | The composite (Phase 133): `DagFold`'s fold theorem instantiated at `TreeOps`, so `skeleton_fold_confluence` holds with no domain hypothesis left. Thin on purpose — the argument is in the two halves it joins. Since Phase 162 the op alphabet is the WHOLE of `SkeletonOp`, `Batch` included and nested to any depth; it was the four non-`Batch` ops until then. Since Phase 175 it is also the first INSTANCE of the kit's `kit/templates/Instance.fst.template`, reproduced from it byte for byte by the `Proofs.Kit` family — see the instantiation contract under Theorem 2. |
| `oracle/TreeOps.fs`, `oracle/Skeleton.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `Chain.fst` | The fourth model (Phase 136): the two INTEGRITY WALKERS — `Dag.firstBreak` / `verifyDag` over the content-addressed DAG and `OpStream.firstChainBreak` / `verifyChain` over the linear chain — clause for clause, with both characterised and `intact_verifies` / `tamper_detected` proved for each under a named injective-hash premise. Phase 145 decomposed the DAG's: the two SPLICES in `nodeHash`'s pre-image are proved unambiguous, the op codec's injectivity moves to a conformance law, and what is assumed is the hash itself. Shares nothing with the models above but `oracle/Prims.fs`. |
| `oracle/Chain.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `JsonParse.fst` | The fifth model (Phase 146): the recursive-descent JSON PARSER — `Json.parseDetailedWithPolicy`'s `skipWs` / `expect` / `parseString` / `parseNumber` / `parseValue` / `parseObject` / `parseArray`, the depth counter, both numeric guards and the `EraseMemberNull` fork — with `parse_total`, `depth_bound_exact`, `int53_guard_exact` and `error_kind_exhaustive` proved. This is the boundary theorem 1 named. Shares nothing with the models above but `oracle/Prims.fs`. |
| `oracle/JsonParse.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `Preservation.fst` | The sixth model (Phase 138): the APPLY ENGINE — `Ops.apply`'s totality with its per-clause rejection characterisation, all-or-nothing rejection, id uniqueness preserved by every accepted operation, `Ops.canApply` agreeing with `apply`, and `Ops.invert`'s round trip. It `open`s `TreeOps` (and through it `DagFold`) rather than remodelling the tree: the theorem is about the algebra that model already describes. |
| `oracle/Preservation.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `TreeDiff.fst` | The seventh model (Phase 141): the DIFF — `Diff.toOps`'s two refusals characterised exactly, its four passes clause for clause, what each pass guarantees about the block it emits, and `Diff.toOpsContained`'s pre-emptive container refusal. Named `TreeDiff` and not `Diff` for the reason `TreeOps.fst` is not called `Ops`: the extracted oracle is a top-level F# module and the differential host opens `Fuaran.Core`. It `open`s `TreeOps` (and through it `DagFold`); it is independent of `Preservation.fst`. |
| `oracle/TreeDiff.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `Limits.fst` | The WIRE_FORMAT §21 resource limits as NAMED PREMISES and nothing else (Phase 149): eight constants with their captions, and the two relations the section's own argument uses. It models no enforcement — §21.2's host obligations are about code it does not describe — and it exists so that a changed limit moves one constant rather than a paragraph of prose, and so the ladder can say which theorem depends on which bound. `WireCanon.fst` is the first consumer and takes one of the eight. |
| `WireCanon.fst` | The eighth model (Phase 149): the CANONICAL ENCODER — `Canon.escape`, `Canon.canonicalFloat` and `Canon.render` clause for clause, a READER for exactly the grammar they emit, and the canonical form proved in BOTH directions. Named `WireCanon` and not `Canon` for the reason `TreeOps.fst` is not called `Ops`: the extracted oracle is a top-level F# module and the differential host opens `Fuaran.Core`, which already carries a `Canon`. It `open`s `Limits`; otherwise it shares nothing with the models above but `oracle/Prims.fs`. |
| `oracle/Limits.fs`, `oracle/WireCanon.fs` | **Generated** — the same extractor, the same byte-for-byte diff, the same suite. |
| `Vocabulary.fst`, `DocVocabulary.fst`, `ScoreVocabulary.fst` and their `…Proofs.fst` | **Generated** — the other way round: not extracted FROM a model but emitted AS one, by `Fuaran.Core.Idl.Codegen`'s F\* target (Phase 150) from the three vocabularies the engine is certified on (Phase 173: `tests/Fuaran.Core.Tests/ReferenceIdl.fs`, `SecondDomainSpike.fs`, `ScoreDomainSpike.fs`). Each model carries a vocabulary's types, encoder and tag-dispatch decoder over `WireDecode`; each `…Proofs` carries the round trip `dec_node (enc_node x) == Ok x` over it. Held to a fresh generation by the `Proofs.Vocabulary` family; checked, not extracted. See theorem 1's generated-vocabulary section. |
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
   - _(**The composite's op alphabet excludes a nested `Batch`** was an assumption here from Phase
     133 — three of the fifteen pairs, all the same shape — and is RETIRED by Phase 162, which
     lifted the diamond along a batch's script. The alphabet is the whole of `SkeletonOp` and there
     is no shape hypothesis left. Counted, and its history kept, in "Theorem 2" below.)_
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

## The Core-to-domain proof contract

Every ladder in this directory ends at level 3 — **assumed, and stated as such** — and until
Phase 174 that was the whole of what such a row said. For a domain instantiating this substrate it
is not enough, because the 20 assumed rows across these ladders are three different kinds of thing
and a domain can act on exactly one of them. `../proofs.json` therefore carries a **`class`** on
every `assumed` row, from a closed set of three, and this section is that classification in one
place together with the contract it implies.

- **`domain-obligation` — what YOU owe, and what a green kit run discharges.** The hypotheses
  the theorems carry about the domain they are generic over. Each row names a **`dischargedBy`**
  law: a function of the shipped `Fuaran.Core.Conformance` kit whose green run **at your own
  witness** is the discharge. The discharge is SAMPLED and never a proof — the kit draws a
  seed-replayable sample, and "sampled, never proved" is what level 3 means here. 4 rows.
- **`model-bridge` — what THIS repository's model has not bridged, and what you inherit whether
  you run anything or not.** Gaps between the F\* model and the F# that ships: a numeric carrier
  the extraction cannot represent, a host-side mapping that is one line per case and is not itself
  proved, a walk order the model is handed rather than derives, a specification row the model
  covers vacuously. Nothing a domain does closes one. Each names a **`closes`**: a
  `fuaran-core#NNN` phase where one has been taken, `permanent` where nothing could close it, and
  `unscheduled` where something could and nobody has. 12 rows.
- **`premise` — what nobody discharges, ever.** The trusted base: a hash that does not collide,
  an extractor and a compiler that are correct, a witness surface that reports every node it holds.
  No kit run touches these and no phase closes them; they are what the rest of the ladder stands
  on. 4 rows.

**The contract line, stated once:** a domain running the conformance kit discharges the first class
and **can never discharge the other two**. A green kit run is evidence about your witness and about
nothing else in this table — which is what makes it worth having, and what a reader must not
over-read.

| Row | Class | Discharged by / closes |
|---|---|---|
| `independence-diamond` | `domain-obligation` | `Conformance.footprintLaws` |
| `lanes-apply` | `domain-obligation` | `Conformance.concurrencyLaws` |
| `dag-outside-the-model` | `model-bridge` | `unscheduled` |
| `extractor-and-compiler-trusted` | `premise` | — |
| `sets-are-lists` | `model-bridge` | `permanent` |
| `parser-null-absorption` | `model-bridge` | `unscheduled` |
| `node-ids-distinct` | `premise` | — |
| `tree-algebra-well-formed-states` | `domain-obligation` | `Conformance.opAlgebra` |
| `content-id-determines-content` | `premise` | — |
| `chain-walk-order-is-productions` | `model-bridge` | `permanent` |
| `parser-float-readback-opaque` | `model-bridge` | `permanent` |
| `parser-alphabet-bridge` | `model-bridge` | `permanent` |
| `lawful-abstract-witness` | `domain-obligation` | `Conformance.witnessLaws` |
| `witness-surface-scope` | `premise` | — |
| `canon-numeral-layouts` | `model-bridge` | `permanent` |
| `canon-key-comparator` | `model-bridge` | `permanent` |
| `canon-character-bridge` | `model-bridge` | `permanent` |
| `evolution-table-coverage` | `model-bridge` | `permanent` |
| `column-cell-carrier-opaque` | `model-bridge` | `permanent` |
| `column-transform-evaluator-abstract` | `model-bridge` | `unscheduled` |
| `capability-scalar-readers-abstract` | `model-bridge` | `permanent` |
| `propagation-order-distinct` | `model-bridge` | `unscheduled` |
| `propagation-evaluator-contract` | `premise` | — |

**Why `unscheduled` is a value rather than a rounding to `permanent`.** Three of the bridges can be
closed and nobody has taken the work, and recording them as `permanent` would assert the opposite
of what this document already says. Theorem 4's ladder says of `parser-null-absorption` that
"relating two models is its own phase and was not taken. The assumption stands where it is";
`dag-outside-the-model` has been narrowed once already, by Phase 134, from "all of it". A closed
set of two values would have forced both into a claim of impossibility, which is a worse error than
a third token.

**Why `witness-surface-scope` is a `premise` though a domain is what owes it.** It is the one row
whose obligation is the domain's and whose discharge no run can perform. A node a domain holds in a
keyed, non-structural position is invisible to `Tree.ids`, to every theorem in this directory and
to every law in the kit — so there is no green run to cite for it, and a `dischargedBy` naming
one would be a citation to a run that does not look. The root [`README.md`](../README.md) states
the same boundary from the domain's own side, "this engine cannot see those nodes and will not
pretend to", and calls it deliberate rather than a gap waiting to be closed. The class says what
discharges a row, which here is nothing; it does not say whose obligation it is.

**This table is CHECKED against `../proofs.json`, row for row** — same rows, same order, same
class, same third column — by the `contract-agrees` clause of the `Proofs.Ladder` family in
`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`, with its own go-red fixtures. It is the ONE
piece of prose in this directory that the ladder family parses; everything else here stays a human
act, per that file's own note. A domain reads this table and a tool reads that file, and two of
them disagreeing is worse than either alone.

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
7. **A mutual TYPE group extracts to F\# that does not parse — and `--strict-indentation-` does
   NOT cover it** (Phase 150 found it; Phase 169 measured and closed it). The backend breaks a
   mutual type group at the space before each `and`, so the `and` lands **one space** in:

   ```fsharp
   type node =
   | Leaf of Prims.string
   | Branch of attr
    and attr =              // <- one leading space
   | Flag of Prims.bool
   ```

   F\# 10 answers `error FS0010: Unexpected keyword 'and' in member definition`, and it answers it
   with finding 3's relaxation already in force — which is what makes this a *separate* defect
   rather than more of the same, and what makes it easy to assume is already handled. It is a
   TYPE-group defect only: a value `and` is emitted at column 0, because the backend hoists local
   mutual recursion to the top level. Measured on the pinned **F\* v2026.09.06**, at every name
   length, for DU groups and record groups alike.

   No model here has hit it yet — `Vocabulary`, `DocVocabulary` and `ScoreVocabulary` have mutual
   type groups and are all `$proofOnly`, so nothing extracts one — but `Query`-shaped models with
   mutually recursive expression and pipeline unions are exactly what is coming, and finding this
   inside such a phase's time box costs that phase the afternoon Phase 150 already spent. So the
   leg carries a **post-pass** between the extraction and the byte diff: it re-indents those `and`
   lines to column 0 and changes nothing else, the committed oracles are the normalised text, and
   the pass is the identity on every oracle standing today. `proofs/kit/extraction-post-pass.ps1`
   is the pass, `proofs/kit/extraction-post-pass.tests.ps1` its go-red proof, and the kit README's
   "The extraction post-pass" section has the whole account — including the **retirement
   condition**, which is that a pin bump makes the fixture stop going red, and which that script
   reports by name rather than passing quietly.

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
pwsh ./proofs/kit/extraction-post-pass.tests.ps1   # the extraction post-pass's go-red proof (finding 7)
```

The last one is deliberately **not** part of the leg — `check.ps1` prints exactly what it printed
before the post-pass existed, and a proof about the pass is not a proof about the models. Run it
when the pass changes, when the prover pin moves, or when you want the retirement condition
answered.

Every model in the script's `$modules` list goes through all three steps, and `-Runs N` means N
cold-cache verifications of all of them; adding a model is adding its name to that list and a
budget entry to `modules.json` beside it. The first
run downloads the pinned release (~200 MB, hash-verified) into `proofs/.fstar/`; `FSTAR_HOME`
pointing at a matching release skips that. Editing a `.fst` without re-extracting fails the leg
with "oracle drift" — that is the point, not an inconvenience.

### A lost pass has three classes, and only one of them is a proof failure (Phase 166)

Over 2026-09-14/15 the leg lost passes three different ways and all three read as "did NOT verify".
Phase 162's worker watched `WireDecode.fst` and `JsonParse.fst` — modules it never touched — fail
with every printed query discharged and no error, warning or exception anywhere in the log; both
retried green. Phase 149's saw `fstar.exe` killed mid-`Preservation` at a different lemma each time,
with free memory under 3 GB and six provers on the machine. Phase 155's saw the per-invocation cache
empty mid-run, so extraction failed with F\* error 317 on a dependency whose `.checked` file had
vanished. Each cost a session twenty minutes of reading a whole log to establish that nothing had
been refuted. Phase 164 closed "implausibly fast reads as green"; this closes "abnormally terminated
reads as a proof failure", and a leg that cannot tell the two apart is not an evidence instrument in
either direction.

So a non-zero prover exit is **classified** before it is reported:

```
==== proofs: Refuted.fst did NOT verify (run 1 of 1)
==== proofs: ColumnOps.fst ABORTED (no diagnostic) — exit -1 after 8s on run 1 of 1, attempt 1 of 2. Nothing was refuted: …
==== proofs: retrying ColumnOps.fst once — the retry is BOUNDED (one per module per run) and its timing is a WARM measurement, …
==== proofs: ColumnOps.fst verified ON RETRY — run 1 of 1, 13s (warm cache: NOT a cold measurement), every query 3/3 under --quake
==== proofs: extraction of PMain hit an APPARATUS fault, not a proof failure: the checked-module file it needs is missing — …\PDep.fst.checked
```

- **REFUTATION** is the prover exiting non-zero **having printed a diagnostic** — an error line, the
  end-of-run error summary, `Failed to prove`, `Unexpected`, or a failing-quake line. It keeps the
  words the leg has always used and the prover's own exit code, and it is **never retried**: a
  refutation is a result, and re-running it to see whether it goes away is the habit this leg exists
  to make impossible. It obliges reading the model.
- **ABORT** is the prover exiting non-zero having printed **nothing** about an undischarged query.
  Nothing was refuted; the prover died. It is **retried once** in the same run — bounded at one
  retry per module per run, logged as a retry — and a **second** abort of the same module fails the
  leg with **exit 3**, which is not a refutation's code. It obliges reading the machine: every run
  now opens with a **pre-flight line** carrying the concurrent `fstar` process count and the free
  physical memory, and the abort prints the same snapshot again at the moment it happened, so a
  post-mortem can tell a contended machine from a broken model without asking anyone who was there.
- **APPARATUS** is the leg's own machinery failing — at extract, a dependency's `.checked` file
  missing from the cache, or the cache directory gone. It is reported **naming the file**, with
  **exit 4**, and it is not retried, because the thing it needs is gone. It obliges finding the
  second writer.

A retry's clock measures a cache the aborted attempt had already half-filled, so it is **not a cold
run**: its line says so and it is compared to neither the budget nor the floor. The leg's closing
verdict names every abort that was retried and passed, because a green run that lost a prover and
got it back is not the same evidence as one that did not, and finding that out should not require
scrolling. Every prover invocation's whole output is teed to `proofs/obj/logs/`, and each verdict
names the transcript it was read from.

**Two things about this were measured rather than assumed, and both inverted the phase's own
premise.** The first: on the pinned prover a diagnostic reads `* Error 19 at Foo.fst(8,39-8,41):`,
**not** `(Error 19)` — so a discriminator that knew only the parenthesised spelling classified a
plain error as an ABORT and *retried* it, which is the exact inversion this verdict exists to
prevent. It was caught by a probe that staged a missing model file, and not by the
deliberately-false-lemma probe, which had passed a moment earlier through `Failed to prove` alone
and was therefore agreeing for the wrong reason. Both spellings are matched now, with the error
summary as a third witness. The second: **a failed extraction is not always a non-zero exit.**
Removing a dependency's `.checked` underneath an extraction makes F\* print `* Error 317` and
`1 error was reported` and then **exit 0**, so the fault fell past an exit-code test and surfaced as
"extraction produced no `<module>.fs`" — a true sentence naming the symptom and not one thing about
the cause. Failure is therefore decided as *non-zero exit **or** no file produced*, and only then
classified.

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
in the file's own `seeding` block — `max(2 × the slowest observed cold run, minimumBudgetSeconds)`,
rounded up to the next 10s, that minimum being 20s and being for the sub-5s modules where 2× is
inside process-start noise. (It was spelled `floorSeconds` until Phase 155, which is the same word
the per-module TIME floor above uses for the opposite thing — the smallest a budget may be against
the smallest a run may be — and the collision was renamed rather than documented because the kit
below ships this file's skeleton.) Editing the
number alone is how a budget stops meaning anything: the point of the entry is that a later reader
can tell a cost someone decided to pay from a drift nobody noticed.

**Why `modules.json` and not `../proofs.json`.** The ladder is a claim about CORRECTNESS, at a closed
set of four levels, checked row by row against the tree by `Proofs.Ladder`; a wall-clock budget is a
claim at none of those levels, and it is per MODULE where a ladder row is per CLAIM. One home, and
this is it — the ladder's `proof-leg` policy row points here for the cost half rather than carrying
numbers the family beside it would not be checking.

### A contended pass is named, so a reader can tell the afternoon from the module (Phase 171)

The paragraph above says a single overshoot on a busy machine is noise and a persistent one is a
regression — and until Phase 171 the log gave a reader nothing with which to tell the two apart.
Three instances, all of them the machine: `Chain` overshot its 30s budget twice in seven runs (35s
and 31s) and came in at 16–25s on the other five, untouched by any phase since it was budgeted;
Phase 162 measured `TreeOps` at 145s in a pass that inflated three untouched modules by the same
factor, and had to depart from `seeding`'s rule by hand and write a paragraph explaining why; and
Phase 182 measured `Capability` at 75s against its 20s budget on a run contended by three sibling
gates. Every one of those ran beside five other provers, and every one reads in the log exactly like
a regression.

So the leg measures the afternoon directly. At the end of **each run** it reports the **median**,
over the modules this working tree did **not** change, of what each module just cost divided by the
`measuredSeconds` its entry records:

```
==== proofs: contention — run 1 of 1, x0.29 over 16 untouched module(s), at or under the x0.80 threshold: an ordinary pass. A cost finding on this run is about its module.
```

and when the machine was busy, the same line says so and the finding it qualifies carries the label
where the closing verdict prints it — this pair is from the phase's own probe, which lowered the
threshold to 0.20 and one budget to 8s so that an ordinary pass crosses it, the labelling path being
the same one a genuinely contended pass takes:

```
==== proofs: contention — run 1 of 1, x0.29 over 4 untouched module(s), ABOVE the x0.20 threshold: this was a CONTENDED pass. 1 cost finding(s) on this run carry the label, and a labelled finding is NOT a re-seed obligation.
     ColumnOps.fst took 12s against its 8s budget on run 1 of 1 — 4s over, 150% of budget — CONTENDED PASS (x0.29 against a x0.20 threshold): the modules this tree did not change ran x0.29 of their recorded measurements on this run, so this figure measures the afternoon and not the module
```

An untouched module's cost is a fact about the machine and not about the tree, so a pass in which all
of them came in at 1.7× their recorded measurements is a pass in which the machine was 1.7× slower,
whatever any one line says. The median rather than the mean, because a module hitting a pathological
query — or aborting and retrying — is exactly the outlier a mean would launder into the number.

Above the threshold, every cost finding from that run is **labelled** where the closing verdict
prints it, and the label carries the whole consequence: **a labelled finding is not a re-seed
obligation.** Re-seeding a budget from a contended run raises a ceiling to fit a slow afternoon,
which is how a budget stops meaning anything — the judgement Phase 162 had to make by hand and argue
in prose. What is new is that the leg makes it, and says so. `check.ps1 -Strict` promotes only the
**unlabelled** findings: a session that asked for a red leg on cost asked to be stopped by a
regression, and a contended pass is not one, so reddening on it would make the flag a coin toss on a
shared machine. Coverage and shape findings belong to no run, are never labelled, and always promote.

**The number's scale is not the obvious one, and this was measured rather than assumed.**
`measuredSeconds` is not a typical cost — by `seeding`'s own rule it is the **slowest** cold run ever
observed for that module, and most of the entries below were seeded under six concurrent sessions. So
the ratio's neutral point sits well *below* one: a quiet cold pass of this leg measures **x0.29**, not
x1, and the 2026-09-14 contended runs sit near x1.0 because `measuredSeconds` *is* one of them. A
threshold picked as though 1.0 meant "normal" would sit above any contention this leg can experience
and would never fire — the *detector that cannot fire* that `TreeOps`'s own entry warns about. The
threshold therefore lives in `modules.json`'s `contentionSeeding` block, seeded from a measured quiet
pass and a measured contended one with both figures recorded there, and re-seeding it is the same
deliberate recorded act as bumping a budget.

Four more things about it are worth knowing before reading a factor:

- **Nothing is multiplied into a measurement.** The seconds a green line prints stay the wall clock
  the module took, and the factor sits beside them. A normalised measurement would be a number nobody
  observed, and the value of this leg's cost half is that every figure in it is one somebody's machine
  really produced. The same reason the optional per-entry `contentionFactor` — recorded beside a
  `measuredSeconds` by whichever phase seeds it, saying what the machine was doing at the time — is
  **provenance only**: the leg holds it to its shape and computes nothing from it, and an absent one
  reads as "not recorded", never as 1.
- **The untouched set is derived, not declared** — `git status --porcelain` over `proofs/`, so
  modified, staged and brand-new all count and no branch name is assumed. Its limit: a session that
  has already **committed** its model edits has a clean tree, so its module reads as untouched and
  votes. The median absorbs one or two such ratios out of a dozen, and that is the honest boundary of
  what a working-tree question can answer. Where git cannot answer at all, every module counts as
  untouched and the leg says so on its own line — "I could not tell" must never print as "nothing is
  touched".
- **A module too cheap to time does not vote.** The clock is whole seconds, so `Skeleton` at 0s
  against a recorded 2s is a ratio of 0 and 1s is a ratio of 0.5, and neither says anything about the
  machine. The cut is `floorSeeding.zeroBelowSeconds`, reused rather than minted again so there is one
  number and one argument for it; `Skeleton`, `Limits` and `WireVersioning` are the three it excludes
  here. A run with fewer than `contentionSeeding.minimumSamples` contributors reports the factor as
  **not computed** rather than taking a median of one.
- **The label arrives after the finding, and that ordering is deliberate.** A ceiling finding prints
  unlabelled at the moment it fires, because at that instant the leg genuinely does not yet know what
  kind of afternoon it is having — the factor is read off the run's own measurements and does not
  exist until they are all in. The run's contention line follows a few lines later, and the closing
  verdict prints every finding with its label. Printing a label the leg could not have computed would
  be a worse lie than printing the measurement alone.

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

## Adopting the leg elsewhere — `proofs/kit/` (Phase 155)

Everything above this line is about *this* repository's models. This section is about the leg
itself, which is not specific to them: a pinned prover, a script that locates or downloads it and
checks each model from a cold cache, an extraction diffed against a committed oracle, a host step,
a budget and a floor, two hand-written runtime shims, a never-packed oracle project, a CI job, a
claims ladder and a cost declaration. Any repository that wants an F\* proof leg wants all of it,
and writing it out by hand each time means **N copies of one design and N prover pins, N−1 of which
are behind the day the Nth moves** — F\* releases weekly, so that is a when and not an if.

`proofs/kit/` is that design, extracted and shipped once. **This repository consumes it in place:**
`proofs/check.ps1` is now a thin caller that declares what this repository has — the `$modules`
list, the two host families, where the test project is — and hands them to
`kit/check-proof-leg.ps1`, which runs the leg. Nothing about the mechanism is in the caller and
nothing about this repository is in the kit. The public surface did not move: the same flags, the
same output lines, the same exit codes, and `$modules` is still one literal line in
`proofs/check.ps1`, which is where a reader looks for it and where the `Proofs.Ladder` family reads
it from.

Two small things that were measured rather than assumed while doing it, because both would have
been silent:

- **The caller invokes the engine with `&`, never with a dot-source.** A dot-sourced script's
  `exit` does not propagate to its caller — measured both ways — so a dot-source would have printed
  the engine's red `==== proofs:` line and then returned **0**. That is a green leg over a failed
  proof: the Phase 164 class, re-created one level up, and the reason the caller carries a comment
  saying so.
- **The engine cannot use `$PSScriptRoot` for the proofs directory**, because inside a called
  script that resolves to the *kit's* directory rather than the caller's. It takes `-ProofsDir` and
  derives everything else from it; a repository that wants a different layout passes `-RepoRoot`,
  `-PinFile`, `-BudgetFile`, `-OracleDir` or `-WorkDir` instead of forking the script.

**How another repository adopts it:** `proofs/kit/README.md` has the file-by-file table and the
nine steps. In one sentence — copy `proofs/kit/` plus the three live files it deliberately does not
duplicate (`proofs/fstar-pin.json` and the two shims under `proofs/oracle/`), fill in the three
declarations at the top of `templates/check.ps1`, and declare every copy in your own `copies.json`.

**The pin moves in one place, and the copies are named.** `copies.json` at this repository's root is
where a copy of any kit file is declared: `source` relative to the declaring repository's own root,
each `consumers` entry relative to the workspace root, `check` of `bytes` or `fingerprint`, and a
`regen` command. The workspace copy registry then names a drifted copy on its sweep — warn-first,
offline, and it never edits anything. That is the whole mechanism by which one pin bump here becomes
a named obligation everywhere else instead of a silent divergence.

**What the file actually says today, which is not what the kit's phase expected.** `copies.json`
ships with an **empty `records` array**, because the copies it was written to check do not exist.
Measured across the workspace on 2026-09-15: this is the only proof leg there is. No other
repository holds a `check.ps1`, an `fstar-pin.json`, a `proofs/` directory or a `.fst` model — the
three consumers the kit was cut for (the program tier, the app-composition tier, the remoting
decoder) had been expected to have copied the leg by hand already, and had not. The kit is still
worth shipping, because a scaffold shipped once is worth the same before the first adopter as after;
what is not worth shipping is a record for a file that does not exist, which would put a permanent
finding on every workspace sweep about an adoption nobody has scheduled. The copy set is therefore
written out in the file's `$sources` block — inert data the registry ignores — so that an adopter's
record is that shape with one path filled in, and adoption is an append in the change-set that
adopts rather than a design decision taken again.

**Adopting is the adopter's act, not this repository's.** Re-pointing a hand-rolled leg at the kit
means reading a diff, running the leg, and committing the copies; nobody can do that from here, and
nothing here will do it for them. Until an adopter appends its record, the registry is silent about
that repository — which is honest, and is the reason the empty array is a measurement rather than a
gap.

**Importing a theorem, not only a leg (Phase 175).** `$sources` also names the models: the generic
theorems (`DagFold`, `Chain`, `WireCanon`, `WireDecode`, `JsonParse`, `Limits`), the reference
instance (`TreeOps`, `Skeleton`, `Preservation`, `TreeDiff`) and `kit/templates/Instance.fst.template`
— each entry saying which theorem the file carries and which obligations it asks a domain for, and
the sources being the live files under this directory, so there is no second copy of any model here.
A domain instantiates fold confluence at its own witness by copying `DagFold.fst`, filling the
template's fourteen holes — one of which, `{{DIAMOND}}`, is the obligation it proves — and adding the
instance to its own `$modules`; the contract is stated under Theorem 2 and the procedure in
`kit/README.md`. Not in the set: the six generated modules (`Vocabulary`, `DocVocabulary`,
`ScoreVocabulary` and their `…Proofs`), which a domain does not copy but REGENERATES from its own
`Idl` with the same backend, and `WireVersioning.fst`, Theorem 8 about this repository's IDL diff.

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

Five things are proved:

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
- **`decode_perm_invariant` / `decode_node_perm_invariant`.** Key-order invariance: WIRE_FORMAT §2
  rule 2's obligation on the decoder, and §20's one-answer rule at this layer. Added by Phase 152
  — see the section below, which is also where the duplicate-key premise is argued and proved
  necessary.

### The vocabulary is GENERATED now, and the round trip is a theorem about the certification set (Phase 150; re-sourced by Phase 173)

Everything above is about a **reference** vocabulary: four cases, written by hand beside the
combinators, chosen to exercise one combinator each. That was the honest scope of Phase 135 and it
said so — but it leaves theorem 1 saying nothing about the vocabulary an IDL actually declares, or
about the backend that turns one into a decoder.

The generated modules close that gap, and they are **generated** — by a fourth backend of the IDL's
own code generator (`FStarTarget`, beside the F# structural layer, the TypeScript encoder and the
JSON schema) from the vocabularies this repository certifies the engine on. There are three pairs,
one per certification vocabulary, and each pair is emitted from ONE walk:

- **`Vocabulary.fst` / `VocabularyProofs.fst`** — the engine's own REFERENCE vocabulary
  (`tests/Fuaran.Core.Tests/ReferenceIdl.fs`; Phase 114, completing D14 for the F# backend): the
  part of the type model neither vendored sample uses — a hosted slot, an opaque sentinel, a
  closure on the wire, a `TMap`, a `TJson`, a generic union at two arguments, a wire-mapped enum, a
  host-only member — on the DEFAULT wire shape.
- **`DocVocabulary.fst` / `DocVocabularyProofs.fst`** — the vendored second-domain sample
  (`SecondDomainSpike.fs`), on the DECLARED non-default wire shape: bare-string discriminator,
  flat node envelope, declaration key order.
- **`ScoreVocabulary.fst` / `ScoreVocabularyProofs.fst`** — the vendored third-domain sample
  (`ScoreDomainSpike.fs`): records and omit-at-default at scale, on the same non-default shape.

The model half carries the vocabulary's types, its discriminated encoder and its tag-dispatch
decoder over the `jval` value model and the combinators above; generic unions are monomorphised
at the arguments the vocabulary reaches, which is what keeps every definition and every lemma
first-order. The proofs half is `rt_node` and the mutual family beside it — **`dec_node (enc_node
x) == Ok x`, for every value of every modelled type**, at every depth, through every list, map,
optional member and omit-default — plus `dec_node_total` for the outcome's exclusivity. Since
Phase 182 that family is emitted **one lemma per constructor over a presence split LINEAR in the
conditional members**, so no query carries more than one key's walk; the shape has its own
subsection below, with what Phase 168 did before it and why it was replaced.

**What is proved is the F\* BACKEND, and that sentence is the whole of Phase 173.** Phase 150
generated the one model from the shared corpus's `idl.json` — the UI vocabulary — which put a
domain's proof in the substrate's CI, the placement D14 had already ruled out for `tests/`
(Phase 114 moved the UI vocabulary out of the engine's certification; Phase 123 deleted its byte
pin). It was a default rather than a decision: the leg existed nowhere else. The generation source
is now the certification set — the same set `IdlCertificationTests` certifies the F# and
TypeScript backends over — so the theorem the leg carries on every push is about the generator,
and it is exercised over both wire shapes, every type case the reference vocabulary was authored
to reach, and the one boundary the backend has (below). A domain proves its OWN vocabulary in its
own repository with the same generator, and the cost is charged to the commits that change its
kinds: the UI vocabulary's model, its proofs, its cost and its exhaustive-coverage decision are
`fuaran#1754`'s, the proof kit's first adopter. Nothing under `proofs/` names a UI kind, and the
corpus read has left the leg — `--emit-fstar` takes its vocabularies from the test project, so the
generation diff is not gated on a sibling clone.

**Why the proof script is generated and not written.** A hand-written proof over a real vocabulary
would be a theorem about the vocabulary as it stood on the day it was written — the same defect as
the reference vocabulary, one release later and harder to see. Generated, a kind added to the IDL
enters the model at the next regeneration and **re-proves itself**, and a kind whose encoder and
decoder disagree fails the leg.

**Two diffs hold it, and they are one level apart.** Step 2 of `check.ps1` holds each committed
oracle to a fresh EXTRACTION, so the artefact the suite runs is the model. The `Proofs.Vocabulary`
family holds each committed model AND its proof script to a fresh GENERATION from the vocabulary
in the test project, so the model is the vocabulary the engine is certified on — a vocabulary that
moves without a regeneration is **vocabulary drift** and the leg names it, with
`dotnet run --project tests/Fuaran.Core.Tests -- --emit-fstar` as the remedy. The models are
CHECKED but not EXTRACTED (`$proofOnly` in `check.ps1`): nothing here runs them beside production,
because the decoder they model is one a generator emits into a consuming host and not one this
repository ships. A proof script extracts to nothing at all.

**What the models cover, and the one reason a kind is outside them.** The emitted headers carry
the live lists; the shape is:

- **A construct with no wire-level meaning in the model is a BOUNDARY**, and a typed refusal
  (`CodegenError.UnmodellableInFStar`) rather than a dropped member — a silently-dropped member
  would make the round trip a theorem about a document nobody sends. The one boundary a
  well-formed vocabulary reaches is the NUMERIC DEFAULT: the model's numeric carriers are opaque
  type parameters, so there is no F\* literal for `Measure.value`'s `Fixed { value = 0.0 }` in the
  reference vocabulary or for `Note.voice` / `Chord.voice`'s `1` in the score sample, and those
  three kinds are named in their headers rather than modelled. `IdlFStarTargetTests` pins the set
  as exactly those three, so a fourth reads as the coverage change it is — and it pins that every
  OTHER refusal class of the backend (an undeclared record, a union arity mismatch, an unresolved
  type parameter, a host codec with no host type, a transparent case that is not one scalar, a
  default omitting a required member, a bare-kind or tree-op slot) is reached by a hand-written
  case, because those are refusals of an ILL-FORMED IDL that no certification vocabulary carries
  by construction. Phase 173's shard assumed the reference vocabulary reaches every refusal class
  "by construction"; checked against the backend, that premise was false — reaching every type
  case is not reaching every refusal class — and the test above is the corrected form.
- **`FStarTarget.proofKinds` is NOT what selects here, and the difference is asserted.** That
  rule — every expressible kind introducing no declared type beyond the node envelope's closure —
  is a cost control sized against a vocabulary whose envelope already carries dozens of declared
  types. Over the reference vocabulary, whose envelope is two scalars, it keeps ONE kind of five
  and drops the four that carry the type-model remainder the vocabulary exists to reach; measured
  before it was replaced, and pinned (`proofKinds refIdl = ["Group"]`). The committed models
  cover EVERY expressible kind, because the whole set costs seconds and there is nothing for a
  cost control to control. The rule is unchanged: it is the adopter's instrument at the scale it
  was measured at.

Three things are deliberately NOT claimed here. The `wf` characterisation — `Ok? (dec el) == wf
el`, which the reference vocabulary carries above — is not restated over the generated
vocabularies. Phase 168 was cut to restate it over a generated acceptance predicate and HALTED,
with the reason in its subsection below: the round trip covers everything the encoder can
produce, and the characterisation of what ELSE is accepted is open. A **host-only**
member is absent from the model entirely, because it is never on the wire. And a **wire-visible
closure** is modelled as `unit` encoding to the fixed `"<closure>"` sentinel — not a weakening but
the precise statement that the member carries no information.

**The cost, measured (Phase 173) — and why the theorems are committed now when Phase 150 could
not commit them.** On the pinned prover, cold, with the leg's own flags (`--z3rlimit 40 --quake
3`; `--ext context_pruning` emitted inside every generated module, `--z3rlimit 200` inside the
proof scripts — the remedy Phase 150 measured at UI scale, which a small envelope never needs and
pays nothing for). `WireDecode` measured 9s here against the 12s `modules.json` records as its
fastest, which is what says the measurement is of this leg rather than of something adjacent to
it. Seeded from `check.ps1 -Runs 3`; the six entries in `modules.json` carry the numbers.

| module | what it is | cold, quaked (fastest–slowest of thirteen runs) | budget / floor |
|---|---|---|---|
| `Vocabulary` | reference model, 4 of 5 kinds | 3–10s | 20s / 0 |
| `VocabularyProofs` | its round trip | 5–17s | 40s / 2s |
| `DocVocabulary` | second-domain model, 11 of 11 kinds | 3–11s | 30s / 0 |
| `DocVocabularyProofs` | its round trip | 5–9s | 20s / 2s |
| `ScoreVocabulary` | third-domain model, 19 of 21 kinds | 8–16s | 40s / 4s |
| `ScoreVocabularyProofs` | its round trip | 12–42s | 90s / 6s |

The same legs re-seeded the FLOORS of eight hand-written modules downward (`modules.json`, each
entry's note): this machine, idle, verified every one of them faster than the six-session machine
their floors were seeded on, and two floors fired on the way — Chain at 4s against a 5s floor,
WireCanon at 13s against 20s — which is the gate doing exactly what Phase 164 built it to do, and
the recorded remedy (re-seed from the fastest genuine cold run, cite the phase) is what was done.

Against the 322–398s the UI model alone cost, the whole set is an order of magnitude cheaper and
the round trip discharges where at UI scale it did not — which is not a different prover or a
different emitter but the finding Phase 150 itself recorded: **the cost is the node ENVELOPE's
closure, not the kinds.** The UI vocabulary's envelope carried five optional members and
fourteen declared types before any kind was reached; the reference vocabulary's carries two
scalars. The 2^k object-shape blow-up that stopped `rt_node` at twenty UI kinds has k = 2 here.
The CI proofs-job wall clock before and after is recorded in Phase 173's outcome. Phase 168
re-measured the three proof scripts under the per-constructor shape — its table is in the
subsection below, and `modules.json` carries the current budgets.

#### The shape — one lemma per constructor, and a presence split LINEAR in the conditional members (Phase 182)

**The problem, restated in one sentence.** A constructor with k conditional members — optional,
or omitted at its default — encodes to 2^k object shapes, and a round-trip lemma over the whole
constructor puts all of them in ONE prover query. That is both failures Phase 150 measured at
twenty UI kinds (below): a 65-goal `rt_node` query over a five-optional envelope failing a
`--quake` seed, and `rt_vkind`'s widest arm — eleven members, five conditional — failing
outright. Raising the rlimit turned failing into grinding, `--split_queries` is not settable as a
pragma on the pinned prover, and a patterned lookup helper made everything slower. The fix is in
the generator, and it is the SHAPE of the emitted proof, not a flag.

**And the second problem, which is Phase 168's own.** Phase 168 isolated a shape by PINNING every
conditional member at once — one lemma per presence PATTERN, which is 2^k lemmas. That trades a
query too wide to discharge for a script too big to hold, and the trade only shows at scale:
`fuaran#1754`, the kit's first adopter, measured 71,722 lemmas in a 114 MB, 713,272-line script at
the UI vocabulary, one kind with sixteen conditional members contributing 65,536 of them. Phase 182
splits by MEMBER instead of by pattern, which is linear in both.

**The shape.** `FStarTarget.proofsModuleFrom` now emits, for every modelled type `T`:

- **`rt_<T>`** — the round trip over the type. For a type with several constructors (the kind
  union `vkind`, every value union) it is a CASE SPLIT whose arms cite the constructor lemmas and
  prove nothing themselves. `rt_node` stays the family's first `let rec`, which is the top-level
  declaration the claims ladder resolves.
- **`rt_<T>__<Ctor>`** — one constructor's arm alone, under `requires C__<T>__<Ctor>? x`. For a
  kind this is the per-kind lemma the phase was cut for: `rt_vkind__Embed`, `rt_vkind__Group`, one
  per modelled kind and none for a refused one.
- **`lk_<T>__<Ctor>__<member>`** — one member's LOOKUP off the encoded object, emitted for a
  constructor that carries `FStarTarget.presenceSplitAt` (two) or more conditional members. A
  member that is always emitted gets one (`get_prop "<key>" (enc_<T> x) == Ok <its encoding>`); a
  conditional member gets two, `…__present` and `…__absent`, whose `requires` pins THAT MEMBER only
  — an optional one by `None?` / `Some?`, an omit-at-default one by equality with the literal the
  encoder itself tests — and leaves every other conditional member FREE. Nothing before the first
  conditional member in key order gets one at all: `find_field` reaches it without meeting a branch.
  The constructor's lemma then cites them a member at a time, the conditional ones under a two-way
  match on that member alone; the node's envelope is the node's one constructor and is treated the
  same way.

That is `2k + r'` lemmas for a constructor with k conditional members and `r'` always-emitted
members sorting after the first of them, where Phase 168 emitted 2^k. (The phase's own figure was
`2k + 1`: it counted the conditional members and the constructor's round-trip lemma, and passed over
the always-emitted members whose key the conditionals before them move. Both numbers are pinned in
`IdlFStarTargetTests`.) The load-bearing fact underneath it is that `find_field name` pushes through
an entry with a different key, so `find_field n (if c then t else (k, v) :: t)` is `find_field n t`
on BOTH sides of the test and the two branches merge instead of multiplying.

**The lookup lemmas are NOT in the mutual family, and that is load-bearing rather than tidy.** They
recurse on nothing, so they need not be — and F\* admits one option set per top-level declaration,
of which a mutual family is one. Each lookup needs FUEL: the default two unfoldings do not reach
past the second key, so a lemma about the last member of a wide constructor cannot even start.
Inside the family that fuel would be paid by every other query in the file; outside it, each lemma
is pushed under its own `--fuel`, sized to the constructor, and the family keeps the leg's defaults.

The family is still one mutual induction, because a kind's children reach `rt_node` through
`rt_items_l_node`; the termination measure is lexicographic — `%[x; tier]`, tier 2 for the type, 1
for the constructor — because the constructor lemmas recurse on the SAME value and differ only in
how much of it they have already fixed. The threshold is two, not five, so that the certification
set itself exercises the split and proves it discharges: the reference vocabulary reaches it at its
envelope and at `Embed`, the score sample at `Measure` and `Score` (omit-at-default members, so
those lookups pin equality with a default rather than `Some?`), and the second-domain sample carries
no constructor wide enough, which is the per-constructor shape alone. The rule is uniform over
kinds, records and union cases — one helper, one measure — rather than special to kinds, because a
five-optional RECORD in an adopter's envelope closure meets the same wall, and the emitter should
not have to be taught it twice.

**What it costs, and what it buys.** At the certification set the change is small, because k is 2
there and 2^2 is not a blow-up: `VocabularyProofs` holds 9 lookups + 18 round-trip lemmas where
Phase 168 emitted 26 round-trip lemmas, `DocVocabularyProofs` is unchanged at 27 (no constructor of
the second-domain sample reaches the split), and `ScoreVocabularyProofs` holds 13 lookups + 42
round-trip lemmas where Phase 168 emitted 62. Where it is not small is at width, which is the whole
point: on a synthetic vocabulary whose widest kind carries sixteen conditional members, the emitted
script goes from **78,192,374 characters and 655,507 lines to 25,847 and 333**. The
`--z3rlimit 200` Phase 150 wrote into the file as the remedy it had measured stays RETIRED: every
generated proof script is checked at the leg's own rlimit of 40, and a query that wants more is a
query the split has failed to isolate.

Measured on the pinned prover, cold, `--z3rlimit 40 --quake 3`, this dev machine, one
`check.ps1 -Runs 3` leg (three cold runs of every module; the leg was green, with two cost findings
on `Capability`, which this phase does not touch):

| module | lemmas | Phase 168 (fastest–slowest) | Phase 182 (three cold runs) | budget (168 → 182) |
|---|---|---|---|---|
| `VocabularyProofs` | 9 lk + 18 rt (was 26 rt) | 11–18s | 12s, 12s, 12s | 40s → **30s** |
| `DocVocabularyProofs` | 27 rt (unchanged) | 8–10s | 17s, 16s, 16s | 20s → 20s |
| `ScoreVocabularyProofs` | 13 lk + 42 rt (was 62 rt) | 46–108s | 31s, 32s, 31s | 220s → **70s** |

**`DocVocabularyProofs` is the control, and it is why the two re-seedings are trustworthy.** Its
content changed in COMMENTS ONLY, and it still ran ~1.7x its recorded time on this leg — so the leg
was contended, and the other two modules' readings are upper bounds rather than best cases. The
same leg ran `Capability` (untouched) at 75s against a 20s budget and `JsonParse` (untouched) at 76s
against a 42s first run, while `ScoreVocabularyProofs` did not move across its three readings.
Its budget is therefore deliberately NOT re-seeded, on the precedent `TreeOps` records: a run that
inflates an untouched module measures the afternoon, not the module.

The floors are deliberately NOT re-seeded either. Every reading above is SLOWER than the
`fastestSeconds` each entry already records, so `floorSeeding`'s own rule forces nothing, and a
floor raised from a loaded afternoon is the one direction that costs a false red on a faster runner
— which is what Phase 164's asymmetry exists to fear.

**The pins.** `IdlFStarTargetTests` holds the emitted shape, not a prover result: at the reference
vocabulary — one `rt_vkind__<Kind>` per modelled kind, `rt_vkind`'s arms citing them and proving
nothing, exactly the `2k + r'` lookup lemmas the vocabulary's conditional constructors warrant
(nine, stated as a literal so a move in the vocabulary reads as the coverage change it is, and
recomputed beside it the way the emitter computes it), every conditional lookup's `requires`
pinning exactly ONE member, a scoped `--fuel` on every lookup, the lexicographic measure on every
lemma of the family, and no in-file rlimit — and at a UI-SCALE fixture: a five-optional envelope
and an eleven-member kind with five conditional members, the two parameters Phase 150 measured the
one-lemma shape failing at, plus a SIXTEEN-conditional kind at `fuaran#1754`'s `DataGrid` width,
which emits 33 lookups where Phase 168's shape emitted 65,536. `__p<bits>` is asserted ABSENT at
both, so the exponential count is pinned gone rather than merely not looked for. The fixture
is authored under neutral names rather than read from the UI vocabulary's `idl.json`: this
repository has no reader for that artefact (`Artifact` renders one and parses none), Phase 123
removed the UI fixture from the test project when Phase 114 cut the reference vocabulary, and the
property the adopter inherits is the scale, not the names. No UI check runs in this leg; the UI
vocabulary's proof, cost and coverage decision remain `fuaran#1754`'s.

**The `wf` characterisation — HALTED, with the reason.** Phase 168's fourth task was to restate
`Ok? (dec_node el) == wf el` over a generated acceptance predicate mirroring the decoder's accept
set. The only predicate this emitter can generate from the same walk IS the decoder's accept set,
restated clause for clause — `get_prop` by `get_prop`, `as_string` by `as_string` — and a lemma
that the decoder succeeds exactly when that predicate holds is a theorem about two renderings of
one definition: true, provable, and empty, because nothing in it could be wrong without the other
half being wrong the same way. Phase 135's `decode_node_wf` earns its keep because its `wf` is
written by hand, independently of the decoder, so agreement between them is evidence. A
characterisation worth the name over a generated model therefore needs an INDEPENDENT statement of
the accept set — a schema-shaped predicate, which the IDL's JSON-schema backend already emits for
hosts and which would have to be modelled in F\* as a third generated artefact of the model's own
size — and that is a phase, not a task inside this one. Named here rather than left to be
assumed; the emitted header says the same.

**What the linear shape does NOT fix, measured — and why the successor is no longer the
mutual-family split (Phase 182).** Two exponentials were in play at `fuaran#1754`'s scale, and only
one of them was the proof shape's. The other is the MODEL emitter's, and it is now the binding one:

- **`encMembers` writes the tail of the member list into BOTH arms of every conditional member's
  match**, so a constructor with k of them emits an expression 2^k long — 5,318,686 characters for
  one kind at k=16 on Phase 182's probe, which is `fuaran#1754`'s 5,072,945-character model line
  reproduced. At that width the prover dies LOADING THE MODEL, with no proof script involved at
  all: `Fatal error: allocation failure during minor GC`, after 719 s. At k=12 the same model
  checks in 281 s.
- **A NEGATIVE lookup still walks the conditional tail.** Showing a key is present ends at the key;
  showing it ABSENT means reaching the end of the list down every combination of the conditional
  members after it, which is 2^(k-1) object shapes in one query. That is what fails at k=12 — the
  first conditional member's `…__absent` lemma, at fuel 30 and again at 60 — while every lookup at
  k=8 discharges.

So the exhaustive-coverage ambition stays REFUTED at sixteen conditional members, and the reason is
now named rather than symptomatic. The successor is a non-duplicating member-list emission in the
MODEL — `let`-bound suffixes, so the emitted text is linear in k AND a suffix is a term a lemma can
be stated about, which is what would make the negative lookup a chain of k cheap steps rather than
one exponential query. Note what that is NOT: Phase 150 tried routing conditional members through
an `opt_cons` helper with an SMT-patterned lookup law and measured it WORSE (below), because the
pattern then fires throughout the encoder. A `let` binding carries no SMT pattern and is not that
lever; the measurement does not refute it, and it is recorded here so the distinction is not lost.

**The mutual-family split, which this phase also does not take.** What the shape above fixes is the
WIDTH of a query; what it leaves alone is the size of the mutual family every query is checked
inside, which is where the cost turns superlinear as the proof vocabulary widens (Phase 150: the
whole expressible UI vocabulary did not finish a check in twenty-five minutes, and
`--ext context_pruning` is what keeps the family out of each query's context rather than out of
the run). Widening a proof vocabulary past the envelope's closure needs that family SPLIT into
groups checked separately, and the node recursion forbids it as the model is shaped: every kind
with children reaches `node`, `node` reaches every kind, so the whole vocabulary is one strongly
connected component. Breaking it means an abstract node parameter, or a two-level model where
kinds are proved against an interface the node satisfies — generator work of its own, sized by
the adopter's vocabulary, and not this phase's. It was named as Phase 168's successor; at the
widths measured above it is no longer what binds first, and the model emission is.

#### History — the UI vocabulary's measurements (Phase 150), now `fuaran#1754`'s problem

What follows is Phase 150's measurement over the UI vocabulary, kept verbatim because it is the
motivation for the per-constructor lemma shape Phase 168 emitted, and the test that shape has to
pass where the UI vocabulary now lives. None of it describes a module in this directory any more: the numbers
are the adopter's to reproduce, and the remedies are the adopter's to spend. "This corpus" below
is the wire-format corpus's `idl.json`, 43 kinds, of which the cost rule selected 20.


**The cost, measured — and the reason the theorems are OPT-IN.** On the pinned prover, cold, with
the leg's own flags (`--z3rlimit 40 --quake 3`; `--ext context_pruning` emitted inside both
generated modules). `WireDecode` measured 37s here against the 33s `modules.json` records, which is
what says the measurement is of this leg rather than of something adjacent to it.

- **`Vocabulary` — 322s and 398s across two runs.** Budgeted in `modules.json` like every other
  module, and checked on every run.
- **the proof script — NOT COMMITTED, and here is exactly how far it got.** The emitted round trip
  verifies for a SMALL vocabulary: green at one kind and at eight, on the pinned prover, with no
  admits. At the twenty kinds this corpus's proof vocabulary selects it does not, and it fails
  twice over, in this order. First `rt_node`, a single query of 65 goals — the node's five OPTIONAL
  envelope members put 32 object shapes into it — proved 64 of 65 at the leg's default rlimit and
  FAILED the third `--quake` seed: green standalone, red under `check.ps1`, the shape
  `Preservation`'s `invert_applicable` entry warns about. Raising to `--z3rlimit 200`, the remedy
  that precedent took, stops that goal failing and starts it grinding, with no run under
  `--quake 3` observed to finish inside half an hour. And at that rlimit WITHOUT quake the run
  completes in 442s and reports a different failure: `rt_vkind`'s `FileUpload` arm — eleven
  members, five of them conditional, so the same 2^k explosion one kind wider. **A `.fst` that does
  not verify is worse than no `.fst`**, so none is committed, and `proofs.json` carries no `proved`
  row for the round trip: a claim the leg does not reproduce is not a claim the ladder will carry.

**Where the cost is, which is the finding worth carrying forward: the node ENVELOPE's closure, not
the kinds.** One kind and eight kinds measured the same, because `Accessibility`, `SemanticStyle`,
`StateBehaviour`, `TextSource` and nine `Binding<…>` instantiations are paid by any kind at all.
The curve then turns superlinear in the size of the mutual family: the whole expressible
vocabulary — 42 kinds, ~30 more declared types — did not finish a single check in twenty-five
minutes. So narrowing the selection further buys almost nothing, and widening it is not a matter of
patience.

**What would make the theorems land, for whoever takes it — and the two levers already spent.** Both
failures are the same shape: one lemma, one query, 2^k object shapes for k conditional members. The
fix is therefore structural, and it is to emit each kind's round trip as its OWN lemma rather than
as one arm of `rt_vkind`, and to break the node's five envelope members across several lemmas
rather than one. That is generator work, and the emitter is where it belongs.

Two levers were tried first, and are recorded so they are not tried again blind. `--split_queries
always` is the option actually designed for one hard goal inside a large query — it is **not
settable as a `#set-options` pragma on the pinned prover** ("unrecognized option"), so it would
have to be passed by the leg. And routing conditional members through an `opt_cons` helper with an
SMT-patterned lookup law — sound, and it removes an exponential duplication in the emitted text as
well — **measured WORSE**: `Vocabulary` itself, 322-398s before, had not finished in over twenty
minutes after, because the helper's pattern then fires throughout the encoder too.

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

### Key order — rule 2's decoder obligation, and §20's one answer (Phase 152)

WIRE_FORMAT §2 rule 2 obliges every decoder to accept an object's members in **any order**, and §20
ratifies that the same bytes decode to the same tree on every conformant host. Both were pinned by
`reject` and round-trip fixtures and neither was a theorem — and **every fixture in the corpus is
canonically ordered**, so a decoder that silently depended on member order would have passed all of
them. That is the gap this closes, and it is why the differential below SHUFFLES rather than adding
fixtures: a fixture family cannot test a property its own canonical form excludes.

**The relation.** `member_perm` relates two documents that differ only by the order of object
members, at any depth. Arrays are compared **pointwise** — an array's order is content, not
presentation, and a relation that permuted them too would be proving something false. Scalars must
be equal. An object's members are matched **by key**, with the matched values related recursively,
so it is a congruence and not a shallow list permutation: `{"a":{"x":1,"y":2}}` and
`{"a":{"y":2,"x":1}}` are related.

**What is proved, in both directions.** Forwards: `decode_perm_invariant` — for every related pair,
the scalar readers, `kindOf`, `strField`, `intField` and `mapList` return **equal** outcomes,
message included, and `getProp`, whose result is itself a reordered subtree, returns a **related**
one. `decode_node_perm_invariant` is §20's sentence at this layer: the node decoder returns the
*same tree*, literally equal, since a decoded node carries no members of its own. Backwards — and
this is the half that keeps the first from being vacuous, since every invariance lemma above is
trivially true of a relation that holds of nothing — `perm_covers_reorder` shows the relation
**contains every reordering of a duplicate-free member list whose values are themselves related**.
That is one level of a deep reordering, with `member_perm`'s scalar arms as the base cases, so it is
the induction step rather than a claim about the root; `member_perm_refl` and
`perm_covers_selection` are the instances a reader can check by eye.

**The premise, and why it is necessary rather than convenient.** `getProp` is a `List.tryFind` over
the member list, so it answers with the **first** member of the given name. On a list with no
repeated key that is order-independent; on one with a repeated key it is not — and **nothing
upstream excludes the case**: `Json.parseObject` appends every member with no key check, and Phase
146's `parse_members` models exactly that accumulation. So the duplicate-free premise is carried
here rather than inherited from the parser, and `duplicate_keys_break_order_invariance` proves it
cannot be dropped: two documents that are the same members in a different order, and `getProp`
answers differently. Matching by key is where the premise lives in this formulation — the relation
declines to relate that pair rather than relating it and lying.

**Not claimed.** The relation is not proved transitive or symmetric; nothing needs either, and
neither is asserted. And nothing here is said about `Canon.render`'s key ordering on the way **out**
— that is the wire-format corpus's, not this theorem's.

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
| the same corpus, SHUFFLED (Phase 152) | every value of every `nodes/` and `ops/` fixture, members reordered at every depth, 4 seeded shuffles each: production against ITSELF across the shuffle, and the oracle against production on the shuffled document | refuse path (asserted) |
| the reference vocabulary, shuffled | 150 generated nodes encoded and reordered, 4 shuffles each, plus 300 generated documents | both (each asserted where its pool reaches it) |

The **go-red cases** are two, because this theorem has two halves to lose. The Phase 135 one hands
the oracle a *blind bridge* that reads every wire integer as a float — the decode family's
counterpart to the fold family's blind footprint — and requires `asInt` to disagree, on a hand-made
value and over the generated sample. The Phase 152 one hands the SHUFFLE differential a `getProp`
that reads the **first** member of an object rather than the one it was asked for: on the
canonically ordered corpus that decoder is very nearly right and every pool above it passes, and
under a reordering it must lose. That case also asserts its shuffles actually **moved** a member and
that every drawn pair is one the extracted relation relates — so a failure there is the decoder's
and not the probe's. A green report is therefore known to be a comparison that can lose, in both
directions.

Each shuffle is additionally checked to be an instance of the theorem at all: the extracted
`member_perm` — the model's own relation, not a second one written in the host — must hold of the
pair, and duplicate-keyed documents are filtered out by the extracted `keys_unique_deep` for the
reason the section above gives.

The **proof** was falsified the same way before it was trusted, on scratch copies: dropping
`as_float`'s `JInt` clause reddens `decode_total`; renaming the `"text"` kind tag in the encoder
reddens `decode_encode_roundtrip`; making the tolerant reader erase one off-policy member shape
reddens `lenient_agrees_off_policy`. Phase 152 added four more, each on a scratch copy and each
landing on a different lemma: making `extract_field` ignore the name it was given reddens
`perm_covers_selection`; relating arrays by length alone reddens `map_list_go_perm`; dropping
`keys_unique` from `perm_covers_reorder`'s hypotheses reddens it at exactly the step the premise
pays for (`key_count k hs' == 0`); and relating two `JInt`s without their payloads being equal
reddens `perm_as_int`. Each landed on the lemma that should have caught it.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** On the model: the five results above, for every input,
   under no hypothesis at all — unlike the fold theorem, which rests on `independence_diamond`,
   this one assumes nothing about a domain. F\* 2026.09.06, Z3 4.13.3, every query 3/3 under
   `--quake 3`, `--report_assumes error` on, no `assume`, no `admit`.
2. **Differentially tested.** The extracted model agrees with `Wire.Decode` over the pools above,
   on class, value and message. Agreement is over those pools, never over all inputs. Since Phase
   152 that includes each pool SHUFFLED: production's answers are compared against its own on the
   unreordered document — equality for every combinator whose result carries no members, the
   extracted `outcome_perm` for `getProp`, whose result is itself reordered — with a decoder that
   reads members by position required to lose.
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
   - **Duplicate keys are out of scope, by a premise taken HERE.** Nothing upstream excludes a
     repeated member name — the parser appends every member, and Phase 146's model says so — and on
     one `getProp` genuinely does depend on order. `member_perm` therefore does not relate a
     duplicate-key reordering, and `duplicate_keys_break_order_invariance` proves that is
     necessary rather than convenient. What the key-order result claims is claimed about
     duplicate-free documents; on a document with a repeated key the question is open and the
     answer is "whichever came first".
4. **Not claimed.** Anything about `Json.parse`; anything about a domain's own decoder beyond the
   reference vocabulary modelled here (what carries to one is the combinator layer it is built
   from, not its clauses); anything about encode — `Canon.render`'s key ordering and float layout
   are certified by the wire-format corpus, not by this theorem; and transitivity or symmetry of
   `member_perm`, which nothing here needs and nothing here asserts.

**Phase 150 amended clause 4's middle third, and Phase 173 re-aimed the amendment.** The third
is "anything about a domain's own decoder beyond the reference vocabulary modelled here". Phase
150 replaced that boundary with a generated model of the UI vocabulary's `idl.json`; Phase 173
replaces it with generated models — and committed, checked round trips — of the three
vocabularies the engine is certified on. So what carries to a domain is the combinator layer
**and** a machine-checked demonstration that the BACKEND which would generate that domain's
model emits total decoders and a discharging round trip over both wire shapes and every type
case the reference vocabulary reaches. What the amendment does NOT buy, and must not be read as
buying: this is a theorem about the generator over the certification set, not about any
domain's vocabulary and not about any HOST's decoder. A domain runs the same generator over its
own `Idl` in its own repository (`fuaran#1754` for the UI one); a host keeps its own hand-written
decoder and is certified against its corpus. The per-kind coverage, the one reason a kind is
outside it, and the cost are in the section above.
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

**The instantiation contract (Phase 175).** That composition is now a TEMPLATE a domain fills rather
than a shape it re-derives. `kit/templates/Instance.fst.template` is `Skeleton.fst` with fourteen
named holes, and the `Proofs.Kit` family (`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`)
reproduces the committed `Skeleton.fst` from it byte for byte — the identifier holes from a
twelve-entry map, the two prose holes recovered from the instance itself — so the template and its
first instance cannot drift apart silently, and a wrong value or an unfilled hole is shown not to
reproduce it. The contract is the preamble's three rules — drop the preamble, fill every hole, leave
no `{{` — and its content is which holes are obligations: **exactly one**. `{{DIAMOND}}` is a lemma
in the domain's own module proving `independence_diamond` for its footprint and its apply, which is
the `independence-diamond` row of the contract table PROVED at that domain rather than sampled by
`Conformance.footprintLaws`; here it is `TreeOps.op_independence_diamond`. What remains in the
theorem's `requires`, `lanes_apply`, is the `lanes-apply` row and stays sampled, because it is a
statement about the lane set in hand and not about the domain. What the template does NOT give is
Theorem 5's preservation clauses, Theorem 6's diff clauses and this algebra's own diamond: those are
stated about `TreeOps`'s algebra, so a domain whose state is the skeleton tree inherits them by
copying the files, and a domain with its own apply models its own. The ten models and the template
are the kit's declared copy set — `copies.json` `$sources`, one entry per file saying which theorem
it carries and which obligations it asks for — and `kit/README.md` "Importing a theorem" is the
procedure.

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

### What was left open, and is now closed — all fifteen pairs (Phase 162)

Counted rather than estimated. The fifteen unordered pairs over the five ops are the ten pairs of
non-`Batch` ops plus the five involving a `Batch`. **All ten non-`Batch` pairs were proved by Phase
133** — nine of them by `relocating_forces_inert` and the three commutation equalities covering the
rest — along with **two of the five `Batch` pairs**, `RemoveNode`/`Batch` and `MoveNode`/`Batch`,
because a relocating op forces the other side inert whatever it is, so no lift was needed.

**Three remained open**, all the same shape: `InsertChild`/`Batch`, `ReorderChildren`/`Batch` and
`Batch`/`Batch`, where the batch is one that neither does nothing nor relocates — a batch built only
from inserts and reorders. `TreeOps.covered` was that boundary written as a predicate, and the
diamond was stated over it.

**Phase 162 performed the lift and `covered` is gone.** `tree_independence_diamond` now quantifies
over every pair with no shape hypothesis, `op_independence_diamond` states it over the guarded
algebra, and `Skeleton.fst` composes over the whole `SkeletonOp` alphabet — `Batch` included and
nested to any depth. The argument is section 20 of `TreeOps.fst` and it is the one Phase 133
predicted: independence descends through a union, then the leaf diamond lifts along a script by
induction, threading the id-uniqueness invariant at each intermediate step, in the shape
`DagFold.replay_diamond` already uses at lane granularity.

**The history of the boundary is worth keeping, because it moved twice and the two moves are
different kinds of thing.** Phase 133 wrote "it closes when Phase 137 lands", which was true of the
BLOCKER and not of the work: 137 fixed the validator and Phase 138 proved `apply_preserves_wf`
unconditionally, so the hypothesis arrived — and the three pairs stayed open anyway, because nobody
had done the induction. A boundary waiting on a theorem and a boundary waiting on labour are not
the same kind of open, and only the second was left by 2026-09-14.

**One thing Phase 162 found while doing it, which is worth the sentence.** The lift did NOT need
`Preservation.apply_preserves_wf`, although that is the lemma every note above names. That module
OPENS `TreeOps`, so it could not have been cited here in any case — and it does not have to be: the
residue `covered` left out is precisely the pairs where NEITHER side relocates, and a non-relocating
op carries no `RemoveNode` and no `MoveNode` at any depth. It is built from inserts, reorders and
nests of them, for which `ins_wf` and `reorder_wf` were already in this module. What section 20 adds
is `no_reloc_preserves_wf`, the non-relocating fragment of 138's invariant, proved where the lift
needs it. The unconditional statement remains `Preservation`'s.

`batch_lift_is_not_vacuous` pins that the widening is real: a concrete batch/insert pair whose
footprints ARE independent, which `covered_classes` REFUSES, and whose two orders reach the same
tree — evaluated, so it goes red if the excluded shape is ever readmitted.

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

1. **Proved (machine-checked, no admits).** The diamond for EVERY operation pair, at every id-unique
   tree — `Batch` included and nested to any depth, since Phase 162 — and the composite
   fold-confluence law over the whole `SkeletonOp` alphabet, with
   its halt half under no hypothesis at all; the preorder-position lemma
   (`preorder_parent_first`) with its preservation corollaries; the refutation of unconditional
   well-formedness
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
   - **The extractor and the F# compiler are trusted** — the same link, and the same wording, as for
     the other two theorems.
   - _(**The op alphabet excludes a nested `Batch`** was the third assumption here and is RETIRED —
     Phase 162 lifted the diamond along a batch's script, so there is no shape hypothesis left to
     assume. The section above keeps what the assumption was and how it closed.)_
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

  **A codec built on `Canon.render` no longer has to be sampled for it — see theorem 7.** Phase 149
  proves the canonical form injective up to object-member order over the subset rule 5's own slot
  rule describes, so a codec that renders through `Canon.render` DISCHARGES this parameter from
  that theorem rather than from its witness, with one thing to check and one to know. The thing to
  check is the subset: the codec's payloads must carry no non-finite float and no float whose token
  is integer-shaped, which are exactly the two aliasing families theorem 7 exhibits. The thing to
  know is that "up to member order" is not a weakening here — a codec's own injectivity is a claim
  about the VALUES it encodes, and two objects differing only in authored member order are the same
  value. A codec that does not render through `Canon.render` is unaffected and stays at this level.

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
   **Phase 162 adds two**, section 10 — the preorder-position lemma's corollaries on this side of
   the module boundary: `rem_preserves_parent_first`, in the SURVIVOR form (a remove takes ids
   away, so the statement is over the tree it hands back and not over the one it was given), and
   `apply_preserves_parent_first`, which is the same over ANY accepted operation including a
   nested `Batch` and is the one a reconstruction argument cites. Both are `rem_wf` /
   `apply_preserves_wf` followed by `TreeOps.preorder_parent_first`; they are here rather than in
   `TreeOps` because `rem_wf` is this module's and that module cannot cite it.
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

**The positional facts about `after` (section 9, Phase 162).** `diff_insert_parent_precedes` — an
insert's parent precedes its own node in `after`'s preorder, which is the source comment's
"top-down" stated about the emitted operations rather than about the loop;
`diff_move_destination_precedes` — a move's destination precedes the moved node; and
`diff_move_destination_is_outside` — therefore, **in `after`, a move's destination is never inside
the moved node's subtree**, which is the consequence Phase 141 named when it deferred the two
theorems below. All three stand on `TreeOps.preorder_parent_first` (section 19 there), with
`precedes`' antisymmetry and its irreflexivity on an id-unique tree closing the last one.
`positional_facts_are_not_vacuous` evaluates a pair that emits exactly one insert and one move, and
pins that the moved node does NOT precede its destination — so the claim is an ordering fact and
not a tautology.

### What is NOT proved, and why the boundary is where it is

`diff_reconstructs` (`applyAll (toOps b a) b = a`) and the OPERATIONAL `diff_applicable` (every
emitted step is ACCEPTED in sequence) are **differentially tested here and not proved**.

**Phase 141 named the blocker as one missing positional fact, and Phase 162 proved it — and the two
theorems are still open, which is the honest correction to make here rather than a note to bury.**
The fact is real and the section above carries it; what it does not do is close these two, because
they are statements about a different quantifier. `apply` validates each step against the tree IN
HAND at that step — the before-tree with the script's earlier operations run on it — and not against
`after`. `diff_move_destination_is_outside` says the destination is outside the moved subtree in
`after`; what `WouldNestUnderSelf` asks is whether it is outside in the intermediate tree. Carrying
one across to the other needs an invariant about how much of `after`'s structure each PREFIX of the
script has already built, and that induction is the remaining work: it reasons about `ins` and
`rem_at` over intermediate trees, in the cost class `Preservation.fst` occupies rather than the one
this module does. Both phases were time-boxed and neither took it.

What changed is the SHAPE of the gap, and it is worth naming because it is the difference between
two kinds of open: this was a missing fact, and it is now a named induction over a proved one. The
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
   `diff_contained_at_total_is_plain` and `diff_applicable_contained` — and, since Phase 162, the
   three positional facts about `after`: `diff_insert_parent_precedes`,
   `diff_move_destination_precedes` and `diff_move_destination_is_outside`. F\* 2026.09.06,
   Z3 4.13.3,
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
   - **Reconstruction and operational applicability**, at level 1 — see the boundary above. They
     were described here as "one positional lemma away"; Phase 162 proved that lemma and they are
     still open, so what is left is the named induction relating `after`'s order to the
     intermediate trees, not a missing fact.
   - **`Diff.toOpsMoved`** (fuaran-core#63, the move-aware diff) — not shipped, so not modelled and
     not claimed. When it lands it is a second emission strategy over the same two trees, and every
     theorem here is about `toOps`' four passes specifically rather than about diffing in general.
   - **`Ops.normalize`**, which no theorem in this directory reaches.
   - **Containment LEGALITY** — which kind may parent which. As for theorem 5: `canHold` answers
     only "can this node hold children at all", and no theorem here says a contained tree is a legal
     document.
   - **Rejection PAYLOADS.** The differential compares a refusal by class and by the offender a
     `TargetNotAContainer` names; `UnknownNode`'s `addressable` list is outside the comparison.

## Theorem 7 — the canonical form is injective (Phase 149)

WIRE_FORMAT §2 promises that its twelve encoder rules make the canonical form **deterministic**:
structurally equal values render byte-for-byte identically. Every digest in the estate needs the
stronger property nobody states — that **equal bytes imply equal values**. The op-stream chain id,
the DAG content id, the teleport digest (§17.3) and cross-host attestation all hash the canonical
rendering and read hash equality as value equality, and theorem 3's decomposition leaves
`op_codec_injective` a parameter precisely because a codec built on `Canon.render` inherits
injectivity only if `Canon.render` has it.

`WireCanon.fst` models `Canon.escape` (rule 6), `Canon.canonicalFloat` (rule 5, including the `-0`
collapse and the three non-finite tokens) and `Canon.render` (rules 2, 3, 5, 6, 7, and the omitted
key that is rule 4) clause for clause, and proves the converse. **Read the refutation first**,
because it is what the theorem's shape is for.

### The finding: `render_injective`, as the phase was chartered to prove it, is FALSE

The shard asked for `render a == render b ==> a == b` with no hypothesis. That statement does not
hold of the shipped encoder, four ways. Each is proved in the model for **every** instantiation
rather than exhibited at a contrived one, so no future choice of numeric carrier escapes them:

1. **A non-finite float is a STRING on the wire.** `canonicalFloat` emits the quoted token `"NaN"`
   — and so does the *string* `NaN`. `render_aliases_nan`, `render_aliases_pos_inf` and
   `render_aliases_neg_inf`.
2. **An integral float is an INTEGER on the wire.** A finite float whose layout carries no `.` and
   no `E` renders exactly as the integer of that token. `render_aliases_integral_float`. This is
   the numeric normalisation `JVal`'s own type doc names ("`render (JFloat 2.0)` emits `2`, which
   `parse` reads back as `JInt 2`"), and rule 5's last paragraph is the same sentence from the
   format's side.
3. **The two zeroes are one token**, by rule 5's `-0` collapse. `render_aliases_negative_zero`.
4. **Member order is not observable**, by rule 2's sort. `render_aliases_member_order`.

The fourth is not a loss at all — it is the entire purpose of a canonical form, and it is why the
theorem below is stated *up to member order* rather than flatly. The second and third are
documented design choices of the format, and the model exhibits them so that a future session
cannot "fix" one by accident.

**The first is the finding, and it is about the shipped code rather than about the format.**
`Json.render` has a guarded companion, `Json.tryRender`, which names a non-finite float as a typed
`Error` "instead of producing un-parseable wire". `Canon` has **no such companion**. A non-finite
float reaching `Canon.render` is not un-parseable — it is worse than that, because it silently
becomes a *string*, so a digest over `JFloat nan` equals the digest over `JStr "NaN"`, which is
precisely the value a reader decodes those bytes back to. Nothing in `Canon` refuses it. This
phase records the asymmetry rather than repairing it: a repair is a refusal-class change to a
shipped encoder, in the shape Phase 137 took, and belongs to a phase chartered for it. The
`Proofs.Oracle` case that holds the four aliases asserts them on **production**, so the finding
goes red if the encoder ever changes.

### What is proved

**The canonical subset is rule 5's own slot rule, read as a predicate.** A float is canonical
exactly when it is finite and its canonical token carries a `.` or an `E` — which is the
discriminator rule 5 uses to decide whether a token "keeps integer identity". Refutations 1 and 2
are exactly its two failure modes, so the premise is the format's sentence rather than a hedge
chosen to make a proof go through.

- **`render_total`.** That `render` reaches a rendering on every value is carried by its type. What
  the lemma adds is that the rendering is never empty and that its FIRST CHARACTER classifies the
  constructor that produced it, and is never a closing or separating one. That is what makes the
  shape dispatch exhaustive rather than merely non-empty — theorem 1's distinction, on the encode
  side — and it is what the reader's own dispatch turns on at every position.
- **`read_render_roundtrip`.** A reader for exactly the grammar `render` emits is a LEFT INVERSE of
  it on the canonical subset, **in every trailing context**: `read (render v ++ rest) == Ok
  (normalise v, rest)`. This is the engine; everything below is a corollary.
- **`render_injective_up_to_key_order`.** Equal bytes imply equal normal forms — the converse §2
  never stated, and the one the digests rest on.
- **`render_deterministic`.** Equal normal forms imply equal bytes — §2's own promise, proved.
- **`canonical_form_iff`.** The two together, which is what "canonical form" means and is more than
  either half.
- **`render_injective_on_normal`.** The literal `render a == render b ==> a == b`, on values whose
  object members are already in canonical key order. That is not a contrivance: it is the shape of
  every value a reader hands back (`read_returns_a_normal_value`), so it is what a consumer
  comparing two decoded documents actually holds.

**The four rule lemmas are the proof's own parts rather than decoration.** Rule 2 —
`sort_produces_sorted` and `sorted_is_its_own_sort`: under the comparator's total-order premises
the sort is idempotent, so the author's key order carries no information into the bytes. Rule 6 —
`read_str_inverts_escape`: the escaped body is uniquely decodable **in any trailing context**,
which is the property injectivity needs and which plain injectivity of `escape` would not give.
Rule 5 — `canonical_float_injective` and `int_layout_injective`, DERIVED rather than assumed (see
the ladder). Rule 4 — `absence_is_structural`: two objects that render alike carry the same keys,
so an omitted key can never be confused with a present one, which is what makes "`None` fields are
excluded" safe rather than merely tidy.

### Why a reader, and not a second model of `Json.parse`

Injectivity of a recursive encoder is either a first-difference induction over rendered byte lists
or the exhibition of a left inverse. The second is very much cheaper, and it is also more useful:
what it produces is a **round trip**, which is a statement a reader of this document already knows
how to want.

The reader is deliberately **not** a second model of `Json.parse`. Theorem 4 models that parser —
its depth counter, its twelve error classes, its two numeric guards — and remodelling it here would
prove the same thing twice at several times the prover cost. What injectivity needs is that an
inverse EXISTS, not a second account of production's own. Its behaviour on input `render` never
emits is therefore not claimed and not tested, and the differential runs the round trip against
`Json.parse` to tie the two together over the corpus.

### The §21 limits, and the one premise that reaches them

`Limits.fst` carries WIRE_FORMAT §21's eight bounds as named premises, with the two relations the
section's own argument uses — every bound admits something, and the node-depth bound sits below the
syntactic-depth bound with room for the worst-shaped kind, which is what makes "a host must never
report a node-depth breach as a syntax-depth breach" a fact about the table rather than a hope
about it. It models **no enforcement**: §21.2's host obligations are about code it does not
describe.

`read_render_roundtrip_within_limits` is the round trip restated with `max_json_depth` carried.
Nothing in the proof uses the hypothesis, and that is the point — within the format's own limits
the round trip is unconditional, and outside them it is *production's* cap and not this model that
decides. The premise is there so a change to the §21 table moves one constant and the ladder can
say which theorem depended on which bound.

### What the corpus covers

The differential host runs the extracted encoder beside `Canon.render` and compares the **bytes**,
which is the only comparison that means anything for an encoder whose whole job is to produce a
digest input.

| Pool | What is asked | Reached |
|---|---|---|
| the wire corpus's `nodes/` fixtures | every fixture rendered by both, byte for byte; and the round trip on the canonical subset | yes (asserted: fixtures, sortable objects, round trips) |
| the wire corpus's `ops/` fixtures | the same | yes (asserted) |
| a generated `JVal` pool | 1,200 seed-replayable documents over an alphabet carrying rule 6's three escape classes, astral and private-use keys, and rule 5's scientific layout | yes (each shape asserted reached) |
| the four aliasing pairs | asserted on PRODUCTION, and on the model beside it | the finding, as a check that can go red |
| the non-canonical arms | both infinities, NaN, both zeroes, the Int32 boundaries, an empty string, an empty object and array | the model is a model of the whole encoder, not only of the part the theorem covers |

The **go-red** is rule 2's comparator REVERSED — the sort the rule mandates still runs, but orders
keys the other way. Every object carrying two distinct keys must then disagree, and a document
carrying none must still agree; both are asserted, so the instrument is known to be one that can
lose *and* to be narrow to the rule it is about.

One thing about the host is worth knowing before anyone reads a stack trace. The extracted model is
a **character-list interpreter** and F\*'s F# backend emits plain recursion with no tail calls, so
rendering a multi-kilobyte fixture walks a stack proportional to the document's bytes and the
largest fixture in the corpus overflows the default 1 MB one. That is a property of the
EXTRACTION, not of the model — the theorem is about a function, not about a runtime's frame budget
— so the differential runs on a thread with a stack sized for the corpus rather than shrinking the
pool until it fits. Shrinking would have silently narrowed what the corpus leg certifies, and the
fixture it would have dropped first is the deepest one.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** On the model: `render_total`,
   `read_render_roundtrip`, `render_injective_up_to_key_order`, `render_deterministic`,
   `canonical_form_iff`, `render_injective_on_normal`, `read_returns_a_normal_value`, the four
   rule lemmas, the §21 relations in `Limits.fst`, and the four refutations. F\* 2026.09.06,
   Z3 4.13.3, every query 3/3 under `--quake 3`, `--report_assumes error` on, no `assume`, no
   `admit`. The module carries a scoped `--ext context_pruning` for the reason `TreeOps.fst` gives
   at the same line.
2. **Differentially tested.** The extracted encoder agrees with `Canon.render` byte for byte over
   both corpus families and the generated pool, and the model's round trip agrees with
   `Json.parse ∘ Canon.render` over the same documents on the canonical subset. Agreement is over
   the corpus and the pool drawn, never over all inputs. One go-red, required to lose.
3. **Assumed, and stated as such.**
   - **The numerals are opaque, and there is exactly ONE premise about them.** The two layouts and
     the numeral read-back are parameters, and `tok_read_ok` says the read-back inverts the
     layouts — which is what rule 5 means by "the shortest digit sequence that ROUND-TRIPS", so the
     premise is the float layout's own definition rather than an extra assumption beside it. Rule
     5's injectivity lemmas are **derived** from it, because a read-back that is a function cannot
     answer twice for one token. What is therefore not said is anything about the digits: which
     decimal .NET produces for a given double is the differential's and the cross-host parity
     vectors' to measure.
   - **The comparator is a parameter constrained to be a total order.** What is proved is that a
     canonical form follows FROM a total order. That `System.String.CompareOrdinal` IS one, and
     that it is UTF-16 code-unit order rather than code-point or UTF-8-byte order, are facts about
     .NET — observable exactly where rule 2's own note says they are, above the BMP, and pinned by
     the corpus fixture and by the differential's astral keys.
   - **A character is a constructor, and the bridge is the correspondence.** As in theorem 4. The
     model writes the bridge's condition down as `bridged` — a verbatim character never aliases a
     constructor's own spelling — rather than leaving it in prose, and the host's mapping satisfies
     it by construction; the mapping itself is one line per class and is not proved.
   - **The extractor and the F# compiler are trusted** — the same link, and the same wording, as
     for the six theorems above.
4. **Not claimed.**
   - **Injectivity outside the canonical subset**, which the four refutations show is not merely
     unproved but false. A consumer hashing `Canon.render` output is relying on the subset whether
     it says so or not, and the two families it has to exclude are named above.
   - **`Canon.renderOrdered`.** It is the declared-key-order leg, where the ENCODER is the order
     authority and no sort runs; its canonicity rests on a different argument (the IDL's
     `WireShape.KeyOrder`), and nothing here carries to it.
   - **That a non-finite float cannot reach `Canon.render`.** It can; nothing refuses it. See the
     finding above.
   - **Anything about `Json.parse`'s own behaviour** beyond the round trip the differential
     measures — that is theorem 4's, and the boundary between the two is deliberate.

## Theorem 8 — evolution-policy soundness (Phase 151)

WIRE_FORMAT §15.4 classifies a vocabulary change by an IDL diff — no removed tags means additive,
any removal or rename means breaking — and §15.3 promises that a `Behind` consumer **preserves**
what it does not understand. Since Phase 127 the classification is computed rather than
hand-applied: `Versioning.classify` and `Versioning.bump` in `src/Fuaran.Core.Wire/Wire.fs`, driven
by the kind-tag delta an `idl.json` pair yields. It is exercised by tests that perturb the real
vocabulary, and those tests answer a different question from the one the table raises. A test says
the classifier returned `Additive` for this pair. The table says an `Additive` step is one every
old document survives — and no sample establishes that, because the claim is universally quantified
over documents nobody wrote.

`WireVersioning.fst` models `classify`, `bump`, `negotiate`, `decodeTolerant` and `reencode` clause
for clause and closes the gap. It is the first model here that is about a POLICY rather than about
an algorithm's output, which changes what "sound" means: there is nothing to compare the classifier
against except the sentence the specification writes, so the theorems are that sentence,
mechanised.

**The module is `WireVersioning` and not `Versioning`**, for the reason `WireCanon.fst` is not
called `Canon`: the extracted oracle is a top-level F# module and the differential host opens
`Fuaran.Core`, which already carries a `Versioning`. Here the collision is unavoidable rather than
merely likely — the host step is `classify` beside `classify`, in one expression.

### What is proved

The order matters — the first is what the other three stand on.

- **`classify_sound`.** `classify` answers `Additive` only when every tag of the before-vocabulary
  survives into the after-vocabulary. The function computes a LIST (a difference) and the table
  talks about a SUBSET, and the two are the same statement only through an induction; that
  induction is the module's whole substance. `classify_additive_is_sub` packages the quantified
  form for a reader who wants the table's sentence literally.
- **`rename_is_breaking`.** A tag present before and absent after forces `Breaking`, whatever else
  the change added. §15.4 calls this row out by name because it is the one an author gets wrong — a
  rename LOOKS additive from the new vocabulary's side, since a new tag appeared. Nothing about the
  addition enters the proof, which is the point.
- **`additive_monotone`.** Given an `Additive` verdict, a document whose tag the OLD vocabulary
  already carried decodes under the NEW vocabulary to exactly what it decoded to before — the same
  `Known`, the same payload, the same error if the domain decoder errors. Not "still works" but
  `==`: nothing about the widening is observable to a document that predates it. Quantified over
  every discriminator reader, every known-decoder and every required-profile reader, so it is a
  fact about the seam rather than about one domain's codec.
- **`preserve_exact`.** §15.3's must-ignore-but-preserve, **at the bytes**. A producer authors a
  value under the new vocabulary and renders it; an old consumer parses those bytes, meets a tag it
  does not know, tolerantly decodes, re-encodes and renders — and what comes out is what went in.
  This is why the module opens `WireCanon`: the claim is about bytes, theorem 7 already modelled
  the renderer that produces them, and re-deriving it here would be a second renderer to keep in
  step with the first. Two of theorem 7's lemmas discharge it (`read_render`,
  `render_ignores_key_order`), and the proof needs nothing about the payload beyond its identity —
  which is the shape of a preservation claim that holds by construction.
- **`unknown_transport_only`.** No value an ENCODER produces decodes to `Unknown`, for a consumer
  that knows the vocabulary the encoder authors against. The shipped source says this in a comment —
  "transport-only: it is reachable here and nowhere on the authoring/encode path" — and a comment is
  what this directory exists to replace. `reencode_known_is_encode` closes the other direction:
  `reencode` is the only function that consumes a `decoded`, and on a `Known` it is exactly the
  encoder, so no round trip through the seam can introduce one either.
- **`breaking_bump_is_foreign` / `additive_bump_is_behind`.** The table's two rows as the two
  outcomes they produce: a breaking verdict mints a new major and the old consumer is `Foreign` (it
  refuses rather than mis-decoding); an additive verdict keeps the major and the same consumer is
  `Behind` (it tolerates). A soundness claim about `classify` that stopped short of `bump` would
  leave the consequence a reader acts on unproved.

### The finding: §15.4's optional-field row is satisfied VACUOUSLY

The table has a row for an added optional field, and it is additive. **Nothing in this module sees
it, and nothing in this module can.** `classify` ranges over KIND TAGS; an optional field added to
an existing kind changes no tag, so the delta is empty and the verdict is `Additive []` — the no-op
arm. The row comes out right for a reason unrelated to what it is about.

This is recorded rather than repaired, per the phase's own rule that a row the theorem cannot
support is raised to the specification's owners and not made true by rewriting `classify`. Two
things are worth knowing about it before anyone takes it.

**It is not an oversight in the classifier.** It is a consequence of rule 2's unknown-key tolerance:
an added optional field is invisible to the CLASSIFIER because it is invisible to the DECODER, and
both facts have the same cause. A consumer that meets a member it does not recognise ignores it and
preserves it, so the document decodes the same either way — which is exactly the property the
additive row promises, arrived at by a route the tag delta does not describe. The policy's verdict
is correct; its stated reason is not the one that makes it correct.

**Widening `classify` here would model a function this repository does not ship.** `Diff.classify`
in `Fuaran.Core.Idl.Codegen` DOES see field-level changes and classifies them (`classifyFieldAdd`
reads the four-way optionality), and it is a different function with a different signature serving a
different caller — an advisory host-strand report rather than a profile bump. A model that merged
the two would be about neither. What the module states instead is the vacuity made explicit
(`optional_field_addition_is_a_no_op`): a change that adds and removes no tag classifies as the
empty additive, bumps nothing, and leaves a consumer at the base profile `Current`. That is the
row's observable content on the tag delta, and it is all of it.

The differential carries the same row as a perturbation of the real `idl.json`, with an assertion
that the verdict IS `Additive []` and a failure message saying what a non-empty verdict would mean.
So the finding goes red if the classifier ever grows field awareness, rather than quietly becoming
stale prose.

### What the differential measures

`Proofs.Oracle` runs the extracted model beside production over the two inputs the policy has:

- **The corpus's `envelope/` family**, for the decode half. Each fixture carries a `$profile` and a
  `$payload`; the consumer is `core@1.0`. Four things are compared per fixture — the negotiation and
  the authored profile it carries, whether the tolerant decode found a known kind or preserved an
  unknown one, the `requiredProfile` an unknown carries, and the BYTES out of `reencode` +
  `Canon.render`. The adequacy assertions require all three negotiation outcomes to have been
  reached and at least two fixtures to carry a tag the consumer does not know: a family that only
  ever produced `Current` would exercise neither tolerance nor preservation and would be green
  having tested nothing §15.3 is about.
- **Pairs of `idl.json` revisions**, for the classify half, produced by PERTURBING the pinned
  artifact and re-reading it through `Diff.parse` — add a kind, add an optional field, remove a tag,
  rename. Going through the artifact reader rather than writing two tag sets by hand is deliberate:
  the row is about the classifier applied to an IDL diff, and a hand-written pair would establish
  that the classifier agrees with itself over two lists somebody chose. Each of the four verdicts is
  asserted to land where §15.4 puts it, so a pass in which every pair classified alike is a failure
  rather than a green.

The **go-red** is the model's own `classify_ignoring_removals` — a classifier that computes the
additions and never looks for removals. It is not a strawman: it is what an author writes who reads
the additive row and stops there, and it agrees with production on every purely additive change,
which is most of them. F* refutes it in the same file (`classify_ignoring_removals_is_unsound`), and
the differential asserts it disagrees with production on the removal and the rename and on NOTHING
ELSE — which is what says the comparison is narrow to the rule it is about rather than merely
capable of failing.

### The boundary

1. **Level 1, over the shipped functions.** `classify`, `bump`, `negotiate`, `decodeTolerant` and
   `reencode` are modelled clause for clause and the extraction is run beside production, so the
   theorems are about the functions that ship and the differential says so over real inputs.
2. **Level 3 premises, all `WireCanon`'s.** `preserve_exact` carries `tok_read_ok` and
   `key_order_ok` — theorem 7's two assumptions about the host's numeral reader and its key
   comparator — and rule 5's canonical-subset predicate. It inherits theorem 7's boundary exactly
   and adds none of its own.
3. **Not claimed.**
   - **That §15.4's table is COMPLETE.** Four rows are covered — add a kind, remove a tag, rename,
     and the optional field (vacuously, see the finding). A row the table may grow is a row this
     module does not have.
   - **Anything about the ENVELOPE's own parsing.** `Versioning.parse` / `decode` and
     `Profile.tryParse` are not modelled; the differential drives production's own parser and
     compares what happens after it. A malformed `$profile` is theorem 4's boundary, not this one.
   - **That a consumer ACTS on the negotiation.** `negotiate` returns a verdict; whether a host
     refuses a `Foreign` artifact is host code this module does not describe, in the same sense that
     `Limits.fst` models no enforcement.
   - **The nested case.** An unknown kind inside a known tree is preserved by the same clause, and
     production has a corpus case for it, but the model's `decode_tolerant` is applied to ONE
     artifact object — recursion through a domain tree is the domain codec's, which is a parameter
     here.

## Theorem 9 — columnar op preservation (Phase 176)

_(This directory's ninth. The compute strand is a first-class, stability-critical member of the
substrate, and every domain's table edits replay through `Column.Ops`; until this phase it had no
model. What `Preservation.fst` proved for trees — totality with rejection characterisation,
all-or-nothing rejection, the dry run's agreement, the invariant preserved, `invert`'s round trip —
`ColumnOps.fst` proves for a table with a validity mask, and adds the one clause the tree side
left open at level 1, the diff's reconstruction, because on columns it is an induction over two
list walks rather than over intermediate trees.)_

The doc comments of `ColumnOps.fs` state four promises and one of them is the theorem's name:

> **`apply` — total (a typed `ColumnRejection`, never a throw). `canApply` — "they can never
> disagree". `invert` — `apply (invert op t) (apply op t) = t`. `toOps` — "`apply`-ing it in order
> yields `after`".**

`Conformance.columnarOpLaws` samples the first three at one seed over one shape of table — and,
since Phase 181, the inverse-only-for-applicable law beside them. Nothing sampled the fourth
promise, and nothing said WHICH tables any of the four hold on. `ColumnOps.fst` models
`Column.Ops` clause for clause — the six-case op DU, the eight-case rejection, `apply`, `canApply`,
`invert`, `applyAll` and `Diff.toOps` — over a table read exactly as the algebra reads it: a schema
(an ordered `(name, type)` list), the columns (name, type, cells), `Null` as the validity mask's
absent marker, and a present cell as its type and an **opaque carrier**. `ApplyTransform` runs a
`DataFrame` pipeline the algebra never interprets, so the evaluator is a **parameter** of every
function in the model, as `float i` is in theorem 1 and `ReplaceChildren` in theorem 5.

### What is proved

Seven lemmas, over the six operations, any table and any evaluator — six of them Phase 176's, the
seventh Phase 181's closure of the finding Phase 176 recorded:

1. **`apply_total`** — one outcome on every input, and WHICH rejection each clause can raise is a
   predicate (`raisable`) rather than a list. `NotInvertible` — the eighth class — is proved
   unreachable from `apply`; it is `invert`'s alone.
2. **`reject_identity`** — a refused step leaves the caller holding the input, and for a script the
   all-or-nothing discipline at an arbitrary failure position. A rejected `ApplyTransform` is
   stated on its own (`reject_identity_transform`): `apply` builds nothing before the evaluator
   answers, so there is no partial table to escape.
3. **`canapply_agrees`** — the dry run's verdict and rejection are the mutating call's.
4. **`apply_preserves_wf`** — every accepted structural operation preserves well-formedness. The
   predicate is this phase's, not production's: the schema is the columns' `(name, type)`
   projection, no two columns share a name, every column is the row count long, every cell fits
   its column's type. `Table`'s doc comment states the first three and `Column.create` deliberately
   checks none of them ("no validation — the codec validates the wire"), so a table the F# `apply`
   accepts can break every one, and the two theorems below say exactly which conclusions such a
   table forfeits. `ApplyTransform` preserves it under the evaluator premise (`ev_preserves_wf`),
   and so does every accepted script.
5. **`invert_roundtrip`** — on a well-formed table, the inverse of an accepted `SetCell`,
   `SetColumn`, `InsertColumn` or `RemoveColumn` is accepted at the result and restores the input
   EXACTLY. The partial cases are characterised beside it: `AppendRows` and `ApplyTransform` are
   `NotInvertible` unconditionally; and — since Phase 181 — on all four invertible operations
   `invert` refuses exactly what `apply` refuses, with the same rejection, and answers wherever
   `apply` does (`invert_refuses_as_apply`, an equality of verdicts). As Phase 176 could state it,
   that held for three of the four and only for the pre-states those three READ: where `apply` went
   on to refuse the VALUE, `invert` had already answered `Ok`.
6. **`diff_applicable`** — `applyAll (toOps before after) before = Ok after` on well-formed tables,
   both branches. The column-granular branch is a walk over `after`'s columns replacing each changed
   one in place, with the invariant that what the walk has passed is already `after`'s and what it
   has not reached is still `before`'s (`changed_apply`); the rebuild branch empties the table
   (`removes_apply` — in any order, since the names are distinct) and refills it in order with the
   invariant that the table so far is a prefix of `after` (`inserts_apply`).
7. **`invert_only_for_applicable`** (Phase 181) — an inverse exists ONLY for an applicable
   operation, over all six. This is the clause the finding below reports FALSE of the engine Phase
   176 modelled, and Phase 181 is where it becomes true. `invert_ignores_evaluator` states the other
   half of the guard's shape: it consults no evaluator, which is why the two never-invertible
   operations answer ahead of the guard rather than through it — `invert` does not run a pipeline to
   report that an `ApplyTransform` has no inverse.

### The finding: `invert` on `InsertColumn` read nothing — CLOSED by Phase 181

The F# clause **was** `InsertColumn(_, col) -> Ok(RemoveColumn col.Name)`, unconditionally. Every
other invertible clause read the pre-state — the cell it would restore, the column it would put
back — and refused when the pre-state did not hold it; this one answered the same for a REFUSED
insert as for an accepted one. `invert_insert_reads_nothing` states it, and
`refused_insert_inverse_is_live` states the consequence: the "inverse" of an insert refused as a
`DuplicateColumn` is a remove that SUCCEEDS at the pre-state and takes the column that was already
there. A caller that derives the inverse without first checking acceptance — the natural shape of an
undo stack that records `invert op pre` beside every op it attempts — loses a column the refused
operation never touched.

The contract was not violated: the doc comment defines `invert op t` for "`op` applied to the
PRE-state `t`", which presumes acceptance. But the tree engine keeps that presumption HONEST and
this one did not. `Ops.invert` on the tree side runs `canApply` first and refuses when the forward
step would be refused — theorem 5's `invert_leaf` models that guard as its first line — so a
refused `InsertChild` has no inverse to misapply. The columnar `invert` guarded three of its four
clauses by reading the pre-state and the fourth not at all, and the model made the asymmetry
visible. Phase 176 reported it and did not fix it, per its shard's own rule that a gap the theorem
finds is fixed by its own phase, as Phase 137 preceded Phase 138.

**Phase 181 took the fix, and it is the shape this section named.** `ColumnOps.invert` now runs
`canApply` on the pre-state and returns its rejection where it refuses, so `invert_refuses_as_apply`
is true of a fourth operation and `invert_only_for_applicable` holds over all six. Three details are
worth recording because each was a decision rather than a consequence:

- **The refusal is the REFUSING rejection, not a blanket `NotInvertible`.** A duplicate insert's
  inverse is `Error (DuplicateColumn "a")` — what `apply` itself said. `NotInvertible` keeps its
  meaning, which is "this operation has no inverse at any table", and stays the two never-invertible
  operations' alone. The alternative would have made `invert_refuses_as_apply` false again, in the
  other direction.
- **The guard strengthened all four clauses, not one.** `SetCell` and `SetColumn` read the pre-state
  for the column and the row but never for the VALUE, so a wrong-typed cell or a wrong-length column
  — both of which `apply` refuses — had an inverse too. Those inverses were harmless (a `SetCell`
  restoring a cell to what it already held) rather than destructive, which is why the finding named
  only the insert; they are gone with it.
- **The two never-invertible operations answer BEFORE the guard.** `canApply (ApplyTransform p)` runs
  the evaluator, and `invert` must not evaluate a pipeline to report what it already knows.
  `invert_ignores_evaluator` is that sentence as a theorem, and it is what earns the model's `invert`
  an evaluator parameter it never consults — the model calls `can_apply ev`, as the F# calls
  `canApply`, so the parameter is there and proved dead.

The two negative theorems are **kept, not deleted**: they are now about `invert_pre181`, the model's
copy of the clause as it stood, and `refused_insert_inverse_is_live` states the shipped refusal in
the same lemma as the old live remove. A finding deleted at the moment it is fixed leaves nothing
that goes red if the fix is ever reverted; stated as a pair, neither half can be re-proved while the
other quietly stops holding. The conformance kit carries the closure as a law of its own —
`columnarOpLawsWith`'s "an inverse exists only for an applicable op", with the injectable `invert`
seam `concurrencyLawsWith` established, so the pre-181 clause can be handed to the kit and watched
to lose.

### The differential

`Proofs.Oracle`'s columnar family runs the extracted model beside production over GENERATED tables
and scripts — sixty scripts of six ops at seed 1760, every op asked at every state its script
reaches (360 probes), and 120 pairs of tables at seed 1761 for the diff. The tables are drawn with
the invariant deliberately broken one draw in ten — a repeated name, a column a row long or short,
a schema that is not the columns' projection, a cell of the wrong type — because `apply` is total
over all of them and the model must agree there too; the model's own `wf` on the bridged table says
which population a probe fell in (278 of 360 well-formed). Compared per probe: the verdict, an
accepted result through the bridge, a rejection by class AND payload, the dry run against the
mutating call on each side separately, the derived inverse as an operation, the round trip asserted
exactly where the pre-state is well-formed (71 asserted) and only COUNTED where it is not (17
attempted, 8 failed — the well-formedness hypothesis is load-bearing, and the case asserts that
count non-zero so the hypothesis cannot quietly become decoration), the invariant on production's
result judged by the model's `wf` (85), and every script whole. Over the pairs: the emitted script
compared, and the reconstruction asserted on both sides where both tables are well-formed (67
pairs: 44 through the column-granular branch, 23 through the rebuild). Every one of the eight
rejection classes is reached.

The bridge is the carrier premise made concrete: `Int 5` crosses as `Present(IntType, "5")`, a
float through the round-trip `R` format, a bool as its word, the three string-carried kinds
verbatim, and parses back exactly; the pipeline evaluator is production's own
`DataFrame.evalPipeline` reached through the bridge, so the `ApplyTransform` arm compares the
model's envelope and nothing about the pipeline. The go-red hands the differential a BLIND cell
bridge — every present cell read as a string — and requires it to lose on the type check, which it
does. Seeded and replayable: the same seed reproduces the same tally, asserted.

The family's fourth case is the finding's pin, and since Phase 181 it pins BOTH halves: the shipped
`ColumnOps.invert` refuses a duplicate insert with the rejection `apply` gave, the model's guarded
`invert` refuses it too, and the model's `invert_pre181` still derives the live remove that would
have taken the column already there. A reverted guard and a lost finding each go red, at the same
assertion.

### What it cost

Cheap, and self-contained. Three cold runs through the kit on this machine under `--quake 3` at
the leg's rlimit of 40: **47s, 31s, 22s** — the first carrying the prover's first-run cost beside
concurrent sessions; the prover invoked directly on the same file with the same flags during
authoring, a fresh verification each time, 9–20s. Budget **100s** (2 × 47, rounded up to the next
10s), floor **4s** (half of 9, rounded down), seeded per Phase 148/164's rules and recorded in
`modules.json`. No `--ext context_pruning`: the module opens nothing, so there is nothing to prune,
and the whole of it — 1,700 lines, six theorems, a hundred-odd list lemmas — discharges in the time
`TreeOps.fst` spends on its context alone. The proof shapes that cost anything are the
well-formedness preservation for `AppendRows` (a `forall` over the columns, threaded through
`typed_append_aux`) and the diff's two walks (`changed_apply`, `inserts_apply`), each an induction
carrying an invariant about the prefix already processed. The extraction is byte-identical to a
fresh one on the first leg run, and the oracle compiles against `Prims.fs` with one new alias.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** The seven lemmas above plus the characterisations
   (`apply_never_not_invertible`, `reject_identity_transform`, `invert_not_invertible`,
   `invert_refuses_as_apply`, `invert_ignores_evaluator`, `invert_insert_reads_nothing`,
   `refused_insert_inverse_is_live`,
   `apply_preserves_wf_ev`, `apply_all_preserves_wf`), over the six operations, any table and any
   evaluator. F\* 2026.09.06, Z3 4.13.3, every query 3/3 under `--quake 3`, `--report_assumes
   error` on, no `assume`, no `admit`. The module is self-contained — it opens nothing, restates
   `outcome` and its list helpers as `Chain.fst` and `JsonParse.fst` do — and the first to need a
   SIGNED integer in the extraction: `Prims.fs` gains an `int` alias beside Phase 160's `nat`, so
   the `row < 0` clause is a modelled refusal rather than a bridge-side convention.
2. **Differentially tested.** The extracted model agrees with `ColumnOps.apply`, `canApply`,
   `invert`, `applyAll` and `toOps` over the pools above, with the blind bridge required to lose.
   Agreement is over those pools, never over all inputs.
3. **Assumed, and stated as such.**
   - **The carrier premise** (`column-cell-carrier-opaque`, a `model-bridge`, permanent). A present
     cell is its type and an opaque carrier compared as a string. Production's structural equality
     on the six value constructors agrees on every finite value and disagrees at a NaN float —
     `Float nan <> Float nan` there, `"NaN" = "NaN"` here — so a table holding one is a table the
     model and production can diff differently at `toOps`'s changed-column test. The generator
     draws no NaN. The model cannot represent IEEE equality without modelling the float, which is
     finding 2's cost and not this theorem's.
   - **The evaluator is abstract** (`column-transform-evaluator-abstract`, a `model-bridge`,
     unscheduled). Every theorem about `ApplyTransform` is about its envelope — replace wholesale
     on `Ok`, `TransformRejected` carrying the evaluator's words on `Error`, nothing built before it
     answers — and the one place the abstraction has content, that `DataFrame.evalPipeline` returns
     well-formed tables, is the hypothesis `ev_preserves_wf` and is discharged nowhere. Modelling the
     evaluator is its own phase and nobody has taken it.
   - **The extractor and the compiler**, inherited from theorem 1's `extractor-and-compiler-trusted`.

## Theorem 10 — default-deny dispatch and the three function laws (Phase 177)

_(This directory's tenth, and the first about the seam an AI-driven edit crosses rather than about
the data it edits. `Fuaran.Core.Function` is the artifact-function protocol under its three laws
and the invocable `Capability` registry with default-deny dispatch and arg-validated invocation.
Its guarantees are the ones an assurance reader asks about first, and until this phase they were
property-tested — `capabilityLaws`, `compositionLaws`, `functionVerifyLaws` — and proved nowhere.
Phase 153 is chartered to prove the WIRE cannot carry an invokable; this proves the SEAM cannot
invoke what was not registered.)_

The doc comments of `Function.fs` state the contract in four places and the theorems are their
names:

> **`Registry.dispatch` — "default-deny — an unregistered id is `NoSuchCapability`".
> `Capability.invoke` — "validate the args, then run the host `body`". `Registry.enumerate` — "the
> discovery surface … what compute may I invoke". The three laws — totality ("bounded iteration
> only"), hygiene ("bound by their absolute lexical address, never a bare name, so composition
> cannot capture"), effect signature ("joined componentwise through composition").**

`Capability.fst` models the file clause for clause — the effect lattice through its two rank tables,
the five-constructor value-space vocabulary and `Space.validate`, `signature` / `signatureExcluding`
/ `isTotal` / `guardTotal` / `validateArg` / `bindArgs` and the `apply` / `curry` that are its two
faces, `compose` / `composedEffect` / `observedEffect` / `auditEffect`, and `Capability.create` /
`validateArgs` / `invoke` with `Registry.register` / `tryFind` / `enumerate` / `dispatch` — over
two **parameters**. The witness (`Holes`, `Effect`, `Bind`, the tree witness's `KindTag`, and
`Tree.preorder` over it) is a record of functions over an abstract node type, exactly as theorem 5
takes `ReplaceChildren` abstractly; and the three scalar readers `Space.validate` reaches for —
`Int32.TryParse`, `Double.TryParse` against a float range, `String.Length` — are a `readers` record,
exactly as theorem 9 takes the pipeline evaluator. Neither is a hole in the argument: nothing any
theorem below says depends on what a domain's `Bind` does or on whether `"12"` is in `[0, 10]`.

### What is proved

Six theorems, over any witness, any readers, any registry and any host body:

1. **`unregistered_refused`** — `dispatch` of an id the registry does not hold is the typed
   `NoSuchCapability id known`, and the result is the SAME under every host body. That second
   clause is the whole content: "runs no handler" is not a thing a pure function can be
   instrumented for, and body-independence is what it means. `invoke` can never raise the
   registry's two refusals, so the classes stay disjoint.
2. **`validate_before_invoke`** — an argument set `validateArgs` rejects makes `invoke` return that
   rejection, for every body alike; at the registry, a resolved id still runs nothing on a set
   validation rejects. The converse holds (`body_runs_only_validated`), the refusal is one of
   validation's own four (`validate_args_shape`), an accepted set addresses declared holes only
   and binds every required one (`validate_args_sound`), and a refusal is truthful — an
   `ArgOutOfSpace` names a value the space really refuses (`refusal_is_truthful`).
3. **`enumerate_is_registry`** — an id is enumerable exactly when `tryFind` resolves it, and
   `dispatch` raises `NoSuchCapability` exactly off the enumeration. `register` refuses a held id
   and extends by exactly one entry otherwise; a registry built by it holds distinct ids.
   Membership, not order: production's `Map` sorts the enumeration and the model holds the map as
   a list, so the id order is `capabilityLaws`'s to certify.
4. **`totality_law`** — `isTotal` over the derived signature answers exactly the guard `apply` and
   `curry` run first; when the guard fires both refuse with `NonTotal` at the first unbounded
   repeat, and the refusal is the same under ANY `Bind` (`rejected_never_bound`) — "rejected,
   never run", stated as bind-independence the way theorem 1 states body-independence.
5. **`hygiene_law`** — hole NAMES are inert: renaming every hole leaves `apply`, `curry` and
   `compose` unchanged, because the walk keys on the absolute address and reads nothing else. An
   argument at an undeclared address is refused by name before any binding
   (`undeclared_address_refused`). And no capture: a single binding at `k` lands at the hole
   declared at `k` and nowhere else (`walk_single`), so binding one of two same-named holes is the
   same on a witness that declares the other and on one that does not (`same_name_no_capture`).
6. **`effect_law`** — `composedEffect` is `Effect.join`, and the join is the least class covering
   both parts (`join_least`): any class assigned to a composition that is below either part fails
   `covers`. Commutative, associative, idempotent, `pureDeterministic` the identity — a bounded
   join-semilattice with `covers` its order, resting on the two rank tables being inverse on the
   ranks they produce. **`audit_effect_join`** carries it to the walk: the observed effect is the
   least class covering every node (`observed_least`), an `Ok` audit says the root covers every
   descendant, and an `Error` audit exhibits a descendant it does not.

### The finding: a slot hole makes a capability un-invocable

`Function.signature` enters a `SlotHole` as `Required = true, Space = None` — required on the data
axis, because `apply` must bind it, and spaceless, because a tree is not a scalar. `validateArgs`
reads the same entries: an argument at a spaceless address is `UninvocableArg`, and a required
address with no argument is `RequiredArgsUnbound`. `slot_hole_uninvocable` states what follows —
every argument list is refused — and `slot_entry_shape` states that `signature` is where such an
entry comes from. So a capability declared over an artifact with a tree-typed slot can be
registered, enumerated and advertised through `toJsonSchema`, and never dispatched.

The contract is not violated: a slot is not scalar-invocable by design, and `validateArgs`'s doc
comment says exactly that of the ARGUMENT. What nothing said is that the entry makes the whole
capability un-invocable, and the differential found it before the model did — the first draft of
the seam case lifted `Reference.template`'s signature straight into `Capability.create`, which is
the natural shape of a host publishing a template as a capability, and the accepted-set assertion
came back `RequiredArgsUnbound ["tpl/s"]`. Asserted on the shipped seam by the differential's fourth
case, and NOT fixed in this phase, per the standing rule that a gap the theorem finds is fixed by
its own phase. The fix shape, if taken: `signature` marking a slot entry non-required on the
invocation axis, as it already does for action holes — or `Capability.create` refusing a spaceless
required entry so the un-invocable capability is refused at registration rather than at every
dispatch. Both change a public function's behaviour.

### The differential

`Proofs.Oracle`'s seam family runs the extracted model beside production with the model's node type
instantiated at the reference domain's `RNode`, so the witness is `Reference.artw` bridged field for
field and a tree crosses untranslated. Over 200 generated artifacts at seed 1770 — up to four
children, each a hole of a random kind (one repeat in five unbounded), a leaf, or a group holding a
hole that REUSES an earlier hole's name, every node with its own effect — and an argument set per
artifact (three holes in four bound, one arg in eight on the wrong axis, one set in six with an
undeclared address): `signature`, `isTotal`, `signatureExcluding`, `apply`, `curry`, `compose`
against a slot chosen to be a real slot hole three draws in four, `composedEffect`,
`observedEffect` and `auditEffect`, compared as verdict, result tree, refusal class AND payload,
and effect class. Measured: applied 73, refused 127; curried 96, refused 104; composed 17, refused
183; 11 non-total artifacts; 92 under-declared roots; all six `ApplyError` classes reached.

Over 150 generated registries at seed 1771 — up to three capabilities from a three-id pool, so a
duplicate registration is drawn one time in four, each signature production's own `signature` of a
generated artifact — and up to four invocations each, one id in four unregistered: `register`,
`enumerate` (sorted on both sides — the one ordering the model does not carry), `tryFind`,
`validateArgs` and `dispatch`, with an INSTRUMENTED body on each side that must have run on both or
on neither, and never past a refusal that is not the body's own. Measured: registered 172, refused
51; dispatched 33, `NoSuchCapability` 270, validated 45, refused 71, `BodyFailed` 12; 341 refusals
checked for a body that did not run; all six reachable `InvokeError` classes reached. The body-ran
count equals dispatched plus body-failed, asserted — which is theorems 1 and 2 as a tally.

The go-red hands the model a BLIND int reader — `Int32.TryParse` that reads nothing — and requires
the differential to lose on the space check, on the seam (`ArgOutOfSpace`) and on the algebra
(`ValueOutOfSpace`), which it does. Seeded and replayable; the same seed reproduces the same tally,
asserted.

### What it cost

The cheapest module in the leg by some distance, and the largest theorem count. Cold runs through
the kit on this machine at the leg's rlimit of 40 under `--quake 3`: **7s** (a `-Runs 1` leg beside
concurrent sessions; the gate's `-Runs 3` is the citable three); the prover
invoked directly on the file with the same flags during authoring, a fresh verification each time,
6–8s. Budget **20s** (the minimum — 2 × 7 is under it) and floor **3s** (half of 6, rounded down),
seeded per Phase 148/164's rules and recorded
in `modules.json`. No `--ext context_pruning`: the module opens nothing. Nothing in it needed a
scoped rlimit; the one lesson worth recording is about CLOSURES rather than cost. The first draft
wrote `List.tryFind (fun h -> h.Addr = addr)` as the F# does, in the model and again in the lemma
about it, and the prover could not connect the two — a closure and a second closure with the same
body are two terms to the encoding, and `check_args_shape` failed on every arm with the context
knowing only `matches Error _`. Every such walk is a NAMED function now (`find_entry`, `find_hole`,
`first_unknown`, `excluding`, `others`, `none_keyed`, `all_declared`, `all_covered`), each naming
the F# lambda it stands for, and the module discharged on the next run. The extraction is
byte-identical to a fresh one on the first leg run; the oracle compiles against `Prims.fs` and the
`option` shim with nothing added.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** The six theorems above plus the characterisations
   (`invoke_never_registry_refusal`, `dispatch_validates_first`, `body_runs_only_validated`,
   `validate_args_shape`, `validate_args_sound`, `refusal_is_truthful`, `no_such_iff_unregistered`,
   `registered_dispatches`, `register_refuses_duplicate`, `register_extends`,
   `register_keeps_distinct`, `rejected_never_bound`, `undeclared_address_refused`,
   `same_name_no_capture`, `signature_excluding_exact`, `data_holes_all_data`, `observed_least`,
   `audit_effect_join`) and the finding (`slot_hole_uninvocable`, `slot_entry_shape`), over any
   witness, any readers, any registry and any host body. F\* 2026.09.06, Z3 4.13.3, every query
   3/3 under `--quake 3`, `--report_assumes error` on, no `assume`, no `admit`. Self-contained: it
   opens nothing and restates `outcome` and its list helpers as `ColumnOps.fst` does.
2. **Differentially tested.** The extracted model agrees with the algebra and the seam over the
   pools above, with the blind reader required to lose. Agreement is over those pools, never over
   all inputs.
3. **Assumed, and stated as such.**
   - **The readers premise** (`capability-scalar-readers-abstract`, a `model-bridge`, permanent).
     The three host functions behind `Space.validate` are parameters; a float range's bounds cross
     as opaque carriers. Every validation theorem is about the envelope — which entry a value is
     checked against, which refusal names it, that the body waits on the answer — and nothing
     about the two numeral grammars, which is theorem 4's cost and not this theorem's.
   - **The witness**, which is the standing `lawful-abstract-witness` obligation and not a second
     row: nothing here says what a domain's `Bind` does, and `compositionLaws` /
     `functionVerifyLaws` are where a domain's `Bind` is sampled.
   - **The extractor and the compiler**, inherited from theorem 1's `extractor-and-compiler-trusted`.

## Theorem 11 — incremental evaluation agrees with full evaluation (Phase 186)

_(This directory's eleventh, and the second about the compute strand: theorem 9 proved the columnar
op algebra, this proves the promise every dashboard and every incremental transform rests on.
`Fuaran.Core.Propagation` derives the dirty set from a change and drives an incremental
re-evaluation over it, and its doc comment says the result is "byte-identical to a full `eval`".
Until this phase that sentence was sampled — `Conformance.dirtyPropagationLaws` (Phase 68) and
`Conformance.propagationEvalLaws` (Phase 69), over acyclic graphs, one changed id and one toy
evaluator — and proved nowhere.)_

The doc comments of `Propagation.fs` state the contract in three places and the theorems are their
names:

> **`dirtyFromChangedIds` — "`changed` ∪ every id transitively downstream of it … Minimal by
> construction — an id not reverse-reachable from any change is never included." `evalFrom` —
> "recompute only the dirty subgraph … reusing each clean node's prior value. Byte-identical to a
> full `eval` over the same inputs … A node absent from `prior` (never evaluated) is always
> recomputed. A `changed` id not in the dependency map is a named `EvalUnknownChange`."**

`Propagation.fst` models the two halves of that clause for clause — `dependents` through the same
pair list and grouping the F# writes, `dirtyFromChangedIds` with its private frontier loop `grow`,
`staleSet`, and the driver: `PropagationError`, `EvalOutcome`, the private `walk` and its loop `go`,
`eval`, and `evalFrom` with its unknown-change guard — over two **parameters**. The node evaluator
is a function over an abstract value type, exactly as theorem 9 takes the pipeline evaluator: Core
owns no evaluator, and nothing here says what a domain computes. And the order the driver walks is
a `topo_result` handed in where production computes `sort deps`: `sort` is Tarjan's algorithm over
mutable dictionaries and a stack, the model does not restate it, and the ONE fact about its output
the agreement theorem turns out to need is a hypothesis, a ladder row and a check the differential
makes on every graph. `dependencyMap`, `cycleThrough`, `touchedBy` and `dirtyFromOp` build a
dependency map or a change set from a tree and are outside the model; the theorems start from both.

One clause is restructured rather than transcribed, and says so where it stands. Production guards
`Map.find id prior` with `recompute id || not (Map.containsKey id prior)`; a partial `find` under a
refinement extracts to an incomplete match, so the model reads the guard AND the lookup as one
total function, `reuse`, and `reuse_is_guard` proves it is `None` exactly when the guard is true.

### What is proved

Over any dependency map, any change set, any value type and any evaluator:

1. **`dirty_sound`** — every id whose input changed is dirty: for any read path
   `c <- n1 <- … <- n` out of a changed `c` (each id reading the one before it), `n` is in
   `dirtyFromChangedIds deps changed`. It is read off `dirty_closed` — the set contains the change
   and is closed under "reads" — through `dependents_edge`, which says the inversion is EXACT: an
   id is among `r`'s dependents exactly when it reads `r`.
2. **`dirty_least`** — the dirty set is the LEAST set that contains the change and is closed under
   "reads": any such set contains all of it. That is "minimal by construction" as a theorem — and
   it is the only sense in which a set can be minimal without naming what it is minimal FOR.
3. **`evalfrom_agrees`** — `evalFrom ev1 prior changed deps` equals `eval ev1 deps`, as a whole
   `Result`: the same values in the same order, the same cyclic groups, and under a failing
   evaluator the same `EvalNodeFailed` at the same node. Four premises, each a sentence the
   production doc comment states in prose: `prior` holds the values `eval ev0` returned over the
   SAME dependency map, or fewer (`prior_of` — a hole is recomputed, so a prior with holes in it is
   admitted, as production admits it); the old evaluator reads other nodes only through its
   DECLARED reads (`local`); `changed` names every id on which `ev1` differs from `ev0`
   (`agree_off`), and every changed id is known; and the walked order holds no id twice.
   `evalfrom_agrees_exact` is the reading with no hole.
4. **`evalfrom_minimal`** — a node outside the dirty set AND present in `prior` is not
   re-evaluated: `evalFrom` returns the same `Result` under any two evaluators that agree on the
   dirty ids and on the ids `prior` does not hold, which is what "is not evaluated" means for a
   pure function without instrumenting one (theorem 10's reading of "runs no handler").
   `invoked_only_stale` is the instrumented reading: every id `walk_invoked` lists — the ids the
   walk hands to the evaluator, in order — is on the walked order and is dirty or absent from
   `prior`.
5. **`evalfrom_unknown_refused`** — a changed id the map does not hold makes `evalFrom` the typed
   `EvalUnknownChange` naming exactly the unknown ids (`unknown_exact`), under every evaluator
   alike, with nothing evaluated.

**`grow` is a total function here, which production's is only by argument.** The F# loop stops
because the accumulator grows inside a finite universe, and nothing checks that. The model's `grow`
carries the measure — the ids of the dependents map not yet accumulated, then the frontier, in
that order — and `grow_measure` discharges it: a round that finds nothing fresh leaves the
accumulator alone and hands on an empty frontier, and a round that finds something strictly shrinks
what is left. It is established BEFORE the function it justifies and re-used by every induction
over the loop.

### The findings: what the theorem needed, and what it did not

**Agreement does NOT need a dependency order.** The proof of `evalfrom_agrees` uses one fact about
the walked order — no id twice — and nothing else: not that a node's reads come before it, not that
cycles were removed. A clean node's reads are clean (the dirty set is closed), so whatever the old
walk resolved for them — a value, or nothing, because the read had not been reached yet — the new
walk resolves the same. Dependency order is what makes the VALUES mean something; it is not what
makes the two paths agree. The one fact it does need is load-bearing, measured by deleting it:
without `distinct` the prover refuses `go_agree` at exactly the step that reads a prior value back
(an id walked twice has its prior written at the first occurrence, where the full walk writes a
value computed from fewer resolved reads). That fact is `propagation-order-distinct`, a bridge and
not a theorem, because `sort` is outside the model.

**The evaluator contract is a premise production states and cannot enforce.** `walk` hands
`evalNode` a resolver over EVERYTHING computed so far — `fun k -> Map.tryFind k results` — not
over the node's declared reads. An evaluator that reads a node it did not declare therefore
type-checks and runs, its node is not downstream of that read in the dirty set, and `evalFrom`
returns its STALE prior value where `eval` returns a fresh one. The differential's third case
exhibits it on the shipped driver: three nodes, `b` reading `a` without declaring it, `a` changed,
and `evalFrom <> eval`. Nothing is wrong with the driver's arithmetic — `local` is simply the
domain's to keep, and until this phase it was written down nowhere but in the phrase "a clean
node's inputs are unchanged". It is `propagation-evaluator-contract`, a `premise`, and the two ways
to narrow it are each their own phase (the phase charter says a gap the theorem finds is one, and
`Propagation.fs` is not changed here): restrict the resolver `walk` hands out to the node's
declared reads, which makes `local` hold by construction; or make the law family generic over a
domain's evaluator, which makes the row dischargeable by a kit run.

**Two statements the phase was filed with were not true as written**, and the true ones are what
is proved. "A node outside the dirty set is not re-evaluated" is false without "and present in
`prior`": an id absent from `prior` is always recomputed, which is production's documented
contract and which the differential reaches on purpose. And the families named as sampling the
promise were `incrementalLaws` and `dirtyPropagationLaws`; `incrementalLaws` is the COLUMNAR
`DataFrame.evalFrom`'s family (Phase 34) and says nothing about this module — the tree-level
driver's is `propagationEvalLaws`.

**The first consumer.** `Fuaran.Core.Propagation` had no consumer outside this repository when the
phase was filed. The first is filed and not yet started: `fuaran#1760`, a production
binding-dependency graph over `dependencyMap` and `dirtyFromChangedIds`. It consumes the dirty-set
half — theorems 1 and 2 here — and an evaluator it supplies to `evalFrom` would owe the contract
above.

### The differential

`Proofs.Oracle`, three cases. The generator is the one the two law families share — node `k` reads
a random subset of the nodes below it, a base value per node, `value(n) = base(n) + Σ value(reads)`
— WIDENED where the families are narrow: one graph in four carries back edges (cycles, so `Cyclic`
is non-empty and an acyclic node reads a cyclic one through a resolver that answers nothing), one
in four carries dangling reads, the change set is one to three ids, one trial in eight names an id
the map does not hold, one changed base in six is the designated FAILING base, and one trial in
five hands `evalFrom` a `prior` with a hole in it. Per trial the two sides are held together on the
dependents map, the dirty set and its alias, the whole `Result` of `eval` before and after the
change, the whole `Result` of `evalFrom`, and the ids `evalFrom` evaluated IN ORDER — production's
own recorder against the model's `walk_invoked`. Every graph also holds production's `sort` to
`propagation-order-distinct`, and every trial whose change is known holds the shipped `evalFrom` to
the shipped `eval`. Measured at seed 1861 over 400 graphs: 54 cyclic, 67 with a dangling read,
1031 dirty nodes and 718 clean, 461 reuses, 27 clean ids recomputed because `prior` lacked them,
354 agreements on the shipped driver, 84 evaluator failures reached on both paths, 46 unknown
changes refused — each with an adequacy guard, and the same seed reproduces the tally.

The go-red hands the model a deps bridge that LOSES a read edge (each node's last read is dropped),
so its dependents map, its dirty set and its reuse are computed over a smaller graph; it is
required to lose on the dirty set and on `evalFrom`'s values. During authoring the extracted oracle
itself was perturbed — `grow` cut to one round, so no transitive closure — and the first case went
red on trial 2 before the fresh extraction was restored and went green.

### What it cost

Cold runs through the kit on this machine at the leg's rlimit of 40 under `--quake 3`, with no
other prover running: **4s, 4s, 4s**. Budget **20s** (the minimum — 2 × 4 is under it) and floor
**0** (`floorSeeding.zeroBelowSeconds`: the fastest genuine cold run is under 5s, so half of it is
inside process-start noise), seeded per Phase 148/164's rules and recorded in `modules.json`. No
`--ext context_pruning`: the module opens nothing. No scoped rlimit, no SMT pattern, and no query
needed a second attempt. Three authoring notes, none about cost. The set helpers (`union`, `diff`,
`app`, `dedup`) rewrite into one another and their lemmas carry NO pattern, on the lesson recorded
under *Running it* — each is called by name where it is needed. Every closure the F# writes twice is a NAMED
function (`always`, `in_set`, `resolve_in`), on theorem 10's. And the quantified premises (`local`,
`agree_off`, `agree_on_stale`) each have a one-line elimination lemma, so an induction instantiates
them at the two resolvers it means rather than leaving the choice to the solver. The extraction is
byte-identical to a fresh one on the first leg run; the oracle compiles against `Prims.fs` and the
`option` shim with nothing added.

### The claims ladder, for this theorem

1. **Proved (machine-checked, no admits).** The five theorems above plus the characterisations
   (`dependents_edge`, `dirty_closed`, `grow_measure`, `reuse_is_guard`, `evalfrom_agrees_exact`,
   `invoked_only_stale`, `unknown_exact`), over any dependency map, any change set, any value type
   and any evaluator. F\* 2026.09.06, Z3 4.13.3, every query 3/3 under `--quake 3`,
   `--report_assumes error` on, no `assume`, no `admit`. Self-contained: it opens nothing and
   restates `outcome` and its list helpers as `ColumnOps.fst` and `Capability.fst` do.
2. **Differentially tested.** The extracted model agrees with the dirty set and the driver over the
   pools above, with the blind bridge required to lose. Agreement is over those pools, never over
   all inputs.
3. **Assumed, and stated as such.**
   - **The order bridge** (`propagation-order-distinct`, a `model-bridge`, unscheduled). `sort` is
     a parameter; that its `Order` holds no id twice is a hypothesis, sampled on every generated
     graph and proved nowhere. Closable — a functional model of Tarjan's algorithm with its
     emitted components proved disjoint — and not taken.
   - **The evaluator contract** (`propagation-evaluator-contract`, a `premise`). Declared reads
     only, a complete change set, and a `prior` that is `eval`'s own output over the same map. The
     domain's, enforced nowhere, and discharged by no run: the shipped law family runs a toy
     evaluator, never a domain's — the `witness-surface-scope` precedent.
   - **Sets and maps are lists**, the standing `sets-are-lists` bridge and not a second row: every
     theorem about a set here is about membership, and the values map is compared as a map.
   - **The extractor and the compiler**, inherited from theorem 1's `extractor-and-compiler-trusted`.

## Next

**A resolver that resolves only declared reads** — theorem 11's finding, and the one item on this
list that would turn a `premise` into a property. `Propagation.walk` hands `evalNode` a resolver
over everything computed so far; restricted to the node's declared reads, an evaluator COULD NOT
read an undeclared node, `local` would hold by construction, and `propagation-evaluator-contract`
would shrink to its other two clauses. It changes what a non-conforming evaluator sees (`None`
where it saw a value), so it is a behaviour change with its own phase, not a repair made here. The
alternative that leaves the driver alone — a law family generic over a DOMAIN'S evaluator, so the
row becomes a `domain-obligation` a kit run discharges — is the same size and is not taken either.

**`sort` inside the model** — theorem 11's one bridge (`propagation-order-distinct`). A functional
model of Tarjan's algorithm with its emitted components proved pairwise disjoint would make "`Order`
holds no id twice" a theorem; the agreement theorem needs nothing else of `sort`, so that is the
whole of what closing the bridge has to prove.

**§15.4's optional-field row, raised to the specification's owners** — theorem 8's finding, and the
one item on this list that is not work for this repository. The row says an added optional field is
additive, and it is; what the theorem cannot support is the row's stated REASON. `classify` decides
by the kind-tag delta, an optional field moves no tag, and the verdict is the no-op `Additive []` —
so the row is satisfied vacuously and would stay satisfied if the field had been added as REQUIRED,
which `Diff.classifyFieldAdd` separately and correctly calls breaking for emitters. The two
classifiers see different things and neither is wrong; what is missing is a sentence in §15.4 saying
which decides the profile bump. Nothing here should be changed to close it: widening
`Versioning.classify` would model a function this repository does not ship, and the differential
already asserts the vacuity, so the day the answer changes this goes red rather than stale.

**A guarded `Canon.tryRender`** — theorem 7's finding, and the smallest item on this list. `Json`
has the pair: `render` formats a non-finite float into a token that is not valid JSON, and
`tryRender` names it as a typed `Error` instead. `Canon` has only the unguarded half, and its
failure mode is worse rather than better — a non-finite float does not produce un-parseable wire,
it produces a `"NaN"` STRING, so the digest over `JFloat nan` equals the digest over the value a
reader decodes those bytes back to. `render_aliases_nan` is that sentence proved, and the
`Proofs.Oracle` alias case is it asserted on production. What it needs is a refusal-class addition
in the shape Phase 137 took — a guarded entry point beside the existing one, not a change to what
`render` does, since the bytes are pinned by the corpus and by four other hosts. The theorem's
canonical subset is already the predicate such a guard would enforce, which is why this is small:
`float_canonical`'s two clauses ARE the refusal, and the second of them (an integer-shaped float
token) is a design choice the guard should NOT refuse, so the guard is the first clause alone.

**The diff's RECONSTRUCTION, at level 1** — theorem 6's stated boundary, and the item here with the
shortest informal argument and the longest mechanisation. **This entry has been corrected by Phase
162 and the correction is the useful part of it.** It used to say the item needed one positional
lemma — a parent precedes its children in a preorder walk, so by the time the second pass emits
`MoveNode(c, p)` while processing `p`, every after-ancestor of `p` has already been placed and `p`
cannot be inside `c`'s subtree. That lemma is now proved (`TreeOps.preorder_parent_first`, section
19) and so is its instantiation at the diff (`TreeDiff` section 9, including the conclusion about
`c`'s subtree), and reconstruction is still open — because the fact holds of `after`, and `apply`
validates each step against the INTERMEDIATE tree, the before-tree with the script's earlier
operations run on it. What remains is an induction carrying an invariant about how much of `after`'s
structure each PREFIX of the script has built; everything else follows from the four block
characterisations theorem 6 already proves, plus the three positional facts. The cost is unchanged
and is the honest price rather than a reason not to pay it: the induction is about `ins` and
`rem_at` over intermediate trees, `Preservation.fst`'s cost class rather than `TreeDiff.fst`'s, so
the module that checks in twelve seconds today would not afterwards. `Diff.toOpsMoved`
(fuaran-core#63) travels beside it: a second emission strategy over the same two trees, which would
want the same invariant.

_(**The batch lift** — "the three `Batch` pair shapes `TreeOps.covered` names" — was an item here
and is DONE: Phase 162, section 20 of `TreeOps.fst`, with `covered` deleted and `Skeleton.fst`
restated over the whole `SkeletonOp` alphabet. Theorem 2's "What was left open" section carries
what it was and how it closed.)_

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
