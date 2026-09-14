# The shared wire-format conformance corpus

Three suites in this repository certify against a corpus that lives in **another repository**:

| Family | What it certifies | Suite |
|---|---|---|
| `laws/transform-laws.json` | the transform-law reference vectors this kit publishes are the vectors the corpus carries, and each one is still true of this evaluator | `tests/Fuaran.Core.Tests/LawVectorTests.fs` |
| `nodes/*.json` | the vendored wire snapshots the IDL inversion gate asserts against have not drifted from the live corpus | `tests/Fuaran.Core.Tests/IdlSpikeTests.fs` |
| `apply/` | the apply engine answers what the published apply contract says it answers, and the F\* preservation model agrees with it over those same fixtures | `tests/Fuaran.Core.Tests/ApplyVectorTests.fs`, `ProofOracleTests.fs` |

The corpus is <https://github.com/fuaran-ui/fuaran-ui-specification>, resolved on disk under the
directory name `wire-format-fixtures`. The directory name is the interface; the repository name is
not.

## Getting it

Either of these is found with no further configuration:

```powershell
# beside this checkout
cd ..; git clone https://github.com/fuaran-ui/fuaran-ui-specification.git wire-format-fixtures

# or inside the repository root (.gitignore carries it, so the tree stays clean)
git clone https://github.com/fuaran-ui/fuaran-ui-specification.git wire-format-fixtures
```

A clone anywhere else is named explicitly:

```powershell
$env:FUARAN_CORE_CORPUS_DIR = 'C:\somewhere\wire-format-fixtures'
```

`FUARAN_CORE_CORPUS_DIR` must point at the corpus **root** — the directory holding `manifest.json`
and the family directories — not at a family. A directory that is not the corpus is refused by name
rather than searched past: acceptance reads the `schema` and `idl` documents the corpus's own
manifest declares, so an index that merely *mentions* those families cannot pass for one.

## An absent corpus FAILS. It does not skip

This is the decision worth stating plainly, because the opposite was true until Phase 130 and it
cost a day.

Both suites used to locate the corpus by climbing upwards from wherever the test binary was running,
and to `skiptest` by name when the climb found nothing. From the repository's main working tree the
climb reached a corpus checked out alongside; from a **linked git worktree** of the same repository
it never did, because a worktree sits somewhere else entirely. So four consecutive green gate runs
had certified against a corpus none of them had read, and the first run that actually compared —
one in the main tree — failed on drift that had been accumulating invisibly.

A skip that means "this check did not run" is indistinguishable, in a green report, from a check
that ran and passed. So:

- the corpus is resolved from the repository's **main working tree**, which git answers identically
  from every worktree of a repository (`git rev-parse --git-common-dir`, whose parent is the main
  tree);
- an absent corpus **fails**, naming every path it tried and the remedy;
- the only way to skip is to ask for it.

## Asking to skip

```powershell
$env:FUARAN_CORE_SKIP_CORPUS = '1'
```

The corpus legs then skip, and each printed skip names the variable and says that nothing was
compared against the shared corpus. Use it when you are working on something unrelated and do not
want a second clone; do not propose a change whose only green run was made with it set.

## What CI does

`.github/workflows/ci.yml` checks the corpus out beside the repository checkout and sets
`FUARAN_CORE_CORPUS_DIR` to it. That is deliberate rather than incidental: it means every push
compares against the corpus at its own `main`, so drift between this kit and the published corpus
surfaces on the change that caused it instead of on somebody else's change days later.

It also means a change to what this kit renders into the corpus is not finished until the corpus
commit is pushed too. The two repositories are one change-set.

## The `apply/` family

Every other family in that corpus is a DECODE family: `nodes/`, `ops/`, `reject/` and `lenient/`
each say "these bytes decode to that value", and every `reject/*` is a decode refusal. Nothing said
what happens when an op is APPLIED — so each host certified its own apply semantics against its own
tests, and agreement between them was a claim nothing could falsify. `apply/` is that missing
family.

### Its shape

