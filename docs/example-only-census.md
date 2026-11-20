# Operations held only by examples — the example-only census

Every public operation Core publishes is mapped to a proof, a law family or a recorded exclusion
([`proofs/README.md`](../proofs/README.md), the operation clause of the `Proofs.Coverage` family, Phase 335). One
exclusion class, `measured-elsewhere`, means the operation has no ladder row and no law family: a test file stands in
for it, and that test is almost always a set of examples. This census counts those operations, per package and per
owner, so the next law family is chosen from figures rather than from memory.

Phase 335 took the first count on 2026-10-02 (tree `c6307c33`). Phase 715 re-ran it on 2026-11-20 (tree `abd6034c`,
the `0.45.0` draft) and named the next law family from the new figures.

## The rule

1. **The census** is every `method` line of the managed API baselines, `api/*.txt` (not `api/wire/`), spelled as the
   coverage records spell it: `Fuaran.Core.` dropped, generic arity dropped, nested types joined by `.` and the
   compiler's `Module` suffix dropped (`ProofCoverage.operationName`).
2. **Each operation is counted once, under the first route that maps it:** a `proved` or `tested` row of
   `proofs.json` whose `evidence.operations` names it; then a law family's roster line or the family's own entry
   point; then the class of its entry in `proofs/coverage-exclusions.json`.
