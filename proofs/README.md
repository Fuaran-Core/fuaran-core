# proofs/ — the F\* models of Fuaran.Core's kernel algebras

**Status: GO** (Phase 131, 2026-09-12). Phase 131's three exit criteria were met and remain met — the
confluence proof is reproducible on the pinned prover, the extracted model agrees with production over every
lane set the differential host draws, and the model reads beside the F# in one sitting ("Exit criteria, with
evidence" below). What each model proves, and what it does not, is stated once: in `../proofs.json`'s rows,
each with its `statement` and its `boundary`. "The models" below is their render, one section per model,
written by `render proofs-readme` and held by the `Proofs.Ladder` family (Phase 533). The count is not kept
by hand either: the line below is generated from the ladder.

<!-- ladder-summary:begin — rendered from ../proofs.json by `render proofs-readme`; held by the Proofs.Ladder family -->
**The ladder, counted:** 386 claims — 281 proved across 37 models, 63 tested, 40 assumed (9 `domain-obligation`, 25 `model-bridge`, 6 `premise`), 2 policy.
<!-- ladder-summary:end -->

**No schedule is kept for the `model-bridge` rows (Phase 400; Phase 537).** A bridge is `permanent` or
`unscheduled`, and the ladder refuses any other `closes`, a phase included; the count above is therefore a
statement about what is proved today, not about what is planned. The reason is a boundary, not a gap: this
repository's roadmap lives outside it and cannot be read from here, so a list of open phases kept in this
tree would be a copy that nothing could hold to its source. Phase 537 deleted the inert list that stood in
for one, and the clause half that read it; a scheduled `closes` returns with a writer for that list, not
before.

This directory is the mechanised half of the correctness story whose differential half already
existed: Phase 80 certified two-script confluence, Phase 83 the two-head `Dag.reconcile`, Phase 100
the N-lane fold-confluence pack. Those are property tests over sampled orders; this is the same law
as a theorem, and the theorem's model run as a sixth host through the same differential test.

| File | What it is |
|---|---|
| `*.fst` | The models, one per kernel algebra, and the shared `Twin.fst` and `ListAlgebra.fst` they open. `modules.json` lists every one in the order the leg checks it; "The models" below renders each model's description, its claims and the rows it rests on. |
| `oracle/*.fs` | **Generated**, except `Prims.fs` and `FStar_Pervasives_Native.fs` — the extracted models, emitted to F# by F\*'s own code generator and diffed byte for byte against a fresh extraction on every run. The suite runs them beside production. |
| `Vocabulary.fst`, `DocVocabulary.fst`, `ScoreVocabulary.fst`, their `…Proofs.fst` and `VocabularyVectors.fst` | **Generated** the other way round: not extracted FROM a model but emitted AS one, by `Fuaran.Core.Idl.Codegen`'s F\* target (Phase 150) from the vocabularies the engine is certified on (the fixtures library's `ReferenceIdl.fs`, `SecondDomainIdl.fs`, `ScoreDomainIdl.fs`). Held to a fresh generation by the `Proofs.Vocabulary` family; checked, not extracted. |
| `oracle/Prims.fs` | The `Prims` names the F# backend emits and the release does not ship. |
| `oracle/Fuaran.Core.Proofs.Oracle.fsproj` | The oracle assembly. Never packed; nothing extracted enters the shipped kernel. |
| `fstar-pin.json` | The pinned F\* release (which bundles Z3) and its hash. |
| `check.ps1` | The proof leg: check with the pin, re-extract and diff against the committed oracle, run the host. |
| `modules.json` | What the leg COSTS: one entry per checked module — its `budgetSeconds`, the measurement that budget was seeded from, and the phase that set it. `check.ps1` reads it and prints measured-against-budget on every green line. See "What the leg costs" below. |
| `../tests/Fuaran.Core.Tests/ProofOracle.*Tests.fs` | The differential host (`Proofs.Oracle`), one file per model over the shared bridges in `ProofOracle.Bridges.fs`; `ProofOracle.DagFoldTests.fs` holds the family that measures the fold theorem's one domain hypothesis on the reference witness. |

## The claims ladder

Every claim this directory makes is a row of `../proofs.json`, at one of four levels. The set of levels is
closed, because a ladder that can grow a level can hide a demotion; `kit/LADDER.md` specifies the schema.

1. **Proved** — machine-checked on the model by the pinned prover (`fstar-pin.json`), with no `admit` and no
   `assume` (`--report_assumes error`). The model's correspondence to the shipped F# is not part of the proof:
   it is the extraction and the differential run beside it.
2. **Tested** — sampled agreement: an extracted model run beside production by `Proofs.Oracle`, or a law
   family over generated inputs. Agreement is over the pools drawn, never over all inputs.
3. **Assumed** — stated, and classed in the contract table below: a `domain-obligation` a domain discharges,
   a `model-bridge` between a model and the code, or a `premise` nothing discharges.
4. **Policy** — a documented choice, which no theorem addresses.

"Formally verified" is spent on level 1 alone. A row says what it claims in `statement` and what it does not
cover in `boundary`, and a finding about a model is a row, or the boundary of the row it bounds. The rows,
grouped by model, are "The models" below.

## The Core-to-domain proof contract

Every ladder in this directory ends at level 3 — **assumed, and stated as such** — and until
Phase 174 that was the whole of what such a row said. For a domain instantiating this substrate it
is not enough, because the 40 assumed rows across these ladders are three different kinds of thing
and a domain can act on exactly one of them. `../proofs.json` therefore carries a **`class`** on
every `assumed` row, from a closed set of three, and this section is that classification in one
place together with the contract it implies.

- **`domain-obligation` — what YOU owe, and what a green kit run discharges.** The hypotheses
  the theorems carry about the domain they are generic over. Each row names a **`dischargedBy`**
  law: a function of the shipped `Fuaran.Core.Conformance` kit whose green run **at your own
  witness** is the discharge. The discharge is SAMPLED and never a proof — the kit draws a
  seed-replayable sample, and "sampled, never proved" is what level 3 means here. 9 rows.
- **`model-bridge` — what THIS repository's model has not bridged, and what you inherit whether
  you run anything or not.** Gaps between the F\* model and the F# that ships: a numeric carrier
  the extraction cannot represent, a host-side mapping that is one line per case and is not itself
  proved, a walk order the model is handed rather than derives, an abstract reader the model is
  handed rather than models. Nothing a domain does closes one. Each names a **`closes`**:
  `permanent` where nothing could close it, and `unscheduled` where something could and nobody has
  (no schedule is kept here, so a phase is not a value). 25 rows.
- **`premise` — what nobody discharges, ever.** The trusted base: a hash that does not collide,
  an extractor and a compiler that are correct, a witness surface that reports every node it holds.
  No kit run touches these and no phase closes them; they are what the rest of the ladder stands
  on. 6 rows.

**The contract line, stated once:** a domain running the conformance kit discharges the first class
and **can never discharge the other two**. A green kit run is evidence about your witness and about
nothing else in this table — which is what makes it worth having, and what a reader must not
over-read.

**One obligation is checked against your own word, and says so (Phase 309).** A `domain-obligation`
row may carry `"discharge": "domain-declared"`: its law checks your DECLARATION and cannot check
past it, so a domain that declares nothing passes it. `witness-surface-scope` is that row —
`keyedApplyLaws` holds the engine to what `KeyedChildren` reports, and a domain with keyed positions
that reports none is green by declaration. The table marks it "domain-declared, not discharged",
and the coverage line counts it apart from the discharged ones. And the rows, the counts above and
the table below are GENERATED from `../proofs.json` (the tool's `render proofs-readme` writes them and
the `Proofs.Ladder` family holds them); edit the ladder, never these.

| Row | Class | Discharged by / closes |
|---|---|---|
| `independence-diamond` | `domain-obligation` | `Conformance.footprintLawsAt` |
| `dag-outside-the-model` | `model-bridge` | `unscheduled` |
| `extractor-and-compiler-trusted` | `premise` | — |
| `sets-are-lists` | `model-bridge` | `permanent` |
| `node-ids-distinct` | `premise` | — |
| `tree-algebra-well-formed-states` | `domain-obligation` | `Conformance.opAlgebra` |
| `content-id-determines-content` | `premise` | — |
| `chain-walk-order-is-productions` | `model-bridge` | `permanent` |
| `signature-binds-one-head` | `model-bridge` | `unscheduled` |
| `parser-float-readback-opaque` | `model-bridge` | `permanent` |
| `parser-alphabet-bridge` | `model-bridge` | `permanent` |
| `lawful-abstract-witness` | `domain-obligation` | `Conformance.witnessLaws` |
| `witness-surface-scope` | `domain-obligation` | `Conformance.keyedApplyLawsAt` (domain-declared, not discharged) |
| `canon-numeral-layouts` | `model-bridge` | `permanent` |
| `canon-key-comparator` | `model-bridge` | `permanent` |
| `canon-character-bridge` | `model-bridge` | `permanent` |
| `capability-scalar-readers-abstract` | `model-bridge` | `permanent` |
| `capability-key-renderers-abstract` | `model-bridge` | `permanent` |
| `propagation-change-set-and-prior` | `domain-obligation` | `Conformance.propagationEvaluatorLawsAt` |
| `propagation-prior-blind` | `domain-obligation` | `Conformance.propagationPriorLawsAt` |
| `propagation-read-witness` | `model-bridge` | `permanent` |
| `query-renderers-abstract` | `model-bridge` | `permanent` |
| `utf8-model-is-the-encoder` | `model-bridge` | `permanent` |
| `canon-unit-spelling` | `model-bridge` | `permanent` |
| `column-int-layouts` | `model-bridge` | `permanent` |
| `column-codec-abstractions` | `model-bridge` | `permanent` |
| `capability-body-total` | `premise` | — |
| `capability-sections-parameters-abstract` | `model-bridge` | `permanent` |
| `capability-pipeline-body-total` | `premise` | — |
| `query-codec-column-premises` | `model-bridge` | `permanent` |
| `propagation-ops-model-bridge` | `model-bridge` | `unscheduled` |
| `propagation-evaluator-total` | `premise` | — |
| `write-gate-model-bridge` | `model-bridge` | `unscheduled` |
| `column-vector-is-its-contents` | `model-bridge` | `permanent` |
| `decode-layer-round-trip-premises` | `domain-obligation` | `Conformance.codecLawsAt` |
| `decode-layer-refusal-premises` | `domain-obligation` | `Conformance.codecLawsAt` |
| `decode-layer-model-bridge` | `model-bridge` | `permanent` |
| `index-node-is-its-record` | `model-bridge` | `permanent` |
| `op-stream-load-premises` | `domain-obligation` | `Conformance.streamLaws` |
| `op-stream-load-model-bridge` | `model-bridge` | `permanent` |

**Why `unscheduled` is a value rather than a rounding to `permanent`.** Three of the bridges can be
closed and nobody has taken the work, and recording them as `permanent` would assert the opposite
of what this document already says. `dag-outside-the-model` has been narrowed twice already — by
Phase 134, from "all of it", and by Phase 158, which took `Dag.mergeBase` out of it for every shape
but a criss-cross merge — Phase 437 took the criss-cross, and Phase 457 the merge base as
production computes it, `commonBase`, the index's queries and `cut`. A closed set of two values would have forced those into a claim of
impossibility, which is a worse error than a third token.

**And the token has now paid for itself: `parser-null-absorption` was `unscheduled` and is
`proved` (Phase 190).** It was this paragraph's first example, on the strength of the parser model's own
"relating two models is its own phase and was not taken. The assumption stands where it is" — a
sentence that names work rather than an impossibility, which is exactly the distinction the third
token exists to carry. Phase 190 took the work, and the row left this table rather than being
reworded inside it. A `permanent` there would have told a reader not to look.

**And a `permanent` DID tell a reader not to look: `evolution-table-coverage` is `tested` now
(Phase 200).** It carried `closes: permanent` — §15.4's optional-field row is satisfied vacuously,
because `classify` ranges over kind tags and a field addition moves no tag — and the argument for
the token was that widening `classify` to see field sets would model a function this repository
does not ship. The first half was right and the second was wrong, in a way a reader who trusted
the token would never have checked: `Versioning.classify` does not range over kind tags at all. It
takes two SUBJECT SETS, and `Diff.evolution` is the shipped caller that builds them from an IDL
diff by partitioning each row on the severity `Diff.classifyFieldAdd` reads off its optionality
class, with `Diff.bumpProfile` carrying the verdict to a published profile. Modelling that needed
no widening of anything. Two lessons, and the second is the one worth carrying: a `permanent` is a
claim about what is POSSIBLE and is therefore the most expensive token in this table to get wrong
— `unscheduled` invites a reader to look, `permanent` tells them not to. The argument for one
should name the obstruction concretely enough to be refuted, which this one did, which is how it
was.

**Why `witness-surface-scope` stopped being a `premise` (Phase 189).** It was the one row whose
obligation was the domain's and whose discharge no run could perform: a node a domain holds in a
keyed, non-structural position is invisible to `Tree.ids`, to every theorem in this directory and
to every law in the kit, so there was no green run to cite and a `dischargedBy` would have named a
run that does not look. That argument was about what the kit could SEE, and it stopped holding the
moment a domain could DECLARE the positions it holds. `Conformance.keyedChildrenLaws` takes
`KeyedWitness.HasKeyedChildren` as that declaration and certifies the domain's own full-walk id
check against it — refusing an id held in a keyed position that the witness surface also holds, and
one held in two keyed positions, both BUILT rather than drawn — so the row is now an obligation with
a law like any other.

**What did NOT change then was the boundary itself.** The witness surface was unwidened: `Ops`
still saw exactly what `Children` reports. What was certified was the DOMAIN'S check, at the
domain's own witness — which is what the row always said the obligation was. A domain that declares
no keyed position runs the family and is told, in the adequacy line, that its report is vacuous by
declaration; that is a different thing from a green run, and the report says which one it is.

**What Phase 286 changed, and why the row moved to `Conformance.keyedApplyLaws`.** The declaration
now reaches the engine. `KeyedWitness` carries the keyed NODES (`KeyedChildren`, with an
arity-preserving `ReplaceKeyedChildren`), `Tree.traversal` walks them, and `Ops.applyContainedKeyed`
refuses a `DuplicateId` over the keyed walk — proved in `Preservation.fst` section 12
(`keyed_apply_preserves_wf`), which shows the keyed insert is the unkeyed insert over the traversal
and transfers `apply_preserves_wf` rather than re-proving it. `Children` is still what the engine
rebuilds through: keyed positions are located and read, never appended to, vacated or reordered.
So the uniqueness is Core's refusal given the declaration, and what the domain still owes is the
DECLARATION — that it reports every keyed position the domain's own walk sees. That is exactly what
`keyedApplyLaws`' agreement law measures, over collisions built through `PlaceKeyedChild`, which is
why the row's discharge moved there. The unkeyed forms keep the Phase 189 boundary unchanged.

**Why `propagation-change-set-and-prior` stopped being a `premise` (Phase 211).** The same move,
one row further on. What the [`Propagation`](#propagation) model still assumes after Phase 209 is about the EVALUATOR — that a
change set names every node an edit moved, in its results and in its reads, and that `prior` is
`eval`'s own output over the same map — and the evaluator is the model's parameter, so no kit law
ran one and there was no green run to cite. `Conformance.propagationEvaluatorLaws` runs a DOMAIN'S:
it takes an `EvaluatorWitness` (the evaluator, its dependency map, the domain's model generator and
edits, each edit with the change set the domain names) and certifies at that evaluator that it is
pure and deterministic, that its change sets are honest, and that `evalFrom` over the priors the
theorem admits agrees with `eval`. Purity is the obligation a resolver restriction can never reach,
which is why Phase 209's fix and this law are complements and not alternatives. The `prior`
clause is carried by construction — the law builds priors the one way the theorem admits — and the
half no law can see stays yours in words: **a `prior` persisted across an edit that moves the
dependency map is re-primed with `eval`, never replayed.** The family's adequacy witness is an
in-repo formula sheet, because no adopter evaluator existed when it shipped; the go-reds are an
impure evaluator, a change set naming the wrong cell, and an edit that moves only what an unnamed
cell asks for.

**This table is CHECKED against `../proofs.json`, row for row** — same rows, same order, same
class, same third column — by the `contract-agrees` clause of the `Proofs.Ladder` family in
`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`, with its own go-red fixtures. It is the ONE
piece of prose in this directory that the ladder family parses; everything else here stays a human
act, per that file's own note. A domain reads this table and a tool reads that file, and two of
them disagreeing is worse than either alone.

**And since Phase 203 the four COUNTS in the sentences above are checked too** — the total in the
opening paragraph and the "N rows." each bullet ends on, by the `contract-counts` clause of the
`Proofs.Coverage` family. Phase 187 found them already stale and nothing looked at them: the table
beside them was pinned and the sentences about it were not, which is the worst of the two
arrangements, because a reader who sees a checked table has no reason to doubt the paragraph above
it. If a rewording moves a count out of that clause's reach, the clause FAILS rather than passes —
a count it cannot locate is a count it cannot check, and a gate that quietly stopped looking is
what this whole section exists to make impossible.

## Coverage is total or declared — the stopping rule (Phase 203)

This directory's claim is that Fuaran-Core is "exhaustively proved in the sense a parametric spine
admits". Until Phase 203 that was a sentence. Nothing computed it, so the only way to ask the
question was to survey the tree, and every survey found another packable package with no model and
no record of whether it needed one — and filed a phase. Thirty-three theorem phases were open the
day that phase was written. A claim nobody can evaluate is a claim that can only grow.

So the sentence is a PREDICATE now, checked in every lane by the `Proofs.Coverage` family
(`../tests/Fuaran.Core.Tests/ProofCoverageTests.fs`, wired into `check.ps1`'s host step beside the
other three). It reads JSON this directory already keeps and runs no prover, and it emits one line
whatever the verdict:

```
proofs: exhaustive (11 packages modelled, 6 excluded; 27 assumed: 18 permanent, 5 domain-discharged, 1 domain-declared, 3 unscheduled)
```

Three clauses stand behind it.

1. **Every packable package is named by a model, or carries a declared exclusion.** The roster is
   Phase 199's derivation, called rather than restated. `modules.json` gains a `packages`
   member per model saying which package's production code it is about — that file is the only
   place the model roster is enumerated, so it is the only place the join can live without
   inventing a second roster to drift against. `coverage-exclusions.json` carries the rest. There
   is no third state. The check runs BOTH ways, on the Phase 185 precedent: an exclusion
   naming a package that is no longer packable, or one that has SINCE gained a model, fails too.
2. **Every `assumed` row is accounted for by its class.** See the reading recorded below.
3. **Every differential is paired to a theorem.** A `tested` row on `Proofs.Oracle` names the model
   it ran beside (`evidence.model`) and that model must carry a `proved` row. A `tested` row on a
   `Conformance.<law>` family is a LAW row, carries no model, and is checked not to.

**What this is NOT is more theorems.** When that line is green the side stops filing theorem phases
on its own initiative. A new one arrives when a consumer's failure or an operator's decision
deletes an exclusion — at which point the gate names the gap, and keeps naming it, until the model
exists. That is the whole mechanism: the exclusions are not a way of avoiding proofs, they are the
list of proofs nobody has asked for, written down where deleting a line is how you ask.

### The six exclusions, and the two reasons that were not carried

The six packages with no model are `Fuaran.Core.Idl.Cli` (**`facade`** — a surface over modelled
packages, whose every claim is its callee's restated in a second syntax);
`Fuaran.Core.Conformance`, `Fuaran.Core.Validator` and `Fuaran.Core.Observer`
(**`content-free-seam`** — generic seams whose content is supplied entirely by the domain, so there
is no concrete computation here for a theorem to be about); and `Fuaran.Core.Projection` and
`Fuaran.Core.AiSurface`
(**`law-tested-by-design`** — real computation, theorem declined in favour of a named `Conformance`
family, which the entry cites and the gate holds to the shipped roster). Each entry's own prose is
in `coverage-exclusions.json`; it is the decision, and the token is only its kind. There were nine
until Phase 154: `Fuaran.Core.DataFrame`'s entry retired the day `Pipeline.fst` gave the package a
model, because clause 1's second direction fails an exclusion for a package that has since gained
one — and what that entry declined (the verbs' semantics, left to `incrementalLaws` and its
siblings) is still declined for the twelve verbs that evaluate no expression, as the model's own
`pipeline-step-evaluator-abstract` row (Phase 234 made the other two, `Filter` and `Derive`, and the
expression evaluator under them, clauses; the cell primitives are the `pipeline-cell-primitives-abstract`
row). The file keeps the retirement under `$retired` so the next reader finds the decision where the
entry was. Phase 258 then moved the package itself, with both compute models and their rows, to
[`Fuaran-Core/fuaran-core-compute`](https://github.com/Fuaran-Core/fuaran-core-compute) (DECISIONS.md D66), and with it the two
exclusions Phase 257 had filed for `Fuaran.Core.DataFrame.CSharp` and
`Fuaran.Core.DataFrame.Conformance`: the eight left were this repository's whole list again. Phase 231
then removed `Fuaran.Core.CSharp`, the other `facade` entry, with the package itself (DECISIONS.md D28),
which left seven. Phase 306 retired `Fuaran.Core.Column`'s — the entry said an operator who wanted the
codec proved would delete it, and `WireColumn.fst` is that theorem ([`WireColumn`](#wirecolumn) below) — which leaves the six
above. What the entry cited is unchanged: `Column.aggregate` is still held by its law family.

Phase 203 was filed naming four reasons and this file carries three, which is a correction and is
recorded rather than quietly absorbed. **`tooling`** was written for `Fuaran.Core.Idl.Codegen`'s
emitter "beyond the evolution table", and by the time the list was cut that package HAD a model:
the six generated-vocabulary modules are its F\* target's own output, and `check.ps1`'s header says
in as many words that what the leg proves over them is the F\* BACKEND. A `tooling` entry for it
would have been stale on arrival — which is exactly what clause 1's second direction fails, so the
gate would have said so on the first run. **`leaving-core`** was written for a `Lease` package that
had already left. A closed vocabulary term no entry carries is a term nothing tests, and the family
asserts that no such term exists, so both were dropped and **`content-free-seam`** was added for
the three packages whose content is the domain's.

### Clause 2 is about the class, not about a `closes` on every row

The phase as filed asked for `closes` on every `assumed` row, including the nine that carry none.
That reading collides with Phase 174, which is shipped and enforced: a `domain-obligation`
carries `dischargedBy` and **no** `closes`, a `premise` carries neither, and the `Proofs.Ladder`
family has go-red fixtures for both misplacements. Implementing the phase literally would have
meant deleting two shipped guards and rewriting the third column of the contract table above, to
say in a new field what each row's class already says.

So the clause is about the ACCOUNT rather than the field. Every `assumed` row is accounted for by
its class: a `premise` is closed by nothing and that is what the class means; a
`domain-obligation` is discharged at a domain's own witness by the law it names; a `model-bridge`
names `closes`, `permanent` or `unscheduled`. The predicate's own line reports
the four buckets separately for the reason the contract section argues at length above — rounding
`unscheduled` into `permanent` asserts an impossibility, and `permanent` is the most expensive
token in this directory to get wrong.

**There is no scheduled half (Phase 537).** A `closes` naming a phase would be a claim about this
repository's roadmap, which is not in this repository and cannot be, so nothing here could check it.
The clause once held such a claim to a committed list of open phases, declared inert because no
writer filled it, and failed any scheduling claim while it was; no row ever made one. Phase 537
deleted the list, its reader and the arm, and a phase-form `closes` is now outside the closed set,
refused by the ladder and by this clause alike. The scheduled half returns with a writer for the list.

### Clause 4 — every public operation is mapped (Phase 335)

Clause 1 stops at PACKAGE granularity: a package with a model is covered, so an operation inside
it could ship with no proved row, no law family and no recorded reason, and nothing said so. The
operation clause reads the committed API baselines (`../api/*.txt`) as its census, every `method`
line, and holds each to at least one of:

- a `proved` or `tested` row in `../proofs.json` whose `evidence.operations` names it;
- a law family whose record (`Fuaran.Core.Families.LawFamily.Operations`, shipped with the kit)
  lists it, a family's own entry point being mapped by its roster row; or
- an entry in `coverage-exclusions.json`'s `operations` block, with a one-line reason and a class
  from that file's closed `operationClasses` vocabulary: `trivial`, `forward`, `obsolete`,
  `host-seam`, `measured-elsewhere`.

An operation is spelled `<Owner>.<member>`, with the `Fuaran.Core.` prefix, generic arity and the
compiler's `Module` suffix dropped (`Dag+ReachModule.ancestors` is `Dag.Reach.ancestors`). An
unmapped operation reds the family and names itself, and because the census IS the baselines, a
newly published operation reds at the commit that publishes it. The check runs both ways: an entry
for an operation that is no longer published, or that has since gained a ladder row or a roster
line, fails too. Each class is held to what it says as far as a file can be — a `forward` names a
target that is itself mapped, an `obsolete` entry's member carries `System.Obsolete` (read by
reflection), and a `measured-elsewhere` entry names a test file under `tests/` that mentions the
member, or the measured operation it is reached `through`. The family prints one more line beside
the package predicate, saying which verdict it computed.

**What `measured-elsewhere` admits is the weakest thing in this directory, and it says so.** It
records that no theorem and no law family states the operation's contract and that example tests
stand in. It is not a pass on the operation's design; it is the inventory of where the next law
family would earn its keep. A law family that DRIVES an operation as harness (hashing a fixture,
walking a tree to draw a node) does not list it on its roster: the roster says what a green run is
evidence FOR. And a roster line is credit for what the family SAMPLES: since Phase 349 the kit's op
generator (`LawKit.genBatch`) builds a `Batch` of one to three ops any of which may itself be a
`Batch`, to two levels below the outermost, and every family that draws ops demands a nested batch in
its op-kind guard — a sample that never nested is starved by name — so the apply operations those
families credit are credited over nested batches as well as flat ones.

<!-- operation-coverage:begin — generated from ../api/*.txt, ../proofs.json, the kit's operation roster and coverage-exclusions.json by `render proofs-readme`; held by the Proofs.Ladder family -->
| Package | Operations | Ladder | Law family | trivial | forward | obsolete | host-seam | measured-elsewhere |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `Fuaran.Core.AiSurface` | 26 | 0 | 14 | 1 | 0 | 0 | 0 | 11 |
| `Fuaran.Core.Column` | 124 | 47 | 51 | 3 | 0 | 0 | 0 | 23 |
| `Fuaran.Core.Conformance` | 164 | 2 | 106 | 2 | 6 | 0 | 0 | 48 |
| `Fuaran.Core.ContentAddress` | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 2 |
| `Fuaran.Core.Function` | 175 | 42 | 80 | 5 | 0 | 0 | 0 | 48 |
| `Fuaran.Core.Idl` | 71 | 0 | 6 | 1 | 1 | 0 | 0 | 63 |
| `Fuaran.Core.Idl.Cli` | 1 | 0 | 0 | 0 | 0 | 0 | 1 | 0 |
| `Fuaran.Core.Idl.Codegen` | 49 | 8 | 0 | 0 | 0 | 0 | 0 | 41 |
| `Fuaran.Core.Observer` | 12 | 0 | 7 | 1 | 0 | 0 | 0 | 4 |
| `Fuaran.Core.OpStream` | 125 | 28 | 39 | 3 | 2 | 0 | 2 | 51 |
| `Fuaran.Core.OpStream.Dag` | 87 | 38 | 26 | 0 | 0 | 0 | 0 | 23 |
| `Fuaran.Core.Ops` | 56 | 21 | 12 | 14 | 0 | 0 | 0 | 9 |
| `Fuaran.Core.Projection` | 13 | 0 | 8 | 0 | 0 | 0 | 0 | 5 |
| `Fuaran.Core.Propagation` | 27 | 8 | 5 | 0 | 0 | 0 | 0 | 14 |
| `Fuaran.Core.Query` | 57 | 23 | 19 | 0 | 0 | 0 | 0 | 15 |
| `Fuaran.Core.Relation` | 11 | 11 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Fuaran.Core.Text` | 30 | 5 | 2 | 1 | 2 | 0 | 0 | 20 |
| `Fuaran.Core.Tree` | 48 | 12 | 8 | 0 | 0 | 0 | 0 | 28 |
| `Fuaran.Core.Unit` | 9 | 8 | 0 | 1 | 0 | 0 | 0 | 0 |
| `Fuaran.Core.Validator` | 56 | 0 | 25 | 3 | 0 | 0 | 0 | 28 |
| `Fuaran.Core.Wire` | 141 | 64 | 11 | 3 | 4 | 0 | 0 | 59 |
| **Total** | 1284 | 317 | 419 | 38 | 15 | 0 | 3 | 492 |
<!-- operation-coverage:end -->

## Exit criteria, with evidence

1. **Reproducible — met.** `check.ps1 -Runs 3` verifies the module three times from a cold
   cache with `--quake 3` (each query 3/3 over varying Z3 seeds); CI's `proofs` job runs exactly
   that on `windows-latest` and, since Phase 393, `ubuntu-latest` on every push. Locally the module also checks at `--z3rlimit 10` (the
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
pwsh ./proofs/check.ps1 -Strict    # the recorded run: cost overruns stay findings; an incomplete budget declaration is red
pwsh ./proofs/check.ps1 -Runs 3 -Strict   # what the weekly proofs-strict workflow runs; records the strict baseline
pwsh ./proofs/check.ps1 -CacheDir <dir>   # put the checked-module cache somewhere you name
pwsh ./proofs/check.ps1 -Since origin/main  # only the module CONE that changed against a tree (below)
pwsh ./proofs/check.ps1 -Modules TreeOps,Skeleton   # a cone named by hand
pwsh ./proofs/check.ps1 -Since HEAD -PlanOnly       # print the cone and what the kit would check; run nothing
pwsh ./verify.ps1 -Proofs          # the whole repo gate plus the proof leg
pwsh ./proofs/kit/extraction-post-pass.tests.ps1   # the extraction post-pass's go-red proof (finding 7)
```

The last one is deliberately **not** part of the leg — `check.ps1` prints exactly what it printed
before the post-pass existed, and a proof about the pass is not a proof about the models. Run it
when the pass changes, when the prover pin moves, or when you want the retirement condition
answered.

Every module in the roster, `modules.json`, goes through all three steps (a `proofOnly` or
`generated` one is checked and not extracted), and `-Runs N` means N cold-cache verifications of all
of them; adding a model is adding ONE entry there, with its `kind`, its `opens` and its budget
(Phase 504: nothing else enumerates the models). The first
run downloads the pinned release (~200 MB, hash-verified) into `proofs/.fstar/`; `FSTAR_HOME`
pointing at a matching release skips that. Editing a `.fst` without re-extracting fails the leg
with "oracle drift" — that is the point, not an inconvenience.

**Which operating systems (Phase 393).** `fstar-pin.json` carries one entry per OS — `windows` (a
`.zip`) and `linux` (a `.tar.gz`) — for the same release, each with the sha256 GitHub publishes for
that asset. The kit resolves the entry by the host's OS, refuses an OS with no entry **by name**
(exit 2, the same code as before: no prover can be resolved here), and refuses an entry that is
incomplete or names a different release than the pin, so a pin bump that forgets one entry is red
on that OS rather than quietly running the old prover there. A macOS machine has no entry and sets
`FSTAR_HOME`. `pwsh ./proofs/kit/check-proof-leg.tests.ps1` holds the resolution (its `R` arms need
no prover), and `-ResolveOnly [-Platform <os>]` on the kit prints the entry an OS would fetch. CI's
`proofs` job runs on both a Windows and a Linux runner.

**Where the prover comes from (Phase 399).** Each entry carries a second source, `mirror`: the same
asset attached to this repository's `prover-fstar-<release>` release (a tag that never starts with
`v`, so it cannot fire the publish workflow). The kit tries `url`, then `mirror`. A source that
cannot serve the archive is reported and the next one is tried; the leg fails only when both have
failed, and it names each source and why. A source that serves **different bytes** is refused on the
spot, naming the source, the digest it served and the pin's sha256, and the other source is **not**
tried: different bytes mean the asset was replaced, which is a finding about that source, not an
outage to route around. Before this, the archive had one upstream source and CI cached it by the
pin's hash, so a deleted upstream asset would have broken the leg only when the cache was evicted,
with a message about a download rather than about the pin. The `M` arms of
`kit/check-proof-leg.tests.ps1` hold the order and both refusals, with no prover and no network.
**A pin bump attaches the new release's assets to a new `prover-fstar-<release>` release** in the
same change that moves the pin (`kit/README.md`, "The pin moves in one place", has the commands);
the kit refuses a mirror that names a different release than the pin.

**Which run gates a publish (Phase 393).** The tag workflow (`.github/workflows/publish-packages.yml`)
runs its own `proofs` job — `./proofs/check.ps1 -Runs 3`, the ci job's leg except that it runs the
oracle HOST step, which the ci job skips (`-SkipOracleHost`) because its own suite job runs those families — on the tagged
commit, and its `publish` job `needs:` it, so nothing is packed over a red leg. The alternative was a
step reading the tagged commit's `ci` proof-job conclusion through the Checks API and refusing
anything but `success`; it was declined. A Checks-API read answers a question about a DIFFERENT run
— one that may still be in flight when the tag is pushed (the release sequence pushes the commit and
the tag together), that may have been re-run or cancelled, and that a tag on a commit `ci` never ran
for (a dispatch, a tag pushed ahead of its commit) cannot have — and it makes the gate depend on a
token permission and on a job's display name. A job in the publish run is about the exact commit
being packed and is red or green in the same run that would pack it. The cost is one more proof run
per release, which a release can afford.

### The module cone — `-Since`, `-Modules`, and when to run which (Phase 328)

A phase that edits one model used to pay for all twenty-one: the leg checks every registered module
or none. `-Since <tree>` checks only the **cone** — the registered modules whose inputs changed
between `<tree>` and the working tree — still cold, still under every gate the full leg applies,
and prints every registered module `IN`, `DEP` or `OUT` with the path that put it there. It is the
safe form of a cache: it recomputes from the tree every time, and the only thing it reuses is the
knowledge of what did not change. A shared checked-module cache would buy more and is still
declined, for the reason the section on the cold cache below gives (DECISIONS.md D74).

**What puts a module in.** Soundness is the whole of the selector's correctness, so the rule is
written as what a module's verdict *reads*:

- its model `proofs/<M>.fst` — and then **every module that references it, however transitively**,
  because a check re-reads everything it references;
- its committed oracle `proofs/oracle/<M>.fs`, which the extraction diff reads;
- a production source under `src/<Package>/` for a package its `modules.json` entry names — Phase
  203's `packages`, which is where the map from subject to model lives (`../proofs.json`'s rows name
  models and theorems, never a source path);
- its `modules.json` entry (its `fastestSeconds` decides whether the cached-read gate applies), which
  since Phase 504 is also its registration and its `kind`.

**Every** module is in when a shared input of the whole leg moved: `fstar-pin.json`, the kit's
engine or post-pass, `check.ps1`'s *code* (a comment-only edit is not a code change), an unregistered
model, or any path under `proofs/` the selector does not classify — an unknown input defaults to
"it matters". Documentation, `kit/templates/`, the kit's own `*.tests.ps1`, `coverage-exclusions.json`
and the oracle project's `Prims.fs` put nothing in: the prover and the extraction never read them,
and the host step that does is never narrowed.

**References are read from the models' code, and they are wider than `open`.** F\* also resolves a
module named by an abbreviation or by a qualified name, with no `open` anywhere — `WireCanon`
reaches `JsonParse` by `module JP = JsonParse` and by nothing else, so a selector that followed
`open` lines alone would leave `WireCanon` stale after a `JsonParse` edit. The reading takes every
`open` / `include` / `friend`, every abbreviation and every qualified `M.x`, with comments and string
literals blanked first (these models name each other constantly in prose). It over-approximates
F\*'s own dependency scan, which can cost prover time and never soundness; the `Proofs.Cone` family
holds it to an independent reading of every declared `open` and abbreviation, and to acyclicity,
which F\* requires and which prose read as code would break.

**`DEP` is not `IN`.** A module the cone references whose own inputs did not change is checked
*ahead* of the modules that need it — on a cold cache the prover must check it anyway before it can
check them — and listed as `DEP`, so every clock the leg prints is its own module's and a budget is
compared against the module it was seeded for. `-Modules <list>` is the named modules `IN` and what
they reference `DEP`, by the same closure and the same tool (Phase 522): until then a hand cone was
exactly what it named, and a named model whose shared modules were not named failed at extraction as
an apparatus fault, because nothing had checked the modules it opens.

**Where it is computed.** In the tool (`proof-cone` in `../tools/Fuaran.Core.Tools`, over `ProofCone`
beside `parseRoster` in `../tests/Fuaran.Core.Tests.Fixtures/`), which `check.ps1` builds and asks before it hands
the kit the cone. That placement is the point rather than a convenience: the roster is read by the
same function the `Proofs.Ladder` family reads it with, so the ladder and the selector cannot
disagree about which models exist. The kit is handed the cone and a copy of `modules.json` without
the entries of registered modules left `OUT`, so its both-ways budget coverage still fires for every
module it checks and for any orphan entry; the kit itself is unchanged. **With neither switch,
`check.ps1` hands the kit the whole roster and the declared budget file**, so the full leg's verdict
cannot move — `-PlanOnly` prints those arguments, and the family holds them to the roster.

**Every run leaves one record, and an empty cone is green only over a recorded strict run.** Every
run of the leg writes its **run verdict** (Phase 522; `kit/check-proof-leg.ps1`'s header, "THE RUN
VERDICT", is the field list) to `proofs/obj/last-run.json`, red or green, with the cone it ran and
whether the refusal tests held. A green `-Strict` *full* run's record, with the tree it verified, is
`proofs/last-strict.json`, and whoever ran it commits the file: the same record, so a baseline and any
other run are read by one reader. The green empty-cone line repeats its machine, contention and
over-budget modules, so a reader can see what stands behind it. The weekly `proofs-strict` workflow
(below) produces a fresh record as an artefact.
An empty cone exits green only when that tree is an ancestor of HEAD **and** the cone against it is
empty too, so an empty cone against some later tree cannot lean on a baseline that predates a change
nobody verified. Otherwise the leg says it has **no strict baseline** and exits 1. The record is
refused, in yellow and without touching the verdict, when the working tree differs from HEAD in
anything a module reads: that run verified bytes no commit holds.

**The gate policy the selector serves:**

| who | runs | when |
|---|---|---|
| a phase's worker | `check.ps1 -Since <the tree the phase started from>` — the cone, cold | before hand-over, on a phase that touches a model, an oracle or a covered source |
| the integrator | the full leg, `check.ps1 -Runs 3` (what CI runs) | at every tier landing |
| a release freeze | `check.ps1 -Strict -Runs 3` over the committed candidate, and the record it writes committed | at every freeze, which is what makes the next empty cone green |

**What it measured.** When the selector shipped, the twenty-one entries then in `modules.json` recorded
783 s of slowest-observed cold checks between them (1,710 s of budget; `modules.json` is the current
figure, and the roster has grown since), and a quiet cold pass of the whole leg measured ×0.29 of
that (Phase 171). The first cone run — `Chain.fst` perturbed by a comment, `-Since HEAD -Runs 1
-SkipOracleHost` on a machine with 2.7 GB free — put exactly `Chain` in, checked it in 46 s against
its 60 s budget, re-extracted and diffed its oracle, ran the kit's refusal tests and exited green in
82 s of wall clock, the first prover download included. The same tree with nothing changed exits 1,
naming the absent baseline, before the prover is touched.

### What `==== proofs: green` stands behind — and what it does not (Phase 221)

**Until Phase 221 the word stood behind nothing a step could fail.** The kit script carried
`$LASTEXITCODE = 0` at script scope, a line inherited from the pre-kit `check.ps1`, where it had
been harmless. Once `check.ps1` became a caller invoking the kit with `&` (Phase 155), that line
became a script-scope variable SHADOWING the automatic one, and every exit code the leg read — the
prover's at the check and extract steps, `dotnet build`'s and `dotnet run`'s at the host step —
read 0. Measured 2026-09-24 against the pinned prover, through the same `&` a caller uses: a host
build of a project that does not exist (MSB1009) printed `==== proofs: green` and exited 0, a host
filter that could not run did the same, and **so did a model with a type error** — F* printed
`Error 19` and the leg printed `LegBad.fst verified … every query 3/3`. The shard that filed this
(found by the fuaran-core#200 worker) suspected the host step's `try`/`finally` beside the script's
`trap`; that hypothesis was wrong, and the check step being blind as well is what the sweep it
asked for found. How far that reached, measured on the pre-221 kit: a refuted model that is also
EXTRACTED was still caught, one step late and in the wrong words — F* then refuses the extraction,
no `.fs` appears, and the file-exists test (which reads no exit code) failed the leg as an
APPARATUS fault (exit 4, "the model is not implicated"). A refuted model in `$proofOnly` — the six
generated vocabulary models and their proof scripts — was not caught at all. So a leg green between
Phase 155 and Phase 221 is evidence that every extracted model verified and matched its oracle, and
is NOT evidence that a `$proofOnly` model verified or that any host family passed. The host half
was worse than "did not run": with a compile error planted in the test project, the pre-221 kit
printed `Build FAILED.`, read exit 0, and ran `dotnet run --no-build` against the PREVIOUS build's
binary — `Proofs.Oracle` reported 108 passed and the leg said `green` about code that did not
compile. The fixed kit stops at the build with exit 1 and a line naming the HOST step.

What a green `check.ps1` means now, step by step:

| Step | Green means | Refused (non-zero exit, no `green`) when |
|---|---|---|
| CHECK | every `$modules` entry verified, `-Runs` times, from a cold cache, and no check faster than a cached read | the prover exits non-zero (a refutation, exit 1; an abort twice, exit 3), a second writer is caught in the cache, or a check finishes under the cached-read threshold |
| EXTRACT | every non-`$proofOnly` model's fresh extraction is byte-identical to its committed oracle | the prover exits non-zero, no `.fs` is produced (an APPARATUS fault is exit 4), or the diff is non-empty |
| HOST | the host project built and every `$hostFilters` family passed | the build or any family exits non-zero — the verdict line says `HOST step` and names the project or filter |
| leg-tests | the leg itself refused a failed host build, an unrunnable host filter and a refuted model, beside a green control | any of those came back exit 0 or printed `green` |

What it does **not** cover, said so a ship record citing it does not over-claim:

- **A skipped host step.** `-SkipOracleHost` (which `verify.ps1 -Proofs` passes, leaving the
  families to its own suite run) and an empty `-HostFilters` both reach `green` without running a
  host family. The leg now prints `the HOST step did NOT run (…)` immediately before the verdict,
  so the log says which green it is.
- **A zero-exit check that printed a diagnostic.** The check step trusts the prover's exit code;
  the Phase-166 discriminator only CLASSIFIES a non-zero one. F* was measured exiting 0 over an
  error at the EXTRACT step (Error 317), which is why that step also asks whether the file exists;
  no such case has been observed at the CHECK step, so none is guarded against.
- **Cost.** A budget overshoot is a finding and the leg stays green, with `-Strict` or without it
  (Phase 399). A strict run records each module's time against its budget instead.
- **The claims ladder and the coverage predicate** are host families (`Proofs.Ladder`,
  `Proofs.Coverage`), so they stand behind the word only when the host step ran.

`kit/check-proof-leg.tests.ps1` is the regression test. It runs the leg the way a caller does
(`&`, in process) over a scratch proofs directory, asserts the EXIT CODE of each arm, and reads
the transcript only to assert `proofs: green` is absent. It was run against the pre-221 kit first
and went red on six of its eight assertions (arms B, C and D; the green control held), then green
against the fixed one. Unlike the post-pass's go-red proof above, it IS part of `check.ps1`: it
takes a few seconds, and it is what the leg's green is a claim about.

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
==== proofs: Unit.fst ABORTED (no diagnostic) — exit -1 after 8s on run 1 of 1, attempt 1 of 2. Nothing was refuted: …
==== proofs: retrying Unit.fst once — the retry is BOUNDED (one per module per run) and its timing is a WARM measurement, …
==== proofs: Unit.fst verified ON RETRY — run 1 of 1, 13s (warm cache: NOT a cold measurement), every query 3/3 under --quake
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

> **Superseded in part by Phase 399.** The per-module floors are retired: one absolute **cached-read
> threshold** answers the question they answered (see "Cached read or cold check" below, and the kit
> engine's header, step 1). The per-invocation cache and the cache provenance check stand.

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

**The host step runs two families, and the second is about this document.** `../proofs.json`
declares the claims ladders below as data — what is proved, what is only tested, what is assumed —
and since Phase 144 the leg CHECKS it rather than trusting it: the `Proofs.Ladder` family
(`../tests/Fuaran.Core.Tests/ProofsLadderTests.fs`) holds every row to this tree. A `proved` row's
`evidence.theorem` must be a top-level `val` / `let` / `let rec` of that name in the model it
names; that model must be one the roster (`modules.json`) registers, and must be there; a `tested`
row's family must be one of three closed forms (`Proofs.Oracle`, `Conformance.<law>`,
`Suite:<Module>.<list>`) and its cases must be cases of that family; every model in the roster (every
entry but the two `shared` modules) must have at least one `proved` row; every row's `phase` and
`level` must be the closed forms; and since Phase 522 every row states what it claims in `statement`
and what it does not cover in `boundary`, tells no history in either (its `phase` is its one
citation), and carries `claim` as their exact join for registries that still read it. So renaming a theorem without
touching the file, or adding a model with no claim written down, fails the leg with the row named
— the same posture as oracle drift, applied to the ladder. Each clause has a go-red fixture beside
it (`../tests/Fuaran.Core.Tests/fixtures/proofs-ladder/`), so a green ladder is known to be one
that could have failed.

What it does **not** check is this prose. Row-to-README agreement stays a human act; what is
mechanical is row-to-tree agreement, which is the half a check can settle. Editing a ladder still
means editing both.

### Cached read or cold check — one threshold instead of a floor per module (Phase 399)

**The ruling.** On 2026-10-08 the operator separated the two questions `-Strict` had been
conflating. A budget overrun is a slow cold check, the opposite of a warm one, so it is a recorded
finding and never a red leg. The hard gate is the other question: *was this a real cold
verification?* The primary answer is the cache provenance check (Phase 402): nothing may appear,
change or vanish in the cache between this run's invocations, and a module's own `.checked` may not
be there before its check. What that check cannot see is a writer active only DURING one invocation.
The backstop for that case is now one absolute number, `cachedRead.thresholdSeconds` in
`modules.json`. A module whose check finishes under it fails the leg as a **probable cached read**,
before its green line is printed.

**What a cached read costs, measured** (2026-10-08, the pinned prover, a local Windows dev machine
with other sessions active). Ten modules were cold-checked in roster order into one fresh cache, as
the leg does, and each was then re-run three times against the populated cache with the leg's own
flags:

| module | cold check | three cached reads |
|---|---|---|
| `DagFold` | 75.1s | 0.44s, 0.34s, 0.34s |
| `WireDecode` | 36.1s | 0.24s, 0.24s, 0.23s |
| `TreeOps` | 83.1s | 0.43s, 0.43s, 0.44s |
| `Skeleton` | 1.61s | 0.43s, 0.44s, 0.46s |
| `Chain` | 39.9s | 0.28s, 0.28s, 0.32s |
| `JsonParse` | 146.4s | 0.23s, 0.25s, 0.24s |
| `Preservation` | 77.3s | 0.51s, 0.49s, 0.51s |
| `Limits` | 0.38s | 0.18s, 0.18s, 0.18s |
| `Utf8` | 29.8s | 0.20s, 0.18s, 0.20s |
| `WireCanon` | 184.0s | 0.38s, 0.37s, 0.38s |

A cached read costs prover start-up and deserialisation: 0.18s to 0.51s, whatever the module costs
cold. So the threshold is **1.0s**, about twice the slowest read. Two modules check genuinely faster
than that cold (`Limits` at 0.38s here; the kit tests' one-line models at about 0.2s), so the
threshold cannot judge every module. It applies to a module whose recorded `fastestSeconds` is at
least `cachedRead.appliesFromFastestSeconds`, which is **3s**, three times the threshold. `Skeleton`,
`Limits` and `WireVersioning` fall below that and rest on the provenance check alone. The margin
covers a machine about twice as fast as this one, which is what the Linux runner measured
(`WireColumn` at 16s there).

**The per-module floors are retired**, and a budget file still carrying `floorSeeding` or a
`floorSeconds` is refused by name; `fastestSeconds` stays as the threshold's input. Why one threshold
replaces them, and why no OS gate is needed, is stated once in the kit engine's header (step 1).

**Observed and not adopted.** Every cold check above printed at least three `Quake:` query lines
and every cached read printed none. That is a second discriminator that needs no clock, but this
phase did not adopt it, because the ruling asked for a threshold. `DECISIONS.md` D131 records it as
an open option.

**Held by** the `C` arms of `kit/check-proof-leg.tests.ps1`: a genuine checked file put in front of
its module is refused, a check under the threshold is refused before its green line, and at the real
numbers (1.0s from 3s) a sub-second module's genuine cold check stays green while a warm re-run of a
slow module is red (its genuine checked file put in front of the check through the kit's
`-BeforeInvocation` test seam, after the provenance check has passed), a module
recorded as genuinely fast is not judged by the threshold, a retired `floorSeconds` is refused, a
cold check several times over budget under `-Strict` is green with the finding recorded, and a
missing budget is still red under `-Strict`.

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
gate.** Since Phase 399 that holds under `check.ps1 -Strict` as well: an overshoot is a slow COLD check, the
opposite of the failure a gate exists for, so it is recorded with its percentage and never turns the
leg red. It is also not a job timeout: a timeout says a run died and
nothing about which module, where an overshoot is attributable.

Coverage is checked both ways, and both are findings rather than failures: a module the leg checks
with no entry is named — so adding a model tells you to budget it, without reddening the leg of
whoever added it — and an entry for a module the leg does not check is named too, so a removed model
cannot leave a budget behind pretending to measure something. The entry's SHAPE is the one thing
here that IS a failure: a missing or duplicate module name, or a `budgetSeconds` that is not a
positive number, fails the leg with the entry named, because a file in that state cannot be read as
a budget at all.

**The strict run is scheduled (Phase 399).** CI's `proofs` job does not pass `-Strict`, so on a push
an overshoot is only ever a warning, and a real regression could stay a warning forever. The
`proofs-strict` workflow (`.github/workflows/proofs-strict.yml`) runs `check.ps1 -Runs 3 -Strict` on
a Windows runner every Monday and on demand, never on a push or a tag. Its job summary is read from
the run's verdict record, never from the log: the verdict and its red line, and every `COST` finding
against `modules.json` with its percentage, which are the trend — a module that is over budget week
after week is the case for the re-seeding act in the next paragraph, once a quiet re-measurement
confirms the module really grew. A cost finding never makes the run red; what does is the kit
engine's header's to say. A **green** run records the strict baseline and uploads it as the
`strict-baseline` artefact, beside the run's verdict record. The workflow commits nothing; a
maintainer commits the file when `-Since` should lean on that run.

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

So the leg measures the afternoon directly. At the end of every **run** it reports a **contention factor** — the median, over the
modules the working tree did not change, of what each just cost divided by the `measuredSeconds` its
budget entry records — and labels every cost finding from a run above the budget file's
`contentionSeeding.threshold` a CONTENDED PASS, which is **not a re-seed obligation**:

```
==== proofs: contention — run 1 of 1, x0.29 over 16 untouched module(s), at or under the x0.80 threshold: an ordinary pass. A cost finding on this run is about its module.
==== proofs: contention — run 1 of 1, x0.29 over 4 untouched module(s), ABOVE the x0.20 threshold: this was a CONTENDED pass. 1 cost finding(s) on this run carry the label, and a labelled finding is NOT a re-seed obligation.
     ColumnOps.fst took 12s against its 8s budget on run 1 of 1 — 4s over, 150% of budget — CONTENDED PASS (x0.29 against a x0.20 threshold): the modules this tree did not change ran x0.29 of their recorded measurements on this run, so this figure measures the afternoon and not the module
```

(the first from a quiet cold pass of this leg, the second from Phase 171's probe, which lowered the
threshold to 0.20 and one budget to 8s; `ColumnOps.fst` is a model that has since left with the compute strand). **The explanation lives once, in the kit engine's
header** (`kit/check-proof-leg.ps1`: THE CONTENTION FACTOR, WHAT THE NUMBER'S SCALE ACTUALLY IS, and its
sections 2b and 3c): why the neutral point sits well below one and the threshold must be seeded from a
measured quiet pass and a measured contended one, why nothing is multiplied into a measurement, how
the untouched set is derived and where that stops, why a module too cheap to time does not vote
(`minimumSeconds`, `minimumSamples`), why an absent `contentionSeeding` block is not a finding, and
why an entry's optional `contentionFactor` is provenance only. This repository's seeding record is the
`contentionSeeding` block itself, and every run's factor and label are in its run verdict
(`proofs/obj/last-run.json`).

**The two cost cliffs, to be read before the next model is written.** Both have been paid for once:

- **Context pruning is the difference between minutes and tens of minutes.** `TreeOps.fst` checks in
  300s under the default SMT context and 56s under `--ext context_pruning`; `JsonParse.fst`, 128s
  against 72s. Both set it with `#set-options` INSIDE the module rather than in `check.ps1`'s flags,
  and that placement is load-bearing rather than tidy: pruning changes which facts a query can see,
  so a module that has not been checked under it must not be switched to it as a side effect of
  another module's cost. If either budget ever needs bumping by a large factor, check first that the
  option is still in force — losing it reads in the log exactly like ordinary growth.
- **An SMT pattern on a rewriting lemma is a cliff, not a slope** (Phase 134's lesson, at
  `DagClosure.fst` section 11). `rev`, `app`, `ids_of` and `ops_of` rewrite into one another, and their
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
`proofs/check.ps1` is now a thin caller that hands the kit what this repository has — the roster
(read from `modules.json` since Phase 504), the two host families, where the test project is — to
`kit/check-proof-leg.ps1`, which runs the leg. Nothing about the mechanism is in the caller and
nothing about this repository is in the kit. The public surface did not move: the same flags, the
same output lines, the same exit codes. (Until Phase 504 `$modules` was one literal line in
`proofs/check.ps1` that the `Proofs.Ladder` family read with a regex; the roster is `modules.json`
now, read by the script and the tests alike.)

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
A domain instantiates fold confluence at its own witness by copying `DagFold.fst` (and `DagClosure.fst`
when it uses the DAG beneath the fold), filling the
template's fourteen holes — one of which, `{{DIAMOND}}`, is the obligation it proves — and adding the
instance to its own roster (`modules.json`); the contract and the procedure are
`kit/README.md`'s ("Importing a theorem"). Not in the set: the six generated modules (`Vocabulary`, `DocVocabulary`,
`ScoreVocabulary` and their `…Proofs`), which a domain does not copy but REGENERATES from its own
`Idl` with the same backend, and `WireVersioning.fst`, a theorem about this repository's IDL diff ([`WireVersioning`](#wireversioning)).

## The models

One section per model, in the order the leg checks them, rendered from `../proofs.json`'s rows and each model's
header by the tool (`dotnet run --project tools/Fuaran.Core.Tools -- render proofs-readme`); the `Proofs.Ladder`
family holds this file to that render. Edit the rows or the header, never this block.

<!-- models:begin — rendered from ../proofs.json, modules.json and each model's header by `render proofs-readme`; edit those, never this block -->
### `DagFold`

[`DagFold.fst`](DagFold.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`.

> DagFold — an F\* model of Fuaran.Core's N-lane DAG fold, with fold confluence as a
> machine-checked theorem (fuaran-core Phase 131, the prover spike).

**What is modelled.** The fold that `Fuaran.Core.OpStream.Dag.reconcileMany` performs and that
`Fuaran.Core.Conformance.FoldConfluence.foldOnce` measures: N lane deltas (one op list per
writer, off one shared base), checked pairwise for footprint interference exactly as
`Dag.conflicts` checks them, then either HALTED with the conflict report and nothing applied,
or composed in arrival order and REPLAYED through the domain reducer from the base state.

**Proved** (2)

- <a id="row-fold-confluence-total"></a>**`fold-confluence-total`** — `fold_confluence_total` · fuaran-core#300

  Fold confluence with NO hypothesis about the lane set: `fold_once` (F#: `FoldConfluence.foldOnce`, and `Dag.reconcileMany`'s lanes-apply step) replays every lane on its own from the base BEFORE composing anything and refuses a set with a rejecting lane with the set of rejecting lanes, so for every permutation of any lane set the outcome is the same fold, the same canonical halt report, or the same rejecting-lane set. The `lanes_apply` premise of the fold half is discharged by the fold itself (`rejecting_nil_iff`).

  _With:_ `fold_confluence_halt`, `conflicts_nil_independent`, `all_conflicts_perm`, `replay_perm`.

  _Boundary:_ The one hypothesis is the domain's `independence_diamond`. The HALT half (`fold_confluence_halt`) asks for nothing at all — not the diamond, nothing about `apply`: whether a lane set halts, and the report it halts with, are properties of the footprints alone. Two folds reach the same state as propositional equality in the model, not as a hash. The halt report is a permutation invariant of the pairwise sweep (`all_conflicts_perm`), a conflict-free set replays identically under any order (`replay_perm`), and `Dag.conflicts` is empty exactly when every cross-lane pair is `Ops.independent`, both directions (`conflicts_nil_independent`). NOT claimed: anything about the linear `OpStream`; any consumer's own projection order over its own op union, which is that consumer's certification (this theorem is what makes its choice of total order canonical-form-only); or any domain's reconciliation policy — on a halt the model, like production, decides nothing and applies nothing.

- <a id="row-slot-conflicts-agree"></a>**`slot-conflicts-agree`** — `pair_conflicts_nil_iff` · fuaran-core#340

  Over SIX address sets — the four node sets and the slot reads and slot writes a footprint carries as `(node, slot)` pairs — `Dag.conflicts` fires on an op pair exactly when `Ops.independent` refuses it: the node-level slot collisions (a slot access of a node the other side touches whole, `slots_against_node`) join the `ConcurrentUpdate` class, and each slot one side writes and the other accesses (`slot_clash`) is tagged `SlotClash` at its node. The halt report's arrival-order invariance (`conflicts_sym`, `all_conflicts_perm`) holds over the widened record, and the fold theorems consume `independent` abstractly, so they are untouched.

  _With:_ `pair_conflicts_sym`, `slot_clash_sym`, `concurrent_or_slots_sym`, `mem_u_tag_slots`.

  _Boundary:_ This row does NOT say that a slot write and a whole-node write of one node commute — the model refuses that pair on purpose (`whole_node_refuses_every_slot`), because a whole-node write is a write of every slot. Each clashing slot is tagged in its own address space, so a `SlotClash` is never deduplicated against the node-keyed shapes. No skeleton op narrows a footprint to a slot, so `Ops.footprint`, under a plain or a keyed `OpScope`, gives a domain that declares no slot access the four-set verdict; `Conformance.footprintLawsAt` counts the pairs only slot granularity frees, vacuously where none is declared.

**Tested** (1)

- <a id="row-fold-differential"></a>**`fold-differential`** — `Proofs.Oracle`, 8 cases · fuaran-core#131

  Production's delta recovery from a content-addressed DAG, its pairwise conflict sweep, the composition and the replay together agree with the extracted model over three lane pools, per lane set and per sampled arrival order — outcome and composed merge script, not merely a hash.

  _Boundary:_ Agreement is over the sets drawn and the orders enumerated, never over all inputs. Arrival orders are exhaustive up to four lanes and a 24-order sample above (the work-plan domain at five lanes). Every pool asserts that both outcome classes, fold and halt, were reached; on the two domain pools the host also asserts the theorem's instance on the extracted code — the oracle's own outcomes across all sampled orders are one outcome.

### `DagClosure`

[`DagClosure.fst`](DagClosure.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `DagFold`.

> DagClosure — the DAG beneath the fold: Fuaran.Core's lane DAG, its closure, its drain, the
> recovery of each lane's delta, merged heads, the reconcile's partition, replay, checkpoints,
> truncation and the reachability index, each modelled clause for clause with
> `src/Fuaran.Core.OpStream.Dag/DagOpStream.fs` (fuaran-core Phases 134 to 437; a module of its
> own since Phase 534).

**Proved** (19)

- <a id="row-slot-writes-commute"></a>**`slot-writes-commute`** — `slot_writes_commute` · fuaran-core#340

  Two writes to DIFFERENT slots of one node commute, at every slot a reader of the store can look up, and two slot writes are independent exactly when their slots differ (`slot_writes_independent_iff`) — so the precision the slot clauses buy is sound against a model of what a slot write does. The model is a store of `(slot, value)` bindings, latest first, where a write binds its slot and a read finds the latest binding. Two writes to ONE slot with different values disagree at that slot and the footprint refuses the pair (`same_slot_writes_disagree`); a whole-node write of the slot's node is refused against each of its slots, from either side (`whole_node_refuses_every_slot`).

  _Boundary:_ Commutation is UP TO LOOKUP, which is all a reader of the store can observe — the two stores are not list-equal, and the theorem does not say they are. No skeleton op writes a slot — `TreeOps.op_fp` leaves both slot sets empty — so this row is about a DOMAIN's own lowering (`Footprint.slotEdit`), sampled at its ops by `Conformance.footprintLawsAt`.

- <a id="row-reconcile-sound"></a>**`reconcile-sound`** — `reconcile_sound` · fuaran-core#300

  The reconcile's partition of the region above the base (F#: `region` in DagOpStream.fs, clause for clause: shared history held by two or more heads' closures, one exclusive delta per head) puts each node in the script AT MOST ONCE, and exactly the enumerated nodes above the base that some head holds — for any closures, overlapping, nested (a fast-forward), repeated (a duplicate head) or criss-crossed.

  _Boundary:_ Stated over any duplicate-free enumeration of the heads' closures, which the drain is by `general-topological-order`. The shard's further clause, that the script replays to `Dag.tryReplayTo` of a merge of the heads under the diamond, is REFUTED rather than proved: a merged history whose branches do not commute has no order-free replay of its own (`ReconcileShapeTests.fs` pins the witness).

- <a id="row-reconcile-order-free"></a>**`reconcile-order-free`** — `reconcile_fold_order_free` · fuaran-core#300

  `Dag.reconcileMany` over the same heads named in two orders: the shared region is the same list, and from the state it reaches the checked fold of the exclusive lanes is outcome-equivalent (`reconcile_fold_order_free`, composing the partition's invariance under a permutation of the closures with `fold_confluence_total`).

  _Boundary:_ Under the diamond and nothing else.

- <a id="row-dag-delta-recovery"></a>**`dag-delta-recovery`** — `between_chain` · fuaran-core#134

  For the base-plus-N-chains shape — one shared base node and one linear chain per writer, which is what the fold's own generator builds and what a local-first deployment has — the delta of a lane off the base is that lane's nodes, and its ops, in append order. The whole layer is computed rather than assumed: the head's ancestor closure, the base's, the topological order, the difference and the lookup. The CHOICE of topological order is proved at `topological-order-choice`.

  _With:_ `between_ops_chain`, `resolves_of_distinct`.

  _Boundary:_ Under the id-distinctness premise, and only for the base-plus-N-chains shape. The premise enters as `distinct_ids` and is consumed as `resolves`, bridged by `resolves_of_distinct`: two nodes sharing a content id is the only way `lookup` returns a node other than the one a chain named, and the only case on which this theorem says nothing.

- <a id="row-fold-from-the-dag"></a>**`fold-from-the-dag`** — `fold_confluence_dag` · fuaran-core#134

  The fold over the DAG is the fold over the lane lists, and the confluence law therefore holds stated from the content-addressed DAG rather than from deltas someone had already recovered: the same heads over the same base fold to the same state, or halt with the same canonical report, however the heads arrive.

  _With:_ `reconcile_many_dag_eq`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). `Dag.mergeBase` is not on this path: `foldOnce` hands `reconcileMany` the base node's id directly, so nothing here locates a divergence point (that is `merge-base-divergence`).

- <a id="row-topological-order-choice"></a>**`topological-order-choice`** — `topo_of_is_the_kahn_drain` · fuaran-core#142

  That the order production picks and the order the model computes are the same order, on the base-plus-N-chains shape. Two lemmas: a distinct enumeration of a spine's closure in which every node follows its single parent is that spine in append order (`spine_order_forced`, a list argument naming no DAG and no hash), and the frontier drain over that closure produces such an enumeration (`kahn_drain_is_such_an_enumeration`). The recovery is therefore restated with the order universally quantified rather than fixed to the parent-walk reversal (`between_chain_any_order`), and the fold above it with it (`reconcile_many_dag_ordered_eq`, `fold_once_dag_ordered_eq`). The selector is a parameter constrained only to return a member of the frontier it is handed, and on a spine that frontier is one element wide at every step (`kahn_frontier_singleton`), so every selector agrees and nothing here says how ids compare. These spine results are corollaries of `general-topological-order`, re-derived at its selector (`spine_drain_is_the_parent_walk`, `spine_drain_is_append_order`).

  _Boundary:_ Only on the base-plus-N-chains shape. The smallest-id tie-break is NOT modelled here and does not need to be; the order over a MERGE DAG, where the frontier genuinely widens and the tie-break becomes observable, is not claimed by this row (it is `general-topological-order`'s). Neither universal quantification is vacuous: `pick_head` is exhibited as a selector meeting `picks_from_frontier`, and the enumeration `kahn_drain_is_such_an_enumeration` produces meets `between_chain_any_order`'s hypotheses and is the model's own order (`topo_of_is_the_kahn_drain`).

- <a id="row-general-topological-order"></a>**`general-topological-order`** — `drain_deterministic` · fuaran-core#156

  The id-ordered Kahn drain over an ABSTRACT node set — no base, no lanes, no hash — is DETERMINISTIC, a function of the node set alone and invariant under the order the nodes arrived (`drain_deterministic`, stated over the module's own permutation relation); a LINEAR EXTENSION of the parent relation over every node it places (`drain_linear_extension`); and TOTAL on an acyclic set, placing every node exactly once and producing a topological enumeration (`drain_total_on_acyclic`). Acyclicity is taken as a topological enumeration supplied as a witness list, which is the standard characterisation and is not circular — the witness is never the drain's own output — and `drain_complete_is_acyclic` gives the converse from the linear-extension theorem, so complete drain and acyclic set are an IFF, which is exactly the signal `Dag.isAcyclic` / `Dag.tryTopoOrder` read: they answer `Cyclic` exactly when the drain leaves a node unplaced. The smallest-id tie-break is load-bearing here: `frontier` answers in the work list's order, so only an order-invariant selector survives `drain_deterministic`, and the spine model's `pick_head` does not. The spine results of `topological-order-choice` are corollaries at this selector.

  _Boundary:_ The id order is a PARAMETER constrained to be a total order, so the claim is that clones sharing an order agree rather than that any particular order is production's — the differential ties it to `String.CompareOrdinal`, which is what `List.sort` gives a string list. The delta recovery over a merged head and `Dag.mergeBase` are not claimed here (see `dag-delta-recovery-merged-head` and `merge-base-divergence`). NOT claimed: any consumer's own instantiation. Production no longer compares lengths: since `fuaran-core#529` the drain returns the nodes it left unplaced, and `Dag.tryTopoOrder`, `Dag.isAcyclic` and `Dag.tryReplayTo` answer `Cyclic` exactly when that list is non-empty — an incomplete drain, the signal this row's iff characterises. `Dag.between` takes the drain's placed nodes without that check, so over a cyclic closure it returns the truncated prefix rather than refusing.

- <a id="row-dangling-parent-policy"></a>**`dangling-parent-policy`** — `drain_policies_agree` · fuaran-core#156

  What the drain does with a parent OUTSIDE the set is a parameter of the model, because the two production call sites differ on it and a theorem about "the drain" that did not say which would be a theorem about neither. `IgnoreDangling` is `Dag.drainClosure`'s closure walk, the one `Dag.ancestorsOf` runs, which skips a parent the DAG does not hold, and the held-parent filter of the drain that follows — the parent never enters the closure, is filtered out of the in-degree, constrains nothing, and the drain proceeds; it is the policy on the fold path (`drainClosure` to `between` to `betweenOps` to `reconcileMany` to `foldOnce`) and on `Dag.tryReplayTo`. `RefuseDangling` is `Dag.firstBreak`'s `MissingParent`, hence `verifyDag` and `fromJsonlVerified`, which refuse the whole set before any drain runs. `drain_policies_agree` proves the two are the same function on a set holding every parent it names, so the fold path pays nothing for the refusing one's existence; `drain_refusal_characterised` proves the refusal fires exactly when a parent lies outside, and `drain_refusal_needs_a_dangler` the converse. The refusal names the SMALLEST offending id rather than the first in the work list — an order-invariant choice, which is what lets `drain_deterministic` cover the refusing policy too.

  _Boundary:_ Production's `firstBreak` makes another order-invariant choice — it scans the whole DAG's drain, naming the EARLIEST faulty node — and which node production names among several is not modelled. NOT modelled: the refusal's diagnostic payload — `firstBreak` reports the missing parent beside the node, and the node determines it.

- <a id="row-dag-delta-recovery-merged-head"></a>**`dag-delta-recovery-merged-head`** — `between_merged` · fuaran-core#158

  Delta recovery over a MERGED HEAD — the shape a clone folds after it has folded once and pulled again. For a lane hanging off ANY node `m` the DAG holds — a merge node with the whole first round beneath it included — `Dag.between m head` under production's own order, section 13's id-ordered drain over the head's closure (`topo_drain`, `between_drained`), is that lane's nodes in append order and nothing else: not the first round beneath `m`, and nothing from a sibling lane (`between_merged`; the ops, `between_ops_merged`). The drain's order beneath `m` is a genuine choice and the theorem does not depend on which way it went: the difference against `m`'s closure deletes every node the choice was about, and what survives is a spine, where a parent-respecting order is forced (`spine_ids_forced`).

  _With:_ `follows_spine_diff`, `distinct_diff`.

  _Boundary:_ Nothing is asked of what lies beneath `m` beyond acyclicity. PREMISES, and no new one: id distinctness exactly as `fuaran-core#134` states it, read at this granularity — every lane node is the node the DAG holds under its own id, and no lane id is an id in `m`'s closure, which is 'the base id is not one of the lane's' at a base whose closure is more than itself (`merged_recovers`, `lane_ids_distinct`; `node-ids-distinct`); acyclicity as section 13 takes it, a topological enumeration of the head's closure supplied as a witness (`head_drains`); and the model's own fuel bound, stated as a computable predicate (`walk_ok`) and discharged by `anc_stable` — a walk that never ran out is the same list under any longer fuel. NOT claimed: a second-round lane that itself merges before the fold, whose delta is no spine. The fuel is the model's own; production's work-list walk has none. A delta that itself merges is produced neither by `foldOnce` nor by the fold-pull-fold rhythm; a consumer that builds one is outside every row here.

- <a id="row-fold-from-a-merged-dag"></a>**`fold-from-a-merged-dag`** — `reconcile_many_merged_eq` · fuaran-core#158

  `Dag.reconcileMany` and `FoldConfluence.foldOnce` FROM a merged head equal the deltas-first fold on the lanes that were appended off it (`reconcile_many_merged_eq`, `fold_once_merged_eq`) — `fold-from-the-dag`, over the shape a clone folds after a pull, under the drain production runs rather than the parent-walk reversal.

  _Boundary:_ Premises: one acyclicity witness per head (`lanes_merged_ok`); otherwise the premises of `dag-delta-recovery-merged-head` and nothing more.

- <a id="row-dag-checkpoint-replay"></a>**`dag-checkpoint-replay`** — `replay_from_is_replay_to` · fuaran-core#437

  Replay from a sealed checkpoint IS replay from genesis (`replay_from_is_replay_to`): `Dag.replayFrom` from the checkpoint `Dag.checkpointAt` sealed answers `Dag.tryReplayTo`'s answer — the state on `Ok`, the rejecting node and reason on `Rejected`. `checkpoint_at`, `seal_at`, `replay_from` and its `above` check are production's clause for clause and refusal for refusal. The load-bearing lemma is `drain_restricts`: the id-ordered drain of a head's closure, kept to the closure of a node inside it, IS that node's own drain — production's docstring sentence, as a theorem — and with every id above the checkpoint descending from it (what `above` checks) the head's drain is the checkpoint's drain followed by the history above it (`drain_split`), which `replay_ids_app` folds in two. `checkpoint_from_is_checkpoint_at`: a checkpoint `Dag.checkpointFrom` takes from an earlier one is the one `checkpointAt` takes from genesis, node, state and seal. GO-RED, run at the leg: a `checkpoint_at` that seals a node other than the one whose closure it folded makes the theorem unprovable (`cp.cnode == c` fails).

  _Boundary:_ Over an id-distinct DAG whose head and checkpoint closures carry section 13's acyclicity witness, and only on heads the replay does not refuse as unreached or uncovered. The seal is a parameter; production's is the strict snapshot hash, which the model does not compute.

- <a id="row-dag-checkpoint-truncation"></a>**`dag-checkpoint-truncation`** — `truncation_preserves_replay` · fuaran-core#437

  Truncating behind a checkpoint preserves every replay at or after it (`truncation_preserves_replay`): for ANY kept set meeting `cut`'s contract — the checkpoint's node kept, every node not behind it kept, and every kept node behind it still reaching it in the truncated DAG (the band) — `Dag.replayFrom` on the truncated DAG answers what it answers on the full one, whatever state the checkpoint carries. The contract is `truncation_ok`, computable and extracted, and the `dag-primitives-differential` row evaluates it on every DAG production's `Dag.compactAt` returns. The proof resumes both drains after the checkpoint's closure (`kahn_after`) and shows the drain reads only which parents are still pending (`kahn_pending`) and not its fuel (`kahn_fuel`), so the history above the checkpoint is one list in both DAGs. `cut`'s computation of the kept set is modelled and proved to meet the contract (`dag-compaction-truncation`).

  _Boundary:_ Only for kept sets meeting `truncation_ok`, and only on heads the full replay does not refuse as unreached or uncovered. NOT modelled: the seal's hash, `firstBreakFrom` / `verifyDagFrom` and the JSONL sidecar — `dag-checkpoint-truncation-sampled` samples those.

- <a id="row-conflicts-of-heads-is-the-sweep"></a>**`conflicts-of-heads-is-the-sweep`** — `conflicts_of_heads_is_the_sweep` · fuaran-core#437

  `Dag.conflictsOfHeads` reports exactly the interfering pairs the proved pairwise sweep reports (`conflicts_of_heads_is_the_sweep`): renaming each reported NODE to its op gives the `LanesInterfere` report `reconcileMany` refuses with, conflict for conflict and in order, so it is empty exactly when that sweep is. `Dag.conflictsOfNodes` is modelled as production defines it, `conflicts` at `fun n -> footprintOf n.Op`, and the renaming lemmas (`map_all_conflicts` and below) hold for any projection.

  _Boundary:_ Over the region partitioned as section 15 partitions it; within the model, the correspondence to production is the differential's (`dag-primitives-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-reach-index-agrees"></a>**`reach-index-agrees`** — `reach_of_dag_agrees` · fuaran-core#437

  The reachability index answers exactly what the ancestor walk answers (`reach_of_dag_agrees`): `Reach.reaches` is `Set.contains ancestor (Dag.ancestorsOf dag descendant)` and `Reach.ancestors` is `Dag.ancestorsOf`, with production's fallback to the walk for an unslotted id included. The index is modelled as its slots, each an id and its ancestor set, built by `Reach.ofDag`'s drain loop and `Reach.extend`'s leaf step (one `reach_step`); the invariant is per slot and is kept by every step (`reach_step_valid`), so it holds whatever order the steps run in. The index's `mergeBase` / `commonBase` / `between` / `tryTopoOrder` are modelled over the slots (`reach-merge-base-agrees` and its three siblings).

  _Boundary:_ For every descendant the walk completes on. NOT modelled: the bitset's word layout and the position arrays, read as sets under the slot-agreement abstraction `dag-outside-the-model` states.

- <a id="row-replay-to-fold-over-drain"></a>**`replay-to-fold-over-drain`** — `replay_to_is_fold_over_drain` · fuaran-core#309

  `Dag.tryReplayTo` (the replay every store the plane holds goes through, `replayClosure` at one root) is the fold over the PROVED drain: the model's `replay_to` — clause for clause: refuse an unknown head, drain the head's closure with the id-ordered drain, refuse a drain that did not place the whole closure as `Cyclic`, then apply each drained node's op in order, the first rejection naming its node — never reports a cycle, its order is a topological enumeration of the closure, and its outcome is `DagFold.fst` section 3's `replay` over the ops of the drained nodes (the same state on `Ok`, the same rejection under `Rejected`).

  _Boundary:_ Holds over an id-distinct DAG and a head it holds whose closure is acyclic (section 13's witness form, `head_drains`). Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-replay-to-unknown-head-refused"></a>**`replay-to-unknown-head-refused`** — `replay_to_unknown_head_is_refused` · fuaran-core#309

  `Dag.tryReplayTo` answers `UnknownHead` EXACTLY for a head the DAG does not hold, naming it — never the initial state as `Ok` for a typo'd head, and never for a head the DAG holds.

  _Boundary:_ Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-replay-to-deterministic"></a>**`replay-to-deterministic`** — `replay_to_deterministic` · fuaran-core#309

  Two clones holding the same id-distinct node set, received in any order, replay any head to the same outcome — state, rejection and refusal alike: `drain_deterministic` carried through the closure walk and the node lookup, which proves the sentence 'two convergent histories over the same node set replay to the same state' in `tryReplayTo`'s docstring.

  _Boundary:_ Requires the node set to be id-distinct. Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-append-refuses-differing-node"></a>**`append-refuses-differing-node`** — `append_refuses_differing_node` · fuaran-core#309

  `Dag.append` / `Dag.merge` (`addNode`) refuse, by name, a node whose content id the DAG already holds for content production's `sameNode` does not accept (`ContentIdCollision`), and an accepted append NEVER REPLACES: every node held before is the node held after, and an id-distinct DAG stays id-distinct (`append_never_replaces`). This is what narrows `node-ids-distinct` from 'node ids are distinct' to 'the hash is injective on the nodes a store holds'.

  _Boundary:_ `sameNode` is a parameter of the model; only the differential ties it to production.

- <a id="row-total-order-by-linear-extension"></a>**`total-order-by-linear-extension`** — `total_order_by_is_a_linear_extension` · fuaran-core#311

  `Dag.totalOrderBy key` at ANY key is section 13's drain at a total order: the frontier's comparison — the key first, the id breaking ties, read through the node set by id — is proved a total order whenever the key order and the id order are (`by_key_total`), so the drain at it places every node once after its in-set parents (`total_order_by_is_a_linear_extension`), places every node of an acyclic set and yields a topological enumeration (`total_order_by_total_on_acyclic`), and is a function of the node set under every arrival order (`total_order_by_deterministic`). The three downstream orders — the smallest id, a Lamport rank then lane then id, a domain rank then id — are this drain at three keys.

  _Boundary:_ The key order is a PARAMETER (any strict total order), as the id order is, and the comparison is a total order only when both are; placing every node is claimed of an acyclic set. NOT claimed: that any particular key is the right one for a domain, which is the domain's to choose.

**Tested** (6)

- <a id="row-reconcile-differential"></a>**`reconcile-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#300

  The extracted partition (`reconcile_ids` over the model's own closures and drain) is production's `Dag.reconcile` script over the four shapes the old rule got wrong — disjoint, fast-forward, duplicate head and criss-cross over `mergeBase` — with the old rule shown to differ from it on the pool; and the extracted checked fold renders what production's `foldOnce` renders over a pool of lane sets carrying a lane that does not apply, identically under every arrival order.

  _Boundary:_ Agreement over the sampled shapes and lane-set pools only, never over all inputs.

- <a id="row-delta-recovery-differential"></a>**`delta-recovery-differential`** — `Proofs.Oracle`, 7 cases · fuaran-core#134

  Production's own ancestor closure, topological order and delta recovery, on the DAG the fold's generator actually builds, agree per lane with the extracted model's recovery over the SAME nodes across three lane pools — the ids the model walks are the content hashes production minted, so a disagreement can only be about the recovery and never about the hash. A DAG whose lanes hang off the wrong parent is required to make the comparison fail, and two vacuity guards refuse a run that recovered no ops or walked no chain longer than one node.

  _Boundary:_ Agreement over the three lane pools drawn, on the DAG the fold's generator builds, only — never over all inputs. The DAG-shaped fold case runs `fold_once_dag` against production and against the deltas-first oracle beside it, so a divergence says which half moved. The wrong-parent go-red leaves every id untouched, so every lookup still succeeds and only the shape the recovery walks has moved.

- <a id="row-general-drain-differential"></a>**`general-drain-differential`** — `Proofs.Oracle`, 6 cases · fuaran-core#156

  The extracted drain, instantiated at `String.CompareOrdinal`, run beside `Dag.tryTopoOrder` over the same nodes — per LANE HEAD, whose closure is a spine and where no tie-break can be wrong, and per MULTI-HEAD UNION built with `Dag.merge`, where the frontier is N wide once the base is drained and the tie-break decides the sequence. The adequacy guard is the frontier WIDTH: a run whose frontier never passed one node re-measured the spine under another name, and the case fails by number rather than passing quietly. The go-red is a model draining LARGEST-id-first — as legitimate a selector as the model's own, since it still returns a member of the frontier it is handed — run over the unions and not the lane heads, because a one-element frontier has one minimum and one maximum. Three cases ride with it: every permutation of the node set drained and compared against the set's own drain; the two policies, agreeing on a parent-closed closure and diverging the moment the base node is dropped from it; and a hand-built cyclic set, unreachable through `Dag.append`, where production's `isAcyclic` must see the cycle and the model's drain must stop at the same short prefix, with an acyclic control beside it.

  _Boundary:_ Agreement is over the sets drawn, never over all inputs.

- <a id="row-merged-head-differential"></a>**`merged-head-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#158

  The extracted recovery and fold, and `merge_base_now` (production's `deepest`), at `String.CompareOrdinal`, run beside production's `Dag.betweenOps`, `Dag.reconcileMany` and `Dag.mergeBase` over FOLD-PULL-FOLD unions built through production's own `Dag.append` / `Dag.merge`: round one's lanes off a base, their heads folded into one convergent head, round two's lanes appended off it. Per round-two head the delta is compared three ways — production's, the model's, and the lane that was appended, which is the theorem's own right-hand side — and the model's fuel premise is EVALUATED on every DAG, so a green run is one the theorems are about. Per pair of round-two heads the merge base is compared three ways likewise: production's, the model's, and the merged head. Adequacy guards: ops were recovered, a chain was walked, a merge base was located, and the merged head really had two-parent nodes beneath it — without the last the case would re-measure the earlier, unmerged shape. GO-RED: a locator that is the model's own `max_by` over the model's own intersection at the REVERSED key names the shallowest common ancestor — the ORIGINAL BASE rather than the divergence point — and must lose twice, on the merge base and on the delta recovered from the base it names.

  _Boundary:_ Agreement over the sampled FOLD-PULL-FOLD unions only, at `String.CompareOrdinal`, never over all inputs.

- <a id="row-dag-primitives-differential"></a>**`dag-primitives-differential`** — `Proofs.Oracle`, 5 cases · fuaran-core#437

  The extracted section 18 to 21 functions (and `DagPrimitives`' `merge_base_now`) run beside production's over DAGs a clone holds: `merge_base_now` beside `Dag.mergeBase` and `Dag.Reach.mergeBase` over criss-crosses, the answer checked to be a maximal common ancestor (guard: pairs with two maximal common ancestors were reached); `conflicts_of_heads` beside `Dag.conflictsOfHeads` over the four reconcile shapes under the domain's footprint, read canonically, and the report empty exactly when `reconcileMany` does not refuse with `LanesInterfere` (guards: both arms); the extracted index beside `Dag.Reach` and the walk over every ordered pair; and `checkpoint_at` / `replay_from` beside `Dag.checkpointAt` / `Dag.replayFrom` at every node and head of fold-pull-fold DAGs carrying a side merge, with `Dag.compactAt` at every node — its kept set held to `truncation_ok`, its DAG to the model's `truncate`, and its replays to the full DAG's (guards: a resumed replay, an uncovered and an unreached refusal, a compaction, a compaction keeping a band, a refused compaction). GO-RED: the checkpoint production took, handed to the model sealed at another node, disagrees with production.

  _Boundary:_ Agreement over the sampled DAGs only (DAGs a clone holds, the four reconcile shapes, fold-pull-fold DAGs carrying a side merge), never over all DAGs.

- <a id="row-replay-to-differential"></a>**`replay-to-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#309

  The extracted `replay_to` renders exactly what production's `Dag.tryReplayTo` renders — the state through the domain's own hash, a refusal by its case and the id it names — at every lane head, the base and the merged union head (whose closure is every node, so the drain's tie-break decides the order the ops are applied in) over two domains, plus a head the DAG does not hold, plus a hand-built cyclic closure and its acyclic control. The comparison can lose: a model handed a store that lost one node of the union head's closure disagrees with production. And the extracted `add_node` refuses and deduplicates exactly where production's `addNode` does.

  _Boundary:_ Agreement is over the pools drawn, never over all inputs.

**Rests on, or is bounded by:** [`dag-outside-the-model`](#row-dag-outside-the-model) (assumed), [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`node-ids-distinct`](#row-node-ids-distinct) (assumed).

### `WireDecode`

[`WireDecode.fst`](WireDecode.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`.

> WireDecode — an F\* model of Fuaran.Core's wire DECODE combinators, with decoder totality as a
> machine-checked theorem (fuaran-core Phase 135).

**What is modelled.** `Fuaran.Core.Decode` (src/Fuaran.Core.Wire/Decode.fs), clause for clause:
`getProp`, `asString` / `asInt` / `asBool` / `asFloat`, `kindOf`, `strField` / `intField`, and
`mapList` — the array walker, in its accumulator-and-`rev` form, which is what production is.
On top of them a REFERENCE VOCABULARY (`rnode`) with its encoder and the kind-dispatch node
decoder a domain writes: read the `"kind"` tag, match it, read the declared members, walk the
child array. And, separately, the Phase 102 read policy — see the boundary note below.

**Proved** (12)

- <a id="row-decode-total"></a>**`decode-total`** — `decode_total` · fuaran-core#135

  Every decode combinator reaches exactly one outcome on every input, and which one is characterised structurally — so the failure classification is exhaustive rather than merely non-empty.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Everything begins at a `JVal` that already exists: `Json.parse` and `Decoder.parse` are not modelled, so 'the decoder is total' is spent on the combinator layer alone and says nothing about the bytes before that value exists (the parser's totality is `JsonParse.fst`'s). The characterisation includes `as_float` succeeding on an int as well as a float, the numeric normalisation a reader must not assume away. The numeric payloads are opaque carriers and `as_float` takes the widening as a parameter: no combinator looks inside a number, so nothing is said about Int32 range or float precision, while the `JInt`/`JFloat` distinction is modelled. Proved under no hypothesis about a domain.

- <a id="row-decode-node-wf"></a>**`decode-node-wf`** — `decode_node_wf` · fuaran-core#135

  The recursive node decoder is total on an arbitrary JSON value and succeeds on exactly the well-formed documents. Termination is content here rather than a formality: the decoder descends into a child array obtained by name, so nothing structural is visible at the call site.

  _With:_ `decode_node_total`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). `wf` is written by hand, independently of the decoder, which is what makes the characterisation evidence; it is not restated over the generated vocabularies (see `vocabulary-round-trip`). `Versioning.decodeTolerant`, the shipped generic instance of the kind-dispatch pattern, is not modelled: its `requiredProfile` read goes through `Versioning.Capability.tryParse`, string-splitting at a different layer. The pattern is modelled where a domain meets it, as `decode_node`.

- <a id="row-decode-encode-roundtrip"></a>**`decode-encode-roundtrip`** — `decode_encode_roundtrip` · fuaran-core#135

  Decoding an encoded node returns that node, for every node of the reference vocabulary, at every depth.

  _Boundary:_ Only nodes of the reference vocabulary, and within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Nothing here is about a domain's own decoder: what carries to one is the combinator layer it is built from, not the reference vocabulary's clauses. The generated vocabularies' round trips are the `vocabulary-round-trip` rows, and a host's hand-written decoder is certified against its corpus, not here.

- <a id="row-lenient-agrees-off-policy"></a>**`lenient-agrees-off-policy`** — `lenient_agrees_off_policy` · fuaran-core#135

  On every document the read policy does not name, the tolerant and strict readers accept the same values and refuse the same documents; on a document with no null at all the two are literally the same function.

  _With:_ `strict_unchanged_on_null_free`.

  _Boundary:_ A promise about the VERDICT, not about the message; documents the read policy names are outside it. The refusals differ on purpose at the two positions the tolerant policy declines to erase, a bare root null and an array element, since the remedy differs. The policy is modelled where production has it, as a document-level read normalisation (`read : null_policy -> jvaln -> outcome jval`) upstream of every combinator: `NullPolicy` is a parameter of a read and of nothing else, and no combinator takes one.

- <a id="row-decode-perm-invariant"></a>**`decode-perm-invariant`** — `decode_perm_invariant` · fuaran-core#152

  WIRE_FORMAT §2 rule 2's obligation on the decoder, as a theorem: on two documents that differ only by the ORDER of object members, at any depth, every decode combinator answers the same. The scalar readers, kindOf, strField, intField and mapList return EQUAL outcomes, message included; getProp, whose result is itself a reordered subtree, returns a related one.

  _With:_ `map_list_go_perm`, `perm_as_int`.

  _Boundary:_ Duplicate-free member lists only — see duplicate-keys-break-order-invariance for why that premise cannot be dropped. getProp's answer is related, not equal. Arrays are related pointwise, never permuted, because an array's order is content. Nothing is said about the encoder's key order on the way out.

- <a id="row-decode-node-order-invariant"></a>**`decode-node-order-invariant`** — `decode_node_perm_invariant` · fuaran-core#152

  WIRE_FORMAT §20's one-answer rule at this layer: the kind-dispatch node decoder returns the SAME TREE on a document whose object members have been reordered at any depth — literally equal, since a decoded node carries no members of its own — or the same named refusal.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-perm-covers-reorder"></a>**`perm-covers-reorder`** — `perm_covers_reorder` · fuaran-core#152

  The converse direction, which is what keeps the order invariance from being vacuous: the relation CONTAINS every reordering of a duplicate-free member list whose values are themselves related — one level of a deep reordering, with the scalar arms as the base cases. A relation nothing is related by would satisfy every invariance lemma and say nothing.

  _With:_ `member_perm_refl`, `perm_covers_selection`.

  _Boundary:_ Only reorderings of duplicate-free member lists whose values are themselves related, one level at a time; within the model, the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). `member_perm` is not proved transitive or symmetric; nothing needs either, and neither is asserted.

- <a id="row-duplicate-keys-break-order-invariance"></a>**`duplicate-keys-break-order-invariance`** — `duplicate_keys_break_order_invariance` · fuaran-core#152

  The duplicate-free premise is NECESSARY, not convenient: two documents that are the same members in a different order, whose keys repeat, and getProp answers differently. The premise is therefore carried by this theorem rather than inherited, and a session weakening the relation to a plain list permutation reddens this lemma first.

  _Boundary:_ Nothing upstream excludes such documents: the parser refuses a repeated name (`DuplicateMember`), but a value built in memory can still carry one, and on it order invariance does not hold. On such a value `getProp` answers with the FIRST member of the name, so there the answer is whichever came first.

- <a id="row-no-null-ever"></a>**`no-null-ever`** — `no_null_ever` · fuaran-core#153

  NO `JVal` CONTAINS A NULL, at any depth — WIRE_FORMAT section 2 rule 4's value half. Stated over the one model here that HAS a null, the foreign document model `jvaln` of `WireDecode.fst`'s read policy, so it is a statement and not a tautology: every wire value, viewed as a document, is null-free and reads back as EXACTLY itself under either read policy; the strict reader accepts EXACTLY the null-free documents, an iff; and whatever EITHER reader returns is null-free — the tolerant policy erases an object-member null, it never carries one through. Proved under no hypothesis at all.

  _With:_ `embed_is_null_free`, `strict_read_refuses_null`.

  _Boundary:_ NOT claimed: anything about a host, which may hold nulls of its own in its own types; this is about what a document can contain once it is a `JVal`. The encoder's half is a separate row, `no-null-rendered`. No differential of its own: the lemma is ghost, so nothing extracts to run beside production. What stands behind it at level 2 is `decode-differential`'s read-policy pool, which carries the null token at every position under both policies.

- <a id="row-no-invokable-term"></a>**`no-invokable-term`** — `no_invokable` · fuaran-core#153

  NO VALUE A DECODE COMBINATOR RETURNS CAN CARRY A FUNCTION, at any depth — the type half of 'a closure crosses the wire only as the string "<closure>"' (section 2 rule 10, section 4). The lemma is that every type a combinator or the node decoder returns, and the wire value and the foreign document model themselves, has DECIDABLE EQUALITY: F\* derives `hasEq` for an inductive only when every constructor argument has it and derives it for no arrow type, so this is the prover's own certificate that the closed value type has no function-typed component. The closure sentinel is a string like any other. The control was run both ways: a type with one function-typed constructor argument cannot be declared without `noeq`, and declared with it the same lemma fails to prove.

  _Boundary:_ NOT claimed: anything about a HOST, a `Custom` renderer or a host-call seam — a registered function is host code the wire merely NAMES, which the UI language's escape-hatch inventory enumerates and these lemmas do not — nor about a decoder handed to the array walker, which is the domain's own function and not a wire value. The numeric carriers are `eqtype` parameters, as everywhere in the model, and the certificate is relative to that. Nor does it cover a GENERATED vocabulary's closure slot: that the generated decoders never read it is a property of the emitted text, which the Proofs.Vocabulary family holds to a fresh generation, and not of these lemmas.

- <a id="row-sentinel-inert"></a>**`sentinel-inert`** — `sentinel_inert` · fuaran-core#153

  NO COMBINATOR INSPECTS A STRING VALUE, so the closure sentinel is inert. Replacing one string by another throughout a document COMMUTES with every combinator — under no premise, and word for word: every refusal is the same refusal, message included (`sentinel_inert_combinators`) — and with the node decoder: the same documents decode, the same documents are refused, and the decoded node is the original with the same replacement made. The sentinel is the instance (`closure_sentinel_is_a_string`), not a special case. Decoding is EQUIVARIANT under the replacement, which is stronger than invariance and is the true statement.

  _With:_ `sentinel_inert_combinators`, `closure_sentinel_is_a_string`, `closure_sentinel_is_not_a_tag`.

  _Boundary:_ Member KEYS are untouched, since a key is a name the decoder asks for and not a value it is handed. Do not read this as decoding being INVARIANT under replacing the sentinel with ANY string: the node-level premise is exactly that neither string is one of the vocabulary's own kind tags, which the sentinel satisfies, and 'any' fails in that one position — see `sentinel-tag-position-is-the-exception`. At node level the relation is `inert`, the replaced node or a refusal on both sides, and the message is not part of it: `unknown kind: <tag>` quotes the tag, which is itself a string value and is replaced like any other.

- <a id="row-sentinel-tag-position-is-the-exception"></a>**`sentinel-tag-position-is-the-exception`** — `tag_position_is_the_exception` · fuaran-core#153

  The premise of `sentinel-inert` is NECESSARY, and it is the vocabulary's own dispatch rather than any combinator that makes it so: a document whose `kind` member IS the sentinel is refused as an unknown kind, and rewriting the sentinel into a kind tag makes it decode, and the two outcomes are proved NOT related. That is a domain reading its own discriminator, not a payload being inspected, and a session weakening the premise goes red here first.

  _Boundary:_ 'Replace it with any other string' is false in exactly the tag position; the sentinel's inertness at node level holds only where neither string is a kind tag.

**Tested** (2)

- <a id="row-decode-order-differential"></a>**`decode-order-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#152

  Every corpus fixture and every generated document, with the members of every object reordered at every depth, seeded and replayable: production's answers on the shuffle are compared against its own answers on the original — equality for every combinator whose result carries no members, the extracted outcome_perm for getProp, whose result is itself reordered — and the oracle is compared against production on the shuffled document. This is the case the corpus structurally cannot present, every fixture in it being canonically ordered. Each pair is checked to be an instance of the extracted member_perm, and a getProp that reads the FIRST member rather than the named one is required to lose.

  _Boundary:_ Agreement over the corpus fixtures, the generated documents and the seeded shuffles drawn, never over all inputs; duplicate-keyed documents are filtered out by the extracted keys_unique_deep. The go-red case also asserts that its shuffles actually moved a member and that every drawn pair is related by the extracted `member_perm`, so a failure there is the decoder's and not the probe's.

- <a id="row-decode-differential"></a>**`decode-differential`** — `Proofs.Oracle`, 7 cases · fuaran-core#135

  The extracted decode model agrees with the shipped combinators over the wire corpus and a generated sample, compared on outcome CLASS, decoded VALUE and error MESSAGE at once — a model agreeing on accept-versus-refuse alone would not notice a decoder naming the wrong expectation. A blind bridge reading every integer as a float is required to make the comparison fail, so a green report is known to be one that can lose.

  _Boundary:_ Agreement over the wire corpus and the generated sample only, never over all inputs. Each pool asserts that both outcome classes are exercised where it reaches them. The read-policy pool renders the null token at every position to wire text, reads it under BOTH policies and asserts that the policy fired. The blind bridge is required to disagree at `asInt`, on a hand-made value and over the generated sample.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `TreeOps`

[`TreeOps.fst`](TreeOps.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `DagFold`.

> TreeOps — an F\* model of Fuaran.Core's skeleton-op tree algebra, with the fold theorem's one
> DOMAIN HYPOTHESIS proved for it rather than sampled (fuaran-core Phase 133).

**What is modelled.** `Ops.apply` over the `NodeWitness`/`IdWitness` view of a tree: a node is an
id, a kind tag and an ordered child list, and nothing else is visible to the algebra. The six
skeleton ops (`InsertChild`, `RemoveNode`, `MoveNode`, `ReorderChildren`, `Batch`, and since
Phase 250 `UpdateNode`), each validation clause and each `Rejection` it raises, and
`Ops.footprint` clause for clause. `UpdateNode`'s content is the one piece of a node the witness
shows — its kind tag — so an in-place rewrite is modelled as a rewrite of the kind, children
kept, which is everything the shipped op can do that the algebra can see.

**Proved** (14)

- <a id="row-tree-independence-diamond"></a>**`tree-independence-diamond`** — `tree_independence_diamond` · fuaran-core#133

  For the skeleton-op tree algebra, two operations whose footprints are declared disjoint and which both apply at an id-unique tree each apply after the other and reach the same tree — the fold theorem's domain hypothesis, proved rather than sampled, for all TWENTY-ONE unordered operation pairs: the fifteen with the diamond lifted along a batch's script (the batch pairs are `tree-algebra-batch-nesting`, a proved row), and the six `UpdateNode` pairs, which close by the same footprint elimination as a remove or a move. Nine of the fifteen are closed by the conservative footprint alone and never look at a tree: a remove or a move writes an unknown parent, independence refuses that against any structural write, and every skeleton operation except a do-nothing batch writes structure — so such a pair is independent only where the other operation does nothing at all.

  _With:_ `relocating_forces_inert`, `update_is_relocating`, `wstep_sym`, `independent_sym`, `ins_ins_comm`, `ins_reorder_comm`, `reorder_reorder_comm`.

  _Boundary:_ Holds only for footprints declared disjoint, both operations applicable, and an id-unique tree (`tree-algebra-well-formed-states`). Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). The pairs the footprint does not close rest on three commutation equalities on the tree (insert/insert, insert/reorder, reorder/reorder), and one of them rests on a footprint clause: `Ops.footprint` puts an operation's structural parent into `Reads` as well as `StructureWrites`, which is what guarantees neither insert's subtree carries the other's anchor; without it two independent inserts would not commute. The footprint is the pinned over-approximation, conservative rather than tight: nothing here carries to a tighter footprint, which needs its own proof (`footprint-in-sound`).

- <a id="row-footprint-precision-ceiling"></a>**`footprint-precision-ceiling`** — `relocation_clause_is_necessary` · fuaran-core#143

  The unknown-parent over-approximation in `Ops.independent` is NECESSARY over the `Footprint` record as it stands, not merely conservative: no predicate over its six address sets, the four node sets and the two slot sets, can widen the promise. Proved at one well-formed tree by three facts — a relocation that MOVES a node and a structural write under a parent inside the moved subtree do commute, with every clause but the pinned relocation pair already satisfied (`relocation_disjoint_diamond`); the same shape with a REMOVE in it does not, because the structural write's parent is destroyed with the subtree (`relocation_diamond_fails_for_a_remove`); and the two operations carry byte-identical footprints (`relocation_footprints_coincide`). One verdict therefore covers both, so freeing the safe pair frees the fatal one. A single witness refutes the universal claim. The second pinned over-approximation — a remove's content-write records the target id and not its tree-unknown subtree — is thereby shown to be load-bearing for the first, as STABILITY.md asserts in prose.

  _With:_ `relocation_disjoint_diamond`, `relocation_diamond_fails_for_a_remove`, `relocation_footprints_coincide`, `but_for_relocation`.

  _Boundary:_ Necessity is over the current `Footprint` record only. NOT claimed: anything about what a WIDER record would admit, or the general move-versus-structural-write theorem, which is true and deliberately unproved because no clause could consume it. The fatal operation in the witness is a batch that removes and then reorders. Its footprint coincides with the move's across all six address sets (the two slot sets are empty for every skeleton operation, so the four sets that matter are the node sets), because `union_fp` is a union and neither the operation's shape nor the direction of its structural write survives it. A tighter verdict therefore needs a footprint that can NAME the difference, a relocation kind or a destroyed-subtree set the pure script cannot compute; the ceiling binds the PURE footprint, and the base-aware `Ops.footprintIn` resolves those sets against the base tree (`footprint-in-sound`).

- <a id="row-relocation-pairs-still-refused"></a>**`relocation-pairs-still-refused`** — `relocation_move_pair_also_fails` · fuaran-core#143

  The pairs the pinned clause refuses, enumerated: exactly {a relocating operation} x {an operation that is not inert}, both ways round — `still_refused`, the contrapositive of `relocating_forces_inert`. Part of that set is GENUINELY dependent and could not be freed by any footprint record whatever: two `MoveNode`s whose destinations sit inside each other's subtrees each apply alone and reject each other with `WouldNestUnderSelf`, because the obstruction is the cycle check rather than the addresses (`relocation_move_pair_also_fails`). So the refused set is not one homogeneous class waiting on a better footprint.

  _With:_ `still_refused`, `relocating_forces_inert`.

  _Boundary:_ No claim is made about the remove-versus-remove, remove-versus-move or remove-versus-reorder members, which are unexamined. `still_refused` records, per class, whether a member is known to commute (move x insert, move x reorder), known not to (remove x insert, move x move), or unexamined. The enumeration is of the PURE footprint's refusals; the remove pairs the base-aware footprint frees are proved to commute at `footprint-in-sound`.

- <a id="row-insert-breaks-id-uniqueness"></a>**`insert-breaks-id-uniqueness`** — `insert_breaks_wf_pre137` · fuaran-core#133

  HISTORICAL, and kept rather than deleted: the unconditional statement 'every accepted skeleton operation preserves id uniqueness' is refuted against the superseded validator, which tested the inserted node's OWN id against the tree and not its descendants, so a concrete accepted insert left the tree carrying an id twice — the counterexample and the clause it is about are `insert_breaks_wf_pre137` and `apply_pre137`. Proved beside it: the conditional form `ins_wf` and its converse `ins_wf_conv`, which are the specification the fix had to meet.

  _With:_ `ins_wf`, `ins_wf_conv`.

  _Boundary:_ Not a claim about the LIVE algebra: against the current validator the refuted statement is TRUE and proved, at `apply-preserves-wf`, and that the fix closes THIS counterexample is proved at `insert-subtree-check-closes-it`. The refutation is about the superseded clause only.

- <a id="row-tree-digest-preimage-injective"></a>**`tree-digest-preimage-injective`** — `preorder_arity_injective` · fuaran-core#290

  THE DIGEST SEES THE SHAPE. The pre-image `Tree.contentHash`, `Tree.encodePreimage` and `Tree.encodeHash` fold — every preorder node as its label and its ARITY — is injective over ordered trees: two trees with one (label, arity) preorder are one tree (`preorder_arity_injective`, no hypothesis; the label is the (id, kind) pair the witness shows and the arity is computed). The induction is `shape_prefix`: a shape followed by any suffix is read back uniquely, because the arity says how many of the following entries belong under the node. The arity is load-bearing rather than decorative, and the premise is evaluated beside it (`preorder_alone_aliases`): the label preorder alone identifies `root(a(a1,a2), b(b1))` with `root(a(a1,a2,b(b1)))`, which are two trees with two shapes and are one accepted `MoveNode(b, a)` apart — so a memo keyed on an arity-free `encodeHash` serves one function's tree for the other.

  _With:_ `shape_prefix`, `preorder_alone_aliases`.

  _Boundary:_ Within the model, over the witness's (id, kind) label; the correspondence to the string production folds is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-tree-digest-fields-recover-shape"></a>**`tree-digest-fields-recover-shape`** — `digest_fields_injective` · fuaran-core#290

  The FIELD LIST recovers the shape: the two fields per node the F# fold writes — the rendered label, then the arity as a numeral — are read back to the (label, arity) preorder, and so to one tree (`digest_fields_injective`, composing `fields_recover_shape` with `preorder_arity_injective`). The step from the field list to the ONE string the hash reads is `Hash.canonicalFields`, proved injective as `enc_injective` in `proofs/Query.fst` (row `query-invocation-key-injective`) and not restated.

  _With:_ `fields_recover_shape`.

  _Boundary:_ CONDITIONAL, and named as such: the label renderer (production's caller-supplied `encode`) and the numeral renderer (`string` on an int) enter as parameters with their injectivity as hypotheses — the encoder-injectivity obligation `Conformance.encoderInjectivityLawsAt` samples at a domain's witness, and the numeral premise `seq-numeral-code-certified` already carries for the chain. Whether two distinct pre-images HASH apart under FNV-1a is a claim about FNV-1a and is not made; `Function.applyMemo` keys on the pre-image itself for that reason.

- <a id="row-tree-algebra-batch-nesting"></a>**`tree-algebra-batch-nesting`** — `op_independence_diamond` · fuaran-core#162

  The diamond covers the pairs where either side is a batch that neither does nothing nor relocates, so it holds over the whole of the skeleton operations (`op_independence_diamond`): independence descends through a union, the leaf diamond lifts along a batch script by induction threading the id-uniqueness invariant at each intermediate state of the script — the argument the fold theorem performs at lane granularity — and the guarded algebra's diamond follows.

  _With:_ `leaf_independence_diamond`, `no_reloc_preserves_wf`, `reorder_wf`, `batch_lift_is_not_vacuous`.

  _Boundary:_ The lift needs the id-uniqueness invariant at each intermediate state of the script. It does NOT consume `apply-preserves-wf`: these pairs are exactly those where neither side relocates, so the operations involved are inserts, reorders and nests of them, whose per-operation preservation lemmas are in the tree model — and `Preservation.fst` opens `TreeOps.fst`, so it could not be cited from it in any case. The widening is pinned as real by `batch_lift_is_not_vacuous`: a concrete batch/insert pair whose footprints are independent, which the retired shape predicate (`covered_classes`) refuses, and whose two orders reach one tree. It is evaluated, so it goes red if the excluded shape is ever readmitted. The leaf statement (`leaf_independence_diamond`) is kept beside the general one so the widening can be checked against it.

- <a id="row-preorder-parent-first"></a>**`preorder-parent-first`** — `preorder_parent_first` · fuaran-core#162

  On an id-unique tree, a node's parent PRECEDES it in the preorder walk, proved over the model's own preorder — the fact the diff's reconstruction needs. It is a theorem of the WALK rather than an invariant an edit could break: the walk emits a node before its subtree by construction. So preservation by insert, remove and reorder is the COROLLARY that each preserves id uniqueness, and the remove's form is over the SURVIVORS — a removed subtree's positions do not move, they vanish, and a statement over the original tree's nodes would be false on exactly the nodes the operation exists to destroy. The ordering predicate is written without indices, as the model writes `List.sort` and `List.length`, and its direction is pinned by evaluation, because an ordering predicate written backwards makes every theorem over it true and none of them meaningful.

  _Boundary:_ Id uniqueness is needed so that the parent's occurrence is the first one, which is what makes `precedes` well defined at all; the remove's preservation form covers the survivors only, not the original tree's nodes.

- <a id="row-validator-decides-the-invariant"></a>**`validator-decides-the-invariant`** — `first_dup_none_iff` · fuaran-core#138

  The insert graft's uniqueness scan decides EXACTLY the hypothesis the preservation argument needs: it finds nothing precisely when the graft's ids repeat neither the tree's nor each other's. Both directions, and both matter: left to right is soundness — an accepted graft cannot break the invariant; right to left is sharpness — a refused graft genuinely would have broken it, so the check refuses nothing safe. Beside it, `wf_iff_no_dups`: the structural well-formedness predicate the preservation arguments are written over and the flat no-duplicates list the scan computes are the same predicate, a fact the check makes load-bearing.

  _With:_ `wf_iff_no_dups`.

  _Boundary:_ About the insert graft's scan only. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-insert-subtree-check-closes-it"></a>**`insert-subtree-check-closes-it`** — `cx_insert_refused_now` · fuaran-core#138

  The concrete counterexample insert — a graft whose descendant id the tree already holds — is REFUSED by the lifted model, naming the descendant id as the offender: the fix machine-checked at the exact point where the algebra was broken, and a go-red by construction — narrow the validator back and this stops verifying. Its twin, `insert_breaks_wf_pre137`, proves the same insert is admitted by the narrower clause.

  _Boundary:_ One concrete witness, not a family. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-tree-placement-lowers-to-insert-and-reorder"></a>**`tree-placement-lowers-to-insert-and-reorder`** — `placed_insert_is_insert_plus_reorder` · fuaran-core#312

  A PLACED INSERT IS ONE INSERT PLUS AT MOST ONE REORDER. `TreePlacement.place`'s script once the position `k` is resolved — `[InsertChild(parent, node)]` when `k` is the parent's child count, else `[Batch [InsertChild(parent, node); ReorderChildren(parent, List.insertAt k (id node) (ids cs))]]` — is modelled as `place_script`, and on every WELL-FORMED tree, with the parent present, the insert's own uniqueness clause passing (`first_dup n t = None`, the check `InsertChild` makes) and `k <= length cs`, `apply_all` of it is `Ok (ins_at p k n t)`: the tree whose parent's children are `insertAt k n cs` and in which nothing else has changed (`placed_insert_is_insert_plus_reorder`). So the reorder leg is always accepted, lands the node exactly at position `k`, and moves no other child and touches no other node. `place_at_end_is_bare_insert` is the end case alone (the script IS the single insert), `reorder_leg_is_not_identity` its converse (at every earlier position the wanted order differs from the appended one, so the reorder leg is dropped exactly when it would be the identity), and `placement_lands_at_position` pins the zero-based reading by evaluation.

  _With:_ `place_at_end_is_bare_insert`, `reorder_leg_is_not_identity`, `placement_lands_at_position`.

  _Boundary:_ Holds only on a well-formed tree with the parent present, the uniqueness clause passing and `k <= length cs`. NOT claimed: the anchor's resolution to a position (`positionOf`: the sibling lookup and the range check) and the engine dry run that precedes it, which the theorem takes as given; container capability (`canHold` is not modelled, as the model's header says, so `TreePlacement.place` is covered only under a scope whose `canHold` is `fun _ -> true`); and `TreePlacement.move` and `clone`, which are not modelled. `Conformance.placementLaws` samples the same law at a domain's witness; that is a law run, not this proof. Well-formedness is needed, not convenient: `reorder_at` acts at every node carrying the parent id and `arrange` resolves children by id, so the theorem needs the parent to be one node and the children after the insert to carry distinct ids.

- <a id="row-index-agrees-with-the-walk"></a>**`index-agrees-with-the-walk`** — `index_agrees` · fuaran-core#438

  THE BUILD-ONCE INDEX IS A CACHE, NOT A NEW SEMANTICS. Over a well-formed tree, each lookup of the index `Tree.Index.build` makes answers exactly what the walk it caches answers (`index_agrees`): `Index.tryFind` is `Tree.tryFind`, `Index.parentOf` is `Tree.parentOf`, `Index.path` is `Tree.path`, `Index.depth` is `Tree.depth` and `Index.ancestors` is `Tree.ancestors`, at every id, an absent one included. `build` on a well-formed tree, the two maps and the five lookups are production's clause for clause (production's `build` refuses any other tree, `index-fresh-on-build`): a map is its association list and `Map.tryFind` reads the last binding `Map.ofList` kept, and `path` walks the parent links up carrying its visited ids exactly as production does. The load-bearing lemmas: a node of a well-formed tree is its id (`ix_unique`); every parent link is the parent relation and names a node strictly below the top (`ix_link_is_parent`); and the walk up the links from a node returns the walk down to it (`ix_up_walks`).

  _With:_ `ix_unique`, `ix_link_is_parent`, `ix_up_walks`, `repeated_ids_never_fresh`.

  _Boundary:_ Holds over a well-formed tree only, and well-formedness is necessary, not convenient: over a tree carrying an id twice `Map.ofList` keeps the last node and `Tree.tryFind` the first, so the two genuinely disagree (`repeated_ids_never_fresh` evaluates the disagreement at such a tree), which is why production's `Tree.Index.build` refuses such a tree, naming its first repeated id, rather than indexing it.

- <a id="row-index-freshness-tracks-change"></a>**`index-freshness-tracks-change`** — `freshness_tracks_change` · fuaran-core#447

  A FRESH INDEX DESCRIBES THE TREE IN FRONT OF IT, EXACTLY. `Index.isFreshFor` compares the index with the tree node by node, with no hash in between: the root's id; no id carried twice; every node's key in `ById` at a node with the same record — id, kind and ordered child ids — and the same `encode` reading, and each child's key in `ParentOf` linked to it; and no key in either map beyond the tree's. Proved over the model: an index built from a well-formed tree is fresh for a tree only if the two are the same tree (`freshness_tracks_change`) — a fresh tree is well-formed (`ix_fresh_wf`), each tree's node records include the other's (`ix_agree_records`, `ix_covered_records`), and two well-formed trees in that relation are one tree (`ix_records_determine`) — so every skeleton edit, kind change or id remap since `build` reads as stale, and over a fresh index every lookup is the walk over the CURRENT tree (`fresh_index_agrees`). NO HASH PREMISE: freshness is not compared through a stamp, and the pair pinned as a collision of the retired 32-bit FNV-1a stamp reads as stale (`the_stamp_collision_pair_is_stale`). GO-RED, run at the leg: with each record narrowed to (id, kind, child count) the module does not verify — `ix_sub_in` cannot discharge `ix_kids_eq` — and `a_childless_check_misses_a_reparent` proves the narrowed check, parent links unread, reads two trees one reparent apart as fresh.

  _With:_ `ix_fresh_wf`, `ix_agree_records`, `ix_covered_records`, `ix_records_determine`, `fresh_index_agrees`, `the_stamp_collision_pair_is_stale`, `a_childless_check_misses_a_reparent`.

  _Boundary:_ The model keeps every conjunct but the content one; the content conjunct is the caller's `encode`, exact only under its injectivity (`index-node-is-its-record`). The exactness theorem is stated for an index built from a well-formed tree. Not modelled: `Index.rebind` / `Ops.Index.afterOp`. The theorem quantifies over the index `build` made; an index maintained through an edit is `build`'s index of the edited tree by their law, which the index tests compare with a rebuild and no theorem states.

- <a id="row-index-fresh-on-build"></a>**`index-fresh-on-build`** — `try_build_exact` · fuaran-core#447

  BUILD BUILDS EXACTLY THE WELL-FORMED TREES, AND ITS INDEX IS FRESH. `Tree.Index.build` answers `Ok` exactly on a well-formed tree, with an index fresh for the tree it was built from, and otherwise refuses with `Tree.wellFormed`'s first repeated id (`try_build_exact`, through `scan_dup_none_iff` and `wf_iff_no_dups`; `fresh_on_build` for the index). Production's `build` is the model's `ix_try_build`; the model's lenient `ix_build` has no production counterpart.

  _With:_ `fresh_on_build`, `repeated_ids_never_fresh`, `scan_dup_none_iff`, `wf_iff_no_dups`.

  _Boundary:_ Freshness is for a well-formed tree only: over a tree carrying an id twice NO index is fresh (`ix_fresh_wf`, `repeated_ids_never_fresh`), and production's `build` makes none there, refusing the tree instead.

**Tested** (5)

- <a id="row-footprint-ceiling-differential"></a>**`footprint-ceiling-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#143

  The precision-ceiling witness runs on the EXTRACTED model and on production `Ops.footprint` / `Ops.independent` / `Ops.apply` side by side, over the reference domain: the same two operations are footprint-indistinguishable in both, both refuse the pair, the move commutes in both and the remove-shaped batch diverges in both. A decision not to change something survives only as a check that goes red when someone does, and this is that check.

  _Boundary:_ Agreement is over this witness, never over all inputs.

- <a id="row-footprint-in-differential"></a>**`footprint-in-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#500

  The extracted base-aware footprint (`TreeOps.fp_in_all`, section 24) is production's `Ops.footprintIn` over the generated pool, script by script and at every state a prefix of the pool reaches: the same six address sets, with scripts the base resolved (no unknown-parent write left) and scripts that fell back to the pure arm both reached. The measurement can fail: the same comparison against the model's PURE footprint disagrees on a removal whose subtree the base resolved, so the differential compares production with the model and never with itself. The extracted model also keeps the in-place diamond its theorem proves and the refinement, sampled over the pool.

  _Boundary:_ Sampled over the generated pool only, never over all scripts; `Ops.footprintIn` under a keyed `OpScope` is held to the unkeyed scope on a domain with no keyed position by the suite and is not modelled.

- <a id="row-tree-differential"></a>**`tree-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#133

  The extracted tree model agrees with the shipped apply engine and footprint derivation over the generated operation pool and every state a prefix of it reaches — the footprint as four address sets, the verdict, an accepted result through the content hash computed by production's own function on both sides, and a rejection by class. Every rejection class the plain apply can raise is asserted reached BY NAME rather than counted, and a bridge that erases every kind tag is required to make the result comparison fail, so a green report is known to be one that can lose.

  _Boundary:_ Agreement over the generated pool and the states its prefixes reach only, never over all inputs; a rejection is compared by class only. The result hash runs over the per-node content the witness exposes, id and kind tag, and nothing else: a node's value, hole or effect class is outside the model and so outside the comparison. Rejection PAYLOADS are not compared: `UnknownNode`'s `addressable` (the whole id set of the tree it was raised against) and `ReorderMismatch`'s two orders are order-dependent, so they could not be claimed to agree across orders in the first place.

- <a id="row-tree-digest-differential"></a>**`tree-digest-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#290

  The pre-image production folds is the extracted model's shape, field for field: at every state the generated pool reaches, `Tree.encodePreimage` under the witness label equals `Hash.canonicalFields` over the extracted `TreeOps.fields` of `TreeOps.shape`, with the model's (id, kind) rendered exactly as the production label renders it — so `preorder_arity_injective` is known to be about the string the memo keys on. The go-red is a MIS-NESTING op bridge: every `InsertChild` crosses to the model as an insert under the parent's deepest last descendant, which keeps the preorder and moves the shape; under an arity-free fold this bridge agrees with production on the accepted-result hash (measured), and against the arity-carrying fold it loses on that hash and on no verdict.

  _Boundary:_ Agreement is over the pools drawn, never over all inputs.

- <a id="row-index-differential"></a>**`index-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#447

  The extracted `TreeOps.fst` section 23 runs beside production's `Tree.Index` over every state a generated lane pool reaches, the reparent pair, and each of those with a node copied under the root (a tree carrying an id twice): `build`'s verdict and named id beside the model's `ix_try_build`, all five lookups at every id and an absent one wherever production's `build` makes an index (a well-formed tree), and `isFreshFor` over every ordered pair whose first tree production indexes; over a well-formed tree, production's index, production's walk and the model's walk all agree. A fresh pair whose trees differ fails the case — no premise allows one (guards: a repeated id, a path three ids long, stale and fresh pairs, and a stale pair only a child id tells apart). The pair pinned as a collision of the retired 32-bit stamp is asserted still to collide under that term and to read as stale in production and in the model. GO-RED: the model's freshness narrowed to (id, kind, child count), parent links unread, disagrees with production, on the freshness arm alone.

  _Boundary:_ Agreement over the sampled cases only, never over all inputs; production is compared through the model's view of a node, an encoder that reads nothing (`index-node-is-its-record`).

**Rests on, or is bounded by:** [`independence-diamond`](#row-independence-diamond) (assumed), [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `Skeleton`

[`Skeleton.fst`](Skeleton.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `DagFold`, `TreeOps`.

> Skeleton — the composite: fold confluence for the skeleton-op tree algebra, with the domain
> hypothesis DISCHARGED rather than assumed (fuaran-core Phase 133; widened to the whole
> operation alphabet by Phase 162).

**Proved** (3)

- <a id="row-skeleton-fold-confluence"></a>**`skeleton-fold-confluence`** — `skeleton_fold_confluence` · fuaran-core#133

  Fold confluence for the skeleton-op tree algebra with NO domain hypothesis remaining, over the WHOLE skeleton operation alphabet — batch included and nested to any depth: the same lanes reach the same tree however they arrive, a lane set that cannot fold halts with the same canonical report however it arrives, and none folds under one order and halts under another. The halt half is proved separately under no hypothesis at all — not the diamond, not id uniqueness, nothing about the apply engine.

  _With:_ `skeleton_fold_confluence_halt`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). The algebra folded is `TreeOps.wapply`, which is `Ops.apply` declining a step that would leave an id twice. By `ins_wf` / `ins_wf_conv` that class is exactly the one the validator refuses (`validator-decides-the-invariant`), so on an id-unique tree the guard declines nothing `Ops.apply` accepts: the theorem is about the shipped engine on id-unique input (`tree-algebra-well-formed-states`). The statement carries no `requires`: it covers every lane set, a rejecting lane included, because `fold-confluence-total` discharges what a lane-set premise would assume, and no row carries one. The module is the instantiation template's first instance: `kit/templates/Instance.fst.template` with its holes filled reproduces it byte for byte (the `Proofs.Kit` family), and the template's one proof obligation is the `{{DIAMOND}}` hole, `independence_diamond` at the domain's own footprint and apply. The template does not yield `TreeOps.fst`'s diamond, `Preservation.fst` or `TreeDiff.fst` at another algebra: those are about this tree's algebra, opened read-only from the kit asset by a domain on the skeleton tree and modelled afresh by a domain with its own apply.

- <a id="row-skeleton-update-in-alphabet"></a>**`skeleton-update-in-alphabet`** — `update_lane_folds` · fuaran-core#250

  The fold theorem's alphabet is the SHIPPED alphabet, `UpdateNode` — an in-place rewrite of a node's content that keeps its children — included: `TreeOps.op` carries the case with its apply and footprint clauses, so `skeleton_fold_confluence` is not about a sub-alphabet, and a lane carrying an update folds. The diamond's six update pairs close by the same elimination as a remove or a move (`update_is_relocating`): the update's footprint carries an unknown-parent write, which is REQUIRED because an update of a node and a concurrent remove of its ancestor share no address the script can name and do not commute.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-skeleton-update-pair-halts"></a>**`skeleton-update-pair-halts`** — `update_pair_halts` · fuaran-core#250

  Two lanes each updating a DIFFERENT node halt rather than fold — the cost of the update's unknown-parent write, as an evaluated fact rather than prose. They would commute, but the footprint cannot tell them from an update against a remove of the updated node's ancestor, which does not — so both are relocating, and the fold reports the pair instead of guessing.

  _Boundary:_ This is the same pinned over-approximation `RemoveNode` and `MoveNode` pay, a precision limit of the current record rather than a real conflict between the updates; tightening it is a change to the `Footprint` record.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed).

**Inherited through the models it opens:** [`independence-diamond`](#row-independence-diamond) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `Chain`

[`Chain.fst`](Chain.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`.

> Chain — an F\* model of Fuaran.Core's two integrity walkers, with tamper detection as a
> machine-checked theorem under one named hash assumption (fuaran-core Phase 136).

**What is modelled.** Both shapes the downstream ledgers rest on, side by side:

- the CONTENT-ADDRESSED DAG of `Fuaran.Core.OpStream.Dag` — a node is (parents, actor, op),
its id is `Dag.nodeHash` of the SORTED parents and the encoded actor-and-op, and
`Dag.firstBreak` / `Dag.verifyDag` recompute that id per node and check that every named
parent is present;
- the LINEAR hash chain of `Fuaran.Core.OpStream` — a record is (seq, actor, op, prevHash,
hash), and `OpStream.firstChainBreak` / `verifyChain` walk it checking the sequence, the
prev-link, and the record's own recomputed hash.

**Proved** (20)

- <a id="row-chain-intact-verifies"></a>**`chain-intact-verifies`** — `intact_verifies` · fuaran-core#136

  A content-addressed DAG grown from nothing by append and merge alone, each naming parents the DAG already held, has no integrity break; and a linear hash chain grown from genesis by append alone verifies. `intact_verifies` takes no injectivity hypothesis of any kind — not the hash's, not the codec's, not the splices' — because append mints the id from the content it is storing, so the id cannot be wrong however weak the hash is.

  _With:_ `intact_chain_verifies`.

  _Boundary:_ The id half is free by construction, but the parent half is not: append does not check that the parent it is handed names a node the DAG holds, so every-parent-present is a precondition on how a DAG was grown rather than a property of the constructor, and the theorem carries it as one. NOT claimed: acyclicity. A DAG whose ids all recompute cannot carry a cycle, since a node's id folds its parents' and a cycle would need a collision, but that is an argument and not a theorem here; `Dag.isAcyclic` remains a separate check on a structurally-loaded DAG.

- <a id="row-chain-tamper-detected"></a>**`chain-tamper-detected`** — `tamper_detected` · fuaran-core#136

  Changing one DAG node's op, actor or parent multiset while leaving its address as it was, in a DAG whose node under that id was intact, is reported as a break; and the same for one linear record's sequence, actor or op with its two hash fields left alone. Both are corollaries of a characterisation of the walker itself: the walk finds nothing exactly when every key is its content hash and every named parent is present, and exactly when every record's sequence, prev-link and recomputed hash agree.

  _With:_ `break_none_iff`, `chain_break_none_iff`, `tamper_op_detected`, `tamper_actor_detected`, `tamper_parents_detected`, `chain_tamper_detected`, `chain_tamper_detected_verify`, `tamper_changes_the_id`.

  _Boundary:_ The DAG arm's premise is `node_injective_on`, the composite injectivity premise RESTRICTED to pre-images whose parents are ids and whose actor string is one the actor code emits — what the splice lemmas need, and what production supplies, since a stored parent is a key of the map or the walk's second clause reports it and a stored actor is an Actor the walker re-encodes; a caller builds that premise from the two that remain assumptions, through `node_injective_derived`. The linear arm's premise is `rec_injective_on`, the same RESTRICTION on the actor string, built by `rec_injective_derived` from the same two assumptions plus the numeral code (`chain-envelope-parse` below); no bundled, unrestricted form of the premise is in the module. NOT claimed: a REWRITE. A tamper that also re-mints the tampered node's id and every descendant's produces a structure that is intact by construction, and no walker finds it; for the linear chain the signed head closes it (`signed-head-rejects-splice`), for the DAG it is not claimed. Nor loading: the walkers begin at a structure already in memory, and loading is not verifying; the linear stream's JSONL readers are the `op-stream-load-*` rows, and the DAG's (`Dag.fromJsonl`) are not claimed here.

- <a id="row-chain-unconditional-break-classes"></a>**`chain-unconditional-break-classes`** — `chain_tamper_prev_detected` · fuaran-core#136

  Three break classes are proved under NO hypothesis at all, which is the sharpest thing the mechanisation says: a linear record renumbered, a linear record re-pointed at a different predecessor, and a DAG node another node names being absent. Those three checks compare stored data against the walk's own running values or against the key set, and never consult the hash — so they are detectable independently of the hash's strength.

  _With:_ `chain_tamper_seq_detected`, `dangling_parent_detected`.

  _Boundary:_ Only these three classes are unconditional: the recomputed-hash check spends the cryptographic premise.

- <a id="row-merge-id-parent-order-independent"></a>**`merge-id-parent-order-independent`** — `merge_id_parent_order_independent` · fuaran-core#136

  merge(A,B) and merge(B,A) converge to one content id, because the parents are sorted before hashing; it needs nothing about the hash.

  _With:_ `parent_reorder_undetected`.

  _Boundary:_ Needs that the comparison is a total order. Its converse is proved too and is a boundary rather than a result: re-ordering a node's parents is NOT a detectable tamper, and is not meant to be. `String.CompareOrdinal(a, b) <= 0` is such an order: any two strings compare, and two that compare both ways are equal.

- <a id="row-preimage-splices-unambiguous"></a>**`preimage-splices-unambiguous`** — `node_injective_derived` · fuaran-core#145

  The two SPLICES in `Dag.nodeHash`'s pre-image are unambiguous, and the composite premise the tamper theorems take is therefore DERIVED from the hash's own injectivity and the op codec's rather than assumed alongside them (`node_injective_derived`). `parent_splice_unambiguous`: `String.concat ","` is injective on lists of parent ids. `actor_op_splice_unambiguous`: `actor ^ "|" ^ encodedOp` recovers both halves.

  _With:_ `parent_splice_unambiguous`, `actor_op_splice_unambiguous`, `joined_injective`, `splice_split`.

  _Boundary:_ `parent_splice_unambiguous` needs that an id carries no comma and is not empty — both conditions load-bearing, since `["a,b"]` and `["a";"b"]` join alike without the first and `[]` and `[""]` do without the second. `actor_op_splice_unambiguous` needs that neither actor encoding is a proper prefix of the other. That condition is NOT separator-freedom, which is false of production: `Actor.encode` does not escape the pipe, so a Human whose id is `a|b` encodes with one in it. What is true is stronger — a JSON object whose string literals are self-delimiting is a prefix-free code — and it is what the lemma takes. The reading of a string as its symbols is a stated hypothesis (`symbols_faithful`) in the same style as `total_order`, not a parameter: concatenation is symbol-list append and two strings with the same symbols are the same string. The splice condition is not academic: the work-plan witness's op codec spells an op as `"A|" + id + "|" + title`, so the composite pre-image really carries several `|`. Both halves of `symbols_faithful` are true of `System.String` by construction; F\*'s own `FStar.String` states them as admitted `val`s, so naming them in the module puts the assumption in a signature rather than inheriting it from a library `--report_assumes` does not quantify over.

- <a id="row-chain-envelope-parse"></a>**`chain-envelope-parse`** — `rec_injective_derived` · fuaran-core#197

  The LINEAR side's premise is discharged the way the DAG's is. `rec_hash` hashes the envelope `{"seq":<n>,"actor":<a>,"op":<o>}` beside the predecessor's hash, and `rec_payload_parsed` proves that envelope injective in its three fields for actor strings the code emits: three splits, each forced by the LEADING COMMA of the separator that follows it — the sequence cut at the first comma (`app_sep_split`, because a numeral carries none), the actor cut at the comma opening `,"op":` (`splice_split`, because the actor code is prefix-free — the DAG side's premise again, NOT comma-freedom, which `Actor.encode` does not have), and the op cancelled against the closing brace (`app_snoc_inj`, which needs NOTHING of the op encoding's alphabet). The shard's digraphs are not what the proof spends: `{"seq":`, `"actor":` and `"op":` are cancelled as shared prefixes without being read, and the two literal facts the proof needs — that each separator begins with the comma — are discharged by normalising the literals. `rec_injective_on` — the composite injectivity premise RESTRICTED to actors the code emits, injective in all four components with the predecessor's hash among them — is then BUILT by `rec_injective_derived` from `hash_injective`, `op_codec_injective`, the reading, the numeral code and the actor code, and every linear theorem in sections 6-8 takes it. `payload_splice_breaks_chain` states the tamper theorem positively on the named premises alone. Proved with no assume and no admit under --report_assumes error.

  _With:_ `rec_payload_parsed`, `app_sep_split`, `app_snoc_inj`, `app_cancel_left`, `rec_payload_symbols`, `payload_splice_breaks_chain`.

  _Boundary:_ The reading hypothesis is section 1b's plus one symbol, the brace, and the derivation still spends `hash_injective`, `op_codec_injective`, the numeral code and the actor code. The chains the linear theorems are stated over carry actors the code emits (`actors_ok`; a re-minted splice carries the condition on its steps). The unrestricted form is NOT a theorem and is not in the module: over a free actor string `A,"op":B` with op `C` and `A` with op `B,"op":C` mint one envelope, which the differential exhibits. The actor code premise is load-bearing: a falsifier run that dropped it failed at the splice. NOT claimed: the `StreamConfig` migration path (`StreamConfig.legacyActor`, `OpStream.rehash`). The model carries the canonical payload only, with the payload's shape a parameter, so a second config is a second instantiation rather than a second model, and nothing is said of the legacy payload or of the migration between them. Nothing about the DAG's `|` splice beyond `preimage-splices-unambiguous`, and nothing about the collision resistance of any concrete hash.

- <a id="row-snapshot-replay-equivalence"></a>**`snapshot-replay-equivalence`** — `replay_from_snapshot_eq` · fuaran-core#191

  For every stream, every reducer, every boundary n and the (snapshot, tail) that compact produces at n, replay from the origin IS replayFrom the snapshot, READ AGAINST THE ORIGIN'S NUMBERING; `replay_from_snapshot_state` is the offset-free corollary for the accepted case, which is the sentence `Conformance.snapshotLaws` samples. It takes no hypothesis on the chain, the hash or the snapshot's payload: bounded replay is a fact about the fold, and it holds of a stream that does not even verify. Beside it, `compact_refusal_is_the_origins`: compact refuses an in-range boundary exactly when the origin's own replay halts inside the prefix, at the same index with the same rejection, so a refused compaction loses nothing. The reducer, the state and the rejection are parameters, so this is one theorem for every consumer's cache rather than one per domain. Proved with no assume and no admit under --report_assumes error.

  _With:_ `replay_from_snapshot_state`, `compact_refusal_is_the_origins`.

  _Boundary:_ The qualification is the finding: replayFrom is `replay w snap.State tail` and replay numbers from zero, so a tail op that rejects at origin index n+j is reported by replayFrom at j — the unqualified equality is true of the state and false of the rejection index. It is deliberately not a claim about any cache's on-disk format or its self-verification against a stream head, which belong to the host that keeps the cache.

- <a id="row-compact-preserves-verify"></a>**`compact-preserves-verify`** — `compact_preserves_verify` · fuaran-core#191

  The original stream verifies exactly when its DISCARDED PREFIX verifies and the compaction verifies across its boundary — `verifyChain rs = (verifyChain prefix && verifyAcross snap tail)`, for the snapshot compact produces, under no hypothesis on the hash at all, because it is the walker's own arithmetic. The corollary `compact_verifies_iff_original` is that the compacted stream verifies exactly when the original does. `compacted_tail_tamper_detected` is `chain-tamper-detected`'s linear arm restated at the boundary, an appeal to it and nothing more. `compact_at_zero_verifies_under_any_genesis`: the compaction at zero of a stream that verifies from a genesis verifies across under that genesis, whatever it is — the model's `compact` takes the genesis, as production's `Snapshots.take` and `Snapshots.compact` read it from the `Stream`'s config (a stream built by `OpStream.canonical` is the empty-genesis instantiation, byte for byte), and the split theorem carries no condition on it. The snapshot's hash payload — state-hashed or chain-only — is a parameter neither theorem reads, so `Snapshots.compact` under `SnapshotMode.Strict` and under `SnapshotMode.ChainOnly` are two instantiations of one result.

  _With:_ `chain_ok_split`, `compact_verifies_iff_original`, `compacted_tail_tamper_detected`, `compact_at_zero_verifies_under_any_genesis`, `boundary_ignores_the_seed`.

  _Boundary:_ `compact_verifies_iff_original` CARRIES A PREMISE: the prefix verified. Without it the sentence is false in the direction that matters — compact does not walk the chain, it reads `records[n-1].Hash` and trusts it, so a compaction of a stream whose prefix was tampered verifies across, and once the prefix is discarded nothing can find the tamper again. Verify, then compact: that ORDERING stays a premise, stated on `Snapshots.compact`'s doc comment and in the repository README's op-stream section, where a host reads it. `compacted_tail_tamper_detected` spends the same premise as `chain-tamper-detected`, `rec_injective_on`, so the two records' actors are ones the code emits. Past sequence zero the boundary hash is a stored one and the genesis never reaches it (`boundary_ignores_the_seed`), so `Snapshots.compact` over `OpStream.canonical` and over `OpStream.ofConfig cfg` agree there whatever the genesis. NOT claimed: the snapshot's own hash as evidence about the STATE. `verify_across` checks `snap.Hash = hashFn snap.PrevHash payload`, but that a swapped state is therefore detected under the state-hashed payload needs the hash injective on snapshot pre-images, a second instance of `content-id-determines-content`'s premise that is not stated here; under the chain-only payload it is not detected at all, by design (fuaran-core#258). Nor the JSONL snapshot lines (`Snapshots.toJsonl`, `Snapshots.ofJsonl`): loading is not verifying.

- <a id="row-signed-head-binds-chain"></a>**`signed-head-binds-chain`** — `signed_head_binds_chain` · fuaran-core#193

  The signed head binds the chain it seals: two linear chains that both verify from one genesis, and whose heads both verify under ONE attestation, are the same chain — the same records, so the same ops in the same order. It is the composition of two facts. The attestation verifies against at most one head (`signature_binds`, the row `signature-binds-one-head` below), and a verified chain's head determines the chain: `same_head_same_chain_from` is the linear chain's own injectivity, walked tip to root, spending `rec_injective_on` — the DERIVED composite (`chain-envelope-parse`) — once per record, with a chain told apart from its own extensions by the sequence number in the pre-image. `signed_sentinel_covers_the_empty_chain` states the other side, that under a signed sentinel the empty chain is accepted under every genesis. Proved with no assume and no admit under --report_assumes error.

  _With:_ `same_head_same_chain_from`, `head_seq_at_least`, `signed_sentinel_covers_the_empty_chain`.

  _Boundary:_ Both chains carry actors the code emits and a re-minted splice carries the condition on its steps. It carries one boundary as a premise rather than hiding it: the model's `chain_head` returns the literal empty string for the empty chain, where the shipped `OpStream.head` returns the configured genesis (`cfg.Genesis`), so the theorem asks that the signed head is not that sentinel, which is a condition on the one head being verified and one a verifier can check. What it deliberately is not: a claim about any signature algorithm, key custody or a keyring's lockout rules, and not UNFORGEABILITY — the theorem is about the one original attestation, and that nobody can mint a second one over a rewritten head is the signature scheme's own claim. Nothing is stated over a compacted stream: `compact` trusts the boundary hash it reads, so a signed head over a compaction binds the discarded prefix only under `compact-preserves-verify`'s premise. The sentinel premise is what the drop arm needs at its smallest: a one-record chain with its record dropped re-mints to the empty chain. A host that signs heads should not sign an empty stream's. `signed_sentinel_covers_the_empty_chain` is likewise the model's: production's empty-chain head is its genesis, so a signature over it covers the empty chain under the genesis it names rather than under every genesis. `same_head_same_chain_from` is stated at an arbitrary boundary `(prev, i)`, so two tails verified from one boundary index under one head are the same tail from the same boundary hash, but no theorem here is stated over `verify_across`. NOT claimed: the DAG's heads. Both theorems are over the linear chain; a content-addressed DAG's head set is a different object, and nothing here signs it.

- <a id="row-signed-head-rejects-splice"></a>**`signed-head-rejects-splice`** — `signed_head_rejects_splice` · fuaran-core#193

  Replace, insert or drop ONE op and RE-MINT the whole chain from genesis, so that `verifyChain` accepts the result, and it is still refused under the original attestation. The re-mint is the point: it is the rewrite `chain-tamper-detected` cannot see, because it produces a perfectly intact chain, and it is the forgery `Conformance.attestationLawsAt` builds with its own rehash. One theorem over a three-case `splice`, guarded by `splice_changes` — a replacement by a different step at an index the chain has, any insertion, a removal at an index the chain has. `signed_head_rejects_rewrite` is the general form beside it: ANY chain other than the one that was signed is refused, however intact.

  _With:_ `signed_head_rejects_rewrite`, `build_chain_actors_ok`, `apply_splice_actors_ok`.

  _Boundary:_ A splice that changes nothing re-mints the signed chain itself and is rightly accepted, which is what the `splice_changes` guard excludes. Same premises as `signed-head-binds-chain`, of which both are corollaries.

- <a id="row-append-after-compact"></a>**`append-after-compact`** — `append_after_compact` · fuaran-core#301

  A COMPACTED STREAM KEEPS WRITING. For every stream, reducer, hash and snapshot payload, every boundary n and the (snapshot, tail) that compact produces at n, the compaction of the APPENDED stream is the appended compaction — `compact (append_full rs a o) n == Compacted snap (append_to snap tail a o)`, snapshot and tail alike — so the record `Compacted.appendTo` mints onto the tail is the record `append` mints onto the whole stream: at sequence `snap.Seq + length tail` and linked to the tail's last hash or, on an EMPTY tail, to the snapshot's boundary hash. With it (`compact_preserves_verify` on the appended stream) the appended original verifies exactly when its discarded prefix verifies and the appended compaction verifies across, and `append_after_compact_verifies_iff` is the chartered sentence with its premise: over a prefix that verified, `verifyChain (rs @ [r]) = verifyAcross snap (appendTo snap tail r)`. Proved with no assume and no admit.

  _With:_ `append_after_compact_verifies_iff`.

  _Boundary:_ The verification equation holds only over a prefix that verified. NOT claimed: the compare-and-append and idempotent forms beyond the record they share with `appendTo` (`appendIfTo` compares `Compacted.head`, which is the head `append_to` links to; `appendIdempotentTo` is `appendTo` behind a key lookup), and any persisted form of the compacted stream.

- <a id="row-compact-compose"></a>**`compact-compose`** — `compact_compose` · fuaran-core#301

  RE-COMPACTION IS COMPACTION. Compacting a compacted stream again k records past its boundary n — `compact_from snap tail k`, the model of `Compacted.compactFrom` at `atSeq = n + k` — is compacting the full stream at n + k: the same snapshot (sequence, state, boundary hash and seal, in the snapshot's own payload) and the same tail, or the same refusal with the same message, for every stream, reducer, hash and payload. A prefix refusal is reported at the ORIGIN index, because the cut records are replayed numbered from the snapshot's boundary, exactly as `compact` numbers them. Proved with no assume and no admit.

  _Boundary:_ Given only that the tail starts at its boundary (`starts_at`, the seam `compactFrom` checks before it replays, as `Snapshots.replayFrom` does). NOT claimed: anything of a tail whose first record is not at its boundary, which `compactFrom` refuses by name (`TailSeqMismatch`).

- <a id="row-key-index-rebuild-parity"></a>**`key-index-rebuild-parity`** — `key_index_rebuild_parity` · fuaran-core#301

  THE KEY INDEX SURVIVES COMPACTION. The first-wins key-index fold splits at ANY boundary: the index of the first n records, continued over the rest, is the index of the whole stream (`key_index_rebuild_parity`), and at a compaction `compact` produced, the discarded prefix's index (`Compacted.Keys`) continued over the tail is `KeyIndex.ofStream` of the uncompacted stream (`key_index_compact_parity`). So `Compacted.keyIndex` answers every key the stream ever produced, with the entry it first produced, and an idempotent retry of a pre-compaction key is the `Duplicate` it was before the compaction. Proved with no assume and no admit.

  _With:_ `key_index_compact_parity`.

  _Boundary:_ NOT claimed: the persistence of `Keys`, which no hash binds and which the host carries beside the compacted stream (DECISIONS, the `fuaran-core#301` entry).

- <a id="row-verify-captures"></a>**`verify-captures`** — `intact_captures_verify` · fuaran-core#301

  THE CAPTURE JOURNAL IS A CHAIN. Section 6 at the capture payload `{"capture":true,"seq":…,"eff":…,"det":…,"value":…}`: a journal recorded from its genesis by `captureEffect` — a `deterministic` effect journalling nothing, any other appending at the journal's length linked to its head — verifies (`intact_captures_verify`), and a journal one of whose captures had its VALUE changed, its sequence, identity, label and links left alone, does not (`capture_value_tamper_detected`). Proved with no assume and no admit.

  _With:_ `capture_value_tamper_detected`.

  _Boundary:_ Tamper detection is given the premise `cap_value_injective` that at one predecessor, sequence, identity and label the hash determines the value; that premise is stated BUNDLED — unlike the record envelope's premise, which section 6b decomposes into the hash's and the codecs', it is not decomposed for the capture envelope. The escaper the payload quotes the identity and the label with is a parameter, as the hash is.

- <a id="row-replay-record"></a>**`replay-record`** — `replay_record` · fuaran-core#301

  REPLAY OF A RECORDED SESSION IS THE SESSION. STRICT replay (`EffectCapture.replayEffectStrict`, modelled before the domain decode as `replay_strict`) of a recorded session over its own complete journal answers every value the session realised, in order, and leaves nothing in the journal (`replay_record` — `replay (record s) = s`): a non-deterministic effect from its capture, a deterministic one from the live source. Beside it, `truncated_journal_exhausted`: the same session replayed against a journal that lacks the capture of its LAST non-deterministic effect stops `Exhausted`, naming that effect — the tail truncation the lenient `replayEffect` answers from the live source with no signal. Proved with no assume and no admit.

  _With:_ `truncated_journal_exhausted`.

  _Boundary:_ Given only that every label is canonical. NOT claimed: the domain decode, a round trip the value codec owns (the theorem says the decoder is handed the recorded encoding); the label vocabulary itself (`isDeterminismLabel` is a parameter, held equal to `Effect.tryDeterminismOfTag` by a test); and a truncation the session never reaches, which only an anchored head (`verifyCapturesAt`) detects.

- <a id="row-keyed-capture-chain"></a>**`keyed-capture-chain`** — `intact_keyed_verify` · fuaran-core#318

  THE KEYED CAPTURE JOURNAL IS A CHAIN. Section 6 at the keyed capture payload `{"capture":"keyed","seq":…,"key":…,"occ":…,"det":…,"phase":…,"value":…}`: a journal recorded from its genesis by `captureEffectKeyed` — each invocation an `attempted` record and, when its body answered, a `completed` or `refused` record straight after it, under the occurrence that counts the earlier attempts under its key — verifies (`intact_keyed_verify`). Proved with no assume and no admit.

  _Boundary:_ The escaper and the hash are parameters, as in section 7c. The PAIRING half of `verifyKeyedCaptures` (attempts numbered in order, one settlement per attempt, after it, under its label) is not in the model; `Conformance.keyedCaptureLaws` samples it. Everything the section defines is `noextract_to "FSharp"`, so the extracted oracle is unchanged and no differential compares it.

- <a id="row-keyed-replay-by-key"></a>**`keyed-replay-by-key`** — `replay_skips_other_keys` · fuaran-core#318

  KEYED REPLAY FINDS AN INVOCATION BY WHAT IT WAS, NOT WHEN. The model of `replayEffectKeyed` for a non-deterministic label, before the domain decode: a key the journal holds no record of is refused `NoCapture` and never answered (`missing_key_no_capture`); records of OTHER keys in front of an invocation's records change nothing about how it replays (`replay_skips_other_keys` — positional independence as an equation); and the first invocation of a recorded session whose key no later invocation reuses replays to exactly what it settled to — its value, its refusal, or nothing for an attempt its body never answered (`replay_answers_head`). Proved with no assume and no admit.

  _Boundary:_ Modelled before the domain decode, for a non-deterministic label. NOT claimed: that EVERY invocation of a session replays in any order across keys — the composition of the last two through a decomposition of the recorded journal this section does not state; `Conformance.keyedCaptureLaws` samples it, shuffling the calls across keys.

- <a id="row-chain-tamper-evident-iff-hash-distinguishes"></a>**`chain-tamper-evident-iff-hash-distinguishes`** — `chain_tamper_evident_iff_hash_distinguishes` · fuaran-core#309

  The chain's tamper evidence stated with NO premise, which is the form a reader of the default hash needs: in a chain `verifyChain` accepted, change record `i`'s actor or op and leave its sequence, prev-link and stored hash alone, and `verifyChain` rejects the result IF AND ONLY IF `h(prev_i, env_i) <> h(prev_i, env'_i)` — the record hash distinguishes the original envelope from the tampered one under that record's predecessor. `chain-tamper-detected` is the injective-hash corollary; this is the iff it is a corollary of.

  _Boundary:_ Covers only a change to a record's actor or op with its sequence, prev-link and stored hash left alone. Under `OpStream.defaultHash` — a 32-bit FNV-1a that `Conformance.hashFnAdversarialLaws` exhibits an in-budget collision for on exactly this pre-image shape — the tamper is hidden exactly at the collision and nowhere else; whether a host's own HashFn distinguishes the pre-images a store holds is only what `Conformance.hashFnLaws`' op-tamper arm samples.

- <a id="row-append-on-is-merge-all"></a>**`append-on-is-merge-all`** — `append_on_is_merge_all` · fuaran-core#311

  An N-parent node is named before it is built: over distinct parents, the node `Dag.appendOn` mints has exactly the id `Dag.mergeAll` gives the same heads (`append_on_is_merge_all`), and two head lists holding the same ids, in any order and with any repetition, converge to one id (`merge_all_set_determined`) — so two replicas folding the same heads mint the same node, and no consumer needs a private `nodeHash` (`Dag.nodeId` is the public one). Section 5's two-parent `merge_id_parent_order_independent` is the smallest case.

  _Boundary:_ Needs the comparison to be a TRANSITIVE total order, which `String.CompareOrdinal(a, b) <= 0` is, and nothing about the hash. NOT claimed: anything about a list WITH a repeated parent under `appendOn`, which stores and hashes it as given.

- <a id="row-merge-all-set-determined"></a>**`merge-all-set-determined`** — `merge_all_set_determined` · fuaran-core#311

  `Dag.mergeAll` is a function of the head SET: two head lists with the same members — in any order, with any repetition — store the same deduplicated, sorted parents and mint the same content id (`merge_all_set_determined`).

  _Boundary:_ The model's `dedup` keeps last occurrences where `List.distinct` keeps first; the sort that follows erases the difference, which the theorem is the statement of, so the claim is about the sorted result, not about which occurrence a dedup keeps.

**Tested** (7)

- <a id="row-chain-walker-differential"></a>**`chain-walker-differential`** — `Proofs.Oracle`, 8 cases · fuaran-core#136

  The extracted model agrees with Dag.firstBreak and OpStream.firstChainBreak over generated DAGs and chains and every single-node tamper of each, comparing the verdict, the location, WHICH check failed and the expected and got values at once — and mints production's own content ids node by node, which is checkable only through append because nodeHash is private. Two go-red cases require a model handed a different hash to disagree, and a third runs one tamper under two hashes: found under the real one, INVISIBLE under a deliberately non-injective one, to production and to the model alike. Every run asserts that detection actually happened, because two walkers that both say intact agree perfectly and certify nothing.

  _Boundary:_ Agreement over the sampled DAGs, chains and single-node tampers only, never over all inputs; under a non-injective hash a tamper is invisible to production and the model alike. The wire corpus's `dag/` family supplies SHAPES, not addresses: a genesis node, a linear step, both actor kinds and a two-parent merge, rebuilt through production's own `Dag.append` / `Dag.merge`. Its `hash` members are the UI host's 64-character SHA-256 ids over an envelope with members Core's `DagNode` lacks, where Core's default `HashFn` (FNV-1a) emits 8, so walking those records would report a content-id mismatch on every one; the size fact is asserted, so the boundary goes red if it moves. The model spells a sequence number as a Peano numeral, since the extraction carries no integers; a NEGATIVE stored sequence is outside what the bridge carries, so the generated tampers stay non-negative, and the walker's check is equality, which is all the numeral supports.

- <a id="row-op-codec-injective-certified"></a>**`op-codec-injective-certified`** — `Proofs.Oracle`, 5 cases · fuaran-core#145

  The op codec is injective. It is the DOMAIN'S — `StreamWitness.Encode` belongs to whoever brings the op type — so the model takes it as a parameter (`op_codec_injective`) and the kit carries the law a witness certifies it with (`Conformance.codecInjectivityLaws`), the same division the fold theorem's `independence_diamond` runs on. Three arms: no collision drawn, the codec has a left inverse (`Decode ∘ Encode = Ok`, which makes it injective rather than merely un-collided, and which one drawn op exercises where a collision search needs the pair), and the draw compared more than one encoding. The two alphabet conditions the splice lemmas ask of production are measured beside it, over an adversarial actor population rather than a typical one, with both directions probed: a bare-id encoder is prefix-comparable and DOES collide on the same population, and a codec that erases its op makes a tamper invisible to production's own walker with the hash untouched.

  _Boundary:_ Certified here only for the work-plan witness the DAG and chain differentials themselves run on, and over the sampled ops and actor population only, never over all inputs. A codec that renders its ops through `Canonical.write Rendering.CrossHost` discharges this premise from `canonical-form-injective` rather than by sampling, with one thing to check: its payloads carry no non-finite float and no float whose token is integer-shaped, the aliasing families `canonical-render-not-injective` proves. 'Up to member order' is not a weakening there, since two objects differing only in authored member order are one value. A codec that does not render through it stays at this level.

- <a id="row-seq-numeral-code-certified"></a>**`seq-numeral-code-certified`** — `Proofs.Oracle`, 7 cases · fuaran-core#197

  The sequence numeral's code (`seq_numeral_code`), the one premise the envelope parse names beyond the DAG side's — `show` is injective and a numeral carries no comma — is measured, because production's `show` is `string : int -> string` and the model cannot inspect it: comma-free and injective over the adversarial sequences and 0..2000, beside the byte-for-byte check that `rec_payload` IS `StreamConfig.canonical.Payload` over the adversarial actors, envelope ops and sequences, and the parse's conclusion over the same population — no two distinct (seq, actor, op) triples share an envelope. The theorem's arm beside production: every payload splice — one record's content, or its actor-and-op alone, swapped for another record's with its hash and prev-link kept — is refused by `verifyChain` and by the oracle alike, over the work-plan and reference streams.

  _Boundary:_ Sampled over those populations and streams, never proved. Both alphabet premises are probed in the losing direction: under a FREE actor code the same envelope is ambiguous (the reading the model's section 6b names, exhibited), and a thousands-separated numeral defeats the first-comma cut — which is a refutation of the PROOF STEP and not of the envelope, since the bytes after that comma are `000,"actor":` and no skeleton begins with them. Comma-freedom is therefore recorded as sufficient and true of production rather than necessary. Under a hash that folds its payload the same splice is invisible to both, which is the boundary of the discharge: the parse makes the envelope injective, and `hash_injective` stays the one premise the linear side assumes.

- <a id="row-snapshot-replay-differential"></a>**`snapshot-replay-differential`** — `Proofs.Oracle`, 7 cases · fuaran-core#191

  The extracted model agrees with production's snapshot family — `Snapshots.compact` and `Snapshots.verify` in both modes, `OpStream.replay`, and the fold of a tail from a snapshot's state, driven through the tests' `SnapshotMatrix`, which keeps the retired entry points' shapes over `Snapshots` and renders each `SnapshotFault` as the model's string — over generated streams from two witnesses, at EVERY boundary of each including the out-of-range one, and over every single-record tamper of each — so the streams compared include ones that do not verify and ones that do not replay, which is where the two theorems say something a green snapshotLaws run does not. A compaction is compared whole: the refusal's message verbatim, or every field of the snapshot and the tail record for record. Beside the model-against-production comparison, PRODUCTION is held to the theorems' own statements on its own values — `replay = offset n (replayFrom snap tail)` and `verifyChain rs = (verifyChain prefix && verifyAcross snap tail)` — so a model that agreed with production while both drifted from the theorem would still be caught. Nine vacuity guards require every class the theorems speak about to have been met: an accepted bounded replay, a halt past boundary zero (the only place the offset is observable), an out-of-range refusal, a prefix refusal, a verified and a rejected boundary, a prefix tamper compacted away unseen, and a detected tail tamper. Four further cases: a model handed a different hash must disagree; the offset shown load-bearing on production alone; a prefix tamper verifying across, to production and to the model alike; and the non-empty-genesis boundary measured on production under its own config: `Snapshots.compact` in both modes, over a stream whose config's genesis is not empty, agrees with the model handed the same genesis at every boundary, verifies across at zero, and holds the split theorem under the config, while under both shipped configs (`StreamConfig.canonical`, `StreamConfig.legacyActor`, each of the empty genesis) a compaction and a snapshot are pinned byte-identical to the canonical stream's at every boundary, and the snapshots at zero by a digest vector. The differential was also reddened by hand before it was trusted, by two perturbations of the extracted oracle — an offset that drops n, and a boundary hash taken one record early — each of which failed the generated cases.

  _Boundary:_ Agreement is over those pools, never over all inputs; a prefix tamper compacted away is unseen by production and the model alike. A boundary is a Peano numeral in the model, so a NEGATIVE `atSeq`, which production refuses as out of range, is outside what the bridge carries, and the differential's boundaries start at zero.

- <a id="row-signed-head-differential"></a>**`signed-head-differential`** — `Proofs.Oracle`, 5 cases · fuaran-core#193

  The extracted model agrees with OpStream.head, attestHead, verifyAttestation and the verifyChain-and-verifyAttestation composition over generated chains from two witnesses, generated KEYRINGS, and every replace / insert / drop splice of each chain followed by a full re-mint — beside the `IAttestationSink` SEAM driven by a test-local keyring sink. The sink's `Verify` deliberately does not compare the attestation's recorded head, so binding is spent on the keyed digest rather than satisfied by construction. Each splice is spelled twice and independently, as the model's `splice` and as a plain list edit of production's steps, and the two must mint the same records; `splice_changes` must agree with whether production's steps moved. Beside the model-against-production comparison, PRODUCTION is held to `signed_head_rejects_splice` on its own verdict: every re-minted splice must pass verifyChain (or it is not the rewrite the theorem is about) and must be refused under the original attestation. Vacuity guards require a signed chain, a multi-key ring, a verifier lacking the key, a re-labelled attestation, a refused splice of each of the three kinds, an accepted no-op splice, and every in-place tamper refused. Three further cases: a model handed a different hash must disagree; under a sink that verifies EVERYTHING the re-minted splice is ACCEPTED, by production and by the model alike, so `signature_binds` is observed load-bearing; and the empty-chain sentinel measured on production. The differential was also reddened by hand before it was trusted, by two perturbations of the extracted oracle — a head read from the first record rather than the last, and a drop that drops nothing — each of which failed the generated cases.

  _Boundary:_ Agreement is over those pools, never over all inputs. Core ships no production signer, so the seam is exercised only through a test-local keyring sink and the differential claims no production signer it cannot reach.

- <a id="row-compacted-stream-differential"></a>**`compacted-stream-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#301

  The extracted model of the compacted stream agrees with production over a seeded pool of sixty counter streams of every length up to seven: `compact_from` against `Compacted.compactFrom` at EVERY (n, k) pair of every stream in both snapshot modes, out-of-range boundaries included, and over a STARVED snapshot whose state was set back to the initial one so the prefix refusal is met and its origin index compared; `append_to` against `Compacted.appendTo` at every boundary, the empty tail included; `append_full` against `append`; and `index_onto` against `KeyIndex.ofStream` and `Compacted.keyIndex` at every boundary. PRODUCTION is also held to the theorems' own statements on its own values — `compactFrom (compactAt rs n) (n + k) = compactAt rs (n + k)` and `Compacted.verify (appendTo …) = verifyChain (append …)` — so a model that agreed with production while both drifted from the theorem would still be caught. A model handed a different hash must disagree, and perturbing the extracted oracle by hand to number an appended record and a re-compaction from the tail rather than the boundary fails both comparisons.

  _Boundary:_ Agreement is over that pool, never over all inputs.

- <a id="row-capture-journal-differential"></a>**`capture-journal-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#301

  The extracted model of the capture journal agrees with production over a seeded pool of 120 sessions of up to six effect evaluations over two identities and five labels (the deterministic one included): `record_session` against `captureEffect` capture for capture, `first_capture_break_from` against `firstCaptureBreak` on every intact journal and on every single-capture tamper of its value, sequence and prev-link (each detected, by both), and `replay_session` against a fold of `replayEffectStrict` over the complete journal and over the journal missing its last capture (each stopping `Exhausted`, by both). A model handed a different hash must disagree.

  _Boundary:_ Agreement is over that pool, never over all inputs.

**Rests on, or is bounded by:** [`content-id-determines-content`](#row-content-id-determines-content) (assumed), [`signature-binds-one-head`](#row-signature-binds-one-head) (assumed).

### `JsonParse`

[`JsonParse.fst`](JsonParse.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`.

> JsonParse — an F\* model of Fuaran.Core's recursive-descent JSON PARSER, with the two guards and
> the error classification as machine-checked theorems (fuaran-core Phase 146 — the boundary
> the `WireDecode` model (Phase 135) named and left open).

**What is modelled.** `Fuaran.Core.Json.read` at no bound but the cap (src/Fuaran.Core.Wire/Json.fs),
clause for clause: `skipWs`, `expect`, `parseString` with its eight short escapes, the
`\uXXXX` path and — since Phase 306 — its refusal of a lone or ill-ordered surrogate,
`parseNumber` with the JSON number grammar (`Json.isJsonNumber`, also since Phase 306) and the
Int32 and int53 token guards behind it, `parseLiteral`, and the mutual `parseValue` /
`parseObject` / `parseArray` with the explicit depth counter — including the `EraseMemberNull`
fork, which lives in the member loop and nowhere else. The entry point's trailing-input check is
modelled too, because `TrailingCharacters` is the one classified failure the parser does NOT
raise through its internal exception. Since Phase 465 the descent also takes a LIMITS record
(F#: `JsonLimits`, through `Json.read`'s `ReadOptions`): a string-length bound counted in code points
as the string is read, an items-per-array and a members-per-object bound checked before the
item or member is read, and the document-byte bound checked before anything is — each spent as
the depth cap is, and each refused as `LimitExceeded`, a kind the descent carries internally
and that NEITHER entry point reports as malformed JSON (section 13).

**Proved** (15)

- <a id="row-parser-null-absorption"></a>**`parser-null-absorption`** — `null_absorption_is_erasure` · fuaran-core#190

  The parser's absorption of an object-member null IS the erasure: reading a document under EraseMemberNull is reading the document with those members DELETED under RejectNull — the value where the strict reader accepts, and the kind and message and position where it refuses for any reason other than a null. It is three statements: `member_null_absorbed` is the fork universally, over every key, accumulated member list, remaining budget and suffix, which is what makes it 'at every depth, at any position in any object'; `all_nulls_is_the_empty_object` is the one erased shape that composition cannot reach; `policies_agree_off_the_fork` is that the policies differ NOWHERE else.

  _With:_ `null_members_absorbed`, `null_absorption_is_erasure_witness`.

  _Boundary:_ The one-equation form is not statable here: `jval` is production's `JVal` and has no null constructor, so 'the strict tree with its member nulls erased' names nothing (the strict reader refuses at the token rather than building a tree), and the erasure is stated where it is observable — on the document. Two hypotheses are carried and both are falsifiable-by-perturbation rather than decorative (each has been run): a budget of at least one, and an erased object that still has a member. The absorbed member is spelt without interior whitespace — whitespace there is read by the policy-INDEPENDENT `skip_ws` and so sits inside the agreement statement rather than inside the fork — and the whitespace-bearing spellings, plus the escaped and non-ASCII keys the model's `plain_all` hypothesis declines, are swept by the differential's erase-then-compare probe. Grammar conformance to RFC 8259 stays unclaimed. `null_absorption_is_erasure` composes the three at the entry point and absorbs nulls in the TOP-LEVEL object only: a null nested inside the suffix makes the erased document's strict parse refuse with `NullNotRepresentable`, which both hypotheses exclude, so the every-depth statement is `member_null_absorbed`, lifted to a run of members by `null_members_absorbed`. The budget hypothesis is needed because with none the object is refused before its members are read, at a position the two documents report differently; the member hypothesis because `{}` is read by `parse_object`'s empty-object branch. The refusal half's `NullNotRepresentable` carve-out is the fork itself, not a convenience: removing it was refuted by the prover. `null_absorption_is_erasure_witness` exhibits a document meeting both hypotheses that the strict reader ACCEPTS, so the accept half is not vacuous. The erase-then-compare probe compares class, value, kind and message, and deliberately NOT position, which differs with the two documents' lengths.

- <a id="row-parse-total"></a>**`parse-total`** — `parse_total` · fuaran-core#146

  The recursive-descent JSON parser reaches exactly one outcome on every input — an accepted value or a CLASSIFIED refusal, never both and never neither. That it is Tot at all is the content rather than a formality: F\* admits the mutual group only after showing it terminates on every input, and a recursive-descent parser over a mutable index has no syntactic reason to. Every consuming sub-parser carries the remaining input's strict decrease in its RETURN TYPE, and that refinement is the whole termination argument.

  _Boundary:_ Within the model (`proofs/JsonParse.fst`): it says every input reaches exactly one outcome, not which outcome a given input reaches, and this row does not itself establish production's agreement with the model. The measure is `%[remaining input; phase]`, with `parse_items` above `parse_value` above `parse_object`/`parse_array`: those are the three edges that recurse at an unchanged input position.

- <a id="row-depth-bound-exact"></a>**`depth-bound-exact`** — `depth_bound_exact` · fuaran-core#146

  On the canonical nesting family the depth bound is EXACT at every depth and for every suffix: the parse succeeds iff the cap is at least the nesting, and refuses by name when it is not. The other two halves are depth_error_only_at_a_container (the kind is confined to a container position, carried in every signature's return type so it is re-checked at every call site) and depth_bound_scalars_unaffected (a scalar parses at a cap of zero, where every container is refused).

  _With:_ `nest_accepts`, `nest_refuses`.

  _Boundary:_ Stated over the canonical nesting family only, because the obvious form is false — the parser is left to right and the first failure wins, so a document that is both too deep and malformed earlier fails with the earlier kind. NOT claimed: that the bound prevents a stack overflow on .NET. The model proves the bound is reached and named; that 512 frames of `parseValue` fit a .NET thread's stack is an engineering judgement about a runtime. The cap is spelt as an exhausted `list unit` budget, one element per descent, because the extraction carries no integers, and the rendered cap in the message is a string parameter; every non-negative cap is representable, the shipped `Json.defaultMaxDepth = 512` among them.

- <a id="row-int53-guard-exact"></a>**`int53-guard-exact`** — `int53_guard_exact` · fuaran-core#146

  Every integer token the parser ACCEPTS denotes an int53-safe value, whichever branch produced it — JInt by the Int32 range, JFloat by the guard — OR is the canonical float layout of the double it reads as (the float reader's `FCanonical` verdict): the one integer-shaped spelling past 2^53 a canonical rendering carries, read as that double and re-rendered to the same token (`canonical_layout_past_int53_is_read`). Every other integer token past 2^53 — 2^53 + 1, a 19-digit identifier, the exact value of a double whose layout spells it differently — is refused by name (`int53_guard_refuses`).

  _Boundary:_ Past 2^53 only the canonical float layout is accepted, and it is read as that double. Note where the guard is NOT: the Int32 branch runs no int53 test and needs none, because the Int32 range is a strict subset (int32_within_int53 proves it).

- <a id="row-int53-guard-sound"></a>**`int53-guard-sound`** — `int53_guard_sound` · fuaran-core#146

  The int53 guard as production spells it — a lexical comparison against the range's own digits — never admits an integer whose VALUE is outside the range.

  _Boundary:_ This is the soundness half only, the half that matters: over-refusing is not excluded by it, and costs a user an error message, where under-refusing costs a corrupted identifier.

- <a id="row-int53-guard-conservative"></a>**`int53-guard-conservative`** — `int53_guard_conservative` · fuaran-core#146

  On a zero-padded digit string the int53 guard's lexical comparison refuses a safe value: 0009007199254740992 — exactly 2^53, the largest int53-safe integer there is — fails the test because its STRING is 19 characters, proved as a witness rather than described. It is a fact about the PREDICATE alone: the parser holds every token to the JSON number grammar before the guard reads it, the grammar has no leading zero, and the guard is therefore never handed a padded string — `int53-guard-exact-on-the-grammar` is the consequence, and `padded_token_is_refused_by_the_grammar` shows this very witness is refused as a malformed number rather than as an unsafe integer.

  _Boundary:_ Not a fact about the parser, which never hands the guard a padded string; the row stands because the lemma does — anyone who changes the comparison reddens it and is told what moved. The error only ever runs this (conservative) way; soundness is the other half, a separate row.

- <a id="row-error-kind-exhaustive"></a>**`error-kind-exhaustive`** — `error_kind_exhaustive` · fuaran-core#146

  Every one of the thirteen JsonErrorKind cases, `DuplicateMember` included, is REACHABLE by a named witness input — a classification whose cases are unreachable is closed but not exhaustive, and the difference is what makes the differential's coverage mean anything. The confinement half is carried in the parsers' return types rather than asserted once, so it is re-checked at every call site: parse_string cannot report a number failure, the descent cannot report TrailingCharacters (which the entry point constructs from the leftover-input test rather than raising), and MaxDepthExceeded cannot arise anywhere but at a container.

  _Boundary:_ Reachability is shown by one witness per case. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-parser-distinct-member-names"></a>**`parser-distinct-member-names`** — `parse_accepts_only_distinct_member_names` · fuaran-core#462

  A PARSED OBJECT HAS PAIRWISE DISTINCT MEMBER NAMES: whatever document the parser is given, under either null policy and at any cap, a value it returns holds no object that carries a member name twice, names compared in their decoded form. The refusal in `parse_members` is `has_key key acc` over the members accumulated so far, applied when a member is accumulated (after its value is read), so the theorem is the loop's invariant made visible — every accumulator carries distinct names and the object returned is that accumulator reversed (`rev_app_keys_distinct`, with `apart` carrying that the accumulator's names are not the tail's). Stated at the entry point as `parse_accepts_only_distinct_member_names`, lifted over a value as section 12 lifts well-formedness. An erased member null is never accumulated, so the tolerant reading needs no clause of its own, and `null_absorption_is_erasure` still holds beside it.

  _Boundary:_ Names are compared in decoded form, not as spelled, and an erased member null is not an occurrence. The concrete refusals beside the proof — `repeated_member_refused` (refused apart, read in sibling objects and at two depths, an erased null not an occurrence) and `raw_control_refused` — are exhibited at concrete documents rather than proved. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-parser-limited-at-no-bound-is-the-parser"></a>**`parser-limited-at-no-bound-is-the-parser`** — `limited_at_no_bound_is_the_parser` · fuaran-core#465

  THE LIMITED ENTRY POINT AT NO BOUND IS THE PARSER: for every document, policy and depth budget, `parse_limited` at `unbounded` and a true byte verdict answers exactly what `parse` answers — the same value, or the same kind, message and position. Both run the one descent, which takes a `lims` record (string, items and members bounds, each `Unbounded` or a budget spent as the depth cap is); `bounds_confined`, carried in every descent signature, is what lets the type-checker see that at `unbounded` the descent raises no breach, so `parse` keeps its thirteen kinds and every pre-existing entry point (production: `Json.read` under a record of `JsonLimits.depthOnly`, and every reader over it — `Json.parse`, `Decoder.parse`, `Reader.read`) answers every document as `parse` does.

  _Boundary:_ Holds only at `unbounded` with a true byte verdict, and with one difference: the depth cap's `MaxDepthExceeded`, which `parse_limited` lifts to `RLimit LDepth` at the same suffix. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-parser-limit-never-reported-as-malformed"></a>**`parser-limit-never-reported-as-malformed`** — `limit_never_reported_as_malformed` · fuaran-core#465

  A LIMIT BREACH IS NEVER MALFORMED JSON: whatever bounds are set and whatever the byte verdict, a refusal `parse_limited` reports as `RErr` carries one of the thirteen kinds `parse` reports — never the descent's internal `LimitExceeded` — and a breach is the distinct `RLimit` constructor, lifted from the descent before it is reported. Beside it, `bytes_checked_before_the_descent`: a false byte verdict is `RLimit LBytes` at the start of the input, before a character is read.

  _Boundary:_ Proved of the model; production's counterpart — `JsonRefusal`'s two cases and `DecodeError.ofJsonRefusal`, which maps a breach to `LimitExceeded` at its path and never to `InvalidJson` — is reached through the differential and the extractor (premise `extractor-and-compiler-trusted`), not proved directly. The breach's decode PATH is not modelled: the model reports the input suffix, as for every refusal, and production's path is checked by the decode vectors rather than proved. The byte bound enters as a verdict (`bytes_ok`), because the model's alphabet cannot express a UTF-8 length (`COther` carries a character of unknown width); what is modelled is that a false verdict is refused before a character is read.

- <a id="row-parser-string-bound-exact"></a>**`parser-string-bound-exact`** — `string_bound_exact` · fuaran-core#465

  THE STRING BOUND IS EXACT: the document `"aaa...a"` of k characters, under a string bound of m code points and no other bound, is accepted to the k-character string iff k <= m, and refused as `RLimit LString` otherwise — at every k and every m. The bound is counted inside `string_body` as `check`'s third test, after the two surrogate tests and before the unit is appended, a unit counting exactly when nothing is pending (so a low half completing a pair is not a new code point), and a unit that would be the max + 1-th is refused after it, before anything is built from it. `items_bound_exact` is the same statement for the items bound on the array of k + 1 empty strings, tested at the loop's head before the item is read.

  _Boundary:_ Proved on that one family of documents under the one bound alone. The members bound, the depth lift, a breach outranking a later malformed item, the string bound reaching a member key and the byte verdict first are exhibited at concrete documents (`limits_exhibited`), not proved over all documents.

- <a id="row-parser-items-bound-exact"></a>**`parser-items-bound-exact`** — `items_bound_exact` · fuaran-core#465

  THE ITEMS BOUND IS EXACT: the array of k + 1 empty strings, under an items bound of m and no other bound, is accepted iff k + 1 <= m and refused as `RLimit LItems` otherwise, at every k and every m — the bound tested at `parse_items`'s head, before the item's value is read, with the items accumulated so far as the count. The members bound is the same test at `parse_members`'s head, after the loop's whitespace and before the key, over the members accumulated — an erased member null was never accumulated and is not one, which is what keeps the null-absorption bridge true under a bound.

  _Boundary:_ Proved on that one family under the items bound alone. The members bound is exhibited from both sides rather than proved on a family, because a distinct-key family is the harder thing to state and the vectors pin it.

- <a id="row-parser-accepts-only-well-formed-strings"></a>**`parser-accepts-only-well-formed-strings`** — `parse_accepts_only_well_formed_strings` · fuaran-core#306

  WHAT THE PARSER ADMITS IS WELL-FORMED UTF-16. Whatever document it is given, under either null policy and at any cap, a value `Json.parse` returns holds no string and no member key in which a high surrogate is not followed at once by a low one or a low one does not follow a high one (`parse_accepts_only_well_formed_strings`, a mutual induction over the descent; `parse_string_accepts_only_well_formed` at one string). It is the hypothesis of `canonical-digest-preimage-injective`, proved of everything the parser admits. The model has no code points and needs none: whether a unit is a surrogate is read off what SPELLS it — a raw unit by its constructor, a `\uXXXX` escape by its first two hex digits, a short escape never — and `string_body` carries production's `pendingHigh`. `surrogate_refusals` pins seven witnesses with the kind, the message and the position production reports.

  _With:_ `surrogate_refusals`.

  _Boundary:_ That a raw unit's class is what the host's bridge says it is rides `parser-alphabet-bridge`, and is not proved here.

- <a id="row-parser-number-grammar"></a>**`parser-number-grammar`** — `number_grammar_is_checked_first` · fuaran-core#306

  THE JSON NUMBER GRAMMAR IS TESTED FIRST. `is_json_number` is `JsonSyntax.isNumber` (the text kernel's, which `Json.isJsonNumber` and the line reader's literal check read) in production's own three steps — an optional minus; `0`, or a non-zero digit and the digits after it; an optional point and at least one digit; an optional exponent, sign and at least one digit — and `classify_number` consults it before either numeric guard: a token outside it is `malformed number: <tok>` whatever its digits denote, and every token the parser accepts is inside it (`number_grammar_is_checked_first`). `number_grammar_boundary` pins the tokens the scanner collects and the grammar refuses (`01`, `1.`, `-.5`, `1.e5`, `1e`, `1e+`, `-`) beside the ones it reads.

  _Boundary:_ Grammar conformance for the rest of RFC 8259 — whitespace, the literals, the string escapes — is what the rest of the model carries clause for clause; nothing here is a proof that the parser accepts every RFC 8259 document, and the nesting cap alone means it does not.

- <a id="row-int53-guard-exact-on-the-grammar"></a>**`int53-guard-exact-on-the-grammar`** — `int53_guard_exact_on_the_grammar` · fuaran-core#306

  THE INT53 FINDING, CLOSED ON THE PARSER. A token is held to the JSON number grammar before either guard reads it, the grammar has no leading zero, and an integer token of the grammar is `0` or begins with a non-zero digit (`json_integer_is_unpadded`) — so on the tokens the parser reads the lexical test and the test on the value are ONE test, and the token is accepted exactly when its value is in the Int32 range, or is int53-safe and the float reader reads it, or is the canonical float layout of the double it reads as (`int53_guard_exact_on_the_grammar`, an iff). `padded_token_is_refused_by_the_grammar` shows the zero-padded witness is refused as a malformed number, not as an unsafe integer. This is the exactness the comment in Wire.fs justifying the lexical comparison assumes.

  _With:_ `json_integer_is_unpadded`, `padded_token_is_refused_by_the_grammar`.

  _Boundary:_ Exact on the PARSER only, over tokens inside the JSON number grammar: the guard's PREDICATE read alone still refuses a zero-padded safe integer by its lexical comparison (`int53-guard-conservative`).

**Tested** (1)

- <a id="row-parser-differential"></a>**`parser-differential`** — `Proofs.Oracle`, 8 cases · fuaran-core#146

  The extracted model agrees with production's parser (`Json.read` at no bound but the cap, `JsonLimits.depthOnly`) over a 102-input near-miss table under both read policies — every classified failure, the number grammar's boundary, every surrogate class in every spelling a unit can arrive in, raw and escaped, every wire-corpus nodes/ and ops/ fixture as raw text, both nesting families at every depth 0-8 under every cap 0-8, arrays and objects up to 64 members, and 4,000 seed-replayable generated inputs — comparing the outcome class, the decoded value, and on a failure the KIND, the POSITION and the MESSAGE at once — the depth cap's refusal excepted, which production reports as the `Depth` breach and the comparison reads through the model's own lift (`limited_at_no_bound_is_the_parser`) as one answer at the same POSITION, its message not compared; a production depth guard one level loose is shown to disagree past every cap. Beyond that rendering the comparison has NO carve-out: the model makes the JSON grammar and surrogate refusals itself, the bridge classifies a raw surrogate unit as the high or the low half, and the generated soup's alphabet carries both halves, so a lone, a paired and a reversed surrogate are each reached. The model also makes the two decode-determinism refusals itself: a repeated member name (`has_key` over the accumulated members, checked when a member is accumulated, so an erased member null is not an occurrence) and a raw C0 control inside a string (the bridge classifies a raw control as `CCtrl` with its `U+00XX` spelling, and the soup's alphabet carries a tab). The differential also runs `Json.read` beside the extracted `parse_limited` over hand-picked documents (each bound at and past its value, the astral and escaped spellings, a key, nested positions, a breach on either side of malformed text), the near-miss table and a 1,500-input soup under five limits records, with the platform's UTF-8 encoder as the byte verdict's independent oracle, and asserts every one of the five limits breached somewhere. Go-red cases: a model holding a cap production has already spent must accept what production refuses by name; a blind bridge hiding the two container characters must break every structured document while leaving a scalar in agreement; a model handed a wider items bound accepts what production refuses by name. The member-null fork is asserted to have FIRED, because two parsers that never met a member null agree about a policy neither exercised.

  _Boundary:_ Agreement over the sampled cases only — the near-miss table, the fixtures, the bounded nesting families, the generated soups and the hand-picked limit documents — never over all inputs.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`parser-float-readback-opaque`](#row-parser-float-readback-opaque) (assumed), [`parser-alphabet-bridge`](#row-parser-alphabet-bridge) (assumed).

### `Preservation`

[`Preservation.fst`](Preservation.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `DagFold`, `TreeOps`.

> Preservation — the apply engine's own theorem: a legal operation applied to a valid tree yields
> a valid tree, and the three clauses around that sentence which no test or law states
> (fuaran-core Phase 138).

**What is modelled.** Nothing new. `TreeOps.fst` (Phase 133, lifted by Phase 138 to the validator
Phase 137 shipped) is imported rather than remodelled, and this module adds `Ops.canApply` and
`Ops.invert` — the two surfaces of the engine that model did not reach — clause for clause.

**Proved** (20)

- <a id="row-apply-preserves-parent-first"></a>**`apply-preserves-parent-first`** — `apply_preserves_parent_first` · fuaran-core#162

  The preorder-position lemma under ANY accepted operation, batch included: if a well-formed tree accepts an operation, then in the tree it hands back a node's parent still precedes it in the walk. It is `apply-preserves-wf` composed with `preorder-parent-first`, and it lives beside the first of those because the remove clause's well-formedness lemma is that module's and the tree model cannot cite it — that module opens the tree model. `rem_preserves_parent_first` states the remove alone, in the survivor form. This is the statement a reconstruction argument cites.

  _With:_ `rem_preserves_parent_first`.

  _Boundary:_ Needs a well-formed starting tree and an accepted operation. It does NOT give the same fact about the tree in hand at step k of a script, which is a different quantifier and is the induction the diff's reconstruction still needs.

- <a id="row-apply-total"></a>**`apply-total`** — `apply_total` · fuaran-core#138

  The apply engine reaches exactly one outcome on every operation and every tree — which in the model is the type of the function, discharged by construction — and WHICH rejection each clause can raise is characterised, not listed. The characterisation is the content: `NotAContainer` and `Rejected` are proved unreachable from `apply` (`apply_never_container_or_domain`); a `Batch` inherits exactly the union of its members'.

  _With:_ `apply_never_container_or_domain`.

  _Boundary:_ Totality itself is by construction of the model's types. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-reject-identity"></a>**`reject-identity`** — `reject_identity` · fuaran-core#138

  A refused step leaves the caller holding the input tree. For one operation that is a type-level fact of persistent values that no test and no conformance law names; for a script it is the all-or-nothing discipline, and there it has content — the statement is quantified over an ARBITRARY failure position in an arbitrary batch, so however many intermediate trees the prefix built, none of them escapes.

  _Boundary:_ The single-operation case carries no content beyond the type-level fact. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-apply-preserves-wf"></a>**`apply-preserves-wf`** — `apply_preserves_wf` · fuaran-core#138

  Every accepted operation preserves id uniqueness — unconditionally, for all six skeleton operations including a nested batch and `UpdateNode`. It is true because the code's validator decides the invariant, not because the model was weakened: `validator-decides-the-invariant` is the link between the two. Its remove and move clauses rest principally on a remove taking the whole removed subtree's ids away with it, which is what makes the move's re-insert provably fresh.

  _With:_ `upd_wf`, `rem_kills`, `rem_wf`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). NOT claimed: that a preserved tree is a legal DOCUMENT. Vocabulary and schema validity are the wire boundary's question (`decode-node-wf`) and the domain rule families'; nothing here says a preserved tree is a meaningful one.

- <a id="row-keyed-apply-preserves-wf"></a>**`keyed-apply-preserves-wf`** — `keyed_apply_preserves_wf` · fuaran-core#286

  The keyed engine keeps id uniqueness over every id-bearing position a domain declares. An insert `Ops.apply` accepts under `OpScope.keyed` into a tree whose keyed walk (`Tree.traversal`: a node's keyed children, then its structural ones) repeats no id leaves a tree whose keyed walk repeats none. It is not a second preservation proof: `erase_kins` shows the keyed insert, read through the traversal, IS the unkeyed insert over the keyed walk — the structural list is the tail of the combined one, so appending to it appends to the combined list — and `kapply_insert_is_apply` that the keyed clause therefore answers exactly what `Ops.apply` under an unkeyed scope answers there, so `apply_preserves_wf` transfers and its `InsertChild _ _, DuplicateId _` clause quantifies over the keyed walk (`keyed_dup_refusal_exact`). The model is a concrete two-list tree, not a parameter.

  _Boundary:_ The keyed engine's other clauses bring no id in, except `UpdateNode`'s keyed payload, whose check is tested (`KeyedApplyTests.fs`) and not modelled; the declaration's own accuracy is `witness-surface-scope`, a domain obligation.

- <a id="row-canapply-preserves"></a>**`canapply-preserves`** — `canapply_preserves` · fuaran-core#138

  The dry run and the mutating call accept exactly the same operations, and what the mutating call then produces is well-formed; the sampled `canApply` = `apply` law becomes a corollary. The part worth naming is what the proof needed: two defensive `UnknownNode` fallbacks the shipped engine carries after its `parentOf` lookup are UNREACHABLE, because a non-root id the tree holds always has a parent — so the two surfaces cannot diverge by one of them taking a branch the other does not.

  _With:_ `parent_exists`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-invert-round-trip"></a>**`invert-round-trip`** — `invert_applicable` · fuaran-core#138

  The inverse of an accepted operation is itself accepted at the result and restores the input exactly, for the five non-batch operations (the four structural ones and `UpdateNode` — `leaf_op` is every non-`Batch` op). Both halves in one statement: nothing about the derived inverse can be refused at the tree the forward step produced, and applying it gives back the tree the forward step was applied to. The defining law `apply (invert op pre) (apply op pre) = pre`, which the engine's own doc comment states and the conformance pack samples, is a theorem here.

  _With:_ `parent_node_restore`.

  _Boundary:_ The batch alphabet is excluded and the exclusion is stated: a batch's inverse is its members' inverses in reverse order, each derived against the state that member saw, so the lift is the one the fold theorem already performs at lane granularity. That lift is proved at `invert-batch-round-trip`.

- <a id="row-invert-batch-round-trip"></a>**`invert-batch-round-trip`** — `invert_batch_round_trip` · fuaran-core#305

  `Ops.invert` over the WHOLE skeleton alphabet, `Batch` nested to any depth: on a well-formed tree the inverse of an accepted operation is accepted at the result and restores the tree exactly, and an operation that is refused has no inverse (the inverse exists iff the forward operation applies). The `Batch` arm is modelled as production has it — the pre-state threaded through the members and each inverse PREPENDED, so a batch undoes as its members' inverses in reverse forward order, each derived against the state that member saw — and the induction carries the accumulator: the inverses collected so far take the current state back to the original. `invert_applicable` (the leaf alphabet) is the per-member step.

  _Boundary:_ Requires a well-formed tree. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-invert-all-round-trip"></a>**`invert-all-round-trip`** — `invert_all_round_trip` · fuaran-core#305

  `Ops.invertAll`, the script-level inverse: on a well-formed `pre`, `applyAll (invertAll s pre) (applyAll s pre) = pre` — the undo script is accepted at the tree the script reaches and restores `pre` exactly — and the undo script exists iff the script applies. The same accumulator induction as `invert_batch_round_trip`, over a list rather than a `Batch`; the model carries `applyAll`'s 0-based index of the first refused op, which the production law's refusal clause states and the `ContentDiffTests` law samples.

  _Boundary:_ Requires a well-formed `pre`. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-contained-clause-for-clause"></a>**`contained-clause-for-clause`** — `apply_contained_is_apply` · fuaran-core#140

  The container-aware engine is the plain one plus a guard, and the model of it is faithful clause for clause. At `canHold = fun _ -> true` the two are the SAME FUNCTION — which is what `Ops.apply = applyWith (fun _ -> true)` asserts in the code — and proving it discharges the two lookups the model writes as total matches where the engine writes a fall-through: a parent whose id the tree holds is always found, so neither engine can take a branch the other does not.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-not-a-container-exact"></a>**`not-a-container-exact`** — `not_a_container_exact` · fuaran-core#140

  `NotAContainer` is exactly the container violation's refusal: `apply` never raises it (`apply_never_container_or_domain`), the container-aware engine differs from `apply` ONLY by raising it, and where it raises it the named node really holds children, the reported kind tag is that node's own, and the predicate refuses it. The middle clause is the one with content — it says the capability can never accept what `apply` refuses, never produce a different tree, and never refuse under another class of its own. The located half is a DISJUNCTION, which is content rather than a weakening: there are two capability sites and they name nodes in two different places — the (new) PARENT, a node of the tree, and an interior node of the SUBTREE an `InsertChild` carries, which the duplicate-id scan guarantees is NOT in the tree; a single tree lookup would be false of the second site.

  _Boundary:_ It deliberately does NOT say the capability only adds a refusal: it can PRE-EMPT another one. A self-move into a leaf is `NotAContainer` here and `WouldNestUnderSelf` under `apply`, because the capability test precedes the descendant test in the shipped `MoveNode` arm — so the theorem reads `identical, or NotAContainer`, and a reading of the form `the same class unless the capability adds one` is false.

- <a id="row-contained-preserves"></a>**`contained-preserves`** — `contained_preserves` · fuaran-core#140

  `Ops.apply` under `OpScope.contained` preserves the invariant the capability exists to keep — every node with children satisfies `canHold` — for all six operations including a nested batch and `UpdateNode`, with `MoveNode` covered as well as `InsertChild`. The move clause is the one with content: the moved subtree inherits the invariant from the tree it came out of, the removal preserves both the invariant and the destination's capability, and the graft is then an insert under an admitted parent — a derivation that needs no hypothesis about the operation at all, which is also why the graft walk sits at `InsertChild` and not at `MoveNode`.

  _With:_ `upd_contained`, `insert_only_breaks_contained`.

  _Boundary:_ One hypothesis remains, `child_blind`: the statement is refuted without it (`contained-premises`), and it is certified for an adopting domain by `Conformance.containerLaws` rather than assumed. The statement's other hypothesis is discharged in code (`graft-containment`). NOT claimed: containment LEGALITY, which kind may parent which. `canHold` answers only whether a node can hold children at all; a domain's parenting rules are its own, and no theorem here says a contained tree is a legal document.

- <a id="row-contained-premises"></a>**`contained-premises`** — `contained_needs_child_blind` · fuaran-core#140

  The preservation theorem's remaining hypothesis is necessary, by a machine-checked counterexample the module can evaluate — the `insert_breaks_wf_pre137` shape. `contained_needs_child_blind`: `canHold` has type `'Node -> bool`, so it may read the CHILD LIST, and a predicate that does can admit a node at the moment it is checked and refuse it the instant it gains a child — every other hypothesis holds and the invariant breaks anyway. An adopting domain does not merely inherit the assumption: `Conformance.containerLaws` perturbs a node's children and requires `canHold` to be unchanged, so a domain whose predicate reads the child list fails a law.

  _Boundary:_ This is the one premise NO engine check can discharge, because the predicate's answer changes under the very edit the check licensed; the other premise (`contained_op`) is discharged by a code change and recorded as `graft-containment`. It is not a modelling convenience: it is a property of a real predicate that the engine's type does not demand, and naming it is what makes the theorem about `Ops.apply` under a contained `OpScope` rather than about a function nobody calls.

- <a id="row-graft-containment"></a>**`graft-containment`** — `nested_graft_refused` · fuaran-core#161

  A premise DISCHARGED BY A CODE CHANGE, with its refutation kept evaluable rather than deleted along with the defect. The premise `contained_op` said the operation's own trees satisfy the container invariant; it was needed while `canHold` was applied to the PARENT of an insert and to nothing inside the subtree, so a graft whose own interior node held children while the predicate refused it was accepted whole and the invariant broke across an ACCEPTED operation. Per the operator's ruling (DECISIONS D38), `validateInsert` walks the inserted subtree and refuses with `NotAContainer` naming the first interior offender — last in the clause order, so no operation the engine already refused changes its class. `nested_graft_refused` states both halves in ONE lemma: the earlier engine (`apply_contained_pre161`, kept for the reason `TreeOps.apply_pre137` is kept) still admits the graft and still breaks the invariant, and the shipped engine refuses the same operation naming "a" — the graft's own root, which holds a child while the predicate refuses it — rather than the parent, which can hold children perfectly well. Removing the walk turns the second half red. `contained_graft_still_admitted` is beside it because without a not-over-eager half the first would be satisfied by an engine that refused every insert.

  _With:_ `contained_graft_still_admitted`.

  _Boundary:_ The walk is at `InsertChild` ONLY: a move relocates a subtree already in the tree, so it carries in no interior the tree did not already hold, and `contained_preserves`' move clause derives exactly that with no hypothesis about the operation.

- <a id="row-canapply-contained-agrees"></a>**`canapply-contained-agrees`** — `can_apply_contained_agrees` · fuaran-core#140

  The container-aware dry run returns exactly what the container-aware mutating call returns, envelope included — `Ops.canApply` under `OpScope.contained` is the dry run of the step `Ops.apply` takes under that scope, sharing its checks and simulating a `Batch` through the step itself.

  _Boundary:_ It rests on the corollary the remove clause needs — the `parentOf` fallback the engine carries is unreachable — so the two surfaces cannot diverge by one taking a branch the other does not.

- <a id="row-container-sequence-gap"></a>**`container-sequence-gap`** — `can_apply_all_ignores_containment` · fuaran-core#140

  The plain sequence surface is blind to containment, proved rather than asserted: `Ops.canApplyAll` and `Ops.applyAll` under `OpScope.plain` both thread the plain step, so a script the plain `canApplyAll` certifies can be refused by `Ops.apply` under `OpScope.contained` at its first step — the counterexample is one move of a leaf under a leaf (`can_apply_all_ignores_containment`). The plain pair is the `fun _ -> true` instance of the same two functions under `OpScope.contained` and is therefore blind to containment by construction, so the lemma pins that the container-aware pair moved no existing caller.

  _Boundary:_ A caller that pre-flights with `canApplyAll` under `OpScope.plain` and executes with `Ops.apply` under `OpScope.contained` has a check that cannot see the refusal its executor would make. The absence of a container-aware sequence surface is closed by `Ops.applyAll` / `Ops.canApplyAll` under `OpScope.contained` (`container-sequence-gap-closed`), not by changing the plain pair.

- <a id="row-contained-preserves-script"></a>**`contained-preserves-script`** — `contained_preserves_all_with` · fuaran-core#160

  The container invariant survives a SCRIPT under `Ops.applyAll` at a contained `OpScope`, on BOTH arms — the accepted tree and the partial tree a refusal hands back — over any predicate, any script and any well-formed tree carrying the invariant (`contained_preserves_all_with`). The second arm is what a per-operation theorem structurally cannot state: a refused `Ops.apply` returns no tree at all, while a refused script keeps the accepted prefix and hands it to the caller, so a preservation statement covering only the accepted case would be silent about precisely the state a non-atomic surface exists to produce.

  _Boundary:_ Same hypothesis as `contained-preserves`, and the same one: `child_blind`, refuted without it by `contained-premises`. No `contained_op_all` premise is needed, because the sequence lemma only uses the per-operation premise through that call and the engine inspects a graft.

- <a id="row-canapply-all-with-agrees"></a>**`canapply-all-with-agrees`** — `can_apply_all_with_agrees` · fuaran-core#160

  The container-aware sequence dry run returns exactly what the container-aware sequence apply returns — same verdict, same refusal INDEX, same envelope (`can_apply_all_with_agrees`). The index is the half that makes the statement worth having for a script: a pre-flight that agreed on accept-vs-refuse while disagreeing about WHICH step failed would be useless to a caller deciding what to do next. It needs no hypothesis: the sequence pair cannot diverge by construction, since both fold the SAME per-operation call — which is also the honest reading of why the shipped `canApplyAll` threads `apply` rather than `canApply`.

  _Boundary:_ Within the model. Contrast `canapply-contained-agrees`, which does require `wf t`, because the per-operation dry run reaches the three validators directly and must be shown not to take a branch the mutating call does not.

- <a id="row-sequence-surface-total-instance"></a>**`sequence-surface-total-instance`** — `all_with_at_total_is_plain` · fuaran-core#160

  `Ops.applyAll` and `Ops.canApplyAll` under `OpScope.plain` ARE the `fun _ -> true` instances of the same two functions under `OpScope.contained` (`all_with_at_total_is_plain`), so no existing caller's behaviour moved when the capability arrived — the claim an additive API change has to earn, proved rather than asserted. It is discharged against `TreeOps.apply_all` and `Preservation.fst` section 8's `can_apply_all`, the two folds as previously shipped, so what is proved is agreement with the shipped behaviour rather than with a restatement of itself.

  _Boundary:_ The plain pair is blind to containment and always will be, that being what the total instance means, so a caller that pre-flights with `canApplyAll` under `OpScope.plain` and executes with `Ops.apply` under `OpScope.contained` still has the gap `container-sequence-gap` records; the container-aware pair is the surface that closes it.

- <a id="row-container-sequence-gap-closed"></a>**`container-sequence-gap-closed`** — `can_apply_all_with_sees_containment` · fuaran-core#160

  `container-sequence-gap`'s counterexample, answered: at the same script and tree, the plain sequence dry run certifies it, and the container-aware one refuses it at index 0 with `NotAContainer("leaf", "para")` — the envelope `Ops.applyAll` under `OpScope.contained` raises — while the mutating call hands the tree back untouched, because the refusal is at the first step and the accepted prefix is empty (`can_apply_all_with_sees_containment`). The two lemmas are read TOGETHER: the older one is the pin that the plain pair's behaviour did not move.

  _Boundary:_ What is closed is the absence of ANY container-aware sequence surface, not the plain pair's blindness — the plain pair is blind by construction, and changing that would move every existing caller.

**Tested** (3)

- <a id="row-preservation-differential"></a>**`preservation-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#138

  The extracted preservation model agrees with the shipped `Ops.apply`, `Ops.canApply` and `Ops.invert` over the generated pool AND over grafts minted per state from the state's own ids: a subtree whose descendant id the tree already holds, a subtree that repeats an id within itself, and one that does both. Compared at once: the verdict, an accepted result through production's own `Tree.encodeHash`, a rejection by class, the dry run against the mutating call on each side separately, the derived inverse as an operation, and the round trip run on production. The go-red hands the differential the model of the narrower, pre-fix validator and requires it to lose — a comparison the generated pool alone cannot make fail, because the generator's contract is a fresh id and it cannot mint a colliding graft.

  _Boundary:_ Agreement is over the pool drawn and the minted grafts, never over all inputs. A refusal is compared by class only: `UnknownNode`'s `addressable` and `ReorderMismatch`'s two orders are outside the comparison. Adequacy is guarded per disputed shape, each shape re-read through `Tree.ids` rather than trusted from the construction that made it (measured at 12 trials: 276 accepted, 1,082 refused, 507 colliding grafts, 294 internally duplicated grafts, 276 inversions).

- <a id="row-container-differential"></a>**`container-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#140

  The extracted container model agrees with the shipped `Ops.apply` and `Ops.canApply` under `OpScope.contained` over a GENERATED `canHold` — a random subset of the pool's kind vocabulary per trial, including the empty predicate (nothing can hold children) and the total one (where the engine is `Ops.apply` under `OpScope.plain` again), all eight drawn. Five comparisons per probe: the verdict and the accepted result through production's own `Tree.encodeHash`, the rejection by class, the dry run against the mutating call on each side, the contained scope against `OpScope.plain` on production, and the invariant asserted on production wherever its hypothesis is met. Adequacy is counted per capability SITE — insert refusals, move refusals, batch inheritance, graft refusals and pre-emptions separately — because a run that reached only the insert site could not catch the go-red, which is a move. The go-red is the model's own instrument: an engine checking the capability on insert and not on move, proved in F\* to admit a move the real one refuses, required to lose here. The GRAFT site is reached by construction: a leaf graft can never carry an interior offender, so the probes mint a graft whose root holds a child in both a container kind and a leaf kind, and both verdicts arise within one run. Measured at 30 trials, seed 1400: 1,245 accepted, 1,305 refused, 649 insert refusals of which 124 are the graft site, 370 move refusals, 112 batch inheritances, 89 pre-emptions, the invariant asserted on 1,028 probes.

  _Boundary:_ Agreement is over the pool drawn, never over all inputs. The predicate is drawn because a differential run against one hand-picked predicate would certify that instance and nothing about the quantifier the theorems carry.

- <a id="row-script-differential"></a>**`script-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#160

  The extracted sequence model agrees with the shipped `Ops.applyAll` and `Ops.canApplyAll` under `OpScope.contained` over generated scripts and a DRAWN `canHold`. Four things are compared, the last two being the sequence surface's entire contribution over the per-operation one: the verdict and the accepted tree through production's own `Tree.encodeHash`, the rejection class, the refusal INDEX, and the PARTIAL TREE the refusal hands back. Adequacy counts MID-script refusals separately and asserts them, because a refusal at index 0 returns the caller's own input and so compares the partial tree against nothing — measured at 24 trials, seed 1600: 45 scripts accepted, 123 refused, 60 of those mid-script, 103 of them NotAContainer, all 8 predicates drawn. The go-red is the model's own capability-free sequence check, which `container-sequence-gap` proves to be a weakening, substituted into the dry-run slot and required to lose.

  _Boundary:_ Agreement is over the pool drawn, never over all inputs. The predicate is drawn because the theorems quantify over it, and a run against one hand-picked instance certifies that instance and nothing about the quantifier.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed), [`witness-surface-scope`](#row-witness-surface-scope) (assumed).

**Inherited through the models it opens:** [`independence-diamond`](#row-independence-diamond) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `FootprintIn`

[`FootprintIn.fst`](FootprintIn.fst) — checked only; its production side cannot be run beside an extraction (`check.ps1` says why). Opens `DagFold`, `TreeOps`, `Preservation`.

> FootprintIn — the two theorems about Fuaran.Core's base-aware footprint (fuaran-core Phase
> 500): `footprint_in_sound` and `footprint_in_refines`, over the model `TreeOps.fst` section 24
> gives of `Ops.footprintIn`.

**Proved** (2)

- <a id="row-footprint-in-refines"></a>**`footprint-in-refines`** — `footprint_in_refines` · fuaran-core#500

  Every pair of operations the pure footprint (`Ops.footprint`) calls independent, the base-aware footprint (`Ops.footprintIn`) calls independent, at every tree — and the same for two scripts, each threaded from the base, which is what `Dag.conflictsIn` reads against `Dag.conflicts`. So a consumer that moves from the pure projection to the base-aware one loses no fold and no accepted proposal. Proved by the pure footprint's own elimination run the other way: a pure-independent pair either has no relocating side, where each base-aware footprint IS the pure one, or has one, where the other side is inert and its base-aware footprint is empty.

  _With:_ `footprint_in_refines_scripts`, `inert_fp_in`, `no_reloc_fp_in`.

  _Boundary:_ Refinement of the VERDICT only: the base-aware footprint is not a subset of the pure one address for address (it names a source parent and a destroyed subtree the pure one cannot), and no claim is made about `Ops.interference`'s clause list beyond its emptiness. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-footprint-in-sound"></a>**`footprint-in-sound`** — `footprint_in_sound` · fuaran-core#500

  For the skeleton-op tree algebra, two operations built from `InsertChild`, `RemoveNode`, `MoveNode`, `ReorderChildren`, `UpdateNode` and `Batch` — every skeleton op — whose base-aware footprints at an id-unique tree are declared disjoint and which both apply there each apply after the other and reach the same tree, in the shape of the pure diamond (`wstep`); and each one's base-aware footprint at the tree the other leaves is its footprint at the base (`footprint_in_stable`), which is what lets a fold read every footprint once, at the base, and stand on it at every state it reaches. No pair is closed by a footprint clause: the twelve pairs that carry a rewrite, a removal or a move — the pairs the pure footprint refused through its unknown-parent clause — are each a commutation proved on the tree (a rewrite beside a rewrite of another node, an insert whose subtree does not carry its target, any reorder, or any removal outside its target; a removal beside a removal at another parent, an insert or a reorder elsewhere; a move beside a rewrite anywhere, an insert, a reorder or a removal anchored at neither its source parent nor its destination and destroying neither its target nor an ancestor of its destination, and a move whose destination lies outside its subtree), with every id outside a destroyed subtree proved to survive the removal at the same node under the same parent, every id proved to survive a move with every other node found at the same node and every parent but the target's kept, and the path to a node proved unchanged by every edit that does not relocate it — which is what keeps the ancestors a move's footprint reads where they were — and the diamond is lifted along a batch's script as the pure one is. The two acceptance cases are stated outright: two rewrites of different present nodes are independent wherever they sit (`two_updates_independent`), and a rewrite inside the subtree a concurrent removal destroys is refused (`update_under_removal_refused`).

  _With:_ `footprint_in_sound_scripts`, `footprint_in_stable`, `footprint_in_sound_no_move`, `footprint_in_sound_in_place`, `two_updates_independent`, `update_under_removal_refused`, `all_leaf_in`, `move_move_in`, `move_insert_in`, `move_reorder_in`, `move_update_in`, `move_remove_in`, `move_find`, `move_parent`, `move_path`, `anc_of_through`, `no_move`, `in_place`, `wstep_in`.

  _Boundary:_ Holds only for footprints declared disjoint, both operations applicable, and an id-unique tree (`tree-algebra-well-formed-states`). Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed).

**Inherited through the models it opens:** [`independence-diamond`](#row-independence-diamond) (assumed), [`witness-surface-scope`](#row-witness-surface-scope) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `TreeDiff`

[`TreeDiff.fst`](TreeDiff.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `DagFold`, `TreeOps`, `Preservation`.

> Diff — the apply engine's companion in the other direction: `Diff.toOps` derives a script that
> turns one tree into another, and this module is that derivation modelled clause for clause
> (fuaran-core Phase 141).

**What is modelled.** `Diff.fs`'s `Diff.toOps` — the two refusals, the four passes and the order they
are emitted in — and `Diff.toOpsContained`, its `canHold`-aware mirror. The tree, the operations,
the rejection envelope and the whole membership algebra are `TreeOps`'s, opened here rather than
restated, exactly as `Preservation.fst` opens them; this module extracts beside both into the
same oracle assembly.

**Proved** (12)

- <a id="row-diff-refusals-exact"></a>**`diff-refusals-exact`** — `diff_refusals_exact` · fuaran-core#141

  `Diff.toOps` reaches exactly one outcome on every input, and WHICH is characterised: it refuses precisely when the two roots carry different ids or either tree repeats one, in that precedence, with the class each raises — and it refuses on NOTHING else (`diff_refusals_exact`). The converse is a lemma of its own (`diff_ok_on_any_wf_pair`): for ANY two well-formed trees sharing a root id a script is produced. Termination is discharged by the `Tot` annotation rather than argued — every pass recurses on a list derived from one of the two trees by the preorder walk.

  _With:_ `diff_total`, `diff_never_target_not_a_container`.

  _Boundary:_ The any-pair converse is the model's: on the production side `Conformance.diffLaws` only samples pairs one of which was derived from the other by applying operations. The plain diff never raises `TargetNotAContainer` (`diff_never_target_not_a_container`): that class is the container-aware form's alone. The characterisation is of the model, which takes no policy and an unkeyed scope: the shipped `Diff.toOps` also refuses `IllegalChild` under a policy with a `Grammar` (fuaran-core#313) and `KeyedPosition` under a keyed scope (fuaran-core#518), after the refusals modelled here, and neither is modelled.

- <a id="row-diff-emission-order"></a>**`diff-emission-order`** — `diff_emission_order` · fuaran-core#141

  The emitted script IS `inserts ++ moves ++ removes ++ reorders` — four homogeneous blocks in that order, each exactly one pass's output (`diff_emission_order`). This mechanises the argument `Diff.fs`' own doc comment on `Diff.toOps` carries in PROSE ("added nodes go in as shells (top-down), every survivor is then reattached/reordered to its `after` position, and removed regions are deleted **last** (so a surviving child is pulled out before its old container is removed)"), and every applicability claim about a diff script rests on it. It also licenses the differential's order go-red: a stable partition by operation kind recovers the four blocks exactly, so reassembling them with the removes first is the same four passes permuted rather than a different algorithm.

  _Boundary:_ A fact of the model; it reaches the shipped `Diff.toOps` only through the differential whose order go-red it licenses.

- <a id="row-diff-block-shape"></a>**`diff-block-shape`** — `diff_script_shape` · fuaran-core#141

  Each of the four passes guarantees a shape for the block it emits (`diff_script_shape`), the emission argument's four clauses: an insert carries a CHILDLESS graft whose id `before` does not carry, under that node's after-parent; a move names a survivor whose before-parent is not the destination, and the destination HOLDS it in `after`; a removal targets a node `after` does not carry whose before-parent survives — SURVIVOR PRESERVATION, so a relocated survivor diffs to a `MoveNode` and never to a remove-and-reinsert; and a reorder states the after-order of a parent with more than one child. The four predicates hold vacuously on the operations the other passes emit, so they compose over the whole script and a reader holding one operation gets the fact belonging to it without knowing which block it came from. The differential evaluates this same conjunction over PRODUCTION's emitted script, so the theorem is held to the engine and not only to the model of it.

  _With:_ `diff_inserts_are_new_leaf_shells`, `diff_moves_are_survivor_reattachments`, `diff_removes_only_dead`, `diff_reorders_state_the_after_order`.

  _Boundary:_ Proved of the model; on the shipped engine survivor preservation is sampled by `Conformance.diffLaws` and the conjunction is evaluated by the differential over the pool drawn, not proved.

- <a id="row-diff-contained-total-instance"></a>**`diff-contained-total-instance`** — `diff_contained_at_total_is_plain` · fuaran-core#141

  `Diff.toOps` under `OpScope.plain` IS `Diff.toOps` under `OpScope.contained` at a predicate that refuses nothing (`diff_contained_at_total_is_plain`) — the relation `Ops.apply` has across the same two scopes, in the diff direction, and what makes the clause-for-clause model of the pair checkable rather than asserted.

  _With:_ `diff_contained_diff`, `diff_contained_locates`.

  _Boundary:_ Read beside `diff_contained_diff`: the container-aware diff is the plain diff unless it is a `TargetNotAContainer`, stated as the DIFFERENCE rather than as "the same unless the capability adds a refusal", the shape the apply-side container differential went red on and corrected for. When it refuses, the offender it names is a real node of `after` that carries children and that `canHold` refuses (`diff_contained_locates`).

- <a id="row-diff-applicable-contained"></a>**`diff-applicable-contained`** — `diff_applicable_contained` · fuaran-core#141

  Every parent address an operation of a contained script carries resolves, in `after`, to a node that HOLDS the addressed child and that `canHold` accepts (`diff_applicable_contained`). A `toOps`-emitted insert is safe under an INTERIOR containment check too: the graft is childless, so a validator that walks it for non-containers finds nothing to walk.

  _Boundary:_ This is the ADDRESS half, in `after`, and does NOT say such a script cannot be refused for containment at any step — that is FALSE: the script runs against `before`'s kinds and the structural diff carries no content, so a survivor that is a leaf in `before` and a container in `after` refuses the inserts under it with `NotAContainer` (`TreeDiff.contained_script_refused_at_before_kinds` pins the pair; the second-pass review measured it on 41% of drawn pairs). The content-aware `Diff.toOpsWith`, under the same contained `OpScope`, is the form that applies under the predicate it checked (D103's placement rule; `ContentDiffTests`' bridge samples it, 0 refused of 1,540), and its run theorem is `diff-applicable-contained-run`. Because `canHold` may read a node's CHILD LIST (the `child_blind` counterexample, `contained-premises`), the node the engine resolves at apply time is the same node only up to the children the script has already moved; the statement is about the parents the diff ADDRESSES, which is the half the diff can be responsible for. Not covered: containment LEGALITY, which kind may parent which. `canHold` answers only whether a node can hold children at all, so no theorem here says a contained tree is a legal document; production's kind-pair `Grammar` refusal (`IllegalChild`, fuaran-core#313) is not modelled.

- <a id="row-diff-applicable-contained-run"></a>**`diff-applicable-contained-run`** — `diff_applicable_contained_run` · fuaran-core#305

  `Diff.toOpsWith` under a contained `OpScope` applies under the predicate it checked and lands on `after`, content included: every script it returns is accepted at every step by `Ops.applyAll` under `OpScope.contained canHold` and reaches `after` — the tree, not a tree like it — with NO `kinds_agree` hypothesis, because the rewrites carry the kinds across (`diff_applicable_contained_run`). The model (`to_ops_contained_with`) is clause for clause: `changed_kind` is the injective encoder over the two shells (the kind tag is the content the witness shows), `pass_updates_first` the rewrites whose new node `canHold` accepts, placed before the four structural passes, `pass_updates_last` the rest, placed after them (DECISIONS D103.1). The proof reuses section 10's induction rather than restating it: the first block takes `before` to `recolour` — the same tree with those survivors' kinds taken from `after` (`updates_first_run`); the four passes emit the SAME script for `recolour` as for `before`, since they read ids, parents and child lists and never a kind (`diff_blocks_recolour`), so `diff_run` at `recolour` gives the plain run; the contained engine agrees with the plain one at every structural step because every parent an insert or a move addresses already carries `after`'s kind, which `canHold` accepts (`contained_run`, over a kind invariant each structural step keeps); and the last block rewrites the leaves `canHold` refuses, each holding no children by then (`updates_last_run`), after which the tree is `after` node for node (`tree_ext`). Section 12's pair — the one the structural contained script is refused on — is evaluated repaired (`content_aware_script_applies_where_the_structural_one_is_refused`).

  _With:_ `structural_step_kinds`.

  _Boundary:_ Hypotheses: `before` and `after` well-formed and sharing a root id, and `canHold` child-blind. Within the model; the correspondence to the shipped `Diff.toOpsWith` under a contained `OpScope` is the differential's (`content-diff-differential`).

- <a id="row-diff-after-positional"></a>**`diff-after-positional`** — `diff_move_destination_is_outside` · fuaran-core#162

  The positional facts the diff's emitted script carries about `after`, over `preorder-parent-first`: an insert's parent precedes its own node in the after tree's preorder — the walk pass 1 iterates, so the source comment's `added nodes go in as shells (top-down)` is a property of the emitted operations rather than of the loop — a move's destination precedes the moved node, and therefore a move's destination is NEVER INSIDE the moved node's subtree in `after` (`diff_move_destination_is_outside`). The last closes by antisymmetry of the ordering predicate plus its irreflexivity on an id-unique tree. The pair of theorems is evaluated on a concrete before/after that emits exactly one insert and one move, which also pins that the moved node does not precede its destination, so the claim is an ordering fact and not a tautology.

  _With:_ `diff_insert_parent_precedes`, `diff_move_destination_precedes`, `positional_facts_are_not_vacuous`.

  _Boundary:_ READ THE QUANTIFIER: this is about `after`, and the apply engine validates each step against the tree IN HAND — the before tree with the script's earlier operations run on it. Carrying the fact across is an induction over an invariant about how much of `after` each PREFIX of the script has built, which this row does not contain (it is `diff-applicable`'s).

- <a id="row-diff-applicable"></a>**`diff-applicable`** — `diff_applicable` · fuaran-core#167

  Every operation `Diff.toOps` emits is ACCEPTED, in sequence, against the tree IN HAND at that step — so `Ops.applyAll` never refuses a diff's own script (`diff_applicable`). This OPERATIONAL statement is a different claim from `diff-applicable-contained`, which is about the parent addresses a CONTAINED script carries and is a fact about `after`. The proof is `TreeDiff.fst` section 10's four-invariant induction, one invariant per pass over the suffix of that pass's worklist still to run, chained through `apply_all_app`; it relates the positional facts, which hold of `after`, to the intermediate tree `apply` validates against. The intermediate-tree lemmas underneath it are `Preservation.fst` section 11.

  _With:_ `inserts_run`, `moves_run`, `removes_run`, `reorders_run`, `diff_run`, `apply_all_app`.

  _Boundary:_ No hypothesis beyond two well-formed trees sharing a root id: a kind disagreement on a shared id costs RECONSTRUCTION and not applicability, which is pinned by `kinds_agree_is_necessary`. Not covered: `Diff.toOpsMoved` (fuaran-core#63, the move-aware diff), which is not shipped and so not modelled. The four `_run` lemmas are stated over `toOps`' four passes by name, so a second emission strategy needs its own; it would inherit `tree_ext` and `Preservation.fst` section 11, not the induction.

- <a id="row-diff-reconstructs"></a>**`diff-reconstructs`** — `diff_reconstructs` · fuaran-core#167

  `applyAll (Diff.toOps before after) before == Ok after` — the tree, and not merely a tree like it: the last step is `tree_ext`, an induction over the TREE proving that a tree agreeing with `after` at every node on kind and on child ids IS `after` (`diff_reconstructs`). `reconstruction_is_not_vacuous` evaluates a pair whose script carries one insert, one move, one remove and one reorder, so the theorem is not a statement about the empty script.

  _With:_ `tree_ext`.

  _Boundary:_ IT CARRIES ONE HYPOTHESIS AND THE HYPOTHESIS IS NECESSARY: `kinds_agree` — a node id the two trees share names the same kind in both. The unrestricted form is FALSE rather than merely unproved, because `Diff.toOps` is a SHAPE diff: it emits no `UpdateNode`, so a shared id whose kind differs is unreconstructible by any script the diff emits (the operation alphabet can rewrite a node in place — this diff does not; the content-aware form is a separate diff); `kinds_agree_is_necessary` pins a concrete such pair whose diff is the EMPTY script, applicable at every step, landing on `before`. The hypothesis (the two trees agree on the content of every shared id) is a precondition the CALLER owes and no theorem here discharges: it is the condition under which asking a shape diff for a script is a well-formed question at all. It constrains only this row; `diff-applicable` and `diff-applicable-contained-run` hold without it. The differential's pool enforces it (a generator that redrew kinds independently reconstructed 1,738 of 4,000 pairs against 20,000 of 20,000 for the corrected one), and `diff-differential`'s third go-red measures its necessity on the shipped engine; it is now a hypothesis of a proved statement rather than only a property of a generator. The intermediate-tree facts the induction needs, the per-node view (`kids_at` / `kind_at`) and what `ins` and `rem_at` do to it, one lemma per operation per node, are `Preservation.fst` section 11, which `TreeDiff.fst` opens: inductions over `ins` and `rem_at` are that module's cost class.

- <a id="row-subtree-digest-injective"></a>**`subtree-digest-injective`** — `subtree_digest_injective` · fuaran-core#314

  Two trees with one Merkle subtree digest are one tree (`Tree.digests`' `Subtree` map): `subtree_digest h cf enc t == subtree_digest h cf enc u ==> t == u`, for ANY encoder `enc` (`subtree_digest_injective`). The encoder needs no hypothesis because the id and the kind are fields of the own pre-image in their own right (`own_digest h cf enc t = h (cf [id; kind; enc shell])`), so the rollup recovers ids, kinds and shape unconditionally. This discharges, once, the obligation the document domain's roadmap had queued against its own copy of the rollup.

  _Boundary:_ Under the injectivity of the hash `h` (F#: `Hash.sha256Hex` — the standing assumption every digest on the spine rests on, so a collision here is a SHA-256 collision) and of the field encoding `cf` (F#: `Hash.canonicalFields`, proved injective by `canonical-digest-preimage-injective`). What the encoder adds is the content beyond the kind, which the model's tree does not carry; `own_digest_recovers_shell` is the clause a domain's `Conformance.encoderInjectivityLawsAt` extends to it. `Conformance.digestLaws` only samples the shipped engine's half (subtree digests agree iff `Diff.changes` over the two subtrees is empty, both directions).

- <a id="row-digest-partition"></a>**`digest-partition`** — `digest_partition` · fuaran-core#314

  `Tree.Digests.diff`'s four lists partition the union of the two id sets: modelled over the id lists of two trees — `d_added`, `d_removed`, `d_changed` and `d_unchanged`, each a `keep` over one tree's ids by membership in the other and by the own digests agreeing — every id of either tree is in one of the four lists, no other id is in any, no id is in two, and under `wf` each list carries an id once (`digest_partition`, the three clauses `digest_partition_covers`, `digest_partition_disjoint` and `digest_partition_once` as one statement). `digest_theorems_are_not_vacuous` evaluates the four lists on a pair where every list is non-empty, so the statement is not about the identity pair; the twins `digest-diff-partitions-the-ids` and `digest-diff-over-one-tree-is-all-unchanged` run the same pairs through the extracted oracle.

  _Boundary:_ The once-only clause needs `wf`. The lists' ascending key order is a rendering of the F# maps the model does not carry; `Conformance.digestLaws` samples it beside the partition, and it is not proved.

- <a id="row-diff-settled-order"></a>**`diff-settled-order`** — `diff_settles_order` · fuaran-core#305

  After `Diff.toOps`'s three structural blocks — the inserts, the moves, the removals — every parent of `after` holds its kept survivors in `before`'s order, then the inserted shells, then the moved-in survivors, the last two each in `after`'s order (`settled_order`, which step 4 of `Diff.fs`' private `passes` computes clause for clause from the two trees' child lists and the before-id set; `diff_settles_order`). This ORDER-PREDICTION LEMMA is why no `ReorderChildren` trails an append: a parent whose after-order IS the settled order holds it already, so pass 4 emits nothing for it, and `diff_applicable` / `diff_reconstructs` are proved over the pass with that skip inside them (the skip is `reorder_settled`, which reads `inv4`'s settled-order disjunct). Proved by three exact-order invariants carried BESIDE section 10's membership invariants through the same `_run` lemmas — `ord1`/`ord2`/`ord3`, each a `keep` over one of the two trees' child lists under a predicate naming the pass's worklist — with one order lemma per step over the per-node view and a short list algebra: `keep_split_at` (widening a filter by the id just placed APPENDS it, since every id after it in the child list is still on the worklist) and `kids_precede` (child order is preorder order), the latter being how pass 1's walk of `after`'s preorder reaches a parent's new children in child order.

  _Boundary:_ No hypothesis beyond two well-formed trees sharing a root id, like `diff-applicable`. Proved of the model; what it says about the SHIPPED engine is carried by `diff-differential` and `content-diff-differential`, whose script comparison is operation for operation and whose model is this one, re-extracted.

**Tested** (2)

- <a id="row-content-diff-differential"></a>**`content-diff-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#305

  The extracted `to_ops_contained_with` agrees with the shipped `Diff.toOpsWith` under a contained `OpScope`, verdict and script operation for operation, over the independent-pair diff generator's pairs under a drawn predicate, with the kind tag as the encoder — the content the witness shows and the content the model reads. On every accepted pair the theorem is instantiated on both sides: the extracted `apply_contained_all` lands the extracted script on the model of `after`, and `Ops.applyAll` under `OpScope.contained canHold` lands production's on `after`'s shape; the generator keys a kind off its id, so a third of `after`'s nodes are re-kinded (seeded) and the case fails if fewer than fifty pairs carried a rewrite. The go-red hands the model trees whose kinds are all erased, so it sees no content change and emits no rewrite; on every pair where production emitted one the comparison must lose.

  _Boundary:_ Agreement is over the pool drawn, never over all inputs; production's landing is checked on `after`'s shape, with content seen only through the kind-tag encoder.

- <a id="row-diff-differential"></a>**`diff-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#141

  The extracted diff model agrees with the shipped `Diff.toOps` under a plain and under a contained `OpScope` over pairs drawn INDEPENDENTLY from a shared id space — a widening over `Conformance.diffLaws`, which derives `after` by applying operations to `before` and so only ever diffs pairs the algebra could already reach. Six comparisons per pair: the verdict, the error class, the SCRIPT operation for operation, RECONSTRUCTION through production's own `Tree.encodeHash`, APPLICABILITY through `canApplyAll`, and the PROVED block shape evaluated over production's own script. Reconstruction and applicability are also PROVED (`diff-reconstructs` and `diff-applicable`) and stay among these six comparisons for the reason every differential here keeps a comparison a theorem already covers: the theorem is about the MODEL, and this is the shipped engine. Measured at 240 pairs, seed 1410: 179 scripts insert, 146 remove, 140 move, 168 reorder, 80 contained diffs produced a script and 160 were refused for containment, all 8 predicates drawn. THREE go-reds, each required to lose: the same four blocks reassembled with the REMOVES FIRST fails to reconstruct on 52 of the 240 pairs; the model's own plain diff substituted into the contained slot — an engine that emits an insert under a non-container because it never looked — disagrees with the shipped `Diff.toOps` under the contained scope; and a pool whose two trees draw a shared id's KIND independently fails to reconstruct on the shipped engine — 163 of the 240 pairs share a non-root id and all 163 lose, while none loses APPLICABILITY, which is the asymmetry the two theorems predict; run the other way, with the constraint restored, it is 0 of 163, so the probe distinguishes the cases rather than failing for any reason at all.

  _Boundary:_ Agreement is over the pool drawn, never over all inputs. The pool is constrained so that a node's content is a function of its id — a property of the problem rather than of the generator, because `Diff.toOps` is a shape diff that emits no `UpdateNode` (the operation alphabet has rewritten a node in place since fuaran-core#250; this diff does not), and a pair whose shared id carries different content in each tree is unreconstructible by any script it emits; measured, not assumed: a generator that redrew content reconstructed 1,738 of 4,000 pairs and the corrected one 20,000 of 20,000. That constraint is the proved theorem's `kinds_agree` hypothesis, so the pool and the statement quantify over the same pairs. The removes-first go-red loses on only some pairs, by design: the permutation is harmless where the removed regions hold no survivor, and the order buys correctness on the rest. Rejection PAYLOADS are not compared beyond the class and the offender a `TargetNotAContainer` names: `UnknownNode`'s `addressable` list is outside the comparison. Kit side, `Conformance.diffContainedLaws` certifies contained-diff scripts through the contained `applyAll` / `canApplyAll` under the witness's own predicate (`diffLaws` uses the plain pair, which fuaran-core#160 proved blind to containment); its refusal-exactness law's demanding direction was measured: 100 of 200 iterations mint a violating probe, all 100 are refused with the offender named by id and kind, and all 100 are accepted by the plain diff.

**Inherited through the models it opens:** [`independence-diamond`](#row-independence-diamond) (assumed), [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed), [`witness-surface-scope`](#row-witness-surface-scope) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `Limits`

[`Limits.fst`](Limits.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`.

> Limits — the WIRE_FORMAT §21 resource limits, as NAMED PREMISES and nothing else
> (fuaran-core Phase 149).

**What is modelled.** §21 makes eight bounds part of the FORMAT rather than a per-host deployment
choice: a document within them is one every conformant host MUST decode, and a document beyond
them is one every conformant host MUST refuse with the same typed error. Several models in this
directory need to say "within the format's limits" in a hypothesis, and before this module each
would have spelled its own number inline. One home, so that a changed limit moves ONE constant
and the claims ladder can say which theorem depends on which bound.

**Proved** (1)

- <a id="row-wire-resource-limits"></a>**`wire-resource-limits`** — `depth_bounds_do_not_alias` · fuaran-core#149

  WIRE_FORMAT section 21's eight resource limits, carried as named premises in one place so that a changed limit moves one constant rather than a paragraph of prose, with the two RELATIONS between them that the section's own argument uses: every bound admits something, and the node-depth bound sits below the syntactic-depth bound with room for the worst-shaped kind — which is what makes 'a host must never report a node-depth breach as a syntax-depth breach' a fact about the table rather than a hope about it. `canonical-form-roundtrip` takes exactly one of the eight.

  _With:_ `limits_positive`.

  _Boundary:_ The module models no enforcement of its own: section 21.2's host obligations are about code it does not describe, and what a consumer gets here is a number with a caption; the numbers stay premises here. Enforcement of the four bounds the parser can see (syntactic depth, string length, array length over items and members, document bytes) is modelled elsewhere, in `JsonParse.fst` section 13, over a record the HOST instantiates. Two theorems carry a bound, both `max_json_depth`: `read_render_roundtrip_within_limits` (`canonical-form-roundtrip`), whose proof does not use it (within the format's limits that round trip is unconditional, and outside them production's cap, not the model, decides), and `parse_render_roundtrip_within_limits` (`canonical-parse-roundtrip`), where it sizes the parser's budget.

### `Utf8`

[`Utf8.fst`](Utf8.fst) — checked only; its production side cannot be run beside an extraction (`check.ps1` says why).

> Utf8 — an F\* model of Fuaran.Core's UTF-8 ENCODER and its guarded form, with the encoding's
> injectivity on well-formed UTF-16 as a machine-checked theorem (fuaran-core Phase 306).

**What is modelled.** `Hash.utf8Bytes` (src/Fuaran.Core.Tree/Hash.fs), clause for clause and in its
own branch order: one byte below U+0080, two below U+0800, the four-byte arm for a high
surrogate FOLLOWED AT ONCE BY A LOW ONE, the three replacement bytes for any other surrogate,
and three bytes for the rest of the BMP. Beside it `Hash.firstIllFormedUnit` and
`Hash.tryUtf8Bytes`, the guarded form: the scan that names the first unpaired surrogate, and
the encoder behind it.

**Proved** (3)

- <a id="row-utf8-injective-on-well-formed"></a>**`utf8-injective-on-well-formed`** — `utf8_injective` · fuaran-core#306

  THE DIGEST'S LAST STEP. `Hash.utf8Bytes`, modelled clause for clause over integer code units, is INJECTIVE on well-formed UTF-16: two strings in which every high surrogate is followed at once by a low one and every low one follows a high one, with one byte string, are one string. Proved by exhibiting a left inverse (`unread`, with `unread_utf8`) rather than by case analysis on pairs. It is the step that carries the canonical form from characters to the bytes a hash is taken over; `canonical-digest-preimage-injective` is where it is used.

  _With:_ `unread_utf8`, `well_formed_app`, `no_surrogates_well_formed`.

  _Boundary:_ Only on well-formed UTF-16. Nothing is claimed of SHA-256: that two different byte strings have different digests is the hash's collision resistance, a premise wherever a digest is read as an identity. `unread` is a left inverse and nothing else: it validates nothing, answers garbage on bytes no encoder here produced, and is not a UTF-8 decoder. Units and bytes are unbounded integers: production's `>>> 6` and `&&& 0x3F` are written as `/ 64` and `% 64`, which is what they are on a non-negative value, and no machine word is modelled.

- <a id="row-utf8-guard-refuses-exactly-ill-formed"></a>**`utf8-guard-refuses-exactly-ill-formed`** — `try_utf8_refuses_exactly_ill_formed` · fuaran-core#306

  The guarded encoder `Hash.tryUtf8Bytes` IS the encoder wherever the string is well-formed (`try_utf8_is_utf8_on_well_formed`) and refuses exactly where it is not, naming a unit that is a surrogate and is at the index it gives (`try_utf8_refuses_exactly_ill_formed`); so two strings the guard accepted, with equal bytes, are equal (`guarded_utf8_injective`). The scan it refuses on is `Hash.firstIllFormedUnit`, modelled clause for clause.

  _With:_ `try_utf8_is_utf8_on_well_formed`, `guarded_utf8_injective`.

  _Boundary:_ Within the model; the model is not extracted, so that it is the shipped encoder is the premise `utf8-model-is-the-encoder`, read rather than run.

- <a id="row-utf8-replacement-not-injective"></a>**`utf8-replacement-not-injective`** — `replacement_is_not_injective` · fuaran-core#306

  THE REFUTATION the guard exists for. The UNGUARDED encoder maps a lone high surrogate, a lone low one and the replacement character itself to one byte string, `EF BF BD` (`replacement_is_not_injective`), and the units D801 D800 to the bytes of two replacement characters (`unpaired_pair_collides`). These are the platform encoder's own answers and the unguarded path is pinned to them.

  _With:_ `unpaired_pair_collides`.

  _Boundary:_ The unguarded encoder is not injective: "injective" is to be read as a claim about the guarded form and nothing else.

**Tested** (1)

- <a id="row-utf8-independent-oracle"></a>**`utf8-independent-oracle`** — `Proofs.Oracle`, 2 cases · fuaran-core#306

  The programme's first INDEPENDENT-oracle differential. `proofs/Utf8.fst` is checked and not extracted — its units are integers, which do not survive the extraction — so what runs beside `Hash.utf8Bytes` is not the model but the platform's own `System.Text.Encoding.UTF8`, an implementation that shares no line with it: over every one of the 65,536 code units alone, every high surrogate against the units that bound the low range and a non-surrogate each side of it, every low surrogate after a high one, an ordinary unit and nothing, and 20,000 seeded strings over the boundary units. On each string the unguarded encoder is held to the platform's replacing encoder byte for byte, and the guarded one to the platform's STRICT encoder, which throws on exactly the strings that have no code points — the refusal's index and unit included. Asserted reached: more than 90,000 strings, more than 10,000 of them ill-formed. The go-red is an encoder that reads an unchecked low half: it gives the units D801 D800 the bytes of U+10000, loses on every one of the 3,072 high-then-non-low pairs probed, and on no well-formed pair. The strings are BUILT from code units, because an F# string literal's unpaired `\u` surrogate is compiled as U+FFFD.

  _Boundary:_ Agreement over the sampled cases only, never over all inputs; it measures the code against the platform's encoder, not against the model.

**Rests on, or is bounded by:** [`utf8-model-is-the-encoder`](#row-utf8-model-is-the-encoder) (assumed).

### `WireCanon`

[`WireCanon.fst`](WireCanon.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `JsonParse`, `Limits`, `Utf8`.

> WireCanon — an F\* model of Fuaran.Core's CANONICAL ENCODER, with the canonical form's determinism
> and its converse as machine-checked theorems (fuaran-core Phase 149).

**What is modelled.** `Fuaran.Core.Canon` (src/Fuaran.Core.Wire/Canon.fs), clause for clause:
`Json.escape` (WIRE_FORMAT §2 rule 6), `FloatLayout.canonical` (rule 5, including the `-0`
collapse and the three non-finite tokens), and `Canonical.write Rendering.CrossHost` (rules 2, 3, 5, 6, 7 and the
omitted-key corollary of rule 4). Beside it, a READER for exactly the grammar `render` emits —
not `Json.parse`, which `JsonParse.fst` already models, but the left inverse whose existence IS the
canonical form's converse.

**Proved** (12)

- <a id="row-canonical-render-total"></a>**`canonical-render-total`** — `render_total` · fuaran-core#149

  `Canonical.write Rendering.CrossHost` reaches a rendering on every value — carried by its type, since F\* admits a definition only after showing it defined everywhere and terminating — and WHICH constructor produced a rendering is determined by its FIRST CHARACTER: a string opens with a quote, an array with a bracket, an object with a brace, a boolean with one of two letters, a number with a numeric character, and never with a closing or separating one. That is what makes the encoder's shape dispatch exhaustive rather than merely non-empty, the same distinction `decode-total` draws on the decode side, and it is the fact the reader's own dispatch turns on at every position.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Totality is within the model, which has no stack: that the stack is deep enough is a property of the code (the renderers are iterative), held by a test on a 1 MB thread, and is not claimed here.

- <a id="row-canonical-form-roundtrip"></a>**`canonical-form-roundtrip`** — `read_render_roundtrip` · fuaran-core#149

  A reader for exactly the grammar `Canonical.write Rendering.CrossHost` emits is a LEFT INVERSE of it on the canonical subset, in every trailing context: reading the rendering of a value followed by anything returns that value's normal form and the anything, unchanged. This is the engine of the canonical-form model — every other proved row of it is a corollary — and it is what makes the canonical form's converse a construction rather than an induction over byte lists.

  _With:_ `every_finite_float_reads_back`.

  _Boundary:_ Holds on the canonical subset only. The reader is deliberately NOT a second model of `Json.parse`: that parser is modelled in `JsonParse.fst`, and what injectivity needs is the existence of an inverse rather than a second account of production's own. Do not read the bridge rows beside it as making the reader one: `canon-parser-agreement` relates this reader to `JsonParse.fst`'s parser and remodels neither, so what is proved is the relation between the two, not that either is a model of the other. Nothing is claimed or tested about the reader on input `render` never emits. Off the canonical subset, `every_finite_float_reads_back` (fuaran-core#253) says every finite float `render` emits is read back as a value that renders as the same bytes, under a second numeral premise, `integral_float_reads_back`, which production did not satisfy until that phase (its parser refused an integer-shaped float layout past 2^53).

- <a id="row-canon-parser-agreement"></a>**`canon-parser-agreement`** — `canon_reader_agrees_with_parser` · fuaran-core#170

  THE TWO MODELS OF THE SAME LANGUAGE AGREE. On every byte sequence `Canonical.write Rendering.CrossHost` emits, in every trailing context the grammar produces, `WireCanon.fst`'s reader and `JsonParse.fst`'s parser both ACCEPT — neither refuses — and return the SAME value and the SAME remainder, up to the correspondence between the two models' types. It relates a reader proved to exist and invert the encoder to the parser model (`JsonParse.parse`; in production, `Json.read` at no bound but the cap), proved total with its refusals classified, so the round trip a consumer performs is proved rather than only measured by the differential over the corpus. It is a BRIDGE and not a model: nothing new is modelled, neither grammar is widened, and both readers are the ones their own models define. The two models share no type — `pc` is the alphabet correspondence and `pv` the value correspondence, which is not an identity in three places, each a modelling decision already taken (a numeral comes back as its TOKEN, a string as the `och` list `parseString` builds, an object's keys in that same form). It holds under BOTH null policies, which is the half that is easy to miss: the parser's `EraseMemberNull` fork (`parser-null-absorption`) can only fire on a member whose value begins with `n`, and `render_head_dispatches` proves no rendering does — `parse_render_is_policy_independent` is that corollary. `pc` carries NO `bridged` hypothesis, deliberately: `bridged` is the faithfulness condition for a HOST's mapping, and `pc` sends a `CPlain` carrying a reserved spelling to `COther` of it rather than to the constructor that spelling names, so the theorem holds of every value.

  _With:_ `budget_covers_depth`.

  _Boundary:_ NOT claimed: anything about the parser's behaviour on input `render` never emits — its thirteen refusal classes, its depth failure, its two numeric guards are `JsonParse.fst`'s subject (`error-kind-exhaustive`, `depth-bound-exact`, `int53-guard-exact`), and the bridge crosses the boundary in one direction only. ONE HYPOTHESIS: every object in the value carries distinct member names (`distinct_keys`, compared in the parser's own decoded form), because the parser refuses a repeated name where section 6's reader reads what `render` emits; the sort preserves it (`sort_preserves_distinct`), and a value with a repeated member is production's `DuplicateMember` refusal, which the canon differential holds separately. Nothing about `Canonical.write Rendering.Declared`, whose model (`render-ordered-injective`) is joined to the encoder's reader and not to the parser. The encoder's reader has no depth cap, so the parser's budget is stated by `fits` (one element for every container on the path to every leaf, `jdepth`-shaped because the descent walks the SORTED members and the sort preserves the predicate), which `budget_covers_depth` converts to `jdepth`, section 21's own quantity.

- <a id="row-canonical-parse-roundtrip"></a>**`canonical-parse-roundtrip`** — `parse_render_roundtrip` · fuaran-core#170

  THE ROUND TRIP AGAINST PRODUCTION'S OWN PARSER, at level 1 over the canonical subset — render here, parse there, and get the value's normal form back. `Json.parse` of a canonical rendering is `ROk` of that normal form, through the WHOLE entry point, so the theorem also says a canonical rendering leaves nothing over for the trailing-input check. `parse_render_roundtrip_within_limits` restates it with WIRE_FORMAT section 21.1's nesting bound carried, in the shape `read_render_roundtrip_within_limits` has, so a conformant document needs no hypothesis about the depth cap at all — production's `Json.defaultMaxDepth` is the budget it instantiates.

  _Boundary:_ Over the canonical subset only, and the theorem carries `distinct_keys v` beside `canonical w v`: a canonical rendering of an object with a repeated member is text the parser refuses by design. THE ONE PREMISE is `numerals_bridge`, recorded on `canon-numeral-layouts` beside `tok_read_ok`, and it is forced rather than chosen: both models declare the numerals opaque, so a bridge between two opaque parameters can only relate them. It is exhibited SATISFIABLE rather than assumed consistent (`numerals_bridge_is_satisfiable`, a wire with one integer layout and one float layout on which `parseNumber` is run in every trailing context the grammar produces) — a premise about two parameters at once is the shape that can be contradictory without looking it, and a contradictory premise makes every theorem under it true and worthless.

- <a id="row-canonical-form-injective"></a>**`canonical-form-injective`** — `render_injective_up_to_key_order` · fuaran-core#149

  EQUAL BYTES IMPLY EQUAL VALUES: two canonical values that render identically have the same normal form. This is the converse WIRE_FORMAT section 2 never stated and that every digest downstream silently rests on — the op-stream chain id, the DAG content id, the teleport digest and cross-host attestation all hash this rendering and read hash equality as value equality, and `op-codec-injective-certified` leaves a codec's own injectivity to the codec precisely because one built on `Canonical.write Rendering.CrossHost` inherits it only if `Canonical.write Rendering.CrossHost` has it. `render_injective_on_normal` is the literal form on values whose members are already in key order, which is the shape every value a reader returns has.

  _With:_ `read_returns_a_normal_value`, `read_str_inverts_escape`, `canonical_float_injective`, `int_layout_injective`, `absence_is_structural`.

  _Boundary:_ Only up to the object-member order rule 2 normalises away, and only for canonical values; the literal (unnormalised) form holds only on values whose members are already in key order. It is a statement about CHARACTERS: a digest is taken over the UTF-8 bytes, where two strings can share bytes when one holds an unpaired surrogate, and the bytes are `canonical-digest-preimage-injective`'s. The rule lemmas are the proof's parts: rule 6's `read_str_inverts_escape` is unique decodability in any trailing context, which plain injectivity of `escape` would not give; rule 4's `absence_is_structural` is that two objects rendering alike carry the same keys, so an omitted key never aliases a present one. Nothing here carries to `Canonical.write Rendering.Declared` (`render-ordered-injective`).

- <a id="row-canonical-form-deterministic"></a>**`canonical-form-deterministic`** — `render_deterministic` · fuaran-core#149

  EQUAL VALUES IMPLY EQUAL BYTES — section 2's own promise that the twelve rules make the encoding deterministic, proved rather than asserted. With `canonical-form-injective` it is an iff (`canonical_form_iff`), which is what the words 'canonical form' mean and is more than either half. The sorting half of it is where rule 2 is doing the work: the sort is idempotent and depends only on the members, so the author's key order carries no information into the bytes at all.

  _With:_ `sort_produces_sorted`, `sorted_is_its_own_sort`.

  _Boundary:_ For values equal up to the member order rule 2 normalises away; the sorting half holds under the comparator's total-order premises.

- <a id="row-canonical-render-not-injective"></a>**`canonical-render-not-injective`** — `render_aliases_integral_float` · fuaran-core#149

  The REFUTATION: `render a == render b ==> a == b` is FALSE of the shipped encoder four ways, each proved for EVERY instantiation rather than exhibited at a contrived one: a non-finite float renders as the quoted string of its token and therefore as that STRING (`render_aliases_nan` and its two siblings); a finite float whose layout carries no point and no exponent renders as the INTEGER of that token, which is the numeric normalisation `JVal`'s own type doc names; the two zeroes are one token, by rule 5's collapse; and two objects differing only in authored member order are one byte sequence, by rule 2's sort. The fourth is not a loss but the purpose of a canonical form. The first is the finding: unlike `Json.render`, which has the guarded `Json.tryRender` beside it, `Canonical.write Rendering.CrossHost` itself refuses nothing, so a digest over a non-finite float collides with the digest over the string a reader decodes it back to.

  _With:_ `render_aliases_nan`, `render_aliases_pos_inf`, `render_aliases_neg_inf`, `render_aliases_negative_zero`, `render_aliases_member_order`.

  _Boundary:_ This is the reason `canonical-form-injective` and `canonical-form-deterministic` are stated over a subset and up to member order rather than flatly. The refutation is of `Canonical.write Rendering.CrossHost`, whose bytes the guarded counterpart beside it does not move; the guard `Canonical.tryWrite Rendering.CrossHost` is `canonical-render-guard`.

- <a id="row-no-null-rendered"></a>**`no-null-rendered`** — `no_null_ever` · fuaran-core#153

  `Canonical.write Rendering.CrossHost` NEVER EMITS THE TOKEN `null` — not at the root, not at any depth, for ANY value, including those outside the canonical subset the canonical-form rows are stated over. This is section 2 rule 4's encoder half. The statement is LEXICAL on purpose: 'the rendering does not contain n-u-l-l' is false, since the string "null" renders those four characters inside a literal, so the model carries the three-state lexer every JSON reader has and proves something stronger than the absence of one token — outside a string literal the renderer emits ONLY structural punctuation, the characters of a numeral and the letters of `true` and `false`. There is no bare `n` anywhere in a rendering, hence no `null` at any position that lies outside a literal, and the grammar's own reader has no arm that accepts one (`reader_refuses_null`). Rule 6 is what carries the argument: an escaped body never closes its own literal.

  _Boundary:_ ONE premise, `layouts_numeric` — both of rule 5's layouts are numerals — weaker than `tok_read_ok` and recorded on `canon-numeral-layouts`. NOT claimed: `Canonical.write Rendering.Declared`, which is modelled (`render_ordered`, `render-ordered-injective`) but over which no theorem here is stated; JSON a host writes by any other route; or that the byte sequence never appears, which it correctly does inside strings.

- <a id="row-canonical-render-guard"></a>**`canonical-render-guard`** — `tryrender_refuses_exactly_aliasing` · fuaran-core#165

  THE GUARD BESIDE THE RENDERER. `Canonical.tryWrite Rendering.CrossHost` is `Canonical.write Rendering.CrossHost` with three refusals beside it, looked for in order: a non-finite float; then a string or member key that is not well-formed UTF-16 (`canonical-render-guard-strings`); then an object carrying a member name twice. The float refusal's predicate is the FIRST clause of `float_canonical` — finiteness — and nothing else. Proved on the model, for every instantiation: the float refusal fires IF AND ONLY IF some float is not finite, and what a refusal names is a float that is really at the path it gives, whose class is one of the three non-finite ones and whose own rendering is — by `render_aliases_nan` and its two siblings, restated at the class the refusal carries — byte for byte the rendering of a string it is not equal to (`tryrender_refuses_exactly_aliasing`); a non-finite float reachable by ANY path is refused, so no position in a document hides one (`nonfinite_anywhere_is_refused`); the three aliasing families the format DOCUMENTS — an integer-shaped finite float, the two zeroes, authored member order — are accepted and rendered to `render`'s own bytes (`tryrender_keeps_the_documented_normalisations`); and the guard IS the renderer (`tryrender_is_render_on_finite`), in particular on the whole canonical subset (`tryrender_is_render_on_canonical`). The repeated-name refusal names `first_repeated` (production's `Json.firstRepeatedMember`, the name whose second occurrence comes first) and exists because the parser refuses the text `render` would write (`RefusedRepeated`); `names_distinct` is the predicate it decides (`scan_decides_names_distinct`), and `tryrender_refuses_exactly_ill_formed` states all three arms. Path reachability is a RELATION rather than a by-key lookup because an object is an association list and a repeated key is representable.

  _Boundary:_ "The guard IS the renderer" holds only where every float is finite, every string well-formed and every object's names distinct — hypotheses `tryrender_is_render_on_finite` and `tryrender_is_render_on_canonical` carry. What is said of the float refusal holds because the float is looked for first. NOT proved, and said so in the model: MINIMALITY — that the path named is the first offender in document order — is modelled clause for clause and measured by `canon-guard-differential`, with no theorem that everything before it is finite; and DOCUMENT-level aliasing — that a refused document renders identically to the one with the named float replaced by its string — which is a congruence through the sort that a guard whose job is to refuse does not need. `Canonical.write Rendering.Declared` has a guarded companion, `Canonical.tryWrite Rendering.Declared`, which runs the same guard (`Json.firstRenderFault`); the Declared rendering is modelled separately (`render-ordered-injective`), and no theorem here is stated over its guard. `Canonical.write Rendering.CrossHost`'s bytes are not moved by the guard, so `canonical-render-not-injective` stands exactly as written. A float refusal names the float by its token and its path: `$` for the root, `[i]` for an array item, `["key"]` for an object member with the key under rule 6's escape, so the path is unambiguous for any key. The scan runs before any sort, so document order is AUTHORED order. There is no guarded DIGEST companion: `Fuaran.Core.Wire` references nothing that hashes and every digest takes a string a caller has already rendered, so the guarded digest is `Canonical.tryWrite` mapped into the caller's own hash.

- <a id="row-canonical-digest-preimage-injective"></a>**`canonical-digest-preimage-injective`** — `canonical_digest_preimage_injective` · fuaran-core#306

  CANONICAL INJECTIVITY, AT THE BYTES A DIGEST IS TAKEN OVER. For two canonical values whose strings and member keys are all well-formed UTF-16, equal UTF-8 bytes of the two canonical renderings imply equal normal forms (`canonical_digest_preimage_injective`); and the same sentence about two values `Canonical.tryWrite Rendering.CrossHost` accepted (`tryrender_digest_injective`), since the guard refuses exactly the strings this excludes. The rendering of such a value is itself well-formed (`render_good`: a concatenation of structural characters, numerals, the two literals and escaped strings, where rule 6 rewrites only characters that are not surrogates — `escape_good`); `Utf8.utf8_injective` then gives equal units, the unit spelling's injectivity equal characters, and `canonical-form-injective` the rest. `spelling_ok_is_satisfiable` exhibits a spelling that holds, so the theorem is not vacuous.

  _With:_ `tryrender_digest_injective`, `render_good`, `escape_good`, `spelling_ok_is_satisfiable`.

  _Boundary:_ Only for values whose strings and member keys are well-formed UTF-16. The spelling — which UTF-16 unit a character is — is a parameter with its premises named (`spelling_ok`: every character a unit, injective on the bridged characters, no surrogate among the characters the encoder itself emits). NOT proved: SHA-256's collision resistance; and nothing about `Json.render`. The spelling's injectivity is asked on the bridged characters only: asked of every `CPlain` it would be unsatisfiable, there being more strings than code units. `Json.render` is not injective and nothing is claimed of it: it is author-ordered and keeps a negative zero's sign.

- <a id="row-canonical-render-guard-strings"></a>**`canonical-render-guard-strings`** — `tryrender_refuses_exactly_ill_formed` · fuaran-core#306

  THE GUARD'S SECOND REFUSAL. `Canonical.tryWrite Rendering.CrossHost` also refuses a value holding a string or a member key that is not well-formed UTF-16. Proved on the model, for every instantiation: the guard RENDERS exactly the values with no non-finite float and no ill-formed string; a string refusal is made only where every float is finite — the two refusals are ordered, the float looked for first, so every refusal `canonical-render-guard` describes is still the refusal made — and it names a string the well-formedness test refuses (`tryrender_refuses_exactly_ill_formed`, over `scan_decides_strings_ok`). The scan is `Json.firstIllFormedString`, clause for clause: document order, a member's KEY before its value, at the member's own path.

  _Boundary:_ The well-formedness test itself is a field of the model's wire (`str_ok`), because the model's alphabet does not distinguish a surrogate from any other ordinary character; `canon-unit-spelling` is that bridge. NOT proved: minimality of the path named, as for the float scan.

- <a id="row-render-ordered-injective"></a>**`render-ordered-injective`** — `render_ordered_injective` · fuaran-core#306

  `Canonical.write Rendering.Declared` — the declared-key-order renderer, `render` minus rule 2 — is INJECTIVE OUTRIGHT on the canonical subset: equal bytes are equal VALUES, member order included, with no normal form on either side (`render_ordered_injective`). It is the literal statement `canonical-render-not-injective` refutes of `Canonical.write Rendering.CrossHost`, and it is true here because with no sort there is nothing to normalise away: section 6's reader returns members in the order the bytes carry them, which is the order they were authored in, so it is a left inverse on the nose (`read_render_ordered_roundtrip`). `render_is_render_ordered_of_the_normal_form` ties the two renderers together — `render v` is `render_ordered` of `v`'s normal form, so rule 2 is the whole difference.

  _With:_ `read_render_ordered_roundtrip`, `render_is_render_ordered_of_the_normal_form`.

  _Boundary:_ Only on the canonical subset: the three numeric normalisations (`canonical`) are excluded here as there. NOT claimed: that an encoder constructs its members in one order — that is the vocabulary's declaration and the generated encoder's to keep.

**Tested** (4)

- <a id="row-canon-guard-differential"></a>**`canon-guard-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#165

  The extracted guard agrees with the shipped `Canonical.tryWrite Rendering.CrossHost` as the `Result` a caller receives — the rendering byte for byte on an accept, the MESSAGE (token and path) on a refusal — over both corpus families and over a generated pool in which roughly one numeric leaf in three has been replaced by one of the three non-finite floats, so refusals land at every depth the pool reaches. Three comparisons per document, each able to lose alone: the two results agree; an accepted value is exactly `Canonical.write Rendering.CrossHost`'s bytes; and the verdict is a refusal precisely when a THIRD, independent predicate finds a non-finite float in the value. Asserted REACHED: refusals, accepts, refusals two or more steps deep, each of the three tokens, and accepted documents carrying a float outside the canonical subset — the set the guard must not refuse. On the corpus the guard refuses nothing, which is the expected answer measured rather than assumed: JSON cannot spell a non-finite float. A first case pins production directly: the three non-finite witnesses are refused by token and path, the strings they alias and the three documented normalisations are not, the first offender in AUTHORED order is the one named though another sorts first, and a key carrying a quote and a control character is escaped as rule 6 escapes it. The go-red is a model wire that cannot see NaN: it must disagree on exactly the documents whose first non-finite float is a NaN and on no other, a refusal for an infinity included.

  _Boundary:_ The model carries a refusal as data and the host renders it in production's spelling, so the message PREFIX is the bridge's and is not independently modelled. Agreement is over the corpus and the pool drawn, never over all inputs.

- <a id="row-canon-differential"></a>**`canon-differential`** — `Proofs.Oracle`, 6 cases · fuaran-core#149

  The extracted canonical encoder agrees with the shipped `Canonical.write Rendering.CrossHost` BYTE FOR BYTE over every corpus fixture in both families and over a generated pool, and its reader's round trip agrees with production's own parser over the same documents on the canonical subset. The agreement between the two readers is itself PROVED on the models (`canon-parser-agreement`, `canonical-parse-roundtrip`); what this comparison measures is what a differential is for and a proof is not: that the EXTRACTED encoder and production's own parser, in one process over the same bytes, do what the models say. The bytes are the only comparison that means anything for an encoder whose whole job is to produce a digest input. The pool is where the corpus is thin: rule 6's three escape classes, rule 2's comparator above the BMP — an astral key beside a private-use one, the pair the rule's own note says UTF-16 order decides differently from code-point order — and rule 5's scientific layout, each asserted REACHED rather than hoped for. The go-red is rule 2's comparator REVERSED: every object carrying two distinct keys must then disagree, and a document carrying none must still agree, so the instrument is known to be one that can lose and to be narrow to the rule it is about. A sixth case pins the four aliasing renderings on production itself, so the refutation goes red if the encoder ever changes.

  _Boundary:_ Agreement is over the corpus and the pool drawn, never over all inputs; the reader round trip is compared on the canonical subset only. The pool also reaches the encoder's non-canonical arms (both infinities, NaN, both zeroes, the Int32 boundaries, an empty string, object and array), so the extracted model is compared as a model of the whole encoder and not only of the canonical subset. The extracted model is a character-list interpreter with no tail calls, so the differential runs on a thread with an enlarged stack rather than shrinking the pool; an overflow there is the extraction's, not the model's.

- <a id="row-canon-guard-strings-differential"></a>**`canon-guard-strings-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#306

  The extracted guard agrees with the shipped `Canonical.tryWrite Rendering.CrossHost` as the `Result` a caller receives over a generated pool in which roughly one string in four and one member key in six has been replaced by a string of surrogates — a lone high, a lone low after text, a high followed by a high, a pair with a stray low after it, and a well-formed pair that must NOT be refused — and one document in three also carries non-finite floats, so the ORDER of the two refusals is measured as well as each alone. The model is handed the platform's strict encoder as its well-formedness test, not production's scan, and names the string; the host finds the unpaired unit in it with a scan of its own and spells the message, so neither side is asked to agree with itself. Per document: the two results agree, message and path included; an accept is `Canonical.write Rendering.CrossHost`'s bytes; the verdict is a refusal precisely when an independent predicate finds a non-finite float or an ill-formed string; and a value holding a non-finite float is never refused for a string. Asserted reached: string refusals, refusals naming a member key, refusals two or more steps deep, documents holding both faults, and accepts. The go-red is a model whose test accepts every string: it must disagree on exactly the documents production refuses FOR A STRING and on no other.

  _Boundary:_ Agreement over the sampled cases only, never over all inputs.

- <a id="row-render-ordered-differential"></a>**`render-ordered-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#306

  The extracted `render_ordered` agrees with the shipped `Canonical.write Rendering.Declared` byte for byte over a generated pool, and — on the canonical subset — the model's reader returns exactly the value that was rendered, members in authored order, which is the theorem's engine measured. Asserted reached: documents authored out of key order, where this renderer is NOT `Canonical.write Rendering.CrossHost` and without which the case would be the sorting renderer's differential again; and canonical documents read back exactly. The go-red is the model's own sorting `render` standing in for the ordered one: it must lose on exactly the documents where production's two renderers differ, and on no other.

  _Boundary:_ Agreement over the sampled cases only, never over all inputs; the read-back is measured on the canonical subset only.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`canon-numeral-layouts`](#row-canon-numeral-layouts) (assumed), [`canon-unit-spelling`](#row-canon-unit-spelling) (assumed).

**Inherited through the models it opens:** [`parser-float-readback-opaque`](#row-parser-float-readback-opaque) (assumed), [`parser-alphabet-bridge`](#row-parser-alphabet-bridge) (assumed), [`utf8-model-is-the-encoder`](#row-utf8-model-is-the-encoder) (assumed).

### `WireVersioning`

[`WireVersioning.fst`](WireVersioning.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `JsonParse`, `Limits`, `Utf8`, `WireCanon`.

> Versioning — WIRE_FORMAT §15.4's evolution-policy table, as a theorem about the IDL diff
> (fuaran-core Phase 151).

**What is modelled.** §15.4 classifies a vocabulary change by an IDL diff — no removed tags means
additive, any removal or rename means breaking — and §15.3 promises that a `Behind` consumer
PRESERVES what it does not understand. Since Phase 127 that classification is COMPUTED, by
`Versioning.classify` / `Versioning.bump` in `src/Fuaran.Core.Wire/Versioning.fs`, and it is exercised
by tests that perturb the real vocabulary. What no test can say is that the classification is
SOUND: that an "additive" verdict really does leave every old document decoding to the same
value, and that preservation really does reproduce the producer's bytes. A test samples; a
quantifier does not. This module carries the four statements that close that gap:

- `classify_sound` — `classify` answers `Additive` ONLY when the before-vocabulary is a
SUBSET of the after-vocabulary. This is the table's own rule stated as a fact about the
function rather than about the prose, and it is what the other three stand on.
- `additive_monotone` — given that verdict, every document well-formed under the OLD
vocabulary decodes under the NEW one to exactly what it decoded to before. "Additive means
old documents still work", with `still work` meaning `==` and not `approximately`.
- `preserve_exact` — a document authored under the NEW vocabulary, carrying a tag the old
consumer does not know, survives decode → re-encode → render with the PRODUCER'S BYTES
intact. This is §15.3's must-ignore-but-preserve, and it is the reason Phase 149's
canonical-form work is imported rather than restated: the claim is about bytes, and the
renderer that produces them is already modelled and already proved injective on the
canonical subset.
- `unknown_transport_only` — no value an ENCODER produces decodes to `Unknown`, for a
consumer that knows the vocabulary the encoder authors against. §15.3's "transport-only,
un-constructible on encode" is a comment in the shipped source; here it is a theorem.

**Proved** (6)

- <a id="row-evolution-classify-sound"></a>**`evolution-classify-sound`** — `classify_sound` · fuaran-core#151

  WIRE_FORMAT section 15.4's classifier is SOUND, and the three theorems beside it stand on that. `proofs/WireVersioning.fst` models `Versioning.classify` / `bump` / `negotiate` clause for clause and proves `classify_sound`: `classify` answers `Additive` ONLY when every tag of the before-vocabulary survives into the after-vocabulary. The function computes a LIST — a difference — where the table talks about a SUBSET, and the two are the same statement only through an induction, which is the module's whole substance; `classify_additive_is_sub` packages the quantified form. `rename_is_breaking` is the row section 15.4 calls out by name, because a rename LOOKS additive from the new vocabulary's side: a tag present before and absent after forces `Breaking` whatever else the change added, and nothing about the addition enters the proof. `breaking_bump_is_foreign` and `additive_bump_is_behind` carry the verdict through to the consequence a reader acts on — a breaking verdict mints a new major and the old consumer is `Foreign` and refuses; an additive verdict keeps the major and that consumer is `Behind` and tolerates. Section 15.4's FIELD rows are proved beside them, at the composition that decides them: `classify_field_add` models `Diff.classifyFieldAdd` clause for clause, `evolution_of` models `Diff.evolution`'s partition of each row's subject by severity, and `optional_field_addition_bumps_the_minor` carries the result through `bump` and `negotiate` — an added optional field introduces a subject, the verdict is a NON-EMPTY `Additive`, the minor moves and an old consumer is `Behind`. `required_field_addition_is_foreign_not_behind` states the required row: a required field added retires its subject, moves the major and leaves an old consumer `Foreign` — a minor would rest on the premise that every old document still decodes, which `field_additive_monotone` refutes — and `host_only_field_addition_moves_no_profile` states the one that genuinely moves nothing, which is what makes the other two a measurement rather than a constant. `tag_delta_is_blind_to_field_additions` keeps the other half: the kind-tag delta alone cannot decide any of them. Proved with no assume and no admit under --report_assumes error.

  _With:_ `classify_additive_is_sub`, `rename_is_breaking`, `breaking_bump_is_foreign`, `additive_bump_is_behind`, `optional_field_addition_bumps_the_minor`, `required_field_addition_is_foreign_not_behind`, `host_only_field_addition_moves_no_profile`, `tag_delta_is_blind_to_field_additions`, `classify_ignoring_removals_is_unsound`, `ignoring_optionality_moves_a_profile_that_must_not`.

  _Boundary:_ WHAT IS NOT CLAIMED: that the table is COMPLETE. A row the table may GROW is a row this module does not have. Within the model. Not claimed: the classification of a change that is not a field addition. `Diff.classify` decides some thirty change kinds; the model carries the severity TYPE in full, so a reader cannot conclude that every admitted row bumps, but models only `classifyFieldAdd`'s three clauses: `Diff.evolution`'s partition is modelled over an arbitrary row list, and what supplies the severities for the other kinds is not. Nor that a consumer ACTS on the negotiation: `negotiate` returns a verdict, and whether a host refuses a `Foreign` artifact is host code this module does not describe. The two go-red classifiers are refuted in the model: `classify_ignoring_removals_is_unsound`, and `ignoring_optionality_moves_a_profile_that_must_not`, which shows a field classifier that ignores optionality publishing a minor for a host-only field (a different published PROFILE, not merely a different severity).

- <a id="row-evolution-additive-monotone"></a>**`evolution-additive-monotone`** — `additive_monotone` · fuaran-core#151

  An ADDITIVE step is unobservable to a document that predates it. `additive_monotone`: given that `classify` returned `Additive`, a document whose tag the OLD vocabulary already carried decodes under the NEW vocabulary to exactly what it decoded to before — the same `Known`, the same preserved payload, the same error if the domain decoder errors. Not 'still works' but `==`. This is what section 15.4's additive row promises and what no test can establish, the claim being universally quantified over documents nobody wrote. It is quantified over every discriminator reader, every known-decoder and every required-profile reader, so it is a fact about the tolerance SEAM rather than about one domain's codec, usable by a domain this repository does not contain. Proved with no assume and no admit under --report_assumes error.

  _Boundary:_ Holds only under the hypothesis that `classify` returned `Additive`, and only for a document whose tag the OLD vocabulary already carried. It rests on `classify_sound` and on nothing else. Within the model.

- <a id="row-evolution-preserve-exact"></a>**`evolution-preserve-exact`** — `preserve_exact` · fuaran-core#151

  Section 15.3's must-ignore-but-preserve, AT THE BYTES. `preserve_exact`: a producer authors a value under the new vocabulary and renders it; an old consumer parses those bytes, meets a tag it does not know, tolerantly decodes through `decodeTolerant`, re-encodes through `reencode` and renders — and what comes out is what went in, byte for byte. The byte level is the one that matters, because preservation is what makes an unknown artifact survive a hash chain, and a claim at the value level would not say that. The module opens `WireCanon` because the renderer is already modelled and already proved in `WireCanon.fst`, and a second renderer here would be one to keep in step with the first. Two of that module's lemmas discharge it — `read_render` and `render_ignores_key_order` — and the proof needs nothing about the payload beyond its identity, which is the shape of a claim that holds by construction rather than by care. Proved with no assume and no admit under --report_assumes error.

  _Boundary:_ It carries `WireCanon.fst`'s two premises (`tok_read_ok`, `key_order_ok`) and rule 5's canonical-subset predicate, and adds none of its own. Within the model. The nested case is not claimed: an unknown kind inside a known tree is preserved by the same clause, and production has a corpus case for it, but the model's `decode_tolerant` is applied to ONE artifact object; recursion through a domain tree is the domain codec's, which is a parameter here.

- <a id="row-evolution-unknown-transport-only"></a>**`evolution-unknown-transport-only`** — `unknown_transport_only` · fuaran-core#151

  The transport-only `Unknown` is un-constructible from an encoder — section 15.3's by-construction claim, which the shipped source states only as a COMMENT ('it is reachable here and nowhere on the authoring/encode path') and which this row states as a theorem. `unknown_transport_only`: take any value the encoder produces, decode it tolerantly with the vocabulary the encoder authors against, and the result is never an `Unknown`. `reencode_known_is_encode` closes the other direction: `reencode` is the only function consuming a `decoded`, and on a `Known` it is exactly the encoder, so no round trip through the seam can introduce one either. Proved with no assume and no admit under --report_assumes error.

  _With:_ `reencode_known_is_encode`.

  _Boundary:_ The one hypothesis is what 'the vocabulary the encoder authors against' MEANS — the encoder's own tag is in the consumer's vocabulary; where it fails the two are at different profiles, which is `Behind` and is `evolution-preserve-exact`'s case rather than this one. Within the model.

- <a id="row-evolution-bump-refuses-at-the-edge"></a>**`evolution-bump-refuses-at-the-edge`** — `try_bump_refuses_exactly_at_the_edge` · fuaran-core#306

  THE COUNTER HAS A LARGEST VALUE. A profile counter is an Int32 and the model carries the limit (`max_counter`): `try_bump` — `Versioning.tryBump` — refuses EXACTLY at the edge, an additive step with something added and the minor at the limit or a breaking step with the major at the limit (`try_bump_refuses_exactly_at_the_edge`); neither form leaves the range (`bump_stays_in_range`); and `saturated_breaking_bump_is_not_foreign` exhibits what saturation costs — at the limit a breaking change mints no new major and a consumer on the old one negotiates `Current`, which is why the refusing form exists.

  _With:_ `bump_stays_in_range`, `saturated_breaking_bump_is_not_foreign`.

  _Boundary:_ A model holding the counter as an unbounded natural, where `+ 1` is total, would make bump theorems true of the model and false of the code at `Int32.MaxValue`; hence the five theorems that need the bump to have happened (`breaking_bump_is_foreign`, `additive_bump_is_behind` and the three field-add rows) carry that as a hypothesis rather than leaving it in the type, and at the limit a saturated breaking bump is not foreign.

- <a id="row-evolution-field-additive-monotone"></a>**`evolution-field-additive-monotone`** — `field_additive_monotone` · fuaran-core#304

  The field-add rows hold as a claim about DECODING. Section 8 of `proofs/WireVersioning.fst` makes the known-decoder a FUNCTION OF THE VOCABULARY (`decode_known_in`, the presence rule of `Idl.Decode.decodeFields` clause for clause: a host-only field is never read, an absent member of a required field is refused, every other member is read when present) and hands it to the tolerant seam, where `evolution-additive-monotone` fixes one known-decoder for both revisions and so can see a kind arrive and never a field. `field_additive_monotone`: for every field-add row `Diff.classifyFieldAdd` keeps OFF the major (`Additive`, `BreakingForEmitters`, `HostSurfaceOnly` — each promises old documents untouched), every document that does not carry the added member decodes under the new vocabulary exactly as under the old, through `decodeTolerant`: the same known members, the same preserved unknown, the same error. A required field added is classified `BreakingWire`, and `required_field_addition_is_foreign_not_behind` restates the section-7 row over that rule: the major moves, the minor resets, an old consumer is `Foreign`. Proved with no assume and no admit.

  _Boundary:_ The one hypothesis names the documents the contract is about: production tolerates an undeclared member, so a document already carrying a stray member of the added name was not written under the old contract and is outside the theorem. The theorem is false of a rule answering `BreakingForEmitters` (a minor) for a required field added, while every old document is refused: `pre_304_rule_breaks_field_additive_monotone` exhibits that counterexample in the model, and a prover run with that rule restored fails `field_additive_monotone` at `c <> required_chars`.

**Tested** (4)

- <a id="row-evolution-differential"></a>**`evolution-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#151

  The extracted evolution-policy model agrees with the shipped `Versioning` over both inputs the policy has. Over the corpus's `envelope/` family, four things per fixture: the negotiation and the authored profile it carries, whether the tolerant decode found a known kind or preserved an unknown one, the `requiredProfile` an unknown carries, and the BYTES out of `reencode` + `Canonical.write Rendering.CrossHost`. Over pairs of `idl.json` revisions — the pinned artifact PERTURBED four ways and re-read through `Diff.parse`, which is the path a migration author walks rather than two tag sets somebody chose — the whole verdict, both lists and not merely the discriminator, since a classifier naming the right class and the wrong tags leaves an author with the wrong shim. Each of the four verdicts is asserted to land where section 15.4 puts it, so a pass in which every pair classified alike FAILS rather than passing. The adequacy assertions on the envelope half require all three negotiation outcomes reached and at least two fixtures carrying a tag the consumer does not know: a family producing only `Current` would exercise neither tolerance nor preservation and would be green having tested nothing section 15.3 is about. The go-red is the model's OWN `classify_ignoring_removals`, which F\* refutes in the same file — a classifier computing the additions and never looking for removals, which is what an author writes who reads the additive row and stops, and which agrees with production on every purely additive change; the case asserts it disagrees on the removal and the rename and on NOTHING ELSE. A THIRD input is run: the same pinned artifact perturbed by adding ONE field at each of the three optionality classes `Diff.classifyFieldAdd` branches on, read back through `Diff.ofArtifacts`, with two things compared per perturbation — the row's SEVERITY, clause for clause, and the PROFILE the revision mints, computed end to end on each side (`Diff.bumpProfile` beside the model's own `classify_field_add` → `evolution_of` → `bump`). The adequacy assertions are what make it a measurement: the optional row must move the MINOR and not the major, the required row must move the MAJOR, and the host-only row must move NO profile — so a run in which all three minted the same profile fails rather than passing. Its go-red is the model's own `classify_field_add_ignoring_optionality`, which answers additive whatever the class says: it AGREES on the optional row, which is why the case asserts the disagreement on the host-only one.

  _Boundary:_ Agreement is over the corpus's `envelope/` family, the four perturbed pairs and the three field-add classes, never over all inputs. `Versioning.parse` / `decode` and `Capability.tryParse` are not modelled: the differential drives production's own envelope parser and compares what happens after it, so a malformed `$profile` is outside this row. On the field-add input the row's subject string is production's own, recovered through the public `Diff.evolution` over the single row, so the partition being compared is never an input to the comparison.

- <a id="row-evolution-table-coverage"></a>**`evolution-table-coverage`** — `Proofs.Oracle`, 2 cases · fuaran-core#200

  Section 15.4's table is the four rows the model covers, and ALL FOUR are inhabited. Add a kind, remove a tag and rename are decided by the kind-tag delta and are proved (`fuaran-core#151`). The optional-field row has content because `Versioning.classify` does not take kind tags — it takes two SUBJECT SETS — and the shipped caller that builds them from an IDL diff is `Diff.evolution`, which partitions each row by the severity `Diff.classifyFieldAdd` reads off the field's optionality class; `Diff.bumpProfile` carries the verdict through `Versioning.bump` to a published profile. Nothing is widened and no function is merged: `classify` is modelled at its own signature over its own caller's input. The content is measured rather than asserted — the differential perturbs the pinned artifact once per optionality class and requires the three to mint DIFFERENT profiles: an optional field moves the minor, a required field moves the MAJOR (every old document lacks the member and is refused, so the row retires its subject; `field_additive_monotone` refutes the premise that would make it a minor), and a host-only field (WIRE_FORMAT section 9, on no document in either direction) moves none. A pass in which all three agreed would be measuring the arrival of a field rather than its class.

  _Boundary:_ What is NOT claimed is completeness: a row the table may GROW is a row this module does not have. The field rows' inhabitation is measured over the pinned artifact's perturbations only, never over all inputs. An added optional field is invisible to the kind-tag delta for the same reason it is harmless: rule 2's unknown-key tolerance, under which a consumer meeting a member it does not recognise ignores and preserves it, so the document decodes the same either way. The tag delta's silence is not wrong, only not the whole policy; read alone, either half gives the wrong answer, in opposite directions.

- <a id="row-evolution-bump-differential"></a>**`evolution-bump-differential`** — `Proofs.Oracle`, 1 case · fuaran-core#306

  The extracted `try_bump` and `bump` agree with the shipped `Versioning.tryBump` and `Versioning.bump` over every profile whose two counters are each 0, 1, `Int32.MaxValue - 1` or `Int32.MaxValue`, under a no-op additive, an additive and a breaking evolution: the same profile where both bump, a refusal on both where either refuses, and the same saturated profile from `bump`. Asserted: exactly eight refusals — the additive step at each of the four majors where the minor is at the limit, the breaking one at each of the four minors where the major is — and forty bumps, so a run in which nothing reached the edge fails rather than passing; and every profile `bump` returns has non-negative counters, which is the property a wrapped counter breaks.

  _Boundary:_ Agreement over the sampled profiles and the three named evolutions only, never over all profiles or evolutions.

- <a id="row-evolution-old-document-differential"></a>**`evolution-old-document-differential`** — `Suite:IdlStabilityClassTests.evolutionDifferential`, 3 cases · fuaran-core#304

  The classifier's class predicts what an OLD document does, on the hosts that decode it. A corpus of documents written under an old vocabulary (sampled, plus three planted at the edges the perturbations turn on: an absent optional member, a member on an omit-at-default value, a fractional float) is decoded under seven perturbed vocabularies — an optional field added, a required field added, optional tightened to required, required loosened to optional, required loosened to omit-at-default, int widened to float, float narrowed to int — by the interpreter and by the generated TypeScript decoder under node. A row that is not `breaking-wire` must keep every old document decoding and re-encoding to its own bytes on both hosts; a `breaking-wire` row must refuse or move at least one; and the two hosts must agree document by document. The go-red is a rule claiming that old documents still decode when a required field is added: decoded, every one is refused. The narrowing case pins the generated TypeScript `dInt` to the interpreter's 32-bit int range at every int slot of the certification vocabularies.

  _Boundary:_ Agreement over the sampled and planted documents, the seven perturbations and the two hosts only, never over all documents or all vocabulary changes.

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`parser-float-readback-opaque`](#row-parser-float-readback-opaque) (assumed), [`parser-alphabet-bridge`](#row-parser-alphabet-bridge) (assumed), [`canon-numeral-layouts`](#row-canon-numeral-layouts) (assumed), [`utf8-model-is-the-encoder`](#row-utf8-model-is-the-encoder) (assumed), [`canon-unit-spelling`](#row-canon-unit-spelling) (assumed).

### `Unit`

[`Unit.fst`](Unit.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`.

> Unit — an F\* model of the UNIT ALGEBRA (fuaran-core Phase 428): `src/Fuaran.Core.Unit/Unit.fs`
> clause for clause, with the laws its tests sample stated as theorems.

**What is modelled.** The whole of `Unit.fs`: the canonical product of prefixed atoms with non-zero
integer exponents that `UnitOfMeasure` holds (`uom`), the admitted vocabulary (`UnitVocabulary`:
the atom table with each atom's scale and dimension, the alias, the twenty prefixes, the non-ratio
and arbitrary refusal sets, the currency shape), the factor merge `tryCombine` behind `Unit.mul` /
`div` / `pow` with its exponent range, `Unit.compatible` through `dimension`, `Unit.conversionFactor`
through `scale` and the exact `Ratio` in lowest terms, `Unit.render` (`UnitOfMeasure.ToString`),
and `Unit.parse` — the iterative reader over its stack of open terms, every refusal class with the
token and position it names (`UnitRefusal`, with `Malformed`'s prose `expected` as an enumeration).

**Proved** (4)

- <a id="row-unit-exponent-group"></a>**`unit-exponent-group`** — `combine_comm` · fuaran-core#428

  THE EXPONENT VECTORS FORM AN ABELIAN GROUP. `proofs/Unit.fst` models `src/Fuaran.Core.Unit/Unit.fs` clause for clause: the canonical product of prefixed atoms with non-zero integer exponents (`UnitOfMeasure`), the merge `tryCombine` performs as a strictly sorted insert that sums on an equal key (`combine`), and `Unit.mul` / `div` / `pow` over it with production's `int32` bound checked where production checks it. Over well-formed factor lists the merge is commutative (`combine_comm`), associative (`combine_assoc`), has the empty product as its identity (`combine_unit`) and every element its inverse (`combine_inverse`), and `div` is multiplication by the inverse (`div_is_mul_inverse`) — each through the exponent law `combine_exp` (the merge adds exponent vectors) and `ext` (two well-formed lists with one exponent vector are one list), and `mul_wf` / `div_wf` / `pow_wf` say the algebra stays inside the well-formed lists. Measured red with the merge's sorted insert dropping the sum on an equal key.

  _With:_ `combine_assoc`, `combine_unit`, `combine_inverse`, `div_is_mul_inverse`, `combine_exp`, `ext`, `mul_wf`, `div_wf`, `pow_wf`.

  _Boundary:_ The group laws are over well-formed factor lists only. Within the model; the correspondence to production is the differential's (`unit-differential`) and the extractor's (premise `extractor-and-compiler-trusted`). The unit model is self-contained: it opens no other model and takes no host record.

- <a id="row-unit-one-text"></a>**`unit-one-text`** — `parse_render` · fuaran-core#428

  ONE CANONICAL TEXT PER UNIT, AND IT READS BACK: for every canonical unit `parse (render u) == Ok u` (`parse_render`). `render` is a function, so equal units have one text; the theorem is the half that is not free: the text reads back to the unit that wrote it, so two different canonical units have two texts. Proved along the text left to right: the symbol loop reads exactly the token (`read_sym_app`), the exponent loop reads exactly the digits written (`read_exp_digits`, with the bound never crossed on the way: `read_digits_app`), one component is one fold into the open term (`comp_token`), the separator arm threads the rest (`loop`), and the folded product is the unit's own list because every atom is written once with its exponent (`ext` again). Measured red with `render` writing the denominators before the numerators, and with the exponent writer dropping the magnitude.

  _With:_ `read_sym_app`, `read_digits_app`, `read_exp_digits`, `resolve_pos`, `comp_token`, `loop`, `render_no_other`.

  _Boundary:_ Only for canonical units — well-formed, every exponent within production's bound, every atom admitted (its prefix-and-symbol token lexes as one symbol token and `resolve`s to the atom itself). The identity is STRUCTURAL (`fuaran-core#426`): `km/h` and `m/s` are different units with two texts, compatible with factor 5/18. Within the model; the correspondence to production is the differential's (`unit-differential`) and the extractor's (premise `extractor-and-compiler-trusted`). The vocabulary tables are generated from the source row for row and are opaque to the solver: no theorem reads a row (the twins do, by normalisation). Not claimed: that every atom of the vocabulary under every prefix it takes is admitted; the theorems are stated over the `admitted` predicate, and `unit-differential` draws every atom prefixed and bare.

- <a id="row-unit-compatible-is-dimension"></a>**`unit-compatible-is-dimension`** — `compatible_iff` · fuaran-core#428

  COMPATIBILITY IS EQUALITY OF DIMENSION. `dimension` is modelled as production computes it — the base-dimension and currency map, every atom's exponents times the factor's — and `compatible a b` holds exactly when every base dimension's and every currency's exponent agrees (`compatible_iff`, through `dim_val`, the dimension read directly off the factors). The dimension of a product is the sum (`dim_combine`), so compatibility is a congruence for the algebra (`compatible_mul`): compatible operands multiply to compatible products. Measured red with the base-dimension entries written at the wrong index.

  _With:_ `dim_combine`, `compatible_mul`, `dim_val`.

  _Boundary:_ Within the model; the correspondence to production is the differential's (`unit-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-unit-factors-compose"></a>**`unit-factors-compose`** — `factor_compose` · fuaran-core#428

  CONVERSION FACTORS COMPOSE AND ARE EXACT. The scale of a product is the product of the scales and of a quotient the quotient (`scale_combine`, `scale_combine_div`, through `rpow_add` — a power of a sum is a product of powers — and the merge's insert), so for any `a`, `b`, `c` the factor from `a` to `c` is the product of the factors through `b` as rationals (`factor_compose`), a unit's factor to itself is one (`factor_self`) and the two directions multiply to one (`factor_inverse`). `reduce` — production's gcd to lowest terms — changes no value (`reduce_exact`, over Euclid's `gcd` proved to divide both terms): the `Ratio` a consumer receives IS the exact quotient of the two scales. Measured red with the power law's mixed-sign case swapping numerator and denominator.

  _With:_ `rpow_add`, `scale_combine`, `scale_combine_div`, `reduce_exact`, `factor_self`, `factor_inverse`.

  _Boundary:_ `conversionFactor` answers these only when the units are compatible, and refuses otherwise naming both. Within the model; the correspondence to production is the differential's (`unit-differential`) and the extractor's (premise `extractor-and-compiler-trusted`). `factor_compose` is equality of VALUE: that two lowest-terms pairs of one value are one pair (uniqueness of the reduced form, a fact about gcd) is not proved; `unit-differential` holds production's `Ratio` to the model's reduced pair by sampling.

**Tested** (1)

- <a id="row-unit-differential"></a>**`unit-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#428

  The extracted unit algebra agrees with the shipped one: `Unit.parse` beside the model's over a drawn pool of units (every admitted atom, prefixed and bare, under exponents in -3..3, in products of up to three) and a refusal corpus reaching every refusal class with the token and position it names; `Unit.render` of every parsed unit beside the model's text; `mul` / `div` / `pow`, `compatible` and `conversionFactor` over drawn pairs, the factor compared as production's `Ratio` against the model's reduced pair. The go-red is the canonicaliser that SKIPS THE MERGE — the model's `render` handed the concatenation of two factor lists rather than their merge — which loses on exactly the draws whose concatenation is not already the merge, and on no other.

  _Boundary:_ Agreement over the drawn pool and refusal corpus only, never over all inputs. That two lowest-terms pairs of one value are one pair is a fact the model does not prove; it is held here, by sampling. The closure the theorems' premise rests on — every unit the shipped parser and algebra answer being CANONICAL in the model's sense (`canonical`: well-formed, in range, every atom admitted) — is likewise sampled, not proved. Beside it, `ParityVectors` carries unit rows, so the downstream value check compares parse, render, compatibility and the factor between .NET and JavaScript.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `Temporal`

[`Temporal.fst`](Temporal.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `JsonParse`, `Limits`, `Utf8`, `WireCanon`.

> Temporal — an F\* model of the TEMPORAL ENCODINGS (fuaran-core Phase 430, the model Phase 419
> deferred and Phase 422 introduced): `src/Fuaran.Core.Column/TemporalText.fs` clause for clause,
> with the laws the column suite samples stated as theorems.

**What is modelled.** `TimeUnit` and its module (`digits`, `scale`, `ofDigits`, `widens`); the
calendar in integers — `TemporalText.daysOfCivil` / `civilOfDays` (Hinnant's days-from-civil and
civil-from-days, era-shifted exactly as production shifts them), `isLeap`, `daysIn`, `MinDay` /
`MaxDay`, `isDayInRange`; the two canonical texts and their readers — `tryDays` / `dateText` for a
date, `tryInstant` / `instantText` / `unitOf` / `isCanonicalTimestamp` for an instant, over the
private `digitsAt`, `datePart`, `fractionPart`, `parseInstant`, `pad` and `pow10`; the range check
`isInstantInRange`; and `compareInstants`, THE order over timestamp text that `Cell.compare` is.

**Proved** (3)

- <a id="row-column-temporal-calendar"></a>**`column-temporal-calendar`** — `civil_days_civil` · fuaran-core#430

  THE CALENDAR IS A BIJECTION. `proofs/Temporal.fst` models `TemporalText.daysOfCivil` / `civilOfDays` line for line — Hinnant's days-from-civil and civil-from-days in integers, era-shifted as production shifts them — and proves that `civilOfDays` answers a civil date that exists for EVERY day count (`civil_valid_days`: a month `1`..`12`, a day `1`..`daysIn`), that `daysOfCivil` of it is the count it was asked for (`days_civil_days`), and that `civilOfDays (daysOfCivil y m d)` is `{y; m; d}` for every civil date that exists (`civil_days_civil`). A later civil date is a larger day count (`days_of_civil_mono`), so the integers order chronologically, and the canonical range `0000-01-01`..`9999-12-31` is exactly `MinDay`..`MaxDay` (`year_in_range`, `day_in_range_year`). The one hard fact — that the corrected quotient `(doe - doe/1460 + doe/36524 - doe/146096) / 365` recovers the year of the era — is proved from the quotient's monotonicity and its value at the two ends of each of the 400 years of an era, those 800 values computed by normalisation (`year_ends_ok`): a theorem about 146,097 day counts the solver never searches. Measured red with the corrected quotient losing its `36524` term, and with February given 29 days in every year.

  _With:_ `civil_valid_days`, `days_civil_days`, `days_of_civil_mono`, `year_in_range`, `day_in_range_year`, `g_mono`, `doy_mono`, `year_ends_ok_head`, `year_ends_ok_tail`.

  _Boundary:_ Within the model, in F\*'s integers; the correspondence to production's integer types is the differential's (`column-temporal-differential`, over drawn day counts) and the extractor's (premise `extractor-and-compiler-trusted`). Not claimed: `civilOfDays` below day `-865565`, where production's truncating division and the model's Euclidean one part; every day the canonical form spells is 146,000 days above it.

- <a id="row-column-temporal-encodings"></a>**`column-temporal-encodings`** — `try_instant_instant_text` · fuaran-core#430

  A DATE HAS ONE TEXT, AND AN INSTANT ONE TEXT PER UNIT — THE SAME TEXT IN EVERY UNIT. `proofs/Temporal.fst` models `TemporalText` clause for clause over `WireCanon`'s character alphabet — `tryDays` / `dateText`, `tryInstant` / `instantText` / `unitOf` / `isCanonicalDate` / `isCanonicalTimestamp` / `isInstantInRange` over `digitsAt`, `datePart`, `fractionPart`, `parseInstant`, `pad` and `pow10`, with `TimeUnit`'s `digits` / `scale` / `ofDigits` / `widens` — reading a text sequentially where production indexes it. For every day in range `tryDays (dateText d) == Some d` (`try_days_date_text`); whatever `tryDays` reads back is `dateText` of that day, in range (`date_text_try_days`); and `dateText` is canonical exactly on the range (`date_text_canonical`). For every unit and every pair in range `tryInstant u (instantText u s f) == Some (s, f)` (`try_instant_instant_text`); whatever `tryInstant u` reads back is `instantText u` of the pair, in range (`instant_text_try_instant`); widening a coarser unit into a finer one scales the fraction and writes the SAME text (`widen_keeps_instant`) — one text per instant whatever unit holds it, D143.3 as a theorem; and `unitOf` is the coarsest unit that reads the text, so `Cell.typeOf`'s unit widens into exactly the columns that hold the cell (`unit_of_coarsest`). With a fraction in `[0, scale)` the text is canonical exactly when the second is in range (`instant_text_canonical`). Measured red with the fraction read at the column's unit without scaling, and with a trailing zero kept in the written fraction.

  _With:_ `try_days_date_text`, `date_text_try_days`, `date_text_canonical`, `instant_text_try_instant`, `widen_keeps_instant`, `unit_of_coarsest`, `instant_text_canonical`, `try_instant_seconds`.

  _Boundary:_ The model reads in F\*'s unbounded integers where production holds an `int32` day, an integer-valued `float` second and an `int32` fraction; the integer-valued second is the one bridge, and the model's header says why. The theorems are over days and pairs in range; a fraction at or past the scale spells ANOTHER instant's text, which is `Table.validate`'s to refuse first and is why the refinement's agreement rows carry a fraction premise. `dateText` is canonical exactly on the range because a negative year pads to a `-` inside the width and a five-digit year leaves a digit where `-` must stand. The .NET edge (`Column.ofDateOnlys` / `ofDateTimeOffsets` / `tryDateOnly` / `tryDateTimeOffset`) has no model; it is `column-temporal-dotnet-edge`'s.

- <a id="row-column-temporal-order"></a>**`column-temporal-order`** — `compare_instants_chronological` · fuaran-core#430

  THE ORDER ON TIMESTAMP TEXT IS CHRONOLOGICAL, which is `Cell.compare`'s timestamp arm (D143.3). `TemporalText.compareInstants` is modelled arm for arm — two canonical texts compare on their fixed-width `YYYY-MM-DDThh:mm:ss` prefix ordinally and then on their fraction digits ordinally; a text that is not canonical sorts after every canonical one — over an ordinal rank of the alphabet (`ch_rank`). For two canonical texts read at one unit, `compareInstants` is the order of the pairs they spell, the second first and then the fraction (`compare_instants_chronological`): ordinal comparison of equal-width digit runs is the order of their values (`cmp_text_digits`, `cmp_pad`), the prefix is such runs around fixed separators and compares as the second through the calendar's monotonicity (`cmp_prefix`), and the fraction digits, trimmed of trailing zeros, compare as the padded fraction does (`cmp_trim`, `cmp_frac_digits`) — which is why `…12.5Z` sorts above `…12.25Z` where ordinal string order would not. Measured red with the fraction digits compared before the prefix, and with the trim dropping a zero ahead of a digit.

  _With:_ `cmp_text_digits`, `cmp_prefix`, `cmp_trim`.

  _Boundary:_ The chronological theorem is for two canonical texts read at one unit; a non-canonical text is only placed after every canonical one. `ch_rank` is the UTF-16 code of every character but `CPlain`, which the alphabet does not carry and no canonical text puts at a position another canonical text spells differently. Within the model; the correspondence to production is the differential's (`column-temporal-differential`) and the extractor's (premise `extractor-and-compiler-trusted`). Not claimed: `compareInstants` between two texts that are not canonical, where production falls to ordinal string order, which this alphabet cannot rank for a plain character; no column holds such a text.

**Tested** (1)

- <a id="row-column-temporal-differential"></a>**`column-temporal-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#430

  The extracted temporal model agrees with the shipped `TemporalText` and `TimeUnit` over the canonical range's two ends and their neighbours, 1,500 drawn day counts across and beyond the range (`dateText`, `isDayInRange`, `tryDays` of the text, `civilOfDays` and `daysOfCivil` back), fixed and corrupted date texts (`tryDays`), per unit the range's ends and 1,500 drawn pairs with fractions at the scale's edges and out of range both ways (`isInstantInRange`, `instantText`, `tryInstant` of the text, `unitOf`), instant texts of every fraction length and the ill-formed shapes — a trailing zero, ten digits, no `Z`, a bad clock (`isCanonicalTimestamp`, `unitOf`, `tryInstant` at every unit) — and 3,000 ordered pairs of canonical texts, equal, a fraction or a second apart, and unrelated (`compareInstants`). The draws are asserted to reach both sides of every range, canonical and non-canonical texts, fractional pairs and every unit. The go-red is a model asked to read every text at the seconds unit, which loses on exactly the draws with a fraction and on no other — `tryInstant`'s unit arm.

  _Boundary:_ Agreement over the sampled cases only, never over all inputs. The model's integers are `bigint` and production's a day `int`, a second an integer-valued `float` and a fraction an `int`, converted at the edge; the drawn days stay inside the zone where production's truncating division and the model's Euclidean one agree, which holds the canonical range with a margin either side.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

**Inherited through the models it opens:** [`parser-float-readback-opaque`](#row-parser-float-readback-opaque) (assumed), [`parser-alphabet-bridge`](#row-parser-alphabet-bridge) (assumed), [`canon-numeral-layouts`](#row-canon-numeral-layouts) (assumed), [`utf8-model-is-the-encoder`](#row-utf8-model-is-the-encoder) (assumed), [`canon-unit-spelling`](#row-canon-unit-spelling) (assumed).

### `WireColumn`

[`WireColumn.fst`](WireColumn.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `JsonParse`, `Limits`, `Utf8`, `WireCanon`, `Unit`, `Temporal`.

> WireColumn — an F\* model of the COLUMNAR CODEC, with its image and its round trip as machine-checked
> theorems (fuaran-core Phase 306).

**What is modelled.** `src/Fuaran.Core.Column/` at the LIST LEVEL: a column is its name, its type
and its cells, `{ name; ctype; cells }`, and over that column `ColumnType.tag` / `ofTag` /
`widens`, `Cell.typeOf`, `DecimalText.tryCanonical` / `isCanonical`, `Table.validate`'s five
clauses with the cell scan `firstUncarriableCell` performed before Phase 417, and
`ColumnCodec.encodeJson` / `decodeJson` / `tryEncode` with every private function those three
reach (`absentSlot`, the cell encoder `cellJson` that `columnJson` applied per cell, `schemaJson`,
the cell reader `decodeCell` that `decodeColumn` applied per slot, `decodeSchemaEntry`,
`decodeSchema`, `columnParts`, `uniqueColumnKeys`), each clause for clause as the source had
it. The level is the `JVal`: `encodeJson` builds one and `decodeJson` reads one, and everything
the codec decides it decides there. The strings either side of it are `Canonical.write Rendering.CrossHost` and the
parser, which `WireCanon.fst` and `JsonParse.fst` already model; section 9 says exactly how far
this module's theorems compose with those, and where they stop.

**Proved** (5)

- <a id="row-column-codec-image-valid"></a>**`column-codec-image-valid`** — `encode_image_revalidates` · fuaran-core#306

  THE CODEC'S IMAGE LIES INSIDE WHAT `Table.validate` ACCEPTS. Whatever `ColumnCodec.decodeJson` answers `Embedded t` for is a table `validate` accepts (`decode_image_is_valid`) — true by the last line of production's decode, so the half with content is the other one: the table decode REBUILDS from the encoding of a validated table — schema order, schema names, widened cells — is itself one `validate` accepts (`encode_image_revalidates`), without which the final `validate` could refuse the encoder's own output and the round trip would be an error. Modelled at the `JVal`, clause for clause: `Table.validate` with `firstUncarriableCell`, `ColumnCodec.encodeJson` / `decodeJson` / `tryEncode` with every private function those reach, and `DecimalText.tryCanonical`; it is `Fuaran.Core.Column`'s first model.

  _With:_ `decode_image_is_valid`.

  _Boundary:_ Within the model, at the `JVal`; what the model is handed rather than derives is `column-codec-abstractions`, and the correspondence to production is the differential's (`column-codec-differential`) and the extractor's (premise `extractor-and-compiler-trusted`). `OutOfModel`, the model's answer on the decoder's five lenient-ingest arms, is a verdict of the MODEL and not a refusal of the codec; `decode_image_is_valid` holds for every input because `OutOfModel` is a `Bad`.

- <a id="row-column-codec-round-trip"></a>**`column-codec-round-trip`** — `round_trip` · fuaran-core#306

  THE ROUND TRIP, TO A NORMAL FORM. For every table `Table.validate` accepts, `decodeJson (encodeJson (Embedded t))` is `Embedded (normal_table t)` — the table with its columns in schema order and each `Int` cell widened into its float or decimal column (`round_trip`); a `Ref` round-trips exactly and unconditionally (`ref_round_trip`). `normal_table` is a fixed point (`normal_table_is_idempotent`), every table the decoder produces for ANY document is already normal (`decoded_table_is_normal`), and so from the first decode on the round trip is the literal one (`decoded_round_trip_is_exact`). The string level joins the canonical renderer through `round_trip_through_normalise` and, on that renderer's canonical subset, `string_round_trip`.

  _With:_ `ref_round_trip`, `normal_table_is_idempotent`, `decoded_table_is_normal`, `decoded_round_trip_is_exact`, `round_trip_through_normalise`, `string_round_trip`, `round_trip_is_exact_on_a_normal_table`, `second_round_trip_is_exact`, `int_text_canonical_is_necessary`, `int_floats_finite_is_necessary`, `of_tag_inverts_tag`, `decode_cell_inverts`.

  _Boundary:_ A float column holding a whole value or a null is outside the canonical renderer's subset, and the model's header says so. Two facts about Int32 are named hypotheses, each proved NECESSARY (`column-int-layouts`). NOT modelled: the decoder's five lenient-ingest arms, which the encoder's image never reaches — `round_trip` concluding `Good` is the proof of that — the aggregates, `Schema.*`, and `DecimalText`'s arithmetic. Stated over the unit-carrying column type (`fuaran-core#430`): a timestamp cell is typed by the coarsest unit its text names (`Temporal.unit_of`), the decoder reads a timestamp at the column's unit, and a validated timestamp decodes because its coarsest unit widens into the column's (`decode_cell_inverts`, through `unit_of_coarsest`); `of_tag_inverts_tag` covers the ten column types. Every theorem was re-verified over that model unchanged in statement.

- <a id="row-column-codec-literal-round-trip-false"></a>**`column-codec-literal-round-trip-false`** — `literal_round_trip_fails_on_column_order` · fuaran-core#306

  THE LITERAL ROUND TRIP IS FALSE of the shipped codec, three ways, each a refutation for every host and every name: an `Int` in a float column is accepted by `validate`, written as a JSON float and read back a `Float` (`literal_round_trip_fails_on_a_widened_float`); an `Int` in a decimal column comes back a `Decimal` (`literal_round_trip_fails_on_a_widened_decimal`); and `validate` holds the schema's names and the columns' names equal as SETS and does not ask for one order, so a table whose columns are in another order comes back in schema order (`literal_round_trip_fails_on_column_order`). Also proved, about what decode accepts that encode never writes: a `columns` member outside the schema is read past where `validate` refuses the table it would have meant, a masked slot is never read, and `ref` wins over `columns`.

  _With:_ `literal_round_trip_fails_on_a_widened_float`, `literal_round_trip_fails_on_a_widened_decimal`, `decode_drops_a_column_outside_the_schema`, `validate_refuses_a_column_outside_the_schema`, `decode_never_reads_a_masked_slot`, `decode_never_reads_columns_under_an_empty_schema`, `decode_ignores_a_surplus_root_member`, `ref_wins_over_columns`.

  _Boundary:_ None is a defect: the first two are the widenings `ColumnType.widens` pins, and the third is the schema being the order authority, which the type's doc says. `decode (encode t) = Ok t` must not be read into `column-codec-round-trip`; a generative codec law asserting exactly that passes only by sampling inside the normal form. The converse, `encode (decode d) = d`, is not claimed and is false: besides the acceptances above, under an empty schema `columns` need not be an object at all, and a surplus root member is read past unless it is called `ref`. Since the typed column (`fuaran-core#419`), `Column.ofCells` widens an `Int` at construction, so of the three refutations only column order still arises from a production table; the two widenings are of the list model.

- <a id="row-field-codec-round-trip"></a>**`field-codec-round-trip`** — `round_trip_f` · fuaran-core#428

  A FIELD'S METADATA SURVIVES ENCODE THEN DECODE. `proofs/WireColumn.fst` section 10 models the schema entry as a field — name, type, an optional unit (`Unit.fst`'s value), an optional label and description, and the extension map as a key-unique association list — with `ColumnCodec.fieldJson` / `decodeField` clause for clause (`field_json`, `decode_field`: the unit's text parsed by `Unit.parse` and refused when it does not parse, the text members held to strings, the extension object to string members, an unknown member read past), and `encodeJson` / `decodeJson` (`encode_json_f`, `decode_json_f`: the schema as fields, the columns by the entries' names and types, the final `validate` over the stripped table). For a well-formed field `decode_field (field_json f) == Good f` (`decode_field_inverts`, the unit through `Unit.parse_render` and the alphabet bridge `unit_text_inverse`), and for every such table `decode_json_f (encode_json_f (Embedded_f t)) == Good (Embedded_f (normal_table_f t))` (`round_trip_f`): the columns in `column-codec-round-trip`'s normal form, every field's metadata intact. The image stands (`decode_image_f_is_valid`), and a field decode that answers is the entry decode with metadata on top (`decode_field_entry`). Measured red with the extension reader dropping a member, and with the unit written by its non-canonical spelling.

  _With:_ `decode_field_inverts`, `unit_text_inverse`, `uch_ch_inverse`, `decode_ext_inverts`, `ref_round_trip_f`, `decode_image_f_is_valid`, `decode_field_entry`.

  _Boundary:_ Only for well-formed fields — the unit canonical, the extension keys distinct — and for tables whose stripped form `validate` accepts and whose fields are well-formed. Within the model; the correspondence to production is the differential's (`field-codec-differential`) and the extractor's (premise `extractor-and-compiler-trusted`). The field codec inherits `column-int-layouts` and `column-codec-abstractions` unchanged (the same host record, the same dropped prose). Not claimed: `Schema.diff` / `classify` / `fingerprint` over fields, which the list codec model does not model either.

- <a id="row-field-codec-plain-entry"></a>**`field-codec-plain-entry`** — `encode_json_f_plain` · fuaran-core#428

  AN ENTRY WITHOUT METADATA ENCODES EXACTLY AS A PLAIN ENTRY: a field that states no unit, label, description or extension member (`Field.hasMetadata` false) encodes to the two-member object section 4's `schema_json_items` writes for `(name, type)` (`field_json_plain`), and a table whose every field is such encodes, through `encode_json_f`, to BYTE FOR BYTE the document `encode_json` writes for its stripped form (`encode_json_f_plain`) — so every metadata-free column document is the document the field-less codec writes.

  _With:_ `field_json_plain`.

  _Boundary:_ Only for tables whose every field states no metadata. Within the model; that production's documents match is what the corpus families and the pinned-bytes tests sample, not prove.

**Tested** (2)

- <a id="row-column-codec-differential"></a>**`column-codec-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#306

  The extracted codec agrees with the shipped one over two generated pools and a table of hand-written documents. Per generated table: `Table.validate` (the verdict and, on a refusal, the class, the column and which of the five malformations it is); `ColumnCodec.encodeJson` (the `JVal`, of ANY table, valid or not); `decodeJson` of that encoding; and — for a table `validate` accepts — production's round trip held to the MODEL's `normal_table`, both at the `JVal` and through `Canonical.write Rendering.CrossHost` and the parser, which is the round trip a consumer performs. The first pool holds only valid tables and NOT only normal ones: a third have their columns out of schema order, so the round trip is measured where it is not the literal one (the test asserts more than 150 such tables of 2,000 at the pinned seed) as well as where it is (more than 200). An `Int` cell the generator draws into a float or decimal column is widened by `Column.ofCells` at construction, so column order is the only departure from the normal form the pool reaches. The second wanders outside what `validate` accepts and is asserted to reach five refusal classes. The hand-written documents are the decode-only acceptances the model's lemmas name and the five lenient-ingest arms, which the model declines as `OutOfModel` — asserted to be exactly five, so a probe that skipped more would say so. The go-red is the LITERAL comparison: held to the table itself rather than its normal form, production loses on exactly the tables outside the normal form, at both levels, and on no other. A last case samples the two `Int32` hypotheses and holds the modelled `DecimalText.tryCanonical` to production over a pool of decimal texts.

  _Boundary:_ Agreement over the sampled pools and hand-written documents only, never over all tables or documents; the two `Int32` hypotheses and `DecimalText.tryCanonical` are sampled here, not proved. The model drops production's error prose and its counts, so a refusal is compared by class and column, never by message. A fractional float in a timestamp or a decimal column, which production refuses as a `TypeMismatch`, is outside the differential: the model's float arm there answers `OutOfModel` whichever way production's float guard falls, the guard being float arithmetic the model does not carry.

- <a id="row-field-codec-differential"></a>**`field-codec-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#428

  The extracted field codec agrees with the shipped one: `ColumnCodec.fieldJson` beside the model's `field_json` over drawn fields (every column type the list model spells, a unit from the drawn pool or none, a label, a description and up to three extension members, each stated or not), the rendered documents compared byte for byte; `ColumnCodec.decodeField` beside `decode_field` over those documents and a hand-written set — a missing name, an unknown type, a unit the kit does not parse, a non-canonical and an aliased unit spelling read to the unit, a non-string label, an extension object that is not an object and one with a non-string member, and an unknown member read past — the field compared member by member and the refusal by its class; and `encodeJson` / `decodeJson` beside `encode_json_f` / `decode_json_f` over tables carrying metadata, the round trip held to the model's `normal_table_f` with every field intact. The go-red is the bridge that hands the model the extension members in REVERSED order — production holds a map, which writes them in key order — and it loses on exactly the fields with two or more extension members, and on no other.

  _Boundary:_ Agreement over the drawn fields, the hand-written set and the drawn tables only, never over all inputs.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`column-int-layouts`](#row-column-int-layouts) (assumed), [`column-codec-abstractions`](#row-column-codec-abstractions) (assumed), [`query-codec-column-premises`](#row-query-codec-column-premises) (assumed).

**Inherited through the models it opens:** [`parser-float-readback-opaque`](#row-parser-float-readback-opaque) (assumed), [`parser-alphabet-bridge`](#row-parser-alphabet-bridge) (assumed), [`canon-numeral-layouts`](#row-canon-numeral-layouts) (assumed), [`utf8-model-is-the-encoder`](#row-utf8-model-is-the-encoder) (assumed), [`canon-unit-spelling`](#row-canon-unit-spelling) (assumed).

### `Vocabulary`

[`Vocabulary.fst`](Vocabulary.fst) — generated by `Fuaran.Core.Idl.Codegen`'s F\* target and held to a fresh generation. Opens `WireDecode`.

> GENERATED by Fuaran.Core.Idl.Codegen — F\* model. Do not edit by hand: regenerate it from its inputs.
> vocabulary: sha256:8834c0249dad3698aebf9163acb71ffd35543a7d0c8df0dd45510f173b632dc2
> emission: sha256:d8b147293129455e34625ee5e709c0b20e9ceb60b093a617dbd8870e4b57d1c8

**Proved** (1)

- <a id="row-vocabulary-generated-model"></a>**`vocabulary-generated-model`** — `dec_node` · fuaran-core#173

  The F\* BACKEND is certified over the vocabularies the engine is certified on, and the models it emits are total. `proofs/Vocabulary.fst`, `DocVocabulary.fst` and `ScoreVocabulary.fst` carry the types, the discriminated encoder and the tag-dispatch decoder of the engine's reference vocabulary (`tests/Fuaran.Core.Tests.Fixtures/ReferenceIdl.fs`) and of the two vendored non-UI samples, emitted by a fourth backend of the IDL's code generator (`FStarTarget`, beside the F# structural layer, the TypeScript encoder and the JSON schema) over the `jval` model and the combinators modelled with it — the same certification set `IdlCertificationTests` holds the F# and TypeScript backends to, reaching both declared wire shapes and every type case the reference vocabulary was authored to reach. Every decoder is `Tot` on an arbitrary `jval` and therefore total and terminating on every input, which F\* admits only after proving it; the termination argument is `get_prop`'s return-type refinement, since a decoder that descends BY NAME into a value it looked up has no syntactic one. Generic unions are monomorphised at the arguments each vocabulary reaches, which keeps every definition first-order. Proved with no assume and no admit under --report_assumes error. Every kind other than the refused ones is modelled: `FStarTarget.proofKinds`, the UI-scale cost control, is deliberately not what selects here (over the reference vocabulary it keeps one kind of five, which is pinned), because the whole set costs seconds. Each committed file is held to a fresh generation from its vocabulary IN THE TEST PROJECT by the Proofs.Vocabulary family — a vocabulary that moves without a regeneration fails the leg as vocabulary drift, and nothing in the leg reads the shared corpus for these files. Over these models the ROUND TRIP (`dec_node (enc_node x) == Ok x`, `rt_node` in `VocabularyProofs.fst` / `DocVocabularyProofs.fst` / `ScoreVocabularyProofs.fst`) is emitted by the same walk, committed, and carried as its own `proved` rows (`vocabulary-round-trip`, `doc-vocabulary-round-trip`, `score-vocabulary-round-trip`). It is emitted ONE LEMMA PER CONSTRUCTOR over a LINEAR presence split — by MEMBER, one `lk_…` lookup per member with the other conditional members left free, `2k + r'` lemmas for a constructor with k of them, where a per-pattern split is 2^k in the lemma COUNT (`fuaran#1754` measured that at 71,722 lemmas in a 114 MB script) — and discharges at the leg's own rlimit of 40 under `--quake 3`. The certification set exercises the split (the reference vocabulary's node envelope and `Embed`, the score sample's `Measure` and `Score`), and `IdlFStarTargetTests` pins the shape at the reference vocabulary and at a UI-scale fixture (a five-optional envelope, an eleven-member kind with five conditional, and a sixteen-conditional kind at `fuaran#1754`'s `DataGrid` width) so the adopter inherits an emitter already proved to emit it at that scale.

  _Boundary:_ What is proved is the BACKEND, not the UI vocabulary: the UI vocabulary's model, proofs, cost and exhaustive-coverage decision are the adopter's — `fuaran#1754`, the kit's first adopter — where the cost is charged to the commits that change UI kinds, a domain's proof in the substrate's CI being the placement D14 rules out. ONE refusal shape is met, and it is named rather than dropped, because a silently-dropped member would make the round trip a theorem about a document nobody sends: a declared NUMERIC default has no F\* literal when the numeric carriers are opaque type parameters, so `Measure` (reference), `Note` and `Chord` (score) are refused by name in their headers — the same class as `Tabs.activeIndex` — and `IdlFStarTargetTests` pins the set as exactly those three and pins every other refusal class of the backend to a hand-written case. Since `fuaran-core#204`, `#222` and `#224` the model writes a wide constructor's member list as a chain of opaque suffixes whose links pass each member through a per-slot option encoder, and reads each conditional scalar member through an opaque reader with one value lemma: the model text is linear in k and, on a synthetic probe with sixteen conditional members, the model checks, every lookup discharges and the round-trip arm discharges at the leg's rlimit of 40. That probe is not in the leg. The proof script is QUADRATIC in k per wide constructor. A conditional member whose read calls the decoder family (a record, union, node, list or map) is still read inline with its two-way presence citation; no vocabulary here carries a wide constructor of such members, so that case is unmeasured, not known to be cheap. Not addressed: the size of the mutual family each query is checked inside. Every kind with children reaches `node` and `node` reaches every kind, so the family is one strongly connected component, and widening a proof vocabulary past the envelope's closure needs it split, which is generator work sized by the adopter's vocabulary. The `wf` characterisation over the generated models is HALTED with its reason recorded (see the round-trip rows). A host-only member is absent from the model, because it never crosses the wire; a wire-visible closure is modelled as `unit` encoding to the fixed `"<closure>"` sentinel. The models are CHECKED but not EXTRACTED: the decoder they model is one the generator emits into a consuming host, not one this repository ships. Reaching every type case is not reaching every refusal class: the other refusal classes are refusals of an ill-formed IDL, which no certification vocabulary carries, hence the hand-written cases. Nor is anything claimed about a domain's vocabulary or a host's hand-written decoder, which a host certifies against its corpus.

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `VocabularyProofs`

[`VocabularyProofs.fst`](VocabularyProofs.fst) — generated by `Fuaran.Core.Idl.Codegen`'s F\* target and held to a fresh generation. Opens `WireDecode`, `Vocabulary`.

> GENERATED by Fuaran.Core.Idl.Codegen — F\* proofs. Do not edit by hand: regenerate it from its inputs.
> vocabulary: sha256:8834c0249dad3698aebf9163acb71ffd35543a7d0c8df0dd45510f173b632dc2
> emission: sha256:5dab8bdfbed3122673612f332a4e386245e0af50bb0507385817324d2f5b885b

**Proved** (1)

- <a id="row-vocabulary-round-trip"></a>**`vocabulary-round-trip`** — `rt_node` · fuaran-core#173

  The ROUND TRIP over `Vocabulary`, GENERATED and COMMITTED: `rt_node` in `proofs/VocabularyProofs.fst` proves `dec_node (enc_node x) == Ok x` for every value of every modelled type — at every depth, through every list, map, optional member and omit-default — by one mutual induction over the family, recursing on the MODEL value, emitted as ONE LEMMA PER CONSTRUCTOR over a LINEAR presence split (`rt_<T>` a case split citing `rt_<T>__<Ctor>`; a constructor with two or more conditional members proved by citing one `lk_<T>__<Ctor>__<member>` LOOKUP per member — two for a conditional one, `__present` and `__absent`, each pinning only its own member and leaving the others free — so the count is `2k + r'` rather than a per-pattern split's 2^k), so no query carries more than one key's walk and the whole script discharges at the leg's own rlimit with no in-file `--z3rlimit 200` override; `dec_node_total` beside it states that every input reaches exactly one of `Ok` / `Error`. Emitted by `FStarTarget.proofsModuleFrom` from the same walk that emits the model, so the two files cannot be about different vocabularies, and held to a fresh generation by the Proofs.Vocabulary family like the model. Proved with no assume and no admit under --report_assumes error, under the leg's own `--quake 3`, in seconds (`proofs/modules.json`). It is over the engine's REFERENCE vocabulary — four of its five kinds, every type case it was authored to reach, on the default wire shape.

  _Boundary:_ Over the reference vocabulary's modelled kinds on the default wire shape only. The one-lemma shape first emitted at `fuaran-core#150` did NOT discharge over the UI vocabulary — a 65-goal node query failing a quake seed, a 2^k blow-up in the widest kind's arm — and the per-constructor linear split exists for that wall. This script discharges over the certification set, where the envelope is two scalars and k = 2. Whether the current shape discharges over the UI vocabulary is the adopter's (`fuaran#1754`) to measure, and is not this row's claim. NOT claimed: the `wf` characterisation (`Ok? (dec el) == wf el`); the only acceptance predicate this emitter could generate from the same walk is the decoder's own accept set restated clause for clause, and a characterisation against that is a theorem about two renderings of one definition — true and empty; a characterisation worth the name needs an INDEPENDENT statement of the accept set, a model-sized artefact of its own (a schema-shaped predicate, which the IDL's JSON-schema backend already emits for hosts, modelled as a third generated artefact). Contrast `decode-node-wf`, whose `wf` in `WireDecode.fst` is written by hand, independently of the decoder.

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `DocVocabulary`

[`DocVocabulary.fst`](DocVocabulary.fst) — generated by `Fuaran.Core.Idl.Codegen`'s F\* target and held to a fresh generation. Opens `WireDecode`.

> GENERATED by Fuaran.Core.Idl.Codegen — F\* model. Do not edit by hand: regenerate it from its inputs.
> vocabulary: sha256:587bb7678b2b29ba684110454ffbc42eb3f4bad98388f4cdd0d8ad4523627a36
> emission: sha256:c856580f02fcadfe1c7aea5ca127fca3576bd9215859780d15d6943f616833b5

**Proved** (1)

- <a id="row-doc-vocabulary-generated-model"></a>**`doc-vocabulary-generated-model`** — `dec_node` · fuaran-core#173

  The GENERATED model of the vendored SECOND-DOMAIN sample — `proofs/DocVocabulary.fst`: the vocabulary's types, its discriminated encoder and its tag-dispatch decoder, emitted by `FStarTarget.vocabularyModuleFrom` from `tests/Fuaran.Core.Tests.Fixtures/SecondDomainIdl.fs` over the `jval` model and the combinators modelled with it. Every decoder is `Tot` on an arbitrary `jval` and therefore total on every input, which F\* admits only after proving it, by the same `get_prop` termination argument the `vocabulary-generated-model` row states. Every one of its eleven kinds is modelled; it meets no refusal. Proved with no assume and no admit under --report_assumes error, and held to a fresh generation by the Proofs.Vocabulary family. What it adds to the reference vocabulary's row is the DECLARED non-default wire shape — bare-string `kind` discriminator, flat node envelope, declaration key order — which the reference vocabulary, on `WireShape.Default`, does not reach; without this row the backend's other envelope and key-order branches would be certified by nothing under `proofs/`.

  _Boundary:_ Generated from the vendored sample, never the shared corpus. CHECKED but not EXTRACTED, for the reason `check.ps1`'s `$proofOnly` note gives; the claim is within the model.

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `DocVocabularyProofs`

[`DocVocabularyProofs.fst`](DocVocabularyProofs.fst) — generated by `Fuaran.Core.Idl.Codegen`'s F\* target and held to a fresh generation. Opens `WireDecode`, `DocVocabulary`.

> GENERATED by Fuaran.Core.Idl.Codegen — F\* proofs. Do not edit by hand: regenerate it from its inputs.
> vocabulary: sha256:587bb7678b2b29ba684110454ffbc42eb3f4bad98388f4cdd0d8ad4523627a36
> emission: sha256:3bd1652d94721a8a681a26ae5573609c78347c3904fdb4aafb3adf912976a0bd

**Proved** (1)

- <a id="row-doc-vocabulary-round-trip"></a>**`doc-vocabulary-round-trip`** — `rt_node` · fuaran-core#173

  The ROUND TRIP over `DocVocabulary`, GENERATED and COMMITTED: `rt_node` in `proofs/DocVocabularyProofs.fst` proves `dec_node (enc_node x) == Ok x` for every value of every modelled type — at every depth, through every list, map, optional member and omit-default — by one mutual induction over the family, recursing on the MODEL value, emitted as ONE LEMMA PER CONSTRUCTOR over a LINEAR presence split (`rt_<T>` a case split citing `rt_<T>__<Ctor>`; a constructor with two or more conditional members proved by citing one `lk_<T>__<Ctor>__<member>` LOOKUP per member — two for a conditional one, `__present` and `__absent`, each pinning only its own member and leaving the others free — so the count is `2k + r'` rather than a per-pattern split's 2^k), so no query carries more than one key's walk and the whole script discharges at the leg's own rlimit with no in-file `--z3rlimit 200` override; `dec_node_total` beside it states that every input reaches exactly one of `Ok` / `Error`. Emitted by `FStarTarget.proofsModuleFrom` from the same walk that emits the model, so the two files cannot be about different vocabularies, and held to a fresh generation by the Proofs.Vocabulary family like the model. Proved with no assume and no admit under --report_assumes error, under the leg's own `--quake 3`, in seconds (`proofs/modules.json`). It is over the vendored second-domain sample, on the non-default wire shape: the round trip holds through the flat envelope and declaration key order, not only the default shape.

  _Boundary:_ Over the vendored second-domain sample's modelled types only. The one-lemma shape first emitted at `fuaran-core#150` did NOT discharge over the UI vocabulary — a 65-goal node query failing a quake seed, a 2^k blow-up in the widest kind's arm — and the per-constructor linear split exists for that wall. This script discharges over the certification set, where the envelope is two scalars and k = 2. Whether the current shape discharges over the UI vocabulary is the adopter's (`fuaran#1754`) to measure, and is not this row's claim. NOT claimed: the `wf` characterisation (`Ok? (dec el) == wf el`); the only acceptance predicate this emitter could generate from the same walk is the decoder's own accept set restated clause for clause, and a characterisation against that is a theorem about two renderings of one definition — true and empty; a characterisation worth the name needs an INDEPENDENT statement of the accept set, a model-sized artefact of its own (a schema-shaped predicate, which the IDL's JSON-schema backend already emits for hosts, modelled as a third generated artefact). Contrast `decode-node-wf`, whose `wf` in `WireDecode.fst` is written by hand, independently of the decoder.

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `ScoreVocabulary`

[`ScoreVocabulary.fst`](ScoreVocabulary.fst) — generated by `Fuaran.Core.Idl.Codegen`'s F\* target and held to a fresh generation. Opens `WireDecode`.

> GENERATED by Fuaran.Core.Idl.Codegen — F\* model. Do not edit by hand: regenerate it from its inputs.
> vocabulary: sha256:48d2a518690b3f650a2a80e6fed48a571996ff40df48a630fd3a0f35f44927dc
> emission: sha256:e449726a6fad56e3e1f3a90f37727110e5274802759d42d499e8da01a86b41c7

**Proved** (1)

- <a id="row-score-vocabulary-generated-model"></a>**`score-vocabulary-generated-model`** — `dec_node` · fuaran-core#173

  The GENERATED model of the vendored THIRD-DOMAIN sample — `proofs/ScoreVocabulary.fst`: the vocabulary's types, its discriminated encoder and its tag-dispatch decoder, emitted by `FStarTarget.vocabularyModuleFrom` from `tests/Fuaran.Core.Tests.Fixtures/ScoreDomainIdl.fs` over the `jval` model and the combinators modelled with it. Every decoder is `Tot` on an arbitrary `jval` and therefore total on every input, which F\* admits only after proving it, by the same `get_prop` termination argument the `vocabulary-generated-model` row states. Nineteen of its twenty-one kinds are modelled. Proved with no assume and no admit under --report_assumes error, and held to a fresh generation by the Proofs.Vocabulary family. What it adds to the reference vocabulary's row: records and omit-at-default AT SCALE on the non-default shape — the largest generated module here, and an order of magnitude under the UI model it replaced in cost.

  _Boundary:_ `Note` and `Chord` are NOT modelled: they meet the one boundary a well-formed vocabulary reaches — a declared numeric default (`voice = 1`) that the opaque numeric carriers cannot spell — and are refused by name in the header, pinned as exactly that pair by `IdlFStarTargetTests`. Generated from the vendored sample, never the shared corpus. CHECKED but not EXTRACTED, for the reason `check.ps1`'s `$proofOnly` note gives; the claim is within the model.

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `ScoreVocabularyProofs`

[`ScoreVocabularyProofs.fst`](ScoreVocabularyProofs.fst) — generated by `Fuaran.Core.Idl.Codegen`'s F\* target and held to a fresh generation. Opens `WireDecode`, `ScoreVocabulary`.

> GENERATED by Fuaran.Core.Idl.Codegen — F\* proofs. Do not edit by hand: regenerate it from its inputs.
> vocabulary: sha256:48d2a518690b3f650a2a80e6fed48a571996ff40df48a630fd3a0f35f44927dc
> emission: sha256:d8f354fc86f2b23f47f05e7f4bfd6efbe63b48116c45cdd49c32b797bb88201a

**Proved** (1)

- <a id="row-score-vocabulary-round-trip"></a>**`score-vocabulary-round-trip`** — `rt_node` · fuaran-core#173

  The ROUND TRIP over `ScoreVocabulary`, GENERATED and COMMITTED: `rt_node` in `proofs/ScoreVocabularyProofs.fst` proves `dec_node (enc_node x) == Ok x` for every value of every modelled type — at every depth, through every list, map, optional member and omit-default — by one mutual induction over the family, recursing on the MODEL value, emitted as ONE LEMMA PER CONSTRUCTOR over a LINEAR presence split (`rt_<T>` a case split citing `rt_<T>__<Ctor>`; a constructor with two or more conditional members proved by citing one `lk_<T>__<Ctor>__<member>` LOOKUP per member — two for a conditional one, `__present` and `__absent`, each pinning only its own member and leaving the others free — so the count is `2k + r'` rather than a per-pattern split's 2^k), so no query carries more than one key's walk and the whole script discharges at the leg's own rlimit with no in-file `--z3rlimit 200` override; `dec_node_total` beside it states that every input reaches exactly one of `Ok` / `Error`. Emitted by `FStarTarget.proofsModuleFrom` from the same walk that emits the model, so the two files cannot be about different vocabularies, and held to a fresh generation by the Proofs.Vocabulary family like the model. Proved with no assume and no admit under --report_assumes error, under the leg's own `--quake 3`, in seconds (`proofs/modules.json`). It is over the vendored third-domain sample — the widest generated family here, with records and omit-at-default at scale — and it is the largest proof script in the leg.

  _Boundary:_ Over the vendored third-domain sample's modelled types only. The one-lemma shape first emitted at `fuaran-core#150` did NOT discharge over the UI vocabulary — a 65-goal node query failing a quake seed, a 2^k blow-up in the widest kind's arm — and the per-constructor linear split exists for that wall. This script discharges over the certification set, where the envelope is two scalars and k = 2. Whether the current shape discharges over the UI vocabulary is the adopter's (`fuaran#1754`) to measure, and is not this row's claim. NOT claimed: the `wf` characterisation (`Ok? (dec el) == wf el`); the only acceptance predicate this emitter could generate from the same walk is the decoder's own accept set restated clause for clause, and a characterisation against that is a theorem about two renderings of one definition — true and empty; a characterisation worth the name needs an INDEPENDENT statement of the accept set, a model-sized artefact of its own (a schema-shaped predicate, which the IDL's JSON-schema backend already emits for hosts, modelled as a third generated artefact). Contrast `decode-node-wf`, whose `wf` in `WireDecode.fst` is written by hand, independently of the decoder.

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `Relation`

[`Relation.fst`](Relation.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`.

> Relation — questions over a FINITE relation, answered once and totally: `Fuaran.Core.Relation`
> (fuaran-core Phase 552) modelled clause for clause, its specification stated concretely over a
> finite edge list, and what Tarjan's pass is proved to emit — the piece `Propagation.fst` took as
> a PARAMETER until Phase 553 (fuaran-core Phase 553).

**What is modelled.** `src/Fuaran.Core.Relation/Relation.fs` — the PREPARED relation and every
question asked of it:

- the relation itself: `succ_of` (`Relation.successors`), `nodes` (`Relation.nodes`),
`inverse` (`Relation.inverse`);
- the PASS: Tarjan's strongly-connected-components pass (`pass`), written as the recursive
visit production's explicit frame stack replays "exactly — successors in ascending order, a
child's low-link folded into its parent's when the child's frame finishes, a component
emitted when its root's frame does" (its doc comment), so the components, their order and
the order of each one's members are the F#'s, item for item; and what reads it:
`components`, `cycles`, `cycles_among`, `sort` (`Relation.sort`, a `topo`) and `terminals`;
- the CLOSURE: `reachable` (`Relation.reachable`) — the frontier loop `grow`, which
`Propagation.fst` carried as the private `grow` of `dirtyFromChangedIds` until this phase and
now opens from here, as production's `Propagation` delegates both closures to
`Relation.reachable` (Phase 552).

**Proved** (8)

- <a id="row-relation-components-partition"></a>**`relation-components-partition`** — `components_partition` · fuaran-core#553

  The components `Relation.components` emits PARTITION the nodes: no id in two of them, every node of the relation in one, nothing that is not a node, and no component empty — stated of the model's `pass`, Tarjan's algorithm written as the recursive visit production's explicit frame stack replays, over a state of association lists, with its seven-clause invariant (`inv`: the stack and the found components hold no id twice between them; visited is stack-or-found; a visited id is a node; every index is below `next`; a low-link is at most its index; every id on the stack has its low-link at or above the ROOT's index; no found component is empty) carried in the visit's `Pure` postconditions and the root loop proved to leave the stack empty.

  _With:_ `visit`, `roots`, `inv`, `inv_enter`, `inv_set_low`, `inv_pop`, `distinct_move`.

  _Boundary:_ The relation is the PREPARED form — nodes in the list's order with their successor lists; the model sorts nothing — and the differential hands it `Relation.toMap`, so production's ascending visit is the model's visit. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). That a component is a mutual-reachability class is `relation-components-are-sccs`, and that the components come dependencies-first is `relation-components-deps-first` — both proved over the same invariant.

- <a id="row-relation-sort-partition"></a>**`relation-sort-partition`** — `sort_partition` · fuaran-core#553

  `Relation.sort` reads each component once — a singleton with no self-edge into `Order`, every other component into `Cycles` — so `Order` holds no id twice (`sort_order_distinct`) and `Order` with the cycles' members is exactly the node set, each once: the first two clauses of `Propagation.validTopo` as theorems of the pass, and the one premise `Propagation.fst`'s agreement theorems take of the walked order, discharged.

  _With:_ `sort_order_distinct`, `sort_reads_each_mem`, `sort_reads_each_distinct`, `components_partition`.

  _Boundary:_ Over the prepared form, as `relation-components-partition`. The checker's third clause — dependencies-first — is `relation-components-deps-first`, proved.

- <a id="row-relation-reachable-least"></a>**`relation-reachable-least`** — `reachable_least` · fuaran-core#553

  `Relation.reachable` is the LEAST closed superset of its sources: it holds them, every successor of a member is a member (`reachable_closed`), and any set with both properties holds all of it — so a node no source reaches is never in it. The frontier loop is the one `Propagation.fst` carried as the private `grow` of `dirtyFromChangedIds` and `neededFor`, now opened from here as production's `Propagation` delegates both closures to `Relation.reachable`; `dirty_least` and `needed_for_least` are its instances at the dependents map and the dependency map.

  _With:_ `reachable_closed`, `grow`, `grow_measure`, `grow_closed_by`, `grow_within_by`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Membership only: the list the loop answers is in frontier order, and the differential compares it as a set.

- <a id="row-relation-inverse-edge"></a>**`relation-inverse-edge`** — `inverse_edge` · fuaran-core#553

  `Relation.inverse` turns every edge round and adds none: `a` is a successor of `b` in the inverse exactly when `b` is a successor of `a` in the relation, over the nodes; a name that is no node has no successors in either.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). The order of each node's predecessors is the node list's, which the differential compares as a set.

- <a id="row-relation-components-are-sccs"></a>**`relation-components-are-sccs`** — `components_are_sccs` · fuaran-core#556

  TARJAN'S CORRECTNESS, THE CLASS HALF. Every component `Relation.components` emits is a mutual-reachability class: two nodes share a component exactly when each reaches the other by zero edges or more (`mutual`) — the members reach one another (`components_are_sccs`, first clause), whatever a member is mutually reachable with is a member (second clause), and for a node the two readings coincide through the component the pass holds it in (`same_component_iff`). Proved over `visit`'s invariant, strengthened with four clauses — the stack sorted by index (`stack_sorted`); a low-link WITNESS for every id on the stack, an id on the stack at the index its low-link holds that it reaches (`low_wit`); the components found dependencies-first (`comps_ok`); every component found a class (`sccs_ok`) — and a FRAME invariant carried between a frame's successors (`frame_ok`: every id above the frame on the stack is a finished descendant that did not pop, with its low-link at or above the frame's, reachable from the frame, every edge out of it processed; every successor consumed is a processed edge). At a root's pop the segment is exactly the class (`pop_class`): each member reaches the root along its low-link witnesses, which stay at or above the root because the root's low-link is its own index (`climbs`); and whatever the root reaches lies in the segment or in a component found, where nothing reaches the root.

  _With:_ `same_component_iff`, `pop_class`, `climbs`, `pop_edges`, `inv`, `frame_ok`, `above_ok`, `done_ok`, `frame_member`, `low_wit`, `sccs_ok`, `stack_sorted`, `frame_child`, `frame_onstack`, `frame_skip`, `inv_pop`, `inv_set_low`, `inv_enter`, `visit`, `roots`.

  _Boundary:_ Within the model, over the prepared form, and conditional on nothing: reachability is `reachable`, concrete over the edge list, and no theorem takes a parameter. The correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`), which `relation-differential` keeps holding three ways on every drawn relation — the differential guards the extraction and production, not the proof.

- <a id="row-relation-components-deps-first"></a>**`relation-components-deps-first`** — `components_deps_first` · fuaran-core#556

  TARJAN'S CORRECTNESS, THE ORDER HALF. The components come DEPENDENCIES-FIRST: every successor of a member lies in its own component or in one emitted earlier — the checker `deps_first`, which the differential held the pass's order to, as a theorem of the pass (`components_deps_first`). It is clause (j) of the invariant (`comps_ok`), read in emission order (`comps_ok_deps_first`): at a root's pop every edge out of the segment lands in the segment or in a component already found, because a target still on the stack under the root would have pulled the root's low-link under its own index (`pop_edges`). `Propagation.validTopo`'s third clause — every read of an `Order` id the map holds appears earlier, or lies in a cycle — follows for the computed order, so the certificate's three clauses are theorems of `sort`.

  _With:_ `comps_ok_deps_first`, `deps_first_snoc`, `comps_ok`, `comps_ok_closed`, `pop_edges`, `inv_pop`, `visit`, `roots`.

  _Boundary:_ Within the model, over the prepared form, and conditional on nothing: reachability is `reachable`, concrete over the edge list, and no theorem takes a parameter. The correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`), which `relation-differential` keeps holding three ways on every drawn relation — the differential guards the extraction and production, not the proof.

- <a id="row-relation-cycles-among-spec"></a>**`relation-cycles-among-spec`** — `cycles_among_meets_spec` · fuaran-core#556

  `Relation.cyclesAmong` is EXACTLY the nodes that reach themselves by one edge or more — `cycles_among_spec`, the self-reaching nodes by the closure's definition — each once (`cycles_among_meets_spec`): a member of a cycle component is on a cycle, by its self-edge or through a second member it is mutually reachable with (`cycle_member_on_cycle`); and a node on a cycle sits in a cycle component, since a singleton is reached again from itself only through a successor mutually reachable with it, which the class then holds (`on_cycle_component`).

  _With:_ `cycle_member_on_cycle`, `on_cycle_component`, `mem_cyclic_of`, `comp_of`, `reach_split`, `reach_plus_char`, `components_are_sccs`, `components_partition`, `sort_partition`.

  _Boundary:_ As a set, each node once: the list `cyclesAmong` answers is in the pass's pop order and the specification's in node order, and the differential compares the two as sets. Within the model, as `relation-components-are-sccs`.

- <a id="row-relation-terminals-spec"></a>**`relation-terminals-spec`** — `terminals_meets_spec` · fuaran-core#556

  `Relation.terminals` is its chain definition, `terminals_spec`: for every name, the sinks the name reaches, or `None` when it reaches a cycle, a name that is no node ending at itself (`terminals_meets_spec`). The loop over the components in emission order is proved by its invariant (`answer_all_ok`): with every component found so far answered as the specification answers it, the next is too — a cycle component is `None` for each member, every one on a cycle; a singleton with no successors is its own sink; and a singleton with successors joins their ends (`singleton_answer`: `None` exactly when a successor's end is, else the union of the sinks each reaches), every successor of it answered before it, dependencies-first.

  _With:_ `answer_all_ok`, `singleton_answer`, `singleton_none`, `singleton_some`, `join_ends_none`, `join_ends_some`, `bind_all_mem`, `bind_all_other`, `deps_first_prefix`, `reach_sink`, `not_node_no_succ`, `components_deps_first`, `components_are_sccs`.

  _Boundary:_ As a set per answer: the specification lists the sinks in closure order and the pass unions them in component order, and the differential compares each `Some` as a set. Within the model, as `relation-components-are-sccs`.

**Tested** (1)

- <a id="row-relation-differential"></a>**`relation-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#553

  The extracted model agrees with the shipped `Relation.components`, `cycles`, `cyclesAmong`, `sort`, `reachable`, `inverse`, `terminals`, `nodes` and `successors` — the pass's order among components and each component's member order included, so the sort and the components are compared item for item — over 400 seeded random relations (one to fourteen keyed nodes, successors outside the keys, repeated successors, self-edges; the closure from one to three drawn sources, one draw in eight an outside name; every node's chain end and an outside name's), and over the dense diamond (40 layers, 4 wide) with and without its back edge. The bridge is `Relation.toMap` read as ascending keys with ascending successor lists, the form the model's list-order visit is production's ascending visit over. The go-red hands the model a bridge that loses every node's last successor and requires the components to differ, in the agreement's own pass.

  _Boundary:_ Agreement is over the pools drawn, never over all inputs. The 200,000-node chain and ring `RelationTests` hold production to are not drawn here: the model's association lists make it quadratic, and the regression they guard — the explicit frame stack against the call stack — is production's, held by that family.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `Propagation`

[`Propagation.fst`](Propagation.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `Relation`.

> Propagation — incremental evaluation agrees with full evaluation: the compute strand's
> promise, `Fuaran.Core.Propagation`, modelled clause for clause and proved
> (fuaran-core Phase 186; the resolver restricted and the declared-reads premise DISCHARGED by
> Phase 209).

**What is modelled.** `src/Fuaran.Core.Propagation/Propagation.fs` — the two halves the
incremental promise is made of:

- the DIRTY SET: `dependents` (the inverted dependency map), `dirtyFromChangedIds` with its
private frontier loop `grow` (its alias `staleSet` left at `1.0.0`, Phase 391);
- the DRIVER: `PropagationError`, `EvalOutcome`, the private `walk` with its loop `go`, the
reference evaluator `eval`, and the incremental `evalFrom` with its unknown-change guard —
including the RESTRICTED resolver and its `EvalUndeclaredRead` refusal (Phase 209).

TWO things are PARAMETERS rather than clauses, exactly as Phase 135 made `float i` a
parameter, Phase 176 the pipeline evaluator and Phase 177 the witness. The NODE EVALUATOR
(`evalNode : (string -> 'v option) -> string -> Result<'v, string>`) is a function over an
abstract value type: Core owns no evaluator, and nothing here says what a domain computes.
The ORDER the driver walks is COMPUTED, as production computes it (Phase 553): `sort deps`
is `Relation.sort` over the map with its dangling reads removed — `Relation.fst`'s functional
Tarjan pass — read into a `topo_result` as production reads `Relation.Topo` into
`TopoResult`. The one fact about it the agreement theorems need, that `Order` holds no id
twice, is a THEOREM (`sort_distinct`, off `Relation.sort_order_distinct`), so `evalfrom_agrees`
and `evalfromwith_agrees` carry no order premise; the walk over an ARBITRARY order is kept
beside them as `eval_over` and the `_over` family, because that is the stronger statement and
the certificate section (8) is stated over it. Until this phase the order was the model's third
parameter and that fact a ladder row's assumption.
The READS THE EVALUATOR ACTUALLY MAKES are a `read_witness` handed in where production
OBSERVES them: production's resolver is instrumented and records the ids it was asked for, an
effect a `Tot` function cannot have, so the model takes the observation as given exactly as it
takes the order. That parameter is what lets the model carry Phase 209's refusal instead of
modelling a driver production no longer has (`propagation-read-witness`).
`dependencyMap`, `cycleThrough`, `touchedBy` and `dirtyFromOp` are outside the model: they
build a dependency map or a change set from a tree, and the theorems start from both.

**Proved** (14)

- <a id="row-propagation-dirty-sound"></a>**`propagation-dirty-sound`** — `dirty_sound` · fuaran-core#186

  Every id whose input changed is dirty. For any dependency map and any change set, `dirtyFromChangedIds` holds every changed id and the id at the end of every read path out of one (`c <- n1 <- ... <- n`, each id reading the one before it) — read off `dirty_closed`: the set contains the change and is closed under "reads", through the exact inversion `dependents_edge` (an id is among `r`'s dependents exactly when it reads `r`). The frontier loop `grow` is a TOTAL function in the model: its termination measure — the ids of the dependents map not yet accumulated, then the frontier — is checked by the prover (`grow_measure`).

  _Boundary:_ Production's loop is total only by an argument in a doc comment; the model's totality reaches it through the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-propagation-dirty-least"></a>**`propagation-dirty-least`** — `dirty_least` · fuaran-core#186

  The dirty set is MINIMAL, in the only sense a set can be: it is the LEAST set that contains the change and is closed under "reads" — any such set contains all of it. An id that is not reverse-reachable from a change is therefore never dirty, which is the production doc comment's "minimal by construction" as a theorem.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-propagation-evalfrom-agrees"></a>**`propagation-evalfrom-agrees`** — `evalfrom_agrees` · fuaran-core#209

  Incremental evaluation agrees with full evaluation. `evalFrom ev1 prior changed deps` equals `eval ev1 deps` as a whole `Result` — the same values in the same order, the same cyclic groups, and under a failing evaluator the SAME `EvalNodeFailed` at the same node — for any value type, any dependency map and any TOTAL evaluator, one that returns, `Ok` or `Error`, at every node it is handed. No declared-reads premise is needed: the driver builds the resolver FROM the node's declared reads, so two result maps that agree on those reads hand the evaluator the same argument and it answers alike (`lookups_eq`, `resolver_eq`). Agreement holds over ANY duplicate-free order; `evalfrom_agrees_exact` is the reading with no hole.

  _With:_ `evalfrom_agrees_over`, `go_agree`, `sort_distinct`.

  _Boundary:_ Stated at the order `sort` computes: the walk over an ARBITRARY duplicate-free order is `evalfrom_agrees_over`, the stronger statement, which this row's theorem instantiates at `sort deps` with `sort_distinct` discharging the order premise. An evaluator that throws is outside the model, whose evaluator is a total function, and the driver does not catch (`propagation-evaluator-total`). The premises: `prior` holds the values `eval ev0` returned over the same map, or fewer (`prior_of` — a hole is recomputed, as production says); `changed` names every id on which `ev1` differs from `ev0` (`agree_off`) and every id whose READS differ (`touches_off`, the same premise's other half, load-bearing only at a clean node the incremental walk reuses without checking), and every changed id is known; and the walked order holds no id twice. These are `propagation-change-set-and-prior` and `propagation-order-distinct`. NOT among the premises: that the order is a dependency order — a dependency order is what makes the values mean something, not what makes the two paths agree. The duplicate-free premise is load-bearing, measured by deleting it: without it the prover refuses `go_agree` at the step that reads a prior value back, because an id walked twice has its prior written at its first occurrence, where the full walk writes a value computed from fewer resolved reads.

- <a id="row-propagation-evalfromwith-agrees"></a>**`propagation-evalfromwith-agrees`** — `evalfromwith_agrees` · fuaran-core#250

  The agreement theorem, restated for the prior-aware driver. `evalFromWith ev1 prior changed deps` equals `evalWith ev1 deps` as a whole `Result`. The shared walk is a lemma too: `eval_is_walk_with` shows `eval` and `evalFrom` are the prior-aware walk at an evaluator that ignores its prior, so every theorem about them stands unchanged.

  _With:_ `evalfromwith_agrees_over`, `sort_distinct`.

  _Boundary:_ Stated at the order `sort` computes, through `evalfromwith_agrees_over` and `sort_distinct`, as `propagation-evalfrom-agrees`. Under `evalfrom_agrees`' premises taken at the evaluators' PRIOR-BLIND readings (the evaluator handed `None`), with none of them weakened, and exactly one more: `prior_blind_along` — the evaluator's answer does not depend on the prior it is handed, at every node the incremental walk recomputes, under the resolver the walk hands it there. Stated along the walk rather than for every resolver, because an evaluator whose prior carries reuse state keyed to the inputs it was built from is entitled to trust it beside the resolver the walk builds and nowhere else.

- <a id="row-propagation-evalfrom-minimal"></a>**`propagation-evalfrom-minimal`** — `evalfrom_minimal` · fuaran-core#186

  A node outside the dirty set AND present in `prior` is not re-evaluated: `evalFrom` returns the same `Result` under any two evaluators that agree on the dirty ids and on the ids `prior` does not hold — which is what "is not evaluated" means for a pure function without instrumenting one — and the instrumented reading says the same (`invoked_only_stale`: every id `walk_invoked` lists is on the walked order and is dirty or absent from `prior`).

  _With:_ `reuse_is_guard`.

  _Boundary:_ The unqualified statement — "a node outside the dirty set is not re-evaluated" — is FALSE, by production's own contract: an id absent from `prior` is always recomputed, dirty or not, and the differential exhibits it. The model does not transcribe production's guard (`recompute id || not (Map.containsKey id prior)`) and its `Map.find` separately: a partial `find` under a refinement extracts to an incomplete match, so the two are read as ONE total function, `reuse`, and `reuse_is_guard` proves it is `None` exactly when the guard holds. Stated over the walk at an ARBITRARY order (the `_over` family); `eval`, `evalFrom`, `evalFor` and their prior-aware readings are that walk at `sort deps`, so the theorem holds of them by instantiation.

- <a id="row-propagation-unknown-change-refused"></a>**`propagation-unknown-change-refused`** — `evalfrom_unknown_refused` · fuaran-core#186

  A changed id the dependency map does not hold makes `evalFrom` the typed `EvalUnknownChange`, naming exactly the unknown ids (`unknown_exact`: changed, and absent from the map), under every evaluator alike, with nothing evaluated (`walk_invoked` is empty).

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Stated over the walk at an ARBITRARY order (the `_over` family); `eval`, `evalFrom`, `evalFor` and their prior-aware readings are that walk at `sort deps`, so the theorem holds of them by instantiation.

- <a id="row-propagation-needed-least"></a>**`propagation-needed-least`** — `needed_for_least` · fuaran-core#317

  The PULL dual of the dirty set. `neededFor deps targets` — the same frontier loop the dirty set runs, handed the dependency map itself where the dirty set is handed its inverse — holds the targets, is closed under reads (every id a member reads is a member; `needed_closed`), and is the LEAST such set: any set that holds the targets and is closed under reads contains all of it. An id no target reads, directly or through another, is never needed. Both facts are stated over the loop at an ARBITRARY edge map (`closed_by`), so they are `dirty_closed` / `dirty_least` read in the other direction.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-propagation-evalfor-agrees"></a>**`propagation-evalfor-agrees`** — `eval_for_agrees` · fuaran-core#317

  Demand-driven evaluation agrees with full evaluation on what it evaluates. When `eval ev deps` succeeds, `evalFor ev targets deps` succeeds with exactly its values on `neededFor deps targets` and exactly its cyclic groups that meet that set — for any value type, any dependency map, any target set and any evaluator, over ANY order handed to both, assuming nothing about `sort`. The one premise of the step lemma (`go_restrict`) is that the restriction set is closed under reads, which `needed_closed` discharges, so the theorem as stated carries no premise beyond `eval`'s success.

  _Boundary:_ Conditional on `eval`'s success. The converse is deliberately NOT claimed: a node that fails outside the needed set is never reached, so the pull can succeed where the full evaluation fails. Stated over the walk at an ARBITRARY order (the `_over` family); `eval`, `evalFrom`, `evalFor` and their prior-aware readings are that walk at `sort deps`, so the theorem holds of them by instantiation.

- <a id="row-propagation-evalforwith-agrees"></a>**`propagation-evalforwith-agrees`** — `eval_for_with_agrees` · fuaran-core#317

  The same agreement for the prior-aware pair: when `evalWith evw deps` succeeds, `evalForWith evw targets deps` succeeds with its values on the needed set and its cyclic groups that meet it. `evalForWith` hands no prior, so it is the prior-blind reading's demand-driven walk (`prior_blind_empty`, `go_with_blind`) and needs none of `evalfromwith_agrees`' extra premise.

  _Boundary:_ Conditional on `evalWith`'s success; within the model, with the correspondence to production the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Stated over the walk at an ARBITRARY order (the `_over` family); `eval`, `evalFrom`, `evalFor` and their prior-aware readings are that walk at `sort deps`, so the theorem holds of them by instantiation.

- <a id="row-propagation-evalfor-invokes-needed"></a>**`propagation-evalfor-invokes-needed`** — `eval_for_invokes_needed` · fuaran-core#317

  Nothing outside the needed set is evaluated: every id the demand-driven walk hands the evaluator (`walk_for_invoked`, the instrumented reading the differential holds to production's own recorder) is in `neededFor deps targets`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Stated over the walk at an ARBITRARY order (the `_over` family); `eval`, `evalFrom`, `evalFor` and their prior-aware readings are that walk at `sort deps`, so the theorem holds of them by instantiation.

- <a id="row-propagation-sort-computed"></a>**`propagation-sort-computed`** — `sort_distinct` · fuaran-core#553

  THE ORDER BRIDGE, CLOSED. `Propagation.sort` is in the model: `Relation.sort` over the map with its dangling reads removed (`known`), read into `TopoResult` as production reads `Relation.Topo`. Its `Order` holds no id twice (`sort_distinct`, off `Relation.sort_order_distinct`) and `Order` with the cycles' members is exactly the map's keys (`sort_same_set`, off `Relation.sort_partition`) — `validTopo`'s first two clauses as theorems — so the agreement theorems hold at the computed order with no order premise, where the order used to be a parameter and the fact an assumed row (`propagation-order-distinct`, retired).

  _With:_ `sort`, `known`, `sort_same_set`, `nodes_known`.

  _Boundary:_ The checker's third clause — every read of an `Order` id the map holds appears earlier, or lies in a cycle — is dependencies-first, which `Relation.fst` states (`deps_first`) and proves of the pass (`relation-components-deps-first`), so every clause of `validTopo` is a theorem of the computed order; the differential still holds `sort` to `validTopo` on every generated graph, and to the extracted `sort` item for item. That `Relation.ofMap`'s ascending preparation is the list order the differential hands the model is the bridge, sampled there.

- <a id="row-propagation-evaluator-contract"></a>**`propagation-evaluator-contract`** — `undeclared_refused` · fuaran-core#209

  THE EVALUATOR CONTRACT'S FIRST CLAUSE is a property of the driver: `evalfrom_agrees` needs an evaluator that reads other nodes ONLY through the reads its dependency map declares, and the driver enforces it — the resolver `evalNode` is handed is restricted to `deps[id]`, and an evaluation that reads outside it ends with the typed `EvalUndeclaredRead` naming the node and the first id it read outside its declaration, declared LAST in `PropagationError`, since a case's declaration order is its tag number. Proved of the model's identically restricted driver: `undeclared_refused` (a walk whose first node reads undeclared refuses with that node and that read, under every evaluator alike and whatever the reuse policy, given the node is recomputed there), with `eval_refuses_undeclared` and `evalfrom_refuses_undeclared` as the two drivers' readings, and `ok_implies_declared` / `eval_ok_declared` as the sentence that makes the `prior` premise carry the enforcement — a full evaluation that SUCCEEDED checked every node on the order, so there is no successful `eval` of a non-conforming evaluator to take a `prior` from.

  _Boundary:_ `evalFrom` invokes the evaluator only where it recomputes, so a violating node that is clean AND present in `prior` is reused and not re-invoked — which is `evalfrom_minimal` and not a hole, by the sentence above; this is the one thing a reader must not over-read. Restricted to the FIRST node of the order because a violator further along is reached only if the evaluator did not fail before it, which is a fact about the evaluator; the unknown-change refusal, which precedes all evaluation, has no such qualifier. The contract's other two clauses are not discharged here: they are `propagation-change-set-and-prior`. Beside the proof, `Conformance.propagationEvalLaws` samples the refusal on the shipped driver with its leaks BUILT rather than drawn, two per iteration (one reading a real node of the graph, one reading an id the map does not hold), so a short run cannot go vacuous. Stated over the walk at an ARBITRARY order (the `_over` family); `eval`, `evalFrom`, `evalFor` and their prior-aware readings are that walk at `sort deps`, so the theorem holds of them by instantiation.

- <a id="row-propagation-topo-certificate"></a>**`propagation-topo-certificate`** — `valid_topo_distinct` · fuaran-core#308

  An order the certificate accepts holds no id twice. `valid_topo` is `Propagation.validTopo` clause for clause — `Order` and the cycles' members together distinct, holding exactly the ids the map holds, and every read of an `Order` id that the map holds appearing earlier or lying in a cycle — and `valid_topo_distinct` proves that acceptance implies the one premise the agreement theorems take of the walked order. `evalfrom_agrees_certified` and `evalfromwith_agrees_certified` are the two agreement theorems with the order premise replaced by the certificate.

  _Boundary:_ The third clause is what makes the walk's values mean something (an acyclic read is computed before its reader); no theorem needs it, since agreement holds over any duplicate-free order, and it is modelled so the extracted checker is production's. Acceptance does NOT say that `Cycles` are the strongly connected components, or that they are cycles at all: an order that puts every id in one cyclic group passes the checker (clauses 1 and 2 hold and clause 3 is vacuous over an empty `Order`). Nothing is claimed of `sort`'s groups. `sort` is in the model and its first two clauses are theorems of it (`sort_distinct`, `sort_same_set`, row `propagation-sort-computed`); the checker stays for its third, dependencies-first, which `Relation.fst` states and does not prove.

- <a id="row-propagation-evalfrom-certified"></a>**`propagation-evalfrom-certified`** — `evalfrom_agrees_certified` · fuaran-core#308

  `evalfrom_agrees` with its order premise replaced by the certificate: whatever produced the order, if `validTopo` accepts it, `evalFrom` over the dirty set equals `eval`. The order half of `evalFrom`'s contract is then a property of a checker production ships.

  _Boundary:_ Under the change-set and prior premises unchanged. Tarjan's algorithm, which produces the order, is not restated in the model (`propagation-order-distinct`). Stated over `eval_over` / `eval_from_over`, the walk at a given order, which is what a certificate is about; `evalfrom_agrees` is the same theorem at the computed order.

**Tested** (1)

- <a id="row-propagation-differential"></a>**`propagation-differential`** — `Proofs.Oracle`, 5 cases · fuaran-core#209

  The extracted model agrees with the shipped `Propagation.dependents`, `dirtyFromChangedIds`, `eval` and `evalFrom`, and with `neededFor`, `evalFor` and `evalForWith` with the ids `evalFor` evaluates, over a drawn target set of one or two ids (one trial in eight naming an id the map does not hold), with `evalFor` held to `eval` on the needed set on the shipped driver — over generated dependency maps — the acyclic generator of `Conformance.propagationEvalLaws` and `dirtyPropagationLaws`, WIDENED with back edges (cycles), dangling reads, change sets of one to three ids, an unknown id, a failing evaluator and a `prior` with a hole in it — on the dependents map, the dirty set, the whole `Result` of both evaluators, and, per `evalFrom`, the ids the evaluator was handed IN ORDER (production's own recorder against the model's `walk_invoked`). Every generated graph holds production's `sort` to the extracted `sort`, `Order` and `Cycles` item for item, and every trial whose change is known holds the shipped `evalFrom` to the shipped `eval`. Every model call carries the `read_witness` the restricted resolver is checked against, derived from the SAME map the toy evaluator folds over so the two cannot drift; the go-red hands the model a deps bridge that loses a read edge and requires it to lose on the dirty set and on `evalFrom`'s result, and because the witness still names the read the bridge dropped, the model refuses it as undeclared where production does not. A third case instantiates the theorems on the shipped driver as the regression test for the undeclared-read refusal: an evaluator reading a node it did not declare, with both drivers refusing the same node and read, the model agreeing in the same words, and the one place the refusal is not observable (a violating node clean and present in `prior`) asserted rather than assumed. Two more cases sit beside it: the certificate case (production's `sort` passes production's `validTopo` on every generated graph, and the extracted `valid_topo` agrees with production's on the sorted order and on four perturbations of it) and the prior-aware case (`evalWith` and `evalFromWith` are the extracted `eval_with` and `eval_from_with` over generated maps and a prior-consulting evaluator, and `evalFromWith` over `evalWith`'s prior equals `evalWith` — the bridge `evalfromwith_agrees` needs).

  _Boundary:_ Agreement is over the pools drawn, never over all inputs. Each widening carries an adequacy guard: cyclic graphs, dangling reads, dirty and clean nodes, reuses, clean ids recomputed for a hole, evaluator failures, unknown changes and partial target sets must each arise, and the same seed must reproduce the tally, so a run that draws none of them fails rather than passing vacuously.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`propagation-change-set-and-prior`](#row-propagation-change-set-and-prior) (assumed), [`propagation-evaluator-total`](#row-propagation-evaluator-total) (assumed).

### `Capability`

[`Capability.fst`](Capability.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `Relation`, `Propagation`.

> Capability — default-deny dispatch and the three function laws as theorems: the seam every
> AI-driven edit crosses, `Fuaran.Core.Function`, modelled clause for clause and proved
> (fuaran-core Phase 177).

**What is modelled.** `src/Fuaran.Core.Function/Function.fs` — three surfaces of one file:

- the EFFECT LATTICE (`Effect.join` / `Effect.covers` over a host chain beside a determinism SET,
the set written as its characteristic vector since Phase 319), the VALUE SPACES
(`Space.validate` / `Space.isBounded`) and the hole
vocabulary (`HoleKind` / `HoleDecl` / `SigEntry` / `Signature` / `Arg` / `ApplyError`);
- the FUNCTION ALGEBRA over the domain-witness record: `Function.signature`,
`signatureExcluding`, `isTotal`, the private `guardTotal` / `validateArg` / `bindArgs` and
the `apply` / `curry` that are its two faces, `compose`, `composedEffect`, `observedEffect`
and `auditEffect`;
- the CAPABILITY SEAM: `Capability.create` / `validateArgs` / `invoke`, `CapabilityRegistry.empty` /
`register` / `tryFind` / `enumerate` / `dispatch`, the seven-case `InvokeError`, and — since
Phase 210 — the `Deferred<'T>` envelope the host body answers in. The envelope's three cases
are modelled; its COMBINATORS (`Deferred.map` / `bind` / `toResult` / `tryValue`) are not,
being outside the seam.

And since Phase 354, three sections the admission work of Phase 307 left unmodelled:

- the HANDLER TABLE (section 14): `Function.bindHandlers` — the behaviour-axis binding of a
host's handlers to an artifact's action holes — with the `Map` its table is;
- the CAPABILITY PIPELINE (section 15): `CapabilityPipeline.typeCheck` with its cycle search,
`eval`, `dependencyMap` and `evalFrom` over an abstract host body, from
`src/Fuaran.Core.Function/CapabilityPipeline.fs`;
- the CODECS (section 16): `CapabilityCodec` for a signature and a capability and the
pipeline's codec for a node, with `SpaceCodec` and `EffectCodec`, at the `JVal`.

Two things are PARAMETERS rather than clauses, exactly as Phase 135 made `float i` a parameter
and Phase 176 made the pipeline evaluator one. The WITNESS (`ArtifactWitness`'s `Holes`,
`Effect`, `Bind`, the tree witness's `KindTag`, and `Tree.preorder` over it) is a record of
functions over an abstract `node` type: the algebra reads holes off it and hands bindings back
to it, and nothing here says what a domain's `Bind` does. And the three SCALAR READERS the
value-space check needs — `System.Int32.TryParse`, `System.Double.TryParse` against a float
range, and `String.Length` — are a `readers` record: the space vocabulary is modelled, the
lexical parsers behind two of its five constructors are not, and a float range's bounds cross
as opaque carriers. `invocationKey` entered the model with Phase 225 (section 12), over a
`key_renderers` record — `Hash.fnv1a`, the address comparator and `Hash.canonicalField` — for
the same reason. The Phase 354 sections add three more of the same kind and no other: the
ordinal comparator an F# `Map<string, _>` keeps its keys by (`le`, under `total_order` where a
theorem spends it), the two float comparisons `Space.subsumes` makes (`feed_readers`), and the
one float read the codecs make (`codec_readers`). The pipeline's host body and the value it
spells (`node_body`, `spell`) are parameters as `invoke`'s body is; a handler is an abstract
type.

**Proved** (31)

- <a id="row-capability-unregistered-refused"></a>**`capability-unregistered-refused`** — `unregistered_refused` · fuaran-core#177

  `CapabilityRegistry.dispatch` of an id the registry does not hold is the typed refusal `NoSuchCapability id known`, naming the id and every id the registry does hold — and the result is the SAME under every host body, which is what "runs no handler" means for a pure function without instrumenting one. `invoke` can never produce the registry's two refusals (`invoke_never_registry_refusal`), so the classes stay disjoint: a `NoSuchCapability` is always the registry's answer and never a body's.

  _Boundary:_ "Runs no handler" is stated as result-independence from the body, not by instrumenting one. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-policy-gate-before-body"></a>**`policy-gate-before-body`** — `gate_before_body` · fuaran-core#318

  THE POLICY GATE RUNS BEFORE THE BODY AND NAMES ITSELF. Over a registry carrying a list of named gates — the model of `CapabilityRegistry.withGate` / `dispatch`: resolve the id, validate the arguments, take the join of every gate's decision, and run the body only under `Allow` — an invocation of a held id whose arguments validate and whose gates' join is not `Allow` is refused `ApprovalRequired n` (a `NeedsApproval`) or `PolicyRefused n reason allowed` (a `Deny`), the SAME under every body, so no body ran; and `n` names a gate of the list whose own decision is exactly the one the refusal reports (`gate_before_body`, with `decide_named_names_a_gate`). With no gate the gated dispatch is the seam's ungated `dispatch`, its refusals wrapped (`no_gate_is_dispatch`), so the gate is additive over the ungated capability theorems. Proved with no assume and no admit.

  _Boundary:_ The model wraps the ungated refusals in its own `gated_error` rather than widening the extracted `invoke_error`; production adds the two cases to `InvokeError`. Everything in the section is `noextract_to "FSharp"`, so the extracted oracle is unchanged and the bridge to production is the `Conformance.policyLaws` family, which samples the same refusals, the observer and the untouched body on all three registries. NOT claimed: that no effect is CAPTURED for a refused invocation — capture is not in this model; `Conformance.keyedCaptureLaws` samples it.

- <a id="row-policy-join-monotone"></a>**`policy-join-monotone`** — `policy_join_monotone` · fuaran-core#318

  THE POLICY JOIN IS MONOTONE. `PolicyDecision.join` is the maximum in the order `Allow < NeedsApproval < Deny` (`join_is_max`), and raising either argument never lowers it (`policy_join_monotone`); the gates' fold only ever rises along its list (`decide_from_rises`), which is why `withGate` can tighten a registry and never loosen it. Proved with no assume and no admit.

  _Boundary:_ Over the model's `decision`, whose ranks are production's `PolicyDecision.rank`; the bridge to production is `Conformance.policyLaws`' lattice law, which samples it.

- <a id="row-capability-validate-before-invoke"></a>**`capability-validate-before-invoke`** — `validate_before_invoke` · fuaran-core#177

  An argument set `validateArgs` rejects makes `invoke` return that rejection, for every body alike — no body ran — and, at the registry, a dispatch that resolved its id still runs nothing on a set validation rejects (`dispatch_validates_first`). The converse holds too: an accepted result or a `BodyFailed` is reachable only past an accepted validation (`body_runs_only_validated`). What validation says is characterised beside it — the refusal is one of its own five classes (`validate_args_shape`), an accepted set addresses declared holes only and binds every required one (`validate_args_sound`), and a refusal is truthful: an `ArgOutOfSpace` names a value the named space really refuses, an `UnknownArg` an address no entry declares, an `UninvocableArg` an entry with no value space (`refusal_is_truthful`).

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). The refusal classes `validate_args_shape` admits include `DuplicateArg`, checked before the other four. An `UninvocableArg` may also name a slot entry, whose space is a tree space, bound to an argument that is no tree (`refusal_is_truthful`, `slot_scalar_uninvocable`).

- <a id="row-capability-envelope-three-outcomes"></a>**`capability-envelope-three-outcomes`** — `invoke_never_ok_failed` · fuaran-core#210

  THE ENVELOPE HAS NO FOURTH OUTCOME. Since the body answers in `Deferred<'v>`, an invocation past an accepted validation carries the body's `Ready` or `Pending` and nothing else: a body's `Failed m` crosses into the enumerated `BodyFailed m` at the seam, so `Ok (Failed _)` is unreachable rather than merely unproduced, for every body and every argument set alike (`invoke_never_ok_failed`), and through the registry too, where a host actually reaches the seam (`dispatch_never_ok_failed`). That is what makes the async axis and the typed-error axis independent: the envelope carries "not yet", the outer `outcome` carries every refusal, and the one shape that would blur them cannot be constructed. `capabilityLaws` samples the same claim on the shipped seam; this is it over all bodies.

  _Boundary:_ The envelope's COMBINATORS (`Deferred.map` / `bind` / `toResult` / `tryValue`) are outside the model and stay `deferredLaws`'s. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-capability-enumerate-is-registry"></a>**`capability-enumerate-is-registry`** — `enumerate_is_registry` · fuaran-core#177

  An id is in `CapabilityRegistry.enumerate`'s output exactly when `CapabilityRegistry.tryFind` resolves it, and `dispatch` raises `NoSuchCapability` exactly off that enumeration (`no_such_iff_unregistered`) — no hidden entry, no phantom one. A resolved id dispatches to ITS capability's `invoke` (`registered_dispatches`); `register` refuses a held id (`register_refuses_duplicate`) and extends a TOTAL capability by exactly one entry otherwise (`register_extends`); and a registry built by `register` from `empty` holds distinct ids (`register_keeps_distinct`).

  _Boundary:_ `register_extends` covers a total capability only; a non-total one is refused (`capability-register-total`). Membership, not order: production's `Map` sorts the enumeration by id and the model holds the map as a list, so the sorted order is `capabilityLaws`'s to certify and is outside the theorem. Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). The `FunctionRegistry` and `ContentPack` surfaces are not modelled, and nothing here covers them.

- <a id="row-capability-slot-hole-invocable-in-space"></a>**`capability-slot-hole-invocable-in-space`** — `slot_hole_invocable_in_space` · fuaran-core#229

  A capability over a slotted artifact is INVOCABLE. `Function.signature` enters every `SlotHole c` required and WITH the tree space `SlotTree c` (`slot_entry_shape`) — a wire document whose `"kind"` satisfies the constraint (any kind when unconstrained; `slot_space_exact`), read through the `kind_of` reader (production's `Space.slotKindOf`). An argument set binding the slot to a conforming tree, with every other argument in its entry's space and every required entry bound, is ACCEPTED (`slot_hole_invocable_in_space`, over the completeness lemma `validate_args_complete`, the converse of `validate_args_sound`); a tree of the wrong kind is refused BY NAME as `ArgOutOfSpace addr (SlotTree (Some k)) v` (`slot_wrong_kind_refused`) and an argument that is no tree as `UninvocableArg addr` (`slot_scalar_uninvocable`). About the one shape `signature` does not make: a REQUIRED entry with NO space refuses every argument list (`spaceless_required_uninvocable`). Asserted on the shipped seam by the differential's fourth case, which dispatches the whole template with a conforming slot and refuses the non-conforming ones by name, and built each iteration by `capabilityLaws`' eighth law.

  _Boundary:_ What is NOT claimed: that the host's decode of the document into its node succeeds — Core checks shape and kind only, per the witness pattern. An entry is its hole kind, so the required spaceless entry `spaceless_required_uninvocable` speaks of can only be a hand-built ACTION entry marked required; `Function.signature` never makes one.

- <a id="row-capability-invocation-key-injective"></a>**`capability-invocation-key-injective`** — `invocation_key_injective` · fuaran-core#225

  `Capability.invocationKey`'s pre-image is INJECTIVE: two argument lists whose address-sorted canonical strings agree ARE the same sorted list and hold exactly the same bindings (`invocation_key_injective`) — over the KEYED list (`keyed`), each argument in its canonical spelling in its hole's space (`Space.canonical`, the `k_canonical` renderer), so `05` and `5` for an integer hole are one key — so distinct argument sets have distinct pre-images whatever their values contain (`[a = "1\u0001b=2"]` and `[a = "1"; b = "2"]` key apart on production), with no premise about the address comparator. The pre-image is `Hash.canonicalFields` over two fields per binding (address, value) — the canonicaliser `Query.invocationKey` also builds through, so the two seams cannot drift apart.

  _With:_ `esc_split`, `enc_injective`, `key_fields_injective`, `binding_fields_injective`.

  _Boundary:_ What it spends is named in `key_premises` — the symbol reading of a string and the escaper premise (`capability-key-renderers-abstract`); no numeral layout is among them, because a capability argument is already a string. Whether two distinct pre-images HASH apart is a claim about FNV-1a and is not made. `CapabilityPipeline.nodeInvocationKey`, the pipeline's capture key, is not modelled: that its two pre-Phase-225 collision exhibits now key apart is asserted on the shipped seam by the closure case, sampled, not proved.

- <a id="row-function-totality-law"></a>**`function-totality-law`** — `totality_law` · fuaran-core#177

  Law 1, totality. `isTotal` over the derived signature answers exactly the guard `apply` and `curry` run first: when some data hole is an unbounded repeat both refuse with `NonTotal` at the FIRST such hole's address (`guard_total_shape`), and the refusal is the same under ANY `Bind` whatsoever (`rejected_never_bound`) — the witness was never asked to bind, which is "rejected, never run" made precise. Action holes are total and their filtering off the data axis changes nothing (`data_total`).

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-function-hygiene-law"></a>**`function-hygiene-law`** — `hygiene_law` · fuaran-core#177

  Law 2, hygiene. Hole NAMES are inert: renaming every hole of a witness leaves `apply`, `curry` and `compose` unchanged, because the walk keys on the absolute address and reads nothing else (`walk_rename`). An argument at an address no data hole declares is refused as `UnknownHoleAddr`, naming the address and the declared set, before any binding — the same under any `Bind` (`undeclared_address_refused`). And no capture: a single binding at address `k` lands at the hole declared at `k` and nowhere else (`walk_single`), so binding one of two same-named holes through `curry` is the same on a witness that declares the other and on one that does not (`same_name_no_capture`). `signatureExcluding` removes exactly the bound addresses and keeps the effect (`signature_excluding_exact`); `validateArg`'s defensive `ActionHole` arm is unreachable from the walk (`data_holes_all_data`).

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Nothing here says what a domain's `Bind` does (that a bound hole is cleared, that a slot's inner tree is where `compose` put it): that is the witness contract (`lawful-abstract-witness`), sampled at a domain by `compositionLaws` and `functionVerifyLaws`. `Function.applyMemo` is not modelled.

- <a id="row-function-effect-law"></a>**`function-effect-law`** — `effect_law` · fuaran-core#177

  Law 3, the effect signature. `composedEffect` is `Effect.join`, and the join is the LEAST class covering both parts — a class covers the join exactly when it covers each part (`join_least`), so any class a composition is assigned that is below either part fails `covers`. With `join_comm`, `join_assoc`, `join_idem` and `join_pure` the effect classes under `join` are a bounded join-semilattice with `pureDeterministic` at the bottom, and `covers` is the order (`covers_refl` / `covers_trans` / `covers_antisym`). The determinism axis is a SET of factors joined by union (written as its characteristic vector), so its half of every one of those laws is pointwise and needs no table.

  _Boundary:_ The host half of those laws rests on the host rank table being inverse on the ranks it produces (`host_of_rank`). Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-function-audit-effect-join"></a>**`function-audit-effect-join`** — `audit_effect_join` · fuaran-core#177

  The effect law carried to the audit. `observedEffect` is the least class covering every node on the preorder walk (`observed_least`, by `fold_covers` over the join-fold), so it covers each node's declared class; an `Ok` audit says the root's declaration covers every descendant's; an `Error` audit is `(declared root, observed)` and exhibits a node under the root the declaration is narrower than. A composition's effect class is the join of its parts and never lower, at the declaration (`effect_law`) and at the walk (this).

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-function-determinism-label-bijection"></a>**`function-determinism-label-bijection`** — `det_tag_injective` · fuaran-core#319

  The determinism axis is a SET of factors, and its canonical wire label is a BIJECTION with the eight sets over the closed three-factor alphabet: a set has exactly one label (`determinism_tag`, the fixed order clock, random, network joined by `+`), the label inverts to exactly that set (`det_tag_roundtrip`), a label names a set only if it is that set's canonical label (`det_tag_canonical`), and two sets with one label are one set (`det_tag_injective`). So the capture key cannot confuse two determinism classes and the capability codec's cross-check of the wire tag against the signature compares like with like.

  _Boundary:_ The set is modelled as its characteristic vector, bridged to the F# `Set` by the differential host factor by factor; the alphabet is the closed three-factor one.

- <a id="row-capability-capture-records-declared"></a>**`capability-capture-records-declared`** — `capture_records_declared` · fuaran-core#319

  The capture seam records the WHOLE declared determinism class. The label a capture journals is `Capability.determinismTag`, and it decodes back to exactly the capability's declared set (`capture_records_declared`); a body whose exercised factors lie inside that set is covered by what the capture records, and a body exercising a factor outside it is NOT (`capture_covers_exercised`).

  _Boundary:_ The body's own reads are a parameter of the statement — the theorem is about what the record says, and `capabilityLaws` samples it on the shipped seam against a body that notes the factors it reads.

- <a id="row-capability-register-total"></a>**`capability-register-total`** — `register_refuses_non_total` · fuaran-core#295

  A registry admits only TOTAL capabilities, and the signature and the artifact protocol agree on what a repeat requires. `CapabilityRegistry.register` refuses a fresh id whose signature is not total as `NonTotalCapability`, naming a non-empty list of its non-total entries (`register_refuses_non_total`), and every registration it accepts is of a total signature (`register_admits_total`), so `dispatch` never reaches a body over an unbounded repeat. `isTotal` reads each entry through its hole kind: a repeat over a bounded count is total, every other hole kind is, and an entry that projects to no hole kind is not (`entry_total`; `non_total_addrs_empty` ties the named list to the verdict). `Function.signature` enters a bounded repeat `Required` (`entry_of`), exactly as strict `apply` demands it; `compose` checks totality on both parts first, as `composeAcross` does, and its hygiene theorem survives the guard (`compose_rename`).

  _Boundary:_ Within the model; its correspondence to production is sampled by the model's twins and by `capability-differential` over generated registries, never over all registries.

- <a id="row-capability-wf-holes"></a>**`capability-wf-holes`** — `wf_holes` · fuaran-core#307

  `wf_holes`: over an artifact whose holes have DISTINCT addresses — what `Signature.validate` enforces at every admission (`DuplicateHoleAddr`) — the data holes' addresses are distinct, so `Function.toJsonSchema`'s property keys are, and an invocation `Capability.validateArgs` ACCEPTS over the derived signature, lifted to `apply`'s arguments (a slot's value decoded into a tree, every other value a scalar), never meets `NotASlot`, `RequiredHolesUnbound` or `UnknownHoleAddr` in `Function.apply`: the capability seam and the artifact protocol agree on which arguments an artifact takes. The walk lemma is `walk_wf`.

  _Boundary:_ Requires the holes' addresses distinct. What `apply` may still answer concerns the VALUES and the witness (`ValueOutOfSpace`, `SlotKindMismatch`, `NonTotal`, `SlotArgOpen`, `BindFailed`); that a hole beneath a slot surfaces as the witness's `BindFailed` is the witness contract's, which is why `Function.validate` refuses it (`HoleUnderSlot`) rather than this theorem ruling it out.

- <a id="row-capability-invocation-key-deterministic"></a>**`capability-invocation-key-deterministic`** — `accepted_invocation_key_deterministic` · fuaran-core#307

  `Capability.invocationKey` is a function of the capability and the BINDINGS: two argument lists binding the same distinct addresses to the same values, in any order, key identically (`invocation_key_deterministic`, over `sorted_unique_b` and the fact that the canonical spelling keeps the address and so commutes with the address sort, `sort_map_spell`), and two ACCEPTED invocations with the same bindings key identically with no hypothesis left (`accepted_invocation_key_deterministic`), because `validateArgs` refuses a repeated address as `DuplicateArg` before any value is read (`validate_args_distinct`).

  _Boundary:_ The unaccepted form needs the addresses distinct and the address comparator a total order; distinctness is discharged only for invocations `validateArgs` accepts.

- <a id="row-capability-register-well-formed"></a>**`capability-register-well-formed`** — `register_refuses_ill_formed` · fuaran-core#307

  A registry admits only WELL-FORMED capabilities. After the totality check, `CapabilityRegistry.register` refuses a fresh, total capability whose signature `Signature.validate` refuses as `IllFormedCapability` carrying exactly that fault (`register_refuses_ill_formed`), and every capability it admits has a signature the check accepts (`register_admits_well_formed`): distinct addresses, and spaces that admit a value (`EmptySpace` otherwise) with finite bounds (`NonFiniteBound` otherwise). The check reads addresses and spaces and never a name, so the hygiene law survives it, and `compose`, which runs `Function.validate` over the tree it builds (`IllFormedResult`), is still name-blind (`compose_rename`, through `validate_decl_rename`). Totality reads a repeat's count through `is_count` — a capped, non-empty, non-negative integer range — so a repeat over `FloatRange(0, infinity)` is not total.

  _Boundary:_ Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-capability-bind-handlers-exact"></a>**`capability-bind-handlers-exact`** — `bind_handlers_exact` · fuaran-core#354

  `bind_handlers_exact`: `Function.bindHandlers` accepts a handler map exactly when every key is a declared action hole's address and every declared action hole has a handler whose declared effect its ceiling covers, and the table it hands back is the handlers given, unchanged (`bind_handlers_complete` is the converse); each refusal says what is refused and why, in the order the checks run — the first key, in key order, that is no action hole's address (`NotAnActionHole` when it is a declared hole of another kind, `UnknownActionAddr` naming every declared action when it is no hole at all), then the first action hole, in declaration order, whose handler exceeds its ceiling (`HandlerEffectExceedsCeiling`, naming both effects), then exactly the action addresses left without a handler (`RequiredActionsUnbound`).

  _Boundary:_ Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`). A handler is an abstract type: its MESSAGE typing lives in the host language and is not modelled.

- <a id="row-capability-bind-handlers-order-independent"></a>**`capability-bind-handlers-order-independent`** — `bind_handlers_order_independent` · fuaran-core#354

  `bind_handlers_order_independent`: two arrivals of the same handlers — distinct addresses, the same bindings, in any order — bind identically through `Map.ofList`, the same table when accepted and the same refusal when not (`map_of_list_order_independent`, over `sorted_unique_k`).

  _Boundary:_ Given only that the map's key comparator is a total order, and only for distinct addresses: with a repeated address the later binding replaces the earlier, which is why distinctness is the hypothesis.

- <a id="row-capability-pipeline-typecheck-topological"></a>**`capability-pipeline-typecheck-topological`** — `typecheck_topological` · fuaran-core#354

  `typecheck_topological`: a pipeline `CapabilityPipeline.typeCheck` accepts has distinct node ids and every `FromNode` edge naming a node declared strictly earlier — its declaration order is a topological order of its edges, so it is acyclic and each upstream runs before the node it feeds (`typecheck_ordered`) — and of two declarations of the same nodes, each in a topological order, the check accepts both or refuses both (`typecheck_order_independent`): the verdict is a fact about the node set, never about which topological order was written. The cycle search is modelled with its visited set and terminates by a checked measure.

  _Boundary:_ The check walks DECLARATION order and refuses one that is not topological (`PipelineCycle`, `PipelineForwardEdge`); it does not search for an order, so order independence holds only between declarations that are each topological.

- <a id="row-capability-pipeline-illtyped-iff-refused"></a>**`capability-pipeline-illtyped-iff-refused`** — `pipeline_illtyped_iff_refused` · fuaran-core#354

  `CapabilityPipeline.eval` and `evalFrom` answer `EvalIllTyped e` exactly when `typeCheck` refuses with `e` (`pipeline_illtyped_iff_refused`). A refused pipeline is answered with the check's own refusal under every host body, prior evaluation and change set alike, so no body runs (`pipeline_refused_never_evaluated`); over an accepted one neither evaluator answers `EvalIllTyped` — the two arms `runNode` marks unreachable are unreachable — and what remains is a result, a body's failure (`EvalNodeFailed`) or an upstream value outside the space of the hole it feeds (`EvalArgRefused`).

  _Boundary:_ Within the model, under the total-body premise (`capability-pipeline-body-total`); the correspondence to production is the differential's (`capability-sections-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-capability-pipeline-evalfrom-agrees"></a>**`capability-pipeline-evalfrom-agrees`** — `pipeline_evalfrom_agrees` · fuaran-core#354

  Where `prior` is what `CapabilityPipeline.eval` answered under one host body and a second body agrees with it at every node outside `changed`, `evalFrom` under the second body from `prior` and `changed` answers exactly what a full `eval` under it answers — the same result map, so at every node the value the whole evaluation assigns it, and the same refusal when a re-run node fails (`pipeline_evalfrom_agrees`). It rests on the dirty set containing the change and being closed downstream — `Propagation.fst`'s `dirty_closed` read at the pipeline's own edges (rows `propagation-dirty-sound` and `propagation-dirty-least`), not a second model of the closure: the Capability model opens `Propagation` and instantiates its closure at `deps_of`, the model of `CapabilityPipeline.dependencyMap` (`capability-pipeline-dirty-instance`).

  _Boundary:_ It assumes nothing about the pipeline beyond the prior evaluation having succeeded, but it needs the agreement hypothesis: without it the claim is false, since a body that moves at a node the caller did not name is a stale reuse. The body is also taken to be a function of the node and its arguments (`capability-pipeline-body-total`).

- <a id="row-capability-pipeline-dirty-instance"></a>**`capability-pipeline-dirty-instance`** — `pipeline_dirty_least` · fuaran-core#544

  The pipeline's dirty set is `Propagation.fst`'s closure INSTANTIATED at the pipeline's `FromNode` edges, not a second model of it. `deps_of`, the model of `CapabilityPipeline.dependencyMap`, names exactly the pipeline's edges: `n` reads `r` in the map exactly when some node carrying id `n` names `r` by a `FromNode` edge (`pipeline_edge_iff`). Over that map `Propagation.dirty_sound` and `dirty_least` are read at the pipeline (`pipeline_dirty_sound`, `pipeline_dirty_least`): a changed id and every node at the end of a `FromNode` read path out of one is dirty, and the dirty set is the LEAST set that holds the change and is closed under the pipeline's edges — so a node no change reaches is never re-invoked. `pipeline_evalfrom_agrees` rests on the same instance (rows `propagation-dirty-sound`, `propagation-dirty-least`).

  _Boundary:_ Within the model; the correspondence of `deps_of` to `dependencyMap`, and of the closure over each to production's, is the Capability differential's (the edge relation of the two maps, then `Propagation.dirtyFromChangedIds` over production's map against the extracted closure over the model's) and the extractor's (premise `extractor-and-compiler-trusted`). Production unions a repeated id's edges into one entry where the model keeps one entry per node; the edge relation is the same and `typeCheck` refuses a repeated id before an evaluator consults either.

- <a id="row-capability-codec-signature-roundtrip"></a>**`capability-codec-signature-roundtrip`** — `signature_roundtrip` · fuaran-core#354

  `CapabilityCodec.signatureOf` after the signature writer is characterised exactly, for every signature, at the `JVal` (`signature_roundtrip`): a signature `Signature.validate` refuses is refused `OutOfRange` at `holes`, and every other reads back as itself. An entry IS its hole kind, so the entry of no hole kind and the spaceless slot cannot be built, and the reader refuses a tag without the member its kind needs, `MissingField` at `space` or `actionEffect`. On a well-formed signature the round trip is the identity (`signature_roundtrip_identity`), and every document the reader accepts is a well-formed signature that reads back as itself (`signature_decoded_wf`). A value space and an effect class read back as themselves with no condition (`space_roundtrip`, `effect_roundtrip`).

  _Boundary:_ The identity holds only on well-formed signatures; a signature `Signature.validate` refuses is refused, not round-tripped. At the `JVal` only: the bytes on either side and the codecs' float read are outside the model (`capability-sections-parameters-abstract`).

- <a id="row-capability-codec-capability-roundtrip"></a>**`capability-codec-capability-roundtrip`** — `capability_roundtrip` · fuaran-core#354

  `CapabilityCodec.decodeJson` after `encodeJson` is characterised exactly, for every capability, at the `JVal` (`capability_roundtrip`): the signature's refusal under `signature` where `signature_roundtrip` refuses it, `OutOfRange` at `signature.holes` where the signature read back is not total (the reader runs the registries' whole admission gate, which `register` answers `NonTotalCapability`), `OutOfRange` at `determinism` where the label written disagrees with the one the signature's effect derives, otherwise the capability with the signature it read back. On a well-formed capability the round trip is the identity (`capability_roundtrip_identity`), and every document the reader accepts is a well-formed capability that reads back as itself (`capability_decoded_wf`).

  _Boundary:_ This is the true form of "the reader refuses what the writer never produces", not that form: the reader is lenient, so it accepts documents the writer never wrote, and what it never does is yield a value the writer could not have written. The identity holds only on well-formed capabilities, and only at the `JVal` (`capability-sections-parameters-abstract`).

- <a id="row-capability-codec-node-roundtrip"></a>**`capability-codec-node-roundtrip`** — `node_roundtrip` · fuaran-core#354

  The pipeline codec's node reader after its writer is characterised exactly, for every `PipelineNode`, at the `JVal` (`node_roundtrip`): a node whose output space `Space.wellFormed` refuses is refused `OutOfRange` at `outputType`, and every other node reads back as itself, its arguments in order, each edge and each literal; a whole pipeline is refused at its first such node, naming it, and is otherwise the identity (`pipeline_roundtrip`), and every document the node reader accepts carries a well-formed output space and reads back as itself (`node_decoded_wf`).

  _Boundary:_ A node or pipeline with an ill-formed output space is refused, not round-tripped. At the `JVal` only: the bytes on either side and the codecs' float read are outside the model (`capability-sections-parameters-abstract`).

- <a id="row-capability-codec-invocation-roundtrip"></a>**`capability-codec-invocation-roundtrip`** — `invocation_roundtrip` · fuaran-core#455

  `CapabilityCodec`'s invocation reader after its writer is characterised exactly, for every capability id and argument list, at the `JVal` (`invocation_roundtrip`): an argument list binding an address twice is refused `OutOfRange` at `args[i]`, `i` the second binding's position (the binding `validateArgs` refuses `DuplicateArg`), and every other reads back as itself, its arguments in wire order; the round trip is the identity exactly on the lists with no repeated address (`invocation_roundtrip_identity`, joined to `validateArgs`' own `repeated` by `first_repeat_none`). The codec is written over `JVal` with the typed decode layer's combinators, not built from the `Codec` blocks, so this is proved directly.

  _With:_ `inv_args_roundtrip`.

  _Boundary:_ The composed round trip of the `Codec` blocks does not apply to this codec. The lenient reader reads no `$type`; the strict policy's members check is not modelled.

- <a id="row-capability-codec-deferred-roundtrip"></a>**`capability-codec-deferred-roundtrip`** — `deferred_roundtrip` · fuaran-core#455

  The async envelope's reader after its writer reads back exactly, for every `Deferred<'T>` — `pending`, `ready` and `failed` — over any payload codec that reads back what it writes (`deferred_roundtrip`); `CapabilityCodec.decodeDeferred`, whose caller's reader answers a sentence, is the same envelope with that reader's refusal raised `OutOfRange` at `value` (`deferred_of_roundtrip`). The typed envelope reader is `CapabilitySeamCodec.decodeDeferred`, which the query seam's async result reads through.

  _Boundary:_ Conditional on the payload codec reading back what it writes; through `CapabilityCodec.decodeDeferred` the payload reader's refusal is a sentence, raised `OutOfRange` at `value`. Within the model, at the `JVal`.

- <a id="row-capability-codec-invoke-error-roundtrip"></a>**`capability-codec-invoke-error-roundtrip`** — `invoke_error_roundtrip` · fuaran-core#455

  `CapabilitySeamCodec.decodeInvokeError` after `encodeInvokeError` (the codec derived from the union's IDL declaration) reads back exactly, for every `InvokeError` — all twelve cases, as the model's `gated_error` (section 17's ten-case `invoke_error` under `GRefused`, and the policy gate's two), with a value space and a declaration fault (`encodeDeclarationFault` / `decodeDeclarationFault`, `fault_roundtrip`) inside them: every refusal reads back as itself (`invoke_error_roundtrip`).

  _Boundary:_ Within the model, at the `JVal`; the correspondence to production's reader is the differential's (`capability-seam-codec-differential`), which compares this string-answering reader on acceptance and value alone.

- <a id="row-capability-codec-refusals-resolve"></a>**`capability-codec-refusals-resolve`** — `invocation_refusals_resolve` · fuaran-core#455

  Every refusal `CapabilityCodec`'s readers answer over a document RESOLVES in it (`DecodeError.resolvesIn`, restated over the model's `jval` as `resolves_in`) and is not `InvalidJson` (`invocation_refusals_resolve`) — the invocation reader (the repeated-address refusal included, walked through `at`), the envelope reader for EVERY payload reader a caller hands it (`deferred_refusals_resolve`), the invoke-error reader (`invoke_error_refusals_resolve`) and the declaration reader (`capability_refusals_resolve`, the admission refusals at `signature.holes` and `determinism` and an entry's missing kind member included).

  _Boundary:_ Proved from one soundness lemma per decode-layer combinator and the transport lemma `ok_at_under`, `DecodeLayer.fst`'s section 6 restated at this module's types. Within the model: a refusal is its code and its path, its sentence not modelled (`capability-sections-parameters-abstract`).

**Tested** (3)

- <a id="row-capability-differential"></a>**`capability-differential`** — `Proofs.Oracle`, 8 cases · fuaran-core#177

  The extracted seam model agrees with the shipped `Function.signature`, `isTotal`, `signatureExcluding`, `apply`, `curry`, `compose`, `composedEffect`, `observedEffect` and `auditEffect` over generated artifacts (`Reference.artw`, the model's node type instantiated at `RNode` so trees cross untranslated) and argument sets, and with `CapabilityRegistry.register`, `enumerate`, `tryFind` and `dispatch` and `Capability.validateArgs` over generated registries and typed invocations — the verdict, an accepted tree or value, an accepted `Deferred` ENVELOPE rendered case for case (the draw reaches a settling, a pending and a failing body, and neither side may hand out an `Ok (Failed _)`), a refusal by class AND payload, the effect surfaces, and, per dispatch, an instrumented body on each side that must have run on both or on neither and never past a refusal that is not its own. The go-red blinds the model's int reader and requires it to lose on the space check, on the seam and on the algebra. The extracted `invocation_key` agrees with `Capability.invocationKey` byte for byte over invocations drawn from an adversarial alphabet (both symbols of the key encoding, `=`, `#`, empty values). The admission gate too — `Function.validate` against `validate_decl`, registration refusing `IllFormedCapability`, `DuplicateArg`, a strict application refusing `SlotArgOpen` and `compose` refusing `IllFormedResult` — over a draw that reaches negative, empty, NaN, infinite and past-2^53 bounds, a count past the cap, duplicate hole addresses, a hole beneath a slot, an open slot argument, a repeated argument address and the five culture-sensitive integer spellings, with both differentials run under he-IL.

  _Boundary:_ Agreement is over the pools drawn, never over all inputs; the capture-key agreement is in the escaper case. Seeded and replayable: the same seed reproduces the same tally, asserted, and the count of bodies that ran is asserted equal to dispatched plus `BodyFailed`.

- <a id="row-capability-seam-codec-differential"></a>**`capability-seam-codec-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#455

  The extracted section 18 agrees with `CapabilityCodec`'s invocation, envelope and invoke-error codecs over generated values (addresses drawn with replacement, every invoke-error case, every envelope case) and four mutations of each encoding — the canonical text or the encoded value, acceptance, the value read back, and, where production answers a typed refusal, its code and path — and on production every typed refusal resolves in the document it read. The go-red: a model writer that drops an argument disagrees.

  _Boundary:_ The invoke-error reader (`CapabilitySeamCodec.decodeInvokeError`) and the envelope over a sentence-answering payload reader are compared on acceptance and value alone, as the model's refusal carries no sentence. Agreement is over the pools drawn, never over all inputs.

- <a id="row-capability-sections-differential"></a>**`capability-sections-differential`** — `Proofs.Oracle`, 6 cases · fuaran-core#354

  The extracted sections 14 to 16 agree with the shipped `Function.bindHandlers` (the table or the refusal by class and payload, over handler sets bound in two arrival orders, with a repeated key and with two undeclared keys at once), with `CapabilityPipeline.typeCheck`, `eval`, `dependencyMap` (its edge relation against the model's `deps_of`) and `evalFrom`, with the pipeline's dirty set (`Propagation.dirtyFromChangedIds` over `dependencyMap`, against the extracted `Propagation` closure over `deps_of` — never production against itself) and with `Space.subsumes` (the verdict, the refusal by class and payload, the result map, and the nodes and resolved arguments each body was handed, over pipelines carrying a repeated id, a forward edge, a self-edge, a two-node cycle, an edge to no node, an ill-typed edge and an argument bound twice or dropped, and over reorderings of the accepted ones), and with the capability and pipeline codecs (the encoding, an accepted value, a refusal by code and path, over each encoding and three mutations of it, and the round trip through the bytes). Each theorem's statement is also instantiated on the shipped functions: the incremental answer against the full one, a reordering accepted exactly when it is topological, a round trip that is the identity exactly on the well-formed. Three go-reds: a comparator that is not the map's key order, a blind int reader and a blind float-fault reader each lose.

  _Boundary:_ Agreement is over the pools drawn, never over all inputs.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`lawful-abstract-witness`](#row-lawful-abstract-witness) (assumed), [`capability-key-renderers-abstract`](#row-capability-key-renderers-abstract) (assumed), [`capability-sections-parameters-abstract`](#row-capability-sections-parameters-abstract) (assumed), [`capability-pipeline-body-total`](#row-capability-pipeline-body-total) (assumed).

**Inherited through the models it opens:** [`propagation-change-set-and-prior`](#row-propagation-change-set-and-prior) (assumed), [`propagation-evaluator-total`](#row-propagation-evaluator-total) (assumed).

### `Query`

[`Query.fst`](Query.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `Relation`, `Propagation`, `Capability`.

> Query — the data-acquisition seam, the sibling of the capability seam: `Fuaran.Core.Query`'s
> default-deny registry, its parameter validation, its host-supplied resolver and its capture
> key, modelled clause for clause and proved (fuaran-core Phase 187).

**What is modelled.** `src/Fuaran.Core.Query/Query.fs` — the two modules a dispatch crosses:

- the DECLARATION vocabulary (`QueryParam` / `Query` / `QueryError`, with the `ColumnType`
and `Cell` constructors and the `EffectClass` a declaration carries) and `Query`'s four
functions: the internal `cellType`, `determinismTag`, the private `cellFields` with
`invocationKey` over it (through `Hash.canonicalFields`, Phase 225), `validateParams` (its local `checkArgs` walk and its
required-params step) and `invoke`;
- (Phase 398) the declared FILTER and ORDER: `ColumnPredicate` (all eight cases),
`SortDirection` and `SortKey` as closed types, `Query.Where` / `Query.OrderBy` as members of
the declaration, the admission checks `QueryShape.whereFault` / `orderFault` that
`QueryRegistry.admissionFault` runs after the repeated-parameter check, and the shape
fields `QueryShape.keyFields` adds to the capture key's pre-image;
- the REGISTRY: `QueryRegistry.empty` / `register` / `tryFind` / `enumerate` / `dispatch`;
- the `Deferred<'T>` envelope the resolver answers in (Phase 198) — its three cases, and
none of its combinators, exactly as `Capability.fst` carries it for the other seam.

Three things are PARAMETERS rather than clauses. The RESOLVER (`Query -> Deferred<QueryResult>`)
is an argument of `invoke` and `dispatch` and stays outside every claim: nothing here is a
theorem about any resolver, and the result payload is an abstract type. The RENDERERS the
capture key is written through — `string` on an int, `FloatLayout.canonical`, `Hash.fnv1a`,
the ordinal string order `List.sortBy fst` sorts by, and (Phase 225) `Hash.canonicalField` —
are a `renderers` record, as Phase 177 made the three scalar readers one; a float cell
crosses as an opaque carrier. And a declaration's `Source` is an opaque carrier too: the seam
never reads it.

**Proved** (18)

- <a id="row-query-unregistered-refused"></a>**`query-unregistered-refused`** — `unregistered_refused` · fuaran-core#187

  `QueryRegistry.dispatch` of an id the registry does not hold is the typed refusal `NoSuchQuery id known`, naming the id and every id the registry does hold — and the result is the SAME under every host resolver, which is what "runs no resolver" means for a pure function without instrumenting one. `invoke` can never produce the registry's two refusals (`invoke_never_registry_refusal`), so the classes stay disjoint: a `NoSuchQuery` is always the registry's answer and never a resolver's.

  _Boundary:_ The resolver is the model's parameter and stays outside the claim: nothing here is a theorem about any resolver.

- <a id="row-query-validate-before-resolve"></a>**`query-validate-before-resolve`** — `validate_before_resolve` · fuaran-core#187

  An argument set `validateParams` rejects makes `invoke` return that rejection, for every resolver alike — no resolver ran — and, at the registry, a dispatch that resolved its id still runs nothing on a set validation rejects (`dispatch_validates_first`). The converse holds too: a settled or pending result, or an `ExecutionFailed`, is reachable only past an accepted validation (`resolver_runs_only_validated`). Validation is characterised EXACTLY rather than soundly: it accepts precisely the sets whose every binding addresses a declared param with a `Null` or an in-type cell, that leave no required name unbound, and that bind none of them only to `Null` (`validate_params_exact`), the refusal is one of its own five classes (`validate_params_shape`), and a refusal is truthful — an `UnknownParam` names a bound name no param declares and lists the declared ones, a `ParamTypeMismatch` names a declared param, its declared type and a DIFFERENT type the bound cell really has (`refusal_is_truthful`), a `RequiredParamsUnbound` names only declared names the args really leave out (`unbound_required_truthful`), and a `RequiredParamsNull` only declared names the args bind, and bind only to `Null` (`null_required_truthful`).

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). The refusal classes `validate_params_shape` admits include `DuplicateParam`, checked before the other four. No clause of the seam model raises `SourceNotResolved` or `Timeout` (a resolver's to raise), the typed resolver's `PredicateNotHonoured` / `OrderNotHonoured`, the policy gate's refusals or `UnreadableArgs`; those are carried only as cases of the error codec (`query-codec-error-roundtrip`).

- <a id="row-query-envelope-three-outcomes"></a>**`query-envelope-three-outcomes`** — `invoke_never_ok_failed` · fuaran-core#187

  THE ENVELOPE HAS NO FOURTH OUTCOME, on this seam as on its sibling. The resolver answers in `Deferred<QueryResult>`, and an invocation past an accepted validation carries its `Ready` or `Pending` and nothing else: a resolver's `Failed m` crosses into the enumerated `ExecutionFailed (m, [])` at the seam, so `Ok (Failed _)` is unreachable rather than merely unproduced, for every resolver and every argument set alike (`invoke_never_ok_failed`), and through the registry, where a host actually reaches the seam (`dispatch_never_ok_failed`).

  _Boundary:_ `queryLaws` samples the same claim on the shipped seam over three drawn answers; this is it over all resolvers. The envelope's combinators are outside the model and stay `deferredLaws`'s.

- <a id="row-query-enumerate-is-registry"></a>**`query-enumerate-is-registry`** — `enumerate_is_registry` · fuaran-core#187

  An id is in `QueryRegistry.enumerate`'s output exactly when `QueryRegistry.tryFind` resolves it, and `dispatch` raises `NoSuchQuery` exactly off that enumeration (`no_such_iff_unregistered`) — no hidden entry, no phantom one. A resolved id dispatches to ITS declaration's `invoke` (`registered_dispatches`); `register` refuses a held id (`register_refuses_duplicate`) and extends by exactly one entry otherwise, over a declaration the admission gate admits (`register_extends`; a repeated name is refused, row `query-register-refuses-duplicate-params`, and so is a filter or an order its schema does not admit, row `query-register-refuses-shape`); and a registry built by `register` from `empty` holds distinct ids (`register_keeps_distinct`), which is what makes a capture key's id prefix name ONE declaration.

  _Boundary:_ Membership, not order: production's `Map` sorts the enumeration by id and the model holds the map as a list, so the sorted order is `queryLaws`'s to certify and is outside the theorem.

- <a id="row-query-invocation-key-deterministic"></a>**`query-invocation-key-deterministic`** — `invocation_key_deterministic` · fuaran-core#187

  The capture key reads the declaration through its `Id`, its `Where` and its `OrderBy` and the arguments through their name-sorted canonical form alone: two declarations sharing an id, a filter and an order — whatever else they declare — and two argument lists binding the same DISTINCT names to the same cells in ANY order key identically, under any hash and any renderer. The order half rests on `sorted_unique`: two name-sorted lists with distinct names holding the same bindings are the same list. The declaration half needs no premise at all (`invocation_key_reads_id_and_shape`), and a declaration whose filter and order are empty keys exactly as a key reading the id alone (row `query-invocation-key-unshaped`). `validateParams` refuses a repeated name as `DuplicateParam` before any cell is read (`validate_params_distinct`), so two accepted lists with the same bindings key identically with no hypothesis left (`accepted_invocation_key_deterministic`).

  _Boundary:_ The argument-order half needs the names distinct and the name comparator a total order. Distinct names are a real hypothesis and not a convenience — `List.sortBy` is stable, so two bindings under ONE name keep the caller's order and the key moves with it — and distinctness is discharged only for lists `validateParams` accepts. That the key reads no resolver answer and no clock is NOT a lemma here and is not dressed as one: `invocationKey` is handed neither, so in a pure total function that independence is the function's type.

- <a id="row-query-required-is-non-null"></a>**`query-required-is-non-null`** — `required_is_non_null` · fuaran-core#226

  `Required` means a VALUE: `validateParams` accepts EXACTLY the argument sets whose every binding addresses a declared param with a `Null` or an in-type cell AND that bind every required param to a non-`Null` cell (`required_is_non_null`, the positive iff over `required_valued`). A required param left out is refused as `RequiredParamsUnbound`; one that is present but bound only to `Null` is refused as the DISTINCT `RequiredParamsNull` (`null_required`), naming it, before any resolver runs. What the argument set binding each param to `Null` gets is stated exactly (`all_null_refusal_exact`): it passes the type and presence steps and is refused as `RequiredParamsNull` naming every required param in declaration order, and is accepted only by a declaration that requires nothing. A required name with SOME binding to a value is bound whatever else binds it (`has_value`).

  _Boundary:_ Within the model; on the shipped seam it is asserted by the closure case and certified by `queryLaws`' param-validation law over the all-`Null` set BUILT from its declaration, both sampled rather than proved.

- <a id="row-query-invocation-key-injective"></a>**`query-invocation-key-injective`** — `invocation_key_injective` · fuaran-core#225

  `Query.invocationKey`'s pre-image is INJECTIVE: two (declaration, argument list) pairs whose pre-images agree have the SAME `Where`, the SAME `OrderBy` and name-sorted argument lists that ARE the same list, holding exactly the same bindings (`invocation_key_injective`), so declarations filtered or ordered differently, and distinct argument sets, have distinct pre-images, for every declaration, accepted or not, and with no premise about the name comparator. The pre-image is `Hash.canonicalFields` over the declaration's shape fields — for a non-empty filter an empty name, the tag `w` and its canonical text, then for a non-empty order the same with `o`, tags no cell carries (`cell_tag_not_shape`), recovered by `shape_fields_injective` — then three fields per binding (name, cell tag, cell payload): every field is escaped (`U+0010` before each `U+0010` and `U+0001` it carries) and terminated by `U+0001`, so the first unescaped terminator ends each field (`esc_split`), the field list is recovered (`fields_injective`), and three fields per binding recover the bindings (`arg_fields_injective`). The exhibits `[a = "1b=s2"]` and `[a = "1"; b = "2"]` key apart on production.

  _With:_ `enc_injective`, `cell_fields_injective`, `canonical_injective`, `determinism_tag_injective`.

  _Boundary:_ It holds over argument lists whose timestamp cells carry the unit their text names (`units_named`: the model's `Timestamp` carries the coarsest unit `Cell.typeOf` reads off the text, the key writes the text alone as production does, and every cell production builds satisfies the premise, the oracle host computing the unit from the text at the edge). What it spends is named in `key_premises`: the Chain-style reading of a string as its symbols (`symbols_faithful`), the escaper premise (`field_faithful`, measured by the round-trip case), and injectivity of the int and float renderers over the model's carriers and of the filter and order renderers (`query-renderers-abstract` says where the float one fails). Whether two distinct pre-images HASH apart is a claim about FNV-1a and is not made. Beside it, the determinism label a query's capture carries, `Query.determinismTag`, is injective over the eight determinism sets (`determinism_tag_injective`), as `function-determinism-label-bijection` states for the capability seam.

- <a id="row-query-validate-widens"></a>**`query-validate-widens`** — `validate_params_exact` · fuaran-core#295

  A parameter takes a cell exactly when the cell's type WIDENS to the parameter's (`ColumnType.widens`: the identity, `int` into `float`, `int` into `decimal`), the one lattice the column codec reads by, so an `int` argument to a `float` parameter is accepted as the same value decodes into a `float` column. `validateParams` is characterised exactly over it (`validate_params_exact`, through `well_typed`), and a `ParamTypeMismatch` names a cell type that does NOT widen to the declared one (`refusal_is_truthful`).

  _Boundary:_ On the numeric types the widening is `Space.subsumes`, which `queryLaws` certifies. The cell a resolver reads through `invokeWithArgs` is promoted to the declared type, which is outside this model.

- <a id="row-query-register-refuses-duplicate-params"></a>**`query-register-refuses-duplicate-params`** — `register_refuses_duplicate_params` · fuaran-core#316

  `QueryRegistry.register` refuses a fresh declaration that names a parameter twice, as `DuplicateParam` naming the first repeated name (`register_refuses_duplicate_params`), after the id check and before anything is held. So a registry built by `register` from `empty` holds only declarations with distinct parameter names (`register_keeps_params_distinct`, over `params_distinct`), and the premise `all_null_refusal_exact` is stated under is DISCHARGED for every query such a registry resolves rather than assumed (`registered_all_null_refusal_exact`): the all-`Null` argument set of a registered query is refused as `RequiredParamsNull` naming every required parameter in declaration order, and accepted only when it requires nothing.

  _Boundary:_ Only for registries built by `register` from `empty`: a hand-built `QueryRegistry` record is outside the claim, because the record is public and the invariant is `register`'s.

- <a id="row-query-invocation-key-page-injective"></a>**`query-invocation-key-page-injective`** — `invocation_key_page_injective` · fuaran-core#316

  `Query.invocationKeyPage`'s pre-image is INJECTIVE over the triple (the declaration's filter and order, argument set, page token): two triples whose page pre-images agree hold the same token, the same `Where`, the same `OrderBy` and exactly the same bindings (`invocation_key_page_injective`, extending `invocation_key_injective`), so distinct tokens never share a pre-image (`distinct_tokens_distinct_preimages`), nor do declarations filtered or ordered differently, on any page (`distinct_shapes_distinct_preimages`), and a paged query captured page by page replays page n from page n's capture. The page fields are a triple in front of the key fields — an empty name, the tag `p` that no cell's tag and no shape triple's tag is (`cell_tag_not_page`, `key_fields_not_page`), the token — and nothing for the first page, so the first page keys exactly as `invocationKey` (`invocation_key_page_none`) and every journal keyed without paging still replays. The paged verbs carry every dispatch theorem unchanged: `dispatchPage` IS `dispatch` under the resolver that reads the token (`dispatch_page_is_dispatch`).

  _Boundary:_ CONDITIONAL on `key_premises`, as `invocation_key_injective` is: the symbol reading of a string, the escaper premise of `Hash.canonicalField`, and injective int and float renderers (`query-renderers-abstract`). Whether two distinct pre-images HASH apart is a claim about FNV-1a and is not made.

- <a id="row-query-invocation-key-unshaped"></a>**`query-invocation-key-unshaped`** — `invocation_key_unshaped` · fuaran-core#398

  A declaration whose `Where` and `OrderBy` are both empty keys EXACTLY as `Query.invocationKey` keys without either member: the id, `#`, and the hash of the name-sorted arguments' canonical string alone (`invocation_key_unshaped`), so every journal keyed without the filter and order members still replays and every declaration that does not filter or order is untouched by them. The shape fields are added only for a non-empty member (`shape_fields`). Unconditional: no premise about any renderer.

  _Boundary:_ It covers only declarations whose `Where` and `OrderBy` are both empty. That the same rule keeps the stored declaration document byte-identical is a test on the shipped codec rather than this row.

- <a id="row-query-register-refuses-shape"></a>**`query-register-refuses-shape`** — `register_refuses_shape` · fuaran-core#398

  `QueryRegistry.register` runs ONE admission gate (`admission_fault`, production's `admissionFault`) after the id check — a repeated parameter first, then the filter (`where_fault`: per predicate in order, an undeclared column as `UnknownColumn` naming the schema's columns, `contains` on a column that is not a string as `PredicateNotApplicable`, a `Null` literal as `IllFormedLiteral`, a literal of another type than its column's as `PredicateTypeMismatch`), then the order (`order_fault`: an undeclared column, then a column named a second time as `DuplicateSortColumn`) — and refuses a fresh declaration with the gate's first fault (`register_refuses_shape`), admitting exactly the declarations with none (`register_extends`). The same gate is what `replace` and the declaration reader run.

  _Boundary:_ The column layer's carriability clause (`Table.validate`'s non-finite float and non-canonical decimal, date or timestamp text, also `IllFormedLiteral`) is not restated in this model: it is that layer's grammar, the model takes a present, well-typed literal as carried, and the differential draws carried literals only. This row is about `register`, not `replace` or the declaration reader.

- <a id="row-query-registry-lifecycle"></a>**`query-registry-lifecycle`** — `union_members` · fuaran-core#316

  A registry is a lattice, not an append log. `QueryRegistry.unregister` after a successful `register` gives back exactly the declarations the registry held before (`unregister_register`), and refuses an id the registry does not hold as `NoSuchQuery` naming every id it holds (`unregister_refuses_unheld`); `restrict` holds exactly the held ids that are kept, so it never widens (`restrict_members`); `union` is refused exactly when the two registries share an id, as `DuplicateQuery` naming one they share, and otherwise holds exactly the ids of both (`union_members`).

  _Boundary:_ Membership, not order: production's `Map` sorts by id and the model holds a list, so the order, `union`'s associativity and the same lifecycle on the capability, function and validator registries are `registryLaws`' to certify, over drawn registries, and are outside the theorem.

- <a id="row-query-codec-declaration-roundtrip"></a>**`query-codec-declaration-roundtrip`** — `query_roundtrip` · fuaran-core#455

  `QueryCodec.queryOf` after `queryJson` is characterised exactly, for every declaration whose filter literals are carried, over any column codec that reads back what it writes (`column_codec_laws`), by `query_roundtrip`: the registry's admission refusal, `OutOfRange` at the member at fault (`params`, `where[i]`, `orderBy[i]`: `admission_fault_at`, which refuses exactly what `register`'s `admission_fault` refuses, `admission_fault_at_agrees`), where the gate refuses the declaration, and the declaration itself otherwise; the identity exactly on the admitted declarations (`query_roundtrip_identity`). Parameters, effect, filter predicates (`predicate_roundtrip`) and order keys are modelled clause for clause. Like the capability codec, this one is written over `JVal` with the decode layer's combinators, not the `Codec` blocks, so this is proved directly.

  _Boundary:_ Only declarations whose filter literals are carried; the identity holds only on admitted declarations. The column codec is a parameter whose round trips are assumed (`query-codec-column-premises`). `QueryCodec.decodeArgs`, which reads a model's argument object that no writer of this codec produces, is not modelled.

- <a id="row-query-codec-result-roundtrip"></a>**`query-codec-result-roundtrip`** — `result_roundtrip` · fuaran-core#455

  `resultOf` after `resultJson` reads back exactly, for every result page (`result_roundtrip`): the rows, the page number, the total and the next-page token, the two optional members omitted when absent.

  _Boundary:_ Only over a column codec that reads back the rows it writes, a parameter whose round trips are assumed (`query-codec-column-premises`); within the model, at the `JVal`.

- <a id="row-query-codec-deferred-result-roundtrip"></a>**`query-codec-deferred-result-roundtrip`** — `deferred_result_roundtrip` · fuaran-core#455

  The async result's reader after its writer reads back exactly, for every `Deferred<QueryResult>` — the capability seam's envelope over the result codec (`deferred_result_roundtrip`). The reader is the envelope's TYPED reader over `resultOf` (`CapabilitySeamCodec.decodeDeferred`), so a refusal keeps its code and its path.

  _Boundary:_ Resolution alone does not pin the code and path: a reader answering every refusal `OutOfRange` at the root would be admitted by `deferred_result_refusals_resolve`, since the root resolves, and only the differential would see the code and the path lost.

- <a id="row-query-codec-error-roundtrip"></a>**`query-codec-error-roundtrip`** — `query_error_roundtrip` · fuaran-core#455

  `QuerySeamCodec.decodeQueryError` after `encodeQueryError` (the codec derived from the union's IDL declaration) reads back exactly, for every `QueryError` the codec carries — all twenty cases, as `wire_query_error` (the fifteen of the model's `query_error` under `QRefused`, and the policy gate's two, `UnreadableArgs` with its decode refusal whole, and the typed resolver's two), with a decode refusal (`decode_error_roundtrip`, its path through `DecodePath.toJson` / `ofJson`) and a predicate inside them (`query_error_roundtrip`).

  _Boundary:_ Only the cases the codec carries, within the model at the `JVal`; the correspondence to production is the differential's (`query-codec-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-query-codec-refusals-resolve"></a>**`query-codec-refusals-resolve`** — `query_refusals_resolve` · fuaran-core#455

  Every refusal the declaration reader answers over a document resolves in it and is not `InvalidJson` (`query_refusals_resolve`) — the admission refusal included, at `params`, or at the `where` or `orderBy` item the gate names, which the document holds (`where_index_resolves`, `order_index_resolves`) — and so does every refusal of the result reader (`result_refusals_resolve`), the async result reader (`deferred_result_refusals_resolve`) and the query-error reader (`query_error_refusals_resolve`), over any column codec.

  _Boundary:_ A column-codec refusal is not carried through with its own code: it is raised `OutOfRange` at the member it was reading. Resolution says where a refusal points, not that its code is production's; within the model.

**Tested** (2)

- <a id="row-query-differential"></a>**`query-differential`** — `Proofs.Oracle`, 6 cases · fuaran-core#187

  The extracted seam model agrees with the shipped `QueryRegistry.register`, `enumerate`, `tryFind` and `dispatch` and with `Query.validateParams`, `invocationKey` and `determinismTag` over generated registries, declarations and argument sets — the generator `queryLaws` uses, widened: a three-id pool so duplicates arise, up to three params over all six column types with names drawn with replacement so the registration refusal of a repeated name is compared (500 registries drawn, to keep the sample's floors), argument sets that bind, skip, null, mistype, repeat and reverse, and declarations carrying a drawn filter and order over a drawn schema — undeclared columns, `contains` on a column that is not a string, a `Null` literal, a literal of another type and an order naming a column twice among them, every literal one the column layer carries — so the admission's refusals are compared. Compared: the verdict, an accepted `Deferred` ENVELOPE rendered case for case (the draw reaches a settling, a pending and a failing resolver, and neither side may hand out an `Ok (Failed _)`), a refusal by class AND payload, the capture key byte for byte for every argument set accepted or not, with the shape fields of a filtered or ordered declaration in its pre-image, and the PAGE key `invocationKeyPage` byte for byte under six tokens carrying the page tag and the encoding's own symbols, with the first page's key held equal to `invocationKey`, the shipped key held still under a reordering of distinct names, and, per dispatch, an instrumented resolver on each side that must have run on both or on neither and never past a refusal that is not its own. Two go-reds, one per half of the model: a bridge that forgets `Required` must lose on the required-params step, and an order-blind comparator must lose on the capture key and on nothing else.

  _Boundary:_ Agreement is over the pools drawn, never over all inputs. The generator draws no character of the key encoding, so it cannot tell an escaping `field` from a bare one (measured: a `field` that only appends the terminator leaves it green); the escaper case compares the capture key over string cells drawn from an adversarial alphabet, and it goes red on exactly that. Seeded and replayable: the same seed reproduces the same tally, asserted, and the count of resolvers that ran is asserted equal to settled plus pending plus `ExecutionFailed`.

- <a id="row-query-codec-differential"></a>**`query-codec-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#455

  The extracted section 19 agrees with `QueryCodec` over its own column codec — the declarations the seam differential's generator draws (repeated parameter names, filters and orders the admission gate refuses), result pages, every async-result case and every query-error case, and four mutations of each encoding: the canonical text or the encoded value, acceptance, the value read back, and every typed refusal's code and path — and on production every typed refusal resolves in the document it read. The go-red: a model writer that drops a parameter disagrees.

  _Boundary:_ Agreement is over the pools drawn, never over all inputs.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`query-renderers-abstract`](#row-query-renderers-abstract) (assumed), [`query-codec-column-premises`](#row-query-codec-column-premises) (assumed).

**Inherited through the models it opens:** [`lawful-abstract-witness`](#row-lawful-abstract-witness) (assumed), [`capability-key-renderers-abstract`](#row-capability-key-renderers-abstract) (assumed), [`propagation-change-set-and-prior`](#row-propagation-change-set-and-prior) (assumed), [`capability-sections-parameters-abstract`](#row-capability-sections-parameters-abstract) (assumed), [`capability-pipeline-body-total`](#row-capability-pipeline-body-total) (assumed), [`propagation-evaluator-total`](#row-propagation-evaluator-total) (assumed).

### `Arbitrate`

[`Arbitrate.fst`](Arbitrate.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `DagFold`, `TreeOps`, `Preservation`.

> Arbitrate — an F\* model of Fuaran.Core's proposal arbitration, with the three promises its
> doc comment makes proved rather than sampled (fuaran-core Phase 157).

**What is modelled.** `Arbitration.arbitrate` in `src/Fuaran.Core.Ops/Arbitration.fs`, clause for
clause: the pinned order (a STABLE ascending sort on the proposal id), the batch dry run
(`Ops.canApplyAll`, failing index and envelope included), the greedy footprint-independence
pass, the re-citation of every conflict against the FULL accepted set, and the three fields of
the result. It is a model OVER `TreeOps` (Phase 133): a script is a `list TreeOps.op`, the dry
run threads `TreeOps.apply`, the footprint is `TreeOps.fp_all` and independence is
`DagFold.independent` — the same relation the fold theorem is about, imported, not restated.

**Proved** (6)

- <a id="row-arbitration-accepted-pairwise-independent"></a>**`arbitration-accepted-pairwise-independent`** — `accepted_pairwise_independent` · fuaran-core#157

  `Arbitration.arbitrate`'s accepted set is pairwise `Ops.independent`, for every base tree and every proposal list — duplicates, inapplicable scripts and all; the theorem carries no hypothesis. Pairwise is POSITIONAL (every member against every later one, which with `independent`'s symmetry is the whole relation), because a proposal need not be independent of itself and a list may carry one value twice. `accepted_pair_commutes` is what that buys: any two accepted proposals, read as the single ops `Batch a.script` and `Batch b.script`, both apply at the base, and at a well-formed base each applies after the other and the two orders reach the same tree — `tree-independence-diamond` reaching the accepted set, which is the doc comment's "by footprint soundness, confluent in any order" for a pair.

  _With:_ `accepted_pair_commutes`, `can_script_is_apply_all`, `accepted_all_applicable`.

  _Boundary:_ The commuting corollary needs a well-formed base, and is about a pair. The N-script any-order claim is not this row's: it is `skeleton-fold-confluence`'s, whose hypothesis this discharges, sampled end to end by `arbitrationLaws`' confluence law and restated at the accepted set as `arbitration-merged-order-free`.

- <a id="row-arbitration-merged-order-free"></a>**`arbitration-merged-order-free`** — `merged_applies_and_order_free` · fuaran-core#305

  At a well-formed base, `Arbitration.arbitrate`'s `MergedScript` — the accepted scripts concatenated in pinned order — APPLIES, and applying the accepted proposals' scripts in ANY order reaches the same tree: no order of the accepted set is refused and none reaches a different tree, which is the doc comment's "by footprint soundness its scripts apply confluently in ANY order" for N scripts rather than for a pair (`arbitration-accepted-pairwise-independent` is the pair). Read off `DagFold.replay_perm` at `Skeleton`'s instantiation rather than proved again: the accepted scripts are the fold's lanes, `arbitration-accepted-pairwise-independent`'s pairwise independence is an empty conflict sweep over them (`all_conflicts_nil`, through `independent_union_left` — independence of a script's union footprint descends to every op), every lane applies at the base (`accepted_all_applicable`), the domain hypothesis is `op_independence_diamond`, and at a well-formed base the guarded `wapply` the composite folds IS `apply` because every accepted step keeps `wf` (`replay_wapply_is_apply_all`, citing `Preservation.apply_preserves_wf`). `arbitrate` refuses a base `Tree.wellFormed` rejects before arbitrating anything (D103.4), so below `wf` the merged script is empty.

  _With:_ `all_conflicts_nil`, `lanes_apply_of_applicable`, `concat_applies`, `replay_wapply_is_apply_all`.

  _Boundary:_ The hypothesis is a well-formed base — a property of every base the shipped function lets through (D102's first form). The model keeps its clause-for-clause shape and does not model the refusal; the refusal is the production guard in front of it.

- <a id="row-arbitration-accepted-maximal"></a>**`arbitration-accepted-maximal`** — `accepted_maximal` · fuaran-core#157

  Every rejection `arbitrate` returns is JUSTIFIED, so nothing rejected could be added to the accepted set: an `Inapplicable (i, e)` is exactly the `Ops.canApplyAll` dry run's failing index and envelope against the base, and a `Conflicts ids` names a script that DOES apply, is NOT independent of the final accepted set, and cites a non-empty list that is exactly the accepted ids it interferes with, in pinned order. The citation is sound — every cited id resolves to an accepted proposal that genuinely interferes (`conflict_cites_an_accepted_interferer`) — and complete — every accepted interferer is cited (`every_interferer_is_cited`), "the complete rebase target, not just the first collision". The partition is total by count: accepted and rejected together are as long as the input (`arbitrate_is_total`). No hypothesis about ids.

  _With:_ `all_justified`, `provisional_grows`, `interfering_nil_iff`, `conflict_cites_an_accepted_interferer`, `every_interferer_is_cited`, `justified_mem`, `arbitrate_is_total`.

  _Boundary:_ MAXIMAL, not maximum: see `arbitration-maximal-is-not-maximum`. Maximality is with respect to `Ops.independent`, which is conservative (`footprint-precision-ceiling`): a `Conflicts` means "not provably coexistent", never that the two scripts would in fact fail to commute. And the dry run modelled is `Ops.canApplyAll` under `OpScope.plain`, which consults no container capability, so nothing here is about a contained scope (`container-sequence-gap`). Only the unkeyed `arbitrate` is modelled: the keyed `arbitrateWith`, which arbitrates at a domain's own footprint and dry run (fuaran-core#247), is not; `Conformance.arbitrationLaws` and the keyed-arbitration family (`KeyedArbitrationLaws.fs`) sample it.

- <a id="row-arbitration-deterministic"></a>**`arbitration-deterministic`** — `arbitrate_deterministic` · fuaran-core#157

  Under every arrival order (`DagFold.perm`) of a proposal list whose ids are DISTINCT, `arbitrate`'s whole result is equal — the accepted set, the merged script, and every rejection with its reason. `arbitrate` reads its input only through the pinned sort, so the theorem is the sort's: two inserts under different keys commute into any list at all (`insert_comm`), and distinctness survives a permutation (`perm_distinct`). The pinned order IS ascending id (`pin_sorted`).

  _With:_ `pin_sorted`, `pin_perm`, `insert_comm`, `perm_distinct`.

  _Boundary:_ THE HYPOTHESIS IS STATED, BY NAME (`distinct_ids`), and is what the shipped total check decides: `Arbitration.duplicateIds proposals = []` holds exactly when it does, which the differential asserts on every generated set. That it cannot be dropped is `arbitration-id-uniqueness-is-needed`. Proposal ids are unbounded integers in the model and `int` in the F#; the two orders agree on the whole `int` range, so nothing turns on the width.

- <a id="row-arbitration-id-uniqueness-is-needed"></a>**`arbitration-id-uniqueness-is-needed`** — `duplicate_ids_break_invariance` · fuaran-core#157

  THE FINDING. `arbitrate` is TOTAL on duplicate ids — every function of the model is total, and pairwise independence, maximality and the partition count carry no hypothesis about ids — but permutation invariance is FALSE without id uniqueness, and not merely unproved: two proposals sharing id 1 that each insert under the same parent interfere, so exactly one is accepted, and `List.sortBy`'s stability leaves them in arrival order, so WHICH one is accepted is the arrival order. The two lists are one `PSwap` apart. What a repeated id costs is invariance and nothing else. Closed on the shipped side by the additive `Arbitration.duplicateIds`: a total check that names the repeated ids, never an assigner, asserted on the shipped function over the model's own witness by the differential's third case.

  _Boundary:_ This refutes `arbitrate_deterministic` with its `requires` deleted rather than some stronger claim; `duplicateIds` names repeated ids and never assigns fresh ones. Uniqueness is SUFFICIENT, not necessary, on a single input: two proposals sharing an id, one of them inapplicable, arbitrate identically in either order, so "invariance holds exactly when `duplicateIds` is empty" is false and is not claimed. What cannot be dropped is uniqueness between two DIFFERENT proposals that land in the same bucket (both accepted, both rejected, or one displacing the other). `Conformance.arbitrationLaws` holds the check to the hypothesis: `duplicateIds` is exact against an independent recount, empty on every set the permutation law is certified over, and a twin (same id and script, another holder) makes arrival order observable on every set holding an applicable self-interfering proposal, with its own vacuity guard.

- <a id="row-arbitration-maximal-is-not-maximum"></a>**`arbitration-maximal-is-not-maximum`** — `maximal_is_not_maximum` · fuaran-core#157

  Greedy-in-pinned-order does NOT return a maximum independent set, and this is the witness: proposal 1 writes under both `a` and `b`, proposals 2 and 3 under one each; in the pinned order 1 is accepted alone — an accepted set of ONE, both rejections justified — while `[2; 3]` is itself applicable and pairwise independent, a set of TWO, and renumbering proposal 1 to come last makes that larger set the accepted one. Both results are maximal; only one is maximum; the ids decide which the caller gets. Asserted on the shipped function by the differential's fourth case.

  _Boundary:_ A theorem about one input, stated so that MAXIMUM is a refuted claim and not an untried one.

**Tested** (1)

- <a id="row-arbitration-differential"></a>**`arbitration-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#157

  The extracted arbitration model agrees with the shipped `Arbitration.arbitrate` over generated proposal sets against the base tree — scripts from the lane generator, about one in four corrupted into a provably inapplicable script, under ids that are a SHUFFLE of 1..n (so the pinned order is not the arrival order) and, in a second mode, under ids drawn from {1, 2} (so most sets carry a repeated id and the STABLE sort's tie-break is compared too). Compared per set: the accepted proposals in order (id, holder, script), the merged script, and every rejection in order with its reason — an `Inapplicable`'s index exactly and its envelope by CLASS, which is what the tree model claims (`tree-differential`), and a `Conflicts`' citation exactly. On every set the shipped `Arbitration.duplicateIds` is empty exactly when the extracted `distinct_ids` holds, and the extracted `pairwise_independent` and `all_applicable` are asked of PRODUCTION's own accepted set. One go-red: the extracted model with the greedy pass's independence test removed and nothing else touched — a model that accepts a conflicting pair — must lose, on a sample shown to have reached a conflict. The model recomputes a footprint where the F# caches it beside the accepted proposal; the two are the same value of a pure function, and this family is what holds them to the same verdicts.

  _Boundary:_ An `Inapplicable`'s envelope is compared by class only. Proposal ids cross as unbounded integers on the model side and `int` on production's; the generated ids are small, so the width is not exercised. Agreement is over the pools drawn, never over all inputs.

**Rests on, or is bounded by:** [`arbitration-pinned-order`](#row-arbitration-pinned-order) (policy).

**Inherited through the models it opens:** [`independence-diamond`](#row-independence-diamond) (assumed), [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed), [`witness-surface-scope`](#row-witness-surface-scope) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `DecimalText`

[`DecimalText.fst`](DecimalText.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`.

> DecimalText — the exact decimal's text arithmetic: `Fuaran.Core.DecimalText`'s canonical
> form, order and sum, modelled clause for clause and proved (fuaran-core Phase 279).

**What is modelled.** `src/Fuaran.Core.Column/DecimalText.fs`, module `DecimalText` — the carrier of a
`Decimal` cell (DECISIONS.md D72) and the only arithmetic the column layer does over it:

- the READ, `parts`: the optional minus, the first point, the two digit runs, the two trims
and the sign-on-zero clause, over a text read as its SYMBOLS — the minus, the point, a
digit by its value, and `Other` for every character the grammar does not name, because
`parts` refuses a text holding one wherever it stands, so the class is all a theorem needs;
- the WRITE, `render`, and `tryCanonical` / `isCanonical` / `zero` over the two;
- the MAGNITUDES: `aligned` (the fractions padded to one scale on the right, the whole padded
to one width on the left), the right-to-left loops `addMagnitudes` and `subMagnitudes` as
the recursion over the reversed digit list they are (`add_lsb` / `sub_lsb`, the carry and
the borrow carried as the loop carries them), and `sign (CompareOrdinal ma mb)` over two
equal-width digit strings as the lexicographic order of their digits (`lex`);
- `compare` and `add`, each as `parts` on both texts and then its `Some` arm over the two
records (`compare_parts` / `add_parts`), which is the shape the F# has.

`tryToFloat` is NOT modelled: it is the one place the type rounds, and it is the aggregates'
business (D72 K7). Nothing here opens another module; the list helpers are restated and the
extraction shares only `Prims.fs` and the option shim with the models beside it.

**Proved** (11)

- <a id="row-decimal-text-canonical-idempotent"></a>**`decimal-text-canonical-idempotent`** — `canonical_idempotent` · fuaran-core#279

  `DecimalText.tryCanonical` is idempotent: a text it writes, it reads back to that same text. Proved through the two halves of the canonical form: every record `parts` returns is NORMAL (`parts_normal` — no leading zero on the integer digits, no trailing zero on the fraction, no sign on zero) and `parts (render p) == Some p` for every normal `p` (`render_parts` — the rendered text is in the grammar and both trims are the identity on it). `render` is therefore injective on normal records (`render_injective`): two canonical texts that agree are one record.

  _With:_ `parts_normal`, `render_parts`, `render_injective`.

  _Boundary:_ Within the model, over its symbol classes; the correspondence to production's .NET strings is the bridge `decimal-text-differential` samples, and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-decimal-text-one-number-one-text"></a>**`decimal-text-one-number-one-text`** — `same_number_iff_canonical` · fuaran-core#279

  D72 K3: two accepted texts denote one number if and only if their canonical forms are one text. The number a text denotes is the rational `numer / 10^scale`, carried as its numerator at a common scale (`denotes p m`, with `denotes_scale` saying the scale chosen reads nothing into it). The arithmetic core is `normal_injective`: a normal record is DETERMINED by its numerator at the common scale — equal numerators force equal signs, then equal scales (a fraction without a trailing zero is not a multiple of ten, `scaled_not_multiple`), then equal digit runs (`mag_eq_normal`, through the bound `mag fp < 10^scale` and the injectivity of a digit run's value at a fixed length). Everything a consumer does by string comparison on a decimal column — grouping, distinctness, the distinct token, the capture key, a join — rests on this theorem.

  _With:_ `normal_injective`, `denotes_scale`.

  _Boundary:_ Over accepted texts only, within the model and its symbol classes; the correspondence to production's .NET strings is the bridge `decimal-text-differential` samples, and the extractor's (premise `extractor-and-compiler-trusted`). Nothing is claimed here of the column layer's USE of the module (`Column.aggregate`, the distinct token, the capture key); the models beside it state those in their own terms.

- <a id="row-decimal-text-zero-unique"></a>**`decimal-text-zero-unique`** — `zero_unique` · fuaran-core#279

  Zero has exactly one canonical text, `"0"`: an accepted text whose numerator is zero canonicalises to `zero_text`, whatever sign, leading zeros, point or trailing zeros it was written with (`-0.00` included), and `zero_text` reads as the zero record (`zero_text_parts`). It is the no-sign-on-zero clause of D72 K3 stated on its own, because it is the one place the canonical form drops information a reader might think it keeps.

  _Boundary:_ Over accepted texts only, within the model; the correspondence to production is the bridge `decimal-text-differential` samples, and the extractor's (premise `extractor-and-compiler-trusted`). No clause was added for it, and none was needed: `parts` drops the minus on a zero magnitude in BOTH arms (`-0` and `-0.00` alike), and `add` drops it again on a cancelled sum. A finding was looked for here and not found.

- <a id="row-decimal-text-order-is-the-numeric-order"></a>**`decimal-text-order-is-the-numeric-order`** — `compare_denotes` · fuaran-core#279

  `DecimalText.compare` answers the SIGN OF THE DIFFERENCE of the two numbers denoted, read at the common scale: `compare a b == Some (sign (denotes pa m - denotes pb m))` for every pair of accepted texts. Proved clause for clause: a sign that differs decides (a negative normal record is strictly negative, `numer_sign`); otherwise `aligned` writes each magnitude as the absolute value of its numerator at the common scale (`aligned_spec`) and the ordinal order of two equal-width digit strings is the order of the numbers they write (`lex_sign`, through the least-significant-first comparison `cmp_lsb_sign` and `lex_rev`). Canonical or not: `1.50` and `1.5` compare equal because they denote one number.

  _Boundary:_ Over accepted texts only, within the model; that `CompareOrdinal` over equal-width digit strings is the model's `lex` is held by the sample in `decimal-text-differential`, not proved, as is the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-decimal-text-order-total"></a>**`decimal-text-order-total`** — `compare_transitive` · fuaran-core#279

  `compare` is a TOTAL ORDER on accepted texts that answers 0 exactly where the canonical forms are one text. Transitivity (`compare_transitive`) lifts three pairwise answers to a three-way common scale, where `scale_up` preserves strict order; antisymmetry (`compare_antisymmetric`) is the sign of the negated difference; and `compare_zero_iff_canonical` is `compare_denotes` joined to `same_number_iff_canonical`, so the equality `compare` reports is the string equality the column layer uses, and neither can disagree with the other.

  _With:_ `compare_antisymmetric`, `compare_zero_iff_canonical`.

  _Boundary:_ On accepted texts only, within the model; the correspondence to production is the bridge `decimal-text-differential` samples, and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-decimal-text-sum-is-the-sum"></a>**`decimal-text-sum-is-the-sum`** — `add_denotes` · fuaran-core#279

  THE SUM IS THE SUM. For every pair of accepted texts `add a b` is `Some` of the render of a normal record whose scale is at or below the common scale and whose numerator at that scale is the sum of the two numerators (`add_parts_denotes`, lifted to the texts by `add_denotes`). Proved clause for clause over production's arms: equal signs add the aligned magnitudes (`add_magnitudes_mag` — the digit-wise sum with its carry, `add_lsb_value`, writes the sum); differing signs subtract the smaller from the larger as `lex` orders them (`sub_magnitudes_mag` — the digit-wise difference with its borrow, `sub_lsb_value`, writes the difference and the borrow it discards is zero over `a >= b`) or write zero when they are equal; the `PadLeft(scale + 1)` and the split at the point move no digit's value; and the two trims change the numerator only by the scale they remove. Nothing is rounded and nothing overflows.

  _With:_ `add_lsb_value`, `sub_lsb_value`, `sub_magnitudes_mag`.

  _Boundary:_ The model states the no-rounding, no-overflow claim by computing in unbounded integers; over accepted texts only, within the model, with the correspondence to production the bridge `decimal-text-differential` samples and the extractor's (premise `extractor-and-compiler-trusted`). `subMagnitudes` discards the borrow its last step leaves, which would be a defect in general and is not one here: production reaches it only past the `CompareOrdinal` that orders the magnitudes, and `sub_magnitudes_mag` is stated, and needed, over exactly that premise. `tryToFloat` is not modelled: it is the one place the type rounds, and is the aggregates' business (D72 K7).

- <a id="row-decimal-text-sum-canonical"></a>**`decimal-text-sum-canonical`** — `add_canonical` · fuaran-core#279

  `add` always answers canonical text: whatever its two inputs were written as, the sum is a text `isCanonical` accepts, because the record it renders is normal (the trims and the sign-on-zero clause are applied by the arm itself, and `render_parts` reads it back). It is the property that lets a `Sum` over a decimal column be a `Decimal` cell without a second canonicalisation.

  _Boundary:_ For accepted inputs (off the grammar `add` answers `None`), within the model; the correspondence to production is the bridge `decimal-text-differential` samples, and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-decimal-text-sum-commutative"></a>**`decimal-text-sum-commutative`** — `add_commutative` · fuaran-core#279

  `add a b == add b a` as TEXTS, over every pair (both `None` off the grammar). It follows from the sum theorem and from one number having one canonical text: the two sums are normal records with one numerator at one common scale, so `normal_injective` makes them one record and `render` one text. The algebraic laws are corollaries of the canonical form, which is the point of proving it rather than sampling it.

  _Boundary:_ Within the model; the correspondence to production is the bridge `decimal-text-differential` samples, and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-decimal-text-sum-associative"></a>**`decimal-text-sum-associative`** — `add_associative` · fuaran-core#279

  Over three accepted texts `(a + b) + c` and `a + (b + c)` are one text: each inner sum is accepted (it is canonical), and both outer sums denote one number at the three-way common scale — `denotes_scale` lifts each pairwise sum to it and `scale_up_plus` distributes the lift over the sum — so `normal_injective_at` (the witness scale cancelled down by `scale_up_inj`) makes the two records one. A fold of `add` over a decimal column therefore sums the column whatever order it visits the cells in.

  _Boundary:_ Over accepted texts only, within the model; the correspondence to production is the bridge `decimal-text-differential` samples, and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-decimal-text-sum-zero-identity"></a>**`decimal-text-sum-zero-identity`** — `add_zero_identity` · fuaran-core#279

  `add a "0" == tryCanonical a` for every text: adding zero canonicalises and does nothing else. The zero record has numerator zero at every scale, so the sum denotes `a`'s own number at `a`'s own scale and `normal_injective_at` identifies it with `a`'s canonical record. With `zero_unique`, any text that denotes zero is the same identity.

  _Boundary:_ Within the model; the correspondence to production is the bridge `decimal-text-differential` samples, and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-decimal-text-refusal-exact"></a>**`decimal-text-refusal-exact`** — `refusal_exact` · fuaran-core#279

  D72 K4: the accepted set is EXACTLY the grammar `-?[0-9]+(\.[0-9]+)?`, stated as a recogniser over the symbols (`grammar`, through `intpart` and `frac`) that shares nothing with `parts`, and `parts` accepts a text if and only if the recogniser does (`parts_iff_grammar`, via `intpart_split`: the first-point split and the two digit-run checks are the regular expression). Each of `tryCanonical`, `compare` and `add` answers `None` exactly off it — `compare` and `add` exactly when either input is off it — so a leading `+`, a bare point, an exponent, a separator, white space, a second point, an empty text and a lone sign are refused by every function alike, and nothing else is.

  _With:_ `parts_iff_grammar`.

  _Boundary:_ Over the model's symbol classes; how a .NET string is read as those classes is the bridge `decimal-text-differential` samples, not proved, and the extraction is the extractor's (premise `extractor-and-compiler-trusted`).

**Tested** (1)

- <a id="row-decimal-text-differential"></a>**`decimal-text-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#279

  The extracted decimal-text model agrees with the shipped `DecimalText.tryCanonical`, `compare` and `add` over a generated pool: texts shaped to reach every clause (leading and trailing zeros to trim, a sign on zero to drop, bare and fractional forms, scales and widths that differ within a pair, magnitudes of twenty-five digits, and the refused forms D72 K4 names, one each) — the canonical form text for text, the order sign for sign, the sum text for text, and `None` on exactly the pairs the model refuses, with the pool asserted to reach the opposite-sign arm, the alignment and a cancellation to zero. The bridge is a .NET string read as the model's symbol classes and the model's symbols written back. Two go-reds, one per theorem family: a canonicaliser that keeps the written scale (K3 with one clause missing) and an ORDINAL comparator over canonical text both lose against the model on the same pools production does not lose on. A last case reads the theorems' statements at the shipped code — idempotence, zero as the identity under any spelling of zero, antisymmetry, 0 exactly where the canonical forms agree, the sum canonical and commutative — so a reader can see what each says without the model open.

  _Boundary:_ Agreement over the sampled pool only, never over all texts. The bridge is where the two host facts the model does not state enter — that `'0'..'9'` are the ten consecutive code units `int c - int '0'` reads, and that `string` on a digit writes the character `of_digits` stands for — and `CompareOrdinal` over equal-width digit strings being the model's `lex` is held only by the sample. The last case is a sample of the shipped seam, not the differential.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `Normalize`

[`Normalize.fst`](Normalize.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `DagFold`, `TreeOps`, `Preservation`, `TreeDiff`.

> Normalize.fst — the model of `Ops.normalize` (Phase 305), the structural peephole that
> collapses adjacent redundant operations without the tree.

**What is modelled.** `src/Fuaran.Core.Ops/Ops.fs`, `normalize`, clause for clause, as the single
left fold with an output stack it has been since Phase 305: `collapse` is the six-row table
that says what a committed top and an incoming operation net to (`None` keeps both, `Some []`
drops both, `Some [y]` replaces the top with `y`), `push` is the loop that collapses against
the top for as long as it collapses, `norm_go` is the fold, and `norm_step` is the one
dispatch — a `Batch` is normalised inside first and dropped when empty. The script alphabet,
`apply` and `apply_all` are `TreeOps`'s; the per-operation facts about the tree are
`Preservation`'s; two trees that agree node for node are the same tree by
`TreeDiff.tree_ext`, which is why that module is cited rather than restated here.

**Proved** (3)

- <a id="row-normalize-preserves"></a>**`normalize-preserves`** — `normalize_preserves` · fuaran-core#305

  `Ops.normalize` (a single left fold with an output stack) never changes what a script does: for a script that APPLIES to a well-formed tree, `applyAll (normalize s) t = applyAll s t` — the defining law `Conformance.normalizeLaws` samples, as a theorem over the whole skeleton alphabet with `Batch` nested to any depth. The model is clause for clause — the six-row collapse table (insert/remove cancel, move/move, reorder/reorder, rewrite/rewrite, insert/rewrite, rewrite/remove), the push loop that re-examines the new top after every collapse, the fold, and the `Batch` arm that normalises inside and drops an empty one — and the proof is one stack invariant carried through `push` by one lemma per row: three syntactic (`ins_rem_inverse`, `upd_upd`, a rewrite pushed through an insert) and three extensional (the two routes reach a tree whose every node has the same children and kind, so `tree_ext` makes them one tree).

  _With:_ `ins_rem_cancels`, `move_move_sound`, `reorder_reorder_sound`, `upd_upd_sound`, `ins_upd_sound`, `upd_rem_sound`, `push_sound`, `norm_go_sound`, `upd_over_ins`, `upd_root`.

  _Boundary:_ Both hypotheses are the function's own: the law is about an applyable script because a cancelled pair can turn a REFUSED script into an accepted one (`cancellation_can_admit_a_refused_script` pins it — normalise after validating), and the move, reorder and rewrite-remove rows are sound only because the node they address occurs once, which is `wf`. A rewrite's content is the kind tag at the witness level, so "the last rewrite wins" is proved about the kind and reaches the whole content only through the witness laws (`lawful-abstract-witness`). Nothing is claimed about a script that does not apply beyond that counterexample.

- <a id="row-normalize-idempotent"></a>**`normalize-idempotent`** — `normalize_idempotent` · fuaran-core#305

  `Ops.normalize` is idempotent, unconditionally: `normalize (normalize s) = normalize s` for every script. The output is STABLE — no adjacent pair collapses, no `Batch` is empty or un-normalised (`normalize_stable`, a stack invariant `push` keeps because every replacement the table emits is a leaf operation) — and the fold is the identity on a stable script (`normalize_fixed`), which is what the doc comment's `normalize ∘ normalize = normalize` claims and `ContentDiffTests`' differential samples.

  _With:_ `push_sstable`, `norm_go_sstable`, `stable_rev`, `norm_go_fixed`, `collapse_leaf`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Not claimed: that `normalize` is a CANONICAL form. Two scripts with the same effect need not normalise to the same script (DECISIONS D86); the table is a peephole over ADJACENT pairs, and a collapsible pair with an unrelated op between them is left alone by design.

- <a id="row-normalize-never-longer"></a>**`normalize-never-longer`** — `normalize_never_longer` · fuaran-core#305

  `Ops.normalize` never lengthens a script: `len (normalize s) <= len s`, unconditionally — a push adds at most one operation to the stack whatever it collapses on the way down, and a `Batch` contributes at most itself. The third of the doc comment's three promises, stated so that the fold's output can be bounded by its input without reading the table.

  _With:_ `push_len`.

  _Boundary:_ Within the model; the correspondence to production is the differential's and the extractor's (premise `extractor-and-compiler-trusted`). Nothing is claimed about cost: linearity is measured by `ContentDiffTests`' scale test, not proved.

**Tested** (1)

- <a id="row-normalize-differential"></a>**`normalize-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#305

  The extracted peephole agrees with the shipped `Ops.normalize`, op for op, over scripts drawn by `ContentDiffTests`' generator with ADJACENT-COLLAPSE BIAS — an accepted op is often followed by the op that collapses with it, so every row of the table is reached, and the case fails if fewer than a quarter of the scripts collapsed — through the bridge under which production's `ReplaceChildren n' (Children n)` and the model's `TNode (tid n') (kind n') (kids n)` are one value. On the model side the three theorems are instantiated per sample: the normalised script reaches the tree the script reached, is a fixed point, and is no longer. The go-red is a bridge that renames the remove of a cancelling pair and nothing else: it must lose on every script carrying such a pair and still agree on every script without one, so the comparison is shown to see exactly the two cancelling rows.

  _Boundary:_ Agreement is over the pool drawn, never over all inputs. The production law itself is sampled by `Conformance.normalizeLaws`, at this level since fuaran-core#23.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`lawful-abstract-witness`](#row-lawful-abstract-witness) (assumed).

**Inherited through the models it opens:** [`independence-diamond`](#row-independence-diamond) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed), [`witness-surface-scope`](#row-witness-surface-scope) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `VocabularyVectors`

[`VocabularyVectors.fst`](VocabularyVectors.fst) — generated by `Fuaran.Core.Idl.Codegen`'s F\* target and held to a fresh generation. Opens `WireDecode`, `Vocabulary`, `DocVocabulary`, `ScoreVocabulary`.

> GENERATED by Fuaran.Core.Idl.Codegen — F\* vectors. Do not edit by hand: regenerate it from its inputs.
> vocabulary: sha256:587bb7678b2b29ba684110454ffbc42eb3f4bad98388f4cdd0d8ad4523627a36
> vocabulary: sha256:48d2a518690b3f650a2a80e6fed48a571996ff40df48a630fd3a0f35f44927dc
> vocabulary: sha256:8834c0249dad3698aebf9163acb71ffd35543a7d0c8df0dd45510f173b632dc2
> emission: sha256:7cbc3a4a904d6668ee5d8ec24c618d5db306a90ae9a7a57c749a21e61baa1732

**Proved** (1)

- <a id="row-vocabulary-vectors-agree"></a>**`vocabulary-vectors-agree`** — `r_vectors_agree` · fuaran-core#303

  The GENERATED vocabulary models and the REFERENCE INTERPRETER write the same object for every vector the three-way differential carries into F\*: `proofs/VocabularyVectors.fst`, emitted by `FStarTarget.vectorsModule` from the test project, holds `d_vectors_agree`, `s_vectors_agree` and `r_vectors_agree` — one per certification model (`DocVocabulary`, `ScoreVocabulary`, `Vocabulary`) — each a sequence of `assert_norm (enc_node v == e)` facts, where `v` is the model value of what the interpreter DECODES a vector's canonical bytes to and `e` is the `jval` those bytes PARSE to, member for member and key for key, at the instantiation `num = int`, `flt = string` (a float carried as its canonical layout). The vectors are the differential's own seeded draws, drawn and adversarial, twelve per model, hostile map keys sorted first; a vector the model cannot express (a kind the model refuses, a non-finite float, a string F\* cannot spell, a transparent union) is REFUSED by the target naming it, never dropped, and the test project selects among the admitted ones. The facts are discharged by the NORMALISER, not by Z3: a model encoder that put a member under another key, in another order, or a presence rule the interpreter does not follow fails the module (measured: one renamed key in one expected value fails it with `Assertion failed`). Proved with no assume and no admit under --report_assumes error. Held to a fresh generation by `IdlThreeHostTests`.

  _Boundary:_ NOT CLAIMED: the float LAYOUT (the model's `flt` is opaque, so the facts carry the canonical layout as a string on both sides) and the WIRE_FORMAT section-7 sentinels (a non-finite float is spelled on the wire as a string the model's float slot does not carry); both are covered by the three-way differential over the interpreter, the compiled generated F# and the generated TypeScript, which is a TEST, not this row. A fact is about its vector: these are instances of the round trip `rt_node` proves universally, pinned to the bytes the interpreter actually writes.

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

### `PropagationOps`

[`PropagationOps.fst`](PropagationOps.fst) — checked only; its production side cannot be run beside an extraction (`check.ps1` says why). Opens `DagFold`, `TreeOps`, `Relation`, `Propagation`.

> PropagationOps — the change set of a structural edit is COMPLETE: `Propagation.changedForOp`
> joined with `Ops.apply` (fuaran-core Phase 308).

**What is modelled.** `src/Fuaran.Core.Propagation/Propagation.fs` — the two functions that turn a
structural edit into the change set `evalFrom` replays:

- `touchedBy`, every arm, with Phase 308's two corrections: a `RemoveNode` touches the parent
it removes from and a `MoveNode` the parent it leaves, and a `Batch` resolves each sub-op
against the tree the sub-ops before it produced (`Ops.apply`, threaded; a refused sub-op is
resolved against the tree in hand and the threading continues from it);
- `changedForOp`, derived from the DIFF of the pre- and post-edit trees: the seeds are every
post-edit id that is new, whose child ids differ, whose reads differ or that the op
content-writes (`Ops.footprint`), every removed id, and `touchedBy`; closed over the PRE-edit
dependency graph (`Propagation.dirtyFromChangedIds` over `dependencyMap`, here
`Propagation.dirty_from_changed_ids` over `deps_of`) and restricted to the survivors.

The tree, the ops, `apply` and the footprint are `TreeOps.fst`'s, clause for clause with
`Ops.apply`; the closure is `Propagation.fst`'s. A node's CONTENT is what the witness shows of it,
its kind tag, exactly as `TreeOps.fst` reads `UpdateNode`. The reads a node declares are a
PARAMETER (`rd`, F#: the caller's `readsOf`), any function of the node and its subtree.

**Proved** (5)

- <a id="row-propagation-changed-for-op-complete"></a>**`propagation-changed-for-op-complete`** — `changed_for_op_complete` · fuaran-core#308

  The change set of a structural edit is COMPLETE. `changed_for_op` is `Propagation.changedForOp` clause for clause — seeded from the DIFF of the pre- and post-edit trees (every new post-edit id, every id whose child ids or declared reads differ, every id the op content-writes, every removed id, and `touchedBy` with its Remove/Move arms and its Batch threaded through the intermediate trees), closed over the pre-edit dependency graph and restricted to the survivors — over `TreeOps.fst`'s tree and `apply`. For an op `apply` accepts on a well-formed tree, `Batch` included at any depth, every surviving node that is new, whose content (the kind the witness shows), child ids or declared reads differ, and every surviving node that declared a read of a removed id, is in the change set. The one clause that needs the op is `kind_kept`: content moves only where the op content-writes, proved over `apply` and threaded through a batch with no well-formedness needed at the intermediate trees. This is the premise `evalfrom_agrees` takes of a change set (`agree_off` / `touches_off`).

  _Boundary:_ Holds for ops `apply` accepts on a well-formed tree, and within the model (the model-to-code step is `propagation-ops-model-bridge`). The premise serves only evaluators that are a function of a node's own content, its child ids and its resolved declared reads; an evaluator that reads a node's subtree without declaring the reads is outside that class. NOT claimed: that the kind tag the witness shows is all the content a domain's encoder distinguishes (`Ops.apply` rewrites content only at an `UpdateNode` target or an inserted subtree, both named by the footprint); or that the post-edit tree is right for an op some OTHER engine (contained, keyed) accepted, which holds only where `apply` accepts the op too and lands on the same tree. Over an ill-formed tree nothing is stated, because `find` resolves an id held twice to its first occurrence and the two ends can disagree about which node it means.

- <a id="row-propagation-kind-kept"></a>**`propagation-kind-kept`** — `kind_kept` · fuaran-core#308

  On a well-formed tree, an op `Ops.apply` accepts changes the content of a node that survives it ONLY where the op content-writes that node (`Ops.footprint`'s `ContentWrites`), proved per arm and through `Batch` by the step lemma `kinds_kept` — no kind for an id the op does not content-write appears that the tree before it did not have. It is what lets `changedForOp` see content change without a content accessor on the witness.

  _Boundary:_ Needs the start tree well-formed, which is what pins one kind per id, and the op accepted by `apply`; speaks of surviving nodes only. Within the model; the model-to-code step is `propagation-ops-model-bridge`.

- <a id="row-propagation-dirty-from-op-complete"></a>**`propagation-dirty-from-op-complete`** — `dirty_from_op_complete` · fuaran-core#456

  THE DIRTY SET OF AN OP IS COMPLETE. `dirty_from_op` is `Propagation.dirtyFromOp` clause for clause: the seeds `changedForOp` closes over — read off the DIFF of the pre- and post-edit trees, with `touchedBy` — closed over the PRE-edit dependency graph (`deps_of`, `Propagation.dependencyMap`) and never restricted, the post-edit tree computed by `apply`. For an op `apply` accepts on a well-formed tree, `Batch` included at any depth, every surviving node that is new or whose content, child ids or declared reads differ, every id the edit removed, and every pre-edit node that declared a read of a removed id — survivor or not — is in the set (`dirty_from_op_complete`), and `changedForOp` is this set restricted to the survivors (`changed_for_op_is_dirty_from_op`). The set includes a node whose declared reads the edit moved with no container of it touched — a node reading every leaf below it, with a leaf inserted two levels down, whose value can differ though it reads nothing the op touched — and the differential reaches that case and holds production to this statement.

  _Boundary:_ Completeness is stated for ops `apply` accepts on a well-formed tree; over an op `apply` refuses there is no post-edit tree to diff, and the closure is over `touchedBy` alone. Within the model; production is held to it by the differential (`propagation-ops-model-bridge`). That a refused op closes over `touchedBy` alone is pinned by the differential's go-red case (a root moved under its own descendant), not proved.

- <a id="row-propagation-dirty-from-op-least"></a>**`propagation-dirty-from-op-least`** — `dirty_from_op_least` · fuaran-core#456

  THE DIRTY SET IS THE LEAST CLOSED SET OVER ITS SEEDS. `dirty_from_op_least` and `changed_for_op_least`: any set that holds the seeds and is closed under "reads" over the pre-edit graph holds the whole dirty set, and the whole change set — `Propagation.fst`'s `dirty_least` at the op's seeds, so nothing is dirty that the seeds and the declared dependencies do not reach.

  _Boundary:_ It does NOT say that every seed moved: `touchedBy` and the op's content writes name a moved node's target whether or not its kind, children or reads differ, so the seeds over-approximate the moved set by design (D108: never missing a reader), and the bound is minimality over the seeds, stated so.

- <a id="row-propagation-evalfrom-agrees-for-op"></a>**`propagation-evalfrom-agrees-for-op`** — `evalfrom_agrees_for_op` · fuaran-core#456

  INCREMENTAL EVALUATION DRIVEN FROM AN OP AGREES WITH FULL EVALUATION, WITH NO CHANGE-SET PREMISE. `evalfrom_agrees_for_op`: for an evaluator in the STRUCTURAL class — `structural g t`, a function of the node's kind, its child ids and the resolver it is handed, reading its node off the tree and asking only its declared reads, whose witness is therefore the map's own declaration (`declared rd t`) — `evalFrom` from `changedForOp` over the post-edit map equals `eval` of the post-edit tree. The two premises `evalfrom_agrees` takes of a change set are DISCHARGED: `agree_on` (off the change set the old and new evaluators are the same function at every id the walk visits — `structural_off`, which is `changed_for_op_names_moved` read backwards) and `checks_on` (the declared witness passes the declared-reads check wherever the old one did — `first_undeclared_of_declared`, in whatever order the reads are listed); `Propagation.fst`'s theorem is stated over them (`evalfrom_agrees_on`, with `evalfrom_agrees` its corollary), because `agree_off` quantifies over EVERY id and no evaluator read off a tree the edit removed a node from can meet it there.

  _Boundary:_ Only for evaluators in the structural class. The theorem INHERITS `evalfrom_agrees`'s one-map shape: the prior is the old evaluator's values over the post-edit map and order, or fewer of them. So it speaks for an edit after which every node's declared reads are where they were (a content rewrite, a reorder, a move that reads nothing new), with the prior `propagation-change-set-and-prior`'s remaining clause names. Carrying a prior from a walk over the OLD map across an edit that moves the map needs what no theorem here states — that a full evaluation's values do not depend on which certified order produced them — and that is the open step, named on that row.

**Tested** (1)

- <a id="row-propagation-ops-differential"></a>**`propagation-ops-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#308

  Production's `changedForOp` and `dirtyFromOp` are held to the statements of `changed_for_op_complete` and `dirty_from_op_complete` over generated trees whose nodes declare reads (present, dangling or absent; a `sum` node also reads every leaf below it, the class whose declared reads an edit below it moves with no container of it touched) and generated ops of EVERY kind — insert, remove, move, reorder, a content-and-reads update, a Batch drawn against one pre-state and a Batch with a Batch nested in it: for every op `Ops.apply` accepts, every survivor whose content, child ids or declared read moved, and every survivor that read a removed id, is in the change set; every removed id and every pre-edit reader of one is in the dirty set, and the change set is the dirty set restricted to the survivors; and `evalFrom` from the survivors' prior over that change set equals `eval` of the post-edit tree under an evaluator that is a function of a node's own content, child count and resolved declared reads. The case asserts it drew and accepted every kind, nested Batch included, reached a reader of a removed id, and reached a survivor whose declared reads moved with no container of it touched. Measured red with the change set's seeds cut to the removed ids, and red both ways with `dirtyFromOp`'s seeds cut back to `touchedBy`.

  _Boundary:_ Agreement is over the pools drawn, never over all inputs; the `evalFrom` agreement is sampled only under an evaluator that is a function of a node's own content, child count and resolved declared reads.

**Rests on, or is bounded by:** [`propagation-change-set-and-prior`](#row-propagation-change-set-and-prior) (assumed), [`propagation-ops-model-bridge`](#row-propagation-ops-model-bridge) (assumed).

**Inherited through the models it opens:** [`independence-diamond`](#row-independence-diamond) (assumed), [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed), [`propagation-evaluator-total`](#row-propagation-evaluator-total) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `TreeFrame`

[`TreeFrame.fst`](TreeFrame.fst) — checked only; its production side cannot be run beside an extraction (`check.ps1` says why). Opens `DagFold`, `TreeOps`, `Preservation`, `TreeDiff`.

> TreeFrame — the FRAME of the skeleton-op tree algebra: where an applied op writes, that the
> write gate's targets contain it, and that a gated apply leaves a locked id alone
> (fuaran-core Phase 362).

**What is modelled.** Nothing new about the algebra: the tree, the ops, `apply` and the footprint are
`TreeOps.fst`'s, clause for clause with `Ops.apply` and `Ops.footprint`, and the per-node view
(`kids_at`, `kind_at`) and what each edit does to it are `Preservation.fst`'s. Added here, clause
for clause with `src/Fuaran.Core.Ops/WriteGate.fs` as Phase 318 landed it:

- `targets_of_one` / `targets_go` / `targets_of` — `WriteGate.targetsOfOne` and `targetsOf`:
the footprint's structure, content and unknown-parent writes and its slot writes' nodes,
plus the subtree a `RemoveNode` destroys, read from the tree; a `Batch` flattened onto the
work list and taken op by op over the tree the ops before it produced, stopping at the first
op that does not apply;
- `chain_of`, `decide_one`, `decide_go` / `decide`, `apply_gated` — `chainOf`, `decideOne`,
`decide` and `applyGated`, with `path_in` for `Tree.path`.

WHAT "WRITTEN" MEANS, once. `written x t t'`: what the witness shows AT the id `x` differs
between the two trees — its presence, its content (the kind tag, as `TreeOps.fst` reads
`UpdateNode`) or its ordered list of child ids. That is the entry `Conformance.writeGateLaws`'
cover law compares (`Map.tryFind id before <> Map.tryFind id after` over each node's child-id
list), with the content beside it. An id's PARENT and its PLACE AMONG ITS SIBLINGS are not
separate clauses: they are the parent's child list, and `placement_is_the_parents_entry` proves
the reading loses nothing — an id whose parent or sibling order differs has a written parent.
The definition is over the model's own tree and takes no parameter.

**Proved** (5)

- <a id="row-tree-apply-frame"></a>**`tree-apply-frame`** — `apply_frame` · fuaran-core#362

  THE FRAME OF ONE OP. Over `TreeOps.fst`'s tree, `apply` and `op_fp` — `Ops.apply` and `Ops.footprint` clause for clause — an id is WRITTEN by an apply when what the witness shows at it differs between the tree before and the tree after: its presence, its content (the kind tag) or its ordered child ids (`written`, defined over the model's own tree with no parameter; an id's parent and its place among its siblings are its parent's child list, and `placement_is_the_parents_entry` proves an id whose parent or sibling order differs has a written parent). For every op and every well-formed tree at which it applies, every written id is an address the footprint names — a structure, content or unknown-parent write, or a slot write's node — or is one of the two tree facts an unknown-parent write stands for, at the tree the op lands on: the source parent of a removed or moved node, or an id of the subtree a removal destroys (`unnamed`; `unnamed_is_resolved` ties each to an id the footprint carries as an unknown-parent write). This covers every op kind the model carries: the graft an insert introduces is named, the subtree a removal destroys is not and is read from the tree. Not conditional on a parameter. Measured red with the insert's content writes dropped from the model's footprint.

  _Boundary:_ Needs a well-formed tree at which the op applies; the source parent and destroyed subtree are bounded through the tree the op lands on, not the footprint alone. What this row does NOT say: that the footprint alone bounds the writes of a relocating op — `tree-named-frame` proves where that holds and where it fails. Not covered: keyed positions and container capability, as for `TreeOps.fst`'s `apply`.

- <a id="row-tree-batch-frame"></a>**`tree-batch-frame`** — `batch_frame` · fuaran-core#362

  THE FRAME OF A SCRIPT. The same statement as `tree-apply-frame` for a `Batch`, at any depth, by the fold the footprint is defined by: every id written between the tree a script starts from and the tree it reaches is named by the union of its ops' footprints (`fp_all`), or is the source parent or destroyed subtree of an unknown-parent write of one of its ops, resolved at the tree THAT op landed on (`unnamed_all` threads `apply` as `fp_all` folds `op_fp`). A write one step makes and a later step undoes is not a write of the script; `written` composes (`written_trans`). Measured red with the unnamed half left unthreaded.

  _Boundary:_ Inherits `tree-apply-frame`'s premises at every step: well-formedness of the intermediate trees is `apply_preserves_wf`'s, and the unnamed writes are resolved at the tree each op landed on, not at the script's start tree; the footprint union alone does not bound a relocating op's writes (`tree-named-frame`).

- <a id="row-tree-named-frame"></a>**`tree-named-frame`** — `named_frame` · fuaran-core#362

  THE PURE-FOOTPRINT FRAME, WHERE IT HOLDS. An op whose footprint carries no unknown-parent write — inserts, reorders and nests of them — writes ONLY at addresses its footprint names, with no tree read: the bound a consumer that holds footprints and no tree may use.

  _Boundary:_ For a relocating op the pure form is FALSE, proved with witnesses at one well-formed tree: a removal destroys a descendant its footprint does not name and rewrites the child list of a parent it does not name (`pure_frame_fails_for_a_remove`), and a move rewrites its source parent's (`pure_frame_fails_for_a_move`). These are `Ops.footprint`'s two pinned over-approximations, which `Ops.independent` already serialises; the footprint is not, by itself, an upper bound on a relocating op's writes. That they are necessary over the `Footprint` record is `footprint-precision-ceiling`'s.

- <a id="row-write-gate-targets-cover-written"></a>**`write-gate-targets-cover-written`** — `targets_cover_written` · fuaran-core#362

  THE WRITE GATE'S TARGETS COVER THE WRITES. `targets_of` is `WriteGate.targetsOf` clause for clause — the footprint of the one-op script (structure, content and unknown-parent writes and the slot writes' nodes) plus the subtree a `RemoveNode` destroys, read from the tree; a `Batch` flattened onto the work list and taken op by op over the tree the ops before it produced, stopping at the first op that does not apply. For every op `apply` accepts on a well-formed tree, `Batch` included at any depth, every written id — created, destroyed, rewritten, or its child list changed — is a target, or is the parent, in the tree the op was applied to, of a target. This is the statement `Conformance.writeGateLaws`' cover law samples at a domain's witnesses, with content added. Measured red with the destroyed-subtree clause dropped from the model.

  _With:_ `parent_inv`.

  _Boundary:_ Only for ops `apply` accepts on a well-formed tree. The second case is the source parent of a removed or moved node, the one written id `targetsOf` does not name. Within the model; that production computes `targets_of` is assumed (`write-gate-model-bridge`). The slot-write clause is carried only because `targetsOfOne` carries it: no skeleton op writes a slot, so it is empty for every footprint `op_fp` produces.

- <a id="row-write-gate-respects-lock"></a>**`write-gate-respects-lock`** — `gated_apply_respects_lock` · fuaran-core#362

  A GATED APPLY LEAVES EVERY LOCKED SUBTREE AS IT WAS. `decide_one`, `decide` and `apply_gated` are `WriteGate.decideOne`, `decide` and `applyGated` clause for clause, with `chain_of` for `chainOf` and `path_in` for `Tree.path`; the model decides the verdict. When `apply_gated` succeeds on a well-formed tree, for every locked id `find_in` answers the same before and after: a locked node the tree held is still there with its whole subtree unchanged — nothing in it rewritten, reordered, added to, moved out or removed, on its own or inside a subtree a removal destroyed — and a locked id the tree did not hold was not created (`nothing_under_a_lock_is_written` is the reading at each id under a lock). Measured red with the gate not consulted.

  _With:_ `chain_has_parent`, `ancestor_on_path`.

  _Boundary:_ Holds when `apply_gated` succeeds on a well-formed tree; which of several offending targets is named first is production's `Set` order, not the model's. What this row does NOT say, each proved as a witness: a lock does not pin the locked node's place under an UNLOCKED parent, whose child order is the parent's state (`lock_does_not_pin_sibling_place`); and no such theorem is stated for an allow-list, because removing or moving an allowed node rewrites the child list of a source parent the list does not cover (`allow_list_does_not_cover_the_source_parent`). Within the model; the code step is `write-gate-model-bridge`. Whether the allow-list should also require the source parent is a question about `WriteGate`, not one this model settles.

**Rests on, or is bounded by:** [`write-gate-model-bridge`](#row-write-gate-model-bridge) (assumed).

**Inherited through the models it opens:** [`independence-diamond`](#row-independence-diamond) (assumed), [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`tree-algebra-well-formed-states`](#row-tree-algebra-well-formed-states) (assumed), [`witness-surface-scope`](#row-witness-surface-scope) (assumed), [`index-node-is-its-record`](#row-index-node-is-its-record) (assumed).

### `ColumnRefinement`

[`ColumnRefinement.fst`](ColumnRefinement.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `JsonParse`, `Limits`, `Utf8`, `WireCanon`, `Unit`, `Temporal`, `WireColumn`.

> ColumnRefinement — the typed-vector column is the cell-list column it replaces: a refinement
> theorem that carries the column proof ladder across the representation change (fuaran-core
> Phase 419).

**What is modelled.** The column `src/Fuaran.Core.Column/Column.fs` has held since Phase 417, clause
for clause: `Validity` and `Validity.isPresent`, the `ColumnData` union (one typed vector and a
mask per case — since Phase 420 the MATERIALISED mask, `Validity.toMask` of the column's two-case
`Validity`, whose own model and the three facts that reading rests on are section 7), `Column` with its derived `Type`, `Column.cell`, `Column.toCells`,
`Column.ofCells` with its private `storageOfCells` (the per-type `pick`, the `fill` loop and the
type's zero at an absent row), `ColumnData.Equals` through `ColumnStorage.presentEqual`, the
typed form `Table.firstUncarriableCell` took in that phase (a mask that is not the values'
length first, then one scan per case at the present rows) with `Table.validate` over it, and
`ColumnCodec.columnJson`'s two walks off the typed storage, with `encodeJson` / `tryEncode` over
them. A `Vector<'T>` is read through `Vector.toArray` and nothing else: the model sees a list of
the elements a vector holds, never its storage — no offset, no sharing, no backing array.

**Proved** (6)

- <a id="row-column-refinement-to-cells-of-cells"></a>**`column-refinement-to-cells-of-cells`** — `to_cells_of_cells` · fuaran-core#419

  `toCells (ofCells n ty cs)` IS THE NORMAL FORM OF THE CELLS. `proofs/ColumnRefinement.fst` models the column — `Validity`, the `ColumnData` union, `Column.toCells`, `Column.ofCells` with its per-type `pick`, `fill` loop and the type's zero at an absent row — clause for clause, every `Vector` read through `toArray`. When `ofCells` builds, the cells its column reads back are `WireColumn.norm_cells` of the cells it was handed — the SAME function the codec model's round trip is stated up to, so a widened `Int` is normalised at construction exactly as decode normalises it (`to_cells_of_cells`). It builds exactly when every present cell's type widens into the column's AND every temporal text reads (a date or an instant the integer storage cannot hold is the `MalformedShape` production spells, `Unreadable` in the model), and refuses otherwise — as the `TypeMismatch` naming the column when every text reads (`of_cells_good_iff`); so a list column `validate`'s clause (e) passes is one `ofCells` builds, and reads back as its normal form (`validated_cells_build`). The temporal arms read the text through `Temporal.fst` — `tryDays` to a day count, `tryInstant` at the column's unit to a second and a scaled fraction, with `normalFraction` dropping a fraction vector no present row sets. Measured red with `pick`'s widening arm writing the wrong value.

  _With:_ `of_cells_good_iff`, `validated_cells_build`.

  _Boundary:_ The read-back is the normal form, not the cells themselves, and the theorem rests on the text reading back to itself (`date_text_try_days`, `instant_text_try_instant`). Within the model; the correspondence to production is the differential's (`column-refinement-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-column-refinement-of-cells-to-cells"></a>**`column-refinement-of-cells-to-cells`** — `of_cells_to_cells` · fuaran-core#419

  `ofCells n ty (toCells c)` IS `c`: for every well-formed typed column — its mask the values' length — whose temporal values are in range (`temporal_ok`: a day the canonical form spells, an instant whose second and fraction it spells), `ofCells` of its `toCells` builds a column EQUAL to it under `ColumnData.Equals` (`ColumnStorage.presentEqual`, modelled: one case, one mask, equal at every present row; a timestamp column also one unit and `sameFraction`, the fractions equal at the present rows whether kept or dropped to `None`), and is the column ITSELF where the absent rows hold the type's zero and the fraction vector is in its normal form (`of_cells_to_cells_exact`), which every column `ofCells` or decode builds does (`of_cells_wf_zeroed`). With `column-refinement-to-cells-of-cells` this makes the map from the typed column to the list column a bijection on the columns `validate` accepts, which is what lets every list-level theorem transfer. Measured red with the model's `toCells` ignoring an absent bit, and with `Equals` not comparing the masks.

  _With:_ `of_cells_to_cells_exact`, `of_cells_wf_zeroed`, `of_cells_to_cells_needs_wf`.

  _Boundary:_ Needs temporal values in range: out of range `toCells` renders a text `ofCells` refuses, which is `validate`'s to catch first. Well-formedness is NEEDED and the witness is in the file: a column whose mask is shorter than its values reads its tail as absent and comes back with a longer mask (`of_cells_to_cells_needs_wf`). Equality is `ColumnData.Equals`, and identity only where the absent rows hold the type's zero and the fraction vector is in normal form. Within the model; the correspondence to production is the differential's (`column-refinement-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-column-refinement-validate-agrees"></a>**`column-refinement-validate-agrees`** — `validate_agrees` · fuaran-core#419

  THE TYPED `Table.validate` IS THE LIST MODEL'S, THROUGH THE MAP. `validate_t` is `Table.validate` clause for clause — (a)-(d) over a column's name, type and length, and (e) the typed `firstUncarriableCell`: a mask that is not the values' length first (`LengthMismatch`), then one scan per case at the present rows. On every typed table whose columns are well-formed and whose timestamp columns hold their fractions in `[0, scale)` with a fraction vector, when held, of the seconds' length (`frac_ok`), it answers what `WireColumn.validate` answers of the table's image (`validate_agrees`), and a typed table it accepts IS well-formed with its fractions in range (`validate_t_good_wf`), so the agreement covers every table either validator accepts. The temporal scans agree through `Temporal.fst`: `dateText` is canonical exactly on the range (`date_scan`), and with its fraction in range an instant's text is canonical exactly when its second is (`timestamp_scan`). The one arm the list model has and the typed scan does not — a present cell outside its column's type — is proved unreachable through the map: `toCells` writes a cell of the column's own type at every present row (`first_uncarriable_agree`). Measured red with the scan reading an absent row, and with the mask check dropped.

  _With:_ `validate_t_good_wf`.

  _Boundary:_ Agreement on an arbitrary table needs the `frac_ok` premise: a fraction at or past the scale renders as ANOTHER instant's canonical text, which the typed clause (e) refuses and the list clause cannot see, and the fraction-length clause has no list counterpart. Within the model; the correspondence to production is the differential's (`column-refinement-differential`) and the extractor's (premise `extractor-and-compiler-trusted`). Apart from the timestamp clauses, clause (e)'s mask-length check is the one place the two validators differ, and it is reached only by a column the list model cannot express: a list column has one length. With clause (e)'s `TypeMismatch` arm unreachable through the map, production reaches `validate`'s `TypeMismatch` only through clause (c), a column whose type is not its schema entry's.

- <a id="row-column-refinement-encode-agrees"></a>**`column-refinement-encode-agrees`** — `encode_agrees` · fuaran-core#419

  THE TYPED ENCODER WRITES WHAT THE CELL ENCODER WROTE. `ColumnCodec.columnJson`'s two walks off the typed storage — the element's JSON value at a present row and the absent slot at an absent one, never what the vector holds there, and one validity bit per row — are modelled as the walks, and `encodeJson` / `tryEncode` over them; `encode_agrees` says the typed encoder's `JVal` is the list model's `encode_json` of the table's image on EVERY typed source, valid or not, well-formed or not, because both walk the values' length and read the mask the same way. Measured red with the model writing the stored element at an absent row.

  _With:_ `try_encode_agrees`.

  _Boundary:_ `try_encode_agrees` adds the guard only on the tables the two validators agree about. Within the model; the correspondence to production is the differential's (`column-refinement-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-column-refinement-ladder-transfers"></a>**`column-refinement-ladder-transfers`** — `typed_round_trip` · fuaran-core#419

  `WireColumn`'S TWO THEOREMS, STATED OF THE TYPED COLUMN. The widening half of the codec model's normal form is the IDENTITY on a typed column — an `Int` cell comes only from an `Ints` column, where `norm_cell` changes nothing (`norm_cells_vacuous`) — so the normal form of a typed table moves its column ORDER and nothing else: `normal_table_t`'s columns are the table's own, found by schema name (`normal_columns_are_the_columns`). Composed with the agreements: `typed_round_trip` — for every typed table the typed `validate` accepts, `decode_json (encode_json_t (TEmbedded t))` is `Good (Embedded (to_list_table (normal_table_t t)))`; `typed_round_trip_in_schema_order` — when the columns are already in schema order that is the table itself, the LITERAL round trip, false of the list column three ways and of the typed column one; and `typed_decode_image_is_valid` — a well-formed typed table with its fractions in range whose image is what decode answered is one the typed `validate` accepts; production's decode builds every fraction in range through `tryInstant`. Measured red with the typed normal form dropping a schema entry.

  _With:_ `norm_cells_vacuous`, `normal_columns_are_the_columns`, `typed_round_trip_in_schema_order`, `typed_decode_image_is_valid`, `cell_at_is_nth_to_cells`.

  _Boundary:_ The round trip is up to column order except when the columns are already in schema order; `typed_decode_image_is_valid` needs the fraction-range premise. What stays with the differential: that the typed column production's decode builds through the typed readers has `toCells` equal to the model's decoded cells (`column-refinement-differential`). It inherits `column-int-layouts` and `column-codec-abstractions` unchanged: the host record is the same record. Not claimed: the typed builders and readers (`Column.ofInts` ... `Column.tryDecimals`), which check nothing and convert nothing; `Column.aggregate`; and anything about a `Vector`'s storage, its slices, sharing and the borrow (`column-vector-is-its-contents`).

- <a id="row-column-refinement-validity-two-cases"></a>**`column-refinement-validity-two-cases`** — `same_mask_is_mask_equality` · fuaran-core#420

  A NULL-FREE COLUMN'S `AllValid` IS THE ALL-TRUE MASK OF ITS LENGTH. `Validity` is two cases — `AllValid`, holding no mask, or a `Mask` — and the refinement's sections 1 to 6 read it MATERIALISED (`Validity.toMask`, `Column.mask`). Section 7 of `proofs/ColumnRefinement.fst` models the two cases, the materialisation, the normalising constructors and `ColumnStorage.sameMask`, and proves the three facts that reading rests on: `AllValid` materialises to one set bit per row and no row past the end, so an `AllValid` column is always well-formed (`all_valid_reads_all_true`, `all_valid_wf`); the normalising constructors lose nothing and never build an all-true `Mask` (`of_mask_to_mask`, `of_mask_normal`); and `sameMask`, which compares two validities without materialising either, IS equality of the materialised masks at every length (`same_mask_is_mask_equality`) — so column equality reads `AllValid` and a hand-built all-true `Mask` of the column's length as one value, and nothing else as that value. Measured red, one perturbation per run, with `sameMask` dropping the length check, with the all-true mask writing a clear bit, and with the constructor's normalisation inverted.

  _With:_ `all_valid_reads_all_true`, `all_valid_wf`, `of_mask_to_mask`, `of_mask_normal`.

  _Boundary:_ Within the model; the correspondence to the shipped `Validity` is the differential's (`column-refinement-validity-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

**Tested** (2)

- <a id="row-column-refinement-differential"></a>**`column-refinement-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#419

  The extracted refinement agrees with the shipped column over drawn columns of every type, with nulls and widened cells. Per drawn cell list: `Column.ofCells` beside the model's `of_cells` — the verdict, the refusal's class and column, and the cells of what each built; `Column.toCells` beside the model's `to_cells` of the production column read through its typed reader and `Vector.toArray` (the bridge the theorems are stated over). Per drawn table (the `fuaran-core#306` pools, valid and wild): `Table.validate` beside `validate_t`, and `ColumnCodec.encodeJson` beside `encode_json_t`, both over the typed column rather than its cells. The go-red is the bridge that DROPS the widening normalisation — `toCells (ofCells cs)` held to `cs` itself rather than to `norm_cells` — which loses on exactly the draws holding an `Int` in a float or decimal column and on no other. The pools draw every timestamp unit, an instant of a coarser unit in a finer column, an instant FINER than its column (refused as the `TypeMismatch` naming the finer type) and temporal text that is not canonical (refused as `MalformedShape`), and the typed column crosses as the integers the model holds — day counts, whole seconds and the fraction vector where held.

  _Boundary:_ Agreement over the drawn columns and tables only, never over all inputs. The draws are asserted to reach every column type, nulls, widened cells and both refusal classes `ofCells` has, so a pool that stopped sampling one would say so. On each drawn table `validate_t` is also held to the list model's `validate` of the table's image, so `validate_agrees` is evaluated on the extracted code; per cell list the masks, and the model's `Equals` between the two built columns, are compared as well. Asserted reached: more than 150 widened draws, more than 400 with a null, and more than 150 refusals in the wild pool, beside the 2,000- and 3,000-table pools.

- <a id="row-column-refinement-validity-differential"></a>**`column-refinement-validity-differential`** — `Proofs.Oracle`, 2 cases · fuaran-core#420

  The extracted two-case validity agrees with the shipped `Validity`. Per drawn mask: `Validity.ofList`, `ofArray` and `ofVector` beside the model's `of_mask`; per drawn pair of validities of one row count — `AllValid`, a built mask, a hand-built all-true `Mask` of the right length or of another, a hand-built `Mask` with a clear bit — `Validity.toMask` beside `to_mask`, and the equality of two columns over ONE values vector, so only the validities differ, beside `same_mask`. The go-red is the model that compares the representations with `=`, which loses on exactly the pairs where `AllValid` meets an all-true `Mask` of the column's length and on no other.

  _Boundary:_ Agreement over the drawn masks and pairs only, never over all inputs. The draws are asserted to reach both constructor answers and both answers of the mixed comparison.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`column-int-layouts`](#row-column-int-layouts) (assumed), [`column-codec-abstractions`](#row-column-codec-abstractions) (assumed), [`column-vector-is-its-contents`](#row-column-vector-is-its-contents) (assumed).

**Inherited through the models it opens:** [`parser-float-readback-opaque`](#row-parser-float-readback-opaque) (assumed), [`parser-alphabet-bridge`](#row-parser-alphabet-bridge) (assumed), [`canon-numeral-layouts`](#row-canon-numeral-layouts) (assumed), [`utf8-model-is-the-encoder`](#row-utf8-model-is-the-encoder) (assumed), [`canon-unit-spelling`](#row-canon-unit-spelling) (assumed), [`query-codec-column-premises`](#row-query-codec-column-premises) (assumed).

### `DecodeLayer`

[`DecodeLayer.fst`](DecodeLayer.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `WireDecode`.

> DecodeLayer — an F\* model of Fuaran.Core's typed decode layer and its codec combinators, with
> the building blocks a domain composes proved GENERICALLY in what they build (fuaran-core
> Phase 436).

**What is modelled.** WHAT IS MODELLED, clause for clause, over `WireDecode.fst`'s `jval` (parametric in its two
numeric carriers, as there):
- `src/Fuaran.Core.Wire/DecodePath.fs` — `PathSegment` and `DecodePath.resolve` with its
first-member `step`.
- `src/Fuaran.Core.Wire/DecodeError.fs` — `DecodeCode`, `RootDecodeCode` (Phase 443),
`DecodeError.make` / `under` / `within` / `resolvesIn`.
- `src/Fuaran.Core.Wire/Decoder.fs` — every combinator but the two that are not over a value
(`parse`, which starts from TEXT and is `Json.read` with its refusal lifted, and
`describing`, which answers the sentence): `succeed`, `fail`, `map`, `bind`, `andThen`, `wrongKind`,
`missing`, `tryMember`, the six kind readers, `field` / `optField` / `fieldOr`, `at`,
`mapListIndexed` / `list` / `boundedList` / `listAll`, `sequence` / `all`, `oneOf`,
`tagDispatch` / `tagDispatchWith` / `kindDispatch`, `intRange`, `undeclared` / `members` /
`closed`.
- `src/Fuaran.Core.Wire/Codec.fs` and `CodecDeclarationFault.fs` — `Codec<'T>`'s `Write` and
`ReadAll`, `CodecDeclarationFault.under`, `make` / `ofDecoder` / `decoder`, the five
leaves, `enum`, `list`, `map`, `refine`, `record` / `field` / `optField` / `build`,
`case` / `union`, with every private helper they reach (`one`, `under`, `both`,
`repeats`, `traverse`, `strictObject`).

**Proved** (3)

- <a id="row-decode-layer-round-trip"></a>**`decode-layer-round-trip`** — `build_round_trip` · fuaran-core#436

  THE CODEC COMBINATORS COMPOSE ROUND TRIPS. `proofs/DecodeLayer.fst` models `Codec<'T>`'s `Write` and collecting `ReadAll` clause for clause over `WireDecode`'s `jval`, GENERIC in the type a codec decodes, with every block the operation census names and every private helper they reach (`both`, `repeats`, `traverse`, `strictObject`). `rt c` — whatever `write` answers `Ok j` for, `read_all j` reads back as the value written — holds of the five leaves (`string_round_trip` and its siblings) and is PRESERVED by every block: `list` (`list_round_trip`, through `traverse`'s accumulator loop proved equal to the direct recursion), `map` and `refine` under their premises, `enum` (`enum_round_trip`), a record built by `record` / `field` / `optField` / `build` (`field_round_trip`, `opt_field_round_trip`, `build_round_trip`) and a union by `case` / `union` (`case_round_trip`, `union_round_trip`). A record's invariant is stated over every member list that AGREES with the written one on the declared names, which is what lets the next member append and lets a union read a case past its discriminator. The build-time refusals are exactly what the proof uses: `build`, `enum` and `union` answer `Ok` only when no member, spelling or tag repeats (`repeats_nil`: `repeats l == []` gives `no_dups l`) and no case declares the discriminator (`union_faults_nil`).

  _With:_ `string_round_trip`, `int_round_trip`, `float_round_trip`, `bool_round_trip`, `json_round_trip`, `list_round_trip`, `map_round_trip`, `refine_round_trip`, `enum_round_trip`, `field_round_trip`, `opt_field_round_trip`, `record_round_trip`, `case_round_trip`, `union_round_trip`, `make_round_trip`, `of_decoder_round_trip`, `repeats_nil`, `union_faults_nil`.

  _Boundary:_ `map`, `refine`, records, unions and `make` / `ofDecoder` preserve the round trip only under the premises a domain owes for the parts it supplies: `decode-layer-round-trip-premises`. Within the model; the correspondence to production is the differential's (`decode-layer-differential`) and the extractor's (premise `extractor-and-compiler-trusted`).

- <a id="row-decode-layer-totality"></a>**`decode-layer-totality`** — `strict_reader_total` · fuaran-core#436

  A DECODER BUILT FROM THE BLOCKS ANSWERS ON EVERY JVAL. Every `Decoder` combinator and every codec block is a `Tot` function of the `jval` in the model — no exception path and no divergence, `at` terminating on the path it walks and every list walker on its list (`decoder_total` states it) — and the half a typing cannot state is proved: the COLLECTING reader of every codec built from the blocks answers a value or a NON-EMPTY refusal list, so `Codec.decoder`'s third arm, the `SchemaFault` "a collecting decoder answered no defect and no value", is unreachable for any such codec, and the strict reader answers `Ok` exactly when the collecting one does and otherwise its first defect (`strict_reader_total`). The non-emptiness rides the same invariant as `decode-layer-refusals-resolve` (`sound_all`), so every block's lemma there is this theorem's too.

  _With:_ `decoder_total`, `sound_all`.

  _Boundary:_ For codecs built from the blocks only, and within the model: that production raises nothing on the generated inputs is the differential's (`decode-layer-differential`), sampled rather than proved.

- <a id="row-decode-layer-refusals-resolve"></a>**`decode-layer-refusals-resolve`** — `refusal_paths_resolve` · fuaran-core#436

  EVERY REFUSAL CARRIES A PATH THAT RESOLVES TO THE OFFENDING PART OF THE INPUT. `DecodePath.resolve` and `DecodeError.resolvesIn` are modelled clause for clause, `List.rev` and all; a decoder is `sound` when every refusal it answers over a value satisfies `resolvesIn` there and is not `InvalidJson` (the parser's code, which names only a root and so cannot survive a step). Every `Decoder` combinator that starts from a `JVal` is proved to PRESERVE soundness (`field_sound`, `at_sound`, `map_list_indexed_sound`, `list_all_sound`, `tag_dispatch_sound`, `closed_sound` and the rest), and so is every codec block's collecting reader (`list_codec_sound`, `field_codec_sound`, `build_sound`, `union_sound`, `decoder_sound`, ...), so a decoder or a codec a domain composes from them refuses only at paths that resolve (`refusal_paths_resolve`). The content is the transport lemma `ok_at_under`: a refusal owed to the value one step reaches is owed, one step longer, to the value the step started from — for a `MissingField` because the absent member is the path's LAST step, so the step is prepended to the parent the law resolves (`rev_cons`, `rev_snoc`). Measured red with `Decoder.field`'s refusal not taken under its member (`| Some v -> d v`): `field_sound` fails. No block carries a premise on the caller's code: `Decoder.fail` takes a `RootDecodeCode`, which carries neither `MissingField` nor `InvalidJson`, so `fail_sound` holds for every code it accepts; and `Decoder.andThen`'s function refuses with that same root code, raised at the value as `fail` raises it, so `and_then_sound` holds for every function (`and_then_needs_root_refusals` states the pathed refusal it does not take, which is unsound). A check that points inside the value is `bind` to `at <path> (fail ...)`, sound by `bind_sound`, `at_sound` and `fail_sound`.

  _With:_ `ok_at_under`, `ok_at_within`, `rev_cons`, `rev_snoc`, `field_sound`, `at_sound`, `map_list_indexed_sound`, `list_all_sound`, `tag_dispatch_sound`, `closed_sound`, `list_codec_sound`, `field_codec_sound`, `build_sound`, `union_sound`, `decoder_sound`, `fail_sound`, `and_then_sound`, `bind_sound`, `root_excludes`, `and_then_needs_root_refusals`, `root_missing_never_resolves`, `root_invalid_json_stops_resolving`.

  _Boundary:_ For decoders and codecs composed from the blocks; what the caller still supplies is the reader handed to `Codec.make` / `ofDecoder`, whose obligation is `decode-layer-refusal-premises`. Within the model; the correspondence to production is the differential's (`decode-layer-differential`) and the extractor's (premise `extractor-and-compiler-trusted`). Two refuted readings stay in the model as statements about refusals no block can now mint: a `MissingField` at the empty path names no member and resolves in no document (`root_missing_never_resolves`), and an `InvalidJson` resolves at the root and stops resolving one member down (`root_invalid_json_stops_resolving`).

**Tested** (1)

- <a id="row-decode-layer-differential"></a>**`decode-layer-differential`** — `Proofs.Oracle`, 4 cases · fuaran-core#436

  The extracted decode layer agrees with the shipped one. Every `Decoder` combinator the model covers, `DecodeError.make` / `under` / `within` / `resolvesIn` over a refusal of every code, and `DecodePath.resolve`, over 1,500 generated `JVal`s (repeated keys included, so the first-member rule is exercised) and generated paths; and generated codec SHAPES — records of up to three required and optional members, unions under one of two discriminators, enumerations, lists, an even-int refinement and the five leaves, nested to depth three, member names drawn so that repeats and a discriminator-as-member occur — built by both sides (their build-time refusals compared fault for fault), writing generated values (an unrecognised enum value or union case among them) and reading back those writes and three MUTATIONS of each, compared value for value and refusal for refusal by code and path, through both the collecting reader and `Codec.decoder`. Over every production read the theorems' statements are held to PRODUCTION: every refusal list non-empty, the strict reader its first defect, every refusal resolving in its input and none `InvalidJson`. The go-red is the BLIND bridge that drops the first step of every model refusal's path — what a block that forgot its `under` would answer one level up — which loses on exactly the refusal lists holding a refusal below the root, and on no other.

  _Boundary:_ Instantiated at `<int, float>` only, as `WireDecode`'s oracle is; agreement over the generated values, paths and shapes only, never over all inputs.

**Rests on, or is bounded by:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`decode-layer-round-trip-premises`](#row-decode-layer-round-trip-premises) (assumed), [`decode-layer-refusal-premises`](#row-decode-layer-refusal-premises) (assumed), [`decode-layer-model-bridge`](#row-decode-layer-model-bridge) (assumed).

### `OpStreamLoad`

[`OpStreamLoad.fst`](OpStreamLoad.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `Chain`, `JsonParse`.

> OpStreamLoad — an F\* model of Fuaran.Core's op-stream readers: the one JSONL line scanner and
> the stream loads built on it, with totality, locating faults and the write-then-load round trip
> proved (fuaran-core Phase 454).

**What is modelled.** WHAT IS MODELLED, clause for clause, over `JsonParse.fst`'s alphabet and value spellings (`ch`,
`och`: a character is what spells it, a decoded character is the spelling that produced it) and
`Chain.fst`'s records (the linear walk `first_chain_break`, which the verified load composes):
- `src/Fuaran.Core.OpStream/Jsonl.fs`, `OpStream.Jsonl` — the scanner: `skipString` and
`decodeString` as ONE pass (`string_tail`: the decode is total on what the skip accepted, so
the pass that does both answers the same fault at the same position), `isLiteral`,
`skipValue` with its structural walk (Phase 466: every depth read by the strict grammar),
`checkRaw`, `membersOf` (the walk's depth-0 frame over the line's own object, Phase 530)
refusing a repeated key, `parseLine`, `topFields`, `rawSpan`, `unquote`, `lineNumber`, `lineText`, `refuse`,
`tryRawField`, `rawField`, `stringField`, `intField`, `stringsField`, `actorField`,
`scanRecords` with the `\r\n` normalisation and the blank-line skip.
- the loads over it: `recordLine` / `toJsonl` / `tryToJsonl` (`checkedLines`), `recordOf`,
`isSnapshotLine`, `scanJsonlWithSnapshots`, the carrying witness `carry`, `settle` (the
public `Jsonl.settle` over the op record, Phase 530),
`readCarried`, `fromJsonlWithSnapshots`, `fromJsonl`, `fromJsonlLegacyActor` and
`fromJsonlVerified` (Phase 416's typed `StreamLoadFault`, `Unreadable` / `Broken` /
`Undecodable`, the three a load of one text answers).
- `src/Fuaran.Core.OpStream/Actor.fs` — `Actor.encode` (`JsonString.quote`'s three escape
classes: `"`, `\` and a control character as `\u00xx`), `Actor.validate`, `ofLegacyString`.

**Proved** (3)

- <a id="row-op-stream-load-totality"></a>**`op-stream-load-totality`** — `loads_total` · fuaran-core#454

  THE OP-STREAM READERS ARE TOTAL. `proofs/OpStreamLoad.fst` models the one JSONL line scanner (`Jsonl`: `skipString` and `readString` as one pass (the text kernel's string reader), `isLiteral`, `skipValue` with its structural walk (an object or array value read by the strict grammar at every depth, a repeated key, a raw control and a raw unpaired surrogate refused at the path they are at), `checkRaw`, `membersOf` refusing a repeated key, `parseLine`, `topFields`, `rawSpan`, `unquote`, `lineNumber`, `lineText`, `refuse`, `tryRawField`, `rawField`, `stringField`, `intField`, `stringsField`, `actorField`, `scanRecords` with its `\r\n` normalisation and blank-line skip) and the loads built on it (`recordOf`, `isSnapshotLine`, the snapshot scan, the carrying witness, `settle`, `Jsonl.read`, `Jsonl.readWithSnapshots`, `Jsonl.readLegacyActor`, `Jsonl.readVerified` composing `Chain.fst`'s walk) clause for clause over `JsonParse.fst`'s alphabet and value spellings, and every one is a `Tot` function of its input — no exception path and no divergence, every walk terminating on the suffix it consumes (`loads_total` states it for the loads).

  _Boundary:_ Totality is the model's: production raises `JsonlScanFault` deep in the scan and catches it at six boundaries, while the model has no exception, so every `fail` is a returned `SErr` and the boundaries are the ones that turn it into a `JsonlFault`. That production raises nothing is established only on the generated inputs, by the differential (`op-stream-load-differential`). NOT claimed: the fault renderers (`JsonlFault.toString` and its siblings); the lane loads and the DAG's readers (`Dag.loadLanes`, the checkpoint sidecar), which read through the same scanner and are measured by the lane and DAG tests; the snapshot sidecar's own reader (`Snapshots.ofJsonl`).

- <a id="row-op-stream-load-faults-locate"></a>**`op-stream-load-faults-locate`** — `site_names_its_op` · fuaran-core#454

  EVERY FAULT NAMES A LINE THAT EXISTS, AND AN UNDECODABLE SITE ITS OP. A fault `scanRecords` answers names a line of the text that the scan reached and did not skip as blank — the 1-based number over EVERY line of the text, blank lines counted, as an editor shows it — and is exactly the fault that line's parse or decode answered (`scan_fault_locates`); the scanner's own fault sits within the line, at a character of it or just past its last (`parse_fault_in_line`). Every site of an `Undecodable` load names a line that exists, which parsed to a record whose `op` span the witness refused with the site's reason, and whose `hash` member is the site's node id (`site_names_its_op`, through the carrying witness's answer `Ok (raw, w.Decode raw)` recovered by `record_of_inv`). No fault is produced for a line the writer wrote: `record_of_line` is the per-line half of the round trip.

  _With:_ `scan_fault_locates`, `parse_fault_in_line`, `line_item_faithful`, `record_of_inv`, `record_of_line`.

  _Boundary:_ `scan_fault_locates` holds under `faithful`: a decoder whose faults name the line they were handed, which every reader of the model is (`line_item_faithful`).

- <a id="row-op-stream-load-round-trip"></a>**`op-stream-load-round-trip`** — `verified_round_trip` · fuaran-core#454

  LOADING WHAT THE CHECKED WRITER WROTE YIELDS THE STREAM THAT WAS WRITTEN. `Jsonl.tryWrite` (`checkedLines` over `checkRaw`, then `recordLine`) is modelled beside the loads, with `Actor.encode` and `JsonString.quote`'s three escape classes; `round_trip` proves `Jsonl.readWithSnapshots` answers the written records with no snapshot and `Jsonl.read` answers them, and `verified_round_trip` that `Jsonl.readVerified` answers them or the break `Chain.first_chain_break` reports of the ORIGINAL records (`walk_carried`: the walk over the carrying witness's records is the walk over the records they carry), so `verify` over a loaded stream is `verify` over the original (`verify_preserved`). The content: a decoded string a writer can spell reads back through the one string pass (`string_tail_quoted`, over `wf_str`: no raw quote, backslash or line break, the eight short escapes, four hex digits, a high surrogate escape followed by a low one); a scan that consumed its whole input consumes that prefix of any extension that a terminator follows (`string_tail_ext`, `skip_val_ext` with its mutual `obj_members_ext` and `arr_items_ext`, `bare_token_ext`, `skip_value_ext`); one written member is read as one member (`members_step`); the decimal numeral `string` writes reads back through `intField`'s digit fold within Int32 (`read_int_show`); the actor object reads back (`actor_field_reads`); `\r\n`-free lines split as they were joined (`split_join`).

  _With:_ `round_trip`, `verify_preserved`, `walk_carried`, `string_tail_quoted`, `string_tail_ext`, `bare_token_ext`, `skip_val_ext`, `skip_value_ext`, `members_step`, `read_int_show`, `split_join`, `actor_field_reads`, `line_members`, `keys_plain`, `record_keys_distinct`, `clean_quote`.

  _Boundary:_ Holds under the two premises of `op-stream-load-premises` (the domain's witness reads back what it writes, and its records are well formed). The string read-back is over `wf_str` strings only, the numeral read-back within Int32 only, and the line split for `\r\n`-free lines only.

**Tested** (1)

- <a id="row-op-stream-load-differential"></a>**`op-stream-load-differential`** — `Proofs.Oracle`, 5 cases · fuaran-core#454

  The extracted op-stream readers agree with the shipped ones, at the reference counter witness: every building block of `Jsonl` over every distinct line of a pool of written streams, their mutations and a set of hostile lines (every prefix of a record line, bad and surrogate escapes, repeated keys, snapshot and actor shapes, over-limit numerals, blank shapes of both whitespace kinds, array spans for `stringsField`), both sides rendered the same way; `Jsonl.read`, `Jsonl.readWithSnapshots`, `Jsonl.readLegacyActor` and `Jsonl.readVerified` over written streams and texts mutated by replacement, deletion, insertion, truncation, interleaved blank, snapshot and foreign lines, duplicated and renamed keys, pushed numerals and `\r\n` endings, fault for fault and record for record, the undecodable sites included; `Jsonl.tryWrite` and `Jsonl.write` over the written records and over witnesses whose encoding the checked writer refuses; and the negative sequence number the scan admits and the walk refuses. Over every production read the theorems' statements are held to PRODUCTION: nothing raises, every fault names a line of the text that is not blank and is that line's own fault at a position within it, every site names a line whose `op` the witness refuses with the site's reason by the line's `hash`, and every written stream loads back verified and unverified. The go-red is the BLIND bridge that renumbers every model fault to line 1 — a scanner that dropped the line's number — which loses on exactly the texts refused past their first line and on exactly the lines that fault when numbered 3, and on no other.

  _Boundary:_ Agreement over the sampled pools only, at the reference counter witness, never over all inputs. The one boundary the pool exposes is pinned by name: a key spelled with an escape is read by production and missed by the model (`op-stream-load-model-bridge`).

**Rests on, or is bounded by:** [`op-stream-load-premises`](#row-op-stream-load-premises) (assumed), [`op-stream-load-model-bridge`](#row-op-stream-load-model-bridge) (assumed).

**Inherited through the models it opens:** [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed), [`content-id-determines-content`](#row-content-id-determines-content) (assumed), [`signature-binds-one-head`](#row-signature-binds-one-head) (assumed), [`parser-float-readback-opaque`](#row-parser-float-readback-opaque) (assumed), [`parser-alphabet-bridge`](#row-parser-alphabet-bridge) (assumed).

### `DagPrimitives`

[`DagPrimitives.fst`](DagPrimitives.fst) — checked, extracted to the oracle and run beside production by `Proofs.Oracle`. Opens `DagFold`, `DagClosure`.

> DagPrimitives — the lane DAG's remaining boundaries, modelled (fuaran-core Phase 457).

**What is modelled.** WHAT IS MODELLED, clause for clause with `src/Fuaran.Core.OpStream.Dag/DagOpStream.fs`:
1. `Dag.mergeBase` as production computes it NOW — Phase 388's private `deepest`, which sizes
only the MAXIMAL members of the common set when that set drains completely, and every
member when it does not (`deepest`, `merge_base_now`).
2. `Dag.commonBase` over N heads (`common_base`), on the same `deepest`.
3. The reachability index's own queries — `Reach.mergeBase`, `Reach.commonBase`,
`Reach.between` and `Reach.tryTopoOrder` — over section 20's slots, with the index's
`Order` array read as the whole DAG's drain, the order `Reach.ofDag` builds it in.
4. `cut`, the kept set of a compaction: `forwardFrom` (a depth-first walk along child edges,
under fuel), `after`, the uncovered-node refusal, the side parents and the band.

**Proved** (8)

- <a id="row-merge-base-divergence"></a>**`merge-base-divergence`** — `merge_base_now_is_divergence` · fuaran-core#158

  `Dag.mergeBase` of two heads whose lanes hang off one merged node `m` is `m` — the divergence point, not the original base and not anything else the two heads share (`merge_base_now_is_divergence`). The theorem is stated over production's `mergeBase` as it runs, the private `deepest` (`merge_base_now`): the intersection of the two ancestor closures, then the maximum by (closure size, id) over its MAXIMAL members when the common set drains completely and over all of them when it does not, with the size comparison spelled as a length comparison of de-duplicated closures so the oracle holds no integer. The common set is exactly `m`'s closure (`merged_common_is_the_closure`), and every member but `m` lies below a parent of `m`, which is itself a member, so `deepest`'s filter keeps `m` alone or nothing and either branch of the drain test answers `m`. That `m` beats every other member is production's own docstring sentence — a node strictly deeper than any of its own ancestors has a strictly larger closure — PROVED rather than argued: the closure is transitive (`anc_trans`), `m` lies in no proper ancestor's closure (`off_cycle`), and a distinct list strictly inside another is strictly shorter (`pigeon`). The maximum is therefore STRICT and the id tie-break is unexercised on this shape: the theorem holds for every `lt` whatsoever and every drain fuel. Acyclicity is consumed as `off_cycle` and DERIVED from `DagClosure.fst` section 13's witness form by `off_cycle_of_witness` (an ancestor stands before its descendant in any topological enumeration, `anc_before`); `merge_base_now_is_divergence_acyclic` is the theorem with that bridge applied.

  _Boundary:_ Premises: those of `dag-delta-recovery-merged-head`, plus the two lanes' ids being disjoint — which is what `foldOnce` folds the actor into every node id for. NOT claimed: heads with several maximal common ancestors, which stays at `dag-outside-the-model`.

- <a id="row-merge-base-as-it-runs"></a>**`merge-base-as-it-runs`** — `merge_base_now_maximal` · fuaran-core#457

  `Dag.mergeBase` as production computes it — the private `deepest`, which sizes only the MAXIMAL members of the common set when that set drains completely and every member when it does not (`merge_base_now`) — over ANY two heads, a criss-cross merge (two merges of the same two lanes with two maximal common ancestors) included, either refuses exactly when the heads share no ancestor, or returns a common ancestor that NO other common ancestor descends from (`merge_base_now_maximal`). The key (closure size, id) is a strict total order on ids (`deeper_strict_total`), so `List.maxBy` over the whole common set returns a member nothing beats (`max_by_top`), a strictly deeper node has a strictly larger closure (`strictly_deeper`), and that maximum is therefore a maximal common ancestor (`max_common_is_maximal`). The member it picks is below no member — not below another one by maximality, and not below itself, which the witness forbids (`top_not_below`) — so it survives `deepest`'s filter and still beats every member left (`deepest_is_max_by`): `deepest` answers what the maximum over the whole set answers. Either branch of the drain test gives that answer, so the test needs no lemma of its own.

  _Boundary:_ Holds only over a left head whose closure carries `DagClosure.fst` section 13's acyclicity witness, which is what rules out a member below itself (every id in it is off every cycle: `off_cycle_in_witness`). WHICH maximal common ancestor is returned is the tie-break, a policy, and a function of the two closures — not claimed here. Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-common-base-maximal"></a>**`common-base-maximal`** — `common_base_maximal` · fuaran-core#457

  `Dag.commonBase` over N heads (`common_base_maximal`) refuses exactly when no node is in every head's closure — no heads, an absent head, or histories with nothing common to all — and otherwise returns a node in every head's closure that no other such node descends from. `List.distinct`, the fold of intersections and `deepest` are modelled clause for clause; the answer is a function of the head SET, so the order the heads are named in cannot reach it.

  _Boundary:_ Holds given any one head's closure witnessed acyclic. Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-reach-merge-base-agrees"></a>**`reach-merge-base-agrees`** — `reach_merge_base_agrees` · fuaran-core#457

  The reachability index's `Reach.mergeBase` answers what `Dag.mergeBase` answers (`reach_merge_base_agrees`): an absent id refuses on both, an unslotted one falls back to the walk, and two slotted ones rank the slots set in both bitsets by `Count` and the id, which is `deeper`'s key read off the index (`reach_deeper_is_deeper`: a slot's count is its closure's size).

  _Boundary:_ Only over the index built by `DagClosure.fst` section 20's steps (`reach_valid`) and a left head witnessed acyclic; the bitset is read as a set under the slot-agreement abstraction `dag-outside-the-model` states.

- <a id="row-reach-common-base-agrees"></a>**`reach-common-base-agrees`** — `reach_common_base_agrees` · fuaran-core#457

  The index's `Reach.commonBase` answers what `Dag.commonBase` answers (`reach_common_base_agrees`): no heads or an absent head refuse on both, an unslotted head falls back to the walk, and otherwise the slots set in every head's bitset, ranked by the index's key, give the node `deepest` gives.

  _Boundary:_ The scan over slot positions `0 .. top` is read as a pass over the first head's set, which holds every slot set in all of them. Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-reach-try-topo-order-agrees"></a>**`reach-try-topo-order-agrees`** — `reach_try_topo_agrees` · fuaran-core#457

  The index's `Reach.tryTopoOrder` answers what `Dag.tryTopoOrder` answers (`reach_try_topo_agrees`): a slotted head's set kept from the index's order IS its closure's drain (`order_restricts`, `DagClosure.fst` section 21's `drain_restrict` at the whole DAG), and every id of a slotted head's closure is slotted and so placed, which makes the DAG-level answer `Ok` too (`slot_order_is_drain`). An unslotted head falls back to the walk, cyclic refusal included.

  _Boundary:_ Only over the index `Reach.ofDag` builds, whose order is the whole DAG's drain. Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-reach-between-agrees"></a>**`reach-between-agrees`** — `reach_between_agrees` · fuaran-core#457

  The index's `Reach.between` answers what `Dag.between` answers (`reach_between_agrees`): a slotted head's set minus a slotted base's, kept from the index's order, is the head's drain with the base's closure removed; an absent base removes nothing on both; any other base falls back to the walk. GO-RED: a model `Reach.between` that drops the first node of its delta makes the theorem unprovable, and the differential carries the same go-red against production.

  _Boundary:_ Any base that is neither slotted nor absent is covered only through the fallback to the walk. Within the model; the correspondence to production rests on the differential and the extractor (premise `extractor-and-compiler-trusted`).

- <a id="row-dag-compaction-truncation"></a>**`dag-compaction-truncation`** — `compaction_preserves_replay` · fuaran-core#457

  Truncation is proved end to end (`compaction_preserves_replay`): the set `cut` keeps — `forwardFrom` (a depth-first walk along child edges), `after`, the smallest-id uncovered refusal, the side parents and the band — meets `truncation_ok` (`cut_keep_meets_contract`), so `DagClosure.truncation_preserves_replay` applies to every DAG `Dag.compactAt` returns. Both walks are proved CLOSED under the child edges they keep to (`fwd_closed`), and closure is all the contract needs: a node whose walk reaches a kept node is kept (`closed_up`). The refusal names a node that is neither behind the checkpoint's node nor reachable from it, and the walk's fuel never runs out (`fwd_enough`).

  _Boundary:_ `forwardFrom` is modelled under fuel. The contract does not need the band; production keeps it for `firstBreakFrom`, which stays sampled (`dag-checkpoint-truncation-sampled`), not proved. A kept set of the checkpoint's node and everything after it already meets `truncation_ok`. Which uncovered node the refusal names is read as `pick_min` over `Map.toList`'s ascending key order; the differential holds production's choice to it. GO-RED, run at the leg: a `cut_keep` that keeps the band and the checkpoint's node but drops what follows it makes `cut_keep_meets_contract` unprovable.

**Tested** (1)

- <a id="row-dag-boundaries-differential"></a>**`dag-boundaries-differential`** — `Proofs.Oracle`, 3 cases · fuaran-core#457

  The extracted DAG-boundary functions run beside production's over DAGs a clone holds: `merge_base_now`, `reach_merge_base`, `try_topo_order`, `reach_try_topo_order`, `reach_between` (with DagClosure's `between_drained`) beside `Dag.mergeBase`, `Dag.tryTopoOrder`, `Dag.between` and the index's four queries over every ordered pair of ids and an absent id, on fold-pull-fold DAGs with a side merge and on criss-crosses; `common_base` and `reach_common_base` beside `Dag.commonBase` and `Dag.Reach.commonBase` over head sets of three or more, a repeated head, an absent head and none; and `cut_keep` beside `Dag.compactAt` at every node, the kept set equal and the uncovered refusal naming the same node (guards: shared and disjoint pairs, non-empty deltas, an N-head base, a compaction keeping a band, a refused compaction). GO-RED: an index delta with one node dropped disagrees with production.

  _Boundary:_ Agreement over the sampled DAGs and head sets only, never over all inputs.

**Rests on, or is bounded by:** [`dag-outside-the-model`](#row-dag-outside-the-model) (assumed), [`extractor-and-compiler-trusted`](#row-extractor-and-compiler-trusted) (assumed).

**Inherited through the models it opens:** [`node-ids-distinct`](#row-node-ids-distinct) (assumed).

### Assumed, policy, and tested beyond a model

The rows no model section carries. An `assumed` row is classed in the contract table above; a model section links each one its rows rest on.

**Assumed** (40)

- <a id="row-independence-diamond"></a>**`independence-diamond`** — `domain-obligation` · discharged by `Conformance.footprintLawsAt` · fuaran-core#132

  The fold theorem's one domain hypothesis: two operations whose footprints are declared disjoint, and which both apply at a state, each apply after the other and reach the same state. This row is about a DOMAIN'S OWN witness, which is what the fold theorem is generic over, and it is discharged by `Conformance.footprintLawsAt`, the family that samples it at the domain's own ops.

  _Assumes:_ independence_diamond holds of the domain the fold is run over.

  _Boundary:_ Sampled — never proved — including directly against the extracted model's own independence predicate on the reference witness, with a sample-adequacy guard and its own go-red; `Conformance.footprintLaws` samples the skeleton algebra only. For the skeleton-op tree algebra specifically it is not assumed but proved, at `tree-independence-diamond`. It is no more than the op-algebra law of `fuaran-core#80`: interleaving totality (accepted scripts stay accepted) plus confluence, at op granularity. NOT claimed, and FALSE of the reference witness: REJECTION IDENTITY — that two independent ops, one of which rejects, reject identically whichever ran first. `Rejection.UnknownNode` carries `addressable`, the whole id set of the tree it was raised against, and `ReorderMismatch` the parent's current children, so after an independent insert both orders reject, with different envelopes. The diamond asks only about ops that BOTH apply, and a `Proofs.Oracle` case ('the reference algebra cannot keep rejection identity') holds the refuting witness, so the sentence goes red if the algebra changes back.

- <a id="row-dag-outside-the-model"></a>**`dag-outside-the-model`** — `model-bridge` · closes: `unscheduled` · fuaran-core#134

  What of the DAG layer remains outside the model. Hashing is abstracted behind an opaque minting function; what it buys is stated separately as `node-ids-distinct`. `Dag.mergeBase` is modelled clause for clause as it runs, the private `deepest` included, and proved to locate the divergence point for heads whose lanes hang off one merged node (`merge-base-divergence`), and over ANY two heads, a criss-cross merge included: it returns a common ancestor no other common ancestor descends from, or refuses exactly over disjoint histories (`merge-base-as-it-runs`). So are `commonBase` over N heads, the index's own four queries and `cut`'s kept set. The index's queries are modelled over its slots under the SLOT-AGREEMENT abstraction: a slot's bitset is read as the set of ids whose bits it sets, `Count[s]` as that set's size, a scan over slot positions as a pass over the set, and the index's order array as the whole DAG's drain, the order `Reach.ofDag` builds. The ancestor closure, the difference against the base's closure, the lookup and the fold built on them are not assumed at either shape: see `dag-delta-recovery`, `fold-from-the-dag`, `dag-delta-recovery-merged-head` and `fold-from-a-merged-dag`.

  _Assumes:_ The hash function itself is correct; a delta holding a two-parent node is recovered in the drain's order, which no theorem here identifies with an append order; a slot's words set exactly the bits of the ids its set holds; and an extended index's order is the extended DAG's drain.

  _Boundary:_ Outside the model: the hashing itself; the recovery of a delta that itself contains a two-parent node, which is no spine and has no append order to be identified with; the word layout under the slot-agreement reading; and `Reach.extend`'s splice of a new leaf into the order, which `Conformance.reachLaws` samples. WHICH maximal common ancestor `Dag.mergeBase` returns is the (closure size, id) tie-break, a policy and not a fact, and a deterministic function of the two closures. Below the slot-agreement reading also sit the position bounds of the scans — every set bit of slot `s` is at a position no greater than `s` — sampled with the word layout by `Conformance.reachLaws` and `dag-boundaries-differential`.

- <a id="row-extractor-and-compiler-trusted"></a>**`extractor-and-compiler-trusted`** — `premise` · fuaran-core#131

  The proof leg holds the committed oracle to a fresh extraction byte for byte, which makes 'the oracle is the model' a checked claim. The extractor half is DISCHARGED ON SAMPLED INPUTS by twin evaluation: every extracted model ends with a `twins` list whose fixtures F\*'s normaliser evaluates to `true` under the model's semantics (`assert_norm`), the list is extracted with the model, and the `Proofs.Oracle` family's 'twin evaluation' cases run the extracted closures against the extracted oracle — measured red on a deliberately perturbed oracle (the drain's minimum flipped to a maximum: two DagFold fixtures fail). The kit's TWIN step refuses an extracted model with no twins.

  _Assumes:_ the F\* extractor and the F# compiler are correct — on the inputs the twin fixtures sample, checked against the normaliser since Phase 309; everywhere else, assumed.

  _Boundary:_ It does not make the F# backend correct — the least-examined link in the chain, and named as such. What remains assumed is the backend and the compiler on every input no fixture samples, which is still a premise.

- <a id="row-sets-are-lists"></a>**`sets-are-lists`** — `model-bridge` · closes: `permanent` · fuaran-core#131

  The model reads sets as lists under membership and the host compares canonical (deduplicated, sorted) reports.

  _Assumes:_ the list-for-set reading loses nothing the comparison observes.

  _Boundary:_ Multiplicity and order inside a report are therefore unobserved by construction, and nothing here claims anything about them.

- <a id="row-node-ids-distinct"></a>**`node-ids-distinct`** — `premise` · fuaran-core#134

  The hash-collision assumption, and the only thing the delta recovery assumes about content addressing, narrowed to what it is true OF: the hash is injective on the nodes a store HOLDS. Id distinctness itself is not assumed of a store built through `Dag.append` / `Dag.merge` — `append_refuses_differing_node` and `append_never_replaces` (`append-refuses-differing-node`) make it a property of every such store, because a node minting a held id with different content is refused rather than replacing the held one — and `verifyDag` re-checks every key against its content on load.

  _Assumes:_ Dag.nodeHash is injective on the nodes a store holds — (sorted parents, actor, encoded op) of two held nodes never mint one id; append refuses a differing node and verifyDag re-checks every key.

  _Boundary:_ What is left assumed is only that two nodes a store actually holds do not share an id under a hash collision the store never saw offered. Under the 32-bit FNV-1a default a collision among offered nodes is a real event (about 0.3% at 5,000 nodes, even odds near 77,000): what that costs is a REFUSED append, never a silently replaced node.

- <a id="row-tree-algebra-well-formed-states"></a>**`tree-algebra-well-formed-states`** — `domain-obligation` · discharged by `Conformance.opAlgebra` · fuaran-core#133

  The tree the algebra runs over carries each id at most once — a hypothesis about the INPUT alone. It is a hypothesis rather than an oversight: the lookup that resolves an id returns the FIRST match in document order, so on a tree carrying one id twice what it returns is a fact about the order as well as the contents — and a reorder moves the order, which makes the diamond FALSE there rather than merely unproved. Preservation is not assumed: it is a theorem, `apply-preserves-wf`, so a tree that starts id-unique stays id-unique under every accepted operation.

  _Assumes:_ the tree the fold runs over carries each id at most once.

  _Boundary:_ What remains unenforced is the entry point, and only the entry point: nothing downstream produces such a tree and the structural diff refuses one outright, but no shipped type carries the invariant. A shipped type or entry-point check carrying it would discharge the assumption.

- <a id="row-content-id-determines-content"></a>**`content-id-determines-content`** — `premise` · fuaran-core#145

  The cryptographic premise, and the only claim about the DAG's content id that is assumed rather than proved or certified: two (key, value) pairs that mint one digest are the same pair. It is an explicit lemma-valued PARAMETER (`hash_injective`) of the theorems that need it rather than an assume, so which results depend on it is visible in their signatures. The two splices and the op codec are not part of it — they are `preimage-splices-unambiguous` and `op-codec-injective-certified` below. It is stated MODULO THE PARENT SORT, which is the strongest true form. It is also the whole of what the LINEAR side assumes about its record hash: that side's pre-image is a four-way JSON envelope rather than nodeHash's two splices, and `chain-envelope-parse` below parses it, so `rec_injective_on` is derived from this row, `op-codec-injective-certified` and `seq-numeral-code-certified` exactly as `node_injective_on` is; nothing about the record hash is bundled.

  _Assumes:_ the HashFn is injective on the (key, value) pairs it is handed — on both sides; since Phase 197 the linear side spends it on (prevHash, envelope) pairs and nothing else.

  _Boundary:_ It holds for the host's collision-resistant HashFn; it is FALSE for the default FNV-1a at scale (`hashFnAdversarialLaws` exhibits an in-budget collision). `Conformance.hashFnLaws` carries it for the host's own HashFn — its op-tamper, re-mint and distinguishing arms are the ones a constant or colliding HashFn fails — sampled, never proved; and a premise is discharged by nothing, so the row names no `dischargedBy`. What follows when it FAILS is a theorem rather than a shrug: `chain-tamper-evident-iff-hash-distinguishes` proves, with no premise, that an op tamper at record `i` is reported exactly when the hash distinguishes the two envelopes under record `i`'s predecessor — so under a colliding default the tamper is hidden exactly at the collision and nowhere else.

- <a id="row-chain-walk-order-is-productions"></a>**`chain-walk-order-is-productions`** — `model-bridge` · closes: `permanent` · fuaran-core#136

  That the order the model walks is the order production walks. Production enumerates Map.toList, which is ascending key order; the model walks the list it is given, and the differential hands it exactly that list. The VERDICT is order-independent by construction — a break exists or it does not.

  _Assumes:_ the entry list the model is handed is Map.toList's own order.

  _Boundary:_ Only which break is reported first depends on this, and nothing here is said about F#'s Map enumeration.

- <a id="row-signature-binds-one-head"></a>**`signature-binds-one-head`** — `model-bridge` · closes: `unscheduled` · fuaran-core#193

  The signature primitive, abstract, with ONE assumed property: an attestation verifies against at most one head — `verify` holds only for the signed bytes. It is an explicit lemma-valued PARAMETER (`signature_binds`) of the two signed-head theorems rather than an assume, so which results depend on it is visible in their signatures. It is BINDING and not unforgeability: a sink whose `Verify` compares the attestation's recorded `Head` has it by construction, and what the cryptography buys is the half no row here states.

  _Assumes:_ signature_binds — the sink's Verify accepts one attestation against at most one head.

  _Boundary:_ Core ships no signer (`Attestation.noAttestation` signs nothing and verifies nothing) and the only concrete sinks in this repository are test-local, so the model's abstract `verify` is bridged to no production signature at all: that is the bridge this row names. It is `unscheduled` rather than `permanent` because something COULD close it, and it is already half-built — the property is one a host's `IAttestationSink` either has or lacks (a sink that verifies everything falsifies it), and the shipped kit samples it at the host's own sink: `Conformance.attestationLawsAt`' prefix arm and its two rehashed-forgery arms are each one attestation offered two heads. Recording it as a `domain-obligation` discharged by that law is what would close it, and it is not done, because the discharge relation is shipped data (`Fuaran.Core.Families.obligations`, held to this file row for row by the Conformance.Families family), so adding the pair is a change to a shipped package's source rather than a proof-leg artefact, and is an operator's call. Under the `noAttestation` default these theorems say nothing, which is correct — there is no signed head.

- <a id="row-parser-float-readback-opaque"></a>**`parser-float-readback-opaque`** — `model-bridge` · closes: `permanent` · fuaran-core#146

  System.Double.TryParse's four-valued verdict on a number token — parses finitely to a double whose canonical layout IS the token, parses finitely otherwise, parses to a non-finite, does not parse — is a PARAMETER of the model, instantiated by the differential host with the call production makes; what the model decides is which verdict leads where, which is the parser's own logic. System.Int32.TryParse is likewise modelled lexically, as a comparison against the range's own digits after stripping leading zeros.

  _Assumes:_ the float readback and the Int32 range test are the host's, not the model's.

  _Boundary:_ Nothing here is said about float precision, about which tokens .NET accepts, or about the shortest-round-trip layout. The lexical Int32 model is argued rather than mechanised, and measured by the differential's boundary cases from both sides. The fourth value, `FCanonical` (fuaran-core#253), is the one `int53-guard-exact`'s past-2^53 clause turns on. A number carries its TOKEN, not its value: which constructor the parser chooses is modelled exactly, and the host reads the model's token back with the call production made, so a model that scanned one character more or less renders a different value and the differential sees it.

- <a id="row-parser-alphabet-bridge"></a>**`parser-alphabet-bridge`** — `model-bridge` · closes: `permanent` · fuaran-core#146

  That the model's character list IS the production string. A character is a constructor of the model's alphabet — one per character the parser distinguishes, plus a catch-all carrying the character verbatim, so two different characters are never identified — and the host's one-line-per-character bridge is what makes the correspondence. The bridge also CLASSIFIES a raw surrogate code unit into the model's high-half or low-half constructor, because the parser distinguishes them and the model has no code points to do it with.

  _Assumes:_ the host's toCh bridge maps each production character to the model's alphabet faithfully.

  _Boundary:_ The bridge is not itself proved, and its surrogate classification is on the same footing as the rest of it; `parser-accepts-only-well-formed-strings` is stated over it. The value of a \uXXXX escape is on the same footing: the model carries the four hex digits symbolically because the extraction carries no integers, and the host resolves the code point, so the decoded character is compared but the transliteration is sampled rather than proved.

- <a id="row-lawful-abstract-witness"></a>**`lawful-abstract-witness`** — `domain-obligation` · discharged by `Conformance.witnessLaws` · fuaran-core#138

  The witness laws themselves. The model reads a node as an id, a kind tag and an ordered child list, and treats `ReplaceChildren` as an abstract function satisfying `Conformance.witnessLaws` — exactly as the fold theorem takes the independence diamond abstractly. The id-witness axiom is named here too, because it is the witness law every id-equality theorem consumes: every model reads an id through ONE decidable equality — `TreeOps` and `Preservation` take ids as their key strings, the form `IdWitness.ToString` writes every address in, compared with `=` — while production keys `ToString` in `wellFormed` / `footprint` / `Tree.Index` and calls `Equals` in `tryFind` / `updateNode`. The theorems transfer to a domain's `'Id` exactly when the two agree: `id_key_faithful idw.Equals idw.ToString` (`Equals a b <==> ToString a = ToString b`), defined in `TreeOps.fst` section 0.

  _Assumes:_ the domain's ReplaceChildren satisfies Conformance.witnessLaws, and its IdWitness keeps id_key_faithful — Equals a b <==> ToString a = ToString b, the one id equality every model reads.

  _Boundary:_ A domain's own `ReplaceChildren` is that domain's promise, sampled by the conformance pack and proved by nothing; where it does not hold, every theorem above is about a different function from the one that runs. The pack's own boundary: `witnessLaws` exempts a leaf from the round trip, so a witness whose `ReplaceChildren` is partial on leaves is lawful. A domain whose `Equals` and `ToString` disagree runs a different algebra from the one proved; `witnessLaws`' identities cell samples exactly that relation, over drawn pairs and over pairs built to share a key, so the law covers the axiom the models consume rather than only reflexivity and the round trip — by sampling, not proof.

- <a id="row-witness-surface-scope"></a>**`witness-surface-scope`** — `domain-obligation` · discharged by `Conformance.keyedApplyLawsAt` · fuaran-core#138

  That the witness surface is the whole of the tree. `Tree.ids` walks `NodeWitness.Children`, so a node a domain holds in a keyed, non-structural position is invisible to the uniqueness scan, to every theorem above, and to the differential. Stated as an assumption rather than a limitation because it is what the theorems quantify over: they are true of the witness view, and the witness view is what `Ops` can see. A domain that hands its `KeyedWitness` to the engine through `OpScope.keyed` gets the `DuplicateId` refusal over the keyed walk from Core (`keyed-apply-preserves-wf`), so what is left of the obligation is the DECLARATION — that `KeyedChildren` reports every keyed position the domain's own walk sees, and that `ReplaceKeyedChildren` and `ReplaceChildren` leave each other's positions alone. `Conformance.keyedApplyLawsAt` certifies exactly that: its agreement law holds the engine's refusal to the domain's own check over collisions BUILT through `PlaceKeyedChild`, and its witness laws hold the two rebuilds apart.

  _Assumes:_ the domain's KeyedWitness declares every keyed position its own full walk sees, and its two rebuilds leave each other's positions alone: applyContainedKeyed refuses exactly the inserts the domain's own id check would refuse afterwards, over collisions built through PlaceKeyedChild — Conformance.keyedApplyLawsAt.

  _Boundary:_ The unkeyed `Ops` forms still see exactly what `Children` reports, and a domain that does not use the keyed forms keeps uniqueness over its keyed positions as its own obligation, with `Conformance.keyedChildrenLawsAt` certifying the domain's own full-walk id check against its declaration. The row is marked `domain-declared` because the law checks the declaration and cannot check past it: `keyedApplyLawsAt` holds the engine to what `KeyedChildren` REPORTS, so a domain with keyed positions that declares none passes it by declaration. A green run says the declaration is consistent; only the domain can say it is complete — so this row is not DISCHARGED by the law it names, it is checked against the domain's own word.

- <a id="row-canon-numeral-layouts"></a>**`canon-numeral-layouts`** — `model-bridge` · closes: `permanent` · fuaran-core#149

  The numerals are OPAQUE, as they are for the decode combinators and for the parser, and for the same reason: the F# extraction carries no floats and F\*'s integers are unbounded where `JInt` is Int32. The two layouts and the numeral read-back are parameters, and the ONE premise the canonical-form theorems carry about them is that the read-back inverts the layouts (`tok_read_ok`) — which is the property `Double.ToString("R")` is DEFINED by, since rule 5 pins the float layout as the shortest digit sequence that round-trips. Rule 5's injectivity lemmas are DERIVED from that premise rather than assumed beside it, because a read-back that is a function cannot return two different values for one token. One lemma, `no-null-rendered`, carries a SECOND and WEAKER premise about the same parameters — `layouts_numeric`, that every character of both layouts is a numeral character — because it speaks of every value, including the integral floats outside the canonical subset about whose layout `tok_read_ok` is silent. The two BRIDGE rows — `canon-parser-agreement` and `canonical-parse-roundtrip` — carry a THIRD premise about the same two layouts, `numerals_bridge`, and it is this row's because it is the same opacity seen from the other side: `JsonParse.fst` takes `System.Double.TryParse`'s verdict as its own parameter (`parser-float-readback-opaque`), so a bridge between two opaque parameters can only relate them. It says each layout begins with a character the parser's value dispatch sends to `parseNumber`, and that in every trailing context the grammar produces `parseNumber` scans back exactly the token the encoder emitted and classifies it on the constructor rule 5 chose. It is exhibited SATISFIABLE rather than assumed consistent — `numerals_bridge_is_satisfiable` runs `parseNumber` on a concrete wire's two layouts in every such context — because a premise about two parameters at once is the shape that can be contradictory without looking it, and a contradictory premise would make both bridge rows true and worthless. A FOURTH, `integral_float_reads_back`, is carried by one lemma alone, `every_finite_float_reads_back`, which supports `canonical-form-roundtrip` off the canonical subset: a finite float outside that subset, whose layout `tok_read_ok` is silent about, reads back as a number that renders as the same token.

  _Assumes:_ tok_read_ok — the numeral read-back inverts the integer and float layouts, which is rule 5's own definition of the float layout; for `no-null-rendered` alone, layouts_numeric — both layouts consist of numeral characters; and since Phase 170, for the two bridge rows alone, numerals_bridge — the parser's number path accepts each layout as the token the encoder emitted, on the constructor rule 5 chose.

  _Boundary:_ Nothing is said about the digits themselves: which decimal .NET produces for a given double is measured by the differential and by the cross-host parity vectors, not proved; `numerals_bridge` likewise says nothing about which digits either layout produces. `layouts_numeric` binds `no-null-rendered` alone, `numerals_bridge` the two bridge rows alone and `integral_float_reads_back` `every_finite_float_reads_back` alone; every other canonical-form theorem carries exactly one premise.

- <a id="row-canon-key-comparator"></a>**`canon-key-comparator`** — `model-bridge` · closes: `permanent` · fuaran-core#149

  Rule 2's comparator is a PARAMETER, constrained to be a total order: reflexive, total, transitive and antisymmetric on keys. What is proved is that a canonical form follows FROM a total order — which is the half a model can settle.

  _Assumes:_ key_order_ok — the ordinal comparator is a total order on keys, and it is UTF-16 code-unit order.

  _Boundary:_ NOT proved: that `System.String.CompareOrdinal` is a total order, or that it is UTF-16 code-unit order rather than code-point or UTF-8-byte order; those are facts about .NET, and the distinction is observable exactly where the rule's own note says it is, above the BMP. The corpus fixture that pins it and the differential's astral keys are the evidence, not this theorem.

- <a id="row-canon-character-bridge"></a>**`canon-character-bridge`** — `model-bridge` · closes: `permanent` · fuaran-core#149

  A character is a CONSTRUCTOR, exactly as in the parser model and for the same reason. The model distinguishes the three classes rule 6 names and the characters a numeric or literal token can carry, and carries every other character verbatim, so two ordinary characters are never identified.

  _Assumes:_ the character bridge is faithful — a verbatim character never aliases a constructor's own spelling.

  _Boundary:_ What is assumed is the BRIDGE: that the host's mapping never puts a character into the verbatim constructor when another constructor already denotes it. The model writes that condition down as `bridged` rather than leaving it in prose, and the host's mapping is total and classifies before it falls through, so it holds by construction — but the correspondence between a `ch` and a .NET `char` is one line per class and is not itself proved.

- <a id="row-capability-scalar-readers-abstract"></a>**`capability-scalar-readers-abstract`** — `model-bridge` · closes: `permanent` · fuaran-core#177

  THE READERS PREMISE. `Space.validate` and `Space.wellFormed` reach for five host functions — `Space.readInt` (an optional `-` and decimal digits under the INVARIANT culture, `Json.readInt32`, no `+`, no white space, no culture's own minus sign), `Space.readFloat` against a float range, `String.Length`, `Space.slotKindOf`, and `Space.wellFormed`'s float case (`float_fault`: a NaN or infinite carrier, else an empty range) — and the model takes them as a `readers` record: the space vocabulary, the clause structure of `validate`, `space_wf` and `is_count` are modelled, the lexical parsers are not, and a float range's two bounds cross as opaque carriers (the round-trip `R` format). The premise the theorems spend is that each reader is a function of the STRING alone, which production honours; the differential holds it by running under he-IL. The differential instantiates the readers at production's own calls (`int_of` through `Space.canonical`), so the envelope is compared and the parsers are exercised, not modelled; `ParityVectors` pins the reader's answer on `-5`, U+2212 `5`, ` 5`, `+5` and `1e999` on both pipelines.

  _Assumes:_ the five readers behind Space.validate and Space.wellFormed are host functions of the string alone the model takes as parameters; a float range's bounds are opaque carriers.

  _Boundary:_ Every theorem about validation is about the ENVELOPE — which entry a value is checked against, which refusal names it, that the body waits on the answer — and nothing about whether "12" is in `[0, 10]`. Permanent: modelling the two numeral grammars is a lexical model, which is `JsonParse.fst`'s cost and not `Capability.fst`'s. The witness (`Holes` / `Effect` / `Bind` / `KindTag` / `Children` / `preorder` / the node id) is the model's other parameter and is the standing `lawful-abstract-witness` obligation, not a second row here.

- <a id="row-capability-key-renderers-abstract"></a>**`capability-key-renderers-abstract`** — `model-bridge` · closes: `permanent` · fuaran-core#225

  THE KEY RENDERERS PREMISE. `Capability.invocationKey` reaches for three host functions — `Hash.fnv1a`, the ordinal string order `List.sortBy fst` compares addresses by, and `Hash.canonicalField` — and the model takes them as a `key_renderers` record. The stable address sort, the two-fields-per-binding flattening and the field concatenation are modelled; the hash, the comparator and the escaper are not. `invocation_key_injective` states what it needs of them in `key_premises`: that the escaper is `esc` then one terminator when a string is read as its symbols (`field_faithful`), with `symbols_faithful` the reading itself — two facts true of `System.String` by construction, the same reading `Chain.fst` takes for its splices.

  _Assumes:_ the hash, the address comparator and the field escaper behind Capability.invocationKey are host functions the model takes as parameters; the escaper is assumed to be esc-then-terminator over a symbol reading of strings where a theorem needs it.

  _Boundary:_ The escaper clause is measured on the shipped function by the round trip through an independently written decoder over adversarial fields, with a bare separator as its go-red — sampled, not proved. Nothing is claimed about the hash. Permanent: a model of the escaper is a model of `String.Replace` over UTF-16 code units, and F\*'s `string` is primitive.

- <a id="row-propagation-change-set-and-prior"></a>**`propagation-change-set-and-prior`** — `domain-obligation` · discharged by `Conformance.propagationEvaluatorLawsAt` · fuaran-core#209

  WHAT IS LEFT OF THE EVALUATOR CONTRACT, a domain obligation with a law. `evalfrom_agrees` holds of an evaluator that (1) differs from the evaluator that produced `prior` only on the ids the change set names (`agree_off`), and reads different nodes only there too (`touches_off` — the same premise's other half, stated separately only because the read set is a model parameter; it is load-bearing at exactly one step, a CLEAN node the incremental walk reuses without checking, where the full walk's check must be the one the old walk already passed); and (2) is handed a `prior` that came from `eval` over the SAME dependency map, or fewer of its values. For an evaluator in the structural class — a function of the node's kind, its child ids and the resolver, asking only its declared reads — clause (1) is a THEOREM off `changedForOp` (`evalfrom_agrees_for_op`, `propagation-evalfrom-agrees-for-op`), so what such a domain owes is that its evaluator IS in the class (the undeclared-read refusal enforces the reads half at run time) and clause (2). A domain's own green run discharges the obligation: `Conformance.propagationEvaluatorLawsAt` takes an `EvaluatorWitness` — the domain's own evaluator, dependency map, model generator and edits, each edit with the change set the domain names for it — and certifies, at that evaluator, that it is a function of what it reads (repeated and reordered evaluation agree), that off the named ids an edit moves neither the declared reads nor the evaluator's results or asked reads (`agree_off` and `touches_off`, sampled at four resolvers), and that `evalFrom` over `eval`'s own prior, whole and with holes, equals `eval` over the same map. The second clause is carried by CONSTRUCTION: the law builds every prior the one way the theorem admits.

  _Assumes:_ the domain's evaluator is a function of what it reads, changes and reads differently only where the change set says (a theorem for the structural class since Phase 456), and prior is eval's own output over the same dependency map — Conformance.propagationEvaluatorLawsAt.

  _Boundary:_ Neither clause can be read off a resolver: the first is a claim about two evaluations the driver never sees together, the second about where a `Map` came from. Across an edit that MOVES the map, clause (2) is the whole premise, and discharging it needs a theorem the model does not state — that a full evaluation's values do not depend on which certified order produced them — so a domain that persists a prior across such an edit owns re-priming with `eval` over the new map, and the family checks such an edit for honesty and never replays it. The level stays what a law run can earn: the evaluator is the model's parameter and outside every theorem. Its adequacy witness is an in-repo formula sheet, because no adopter evaluator existed when the law shipped.

- <a id="row-propagation-prior-blind"></a>**`propagation-prior-blind`** — `domain-obligation` · discharged by `Conformance.propagationPriorLawsAt` · fuaran-core#250

  THE ONE PREMISE THE PRIOR ADDS. `evalfromwith_agrees` holds of a prior-aware evaluator whose answer does not depend on the prior it is handed, at every node the incremental walk recomputes, under the resolver the walk hands it there (`prior_blind_along`). `Conformance.propagationPriorLawsAt` takes the domain's `EvaluatorWitness` (whose `EvalNode` is the domain's REFERENCE evaluator) and its prior-aware evaluator, and certifies that the prior-blind reading is the reference, that a node handed its prior — its own value from `evalWith` of the model before the edit, carrying whatever reuse state the domain keeps in it — answers what it answers handed none at the answers the full evaluation of the edited model gives (the answers the incremental walk hands it, by the theorem itself), and that `evalFromWith` over `evalWith`'s own prior, whole and with holes, equals `evalWith`. Its adequacy witness is an in-repo sheet whose table nodes refresh their cached cells from the prior the driver hands them.

  _Assumes:_ the domain's prior-aware evaluator answers the same handed its prior as handed none, at the resolver the incremental walk hands it — Conformance.propagationPriorLawsAt.

  _Boundary:_ It is about the evaluator, which is the model's PARAMETER, so it is a domain obligation in the shape of `propagation-change-set-and-prior`. The theorem's equality is the value type's, so a value carrying a reuse cache defines its equality over what it means rather than over the cache.

- <a id="row-propagation-read-witness"></a>**`propagation-read-witness`** — `model-bridge` · closes: `permanent` · fuaran-core#209

  THE READ BRIDGE. Production detects an undeclared read by INSTRUMENTING the resolver it hands `evalNode`: the resolver records the ids it is asked for, and the walk refuses on the first one outside the declaration. A `Tot` function cannot observe a call, so the model takes the observation as a parameter — a `read_witness : string -> list string` beside the evaluator, exactly as `topo_result` sits beside `deps` for `propagation-order-distinct`. The structural class takes the declaration itself as the witness (`declared rd t`), which is what an evaluator that asks only its declared reads observes; the bridge is then that production's evaluator asks exactly what it declares — enforced at run time by the undeclared-read refusal.

  _Assumes:_ the read witness handed to the model is the set of ids production's instrumented resolver would observe the evaluator asking for at that node.

  _Boundary:_ Nothing in the model ties `read_witness` to `ev`. The differential supplies it from the same lines the evaluator is written in, so it is sampled there and proved nowhere; for the structural class too the bridge is sampled rather than proved. `permanent` rather than `unscheduled`, because the obvious closure does not work: a pure model CAN define a semantic dependency set (the ids at which two resolvers differing only there change the answer), but that is a DIFFERENT set — an evaluator may ask for an id and ignore what it gets, and production refuses on the asking. Modelling the asking means modelling an effect the evaluator does not have.

- <a id="row-query-renderers-abstract"></a>**`query-renderers-abstract`** — `model-bridge` · closes: `permanent` · fuaran-core#187

  THE RENDERERS PREMISE. `invocationKey` reaches for seven host functions — `string` on an `int`, `FloatLayout.canonical`, `Hash.fnv1a`, the ordinal string order `List.sortBy fst` compares names by, `Hash.canonicalField` (one escaped, terminated field of the pre-image), and the canonical text of a declaration's filter and of its order, `Canonical.write Rendering.CrossHost` over `QueryShape`'s predicate and order-key documents — and the model takes them as a `renderers` record, with a float cell crossing as an opaque carrier (its round-trip `R` text). The clause structure of the tag/payload split, the stable name sort and the field concatenation are modelled.

  _Assumes:_ the int and float renderers, the hash, the name comparator, the field escaper and (since Phase 398) the filter and order renderers behind invocationKey are host functions the model takes as parameters; the comparator is assumed a total order, the escaper is assumed to be esc-then-terminator over a symbol reading of strings, and the int, float, filter and order renderers are assumed injective on carriers, where a theorem needs each (the float one, and the filter one through a float literal, fail at -0/0, a pair production's Cell equality identifies).

  _Boundary:_ The two numeral layouts, the hash, the comparator and the escaper are not modelled; the differential instantiates the record at production's own seven calls, so the envelope is compared and the renderers are exercised, not modelled. `invocation_key_deterministic` states what it needs of the comparator — that it is a total order — as a hypothesis. `invocation_key_injective` states what it needs of the rest in `key_premises`: the escaper is `esc` then one terminator when a string is read as its symbols (`field_faithful`, with `symbols_faithful` the reading itself — two facts true of `System.String` by construction), and the int and float renderers, and the filter and order renderers, are injective on the model's carriers (the filter and order renderers measured on the shipped codec by the differential's declaration round trip). THE FLOAT CLAUSE IS FALSE OF PRODUCTION AT ONE PAIR, and so is the filter renderer's at the same pair in a float literal: `FloatLayout.canonical` renders both `-0.0` and `0.0` as `0` (the `canonicalFloat/neg-zero` parity vector), so the carriers `-0` and `0` share a pre-image. Production's own `Cell` equality identifies the two (IEEE `-0.0 = 0.0`), so the shared key is between argument sets production calls EQUAL — pinned on the shipped seam by the closure case — but the theorem is about carriers and that pair is outside it. Nothing is claimed about whether two different canonical strings hash apart, which is a claim about FNV-1a. Permanent: `FloatLayout.canonical` is `WireCanon.fst`'s numeral-layout bridge already (`canon-numeral-layouts`), a model of the ordinal comparator is a model of UTF-16 code-unit order, and a model of the escaper is a model of `String.Replace` over UTF-16 code units.

- <a id="row-utf8-model-is-the-encoder"></a>**`utf8-model-is-the-encoder`** — `model-bridge` · closes: `permanent` · fuaran-core#306

  That `proofs/Utf8.fst` describes the shipped `Hash.utf8Bytes` and `Hash.tryUtf8Bytes`.

  _Assumes:_ the unextracted UTF-8 model is the shipped encoder, clause for clause — production's shifts and masks are the model's divisions and remainders on a non-negative unit.

  _Boundary:_ Every other model in this directory is EXTRACTED and run beside production; this one cannot be — a unit and a byte are integers there, and F\*'s `int` does not survive the extraction (which is why `check.ps1` lists the module in `$proofOnly`) — so the step from the model to the code is read, not run. What stands in its place is `utf8-independent-oracle`: the shipped encoder held to the platform's over every code unit. That measures the code against a third party, not against the model, which is why this is an assumed row with a tested one beside it rather than a tested row.

- <a id="row-canon-unit-spelling"></a>**`canon-unit-spelling`** — `model-bridge` · closes: `permanent` · fuaran-core#306

  THE UNIT SPELLING. `canonical-digest-preimage-injective` is stated over a `spelling` — the UTF-16 code unit each character of the model's alphabet IS, and the set of characters the host's bridge produces — and over a well-formedness test (`str_ok`) the model takes as a parameter. What is assumed is that the host's character bridge is such a spelling: one constructor, one unit; distinct bridged characters, distinct units; no surrogate among the punctuation, the numerals' characters and the letters of `true` and `false`; and that `Json.isWellFormedUtf16` is well-formedness at that spelling.

  _Assumes:_ the host's character bridge is a spelling_ok spelling, and its well-formedness test is well-formedness at that spelling.

  _Boundary:_ Each is true of the bridge by construction (a `ch` is built from one .NET `char`, and its unit is that char's code), and none is proved: the correspondence between a `ch` and a `char` is the same one-line-per-class bridge `canon-character-bridge` records.

- <a id="row-column-int-layouts"></a>**`column-int-layouts`** — `model-bridge` · closes: `permanent` · fuaran-core#306

  THE TWO FACTS ABOUT Int32 the codec's round trip takes as hypotheses: the decimal text of an int is canonical decimal text (`int_text_canonical` — what makes an `Int` in a decimal column decode at all), and the float of an int is finite (`int_floats_finite` — what lets the widened cell through the `validate` decode ends in). Both are true of every Int32 value. Each is shown NECESSARY in the model (`int_text_canonical_is_necessary`, `int_floats_finite_is_necessary`): a host where one fails is a host where the round trip is an error.

  _Assumes:_ the text of an Int32 is canonical decimal text, and the float of an Int32 is finite.

  _Boundary:_ Neither is proved here: the model is parametric in its number carriers, as the canonical renderer's is, and what a host computes about a number is a field of a record. They are only sampled, by `column-codec-differential`; modelling the host's int-to-text and int-to-float layouts would discharge them.

- <a id="row-column-codec-abstractions"></a>**`column-codec-abstractions`** — `model-bridge` · closes: `permanent` · fuaran-core#306

  WHAT THE CODEC MODEL IS HANDED RATHER THAN DERIVES. The loops: production's three accumulate-and-reverse `go` loops are written as the direct recursion they are equal to, a claim about `List.rev` rather than about the codec. The envelope: a refusal keeps its class and column and loses its prose and its counts, the extraction carrying no integers. And `Table.firstDuplicate`'s `Set` is a list asked for membership. The calendar is NOT handed: `TemporalText.isCanonicalDate` / `isCanonicalTimestamp` are modelled in `Temporal.fst`, which `WireColumn` opens, because the unit a timestamp cell's type carries is read off its text, and a typing that reads text cannot be a host field without making every theorem about the typing a theorem about the host.

  _Assumes:_ the reversal of three accumulator loops and the dropped error prose are as the model takes them.

  _Boundary:_ Each abstraction is sampled by `column-codec-differential` and none is proved.

- <a id="row-capability-body-total"></a>**`capability-body-total`** — `premise` · fuaran-core#307

  THE TOTAL-BODY PREMISE. The model's host body is `unit -> deferred v`, a TOTAL function, and every theorem about `invoke` and `dispatch` spends that: the three outcomes, validation before invocation, `invoke_never_ok_failed`. So the theorems are about bodies that RETURN, which is the obligation `Capability.invoke`'s documentation states: report failure by answering `Failed reason`, which the seam turns into `BodyFailed reason`.

  _Assumes:_ a capability's host body returns a Deferred rather than throwing; the seam propagates an exception unconverted.

  _Boundary:_ Production's body is an F# closure that may throw, and `Capability.invoke` does not wrap it — an exception propagates to the caller unconverted, because catching every exception would also swallow the ones a host means to escape (cancellation, its own fatal faults). Nothing samples a throwing body; the obligation is the host's, and only a host body that returns rather than throws discharges it.

- <a id="row-capability-sections-parameters-abstract"></a>**`capability-sections-parameters-abstract`** — `model-bridge` · closes: `permanent` · fuaran-core#354

  Three host functions sections 14 to 16 reach for are parameters of the model, each for the reason the scalar readers are: the order an F# `Map<string, _>` keeps its keys in (`le`, which `bind_handlers_order_independent` asks to be a total order and no other theorem asks anything of), the two float comparisons `Space.subsumes` makes (`feed_readers`, of which nothing is assumed — every pipeline theorem holds for any relation), and `float i`, the one float read the codecs make (`codec_readers`, likewise unconstrained). The codecs are modelled at the `JVal`.

  _Assumes:_ the map's key comparator, the float comparisons behind Space.subsumes and the codecs' one float read are host functions the model takes as parameters; the codecs are compared at the JVal, between the canonical renderer and the parser.

  _Boundary:_ The bytes on either side are `Canonical.write Rendering.CrossHost` and `Json.parse`, which are other models' theorems, and the differential runs the round trip through them. A refusal is its code and its path; its sentence and its `Expected` phrase are not modelled. Permanent: the model has no float and no string order, and a `JVal` is where a codec's own logic ends.

- <a id="row-capability-pipeline-body-total"></a>**`capability-pipeline-body-total`** — `premise` · fuaran-core#354

  The model's host body is a total function from a node and its resolved arguments to a value or a failure's message, and every theorem about `eval` and `evalFrom` spends that; `pipeline_evalfrom_agrees` also takes the body to be a FUNCTION of the node and its arguments.

  _Assumes:_ a pipeline's host body returns rather than throwing, and answers as a function of the node and its resolved arguments.

  _Boundary:_ Production's body is an F# closure that may throw, and `runNode` does not wrap it, for the reason `Capability.invoke` does not (`capability-body-total`). A body that answers differently on a second call with the same arguments is outside `pipeline_evalfrom_agrees`, which is what journalling a non-deterministic capability's result is for.

- <a id="row-query-codec-column-premises"></a>**`query-codec-column-premises`** — `model-bridge` · closes: `permanent` · fuaran-core#455

  The four places `QueryCodec` hands a value to `ColumnCodec` — a result column's schema entry, the declaration's `Source`, a page's rows and a predicate literal — are the query codec model's `column_codec` parameter, and its round trips (`column_codec_laws`) are assumed. A schema entry is the model's `(name, type)` pair; the metadata `fieldJson` also carries is the column codec's. A refusal keeps its code and its path and drops its prose.

  _Assumes:_ the column codec reads back what it writes at each of the four members, and the strict members check is as the decode layer's `members` is.

  _Boundary:_ The column strand proves the round trips in its own model (`column-codec-round-trip`, `field-codec-round-trip`), and the differential instantiates the parameter at production's `ColumnCodec`. The strict read policy's members check (`…With` under `Strict`) is not modelled, for either seam.

- <a id="row-propagation-ops-model-bridge"></a>**`propagation-ops-model-bridge`** — `model-bridge` · closes: `unscheduled` · fuaran-core#308

  THE MODEL-TO-CODE STEP FOR `PropagationOps.fst`. The model is checked and not extracted (`proofs/check.ps1`'s `$proofOnly`, on `Utf8`'s footing): production reads a node through the witness and the caller's `readsOf`, the model reads content as the kind tag with the reads a parameter over the model's tree, and an extraction would run beside production only through a bridge that re-encodes both — the step under test. So that `changedForOp` IS `changed_for_op`, and `dirtyFromOp` IS `dirty_from_op` on the same footing, is assumed; the evidence is the differential that holds production to the theorems' statements directly (`propagation-ops-differential`): to `changed_for_op_complete`, to `dirty_from_op_complete`, and to `changedForOp` being `dirtyFromOp`'s restriction to the survivors.

  _Assumes:_ Propagation.changedForOp, dirtyFromOp and touchedBy compute what PropagationOps.fst's changed_for_op, dirty_from_op and touched_by compute, read through the witness — sampled by propagation-ops-differential.

  _Boundary:_ Assumed, not proved: the differential samples the correspondence, it does not establish it. Closable by extracting the model and bridging the witness, which nobody has taken; until then the bridge row stands on the differential.

- <a id="row-propagation-evaluator-total"></a>**`propagation-evaluator-total`** — `premise` · fuaran-core#308

  THE EVALUATOR RETURNS. Every theorem about `eval`, `evalFrom`, `evalWith`, `evalFromWith` and the pull drivers quantifies over a node evaluator that is a total function — the model's evaluator is a `Tot` function — so the claims are about evaluators that return, `Ok` or `Error`, at every node they are handed.

  _Assumes:_ the domain's node evaluator returns Ok or Error at every node it is handed and never throws.

  _Boundary:_ Production does not catch: an evaluator that throws propagates its exception out of the driver, and nothing the driver promises holds of that call. Wrapping the call to turn an exception into `EvalNodeFailed` is declined (DECISIONS D108): it would catch what no total function raises and report a host fault as a domain failure. A domain whose evaluator can throw returns `Error` instead; that is its contract to keep, and no run of anything discharges it.

- <a id="row-write-gate-model-bridge"></a>**`write-gate-model-bridge`** — `model-bridge` · closes: `unscheduled` · fuaran-core#362

  THE MODEL-TO-CODE STEP FOR `TreeFrame.fst`. The module is checked and not extracted (`proofs/check.ps1`'s `$proofOnly`, on `PropagationOps`'s footing). Its frame theorems are about `TreeOps.fst`'s `apply` and `op_fp`, which ARE extracted and held to `Ops.apply` and `Ops.footprint` by the oracle's differential; what is assumed is that `WriteGate.targetsOf`, `decide` and `applyGated` compute what `targets_of`, `decide` and `apply_gated` compute, read through the witness. The evidence is the law that holds production to the theorem's statement directly: `Conformance.writeGateLaws` checks, at a domain's tree witnesses and op generator, that every id an applied op creates, destroys or whose child list it changes is a target or the source parent of one, that `applyGated` is `Denied` exactly when `decide` refuses, and that a lock refuses every op with a target in the locked subtree.

  _Assumes:_ WriteGate.targetsOf, decide and applyGated compute what TreeFrame.fst's targets_of, decide and apply_gated compute, read through the witness — sampled by Conformance.writeGateLaws.

  _Boundary:_ Assumed, not proved: the law samples at a domain's witnesses and generator only. Closable by extracting the gate model beside `TreeOps` and running it against `WriteGate`, which nobody has taken.

- <a id="row-column-vector-is-its-contents"></a>**`column-vector-is-its-contents`** — `model-bridge` · closes: `permanent` · fuaran-core#419

  A `Vector` IS WHAT ITS `toArray` ANSWERS, AND STAYS SO: the refinement model reads every vector through `Vector.toArray` — a list of its elements — and sees no storage: no offset, no sharing, no backing array, and no mutation to speak of.

  _Assumes:_ a Vector is the list its toArray answers and its contents do not change after construction — the element identity sampled by the differential, the ownership contract held by Phase 418's law family.

  _Boundary:_ Two things are therefore assumed rather than proved. That the read is faithful: `toArray`, `Length` and the indexer agree, which the element identity `VectorElements.equal` (every NaN one value, `-0.0` equal to `0.0`) qualifies and the differential samples with the host record's other fields. And that the contents never change after construction — no public member of `Vector` writes, `adopt` takes a fresh array, `Vector.Unsafe.borrow` lends under a written rule — which is out of the model's reach: the ownership contract (`fuaran-core#418`) is stated in `STABILITY.md` and held by the consumer-runnable law family that hashes every column before and after an operation and names the column whose bytes moved. Permanent: a model of mutation is a model of a different language. It also carries the temporal storage's one bridge (`fuaran-core#430`): a timestamp's second is an integer-valued `float` in production and an integer in the model, and a float that is not whole is a value no builder makes and `validate` refuses.

- <a id="row-decode-layer-round-trip-premises"></a>**`decode-layer-round-trip-premises`** — `domain-obligation` · discharged by `Conformance.codecLawsAt` · fuaran-core#436

  WHAT A DOMAIN OWES FOR THE PARTS IT HANDS THE CODEC BLOCKS. The composed round trip is proved under four hypotheses about functions only the domain writes, and they are its obligations: `Codec.map`'s `there` inverts its `back` (`there (back u) = u`); `Codec.refine`'s `check` admits every value `back` writes (`check (back u) = Ok u` — the refinement predicate holds on the representation of every refined value); a record's constructor applied to its getters is the value (`ctor (get1 r) ... (getN r) = r`); and a union case's `inject` inverts its `project` (`project v = Some p` implies `inject p = v`); plus, for `make` / `ofDecoder`, that the reader reads back what the writer writes. They are the domain's to keep. `Conformance.codecLawsAt` samples them at the domain's own `Codec<'T>` over the values its generator draws, and this row is the obligation its green run discharges. A codec is opaque once built, so the family states each hypothesis by its consequence at the composed codec: every drawn value is written, every written value reads with no defect (a `refine` that refuses a value its `back` writes, or a reader that does not admit its writer, is red here), every written value reads back EQUAL (a `map`, a constructor or an `inject` that does not invert is red here), and the canonical text under every encoding profile reads back as the value. `EncodingProfileVectors.storedCodecLawsAt` sits beside it: it reads a STORE through the codec and re-writes it byte for byte, the reverse direction of the same agreement.

  _Assumes:_ map's there inverts its back, refine's check admits every back-image, a record's constructor applied to its getters is the value, a case's inject inverts its project, and a make or ofDecoder reader reads back its writer — Conformance.codecLawsAt, at the domain's own codec.

  _Boundary:_ SAMPLED, never proved, and blind to a hypothesis broken only on values the generator never draws.

- <a id="row-decode-layer-refusal-premises"></a>**`decode-layer-refusal-premises`** — `domain-obligation` · discharged by `Conformance.codecLawsAt` · fuaran-core#436

  WHAT THE CALLER SUPPLIES THAT `decode-layer-refusals-resolve` CANNOT REACH. One block raises a refusal the caller wrote and Core never sees: the reader handed to `Codec.make` / `ofDecoder` — and with it any raw function a domain uses as a `Decoder<'T>`, which is an abbreviation of `JVal -> Result<'T, DecodeError>`. `Decoder.fail`'s code and `Decoder.andThen`'s function are each a `RootDecodeCode` raised at the value, so the unsound refusals are unrepresentable there. Core's own blocks never use a reader outside those terms. `Conformance.codecLawsAt` is a family of the shipped law-family roster, and this row is the obligation its green run discharges: over every structural mutation (`Corpus.mutations`) of every written value, every refusal the codec's collecting reader answers resolves in the mutated document (`DecodeError.resolvesIn`) and is not `InvalidJson` — the soundness `DecodeLayer.fst` proves of Core's own blocks (`decode-layer-refusals-resolve`) — so a domain's reader that refuses outside those terms is red at its own codec.

  _Assumes:_ a make or ofDecoder reader, or a raw function used as a decoder, is sound — Conformance.codecLawsAt, at the domain's own codec.

  _Boundary:_ The caller's reader is sound only when it is; a domain that uses a reader outside those terms is outside the theorem. The obligation is sampled, never proved, and guarded: a codec that refuses no mutation holds the law over nothing, and the family says so by name.

- <a id="row-decode-layer-model-bridge"></a>**`decode-layer-model-bridge`** — `model-bridge` · closes: `permanent` · fuaran-core#436

  WHAT THE DECODE-LAYER MODEL IS HANDED RATHER THAN DERIVES. A refusal keeps its `Code` and its `Path` and drops `Expected` and `Message`, as `WireColumn.fst` drops its refusals' prose; the codec's `Schema` (and a field's `Properties` / `Required`) is dropped as descriptive; `intRange lo hi`'s comparison is a host predicate over the opaque integer carrier and `float`'s widening a parameter, as in `WireDecode.fst`; production's filter-then-map (`undeclared`) and its `List.mapi` / `List.choose` / `List.collect` passes are written as the direct recursions they equal.

  _Assumes:_ the dropped refusal prose and codec schema, the host range predicate, and the fused list passes are as the model takes them.

  _Boundary:_ The text layer (`Codec.write` / `read` / `corpus`, `Decoder.parse`, `DecodeError.ofJsonRefusal`) is `Canonical`'s and the parser's, and is not modelled here. Each of these bridges is sampled by `decode-layer-differential` and none is proved. A path index and `boundedList`'s bound are F\* `int`s, which extract to `Prims.int` and are bridged at the edge. `Reader.read` is a text entry point beside `Decoder.parse` and is not modelled either. Not claimed: that the codec's schema agrees with what its reader accepts; the decoder, not the schema, is the authority on acceptance.

- <a id="row-index-node-is-its-record"></a>**`index-node-is-its-record`** — `model-bridge` · closes: `permanent` · fuaran-core#447

  WHAT THE INDEX MODEL'S NODE IS, AND WHAT THE CONTENT CONJUNCT RESTS ON. The model's node is what the witness shows — id, kind, children — and nothing else, so its `Index.tryFind` hands back a node that is its record and subtree. Production's node also carries content the witness cannot read. `Index.isFreshFor` takes the caller's content encoder and compares each cached node's `encode` reading with the tree's, so a content-only `UpdateNode` reads as stale. That conjunct is exact when `encode` is injective over a node's own content — a node is then determined by its id, kind, reading and children — the precondition `Tree.encodeHash` already states and `Conformance.encoderInjectivityLawsAt` certifies at a domain's encoder.

  _Assumes:_ the caller's encoder is injective over a node's own content, and the model's node carries none.

  _Boundary:_ Exactness holds only when the caller's encoder is injective over a node's own content; a caller passing a constant encoder has asked for the structural check alone. The model carries no content, so the conjunct is outside it by construction; the differential compares production through the same view, and the content conjunct is tested beside it (`TreeExtTests`, `ContentDiffTests`, `AlgebraSymmetryTests`).

- <a id="row-op-stream-load-premises"></a>**`op-stream-load-premises`** — `domain-obligation` · discharged by `Conformance.streamLaws` · fuaran-core#454

  WHAT A DOMAIN OWES FOR THE ROUND TRIP. `round_trip` and `verified_round_trip` hold under two hypotheses about what only the domain supplies: its witness reads back what it writes (`witness_rt`: `Decode (Encode op) = Ok op` for every op) and its records are well formed (`records_wf`: an actor that names somebody, strings whose decoded form the writer can spell, a sequence number within Int32 — which `OpStream.append` keeps). Both are sampled by `Conformance.streamLaws` at the domain's own witness: its JSONL cell writes a drawn stream through `Jsonl.tryWrite`, reads it back through `Jsonl.read` and holds `verifyChain` over the result, so a witness whose `Decode` does not invert its `Encode`, or whose `Encode` the checked writer refuses, is red there.

  _Assumes:_ the witness's Decode inverts its Encode and the records it chains are well formed — Conformance.streamLaws, at the domain's own witness.

  _Boundary:_ Sampled, never proved, and blind to a hypothesis broken only on ops the generator never draws.

- <a id="row-op-stream-load-model-bridge"></a>**`op-stream-load-model-bridge`** — `model-bridge` · closes: `permanent` · fuaran-core#454

  WHAT THE OP-STREAM LOAD MODEL IS HANDED RATHER THAN DERIVES. A decoded string is a `list och` as `JsonParse.fst` spells one — a character is what spelled it — so the code unit a `\uXXXX` denotes and the character a short escape spells are the host's (`ren`, the verified load's parameter; the differential's rendering). `String.Trim`'s whitespace is a host predicate over the characters the alphabet carries as `COther`. The chain walk re-encodes a LOADED actor by its code units (`JsonString.quote` spells a control as `\u00xx` and `\u0041` back as `A`), which the model does not compute, so the walk's actor text is `Actor.encodeWith` over the host's quote (`qh`) of the host's rendering (`ren`): the model owns the shape, the host the escaping, as it owns the hash; the model's own writer spells the canonical strings a host hands it, which is what the round trip reads back. The chain walk counts in `Chain.fst`'s unary `pos`, so a sequence number OUTSIDE the stream's indices — negative, or at or past its length, which the scan admits and no walk index equals — is carried AT the stream's length in the model. The lane (`None` on every load of one text), the three fault cases such a load never answers, and production's accumulators, `Replace` / `Split` and `HashSet` passes are written as the direct recursions they equal.

  _Assumes:_ a decoded character's code unit, the host's whitespace, the host's quote of a loaded actor for the chain walk, the unary carriage of a sequence number outside the stream's indices, and the fused passes are as the model takes them.

  _Boundary:_ Each is sampled by `op-stream-load-differential` and none is proved. Two spellings of one character are two strings in the model where production reads one: a key spelled `"s\u0065q"` is not `seq` there, which the differential pins by name as the one divergence its pool exposes. For a sequence number outside the stream's indices the walk's verdict agrees but the break's `got` text does not (the differential compares a sequence-number mismatch by its index and reason). Which keys are the SAME follows from it: production compares keys after unescaping and the model compares their spellings, so a key spelled `a` and one spelled with the escape of `a` are one key to production's repeated-key refusal and two in the model; the two agree wherever keys are spelled without escapes, which the differential's generators keep, and a refusal's path is rendered from the decoded spelling on both sides. Production's explicit stack of open containers, and its one state machine whose depth-0 frame records its members' spans for `membersOf`, `stringsField` and `actorField`, are written as recursions with the same clauses (`members_loop` and `items_loop` beside `obj_members` / `arr_items`), because the spans that frame answers differ in type from a nested frame's `unit`.

**Policy** (2)

- <a id="row-proof-leg"></a>**`proof-leg`** — fuaran-core#131

  Which prover is pinned, how many cold-cache runs a check is, and that a model edited without re-extracting fails the leg — the reproducibility posture the proved rows above are only as good as. The posture also says what the leg COSTS: each module's cold-run budget is declared in proofs/modules.json, measured on every run and printed beside the measurement, with an overshoot named as a warning, and `check.ps1 -Strict` for a session that wants one red.

  _Boundary:_ Policy, not algebra: no theorem addresses it. The budget is a smoke detector rather than a gate, because prover time varies by machine and by load; the numbers live in proofs/modules.json rather than in a row here, because a wall-clock budget is a claim at none of this ladder's four levels and is per module where a row is per claim. "Formally verified" is spent on the proved level alone.

- <a id="row-arbitration-pinned-order"></a>**`arbitration-pinned-order`** — fuaran-core#157

  WHICH maximal independent set `arbitrate` returns is decided by ascending proposal id. That the order is ascending id, that it makes the result a function of the proposal set, and that every result is maximal are theorems above; that ascending id is the RIGHT order — rather than arrival time, script size, or an order chosen to maximise the accepted count — is a policy choice: pinned, documented, deterministic.

  _Boundary:_ A posture, not a theorem: argued for nowhere in this directory, no theorem addresses it, no ranking or quality judgement is modelled, and `arbitration-maximal-is-not-maximum` is the measured cost of it.

**Tested beyond a model** (7)

- <a id="row-footprint-in-laws"></a>**`footprint-in-laws`** — `Conformance.footprintInLawsAt`, 3 cases · fuaran-core#500

  `Conformance.footprintInLawsAt` certifies the base-aware footprint at a domain's own ops, state and generator: soundness (a pair `Dag.conflictsIn` reports clean from a state commutes under the domain's `Apply` from it), refinement (a pair the pure projection calls independent, the base-aware one calls independent) and determinism, guarded on a pair only the base-aware footprint frees — without which the sample has not exercised the precision the projection exists for. The reference tree domain certifies green; a footprint that forgets the removed subtree is red on soundness, and one coarser than the pure footprint is red on refinement.

  _Boundary:_ Sampled over the domain's generator only, never over all scripts; what `footprint_in_sound` proves of the model, this certifies of the domain's own engine and ops.

- <a id="row-container-laws"></a>**`container-laws`** — `Conformance.containerLaws`, 5 cases · fuaran-core#161

  The two obligations the container capability rests on are certified over an adopting domain's own witness rather than assumed of it. `Conformance.containerLaws` is opt-in (the `chainBreakReasonLaws` shape — `OpGen.CanHold` is an option, so folding it into `certify` would add laws that cannot fail for every domain without a container notion, and would make `certify`'s law count depend on its input; it still returns 14). Three laws and an adequacy guard: `canHold` is CHILD-BLIND, sampled by perturbing a drawn node's children — emptied, and extended by one — and requiring the predicate to be unchanged; the engine preserves the container invariant over the domain's own nodes, operations and predicate, which is `contained_preserves` sampled against the shipped code for an adopter who has neither a prover nor the model; and a graft with an interior non-container is refused NAMING that node, since a caller repairs the node the envelope names. Every arm is BUILT rather than drawn, and the family emits a `SampleAdequacy.reached` law over the three arms so a run that measured nothing says so with the counts. The reference witness is the first certifier. Two go-reds: a `canHold` that reads the child list fails the blindness law, and a predicate admitting every node makes the interior-graft arm unreachable and reddens the adequacy guard by name. A domain that declares no predicate at all is reported by name rather than skipped.

  _Boundary:_ The laws hold over the sampled cases only, never over all inputs. Child-blindness is the premise no engine check can discharge. Whether the WITNESS honours the rebuild is drawn — the kit deliberately admits a witness whose `ReplaceChildren` is partial on leaves — so each arm counts only where the rebuild was honoured and the id and kind survived it.

- <a id="row-dag-checkpoint-replay-sampled"></a>**`dag-checkpoint-replay-sampled`** — `Conformance.checkpointLaws`, 3 cases · fuaran-core#288

  A checkpoint on the lane DAG is a checkpoint and not a cache: on generated DAGs with branch points, for every node and an absent id, `Dag.replayFrom` from a checkpoint `Dag.checkpointAt` took gives `Dag.tryReplayTo`'s answer from the initial state whenever the checkpoint covers the head, and otherwise the refusal an independent reading of the graph predicts — an unknown head, a head that does not reach the checkpoint's node, or the first node above the checkpoint that does not descend from it. That last refusal is load-bearing: the drain folds a branch that left below the checkpoint and merges above it BEFORE part of the checkpoint's own closure, so a replay that resumed from the checkpoint would fold it in an order the full replay never uses, and on a witness whose ops do not commute it reaches a state the full replay does not. `replayFromWith` answers as `replayFrom`. The statement is proved (`dag-checkpoint-replay`); this row runs it at the witness a consumer certifies.

  _Boundary:_ Sampled at a domain's own witness and over the generated DAGs only, never over all DAGs or witnesses.

- <a id="row-dag-checkpoint-truncation-sampled"></a>**`dag-checkpoint-truncation-sampled`** — `Conformance.checkpointLaws`, 3 cases · fuaran-core#288

  The seal and the truncation, sampled: `Dag.verifyCheckpoint` accepts a sealed checkpoint and refuses it with its seal changed, its node moved to another node or to an absent id, or its state swapped for one that encodes differently; `Dag.compactAt` refuses exactly a DAG holding a node neither behind nor after the checkpoint's node, and otherwise keeps the node and everything after it, verifies from the checkpoint under `Dag.verifyDagFrom`, and replays every node it keeps as the full DAG does; a kept node with its op changed, or the checkpoint with its seal or state changed, fails `verifyDagFrom`; and the compacted DAG lives on — an append onto its checkpoint's node replays as on the full DAG, and `Dag.compactFrom` at a later node answers as `compactAt` of the full DAG there. A sample that never branches reds the family's guard rather than reading green.

  _Boundary:_ Sampled only. The seal binds the state to the node and is exactly as trustworthy as the act that sealed it: it does not re-fold the discarded history, so verify, then compact. The replay half is proved for any kept set meeting `cut`'s contract (`dag-checkpoint-truncation`); the seal, `verifyDagFrom` and the sidecar are only sampled here.

- <a id="row-lane-store-laws"></a>**`lane-store-laws`** — `Conformance.laneLaws`, 3 cases · fuaran-core#311

  The lane store, sampled on drawn multi-writer stores: `Dag.loadLanes` of `Dag.lanesToJsonl` is the store and its union is `fromJsonl` of the lane texts concatenated; the lane files read in any order load, order and replay identically; `totalOrderBy` is a linear extension at the default key and a drawn one, and restricts to every head's `tryTopoOrder` at a constant key; `laneCollisions` names exactly a lane handed a second, incomparable node and `loadLanes` refuses a node two lane files hold with different content; `appendOn` over the heads in any order mints `mergeAll`'s id, and the history below it is the folded binary merges' without their merge nodes, in the same drain order; `commonBase` is order-free and is `mergeBase` at two heads; `Dag.rehash` to SHA-256 verifies there and rehashes back to the store, and refuses an unverified one; `verifyLanes` names the lane of a changed node; and a DAG pruned of `prunable` keeps every retained root's replay, order and verification.

  _Boundary:_ Agreement over the drawn stores only, never over all inputs. A sample without a cross-lane merge, two heads, a lane fork or a content collision reds the family's guard rather than reading green.

- <a id="row-column-temporal-dotnet-edge"></a>**`column-temporal-dotnet-edge`** — `Suite:ColumnTests.temporalTests`, 1 case · fuaran-core#430

  The .NET edge converts through the integers: `Column.ofDateOnlys` / `tryDateOnly` go through `DayNumber`, `ofDateTimeOffsets` / `tryDateTimeOffset` through `UtcTicks` in integer arithmetic; a tick finer than the column's unit is refused as the `TypeMismatch` naming the finer unit, and a nanosecond with no tick answers `None`.

  _Boundary:_ Sampled by the column suite, not proved: the model has no host date type — `TemporalText` is FSharp.Core-only and the edge is `#if !FABLE_COMPILER` — so this half of the temporal row has no theorem.

- <a id="row-column-field-metadata"></a>**`column-field-metadata`** — `Suite:ColumnTests.fieldTests`, 7 cases · fuaran-core#427

  A schema entry is a `Field` that carries what a column MEANS: an optional `UnitOfMeasure`, an optional label, an optional description, and an extension map every host preserves verbatim, behind total builders over an internal representation, so the next member is a builder, not a break. Metadata is part of a field's identity (two schemas differing only in a unit differ) and is not storage (`Table.validate` reads none of it, a column holds its name and its storage, and its field is read from the table by name). The wire writes the members a field states and no other, so every table without metadata encodes byte-identically to the plain `(name, type)` encoding; decode reads a parseable unit spelling to the unit and refuses one the algebra does not parse, naming the column; `Schema.diff` reports a metadata change as `Amended` beside a retype, `Schema.patch` replays it and refuses one computed against another schema, `Schema.classify` reads a stated unit replaced or withdrawn as breaking and an extension change as unknown, and `Schema.fingerprint` is unchanged over a schema without metadata and injective over one with it.

  _Boundary:_ Sampled, not proved: agreement over the sampled cases only, never over all inputs. The field codec and the unit algebra are the theorems of the `fuaran-core#428` rows (`field-codec-round-trip`, `unit-one-text` and their siblings).
<!-- models:end -->