One vector per validator clause per skeleton op, plus `Batch`'s all-or-nothing atomicity and the
three id-collision shapes. Each vector carries an `input.tree` and an `input.op` as canonical JSON
strings, and an expectation computed by CALLING `Ops.apply` — never by restating what it ought to
answer. An `accept` carries the result's canonical bytes and their `sha256:`-prefixed digest; a
`reject` carries the rejection class.

The tree envelope is **the witness surface and nothing else** — `{"children":[…],"id":…,"kind":…}`.
The five skeleton ops are structural, so structure is all they read, and a host runs the family by
mapping three members onto its own node type rather than by decoding its own wire format. That is
what makes an apply family expressible at all in a core that owns no node type.

`manifest.json` beside the vectors is authoritative for the count and for per-host `adoption`; both
files are emitted, so neither can drift from the other. **Do not restate a vector count anywhere.**

### Rejection classes are pinned by CORRESPONDENCE, not by rename

Core says `DuplicateId` where the TypeScript, Python, Go and Rust hosts say `DuplicateNodeId`. Every
reject vector therefore carries an `expected.hosts` map naming the code each host raises for that
clause, read off each host's own source. Two vectors carry an `expected.hostsNote` and no mapping at
all, because there is nothing to map:

- **the graft that repeats an id within ITSELF.** All four of those hosts seed their duplicate scan
  from the root's ids alone, so such a graft is ACCEPTED there and the tree then carries one id
  twice. Core refuses it (Phase 137). A host adopting that vector adds a check; it does not
  translate a code it already has.
- **the refused `Batch`.** Those hosts wrap an inner failure as `BatchAborted` carrying the inner
  op's index and discard the inner class; Core returns the inner rejection itself. A host asserts
  the wrapper and the index; the inner class is not comparable across the two shapes.

### What it deliberately does not pin

The ORDER clauses are checked in — Core checks the root and existence before the cycle guard, and at
least one host checks the cycle guard first, so a vector breaking two clauses at once would compare
implementations rather than the contract, and none does. `UnknownNode`'s enumeration of addressable
ids and `ReorderMismatch`'s two orders, which are evidence for a class rather than the class itself.
And the container-capability refusal (`NotAContainer` / `ChildlessKind`), which comes from a
predicate the engine is HANDED rather than from anything in a wire document.

### Adoption

The F# host certifies first, and its `adoption` entry reads `adopted`. The other four read
`proposed`: the family is fully specified and certified before any of them emits it, and a
`proposed` family is reported BY NAME by a host that has not adopted it rather than skipped
silently. A host flips its own entry in the change-set that lands its leg.

The family is **self-enumerated** — its own `apply/manifest.json`, deliberately not indexed by the
corpus root `manifest.json`, which indexes the canonical wire-format codec families only. That is
the `laws/`, `dag/`, `merge-conformance/` and `devtools-relay/` precedent, and it is also the only
shape that does not break a host on arrival: at least one host's certification kit reads the root
manifest's fixture list as a whole and asserts that its per-kind leg tallies account for exactly the
manifest's fixture count, so a new kind there is a red build in a repository that has adopted
nothing.

### Emitting it

```powershell
dotnet run --project tests/Fuaran.Core.Tests -- --emit-apply <corpus dir>
```

A flag rather than a test side-effect, for the reason `--emit-laws` is one: the corpus is a separate
repository and a suite that wrote into it on every run would dirty a shared clone. Note the name —
the UI language repository's `--emit-corpus` renders THAT domain's node vectors and knows nothing
about this engine; the apply semantics are Core's, so Core emits them.

**There is no `kitVersion` stamp in either file, and that is deliberate.** `laws/transform-laws.json`
carries one and is then byte-compared whole, so any `<Version>` move reddens its freshness leg until
the corpus is re-emitted — including a draft-slot cut, which is made once and ridden by several
phases landing days apart. This family is not a sample of one kit's answers; it is a specification
of apply semantics whose every expectation is RECOMPUTED on each run, which is a stronger guarantee
than a stamp. An artefact that cannot go stale for a reason unrelated to its content does not need
one.