3. **Example-only** is the `measured-elsewhere` route. The entry's `standIn` names the test file that holds it. Most
   stand-ins are example tests; a few are differentials (the IDL interpreter's codec, below), so the count is an upper
   bound on what is held by examples alone.
4. **The owner** of an operation is its name without the last segment (`Decoder` for `Decoder.field`,
   `Tree.Index` for `Tree.Index.build`).

## Re-running it

- **Per package:** the operation table in [`proofs/README.md`](../proofs/README.md), rendered by
  `dotnet run --project tools/Fuaran.Core.Tools -- render proofs-readme` and held to the tree by `Proofs.Ladder`. Its
  `measured-elsewhere` column is this census.
- **Per owner:** `dotnet run --project tests/Fuaran.Core.Tests -- --filter-test-list "Proofs.Coverage"` prints one
  `example-only <package> <n>/<published>: <owner> <n>/<published>, …` line per package
  (`ProofCoverage.exampleOnlyCensus`, rule 2 read through the same route the tally reads, and a test holding its
  total to the table's).
- **Against an earlier tree:** read that commit's `proofs/README.md` table, and group that commit's
  `measured-elsewhere` members of `coverage-exclusions.json` by owner. On a green tree the class and the route agree,
  because the clause refuses an exclusion for an operation a ladder row or a roster line already maps.

Phase 335's per-package figures were reproducible from the README table. Its per-area figures were grouped by hand,
with no recorded rule; the owner census above is that rule, and the area figures below are sums of its rows.

## The totals

| Package | Operations, Oct | Example-only, Oct | Operations, Nov | Example-only, Nov |
|---|---:|---:|---:|---:|
| `Fuaran.Core.AiSurface` | 21 | 10 | 30 | 11 |
| `Fuaran.Core.Column` | 38 | 26 | 127 | 23 |
| `Fuaran.Core.Conformance` | 129 | 37 | 165 | 48 |
| `Fuaran.Core.ContentAddress` | — | — | 2 | 0 |
| `Fuaran.Core.Function` | 105 | 47 | 200 | 51 |
| `Fuaran.Core.Idl` | 61 | 59 | 89 | 81 |
| `Fuaran.Core.Idl.Cli` | 1 | 0 | 1 | 0 |
| `Fuaran.Core.Idl.Codegen` | 53 | 44 | 55 | 47 |
| `Fuaran.Core.Observer` | 20 | 3 | 12 | 4 |
| `Fuaran.Core.OpStream` | 123 | 79 | 130 | 56 |
| `Fuaran.Core.OpStream.Dag` | 81 | 24 | 87 | 23 |
| `Fuaran.Core.Ops` | 66 | 17 | 63 | 16 |
| `Fuaran.Core.Projection` | 13 | 5 | 13 | 5 |
| `Fuaran.Core.Propagation` | 26 | 16 | 27 | 14 |
| `Fuaran.Core.Query` | 32 | 18 | 64 | 15 |
| `Fuaran.Core.Relation` | — | — | 11 | 0 |
| `Fuaran.Core.Text` | — | — | 30 | 20 |
| `Fuaran.Core.Tree` | 57 | 44 | 48 | 28 |
| `Fuaran.Core.Unit` | — | — | 9 | 0 |
| `Fuaran.Core.Validator` | 34 | 19 | 64 | 36 |
| `Fuaran.Core.Wire` | 124 | 105 | 193 | 59 |
| **Total** | **984** | **553** (56 %) | **1,420** | **537** (38 %) |

The ladder maps 395 operations where it mapped 109, and law families 432 where they mapped 264.

Operation by operation, of October's 553: 331 are still example-only, 72 moved to a ladder row, 13 to a law family,
1 became a forward, and 136 are no longer published under their October name (removed, or renamed into another
owner). Of today's 537, 331 are those carried from October and 206 were published since, under a new name or for the
first time.

## The five areas Phase 335 named

**Wire decode** (`Fuaran.Core.Wire`): 105 of 124 → 59 of 193. The decode layer was rebuilt and proved: 38 operations
moved to the ladder (33 `Decoder` combinators, `DecodeError.make`, `under`, `within` and `resolvesIn`, and
`DecodePath.resolve`), leaving `Decoder` with 2 of 35. Another 33 left the surface with the old layer: the `Decode`
helper module (16), five `Json.parse*` variants, `Decoder.ofString` and `parseWith`, `DecodeError.ofJsonError` and
`reword`, `RowCodec`'s two, `Canon.typed` (now `Canonical.typed`) and `Versioning.Profile`'s five (now
`Versioning.Capability`). `Json.escape` became a forward. 26 were published since and have no law: `Reader` (7),
`Codec` (6), the renamed `Versioning.Capability` (5), `Versioning.EnvelopeRefusal` (2) and six single operations.
What remains is the reading surface around the proved combinators: `Json` 8 of 19, `Versioning` 8 of 11, `Reader` 7
of 11, `Codec` 6 of 21, `Corpus` 6 of 8, `DecodeError` 5 of 12.

**OpStream variants** (`Fuaran.Core.OpStream`): 79 of 123 → 56 of 130. Three moved to a law family (`appendMany`,
`firstChainBreak`, `rehash`); the rest of the movement is a reshaped surface, not new laws. Of the 66 October
operations no longer published, 19 were renamed into owner modules (`Snapshots`, `Compacted`, `Attributed`,
`Attestation`, `Sealed.replayFrom`), 14 were the hash-parameterised `With` variants (removed, so Phase 335's finding
that they were measured only at the default hash closed by removal), 15 were `OpStream.Jsonl` internals that left the
surface, and 18 others were removed. Of the 46 published since, 19 are those renames and 27 are new, among them two
owners held by examples alone: `ChainShape` 6 of 6 and `Sealed` 5 of 5.

**The Codegen diff** (`Fuaran.Core.Idl.Codegen`): 44 of 53 → 47 of 55. Nothing gained a law. `Idl.Diff` was reshaped
(nine operations removed, among them `classifyDiff`, `runWith` and `snapshot`; `verdict`, `ofValues`,
`absorbedWhenUnknownMembersIgnored` and `bumpProfileUnder` added) and stands at 17 of 21; the generators grew by
seven and `Idl.Gen` stands at 16 of 16. The eight ladder operations are unchanged.

**Tree** (`Fuaran.Core.Tree`): 44 of 57 → 28 of 48. The index was proved: seven `Tree.Index` operations moved to the
ladder (`build`, `tryFind`, `parentOf`, `ancestors`, `path`, `depth`, `isFreshFor`) and three of its variants were
removed, leaving `Tree.Index` 1 of 8. Six `Hash` operations moved to the new `Fuaran.Core.Text` package, still
example-only there. The traversals did not move: `Tree` 25 of 35.

**Propagation planning** (`Fuaran.Core.Propagation`): 16 of 26 → 14 of 27. Three moved to the ladder
(`dependencyMap`, `dirtyFromOp`, `touchedBy`), `staleSet` was removed, and `evalScoped` and `evalScopedWith` were
published since. The planner and its evaluators remain example-only: `Propagation` 11 of 24 (`plan`, `neededFor`,
`evalFor`, `evalFromPlan`, `nodeDependencies`, `partDependencyMap`, `dirtyFromChangedParts` and their variants) and
`Graph` 3 of 3.

Outside the five, the counts that grew are `Idl` (59 → 81: contracts, proposals, the wider data universe's
`ScalarText`), `Validator` (19 → 36: the `Validator.Rules` registry, 15 of 15) and the new `Fuaran.Core.Text` (20 of
30). Phase 335's IDL finding about the sanitizers is closed: the `Idl.Sanitize` operations are on a law family's roster.

## The next law family: Tree traversals

By count the order is `Idl` 81, `Wire` 59, `OpStream` 56, `Function` 51, `Conformance` 48, `Idl.Codegen` 47,
`Validator` 36, `Tree` 28. The count is not the order, because the first places are held by something stronger than
examples or are poor subjects for laws:

- `Idl`'s codec (`Idl.Decode`, `Idl.Encode`, `Idl.Sample`) stands in `IdlThreeHostTests.fs`, a three-way differential
  (interpreter, generated F#, generated TypeScript) over trees drawn by `Sample.trySampleNodes`: sampled, not
  examples. Its other owners (`Artifact` 13, `SourceLit` 10, `Declare` 8, `Proposal` 6) read and check
  declarations, are example-tested, and are a candidate after the ones named at the end.
- `Idl.Gen`'s generators are held by drift guards on committed generated modules; a law over a printer is weaker than
  the bytes it prints.
- `Conformance` is the law kit itself, held by its own go-reds.
- `Wire`'s remainder is the reading surface around the proved combinators. `OpStream`'s is in large part a renamed
  surface; its new chain owners (`ChainShape`, `Sealed`) are a candidate after the ones named at the end.

**`Tree` is next.** Its 25 example-only traversals (`preorder`, `preorderKeyed`, `fold`, `foldKeyed`, `map`, `filter`,
`exists`, `count`, `ids`, `tryFind`, `tryPick`, `parentOf`, `ancestors`, `path`, `depth`, `siblings`, `subtree`,
`descendants`, `updateNode`, `remapIds`, `isWellFormed`, `wellFormed`, `firstRepeatedId`, `graftWellFormed`,
`graftWellFormedKeyed`) are 71 % of the owner, and the package is 58 % example-only, second of the five areas after
the Codegen diff (85 %), on the one type every other package walks. They are pure, total and generic over the node
witness; their stand-ins (`TreeExtTests.fs`, `TreeTests.fs`, `KeyedApplyTests.fs`) are examples with no sampled
case; the kit already draws trees (`AlgebraTreeLaws`); and the index they must agree with is on the ladder
(`index-agrees-with-the-walk`, proved), so the family ties the shipped traversals to a proved answer rather than to
each other alone. Phase 335's order put Tree second after Wire, and Wire's decode combinators have since reached the
ladder (`decode-layer-refusals-resolve` and its neighbours), so the order and the new figures agree.

**What a phase for it would cover** — one family in `Fuaran.Core.Conformance`, rostering the 25 operations, over drawn
trees and every id of each tree plus one absent id:

- `preorder` visits every node once; `count` is its length, `ids` its ids, `fold` folds in its order, `filter`,
  `exists`, `tryFind` and `tryPick` answer as the same search over it, and the keyed forms equal the plain ones on a
  tree with no keyed slot;
- `parentOf`, `ancestors`, `path`, `depth`, `siblings`, `subtree` and `descendants` agree with the proved `Tree.Index`
  for every present id, and answer `None` or empty for the absent one;
- `map` keeps the shape and the preorder length, and the identity map is the identity; `updateNode` changes the one
  node and leaves every other node's preorder position and value; `remapIds` under an injective map keeps the shape and
  well-formedness;
- `isWellFormed` holds exactly when `wellFormed` is `Ok` and `firstRepeatedId` is `None`, and `graftWellFormed` (and
  its keyed form) refuses exactly the grafts that would repeat an id, naming that id.

The phase also removes those operations' `measured-elsewhere` entries, which the clause then requires. After it, the
next candidates by the same reading are the `Idl.Diff` classifier (17 of 21) and `Propagation`'s planner (11 of 24).
